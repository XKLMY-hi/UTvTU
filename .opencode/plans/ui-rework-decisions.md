# 新 UI 重做 · 决定与注意事项（总纲）

> **用途**：这次 UI 重做的**唯一决策文件**——做成什么样、为什么这么定、实现时别踩什么坑。
> **不含实施计划与排期**（动工切分另起）。
> **来源**：2026-09-25 五轮讨论 + Pen 设计交付包 `C:\Users\XKLMY\Desktop\UTVTU-设计交付`（六屏 HTML / PNG / tokens / README）。
> **状态**：形态层已定稿；功能层只保留接口；设计稿不再迭代（**后续不需要 Pen**）。

---

## 0. 一页速览

- **为什么**：现有 UI 的所有东西几乎都外挂在原版 OpenUTAU 的逻辑上 → 割裂；风格是三方主题打补丁链 → 四不像。
- **做什么**：先「所有窗口内嵌 + MD3 化」，再性能优化。**不换前端框架**（Windows 优先）。
- **核心一刀**：把 VST 控制面从"UI 缝合"变成"效果器链一等公民"——内置效果与 VST **伪封装为同一等级的插件**，只在**混音台右侧一条链**上管理，双击开编辑器（VST 开原生窗口，内置开自绘弹层）。
- **外围两刀**：窗口收进**一个窗口的多个视图**（可分离）；主题改成**壁纸种子的莫奈动态取色 + 全量 MD3 色阶**。
- **不动**：钢琴卷帘的交互逻辑（它是本项目最好用的部分），只换皮。

---

## 1. 为什么要重做（诊断，均本地实测）

| 层 | 症状 | 证据 |
|---|---|---|
| **使用逻辑** | 几乎所有东西都外挂在原版逻辑上 → 割裂 | 模型里**没有设备链**：`UMixFx`（42 行）是写死的具名参数（EqLowDb/CompRatio…）；VST 槽位是另一套（`VstTrackInstances` 199 行 + `TrackMixCommands`）；**UI 是唯一把两者缝成"机架"的地方**——`TrackEffectRack.axaml` 105 行 vs `.axaml.cs` **495 行命令式建 UI**（`BuildUI / BuildBuiltInRow / BuildVstRow / Param`） |
| **风格** | 三方主题打补丁链（Fluent → SukiTheme → SukiOverrides → SukiCompactMenu → Plus.Resources）→ 四不像 | 资源键引用 **1221 处 / 667 键**；主题资产 9 文件 1822 行；`BaseTheme/ColorTheme` 切换还带顺序铁律 |
| **外壳** | 主窗 + 一堆独立小窗，没有"工作台"概念 | `MainWindow.axaml.cs` **2214 行**、`PianoRoll.axaml.cs` **1966 行**；侧栏 VST 页仍是占位（`sidebar.vst.comingsoon`） |

**判据**：外挂感的根在**模型缺设备链**，不在皮肤。换框架会把同一个错误用新管道复现一遍。

---

## 2. 目标、顺序与边界

- **顺序**：① 所有窗口内嵌 + MD3 化 → ② 性能优化。
- **平台**：**Windows 优先**；跨平台收尾与鸿蒙（Flutter 路线）**另案**，本文件不涉及。
- **边界（本轮只预留接口，不实现）**：槽位语义、链级操作、**参数自动化**、主输出 / 发送总线、VSTi 乐器。

---

## 3. 决定

### A. 窗口与视图

| # | 决定 |
|---|---|
| A1 | **单窗口是唯一形态**：欢迎页 / 工作台 / 钢琴卷帘 / 混音台 / 偏好设置 / 效果链都是**同一个窗口里的视图**；没有"欢迎窗口""编辑器窗口"之分 |
| A2 | **「分离」是可选动作**：把当前视图弹成独立窗口（保留原版可拆能力）；**分离状态持久化** |
| A3 | **不做** FL 式自绘内部窗口与布局保存（要求过高，未来再议） |
| A4 | **视图切换器 = 顶栏胶囊分段控件**（取自 Pen 的 VST 编辑器页 tab bar）：容器圆角 full / 高 **36px** / 内边距 **3px**；选项圆角 full / 高 **30px** / 左右内边距 **18px** / 文字 **11px semibold** |
| A5 | 卷帘进入方式不变（双击片段进入，返回走顶栏）；Pen 稿里卷帘/编辑器屏的最小化·最大化·关闭三钮 → **改为一个「分离」按钮** |
| A6 | 启动流程不变：欢迎页为初始视图，**不自动打开上次工程**，最近工程排首位 |
| A7 | **已落地（2026-09-25）**：欢迎页**回归主窗口**，作为内嵌初始视图（独立 `WelcomeWindow` 已删除）；Splash 直接打开 MainWindow；打开 / 新建 / 拖入工程后隐藏欢迎视图；带命令行工程文件启动则直接进编辑器 |

### B. 效果器（"内化"的核心）

| # | 决定 |
|---|---|
| B1 | **只统一效果器**；音源（UTAU / DiffSinger / 未来 VSTi）**不进混音台**；混音台**按轨道区分** |
| B2 | **内置效果伪封装**为与 VST **同等级的插件**：实现仍内置，接口统一 → 操作统一 + 便于同步上游更新 |
| B3 | **混音台通道条不再显示插入列表**；效果链**只在混音台右侧面板**显示（表头 = **轨道名，无背景** + 「＋」） |
| B4 | 链行 = 拖拽把手 · 序号 · 名称 · 格式徽标（VST3 / 内置）· 旁通开关；**双击 = 打开编辑器**；空态一行「从素材库 · 效果器 拖入」 |
| B5 | 编辑器形态：**VST → 原生窗口**（不做自绘参数界面，插件 GUI 难内嵌是既定事实）；**内置伪插件 → 自绘编辑器弹层**，外框观感与 VST 原生窗口一致（标题栏、关闭位置统一），内容自绘 |
| B6 | **插件入口**：素材库第四页改名**「效果器」**作插件浏览器；**插件路径管理在素材库与偏好设置两处都有** |
| B7 | 轨道上**不显示链**（链只有混音台右侧一处管理） |
| B8 | 生命周期 / 撤销粒度：**向专业软件靠拢** |

### C. 钢琴卷帘

| # | 决定 |
|---|---|
| C1 | **交互逻辑不动**——只做 UI 升级（现有卷帘是歌声合成领域最好用的卷帘） |
| C2 | 底部三条参数轨（音高 / 力度 / 动态）= **现有参数曲线的换皮**，不新增语义 |
| C3 | 「调音主战场在卷帘」→ Pen 稿里素材库的「风格」chips 与「声音参数」区**作废**（那是 Pen 的误解） |

### D. 主题（莫奈动态取色）

| # | 决定 |
|---|---|
| D1 | **种子来自壁纸**，**启动时取一次**；壁纸不可用时用**黑白系**兜底 |
| D2 | **全量色阶**：所有控件都走 MD3 角色，不允许游离的硬编码色 |
| D3 | 色阶**照 Google 参考实现**（material-color-utilities，HCT + 量化器）；**引擎不改**（不做自定义配色算法） |
| D4 | 设置项：选择**种子**、选择**配色方案** |
| D5 | **轨道默认无颜色（半透明黑）**，编排区靠**文本**区分；右键手动指定颜色，右键色板**与设置里提供的色板同一套** |
| D6 | 浅色 / 深色**由同一种子按 MD3 规则生成**；Pen 稿只有深色，**浅色不单独设计** |
| D7 | Pen 稿里的 hex 全是**模拟值**，实现时整体替换为角色（含 `--primary` 橙 与 `--md3-primary` 青绿 并存的问题——一律走角色，不留"两套强调色"） |

### E. 文案

| # | 决定 |
|---|---|
| E1 | 一律使用项目现有字符串资源：`OpenUtau/Strings/Strings.axaml`（EN **899** 键）、`Strings.zh-CN.axaml`（**958** 键，**缺失 0**）；XAML 走 `{DynamicResource key}`，代码走 `ThemeManager.GetString` |
| E2 | **Pen 稿里的中文文案不作依据**（未经审核、大概率有歧义）："工作台""欢迎回来，制作人""素材库""插入""声部""拖音""VST3 / ARA2 宿主集成"等一律不用 |
| E3 | 只有**真正的新概念**才新增键（视图切换器选项名、链面板空态、「分离」、素材库第四页名称…）；新增键须 **EN 与 zh-CN 同步** |

### F. 图标（Lucide）

| # | 决定 |
|---|---|
| F1 | **改用 Lucide**（描边风格更贴近 MD3；ISC 许可），**全量替换**现有 Phosphor 图标集（`OpenUtau/Assets/Icons.axaml`，55 键 / MIT / 实心圆润 / 256px 网格） |
| F2 | 替换量：现有 55 键中 **25 键**在 Pen 稿里已有对应（路径可直接沿用）；**30 键**需从 Lucide 全库另挑；**新增 15–20 个** → 总量约 **70–75 个**（明细见附录 B） |
| F3 | **渲染方式沿用填充路径**（`StreamGeometry` 键）：Pen 导出的 SVG 已把描边转成填充轮廓，可直接搬进 `Icons.axaml`；**不改成 Stroke 渲染**（会牵动所有消费点） |
| F4 | **同步更新**：`Icons.axaml` 头部署名（Phosphor MIT → Lucide ISC）；`THIRD-PARTY-NOTICES.md` 目前未列图标集，可顺带加一行 |

### G. 颜色池与颜色接口（**已落地** 2026-09-25）

统一颜色池是"全量色阶"的落点：**所有控件的颜色都从这一个接口取**。

| 层 | 位置 | 内容 |
|---|---|---|
| 算法（纯逻辑，无 UI 依赖） | `OpenUtau.Core/Theming/` | HCT/CAM16/求解器、色调色板、DynamicColor、49 个角色规格、`Md3SchemeColors` |
| 颜色池与接口 | `OpenUtau/Theming/` | `IMd3ColorPool` / `Md3ColorPool` / `ColorPool` 静态门面 / `Md3ThemeResources` 资源桥 |
| 代码生成 | `tools/md3-codegen/` | 从 Google 参考实现**内省**生成角色表与常数表（角色定义不是我手抄的） |

- **接口**：`ColorPool.Current` → `Color(role)` / `Brush(role)` / `Pen(role, thickness)` / `Color(role, dark)`；`ColorPool.Key(role)` 给出 XAML 资源键。
- **XAML 用法**（与旧键并存，替换一个控件就迁一个）：
  `Background="{DynamicResource md3.surface-container}"` · `BorderBrush="{DynamicResource md3.outline-variant}"` · 渐变用颜色键 `md3.color.*`。
- **覆盖**：**49 个角色** × 深浅 × 对比度档位；配色方案 **7 种**（TonalSpot / Vibrant / Expressive / Monochrome / Neutral / Rainbow / FruitSalad）。
- **保真度**（对照 Google `material-color-utilities@0.3.0`）：
  - 全矩阵 **132 用例 × 49 角色 = 6468 格逐位一致**（SHA-256 固定，见 `Md3ReferenceOracleTests`）；
  - **675 例 HCT 求解**（hue/chroma/tone → sRGB）逐位一致；
  - 三个金标准（TonalSpot 深/浅、Monochrome）+ 正文对比度语义护栏。
- **接线**：`App.SetTheme()`（主题唯一入口）→ `ColorPool.SetDark(...)` 重建资源；种子/方案待设置项接入。
- **与旧体系的关系**：旧键（Fluent/Suki/Plus*）**原样保留**；新控件用 `md3.*`；迁移完成的控件不再引用旧键。两端资源键并不互相映射——迁移是按控件逐个做的。
- **待办**：① 壁纸种子 provider（含黑白兜底，见 D1）；② 偏好设置里的"种子 / 配色方案"选项；③ Content / Fidelity 两个方案（需移植 `TemperatureCache`）；④ 控件逐个迁移到 `md3.*`。
- **首个迁移界面（2026-09-25）**：欢迎页（`Views/WelcomeView.axaml`）——颜色**全部**取 md3 角色键，旧的 Plus*/Suki 颜色键一个不用；契约测试 `WelcomeViewTests` 锁死这一点（顺便校验图标/文案键可解析）。



## 4. 设计基准：Pen 交付包 + 差异清单

- **基准 = Pen 交付包 + 本清单**；Pen 交付包**只读参考**，不再迭代。
- **作废项**：
  1. `5-VST-Plugin` 整屏（Pen 对"内嵌 VST"的误解）
  2. 素材库「风格 / 声音参数」区（调音在卷帘）
  3. 混音台通道条的插入列表（链只在右侧面板）
  4. 卷帘 / 编辑器屏的窗口按钮（改为「分离」）
  5. Pen 的**全部中文文案**；图标则**反向采纳**（全量换成 Pen 用的 Lucide）
- **缺口（稿里没有，按本文件第 3 节与 A4 胶囊规格实现）**：效果器链面板、内置伪插件编辑器、「分离」按钮与分离后形态、浅色主题。
- **稿内自相矛盾处以本文件为准**：橙 / 青绿两套强调色并存；Welcome 屏的 "VST3 / ARA2 宿主集成"（ARA2 仅文案，**不作目标**）。

---

## 5. 设计规格速查（从 Pen 的 HTML 抽取的硬数字）

> 完整结构树（按 `data-pencil-name` 还原，含每元素尺寸/间距/圆角）：`.opencode/design/spec/*.txt`，抽取脚本 `.opencode/design/extract-spec.ps1`；**设计交付包已入库**至 `.opencode/design/UTVTU-设计交付/`（HTML 8 / PNG 6 / tokens / README，约 5MB）。六屏共 **2756** 个命名元素，最大嵌套 8 层。

| 区域 | 关键尺寸 |
|---|---|
| 顶栏 | 高 **56**，底部 1px 描边；App Mark 36×36 圆角 8；应用名 15px bold；视图名 11px |
| 视图胶囊 | 容器高 **36** / 内边距 3；选项高 **30** / 左右 18 / 文字 11px semibold |
| 运输条 | 容器高 **40** 胶囊，按钮 **36×36**；时间显示高 40 圆角 8；速度/拍号 14px |
| 工具条（卷帘） | 高 **44**；工具按钮 30×30；胶囊 chip 高约 27–28（内边距 6/12，文字 10px） |
| 工作台 · 轨头 | 列宽 **264**；每轨高 **97**（色条 4px 满高）；M/S 22×20 圆角 4；音量滑条 150×12 旋钮 10 |
| 工作台 · 编排 | 标尺高 **34**；片段为圆角卡（歌词/音符入卡） |
| 工作台 · 素材库 | 宽 **296**，头部高 **44**；搜索框高 **36** 胶囊；列表项圆角 8 |
| 混音台 | 通道宽 **96** / 内边距 8 / 圆角 12；推子区 80×**500**（表 8 宽、轨 4 宽、手柄 24×14、刻度 9 条）；主输出带响度 / 真峰 / 限制器 / 抖动 |
| 卷帘 | 键盘列 **96**；白键高 **24**、黑键 60×22 圆角 4；音符卡 39×20 圆角 4、歌词 11px；参数轨每条高 **40** |
| 偏好设置 | 左侧导航 **304** 宽（项高 44 胶囊）；内容内边距 32；卡片圆角 16 |
| 圆角阶梯 | 4 / 8 / 12 / 16 / 28 / 999 |
| 间距阶梯 | 4 / 8 / 12 / 16 / 24 / 32 / 48 |
| 字号 | 正文与标签 **7–12px**（插入链标签 7–8、元信息 9、正文 10–12、数值 14、页面标题 28） |

---

## 6. 注意事项

### 6.1 工程与环境

1. **工作区在移动硬盘**，盘符随时变（历史上出现过 D/E/F/G）——每次开工先确认实际路径；`UTvTU` 是唯一写入目标。
2. **每条 git 命令要带 `-c safe.directory="<盘符>:/xklmy文件夹/vibe coding/UTvTU"`**（属主不一致）；**推送需 `-c http.sslVerify=false`**。
3. 构建（离线，实测有效）：
   ```powershell
   dotnet restore OpenUtau.sln -m:1 -p:TreatWarningsAsErrors=false --ignore-failed-sources
   dotnet restore VstProbe\VstProbe.csproj -m:1 -p:RuntimeIdentifiers= -p:TreatWarningsAsErrors=false --ignore-failed-sources
   dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
   dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build
   ```
   - **构建前先关掉运行中的 `OpenUtau.exe`**，否则 dll 锁定（MSB3027/MSB3021）。
   - 构建异常后可能产出双份 avares 损坏 dll → 删 `OpenUtau/obj` 重建；历史 `obj/**` 可能整体只读（ACL 正常也删不掉）→ 整目录删除重建。
4. **测试基线 311**（约 2 分 7 秒）；沙箱内 `dotnet test` 需完整权限（testhost 要父进程句柄）。
5. **编辑工具会吃掉 UTF-8 BOM**（`.editorconfig` 要求 `*.cs` 为 utf-8-bom）→ 提交前用 `UTF8Encoding($true)` 写回，避免首行噪声 diff。
6. 主项目 `TreatWarningsAsErrors=true`（实测 0 错误 / 1579 警告，警告集中在测试项目）。

### 6.2 架构与代码

1. **Core 零 Avalonia 引用**（实测 0 命中）——这是资产：UI 可以整体重做，但**不要**把 UI 依赖泄进 Core。
2. **VST 生命周期已修复的坑不可重踩**（这是本仓库最贵的一批修复）：专用窗口线程（`VstThread` 188 行）、`RenderGate` 在飞计数、`LoadAt` 异步加载（原生加载移出 UI 线程）、Dispose 顺序（先关原生 GUI）、`vst_probe` 进程外化、槽位命令 UndoGroup。任何触碰这层的改动都要配回归。
3. `TrackEffectRack.axaml.cs` 的 **495 行命令式建 UI 是反模式样板**：新链面板必须**声明式**（视图 + VM + DataTemplate），不得再用 `BuildUI`。
4. **不要在 667 个资源键上逐个改 XAML**：MD3 化在**令牌层做映射**并逐屏收拢；现有 Plus* 语义令牌是天然接缝。
5. **13 个主题契约测试**（`ThemeContractTests.cs` 352 行）是护栏，改主题必须同步改契约。
6. **本地化已双向补齐（2026-09-25）**：EN 与 zh-CN 各 **923 条**、双向缺口 **0**。EN 补 24 条（Plus 早期只在中文加了键：`dialogs.messagebox.ok/cancel/yes/no`、`prefs.on/off`、`prefs.penplus`、`pianoroll.tool.drawlinepitch/overwritepitch` 等——英文界面此前会显示键名）；zh-CN 激活并翻译 4 条（`errors.diffsinger.downloadvocoder`、两条 oto 错误、`tip.exps`，此前被注释成英文原文）。构建 0 错误。
7. **两个盘点方法坑（别按朴素结论删键）**：① 资源文件里 `<!--...-->` 注释块中的键会被朴素正则当成"多余/重复键"（zh-CN 曾有 35 个"重复键"，实际全是被注释掉的英文原文）；② **923 个键中 299 个无字面量引用，不可据此删除**——代码存在运行时动态拼键（`ThemeManager.GetString($"languages.{key}")`、`GetString(undoNameKey)`、`GetString(edit.Name)`）。
8. **SukiUI 耦合面比想象薄**：XAML 里 `suki:` 直接引用仅 **3 文件 / 44 处**（窗口靠 `WindowEx : SukiWindow` 继承）；但它的调色板键（`AccentBrush*` / `NeutralAccent*` / `SystemControl*`）散布较广，替换时以令牌层一次性接管。

### 6.3 设计与观感

1. **Pen 的 hex 全是模拟**：实现时全部替换为 MD3 角色；**不得留硬编码色**（含橙 / 青绿两套强调色的问题）。
2. **密度是 DAW 级**（正文 7–12px、插入链 17px 行高、轨头 97px、通道 96px、推子 500px、琴键 24px）——**不要按 MD3 的宽松尺寸放大**；MD3 只提供色 / 形 / 层级 / 动效，不提供空间感。
3. **文案换成项目现有键后宽度会变**（稿子的胶囊/按钮宽度是按 Pen 文案量的）→ 按真实文案测量，不要照搬稿子宽度。
4. **Lucide 是描边集**：Pen 导出已填充化，直接用其路径；**不要混入其他图标集**（Phosphor 实心风格与 Lucide 描边风格混用会破坏一致性）。
5. **浅色主题无稿**、**效果器链面板与内置插件编辑器无稿** → 按本文件规格实现，观感以"深色稿 + MD3 规则"推导。
6. **轨道默认无颜色**（半透明黑）→ 靠文本区分；右键色板与设置色板**同源**。
7. Welcome 屏 "VST3 / ARA2 宿主集成" **只是文案，ARA2 不是目标**。
8. **验收方式要变**：主题全量动态后，"视觉基准"不再是固定截图，而是**规则**（任意种子下层级/对比度/角色映射成立）+ 结构几何对齐稿子。

### 6.4 协作与流程

1. **阶段闸门**：每一段做完 → 构建 0 错误 + 311 测试全绿 → **用户实机预览确认** → 才进下一段。
2. **协作模式（2026-09-25 定）**：**不做预先的阶段切分**——由用户点名任务、我逐项执行；我不主动扩张范围、不自行排期。任务与本文决定冲突时，以本文为准并先对齐。
3. **我无法手操界面**（无 UI 自动化）：交互行为必须由用户实机确认；数值与产物我可以核。**UI 改动默认交用户实机查看**——我只说明"改了什么、看哪里 / 试什么"，**不自己截图核对**；只有用户明确要求"自迭代"时才自己抓图（`auto-look.py` / headless 渲染）逐轮核对（2026-09-25 用户规定，已同步 AGENTS.md）。
4. **记忆体系双份且已分叉**：`.opencode/memory/`（24 条，最新）为准；`.dsh/memory/`（18 条，停在 08-12）为旧。
5. **设计交付包已入库**：`.opencode/design/UTVTU-设计交付/`（原件仍在桌面，可留作备份）；结构树在 `.opencode/design/spec/`。注意 `docs/` 被 `.gitignore` 排除（第 2 行），新文件放进去不会被跟踪。
6. **不换前端框架**（Windows 优先）；鸿蒙 / Flutter 路线另行评估，与本文件无关。

---

## 7. 预留与待定

**只预留接口（功能范畴，本轮不做）**：槽位语义（槽数 / 干湿 / 旁通策略）· 链级操作（复制链 / 整链预设）· **参数自动化**（参数描述符 + 曲线容器；曲线挂设备级还是参数级待定）· 主输出链与发送总线 · VSTi 乐器轨。

**开放项**：
1. 新增字符串键的最小清单（视图切换器选项名、链面板空态、「分离」、素材库第四页名称）。
2. 那 **30 个**现有图标在 Lucide 全库的具体选型（形状语义要对得上）。
3. 设计交付包入库位置（建议 `docs/design/` 或 `.opencode/design/`）与是否连 PNG 一起入库。
4. 浅色主题的推导验收方式（同种子生成后，逐屏对照深色稿的结构）。
5. 参数自动化的数据落点（`.ustx` 里的容器层级）。

---

## 8. 明确不做（本轮）

- **不换前端框架**（Windows 优先；Flutter / 鸿蒙另案）
- 不做 FL 式自绘窗口 / 布局保存
- 不做自绘 VST 参数界面（双击开原生）
- 不动钢琴卷帘的交互逻辑
- 不做槽位语义、参数自动化、send / 主输出链（只预留接口）
- 不使用 Pen 的中文文案（图标反向采纳 Lucide）

---

## 9. 下一步

1. **不做阶段切分**（2026-09-25 用户定）：**任务由用户逐项点名，我按名执行**；不预先排阶段、不主动扩张范围。执行时仍守既有闸门：构建 0 错误 + 测试全绿 → 用户实机预览确认 → 原子中文提交（推送需用户确认）。碰到与本文件决定冲突的做法，先回到本文件对齐。
2. **已完成（2026-09-25）**：
   - 设计交付包 + 结构规格**入库固化** → `.opencode/design/`（交付包 + `spec/` + `extract-spec.ps1`）；注意 `docs/` 在 `.gitignore` 第 2 行、新文件不被跟踪，故不放 `docs/`。
   - 本地化**双向补齐**：EN 与 zh-CN 各 923 条、缺口 0；构建 0 错误。
   - `THIRD-PARTY-NOTICES.md` 增「图标」章节（Phosphor MIT 现状 + Lucide ISC 计划替换说明）。
3. **等图标真正替换时再做**：`Icons.axaml` 头部署名与 notices 的图标条目一并改为 Lucide ISC——**现在不能改**（当前 55 个图标仍是 Phosphor，改了就是错误署名）。

---

## 附录 A：实测数据（2026-09-25，本地 `plus-develop` @ `a0e1995e`）

| 项 | 数值 |
|---|---|
| 构建 / 测试 | **0 错误** · 1579 警告（集中于测试项目）· **311 测试全绿**（2m07s） |
| 代码规模 | C# **118,423** 行（Core 62.6k / UI 25.8k / Plugin 25.3k / Test 4.4k）+ XAML **21,084** 行 |
| UI 分层 | Core **零 Avalonia 引用**；UI = 35 View + 46 Controls + 43 ViewModel |
| 代码后置 Top | MainWindow 2214 · PianoRoll 1966 · SingersDialog 498 · TrackEffectRack 495 · MixerTrackStrip 251 · PreferencesDialog 240 · TrackHeader 239 |
| VST 文件 | Core/Vst 11 文件（VstThread 188 · VstPluginRegistry 297 · VstTrackInstances 199 · VstBridge 203 · VstProbeProcess 136 · VstPluginManager 135 · VstEffect 145 · VstPluginSlot 73 · Vst2Probe 38 · IVstBridge 30 · RealVstBridge 33）；UI 3 文件（VstEditorWindow 55+47 · VstEditorViewModel 61） |
| 本地化 | EN **923** 键 / zh-CN **923** 键，双向缺口 **0**（2026-09-25 补齐；原 899 / 919，差额来自被注释块与 EN 滞后） |
| 图标 | `Icons.axaml` 117 行 / 55 键（Phosphor MIT），XAML 消费 `{DynamicResource icon-*}` |
| 设计结构抽取 | 六屏 2756 个命名元素，最大嵌套 8 层 |

## 附录 B：图标替换对照

- **可直接沿用 Pen 稿路径的 25 键**：check · chevron-down · chevron-right · cpu · equalizer · folder · folder-open · info · mic · minus · music · palette · pencil · play · redo · search · settings · skip-back · sliders · sparkles · square · undo · x · zoom-in · zoom-out
- **需从 Lucide 全库另挑的 30 键**：alert · book-open · chevron-left · chevron-up · clock · copy · download · external-link · file · file-plus · github · globe · headphones · home · layout · list · lock · metronome · monitor · package · pause · plus · refresh-cw · save · skip-forward · stop · trash · user · volume-x · wrench
- **新增（稿里也没有）约 15–20 个**：循环 · 吸附 · 量化 · 选择与框选 · 橡皮与剪刀 · 参数轨三图标（波形 / 信号 / 动态）· 音素 · 音量开 · 网格 2×2 与 3×3 · 竖三点与汉堡 · 导航三件（library-music / piano / speed）· 导入 · 图钉

## 附录 C：设计规格抽取工具

```powershell
# 从 Pen 导出的 HTML 还原结构树（data-pencil-name + 尺寸/间距/圆角），并过滤 SVG 噪声
pwsh -NoProfile -File .opencode\design\extract-spec.ps1 -Path <某屏>.html [-MaxDepth N] [-NoText]
```

- 输出：`.opencode/design/spec/{Welcome,Main-Window,Piano-Roll,Mixer,VST-Plugin,Preferences}.txt`
- 元素命名规范：`data-pencil-name`（如 `Top App Bar` · `Channel Strip · Teto` · `Insert 1 · EQ`）；图标为 `{icon:<name>}`；文本行以引号标出。
- 交付包与抽取物**均已入库**（`.opencode/design/`）。

---

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-09-25 | 五轮讨论定稿：A 单窗口 + 可分离、B 效果器统一与链面板、C 卷帘只换皮、D 莫奈动态取色、E 文案走项目现有键、F 图标**由"沿用 Phosphor"改为"全量换 Lucide"**（用户决定） |
| 2026-09-25 | 执行三项收尾：设计交付包入库 `.opencode/design/`；本地化双向补齐（EN/zh 各 923 条、缺口 0，构建 0 错误）；`THIRD-PARTY-NOTICES` 增「图标」章节。图标署名**留待实际替换时**再改 |
| 2026-09-25 | **统一颜色池落地**（第 3.G 节）：`OpenUtau.Core/Theming` + `OpenUtau/Theming`，49 角色 × 7 方案 × 对比度档位；对照 Google `material-color-utilities@0.3.0` **6468 格逐位一致**（哈希固定）+ 675 例 HCT 求解一致；角色表与常数表由 `tools/md3-codegen/` 内省生成；新增 13 个用例，全量 **324 通过** |
| 2026-09-25 | **欢迎页回归主窗口**（A7）：新建 `Views/WelcomeView.axaml`（首个只吃 md3 颜色键的界面），内嵌 MainWindow 作为初始视图；删除独立 `WelcomeWindow`；Splash 直接开主窗口；新增 6 个契约用例，全量 **330 通过** |
