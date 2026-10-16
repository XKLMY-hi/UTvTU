using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using UTvTU.Setup;

namespace UTvTU.Installer;

internal static class Program {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr owner, string message, string title, uint flags);
    [STAThread]
    public static int Main(string[] args) {
        try {
            InstallerCore.Command = SetupCommandLine.Parse(args);
            if (Path.GetFileName(InstallerCore.SelfPath).Equals(InstallerCore.UninstExe, StringComparison.OrdinalIgnoreCase)) {
                string directory = AppContext.BaseDirectory;
                string cached = Path.Combine(AppContext.BaseDirectory, "target.json");
                if (File.Exists(cached)) { var options = SetupJson.Read<SetupOptions>(cached); if (options.UserSid == InstallerCore.UserSid) directory = options.Directory; }
                InstallerCore.Command = InstallerCore.Command with { Uninstall = true, Directory = InstallerCore.Command.Directory ?? directory };
            }
            if (InstallerCore.Command.WorkerJob is string job) return InstallerCore.RunWorker(job);
            if (RelocateUninstaller()) return Environment.ExitCode;
            if (InstallerCore.Command.Silent) return RunSilent();
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        } catch (Exception error) {
            WriteLog("启动失败", error.ToString());
            if (!InstallerCore.Command.Silent) MessageBoxW(IntPtr.Zero, error.Message + "\n\n日志：" + InstallerCore.LogDirectory, "UTvTU 安装程序", 0x10);
            return 1;
        }
    }
    static bool RelocateUninstaller() {
        var command = InstallerCore.Command;
        if (!command.Uninstall || command.Relocated || command.Preview) return false;
        string target = InstallerCore.DefaultInstallDir();
        if (!SetupPaths.Within(target, InstallerCore.SelfPath)) return false;
        if (command.Silent) throw new IOException("静默卸载请使用 Windows 登记的 QuietUninstallString，或从独立安装包执行 --silent --uninstall --dir。安装目录内的入口用于交互卸载。");
        // Only published single-file executables can be copied without dependencies.
#if !SINGLE_FILE_SETUP
        throw new IOException("请使用正式的单文件卸载程序。");
#else
        string temporary = Path.Combine(Path.GetTempPath(), "UTvTU-Uninstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary); SetupPaths.RejectLinks(temporary);
        string exe = Path.Combine(temporary, InstallerCore.UninstExe); File.Copy(InstallerCore.SelfPath, exe);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = temporary };
        foreach (string arg in (command with { Relocated = true, Directory = target }).Arguments()) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动卸载程序。");
        return true;
#endif
    }
    static int RunSilent() {
        try {
            var command = InstallerCore.Command;
            var options = InstallerCore.Options(InstallerCore.DefaultInstallDir(), command.DesktopShortcut, command.FileAssociation, command.RemoveData);
            var progress = new InstallerCore.DirectProgress(p => WriteLog("进度", $"{p.Percent}% {p.Stage} {p.Detail}"));
            var result = InstallerCore.RunAsync(options, progress, CancellationToken.None).GetAwaiter().GetResult();
            WriteLog(result.Message, string.Join(Environment.NewLine, result.Warnings));
            return result.Success ? result.Warnings.Count == 0 ? 0 : 2 : 1;
        } catch (OperationCanceledException) { WriteLog("已取消", "文件已恢复或未修改。"); return 3; }
        catch (Exception error) { WriteLog("失败", error.ToString()); return 1; }
    }
    internal static void WriteLog(string stage, string detail) {
        try { lock (LogGate) { Directory.CreateDirectory(InstallerCore.LogDirectory); File.AppendAllText(Path.Combine(InstallerCore.LogDirectory, "setup-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), $"{DateTime.Now:O} [{Environment.ProcessId}] {stage} {detail}{Environment.NewLine}"); } } catch { }
    }
    static readonly object LogGate = new();
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
