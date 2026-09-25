using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using SukiUI.Controls;
using SukiUI.Enums;

namespace OpenUtau.App.Controls;

/// <summary>
/// 窗口基类（2026-09-25：**回归系统原生窗口，不再自绘**）。
///
/// 为什么：所有窗口今后都会内嵌进单窗口（见 `.opencode/plans/ui-rework-decisions.md` A 线），
/// 自绘标题栏 / 自绘边框已无必要，也与系统窗口体验不一致（拖拽/贴靠/投影/圆角/无障碍）。
///
/// 保留：<see cref="SukiWindow"/> 的 Hosts（对话框与 Toast 的挂载点，MainWindow 依赖）。
/// 交还系统：标题栏、窗口按钮、边框、投影、圆角、窗口动画。
/// **背景（2026-09-25 用户定）：全面舍弃 SukiUI 的背景**——关掉渐变/着色器/动画，改由颜色池
/// 的 <c>md3.surface</c> 提供（<c>BackgroundStyle = Flat</c> 只是不给 Suki 画东西）。
/// </summary>
public class WindowEx : SukiWindow {
    public WindowEx() {
        ApplyNativeChrome();
        ApplyMd3Background();
        ShowBottomBorder = false;
        RootCornerRadius = new CornerRadius(0);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        // SukiWindow 的模板会为自绘标题栏准备扩展客户区——模板套用后重新断言一次
        ApplyNativeChrome();
        ApplyMd3Background();
        RootCornerRadius = new CornerRadius(0);
    }

    /// <summary>原生窗口装饰：系统标题栏 + 系统边框，关掉 SukiUI 自绘标题栏与透明合成。</summary>
    private void ApplyNativeChrome() {
        IsTitleBarVisible = false;                                  // 关自绘标题栏
        WindowDecorations = WindowDecorations.Full;                 // 系统标题栏 + 边框
        ExtendClientAreaToDecorationsHint = false;                  // 客户区不延伸到系统标题栏
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };  // 透明合成只服务于自绘圆角，已不需要
    }

    /// <summary>背景改为 MD3 颜色池：关掉 Suki 的渐变/着色器/动画，只留一层 md3.surface。</summary>
    private void ApplyMd3Background() {
        BackgroundStyle = SukiBackgroundStyle.Flat;   // 不画 Suki 渐变/Shader
        BackgroundShaderFile = null;
        BackgroundShaderCode = null;
        BackgroundAnimationEnabled = false;
        BackgroundTransitionsEnabled = false;
        BackgroundForceSoftwareRendering = false;
        this[!BackgroundProperty] = new DynamicResourceExtension("md3.surface");
    }
}
