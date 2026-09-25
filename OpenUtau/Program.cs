using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // 音频格式事实来源：当前固定 44100/2/4096（行为零变化，未来可接 Preferences）
            Core.SignalChain.AudioSettings.Configure(44100, 2);
            InitLogging();
            string processName = Process.GetCurrentProcess().ProcessName;
            if (processName != "dotnet") {
                // 按 exe 完整路径匹配单实例——原版与 Plus 进程名同为 OpenUtau，
                // 按进程名判断会把两个版本互相视为"已在运行"（无法共存）。
                // 仅拦截同目录实例：原版在不同路径，可同时运行。
                string myPath = GetExePath(Process.GetCurrentProcess());
                bool exists = Process.GetProcessesByName(processName)
                    .Where(p => p.Id != Environment.ProcessId)
                    .Any(p => string.Equals(GetExePath(p), myPath, StringComparison.OrdinalIgnoreCase));
                if (exists) {
                    Log.Information($"Process {processName} already open. Exiting.");
                    return;
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
}
