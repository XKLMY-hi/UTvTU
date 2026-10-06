# 轨头 v2 + 折叠面板快捷展开 · 设计提案（W35）

> 作者：fx-rack（设计提案，**不含实现**）· 日期：2026-10 · 基线：`plus-develop` @ `2c3e5583`
> 用户原话：「保留官方的大头像轨道头（**改成圆角**），M S FX 设置几个图标**横向并排**」；「折叠了面板就只能在布局栏打开了，太麻烦了，在折叠后的位置添加一个小按钮来快捷打开」。
> 硬性设计要求：**渲染器/引擎入口在任意轨道高下都可达**。
> 相关：`.opencode/plans/ui-standards.md`（尺寸/令牌/契约铁律）、`ui-rework-decisions.md`、`Controls/TrackHeader.axaml(.cs)`、`Controls/PanelSplitter.axaml(.cs)`、`Views/MainWindow.axaml`。

---

## 0. 推荐摘要（给用户过目用）

1. **头像保留 44×44，圆角 8 → 12**（备选正圆 999，见 §12 待确认①）。头像下面是轨号徽标（现状保留）。
2. 卡片拆成**三件**：`头像列` + `可裁信息区` + **`常驻动作条`**（横排 `[M][S][fx][引擎][⚙]`）。
3. **引擎入口从"会消失的正文行"搬进常驻动作条**（badge：状态点 + 引擎名，三档降级到只留状态点 + tooltip）⇒ **任何行高（42–147）都在**；另加卡片右键菜单的「渲染器 ▸」作为第二条路。
4. 裁剪顺序固定为：**音量声像行 → 音素器 chip → 歌手/引擎 chip**；**名称 + 头像 + 动作条永不裁**（42px 行也保留"谁+什么引擎+M/S/FX+设置"）。
5. 极端高度不代表丢功能：42–62px 时音量/声像滑杆收起，但**名称行右侧出现 dB 读数 chip（可按住左右拖动调值）**，右键菜单也有完整入口。
6. 折叠面板恢复：在**折叠处那一条 7px 分隔条列里**贴一枚**边缘竖标签**（默认 14×48 只显示面板图标；悬停/聚焦展开到 92px 显示本地化面板名），**不吃布局、不留夹缝**；箭头方向与面板头部原有的 collapse chevron 镜像 ⇒ 一眼知道它属于哪个面板。
7. 键盘可达：标签是 `Button`（Tab 可到，Enter/Space 展开），tooltip 写"展开轨道头面板 · 双击分隔条恢复默认宽"。
8. 全程**不需要新 VM 状态机**：`IsSelected / Mute / Solo / MixFxEnabled / TrackAccentColor / Muted` 都已存在，密度分档只在 `TrackHeader.SetPosition()` 里算。
9. 不动的契约：`TrackHeightDelta` 行高同步、`PanelSplitter` 五值、折叠零占位、`Preferences.Default.PanelLayout` 单一持久化、`<32` 高按钮压 `MinHeight`+`Margin=0`、颜色只走 `md3.*`。
10. 可视稿见 §11（**已退化为尺寸化示意图**，原因与替代验证方式都写在那一节）。

---

## 1. 现状解剖（都是实测事实，带位置）

### 1.1 轨头结构（`Controls/TrackHeader.axaml`，318 行）

| 区域 | 现状 |
|---|---|
| 卡片 | `Grid Margin=2` → `Border CornerRadius=12`，`Background=SystemControlBackgroundAltHighBrush`（**遗留键，不是 md3 令牌**），内边距 `6,3,4,3` |
| 左：头像 | `Border 44×44 CornerRadius=8` + 歌手图；右下角 `Border 20×14 r3` 轨号徽标（轨道强调色 + 白字 10 Bold） |
| 中：信息 | 竖排 5 行：名称（Button h18 / 11 SemiBold）、歌手（h16 / 10）、音素器（h16 / 10）、渲染器（h16 / 10）、音量+声像（Slider + 9px 等宽读数） |
| 右：动作 | **竖排** `StackPanel`：`⋯`溢出（17×18，仅当渲染器行隐藏时出现）、Mute（24×20）、Solo（24×20）、FX（24×20，文字 "fx"）、设置（24×20，齿轮） |

### 1.2 行高与裁剪（`TrackHeader.axaml.cs:84-92`）

```csharp
ViewModel.IsSingerVisible     = trackHeight >= ViewConstants.TrackHeightDelta * 3;   // ≥63
ViewModel.IsPhonemizerVisible = trackHeight >= ViewConstants.TrackHeightDelta * 4;   // ≥84
ViewModel.IsRendererVisible   = trackHeight >= ViewConstants.TrackHeightDelta * 5;   // ≥105
```
`TrackHeightDelta=21`、`TrackHeightMin=42`、`TrackHeightDefault=105`、`TrackHeightMax=147`（`ViewConstants.cs:10-13`）。

**三个具体缺陷**
1. **引擎入口会整行消失**：行高 <105 时渲染器行 `IsVisible=false`。今天靠"`⋯` 溢出弹出层里再放一份渲染器按钮"兜底——但那个 `⋯` 只有 **17×18**、位置在右上角竖排栈里、且**只在渲染器行隐藏时才出现**（行高恰好 105 时它又不出现）。结果是：**用户要么看不到它，要么看到也不认为它是"引擎入口"**。
2. **动作条竖排**（用户明确要求改横排）：竖排 5 个 20px 高按钮 = 100px，几乎等于默认行高 105 的可用高度，把中间信息区挤到只剩 60px 宽。
3. **极端行高只剩"名字+音量"**：42px 行下可见内容 = 名称行 + 音量声像行（头像被卡片裁掉大半），M/S/FX/设置仍在（竖排在右上），但引擎彻底不可见。

### 1.3 折叠面板（`Controls/PanelSplitter.*` + `Views/MainWindow.axaml`）

- 两个可折叠面板：**轨道头列**（`track-header`，默认 248 / min 200 / max 420）与**素材库列**（`library`，默认 272 / min 220 / max 480）。
- 折叠 = `PanelShown=false` ⇒ 面板 `IsVisible=false`、列宽 0，**不留夹缝**（`ui-standards §2.4`）。
- 折叠入口：面板头部的小 chevron（`MainWindow.axaml:514` 轨道头 / `:732` 素材库，`Classes="panelToggle"`，tooltip `panel.collapse.tracks|library`）。
- **恢复入口（现状 3 条，都不在"折叠处"）**：① 顶栏「布局」弹层里的 `panel.toggle.tracks|library` 勾选项（`:454`）；② 工具菜单同款（`:294`）；③ 拖那条**只剩 1px 细线**的分隔条，或双击它恢复默认宽（`panel.drag.hint` 只写在 tooltip 里）。
  ⇒ 用户抱怨成立：**折叠后"折叠处的入口"消失了**，而分割条细到几乎不可发现。

---

## 2. 设计目标与信息层级

### 2.1 一眼（0.3 秒内必须读出）
1. **这是哪条轨**：轨道色（左缘 3px 色条 + 卡片描边）＋ 名称（11 SemiBold，永远第一行、永不裁）。
2. **谁在唱**：头像（44×44 大图，人眼对图片的识别远快于文字）。
3. **它在响吗/被静音吗**：M/S 的**填充态**（整块实色 vs 描边）——扫一眼列就能看出哪几条被 mute。
4. **用哪个引擎**：动作条里的引擎 badge。

### 2.2 第二眼（1 秒内，行列扫视时）
5. 音素器（JA CVVC / 中文 CVVC…）与歌手名的 chips。
6. 音量/声像读数（等宽 9px，右对齐成列便于纵向比较）。
7. FX 是否有链（fx 填充态 + ≥2 条链时的小圆点）。

### 2.3 藏起来（不占常驻视线）
音素器完整名、渲染器完整名（badge 截断 + tooltip 给全名）、重命名/复制/删除/换色/混音台（右键菜单）、轨道设置（⚙ 弹窗）、音量精调（读数 chip 拖动 / 双击进输入框）。

### 2.4 视觉权重表

| 元素 | 字号/字重 | 颜色 | 视觉权重 |
|---|---|---|---|
| 名称 | 11 / SemiBold | `md3.on-surface` | ★★★★ |
| 头像 | 44×44 r12 | 图片本身 | ★★★★ |
| M / S | 10 / Bold（字形），容器 24×20 | `md3.error-container`/`md3.tertiary-container`（开）· `md3.surface-container-high`+`md3.outline-variant`（关） | ★★★（开态） |
| 引擎 badge | 10 / Regular（截断） | 状态点 `md3.tertiary`（已选）/`md3.error`（未选）；底 `md3.surface-container-high` | ★★★ |
| 歌手/音素器 chips | 10 / Regular | `md3.surface-container-high` 底 + `md3.on-surface-variant` 字 | ★★ |
| 音量/声像读数 | 9 / Regular 等宽 | `md3.on-surface-variant` | ★★ |
| 轨号徽标 | 10 / Bold | 轨道强调色 + 白字 | ★（信息冗余，但"数轨号"很快） |
| fx / ⚙ | 9 / SemiBold · 图标 15 | `md3.on-surface-variant` | ★★ |
| 轨道色 | 3px 竖条 | `TrackAccentColor` | ★★★（色觉优先） |

---

## 3. 候选方案与取舍

### 方案 A · 现状微调（最小改动）
头像圆角 8→12；右侧动作条 **竖排 → 横排**贴在右上；其余不动。
- ✅ 改动最小、风险最低。
- ❌ **没有解决硬要求**：引擎仍住在会被裁的正文行里，42–84px 行依然不可达（只能继续靠 `⋯`）。
- ❌ 264 宽时右上横排 5 个按钮（24×4 + 86）＝ 182px，把名称挤到 ~60px，长轨名截断严重。

### 方案 B · 头像列 + 双区（**推荐**）
```
┌─ 卡片 12 圆角 ────────────────────────────────────────────┐
│ ╭──────╮  Track 1                     [M][S][fx][● Worldline][⚙] │ ← 常驻动作条（横排）
│ │ 头像 │  ╭Sona╮ ╭JA CVVC╮                                │ ← 可裁：chips 行
│ │ 44²  │  ▬▬▬▬▬ +0.0 ─ ▬▬▬▬ C                           │ ← 可裁：音量声像行
│ ╰──────╯                                                  │
└───────────────────────────────────────────────────────────┘
```
- ✅ 引擎入口在**常驻件**里 ⇒ 任意行高可达（硬要求达成）。
- ✅ 动作条横排 = 用户明确要求；且它**不吃中间宽度**（占用的是"第 3 列"，与信息区各让一步）。
- ✅ 裁剪有明确顺序，极端行高仍保留"谁/什么引擎/M/S/FX/设置"。
- ⚠️ 需要 VM 暴露"当前密度"（3 个布尔），但**无需新数据源**。

### 方案 C · 单行紧凑
整条只有一行：头像 28 + 名称 + 引擎 badge + M/S/FX/⚙（全部 20px 高）。
- ✅ 42px 行最舒服，信息不丢。
- ❌ 默认 105px 行里"空"得太厉害（用户要的正是"大头像"），且窄面板最先崩。
- ❌ 把音量/声像永久挤到别处（用户没要求这个牺牲）。

**结论：推荐方案 B**，并把 A 的"动作条横排"与 C 的"极端行高档位"分别吸收为 B 的默认态与 42px 态。

---

## 4. 网格与尺寸体系

### 4.1 对齐既有阶梯
- 间距/内边距：**4 / 6 / 8 / 10 / 12 / 16**（不使用 5/7/9/11）。
- 圆角：**4**（chip、小按钮）/ **8**（短行头像、标签默认态）/ **12**（卡片、头像默认态）/ **999**（引擎 badge、标签悬停态）。
- 字号：**10**（chips、徽标、读数、fx）/ **11**（名称、面板名）/ **13**（弹层标题）；字重只用 Regular / SemiBold（M/S 的 Bold 字形例外，属既有实现）。

### 4.2 卡片与列
| 项 | 值 | 说明 |
|---|---|---|
| 轨头列宽 | 268（默认 248 时卡片净宽 ≈264） | 沿用现有 canvas 宽度绑定，不新增常量 |
| 卡片外边距 | 2（四边） | 现状 |
| 卡片圆角 | 12 | 现状 |
| 卡片内边距 | 6 / 4 / 6 / 4 | 现状 `6,3,4,3` → 上下提到 4（42px 行要留出 24px 头像 ± 4） |
| 列结构 | `Auto`（头像）· `*`（信息）· `Auto`（动作条） | 现状一致 |

### 4.3 头像阶梯（跟随行高，避免"头像比卡片还高"）
| 行高 | 头像 | 圆角 | 徽标 |
|---|---|---|---|
| 147 / 105（默认） | **44** | **12** | 20×14 r4，右下 -4 重叠 |
| 84 | 36 | 10 | 18×13 r4 |
| 63 | 28 | 8 | 16×12 r4（或隐藏，见 §6 极矮态） |
| 42（最小） | 24 | 8 | 14×11 r4 |

> 为什么不做正圆：正圆会把 4:3 的歌手立绘裁掉左右两缘（多数音源立绘是方形构图），且 44 直径的圆会与右下角徽标产生视觉粘连。备选方案见 §12。

### 4.4 动作条（常驻）
| 控件 | 常规（≥84） | 矮（<84） | 说明 |
|---|---|---|---|
| M / S | 26×24 | 22×20 | `ToggleButton`，**必须**在自己的样式里压 `MinHeight=0` + `Margin=0`（`ui-standards §1` 的 32px 陷阱） |
| fx | 26×24 | 22×20 | `Button`，`fxOn` 类=填充态 |
| 引擎 badge | 86×24（点 8 + 文本截断） | 74×20 → 30×20（只留状态点） | 宽度三档降级；`ToolTip.Tip` 始终给「渲染器：<全名>」 |
| ⚙ | 26×24 | 22×20 | 现状按钮保留 |
| 动作条间距 | 4 | 4 | |
| 合计宽 | 26×4 + 86 + 4×4 = **206** | 22×4 + 30 + 16 = **134** | 268 列宽下 ≥206 时名称仍有 ~56px；因此 **<240 列宽**时进入窄面板降级（§5.3） |

### 4.5 窄面板降级（列宽 200–239，最小 200）
1. 头像 44 → **36**；
2. 引擎 badge → **仅状态点 30**（tooltip 给全名）；
3. chips 行只留歌手 chip（音素器进 tooltip）；
4. 读数 chip 宽度 34 → 30。

---

## 5. 引擎入口：任意行高可达（硬要求）

### 5.1 解法
引擎入口 = **动作条里的 badge**（常驻），点击打开现有 `RenderersMenuRes` 菜单（复用 `RendererButtonClicked`，零新逻辑）。三档呈现：

| 行高/列宽 | 呈现 | 宽度 |
|---|---|---|
| ≥105 且列宽 ≥240 | `● Worldline ▾`（状态点 + 截断名） | 86 |
| 84–104 或窄面板 | `● World`（截断更狠） | 74 |
| <84 或列宽 <240 | `●`（仅状态点；未选时 `●` 用 `md3.error`） | 30 |

### 5.2 第二条路（成本最低的保险）
卡片右键菜单（现有：上移/下移/重命名/轨道颜色/复制/复制设置/音色映射/删除/混音台）**新增一项**：`渲染器 ▸`（子菜单，`ItemsSource=RenderersMenuItems`，与正文行同一个数据源）。理由：右键菜单不吃高度、天然可达、且与我们已有 M/S 的右键菜单风格一致。

### 5.3 可测判据（交给实现者与验证者）
- 行高遍历 `{42, 63, 84, 105, 147}` × 列宽 `{200, 248, 420}`：**引擎 badge 与 M/S/FX/⚙ 的布局 `Bounds` 均存在且 `Height ≥ 20`**（headless 布局断言，不做像素断言）。
- 点击 badge 的处理器与正文渲染器行**必须是同一个**（避免两套逻辑漂移）。

---

## 6. 状态矩阵

行高 105 / 列宽 268 为基准；括号内注明极端档位差异。

| 状态 | 卡片 | 头像 | 名称 | 动作条 | 其它 |
|---|---|---|---|---|---|
| **默认** | `md3.surface-container` 底 + `md3.outline-variant` 1px | 44 r12 | 11 SemiBold `on-surface` | M/S 描边态；引擎 `●` `tertiary` + 名；fx 描边 | chips：`surface-container-high` |
| **hover** | 底 → `md3.surface-container-high`；描边 → `md3.outline` | 不变 | 不变 | 按钮各自 hover：底 `md3.surface-container-highest` | 光标 = 手型（仅按钮与名称） |
| **选中轨** | 描边 → `md3.primary` **2px**；左缘 **3px `TrackAccentColor` 竖条**；底 `md3.surface-container-high` | 不变 | 不变 | 不变 | 竖条高度 = 卡片高 − 8，圆角 999（42px 时 2px 宽） |
| **静音 M** | 底 → `md3.surface-container-low`；名称 60% 不透明 | 40%（`Opacity=0.6`） | 同上 | M = `md3.error-container` 底 + `md3.on-error-container` 字；音量/声像 `IsEnabled=false` | 引擎 badge 不变（引擎与静音无关） |
| **独奏 S** | 与默认同（**不**把其它轨变暗，避免"谁在响"误读） | 不变 | 不变 | S = `md3.tertiary-container` 底 + `md3.on-tertiary-container` 字 | 播放时 S 按钮外边 1px `md3.tertiary` 呼吸环（`ReduceMotion` 时静态） |
| **FX 有链** | 不变 | 不变 | 不变 | fx = `md3.primary-container` 底 + `md3.on-primary-container` 字；**链数 ≥2** 时按钮右下 4px 圆点 `md3.primary` | tooltip 追加「（N 个效果）」 |
| **无歌手** | 不变 | 占位：`md3.surface-container-highest` + 麦克风字形 `on-surface-variant` | 名称行右侧出现警示 chip「未选歌手」（`md3.error` 字 + `md3.error` 1px 描边，透明底），点击 = 现有歌手菜单 | 不变 | 名称仍显示；不弹窗打断 |
| **轨道极矮 42** | 内边距 4；**无** chips 行、**无**滑杆行 | **24 r8**（徽标 14×11） | 11 SemiBold，右侧接 **dB 读数 chip**（34×20，`surface-container-high`，按住左右拖 = 调音量；双击 = 输入框） | 22×20 / 引擎 30 仅状态点 / ⚙ 22×20 | 声像：右键菜单 + 混音台（不做 chip，避免"哪个是哪个"） |
| **面板极窄 200–239** | 内边距 6/4 | **36 r10** | 单行截断（chips 全部收起，tooltip 补全） | 22×20 / 引擎 30 / 间距 3 | 引擎 badge 悬停才显示全名 |

> 色板口径：以上只出现 `md3.*` 令牌（`surface-container` 系列 5 档、`primary/tertiary/error` 及其 `-container`/`on-` 配对）。**现状的 `SystemControlBackgroundAltHighBrush` 与 `NeutralAccentBrushSemi` 属于遗留键，本次一并归位到 md3**（这是 lint 与主题一致性的既有债）。
>
> **令牌可用性已核对**（基线 `plus-develop` @ `2c3e5583`）：本文引用的 `md3.*` 键（`surface-container` / `-low` / `-high` / `-highest` / `-lowest`、`primary` / `primary-container` / `on-primary` / `on-primary-container`、`tertiary` / `tertiary-container` / `on-tertiary-container`、`error` / `error-container` / `on-error-container`、`outline` / `outline-variant`、`on-surface` / `on-surface-variant`）在现网代码中**均已在用**（例：`Controls/FxChainPanel.axaml`、`Controls/FxChainRow.axaml.cs`、`Controls/FxRackPanel.cs`、`Controls/MasterStrip.axaml`、`Views/ShortcutOverviewWindow.axaml`）。这些键由运行时主题调色板注入（**不在** `Colors/*.axaml` 里以 `x:Key` 声明），所以不要在 XAML 里找定义，直接用 `{DynamicResource md3.xxx}`。图标同理：`icon-list` / `icon-folder-open` / `icon-chevron-right|left` / `icon-equalizer` / `icon-alert` 均已存在于 `Assets/Icons.axaml`，无需新增图标资源。

---

## 7. 交互

| 操作 | 对象 | 行为 |
|---|---|---|
| 单击 | 卡片空白 | 选中轨（现状）；`Shift` = 区间选、`Ctrl/Cmd` = 加选（现状保留） |
| 单击 | M / S | 切换静音/独奏（现状 `ToggleMute`/`ToggleSolo`） |
| 右键 | M / S | 现有批量菜单（只静音/全部静音/全部取消…）保留 |
| 单击 | fx | 打开混音台效果链（现状） |
| 单击 | 引擎 badge | 打开渲染器菜单（**与正文行同一处理器**） |
| 单击 | ⚙ | 轨道设置弹窗（现状） |
| 单击 | 名称 | 重命名（**现状行为保留**；DAW 惯例是双击重命名，但改它属于行为变更，留给用户裁决，见 §12） |
| 右键 | 卡片 | 现有菜单 + 新增「渲染器 ▸」+「音量/声像…」 |
| 拖动 | dB 读数 chip（仅 42–62px 档） | 左右拖动 ±0.1 dB/px；双击进输入框（复用现有 `VolumePointerPressed` 路径） |
| 拖动 | 卡片 | **不做**轨道重排（属工程数据变更，需要 `DocManager` 命令 + 撤销，超出本轮设计范围） |
| 键盘 | 全部 | Tab 顺序：名称 → 引擎 → M → S → fx → ⚙（视觉顺序 = 焦点顺序）；每个都有 tooltip；**不新增默认快捷键**（快捷键归 W28 命令注册表统一裁决） |

### 7.1 tooltip 键名（EN / zh）

| 位置 | 键 | EN | zh-CN | 状态 |
|---|---|---|---|---|
| M | `tracks.mute` | Mute | 静音 | 已有 |
| S | `tracks.solo` | Solo | 独奏 | 已有 |
| fx | `context.track.effects` | Effects | 效果 | 已有 |
| ⚙ | `tracks.tracksettings` | Track Settings | 轨道设置 | 已有 |
| 引擎 badge | `tracks.selectrenderer` | Select Renderer | 选择渲染器 | 已有（**建议** tooltip 格式改为「渲染器：{0} —— 点击更换」，需要新键，见下） |
| 引擎 badge 全名 | `tracks.renderer.current` | Renderer: {0} | 渲染器：{0} | **新增** |
| 无歌手 chip | `tracks.nosinger.warn` | No singer selected | 未选歌手 | **新增** |
| 折叠标签（轨头） | `panel.expand.tracks` | Expand track headers | 展开轨道头面板 | **新增**（与已有 `panel.collapse.tracks` 成对） |
| 折叠标签（素材库） | `panel.expand.library` | Expand library | 展开素材库面板 | **新增**（与 `panel.collapse.library` 成对） |
| 折叠标签提示 | `panel.expand.hint` | Click to expand · drag the splitter to resize | 点击展开 · 拖动分隔条调整宽度 | **新增** |

> 键名放 `Strings.axaml` / `Strings.zh-CN.axaml` 末尾的独立标记块（例如 `⚠ mx-th2`），EN/zh 成对，键集必须相等（现有 `M3` 契约测试会查）。

---

## 8. 折叠面板的快捷展开（第二件）

### 8.1 约束回顾
折叠 = 面板 `IsVisible=false` + 列宽 0，**不留占位、不留夹缝**；`PanelSplitter` 的 **7px 分隔条列恒占位**（`ui-standards §2.1`）。所以 affordance **不能吃布局空间**，只能做成 **overlay**（叠在分隔条列上、允许 ±3px 溢出到邻居，但不参与测量）。

### 8.2 三个候选

| | 形态 | 位置 | 发现性 | 侵入 | 能一眼看出归属？ |
|---|---|---|---|---|---|
| **1 · 边缘竖标签（推荐）** | 14×48 圆角 8 胶囊（图标）；悬停/聚焦展开到 92×48 圆角 999（图标 + 面板名） | **折叠处那条 7px 分隔条列**，顶部对齐原面板头 | ★★★★ 常显 | overlay，0 布局 | ★★★★ 位置 + 方向 + 字形 + 悬停名 |
| 2 · 分隔条 hover 药丸 | 平常仍是 1px 线；指针进入才浮出 92×28 药丸 | 同左 | ★★ 需要"去摸那条缝" | overlay，0 布局 | ★★★ 悬停后有名 |
| 3 · 顶栏/布局菜单（现状） | 菜单勾选 / 重置 | 顶栏 | ★ 要想起菜单 | 0 | ★★ 菜单文字 |

**推荐候选 1**，理由：用户的原话就是"**在折叠后的位置**添加一个小按钮"——候选 1 是唯一同时满足"在折叠处"与"常显可发现"的方案；候选 2 保留为 hover 增强（标签 hover 时也可以顺带把分隔条染成 `md3.primary`，与 `PanelSplitter` 现有的 hover 语义一致）；候选 3 作为兜底路径**保持不变**（不删任何现有入口）。

### 8.3 规格（候选 1）

| 项 | 默认态 | hover / focus 态 | pressed |
|---|---|---|---|
| 尺寸 | 14 × 48 | 92 × 48（向右/左展开，**只在 overlay 内**） | 同 hover，底 `md3.primary` 90% |
| 圆角 | 8 | 999 | 999 |
| 底色 | `md3.surface-container-highest` | `md3.primary` | `md3.primary` |
| 描边 | 1px `md3.outline-variant` | 无 | 无 |
| 内容 | 10px 字形（面板图标）`md3.on-surface-variant` | 图标 + 面板名（11 Regular）`md3.on-primary` | 同 |
| 位置 | 左面板：贴该列**右缘**（向右侧展开）；右面板：贴该列**左缘**（向左侧展开） | 同 | 同 |
| 垂直位置 | 顶部对齐原面板头（≈ 顶栏下方 8），高 48 | 同 | 同 |
| 焦点环 | focus 时 2px `md3.primary` 外环（`md3` 焦点规范） | — | — |
| 出现条件 | **仅当该面板 `PanelShown == false`** | | |
| 隐藏条件 | 面板展开后立即隐藏；拖拽分隔条过程中隐藏（避免误击） | | |

**"一眼看出属于哪个面板"的四条线索**
1. **位置**：就在它被折叠的那条边上（左列/右列）。
2. **方向**：箭头/字形朝向 = 该面板的展开方向，与面板头部的 collapse chevron **镜像**（轨道头头部是 `icon-chevron-left` 折叠 ⇒ 标签用 `icon-chevron-right` 展开；素材库相反）。
3. **字形**：轨道头 = `icon-list`（列表/行）；素材库 = `icon-folder-open`（内容类）。两者都已存在于 `Assets/Icons.axaml`，不需新增图标资源。
4. **名字**：hover/聚焦展开出的文字直接用 `panel.toggle.tracks` / `panel.toggle.library` 的本地化值（与布局菜单、工具菜单**同一批字符串**，零新增翻译成本）；tooltip 用 `panel.expand.*` + `panel.expand.hint`。

### 8.4 键盘与状态

| 场景 | 行为 |
|---|---|
| Tab 顺序 | 标签是 `Button`（`Focusable=true`），随视觉树顺序进入 Tab 链（左列标签在"轨道头列"原本的位置，右列标签在素材库位置） |
| Enter / Space | 展开面板（= 把 `PanelSlot.Target` 恢复为折叠前的意图值） |
| Esc | 不适用（无弹层） |
| 与拖拽共存 | 标签是 overlay，**不**遮挡分隔条的 7px 命中区（标签垂直居中于顶部 48px 内，分隔条其余高度仍可拖）；标签自身 `Cursor=Hand`，分隔条保持 `SizeWestEast` |
| `ReduceMotion` | 无动画（展开只是尺寸变化，过渡只做颜色 0.12s，已由 `PanelSplitter` 现行口径约束） |

### 8.5 与既有契约的关系（**全部不动**）
- `PanelSplitter` 五值（`PanelColumn/Min/Max/DefaultWidth/CenterMin`）、意图值/有效值分离、升序保留、120/80 阈值、单一持久化（`Preferences.Default.PanelLayout`）——**一行不改**。
- 折叠语义（不占位、不留夹缝）**不改**：标签是 overlay。
- 面板头部的 collapse chevron、顶栏「布局」、工具菜单勾选、`panel.reset`、`panel.drag.hint` **全部保留**。
- 新增的是"第 3 条恢复路径"，且是唯一在折叠处的路径。

---

## 9. DAW 参照与取舍

> 说明：以下基于我对这些 DAW 的既有认识；标 `(未核实)` 的条目表示我无法在本次工作中核对到权威尺寸/行为，**不作为实现依据**，只作方向参考。

### 9.1 四问逐家对照

| DAW | 大图/头像 | M / S / FX 位置与排列 | 引擎（乐器/设备）入口 | 极矮行怎么办 |
|---|---|---|---|---|
| **Ableton Live 12** | **没有**头像；只有左侧一条轨道色 + 名称。缩略图出现在 clip/device 区 | 轨头里 M/S/arm 是**一横排小图标**，位于名称下方/左侧 `(未核实)` | 设备链在**轨头下方**独立区域，随行高被裁；轨头本身不放引擎名 | 轨道高度有 Small/Medium/Large 档位；Small 时**隐藏设备区与推子**，只留名称 + M/S/arm |
| **Logic Pro 11** | 轨头有**用户可自选的轨道图标**（可选图片），尺寸小（约 20–30px）`(未核实)` | M/S/rec 是**一横排**，位置固定在轨头**左上/右上**；**最小行高也保留** | 轨头显示乐器/通道名（文本），极小行高时让位 | 多档轨道高；裁剪顺序：先丢图标/乐器名，再丢自动化/推子，**M/S 始终在** |
| **Bitwig 5** | 轨头有**小轨道图标**（非大图） | M/S/rec 一横排；`fx`/设备链有独立"设备面板" | 设备链有**自己的折叠头**（collapsed 时留一条 24–28px 的头 + 展开箭头） | 行高可拖；小高度时收起推子/表头，保留名称 + M/S/rec |
| **Reaper 7** | 轨头支持**用户设置轨道图标**（图片，可大可小，`tcp` 布局里用 WALTER 定义） | 默认 `tcp` 布局里 M/S/rec/monitor 是**一横排**贴右上；**任何行高都在**（可被用户改） | 不显示"引擎"，但显示 **FX 按钮 + FX 数**（`tcp.fx`），且 FX 按钮**常驻** | **最值得抄的一家**：`rtconfig.txt` 的 WALTER 用 `h` 条件**逐元素裁剪**，顺序由主题作者（或用户）定义；默认顺序≈推子 → 音量读数 → 输入 → 名称（名称最后才动） |
| **Studio One 6/7** | 轨头有轨道图标/乐器图标（小） | M/S/rec 一横排；插入/发送在通道区 | 轨头显示**乐器名 + 预设**；小行高时收起 | 轨道高度多档；小高度保留名称 + M/S/rec，隐藏插入/推子 |

### 9.2 折叠恢复对照

| DAW | 折叠后留下什么 | 恢复 affordance | 一眼看出归属？ |
|---|---|---|---|
| Ableton | 什么都不留（面板整块消失） | 顶栏/状态栏的 **toggle 按钮** + View 菜单 | ★★ 按钮有图标，但不在原位 |
| Logic | 不留 | 控制栏按钮 + 菜单 + 快捷键 `I` | ★★ |
| Bitwig | 若折叠的是**设备面板**：留一条细的头（含展开箭头） | 那条头上的箭头 | ★★★★ |
| Reaper | **dock 的边/把手线仍在**（拖它即可展开；docker 还有标签页） | 边把手 + 右键菜单 | ★★★（位置对，形态不显眼） |
| Studio One | 不留 | 工具栏的 Browser/Editor 按钮（我记得它在被需要时会**闪动提示** `(未核实)`） | ★★ |

### 9.3 抄 / 不抄（**取舍清单**，每条给理由）

1. **抄 Reaper 的"裁剪顺序可视化 + 逐元素条件"** —— 因为我们要的正是"行高再矮也留住关键件"，而 Reaper 是把"哪个先死"写成显式规则；我们把它固化成 §6 的固定顺序（用户可预期），而不是让主题作者随意改。 **不抄** Reaper 的"用户可编辑 WALTER 布局" —— 我们要的是一致性（MD3 体系 + ui-lint），把布局语言暴露给用户会毁掉可测性与主题一致性。
2. **抄 Logic 的"轨道图标"** —— 我们已经有"大图头像"，逻辑同源：**图片是最快的识别通道**；把头像放大并跟行高缩放，是 Logic 思路的放大版。 **不抄** Logic 的"小图标 20–30px" —— 用户明确要"大头像"，缩小图标会背离诉求。
3. **抄 Bitwig 的"折叠面板留一条带头"** —— 它证明"折叠后仍有可点对象"是可行且被用户接受的；我们的折叠语义要求 0 占位，所以把这条头**放进 7px 分隔条列的 overlay**（等价体验，零布局代价）。 **不抄** Bitwig 的"独立设备面板头"那种 24–28px 常驻条 —— 那会永久吃掉一条视觉带宽，与"不留夹缝"的既定体验冲突。
4. **抄 Ableton 的"Small 档位"** —— 行高档位化（42/63/84/105/147 五档）比"连续裁剪"更容易做出一致结果，也让测试可枚举。 **不抄** Ableton 的"没有头像" —— 用户要保留。
5. **抄 Studio One 的"入口在需要时会闪动提示"** —— 如果我们担心折叠标签Still不被注意，可给标签加一次性 300ms 高亮（`ReduceMotion` 时静态高亮）。 **不抄** 常驻脉冲动画 —— 违反 `ReduceMotion` 精神、也抢视觉焦点。`(Studio One 的闪动行为未核实，仅作启发)`
6. **抄 Reaper 的"FX 按钮常驻 + 显示链数"** —— 我们已计划在 fx 按钮上做"≥2 条链出圆点"，同一逻辑。 **不抄** Reaper 的"轨头塞十几行微小控件" —— 信息过载，且我们的名称/引擎/歌手是三层语义。
7. **不抄** Logic/Ableton 的"引擎名放在会消失的正文区" —— 这正是当前缺陷的成因（§1.2 缺陷 1）。
8. **不抄** 任何把 M/S 做成**文字按钮**（Ableton 用图标）：我们的 `M`/`S` 单字在 20px 容器里可读性最好、且已有用户肌肉记忆，保留。
9. **只抄 Reaper 的"边把手"位置感，不抄它的形态** —— 它的把手是 1px 线（用户抱怨的正是"发现不了"）；我们用 14px 胶囊（可发现）但**保持位置一致**（折叠处）。
10. **抄"横向并排"这件事本身来自用户**，并补一条**来自 Bitwig/Reaper 的证据**：主流 DAW 的 M/S 都是横排——竖排会与"轨道是横向条带"的形态语言冲突。

### 9.4 风险提示（抄了会翻车）
1. **小图标 + 高 DPI**：14px 标签、10px 字形在 125%/150% 缩放下会糊；实现时必须用 `Path` 矢量字形（不是位图图标），最小命中区按 `ui-standards §1` "命中区 ≥ 视觉尺寸" 放大到 20×48（可用透明 padding 实现）。
2. **overlay 溢出被裁**：标签放在 7px 的固定宽列里，若任一父容器 `ClipToBounds=true` 会被切成 7px。实现前**必须**先验证该列父级不裁剪；否则退化为"画在邻接视图最边缘 + `ZIndex` 抬高"的同效版本（候选 1′）。
3. **32px 按钮陷阱**：动作条按钮声明 20/24 高，必须在自己样式里同时压 `MinHeight=0` 与 `Margin=0`，否则会被 `Md3ButtonTheme` 抬到 32px、把行高撑破（历史上 M/S 20→32 已踩过）。
4. **`ReduceMotion` 与 hover 过渡**：颜色过渡 0.12s 可以留，但**绝不给宽度/高度加过渡**（`ui-standards §2.8`：不给参与布局的尺寸做动画）；hover 展开标签宽度时若做动画会引起邻居重排。
5. **`⋯` 溢出按钮的退役**：方案 B 让引擎入口常驻后，`⋯` 的唯一职责（暴露被裁行）消失。**建议保留它但改造**为"更多信息"（歌手/音素器/引擎全名），因为 42px 行仍需要一条查看全名的路；或者干脆删除并把这些进右键菜单——**需要用户裁决**（见 §12③）。

---

## 10. 实现分期建议（供派工时切分）

| 期 | 内容 | 面 |
|---|---|---|
| P1（纯样式 + 密度） | 卡片/头像圆角、动作条横排、md3 令牌归位、密度分档（`SetPosition` → `UpdateDensity`）、裁剪顺序、引擎 badge 常驻 + 三档降级 | `TrackHeader.axaml(.cs)`、`TrackHeaderViewModel.cs`（+2 个 `[Reactive]` 密度布尔） |
| P2（可达性保险） | 右键菜单「渲染器 ▸」+「音量/声像…」；42px 档 dB 读数 chip 拖动 | 同上 + 复用现有处理器 |
| P3（折叠恢复） | 边缘竖标签（overlay）+ 2 个 handler + 4 个新字符串键 | `MainWindow.axaml(.cs)`、`Strings/*` |
| P4（收尾） | 状态矩阵逐态核对（hover/选中/静音/独奏/FX/无歌手/极矮/极窄）、`ui-lint`、契约测试 | 测试 + `.dsh/fx/ui-lint.ps1` |

---

## 11. 可视稿（**已退化为尺寸化示意图**，含理由）

### 11.1 为什么退化
任务卡优先方案是"临时预览控件 → 真机截图 → 用完删"。本次**未采用**，理由三条（按重要性）：
1. **我的工作树在任务中途被回收**：W12 完成后 Lead 合并并清理了 `UTvTU-mx-lib`（现只剩一个空的 `OpenUtau/` 残留目录，`git worktree list` 已无该树）。要跑预览必须新建一棵工作树 + **全量构建**（该仓冷构建数分钟）+ 启动 GUI + 截图，而这只是为了一张**并非真实 UI 的 mock 图**。
2. **项目既有约定与本方案冲突**：仓库约定（用户 2026-08-02 制定、2026-09-25 再次明确）是"改完 UI 交给用户实机查看，一般不自截图"。预览控件截图属于"自截图"；而本任务的最终判据是**用户看真机**（设计 → 过目 → 实现 → 真机验收）。
3. **收益/成本比低**：mock 图的边际价值是"让用户提前看到构图"，而 §4 的尺寸表 + 下面的示意图已经能把构图、层级、比例讲清；真机差异（字体度量、DPI、hover）mock 图本来也证明不了。
**替代验证**：(a) 本文尺寸化示意图 + 完整状态矩阵（实现者可 1:1 对齐）；(b) 实现 P1 后由**验证者**用 computer use 在真机上按 §6 逐态核对（这才是可信证据）；(c) 若 Lead/用户希望先看构图再实现，我可以**新建工作树跑一次预览截图**（约 20 分钟），一句话即可开工。

### 11.2 方案 B · 默认行高 105px（列宽 268，单位 px）

```
┌─ 轨头卡片 264×105 · r12 · 底 md3.surface-container · 描边 1px outline-variant ─┐
│ 6                                                            6 │
│ ┌────────┐   Track 1                                    [M][S][fx][● Worldline][⚙] │
│ │        │   ↑ 11 SemiBold on-surface                    ← 常驻动作条 26×24×4 + 86 │
│ │ 44×44  │   ╭ Sona ╮ ╭ JA CVVC ╮                      （右对齐，与名称同基线）    │
│ │  r12   │   ╰──────╯ ╰─────────╯  ← chips 10px，底 surface-container-high        │
│ │        │   ▬▬▬▬▬▬▬▬▬ +0.0 ─ ▬▬▬▬▬▬ C   ← 滑杆行：滑块 8×8，读数 9px 等宽         │
│ └────────┘                                                                        │
│  └ 20×14 r4 轨号徽标（右下 -4 重叠）                                               │
└───────────────────────────────────────────────────────────────────────────────────┘
   ↑ 左缘 3px 轨道色竖条（选中态显示；未选中时用卡片左侧留白）
   裁剪优先级（先丢→后丢）：音量声像行(1) → 音素器 chip(2) → 歌手/引擎 chip(3)
   永不裁：名称 · 头像 · 动作条（M/S/fx/引擎/⚙）
```

### 11.3 五档行高的形态（同一宽度 264，推荐方案 B）

```
42px  ┌─────────────────────────────────────────────┐  头像 24 r8 · 徽标 14×11
      │ ╭──╮ Track 1  [+0.0]        [M][S][fx][●][⚙] │  ← dB chip 34×20 可拖动
      │ ╰──╯                                        │  chips 行与滑杆行都不显示
      └─────────────────────────────────────────────┘

63px  ┌─────────────────────────────────────────────┐  头像 28 r8
      │ ╭───╮ Track 1                   [M][S][fx][● ][⚙] │
      │ │   │ ╭ Sona ╮ ╭ JA CVVC ╮                    │  ← chips 行进来
      │ ╰───╯ ╰──────╯ ╰─────────╯                    │
      └─────────────────────────────────────────────┘

84px  ┌─────────────────────────────────────────────┐  头像 36 r10
      │ ╭────╮ Track 1                  [M][S][fx][● World][⚙] │
      │ │    │ ╭ Sona ╮ ╭ JA CVVC ╮                   │
      │ │    │ ▬▬▬▬▬▬ +0.0 ─ ▬▬▬▬ C                  │  ← 滑杆行回来
      │ ╰────╯                                       │
      └─────────────────────────────────────────────┘

105px（默认）= §11.2

147px ┌─────────────────────────────────────────────┐  头像 44 r12，行距 +2
      │ ╭──────╮ Track 1                [M][S][fx][● Worldline][⚙] │
      │ │      │ ╭ Sona ╮ ╭ JA CVVC ╮ ╭ 音素器 ╮         │
      │ │      │ ▬▬▬▬▬▬▬ +0.0 ─ ▬▬▬▬▬▬ C               │
      │ ╰──────╯                                     │
      └─────────────────────────────────────────────┘
```

### 11.4 状态矩阵示意（同一行高 84，只画变化部分）

```
默认      ┌────────┐ Track 1                        [M][S][fx][● World][⚙]
          │ 描边 outline-variant 1px · 底 surface-container
选中轨    ┃▌┌──────┐ Track 1                       [M][S][fx][● World][⚙]
          ┃▌│ 描边 md3.primary 2px · 左缘 3px 轨道色竖条 · 底 surface-container-high
静音 M    ┌────────┐ Track 1                       [M̶][S][fx][● World][⚙]
          │ 底 surface-container-low · 名称 60% · M 填充 error-container · 滑杆禁用
独奏 S    ┌────────┐ Track 1                       [M][S̶][fx][● World][⚙]
          │ 卡片不变（不暗化其它轨）· S 填充 tertiary-container
FX 有链   ┌────────┐ Track 1                       [M][S][f̶x̶•][● World][⚙]
          │ fx 填充 primary-container · 链数≥2 时右下 4px 圆点
无歌手    ┌────────┐ Track 1  ⟨未选歌手⟩           [M][S][fx][● World][⚙]
          │ 头像 = surface-container-highest + 麦克风字形（虚线描边提示"待填"）
引擎未选  ┌────────┐ Track 1                       [M][S][fx][● 未选][⚙]
          │ 状态点改 md3.error，badge 用 error 1px 描边（点开即选）
极矮 42   ┌──────────────────────────────────────────────────┐
          │ 只留固定件：24 头像 · 名称 · [M][S][fx][●][⚙] · dB chip
极窄 200  ┌────────────────────────────────────┐
          │ 36 头像 · 单行截断名称 · [M][S][fx][●][⚙]（chips 全收）
```

### 11.5 折叠面板的快捷展开（候选 1，画在"折叠后"的上下文里）

```
折叠前                                折叠后（推荐候选 1）
┌────────────┬─┬────────────────┐     ┌────────────────────────────┐
│ 轨道头面板 │▏│  编排区/卷帘   │     │  编排区占满整宽（0 夹缝）  │
│ ┌────────┐ │▏│                │     │▐ ← 14×48 r8 胶囊贴在这条边上
│ │ 头像…  │ │▏│                │     │▐   只显示"轨头"图标        │
│ └────────┘ │▏│                │     │▐   （轨道头头部 chevron 是 ←，它配 →）
│  [‹] 折叠  │▏│                │     │▐
└────────────┴─┴────────────────┘     └────────────────────────────┘
                  ↑ 7px 分隔条列恒占位（overlay 就放这里，不算新增布局）

悬停 / 聚焦（同一个标签，overlay 内向右展开，不吃邻居布局）
┌──────────────────────────────────────┐
│ 编排区                                │
│ ╭──────────────╮                     │  92×48 r999 · 底 md3.primary
│ │ ▸ 轨道头     │  ← 11px on-primary  │  文字 = panel.toggle.tracks 的本地化值
│ ╰──────────────╯                     │  tooltip = 展开轨道头面板 · 拖动分隔条调整宽度
└──────────────────────────────────────┘

素材库（右侧面板，镜像：贴左缘、箭头向左、字形=库/文件夹）
                                                         ┌──────────────┐
                                              编排区 │▌  │ 素材库面板   │
                                                     └──┴──────────────┘
                                                        ↑ 标签贴这条边，箭头 ←
```

### 11.6 建议的真机核对清单（交给验证者，P1+P3 实现后按顺序看）
1. 行高拉到**最小 42**：引擎 badge 与 M/S/FX/⚙ 是否仍可见可点；滑杆是否按设计让位给 dB chip。
2. 行高 **105**：头像是否 44 圆角 12；动作条是否横排且不压名称。
3. 拖列宽到 **200**：是否进入窄面板降级（头像 36、引擎仅状态点）。
4. 点面板头部 chevron **折叠轨道头列**：折叠处是否出现 14×48 胶囊；悬停是否展开出「轨道头」。
5. **Tab** 到该胶囊并 **Enter**：面板是否恢复为折叠前的宽度（而不是默认宽）。
6. 同法验右侧**素材库**（箭头方向应相反）。
7. 选中/静音/独奏/FX 四态逐个切换，核对 §6 表格中的**填充态**与卡片底色。

---

## 12. 待用户确认的三个选择点

1. **头像形状**：推荐 **44×44 圆角 12**（保构图、与徽标贴合）；备选 **正圆 999**（更"圆"，但会裁掉方形立绘左右两缘，且与右下徽标视觉粘连）。
2. **42px 极矮行的音量**：推荐"滑杆让位 + 名称行 dB chip 可拖动"（不丢功能）；备选"滑杆强留一行"（42px 下会挤掉名称或头像）。
3. **`⋯` 溢出按钮的去留**：推荐**改造**为"更多信息"（全名/音素器/引擎），因为 42px 档仍需一条查看全名的路；备选**删除**（这些信息进右键菜单）。
   （另外：名称"单击即重命名"是否改为"双击重命名"，属行为变更，愿意改就一起做。）

---

## 13. 可实施性说明

### 13.1 要改的文件与性质

| 文件 | 性质 | 内容 |
|---|---|---|
| `OpenUtau/Controls/TrackHeader.axaml` | **纯样式/结构**（主要工作量） | 卡片三件式布局；动作条 `StackPanel Orientation=Horizontal`；头像圆角/尺寸绑定；chips 行；`Classes` 状态挂接（`.selected/.muted/.solo/.fxOn`）；状态类样式（hover/选中/静音/独奏/FX/无歌手） |
| `OpenUtau/Controls/TrackHeader.axaml.cs` | **换密度计算** | `SetPosition()` 里的三个 `IsXxxVisible` → 统一的 `UpdateDensity(trackHeight, width)`（输出 chips/滑杆/头像尺寸/动作条紧凑档）；引擎 badge 点击复用 `RendererButtonClicked` |
| `OpenUtau/ViewModels/TrackHeaderViewModel.cs` | **+2~3 个 `[Reactive]`** | `IsCompact`（矮行）、`ShowFaderRow`、`ShowChipsRow`；若复用现有 `IsSingerVisible/IsPhonemizerVisible/IsRendererVisible` 则只加 1 个 `IsEngineBadgeTextVisible` |
| `OpenUtau/Views/MainWindow.axaml` | **+overlay 元素** | `TracksPanelSplitter` / `LibraryPanelSplitter` 同格加边缘标签 `Button`（`IsVisible` 绑 `!PanelShown`）；不改五值、不改列定义 |
| `OpenUtau/Views/MainWindow.axaml.cs` | **+2 个 handler** | `OnExpandTracksPanel` / `OnExpandLibraryPanel`（恢复 `Target` 意图值 → `PanelShown` 由控件自动算回 true），或直接调用现有 `ResetPanelLayout` 的**单面板版本** |
| `OpenUtau/Strings/Strings.axaml` + `.zh-CN.axaml` | **+4~6 键** | `tracks.renderer.current`、`tracks.nosinger.warn`、`panel.expand.tracks`、`panel.expand.library`、`panel.expand.hint`（+若采纳"更多信息"则 1 键） |
| `OpenUtau.Test/**` | **新增契约测试** | 高度×宽度遍历的 `Bounds` 断言；折叠标签可见性与展开后宽度=意图值；EN/zh 键集相等（现有工具可复用） |

### 13.2 纯样式 vs 动 VM 的分界
- **纯样式**：所有颜色/圆角/间距/hover/状态填充、动作条排列方向、chips 外观 → 只需 XAML。
- **必须动代码**：密度分档（行高 → 呈现档位）、头像尺寸随高度、引擎 badge 三档降级、折叠标签的展开动作（要读 `PanelSlot.Target`）、右键菜单新增项。

### 13.3 不能破坏的契约（红线清单）
1. **行高同步**：轨头与编排区共用 `TrackHeaderCanvas.TrackHeight`（`TrackHeightDelta=21` 的整数倍关系），**不得**在轨头内部引入独立行高。
2. **音量/声像绑定**：`Volume` / `Pan` / `Muted` / `IsEnabled="{Binding !Muted}"` 语义不变；dB chip 拖动必须走同一条 `VolumeOrPanSliderValueChanged` 落盘路径。
3. **面板宽度持久化**：只走 `Preferences.Default.PanelLayout`；**不新增平行字段**；折叠态是单次动作、直接落盘（现状）。
4. **折叠零占位**：`PanelShown=false` ⇒ 列宽 0、无残留 1px 线；标签是 overlay，**不得**让分隔条列宽/面板 `Min` 变化。
5. **`<32` 高按钮**：动作条所有按钮（20/24 高）必须在自身样式里压 `MinHeight=0` + `Margin=0`（`ui-standards §1`）。
6. **颜色**：只用 `md3.*`；顺手把 `SystemControlBackgroundAltHighBrush` / `NeutralAccentBrushSemi` 归位（现状债）。
7. **`ui-lint` 不新增告警**：`.dsh/fx/ui-lint.ps1` 必须保持 0 新告警（硬编码色、应用级 `/template/` 补丁、`CheckAccess()`、`PanelSplitter` 五值、<32 按钮）。
8. **UI 线程亲和**：密度计算在布局/属性回调里，不涉及绑定集合则无需 `UiThreadAffinity`；一旦引入集合变更必须过它。
9. **无像素断言**：headless 只断言布局 `Bounds`；hover/填充色交验证者真机核对。
10. **不新增应用级 `/template/` 补丁**：轨头样式写在 `TrackHeader.axaml` 的 `UserControl.Styles` 内（现状已如此）。

---

## 附A · 与现状的逐项改动对照

| 项 | 今天 | 建议 |
|---|---|---|
| 头像 | 44×44 r8 | 44×44 **r12**（矮行阶梯 36/28/24） |
| 动作条 | **竖排**，右上，5 个 20px 高 | **横排**，右上，`M·S·fx·引擎·⚙`（矮行 22×20） |
| 引擎入口 | 正文第 4 行，**<105 时消失**，仅靠 `⋯` 兜底 | **常驻动作条 badge**（三档降级）+ 右键菜单「渲染器 ▸」 |
| 裁剪 | 三行各自按阈值突然消失 | **固定顺序**：滑杆 → 音素器 → 歌手/引擎 chips；名称/头像/动作条永不裁 |
| 卡片底/描边 | `SystemControlBackgroundAltHighBrush` + `NeutralAccentBrushSemi` | `md3.surface-container` + `md3.outline-variant`（hover/选中/静音各有档） |
| 选中态 | 仅 `HeaderBorderBrush` 变化 | 描边 2px `md3.primary` + **左缘 3px 轨道色** |
| 无歌手 | 头像空 + 「选择歌手」按钮 | 头像占位 + 名称行警示 chip「未选歌手」 |
| 折叠恢复 | 只剩菜单/细线 | **折叠处 14×48 边缘标签**（hover 展开出面板名）+ 现有 3 条路径全保留 |

## 附B · 未决与风险

| # | 事项 | 影响 | 处置 |
|---|---|---|---|
| 1 | 边缘标签 overlay 是否会被父级 `ClipToBounds` 裁掉 | 标签可能只剩 7px | 实现第一步先验证；退化版候选 1′ = 画在邻接视图最边缘 + `ZIndex` |
| 2 | 42px 档 dB chip 与"名称+芯片"抢宽度（264 → 剩 ~120px） | 长轨名截断 | 名称 `TextTrimming=CharacterEllipsis` + tooltip 全名（现状已有 trimming） |
| 3 | `⋯` 改造后 42px 档的"全部信息"入口 | 信息可达性 | §12③ 交用户裁决 |
| 4 | 五档密度与"用户自定行高"（滑块连续值）的映射 | 中间值（如 70px）呈现 | 密度档位用 `>=` 阈值判断（同现状），连续值落在最近的下档 |
