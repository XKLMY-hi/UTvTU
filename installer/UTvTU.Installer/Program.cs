using System;
using System.IO;
using System.Threading;
using Avalonia;

namespace UTvTU.Installer {
    internal static class Program {
        /// <summary>
        /// 入口。两种模式：
        ///   · 无参 / 图形安装：正常开安装向导；
        ///   · `--uninstall [--dir &lt;安装目录&gt;]`：卸载流程（安装时把自身复制为 UTvTU-Uninstall.exe）；
        ///   · `--elevated &lt;原始参数&gt;`：提权后的子进程（内部用，避免二次 UAC 递归）。
        /// 启动期异常写 `installer-crash.log`（WinExe 没有控制台，崩溃日志是唯一线索）。
        /// </summary>
        [STAThread]
        public static void Main(string[] args) {
            try {
                InstallerCore.ParseArgs(args);
                if (InstallerCore.Silent) {
                    RunSilent();
                    return;
                }
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            } catch (Exception ex) {
                try {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "installer-crash.log"),
                        $"{DateTime.Now:O}{Environment.NewLine}{ex}");
                } catch {
                    // 连日志都写不了就放弃
                }
                throw;
            }
        }

        /// <summary>
        /// 无界面模式（可回归/可脚本化）：`--silent [--uninstall] [--dir &lt;目录&gt;] [--no-desktop]`
        /// 日志写 `installer-silent.log`（WinExe 没有可用 stdout），退出码 0=成功 / 1=失败。
        /// </summary>
        private static void RunSilent() {
            string log = Path.Combine(AppContext.BaseDirectory, "installer-silent.log");
            var sb = new System.Text.StringBuilder();
            void W(string s) {
                // Progress 回调来自后台线程 ⇒ 必须加锁（否则 StringBuilder 并发写会抛 ArgumentOutOfRange）
                lock (sb) {
                    sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
                    File.WriteAllText(log, sb.ToString());
                }
            }
            try {
                string dir = InstallerCore.DefaultInstallDir();
                W($"mode={(InstallerCore.UninstallMode ? "uninstall" : "install")} dir={dir} elevated={InstallerCore.IsElevated()}");
                if (InstallerCore.UninstallMode) {
                    InstallerCore.Uninstall(dir, removeData: false,
                        new Progress<(int, string)>(p => W($"{p.Item1}% {p.Item2}")));
                    W("OK uninstalled");
                } else {
                    if (!InstallerCore.HasPayload) {
                        W($"FAIL missing payload: {InstallerCore.PayloadPath}");
                        Environment.Exit(1);
                    }
                    InstallerCore.Install(dir, !InstallerCore.NoDesktopShortcut, fileAssociation: true,
                        new Progress<(int, string)>(p => W($"{p.Item1}% {p.Item2}")), CancellationToken.None);
                    W("OK installed");
                }
                Environment.Exit(0);
            } catch (Exception ex) {
                W("FAIL " + ex);
                Environment.Exit(1);
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
