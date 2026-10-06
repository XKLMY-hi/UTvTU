using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App {
    public class Program {
        /// <summary>本进程持有的单实例锁（进程存活期间一直持有；异常退出由系统标记为遗弃）。</summary>
        static Mutex? instanceLock;

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // 音频格式事实来源：当前固定 44100/2/4096（行为零变化，未来可接 Preferences）
            Core.SignalChain.AudioSettings.Configure(44100, 2);
            InitLogging();
            string myPath = GetExePath(Process.GetCurrentProcess());
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "dotnet") {
                // W41 单实例判定：**命名互斥体**，锁名由 exe 完整路径派生。
                // 为什么不能按进程名：原版 OpenUTAU 与 Plus 的进程名同为 "OpenUtau"，
                // 按名字判会把两个**不同应用**互相挡住（用户没法同时开，且退出无提示）。
                // 路径不同 ⇒ 锁名不同 ⇒ 可共存；同一份 Plus 重复启动 ⇒ 拿不到锁 ⇒ 聚焦已有窗口后退出。
                // 互斥体同时修掉"枚举进程"方案的 TOCTOU：两个实例同时启动时彼此都枚举不到对方。
                if (!SingleInstanceGuard.TryClaim(out instanceLock, myPath, out bool wasAbandoned)) {
                    var existing = SingleInstanceGuard.FindRunningInstance(
                        myPath, Environment.ProcessId, Process.GetProcesses().Select(SingleInstanceGuard.Info));
                    Log.Information(existing == null
                        ? "Another instance of this build is already running. Exiting."
                        : $"Another instance of this build is already running (pid {existing.Pid}). " +
                          "Focusing it and exiting.");
                    if (existing != null) {
                        SingleInstanceGuard.TryFocusExistingWindow(existing.Pid);
                    }
                    return;
                }
                if (wasAbandoned) {
                    // 上一个实例是崩溃/被杀退出的：锁被系统标记为"遗弃"，这里接管，不算僵尸锁。
                    Log.Information("Previous instance exited abnormally; took over the instance lock.");
                }
            }
            Log.Information($"{Environment.OSVersion}");
            Log.Information($"{RuntimeInformation.OSDescription} " +
                $"{RuntimeInformation.OSArchitecture} " +
                $"{RuntimeInformation.ProcessArchitecture}");
            Log.Information($"UTvTU v{Assembly.GetEntryAssembly()?.GetName().Version} p{Core.PlusInfo.PlusVersion} " +
                $"{RuntimeInformation.RuntimeIdentifier}");
            Log.Information($"Data path = {PathManager.Inst.DataPath}");
            Log.Information($"Cache path = {PathManager.Inst.CachePath}");
            Log.Information($"System encoding = {Encoding.GetEncoding(0)?.WebName ?? "null"}");
            try {
                Run(args);
                Log.Information($"Exiting.");
            } finally {
                if (!OS.IsMacOS()) {
                    NetMQ.NetMQConfig.Cleanup(/*block=*/false);
                    // Cleanup() hangs on macOS https://github.com/zeromq/netmq/issues/1018
                }
                instanceLock?.Dispose();   // 正常退出即释放单实例锁（下一个实例能正常拿到）
            }
            Log.Information($"Exited.");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() {
            FontManagerOptions fontOptions = new();
            if (OS.IsLinux()) {
                using Process process = Process.Start(new ProcessStartInfo("fc-match")
                {
                    ArgumentList = { "-f", "%{family}" },
                    RedirectStandardOutput = true
                })!;
                process.WaitForExit();

                string fontFamily = process.StandardOutput.ReadToEnd();
                if (!string.IsNullOrEmpty(fontFamily)) {
                    string [] fontFamilies = fontFamily.Split(',');
                    fontOptions.DefaultFamilyName = $"HarmonyOS Sans SC, {fontFamilies[0]}";
                } else {
                    fontOptions.DefaultFamilyName = "HarmonyOS Sans SC";
                }
            } else if (OS.IsMacOS()) {
                fontOptions.DefaultFamilyName = "HarmonyOS Sans SC, Hiragino Sans, Segoe UI, San Francisco, Helvetica Neue";
            } else {
                fontOptions.DefaultFamilyName = "HarmonyOS Sans SC";
            }
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseHarfBuzz()
                .LogToTrace()
                .UseReactiveUI()
                .With(fontOptions)
                .With(new X11PlatformOptions {EnableIme = true});
        }

        public static void Run(string[] args)
            => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(
                    args, ShutdownMode.OnMainWindowClose);

        /// <summary>进程 exe 完整路径（其他进程可能无权限读取，失败返回空串不匹配）。</summary>
        static string GetExePath(Process p) {
            try {
                return p.MainModule?.FileName ?? string.Empty;
            } catch {
                return string.Empty;
            }
        }

        public static void InitLogging() {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Information()
                    .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8))
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.ControlledBy(DebugViewModel.Sink.Inst.LevelSwitch)
                    .WriteTo.Sink(DebugViewModel.Sink.Inst))
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "Unhandled exception");
                DumpDefaultFontControls();
            });
            Log.Information("Logging initialized.");
        }

        /// <summary>
        /// 诊断：崩溃时打印视觉树中 FontFamily 仍为 $Default 的文本控件。
        /// $Default 在部分设备上 Skia 解析失败即渲染崩溃——此 dump 用于定位漏网控件。
        /// </summary>
        static void DumpDefaultFontControls() {
            try {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) {
                    return;
                }
                var sb = new StringBuilder();
                foreach (var window in desktop.Windows) {
                    WalkVisual(window, window.GetType().Name, sb);
                }
                if (sb.Length > 0) {
                    Log.Error($"[FontDiag] Text controls resolving $Default font:\n{sb}");
                } else {
                    Log.Error("[FontDiag] No $Default text controls found in window visual trees.");
                }
            } catch (Exception e) {
                Log.Error(e, "[FontDiag] Failed to dump font diagnostics.");
            }
        }

        static void WalkVisual(Visual visual, string path, StringBuilder sb) {
            if (visual is TextBlock tb) {
                var ff = tb.FontFamily;
                if (ff != null && ff.ToString().Contains("$Default")) {
                    sb.AppendLine($"  {visual.GetType().Name}(Name={tb.Name}) text='{Truncate(tb.Text, 60)}' path={path}");
                }
            }
            foreach (var child in visual.GetVisualChildren()) {
                WalkVisual(child, $"{path}/{child.GetType().Name}", sb);
            }
        }

        static string Truncate(string? s, int len) {
            if (string.IsNullOrEmpty(s)) {
                return "";
            }
            return s.Length <= len ? s : s.Substring(0, len) + "…";
        }
    }

    /// <summary>
    /// W41：单实例判定。
    ///
    /// **判据 = 可执行文件完整路径**，不是进程名 —— 原版 OpenUTAU 与 Plus 的进程名同为
    /// "OpenUtau"，按名字判会把两个不同应用互相挡住（用户无法共存，且退出无任何提示）。
    ///
    /// **实现 = 命名互斥体**（名字由路径哈希派生）。相比"枚举同名进程再比路径"，
    /// 互斥体额外修掉两个坑：
    ///   · TOCTOU：两个实例同时启动时"枚举进程"彼此都看不到对方 ⇒ 双双进主循环；
    ///   · 僵尸锁：上一个实例异常退出留下的锁必须能接管
    ///     （<see cref="AbandonedMutexException"/> 视为可获取，绝不让用户"打不开"）。
    /// 路径不同 ⇒ 锁名不同 ⇒ 原版/便携版/开发构建各自独立，可同时运行。
    /// </summary>
    internal static class SingleInstanceGuard {
        /// <summary>一条"已有实例"的最小事实（做成可注入的纯数据 ⇒ 契约用例不必真起进程）。</summary>
        internal sealed record InstanceInfo(int Pid, string? ExePath);

        /// <summary>真实进程 → 事实。别的进程可能无权限读 MainModule，失败给 null（不参与匹配）。</summary>
        internal static InstanceInfo Info(Process p) {
            string? path = null;
            try {
                path = p.MainModule?.FileName;
            } catch {
                // 无权限/已退出：当作"不知道路径"，不匹配任何人（宁可多开，不可误挡）
            }
            return new InstanceInfo(p.Id, path);
        }

        /// <summary>同一程序 ⇔ exe 完整路径相同（大小写不敏感）。空路径不匹配任何人。</summary>
        internal static bool IsSameApp(string? otherExePath, string myExePath) {
            if (string.IsNullOrEmpty(otherExePath) || string.IsNullOrEmpty(myExePath)) {
                return false;
            }
            return string.Equals(NormalizePath(otherExePath), NormalizePath(myExePath),
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizePath(string path) {
            try {
                return Path.GetFullPath(path);
            } catch {
                return path;
            }
        }

        /// <summary>锁名由"同一程序"的路径派生：同路径（含大小写差异）同名，不同路径不同名。</summary>
        internal static string MutexNameFor(string exePath) {
            string normalized = NormalizePath(exePath ?? string.Empty).ToLowerInvariant();
            byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            // `Local\` 而非 `Global\`：单实例是**每登录会话**语义（多用户/RDP 各自一份，互不影响）
            return $@"Local\UTvTU.SingleInstance.{Convert.ToHexString(hash)[..16]}";
        }

        /// <summary>
        /// 尝试取得本程序的单实例锁。false = 已有同路径实例在跑（调用方应聚焦后退出）。
        /// <paramref name="wasAbandoned"/> = 上一个持有者异常退出，锁被接管（不是僵尸锁）。
        /// </summary>
        internal static bool TryClaim(out Mutex? mutex, string exePath, out bool wasAbandoned) {
            mutex = null;
            wasAbandoned = false;
            Mutex? m = null;
            try {
                m = new Mutex(initiallyOwned: false, MutexNameFor(exePath));
                bool acquired;
                try {
                    acquired = m.WaitOne(0);
                } catch (AbandonedMutexException) {
                    // 前一进程没 ReleaseMutex 就死了：锁归我，且**不算**"已有人在跑"
                    acquired = true;
                    wasAbandoned = true;
                }
                if (!acquired) {
                    m.Dispose();
                    return false;
                }
                mutex = m;
                return true;
            } catch (Exception e) {
                // 平台不支持命名互斥体 / 权限不足：**不阻断启动**（宁可多开一个，不可打不开）
                Log.Warning(e, "Single-instance lock unavailable; continuing without it.");
                m?.Dispose();
                return true;
            }
        }

        /// <summary>找"同一个 Plus"的已有实例（按路径，排除自己）。没有则 null。</summary>
        internal static InstanceInfo? FindRunningInstance(string myExePath, int currentPid,
            IEnumerable<InstanceInfo> candidates)
            => candidates.FirstOrDefault(c => c.Pid != currentPid && IsSameApp(c.ExePath, myExePath));

        /// <summary>
        /// 尽力把已有实例的主窗口置前（重复启动的用户意图就是"把那个窗口给我"）。
        /// 拿不到句柄、被系统拒绝、非 Windows —— 一律静默（不影响"退出"这个主行为）。
        /// </summary>
        internal static void TryFocusExistingWindow(int pid) {
            if (!OS.IsWindows()) {
                return;
            }
            try {
                using var p = Process.GetProcessById(pid);
                IntPtr hwnd = p.MainWindowHandle;
                if (hwnd == IntPtr.Zero) {
                    return;
                }
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            } catch (Exception e) {
                Log.Debug(e, "Failed to focus the existing instance window.");
            }
        }

        const int SW_RESTORE = 9;
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
