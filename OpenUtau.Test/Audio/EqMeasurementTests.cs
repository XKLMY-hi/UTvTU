using System;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 用例组 1：EQ —— 提升某频段 → 该频段能量显著上升、其他频段基本不变。
    /// 信号为已知频率正弦之和（300 Hz / 1 kHz / 8 kHz），测量 Hann-FFT 频带等效 RMS。
    /// </summary>
    [Collection("AudioFixture")]
    public class EqMeasurementTests {
        readonly ITestOutputHelper output;

        public EqMeasurementTests(ITestOutputHelper output) {
            this.output = output;
        }

        const double LowFreq = 300;
        const double MidFreq = 1000;
        const double HighFreq = 8000;

        /// <summary>三段正弦叠加（各 0.1 振幅）；带 10 ms 淡入淡出避免边界宽频瞬变。</summary>
        static float[] ThreeToneTape(double seconds = 0.6) {
            var a = AudioFixtures.Sine(LowFreq, seconds, 0.1);
            var b = AudioFixtures.Sine(MidFreq, seconds, 0.1);
            var c = AudioFixtures.Sine(HighFreq, seconds, 0.1);
            var sum = new float[a.Length];
            for (int i = 0; i < sum.Length; i++) {
                sum[i] = a[i] + b[i] + c[i];
            }
            AudioFixtures.ApplyFade(sum, 10);
            return sum;
        }

        static UMixFx Eq(double lowDb = 0, double midDb = 0, double highDb = 0, bool eqEnabled = true) => new() {
            Enabled = true,
            EqEnabled = eqEnabled,
            EqLowDb = lowDb, EqMidFreq = MidFreq, EqMidDb = midDb, EqHighDb = highDb,
            CompEnabled = false,
            ReverbEnabled = false,
            ReverbPreset = "off", ReverbWet = 0,
        };

        static UProject Project(UMixFx fx) => AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec {
            Samples = ThreeToneTape(),
            MixFx = fx,
        });

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        static double Band(float[] x, double center, double halfWidth = 120) =>
            AudioMeasure.BandEnergyDb(x, AudioFixtures.Channels, center - halfWidth, center + halfWidth, AudioFixtures.Rate);

        [Fact]
        public void HighShelfBoost_RaisesHighBandOnly() {
            var dry = AudioFixtures.RenderMixdown(Project(Eq()), Samples(0.6));
            var boosted = AudioFixtures.RenderMixdown(Project(Eq(highDb: 12)), Samples(0.6));

            double dLow = Band(boosted, LowFreq) - Band(dry, LowFreq);
            double dMid = Band(boosted, MidFreq) - Band(dry, MidFreq);
            double dHigh = Band(boosted, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqHighDb=12 → Δ低(300Hz)={dLow:F3} dB, Δ中(1k)={dMid:F3} dB, Δ高(8k)={dHigh:F3} dB " +
                             $"(拐点处理论 +6.02 dB)");

            Assert.InRange(dHigh, 5.5, 6.5);
            Assert.InRange(Math.Abs(dLow), 0, 0.3);
            Assert.InRange(Math.Abs(dMid), 0, 0.3);
        }

        [Fact]
        public void MidPeakBoost_RaisesMidBandOnly() {
            var dry = AudioFixtures.RenderMixdown(Project(Eq()), Samples(0.6));
            var boosted = AudioFixtures.RenderMixdown(Project(Eq(midDb: 9)), Samples(0.6));

            double dLow = Band(boosted, LowFreq) - Band(dry, LowFreq);
            double dMid = Band(boosted, MidFreq) - Band(dry, MidFreq);
            double dHigh = Band(boosted, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqMidDb=+9 @1kHz (Q=0.707) → Δ低={dLow:F3} dB, Δ中={dMid:F3} dB, Δ高={dHigh:F3} dB");

            Assert.InRange(dMid, 8.0, 10.0);
            // 峰值滤波器对其他频段有旁瓣泄漏，但应远小于目标频段
            Assert.InRange(Math.Abs(dLow), 0, 2.0);
            Assert.InRange(Math.Abs(dHigh), 0, 2.0);
        }

        [Fact]
        public void MidPeakCut_LowersMidBandOnly() {
            var dry = AudioFixtures.RenderMixdown(Project(Eq()), Samples(0.6));
            var cut = AudioFixtures.RenderMixdown(Project(Eq(midDb: -9)), Samples(0.6));

            double dMid = Band(cut, MidFreq) - Band(dry, MidFreq);
            double dHigh = Band(cut, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqMidDb=-9 → Δ中={dMid:F3} dB, Δ高={dHigh:F3} dB");
            Assert.InRange(dMid, -10.0, -8.0);
            Assert.InRange(Math.Abs(dHigh), 0, 2.0);
        }

        [Fact]
        public void LowShelfBoost_RaisesLowBand() {
            // 低架 f0=200 Hz 且 S=1，过渡带很宽：300 Hz 处只有 ~+2.3 dB，60 Hz 才接近全量 +12 dB。
            // 故用 60 Hz + 8 kHz 双音验证"低频段显著上升、高频段不变"。
            var lowTone = AudioFixtures.Sine(60, 0.6, 0.1);
            var highTone = AudioFixtures.Sine(HighFreq, 0.6, 0.1);
            var tape = new float[lowTone.Length];
            for (int i = 0; i < tape.Length; i++) {
                tape[i] = lowTone[i] + highTone[i];
            }
            AudioFixtures.ApplyFade(tape, 10);
            var dry = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec { Samples = tape, MixFx = Eq() }), Samples(0.6));
            var boosted = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec { Samples = tape, MixFx = Eq(lowDb: 12) }), Samples(0.6));

            double expected = ExpectedLowShelfDb(60, lowDb: 12);
            double dLow = AudioMeasure.BandEnergyDb(boosted, AudioFixtures.Channels, 20, 110, AudioFixtures.Rate)
                        - AudioMeasure.BandEnergyDb(dry, AudioFixtures.Channels, 20, 110, AudioFixtures.Rate);
            double dHigh = Band(boosted, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqLowDb=+12：Δ低(60Hz)={dLow:F3} dB（DSP 自身 ResponseDb(60)={expected:F3} dB）, Δ高(8k)={dHigh:F3} dB");
            Assert.InRange(dLow, expected - 0.5, expected + 0.5);
            Assert.True(dLow > 8.0, $"低频段应显著上升，实测 {dLow:F3} dB");
            Assert.InRange(dHigh, -0.5, 0.5);
        }

        /// <summary>用产品自身的 <see cref="BiquadEQ.ResponseDb"/> 作为期望值（DSP 数学 ↔ 渲染音频互证）。</summary>
        static double ExpectedLowShelfDb(double freq, double lowDb) {
            var eq = new BiquadEQ(AudioFixtures.Rate, AudioFixtures.Channels);
            eq.Configure(lowDb, MidFreq, 0.707, 0, 0);
            return eq.ResponseDb(freq);
        }

        /// <summary>
        /// 强互证用例：渲染音频实测的频带增益，必须与 EQ 自身的频响数学一致（±0.5 dB）。
        /// 这条把"曲线屏画出来的响应"和"真正渲染出来的声音"钉在一起。
        /// </summary>
        [Fact]
        public void RenderedEqMatchesBiquadResponseMath() {
            var cases = new (double freq, double lowDb, double midDb, double highDb)[] {
                (LowFreq, 12, 0, 0),
                (MidFreq, 0, 9, 0),
                (MidFreq, 0, -9, 0),
                (HighFreq, 0, 0, 12),
                (HighFreq, 0, 0, -12),
                (100, 6, 0, 0),
            };
            foreach (var c in cases) {
                var tape = AudioFixtures.Sine(c.freq, 0.6, 0.1);
                var dry = AudioFixtures.RenderMixdown(
                    AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec { Samples = tape, MixFx = Eq() }), Samples(0.6));
                var wet = AudioFixtures.RenderMixdown(
                    AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec {
                        Samples = tape, MixFx = Eq(c.lowDb, c.midDb, c.highDb),
                    }), Samples(0.6));

                var math = new BiquadEQ(AudioFixtures.Rate, AudioFixtures.Channels);
                math.Configure(c.lowDb, MidFreq, 0.707, c.midDb, c.highDb);
                double expected = math.ResponseDb(c.freq);

                double half = Math.Max(40, c.freq * 0.2);
                double measured = AudioMeasure.BandEnergyDb(wet, AudioFixtures.Channels, c.freq - half, c.freq + half, AudioFixtures.Rate)
                                - AudioMeasure.BandEnergyDb(dry, AudioFixtures.Channels, c.freq - half, c.freq + half, AudioFixtures.Rate);
                output.WriteLine($"f={c.freq,6:F0} Hz (low={c.lowDb},mid={c.midDb},high={c.highDb}): " +
                                 $"实测 {measured:F3} dB vs ResponseDb {expected:F3} dB（差 {measured - expected:F3}）");
                Assert.InRange(measured - expected, -0.5, 0.5);
            }
        }

        [Fact]
        public void EqModuleDisabled_ChangesNothing() {
            var dry = AudioFixtures.RenderMixdown(Project(Eq()), Samples(0.6));
            var bypassed = AudioFixtures.RenderMixdown(Project(Eq(highDb: 12, eqEnabled: false)), Samples(0.6));

            double dLow = Band(bypassed, LowFreq) - Band(dry, LowFreq);
            double dMid = Band(bypassed, MidFreq) - Band(dry, MidFreq);
            double dHigh = Band(bypassed, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqEnabled=false（参数仍为 +12）→ Δ低={dLow:F4} dB, Δ中={dMid:F4} dB, Δ高={dHigh:F4} dB");
            Assert.InRange(Math.Abs(dLow), 0, 0.05);
            Assert.InRange(Math.Abs(dMid), 0, 0.05);
            Assert.InRange(Math.Abs(dHigh), 0, 0.05);
        }

        [Fact]
        public void EqBypassedParameter_IsHonoured() {
            // 旧 ustx 的反向键 EqBypassed = true → 模块关闭（迁移语义，产品键）
            var fx = Eq(highDb: 12, eqEnabled: false);
            fx.EqBypassed = true;
            Assert.False(fx.EqEnabled);
            var dry = AudioFixtures.RenderMixdown(Project(Eq()), Samples(0.6));
            var bypassed = AudioFixtures.RenderMixdown(Project(fx), Samples(0.6));
            double dHigh = Band(bypassed, HighFreq) - Band(dry, HighFreq);
            output.WriteLine($"EqBypassed=true → Δ高={dHigh:F4} dB");
            Assert.InRange(Math.Abs(dHigh), 0, 0.05);
        }
    }
}
