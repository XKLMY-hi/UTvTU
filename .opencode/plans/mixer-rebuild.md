# 混音台系统性重构（2026-10）

> 立项：用户指令「既然基本可用了，那就开始做混音台的系统性重构吧（之前的文档有详细的架构要求）」
> 目标：把混音台从"停靠行 + 旧几何 + 插入列表"改造成**视图化的、按设计稿规格的、以效果链为核心的**工作台。
> 依据（唯一权威）：`.opencode/plans/ui-rework-decisions.md`（§3.A 视图 / §3.B 效果器内化 / §5 尺寸规格 / §11-S5 视图切换 / §4 差异裁决）+
> 设计交付包 `.opencode/design/UTVTU-设计交付/`（`spec/Mixer.txt` 748 行、`spec/VST-Plugin.txt`、`tokens.css/json`）。

## 0. 现状 → 目标（已实测的差距）

| 元素 | 现状（代码实测） | 目标（设计规格 / 决策） |
|---|---|---|
| 通道条宽 | `MixerTrackStrip.axaml:5` **56**，圆角 8，内边距 4 | **96** / 圆角 **12** / 内边距 **8** / 列间距 6（`Mixer.txt:43`） |
| 通道强调色条 | 4×16 方块 | 满宽 **3px** 圆角 999（`Mixer.txt:44`） |
| 通道名 | 9px | **11px/12px semibold**（`Mixer.txt:45`） |
| EQ 曲线屏 | **无** | **80×76** 圆角 8（零线在 38px + 曲线）（`Mixer.txt:47-49`） |
| 插入列表 | 有 FX 入口按钮（开 `TrackEffectRack`） | **移除**（决策 B3：链只在混音台右侧面板） |
| 声像 | `PanKnob` 旋钮 | 设计稿是**行**（高 14：标签 8px + 值 9px，如 `L12`）（`Mixer.txt:65-68`）→ 需裁决，见 §2 |
| M/S | 18×14 按钮，font 9 | 高 **20** / 圆角 4 / font **10 bold** / 间距 4；设计稿还有 **R**（`Mixer.txt:70-79`）→ 见 §2 |
| 电平表 | **10 段 LED** StackPanel | **8px 宽连续条**，圆角 999，从底部长起（`Mixer.txt:82-83`） |
| 推子 | 4px 轨 + **26×4 横条**柄 | 区域 **80×500**；轨 4px 圆角 999；柄 **24×14 圆角 4**；**9 条刻度**（宽 8/5 交替，top 0/62/125/187/249/311/374/436/498）（`Mixer.txt:81-94`） |
| 推子读数 | 11px | **9px** 满宽（`Mixer.txt:95`） |
| 主输出条 | `MasterStrip`（66+112 行，几何待核） | **宽 160** / 圆角 12 / 名称 12px bold / 四行 14px：**综合响度（-14.0 LUFS）· 真峰值 · 限制器 · 抖动**（`Mixer.txt:691-717`） |
| 混音台区域 | 停靠在编排区下方（`ShowMixer` 布尔 + `MixerContainer`） | **视图化**：与钢琴卷帘同为顶栏胶囊切换的视图（A1/A4/S5）；可「分离」为独立窗口（A2，已有 detach 基础设施） |
| 效果链 | 独立窗口 `TrackEffectRack`（495 行，VST 槽列表）+ 轨道头 fx 按钮开 `MixFxDialog` | **混音台右侧面板**：表头 = 轨道名（无背景）+「＋」；链行 = 拖拽把手·序号·名称·格式徽标（VST3/内置）·旁通开关；**双击开编辑器**；空态「从素材库 · 效果器 拖入」（B3/B4/B5/B7） |
| 插件入口 | 无独立插件浏览器 | 素材库第四页签**「效果器」**= 插件浏览器；插件路径管理在**素材库与偏好设置两处都有**（B6） |

## 1. 工作流分解（写入范围互斥；Lead 负责集成与冻结接口）

### Wave A（三线并行）

| 线 | 内容 | 负责人 | 写入范围 |
|---|---|---|---|
| **W1 混音台本体规格重绘** | `MixerControl` 内部布局 + 通道条 + 主输出条 + 电平表/推子控件按规格重绘；移除通道条插入/FX 入口；为右侧链面板**预留固定宽度的宿主槽**（见 §1.1 冻结接口） | m1-strip | `Controls/MixerControl.axaml(.cs)`、`Controls/MixerTrackStrip.axaml(.cs)`、`Controls/MasterStrip.axaml(.cs)`、`Controls/MixerMeter.cs`(新)、`ViewModels/MixerTrackStripViewModel.cs` |
| **W2 视图化（S5）** | 顶栏**胶囊视图切换器**（A4：容器 36/内边距 3；选项 30/左右 18/文字 11 semibold）；工作台/钢琴卷帘/**混音台**成为同一窗口的视图；视图级「分离」按钮（A2）+ 状态持久化；`SetChromeForView` 契约扩展 | m2-view | `Views/MainWindow.axaml(.cs)`、`ViewModels/MainWindowViewModel.cs`、`Views/MixerWindow.axaml(.cs)`、`Core/Util/Preferences.cs`、`Controls/PianoRoll.axaml.cs`（仅分离入口相关行） |
| **W3 效果链面板（独立控件，可单测）** | 新建 `FxChainPanel`：表头（轨道名无背景 +「＋」）、链行（拖拽把手·序号·名称·格式徽标·旁通·双击编辑）、空态；**内置伪插件与 VST 同等级**（B2）统一渲染；接入现有 `TrackMixCommands`；双击 → VST 原生窗口 / 内置 → `MixFxDialog`（B5） | m3-chain | `Controls/FxChainPanel.axaml(.cs)`(新)、`Controls/FxChainRow.axaml(.cs)`(新)、`ViewModels/FxChainViewModel.cs`(新)、`Views/TrackEffectRack.axaml(.cs)`（并入/退役）、`OpenUtau.Test/App/FxChainPanelTests.cs`(新) |

### Wave B（Wave A 合并后）

| 线 | 内容 | 负责人 | 写入范围 |
|---|---|---|---|
| **W4 素材库「效果器」页签 + 插件路径** | 第四页签作插件浏览器（扫描结果、搜索、拖入链）；插件路径管理在素材库与偏好设置两处（B6） | m4-lib | `Views/MainWindow.axaml`（素材库区）、`Views/PreferencesView.axaml(.cs)`、`Strings/*.axaml`、`OpenUtau.Test/App/**` |
| **W5 集成** | 把 W3 的面板接进 W1 的宿主槽、统一键/文案、合并、构建与三变体全量测试 | Lead | 集成提交 |
| **W6 独立验证** | 构建/测试复跑 + 几何像素核对（按 §3 清单逐条量）+ computer use 视觉验证 + 对抗审查 | verify | 只读 + 报告 |

### 1.1 冻结接口（Wave A 三线共同契约，改动需 Lead 批准）

1. **右侧链面板宿主**：W1 在混音台布局右侧加一个**固定宽度 280** 的区域，内含 `<ContentControl Name="FxChainHost"/>`（宽度 280、左侧 1px `outline-variant` 分隔线）。W3 的 `FxChainPanel` 作为可独立实例化的控件提供；集成时由 Lead 把 `FxChainHost.Content = new FxChainPanel(...)` 接上。
2. **选中轨道 → 链面板**：链面板显示"当前选中轨道"的链。W1 通过 `MixerViewModel.SelectedTrack`（若已有等价物则沿用）暴露；W3 只消费该属性，不直接改 `MixerViewModel`。
3. **电平数据**：W1 的电平表继续用现有 `LevelTracker`（每轨最终输出，fader+FX 后）+ 33ms 定时器；W3 不碰。
4. **内置效果器列表**：内置三件套 = 伪插件的名字/徽标/旁通/编辑器映射由 W3 定义在一处（`FxChainViewModel` 内的静态描述表），W1 不重复定义。
5. **样式归属**：控件一律走自有 ControlTheme/颜色池（`md3.*`）；**禁止新增应用级 `/template/` 补丁**（前几轮已证明它会压过 ControlTheme）。新增样式只允许写在控件自己的 `Styles` 块或同一 `ControlTheme` 文件内。

## 2. 需裁决的设计↔现状冲突（Lead 裁定，写进实现）

| # | 冲突 | 裁定 |
|---|---|---|
| R1 | 设计稿通道条有「插入」列表；决策 B3 明确移除 | **按 B3**：通道条不显示插入；链只在混音台右侧面板 |
| R2 | 设计稿声像是**行**（标签+值），现状是 `PanKnob` 旋钮 | **按设计稿**：改为 14px 行（标签 8px / 值 9px），**保留拖拽改值**；`PanKnob` 保留给其他界面（轨头/旧混音台路径），不删 |
| R3 | 设计稿 M/S/**R** 三键；现状仅 M/S，且 Core 无"录音待录"语义 | **本轮只做 M/S**（按规格重绘：高 20/圆角 4/font 10 bold）；**不做假按钮**（与"机架不放不可用按钮"同一原则）。R 待 Core 有录音待录语义后再加 |
| R4 | 设计稿另有「效果返回」通道条 | **本轮不做**（方案 B1 明确"只统一效果器、音源不进混音台"，返回总线无对应模型）→ 登记为后续 |
| R5 | 主输出四行（响度/真峰/限制器/抖动）：数据源需要确认 | **先按规格落 UI**，数据按"能拿到的真值 → 拿不到的显示占位并标注"处理：具体见 W1 任务书与 `current-state.md`；**限制器/抖动**若无实现则显示为**状态文本**（当前导出设置）而非编造数值 |
| R6 | 轨道头 fx 按钮（现在开 `MixFxDialog`）与 B7「轨道上不显示链」 | **保留该按钮**（它是内置效果编辑器的快捷入口，不是"在轨道上显示链"）；链的**管理**仍只在混音台右侧面板 |

## 2.1 追加裁决（2026-10，依据两名只读侦察代理的产出）

侦察产出：`.dsh/mixer/spec-digest.md`（设计规格 + 冲突清单，348 行）、`.dsh/mixer/current-state.md`（现状与扩展点，66KB）。

| # | 议题 | 裁决 |
|---|---|---|
| R7 | 设计稿 `Mixer Area` 用 `surface-container-lowest`，决策 §12.3 梯度表只列四档 | **用第五档**：`md3.color.surface-container-lowest` **确实存在**于颜色池（`Md3Role.g.cs:13`）——文档滞后，稍后补表 |
| R8 | **reparent 杀死通道条**（`MixerTrackStrip.axaml.cs:89-101` `Unloaded→DisposeSubscriptions` 置 `_vm=null`，而 VU 与 Pan 写入都以 `_vm!=null` 为门） | **W1 必修**：改成可重复挂载（`Loaded` 重建订阅或不依赖 `_vm` 生命周期）+ 回归测试（卸载→重挂后 VU 仍更新、声像仍写模型）。**W2 的视图化会频繁触发这条路径** |
| R9 | `MixerControl` 定时器构造即启、`Shutdown()` 仅由分离窗 `ForceClose` 触发 ⇒ 隐藏后仍 30fps 轮询 | **W1 必修**：定时器随挂载/可见性启停；W2 负责窗口侧时序，二者以"挂载/卸载通知"协同（必要时由 Lead 居中） |
| R10 | **链行拖拽排序无 Core 支撑**（VST 顺序=`SlotIndex` 且无重排命令；内置为固定 DSP 序） | **拖拽只对 VST 槽生效**；**内置行把手禁用** + tooltip「内置模块顺序固定（EQ → 压缩 → 混响）」，**不做假交互**。授权 W3 在 `Core/Commands/TrackMixCommands.cs` 增**可撤销** `ReorderVstSlot`；若需重建 VST 实例则先给证据再定。**登记后续 Core 课题**：统一链序字段（内置与 VST 同一条可排序链） |
| R11 | 内置模块旁通不可撤销（直写模型），违反 B8 | 授权 W3 增可撤销命令 + `-Undo` 往返测试 |
| R12 | `TrackEffectRack` 开窗即写模型（`??= new()` + 默认 3 槽） | 迁移时必修：**只读呈现，用户动作才写** |
| R13 | `VstEditorWindow` 自绘参数列表与 B5「VST → 原生窗口」冲突 | 核实使用情况；双击链行走**原生 GUI** 路径；自绘列表若无价值则建议退役（写进报告） |
| R14 | 链面板起点 | **别从零写**：`Controls/FxRackPanel.cs` 已是"内置 + VST 统一链"只读基线（孤儿控件、无 View 引用）→ 提升为正式 `FxChainPanel` |
| R15 | 设计稿通道条无描边 / Fader 柄带投影 `0 2px 6px #00000066` / 插入行"亮"态语义 | ① **不加描边**（与"卡片=底+outline-variant"不同，按稿）；② **不实现投影**（违反 MD3 无阴影铁律）；③ 亮态按**"已启用"**理解并迁到链行 |
| R16 | 声像行仅 14 高、拖拽热区不足 | 视觉高 **14 不变**，**命中区可更大**（如 22）；并提供键盘（←/→）与滚轮改值 |
| R17 | 横向余量恒为 0（`12×96+160+12×8=1408=1440−32`）、竖向余量 ≈8px | 通道条行**必须横向滚动**；我冻结的 **280 宽链面板在滚动区之外**（固定右侧）；500px 推子不可压缩，不得再往通道条塞行 |
| R18 | 主输出双表数据源只有单值（`MasterAdapter.ReadAndResetPeakDb`） | **按设计做双表，两表绑同一真实峰值**，报告明写"单值源⇒同值；真 L/R 需 Core 按声道统计"，登记后续 |
| R19 | 混音台原有"＋轨道"工具行与底部 `mixer.tracks` 状态行（设计稿无） | **保留**（删除算功能回归） |
| R20 | 字符串键新增 | W1/W2/W4 各自在 `Strings/*.axaml` **文件末尾独立标记区块**加键（EN/zh 成对，差集 0）；合并时由 Lead 保留全部区块 |



## 附A. 对设计稿的有意偏离（登记）

| # | 偏离 | 理由 | 位置 |
|---|---|---|---|
| D1 | 通道条保留「＋轨道 / 轨道数」**28 高工具行**（设计稿无） | 功能性入口优先（删除算回归）；后果：900 高窗口下通道条实际 **748** 而非稿的 780 ⇒ W6 量像素按"含工具行"判 | `MixerControl.axaml` |
| D2 | 主输出条**加回静音键**（设计稿无 M/S/R） | `PlaybackManager.SetMasterMuted` 否则**没有 UI 入口**（功能回归） | `MasterStrip.axaml`（并入名称行右侧，高 20/圆角 4/10px bold，复用 `mixer.mute`） |
| D3 | 不实现 Fader 柄投影 `0 2px 6px #00000066` | 违反"MD3 无阴影"铁律 | — |
| D4 | 通道条**不加描边** | 设计稿通道条确实无描边（与"卡片=底+outline-variant"的既有约定不同，按稿） | — |
| D5 | 声像**视觉高 14 / 命中区 22** | 14 高拖不动；视觉数值仍严格 14，另提供滚轮与 ←/→ | `MixerTrackStrip.axaml` |
| D6 | 主输出**双表同值**（单峰值源） | 视觉忠实 + 数据诚实；真 L/R 需 Core 按声道统计（后续） | `MasterStrip.axaml` |
| D7 | 综合响度显示 `—`、真峰值标 `dBFS`、限制器/抖动显示真实实现状态 | Core 无 LUFS/真峰/限制器/抖动实现 ⇒ **绝不编数**（R5） | `MasterStrip.axaml.cs` |
| D8 | 内置模块把手**禁用**（只有 VST 可拖拽排序） | 内置为固定 DSP 序（EQ→压缩→混响），**不做假交互**；统一链序字段登记为后续 Core 课题（R10） | `FxChainPanel` |
| D9 | **混音台 overlay 让出素材库列**（`ColumnSpan` 3→2），素材库列在**工作台与混音台都可见**（新增 `ViewSwitcher.ShowLibrary`） | W4 报告的可达性问题：素材库只在工作台可见、而链面板在混音台视图内 ⇒ **「素材库 → 效果器 → 拖入链面板」这条 B3/B6 主路径不可达**。让出 296 后该路径真实可用；通道条区本就横向滚动，少一列宽不影响可用性。**代价**：设计稿的混音台是"满宽"（`Mixer.txt:42`），此处有意偏离；W2 的两条布局契约已按新语义更新（`ViewSwitcherTests`），W6 量像素须按"混音台 = 前两列 + 素材库列常驻"判 | `MainWindow.axaml` + `ViewSwitcher.cs` |

## 附B. 跨线接口冻结（Lead 裁定并实测）

| 主题 | 冻结内容 |
|---|---|
| 控件生命周期 | 分离窗口是**纯宿主**：旧 `ForceClose()`（关窗即 `Shutdown()`）**删除**；`ReleaseControl()`（摘 Content、不 Shutdown）+ `ReturnToHost`（用户关窗→控件收回视图区）；`Shutdown()` **只在 MainWindow 退出时调一次**。`MixerControl` 定时器由 `attached && !shutdown && IsEffectivelyVisible` 驱动。 |
| 视图切换语义 | 切视图**只隐藏宿主 Border、不摘 Content**（不重建控件，通道条 `_vm`/滚动位置/VU 状态保住）；只有**分离/收回**才 reparent。⇒ 切视图靠 `IsEffectivelyVisible` 停表，分离/收回靠 attach/detach 停表。 |
| **合并注意（强制核对）** | `Views/MixerWindow.axaml.cs` 与 `Views/MainWindow.axaml.cs` **必须以 W2 版为准**（W1 未改这两个文件）：若拿去基线版，`MainWindow` 的"分离→贴合"会调已删除的 `ForceClose()→Shutdown()`，把定时器**单向闩死**（贴合后 VU 不再起）。集成时逐行核 `ReleaseControl`/`ShutdownDetachedViews` 存在。 |
| 右侧链面板宿主 | `MixerControl` 布局右侧固定 **280**（在横向滚动区之外）+ 1px `outline-variant` 分隔线 + `<ContentControl Name="FxChainHost"/>`；选中轨道经 `MixerViewModel.SelectedTrack` 传给面板；面板由 W3 提供、Lead 接线。 |

## 附C. 集成发现的缺陷与潜在风险（滚动记录）

| # | 项 | 证据 | 处置 |
|---|---|---|---|
| I1 | **`FxChainViewModel.Rebuild()` 未做 UI 线程编组**：非 UI 线程（如 VST 异步通知）变更绑定给 `ItemsControl` 的 `ObservableCollection` → `Dispatcher.VerifyAccess` 抛 `InvalidOperationException` | 集成树三变体一致 2 例失败（`FxChainReorderTests.*`），栈：`DocManager.ExecuteCmd:270 → Publish:401 → FxChainViewModel.OnNext:599 → Rebuild:350 (Rows.Clear) → ItemsControl.OnItemsViewCollectionChanged → Dispatcher.VerifyAccess`。**W3 自己的树里因用例恰好全在 UI 线程而掩盖**（顺序掩盖型缺陷，生产同样会崩） | 派 W3 修（`Dispatcher.UIThread.CheckAccess` + `Post`）+ 补**顺序无关**的回归用例（从非 UI 线程触发）+ 扫同类点 |
| I2 | **`DocManager.Undo()/Redo()` 缺少 `ExecuteCmd` 那样的主线程守卫**（`DocManager.cs:322+` 仍会 `Publish` 给订阅者） | W1 自查报告：当前调用点全在 UI（`MainWindowViewModel.cs:160/163`、`PianoRollViewModel.cs:274/275`）⇒ 暂安全；一旦有人在后台线程调 Undo/Redo，所有订阅者都会在后台线程改 UI 集合（与 I1 同形） | 登记为后续 Core 课题：给 `Undo/Redo` 补同样的派发 |
| I3 | `TrackEffectRack` 成为"编译干净但运行期不可达"的兜底窗（W1 删入口后） | W3 只读核实：全仓除注释外无构造点 | **休眠保留**（(a) 方案）；若要入口，最便宜是链面板表头加"浮动"按钮（~10 行 + 宿主 `Show()`），登记为可选后续 |
| I4 | 字符串文件三线同写 | W1/W2 合并时按预期冲突（`Strings.axaml`/`Strings.zh-CN.axaml`），标记块并集解决 | 已解决：EN 1013 / zh 1048 个 `x:Key` 匹配、双向差集 0（`checks.ps1` 复核） |
| I5 | **「分离」按钮后应用整机退出**（`System.ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager`） | W6 独立验证（3 次尝试命中 1，间歇；触发历史：切混音台→开内置编辑器→ESC→旁通→Ctrl+Z→3 次视图往返→分离）。异常签名 = 跨窗口 reparent 时仍有挂起 arrange 指向旧 `LayoutManager` | **阻断合并**，派 W9（m2-view）修：真机取最小重现 + 延后一帧 reparent 等候选手段 + ≥6 次回归 |
| I6 | **M/S 实测高 32 ≠ 规格 20**（主静音钮 24×31） | W6 像素核对：`Md3ButtonTheme` 的 `MinHeight=32`（ControlTheme）顶掉本地 `Height=20`（布局取 `Max`） | 派 W10（m1-strip）：本地样式补 `MinHeight` + **把几何断言从"属性"升级为"布局后实际 Bounds"**（headless 绘制是桩、但布局真跑 ⇒ 这条盲区可测） |
| I7 | **`VstScanPaths` 默认空** ⇒ 插件浏览器永远空、B6 形同虚设、D9 拖拽无法验证 | W6：D9"未验证（环境缺插件）"；Lead 实测本机 `C:\Program Files\Common Files\VST3` 有 **32 个 .vst3** | 派 W11（fx-rack）：首次运行播种标准路径（平台分支 + 幂等 + 删掉不复活）+ 空态"添加标准路径并扫描" |
| I8 | W7 测量装置发现：`VstEffect.Process` **音频线程每块分配**（`buffer.Length != count` 恒真 ⇒ `new float[count]`，16 轨≈1376 次/秒） | W7 报告（`VstEffect.cs:81-84`）与"音频线程零分配"验收口径直接冲突 | **已修（W7b，`a387edd4`）**：判据放宽为 `buffer.Length >= count`（原生 `vst_bridge.cpp:352` 只按 frames 访问 ⇒ 可证安全，且**零分配 + 零拷贝**）；实测 **1,060,864 B → 0 B**（512 块）；补不变量用例（尾部不动/offset 分支）+ 干轨基线 0 B |
| I10 | `PathManager.CachePath` 仅在 `PlaybackManager` 构造时创建 ⇒ 任何"先渲染/从不播放"入口（无头批量、CLI、测试宿主）会 `DirectoryNotFoundException` | W7 报告发现 3（fx-core 结论：应用内基本看不到，但渲染层不该依赖播放层副作用） | 登记后续小任务：渲染入口 `Directory.CreateDirectory(CachePath)`（一行、幂等） |
| I11 | `UPart.PhonemesUpToDate` 早于 `renderPhrases` 填充变 true（读侧无锁） | W7 报告发现 4（fx-core：渲染链自身安全——消费全程 `lock(this)`；危险的是外部把它当"就绪"读；W7 夹具实测到中间态 `True/phonemes=2/phrases=0`） | 登记后续：把 `phonemesTimestamp` 赋值移到 `renderPhrases` 构建之后，或明确"仅 lock 内有效"；涉及渲染/撤销语义 ⇒ 单独派单 + 独立验证 |
| I9 | W6 未验证项（需实机/环境） | ① D9 真拖（待 I7 修复后可验）② 双击 VST 行→原生 GUI（无插件）③ 播放中改参数即时可听（无歌手）④ VU 跳动/停表（空工程无电平）⑤ 内置行把手禁用态（无 VST 行对照） | 修 I7 后由验证者补验 ①②⑤；③④ 需用户载入音源后确认 |
| I12 | **Light 变体 1 例偶发失败**（集成树 `521` 用例；复跑同样命令 521/0 ⇒ 顺序/时序相关） | Lead 三变体验证（第一次 Light 520/1，复跑 521/0）。W11 引入的用例会备份/恢复 `Preferences.Default` 且触发后台扫描，属"全局状态型" suspect | 派 W12 时要求 fx-rack 顺手自查新增用例隔离性（尤其"扫描线程仍在跑"的窗口）；复验轮次由验证者 Light 连跑 3 次定性 |
| I13 | W11 真机扫描暴露三条**插件生态健壮性**问题：① `moduleinfo.json` 尾逗号 ⇒ bundle 被 `catch {}` 吞掉；② **无 moduleinfo 的老式 bundle 被整体跳过**（含**我们自己的 `OpenUtau Bridge.vst3`**）；③ 测试宿主缺 `vst_probe.exe` ⇒ 测试环境扫不到单文件 VST3（拷入后 5→15） | W11 报告（真机计数：总数 24 / 效果器 15；首扫 20.9s、缓存后秒开） | 派 W12（fx-rack）：三条全修 + `catch {}` 改带日志 + 扫描计数前后对照；**顺带自查 I12** |



## 3. 验收标准

1. **几何可核对**：§0 表中每条规格都有对应实现，且新增**契约测试**断言关键数值（通道宽 96 / 圆角 12 / 内边距 8 / 强调条 3px / 名称 11 semibold / EQ 屏 80×76 / 电平表 8 宽 / 轨 4 宽 / 柄 24×14 / 刻度 9 条 / 主输出宽 160 / 四行 14px / 胶囊 容器 36·选项 30）。
2. **视图化**：顶栏胶囊在「工作台 / 钢琴卷帘 / 混音台」间切换且**不参与布局抖动**；混音台**不再停靠在编排区下方**；分离按钮可用、状态持久化、重启后恢复。
3. **效果链**：混音台右侧面板列出选中轨道的链；内置三件套与 VST 同一行规格（名称/徽标/旁通）；双击分别打开 `MixFxDialog`（内置，非模态）与 VST 原生窗口；旁通/排序经 `DocManager` 命令（可撤销）；空态文案走键。
4. **无回归**：VU/推子/静音/独奏/声像全部功能正常；播放中改参数仍即时可听（上一轮的 Live 接线不被破坏）；`TrackEffectRack` 的 VST 能力不丢。
5. **工程**：构建 **0 错误**（`TreatWarningsAsErrors` 原样）；全量测试**默认/Dark/Light 三变体各绿**（当前基线 438）；EN/zh 唯一键集合差集为 0；`md3.*` 之外无硬编码色值；无新增应用级 `/template/` 补丁。
6. **独立验证**：`verify` 用 computer use 按 §0 表逐条量像素（通道宽/圆角/电平表/推子柄/刻度/主输出行/胶囊几何），并核对"插入列表确已消失""链面板双击行为""分离/恢复"。

## 附D. 执行记录（本轮，滚动）

| 阶段 | 结果 |
|---|---|
| 规划与冻结 | 本文档 §0 差距表 + §1 分解 + §1.1 冻结接口 + §2/§2.1 裁定；设计规格摘要 `.dsh/mixer/spec-digest.md`（含 §8 的 13 条可核对条目）；现状盘点 `.dsh/mixer/current-state.md`（§8 十八条坑） |
| W1 通道条/主输出规格重绘 | `try/mx-strip`：2 提交（`a5209880` + `961f621c` 主输出静音入口）。含两个必修 bug（reparent 后重建订阅、定时器随挂载×可见性启停）与右侧 280 链宿主槽 |
| W2 S5 视图化 | `try/mx-view`：3 提交（`84494ed8`/`705d227b` + `8aacc621` 崩溃修复）。胶囊 + 卷帘/混音台视图化 + 视图级分离（纯宿主 + `ReleaseControl`/`ReturnToHost`）+ `ViewChrome` 五轴 |
| W3 效果链面板 | `try/mx-chain`：6 提交（Core 可撤销命令 + `FxChainPanel`/`Row`/VM + `TrackEffectRack` 退役为宿主壳 + 19 例 + ✕ 中和 + 胶囊 MinHeight）。**重排用"载荷互换"避开下标重载失败面** |
| W4 素材库效果器页签 | `try/mx-lib`：3 提交（浏览器 + 两处路径同步 + 13 例）→ W11 2 提交（播种 + 首扫 + 5 例）→ W12 4 提交（扫描健壮性 + 用例去全局态 + 8 例） |
| W5 集成（Lead） | `try/mx-int`：合并四线 + `FxChainHost` 宿主接线 + **D9 可达性修复**（混音台让出素材库列）+ 两次字符串冲突并集解决 + 契约同步；**ff 前置已验证** |
| W6 独立验证（第一轮） | `.opencode/plans/mixer-verify.md`：**20 项 18 PASS**（几何像素命中 + 功能路径真实交互 + 对抗审查）；FAIL 2 项 → 派 W9/W10；未验证 1 项 → 派 W11 |
| W7 测量装置 | `try/audio-fixture`：4 提交（`AudioMeasure` 仪器自标定 + 伪声库生成器 + 经典渲染端到端**真跑通**（tone 实测 261.93/293.42/349.42 Hz）+ 纯文本 `.ustxp` fixture + 44 例 / 74 条实测数值） |
| W7b VST 分配修复 | 同分支 2 提交（`a387edd4`）：判据 `Length >= count`（**原生源码证明安全**）⇒ **每块 2072 B → 0 B**（16 轨 ≈2.78 MiB/s 垃圾归零） |
| W9 崩溃修复 | `8aacc621`：定位 Avalonia `_toArrangeAfterMeasure` 竞态 → headless 确定性复现 + 负控 → `DetachAndFlush` → 真机 10 轮零异常 |
| W10 几何与测试盲区 | `970e683b`：M/S 32→20（**两条**干涉：主题 MinHeight + 应用级 Margin）+ **几何断言升级到布局层 `Bounds`** + 金丝雀 |
| W11 插件路径播种 | `bee2ab00`：标准路径播种（幂等/不复活/平台纯函数）+ 注册表空时自动首扫 ⇒ 真机效果器列表 **0 → 15 条** |
| W12 扫描健壮性 | `10c12774`：尾逗号容错 + 老式 bundle 回退 + 测试宿主补 `vst_probe` + `catch{}` 带日志 ⇒ **总数 24→28 / 效果器 15→17**；**并修掉自己造成的偶发 flake**（双语用例改全局语言 → 直读资源字典） |
| 终验（Lead，`76281e37`） | `-t:Rebuild` **0 错误**；全量 **538 通过 / 0 失败**，默认 / Dark / **Light ×2** 四次连跑全绿 |
| W13 复验（fx-verify） | **崩溃 0/7 PASS**（W6 时 1/3；7 次 reparent 零异常、日志零 `Unhandled exception`）；**几何 PASS**（M/S **20**、viewTab **30** + 胶囊 36、libTab 28、＋轨道 56×28）；**插件列表 PASS**（17 条 + 徽标正确，日志 `28 plugins (17 effects) … fallback 2 … vst_probe: ok`）；回归抽样无回退；`Styles/**`+`App.axaml` diff 为空。未自动化验证 2 项：**D9 物理拖拽**（Avalonia `DoDragDropAsync` 走 OLE 回路，合成输入进不去 ⇒ 需用户手拖 5 秒）与 **✕ 22×22 像素**（只在 VST 行显示，需先有 VST 行）；另报一条新偶发 `MixFxSourceTest.Mix_SteadyState_DoesNotAllocate`（1/7，Rebuild 后首跑） |
| 并库后的交界面缺陷（Lead） | 两波合并后测试顺序变化暴露 2 例崩溃：`MixerControl.RebuildStrips → Children.Clear()` 与 `FxChainPanel.set_Track → SetValue` 均在非属主线程被调用。**根因同一条：`Dispatcher.UIThread.CheckAccess()` 在 headless 宿主下误判为 true** ⇒ 判据改为**锚定"所属线程"**（控件挂载时/订阅建立时记录）⇒ **587/0**（`4fb9e2cc`）。另把零分配用例改**双窗口**去掉分层 JIT 偶发（`407325cd`） |
| 终局（`plus-develop`） | `9dd…`→`407325cd`：构建 **0 错误**；全量 **587 通过 / 0 失败 × 三变体**（默认 / Dark / Light 各一次，全部 exit 0） |

## 附E. 后续任务登记（本轮不做）

| # | 项 | 出处 |
|---|---|---|
| L1 | **全仓按钮尺寸扫一遍**：Avalonia 类型选择器精确匹配 ⇒ `Button` 不影响 `ToggleButton`；但所有**声明 <32 高**的 `Button` 类都会被 `Md3ButtonTheme.MinHeight=32` 顶掉（Measure 可被父夹紧、**Arrange 仍尊重 MinHeight**，只有 `Bounds` 暴露）⇒ 逐个类补本地 `MinHeight` + 清 `Margin` | W3/W10 |
| L2 | **统一链序字段**（内置与 VST 同一条可跨类排序的链）——B2"同等级"的完整形态；本轮重排只作用于 VST 段内 | W3 |
| L3 | **开关语言统一**：把 34×20 胶囊折进 `Md3PowerSwitch`（或给陈列室加同款），让全仓只有一个"电源/旁通"控件 | W3 |
| L4 | `PathManager.CachePath` 预创建（渲染入口一行） | W7（I10） |
| L5 | `UPart.PhonemesUpToDate` 时序/锁契约（`phonemesTimestamp` 赋值移到 `renderPhrases` 构建后） | W7（I11） |
| L6 | `OpenUtau Bridge`（我们自己的桥接插件）VST3 子类声明为**乐器** ⇒ 不出现在效果器列表；若要它出现需改插件声明而非扫描器 | W12 |
| L7 | `>100MB` 插件跳过目前是 Information 级日志（是否升 Warning 待用户裁） | W12 |
| L8 | `DocManager.Undo/Redo` 缺主线程守卫（与 I1 同形；当前调用点全在 UI） | W1（I2） |
| L9 | `Apply()` 路径的端到端标脏断言（需可注入的偏好存储 seam，避免测试写用户 prefs） | fx-ui |
| L10 | `.ustxp` 形态：先 C 止血（未知键透传 / 版本墙放宽 / **另存为纯净 .ustx** / 噪音清理 / 迁移收敛），再按用户诉求选 A/A+/B | W8 审计 |

## 4. 已知坑（来自前几轮与 `current-state.md`）

- **单写者纪律**：同一棵树的并发 `dotnet` 会互相锁 DLL（MSB3027/3021）；集成期一人跑构建，换手前必须等"已停"。
- **样式优先级**：应用级 Style setter > ControlTheme setter；应用级 `/template/` 补丁会压过主题（前几轮已清 9 条，勿再加）。
- **headless 桩绘制**：`UseHeadlessDrawing` 下 `RenderTargetBitmap` 为空白 ⇒ **不能写像素断言**，改断言属性/几何/契约；真实像素由 `verify` 用 computer use 做。
- **跨测试全局总线**：`MessageBus` 全进程共享且 xUnit 并行 ⇒ "计次"型用例必须按轨道/对象标识过滤（上一轮已踩）。
- **reparent 生命周期**：`MixerControl` 在窗口与视图间移动时，VU 定时器/订阅必须随挂载启停（参考 `PianoRoll` 的既有做法）。
- **本次运行的应用会锁 bin**：主工作区正在跑预览实例，构建/合并一律在**工作树**里做；需要重建主工作区时先请用户关掉预览。

## 5. 不做（本轮）

- 编排区（S2）、轨头（S3）、素材库细化（S4）的规格重绘 —— 仍按 §11 排期（素材库「效果器」页签除外，见 W4）。
- FL 式内部窗口与布局保存（A3 明确不做）。
- 效果返回总线（R4）、录音待录 R 键（R3）、模板/风格 chips（C3 已作废）。
- Lucide 图标全量迁移（图标仅在需要的元素上按现有 Phosphor 键取用）。
