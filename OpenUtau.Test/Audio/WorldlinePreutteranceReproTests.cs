using System;
using System.IO;
using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W14：上游 `ed8e5369`「Fix Worldline crash on lengthened preutterance」在**我们树上**的
    /// 等价复现尝试（上游修的是 C# 移植版 `SynthSegment` 的帧映射，我们是原生 P/Invoke 路径，
    /// 按 Lead 裁决不动 dll ⇒ 只能做调用侧防护 + 复现用例）。
    ///
    /// 触发条件：oto preutter 被"加长"到超过音符前导 ⇒
    /// <c>skipOver = oto.Preutter × stretch − leadingMs &lt; 0</c>（用户侧就是给音素加
    /// preutterance 覆盖）。本文件同时首次覆盖 WORLDLINE-R 渲染器的端到端行为。
    /// </summary>
    [Collection("AudioFixture")]
    public class WorldlinePreutteranceReproTests {
        readonly ITestOutputHelper output;
        static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "w14-evidence");

        public WorldlinePreutteranceReproTests(ITestOutputHelper output) {
            this.output = output;
        }

        static int Samples(double seconds) =>
            (int)Math.Round(seconds * AudioFixtures.Rate) * AudioFixtures.Channels;

        static string Voicebank() {
            Directory.CreateDirectory(TempRoot);
            string dir = Path.Combine(TempRoot, DummyVoicebank.FolderName);
            if (!File.Exists(Path.Combine(dir, "character.txt"))) {
                dir = DummyVoicebank.Create(TempRoot);
            }
            return dir;
        }

        /// <summary>WORLDLINE-R 端到端对照（无覆盖）：验证我们树上该渲染器可离线跑通。</summary>
        [Fact]
        public void WorldlineR_EndToEnd_RendersExpectedPitch() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用：{reason}");
            }
            var singer = DummyVoicebank.Load(Voicebank());
            var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480), ("i", 62, 480));
            var track = project.tracks[0];
            track.RendererSettings.renderer = Renderers.WORLDLINE_R;
            track.RendererSettings.Validate(track);
            output.WriteLine($"renderer={track.RendererSettings.renderer} resampler={track.RendererSettings.resampler ?? "(null)"}");

            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);
            var rendered = AudioFixtures.RenderMixdown(project, Samples(1.4));

            var note2 = AudioMeasure.SliceMs(rendered, AudioFixtures.Channels, AudioFixtures.Rate, 650, 920);
            double f2 = AudioMeasure.DominantFreq(note2, AudioFixtures.Channels, AudioFixtures.Rate, 100, 2000);
            double expected = MusicMath.ToneToFreq(62);
            output.WriteLine($"WORLDLINE-R tone 62：期望 {expected:F2} Hz，实测 {f2:F2} Hz，RMS={AudioMeasure.RmsDb(note2):F2} dBFS，" +
                             $"整段峰值={AudioMeasure.Peak(rendered):F4}");
            Assert.InRange(f2, expected * 0.95, expected * 1.05);
            Assert.True(AudioMeasure.RmsDb(note2) > -40);
        }

        /// <summary>
        /// 复现：给第 1 个音素加 preutterance 覆盖 +100 ms ⇒ skipOver 为负。
        /// 断言"不崩、输出有限"；并把实际 skipOver / 渲染结果打进报告。
        /// </summary>
        [Fact]
        public void LengthenedPreutterance_NegativeSkip_DoesNotCrash() {
            if (!NativeWorldline.EnsureLoaded(out string reason)) {
                Assert.Skip($"原生 worldline 不可用：{reason}");
            }
            var singer = DummyVoicebank.Load(Voicebank());
            var (project, part) = ClassicRenderSetup.BuildProject(singer, ("a", 60, 480), ("i", 62, 480));
            var track = project.tracks[0];
            track.RendererSettings.renderer = Renderers.WORLDLINE_R;
            track.RendererSettings.Validate(track);
            // oto preutter=50ms ⇒ 覆盖 +100ms 后 preutter=150ms，skipOver = 50 − 150 = −100ms
            part.notes.First().phonemeOverrides.Add(new UPhonemeOverride {
                index = 0,
                preutterDelta = 100f,
            });

            using var session = ClassicRenderSetup.StartPhonemizer(project);
            ClassicRenderSetup.PhonemizeAndWait(project, part, output.WriteLine);

            var phrase = part.renderPhrases.First();
            var item = new ResamplerItem(phrase, phrase.phones.First());
            output.WriteLine($"preutter={item.preutter:F2} ms, leadingMs={phrase.phones.First().leadingMs:F2} ms, " +
                             $"skipOver={item.skipOver:F2} ms, durRequired={item.durRequired:F2} ms, offset={item.offset:F2} ms");

            Exception? thrown = null;
            float[] rendered = Array.Empty<float>();
            try {
                rendered = AudioFixtures.RenderMixdown(project, Samples(1.4));
            } catch (Exception e) {
                thrown = e;
            }

            int nonFinite = rendered.Count(v => !float.IsFinite(v));
            output.WriteLine($"渲染：异常={(thrown == null ? "无" : thrown.GetType().Name + ": " + thrown.Message)}；" +
                             $"样本={rendered.Length}，非有限={nonFinite}，峰值={AudioMeasure.Peak(rendered):F4}，" +
                             $"RMS={AudioMeasure.RmsDb(rendered):F2} dBFS");
            Assert.Null(thrown);            // 若抛异常/崩溃 ⇒ 就是 Lead 要的"最小复现 + 影响面"
            Assert.Equal(0, nonFinite);
        }
    }
}
