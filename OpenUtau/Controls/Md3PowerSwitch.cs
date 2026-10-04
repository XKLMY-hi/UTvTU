using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;

// ═══════════════════════════════════════════════════════════════════════════════
//  机架控件族（fx-rack）
//
//  背景：设计交付稿已作废，控件语言由我们自定。唯一基准是颜色池
//  （Theming/Md3ColorPool 的 md3.* / md3.color.* 资源键）+ 既有卡片语言
//  （PreferencesView 的 Border.card：圆角 16 / surface-container / outline-variant）。
//
//  本族五个控件（Md3PowerSwitch / Md3SegmentedControl / Md3NumericReadout /
//  FxModuleCard / FxRackPanel）共用一条最小铁律：
//    · 颜色**只**来自颜色池资源键（DynamicResource，无硬编码色值）；
//    · 模板在 C# 里用 FuncControlTemplate 直接构筑（不经 ControlTheme 查找——
//      本仓库已实证：隐式主题会被 FluentTheme 抢先命中、跨字典 StaticResource 不可靠）；
//    · 交互状态由控件自身事件驱动（不经选择器引擎），`no-motion` 时不动画；
//    · 键盘可达（Tab 聚焦 + 方向键/空格），AutomationProperties 由使用者挂（陈列室已示范）。
//
//  本文件同时承载本族共享的**最小公共类型**（Md3RackVisual 枚举 + Md3RackKit 工具箱）：
//  为避免新增超出任务书写入范围的文件，它们随族内第一个控件一起声明。
// ═══════════════════════════════════════════════════════════════════════════════

namespace OpenUtau.App.Controls;

/// <summary>
/// 机架控件族的视觉状态。常态为 <c>null</c>（跟随真实交互）；
/// 赋值即**冻结**为该状态——陈列室用它并列展示悬停/按下（无需真实鼠标），
/// 契约测试也用它断言各状态配色。
/// </summary>
public enum Md3RackVisual {
    /// <summary>默认静态（无悬停、无焦点）。</summary>
    Default,
    /// <summary>悬停：MD3 状态层 8%。</summary>
    Hover,
    /// <summary>按下：MD3 状态层 12%。</summary>
    Pressed,
    /// <summary>键盘焦点：primary 焦点环。</summary>
    Focus,
    /// <summary>禁用：38% 不透明度（MD3 disabled 规格）。</summary>
    Disabled,
    /// <summary>所属模块旁通：压暗 + 去强调（保留形状，不变成"禁用"）。</summary>
    Bypassed,
}

/// <summary>
/// 机架控件族共享的最小工具箱：颜色池绑定、MD3 状态层、动效开关。
/// 仅本族使用——不跨族外扩，避免与样式层（fx-ctl）抢地盘。
/// </summary>
internal static class Md3RackKit {
    // ── 几何令牌（与 PreferencesView 的卡片语言同源：卡片 16 / 行 12 / 胶囊 999） ──
    internal const double RadiusPill = 999;
    internal const double RadiusCard = 16;
    internal const double RadiusRow = 12;
    internal const double RadiusField = 10;

    // ── 动效令牌（短 120ms / 中 200ms，与 Md3Transitions 的 0.15/0.2s 同量级） ──
    internal static readonly TimeSpan FastMotion = TimeSpan.FromMilliseconds(120);
    internal static readonly TimeSpan BaseMotion = TimeSpan.FromMilliseconds(200);

    /// <summary>颜色池**画刷**键（如 md3.surface-container-low）。</summary>
    internal static string BrushKey(Md3Role role) => ColorPool.Key(role);

    /// <summary>
    /// 把颜色池画刷键绑到部件属性上。用 DynamicResource：深浅色/配色切换自动跟随，
    /// 且与项目既有写法一致（见 WindowEx.ApplyMd3Background、MainWindow 的 ScreenTitle）。
    /// </summary>
    internal static void Paint(AvaloniaObject target, AvaloniaProperty property, string poolKey) {
        target[!property] = new DynamicResourceExtension(poolKey);
    }

    /// <summary>文本走字符串键（DynamicResource ⇒ 运行时切换语言即时生效）。</summary>
    internal static void PaintText(TextBlock target, string stringKey) {
        target[!TextBlock.TextProperty] = new DynamicResourceExtension(stringKey);
    }

    /// <summary>MD3 状态层：叠一层低不透明度画刷（0 即隐藏，不动画）。</summary>
    internal static void StateLayer(AvaloniaObject? layer, AvaloniaProperty fillProperty, bool visible, string poolKey, double opacity) {
        if (layer == null) {
            return;
        }
        if (layer is Visual visual) {
            visual.IsVisible = visible && opacity > 0;
        }
        if (!visible || opacity <= 0) {
            return;
        }
        Paint(layer, fillProperty, poolKey);
        if (layer is Visual v2) {
            v2.Opacity = opacity;
        }
    }

    /// <summary>
    /// 动效开关：应用级「减少动效」偏好，或祖先窗口带 <c>no-motion</c> 类时不动画。
    /// 与 Styles/Md3Transitions.axaml 的约定一致（那边是把 Transitions 置空）。
    /// </summary>
    internal static bool MotionEnabled(StyledElement element) {
        if (Core.Util.Preferences.Default.ReduceMotion) {
            return false;
        }
        for (Visual? v = element as Visual; v != null; v = v.GetVisualParent()) {
            if (v.Classes.Contains("no-motion")) {
                return false;
            }
        }
        return true;
    }

    /// <summary>按动效开关构造过渡集合（不动画时返回 null，即清空 Transitions）。</summary>
    internal static Transitions? Motion(StyledElement owner, params ITransition[] items) {
        if (items.Length == 0 || !MotionEnabled(owner)) {
            return null;
        }
        var transitions = new Transitions();
        foreach (ITransition item in items) {
            transitions.Add(item);
        }
        return transitions;
    }

    /// <summary>维护伪类（:checked / :pressed …）：样式作者与自动化可用，视觉由控件自绘。</summary>
    internal static void SetPseudo(StyledElement element, string name, bool on) {
        ((IPseudoClasses)element.Classes).Set(name, on);
    }
}

/// <summary>
/// 机架式电源开关（MD3 语言 / 自绘模板 / 键盘可达 / IsChecked 风格 API）。
///
/// 设计要点（与 Fluent 胶囊 ToggleSwitch 明确区分——机架面板要的是"点亮"的电源钮）：
///   · 圆形按钮 28×28（部件名 PART_Circle：避开面板容器的 PART_Body，同名会造成跨控件查找歧义），外圈留 3px 焦点环位（控件总尺寸 34×34，不裁剪、不溢出布局）；
///   · 关：底 surface-container-highest + 1px outline-variant 描边 + 电源符号 outline；
///   · 开：底 primary + 符号 on-primary（点亮）；
///   · 悬停/按下：MD3 状态层（on-surface / on-primary，8% / 12%）+ 描边升到 outline；
///   · 焦点：2px primary 外环（Tab 可见）；禁用：38% 不透明度；旁通：压暗去强调；
///   · 键位：Tab 聚焦，Space/Enter 切换，← 置关 / → 置开（开关惯例，便于盲操）。
///
/// 用到的颜色池键：md3.primary、md3.on-primary、md3.on-surface、md3.on-surface-variant、
///   md3.surface-container{,-high,-highest}、md3.outline、md3.outline-variant。
/// 后续接线点：作为 FxModuleCard 的电源钮（IsPowered 双向）与 FxRackPanel 行内的模块开关；
///   真实写入应经 DocManager.ExecuteCmd（本轮只读设计稿，不写模型）。
/// </summary>
public class Md3PowerSwitch : TemplatedControl {
    /// <summary>Material "power_settings_new" 电源符号（24×24 视图框，纯几何，无颜色）。</summary>
    private const string PowerGlyph =
        "M13,3h-2v10h2V3z M17.83,5.17l-1.42,1.42C17.99,7.86,19,9.81,19,12c0,3.87-3.13,7-7,7s-7-3.13-7-7" +
        "c0-2.19,1.01-4.14,2.58-5.42L6.17,5.17C4.23,6.82,3,9.26,3,12c0,4.97,4.03,9,9,9s9-4.03,9-9" +
        "C21,9.26,19.77,6.82,17.83,5.17z";

    private const double BodySize = 28;
    private const double RingSize = 34;
    private const double GlyphSize = 16;

    /// <summary>开/关状态（默认双向绑定）。</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<Md3PowerSwitch, bool>(
            nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>冻结的视觉状态（陈列室/契约测试用；null = 跟随真实交互）。</summary>
    public static readonly StyledProperty<Md3RackVisual?> ForcedVisualProperty =
        AvaloniaProperty.Register<Md3PowerSwitch, Md3RackVisual?>(nameof(ForcedVisual));

    private Ellipse? focusRing;
    private Border? body;
    private Ellipse? stateLayer;
    private Path? glyph;

    private bool hover;
    private bool pressed;
    private bool focused;

    /// <summary>状态切换事件（程序化切换也会触发）。</summary>
    public event EventHandler<bool>? Toggled;

    public Md3PowerSwitch() {
        Focusable = true;
        IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        // 模板在代码里直接构筑：不经 ControlTheme 查找（避开隐式主题被 Fluent 抢先命中）
        Template = new FuncControlTemplate<Md3PowerSwitch>(BuildTemplate);
    }

    /// <summary>开/关。</summary>
    public bool IsChecked {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>强制视觉状态（null = 正常交互）。</summary>
    public Md3RackVisual? ForcedVisual {
        get => GetValue(ForcedVisualProperty);
        set => SetValue(ForcedVisualProperty, value);
    }

    /// <summary>程序化切换（键盘、自动化与测试都走这里）。</summary>
    public void Toggle() => IsChecked = !IsChecked;

    private Control BuildTemplate(Md3PowerSwitch control, INameScope scope) {
        var ring = new Ellipse {
            Name = "PART_Focus",
            Width = RingSize,
            Height = RingSize,
            StrokeThickness = 2,
            IsVisible = false,
        };
        var layer = new Ellipse { Name = "PART_StateLayer", IsVisible = false };
        var symbol = new Path {
            Name = "PART_Glyph",
            Data = Geometry.Parse(PowerGlyph),
            Width = GlyphSize,
            Height = GlyphSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Border {
            Name = "PART_Circle",
            Width = BodySize,
            Height = BodySize,
            CornerRadius = new CornerRadius(Md3RackKit.RadiusPill),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Panel { Children = { layer, symbol } },
        };
        focusRing = ring;
        stateLayer = layer;
        glyph = symbol;
        body = circle;
        return new Panel {
            Name = "PART_Root",
            Width = RingSize,
            Height = RingSize,
            Children = { ring, circle },
        };
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        ApplyMotion();
        ApplyState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        // 挂到带 .no-motion 的窗口后重新裁决动效（模板可能先于挂载构筑）
        ApplyMotion();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty) {
            bool value = change.GetNewValue<bool>();
            Md3RackKit.SetPseudo(this, ":checked", value);
            ApplyState();
            Toggled?.Invoke(this, value);
        } else if (change.Property == ForcedVisualProperty || change.Property == IsEffectivelyEnabledProperty) {
            ApplyState();
        }
    }

    private void ApplyMotion() {
        if (body != null) {
            body.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = Border.BackgroundProperty, Duration = Md3RackKit.FastMotion },
                new BrushTransition { Property = Border.BorderBrushProperty, Duration = Md3RackKit.FastMotion });
        }
        if (glyph != null) {
            glyph.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = Shape.FillProperty, Duration = Md3RackKit.FastMotion });
        }
        if (stateLayer != null) {
            stateLayer.Transitions = Md3RackKit.Motion(this,
                new DoubleTransition { Property = OpacityProperty, Duration = Md3RackKit.FastMotion });
        }
        if (focusRing != null) {
            focusRing.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = Shape.StrokeProperty, Duration = Md3RackKit.FastMotion });
        }
    }

    /// <summary>按当前（或冻结的）状态重刷配色。全部经颜色池资源键，无硬编码色值。</summary>
    private void ApplyState() {
        if (body == null || focusRing == null || glyph == null) {
            return;
        }
        Md3RackVisual visual = ForcedVisual ?? (IsEffectivelyEnabled
            ? (pressed ? Md3RackVisual.Pressed
                : hover ? Md3RackVisual.Hover
                : focused ? Md3RackVisual.Focus
                : Md3RackVisual.Default)
            : Md3RackVisual.Disabled);

        bool dim = visual is Md3RackVisual.Disabled or Md3RackVisual.Bypassed;
        bool on = IsChecked && !dim;

        string bodyBg, bodyBorder, glyphBrush;
        if (on) {
            bodyBg = "md3.primary";
            bodyBorder = "md3.primary";
            glyphBrush = "md3.on-primary";
        } else if (dim) {
            bodyBg = "md3.surface-container";
            bodyBorder = "md3.outline-variant";
            glyphBrush = "md3.outline";
        } else {
            // 未点亮：悬停/按下时描边从 outline-variant 升到 outline、符号升到 on-surface-variant
            bool active = visual != Md3RackVisual.Default;
            bodyBg = visual == Md3RackVisual.Pressed ? "md3.surface-container-high" : "md3.surface-container-highest";
            bodyBorder = active ? "md3.outline" : "md3.outline-variant";
            glyphBrush = active ? "md3.on-surface-variant" : "md3.outline";
        }

        Md3RackKit.Paint(body, Border.BackgroundProperty, bodyBg);
        Md3RackKit.Paint(body, Border.BorderBrushProperty, bodyBorder);
        Md3RackKit.Paint(glyph, Shape.FillProperty, glyphBrush);
        Md3RackKit.Paint(focusRing, Shape.StrokeProperty, "md3.primary");

        double layer = visual switch {
            Md3RackVisual.Hover => 0.08,
            Md3RackVisual.Pressed => 0.12,
            _ => 0.0,
        };
        Md3RackKit.StateLayer(stateLayer, Shape.FillProperty, layer > 0, on ? "md3.on-primary" : "md3.on-surface", layer);

        Opacity = visual switch {
            Md3RackVisual.Disabled => 0.38,   // MD3 disabled 规格
            Md3RackVisual.Bypassed => 0.65,
            _ => 1.0,
        };
        focusRing.IsVisible = visual == Md3RackVisual.Focus;
    }

    protected override void OnPointerEntered(PointerEventArgs e) {
        base.OnPointerEntered(e);
        hover = true;
        ApplyState();
    }

    protected override void OnPointerExited(PointerEventArgs e) {
        base.OnPointerExited(e);
        hover = false;
        pressed = false;
        Md3RackKit.SetPseudo(this, ":pressed", false);
        ApplyState();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
            return;
        }
        pressed = true;
        Md3RackKit.SetPseudo(this, ":pressed", true);
        ApplyState();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) {
        base.OnPointerReleased(e);
        if (!pressed) {
            return;
        }
        bool inside = hover;
        pressed = false;
        Md3RackKit.SetPseudo(this, ":pressed", false);
        if (inside && IsEffectivelyEnabled) {
            Toggle();
        }
        ApplyState();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) {
        base.OnGotFocus(e);
        focused = true;
        Md3RackKit.SetPseudo(this, ":focus", true);
        ApplyState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e) {
        base.OnLostFocus(e);
        focused = false;
        Md3RackKit.SetPseudo(this, ":focus", false);
        ApplyState();
    }

    /// <summary>键盘：Space/Enter 切换；← 置关 / → 置开（开关惯例，便于盲操）。</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled) {
            return;
        }
        switch (e.Key) {
            case Key.Space:
            case Key.Enter:
                Toggle();
                e.Handled = true;
                break;
            case Key.Left:
                IsChecked = false;
                e.Handled = true;
                break;
            case Key.Right:
                IsChecked = true;
                e.Handled = true;
                break;
        }
    }
}
