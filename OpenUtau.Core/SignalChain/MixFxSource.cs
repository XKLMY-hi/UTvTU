namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// 实时效果机架包装器（移植自上游 30d09962「Redesign Track Polish as a
    /// live, non-modal effects rack」）。
    ///
    /// 契约（接口冻结，UI 侧按此引用）：
    /// <list type="bullet">
    /// <item><see cref="SampleRate"/> / <see cref="Channels"/> 跟随全局音频格式设置，
    /// 不硬编码 44100/2（Plus B-3「播放与输出层格式显式化」约定）。</item>
    /// <item><see cref="EqMidQ"/> 为 EQ 中频段 Q，预设不覆盖它。</item>
    /// <item><c>WrapLive(ISignalSource, UTrack)</c>：播放路径，逐音频块跟随轨道
    /// 当前 MixFx（旋钮/模块开关即时可听）；<c>WrapWith(ISignalSource, UMixFx?)</c>：
    /// 导出路径，使用固定快照，且轨道无有效效果时原样返回内层源。</item>
    /// </list>
    ///
    /// 实现体由 Core 侧补齐（本文件在冻结提交中只提供常量，避免 UI 分支阻塞）。
    /// </summary>
    public partial class MixFxSource {
        /// <summary>机架 DSP 使用的采样率（跟随全局音频格式设置）。</summary>
        public static int SampleRate => AudioSettings.SampleRate;

        /// <summary>机架 DSP 使用的声道数。</summary>
        public static int Channels => AudioSettings.Channels;

        /// <summary>EQ 中频段 Q 值（预设不覆盖）。</summary>
        public const double EqMidQ = 0.707;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 实现部分（纯追加）。上方常量桩是 Lead 冻结的 UI 侧契约，一行未改；using 放在
// 第二个 namespace 块内，使本文件的改动成为可审计的纯追加。
// ─────────────────────────────────────────────────────────────────────────────
namespace OpenUtau.Core.SignalChain {
    using System;
    using OpenUtau.Core.SignalChain.Effects;
    using OpenUtau.Core.Ustx;

    public partial class MixFxSource : ISignalSource {
        /// <summary>交叉淡化时长（毫秒）。帧数按实际采样率换算（44.1k → 661 帧）。</summary>
        private const double FadeMs = 15.0;
        /// <summary>scratch 预分配下限（帧）。常规音频后端的块不超过 AudioSettings.BlockSize。</summary>
        private const int MinScratchFrames = 4096;

        private readonly ISignalSource source;
        private readonly Func<UMixFx?> getFx;
        private readonly int channels;
        /// <summary>每帧增益步进 = 1 / FadeFrames。</summary>
        private readonly float fadeStep;
        private readonly BiquadEQ eq;
        private readonly SimpleCompressor comp;
        private readonly Freeverb reverb;

        // 最后一次真正推给 DSP 的参数。音频线程逐字段比较，改参数不需要分配。
        private readonly UMixFx applied = new UMixFx();
        private bool configured;

        // 当前交叉淡化位置：0 = 干声，1 = 处理后的信号。
        private float masterGain;
        private float eqGain;
        private float compGain;
        private float reverbGain;

        // scratch 缓冲区。内层源按加法混音写零缓冲，故需要私有可写副本。
        // 构造时按 AudioSettings.BlockSize 预分配；仅在音频后端给出更大块时按 2 的幂
        // 几何扩容（整个会话 O(log n) 次），稳态每块零分配。
        private float[] scratch;
        private float[] masterDry;
        private float[] stageDry;

        // 上一块结束位置：识别 seek / 循环回跳 → 清 DSP 残留。
        private int nextPosition;
        private bool hasMixed;

        private MixFxSource(ISignalSource source, Func<UMixFx?> getFx) {
            this.source = source;
            this.getFx = getFx;
            // DSP 与淡化步进都以 AudioSettings 为准（无 44100/2 硬编码）。
            channels = Channels;
            fadeStep = 1f / Math.Max(1, (int)Math.Round(SampleRate * FadeMs / 1000.0));
            eq = new BiquadEQ(SampleRate, Channels);
            comp = new SimpleCompressor(SampleRate, Channels);
            reverb = new Freeverb(SampleRate, Channels);
            int capacity = Math.Max(MinScratchFrames, AudioSettings.BlockSize) * Channels;
            scratch = new float[capacity];
            masterDry = new float[capacity];
            stageDry = new float[capacity];
            // 从当前状态起步，播放开始时不做淡入。
            var fx = getFx();
            if (fx != null) {
                Sync(fx);
                masterGain = fx.Enabled ? 1f : 0f;
                eqGain = fx.EqEnabled ? 1f : 0f;
                compGain = fx.CompEnabled ? 1f : 0f;
                reverbGain = fx.ReverbEnabled ? 1f : 0f;
            }
        }

        public bool IsReady(int position, int count) => source.IsReady(position, count);

        /// <summary>
        /// 清空 DSP 时间域状态（seek / 循环回跳 / 换工程）。交叉淡化增益不重置——
        /// 开关的淡化位置要保持连续，否则跳转处会留下电平跳变。
        /// </summary>
        public void Reset() => ResetEffects();

        public int Mix(int position, float[] buffer, int index, int count) {
            // 位置不连续 = seek 或循环回跳：清掉上一个位置的滤波器/混响/包络残留。
            // （本项目 MasterAdapter.SetPosition 的 Reset 经 WaveMix 不下传，故这里
            // 自带连续性检查；嵌套在 WaveMix 下的 VST EffectChain 同样受益不了那次
            // Reset——见 T2 报告"遗留风险"。）
            if (hasMixed && position != nextPosition) {
                ResetEffects();
            }

            EnsureCapacity(count);
            Array.Clear(scratch, 0, count);
            int ret = source.Mix(position, scratch, 0, count);
            hasMixed = true;
            nextPosition = ret;

            // 每个音频块重新取参：旋钮/模块开关在一个块内生效。
            var fx = getFx();
            if (fx != null) {
                Sync(fx);
            }
            float masterTarget = fx != null && fx.Enabled ? 1f : 0f;
            if (configured && (masterGain > 0f || masterTarget > 0f)) {
                bool masterSteady = masterGain == 1f && masterTarget == 1f;
                if (!masterSteady) {
                    Array.Copy(scratch, masterDry, count);
                }
                RunStage(eq, ref eqGain, applied.EqEnabled, count);
                RunStage(comp, ref compGain, applied.CompEnabled, count);
                RunStage(reverb, ref reverbGain, applied.ReverbEnabled, count);
                if (!masterSteady) {
                    Crossfade(masterDry, scratch, ref masterGain, masterTarget, count);
                    if (masterGain == 0f) {
                        // 完全切断后再清，避免淡出过程中被清空导致咔嗒声。
                        ResetEffects();
                    }
                }
            }

            // 加法混音写回输出（与 Fader / WaveMix 约定一致）。
            for (int i = 0; i < count; i++) {
                buffer[index + i] += scratch[i];
            }
            return ret;
        }

        /// <summary>按需扩容（2 的幂）。稳态块大小下不再分配。</summary>
        private void EnsureCapacity(int count) {
            if (scratch.Length >= count) {
                return;
            }
            int capacity = scratch.Length;
            while (capacity < count) {
                capacity <<= 1;
            }
            scratch = new float[capacity];
            masterDry = new float[capacity];
            stageDry = new float[capacity];
        }

        private void ResetEffects() {
            eq.Reset();
            comp.Reset();
            reverb.Reset();
        }

        private void RunStage(IEffect effect, ref float gain, bool enabled, int count) {
            float target = enabled ? 1f : 0f;
            if (gain == 0f && target == 0f) {
                return;
            }
            if (gain == 1f && target == 1f) {
                effect.Process(scratch, 0, count);
                return;
            }
            Array.Copy(scratch, stageDry, count);
            effect.Process(scratch, 0, count);
            Crossfade(stageDry, scratch, ref gain, target, count);
            if (gain == 0f) {
                // 切断后清残留状态，重新打开时不会重放旧尾巴。
                effect.Reset();
            }
        }

        /// <summary>
        /// wet[i] = dry[i] + (wet[i] - dry[i]) * g，g 每帧朝 target 逼近 fadeStep。
        /// 交错布局下逐帧推进；count 不是声道数整数倍时，尾部残样按当前 g 处理
        /// （不留下未淡化的湿信号）。
        /// </summary>
        private void Crossfade(float[] dry, float[] wet, ref float gain, float target, int count) {
            float g = gain;
            int i = 0;
            for (; i + channels <= count; i += channels) {
                if (g < target) {
                    g = Math.Min(target, g + fadeStep);
                } else if (g > target) {
                    g = Math.Max(target, g - fadeStep);
                }
                for (int c = 0; c < channels; c++) {
                    float d = dry[i + c];
                    wet[i + c] = d + (wet[i + c] - d) * g;
                }
            }
            for (; i < count; i++) {
                float d = dry[i];
                wet[i] = d + (wet[i] - d) * g;
            }
            gain = g;
        }

        /// <summary>参数有变时才重配 DSP（<paramref name="fx"/> 与上次推入的逐字段比较）。</summary>
        private void Sync(UMixFx fx) {
            if (configured && SameParams(fx, applied)) {
                return;
            }
            CopyParams(fx, applied);
            configured = true;

            eq.Configure(applied.EqLowDb, applied.EqMidFreq, EqMidQ, applied.EqMidDb, applied.EqHighDb);

            FxPresets.CompParams cParams = FxPresets.Comp.TryGetValue(applied.CompPreset ?? FxPresets.Off, out var cp)
                ? cp
                : FxPresets.Comp[FxPresets.Off];
            comp.Configure(applied.CompThresholdDb, applied.CompRatio,
                           cParams.AttackMs, cParams.ReleaseMs, applied.CompMakeupDb);

            FxPresets.ReverbParams rParams = FxPresets.Reverb.TryGetValue(applied.ReverbPreset ?? FxPresets.Off, out var rp)
                ? rp
                : FxPresets.Reverb[FxPresets.Off];
            double userWet = Math.Clamp(applied.ReverbWet, 0.0, 2.0);
            reverb.Configure(applied.ReverbSize, applied.ReverbDamp, rParams.Width,
                             rParams.Wet * userWet, rParams.Dry, applied.ReverbPreDelayMs);
        }

        // DSP 依赖的一切参数（除主开关 Enabled——它走交叉淡化）。
        private static bool SameParams(UMixFx a, UMixFx b) {
            return a.EqEnabled == b.EqEnabled && a.CompEnabled == b.CompEnabled && a.ReverbEnabled == b.ReverbEnabled
                && a.EqLowDb == b.EqLowDb && a.EqMidFreq == b.EqMidFreq && a.EqMidDb == b.EqMidDb && a.EqHighDb == b.EqHighDb
                && a.CompPreset == b.CompPreset && a.CompThresholdDb == b.CompThresholdDb
                && a.CompRatio == b.CompRatio && a.CompMakeupDb == b.CompMakeupDb
                && a.ReverbPreset == b.ReverbPreset && a.ReverbSize == b.ReverbSize && a.ReverbDamp == b.ReverbDamp
                && a.ReverbWet == b.ReverbWet && a.ReverbPreDelayMs == b.ReverbPreDelayMs;
        }

        private static void CopyParams(UMixFx src, UMixFx dst) {
            dst.EqEnabled = src.EqEnabled;
            dst.CompEnabled = src.CompEnabled;
            dst.ReverbEnabled = src.ReverbEnabled;
            dst.EqLowDb = src.EqLowDb;
            dst.EqMidFreq = src.EqMidFreq;
            dst.EqMidDb = src.EqMidDb;
            dst.EqHighDb = src.EqHighDb;
            dst.CompPreset = src.CompPreset;
            dst.CompThresholdDb = src.CompThresholdDb;
            dst.CompRatio = src.CompRatio;
            dst.CompMakeupDb = src.CompMakeupDb;
            dst.ReverbPreset = src.ReverbPreset;
            dst.ReverbSize = src.ReverbSize;
            dst.ReverbDamp = src.ReverbDamp;
            dst.ReverbWet = src.ReverbWet;
            dst.ReverbPreDelayMs = src.ReverbPreDelayMs;
        }

        /// <summary>至少一个开启的模块会改变信号（导出路径据此决定是否包装）。</summary>
        private bool IsAnythingEnabled =>
            configured
            && ((applied.EqEnabled && !eq.IsBypassed)
                || (applied.CompEnabled && !comp.IsBypassed)
                || (applied.ReverbEnabled && !reverb.IsBypassed));

        /// <summary>
        /// 固定参数包装器（离线导出）。轨道无 FX、主开关关、或所有模块都会原样
        /// 透传时，**原样返回内层源**（零开销）。
        /// </summary>
        public static ISignalSource WrapWith(ISignalSource inner, UMixFx? fx) {
            if (fx == null || !fx.Enabled) {
                return inner;
            }
            var snapshot = fx.Clone();
            var wrapper = new MixFxSource(inner, () => snapshot);
            return wrapper.IsAnythingEnabled ? wrapper : inner;
        }

        /// <summary>
        /// 实时包装器（播放/录制）。总是包装，且每个音频块跟随
        /// <paramref name="track"/> 当前的 <see cref="UTrack.MixFx"/>，
        /// 因此播放中调参即时可听、且不需要重渲染或重启播放。
        /// </summary>
        public static ISignalSource WrapLive(ISignalSource inner, UTrack track) {
            return new MixFxSource(inner, () => track.MixFx);
        }
    }
}
