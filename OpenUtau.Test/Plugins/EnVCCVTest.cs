using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class EnVCCVTest : PhonemizerTestBase {
        public EnVCCVTest(ITestOutputHelper output) : base(output) { }
        protected override Phonemizer CreatePhonemizer() {
            return new EnglishVCCVPhonemizer();
        }

        [Theory]
        [InlineData("en_vccv",
            new string[] { "test", "words" },
            new string[] { "-te", "es-", "st", "w3", "3d-", "dz-" })]
        public void BasicPhonemizingTest(string singerName, string[] lyrics, string[] aliases) {
            SameAltsTonesColorsTest(singerName, lyrics, aliases, "", "C4", "");
        }

        [Fact]
        public void ToneShiftTest() {
            RunPhonemizeTest("en_vccv", new NoteParams[] {
                new NoteParams {
                    lyric = "hi",
                    hint = "",
                    tone = "C4",
                    phonemes = new PhonemeParams[] {
                        new PhonemeParams {
                            alt = 0,
                            shift = 0,
                            color = "",
                        },
                        new PhonemeParams {
                            alt = 0,
                            shift = 12,
                            color = "",
                        },
                    }
                }
            }, new string[] { "-hI", "I-_H" });
        }

        [Theory]
        [InlineData("read", "", new string[] { "-re", "ed-" })]
        [InlineData("read", "r E d", new string[] { "-rE", "Ed-" })]

        [InlineData("asdfjkl", "r E d", new string[] { "-rE", "Ed-" })]
        [InlineData("", "r E d", new string[] { "-rE", "Ed-" })]
        public void HintTest(string lyric, string hint, string[] aliases) {
            RunPhonemizeTest("en_vccv", new NoteParams[] { new NoteParams { lyric = lyric, hint = hint, tone = "C4", phonemes = SamePhonemeParams(4, 0, 0, "") } }, aliases);
        }

        // ── Automatic ConVel（上游 d397f6c5 适配版，W29）─────────────────────────────
        // 评估结论：该提交的**代码**意图（`useConvel` 开关、`CalcConvel`、
        // `unotes`/`utrack` 上下文、yaml 的 `useconvel` 读取）已随上游 `89acfe84`
        // （SBP Update，我们已在 P2-1 ② 取入）进入我们树；本次补齐它**尚未落地**的两块：
        //   ① 数据键 `useconvel: true`（`Data/envccv.template.yaml`）；
        //   ② 两条断言（CV onset 用本音符 vel、VC- coda 继承前音 vel、vel 随时长缩放）。
        // 两处 API 漂移（`ValidateAlias(string)` vs `(string,int)`、`Replacement.Key` 已删）
        // 对我们**不构成适配负担**：我们文件里已是 89acfe84 之后的新签名与新替换表结构，
        // 不再引入旧签名，也**未改基类**。
        // 注意 `Testing = true` 用的是 Plus 的测试钩子（`Phonemizer.Testing`，P2-1 ① 落地），
        // 它让 ConVel 路径走确定性分支。

        /// <summary>用**真实 UVoicePart** 跑音素化（这样 `unotes` 才有内容、Automatic ConVel 生效），
        /// 返回按音素顺序的 vel 表达式值；缺失记 -1 哨兵。</summary>
        float[] RunConvelTest(string singerName, string[] lyrics, int[] durations) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var file = Path.Join(dir, "Files", singerName, "character.txt");

            // VoicebankLoader.IsTest 是进程级开关：记录原值并在结束时还原（测试卫生）。
            bool savedIsTest = VoicebankLoader.IsTest;
            VoicebankLoader.IsTest = true;
            try {
                var voicebank = new Voicebank() { File = file, BasePath = dir };
                VoicebankLoader.LoadVoicebank(voicebank);
                var singer = new ClassicSinger(voicebank);
                singer.EnsureLoaded();

                var project = new UProject();
                Ustx.AddDefaultExpressions(project);
                var track = project.tracks[0];
                project.expressions.TryGetValue(Ustx.CLR, out var colorDescriptor);
                track.VoiceColorExp = colorDescriptor.Clone();
                var voiceColors = singer.Subbanks.Select(subbank => subbank.Color).ToHashSet();
                track.VoiceColorExp.options = voiceColors.OrderBy(c => c).ToArray();
                track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;

                var part = new UVoicePart { trackNo = 0 };
                int pos = 240;
                foreach (var dur in durations) {
                    var note = UNote.Create();   // 必须走工厂：pitch/vibrato/pitch 点都在里面初始化
                    note.position = pos;
                    note.duration = dur;
                    part.notes.Add(note);
                    pos += dur;
                }
                project.parts.Add(part);

                var timeAxis = new TimeAxis();
                timeAxis.BuildSegments(project);

                var groups = lyrics.Select((lyric, i) => new Phonemizer.Note[] {
                    new Phonemizer.Note {
                        lyric = lyric, duration = durations[i], position = 240 + durations.Take(i).Sum(),
                        tone = MusicMath.NameToTone("C4"),
                        phonemeAttributes = new[] {
                            new Phonemizer.PhonemeAttributes { index = 0, consonantStretchRatio = 1 }
                        },
                    }
                }).ToList();

                var phonemizer = new EnglishVCCVPhonemizer {
                    Testing = true,   // Plus 钩子：ConVel 走确定性分支
                };
                phonemizer.SetSinger(singer);
                phonemizer.SetTiming(timeAxis);
                phonemizer.SetUp(groups.ToArray(), project, track);

                var results = groups.Select((g, i) => phonemizer.Process(
                    g,
                    i > 0 ? groups[i - 1][0] : null,
                    i < groups.Count - 1 ? groups[i + 1][0] : null,
                    i > 0 ? groups[i - 1][0] : null,
                    i < groups.Count - 1 ? groups[i + 1][0] : null,
                    i > 0 ? groups[i - 1] : null)).ToList();

                return results
                    .SelectMany(r => r.phonemes)
                    .Select(p => p.expressions?.FirstOrDefault(e => e.abbr == "vel").value ?? -1f)
                    .ToArray();
            } finally {
                VoicebankLoader.IsTest = savedIsTest;
            }
        }

        // CV onset 音素用**本音符**的 vel；coda（VC-）用前一个音符的 vel：
        // "sea"（dur=240 → vel=150）后接 "bird"（dur=960 → vel=50）
        //   -sE → CV,  vel = 150（sea 自己的）
        //   Ed- → VC-, vel = 150（bird 的 coda 继承 prevVel）
        //   -b3 → CV,  vel =  50（bird 自己的）
        [Fact]
        public void ConvelCodaInheritsPrevNoteVel() {
            var vels = RunConvelTest("en_vccv", new[] { "sea", "bird" }, new[] { 240, 960 });

            Assert.All(vels, v => Assert.True(v >= 0, $"Missing vel expression (got {v})"));
            Assert.Equal(150f, vels[0], precision: 1);
            Assert.Equal(150f, vels[1], precision: 1);
            Assert.Equal(50f, vels[2], precision: 1);
        }

        // 无前音时 vel = 本音符 vel，且随时长缩放：240 → 150、480 → 100、960 → 50
        [Theory]
        [InlineData(240, 150f)]
        [InlineData(480, 100f)]
        [InlineData(960, 50f)]
        public void ConvelNoteVelScalesWithDuration(int duration, float expectedVel) {
            var vels = RunConvelTest("en_vccv", new[] { "a" }, new[] { duration });
            Assert.All(vels, v => Assert.Equal(expectedVel, v, precision: 1));
        }
    }
}
