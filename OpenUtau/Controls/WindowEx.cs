using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OpenUtau.App.Controls;

/// <summary>
/// 窗口基类（2026-09-25：**回归系统原生窗口 + MD3 颜色池背景**）。
///
/// 历史：曾继承 <c>SukiUI.Controls.SukiWindow</c> 以复用它的对话框/通知 Host。
/// 但 SukiWindow 的模板里**自带两层背景**——`SukiBackground`（渐变/着色器/动画）
/// 与一层不透明底（位于模板深处，外部样式改不到）——它们会盖住 <see cref="Window.Background"/>，
/// 导致"控件已经是 MD3、背景还是 Suki"（探针 <c>DumpWindowBackgroundLayers</c> 实证）。
///
/// 现在改为直接继承 <see cref="Window"/>：背景干净地由颜色池 <c>md3.surface</c> 提供。
/// SukiUI 的 <c>SukiDialogHost</c> / <c>SukiToastHost</c> 是普通控件，仍挂在 MainWindow 里，
/// 对话框与通知服务不受影响。
///
/// 交还系统：标题栏、窗口按钮、边框、投影、圆角、窗口动画。
/// </summary>
public class WindowEx : Window {
    public WindowEx() {
        ApplyNativeChrome();
        ApplyMd3Background();
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

    /// <summary>背景改为 MD3 颜色池：一层 md3.surface。</summary>
    private void ApplyMd3Background() {
        this[!BackgroundProperty] = new DynamicResourceExtension("md3.surface");
    }
}
