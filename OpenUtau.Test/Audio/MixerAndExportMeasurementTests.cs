using System;
using System.Collections.Generic;
using System.IO;
using OpenUtau.Core.Export;
using OpenUtau.Core.Format;
using OpenUtau.Core.SignalChain;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-3 用例组 4：推子 / 静音 / 声像 / 主输出（含 master mute）。
    /// 全部走产品渲染路径（RenderEngine → Fader → MixFxSource → MasterAdapter），
    /// 用测量数值断言；无外部依赖（波形轨为内存生成，不读用户机器上的任何文件）。
    /// </summary>
    [Collection("AudioFixture")]
    public class MixerAndExportMeasurementTests {
        readonly ITestOutputHelper output;

        public MixerAndExportMeasurementTests(ITestOutputHelper output) {
            this.output = output;
        }

        const double ToneFreq = 1000;
        const double ToneAmp = 0.2; // 输入峰值
        const double CenterPanGain = 0.70710678; // PanToChannelVolumes(0) = (cos45°, sin45°)

        static AudioFixtures.TrackSpec ToneTrack(double volumeDb = 0, double pan = 0, bool muted = false) =>
            new() {
                Samples = AudioFixtures.Tone(ToneFreq, 0.5, ToneAmp),
                VolumeDb = volumeDb,
                Pan = pan,
                Muted = muted,
                AttachMixFx = false,
            };

        static int Samples(double seconds) => (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        static double Db(double x) => AudioMeasure.ToDb(x);

        [Fact]
        public void DryTrack_CenterPan_IsMinus3dB_EqualPowerLaw() {
            var project = AudioFixtures.BuildWaveProject(ToneTrack());
            var rendered = AudioFixtures.RenderMixdown(project, Samples(0.5));

            double peak = AudioMeasure.Peak(rendered);
            double expected = ToneAmp * CenterPanGain;
            output.WriteLine($"input peak={ToneAmp:F6} rendered peak={peak:F6} ({Db(peak):F2} dBFS) " +
                             $"expect={expected:F6} ({Db(expected):F2} dBFS)");
            // 等功率声像律：居中 = 每声道 ×cos45°（-3.01 dB）——产品既有行为
            Assert.InRange(peak, expected * 0.98, expected * 1.02);
            output.WriteLine($"pan law attenuation = {Db(peak / ToneAmp):F3} dB");
        }

        [Fact]
        public void Fader_Minus6dB_LowersRmsBy6dB() {
            var unity = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(volumeDb: 0)), Samples(0.5));
            var minus6 = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(volumeDb: -6)), Samples(0.5));

            double rms0 = AudioMeasure.RmsDb(unity);
            double rms6 = AudioMeasure.RmsDb(minus6);
            double delta = rms6 - rms0;
            output.WriteLine($"RMS 0dB={rms0:F3} dBFS, -6dB={rms6:F3} dBFS, delta={delta:F3} dB");
            Assert.InRange(delta, -6.15, -5.85);
        }

        [Fact]
        public void TrackMute_IsSilent() {
            var rendered = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(muted: true)), Samples(0.5));
            double peak = AudioMeasure.Peak(rendered);
            output.WriteLine($"muted peak={peak:E3} rms={AudioMeasure.RmsDb(rendered):F2} dBFS");
            Assert.Equal(0, peak);
        }

        [Fact]
        public void Pan_HardLeft_And_HardRight_BalanceAndCorrelation() {
            // 注意：UTrack.Pan 是百分比（-100..100），不是 -1..1 —— 传 -1 只是"几乎居中"
            var left = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(pan: -100)), Samples(0.5));
            var right = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(pan: 100)), Samples(0.5));
            var center = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(pan: 0)), Samples(0.5));

            var (ll, lr) = AudioMeasure.ChannelRmsDb(left);
            var (rl, rr) = AudioMeasure.ChannelRmsDb(right);
            output.WriteLine($"pan=-100: L={ll:F3} R={lr:F3} dBFS, balance={AudioMeasure.BalanceDb(left):F2} dB, corr={AudioMeasure.Correlation(left):F4}");
            output.WriteLine($"pan=+100: L={rl:F3} R={rr:F3} dBFS, balance={AudioMeasure.BalanceDb(right):F2} dB, corr={AudioMeasure.Correlation(right):F4}");
            output.WriteLine($"pan=   0: balance={AudioMeasure.BalanceDb(center):F3} dB, corr={AudioMeasure.Correlation(center):F5}");

            Assert.True(lr <= AudioMeasure.SilentDb, "硬左时右声道应为数字静音");
            // 端点残差（实测记录）：硬左时 sin(0)=0 精确静音；硬右时左声道残留 ~1e-8
            // （float 下 cos(π/2) ≈ -8.7e-8 ≠ 0）→ 约 -164 dBFS，可忽略但如实断言区间
            Assert.InRange(rl, -200, -150);
            output.WriteLine($"端点残差：硬右时左声道 {rl:F3} dBFS（float cos(π/2) ≠ 0，非缺陷）");
            Assert.InRange(AudioMeasure.BalanceDb(left), -240, -100);
            Assert.InRange(AudioMeasure.BalanceDb(right), 100, 240);
            Assert.InRange(Math.Abs(AudioMeasure.BalanceDb(center)), 0, 0.05);
            // 居中同相 → 相关系数 1；硬左（右声道精确 0）→ 0；硬右 → 左声道是极小负系数副本 → ≈ -1
            Assert.InRange(AudioMeasure.Correlation(center), 0.999, 1.0001);
            Assert.InRange(AudioMeasure.Correlation(left), -0.01, 0.01);
            Assert.InRange(AudioMeasure.Correlation(right), -1.0001, -0.999);
        }

        [Fact]
        public void MasterMute_IsSilent_AndMasterVolume_FollowsCurve() {
            var project = AudioFixtures.BuildWaveProject(ToneTrack());
            var normal = AudioFixtures.RenderThroughMaster(project, Samples(0.5), masterVolumeDb: 0);
            var muted = AudioFixtures.RenderThroughMaster(
                AudioFixtures.BuildWaveProject(ToneTrack()), Samples(0.5), masterMuted: true);
            var minus6 = AudioFixtures.RenderThroughMaster(
                AudioFixtures.BuildWaveProject(ToneTrack()), Samples(0.5), masterVolumeDb: -6);
            var minus20 = AudioFixtures.RenderThroughMaster(
                AudioFixtures.BuildWaveProject(ToneTrack()), Samples(0.5), masterVolumeDb: -20);

            double rms0 = AudioMeasure.RmsDb(normal);
            double rms6 = AudioMeasure.RmsDb(minus6);
            double rms20 = AudioMeasure.RmsDb(minus20);
            output.WriteLine($"master: 0dB={rms0:F3}, -6dB={rms6:F3} (Δ{rms6 - rms0:F3}), -20dB={rms20:F3} (Δ{rms20 - rms0:F3}), " +
                             $"muted peak={AudioMeasure.Peak(muted):E3}");
            Assert.Equal(0, AudioMeasure.Peak(muted));
            Assert.InRange(rms6 - rms0, -6.15, -5.85);
            // < -16 dB 走 db*2+16 曲线：-20 → -24 dB → 线性 0.0631 → -24 dB
            Assert.InRange(rms20 - rms0, -24.3, -23.7);
        }

        [Fact]
        public void TwoTracks_SumAdditively() {
            var single = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack()), Samples(0.5));
            var doubled = AudioFixtures.RenderMixdown(
                AudioFixtures.BuildWaveProject(ToneTrack(), ToneTrack()), Samples(0.5));

            double p1 = AudioMeasure.Peak(single);
            double p2 = AudioMeasure.Peak(doubled);
            output.WriteLine($"single peak={p1:F6} ({Db(p1):F2} dBFS), two tracks peak={p2:F6} ({Db(p2):F2} dBFS), ratio={Db(p2 / p1):F3} dB");
            Assert.InRange(Db(p2 / p1), 5.9, 6.1); // 同相叠加 = +6.02 dB
            Assert.InRange(AudioMeasure.Correlation(doubled), 0.999, 1.0001);
        }

        [Fact]
        public void Export_TwoRunsAreBitIdentical_AndMatchMixdown() {
            var wavA = Path.Combine(Path.GetTempPath(), $"w7-export-a-{Guid.NewGuid():N}.wav");
            var wavB = Path.Combine(Path.GetTempPath(), $"w7-export-b-{Guid.NewGuid():N}.wav");
            try {
                var options = new ExportSession.Options { PerTrack = false, ApplyMixFx = true };
                float[] mixdown;
                using (var cts = new System.Threading.CancellationTokenSource()) {
                    mixdown = AudioFixtures.RenderMixdown(
                        AudioFixtures.BuildWaveProject(ToneTrack()), Samples(0.5));
                }

                new ExportSession(AudioFixtures.BuildWaveProject(ToneTrack()), wavA, options)
                    .RunAsync(new Progress<ExportSession.ProgressInfo>(_ => { })).GetAwaiter().GetResult();
                new ExportSession(AudioFixtures.BuildWaveProject(ToneTrack()), wavB, options)
                    .RunAsync(new Progress<ExportSession.ProgressInfo>(_ => { })).GetAwaiter().GetResult();

                var bytesA = File.ReadAllBytes(wavA);
                var bytesB = File.ReadAllBytes(wavB);
                var exported = ReadWav(wavA);

                double maxDiff = 0;
                for (int i = 0; i < Math.Min(exported.Length, mixdown.Length); i++) {
                    maxDiff = Math.Max(maxDiff, Math.Abs(exported[i] - mixdown[i]));
                }
                output.WriteLine($"wavA={bytesA.Length} bytes, wavB={bytesB.Length} bytes, identical={bytesA.AsSpan().SequenceEqual(bytesB)}");
                output.WriteLine($"exported peak={AudioMeasure.Peak(exported):F6}, mixdown peak={AudioMeasure.Peak(mixdown):F6}, " +
                                 $"maxDiff={maxDiff:E3} = {maxDiff * 32768:F2} × 16-bit LSB (1 LSB={1.0 / 32768:E3})");

                Assert.Equal(bytesA.Length, bytesB.Length);
                Assert.True(bytesA.AsSpan().SequenceEqual(bytesB), "两次导出的 WAV 必须逐字节一致");
                // 16-bit PCM 量化误差上界：截断 + 读取侧取整 ≤ 2 LSB
                Assert.True(maxDiff <= 2.0 / 32768 + 1e-6, $"导出与 32-bit 混音差超过 2 个 16-bit LSB：{maxDiff}");
                Assert.InRange(AudioMeasure.Peak(exported), AudioMeasure.Peak(mixdown) * 0.99, AudioMeasure.Peak(mixdown) * 1.01);
            } finally {
                foreach (var f in new[] { wavA, wavB }) {
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                }
            }
        }

        static float[] ReadWav(string path) {
            using var stream = Wave.OpenFile(path);
            return Wave.GetStereoSamples(stream);
        }
    }
}
