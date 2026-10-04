using System;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenUtau.App.Controls;

/// <summary>
/// 参数数值读数（值 + 单位 + 可选上下限 / 拖拽改值）。
///
/// 存在理由：机架面板的参数区需要一个**能看、能拖、能键盘调**的小读数件——
/// 现有做法是一个 <c>TextBlock</c> 显示 + 一个 <c>Slider</c> 改值（两处状态、两处样式）。
/// 本控件把它们合成一体：读数即控件，拖拽即改值。
///
/// 设计要点：
///   · 内嵌读数面：圆角 10、底色 surface-container-high、1px outline-variant 描边、内边距 10,6；
///   · 可选小标题（Label，11px on-surface-variant）+ 数值（18px Medium on-surface）+ 单位（11px on-surface-variant）；
///   · 悬停：描边升 outline、底色升 surface-container-highest；
///   · 拖拽中：描边 primary、数值 primary（"正在改"的明确反馈）；
///   · 键盘焦点：2px primary 内环；禁用：38%；旁通：60%；
///   · 交互：垂直/水平拖拽改值（上/右 = 增大，满程由 Minimum..Maximum 自动定标），
///     双击复位 ResetValue，↑/↓ 步进（Shift 精调 1/10）、PgUp/PgDn 粗调 10×、Home/End 到上下限。
///
/// 用到的颜色池键：md3.surface-container-high、md3.surface-container-highest、md3.on-surface、
///   md3.on-surface-variant、md3.outline、md3.outline-variant、md3.primary。
/// 后续接线点：作为三面板（EQ/压缩/混响）每个参数的读数件——拖拽/键盘改值后经
///   DocManager.ExecuteCmd 写入 UTrack.MixFx（本轮只读设计稿，控件自身不写模型）。
/// </summary>
public class Md3NumericReadout : TemplatedControl {
    private const double FieldPaddingX = 10;
    private const double FieldPaddingY = 6;

    /// <summary>当前值（默认双向；自动夹在 Minimum..Maximum 内）。</summary>
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(
            nameof(Value), defaultBindingMode: BindingMode.TwoWay, coerce: CoerceValueToRange);

    /// <summary>下限（默认 0）。</summary>
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(nameof(Minimum));

    /// <summary>上限（默认 100）。</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(nameof(Maximum), 100.0);

    /// <summary>单位（"dB" / "Hz" / "%" / "ms"；可空）。</summary>
    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<Md3NumericReadout, string?>(nameof(Unit));

    /// <summary>小标题（可空；为空时只显示数值行）。</summary>
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<Md3NumericReadout, string?>(nameof(Label));

    /// <summary>.NET 数值格式串（默认 "0.##"；"F1"、"0.0"、"+0.0;-0.0;0.0" 都可）。</summary>
    public static readonly StyledProperty<string?> FormatProperty =
        AvaloniaProperty.Register<Md3NumericReadout, string?>(nameof(Format), "0.##");

    /// <summary>键盘步进（↑/↓ 一次的增量；默认 0.1）。</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(nameof(Step), 0.1);

    /// <summary>拖拽灵敏度（每像素增量；0 = 按范围自动定标 range/200）。</summary>
    public static readonly StyledProperty<double> DragSensitivityProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(nameof(DragSensitivity));

    /// <summary>双击复位值（NaN = 不复位）。</summary>
    public static readonly StyledProperty<double> ResetValueProperty =
        AvaloniaProperty.Register<Md3NumericReadout, double>(nameof(ResetValue), double.NaN);

    /// <summary>是否可拖拽改值（默认 true）。</summary>
    public static readonly StyledProperty<bool> IsDraggableProperty =
        AvaloniaProperty.Register<Md3NumericReadout, bool>(nameof(IsDraggable), true);

    /// <summary>冻结的视觉状态（陈列室/契约测试用；null = 跟随真实交互）。</summary>
    public static readonly StyledProperty<Md3RackVisual?> ForcedVisualProperty =
        AvaloniaProperty.Register<Md3NumericReadout, Md3RackVisual?>(nameof(ForcedVisual));

    /// <summary>值变化。</summary>
    public event EventHandler<double>? ValueChanged;

    private Border? field;
    private Border? focusRing;
    private TextBlock? labelText;
    private TextBlock? valueText;
    private TextBlock? unitText;

    private bool hover;
    private bool dragging;
    private bool focused;
    private TopLevel? dragRoot;
    private Point dragStart;
    private double dragStartValue;

    public Md3NumericReadout() {
        Focusable = true;
        IsTabStop = true;
        Template = new FuncControlTemplate<Md3NumericReadout>(BuildTemplate);
        DoubleTapped += OnDoubleTapped;
    }

    /// <summary>当前值。</summary>
    public double Value {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>下限。</summary>
    public double Minimum {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>上限。</summary>
    public double Maximum {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>单位。</summary>
    public string? Unit {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>小标题。</summary>
    public string? Label {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>数值格式串。</summary>
    public string? Format {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>键盘步进。</summary>
    public double Step {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>拖拽灵敏度（0 = 自动）。</summary>
    public double DragSensitivity {
        get => GetValue(DragSensitivityProperty);
        set => SetValue(DragSensitivityProperty, value);
    }

    /// <summary>双击复位值。</summary>
    public double ResetValue {
        get => GetValue(ResetValueProperty);
        set => SetValue(ResetValueProperty, value);
    }

    /// <summary>是否可拖拽改值。</summary>
    public bool IsDraggable {
        get => GetValue(IsDraggableProperty);
        set => SetValue(IsDraggableProperty, value);
    }

    /// <summary>强制视觉状态（null = 正常交互）。</summary>
    public Md3RackVisual? ForcedVisual {
        get => GetValue(ForcedVisualProperty);
        set => SetValue(ForcedVisualProperty, value);
    }

    /// <summary>程序化微调（键盘、拖拽与测试都走这里）：按增量改值并自动夹取范围。</summary>
    public void Nudge(double delta) => Value += delta;

    /// <summary>当前显示文本（数值 + 单位），测试与无障碍文案都用它。</summary>
    public string DisplayText =>
        (ValueText) + (string.IsNullOrEmpty(Unit) ? string.Empty : " " + Unit);

    /// <summary>数值部分文本（不含单位）。</summary>
    public string ValueText => Value.ToString(Format ?? "0.##", CultureInfo.InvariantCulture);

    private static double CoerceValueToRange(AvaloniaObject instance, double value) {
        var readout = (Md3NumericReadout)instance;
        if (double.IsNaN(value)) {
            return readout.Minimum;
        }
        // 上下限被写反时不夹取（宁可显示原值，也不要抛 ArgumentException）
        if (readout.Minimum > readout.Maximum) {
            return value;
        }
        return Math.Clamp(value, readout.Minimum, readout.Maximum);
    }

    /// <summary>每像素增量：显式设定优先，否则按范围自动定标（满程约 200px）。</summary>
    private double Sensitivity {
        get {
            if (DragSensitivity > 0) {
                return DragSensitivity;
            }
            double span = Maximum - Minimum;
            return double.IsFinite(span) && span > 0 ? span / 200.0 : 0.25;
        }
    }

    /// <summary>拖拽起点（与 <see cref="DragTo"/> 同一坐标系；指针拖拽与程序化拖拽共用同一段数学）。</summary>
    public void BeginDrag(Point position) {
        dragStart = position;
        dragStartValue = Value;
        dragging = true;
        ApplyState();
    }

    /// <summary>拖到 position：主位移方向定增减（上/右 = 增大），步长见 DragSensitivity / 自动定标。</summary>
    public void DragTo(Point position) {
        if (!dragging) {
            return;
        }
        double up = dragStart.Y - position.Y;      // 向上 = 增大
        double right = position.X - dragStart.X;   // 向右 = 增大
        double delta = Math.Abs(up) >= Math.Abs(right) ? up : right;
        Value = dragStartValue + delta * Sensitivity;
    }

    private Control BuildTemplate(Md3NumericReadout control, INameScope scope) {
        var label = new TextBlock {
            Name = "PART_Label",
            FontSize = 11,
            IsVisible = false,
        };
        var value = new TextBlock {
            Name = "PART_Value",
            FontSize = 18,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var unit = new TextBlock {
            Name = "PART_Unit",
            FontSize = 11,
            Margin = new Thickness(3, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsVisible = false,
        };
        var valueRow = new StackPanel {
            Name = "PART_ValueRow",
            Orientation = Orientation.Horizontal,
            Children = { value, unit },
        };
        var content = new StackPanel {
            Name = "PART_Content",
            Spacing = 2,
            Children = { label, valueRow },
        };
        var ring = new Border {
            Name = "PART_Focus",
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusField),
            Margin = new Thickness(-FieldPaddingX, -FieldPaddingY),
            IsVisible = false,
        };
        var box = new Border {
            Name = "PART_Root",
            MinWidth = 84,
            Padding = new Thickness(FieldPaddingX, FieldPaddingY),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusField),
            Child = new Panel { Children = { content, ring } },
        };
        field = box;
        focusRing = ring;
        labelText = label;
        valueText = value;
        unitText = unit;
        return box;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        ApplyMotion();
        ApplyState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty) {
            ApplyState();
            ValueChanged?.Invoke(this, change.GetNewValue<double>());
        } else if (change.Property == MinimumProperty || change.Property == MaximumProperty) {
            CoerceValue(ValueProperty);
            ApplyState();
        } else if (change.Property == LabelProperty || change.Property == UnitProperty || change.Property == FormatProperty) {
            ApplyState();
        } else if (change.Property == ForcedVisualProperty || change.Property == IsEffectivelyEnabledProperty
                   || change.Property == IsDraggableProperty) {
            ApplyState();
        }
    }

    private void ApplyMotion() {
        if (field == null) {
            return;
        }
        field.Transitions = Md3RackKit.Motion(this,
            new BrushTransition { Property = Border.BackgroundProperty, Duration = Md3RackKit.FastMotion },
            new BrushTransition { Property = Border.BorderBrushProperty, Duration = Md3RackKit.FastMotion });
        if (valueText != null) {
            valueText.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = TextBlock.ForegroundProperty, Duration = Md3RackKit.FastMotion });
        }
        if (focusRing != null) {
            focusRing.Transitions = Md3RackKit.Motion(this,
                new BrushTransition { Property = Border.BorderBrushProperty, Duration = Md3RackKit.FastMotion });
        }
    }

    private void ApplyState() {
        if (field == null || valueText == null || unitText == null || labelText == null) {
            return;
        }
        bool enabled = IsEffectivelyEnabled;
        Md3RackVisual visual = ForcedVisual ?? (enabled
            ? (dragging ? Md3RackVisual.Pressed
                : hover ? Md3RackVisual.Hover
                : focused ? Md3RackVisual.Focus
                : Md3RackVisual.Default)
            : Md3RackVisual.Disabled);
        bool dim = visual is Md3RackVisual.Disabled or Md3RackVisual.Bypassed;

        string bg, border, valueBrush;
        switch (visual) {
            case Md3RackVisual.Disabled:
                bg = "md3.surface-container";
                border = "md3.outline-variant";
                valueBrush = "md3.on-surface-variant";
                break;
            case Md3RackVisual.Bypassed:
                bg = "md3.surface-container";
                border = "md3.outline-variant";
                valueBrush = "md3.on-surface-variant";
                break;
            case Md3RackVisual.Hover:
                bg = "md3.surface-container-highest";
                border = "md3.outline";
                valueBrush = "md3.on-surface";
                break;
            case Md3RackVisual.Pressed:
                bg = "md3.surface-container-highest";
                border = "md3.primary";
                valueBrush = "md3.primary";
                break;
            case Md3RackVisual.Focus:
                bg = "md3.surface-container-high";
                border = "md3.primary";
                valueBrush = "md3.on-surface";
                break;
            default:
                bg = "md3.surface-container-high";
                border = "md3.outline-variant";
                valueBrush = "md3.on-surface";
                break;
        }

        Md3RackKit.Paint(field, Border.BackgroundProperty, bg);
        Md3RackKit.Paint(field, Border.BorderBrushProperty, border);
        Md3RackKit.Paint(valueText, TextBlock.ForegroundProperty, valueBrush);
        Md3RackKit.Paint(unitText, TextBlock.ForegroundProperty, "md3.on-surface-variant");
        Md3RackKit.Paint(labelText, TextBlock.ForegroundProperty, "md3.on-surface-variant");
        if (focusRing != null) {
            Md3RackKit.Paint(focusRing, Border.BorderBrushProperty, "md3.primary");
            focusRing.IsVisible = visual == Md3RackVisual.Focus;
        }

        bool showLabel = !string.IsNullOrEmpty(Label);
        labelText.Text = Label;
        labelText.IsVisible = showLabel;
        valueText.Text = ValueText;
        unitText.Text = Unit;
        unitText.IsVisible = !string.IsNullOrEmpty(Unit);

        Opacity = visual switch {
            Md3RackVisual.Disabled => 0.38,
            Md3RackVisual.Bypassed => 0.6,
            _ => 1.0,
        };
        ToolTip.SetTip(this, showLabel ? Label + " · " + DisplayText : DisplayText);
        Cursor = IsDraggable && !dim ? ViewConstants.cursorSizeNS : Cursor.Default;
    }

    protected override void OnPointerEntered(PointerEventArgs e) {
        base.OnPointerEntered(e);
        hover = true;
        ApplyState();
    }

    protected override void OnPointerExited(PointerEventArgs e) {
        base.OnPointerExited(e);
        hover = false;
        if (!dragging) {
            ApplyState();
        }
    }

    /// <summary>
    /// 按下开始**全局追踪**（沿用 PanKnob 的实证做法：不用 Pointer.Capture——
    /// 在 ScrollViewer 内捕获会被抢走，CaptureLost 直接打断拖拽）。
    /// 用 Tunnel + handledEventsToo 挂在窗口根部，拖出控件边界仍跟随。
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e) {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !IsDraggable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
            return;
        }
        Focus();
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) {
            return;
        }
        dragRoot = topLevel;
        BeginDrag(e.GetPosition(topLevel));
        topLevel.AddHandler(PointerMovedEvent, OnGlobalMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        topLevel.AddHandler(PointerReleasedEvent, OnGlobalReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        e.Handled = true;
    }

    private void OnGlobalMoved(object? sender, PointerEventArgs e) {
        if (dragRoot == null) {
            return;
        }
        DragTo(e.GetPosition(dragRoot));
        e.Handled = true;
    }

    private void OnGlobalReleased(object? sender, PointerReleasedEventArgs e) => EndDrag();

    /// <summary>结束拖拽（指针释放/失焦/脱离视觉树/程序化测试都走这里）。</summary>
    public void EndDrag() {
        if (dragRoot != null) {
            dragRoot.RemoveHandler(PointerMovedEvent, OnGlobalMoved);
            dragRoot.RemoveHandler(PointerReleasedEvent, OnGlobalReleased);
            dragRoot = null;
        }
        if (!dragging) {
            return;
        }
        dragging = false;
        ApplyState();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) {
        if (!IsEffectivelyEnabled || !IsDraggable || double.IsNaN(ResetValue)) {
            return;
        }
        Value = ResetValue;
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) {
        base.OnGotFocus(e);
        focused = true;
        ApplyState();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e) {
        base.OnLostFocus(e);
        focused = false;
        EndDrag();
        ApplyState();
    }

    /// <summary>
    /// 键盘：↑/↓ 步进（Shift = 1/10 精调）、PgUp/PgDn 粗调 10×、Home/End 到上下限。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled) {
            return;
        }
        bool fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        double step = fine ? Step / 10.0 : Step;
        switch (e.Key) {
            case Key.Up:
                Nudge(step);
                e.Handled = true;
                break;
            case Key.Down:
                Nudge(-step);
                e.Handled = true;
                break;
            case Key.PageUp:
                Nudge(step * 10);
                e.Handled = true;
                break;
            case Key.PageDown:
                Nudge(-step * 10);
                e.Handled = true;
                break;
            case Key.Home:
                if (double.IsFinite(Minimum)) {
                    Value = Minimum;
                    e.Handled = true;
                }
                break;
            case Key.End:
                if (double.IsFinite(Maximum)) {
                    Value = Maximum;
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        EndDrag();
    }
}
