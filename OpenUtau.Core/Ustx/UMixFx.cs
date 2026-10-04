namespace OpenUtau.Core.Ustx {
    /// <summary>
    /// Per-track post-processing effects state.  Persisted in ustx alongside
    /// the rest of the track.  When a UTrack's MixFx is null or has Enabled =
    /// false, the render path bypasses the entire FX chain (zero overhead).
    /// Individual effects can also be bypassed with their own toggles.
    /// </summary>
    public class UMixFx {
        public bool Enabled { get; set; } = false;

        // Per-module power switches (independent of global Enabled).
        // Default true so ustx files written before these existed load with
        // every module on, exactly as they rendered.
        public bool EqEnabled { get; set; } = true;
        public bool CompEnabled { get; set; } = true;
        public bool ReverbEnabled { get; set; } = true;

        // Plus 旧键兼容别名（反向语义）：旧 ustx 写的是 EqBypassed 等，
        // 保留为读写属性即可让旧工程自动迁移到 EqEnabled 语义。
        // 两套键序列化时都会写出，但取值恒为互反，故加载顺序不影响结果（幂等）。
        public bool EqBypassed { get => !EqEnabled; set => EqEnabled = !value; }
        public bool CompBypassed { get => !CompEnabled; set => CompEnabled = !value; }
        public bool ReverbBypassed { get => !ReverbEnabled; set => ReverbEnabled = !value; }

        // Preset name keys (kept for UI display only).  Slider values below
        // are the source of truth for the actual DSP.
        public string EqPreset { get; set; } = "vocal_air";
        public string CompPreset { get; set; } = "gentle";
        public string ReverbPreset { get; set; } = "small_room";

        public double EqLowDb { get; set; } = 0.0;
        public double EqMidFreq { get; set; } = 3000.0;
        public double EqMidDb { get; set; } = 1.5;
        public double EqHighDb { get; set; } = 3.0;

        public double CompThresholdDb { get; set; } = -18.0;
        public double CompRatio { get; set; } = 2.0;
        public double CompMakeupDb { get; set; } = 2.5;

        public double ReverbSize { get; set; } = 0.30;
        public double ReverbDamp { get; set; } = 0.7;
        public double ReverbWet { get; set; } = 1.0;
        public double ReverbPreDelayMs { get; set; } = 0.0;

        public UMixFx Clone() {
            return new UMixFx {
                Enabled = Enabled,
                EqEnabled = EqEnabled, CompEnabled = CompEnabled, ReverbEnabled = ReverbEnabled,
                EqPreset = EqPreset, CompPreset = CompPreset, ReverbPreset = ReverbPreset,
                EqLowDb = EqLowDb, EqMidFreq = EqMidFreq, EqMidDb = EqMidDb, EqHighDb = EqHighDb,
                CompThresholdDb = CompThresholdDb, CompRatio = CompRatio, CompMakeupDb = CompMakeupDb,
                ReverbSize = ReverbSize, ReverbDamp = ReverbDamp, ReverbWet = ReverbWet,
                ReverbPreDelayMs = ReverbPreDelayMs,
            };
        }
    }
}
