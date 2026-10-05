using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Core.Format {
    /// <summary>
    /// W7-2：纯文本 <c>.ustxp</c> 测试工程 fixture 的加载验证（含版本迁移分支）。
    ///
    /// fixture = <c>OpenUtau.Test/Fixtures/w7_rack.ustxp</c>（可提交的文本，由
    /// <c>Ustxp.Save</c> 生成后固化）：2 轨 + 伪声库绑定 + 完整 MixFx 机架 +
    /// 整机架/模块级旁通 + 2 个 VST 槽位 + 3 音符声部器件。
    /// 本类不渲染（渲染在 <c>OpenUtau.Test/Audio/DummyVoicebankClassicTests</c>），
    /// 只证明"这份 fixture 能被产品加载器正确读入，且迁移分支生效"。
    /// </summary>
    [Collection("AudioFixture")]
    public class UstxpFixtureTests {
        readonly ITestOutputHelper output;

        public UstxpFixtureTests(ITestOutputHelper output) {
            this.output = output;
        }

        static string FixturePath => Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Fixtures", "w7_rack.ustxp");

        static string ReadFixture() => File.ReadAllText(FixturePath);

        /// <summary>生成并注册伪声库（幂等）；返回声库 Id。
        /// **必须**在 <see cref="ScopedSingers"/> 作用域内调用——它写的是进程级全局表。</summary>
        static string RegisterDummySinger() {
            string root = Path.Combine(Path.GetTempPath(), "w7-audio-fixture");
            Directory.CreateDirectory(root);
            string dir = Path.Combine(root, DummyVoicebank.FolderName);
            if (!File.Exists(Path.Combine(dir, "character.txt"))) {
                DummyVoicebank.Create(root);
            }
            var singer = DummyVoicebank.Load(dir);
            SingerManager.Inst.Singers[singer.Id] = singer;
            return singer.Id;
        }

        // ── 进程级全局态纪律 ────────────────────────────────────────────
        // `SingerManager.Inst.Singers` 是**进程级**字典：本类此前直接对它增删（`saved` 取到的
        // 还是**同一个实例**，不是副本），于是断言依赖"此刻全局里有没有 W7DUMMYCV"——与并行
        // collection 的用例互相踩（W23/W26 观察到的 `Fixture_WithoutVoicebank_ClearsRenderSettings`
        // 偶发红正是此因）。改为：用例期间**换上一份副本**，用后还原原实例，本类对全局零副作用。

        static readonly PropertyInfo SingersProperty = typeof(SingerManager)
            .GetProperty("Singers", BindingFlags.Public | BindingFlags.Instance)!;
        static readonly MethodInfo SingersSetter = SingersProperty.GetSetMethod(nonPublic: true)!;

        sealed class ScopedSingers : IDisposable {
            readonly Dictionary<string, USinger> original;

            public ScopedSingers() {
                original = SingerManager.Inst.Singers;
                SingersSetter.Invoke(SingerManager.Inst,
                    new object[] { new Dictionary<string, USinger>(original) });
            }

            public void Dispose() {
                SingersSetter.Invoke(SingerManager.Inst, new object[] { original });
            }
        }

        [Fact]
        public void Fixture_Exists_AndIsPlainText() {
            Assert.True(File.Exists(FixturePath), $"fixture 未复制到输出目录：{FixturePath}");
            string text = ReadFixture();
            output.WriteLine($"fixture={FixturePath} bytes={text.Length} lines={text.Count(c => c == '\n') + 1}");
            Assert.Contains("ustxp_version", text);
            Assert.Contains("mix_fx", text);
            Assert.Contains("vst_slots", text);
            // 纯文本：不得含二进制/非 UTF8 替换字符
            Assert.DoesNotContain('\uFFFD', text);
        }

        [Fact]
        public void Fixture_LoadsWithProductLoader_AllSectionsIntact() {
            using var singers = new ScopedSingers();   // 全局表换副本，用后还原
            RegisterDummySinger();   // 声库必须在加载前就位，否则渲染器设置会被 Validate 清空
            var project = Ustxp.Load(FixturePath);
            output.WriteLine($"name={project.name} tracks={project.tracks.Count} parts={project.parts.Count} " +
                             $"ustxVersion={project.ustxVersion} ustxpVersion={project.ustxpVersion}");

            Assert.Equal("W7 Rack Fixture", project.name);
            Assert.Equal(new Version(1, 0), project.ustxpVersion);
            Assert.Equal(Ustx.kUstxVersion, project.ustxVersion);   // 加载后升级到当前基线
            Assert.Equal(2, project.tracks.Count);

            var lead = project.tracks[0];
            Assert.Equal("Lead", lead.TrackName);
            Assert.Equal("W7DUMMYCV", lead.singer);
            Assert.Equal(Renderers.CLASSIC, lead.RendererSettings.renderer);
            Assert.Equal("worldline", lead.RendererSettings.resampler);
            Assert.Equal("convergence", lead.RendererSettings.wavtool);
            Assert.Equal(-3, lead.Volume);
            Assert.Equal(10, lead.Pan);
            Assert.Equal(typeof(DefaultPhonemizer), lead.Phonemizer.GetType());
            Assert.False(lead.Mute);

            // MixFx：整机架启用 + 三模块开关 + 全部参数 + 预设名
            Assert.NotNull(lead.MixFx);
            Assert.True(lead.MixFx.Enabled);
            Assert.True(lead.MixFx.EqEnabled);
            Assert.True(lead.MixFx.CompEnabled);
            Assert.True(lead.MixFx.ReverbEnabled);
            Assert.Equal(6.0, lead.MixFx.EqHighDb);
            Assert.Equal(3000.0, lead.MixFx.EqMidFreq);
            Assert.Equal(-18.0, lead.MixFx.CompThresholdDb);
            Assert.Equal(2.0, lead.MixFx.CompRatio);
            Assert.Equal("small_room", lead.MixFx.ReverbPreset);
            Assert.Equal(1.0, lead.MixFx.ReverbWet);
            output.WriteLine($"Lead: volume={lead.Volume} pan={lead.Pan} eqHigh={lead.MixFx.EqHighDb} " +
                             $"compRatio={lead.MixFx.CompRatio} reverb={lead.MixFx.ReverbPreset}");

            var harmony = project.tracks[1];
            Assert.Equal("Harmony", harmony.TrackName);
            Assert.True(harmony.Mute);
            Assert.Equal(-6, harmony.Volume);
            Assert.Equal(-25, harmony.Pan);
            Assert.False(harmony.MixFx.Enabled);        // 整机架旁通
            Assert.False(harmony.MixFx.EqEnabled);      // 模块级旁通（跨整机架开关保持）
            Assert.True(harmony.MixFx.CompEnabled);
            Assert.False(harmony.MixFx.ReverbEnabled);
            // 旧反向键与规范键恒互反（fixture 里两套键都写了）
            Assert.True(harmony.MixFx.EqBypassed);
            Assert.False(harmony.MixFx.CompBypassed);
            Assert.True(harmony.MixFx.ReverbBypassed);
            output.WriteLine($"Harmony: enabled={harmony.MixFx.Enabled} eq={harmony.MixFx.EqEnabled} " +
                             $"comp={harmony.MixFx.CompEnabled} reverb={harmony.MixFx.ReverbEnabled}");

            // VST 槽位（含一个空槽）
            Assert.Equal(2, harmony.VstSlots.Count);
            Assert.Equal("test:w7-fixture-slot", harmony.VstSlots[0].PluginUid);
            Assert.Equal(0, harmony.VstSlots[0].SlotIndex);
            Assert.False(harmony.VstSlots[0].Bypassed);
            Assert.Equal(string.Empty, harmony.VstSlots[1].PluginUid);
            Assert.Equal(1, harmony.VstSlots[1].SlotIndex);

            // 声部器件与音符
            var part = Assert.IsType<UVoicePart>(Assert.Single(project.parts));
            Assert.Equal(0, part.trackNo);
            // fixture 里写的是 1440 tick；AfterLoad 会把器件时长撑到小节/拍边界 → 1920（产品行为）
            Assert.Equal(1920, part.duration);
            Assert.Equal(3, part.notes.Count);
            var notes = part.notes.ToArray();
            Assert.Equal(new[] { 60, 62, 64 }, notes.Select(n => n.tone).ToArray());
            Assert.Equal(new[] { "a", "i", "u" }, notes.Select(n => n.lyric).ToArray());
            Assert.Equal(new[] { 0, 480, 960 }, notes.Select(n => n.position).ToArray());
            Assert.All(notes, n => Assert.Equal(480, n.duration));
            // 音高曲线点已随 fixture 持久化（渲染依赖它）
            Assert.All(notes, n => Assert.Equal(2, n.pitch.data.Count));
            output.WriteLine($"part: notes={part.notes.Count} tones=[{string.Join(",", notes.Select(n => n.tone))}] " +
                             $"lyrics=[{string.Join(",", notes.Select(n => n.lyric))}]");

            // 加载后尚未音素化（渲染前流程由音频用例覆盖）
            Assert.False(part.PhonemesUpToDate);
        }

        /// <summary>fixture 的 singer id 能绑定到运行时生成的伪声库（W7-1 ↔ W7-2 联结点）。</summary>
        [Fact]
        public void Fixture_SingerId_BindsToGeneratedDummyVoicebank() {
            using var singers = new ScopedSingers();   // 全局表换副本，用后还原
            string id = RegisterDummySinger();
            var singer = SingerManager.Inst.Singers[id];
            output.WriteLine($"singer Id={singer.Id} Name={singer.Name}");
            Assert.Equal("W7DUMMYCV", singer.Id);

            var project = Ustxp.Load(FixturePath);
            var track = project.tracks[0];
            output.WriteLine($"track.singer={track.singer} → resolved Id={track.Singer?.Id} Found={track.Singer?.Found} " +
                             $"Type={track.Singer?.SingerType}");
            Assert.Equal("W7DUMMYCV", track.Singer?.Id);
            Assert.True(track.Singer!.Found);
            Assert.Equal(USingerType.Classic, track.Singer.SingerType);
            Assert.NotNull(track.RendererSettings.Renderer);   // 加载后重装配渲染器
            Assert.IsType<ClassicRenderer>(track.RendererSettings.Renderer);
        }

        /// <summary>
        /// 声库缺失时的产品行为（记录在案）：<c>AfterLoad</c> 用 <c>CreateMissing</c> 兜底，
        /// 随后 <c>UTrack.Validate</c> 把渲染器设置清空——fixture 必须连同伪声库一起使用。
        /// </summary>
        [Fact]
        public void Fixture_WithoutVoicebank_ClearsRenderSettings() {
            // 全程只动**副本**：不再对进程级字典原地 Remove/写回（旧写法里 `saved` 取到的就是
            // 同一个实例，断言依赖全局瞬时内容 ⇒ 与并行 collection 互相踩，W26 观察到的偶发红）。
            using var singers = new ScopedSingers();
            SingerManager.Inst.Singers.Remove("W7DUMMYCV");   // 副本内显式确保"无声库"
            {
                var project = Ustxp.Load(FixturePath);
                var track = project.tracks[0];
                output.WriteLine($"无声库：Singer.Found={track.Singer?.Found} Name={track.Singer?.Name} " +
                                 $"renderer={track.RendererSettings.renderer ?? "(null)"} " +
                                 $"resampler={track.RendererSettings.resampler ?? "(null)"}");
                Assert.NotNull(track.Singer);
                Assert.False(track.Singer!.Found);
                Assert.Null(track.RendererSettings.renderer);
                Assert.Null(track.RendererSettings.Renderer);
                // 恢复路径 = **声库就位后重新加载工程**（app 里对应"装好声库再打开工程"）：
                // 注意：仅把声库塞回 SingerManager 再 ValidateFull 是不够的——轨道上的
                // Singer 仍是 CreateMissing 占位对象（Found=false），且缺声库那次 Validate
                // 已把 renderer/resampler/wavtool 字段本身置空，CLSC 之类的显式选择
                // 无法原地恢复（既有行为，记为观察项）。
                // 补装声库（只写副本内）：等价于把原来"还原全局"的那一步内联进来。
                RegisterDummySinger();
                project.ValidateFull();
                output.WriteLine($"原地 Validate 后（仅补装声库）：renderer={project.tracks[0].RendererSettings.renderer ?? "(null)"} " +
                                 $"Singer.Found={project.tracks[0].Singer?.Found}");

                var reloaded = Ustxp.Load(FixturePath);
                output.WriteLine($"重新加载后：renderer={reloaded.tracks[0].RendererSettings.renderer} " +
                                 $"Singer.Found={reloaded.tracks[0].Singer?.Found} Found渲染器={reloaded.tracks[0].RendererSettings.Renderer?.GetType().Name}");
                Assert.Equal(Renderers.CLASSIC, reloaded.tracks[0].RendererSettings.renderer);
                Assert.IsType<ClassicRenderer>(reloaded.tracks[0].RendererSettings.Renderer);
            }
        }

        /// <summary>版本迁移分支：ustx 0.3 的 <c>acc</c> 表达式 → <c>atk</c>（MigrateToV04）。</summary>
        [Fact]
        public void LegacyUstxVersion_RunsMigrationBranch() {
            string legacy = ReadFixture()
                .Replace("ustx_version: \"0.9\"", "ustx_version: \"0.3\"")
                .Replace("  atk:\r\n    name: attack", "  acc:\r\n    name: accent");
            Assert.Contains("ustx_version: \"0.3\"", legacy);
            Assert.Contains("name: accent", legacy);

            string path = Path.Combine(Path.GetTempPath(), $"w7-legacy-{Guid.NewGuid():N}.ustx");
            File.WriteAllText(path, legacy);
            try {
                var project = Ustxp.Load(path);
                output.WriteLine($"migrated: ustxVersion={project.ustxVersion} expressions has atk={project.expressions.ContainsKey(Ustx.ATK)} " +
                                 $"has acc={project.expressions.ContainsKey("acc")}");
                Assert.Equal(Ustx.kUstxVersion, project.ustxVersion);   // 版本升级到当前基线
                Assert.True(project.expressions.ContainsKey(Ustx.ATK), "MigrateToV04 未把 acc 迁移成 atk");
                Assert.False(project.expressions.ContainsKey("acc"));
                // 迁移后仍能完成校验（不抛）
                project.ValidateFull();
            } finally {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
