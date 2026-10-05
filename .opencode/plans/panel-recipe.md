# W16 面板系统：接入配方（给 m1-strip / m3-chain 或任何要加"可调宽 + 可折叠"面板的人）

> 出处：task-28（分支 `try/panel`）。控件与口径都在 `OpenUtau/Controls/PanelSplitter.axaml(.cs)`，
> 本文是"别人只加三行就能用"的**唯一权威说明**。四条口径是 m1-strip 只读审 + Lead 裁决定下来的，
> 不是可选建议——任何面板不按这套走都会踩同一个坑（窄窗吸附 / 布局无限循环 / 中央区被吃掉）。

## 0. 心智模型：意图值 ≠ 有效值

| 概念 | 绑定到 | 语义 |
|---|---|---|
| `PanelSplitter.Target`（TwoWay） | 你的 VM 槽 `Width` | **用户意图**：拖动/持久化的值，夹紧**不会**改写它 |
| `PanelSplitter.PanelWidth`（只读） | 面板容器 `Width` | **有效宽**：按当前宿主尺寸夹紧后的值（宿主变宽会自动回到意图值） |
| `PanelSplitter.PanelShown`（只读） | 面板容器 `IsVisible` | 是否显示：控件的 `IsVisible` && 有效宽 ≥ `CollapseThreshold` |
| `PanelSplitter.Min/Max/DefaultWidth/CenterMin/PanelColumn/Invert` | 设计参数 | 见下 |

**面板容器必须绑 `PanelWidth`/`PanelShown`，不要直接绑 VM 的 `Width`/`!IsCollapsed`** ——
否则"宿主变小后静默夹紧"和"放不下自动折叠"这两条就失效了。

## 1. 三行接入（以混音台右侧链面板为例）

现状（`Controls/MixerControl.axaml:48`）：`<Grid Grid.Column="2" ColumnDefinitions="1,280">` + `FxChainHost Width="280"`。

改后：

```xml
<!-- ① 列定义：面板列改 Auto（宽度由容器自己给） -->
<Grid Grid.Column="2" ColumnDefinitions="1,Auto,280">   <!-- 最后一列是给 splitter 的位置，见下 -->
  <Border Grid.Column="0" Name="FxChainDivider" Width="1" .../>
  <!-- ② 一行分隔条：PanelColumn 必填 = 面板所在列 -->
  <c:PanelSplitter Grid.Column="1" x:Name="FxChainSplitter"
                   IsVisible="{Binding #FxChainHost.IsVisible}"   <!-- 或你 VM 的"面板该显示"布尔 -->
                   PanelColumn="2"
                   Invert="True"                                  <!-- 面板在分隔条右侧 ⇒ true -->
                   Min="264" Max="480" DefaultWidth="280" CenterMin="320"
                   Target="{Binding ChainPanel.Width, Mode=TwoWay}"
                   DragCompleted="OnPanelSplitterDragCompleted"/>
  <!-- ③ 面板容器：宽与显隐都取自分隔条 -->
  <ContentControl Grid.Column="2" Name="FxChainHost"
                  Width="{Binding #FxChainSplitter.PanelWidth}"
                  IsVisible="{Binding #FxChainSplitter.PanelShown}"/>
</Grid>
```

配套（三处，各一行）：

```csharp
// A. 你的 VM 里加一个槽（PanelSlot 在 MainWindowViewModel.cs，internal 可见性用 InternalsVisibleTo 或自己 new）
public PanelSlot ChainPanel { get; } = new PanelSlot("fx-chain", 280, 264, 480);
// B. 落盘：拖动结束（DragCompleted）→ 写 Preferences 后 Preferences.Save()；拖动过程中只更新内存
private void OnPanelSplitterDragCompleted(object? sender, EventArgs e) => PersistPanelLayout();
// C. 折叠入口（面板头部 chevron；样式类 Button.panelToggle 见 MainWindow.axaml）：
private void OnCollapseChainPanel(object? sender, RoutedEventArgs e) => viewModel.ChainPanel.ToggleCollapse();
```

持久化字段：在 `OpenUtau.Core/Util/Preferences.cs` 的 `PanelLayoutPreferences` 里加一对
（`MixerChainWidth = 280; MixerChainCollapsed = false;`），**不要另开存储**。
默认值一律"展开"（折叠是用户主动选择）。

`Invert` 速记：**面板在分隔条左边 ⇒ `false`（向右拖变宽）；面板在右边 ⇒ `true`（向右拖变窄）**。
左面板（轨头）走 `false`，右面板（素材库 296→链面板）走 `true`。写反了会"越拖越窄"，W16 的布局测试抓过这个错。

## 2. 四条口径（为什么必须这么绑）

1. **自身列不算保留宽**（`PanelColumn` 必填）：计算 `可用 − 保留 − CenterMin` 时排除面板自己那一列。
   否则窄窗下会把面板自身宽度也减掉 ⇒ 用户拖 1px 就被兜到 Min（吸附）。
   回归断言：`PanelLayoutTests.NarrowWindow_DragOnePixel_DoesNotSnapToMin`（900 宽下必须 272→273）。
2. **宿主尺寸变化后静默夹紧**：`PanelWidth` 每次布局按当前可用宽重算，**不改写 `Target`** ⇒
   宽窗拖到 480 持久化后切到 720 分离窗 → 有效宽被夹到合法值、中央区仍 ≥ CenterMin；窗口变回去 → 恢复 480。
   回归断言：`HostShrink_ReclampsSilently_AndRestoresOnGrow`。
3. **放不下就折叠，不留夹缝**：`MaxAllowed` 允许跌破 `Min`（下限 0）；有效宽 < `CollapseThreshold`（默认 **120px**
   ——轨头卡片要 ≥170、素材库卡片要 ≥140 才不折行，低于 120 只剩夹缝）⇒ `PanelShown=false` + `PanelWidth=0`。
   另：`MainWindow.MinWidth` 已抬到 **800**、`MixerWindow.MinWidth` 抬到 **640**（低于此任何布局都无意义）。
   回归断言：`TooNarrow_AutoCollapsesInsteadOfLeavingACrack`、`VariousWidths_DoNotOverlap_AndKeepCenterOrCollapse`（300/400/720/1000/1440 五档）。
4. **`CenterMin` = 中央列净宽**：所有分隔条（含本控件自己 7px、含当时不可见的兄弟分隔条）**恒占位**，
   所以中央列 `Bounds.Width` 恒 ≥ `CenterMin`，不会因分隔条打折扣。
   回归断言：`CenterMin_MeansNetWidth_SplitterPixelsIncluded`。

**另有一条实现级坑（写在控件注释里）**：`ReservedWidth()` 按 `PanelColumn` **升序**给面板定优先级——
更靠左的面板用其"当前有效宽"预留、更靠右的面板只用其 `Min` 预留。这样每个面板的夹紧只依赖
"更左的当前值 + 更右的最小值"，**天然无环**。早先版本让两个面板互相按当前宽夹紧，
结果两态振荡 ⇒ 实测报 `InvalidOperationException: Infinite layout loop detected`。
`UpdateEffective()` 也因此必须"一次算完 `PanelWidth` 与 `PanelShown`"，不能分两步写。

## 3. 我已经替你验过的部分

- `MainWindow.axaml` 的两处面板（轨头 248 / 素材库 272）已按上面写法接好，可当范本：
  `ColumnDefinitions="Auto,Auto,*,Auto,Auto"` + 两条 `PanelSplitter` + 容器绑 `#XxxSplitter.PanelWidth/PanelShown`。
- 折叠入口：面板头部 chevron（`Classes="panelToggle"`）+ 顶栏「布局」flyout + `工具 → 布局` 菜单；
  三者都会在混音台/卷帘视图下保持可达（顶栏右簇三视图都显示，`ViewSwitcherPolicy.ChromeFor(...).ShowRightCluster`）。
- 布局层测试范本：`OpenUtau.Test/App/PanelLayoutTests.cs`（真实 Window + 真实 LayoutManager，
  断言真实 `Bounds`；不含"隐藏元素 Bounds 会留上次排布值"这种坑——折叠后要看 `PanelWidth` 与邻居占位）。
