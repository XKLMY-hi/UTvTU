using System;
using System.IO;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7-1/W7-2：伪声库 + 经典渲染端到端。
    ///
    /// 前置结论（已在 <see cref="ClassicRenderCapabilityTests"/> 实测）：原生 worldline 库
    /// 随仓库提交、本机可加载，故经典渲染**能在离线沙箱内无外部依赖跑通**。
    /// 本类用生成的伪声库（已知频率正弦）+ CLASSIC 渲染器渲染真实工程，
    /// 再用频谱测量验证"输出的确是这些音素、这些音高"，并验证逐样本确定性。
    ///
    /// 需要原生库：拿不到时 <c>Assert.Skip</c>（不假装跑通）。
    /// </summary>
    [Collection("AudioFixture")]
    public class DummyVoicebankClassicTests {
        readonly ITestOutputHelper output;
        static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "w7-audio-fixture");

        public DummyVoicebankClassicTests(ITestOutputHelper output) {
            this.output = output;
        }

        static string EnsureVoicebank() {
            Directory.CreateDirectory(TempRoot);
            string dir = Path.Combine(TempRoot, DummyVoicebank.FolderName);
            if (!File.Exists(Path.Combine(dir, "character.txt"))) {
                dir = DummyVoicebank.Create(TempRoot);
            }
            return dir;
        }

        static int Samples(double seconds) =>
            (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        [Fact]
        public void DummyVoicebank_IsWellFormed_AndLoads() {
            string dir = EnsureVoicebank();
            output.WriteLine($"voicebank dir = {dir}");
            foreach (var alias in DummyVoicebank.Aliases.Keys) {
                Assert.True(File.Exists(Path.Combine(dir, $"{alias}.wav")), $"缺少 {alias}.wav");
            }
            Assert.True(File.Exists(Path.Combine(dir, "oto.ini")));
            Assert.True(File.Exists(Path.Combine(dir, "character.txt")));

            var singer = DummyVoicebank.Load(dir);
            output.WriteLine($"singer Id={singer.Id} Name={singer.Name} Type={singer.SingerType} " +
                             $"Otos={singer.Otos.Count} Subbanks={singer.Subbanks.Count} Found={singer.Found} Loaded={singer.Loaded}");
            Assert.True(singer.Found);
            Assert.True(singer.Loaded);
            Assert.Equal(USingerType.Classic, singer.SingerType);
            Assert.Equal(DummyVoicebank.Aliases.Count, singer.Otos.Count);
            // 声库可被"音素 → oto"映射命中（DefaultPhonemizer 依赖它）
            Assert.True(singer.TryGetMappedOto("a", 60, "", out var oto));
            output.WriteLine($"oto[a]: wav={oto.File} offset={oto.Offset} preutter={oto.Preutter} overlap={oto.Overlap} cutoff={oto.Cutoff}");
        }

        [Fact]
        public void ClassicRender_EndToEnd_ProducesExpectedPitchAndLevel() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用，经典端到端跳过：{reason}");
            }
            string dir = EnsureVoicebank();
            var singer = DummyVoicebank.Load(dir);
            // 两个音素、两个音高：C4 (60) → 261.63 Hz；D4 (62) → 293.66 Hz
            var (project, part) = ClassicRenderSetup.BuildProject(singer,
                ("a", 60, 480),
                ("i", 62, 480));
            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);
            output.WriteLine($"phonemes={part.phonemes.Count}, phrases={part.renderPhrases.Count}, " +
                             $"lines=[{string.Join(",", part.phonemes.ConvertAll(p => p.phoneme))}]");

            var rendered = AudioFixtures.RenderMixdown(project, Samples(1.4));

            double expected1 = MusicMath.ToneToFreq(60);
            double expected2 = MusicMath.ToneToFreq(62);
            var note1 = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 150, 420);
            var note2 = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 650, 920);
            double f1 = AudioMeasure.DominantFreq(note1, AudioFixtures.Channels, AudioFixtures.Rate, 100, 2000);
            double f2 = AudioMeasure.DominantFreq(note2, AudioFixtures.Channels, AudioFixtures.Rate, 100, 2000);
            output.WriteLine($"note1 (tone 60, 期望 {expected1:F2} Hz): 实测 {f1:F2} Hz, RMS={AudioMeasure.RmsDb(note1):F2} dBFS");
            output.WriteLine($"note2 (tone 62, 期望 {expected2:F2} Hz): 实测 {f2:F2} Hz, RMS={AudioMeasure.RmsDb(note2):F2} dBFS");
            output.WriteLine($"整段峰值={AudioMeasure.Peak(rendered):F4}, 尾部(1.2-1.4s) RMS={AudioMeasure.RmsDb(AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 1200, 1400)):F2} dBFS");

            Assert.InRange(f1, expected1 * 0.97, expected1 * 1.03);
            Assert.InRange(f2, expected2 * 0.97, expected2 * 1.03);
            Assert.True(AudioMeasure.RmsDb(note1) > -40, "第一个音素没有出声");
            Assert.True(AudioMeasure.RmsDb(note2) > -40, "第二个音素没有出声");

            // 独立复核：用 Goertzel 在期望频点与邻点比较（期望频点应显著高于半音邻点）
            double atPitch = AudioMeasure.GoertzelPowerDb(AudioMeasure.Mono(note2, AudioFixtures.Channels), expected2, AudioFixtures.Rate);
            double offPitch = AudioMeasure.GoertzelPowerDb(AudioMeasure.Mono(note2, AudioFixtures.Channels), expected2 * 1.2, AudioFixtures.Rate);
            output.WriteLine($"Goertzel @{expected2:F1}Hz={atPitch:F2} dB vs @{expected2 * 1.2:F1}Hz={offPitch:F2} dB");
            Assert.True(atPitch > offPitch + 10, "期望音高处未见显著峰值");
        }

        [Fact]
        public void ClassicRender_IsSampleExactDeterministic() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用，经典端到端跳过：{reason}");
            }
            string dir = EnsureVoicebank();
            double[] f1 = new double[2], f2 = new double[2];
            float[] first = null, second = null;
            for (int i = 0; i < 2; i++) {
                var singer = DummyVoicebank.Load(dir);
                var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480), ("u", 65, 480));
                using var session = ClassicRenderSetup.StartPhonemizer(project);
                ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);
                var rendered = AudioFixtures.RenderMixdown(project, Samples(1.0));
                if (i == 0) {
                    first = rendered;
                } else {
                    second = rendered;
                }
                var seg = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 700, 950);
                (i == 0 ? f1 : f2)[0] = AudioMeasure.DominantFreq(seg, AudioFixtures.Channels, AudioFixtures.Rate, 100, 2500);
            }
            int diff = AudioMeasure.FirstDifference(first!, second!);
            output.WriteLine($"两次独立渲染（重新建工程/重新音素化）：首个逐样本差索引={diff}；" +
                             $"第二次被测音高={f2[0]:F2} Hz（第一次 {f1[0]:F2} Hz）");
            Assert.Equal(-1, diff);
            Assert.InRange(f2[0], f1[0] * 0.99, f1[0] * 1.01);
        }
    }
}
