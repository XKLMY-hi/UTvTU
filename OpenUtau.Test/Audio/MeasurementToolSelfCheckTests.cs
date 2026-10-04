using System;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 仪器自检：先用**解析已知**的信号标定测量工具本身，再用它去测产品。
    /// 没有这一步，报告里的数值就只是"某个函数的输出"，无法证明尺子是准的。
    /// </summary>
    [Collection("AudioFixture")]
    public class MeasurementToolSelfCheckTests {
        readonly ITestOutputHelper output;

        public MeasurementToolSelfCheckTests(ITestOutputHelper output) {
            this.output = output;
        }

        const double Rate = 44100;

        /// <summary>解析正弦：幅度 A、频率 f、时长秒。</summary>
        static float[] AnalyticSine(double freq, double amp, double seconds) {
            int n = (int)(seconds * Rate);
            var x = new float[n];
            for (int i = 0; i < n; i++) {
                x[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / Rate));
            }
            return x;
        }

        [Fact]
        public void PeakAndRms_MatchAnalyticSine() {
            const double amp = 0.5;
            var x = AnalyticSine(1000, amp, 0.5);
            double rms = AudioMeasure.Rms(x);
            double peak = AudioMeasure.Peak(x);
            double rmsDb = AudioMeasure.RmsDb(x);
            double peakDb = AudioMeasure.PeakDb(x);
            output.WriteLine($"正弦 A=0.5：peak={peak:F6}（解析 {amp:F6}）, rms={rms:F6}（解析 {amp / Math.Sqrt(2):F6}）");
            output.WriteLine($"peakDb={peakDb:F3} dBFS（解析 {-6.0206:F3}）, rmsDb={rmsDb:F3} dBFS（解析 {-9.0309:F3}）");
            Assert.InRange(peak, amp - 1e-4, amp + 1e-4);
            Assert.InRange(rms, amp / Math.Sqrt(2) * 0.999, amp / Math.Sqrt(2) * 1.001);
            Assert.InRange(peakDb, -6.03, -6.01);
            Assert.InRange(rmsDb, -9.04, -9.02);
        }

        [Fact]
        public void BandEnergyAndGoertzel_MatchAnalyticSine() {
            const double amp = 0.5;
            var x = AnalyticSine(1000, amp, 0.5);
            var stereo = AudioFixtures.Sine(1000, 0.5, amp); // 交织立体声
            // bin 中心频率（1378.125 Hz = 64 × 44100/2048）：峰 bin 归一下应精确等于 RMS
            double centHz = 64.0 * AudioFixtures.Rate / AudioMeasure.FftSize;
            var centered = AudioFixtures.Sine(centHz, 0.5, amp);
            double bandCentered = AudioMeasure.BandEnergyDb(centered, AudioFixtures.Channels, centHz - 10, centHz + 10, AudioFixtures.Rate);
            double bandOffCenter = AudioMeasure.BandEnergyDb(stereo, AudioFixtures.Channels, 900, 1100, AudioFixtures.Rate);
            double goertzel = AudioMeasure.GoertzelPowerDb(x, 1000, (int)Rate);
            double offTone = AudioMeasure.GoertzelPowerDb(x, 1500, (int)Rate);
            double dom = AudioMeasure.DominantFreq(stereo, AudioFixtures.Channels, AudioFixtures.Rate, 200, 3000);
            output.WriteLine($"BandEnergyDb @bin中心 {centHz:F1}Hz = {bandCentered:F3} dBFS（解析 RMS {-9.031:F3}）");
            output.WriteLine($"BandEnergyDb @1kHz(非 bin 中心) = {bandOffCenter:F3} dBFS（比解析 RMS 低 {Math.Abs(-9.031 - bandOffCenter):F3} dB——" +
                             $"Hann 主瓣跨 4 个 bin，峰 bin 只含部分能量，属预期偏差）");
            output.WriteLine($"Goertzel @1kHz={goertzel:F3} dBFS（解析峰值 {-6.021:F3}）, @1.5kHz={offTone:F3} dBFS");
            output.WriteLine($"DominantFreq={dom:F2} Hz（真值 1000）");
            Assert.InRange(bandCentered, -9.33, -8.73);            // bin 中心：±0.3 dB
            Assert.InRange(bandOffCenter, -10.6, -8.7);            // 非中心：允许约 1.6 dB 偏低（记录在案）
            Assert.InRange(goertzel, -6.52, -5.52);
            Assert.True(offTone < -30, $"非信号频点应显著更低：{offTone:F2} dBFS");
            Assert.InRange(dom, 995, 1005);
        }

        [Fact]
        public void LufsApprox_TracksGainAndWeighsLowFrequencyDown() {
            var tone1k = AudioFixtures.Sine(1000, 1.0, 0.2);
            var tone1kHot = AudioFixtures.Sine(1000, 1.0, 0.4);   // +6.02 dB
            var tone60 = AudioFixtures.Sine(60, 1.0, 0.2);        // 同 RMS，低频
            double lu1 = AudioMeasure.LufsApprox(tone1k, AudioFixtures.Channels, AudioFixtures.Rate);
            double luHot = AudioMeasure.LufsApprox(tone1kHot, AudioFixtures.Channels, AudioFixtures.Rate);
            double lu60 = AudioMeasure.LufsApprox(tone60, AudioFixtures.Channels, AudioFixtures.Rate);
            output.WriteLine($"LUFS≈ 1kHz@0.2={lu1:F2}, 1kHz@0.4={luHot:F2}（Δ{luHot - lu1:F2} LU，期望 +6.02）");
            output.WriteLine($"LUFS≈ 60Hz@0.2={lu60:F2}（比 1kHz 低 {lu1 - lu60:F2} LU —— K-weighting 高通/高架造成）");
            Assert.InRange(luHot - lu1, 5.8, 6.2);
            Assert.True(lu60 < lu1 - 3, "近似 K-weighting 应把 60 Hz 评得更低");
        }

        [Fact]
        public void CorrelationAndBalance_MatchAnalyticStereo() {
            int frames = (int)(0.5 * Rate);
            var identical = new float[frames * 2];
            var inverted = new float[frames * 2];
            var quadrature = new float[frames * 2];
            var leftOnly = new float[frames * 2];
            for (int i = 0; i < frames; i++) {
                float a = (float)(0.2 * Math.Sin(2 * Math.PI * 500 * i / Rate));
                float q = (float)(0.2 * Math.Cos(2 * Math.PI * 500 * i / Rate));
                identical[i * 2] = a; identical[i * 2 + 1] = a;
                inverted[i * 2] = a; inverted[i * 2 + 1] = -a;
                quadrature[i * 2] = a; quadrature[i * 2 + 1] = q;
                leftOnly[i * 2] = a; leftOnly[i * 2 + 1] = 0;
            }
            output.WriteLine($"corr(同相)={AudioMeasure.Correlation(identical):F5}, corr(反相)={AudioMeasure.Correlation(inverted):F5}, " +
                             $"corr(正交)={AudioMeasure.Correlation(quadrature):F5}");
            output.WriteLine($"balance(左满)={AudioMeasure.BalanceDb(leftOnly):F1} dB, 左右 RMS={AudioMeasure.ChannelRmsDb(leftOnly)}");
            Assert.InRange(AudioMeasure.Correlation(identical), 0.9999, 1.0001);
            Assert.InRange(AudioMeasure.Correlation(inverted), -1.0001, -0.9999);
            Assert.InRange(Math.Abs(AudioMeasure.Correlation(quadrature)), 0, 0.01);
            Assert.InRange(AudioMeasure.BalanceDb(leftOnly), -240, -100);
        }

        [Fact]
        public void Rt60Approx_RecoversSyntheticDecay() {
            // 构造能量衰减 60 dB 恰好耗时 targetRt 秒的信号：A(t)=exp(-αt)，
            // 能量 ∝ A² → 60 dB（因子 1e-6）用时 3·ln10/α = targetRt。
            const double targetRt = 0.5;
            double alpha = 3 * Math.Log(10) / targetRt;
            int frames = (int)(1.2 * Rate);
            var x = new float[frames];
            for (int i = 0; i < frames; i++) {
                double t = (double)i / Rate;
                x[i] = (float)(0.5 * Math.Exp(-alpha * t) * Math.Sin(2 * Math.PI * 1000 * t));
            }
            double measured = AudioMeasure.Rt60Approx(x, 1, (int)Rate, 0);
            output.WriteLine($"人工衰减（真值 RT60={targetRt:F3} s）→ 实测 {measured:F3} s");
            Assert.InRange(measured, targetRt * 0.9, targetRt * 1.1);
        }

        [Fact]
        public void MaxAdjacentDeltaAndFirstDifference_AreExact() {
            var smooth = AnalyticSine(220, 0.2, 0.1);
            var withClick = (float[])smooth.Clone();
            withClick[1000] += 0.5f;
            output.WriteLine($"平滑信号 maxDelta={AudioMeasure.MaxAdjacentDelta(smooth):F5}; " +
                             $"含单点 +0.5 跳变后={AudioMeasure.MaxAdjacentDelta(withClick):F5}");
            // 220 Hz、A=0.2 @44.1k：逐样本最大差 ≈ A·2πf/fs = 0.0063
            Assert.InRange(AudioMeasure.MaxAdjacentDelta(smooth), 0.005, 0.008);
            Assert.True(AudioMeasure.MaxAdjacentDelta(withClick) > 0.4);
            Assert.Equal(1000, AudioMeasure.FirstDifference(smooth, withClick));
            Assert.Equal(-1, AudioMeasure.FirstDifference(smooth, (float[])smooth.Clone()));
            Assert.Equal(3, AudioMeasure.FirstDifference(new float[] { 1, 2, 3 }, new float[] { 1, 2, 3, 4 }));
        }
    }
}
