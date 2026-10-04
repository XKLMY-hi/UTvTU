using System;
using System.Collections.Generic;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 用例组 3：混响 —— 湿声/尺寸变化 → 尾部能量与衰减时间（RT60 近似）单调上升。
    /// 信号 = 0.15 s 纯音脉冲 + 1.0 s 静音；尾部能量取脉冲结束后 250–450 ms 窗口，
    /// RT60 用 Schroeder 反向积分在 -5..-15 dB 段拟合外推（口径见 AudioMeasure 注释）。
    /// </summary>
    [Collection("AudioFixture")]
    public class ReverbMeasurementTests {
        readonly ITestOutputHelper output;

        public ReverbMeasurementTests(ITestOutputHelper output) {
            this.output = output;
        }

        const double BurstMs = 150;
        const double TailFromMs = 250;
        const double TailToMs = 450;

        static float[] BurstThenSilence() =>
            AudioFixtures.Concat(
                AudioFixtures.Tone(1000, BurstMs / 1000.0, 0.3, fadeMs: 5),
                AudioFixtures.Silence(1.0));

        static UMixFx ReverbFx(double wet = 1.0, double size = 0.55, string preset = "hall", bool reverbEnabled = true) => new() {
            Enabled = true,
            EqEnabled = false,
            CompEnabled = false,
            ReverbEnabled = reverbEnabled,
            ReverbPreset = preset,
            ReverbSize = size,
            ReverbDamp = 0.4,
            ReverbWet = wet,
            ReverbPreDelayMs = 0,
        };

        static UProject Project(UMixFx fx) => AudioFixtures.BuildWaveProject(new AudioFixtures.TrackSpec {
            Samples = BurstThenSilence(),
            MixFx = fx,
        });

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        static (double tailDb, double rt60) Measure(float[] rendered) {
            var tail = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, TailFromMs, TailToMs);
            return (AudioMeasure.RmsDb(tail), AudioMeasure.Rt60Approx(rendered, AudioFixtures.Channels, AudioFixtures.Rate, BurstMs));
        }

        [Fact]
        public void ReverbDisabled_TailIsDigitalSilence() {
            var dry = AudioFixtures.RenderMixdown(Project(ReverbFx(reverbEnabled: false)), Samples(1.15));
            var (tailDb, rt60) = Measure(dry);
            output.WriteLine($"ReverbEnabled=false → 尾部 RMS={tailDb:F2} dBFS, RT60={(double.IsNaN(rt60) ? "n/a" : rt60.ToString("F3"))}");
            Assert.True(tailDb <= AudioMeasure.SilentDb, $"未开混响时尾部应为静音，实测 {tailDb:F2} dBFS");
        }

        [Fact]
        public void WetLevel_RaisesTailEnergyMonotonically() {
            var dry = AudioFixtures.RenderMixdown(Project(ReverbFx(reverbEnabled: false)), Samples(1.15));
            var results = new List<(double wet, double tailDb, double rt60)>();
            foreach (double wet in new[] { 0.25, 0.5, 1.0, 2.0 }) {
                var rendered = AudioFixtures.RenderMixdown(Project(ReverbFx(wet: wet)), Samples(1.15));
                var (tailDb, rt60) = Measure(rendered);
                results.Add((wet, tailDb, rt60));
                output.WriteLine($"ReverbWet={wet:F2}: 尾部 RMS={tailDb:F2} dBFS, RT60={(double.IsNaN(rt60) ? "n/a" : rt60.ToString("F3") + " s")}");
            }
            var (dryTail, _) = Measure(dry);
            output.WriteLine($"（干声对照尾部 RMS={dryTail:F2} dBFS）");
            for (int i = 1; i < results.Count; i++) {
                Assert.True(results[i].tailDb >= results[i - 1].tailDb + 1.0,
                    $"尾部能量未随湿声单调上升：wet={results[i - 1].wet}→{results[i].wet} 时 {results[i - 1].tailDb:F2}→{results[i].tailDb:F2} dBFS");
            }
        }

        [Fact]
        public void RoomSize_RaisesDecayTimeMonotonically() {
            var results = new List<(double size, double rt60, double tailDb)>();
            foreach (double size in new[] { 0.2, 0.5, 0.9 }) {
                var rendered = AudioFixtures.RenderMixdown(Project(ReverbFx(size: size, wet: 1.0)), Samples(1.15));
                var (tailDb, rt60) = Measure(rendered);
                results.Add((size, rt60, tailDb));
                output.WriteLine($"ReverbSize={size:F2}: RT60≈{(double.IsNaN(rt60) ? "n/a" : rt60.ToString("F3") + " s")}, 尾部 RMS={tailDb:F2} dBFS");
            }
            for (int i = 1; i < results.Count; i++) {
                Assert.False(double.IsNaN(results[i].rt60), $"RT60 不可测（size={results[i].size}）");
                Assert.True(results[i].rt60 > results[i - 1].rt60 * 1.05,
                    $"衰减时间未随房间尺寸上升：size={results[i - 1].size}→{results[i].size} 时 {results[i - 1].rt60:F3}→{results[i].rt60:F3} s");
            }
        }

        [Fact]
        public void ReverbPreset_ComparesAsExpected() {
            var results = new List<(string preset, double rt60, double tailDb)>();
            foreach (string preset in new[] { "small_room", "vocal_plate", "hall", "ambient" }) {
                var rendered = AudioFixtures.RenderMixdown(Project(ReverbFx(preset: preset, size: 0.55)), Samples(1.15));
                var (tailDb, rt60) = Measure(rendered);
                results.Add((preset, rt60, tailDb));
                output.WriteLine($"preset={preset,-12}: RT60≈{(double.IsNaN(rt60) ? "n/a" : rt60.ToString("F3") + " s")}, 尾部 RMS={tailDb:F2} dBFS");
            }
            // small_room (wet 0.18) < vocal_plate (0.22) < hall (0.28) < ambient (0.35)
            for (int i = 1; i < results.Count; i++) {
                Assert.True(results[i].tailDb > results[i - 1].tailDb,
                    $"预设尾部能量次序异常：{results[i - 1].preset}({results[i - 1].tailDb:F2}) → {results[i].preset}({results[i].tailDb:F2})");
            }
        }

        [Fact]
        public void PreDelay_ShiftsTailOnset() {
            var noDelay = AudioFixtures.RenderMixdown(Project(ReverbFx(preset: "hall", wet: 1.0)), Samples(1.15));
            var fx = ReverbFx(preset: "hall", wet: 1.0);
            fx.ReverbPreDelayMs = 60;
            var delayed = AudioFixtures.RenderMixdown(Project(fx), Samples(1.15));

            // 直接测"整体延迟量"：尾部（脉冲结束后）的互相关最佳滞后应 ≈ 60 ms
            double lag = AudioMeasure.BestLagMs(noDelay, delayed, AudioFixtures.Channels, AudioFixtures.Rate,
                                                BurstMs, 950, maxLagMs: 150);
            // 对照：同一信号自相关的最佳滞后应为 0
            double selfLag = AudioMeasure.BestLagMs(noDelay, noDelay, AudioFixtures.Channels, AudioFixtures.Rate,
                                                    BurstMs, 950, maxLagMs: 150);
            output.WriteLine($"PreDelay 60 ms → 实测尾部互相关滞后 {lag:F2} ms（自身对照 {selfLag:F2} ms）");
            Assert.InRange(lag, 55, 65);
            Assert.InRange(selfLag, -1, 1);
        }
    }
}
