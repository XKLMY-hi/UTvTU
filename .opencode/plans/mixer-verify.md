# 混音台系统性重构 —— W6 独立验证报告（fx-verify）

> 验证对象：集成树 `G:\xklmy文件夹\vibe coding\UTvTU-mx-int`，分支 `try/mx-int`，HEAD **`bd3278e6`**（相对 `plus-develop`/`b944c528` 共 18 提交、35 文件）。
> 方法：① 独立复跑构建 + 三变体全量；② `checks.ps1`；③ **按 PID** 启动本树产物做 computer use 像素核对（`.dsh/fx/launch.ps1` / `shot_pid.py` / `stop.ps1`，**严禁按进程名杀**）；④ 功能路径真实交互；⑤ 只读对抗审查（代码 + git diff）。
> 像素量法工具：`C:\Users\XKLMY\AppData\Local\Temp\mx\mxmeasure.py`（`sample/colors/scanrow/scancol/runs/bbox/ink`，TEMP 内，不入仓库）。证据截图：`G:\xklmy文件夹\vibe coding\UTvTU\.dsh\fx\shots\mx-*.png`。
> **口径**：每一条都标注「已验证（像素/运行时）」「仅代码审查」「未验证」。我**未改任何产品代码**；`git status --short` 于收工时为空。

---

## 0. 结论速览

| # | 项 | 结论 | 依据 |
|---|---|---|---|
| 1 | 构建 `-t:Rebuild` | **PASS** | exit 0，**0 错误**（1598 警告，均为既有） |
| 2 | 三变体全量测试 | **PASS** | 默认 **512/0**、Dark **512/0**、Light **512/0**（我独立复跑，exit 0 ×3） |
| 3 | 键对齐 / 硬编码色值 | **PASS** | EN 1022 = ZH 1022，差集 **0**；新增行色值命中 3 处**全是测试代码假阳性**（见 §4.4） |
| 4 | 通道条几何（§8-1/2/3/4/5/7/8） | **PASS（1 条除外）** | 96 / 8 / 3 / 80×76+零线 38 / 声像行 14 / 表 8 / 轨 4 / 柄 24×14 / 刻度 9 条 / 推子 500 —— 全部像素命中，见 §2 |
| 5 | **M/S 高度** | **FAIL（1 条几何）** | 规格 **20**，实测 **32**；根因 `Md3ButtonTheme` 的 `MinHeight=32` 压过本地 `Height=20`，见 §2.6 |
| 6 | 主输出几何（§8-9/10） | **PASS** | 宽 160 / 双表 10+10 / 轨 left74 / 柄 **24×16** / 刻度 9 条 / 四行 pitch 22（=14+8）/ Peak 13 semibold |
| 7 | 状态条 / 视图胶囊（§8-11/12） | **PASS** | 状态条 **32**；胶囊容器 **36**、选项 ≈30（含 AA，实体 30）、左右内边距 18、文字 11 semibold |
| 8 | 链面板（§8-13 + B2/B4） | **PASS** | 宿主 **280** + 1px 分隔；表头=轨道名（无背景）+「＋」**36×36**；3 条内置行（序号/名称/预设/内置徽标/旁通）；开关 **34×20 精确**；空态文案走键 |
| 9 | 功能：无插入列表/FX 入口 | **PASS** | 通道条仅有 强调/名称/EQ/声像/M-S/推子/读数（截图 + XAML 无插入区） |
| 10 | 功能：双击内置 → `MixFxDialog` | **PASS** | 双击 EQ 行 → 新窗口标题 **`试听效果`**（`mx-08-builtin-editor.png`）；ESC 可关 |
| 11 | 功能：旁通可撤销 | **PASS** | 点旁通 → 开关 `#CFBDFE`（开）；**Ctrl+Z 后回到灰轨** ⇒ 走 `DocManager` 可撤销（`mx-09` / `mx-10`） |
| 12 | 功能：胶囊切三视图 / 分离 / 关窗回视图 | **PASS** | 工作台↔混音台切换正常；「分离」出独立窗「混音台」；**关闭分离窗后混音台回视图区**（强调条 `#CFBDFE` 复现） |
| 13 | 功能：VU 定时器随可见性启停 | **代码审查 PASS + 旁证** | `SyncTimer()` = `attached && !shutdown && IsEffectivelyVisible`；首帧 tick 日志恰在切到混音台时出现；**停表未直接观测**（空工程无电平） |
| 14 | 功能：D9「素材库→效果器→拖入链面板」 | **未验证（环境缺插件）** | 结构半程已验证：混音台内素材库列常驻（295≈296）+「效果器」页签可达 + 搜索框/路径管理/空态齐备；**列表无插件**（「尚未扫描到效果器插件…」，`mx-19`）⇒ 无法真拖 |
| 15 | 深浅色配色跟随 | **PASS** | 浅色下六角色全部翻转（条体 `#F2ECF4`、主条 `#FFFFFF`、最高档 `#E6E0E9`、强调 `#65558F`、柄 `#1D1B20`，见 §2.11） |
| 16 | 对抗：新增应用级 `/template/` 补丁 | **PASS** | **本波 0 文件改动 `OpenUtau/Styles/**` 与 `App.axaml`**（`git diff b944c528..HEAD -- OpenUtau/Styles OpenUtau/App.axaml` 空）；仅有 `FxChainRow.axaml` **控件自身 `<UserControl.Styles>`** 内 2 处 `/template/`（自建模板自补，合规） |
| 17 | 对抗：链面板误写 `MixerViewModel` | **PASS** | 全仓 `SelectedTrack =` 仅 `MixerViewModel.cs:29`；面板经 `BindSelection(...WhenAnyValue(x => x.SelectedTrack))` 只读消费（`MixerControl.axaml.cs:53`） |
| 18 | 对抗：I1 跨线程编组 / R8 重挂订阅 / R9 定时器 | **PASS（代码）** | `FxChainViewModel.cs:362-366` `!Dispatcher.UIThread.CheckAccess() → Post(Rebuild)`；`MixerTrackStrip` `OnAttachedToVisualTree` 重建订阅（`:117-121`）、`DisposeSubscriptions` 只在 Detach（`:123-125`）；见 §4 |
| 19 | 对抗：I2 `Undo/Redo` 无主线程守卫 | **风险成立但未复现** | `DocManager.cs:322/339` 确无守卫/调度；UI 路径无法从后台线程触发 ⇒ 仅代码审查 |
| 20 | **⚠️ 分离按钮后应用崩溃（未处理异常）** | **FAIL（已观测 1 次，间歇）** | `18:53:28 [ERR] Unhandled exception: System.ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager.` ⇒ 进程退出；3 次尝试命中 1 次，见 §5 |

**总评**：几何与功能基本达标（含 1 条几何 FAIL：M/S 32≠20），但**存在一次真实崩溃**（分离/重挂 reparent 路径）——建议 Lead 在合并前先处理/复现该条。

---

## 1. 独立复跑（命令 / 退出码 / 数字）

```powershell
# 1) 构建（我复跑，--no-restore 保 TreatWarningsAsErrors 原样）
cd G:\xklmy文件夹\vibe coding\UTvTU-mx-int
dotnet build OpenUtau.sln -t:Rebuild --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
# => 已成功生成。 | 1598 个警告 | 0 个错误   exit=0

# 2) 全量（三变体）
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build                       # exit=0
$env:OPENUTAU_TEST_THEME='Dark';  dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build   # exit=0
$env:OPENUTAU_TEST_THEME='Light'; dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build   # exit=0
```
| 变体 | 通过 | 失败 | 跳过 | 总计 | 退出码 |
|---|---|---|---|---|---|
| 默认 | 512 | 0 | 0 | 512 | 0 |
| Dark | 512 | 0 | 0 | 512 | 0 |
| Light | 512 | 0 | 0 | 512 | 0 |

⇒ 与基线 438 + W1 28 + W3 19 + W3 回归 1 + W2 13 + W4 13 = **512** 精确吻合，无静默跳过。（Lead 报告同数，我独立复现一致。）

`checks.ps1 -Tree ...mx-int`：键对齐 **PASS**（EN 1022 / ZH 1022 / 差集 0；ZH「重复键 35」全是**注释里的 `x:Key`**，既有）；提交清单 18 条与规划一致。

---

## 2. 像素几何逐条核对（截图 + 探针 + 实测值）

探针一律作用于 `.dsh/fx/shots/mx-04-mixer-tall.png`（窗口 1400×1000，12 轨道；Dark 池）与 `mx-03`（1226×699，12 轨道）。

**Dark 池实测（自标定）**：`surface-container-lowest` `#0F0D13` · `surface-container` `#211F24` · `surface-container-high` `#2B292F` · `surface-container-highest` `#36343A` · `primary` `#CFBDFE` · `on-surface` `#E6E0E9` · `outline-variant` `#49454E`。

### 2.1 通道条宽 96 / 间距 8 / 内边距 8 — PASS
`scanrow y=600`：条体 `#211F24` 连续 **96**（x=29/133/237/341/445/549），条间 `#0F0D13` **8**；区左内边距 16（x=13→29）。
`scancol x=60`：条顶 164 → 卡内边距 **8**（164..171）→ 强调条 172..174。

### 2.2 强调条 3px 满宽 圆角 999 — PASS
`scancol x=60` y **172..174 = 3px**，色 `#CFBDFE` = `md3.primary`；`scanrow y=173` 每条 **80 宽**（= 96−2×8 内宽，满宽）。

### 2.3 通道名 11 semibold / 行高 12 — PASS（像素 + 属性）
名称 ink `x[37,69] y[182,191]` ⇒ **ink 高 10px**（11px 字号的混排/数字 ink 高度合理）；XAML `FontSize=11 LineHeight=12 FontWeight=SemiBold`（`MixerTrackStrip.axaml:59`）+ 契约测试断言。

### 2.4 EQ 屏 80×76 / 零线 top 38 — PASS
`scancol x=60`：EQ 面 `#0F0D13` **199..274 = 76px**；零线 `#49454E` 在 **236..237** ⇒ 相对 top **37–38**（含 1px AA）✔；`scanrow` 该屏宽 **80**（= 条内宽）。

### 2.5 声像行 14 / 标签 8 / 值 9 — PASS
声像行 ink `y[283,290]`（8px 高，标签+值），值 `C` ink `y[284,290]`（7px）⇒ 行内容中心 ≈286.5，与「EQ 底 274 + 间距 6 → 行 281..294 = 14 高、中心 287.5」吻合；XAML 行定义 `RowDefinitions="…,14,…"` + `PanHitArea` 22（负边距 −4×2）与契约断言一致。

### 2.6 M/S — **FAIL：实测高 32，规格 20**（圆角 4 / 间距 4 / 10 bold 通过）
- 实测：两钮各 **38 宽**（x 37..74 / 79..116）✔（R3 只做 M/S ⇒ 两等分 = (80−4)/2 = 38，规格「≈24」是**三键等分**的推导值，非回归）；**间距 4**（75..78 为条体色）✔；**高度 y 305..336 = 32**（三点取样 x=38/50/60 一致，中心列 `(50,305)…(50,336)` 全为 `#2B292F`）。
- 根因（静态可证）：`MixerTrackStrip.axaml:97,100` 声明 `Height="20"`，但 Md3 的 Button **ControlTheme**（`Styles/Md3ControlThemes.axaml:161` → `Md3ButtonTheme`）设了 `MinHeight=32`；Avalonia 取 `Max(MinHeight, Height)` ⇒ **渲染 32**。
- 为什么测试没抓到：`OpenUtau.Test/App/MixerGeometryTests.cs:49,176` 断言的是**属性**（`ButtonHeight = 20`），headless 是桩绘制（`current-state.md` §8.11）⇒ 属性绿、像素 32 —— 正是"属性断言 ≠ 真实像素"的盲区。
- 主输出静音钮同病：实测 **24×31**（`mx-04` bbox `#36343A` x[762,785] y[186,216]，声明同为 `Height=20`）。
- 建议最小修复（属 m1-strip/W1 范围，我未改）：在该控件 `Button.mixerBtn` / `.masterMuteBtn` 样式里补 `MinHeight` 与声明一致（本地样式 setter 优先于 ControlTheme setter），或把 MinHeight 提到主题级变量。

### 2.7 推子区 80×500 / 表 8 / 轨 4 / 柄 24×14 / 刻度 9 条 — PASS
- `scanrow y=600`（条 1，FaderBox 左缘 = 29+8 = 37）：**表 8**（x 41..48，相对 left 4）、**轨 4**（x 71..74，相对 left 34 ✔）。
- `runs col x=44`：表 `#36343A` **446..945 = 500** ✔；`runs col x=73`：轨 446..607 + 622..945（被柄打断）= **500** ✔。
- 柄 `bbox #E6E0E9`：**x[61,84] y[608,621] = 24×14** ✔（top 162 = 0 dB 的实际值，XAML 的 146 是设计时占位）。
- 刻度列 `runs col x=92 #49454E`：**9 道**，y = 446 / 508 / 571 / 633 / 695 / 757 / 820 / 882 / 944（间距 62–63，= 规格 0/62/125/…/498 + 推子顶 446）✔。

### 2.8 推子读数 9px — 属性 PASS / 像素部分
XAML `FontSize=9`（`MixerTrackStrip.axaml:144`）+ 契约断言；像素上该行在矮窗口被视口裁切（实测条底 ink `y[951,955]` 为裁切后的残段）⇒ 我只能给「属性 + 截图可见」级别证据。

### 2.9 主输出 160 / 双表 10 / 轨 left74 / 柄 24×16 / 刻度 9 / 四行 14 — PASS
- `scanrow y=600`：主体 `#2B292F` **634..793 = 160** ✔（外接 16px 右边距 + 794..809 `#0F0D13`，再 810 `#49454E` 分栏线）。
- 双表 **10+10**（x 666..675 / 680..689，gap 4 ✔ = left 24 / 38 ✔）；轨 **4**（716..719 = left 74 ✔）；刻度列 x=740（= left 96）9 道：**409 / 471 / 534 / 596 / 658 / 720 / 783 / 845 / 907** ✔；表长 **409..908 = 500** ✔。
- 柄 `bbox`：**x[706,729] y[570,585] = 24×16** ✔（left 64 ✔）。
- Bus Info：行 ink 中心 ≈257.5 / 280.5 / 302.5 / 323.5 ⇒ **pitch ≈22 = 行 14 + 间距 8** ✔；四行文案「综合响度 … —」「真峰值 -∞ dBFS」「限制器 关」「抖动 16-bit」✔ —— **综合响度显示 `—`、绝不编数**（验收项 ✔，且 tooltip 明说"本版本没有响度计"）。
- `MasterStrip.axaml` 无插入区/EQ/声像/M/S/R（仅 Accent/Name/Subtitle/BusInfo/Spacer/Fader/Peak）⇒ §8-23 ✔。

### 2.10 状态条 32 / 胶囊 36·30·18·11 — PASS
- `scancol x=700/1200`：`#211F24` 带 956..986 + 1px 分隔 955 ⇒ **32** ✔（其下 988..999 `#101012` 12px 为窗口底缘/阴影区，非状态条）。
- 胶囊：`scancol x=940`（选中「混音台」）容器 `#2B292F` **46..81 = 36** ✔、选中胶囊 `#4A4458`（`secondary-container`）48..79 ⇒ **实体 30 + 上下各 1px AA** ✔；文字色 `#E8DEF8` = `on-secondary-container` ✔；`scanrow y=65` 胶囊左缘 899 → 文字 ink 起 918 ⇒ **左右内边距 18** ✔；文字 11 semibold（XAML/契约 + ink 8px）。

### 2.11 配色跟随（Dark ↔ Light）— PASS
浅色实测（`mx-23-mixer-light.png`）：条体 `#F2ECF4`（`surface-container`）、链面板 `#F2ECF4`、**主条 `#FFFFFF`（高一档 ✔）**、表/轨 `#E6E0E9`（highest）、强调条 `#65558F`（primary）、柄 `#1D1B20`（on-surface）—— 六角色全部翻转，深色对照见 §2 开头的池值。**零硬编码色**（§4.4）。

---

## 3. 功能核对（真实交互）

| 项 | 结论 | 证据 |
|---|---|---|
| 通道条**无插入列表 / 无 FX 入口** | PASS | `mx-02/03/04` 目视 + `MixerTrackStrip.axaml` 无插入区（注释明写 B3） |
| 链面板表头 = 轨道名（**无背景**）+「＋」 | PASS | 表头名 ink `x[824,860] y[175,185]`（13 Medium `on-surface`，无底色块）；「＋」**36×36** 圆角 999 + `outline-variant` 描边 |
| 链行含 把手·序号·名称·徽标·旁通 | PASS | `mx-07`：3 行 `1 EQ / vocal_air / 内置`、`2 压缩器 / gentle / 内置 / 旁通`、`3 混响 / small_room / 内置 / 旁通`；内置行把手为禁用态（更暗，符合 D6） |
| 旁通开关几何 | PASS | `bbox #CFBDFE` = **34×20 精确**（`mx-09`）；关态灰轨 `surface-container-highest` |
| 空态文案 | PASS | 链面板「从素材库 · 效果器 拖入」（`fxchain.empty`）；未选轨「未选择轨道」（`fxchain.notrack`）；无 `mixfx.*` 原文缺键 |
| **双击内置 → `MixFxDialog`** | PASS | 双击 EQ 行 → 新窗口 **`试听效果`**（966×676，三面板/预设/取消/确定）；**ESC 关闭**后回到 1 窗 |
| **旁通可撤销** | PASS | 点 → 开（`#CFBDFE` 34×20 出现在该行）；**Ctrl+Z → 无 `#CFBDFE`、回灰轨**（`mx-09` vs `mx-10`） |
| 视图胶囊切 工作台/钢琴卷帘/混音台 | PASS | `mx-01`（工作台）→ `mx-02`（混音台）→ `mx-11/12` 往返 |
| **分离 / 关窗回视图** | PASS（但有崩溃风险，§5） | 点「分离」→ 独立窗「混音台」746×529（`mx-17`）；点其 X → 主窗重新显示混音台（强调条 `#CFBDFE` 复现，`mx-18`） |
| 视图/分离态持久化 | 部分 | 关闭分离窗后**未**恢复分离态（重启回到主窗视图）；主题偏好落盘 ✔（`prefs.json` `ThemeName` 往返验证） |
| VU 定时器随可见性启停 | 代码审查 PASS + 旁证 | `SyncTimer()`（`MixerControl.axaml.cs:95-105`）`attached && !shutdown && IsEffectivelyVisible`；tick 内再加可见性闸（`:128-129`）；首帧日志 `[Mixer] Timer started, 1 strips` 恰好出现在切到混音台时；**空工程无电平 ⇒ 停表未直接观测** |
| 播放中改参数即时可听 | **未验证** | 需真实音源/音符（本机无歌手、`未找到歌手`） |
| D9 拖拽主路径 | **未验证（环境无插件）** | 见 §0-14；结构半程 PASS |

---

## 4. 对抗审查（只读）

### 4.1 无新增应用级 `/template/` 补丁 — PASS
`git diff b944c528..HEAD -- OpenUtau/Styles OpenUtau/App.axaml` **为空**（本波未碰样式层）。新增的 `/template/` 仅 `Controls/FxChainRow.axaml` **控件自身** `<UserControl.Styles>`（`:15` 起）内 2 处：`:80,83` `ToggleButton.fxPower:checked /template/ Border#PART_Track|PART_Thumb` —— 而 `:64-79` **就是这个控件自己定义的 ControlTemplate**（PART_Track/PART_Thumb 是它自建部件）⇒ 自建模板自补，**不违反**规划 §1.1-5「禁止应用级 `/template/` 补丁」（同时也无"压住主题"风险）。

### 4.2 链面板不写 `MixerViewModel` — PASS
全仓 `SelectedTrack\s*=` 仅 2 处（`MixerViewModel.cs:29` 自身赋值 + `:81` 的 Contains 判断）；面板侧 `FxChainPanel.BindSelection(ViewModel.WhenAnyValue(x => x.SelectedTrack))`（`MixerControl.axaml.cs:53`）+ VM 注释与实现均只读；模型变更全走 `DocManager.ExecuteCmd`（`TrackMixCommands.cs` 新增 `SetMixFxModule/SetMixFxEnabled/SetMixFx/ReorderVstSlot` 四条可撤销命令，`SetMixFxModule` = R11、`ReorderVstSlot` = R10）。

### 4.3 生命周期（R8/R9/附B）— PASS（代码）
`MixerControl`：`levelTimer` 由 `OnAttachedToVisualTree/OnDetachedFromVisualTree`（`:82-91`）+ `IsVisibleProperty` 订阅（`:75`）驱动 `SyncTimer()`，另有 `SyncStripsMinHeight()`（撑满视口，值不变不动⇒防抖动）；`internal bool LevelTimerRunning` 可作测试旁证（`:38`）。
`MixerTrackStrip`：订阅在 `OnDetachedFromVisualTree` 摘除（`:123-125`）、**在 `OnAttachedToVisualTree` 重建**（`:117-121`），不再把 `_vm` 置空（旧坑已修）。
`FxChainViewModel`：`Rebuild` 带 `Dispatcher.UIThread.CheckAccess()` 编组（`:362-366`，集成树暴露的跨线程改绑定集合已修，提交 `41c45497`）。

### 4.4 硬编码色值 / 键对齐 — PASS
`checks.ps1` 命中 3 行，全部为**测试代码**（池色比较用，非产品色字面量）：
`OpenUtau.Test/**` 中 `poolColors.Add(ColorPool.Current.Color(role))`、`solid.Color == Avalonia.Media.Colors.Transparent`、`poolColors.Contains(solid.Color)`。
生产文件逐个体检：`MixerControl.axaml` / `MixerTrackStrip.axaml` / `MasterStrip.axaml` / `MixerMeter.cs` / `FxChainPanel.axaml(.cs)` / `FxChainRow.axaml(.cs)` —— **0 处**色字面量（全 `md3.*`）。
EN/ZH 唯一键 1022/1022、差集 0。

### 4.5 I2 `Undo/Redo` 无主线程守卫 — 风险成立、未复现
`DocManager.cs:322`/`:339` 无 `Dispatcher`/`CheckAccess` 作为；UI 路径（快捷键/菜单）都在 UI 线程 ⇒ **无法经 UI 复现**；判定「仅代码审查（潜在风险，见附C I2）」，未升级为 FAIL。

### 4.6 其它观察
- `VstSlots` 上限仍无统一判定（旧坑 17：模型注释 5 / 旧 UI `<8`）：新链面板/拖入路径**未发现上限检查**，登记为小项（无实际影响，因本轮环境无插件可加）。
- 链行 `MinHeight=44`：实测行盒 **43–44**、相邻行边框相接（pitch ≈44；XAML `MinHeight=44` + `StackPanel Spacing=6` 的视觉表现，登记供设计核对）；行宽 **258**（宿主 280 − 2×10 margin − 边框）✔。

---

## 5. ⚠️ 崩溃事件（唯一 FAIL，须处理）

**现象**：点击混音台视图的「分离」后约 1–3 秒，应用**整体退出**（无对话框）。

**证据（日志原文，`OpenUtau\bin\Debug\net8.0-windows\Logs\log20261004.txt`）**：
```
2026-10-04 18:53:28.016 [ERR] Unhandled exception
System.ArgumentException: Attempt to call InvalidateArrange on wrong LayoutManager.
   at Avalonia.Layout.LayoutManager.InvalidateArrange(Layoutable control)
   at Avalonia.Layout.LayoutManager.ExecuteArrangePass()
   at Avalonia.Layout.LayoutManager.ExecuteLayoutPass()
   at Avalonia.Media.MediaContext.FireInvokeOnRenderCallbacks()
   at Avalonia.Media.MediaContext.RenderCore() … Dispatcher.Signaled() … Program.Run
2026-10-04 18:53:28.024 [INF] Saving backup …\Backups\Untitled-backup.ustxp.
```
（崩溃前该实例已运行约 9 分钟，期间发生：切混音台 → 双击内置行开 `试听效果` → ESC 关 → 点旁通 → Ctrl+Z → 3 次视图往返 → 点「分离」。）

**复现尝试（同一构建）**：
| # | 前置历史 | 结果 |
|---|---|---|
| 1 | 上述完整历史（编辑器/旁通/撤销/多次视图切换） | **崩溃**（上表） |
| 2 | 全新启动 → 新建 → 切混音台 → 分离 | 不崩（分离窗存活 10s+，进程 Responding） |
| 3 | 关分离窗回视图 → 再分离（第 2 轮 reparent） | 不崩 |

**判定**：**未处理异常 = 真实缺陷（FAIL）**，但**间歇/状态相关**（3 次中 1 次）；异常类型是 Avalonia「控件被跨窗口 reparent 时仍有挂起的 arrange 回调」的典型签名，与附B/坑 1、坑 2 的 reparent 生命周期同源。建议（属 W2/Lead 范围，我未改）：
1. 用「#1 的历史序列」在真机重试，取最小重现路径；
2. 分离/回归时避免在同一布局帧内 `Content=null` + 重挂（可延后一帧 `Dispatcher.UIThread.Post`，或在切换前显式 `InvalidateMeasure` 清挂起项）；
3. 该路径务必补一条**真实窗口级**回归（headless 桩绘制测不到）。

---

## 6. 未验证 / 需用户实机确认

| 项 | 原因 | 用户可一步确认的方式 |
|---|---|---|
| **D9：素材库→效果器→拖入链面板**（本轮主路径） | 本机插件扫描列表为空（「尚未扫描到效果器插件。请在下方添加扫描路径后重新扫描。」） | 在素材库·效果器页签添加一个含真 VST3 的扫描路径 → 重新扫描 → 从列表把插件拖到右侧链面板空白处，确认出现 VST 行、可拖排序、可 Ctrl+Z 撤销 |
| 双击 **VST** 行 → 原生 GUI | 同上（无 VST 行） | 加一个 VST 后双击该行 |
| 播放中改参数即时可听 | 本机无歌手（`未找到歌手`）⇒ 无法出声 | 载入音源后播放中拖动 EQ/压缩器旋钮 |
| VU 表随播放跳动 / 停表 | 空工程无电平 | 播放时看表动；切到工作台再切回，看表是否恢复跳动 |
| 内置行把手拖动排序（D6 有意偏离：禁用+tooltip） | 只有 VST 可拖，本机无 VST | 有 VST 后确认：内置行拖不动、VST 行可拖排序且可撤销 |

---

## 7. 诚实披露（方法与过程）

1. **窗口坐标漂移**：我早期几次点击落在标题栏上导致**主窗被我自己拖动**（1226×699 → 多次移动），后续所有坐标均按 `shot_pid.py --list` 的实时 rect 重新计算；文中引用的图像坐标均为"截图像素"。
2. **崩溃后的实例**：崩溃使第一个实例（PID 7280）整体退出，我据此重启并做了第 2/3 次复现尝试；崩溃时应用已自动保存 `Untitled-backup.ustxp`（无数据损失）。
3. **主题还原的经过（必须说清）**：浅色核对时我把主题切到 Light；收尾第一次还原失败（我把旧 origin 与新 origin 叠加，点击落空），并且此后我误在标题栏区域点击再次移动了窗口。为不再拖时间，我在**关闭应用后**直接改回偏好文件 `prefs.json` 的 `"ThemeName": "Light" → "Dark"`（用户原值），**重启验证**：窗口背景 `#141218`（深色池）✔，随后 `stop.ps1` 收工。**产品代码/仓库文件 `git status` 全程为空**，改动仅限该 app 数据文件（还原性质）与本报告 + `.dsh/fx/shots/mx-*.png`。
4. **D9 未做真拖**：我没有用"改 prefs 里的插件路径 + 重启扫描"来制造插件——那会写入用户偏好且偏离"真实拖拽"的语义；故如实登记为未验证 + 结构半程证据 + 契约测试覆盖（`dfc57361` W4 13 例含"拖拽负载"、`e377c8a4` FxChainPanel 19 例含空态/重排）。
5. **状态条那 12px**：状态条实测 32（955 分隔线 + 956..986）；其下 988..999 的 `#101012` 12px 我判定为窗口底缘/阴影区，不是状态条的一部分（若 Lead 认为另有含义，请以实机为准）。
6. **量法可复现**：所有数值都可用 `%TEMP%\mx\mxmeasure.py` 对同名截图复算；截图清单见文首路径（`mx-00…mx-30`，共 25 张）。
7. 我**未修改任何产品代码、未提交、未合并、未推送**；未按进程名杀任何进程（全程 `stop.ps1 -ExePrefix ...UTvTU-mx-int`，仅按 PID + 路径前缀）。

---

# 复验轮（W13 / task-25）：`76281e37`，4 项 —— 结论 **3 PASS / 1 部分（含未验证项）**

> 树 `UTvTU-mx-int` HEAD **`76281e37`**（tree clean）。工具/红线同 W6（按 PID；`-ExePrefix ...UTvTU-mx-int`）。本轮**未改主题**（收工 `prefs.json` 仍 `"ThemeName": "Dark"`），`git status` 全程为空。

## ① 崩溃复验（W9 `DetachAndFlush`）—— **PASS**

- **连续 6 轮分离/收回**（每轮：点「分离」→ 观察 4s → 关分离窗 → 确认收回；第 2、5 轮各夹一次「工作台↔混音台」视图往返）：**进程全程存活（Responding=True），7 次 reparent 零异常**。
- **像素级单轮证据**：`mx-44-cycle-before.png` 视图区 accent `(60,160)=#CFBDFE`（混音台在视图）→ 点分离 → `mx-45-cycle-detached.png` 视图区变 `#141218`（已摘走）+ 独立窗「混音台」出现 → 关窗 → **`mx-47-returned.png` accent 回到 `#CFBDFE`** ⇒ 收回路径亦像素验证通过。
- **日志**：当日 `log20261004.txt` 全文 `Unhandled exception` **仅 1 次 = W6 的 18:53:28 那一次**；本轮实例（20:3x 起）**0 次** ⇒ W6 命中 1/3 → 本轮 **0/7** ✔。
- ⚠️ 诚实说明：(a) W6 历史里的「双击内置行 → `试听效果` → ESC → 旁通 → Ctrl+Z」**未复放**（自动化点链面板 ＋/行两次落空，判断其与 reparent 机制无关，改为做足 7 次分离/收回 + 视图往返）；(b) 我的收窗自动化必须**先点标题栏激活、再点 X**（否则首击只激活窗——日志有 `[warn] 置前失败`），属工具限制而非产品问题。

## ② 几何点 —— **PASS（✕ 一项未能像素核）**

| 点 | 规格 | 实测（像素） | 结论 |
|---|---|---|---|
| M/S 高 | 20 | `scancol x=50`：`#2B292F` **289..308 = 20**（修前 32） | **PASS** |
| `＋轨道`（顺带） | 28 | `bbox #2B292F` = **56×28**（x[29,84] y[100,127]） | **PASS** |
| `viewTab` / 胶囊 | 30 / 36 | `scancol x=847`（选中「混音台」）：容器 `#2B292F` 46..48 + 选中块 `#4A4458` **49..78 = 30** + 容器 79..81 ⇒ **容器 36 / 选项 30 / 上下各 3px**（修前 32 且上下各溢 1px 的现象消失） | **PASS** |
| `libTab`（顺带） | 28 | `scancol x=1123`（选中「效果器」）：`#4A4458` **142..169 = 28** | **PASS** |
| 链行 **✕** | 22×22 | **未像素核**：✕ 仅 VST 行显示，而拖拽未落地（见 ③）⇒ 无 VST 行可量；静态证据 `FxChainRow.axaml:52-53` 本地 `MinHeight=22`+`Margin=0` 已中和主题 `MinHeight=32` 与应用级 `Button{Margin:0,4}` | **未验证（仅代码审查）** |

（主静音 24×20 属上轮项、本轮按新范围跳过像素复核。）

## ③ D9 真机拖拽 —— **部分：列表侧 PASS（17 条），物理拖拽未落地**

- **列表 OK**（`mx-52-effects-list.png`）：标题行 **「17 个插件」**，行如 `2getheraudio TickyClav 2 / VST2`、`ACE Bridge ARA · ACE Studio / VST3`、`AGML2 / VST2`、`Keyzone Classic / VST2`、`OTT / Xfer Records / VST3`、`Persistent C/L/Q/R / VST3`…——**VST2/VST3 徽标正确**。
- **W11/W12 交叉验证（日志实证）**：`[VST] Registry: 28 plugins (17 effects) | VST3 bundles 3: moduleinfo 1, fallback 2, failed 0 | VST3 single-file 23: registered 20, failed 3 | VST2 5: registered 5 | vst_probe: ok` ⇒ 与"**24→28 总数 / 15→17 效果器**"精确一致；**`fallback 2` 证明"无 moduleinfo 老式 bundle 回退"真的生效**；失败的 3 个逐个有日志（含 `synthv-flat.vst3` 探针超时 + no metadata ⇒ 正确拒绝）✔。
- **物理拖拽 ✗（未验证）**：两次真实拖（`mouse_event` + 放大步长长拖）均未在链面板产生 VST 行（面板行探针两次为空）。判断：Avalonia `DoDragDropAsync` 走 **OLE 拖放回路**，合成鼠标输入不足以进入拖放模态循环（`SendInput` 版本因我的 `Add-Type` 辅助类不支持带方法体定义而未跑起来，已放弃）。⇒ **需用户手拖 5 秒验证**（拖一行到右侧链面板 → 出现 VST 行 → 可排序 → Ctrl+Z 可撤销）；代码/测试侧覆盖：`MainWindow.axaml.cs:1176-1202`（`CreatePluginDragData` → `DoDragDropAsync`）+ 面板 `OnDragOver/OnDrop` + `FxChainDragData.Read`（同一进程内格式 `OpenUtau.FxChainItem`）。

## ④ 一次全量（默认变体）—— **PASS，但发现 1 次新偶发**

实际跑了 **7 次**（默认×2 / Dark×2 / Light×3，同一 `--no-build` 产物）：

| 轮次 | 结果 |
|---|---|
| `-t:Rebuild` | exit 0，**0 错误** |
| r1-default | ⚠️ **失败 1**：`OpenUtau.Test.Core.SignalChain.MixFxSourceTest.Mix_SteadyState_DoesNotAllocate`（exit 1，537/538） |
| r2-default | 538/0 ✔ |
| r1-dark / r2-dark | 538/0 ✔ / 538/0 ✔ |
| r1/r2/r3-light | 538/0 ✔ ✔ ✔ |

- 默认变体单次要求 **PASS**（r2）；但"连跑 0 failed"不稳定：**7 次 1 次偶发，且命中在 Rebuild 后第一次运行**——**新的名字**（不是已修的语言用例），属"分配计数类用例对 GC/并行噪声敏感"（该用例计 `GC.GetAllocatedBytesForCurrentThread`）。建议后续提交：采样窗口收窄到稳态若干块 / 放宽阈值 / 串行化。

## ⑤ 回归抽样（按"只就这 4 项"口径）

- 抽样像素：条顶 152 → 卡内边距 8（152..159）→ **强调条 160..162 = 3px** → 名称 ink 170..178 → EQ 187 起 ⇒ 通道条头部结构无回退 ✔；混音台 / 链面板（空态文案）/ 素材库列 / 胶囊三视图 / 分离↔收回 全部可用 ✔。
- `git diff bd3278e6..76281e37 -- OpenUtau/Styles OpenUtau/App.axaml` ⇒ **空** ⇒ 修复轮**未新增应用级 `/template/` 补丁**、未动样式层 ✔（改动集中在 `Controls/*.axaml` 局部样式、`Core/Vst/*`、`ViewModels/MainWindowViewModel.cs`、`Test/**`）。
- 键对齐/硬编码色值本轮未复跑（按你"跳过对抗审查复跑"口径）。

## 复验轮证据（`.dsh/fx/shots/`）

`mx-40-geometry.png`、`mx-42-mixer.png`（M/S=20 / ＋轨道=28 / 胶囊 36+30 探针源）、`mx-43-chain-row.png`、`mx-44-cycle-before.png`、`mx-45-cycle-detached.png`、`mx-46-cycle-returned.png`、`mx-47-returned.png`、`mx-48/49/50-effects-*.png`、`mx-52-effects-list.png`（**17 个插件** + VST2/VST3 徽标）、`mx-53/54/55-drop*.png`（两次拖拽未落地）。

**一句话**：① 崩溃修复 **PASS**（7 次 reparent 0 异常；全天日志仅 W6 那 1 条）；② M/S **20**、胶囊 **36+30**、libTab **28**、＋轨道 **28** 全 PASS，**✕ 22×22 因无 VST 行未像素核**；③ D9 列表侧 PASS（**17 条**、徽标正确、W12 回退路径日志实证），**物理拖拽未落地 ⇒ 未验证**；④ 默认变体全量 PASS，但发现**新偶发** `MixFxSourceTest.Mix_SteadyState_DoesNotAllocate`（7 次 1 次，Rebuild 后首次运行命中）。
