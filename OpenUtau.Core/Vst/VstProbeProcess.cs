using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Launches vst_probe.exe in a subprocess so that VST DLL code crashes
    /// (DllMain / VSTPluginMain / AEffect access violations) don't bring down
    /// the main OpenUTAU process.  stdout JSON is parsed and returned.
    /// </summary>
    internal static class VstProbeProcess {
        private static string? _exePath;

        /// <summary>探针可执行文件路径（诊断与日志用；不含存在性判断）。</summary>
        public static string ExePath => GetExePath();

        /// <summary>
        /// 探针是否可用（W12）：缺 vst_probe.exe 时**单文件 .vst3** 与**老式 bundle 回退**都不可用
        /// —— 这是"环境问题"，必须能与"插件解析失败"区分开（否则会被当成"没有插件"）。
        /// </summary>
        public static bool IsAvailable => File.Exists(GetExePath());

        static string GetExePath() {
            if (_exePath != null && File.Exists(_exePath)) return _exePath;
            var dir = Path.GetDirectoryName(typeof(VstProbeProcess).Assembly.Location) ?? ".";
            // 1) 独立子目录（发布布局：self-contained vst_probe——与主程序运行时隔离，
            //    避免 framework-dependent 探针被同目录 hostfxr 干扰而无法启动）
            _exePath = Path.Combine(dir, "vst_probe", "vst_probe.exe");
            if (File.Exists(_exePath)) return _exePath;
            // 2) 顶层（旧发布布局）
            _exePath = Path.Combine(dir, "vst_probe.exe");
            if (File.Exists(_exePath)) return _exePath;
            // 3) runtimes/win-x64/native（dev layout）
            _exePath = Path.Combine(dir, "runtimes", "win-x64", "native", "vst_probe.exe");
            return _exePath;
        }

        /// <summary>
        /// Probe a VST2 DLL in a subprocess.  Returns null on failure,
        /// otherwise a populated entry with IsEffect determined by the probe.
        /// </summary>
        public static VstPluginEntry? ProbeVst2(string dllPath) {
            if (!OS.IsWindows()) return null;

            string exe = GetExePath();
            if (!File.Exists(exe)) {
                Log.Warning($"[VstProbe] exe not found: {exe}");
                return null;
            }

            try {
                using var proc = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = exe,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                };
                // ArgumentList 免引号转义（文件名含引号/换行时防参数注入）
                proc.StartInfo.ArgumentList.Add(dllPath);

                proc.Start();
                // 先等退出（带超时），超时 Kill——禁止同步 ReadToEnd 先于超时：
                // 挂死插件永不关 stdout → ReadToEnd 永久阻塞 → 冻结调用线程
                if (!proc.WaitForExit(5000)) {
                    Log.Warning($"[VstProbe] Timed out probing '{dllPath}' — killing subprocess.");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    proc.WaitForExit();
                    return null;
                }
                // 进程已退出，stdout 已关闭——ReadToEnd 立即返回（限长防刷屏）
                var stdout = proc.StandardOutput.ReadToEnd();
                if (stdout.Length > 65536) {
                    Log.Warning($"[VstProbe] Oversized output from probe of '{dllPath}' ({stdout.Length} bytes) — discarding.");
                    return null;
                }

                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                    return null;

                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out _)) return null;

                string name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                bool isEffect = root.TryGetProperty("isEffect", out var ie) && ie.GetBoolean();

                string dllName = Path.GetFileNameWithoutExtension(dllPath);
                string uid = VstPluginRegistry.BuildVst2Uid(dllName);
                return new VstPluginEntry {
                    Uid = uid, Name = name.Length > 0 ? name : dllName,
                    Vendor = "", Path = dllPath,
                    Type = VstPluginType.VST2,
                    SubCategories = new System.Collections.Generic.List<string>(),
                    IsEffect = isEffect,
                };
            } catch (Exception ex) {
                Log.Warning($"[VstProbe] Subprocess failed for '{dllPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Probe a single-file .vst3 DLL in a subprocess (delegates to
        /// vst_bridge.dll's vst_probe).  Returns the raw factory-metadata JSON
        /// (same format as the old in-process probe), or null on failure —
        /// a crashing plugin DLL only kills the subprocess, not OpenUTAU.
        /// </summary>
        public static string? ProbeVst3(string dllPath) {
            if (!OS.IsWindows()) return null;

            string exe = GetExePath();
            if (!File.Exists(exe)) {
                Log.Warning($"[VstProbe] exe not found: {exe}");
                return null;
            }

            try {
                using var proc = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = exe,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    },
                };
                proc.StartInfo.ArgumentList.Add("--vst3");
                proc.StartInfo.ArgumentList.Add(dllPath);

                proc.Start();
                // 先等退出（带超时），超时 Kill——见 ProbeVst2 注释
                if (!proc.WaitForExit(5000)) {
                    Log.Warning($"[VstProbe] Timed out probing VST3 '{dllPath}' — killing subprocess.");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    proc.WaitForExit();
                    return null;
                }
                var stdout = proc.StandardOutput.ReadToEnd();
                if (stdout.Length > 65536) {
                    Log.Warning($"[VstProbe] Oversized output from VST3 probe of '{dllPath}' ({stdout.Length} bytes) — discarding.");
                    return null;
                }

                if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
                    return null;

                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.TryGetProperty("error", out _)) return null;
                return stdout;
            } catch (Exception ex) {
                Log.Warning($"[VstProbe] Subprocess failed for '{dllPath}': {ex.Message}");
                return null;
            }
        }
    }
}
