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
