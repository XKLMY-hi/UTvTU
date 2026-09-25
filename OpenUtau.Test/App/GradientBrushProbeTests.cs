using System.Linq;
using Xunit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using OpenUtau.App;

namespace OpenUtau.App;

/// <summary>
/// 探针：PlusBrushWindowBackground（LinearGradientBrush）的 GradientStop DynamicResource
/// 是否在 App 资源环境中正常解析。用户反馈 B1（SukiWindow 接管窗口背景）后
/// 音符属性面板背景变成纯色——怀疑渐变停靠点颜色键未解析（全回落默认色）。
/// </summary>
[Collection("Theme")]   // 这些用例会改全局主题/颜色池，串行执行避免互相污染
public class GradientBrushProbeTests {
    [AvaloniaFact]
    public void WindowGradientBrush_Exists() {
        Assert.True(
            Application.Current!.TryGetResource("PlusBrushWindowBackground", ThemeVariant.Default, out var res),
            "PlusBrushWindowBackground 资源键应可解析");
        Assert.IsType<LinearGradientBrush>(res);
    }

    [AvaloniaFact]
    public void DarkVariant_RendersStops() {
        // 实机默认深色主题——验证 Dark 变体下画刷渐变正常
        // 走主题唯一入口（会同步 MD3 颜色池，渐变停靠点接的是池子里的角色色）
        ThemeManager.Apply("Dark");
        Assert.True(
            Application.Current.TryGetResource("PlusBrushWindowBackground", ThemeVariant.Dark, out var res),
            "Dark 变体下 PlusBrushWindowBackground 应可解析");
        var brush = (LinearGradientBrush)res!;
        var border = new Border { Background = brush };
        var win = new Window {
            Content = new Grid { Children = { border } },
            Width = 300,
            Height = 300,
        };
        win.Show();
        try {
            var colors = brush.GradientStops.Select(s => s.Color).Distinct().ToList();
            Assert.True(colors.Count >= 2,
                $"Dark 变体下渐变停靠点颜色全相同（{colors.FirstOrDefault()}），渲染为纯色");
            // 应解析出深色系（不是全黑/全白默认值）
            Assert.True(colors.All(c => c.R < 0x90),
                $"Dark 变体下应解析深色（当前 {colors.FirstOrDefault()}）");
        } finally {
            win.Close();
        }
    }

    [AvaloniaFact]
    public void WindowGradientBrush_RendersStops() {
        Assert.True(
            Application.Current!.TryGetResource("PlusBrushWindowBackground", ThemeVariant.Default, out var res),
            "PlusBrushWindowBackground 资源键应可解析");
        var brush = (LinearGradientBrush)res!;
        Assert.Equal(3, brush.GradientStops.Count);

        // 挂载到视觉树渲染，触发 DynamicResource 解析
        var border = new Border { Background = brush };
        var win = new Window {
            Content = new Grid { Children = { border } },
            Width = 300,
            Height = 300,
        };
        win.Show();
        try {
            var colors = brush.GradientStops.Select(s => s.Color).Distinct().ToList();
            Assert.True(colors.Count >= 2,
                $"渐变停靠点颜色全相同（{colors.FirstOrDefault()}），渲染为纯色");
        } finally {
            win.Close();
        }
    }
}
