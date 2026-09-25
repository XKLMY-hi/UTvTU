using System.IO;
using Avalonia.Headless.XUnit;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.App;

/// <summary>
/// 契约（2026-09-25）：MainWindow 不声明任何第三方对话框/通知 Host；
/// 对话框由自研 <see cref="MessageBox"/>（WindowEx 模态窗口）承担。
/// </summary>
public class MainWindowHostsProbeTests {
    private static string MainWindowXaml() =>
        File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

    [AvaloniaFact]
    public void MainWindow_DeclaresNoSukiHosts() {
        string xaml = MainWindowXaml();
        Assert.DoesNotContain("suki:", xaml);
        Assert.DoesNotContain("SukiDialogHost", xaml);
        Assert.DoesNotContain("SukiToastHost", xaml);
    }

    [AvaloniaFact]
    public void MessageBox_UsesOwnWindow() {
        // 自研模态窗口：WindowEx（原生 Window）承载，不再经第三方门面
        var msgbox = MessageBox.ShowModal(new WindowEx { Width = 200, Height = 100 }, "text", "title");
        Assert.NotNull(msgbox);
    }
}
