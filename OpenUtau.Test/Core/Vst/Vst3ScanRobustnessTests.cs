using System;
using System.IO;
using System.Text.Json;
using OpenUtau.Core.Vst;
using Xunit;

namespace OpenUtau.Test.Core.Vst {
    /// <summary>
    /// W12：VST3 扫描健壮性契约。
    ///
    /// 三条真机暴露的问题各有一条用例：
    ///  ① `moduleinfo.json` 尾逗号/注释 ⇒ 过去 `JsonDocument.Parse` 抛异常被 `catch {}` 吞掉，整个 bundle 消失；
    ///  ② 无 moduleinfo 的老式 bundle（含我们自己的 OpenUtau Bridge.vst3）⇒ 过去直接 return false，永远不出现；
    ///  ③ 测试宿主缺 `vst_probe.exe` ⇒ 单文件 .vst3 与回退探测都不可用（必须能与"解析失败"区分）。
    ///
    /// 这几条都是纯函数/纯文件判定，不需要 Avalonia（用 [Fact]）。
    /// </summary>
    public class Vst3ScanRobustnessTests {
        const string TrailingCommaBundleDir = @"C:\VST3\MODO BASS 2.vst3";

        /// <summary>真实形态（IK Multimedia 实测）：尾逗号 + 注释 + Instrument 子类。</summary>
        const string IkStyleModuleInfo = """
        {
          // IK Multimedia 的 moduleinfo 确实这么写（实测 MODO BASS 2）
          "Name": "MODO BASS 2",
          "Version": "2.0.5",
          "Factory Info": {
            "Vendor": "IK Multimedia",
            "URL": "https://www.ikmultimedia.com",
            "Flags": {
              "Unicode": true,
              "Classes Discardable": false,
            },
          },
          "Classes": [
            {
              "CID": "ABCDEF01-2345-6789-ABCD-EF0123456789",
              "Category": "Audio Module Class",
              "Name": "MODO BASS 2",
              "Sub Categories": [ "Instrument", "Synth" ],
            },
          ],
        }
        """;

        [Fact]
        public void ModuleInfo_WithTrailingCommasAndComments_Parses() {
            VstPluginEntry entry = VstPluginRegistry.ParseVst3ModuleInfo(IkStyleModuleInfo, TrailingCommaBundleDir);

            Assert.NotNull(entry);
            Assert.Equal("vst3:abcdef0123456789abcdef0123456789", entry!.Uid);   // CID 归一化：去横线 + 小写
            Assert.Equal("MODO BASS 2", entry.Name);
            Assert.Equal("IK Multimedia", entry.Vendor);
            Assert.Equal(TrailingCommaBundleDir, entry.Path);                    // 登记路径 = bundle 目录
            Assert.Equal(VstPluginType.VST3, entry.Type);
            Assert.False(entry.IsEffect);                                        // Instrument/Synth ⇒ 不是效果器
            Assert.Contains("Instrument", entry.SubCategories);
        }

        [Fact]
        public void ModuleInfo_StrictJson_StillParses() {
            // 回归：正常（无尾逗号）的 moduleinfo 必须照旧可用
            const string strict = """
            {
              "Name": "TDR Nova",
              "Factory Info": { "Vendor": "Tokyo Dawn Labs" },
              "Classes": [
                {
                  "CID": "0123456789abcdef0123456789abcdef",
                  "Category": "Audio Module Class",
                  "Name": "TDR Nova",
                  "Sub Categories": [ "Fx", "EQ" ]
                }
              ]
            }
            """;
            VstPluginEntry entry = VstPluginRegistry.ParseVst3ModuleInfo(strict, @"C:\VST3\TDR Nova.vst3");
            Assert.NotNull(entry);
            Assert.Equal("TDR Nova", entry!.Name);
            Assert.Equal("Tokyo Dawn Labs", entry.Vendor);
            Assert.True(entry.IsEffect);                                         // Fx ⇒ 效果器
        }

        [Fact]
        public void ModuleInfo_NoAudioModuleClass_ReturnsNull() {
            const string noClass = """
            { "Name": "Empty", "Classes": [ { "CID": "aa", "Category": "Something Else" } ] }
            """;
            Assert.Null(VstPluginRegistry.ParseVst3ModuleInfo(noClass, @"C:\VST3\Empty.vst3"));

            const string noClassesArray = """{ "Name": "Empty" }""";
            Assert.Null(VstPluginRegistry.ParseVst3ModuleInfo(noClassesArray, @"C:\VST3\Empty.vst3"));
        }

        [Fact]
        public void ModuleInfo_TrulyBrokenJson_Throws_SoCallerCanLogAndFallBack() {
            // 语法真的坏时**抛**（而不是静默返回 null）：调用方要能记日志并回退到内部二进制
            Assert.ThrowsAny<JsonException>(() =>
                VstPluginRegistry.ParseVst3ModuleInfo("{ \"Name\": ", @"C:\VST3\Broken.vst3"));
        }

        // ── ② 老式 bundle 回退：Contents/<arch>/*.vst3 ──────────────────────

        [Fact]
        public void FindInnerVst3Binary_PrefersCurrentArch_ThenSortsByName() {
            string root = NewTempDir();
            try {
                string contents = Path.Combine(root, "Bridge.vst3", "Contents");
                Directory.CreateDirectory(Path.Combine(contents, "x86-win"));
                Directory.CreateDirectory(Path.Combine(contents, "x86_64-win"));
                Directory.CreateDirectory(Path.Combine(contents, "other"));
                File.WriteAllText(Path.Combine(contents, "x86-win", "bridge32.vst3"), "");
                File.WriteAllText(Path.Combine(contents, "x86_64-win", "bridge64.vst3"), "");
                File.WriteAllText(Path.Combine(contents, "other", "aaa.vst3"), "");

                string inner = VstPluginRegistry.FindInnerVst3Binary(contents);
                Assert.NotNull(inner);
                // 64 位进程优先取 x86_64-win（本用例运行在 64 位宿主上）
                Assert.Equal(Path.Combine(contents, "x86_64-win", "bridge64.vst3"), inner);

                // 没有架构匹配时按路径名排序取第一个（稳定）
                string contents2 = Path.Combine(root, "NoArch.vst3", "Contents");
                Directory.CreateDirectory(Path.Combine(contents2, "whatever"));
                File.WriteAllText(Path.Combine(contents2, "whatever", "zzz.vst3"), "");
                File.WriteAllText(Path.Combine(contents2, "whatever", "aaa.vst3"), "");
                Assert.Equal(Path.Combine(contents2, "whatever", "aaa.vst3"),
                    VstPluginRegistry.FindInnerVst3Binary(contents2));
            } finally {
                SafeDelete(root);
            }
        }

        [Fact]
        public void FindInnerVst3Binary_MissingContentsOrBinary_ReturnsNull() {
            string root = NewTempDir();
            try {
                Assert.Null(VstPluginRegistry.FindInnerVst3Binary(Path.Combine(root, "nope", "Contents")));

                string emptyContents = Path.Combine(root, "Empty.vst3", "Contents");
                Directory.CreateDirectory(Path.Combine(emptyContents, "x86_64-win"));
                Assert.Null(VstPluginRegistry.FindInnerVst3Binary(emptyContents));
            } finally {
                SafeDelete(root);
            }
        }

        // ── ③ 探针环境：测试宿主必须有 vst_probe.exe ────────────────────────

        /// <summary>
        /// 测试工程必须把 vst_probe.exe 复制到输出目录（W12 修）：否则单文件 .vst3 与老式 bundle
        /// 回退全部不可用，测试环境会把"环境缺失"读成"这台机器没插件"。
        /// </summary>
        [Fact]
        public void TestHost_HasVstProbeExecutable_AndRegistrySeesIt() {
            string expected = Path.Combine(AppContext.BaseDirectory, "vst_probe.exe");
            Assert.True(File.Exists(expected),
                $"测试输出目录缺少 vst_probe.exe（应在 {expected}）——单文件 VST3 扫描会静默空转");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "vst_probe.dll")),
                "vst_probe 需要同目录的 dll + runtimeconfig.json");
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "vst_probe.runtimeconfig.json")));

            // 注册表的可用性开关（"环境缺失"与"解析失败"必须能区分）
            Assert.True(VstPluginRegistry.ProbeAvailable, $"探针不可用：{VstPluginRegistry.ProbeExePath}");
            Assert.True(File.Exists(VstPluginRegistry.ProbeExePath));
        }

        // ── helpers ────────────────────────────────────────────────────────

        static string NewTempDir() {
            string dir = Path.Combine(Path.GetTempPath(), "utvtu-w12-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void SafeDelete(string dir) {
            try { Directory.Delete(dir, true); } catch { /* 清理失败无所谓 */ }
        }
    }
}
