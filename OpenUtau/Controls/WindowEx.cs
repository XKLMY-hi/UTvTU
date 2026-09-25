using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OpenUtau.App.Controls;

/// <summary>
/// 窗口基类（2026-09-25：**回归系统原生窗口 + MD3 颜色池背景**）。
///
/// 背景由颜色池 <c>md3.surface</c> 提供（曾经的三方窗口基类自带两层背景，会盖住 Window.Background，
/// 已随第三方依赖一并移除）。对话框改由自研 <see cref="OpenUtau.App.Views.MessageBox"/> 承担。
///
/// 交还系统：标题栏、窗口按钮、边框、投影、圆角、窗口动画。
/// </summary>
public class WindowEx : Window {
    public WindowEx() {
        ApplyNativeChrome();
        ApplyMd3Background();
        ApplyReducedMotion();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        // 模板套用后再断言一次（模板可能重置透明合成等属性）
        ApplyNativeChrome();
        ApplyMd3Background();
    }

    /// <summary>原生窗口装饰：系统标题栏 + 系统边框。</summary>
    private void ApplyNativeChrome() {
        WindowDecorations = WindowDecorations.Full;                 // 系统标题栏 + 边框
        ExtendClientAreaToDecorationsHint = false;                  // 客户区不延伸到系统标题栏
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };  // 不再需要透明合成（无自绘圆角）
    }

    /// <summary>「减少动效」偏好 → 窗口加 .no-motion 类，样式层把过渡置空（Transitions = null）。</summary>
    private void ApplyReducedMotion() {
        Classes.Set("no-motion", Core.Util.Preferences.Default.ReduceMotion);
    }

    /// <summary>背景改为 MD3 颜色池：一层 md3.surface。</summary>
    private void ApplyMd3Background() {
        this[!BackgroundProperty] = new DynamicResourceExtension("md3.surface");
    }
}
