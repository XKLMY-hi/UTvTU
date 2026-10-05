using System;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;


namespace OpenUtau.Test.Format {
    /// <summary>
    /// W27（task-37）`.ustxp` 格式止血的实测用例（文件驱动）。
    /// fixture `Files/Format/upstream-v0.10.ustxp` = W8 审计里"用上游键集重存"出来的真实文件
    /// （2,509 B、`ustx_version: "0.10"`）—— 修复前我们直接抛"工程比软件新"打不开。
    ///
    /// 五件套：未知键往返 / 0.10 版本墙（+自动 .bak）/ 纯净导出 / 噪音清理（行数字节数）/ 迁移逐级。
    /// </summary>
    public class UstxpFormatTests : IDisposable {
        private readonly string dir;
        private readonly ITestOutputHelper output;

        public UstxpFormatTests(ITestOutputHelper output) {
            this.output = output;
            dir = Path.Combine(Path.GetTempPath(), "ustxp-w27-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
        }

        public void Dispose() {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        private static string Fixture =>
            Path.Combine(AppContext.BaseDirectory, "Files", "Format", "upstream-v0.10.ustxp");

        private string CopyFixture(string name, params (string from, string to)[] edits) {
            string text = File.ReadAllText(Fixture);
            foreach (var (from, to) in edits) {
                text = text.Replace(from, to);
            }
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, text);
            return path;
        }

        // ── 2. 版本墙 ───────────────────────────────────────────────────────

        [Fact]
        public void UpstreamV010File_OpensWithBackup_InsteadOfThrowing() {
            string file = CopyFixture("v011.ustxp", ("ustx_version: \"0.10\"", "ustx_version: \"0.11\""));
            string original = File.ReadAllText(file);

            var project = Ustxp.Load(file);          // 修复前：抛 MessageCustomizableException（0.11 > 我们支持的 0.10）

            Assert.Equal("Probe", project.name);
            Assert.NotEmpty(project.tracks);
            Assert.True(File.Exists(file + ".bak"), "应留下原始文件备份");
            Assert.Equal(original, File.ReadAllText(file + ".bak"));
            Assert.Equal(Ustx.kUstxVersion, project.ustxVersion);
            output.WriteLine($"version wall: 0.10 file opened, .bak={new FileInfo(file + ".bak").Length} B");
        }

        [Fact]
        public void NewerVersion_AlsoOpens_BestEffort() {
            string file = CopyFixture("v020.ustxp", ("ustx_version: \"0.10\"", "ustx_version: \"0.20\""));
            var project = Ustxp.Load(file);
            Assert.Equal("Probe", project.name);
            Assert.True(File.Exists(file + ".bak"));
        }

        [Fact]
        public void UnparsableFile_StillThrows_WithActionableMessage() {
            string file = Path.Combine(dir, "broken.ustxp");
            File.WriteAllText(file, "ustx_version: [not: a mapping\n  {");
            var ex = Assert.Throws<MessageCustomizableException>(() => Ustxp.Load(file));
            Assert.Contains("Failed to parse", ex.Message);
        }

        // ── 1. 未知键往返 ───────────────────────────────────────────────────

        [Fact]
        public void UnknownKeys_SurviveLoadAndSave_AtAllFourLevels() {
            string file = CopyFixture("unknown.ustxp",
                ("ustx_version: \"0.10\"",
                 "ustx_version: \"0.10\"\nfuture_project_key:\n  nested: 7\nexpression_graphs:\n  g1: [1, 2, 3]"),
                ("tracks:", "future_track_key: hello\ntracks:"),
                ("voice_parts:", "future_part_key: 3.5\nvoice_parts:"));
            string[] lines = File.ReadAllLines(file);
            int notesAt = Array.FindIndex(lines, l => l.TrimStart().StartsWith("notes:"));
            if (notesAt >= 0) {
                for (int i = notesAt + 1; i < lines.Length; i++) {
                    if (lines[i].TrimStart().StartsWith("position:")) {
                        string indent = lines[i].Substring(0, lines[i].Length - lines[i].TrimStart().Length);
                        lines[i] = lines[i].TrimEnd() + "\n" + indent + "future_note_key: {a: 1, b: two}";
                        break;
                    }
                }
            }
            File.WriteAllLines(file, lines);

            var loaded = Ustxp.Load(file);
            Assert.Equal("Probe", loaded.name);
            Assert.NotEmpty(loaded.tracks);
            Assert.NotEmpty(loaded.parts);

            string saved = Ustxp.SerializeForSave(loaded);
            foreach (string key in new[] {
                "future_project_key", "expression_graphs", "future_track_key", "future_part_key", "future_note_key" }) {
                Assert.Contains(key, saved);
            }
            Assert.Contains("nested: 7", saved);
            Assert.Contains("future_track_key: hello", saved);
            Assert.Contains("future_part_key: 3.5", saved);

            string file2 = Path.Combine(dir, "unknown2.ustxp");
            File.WriteAllText(file2, saved);
            string saved2 = Ustxp.SerializeForSave(Ustxp.Load(file2));
            foreach (string key in new[] { "future_project_key", "future_track_key", "future_part_key", "future_note_key" }) {
                Assert.Contains(key, saved2);
            }
        }

        [Fact]
        public void UnknownKey_IsNotMisreadAsKnownField() {
            string file = CopyFixture("tricky.ustxp",
                ("ustx_version: \"0.10\"",
                 "ustx_version: \"0.10\"\nfuture_key:\n  position: 999999\n  bpm: 999"));
            var project = Ustxp.Load(file);
            Assert.Equal(120, project.tempos[0].bpm);
            Assert.DoesNotContain(project.parts, p => p.position == 999999);
            Assert.Contains("future_key", Ustxp.SerializeForSave(project));
        }

        // ── 3. 纯净导出 ─────────────────────────────────────────────────────

        [Fact]
        public void ExportCleanUstx_StripsPlusFields_AndLoadsBack() {
            var project = Ustxp.Load(CopyFixture("src.ustxp"));
            project.tracks[0].VstSlots.Add(new OpenUtau.Core.Vst.VstPluginSlot {
                PluginUid = "vst-uid-1",
                SlotIndex = 0,
                StateDataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            });
            project.tracks[0].MixFx = new UMixFx { Enabled = true };
            project.ustxpVersion = Ustxp.kUstxpVersion;

            string clean = Path.Combine(dir, "clean.ustx");
            Ustxp.ExportCleanUstx(clean, project);

            string text = File.ReadAllText(clean);
            foreach (string plusKey in new[] { "ustxp_version", "vst_slots", "mix_fx", "plugin_uid" }) {
                Assert.DoesNotContain(plusKey, text);
            }
            Assert.Contains("ustx_version:", text);
            Assert.Contains("tracks:", text);

            var reloaded = Ustxp.Load(clean);
            Assert.Empty(reloaded.tracks[0].VstSlots);
            Assert.Null(reloaded.tracks[0].MixFx);
            Assert.NotEmpty(reloaded.parts);
            output.WriteLine($"clean export: {new FileInfo(clean).Length} B, no Plus keys");
        }

        [Fact]
        public void CleanExportMenu_IsWired() {
            string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));
            Assert.Contains("menu.file.exportcleanustx", xaml);
            Assert.Contains("Click=\"OnMenuExportCleanUstx\"", xaml);
            string code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml.cs"));
            Assert.Contains("Ustxp.ExportCleanUstx(file, project)", code);
        }

        // ── 4. 噪音清理（行数/字节数对照）────────────────────────────────────

        [Fact]
        public void NoiseCleanup_ReducesSize_AndSemanticsSurvive() {
            var project = Ustxp.Load(CopyFixture("noise-src.ustxp"));
            // 本用例量的是"空集合不恒写"（第 4 件）⇒ 先清掉未知键，走常规保存路径；
            // 含未知键时走的是"保数据"慢路径（树合并），格式与主序列化器不同，见 Ustxp.SerializeForSave。
            UstxYaml.ClearUnknown(project);
            string raw = Yaml.DefaultSerializer.Serialize(project);
            string cleaned = Ustxp.SerializeForSave(project);

            foreach (string noisy in new[] { "track_expressions: []", "vst_slots: []", "voice_color_names: []" }) {
                Assert.DoesNotContain(noisy, cleaned);
            }
            int beforeLines = raw.Split('\n').Length;
            int afterLines = cleaned.Split('\n').Length;
            int beforeBytes = System.Text.Encoding.UTF8.GetByteCount(raw);
            int afterBytes = System.Text.Encoding.UTF8.GetByteCount(cleaned);
            output.WriteLine($"noise cleanup: {beforeLines} → {afterLines} lines, {beforeBytes} → {afterBytes} bytes");
            Assert.True(afterLines < beforeLines, $"行数应减少：{beforeLines} → {afterLines}");
            Assert.True(afterBytes < beforeBytes, $"字节数应减少：{beforeBytes} → {afterBytes}");

            string file = Path.Combine(dir, "noise.ustxp");
            File.WriteAllText(file, cleaned);
            var reloaded = Ustxp.Load(file);
            Assert.Equal(project.tracks.Count, reloaded.tracks.Count);
            Assert.Equal(project.parts.Count, reloaded.parts.Count);
            Assert.Empty(reloaded.tracks[0].TrackExpressions);
            Assert.Equal(project.tempos[0].bpm, reloaded.tempos[0].bpm);
        }

        // ── 5. 迁移表逐级 ───────────────────────────────────────────────────

        [Fact]
        public void MigrationTable_IsOrderedAndReachesCurrentVersion() {
            var steps = UstxMigrations.Table(UstxMigrations.Kind.Ustx);
            Assert.Equal(steps.OrderBy(s => s.To).Select(s => s.To), steps.Select(s => s.To));
            Assert.Contains(steps, s => s.To == new Version(0, 10));
            Assert.Equal(Ustx.kUstxVersion, steps[steps.Count - 1].To);
            Assert.NotEmpty(UstxMigrations.Table(UstxMigrations.Kind.Plus));
            output.WriteLine("migration steps: " + string.Join(" → ", steps.Select(s => s.To)));
        }

        [Fact]
        public void Migrations_RunStepwise() {
            var project = Ustxp.Load(CopyFixture("mig.ustxp"));
            project.expressions.Clear();
            project.expressions["acc"] = new UExpressionDescriptor("accent", "acc", 0, 200, 100);
            UstxMigrations.Run(project, UstxMigrations.Kind.Ustx, new Version(0, 0, 3));
            Assert.False(project.expressions.ContainsKey("acc"));
            Assert.True(project.expressions.ContainsKey(Ustx.ATK));
        }
    }
}
