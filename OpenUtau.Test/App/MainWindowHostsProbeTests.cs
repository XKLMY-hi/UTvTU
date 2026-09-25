using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using OpenUtau.App.Controls;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using Xunit;

namespace OpenUtau.App;

/// <summary>
/// 契约（2026-09-25 变更）：SukiDialog / SukiToast 的挂载点不再走 <c>SukiWindow.Hosts</c>
/// （WindowEx 已改为继承原生 <c>Window</c>，以摆脱 Suki 模板里会盖住 MD3 背景的两层背景），
/// 改为把两个 Host 直接放进 MainWindow 根 Grid 的最上层（ZIndex 1100 &gt; 模态覆盖层 1000）。
/// </summary>
public class MainWindowHostsProbeTests {
    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

    [AvaloniaFact]
    public void Hosts_AcceptDialogAndToastWithManagers() {
        var win = new WindowEx();
        win.Show();
        try {
            var dialogHost = new SukiDialogHost { Manager = new SukiDialogManager() };
            var toastHost = new SukiToastHost { Manager = new SukiToastManager() };
            // 作为普通子元素挂载（不再依赖 SukiWindow.Hosts）
            var grid = new Avalonia.Controls.Grid();
            grid.Children.Add(dialogHost);
            grid.Children.Add(toastHost);
            win.Content = grid;

            Assert.Equal(2, grid.Children.Count);
            Assert.IsType<SukiDialogManager>(dialogHost.Manager);
            Assert.IsType<SukiToastManager>(toastHost.Manager);
        } finally {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_DeclaresHostsAboveModalLayer() {
        string xaml = MainWindowXaml();
        Assert.DoesNotContain("WindowEx.Hosts", xaml);
        Assert.Contains("suki:SukiDialogHost", xaml);
        Assert.Contains("suki:SukiToastHost", xaml);
        // 两个 Host 的 ZIndex 必须高于模态覆盖层（1000），否则对话框会被遮住
        foreach (Match m in Regex.Matches(xaml, @"suki:Suki(?:Dialog|Toast)Host[^/]*ZIndex=""(\d+)""")) {
            Assert.True(int.Parse(m.Groups[1].Value) > 1000, "Host ZIndex 必须大于 1000");
        }
    }
}
