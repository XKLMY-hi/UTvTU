using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using SukiUI.Controls;
using SukiUI.Enums;

namespace OpenUtau.App.Controls;

/// <summary>
/// 窗口基类（2026-09-25：**回归系统原生窗口，不再自绘**）。
///
/// 为什么：所有窗口今后都会内嵌进单窗口（见 `.opencode/plans/ui-rework-decisions.md` A 线），
/// 自绘标题栏 / 自绘边框已无必要，也与系统窗口体验不一致（拖拽/贴靠/投影/圆角/无障碍）。
///
/// 保留：<see cref="SukiWindow"/> 的 Hosts（对话框与 Toast 的挂载点，MainWindow 依赖）
/// 与内容背景样式（主题迁移完成前仍由 Suki 提供）。
/// 交还系统：标题栏、窗口按钮、边框、投影、圆角、窗口动画。
/// 窗口背景由 `Styles/Styles.axaml` 的 <c>Window</c> 样式提供（<c>PlusBrushWindowBackground</c> 主题渐变）。
/// </summary>
public class WindowEx : SukiWindow {
    public WindowEx() {
        ApplyNativeChrome();
        // 内容背景沿用 Suki 渐变（主题迁移未完成前）
        BackgroundStyle = SukiBackgroundStyle.GradientDarker;
        ShowBottomBorder = false;
        RootCornerRadius = new CornerRadius(0);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        // SukiWindow 的模板会为自绘标题栏准备扩展客户区——模板套用后重新断言一次
        ApplyNativeChrome();
        RootCornerRadius = new CornerRadius(0);
    }

    /// <summary>原生窗口装饰：系统标题栏 + 系统边框，关掉 SukiUI 自绘标题栏与透明合成。</summary>
    private void ApplyNativeChrome() {
        IsTitleBarVisible = false;                                  // 关自绘标题栏
        WindowDecorations = WindowDecorations.Full;                 // 系统标题栏 + 边框
        ExtendClientAreaToDecorationsHint = false;                  // 客户区不延伸到系统标题栏
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };  // 透明合成只服务于自绘圆角，已不需要
    }
}
