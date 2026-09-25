using System.Linq;
using Xunit;
using Avalonia.Headless.XUnit;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;

namespace OpenUtau.App;

/// <summary>
/// 窗口基类契约（2026-09-25 变更后）：应用窗口走 <see cref="WindowEx"/>（原生 Window + 颜色池背景），
/// **不再继承 SukiUI 的 SukiWindow** —— 其模板自带两层背景（SukiBackground + 不透明底）
/// 会盖住 Window.Background，也就是"控件已 MD3、背景还是 Suki"的根因。
/// </summary>
public class WindowSukiProbeTests {
    [AvaloniaFact]
    public void WindowEx_IsNativeWindow_NotSukiWindow() {
        var win = new LoadingWindow();
        Assert.IsAssignableFrom<WindowEx>(win);
        Assert.False(win.GetType().GetProperties().Any(p => p.DeclaringType?.Namespace?.StartsWith("SukiUI") == true),
            "窗口不应再依赖 SukiUI");
        Assert.IsAssignableFrom<Avalonia.Controls.Window>(win);
    }

    [AvaloniaFact]
    public void LoadingWindow_Instantiates() {
        var win = new LoadingWindow();
        Assert.NotNull(win);
    }
}
