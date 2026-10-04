using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls;

/// <summary>
/// 机架列表的一行（**只读设计稿的数据投影**）。
/// 内置模块（EQ/压缩/混响）与 VST 槽被统一成同一个模型：链上序号 + 类型角标 + 名字 +
/// 详情 + 电源/旁通状态 + 强调色角色 —— 这就是"统一排序"的落点。
/// </summary>
public sealed class FxRackEntry {
    /// <summary>链上序号（1-based；内置模块在前，VST 槽按 SlotIndex 在后）。</summary>
    public int Order { get; init; }

    /// <summary>类型角标（技术缩写，非文案："EQ" / "COMP" / "REV" / "VST3" / "VST2" / "VST"）。</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>内置模块名走字符串键（DynamicResource ⇒ 切换语言即时生效）。</summary>
    public string? TitleKey { get; init; }

    /// <summary>数据型名字（VST 插件显示名）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>详情（内置模块 = 预设 id；VST = 厂商）。</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>是否内置模块（决定强调色语义与后续接线路径）。</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>模块电源（内置 = MixFx.*Enabled；VST = 已加载且未旁通）。</summary>
    public bool IsPowered { get; init; }

    /// <summary>旁通（内置 = 总开关 Enabled=false；VST = 槽位 Bypassed）。</summary>
    public bool IsBypassed { get; init; }

    /// <summary>空 VST 槽。</summary>
    public bool IsEmptySlot { get; init; }

    /// <summary>VST 槽下标（内置为 -1）。</summary>
    public int SlotIndex { get; init; } = -1;

    /// <summary>强调色角色（EQ=Primary / 压缩=Tertiary / 混响=Secondary / VST=Primary）。</summary>
    public Md3Role Accent { get; init; } = Md3Role.Primary;
}

/// <summary>
/// 效果机架列表（内置模块 + VST 槽按统一视觉排序的**只读设计稿**）。
///
/// 现状（本轮要解决的问题）：内置三件套在 MixFxDialog / TrackEffectRack 的"Built-in"折叠区，
/// VST 槽在同窗的另一个区——两套视觉、两套交互、排序看不见。本控件用**一套行语言**把它们
/// 排成一条可见的链：序号 → 强调色 → 名字/类型 → 状态。
///
/// 设计要点：
///   · 面板：圆角 16、底色 surface-container-low、1px outline-variant（卡片语言）；
///   · 行：圆角 12、底色 surface-container、1px 描边；左侧序号块（20×20）+ 3×26 强调色条；
///     中间名字（13px Medium）+ 类型角标 + 详情（11px）；右侧状态角标 + 拖拽握把（装饰，见下）；
///   · 选中行：secondary-container 底 + on-secondary-container 字；
///   · 悬停行：描边 outline；"不过声"行（旁通或电源关）：内容 50% 不透明度 + 状态角标"旁通"/"关"；
///   · 空槽行：标题走 effects.emptyslot 键、整行压暗、无状态角标；
///   · **只读**：点选只改 SelectedIndex（视觉），绝不写 UTrack / 不过 DocManager。
///
/// 用到的颜色池键：md3.surface-container{,-low,-highest}、md3.secondary-container、
///   md3.on-secondary-container、md3.on-surface{,-variant}、md3.outline{,-variant}、
///   md3.primary、md3.primary-container、md3.on-primary-container、md3.tertiary-container、
///   md3.on-tertiary-container + md3.color.*（无：本控件只用画刷键）。
///
/// 【后续接线点】
///   1) 拖拽排序：握把（PART_Grip）当前仅装饰。接线时在行上挂 PointerPressed/Moved 的
///      全局追踪（沿用 PanudKnob 的 TopLevel Tunnel 方案），落库走
///      DocManager.ExecuteCmd(TrackMixCommands…)，并让 UTrack 增加"链序"（内置三件套固定序
///      + VST 槽可排序）——需要 Core 侧先提供链序字段，否则 UI 排序无处存。
///   2) 行内电源开关：用 <see cref="Md3PowerSwitch"/> 替掉右侧状态角标，
///      经 DocManager.ExecuteCmd 写 MixFx.*Enabled / VstPluginSlot.Bypassed。
///   3) 点选联动：SelectedIndex 变化时通知三面板切到对应模块（fx-ui 的 MixFxViewModel）。
///   4) 实时刷新：订阅 ICmdSubscriber，收到 MixFx/VST 变更后 Rebuild()。
/// </summary>
public class FxRackPanel : TemplatedControl {
    /// <summary>数据源轨道（真实数据：UTrack.MixFx + UTrack.VstSlots）。</summary>
    public static readonly StyledProperty<UTrack?> TrackProperty =
        AvaloniaProperty.Register<FxRackPanel, UTrack?>(nameof(Track));

    /// <summary>选中行下标（默认双向；-1 = 无）。</summary>
    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<FxRackPanel, int>(
            nameof(SelectedIndex), -1, defaultBindingMode: BindingMode.TwoWay, coerce: CoerceSelectedIndex);

    /// <summary>冻结的视觉状态（陈列室/契约测试用；null = 跟随真实交互）。</summary>
    public static readonly StyledProperty<Md3RackVisual?> ForcedVisualProperty =
        AvaloniaProperty.Register<FxRackPanel, Md3RackVisual?>(nameof(ForcedVisual));

    /// <summary>是否显示"拖拽排序留待接线"的提示行。</summary>
    public static readonly StyledProperty<bool> ShowHintProperty =
        AvaloniaProperty.Register<FxRackPanel, bool>(nameof(ShowHint), true);

    /// <summary>选中行变化（仅真正改变时触发）。</summary>
    public event EventHandler<int>? SelectionChanged;

    private sealed class Row {
        public Border Root = null!;
        public TextBlock Title = null!;
        public Border Kind = null!;
        public TextBlock KindText = null!;
        public TextBlock Detail = null!;
        public Border Accent = null!;
        public Border Order = null!;
        public TextBlock OrderText = null!;
        public Border State = null!;
        public TextBlock StateText = null!;
        public Path Grip = null!;
        public bool Hover;
    }

    private readonly List<FxRackEntry> entries = new();
    private readonly List<Row> rows = new();

    private Border? root;
    private TextBlock? titleText;
    private TextBlock? subtitleText;
    private TextBlock? hintText;
    private TextBlock? emptyText;
    private Border? masterChip;
    private TextBlock? masterText;
    private Border? badge;
    private TextBlock? badgeText;
    private StackPanel? items;

    public FxRackPanel() {
        Focusable = true;
        IsTabStop = true;
        Template = new FuncControlTemplate<FxRackPanel>(BuildTemplate);
    }

    /// <summary>数据源轨道。</summary>
    public UTrack? Track {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>选中行下标。</summary>
    public int SelectedIndex {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>强制视觉状态（null = 正常交互）。</summary>
    public Md3RackVisual? ForcedVisual {
        get => GetValue(ForcedVisualProperty);
        set => SetValue(ForcedVisualProperty, value);
    }

    /// <summary>是否显示接线提示行。</summary>
    public bool ShowHint {
        get => GetValue(ShowHintProperty);
        set => SetValue(ShowHintProperty, value);
    }

    /// <summary>当前渲染的机架行（真实数据投影；测试与后续接线都用它）。</summary>
    public IReadOnlyList<FxRackEntry> Entries => entries;

    /// <summary>按 Track 重建行（Track 变化时自动调用；后续接线时由 ICmdSubscriber 调用）。</summary>
    public void Rebuild() {
        entries.Clear();
        entries.AddRange(BuildEntries(Track));
        CoerceValue(SelectedIndexProperty);
        BuildRows();
    }

    /// <summary>
    /// 把 UTrack 的真实数据投影成统一机架行（**只读**，不改模型）。
    /// 语义与渲染管线一致：<c>MixFx == null</c> = 该轨未配置效果（整条链旁通）⇒ 不出内置行；
    /// <c>MixFx.Enabled == false</c> = 总开关关 ⇒ 内置行标为旁通。
    /// </summary>
    public static IReadOnlyList<FxRackEntry> BuildEntries(UTrack? track) {
        var list = new List<FxRackEntry>();
        if (track == null) {
            return list;
        }
        UMixFx? fx = track.MixFx;
        bool master = fx?.Enabled ?? false;
        int order = 0;

        // ── 内置三件套（固定序：EQ → 压缩 → 混响，与 DSP 链一致）──
        if (fx != null) {
            list.Add(new FxRackEntry {
                Order = ++order,
                Kind = "EQ",
                TitleKey = "mixfx.eq",
                Detail = fx.EqPreset,
                IsBuiltIn = true,
                IsPowered = fx.EqEnabled,
                IsBypassed = !master,
                Accent = Md3Role.Primary,
            });
            list.Add(new FxRackEntry {
                Order = ++order,
                Kind = "COMP",
                TitleKey = "mixfx.compressor",
                Detail = fx.CompPreset,
                IsBuiltIn = true,
                IsPowered = fx.CompEnabled,
                IsBypassed = !master,
                Accent = Md3Role.Tertiary,
            });
            list.Add(new FxRackEntry {
                Order = ++order,
                Kind = "REV",
                TitleKey = "mixfx.reverb",
                Detail = fx.ReverbPreset,
                IsBuiltIn = true,
                IsPowered = fx.ReverbEnabled,
                IsBypassed = !master,
                Accent = Md3Role.Secondary,
            });
        }

        // ── VST 槽（按 SlotIndex 排序，紧随内置模块之后）──
        foreach (var slot in track.VstSlots.OrderBy(s => s.SlotIndex)) {
            bool loaded = slot.IsLoaded;
            string kind = slot.PluginTypeDisplay;
            list.Add(new FxRackEntry {
                Order = ++order,
                Kind = string.IsNullOrEmpty(kind) ? "VST" : kind,
                TitleKey = loaded ? null : "effects.emptyslot",
                Title = loaded ? slot.DisplayName : string.Empty,
                Detail = loaded ? slot.PluginVendor : string.Empty,
                IsBuiltIn = false,
                IsPowered = loaded && !slot.Bypassed,
                IsBypassed = loaded && slot.Bypassed,
                IsEmptySlot = !loaded,
                SlotIndex = slot.SlotIndex,
                Accent = Md3Role.Primary,
            });
        }
        return list;
    }

    private static int CoerceSelectedIndex(AvaloniaObject instance, int value) {
        var panel = (FxRackPanel)instance;
        return panel.entries.Count <= 0 ? -1 : Math.Clamp(value, 0, panel.entries.Count - 1);
    }

    private Control BuildTemplate(FxRackPanel control, INameScope scope) {
        var title = new TextBlock { Name = "PART_Title", FontSize = 15, FontWeight = FontWeight.Medium };
        Md3RackKit.PaintText(title, "fxrack.title");

        var subtitle = new TextBlock { Name = "PART_Subtitle", FontSize = 11 };

        var masterText = new TextBlock { Name = "PART_MasterText", FontSize = 10, FontWeight = FontWeight.Medium };
        var masterChip = new Border {
            Name = "PART_MasterChip",
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = masterText,
        };

        var badgeText = new TextBlock { Name = "PART_BadgeText", FontSize = 10, FontWeight = FontWeight.Medium };
        Md3RackKit.PaintText(badgeText, "fxrack.readonly");
        var badge = new Border {
            Name = "PART_Badge",
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeText,
        };

        var headerText = new StackPanel {
            Name = "PART_HeaderText",
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, subtitle },
        };
        var header = new Grid {
            Name = "PART_Header",
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
        };
        header.Children.Add(headerText);
        Grid.SetColumn(headerText, 0);
        header.Children.Add(masterChip);
        Grid.SetColumn(masterChip, 1);
        header.Children.Add(badge);
        Grid.SetColumn(badge, 2);

        var hint = new TextBlock { Name = "PART_Hint", FontSize = 11, TextWrapping = TextWrapping.Wrap };
        Md3RackKit.PaintText(hint, "fxrack.hint.drag");

        var items = new StackPanel { Name = "PART_Items", Spacing = 6 };
        var empty = new TextBlock { Name = "PART_Empty", FontSize = 12, Margin = new Thickness(2, 6), IsVisible = false };
        Md3RackKit.PaintText(empty, "fxrack.empty");

        var panel = new Border {
            Name = "PART_Root",
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusCard),
            Padding = new Thickness(12),
            Child = new StackPanel { Spacing = 8, Children = { header, hint, items, empty } },
        };

        root = panel;
        titleText = title;
        subtitleText = subtitle;
        this.hintText = hint;
        this.items = items;
        this.emptyText = empty;
        this.masterChip = masterChip;
        this.masterText = masterText;
        this.badge = badge;
        this.badgeText = badgeText;
        BuildRows();
        return panel;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
        base.OnApplyTemplate(e);
        ApplyMotion();
        ApplyState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == TrackProperty) {
            Rebuild();
        } else if (change.Property == SelectedIndexProperty) {
            int oldValue = change.GetOldValue<int>();
            int newValue = change.GetNewValue<int>();
            ApplyState();
            if (oldValue != newValue) {
                SelectionChanged?.Invoke(this, newValue);
            }
        } else if (change.Property == ShowHintProperty || change.Property == ForcedVisualProperty
                   || change.Property == IsEffectivelyEnabledProperty) {
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
    }

    private void BuildRows() {
        if (items == null) {
            return;
        }
        items.Children.Clear();
        rows.Clear();
        foreach (FxRackEntry entry in entries) {
            rows.Add(BuildRow(entry));
        }
        foreach (Row row in rows) {
            items.Children.Add(row.Root);
        }
        ApplyState();
    }

    private Row BuildRow(FxRackEntry entry) {
        int index = rows.Count;

        var orderText = new TextBlock {
            Name = "PART_OrderText",
            Text = entry.Order.ToString(CultureInfo.InvariantCulture),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var orderBox = new Border {
            Name = "PART_Order",
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = orderText,
        };

        var accent = new Border {
            Name = "PART_Accent",
            Width = 3,
            Height = 26,
            Margin = new Thickness(8, 0),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var title = new TextBlock {
            Name = "PART_RowTitle",
            Text = entry.Title,
            FontSize = 13,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (entry.TitleKey != null) {
            Md3RackKit.PaintText(title, entry.TitleKey);
        }

        var kindText = new TextBlock {
            Name = "PART_KindText",
            Text = entry.Kind,
            FontSize = 9,
            FontWeight = FontWeight.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var kind = new Border {
            Name = "PART_Kind",
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 1),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = kindText,
        };
        var titleRow = new StackPanel {
            Orientation = Orientation.Horizontal,
            Children = { title, kind },
        };

        var detail = new TextBlock {
            Name = "PART_RowDetail",
            Text = entry.Detail,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsVisible = !string.IsNullOrEmpty(entry.Detail),
        };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { titleRow, detail } };

        var stateText = new TextBlock { Name = "PART_StateText", FontSize = 10, FontWeight = FontWeight.Medium };
        var state = new Border {
            Name = "PART_State",
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = stateText,
        };

        // 拖拽握把：装饰（只读设计稿），后续接线的落点
        var grip = new Path {
            Name = "PART_Grip",
            Data = Geometry.Parse("M3,4 H13 M3,8 H13 M3,12 H13"),
            Width = 14,
            Height = 14,
            Margin = new Thickness(10, 0, 0, 0),
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid {
            Name = "PART_RowGrid",
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
        };
        grid.Children.Add(orderBox);
        Grid.SetColumn(orderBox, 0);
        grid.Children.Add(accent);
        Grid.SetColumn(accent, 1);
        grid.Children.Add(texts);
        Grid.SetColumn(texts, 2);
        grid.Children.Add(state);
        Grid.SetColumn(state, 3);
        grid.Children.Add(grip);
        Grid.SetColumn(grip, 4);

        var row = new Border {
            Name = "PART_Row",
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Md3RackKit.RadiusRow),
            Child = grid,
        };
        var result = new Row {
            Root = row,
            Title = title,
            Kind = kind,
            KindText = kindText,
            Detail = detail,
            Accent = accent,
            Order = orderBox,
            OrderText = orderText,
            State = state,
            StateText = stateText,
            Grip = grip,
        };
        row.PointerEntered += (_, _) => { result.Hover = true; ApplyState(); };
        row.PointerExited += (_, _) => { result.Hover = false; ApplyState(); };
        row.PointerPressed += (_, e) => {
            if (!IsEffectivelyEnabled || index >= entries.Count) {
                return;
            }
            Focus();
            SelectedIndex = index;
            e.Handled = true;
        };
        row.Cursor = new Cursor(StandardCursorType.Hand);
        return result;
    }

    private void ApplyState() {
        if (root == null || items == null || emptyText == null || hintText == null
            || subtitleText == null || masterChip == null || masterText == null
            || badge == null || badgeText == null) {
            return;
        }
        Md3RackVisual visual = ForcedVisual ?? (IsEffectivelyEnabled ? Md3RackVisual.Default : Md3RackVisual.Disabled);
        bool dim = visual is Md3RackVisual.Disabled or Md3RackVisual.Bypassed;

        // 颜色**全部**在这里刷（不在模板构筑期刷：那时部件还没挂进视觉树，
        // DynamicResource 可能解析为空，且不会被重试）。即使部件当前不可见也要刷，
        // 否则未刷的 TextBlock 会继承 Fluent 默认前景，越出颜色池。
        Md3RackKit.Paint(root, Border.BackgroundProperty, "md3.surface-container-low");
        Md3RackKit.Paint(root, Border.BorderBrushProperty, "md3.outline-variant");
        Md3RackKit.Paint(titleText!, TextBlock.ForegroundProperty, "md3.on-surface");
        Md3RackKit.Paint(subtitleText, TextBlock.ForegroundProperty, "md3.on-surface-variant");
        Md3RackKit.Paint(hintText, TextBlock.ForegroundProperty, "md3.on-surface-variant");
        Md3RackKit.Paint(emptyText, TextBlock.ForegroundProperty, "md3.on-surface-variant");
        Md3RackKit.Paint(badgeText, TextBlock.ForegroundProperty, "md3.on-tertiary-container");

        UTrack? track = Track;
        UMixFx? fx = track?.MixFx;
        subtitleText.Text = track?.TrackName ?? string.Empty;
        subtitleText.IsVisible = !string.IsNullOrEmpty(track?.TrackName);

        // 总开关角标（真实状态：MixFx.Enabled）
        bool masterOn = fx?.Enabled ?? false;
        masterChip.IsVisible = fx != null;
        Md3RackKit.PaintText(masterText, masterOn ? "effects.on" : "effects.off");
        Md3RackKit.Paint(masterChip, Border.BackgroundProperty,
            masterOn ? "md3.secondary-container" : "md3.surface-container-highest");
        Md3RackKit.Paint(masterText, TextBlock.ForegroundProperty,
            masterOn ? "md3.on-secondary-container" : "md3.on-surface-variant");

        Md3RackKit.Paint(badge, Border.BackgroundProperty, "md3.tertiary-container");

        hintText.IsVisible = ShowHint;
        emptyText.IsVisible = entries.Count == 0;
        items.IsVisible = entries.Count > 0;
        Opacity = visual == Md3RackVisual.Disabled ? 0.5 : 1.0;

        for (int i = 0; i < rows.Count && i < entries.Count; i++) {
            ApplyRowState(rows[i], entries[i], i == SelectedIndex, dim);
        }
    }

    private void ApplyRowState(Row row, FxRackEntry entry, bool selected, bool panelDim) {
        // "不过声" = 面板禁用/旁通、模块旁通、或电源关（空槽单独压暗）
        bool dimmed = panelDim || entry.IsBypassed || (!entry.IsPowered && !entry.IsEmptySlot) || entry.IsEmptySlot;
        bool hover = row.Hover && !dimmed;

        string bg, border;
        if (selected) {
            bg = "md3.secondary-container";
            border = "md3.secondary-container";
        } else if (hover) {
            bg = "md3.surface-container-high";
            border = "md3.outline";
        } else if (dimmed) {
            bg = "md3.surface-container";
            border = "md3.outline-variant";
        } else {
            bg = "md3.surface-container";
            border = "md3.outline-variant";
        }
        Md3RackKit.Paint(row.Root, Border.BackgroundProperty, bg);
        Md3RackKit.Paint(row.Root, Border.BorderBrushProperty, border);
        row.Root.Opacity = dimmed ? 0.55 : 1.0;

        Md3RackKit.Paint(row.Order, Border.BackgroundProperty, "md3.surface-container-highest");
        Md3RackKit.Paint(row.OrderText, TextBlock.ForegroundProperty,
            selected ? "md3.on-secondary-container" : "md3.on-surface-variant");
        Md3RackKit.Paint(row.Accent, Border.BackgroundProperty,
            dimmed ? "md3.outline-variant" : Md3RackKit.BrushKey(entry.Accent));
        Md3RackKit.Paint(row.Title, TextBlock.ForegroundProperty,
            selected ? "md3.on-secondary-container" : dimmed ? "md3.on-surface-variant" : "md3.on-surface");
        Md3RackKit.Paint(row.Kind, Border.BackgroundProperty,
            selected ? "md3.secondary-container" : "md3.surface-container-highest");
        Md3RackKit.Paint(row.KindText, TextBlock.ForegroundProperty,
            selected ? "md3.on-secondary-container" : "md3.on-surface-variant");
        Md3RackKit.Paint(row.Detail, TextBlock.ForegroundProperty, "md3.on-surface-variant");

        bool showState = !entry.IsEmptySlot;
        row.State.IsVisible = showState;
        bool bypassed = entry.IsBypassed;
        // 角标即使不可见也刷色（避免继承 Fluent 默认前景）
        Md3RackKit.PaintText(row.StateText, bypassed ? "fxcard.bypass" : entry.IsPowered ? "effects.on" : "effects.off");
        Md3RackKit.Paint(row.State, Border.BackgroundProperty,
            bypassed || !entry.IsPowered ? "md3.surface-container-highest" : "md3.primary-container");
        Md3RackKit.Paint(row.StateText, TextBlock.ForegroundProperty,
            bypassed || !entry.IsPowered ? "md3.on-surface-variant" : "md3.on-primary-container");

        Md3RackKit.Paint(row.Grip, Shape.StrokeProperty,
            dimmed || entry.IsEmptySlot ? "md3.outline-variant" : "md3.outline");
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) {
        base.OnGotFocus(e);
        Md3RackKit.SetPseudo(this, ":focus", true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e) {
        base.OnLostFocus(e);
        Md3RackKit.SetPseudo(this, ":focus", false);
    }

    /// <summary>键盘：↑/↓ 移行、Home/End 跳首尾（只改选中视觉，不写模型）。</summary>
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled || entries.Count == 0) {
            return;
        }
        switch (e.Key) {
            case Key.Up:
                // 无选中时 ↑ 落到末行；有选中则环形上移
                SelectedIndex = SelectedIndex < 0 ? entries.Count - 1 : (SelectedIndex - 1 + entries.Count) % entries.Count;
                e.Handled = true;
                break;
            case Key.Down:
                // 无选中时 ↓ 落到**首行**（而不是末行）；有选中则环形下移
                SelectedIndex = SelectedIndex < 0 ? 0 : (SelectedIndex + 1) % entries.Count;
                e.Handled = true;
                break;
            case Key.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.End:
                SelectedIndex = entries.Count - 1;
                e.Handled = true;
                break;
        }
    }
}
