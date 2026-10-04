using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenUtau.App.Controls;

/// <summary>
/// 分段选择器（MD3 segmented control）。
///
/// 存在理由：现有页面用 <c>Border.segmented</c> + 若干 <c>RadioButton.chip</c> + 一个
/// <c>ValueEqualsConverter</c> 手工拼装（见 Views/PreferencesView.axaml 第 100-160 行），
/// 选项一多就三处不同步（选项集合、RadioButton 数量、转换器比较值）。
/// 本控件把「选项集合 + 选中项」收进一个 API，视觉规格沿用既有拼装件的数字，
/// 因此替换后页面看起来不变，但不再需要转换器。
///
/// 设计要点（与既有 Border.segmented 同一套数字，避免"新控件新长相"）：
///   · 容器：高 40（紧凑 32）、胶囊 999、内边距 4、底色 surface-container-high、
///     1px outline-variant 描边；
///   · 选中项：secondary-container 底 + on-secondary-container 字 + Medium 字重；
///   · 悬停项：surface-container-highest 底 + on-surface 字；
///   · 按下项：surface-container-highest 底 + 状态层 12%；
///   · 键盘焦点：选中项外圈 2px primary（Tab 只停一次，方向键移选择——MD3/分段选择器惯例）；
///   · 禁用：整组 38% 不透明度；旁通：整组压暗。
///
/// 用到的颜色池键：md3.surface-container-high、md3.secondary-container、
///   md3.on-secondary-container、md3.surface-container-highest、md3.on-surface、
///   md3.on-surface-variant、md3.outline-variant、md3.outline、md3.primary、md3.on-surface。
/// 后续接线点：EQ 频段选择（Low/Mid/High）、压缩比档位、混响房间大小档位——
///   替换 PreferencesView 里 chip 拼装与 MixFxDialog 的三面板选择条。
/// </summary>
public class Md3SegmentedControl : TemplatedControl {
    /// <summary>选项文本集合（变更即重建分段）。</summary>
    public static readonly StyledProperty<IEnumerable<string>?> OptionsProperty =
        AvaloniaProperty.Register<Md3SegmentedControl, IEnumerable<string>?>(nameof(Options));

    /// <summary>选中项下标（默认双向；空集合时为 -1）。</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<Md3SegmentedControl, int>(
            nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay,
            coerce: CoerceSelectedIndex);

    /// <summary>紧凑规格（高 32、字号 12）。</summary>
    public static readonly StyledProperty<bool> IsCompactProperty =
        AvaloniaProperty.Register<Md3SegmentedControl, bool>(nameof(IsCompact));

    /// <summary>冻结的视觉状态（陈列室/契约测试用；null = 跟随真实交互）。</summary>
    public static readonly StyledProperty<Md3RackVisual?> ForcedVisualProperty =
        AvaloniaProperty.Register<Md3SegmentedControl, Md3RackVisual?>(nameof(ForcedVisual));

    /// <summary>选中项变化（仅在真正改变时触发）。</summary>
    public event EventHandler<int>? SelectionChanged;

    private sealed class Segment {
        public Border Root = null!;
        public TextBlock Text = null!;
        public Border Focus = null!;
        public bool Hover;
        public bool Pressed;
    }

    private readonly List<Segment> segments = new();
    private readonly List<string> optionList = new();
    private UniformGrid? itemsPanel;
    private Border? rootBorder;

    private bool focused;

    public Md3SegmentedControl() {
        Focusable = true;
        IsTabStop = true;
        Template = new FuncControlTemplate<Md3SegmentedControl>(BuildTemplate);
    }

    /// <summary>选项文本集合。</summary>
    public IEnumerable<string>? Options {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>选中项下标（-1 = 无）。</summary>
    public int SelectedIndex {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>紧凑规格。</summary>
    public bool IsCompact {
        get => GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>强制视觉状态（null = 正常交互）。</summary>
    public Md3RackVisual? ForcedVisual {
        get => GetValue(ForcedVisualProperty);
        set => SetValue(ForcedVisualProperty, value);
    }

    /// <summary>当前选中的选项文本（无选中时为 null）。</summary>
    public string? SelectedOption =>
        SelectedIndex >= 0 && SelectedIndex < optionList.Count ? optionList[SelectedIndex] : null;

    /// <summary>代码侧便利写法。</summary>
    public void SetOptions(params string[] options) => Options = options;

    private static int CoerceSelectedIndex(AvaloniaObject instance, int value) {
        var control = (Md3SegmentedControl)instance;
        int count = control.optionList.Count;
        return count <= 0 ? -1 : Math.Clamp(value, 0, count - 1);
    }

    private Control BuildTemplate(Md3SegmentedControl control, INameScope scope) {
        var panel = new UniformGrid { Name = "PART_Items" };
        var root = new Border {
            Name = "PART_Root",
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusPill),
            Child = panel,
        };
        itemsPanel = panel;
        rootBorder = root;
        Rebuild();
        return root;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        ApplyMotion();
        ApplyState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == OptionsProperty) {
            optionList.Clear();
            if (Options != null) {
                optionList.AddRange(Options);
            }
            CoerceValue(SelectedIndexProperty);
            Rebuild();
        } else if (change.Property == SelectedIndexProperty) {
            int oldValue = change.GetOldValue<int>();
            int newValue = change.GetNewValue<int>();
            ApplyState();
            if (oldValue != newValue) {
                SelectionChanged?.Invoke(this, newValue);
            }
        } else if (change.Property == IsCompactProperty) {
            Rebuild();
        } else if (change.Property == ForcedVisualProperty || change.Property == IsEffectivelyEnabledProperty) {
            ApplyState();
        }
    }

    private void ApplyMotion() {
        if (rootBorder == null) {
            return;
        }
        rootBorder.Transitions = Md3RackKit.Motion(this,
            new BrushTransition { Property = Border.BackgroundProperty, Duration = Md3RackKit.FastMotion },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = Md3RackKit.FastMotion });
        foreach (Segment segment in segments) {
            segment.Root.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = Border.BackgroundProperty, Duration = Md3RackKit.FastMotion });
            segment.Text.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = Md3RackKit.FastMotion });
        }
    }

    /// <summary>按选项集合重建分段（选项/紧凑度变化时调用）。</summary>
    private void Rebuild() {
        if (itemsPanel == null) {
            return;
        }
        itemsPanel.Children.Clear();
        segments.Clear();
        bool compact = IsCompact;
        for (int i = 0; i < optionList.Count; i++) {
            int index = i;
            var text = new TextBlock {
                Name = "PART_Text",
                Text = optionList[index],
                FontSize = compact ? 12 : 13,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var focus = new Border {
                Name = "PART_Focus",
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(Md3RackKit.RadiusPill),
                Margin = new Thickness(-3),
                IsVisible = false,
            };
            var root = new Border {
                Name = "PART_Segment",
                MinHeight = compact ? 24 : 32,
                Padding = compact ? new Thickness(10, 4) : new Thickness(14, 6),
                CornerRadius = new CornerRadius(Md3RackKit.RadiusPill),
                Margin = new Thickness(2, 0),
                Child = new Panel { Children = { focus, text } },
            };
            var segment = new Segment { Root = root, Text = text, Focus = focus };
            root.PointerEntered += (_, _) => { segment.Hover = true; ApplyState(); };
            root.PointerExited += (_, _) => { segment.Hover = false; segment.Pressed = false; ApplyState(); };
            root.PointerPressed += (_, e) => {
                if (!IsEffectivelyEnabled) {
                    return;
                }
                segment.Pressed = true;
                ApplyState();
                e.Handled = true;
            };
            root.PointerReleased += (_, e) => {
                if (!segment.Pressed) {
                    return;
                }
                bool inside = segment.Hover;
                segment.Pressed = false;
                if (inside && IsEffectivelyEnabled) {
                    Select(index);
                }
                ApplyState();
                e.Handled = true;
            };
            segments.Add(segment);
            itemsPanel.Children.Add(root);
        }
        itemsPanel.Columns = Math.Max(1, optionList.Count);
        itemsPanel.IsVisible = optionList.Count > 0;
        ApplyMotion();
        ApplyState();
    }

    /// <summary>选中第 index 段（键盘与点击的共同入口）。</summary>
    private void Select(int index) {
        if (index < 0 || index >= optionList.Count || index == SelectedIndex) {
            return;
        }
        SelectedIndex = index;
    }

    private void ApplyState() {
        if (rootBorder == null) {
            return;
        }
        bool enabled = IsEffectivelyEnabled;
        Md3RackVisual visual = ForcedVisual ?? (enabled ? Md3RackVisual.Default : Md3RackVisual.Disabled);
        bool dim = visual is Md3RackVisual.Disabled or Md3RackVisual.Bypassed;

        Md3RackKit.Paint(rootBorder, Border.BackgroundProperty, "md3.surface-container-high");
        Md3RackKit.Paint(rootBorder, Border.BorderBrushProperty, visual == Md3RackVisual.Focus ? "md3.outline" : "md3.outline-variant");
        rootBorder.Padding = new Thickness(4);
        rootBorder.MinHeight = IsCompact ? 32 : 40;
        rootBorder.CornerRadius = new CornerRadius(Md3RackKit.RadiusPill);
        Opacity = visual switch {
            Md3RackVisual.Disabled => 0.38,
            Md3RackVisual.Bypassed => 0.6,
            _ => 1.0,
        };

        for (int i = 0; i < segments.Count; i++) {
            Segment segment = segments[i];
            bool selected = i == SelectedIndex;
            bool hover = visual == Md3RackVisual.Hover || segment.Hover;
            bool pressed = visual == Md3RackVisual.Pressed || segment.Pressed;
            if (dim) {
                hover = false;
                pressed = false;
            }

            string bg, fg;
            if (selected) {
                bg = "md3.secondary-container";
                fg = "md3.on-secondary-container";
            } else if (pressed) {
                bg = "md3.surface-container-highest";
                fg = "md3.on-surface";
            } else if (hover) {
                bg = "md3.surface-container-highest";
                fg = "md3.on-surface";
            } else {
                // 未选中 = 与容器同色（视觉上等同"透明"，但不引入 null/Transparent 字面量）
                bg = "md3.surface-container-high";
                fg = dim ? "md3.outline" : "md3.on-surface-variant";
            }

            Md3RackKit.Paint(segment.Root, Border.BackgroundProperty, bg);
            Md3RackKit.Paint(segment.Text, TextBlock.ForegroundProperty, fg);
            segment.Text.FontWeight = selected ? FontWeight.Medium : FontWeight.Normal;
            Md3RackKit.Paint(segment.Focus, Border.BorderBrushProperty, "md3.primary");
            segment.Focus.IsVisible = selected && (visual == Md3RackVisual.Focus || focused);
            segment.Root.Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) {
        base.OnGotFocus(e);
        focused = true;
        ApplyState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e) {
        base.OnLostFocus(e);
        focused = false;
        ApplyState();
    }

    /// <summary>
    /// 键盘：整组一个 Tab 停靠点，←/→（以及 ↑/↓）移动选择，Home/End 跳首尾。
    /// 焦点环画在选中项外圈，因此"焦点在哪"与"选中谁"始终一致。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled || optionList.Count == 0) {
            return;
        }
        switch (e.Key) {
            case Key.Left:
            case Key.Up:
                Select(SelectedIndex <= 0 ? optionList.Count - 1 : SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
                Select(SelectedIndex >= optionList.Count - 1 ? 0 : SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Home:
                Select(0);
                e.Handled = true;
                break;
            case Key.End:
                Select(optionList.Count - 1);
                e.Handled = true;
                break;
        }
    }
}
