using System;
using System.Linq;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.SignalChain {
    /// <summary>
    /// 实时效果机架（移植自上游 30d09962 的 MixFxSourceTest）+ 规划 §5.4 (a)-(g) 补测。
    /// 覆盖：播放中调参一个块内生效、开关交叉淡化无爆音、模块开关隔离、导出快照冻结、
    /// 空转轨道透传、seek 清残留、音频线程稳态零分配、RenderEngine 三态接线。
    /// </summary>
    public class MixFxSourceTest {
        const int Block = 512;

        // ── 测试用信号源 ─────────────────────────────────────────────────

        /// <summary>立体声同相正弦，幅度 0.2。8 kHz 恰好是 EQ 高架拐点（增益折半）。</summary>
        class SineSource : ISignalSource {
            readonly double freq;
            public SineSource(double freq = 8000) => this.freq = freq;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                int channels = AudioSettings.Channels;
                for (int i = 0; i < count; i++) {
                    int frame = (position + i) / channels;
                    buffer[index + i] += 0.2f * (float)Math.Sin(2 * Math.PI * freq * frame / AudioSettings.SampleRate);
                }
                return position + count;
            }
        }

        /// <summary>常数信号（幅度固定）。用于确定性的"无爆音"判据：稳态相邻样本差恒为 0。</summary>
        class DcSource : ISignalSource {
            readonly float value;
            public DcSource(float value) => this.value = value;
            public bool IsReady(int position, int count) => true;
            public int Mix(int position, float[] buffer, int index, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[index + i] += value;
                }
                return position + count;
            }
        }

        /// <summary>把信号放大 N 倍，模拟 VST 链对信号做的事。</summary>
        class GainEffect : IEffect {
            readonly float gain;
            public GainEffect(float gain) => this.gain = gain;
            public bool IsBypassed => false;
            public void Process(float[] buffer, int offset, int count) {
                for (int i = 0; i < count; i++) {
                    buffer[offset + i] *= gain;
                }
            }
            public void Reset() { }
        }

        // ── 夹具 ─────────────────────────────────────────────────────────

        /// <summary>+12 dB 高架（8 kHz 拐点 → 该频率处 +6 dB：0.2 → ~0.4），其余模块空转。</summary>
        static UMixFx BrightEq(bool enabled) => new UMixFx {
            Enabled = enabled,
            EqLowDb = 0, EqMidDb = 0, EqHighDb = 12,
            CompPreset = FxPresets.Off, CompRatio = 1, CompMakeupDb = 0,
            ReverbPreset = FxPresets.Off, ReverbWet = 0,
        };

        /// <summary>所有模块都在跑但都原样透传的 UMixFx（空转轨）。</summary>
        static UMixFx IdleFx() => new UMixFx {
            Enabled = true,
            EqLowDb = 0, EqMidDb = 0, EqHighDb = 0,
            CompPreset = FxPresets.Off, CompRatio = 1, CompMakeupDb = 0,
            ReverbPreset = FxPresets.Off, ReverbWet = 0,
        };

        static float[] Render(ISignalSource source, ref int position, int blocks) {
            var output = new float[blocks * Block];
            for (int b = 0; b < blocks; b++) {
                position = source.Mix(position, output, b * Block, Block);
            }
            return output;
        }

        static float Peak(float[] samples, int from) => samples.Skip(from).Max(MathF.Abs);

        static void AssertSamplesEqual(float[] expected, float[] actual) {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++) {
                Assert.True(expected[i] == actual[i],
                    $"样本 {i} 不一致：expected={expected[i]} actual={actual[i]}");
            }
        }

        // ── 上游移植用例 ─────────────────────────────────────────────────

        [Fact]
        public void FollowsTrackEditsWhilePlaying() {
            var track = new UTrack { MixFx = BrightEq(false) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;

            var dry = Render(source, ref position, 8);
            Assert.InRange(Peak(dry, 0), 0.19f, 0.21f);

            track.MixFx = BrightEq(true);
            var wet = Render(source, ref position, 16);
            // +12 dB 高架拐点处 +6 dB：0.2 -> ~0.4。
            Assert.InRange(Peak(wet, wet.Length / 2), 0.35f, 0.45f);

            track.MixFx.EqHighDb = 0;
            var flat = Render(source, ref position, 16);
            Assert.InRange(Peak(flat, flat.Length / 2), 0.19f, 0.21f);
        }

        [Fact]
        public void MasterAndModuleSwitchesReturnToExactlyDry() {
            var track = new UTrack { MixFx = BrightEq(true) };
            var inner = new SineSource();
            var source = MixFxSource.WrapLive(inner, track);
            int position = 0;
            Render(source, ref position, 8);

            foreach (Action<UMixFx> switchOff in new Action<UMixFx>[] { fx => fx.Enabled = false, fx => fx.EqEnabled = false }) {
                track.MixFx = BrightEq(true);
                Render(source, ref position, 8);
                switchOff(track.MixFx);
                Render(source, ref position, 4); // 淡化在 661 帧内完成，远小于 4 块
                int reference = position;
                var expected = Render(inner, ref reference, 4);
                var output = Render(source, ref position, 4);
                AssertSamplesEqual(expected, output);
            }
        }

        [Fact]
        public void SwitchingCrossfadesInsteadOfJumping() {
            var track = new UTrack { MixFx = BrightEq(false) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;
            Render(source, ref position, 4);

            track.MixFx.Enabled = true;
            var fadeIn = Render(source, ref position, 4);
            // 硬切会在第一个样本上跳最多 ~0.4。
            float first = fadeIn[0];
            float dryFirst = 0.2f * MathF.Sin(2 * MathF.PI * 8000 * (4 * Block / 2) / 44100f);
            Assert.True(MathF.Abs(first - dryFirst) < 0.05f);
        }

        // ── (e) 空转轨道原样透传 ─────────────────────────────────────────

        [Fact]
        public void ExportSkipsWrappingWhenNothingWouldChange() {
            var inner = new SineSource();
            Assert.Same(inner, MixFxSource.WrapWith(inner, null));
            Assert.Same(inner, MixFxSource.WrapWith(inner, BrightEq(false)));
            var allOff = BrightEq(true);
            allOff.EqEnabled = false;
            Assert.Same(inner, MixFxSource.WrapWith(inner, allOff));
            // 主开关开、但三个模块都会原样透传（增益为 0 / 预设 off）
            Assert.Same(inner, MixFxSource.WrapWith(inner, IdleFx()));
            Assert.NotSame(inner, MixFxSource.WrapWith(inner, BrightEq(true)));
        }

        // ── (a) 播放中改参数：下一个块生效 ───────────────────────────────

        [Fact]
        public void ParameterEditAppliesInTheNextBlock() {
            var track = new UTrack { MixFx = BrightEq(true) };
            track.MixFx.EqHighDb = 0; // 稳态干声：EqEnabled 仍为 true，无淡化过程
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;

            var before = Render(source, ref position, 4);
            Assert.InRange(Peak(before, 0), 0.19f, 0.21f);

            // 播放中拧旋钮：只改参数、不改开关 → 下一块立刻是新的湿声（无淡化）
            track.MixFx.EqHighDb = 12;
            var next = Render(source, ref position, 1);
            Assert.InRange(Peak(next, Block / 2), 0.35f, 0.45f);
        }

        // ── (b) 关主开关：回干声且样本级无爆音 ───────────────────────────

        [Fact]
        public void MasterSwitchOff_CrossfadesToDryWithoutClick() {
            // 常数信号 + 低架 +12 dB：湿声 0.2 → ~0.796，干声 0.2。
            // 稳态相邻样本差恒为 0，于是"相邻帧差有界"直接等价于"淡化没有跳变"：
            // 硬切会在此处跳 ~0.6，而 15 ms 淡化每帧只走 0.6/661 ≈ 0.0009。
            var fx = new UMixFx {
                Enabled = true,
                EqLowDb = 12, EqMidDb = 0, EqHighDb = 0,
                CompPreset = FxPresets.Off, CompRatio = 1, CompMakeupDb = 0,
                ReverbPreset = FxPresets.Off, ReverbWet = 0,
            };
            var track = new UTrack { MixFx = fx };
            var source = MixFxSource.WrapLive(new DcSource(0.2f), track);

            var output = new float[8 * Block];
            int position = 0;
            for (int b = 0; b < 8; b++) {
                if (b == 4) {
                    track.MixFx.Enabled = false;
                }
                position = source.Mix(position, output, b * Block, Block);
            }

            // 关之前：稳态湿声（+12 dB 低架）
            Assert.InRange(output[4 * Block - 1], 0.75f, 0.85f);
            // 淡化完成后精确回到干声
            Assert.InRange(output[8 * Block - 1], 0.19f, 0.21f);
            // 相邻样本差有界（含切换点两侧）
            float maxDelta = 0;
            for (int i = 1; i < output.Length; i++) {
                maxDelta = Math.Max(maxDelta, MathF.Abs(output[i] - output[i - 1]));
            }
            Assert.True(maxDelta < 0.01f, $"淡化过程出现跳变：maxDelta={maxDelta}");
        }

        [Fact]
        public void MasterSwitchOff_FadeTakesAbout15ms() {
            var track = new UTrack { MixFx = BrightEq(true) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;
            var wet = Render(source, ref position, 4); // 稳态湿声（包络 ~0.4）
            Assert.InRange(Peak(wet, wet.Length / 2), 0.35f, 0.45f);

            track.MixFx.Enabled = false;
            var transition = Render(source, ref position, 4);

            int channels = AudioSettings.Channels;
            int fadeFrames = Math.Max(1, (int)Math.Round(AudioSettings.SampleRate * 15.0 / 1000.0));
            // 用包络而非瞬时样本判据（8 kHz 在 EQ 拐点处有相移，瞬时值不可比）
            // 不是瞬切：淡化头 50 帧仍是湿声包络（硬切会立刻掉到干声 0.2）
            Assert.InRange(Peak(transition.Take(50 * channels).ToArray(), 0), 0.30f, 0.45f);
            // 15 ms 之后回到干声包络
            Assert.InRange(Peak(transition.Skip(fadeFrames * channels).Take(200 * channels).ToArray(), 0), 0.19f, 0.21f);
        }

        // ── (c) 模块开关只影响该模块 ─────────────────────────────────────

        [Fact]
        public void ModuleSwitchOff_OnlyAffectsThatModule() {
            // EQ 常开（+12 dB 高架），只切换混响模块：
            // 切掉混响后，输出应在淡化结束时**逐样本等于**"从来只有 EQ"的对照链
            // —— 证明混响开关不会连带改动 EQ 的处理结果（EQ 在链上位于混响之前）。
            var eqOnly = new UTrack { MixFx = BrightEq(true) };
            var withReverb = BrightEq(true);
            withReverb.ReverbPreset = "small_room";
            withReverb.ReverbWet = 1.0;
            var both = new UTrack { MixFx = withReverb };

            var reference = MixFxSource.WrapLive(new SineSource(), eqOnly);
            var source = MixFxSource.WrapLive(new SineSource(), both);
            int refPos = 0, pos = 0;

            var refBlocks = Render(reference, ref refPos, 8);
            var bothBlocks = Render(source, ref pos, 8);
            // 混响确实在改变信号（不是空转模块）
            Assert.NotEqual(refBlocks[4 * Block], bothBlocks[4 * Block]);

            both.MixFx.ReverbEnabled = false;
            var refAfter = Render(reference, ref refPos, 8); // EQ 链继续
            var afterSwitch = Render(source, ref pos, 8);
            // 前 661 帧还在淡化：与 EQ-only 不同（不是瞬切）
            Assert.NotEqual(refAfter[0], afterSwitch[0]);
            // 淡化结束后的那一段：与 EQ-only 逐样本相等
            var expectedTail = refAfter.Skip(4 * Block).ToArray();
            var actualTail = afterSwitch.Skip(4 * Block).ToArray();
            AssertSamplesEqual(expectedTail, actualTail);
            // 而且 EQ 仍然在起作用（峰值 ~0.4，不是干声 0.2）
            Assert.InRange(Peak(afterSwitch, afterSwitch.Length / 2), 0.35f, 0.45f);
        }

        // ── (d) 导出快照不随后续改动 ─────────────────────────────────────

        [Fact]
        public void ExportSnapshot_IgnoresLaterEdits() {
            var track = new UTrack { MixFx = BrightEq(true) };
            var source = RenderEngine.WrapTrackFx(new SineSource(), track, MixFxMode.Snapshot, Array.Empty<IEffect>());
            int position = 0;

            var wet = Render(source, ref position, 8);
            Assert.InRange(Peak(wet, 0), 0.35f, 0.45f);

            // 导出过程中（或导出后）改参数：快照链不受影响 → 导出确定
            track.MixFx.EqHighDb = 0;
            track.MixFx.Enabled = false;
            var stillWet = Render(source, ref position, 8);
            Assert.InRange(Peak(stillWet, 0), 0.35f, 0.45f);

            // 对照：Live 链会跟着改（同一个 track 对象）
            var track2 = new UTrack { MixFx = BrightEq(true) };
            var live = RenderEngine.WrapTrackFx(new SineSource(), track2, MixFxMode.Live, Array.Empty<IEffect>());
            int livePos = 0;
            var liveWet = Render(live, ref livePos, 8);
            Assert.InRange(Peak(liveWet, 0), 0.35f, 0.45f);
            track2.MixFx.EqHighDb = 0;
            var liveFlat = Render(live, ref livePos, 8);
            Assert.InRange(Peak(liveFlat, liveFlat.Length / 2), 0.19f, 0.21f);
        }

        // ── (g) 播放中改 MixFx 不需要重渲染/重建链 ───────────────────────

        [Fact]
        public void Live_WiringFollowsTrackEditsWithoutRebuild() {
            var track = new UTrack { MixFx = BrightEq(false) };
            var source = RenderEngine.WrapTrackFx(new SineSource(), track, MixFxMode.Live, Array.Empty<IEffect>());
            int position = 0;

            var dry = Render(source, ref position, 8);
            Assert.InRange(Peak(dry, 0), 0.19f, 0.21f);

            // 同一个链实例：改开关/改参数即时可听（播放不重启、不重渲染）
            track.MixFx.Enabled = true;
            track.MixFx.EqHighDb = 12;
            var wet = Render(source, ref position, 16);
            Assert.InRange(Peak(wet, wet.Length / 2), 0.35f, 0.45f);
        }

        [Fact]
        public void Live_HandlesMixFxCreatedWhilePlaying() {
            var track = new UTrack(); // MixFx == null：还没配过效果
            var source = MixFxSource.WrapLive(new SineSource(), track);
            int position = 0;

            var dry = Render(source, ref position, 4);
            Assert.InRange(Peak(dry, 0), 0.19f, 0.21f);

            // 播放中首次开启机架
            track.MixFx = BrightEq(true);
            var wet = Render(source, ref position, 16);
            Assert.InRange(Peak(wet, wet.Length / 2), 0.35f, 0.45f);
        }

        /// <summary>
        /// 干轨快路径：没有任何效果参与时，包装器输出必须与内层源逐样本相同
        /// （直接加法混音进输出，不经过 scratch 往返）——播放中的空转轨零开销。
        /// </summary>
        [Fact]
        public void Live_NoFx_FastPathIsTransparent() {
            var inner = new SineSource();
            var noFx = MixFxSource.WrapLive(inner, new UTrack()); // MixFx == null
            int pos1 = 0;
            var wrapped = Render(noFx, ref pos1, 4);

            int pos2 = 0;
            var raw = Render(inner, ref pos2, 4);
            AssertSamplesEqual(raw, wrapped);

            // 主开关关（已配过效果）同样走快路径
            var disabled = MixFxSource.WrapLive(inner, new UTrack { MixFx = BrightEq(false) });
            int pos3 = 0;
            AssertSamplesEqual(raw, Render(disabled, ref pos3, 4));
        }

        // ── RenderEngine 三态接线 ────────────────────────────────────────

        [Fact]
        public void WrapTrackFx_WiresThreeModes() {
            var inner = new SineSource();
            var vst = new IEffect[] { new GainEffect(2f) };
            var track = new UTrack { MixFx = BrightEq(true) };

            // Off：原样返回（VST 也不接，= 旧 applyMixFx:false）
            Assert.Same(inner, RenderEngine.WrapTrackFx(inner, track, MixFxMode.Off, vst));
            // Snapshot / Live：都包装
            Assert.NotSame(inner, RenderEngine.WrapTrackFx(inner, track, MixFxMode.Snapshot, Array.Empty<IEffect>()));
            Assert.NotSame(inner, RenderEngine.WrapTrackFx(inner, track, MixFxMode.Live, Array.Empty<IEffect>()));
            // 无 FX / 空转 FX：Snapshot 原样透传（Live 必须包装，否则播放中打开开关听不到）
            Assert.Same(inner, RenderEngine.WrapTrackFx(inner, new UTrack(), MixFxMode.Snapshot, Array.Empty<IEffect>()));
            Assert.Same(inner, RenderEngine.WrapTrackFx(inner, new UTrack { MixFx = IdleFx() }, MixFxMode.Snapshot, Array.Empty<IEffect>()));
            Assert.NotSame(inner, RenderEngine.WrapTrackFx(inner, new UTrack { MixFx = IdleFx() }, MixFxMode.Live, Array.Empty<IEffect>()));
            // 有 VST 时即使空转 FX 也要建链
            Assert.NotSame(inner, RenderEngine.WrapTrackFx(inner, new UTrack { MixFx = IdleFx() }, MixFxMode.Snapshot, vst));
        }

        [Fact]
        public void VstChain_SitsOnTopOfBuiltInEffects() {
            // 内置 +12 dB 高架（8k 处 +6 dB → 0.2→0.4），VST ×2 → ~0.8
            var track = new UTrack { MixFx = BrightEq(true) };
            var source = RenderEngine.WrapTrackFx(new SineSource(), track, MixFxMode.Snapshot, new IEffect[] { new GainEffect(2f) });
            int position = 0;
            var output = Render(source, ref position, 8);
            Assert.InRange(Peak(output, 4 * Block), 0.7f, 0.9f);
        }

        // ── 音频线程零分配 ───────────────────────────────────────────────

        [Fact]
        public void Mix_SteadyState_DoesNotAllocate() {
            var track = new UTrack { MixFx = BrightEq(true) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            var buffer = new float[Block];
            int position = 0;

            // 预热：首块按需扩容、JIT、以及 Sync → Configure 的慢路径
            for (int b = 0; b < 4; b++) {
                track.MixFx.EqLowDb = (b % 2 == 0) ? 1.0 : 0.0;
                position = source.Mix(position, buffer, 0, Block);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int b = 0; b < 64; b++) {
                // 改参数会走 Sync → Configure，也必须在音频线程零分配
                track.MixFx.EqLowDb = (b % 2 == 0) ? 1.0 : 0.0;
                position = source.Mix(position, buffer, 0, Block);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 双窗口：**分层 JIT 会在执行线程上编译**（首次运行 / `-t:Rebuild` 后最明显），
            // 那是一次性噪声、会污染第一个窗口（实测 7 次全量里偶发 1 次）。
            // 第一个窗口只用来让编译与慢路径沉淀，第二个窗口做严格 0 断言 ——
            // 既保住"零分配"的严格性，又不把 JIT 噪声当成回归。
            long firstWindow = allocated;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int b = 0; b < 64; b++) {
                track.MixFx.EqLowDb = (b % 2 == 0) ? 1.0 : 0.0;
                position = source.Mix(position, buffer, 0, Block);
            }
            long steadyWindow = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, steadyWindow);
            Assert.True(firstWindow >= steadyWindow, $"首个窗口 {firstWindow} 不应少于稳态窗口 {steadyWindow}");
        }

        [Fact]
        public void Mix_100msBackendBlock_DoesNotAllocateOnFirstCall() {
            // 录制（WasapiOut 100ms）与导出（CreateWaveFile16 的 100ms 块）会一次要
            // 8820 个样本：预分配必须覆盖它，否则音频线程首块就要扩容。
            int block = AudioSettings.SampleRate / 10 * AudioSettings.Channels;
            Assert.True(block > 8192, "44.1k 立体声 100ms 的样本数应超过 BlockSize 派生的 8192");

            // 用另一个实例预热同尺寸路径（JIT），被测实例保持"首次调用"
            var warmTrack = new UTrack { MixFx = BrightEq(true) };
            var warm = MixFxSource.WrapLive(new SineSource(), warmTrack);
            var warmBuffer = new float[block];
            warm.Mix(0, warmBuffer, 0, block);

            var track = new UTrack { MixFx = BrightEq(true) };
            var source = MixFxSource.WrapLive(new SineSource(), track);
            var buffer = new float[block];
            long before = GC.GetAllocatedBytesForCurrentThread();
            int pos = source.Mix(0, buffer, 0, block);
            source.Mix(pos, buffer, 0, block);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }

        // ── seek 清残留 ──────────────────────────────────────────────────

        [Fact]
        public void Seek_ClearsDspResidue() {
            var fx = new UMixFx {
                Enabled = true,
                EqLowDb = 0, EqMidDb = 0, EqHighDb = 0,
                CompPreset = FxPresets.Off, CompRatio = 1, CompMakeupDb = 0,
                ReverbPreset = "hall", ReverbWet = 1.0,
            };
            var track = new UTrack { MixFx = fx };
            var jumped = MixFxSource.WrapLive(new SineSource(), track);
            var fresh = MixFxSource.WrapLive(new SineSource(), track);

            int position = 0;
            Render(jumped, ref position, 8); // 建立混响尾音

            const int farAway = 200000;
            int jumpedPos = farAway, freshPos = farAway;
            var jumpedOut = Render(jumped, ref jumpedPos, 4);
            var freshOut = Render(fresh, ref freshPos, 4);

            // seek 后与新链逐样本一致 = 旧位置的混响尾音没有跟过来
            AssertSamplesEqual(freshOut, jumpedOut);
        }
    }
}
