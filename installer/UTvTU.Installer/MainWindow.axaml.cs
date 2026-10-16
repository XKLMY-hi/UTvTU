using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UTvTU.Setup;

namespace UTvTU.Installer;

public partial class MainWindow : Window {
    bool busy, completed, closeAfterCancel;
    CancellationTokenSource? operation;
    SetupOptions? lastOptions;
    InstallReceipt? lastReceipt;
    bool legacy;
    string[] retainedWarnings = Array.Empty<string>();
    readonly bool uninstall;
    readonly bool preview;
    internal Func<SetupOptions, IProgress<SetupProgress>, CancellationToken, Task<SetupResult>> ExecuteOperation { get; set; } = InstallerCore.RunAsync;
    public MainWindow() {
        InitializeComponent();
        uninstall = InstallerCore.Command.Uninstall; preview = InstallerCore.Command.Preview;
        string directory = InstallerCore.DefaultInstallDir(); DirBox.Text = directory;
        VersionText.Text = InstallerCore.Version;
        DesktopChoice.IsChecked = InstallerCore.Command.DesktopShortcut; AssociationChoice.IsChecked = InstallerCore.Command.FileAssociation; RemoveDataChoice.IsChecked = InstallerCore.Command.RemoveData;
        Title = uninstall ? "卸载 UTvTU" : "安装 UTvTU"; ModeBadge.Text = uninstall ? "卸载" : "安装";
        InstallChoices.IsVisible = !uninstall; UninstallChoices.IsVisible = uninstall;
        DirBox.IsReadOnly = uninstall; BrowseButton.IsVisible = !uninstall;
        StepOptionsText.Text = uninstall ? "卸载选项" : "安装选项"; StepProgressText.Text = uninstall ? "卸载进度" : "安装进度";
        Heading.Text = uninstall ? "管理这一次安装" : "让创作准备就绪";
        Subtitle.Text = uninstall ? "移除 UTvTU 程序，保留属于你的创作。" : "选择安装位置，即可开始歌声编辑、合成与混音。";
        ActionButton.Content = uninstall ? "卸载 UTvTU" : "安装 UTvTU";
        DirBox.TextChanged += (_, _) => UpdateLocation(); UpdateLocation();
        ActionButton.Click += async (_, _) => { if (completed) Finish(); else if (!busy) await ExecuteAsync(); };
        CancelButton.Click += (_, _) => { if (busy) CancelOperation(); else Close(); };
        LogButton.Click += (_, _) => { Directory.CreateDirectory(InstallerCore.LogDirectory); Process.Start(new ProcessStartInfo(InstallerCore.LogDirectory) { UseShellExecute = true }); };
        BrowseButton.Click += async (_, _) => {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 UTvTU 安装文件夹", AllowMultiple = false });
            if (folders.Count > 0) DirBox.Text = folders[0].TryGetLocalPath();
        };
        RetryIntegrationButton.Click += async (_, _) => {
            if (lastOptions == null || lastReceipt == null || busy) return;
            busy = true; RetryIntegrationButton.IsEnabled = false; ActionButton.IsEnabled = false;
            try { await InstallerCore.RetryIntegrationAsync(lastOptions, lastReceipt, legacy); Notice.IsVisible = retainedWarnings.Length > 0; NoticeText.Text = string.Join(Environment.NewLine, retainedWarnings); RetryIntegrationButton.IsVisible = false; ResultDetail.Text = "系统入口设置已完成。"; }
            catch (Exception error) { ShowNotice(error.Message); }
            finally { busy = false; RetryIntegrationButton.IsEnabled = true; ActionButton.IsEnabled = true; if (closeAfterCancel) Close(); }
        };
        Closing += (_, e) => { if (busy) { e.Cancel = true; closeAfterCancel = true; CancelOperation(); } };
        if (preview) { ActionButton.IsEnabled = false; ShowNotice("界面预览 · 此窗口不会安装、卸载或修改用户数据。"); }
        else if (!uninstall && !InstallerCore.HasPayload) { ActionButton.IsEnabled = false; ShowNotice("此程序未包含安装文件，请下载完整的 UTvTU 安装包。"); }
    }
    void UpdateLocation() {
        try {
            string target = SetupPaths.ValidateTarget((DirBox.Text ?? "").Trim());
            var receipt = SetupEngine.ReadReceipt(target);
            DataPathText.Text = "数据位置：" + (receipt?.DataDirectory ?? SetupPaths.UserDataDirectory);
            RemoveDataChoice.Content = receipt?.PortableData == true ? "同时清理此便携版的数据" : "同时清理账户共享数据";
            RemoveDataHint.Text = receipt?.PortableData == true ? "勾选后清理此目录内的工程、设置、缓存与声库；未知文件仍保留。" : "勾选后清理此账户共享的工程、设置、缓存与声库，会影响使用同一数据目录的其他 UTvTU 安装。";
            LocationHint.Text = receipt != null ? $"当前版本 {receipt.Version} · 将保留现有用户数据" : SetupEngine.IsUtvtu(target) ? "检测到旧版 UTvTU，将保留数据并更新程序。" : InstallerCore.NeedsElevation(target) ? "此位置需要 Windows 管理员授权。" : "仅为当前用户安装。";
        } catch (Exception error) { LocationHint.Text = error.Message; }
    }
    internal async Task ExecuteAsync() {
        if (busy || preview) return;
        try {
            lastOptions = InstallerCore.Options((DirBox.Text ?? "").Trim(), DesktopChoice.IsChecked == true, AssociationChoice.IsChecked == true, RemoveDataChoice.IsChecked == true);
            lastReceipt = uninstall ? SetupEngine.ReadReceipt(lastOptions.Directory) : null; legacy = SetupEngine.IsUtvtu(lastOptions.Directory);
            busy = true; completed = false; closeAfterCancel = false; operation = new CancellationTokenSource();
            CancelButton.IsEnabled = true;
            OptionsPage.IsVisible = false; ProgressPage.IsVisible = true; ResultPage.IsVisible = false; Notice.IsVisible = false;
            ActionButton.IsEnabled = false; CancelButton.Content = "取消操作"; StepOptions.Classes.Set("active", false); StepProgress.Classes.Set("active", true);
            Heading.Text = uninstall ? "正在卸载" : "正在准备你的工作站";
            Subtitle.Text = uninstall ? "只移除已登记的程序文件。" : "正在校验安装文件并更新程序。";
            var progress = new Progress<SetupProgress>(p => {
                if (!busy) return;
                Progress.Value = p.Percent; PercentText.Text = p.Percent + "%"; StageText.Text = p.Stage; ProgressDetail.Text = p.Detail;
                if (p.Detail != null) { LogText.Text += p.Stage + " · " + p.Detail + Environment.NewLine; Details.IsVisible = true; }
                Program.WriteLog(p.Stage, p.Detail ?? "");
            });
            var result = await ExecuteOperation(lastOptions, progress, operation.Token);
            if (!uninstall) lastReceipt = SetupEngine.ReadReceipt(lastOptions.Directory);
            completed = true; ProgressPage.IsVisible = false; ResultPage.IsVisible = true;
            StepProgress.Classes.Set("active", false); StepDone.Classes.Set("active", true);
            Heading.Text = uninstall ? "UTvTU 已卸载" : "准备就绪"; ResultTitle.Text = result.Message;
            ResultDetail.Text = uninstall ? lastOptions.RemoveData ? "程序与所选本机用户数据已移除。" : "工程、声库、备份和个人设置已保留。" : "下一段创作，从这里开始。";
            LaunchChoice.IsVisible = !uninstall; ActionButton.Content = "完成";
            CancelButton.IsVisible = false;
            if (result.Warnings.Count > 0) {
                Heading.Text = "操作已完成，请检查保留项"; ResultDetail.Text = "请查看下方未完成或保留的项目。";
                ShowNotice(string.Join(Environment.NewLine, result.Warnings));
                retainedWarnings = result.Warnings.Where(warning => !warning.StartsWith("程序文件操作已完成，但系统入口未全部更新：", StringComparison.Ordinal)).ToArray();
                RetryIntegrationButton.IsVisible = result.IntegrationPending && lastReceipt != null;
            }
        } catch (OperationCanceledException) {
            ResetForRetry("操作已取消", "程序文件已恢复或尚未修改，可以重新开始。");
        } catch (Exception error) {
            Program.WriteLog("失败", error.ToString()); LogText.Text += error + Environment.NewLine; Details.IsVisible = true;
            ResetForRetry("暂时无法完成", error.Message);
        } finally {
            busy = false; operation?.Dispose(); operation = null; ActionButton.IsEnabled = !preview;
            if (closeAfterCancel) Close();
        }
    }
    void ResetForRetry(string heading, string message) {
        Heading.Text = heading; ShowNotice(message); OptionsPage.IsVisible = true; ProgressPage.IsVisible = false; ResultPage.IsVisible = false;
        ActionButton.Content = "重试"; CancelButton.Content = "关闭"; StepProgress.Classes.Set("active", false); StepOptions.Classes.Set("active", true);
    }
    void CancelOperation() { operation?.Cancel(); CancelButton.IsEnabled = false; CancelButton.Content = "正在取消…"; }
    void ShowNotice(string text) { NoticeText.Text = text; Notice.IsVisible = true; }
    void Finish() {
        if (!uninstall && LaunchChoice.IsChecked == true && lastOptions != null) {
            try { Process.Start(new ProcessStartInfo(Path.Combine(lastOptions.Directory, InstallerCore.AppExe)) { UseShellExecute = true, WorkingDirectory = lastOptions.Directory }); }
            catch (Exception error) { ShowNotice("程序已安装，启动失败：" + error.Message); return; }
        }
        Close();
    }
}
