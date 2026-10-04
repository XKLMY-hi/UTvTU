using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.Core.Theming;

namespace OpenUtau.App.Controls;

/// <summary>
/// 效果模块面板容器（标题 + 电源开关 + 内容区 + 旁通态视觉）。
///
/// 存在理由：三面板机架（EQ/压缩/混响）现在每块都是"标题行 + 开关 + 一组控件"的
/// 手工重复；把它收成一个容器后，"模块"这个概念在视觉与代码上都只有一处定义——
/// 机架列表（<see cref="FxRackPanel"/>）与详情面板能用同一个卡片语言串联。
///
/// 设计要点：
///   · 卡片：圆角 16、底色 surface-container、1px outline-variant（沿用 PreferencesView 的卡片语言）；
///   · 标题行：[强调色条 3×28] [标题 15px Medium / 副标题 11px] … [状态角标] [电源开关]；
///   · 强调色条按模块角色取池色（EQ=Primary / 压缩=Tertiary / 混响=Secondary…），旁通时褪色；
///     刻意用**实色**：代码里造的 GradientStop 没有资源宿主，DynamicResource 停靠点会解析为透明
///     （md3.color.* 这类颜色键只在 XAML/资源字典里有宿主，用法示范见 ControlGalleryWindow.axaml）；
///   · 选中：底色升 surface-container-high、描边 primary；悬停：描边 outline；
///   · 旁通（IsBypassed 或电源关）：底色降到 surface-container-low、色条褪成 outline-variant、
///     内容区 50% 不透明度 + 状态角标（旁通/关）——形状保留，一眼能看出"还在链上但不过声"；
///   · 禁用：50% 不透明度（容器类刻意比 MD3 控件的 38% 亮一档，否则内容完全不可读）。
///
/// 用到的颜色池键：md3.surface-container{,-low,-high,-highest}、md3.outline{,-variant}、md3.primary、
///   md3.on-surface{,-variant}，以及 <see cref="Md3RackKit.BrushKey"/>（Accent 角色 → md3.{primary,
///   secondary,tertiary,…}）。
/// 后续接线点：IsPowered 接 UTrack.MixFx.{Eq,Comp,Reverb}Enabled 的双向绑定、
///   IsBypassed 接总开关 Enabled=false、PowerToggled 经 DocManager.ExecuteCmd 落库（本轮只读不写）。
/// </summary>
public class FxModuleCard : TemplatedControl {
    /// <summary>标题（已本地化文本）。</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<FxModuleCard, string?>(nameof(Title));

    /// <summary>副标题（模块类型/预设名）。</summary>
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<FxModuleCard, string?>(nameof(Subtitle));

    /// <summary>内容区。</summary>
    public static readonly StyledProperty<object?> CardContentProperty =
        AvaloniaProperty.Register<FxModuleCard, object?>(nameof(CardContent));

    /// <summary>模块电源（默认双向；对应 UTrack.MixFx 的模块开关）。</summary>
    public static readonly StyledProperty<bool> IsPoweredProperty =
        AvaloniaProperty.Register<FxModuleCard, bool>(
            nameof(IsPowered), true, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>旁通（总开关关 / 模块被旁通时置位；与电源开关叠加成"不过声"视觉）。</summary>
    public static readonly StyledProperty<bool> IsBypassedProperty =
        AvaloniaProperty.Register<FxModuleCard, bool>(nameof(IsBypassed));

    /// <summary>选中（机架里当前编辑的模块）。</summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<FxModuleCard, bool>(nameof(IsSelected));

    /// <summary>强调色角色（默认 Tertiary：机架里 EQ 的角色色）。</summary>
    public static readonly StyledProperty<Md3Role> AccentProperty =
        AvaloniaProperty.Register<FxModuleCard, Md3Role>(nameof(Accent), Md3Role.Tertiary);

    /// <summary>是否显示电源开关（只读展示时可关）。</summary>
    public static readonly StyledProperty<bool> ShowPowerSwitchProperty =
        AvaloniaProperty.Register<FxModuleCard, bool>(nameof(ShowPowerSwitch), true);

    /// <summary>冻结的视觉状态（陈列室/契约测试用；null = 跟随真实交互）。</summary>
    public static readonly StyledProperty<Md3RackVisual?> ForcedVisualProperty =
        AvaloniaProperty.Register<FxModuleCard, Md3RackVisual?>(nameof(ForcedVisual));

    /// <summary>电源开关切换（值 = 新状态）。</summary>
    public event EventHandler<bool>? PowerToggled;

    private Border? root;
    private Border? accent;
    private TextBlock? titleText;
    private TextBlock? subtitleText;
    private Border? stateChip;
    private TextBlock? stateText;
    private Md3PowerSwitch? power;
    private Border? divider;
    private Border? body;
    private ContentPresenter? presenter;

    private bool hover;

    public FxModuleCard() {
        Template = new FuncControlTemplate<FxModuleCard>(BuildTemplate);
    }

    /// <summary>标题。</summary>
    public string? Title {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>副标题。</summary>
    public string? Subtitle {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>内容区。</summary>
    public object? CardContent {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    /// <summary>模块电源。</summary>
    public bool IsPowered {
        get => GetValue(IsPoweredProperty);
        set => SetValue(IsPoweredProperty, value);
    }

    /// <summary>旁通。</summary>
    public bool IsBypassed {
        get => GetValue(IsBypassedProperty);
        set => SetValue(IsBypassedProperty, value);
    }

    /// <summary>选中。</summary>
    public bool IsSelected {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>强调色角色。</summary>
    public Md3Role Accent {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>是否显示电源开关。</summary>
    public bool ShowPowerSwitch {
        get => GetValue(ShowPowerSwitchProperty);
        set => SetValue(ShowPowerSwitchProperty, value);
    }

    /// <summary>强制视觉状态（null = 正常交互）。</summary>
    public Md3RackVisual? ForcedVisual {
        get => GetValue(ForcedVisualProperty);
        set => SetValue(ForcedVisualProperty, value);
    }

    /// <summary>是否处于"不过声"视觉（旁通或电源关）。</summary>
    public bool IsDimmed => IsBypassed || !IsPowered || ForcedVisual == Md3RackVisual.Bypassed;

    private Control BuildTemplate(FxModuleCard control, INameScope scope) {
        // 强调色条：实色（颜色在 ApplyState 里按角色刷）。**不用代码造渐变**——
        // GradientStop 不是 Visual，没有资源宿主，DynamicResource 停靠点解析不到（实测为透明）。
        var accentBar = new Border {
            Name = "PART_Accent",
            Width = 3,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 0),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new TextBlock { Name = "PART_Title", FontSize = 15, FontWeight = FontWeight.Medium };
        var subtitle = new TextBlock { Name = "PART_Subtitle", FontSize = 11, IsVisible = false };
        var headerText = new StackPanel {
            Name = "PART_HeaderText",
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, subtitle },
        };

        var stateText = new TextBlock { Name = "PART_StateText", FontSize = 10, FontWeight = FontWeight.Medium };
        var chip = new Border {
            Name = "PART_StateChip",
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Child = stateText,
        };

        var powerSwitch = new Md3PowerSwitch { Name = "PART_Power" };
        powerSwitch.Toggled += (_, value) => {
            IsPowered = value;
            PowerToggled?.Invoke(this, value);
        };

        var header = new Grid {
            Name = "PART_Header",
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            Margin = new Thickness(12, 10),
        };
        header.Children.Add(accentBar);
        Grid.SetColumn(accentBar, 0);
        header.Children.Add(headerText);
        Grid.SetColumn(headerText, 1);
        header.Children.Add(chip);
        Grid.SetColumn(chip, 2);
        header.Children.Add(powerSwitch);
        Grid.SetColumn(powerSwitch, 3);

        var divider = new Border { Name = "PART_Divider", Height = 1 };
        var content = new ContentPresenter { Name = "PART_ContentPresenter" };
        var body = new Border {
            Name = "PART_Body",
            Padding = new Thickness(12, 12, 12, 14),
            Child = content,
        };
        var stack = new StackPanel { Children = { header, divider, body } };

        var card = new Border {
            Name = "PART_Root",
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusCard),
            Child = stack,
        };

        root = card;
        accent = accentBar;
        titleText = title;
        subtitleText = subtitle;
        stateChip = chip;
        this.stateText = stateText;
        power = powerSwitch;
        this.divider = divider;
        this.body = body;
        presenter = content;
        return card;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        ApplyMotion();
        ApplyState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty) {
            AutomationProperties.SetName(this, Title ?? string.Empty);
        }
        if (change.Property == TitleProperty || change.Property == SubtitleProperty
            || change.Property == CardContentProperty || change.Property == IsPoweredProperty
            || change.Property == IsBypassedProperty || change.Property == IsSelectedProperty
            || change.Property == AccentProperty || change.Property == ShowPowerSwitchProperty
            || change.Property == ForcedVisualProperty || change.Property == IsEffectivelyEnabledProperty) {
            ApplyState();
        }
    }

    private void ApplyMotion() {
        if (root == null) {
            return;
        }
        root.Transitions = Md3RackKit.Motion(this,
            new BrushTransition { Property = Border.BackgroundProperty, Duration = Md3RackKit.BaseMotion },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = Md3RackKit.BaseMotion });
        if (body != null) {
            body.Transitions = Md3RackKit.Motion(this,
                new DoubleTransition { Property = OpacityProperty, Duration = Md3RackKit.BaseMotion });
        }
    }

    private void ApplyState() {
        if (root == null || accent == null || titleText == null || subtitleText == null
            || stateChip == null || stateText == null || power == null || divider == null
            || body == null || presenter == null) {
            return;
        }
        bool enabled = IsEffectivelyEnabled;
        Md3RackVisual visual = ForcedVisual ?? (enabled
            ? (hover ? Md3RackVisual.Hover : Md3RackVisual.Default)
            : Md3RackVisual.Disabled);
        bool dim = IsDimmed || visual is Md3RackVisual.Disabled or Md3RackVisual.Bypassed;

        string bg, border;
        if (IsSelected && enabled && !dim) {
            bg = "md3.surface-container-high";
            border = "md3.primary";
        } else if (dim) {
            bg = "md3.surface-container-low";
            border = "md3.outline-variant";
        } else if (visual == Md3RackVisual.Hover) {
            bg = "md3.surface-container";
            border = "md3.outline";
        } else if (visual == Md3RackVisual.Focus) {
            bg = "md3.surface-container";
            border = "md3.primary";
        } else {
            bg = "md3.surface-container";
            border = "md3.outline-variant";
        }
        Md3RackKit.Paint(root, Border.BackgroundProperty, bg);
        Md3RackKit.Paint(root, Border.BorderBrushProperty, border);

        // 强调色条：模块角色色（EQ/压缩/混响各一个角色）；旁通时褪成 outline-variant
        Md3RackKit.Paint(accent, Border.BackgroundProperty,
            dim ? "md3.outline-variant" : Md3RackKit.BrushKey(Accent));

        titleText.Text = Title;
        Md3RackKit.Paint(titleText, TextBlock.ForegroundProperty, dim ? "md3.on-surface-variant" : "md3.on-surface");
        subtitleText.Text = Subtitle;
        subtitleText.IsVisible = !string.IsNullOrEmpty(Subtitle);
        Md3RackKit.Paint(subtitleText, TextBlock.ForegroundProperty, "md3.on-surface-variant");

        // 状态角标：旁通（IsBypassed）优先于关闭（电源关）
        // 注意：即使角标不可见也要刷色——未刷的 TextBlock 会继承 Fluent 默认前景（越出颜色池）。
        bool showChip = IsBypassed || !IsPowered || visual == Md3RackVisual.Bypassed;
        stateChip.IsVisible = showChip;
        Md3RackKit.PaintText(stateText, IsBypassed || visual == Md3RackVisual.Bypassed ? "fxcard.bypass" : "effects.off");
        Md3RackKit.Paint(stateChip, Border.BackgroundProperty, "md3.surface-container-highest");
        Md3RackKit.Paint(stateText, TextBlock.ForegroundProperty, "md3.on-surface-variant");

        power.IsChecked = IsPowered;
        power.IsVisible = ShowPowerSwitch;
        power.IsEnabled = enabled;
        divider.IsVisible = CardContent != null;
        body.IsVisible = CardContent != null;
        body.Opacity = dim ? 0.5 : 1.0;
        Md3RackKit.Paint(divider, Border.BackgroundProperty, "md3.outline-variant");
        presenter.Content = CardContent;

        Opacity = visual == Md3RackVisual.Disabled ? 0.5 : 1.0;
    }

    protected override void OnPointerEntered(PointerEventArgs e) {
        base.OnPointerEntered(e);
        hover = true;
        ApplyState();
    }

    protected override void OnPointerExited(PointerEventArgs e) {
        base.OnPointerExited(e);
        hover = false;
        ApplyState();
    }
}
