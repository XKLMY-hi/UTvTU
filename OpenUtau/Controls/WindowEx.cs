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
    /// <summary>Opt-in integrated decorations. Auxiliary and plugin windows retain their existing chrome.</summary>
    public static readonly StyledProperty<bool> UseWorkspaceChromeProperty =
        AvaloniaProperty.Register<WindowEx, bool>(nameof(UseWorkspaceChrome));
    public bool UseWorkspaceChrome {
        get => GetValue(UseWorkspaceChromeProperty);
        set => SetValue(UseWorkspaceChromeProperty, value);
    }

    /// <summary>Detached editors route auxiliary tools here without changing native window ownership.</summary>
    public Window? WorkspaceOwner { get; set; }
    public WindowEx() {
        ApplyWindowChrome();
        ApplyMd3Background();
        Theming.Md3Motion.Register(this);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        // 模板套用后再断言一次（模板可能重置透明合成等属性）
        ApplyWindowChrome();
        ApplyMd3Background();
        UpdateContentMargin();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == UseWorkspaceChromeProperty) ApplyWindowChrome();
        if (change.Property == UseWorkspaceChromeProperty || change.Property == WindowStateProperty) {
            double width = UseWorkspaceChrome && WindowState != WindowState.FullScreen ? 128 : 0;
            Resources["Md3WorkspaceCaptionWidth"] = width;
            Resources["Md3WorkspaceCaptionInset"] = new Thickness(0, 0, width, 0);
        }
        if (change.Property == WindowDecorationMarginProperty || change.Property == UseWorkspaceChromeProperty) UpdateContentMargin();
    }

    private void ApplyWindowChrome() {
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = UseWorkspaceChrome;
        // The page's app bar IS the title bar. Only the frame gets a content inset.
        ExtendClientAreaTitleBarHeightHint = UseWorkspaceChrome ? 0 : -1;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        if (UseWorkspaceChrome) {
            this[!WindowDecorationsThemeProperty] = new DynamicResourceExtension("WorkspaceWindowDecorationsTheme");
        } else {
            ClearValue(WindowDecorationsThemeProperty);
        }
    }

    private void UpdateContentMargin() {
        if (Presenter is { } presenter) presenter.Margin = UseWorkspaceChrome ? WindowDecorationMargin : default;
    }

    /// <summary>背景改为 MD3 颜色池：一层 md3.surface。</summary>
    private void ApplyMd3Background() {
        this[!BackgroundProperty] = new DynamicResourceExtension("md3.surface");
    }
}
