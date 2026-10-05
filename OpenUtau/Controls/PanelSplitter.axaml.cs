using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtau.App.Controls;

/// <summary>
/// W16 面板系统的**共用分隔条**：一处控件、一处样式，所有可调宽面板都靠它实现
/// "可拖宽 + 双击复位 + 静默夹紧 + 放不下就自动折叠"，接入方不再各写一套拖拽逻辑。
///
/// 输入（接入方绑定）
/// - <see cref="Target"/>（TwoWay）：面板宽度**意图值**（用户拖动/持久化的值，夹紧不会改写它）。
/// - <see cref="Min"/> / <see cref="Max"/>：设计硬边界。
/// - <see cref="DefaultWidth"/>：双击复位到此值。
/// - <see cref="CenterMin"/>：中央自适应列（星号列）最小宽。
/// - <see cref="PanelColumn"/>：**必填**，被调面板所在的 Grid 列索引（计算"其它固定占用"时排除自身列）。
/// - <see cref="Invert"/>：面板在分隔条右侧时为 true（拖拽方向取反）。
/// - <see cref="CollapseThreshold"/>：有效宽低于此值 ⇒ 自动折叠（默认 120，理由见下）。
///
/// 输出（接入方绑定到面板容器）
/// - <see cref="PanelWidth"/>：**夹紧后的有效宽** → 面板容器 `Width="{Binding #XxxSplitter.PanelWidth}"`。
/// - <see cref="PanelShown"/>：面板是否显示（本控件 `IsVisible` && 有效宽 ≥ 阈值）
///   → 面板容器 `IsVisible="{Binding #XxxSplitter.PanelShown}"`；自动折叠时面板隐藏且列宽 0（不留夹缝）。
///
/// 三条已定口径（task-28，m1-strip 只读审出 + Lead 裁决）
/// 1. **自身列不算保留宽**：<see cref="ReservedWidth"/> 排除 <see cref="PanelColumn"/>，
///    否则窄窗下 `可用 − 保留` 把面板自身宽度也减掉，拖 1px 就会被兜到 Min（吸附缺陷）。
/// 2. **宿主尺寸变化后静默重新夹紧**：<see cref="ReclampSilently"/> 在每次布局后按当前可用宽重算
///    <see cref="PanelWidth"/>，但**不改写 <see cref="Target"/>** ⇒ 窗口变窄时夹到合法值、变宽后自动回到用户值。
/// 3. **放不下就折叠**：`MaxAllowed` 允许跌破 Min（下限 0）；有效宽 &lt; 阈值 ⇒ 隐藏面板 + 列宽 0。
/// </summary>
public partial class PanelSplitter : UserControl {
    /// <summary>面板宽度意图值（与宿主双向绑定；夹紧不改写它）。</summary>
    public static readonly StyledProperty<double> TargetProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(Target), 264, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(Min), 200);

    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(Max), 480);

    public static readonly StyledProperty<double> DefaultWidthProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(DefaultWidth), 264);

    /// <summary>中央自适应列（Grid 的星号列）最小宽度，参与夹紧。</summary>
    public static readonly StyledProperty<double> CenterMinProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(CenterMin), 320);

    /// <summary>面板在分隔条右侧时为 true（拖拽方向取反）。</summary>
    public static readonly StyledProperty<bool> InvertProperty =
        AvaloniaProperty.Register<PanelSplitter, bool>(nameof(Invert));

    /// <summary>
    /// **必填**：被调宽面板所在的 Grid 列索引。计算"其它固定占用"时排除该列 ——
    /// 面板自身的宽度绝不能参与减法，否则窄窗下 dynamic 会算成负数，
    /// 任何一次拖拽（哪怕 1px）都会被兜到 Min（m1-strip 审出的吸附缺陷）。
    /// 默认 -1 = 不排除（仅用于"面板不在本 Grid 单列上"的特殊宿主；接入方一般都要显式给值）。
    /// </summary>
    public static readonly StyledProperty<int> PanelColumnProperty =
        AvaloniaProperty.Register<PanelSplitter, int>(nameof(PanelColumn), -1);

    /// <summary>有效宽低于此值时自动折叠（不留夹缝）。默认 120：轨头卡片要 ≥170 才不折行、
    /// 素材库卡片要 ≥140；低于 120 时任何面板都只剩一条"夹缝"，不如折叠交给中央区。</summary>
    public static readonly StyledProperty<double> CollapseThresholdProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(CollapseThreshold), 120);

    /// <summary>
    /// 本控件命中区占用的固定宽（默认 7，与本控件 XAML 的命中区一致）。
    /// 语义：**恒定计入 reserved**，且**所有分隔条都按此值恒占位**（不看它们当时是否可见）——
    /// 否则会出现"折叠腾出 7px → 又够宽 → 重新展开"的两态振荡（双面板临界窗下会互相触发）。
    /// 由此 <see cref="CenterMin"/> 的语义 = 中央列**净**可用宽（不因分隔条打折扣）。
    /// </summary>
    public static readonly StyledProperty<double> SelfReservedWidthProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(SelfReservedWidth), 7);

    /// <summary>【输出】夹紧后的有效宽（面板容器 Width 绑它）。</summary>
    public static readonly StyledProperty<double> PanelWidthProperty =
        AvaloniaProperty.Register<PanelSplitter, double>(nameof(PanelWidth));

    /// <summary>【输出】面板是否显示（视图可见/未折叠 且 有效宽 ≥ 阈值）。</summary>
    public static readonly StyledProperty<bool> PanelShownProperty =
        AvaloniaProperty.Register<PanelSplitter, bool>(nameof(PanelShown));

    public double Target {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }
    public double Min {
        get => GetValue(MinProperty);
        set => SetValue(MinProperty, value);
    }
    public double Max {
        get => GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }
    public double DefaultWidth {
        get => GetValue(DefaultWidthProperty);
        set => SetValue(DefaultWidthProperty, value);
    }
    public double CenterMin {
        get => GetValue(CenterMinProperty);
        set => SetValue(CenterMinProperty, value);
    }
    public bool Invert {
        get => GetValue(InvertProperty);
        set => SetValue(InvertProperty, value);
    }
    public int PanelColumn {
        get => GetValue(PanelColumnProperty);
        set => SetValue(PanelColumnProperty, value);
    }
    public double CollapseThreshold {
        get => GetValue(CollapseThresholdProperty);
        set => SetValue(CollapseThresholdProperty, value);
    }
    public double SelfReservedWidth {
        get => GetValue(SelfReservedWidthProperty);
        set => SetValue(SelfReservedWidthProperty, value);
    }
    public double PanelWidth {
        get => GetValue(PanelWidthProperty);
        private set => SetCurrentValue(PanelWidthProperty, value);
    }
    public bool PanelShown {
        get => GetValue(PanelShownProperty);
        private set => SetCurrentValue(PanelShownProperty, value);
    }

    /// <summary>拖拽结束 / 双击复位后触发：宿主据此落盘（拖动过程中不落盘，避免写爆磁盘）。</summary>
    public event EventHandler? DragCompleted;

    private bool dragging;
    private double dragStartX;
    private double dragStartTarget;
    private double dragAvailable;
    private double dragReserved;

    public PanelSplitter() {
        InitializeComponent();
        HitArea.PointerPressed += OnPressed;
        HitArea.PointerMoved += OnMoved;
        HitArea.PointerReleased += OnReleased;
        HitArea.DoubleTapped += OnDoubleTapped;
        // 宿主尺寸/兄弟变化都会走到 LayoutUpdated：在此静默重新夹紧（口径 2）
        LayoutUpdated += (_, _) => UpdateEffective();
        this.GetObservable(IsVisibleProperty).Subscribe(_ => UpdateEffective());
    }

    /// <summary>
    /// 当前允许的最大有效宽：`可用宽 − 其它固定占用 − 中央最小宽`，**可以跌破 Min**（口径 3-1），
    /// 下限取 0（放不下就该缩到 0 让中央区吃掉，而不是溢出/留夹缝）。
    /// </summary>
    internal double MaxAllowed() {
        double available = dragAvailable > 0 ? dragAvailable : AvailableWidth();
        double reserved = dragReserved > 0 ? dragReserved : ReservedWidth();
        double dynamic = available > 0 ? available - reserved - CenterMin : Max;
        return Math.Max(0, Math.Min(Max, dynamic));
    }

    /// <summary>把意图值夹到 [0, MaxAllowed]（夹紧结果 < 阈值 ⇒ 由 <see cref="PanelShown"/> 折叠）。</summary>
    internal double ClampEffective(double intent) => Math.Min(Math.Max(intent, 0), MaxAllowed());

    /// <summary>
    /// 按拖拽位移应用宽度（指针事件与测试共用这一条路径，保证测试覆盖真实逻辑）：
    /// 拖动改的是**意图值** <see cref="Target"/>，同时夹到当前合法上限。
    /// 未处于按下状态时（例如测试直接调用）先就地快照基准，语义与"按下后拖"一致。
    /// </summary>
    internal void ApplyDragDelta(double deltaX) {
        if (!dragging) {
            dragStartTarget = Target;
            dragAvailable = AvailableWidth();
            dragReserved = ReservedWidth();
        }
        double delta = Invert ? -deltaX : deltaX;
        double max = MaxAllowed();
        SetCurrentValue(TargetProperty, Math.Clamp(dragStartTarget + delta, Math.Min(Min, max), max));
    }

    /// <summary>双击复位到默认宽（并落盘）。</summary>
    internal void ResetToDefault() {
        double max = MaxAllowed();
        SetCurrentValue(TargetProperty, Math.Clamp(DefaultWidth, Math.Min(Min, max), max));
        UpdateEffective();
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 唯一的状态收敛点（口径 2 + 3）：夹紧 → 判定是否放得下 → 同时写 <see cref="PanelWidth"/> /
    /// <see cref="PanelShown"/>。**必须一次算完两个输出**：分开写会出现
    /// "夹到 100 → 判为放不下 → 归零 → 下一帧又夹到 100" 的两态振荡（Avalonia 会报无限布局循环）。
    /// 不变量：结果只依赖 (Target, 布局占用, IsVisible)，不依赖上一次的 PanelWidth/PanelShown。
    /// </summary>
    internal void UpdateEffective() {
        double clamped = ClampEffective(Target);
        bool shown = IsVisible && clamped >= CollapseThreshold;
        double nextWidth = shown ? clamped : 0;
        if (Math.Abs(PanelWidth - nextWidth) >= 0.5) {
            PanelWidth = nextWidth;
        }
        if (PanelShown != shown) {
            PanelShown = shown;
        }
    }

    /// <summary>口径 2 的对外名字：静默夹紧（不改写 <see cref="Target"/>）。</summary>
    internal void ReclampSilently() => UpdateEffective();

    private void OnPressed(object? sender, PointerPressedEventArgs e) {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
            return;
        }
        dragging = true;
        dragStartX = e.GetPosition(this).X;
        dragStartTarget = Target;
        dragAvailable = AvailableWidth();
        dragReserved = ReservedWidth();
        e.Pointer.Capture(HitArea);
        Classes.Set("dragging", true);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e) {
        if (!dragging) {
            return;
        }
        ApplyDragDelta(e.GetPosition(this).X - dragStartX);
        UpdateEffective();
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e) {
        if (!dragging) {
            return;
        }
        dragging = false;
        Classes.Set("dragging", false);
        e.Pointer.Capture(null);
        DragCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) {
        ResetToDefault();
        e.Handled = true;
    }

    private double AvailableWidth() => (Parent as Visual)?.Bounds.Width ?? 0;

    /// <summary>
    /// 其它固定占用（**无环口径**，task-28 实测踩过"两面板互相夹紧 → 布局无限循环"）：
    /// 1. 所有兄弟分隔条按 <see cref="SelfReservedWidth"/> 恒占位（不看可见性 —— 避免"折叠腾出 7px →
    ///    又够宽 → 重新展开"的振荡；也保证 `CenterMin` = 中央列**净**宽）；
    /// 2. 按 **PanelColumn 升序**给面板定优先级：
    ///    - 更靠左的面板（`PanelColumn &lt; 我的`）按其**当前有效宽**预留（它只依赖更左的 + 右侧的 Min ⇒ 稳定）；
    ///    - 更靠右的面板按其 **Min** 预留（不依赖它当前宽 ⇒ 切断互相依赖，天然无环）；
    ///    - 兄弟分隔条不可见（宿主已折叠面板）⇒ 其面板不占位（只占那 7px）。
    /// 3. 非面板的固定单列（分隔线等）按其实际 Bounds 计入；跨列覆盖层（视图宿主）与星号列不计；
    ///    被兄弟分隔条管理的面板列（其 `PanelColumn`）不重复计入。
    /// 由此 `可用 − reserved − CenterMin` 只需 1~2 个布局帧就收敛，且中央列净宽恒 ≥ CenterMin。
    /// </summary>
    private double ReservedWidth() {
        if (Parent is not Grid grid) {
            return SelfReservedWidth;
        }
        int starColumn = -1;
        for (int i = 0; i < grid.ColumnDefinitions.Count; i++) {
            if (grid.ColumnDefinitions[i].Width.IsStar) {
                starColumn = i;
                break;
            }
        }
        double sum = SelfReservedWidth;
        var managedColumns = new List<int>();
        foreach (var child in grid.Children) {
            if (ReferenceEquals(child, this) || child is not PanelSplitter s) {
                continue;
            }
            sum += s.SelfReservedWidth;                       // 1：所有分隔条恒占位
            if (s.PanelColumn < 0) {
                continue;
            }
            managedColumns.Add(s.PanelColumn);                // 该列由兄弟分隔条管理，稍后不重复计入
            if (!s.IsVisible) {
                continue;                                     // 宿主已折叠该面板 ⇒ 不占宽
            }
            sum += s.PanelColumn < PanelColumn
                ? s.PanelWidth                                // 2a：更靠左 ⇒ 用其当前有效宽
                : s.Min;                                      // 2b：更靠右 ⇒ 只按最小宽预留
        }
        foreach (var child in grid.Children) {
            if (ReferenceEquals(child, this) || child is not Control c || !c.IsVisible) {
                continue;
            }
            if (c is PanelSplitter) {
                continue;                                     // 已在上面计入
            }
            int column = Grid.GetColumn(c);
            if (Grid.GetColumnSpan(c) != 1 || column == starColumn) {
                continue;                                     // 跨列覆盖层 / 中央星号列
            }
            if (column == PanelColumn || managedColumns.Contains(column)) {
                continue;                                     // 我的面板 / 兄弟面板（口径 1 + 去重）
            }
            sum += c.Bounds.Width;                            // 3：固定单列（分隔线等）
        }
        return sum;
    }
}
