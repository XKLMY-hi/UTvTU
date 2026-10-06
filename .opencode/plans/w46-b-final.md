# W46 悬浮工具轨（B）· **环境级阻塞的结论** · fx-verify · 主树（收尾时 HEAD `47385a36`/实例建于 `e985eb65`）

## 一、本轮得到的**根因（环境级，不是产品缺陷）**
1. **我无法把该实例置为前台**：启动主树实例（PID 3256，HWND 7603656）后调用 `SetForegroundWindow`，紧接着 `GetForegroundWindow()` **不等于该 HWND**（打印 `前台==实例? False`）。⇒ 我发出的键盘/鼠标注入**落到别的窗口**（DSH/浏览器所在的会话前台），**注入结果不可信**。
2. 这解释了此前三次"键不切"：不是 `mods`/`match` 的问题，而是**注入目标窗口不是该实例**（也解释了作者基线 vs 我的差异：他在自己会话里跑，前台就是他的实例）。
3. **同时**：Lead 给的现成实例 **PID 17960 在我这边没有可见窗口**（`shot_pid --list` 报"没有匹配的可见窗口"）⇒ 无法接手；我按 Lead 授权用 `stop.ps1 -ProcessId 17960` 关闭（工具报"已不在运行"）后自起实例，仍受第 1 条限制。
4. 本轮判据（VK+scan `Ctrl+2` 后，发键等 600ms）：列 `x=30` 是 `#101417`(44)+`#1C2024`(255) 的**面板/车道交替**、无键盘黑白周期；行 `y=300` 起段 `#1C2024`(77) 后才出现 `#424A51`/`#565F68` 零散墨迹；容器色族区域 77.8% `#1C2024` ⇒ **不构成卷帘证据**。按纪律**记为"未验证/无效"，不写 FAIL**。

## 二、B（工具轨）—— **未验证**（环境阻塞）
5. ①轨存在 ②距左上 **12** ③圆角 **12** + 1px `outline-variant` + **无投影** ④**36×36 + 间距 4** ⑤**1px 分隔线** ⑥**三态深+浅** ⑦**无滚动条 + 第 10 个工具不裁** —— **全部未验证**（无法到达卷帘）。

## 三、混音台链面板标签 —— **未验证**
6. `Ctrl+3` → 折叠 → 右上角 **14×48** / `AnchorRight` 向左展开 / 点击回 **320**：均未验证（同一阻塞）。

## 四、唯一 PASS（视图无关，保持）
7. 顶栏品牌 4× 切片 `w46-E-topbar-zoom.png`：`标记（主题蓝）+ UTvTU（小写 v 在位）+ 分隔线 +「工作台」`。

## 五、建议（把我这条死路换成能走通的三条，按推荐序）
8. **① 交给用户 5 秒目视**（最省）：他按 `Ctrl+2` 看卷帘，回答四个是/否（竖排悬浮 + 距左上约 12？容器圆角与无投影？按钮 36 见方、间距均匀、绘制/音高间有一条分隔线？正常窗口下无滚动条且末尾工具完整？）+ 截深/浅各一张。
9. **② 让作者加 headless 几何/位图用例**（最硬、可回归）：`RenderTargetBitmap` 渲染 `PianoRoll` 的 `ToolRailLayer` 后做像素探针，或直接断言可视树（10 个 `ListBoxItem` 的 `Bounds` = 36×36、相邻间距 4、容器 `CornerRadius=12`、`BorderThickness=1`、无 `BoxShadow`、`ScrollViewer` 的 `Extent≈Viewport`）⇒ **完全绕开前台/注入问题**，且下次能防回归。
10. **③ 若坚持真机注入**：需要一个**拥有前台的会话**（即由用户或不在沙箱会话内的进程发键），并把 `GetForegroundWindow()==目标 HWND` 作为**注入前置断言**（我已在流程里加了这条）。

## 六、收尾
11. 我的实例已 `stop.ps1 -ProcessId 3256` 关闭；`-List` 复核 **主树 0 残留**、**全部 OpenUtau 进程 0**；**主题 Dark 原样**；未改产品代码；本轮证据 `w49-2-ws.png`、`w49-3-ctrl2.png`（均标为"无效轮"）。

## 七、给 #2（headless 断言）的锚点表（只读摘录，供 m1-strip 一一对应；也是我后续只读复核的基准）
12. **容器** `Border.toolRail`（`PianoRollStyles.axaml:47-53`）：`CornerRadius=12`、`Background={DynamicResource md3.surface-container-highest}`、`BorderBrush={DynamicResource md3.outline-variant}`、`BorderThickness=1`、`Padding=4`；同段注释（`:44`）明写"**分组分隔线挂在音高组首项（`.pitchGroup`）顶部**"、"按下 = 轻微不透明度"、颜色一律取 `md3.*` 池键。
13. **列表** `ListBox.toolRail`（`:55-59`）：`Background=Transparent`、`BorderThickness=0`、`Padding=0`；**间距** 在 `PianoRoll.axaml:211` 的 `StackPanel Orientation="Vertical" Spacing="4"`。
14. **按钮** `ListBoxItem.railTool`（`:61-71`）：`Width/Height/MinWidth/MinHeight = 36`、`CornerRadius=8`、`Background=Transparent`、`BorderThickness=0`、`Margin=0`、`Padding=0`；**hover** = `/template/ Border#PART_HoverOverlay` 的 `Opacity=0.12`（`:72-74`）、**pressed** = `0.16` + 控件 `Opacity=0.85`（`:75-79`）—— 与卡片"hover = primary 12% 叠加"一致。
15. **无投影**：以上三处**均无 `BoxShadow` setter** ⇒ 断言可直接判 `BoxShadow == null`。
16. **元素锚点**：容器在 `PianoRoll.axaml:203-207`（`ToolRailLayer` Grid `Row=3/Col=1`；`Margin="12"`；`MaxHeight={Binding #ToolRailLayer.Bounds.Height}`；内层 `ScrollViewer VerticalScrollBarVisibility="Auto"`）；**10 项**在 `:215/216/234/235/236/237/238/239/240/241`（`:236` 带 `pitchGroup` = 分隔线首项）；笔浮层 `:216-232`（`Flyout Placement="Right" ShowMode="Transient"`）。
17. **"无滚动条 + 末项不裁"的 headless 判据**：`ScrollViewer.Extent.Height <= Viewport.Height` + 第 10 个 `ListBoxItem` 的 `Bounds.Bottom <= 容器下端`。⚠ 本仓 headless 渲染是 **stub**（`Path.Bounds` 报 0）⇒ **像素级 hover/选中/描边只能由用户目视**，别把 stub 当"验过"。
