using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 用例组 2：压缩器 —— 阈值/比率变化 → 峰值下降、增益衰减随输入电平单调。
    /// 用 1 kHz 稳态正弦（包络有时间建立），取末段 200 ms 的稳态 RMS 作为电平读数。
    /// </summary>
    [Collection("AudioFixture")]
    public class CompressorMeasurementTests {
        readonly ITestOutputHelper output;

        public CompressorMeasurementTests(ITestOutputHelper output) {
            this.output = output;
        }

        const double ToneFreq = 1000;

        static UMixFx CompFx(double thresholdDb = -20, double ratio = 4, double makeupDb = 0,
                             double attackDb = 0, bool compEnabled = true) => new() {
            Enabled = true,
            EqEnabled = false,
            CompEnabled = compEnabled,
            CompPreset = "gentle",           // 攻击 10 ms / 释放 120 ms
            CompThresholdDb = thresholdDb,
            CompRatio = ratio,
            CompMakeupDb = makeupDb,
            ReverbEnabled = false,
            ReverbPreset = "off", ReverbWet = 0,
        };

        static UProject Project(double amp, UMixFx? fx) => AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec {
            Samples = AudioFixtures.Tone(ToneFreq, 0.6, amp, fadeMs: 5),
            AttachMixFx = true,
            MixFx = fx,
        });

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        /// <summary>末段 200 ms 的稳态 RMS（dBFS）——避开起音/释放瞬态。</summary>
        static double SteadyRmsDb(float[] rendered) {
            var tail = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 400, 600);
            return AudioMeasure.RmsDb(tail);
        }

        static double SteadyPeakDb(float[] rendered) {
            var tail = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 400, 600);
            return AudioMeasure.PeakDb(tail);
        }

        [Fact]
        public void BelowThreshold_IsTransparent() {
            // 输入 -26 dBFS（0.05）低于阈值 -20 dB → 无压缩
            var dry = AudioFixtures.RenderMixdown(Project(0.05, null), Samples(0.6));
            var comp = AudioFixtures.RenderMixdown(Project(0.05, CompFx()), Samples(0.6));
            double delta = SteadyRmsDb(comp) - SteadyRmsDb(dry);
            output.WriteLine($"输入 -26 dBFS：dry={SteadyRmsDb(dry):F3} dBFS, comp={SteadyRmsDb(comp):F3} dBFS, Δ={delta:F3} dB");
            Assert.InRange(Math.Abs(delta), 0, 0.2);
        }

        [Fact]
        public void AboveThreshold_PeakDrops_AndGainReductionGrowsWithLevel() {
            double[] amps = { 0.05, 0.2, 0.6, 0.9 };
            var results = new List<(double inDb, double outDb, double grDb, double peakDb)>();
            foreach (double amp in amps) {
                var dry = AudioFixtures.RenderMixdown(Project(amp, null), Samples(0.6));
                var comp = AudioFixtures.RenderMixdown(Project(amp, CompFx(thresholdDb: -20, ratio: 4)), Samples(0.6));
                double inDb = SteadyRmsDb(dry);
                double outDb = SteadyRmsDb(comp);
                results.Add((inDb, outDb, inDb - outDb, SteadyPeakDb(comp)));
                output.WriteLine($"amp={amp:F2}: 输入 {inDb:F3} dBFS → 输出 {outDb:F3} dBFS, 衰减 GR={inDb - outDb:F3} dB, " +
                                 $"输出峰值 {SteadyPeakDb(comp):F3} dBFS");
            }

            // 增益衰减随输入电平单调不减
            for (int i = 1; i < results.Count; i++) {
                Assert.True(results[i].grDb >= results[i - 1].grDb - 0.2,
                    $"GR 非单调：{results[i - 1].grDb:F3} → {results[i].grDb:F3} dB");
            }
            // 阈值以下无衰减；远高于阈值时衰减显著
            Assert.InRange(results[0].grDb, 0, 0.3);
            Assert.True(results[^1].grDb > 6.0, $"高电平衰减不足：{results[^1].grDb:F3} dB");
            // 阈值以上区段的**局部压缩比**应接近配置的 4:1
            //（整体跨度含阈值以下 + 软拐点区，比例必然被稀释，故不用整体比作判据）
            double spanIn = results[^1].inDb - results[1].inDb;
            double spanOut = results[^1].outDb - results[1].outDb;
            double localRatio = spanIn / Math.Max(0.01, spanOut);
            output.WriteLine($"阈值以上局部：输入 {spanIn:F2} dB → 输出 {spanOut:F2} dB（局部压缩比 {localRatio:F2}:1，配置 4:1）");
            Assert.InRange(localRatio, 2.0, 6.0);
        }

        [Fact]
        public void HigherRatio_GivesMoreGainReduction() {
            double amp = 0.6;
            var dry = AudioFixtures.RenderMixdown(Project(amp, null), Samples(0.6));
            double inDb = SteadyRmsDb(dry);
            var gr = new List<(double ratio, double grDb)>();
            foreach (double ratio in new[] { 1.0, 2.0, 4.0, 10.0 }) {
                var comp = AudioFixtures.RenderMixdown(Project(amp, CompFx(thresholdDb: -20, ratio: ratio)), Samples(0.6));
                double grDb = inDb - SteadyRmsDb(comp);
                gr.Add((ratio, grDb));
                output.WriteLine($"ratio={ratio:F1}: GR={grDb:F3} dB（输入 {inDb:F3} dBFS）");
            }
            for (int i = 1; i < gr.Count; i++) {
                Assert.True(gr[i].grDb >= gr[i - 1].grDb - 0.2,
                    $"GR 未随比率单调：ratio={gr[i - 1].ratio}→{gr[i].ratio} 时 {gr[i - 1].grDb:F3}→{gr[i].grDb:F3} dB");
            }
            Assert.InRange(gr[0].grDb, 0, 0.3);          // ratio=1 = 不压缩
            Assert.True(gr[^1].grDb > gr[0].grDb + 4.0, "ratio=10 未明显多于 ratio=1");
        }

        [Fact]
        public void LowerThreshold_GivesMoreGainReduction() {
            double amp = 0.6;
            var dry = AudioFixtures.RenderMixdown(Project(amp, null), Samples(0.6));
            double inDb = SteadyRmsDb(dry);
            var gr = new List<(double threshold, double grDb)>();
            foreach (double threshold in new[] { 0.0, -10.0, -20.0, -30.0 }) {
                var comp = AudioFixtures.RenderMixdown(Project(amp, CompFx(thresholdDb: threshold, ratio: 4)), Samples(0.6));
                double grDb = inDb - SteadyRmsDb(comp);
                gr.Add((threshold, grDb));
                output.WriteLine($"threshold={threshold:F1} dB: GR={grDb:F3} dB");
            }
            for (int i = 1; i < gr.Count; i++) {
                Assert.True(gr[i].grDb >= gr[i - 1].grDb - 0.2,
                    $"阈值下调时 GR 非单调：{gr[i - 1].grDb:F3}→{gr[i].grDb:F3} dB");
            }
            Assert.True(gr[^1].grDb > gr[0].grDb + 4.0, "阈值 -30 未明显多于阈值 0");
        }

        [Fact]
        public void MakeupGain_RaisesOutputButKeepsThresholdBehaviour() {
            double amp = 0.6;
            var dry = AudioFixtures.RenderMixdown(Project(amp, null), Samples(0.6));
            var noMakeup = AudioFixtures.RenderMixdown(Project(amp, CompFx(makeupDb: 0)), Samples(0.6));
            var makeup = AudioFixtures.RenderMixdown(Project(amp, CompFx(makeupDb: 6)), Samples(0.6));
            double delta = SteadyRmsDb(makeup) - SteadyRmsDb(noMakeup);
            output.WriteLine($"makeup 0→+6 dB：输出 {SteadyRmsDb(noMakeup):F3} → {SteadyRmsDb(makeup):F3} dBFS, Δ={delta:F3} dB");
            Assert.InRange(delta, 5.7, 6.3);
            Assert.True(SteadyRmsDb(makeup) < SteadyRmsDb(dry) + 6.0, "补偿增益不应让输出超过未压缩输入 +6 dB");
        }

        [Fact]
        public void CompModuleDisabled_ChangesNothing() {
            double amp = 0.6;
            var dry = AudioFixtures.RenderMixdown(Project(amp, null), Samples(0.6));
            var bypassed = AudioFixtures.RenderMixdown(Project(amp, CompFx(compEnabled: false)), Samples(0.6));
            double delta = SteadyRmsDb(bypassed) - SteadyRmsDb(dry);
            output.WriteLine($"CompEnabled=false → Δ={delta:F4} dB（逐样本差 {AudioMeasure.FirstDifference(dry, bypassed)}）");
            Assert.InRange(Math.Abs(delta), 0, 0.02);
        }
    }
}
