using System;
using OpenUtau.Core.SignalChain.Effects;
using Serilog;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// Per-track audio effect chain.  Renders the inner source into a scratch buffer,
    /// then applies a list of IEffect processors in series before additively mixing
    /// into the output.
    ///
    /// 分工（实时效果机架移植后）：内置三件套（EQ/压缩/混响）由
    /// <see cref="MixFxSource"/> 负责（逐块跟随参数 + 开关交叉淡化），本类**只承载
    /// VST 效果**，位于内置三件套之后（链序：Fader → MixFxSource → EffectChain(VST)）。
    /// 此前本类会顺带按 UMixFx 构建内置效果，那会与 MixFxSource 重复处理同一信号。
    /// </summary>
    public class EffectChain : ISignalSource {
        // 采样率/声道取全局 AudioSettings（B 阶段格式显式化）
        private readonly ISignalSource source;
        private readonly IEffect[] effects;
        private float[]? scratch;

        public EffectChain(ISignalSource source, IEffect[] effects) {
            this.source = source;
            this.effects = effects;
            // 预分配典型块大小（VST Setup 块 4096），避免播放中按需扩容
            scratch = new float[4096];
        }

        public bool IsReady(int position, int count) => source.IsReady(position, count);

        /// <summary>Total reported latency of all effects in the chain (for future PDC).</summary>
        public int TotalLatency {
            get {
                int total = 0;
                foreach (var fx in effects) total += fx.LatencySamples;
                return total;
            }
        }

        /// <summary>Drop time-domain state of all effects. Call on seek/position jump.</summary>
        public void Reset() {
            foreach (var fx in effects)
                fx.Reset();
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            if (scratch == null || scratch.Length < count)
                scratch = new float[count];
            Array.Clear(scratch, 0, count);
            int ret = source.Mix(position, scratch, 0, count);

            foreach (var fx in effects) {
                if (!fx.IsBypassed)
                    fx.Process(scratch, 0, count);
            }

            for (int i = 0; i < count; i++)
                buffer[index + i] += scratch[i];

            return ret;
        }

        /// <summary>True when any effect in the chain is active.</summary>
        public bool HasActiveEffects {
            get {
                foreach (var fx in effects)
                    if (!fx.IsBypassed) return true;
                return false;
            }
        }

        // ── Factory helpers ───────────────────────────────────────

        /// <summary>
        /// 构建只含 VST 效果的链。无效果时原样返回内层源（零开销）。
        /// 内置三件套不在此处构建——见类注释的分工说明。
        /// </summary>
        public static ISignalSource Build(ISignalSource inner, IEffect[]? effects = null) {
            if (effects == null || effects.Length == 0) {
                Log.Information("[EffectChain] No VST effects — returning inner source unchanged");
                return inner;
            }
            Log.Information($"[EffectChain] Created with {effects.Length} VST effect(s)");
            return new EffectChain(inner, effects);
        }
    }
}
