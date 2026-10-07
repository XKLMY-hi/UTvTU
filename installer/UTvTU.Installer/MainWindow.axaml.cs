using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace UTvTU.Installer {
    /// <summary>
    /// 安装向导（5 个互斥页面）。状态机很浅：0 欢迎 → 1 位置 → 2 安装中 → 3 完成；
    /// 卸载模式（--uninstall）只有 1 页。
    /// </summary>
    public partial class MainWindow : Window {
        private readonly bool uninstallMode;
        private int step;

        public MainWindow() : this(false) { }

        public MainWindow(bool uninstallMode) {
            this.uninstallMode = uninstallMode || InstallerCore.UninstallMode;
            InitializeComponent();   // ⚠ 必须走生成的 InitializeComponent：x:Name 字段由 name generator 在此赋值
            Wire();
            BuildInitialState();
        }

        private void Wire() {
            BtnNext.Click += OnNext;
            BtnBack.Click += (_, _) => GoTo(Math.Max(0, step - 1));
            BtnCancel.Click += (_, _) => Close();
        }

        private void BuildInitialState() {
            string dir = InstallerCore.DefaultInstallDir();
            DirBox.Text = dir;
            WelcomeTarget.Text = dir;

            (string Path, string DisplayName)? legacy = InstallerCore.DetectLegacy();
            bool sameAsLegacy = legacy != null &&
                string.Equals(legacy.Value.Path, dir, StringComparison.OrdinalIgnoreCase);
            if (legacy != null) {
                string msg = sameAsLegacy
                    ? $"检测到既有安装：{legacy.Value.DisplayName}（{legacy.Value.Path}）—— 将就地覆盖"
                    : $"检测到另一个安装：{legacy.Value.DisplayName}（{legacy.Value.Path}）—— 不会动它";
                WelcomeLegacy.Text = msg;
                WelcomeLegacy.IsVisible = true;
                LegacyCard.IsVisible = true;
                LegacyInfo.Text = msg;
            }
            ElevationNote.IsVisible = InstallerCore.NeedsElevation(dir) && !InstallerCore.IsElevated();

            string? own = null;
            try {
                using Microsoft.Win32.RegistryKey? k = Microsoft.Win32.Registry.CurrentUser
                    .OpenSubKey(InstallerCore.UninstKey);
                own = k?.GetValue("DisplayVersion") as string;
            } catch {
                // 忽略
            }
            StatusText.Text = own != null
                ? $"已安装版本 {own} ⇒ 将升级到 {InstallerCore.Version}"
                : $"{InstallerCore.Version} · 载荷 {(InstallerCore.HasPayload ? "就绪" : "缺失")}";

            if (uninstallMode) {
                PageTitle.Text = "卸载";
                UninstallTarget.Text = $"将卸载：{dir}";
                BtnNext.Content = "卸载";
                GoTo(-1);   // 只显示卸载页
            } else {
                // 截图/验证用：UTVTU_INSTALLER_PAGE=<0..3> 直接停在指定页（不影响用户正常流程）
                int start = 0;
                if (int.TryParse(Environment.GetEnvironmentVariable("UTVTU_INSTALLER_PAGE"), out int pg)
                    && pg is >= 0 and <= 3) {
                    start = pg;
                }
                GoTo(start);
            }
        }

        private void GoTo(int s) {
            step = s;
            PageWelcome.IsVisible = s == 0;
            PageLocation.IsVisible = s == 1;
            PageInstall.IsVisible = s == 2;
            PageDone.IsVisible = s == 3;
            PageUninstall.IsVisible = uninstallMode;
            BtnBack.IsVisible = !uninstallMode && s == 1;
            if (uninstallMode) {
                PageTitle.Text = "卸载 UTvTU";
            } else {
                PageTitle.Text = s switch { 0 => "安装", 1 => "安装位置", 2 => "正在安装", _ => "完成" };
            }
            BtnNext.Content = uninstallMode ? "卸载" : s switch { 0 => "下一步", 1 => "安装", 2 => "安装中…", _ => "完成" };
            BtnNext.IsEnabled = !(s == 2 && !uninstallMode);
            BtnCancel.IsVisible = !(s == 3);
        }

        private async void OnNext(object? sender, RoutedEventArgs e) {
            try {
                if (uninstallMode) {
                    await RunUninstall();
                    return;
                }
                switch (step) {
                    case 0:
                        GoTo(1);
                        break;
                    case 1:
                        await RunInstall();
                        break;
                    default:
                        if (ChkLaunch.IsChecked == true) {
                            string exe = Path.Combine(DirBox.Text ?? "", InstallerCore.AppExe);
                            if (File.Exists(exe)) {
                                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                            }
                        }
                        Close();
                        break;
                }
            } catch (Exception ex) {
                Log(ex.ToString());   // 完整堆栈：跨线程/COM 这类问题只看 Message 定位不了
                StatusText.Text = "失败：" + ex.Message;
                BtnNext.IsEnabled = true;
            }
        }

        private async Task RunInstall() {
            string dir = (DirBox.Text ?? "").Trim();
            if (dir.Length == 0) {
                StatusText.Text = "请填写安装位置";
                return;
            }
            if (!InstallerCore.HasPayload) {
                StatusText.Text = $"缺少载荷文件 {InstallerCore.PayloadName}（应与本程序同目录）";
                return;
            }
            if (InstallerCore.NeedsElevation(dir) && !InstallerCore.IsElevated()) {
                StatusText.Text = "正在请求管理员权限…";
                if (!InstallerCore.RelaunchElevated(dir)) {
                    StatusText.Text = "已取消提权 —— 请改选一个用户目录，或以管理员身份运行";
                    return;
                }
                Close();
                return;
            }

            GoTo(2);
            var progress = new Progress<(int Percent, string Stage)>(p => {
                Bar.Value = p.Percent;
                StageText.Text = p.Stage;
            });
            // ⚠ 勾选值必须在**切后台线程之前**读好：UI 控件属性只能在 UI 线程访问
            //   （实测踩过：后台线程读 ToggleButton.IsChecked ⇒ "The calling thread cannot access this object"）
            bool desktopShortcut = ChkDesktop.IsChecked == true;
            bool fileAssoc = ChkAssoc.IsChecked == true;
            string log = "";
            void OnLog(string line) {
                log += line + Environment.NewLine;
            }

            await Task.Run(() => {
                OnLog($"目标目录：{dir}");
                if (InstallerCore.DetectLegacy() is { } lg) {
                    OnLog($"检测到既有安装：{lg.DisplayName} @ {lg.Path}");
                }
                InstallerCore.Install(dir, desktopShortcut, fileAssoc, progress, CancellationToken.None);
                OnLog("安装完成");
            });
            await Dispatcher.UIThread.InvokeAsync(() => LogText.Text = log);
            GoTo(3);
        }

        private async Task RunUninstall() {
            string dir = (DirBox.Text ?? "").Trim();
            GoTo(2);
            PageInstall.IsVisible = true;
            var progress = new Progress<(int Percent, string Stage)>(p => {
                Bar.Value = p.Percent;
                StageText.Text = p.Stage;
            });
            bool removeData = ChkRemoveData.IsChecked == true;   // ⚠ 同上：先取好再切线程
            await Task.Run(() => InstallerCore.Uninstall(dir, removeData, progress));
            StatusText.Text = "卸载完成";
            await Task.Delay(700);
            Close();
        }

        private void Log(string line) {
            LogText.Text = (LogText.Text ?? "") + line + Environment.NewLine;
        }
    }
}
