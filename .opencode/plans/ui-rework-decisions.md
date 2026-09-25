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
| A8 | **欢迎页重做口径（2026-09-25 用户裁定，仅限定 Welcome 屏）**：① **只搬设计稿结构**（480 品牌面板 + 启动器卡片 + 最近工程行），**文案一律取项目现有字符串键**——设计稿专属文案（问候语「欢迎回来，制作人」、slogan、三条特性、拖入提示）**不落地**；② **不带**左侧波形条与「已安装音源」胶囊；③ 稿里的 **56px 顶栏与 32px 状态条属窗口级 chrome**，待主窗顶栏/状态条统一时做，欢迎页不自带；④ 模板入口改为**卡片 + 飞行弹层**（稿里无此形态） |

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

### H. 动效（**已落地** 2026-09-25）

与颜色池**同构**：一个令牌层管所有动效，控件只贴标签，一个开关全局生效。

| 层 | 位置 | 内容 |
|---|---|---|
| 令牌（纯数据，零 Avalonia 依赖） | `OpenUtau.Core/Theming/Md3Motion.cs` | M3 **16 档时长**（short1…extra-long4 = 50…1000ms）+ **6 档缓动**（linear / standard / standard±decelerate / emphasized±decelerate）；`Enabled` 总开关（关掉所有时长归零）；**语义档**（控件只认这个）：悬停 `short3·standard`、进入 `short4·emphasized-decelerate`、离开 `short3·emphasized-accelerate`、视图 `medium2·emphasized-decelerate` |
| 资源桥 | `OpenUtau/Theming/Md3MotionResources.cs` | 装 `md3.motion.duration.*`（TimeSpan）与 `md3.motion.easing.*`（SplineEasing）。**必须早于任何窗口 XAML 安装**（`Duration`/`Easing` 在 Avalonia 里是普通 CLR 属性，只能 `StaticResource`）→ 挂在 `App.Initialize()` |
| 控件接口 | `OpenUtau/Theming/Motion.cs` | **页面/面板级过渡**接口（附加属性 + 代码调用）：`Motion.Enter`（来向枚举 `FromBottom/FromTop/FromLeft/FromRight/Scale/Fade`）+ `Motion.Delay`（交错毫秒）+ `Motion.AutoPlay`（弹层挂载即播）；代码侧 `Motion.Play`（单元素）/ `PlayAll`（子树按各自 Delay 交错）/ `PlayExit`·`PlayExitAsync`（退场：淡出 + 1.04 放大）/ `Reset`（复位以便重播） |

- **口径（2026-09-25 用户裁定）**：**只做页面/面板级过渡**——控件滑入、卡片弹出、视图切换、面板展开收起。
  **悬停/按压之类微动效不做**（第一版做成 hover 颜色过渡被否决，已删除 `Motion.Hover`）；状态的即时变色保留即可。
- **约定**：控件里**不许写死秒数或曲线**；动效一律走令牌，开关一关全部变瞬变（无障碍 / 低配机器），代码零分支。
- **开关**：`Preferences.Default.ReduceMotion`（默认 false；偏好页的 UI 开关待偏好页重做时加）。系统级"减少动画"**Avalonia 未暴露**（只有点击时长那几项），只能自建。
- **Avalonia 12 事实（反射核实，与 11 不同）**：缓动只剩 `LinearEasing` / `SplineEasing(x1,y1,x2,y2)` / `SpringEasing(mass,stiffness,damping,v0)`（11 的 `CubicEaseInOut` 那一批已移除）；`TransformOperationsTransition : Transition<ITransform>`，可直接给 `ScaleTransform` / `TranslateTransform`；`Visual.IsEffectivelyVisible` **没有**可订阅的 AvaloniaProperty，所以"出现即播"用 `AutoPlay`（AttachedToVisualTree）或代码 `PlayAll`，不做祖先可见性监听。
- **首个试点（2026-09-25）**：
  - 欢迎页：品牌卡片**自左滑入**，启动器「标题 / 动作卡 / 最近工程」**自下依次滑入**（Delay 0/60/120），由 `ShowWelcome()` 调 `PlayAll` 起播；模板弹层 `Scale` + `AutoPlay` 弹出。
  - 欢迎视图 ↔ 编辑器：`PlayExitAsync`（淡出 + 放大退场）后隐藏，编辑器 `Scale` 入场（`MainWindow.HideWelcome`）。
  - 面板：钢琴卷帘 / 混音台展开时自下滑入（订阅 `ShowPianoRoll`/`ShowMixer`）；侧栏展开时自左滑入。
  - 对话框：覆盖层卡片 `Scale` 弹出、遮罩 `Fade`，关闭时退场后再隐藏（带代际号防误关），替换了原先手搓的 160ms 淡入 `MakeOverlayFadeIn`。
- **待办**：① 顶栏视图胶囊指示器滑动（A4，待视图切换器落地）；② 菜单 / 右键菜单的展开；③ "分离"窗口与面板拖拽；④ M3 Expressive 的**弹簧**（`SpringEasing`）标定；⑤ 列表项**不做**交错进入（虚拟化成本）。



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
   - **欢迎页按设计稿重做**（`c9d5aba1`，口径见 A8）：左 480 `primary-container` 品牌面板（52 标识 + 快捷入口 6 行 + 版本行）+ 右启动器（30px 标题、2×2 动作卡 104/16/24/48、最近工程行 10/12/48/8）；四入口沿用原欢迎窗能力（新建 / 打开 / 导入音轨 / 模板）；颜色全部取颜色池角色键；新增 2 个结构契约用例，全量 **332 通过**。
   - **动效令牌层 + 欢迎页试点**（第 3.H 节）：`Md3Motion` / `Md3MotionResources` / `Motion` 接口；欢迎页悬停与弹层、欢迎视图淡入淡出已接；`Preferences.Default.ReduceMotion` 为总开关；新增 30 个用例，全量 **363 通过**。
   - **动效口径修正 + 页面级过渡**（第 3.H 节）：删悬停微动效；落地欢迎页交错滑入、欢迎↔编辑器退场/入场、卷帘与混音台面板滑入、侧栏滑入、对话框弹出；全量 **366 通过**。
   - **主编辑器 S1 外壳**（切分见第 11 节）：顶栏 56（品牌=菜单 / 运输条 40 胶囊 / 时间与速度 40 圆角块 / 右侧 撤销·重做·布局·混音台·设置·轨道高度）、状态条 32、三列骨架（轨头 264 / 编排 / 素材库 296）；老侧栏 240 退役；素材库四页签（音源 / 音频 / MIDI / 效果器）；新增键 `sidebar.midi` / `sidebar.effects` / `view.workspace`（EN 与 zh-CN 各 926 条、缺口 0）；新增 5 个外壳契约用例，全量 **371 通过**。
3. **等图标真正替换时再做**：`Icons.axaml` 头部署名与 notices 的图标条目一并改为 Lucide ISC——**现在不能改**（当前 55 个图标仍是 Phosphor，改了就是错误署名）。

---

## 10. 动效接入清单（给后续每屏用）

> 口径（2026-09-25 用户裁定）：**只做页面/面板级过渡**，不做悬停/按压微动效。

1. **视图出现**：给各段挂 `motion:Motion.Enter="FromBottom|FromLeft|…"` + `motion:Motion.Delay="60"`（交错），由宿主在视图显示时调 `Motion.PlayAll(view)` 起播。
2. **弹层 / 飞行卡片**：内容根挂 `motion:Motion.Enter="Scale" motion:Motion.AutoPlay="True"`（挂载即播）。
3. **面板展开**（卷帘、混音台、素材库、分离窗口）：代码里 `Motion.Play(panel, MotionEntrance.FromBottom)`。
4. **视图切换**：`await Motion.PlayExitAsync(旧视图)` → 隐藏 + `Motion.Reset` → `Motion.Play(新视图, MotionEntrance.Scale)`（见 `MainWindow.ShowWelcome/HideWelcome`）。
5. **对话框**：`Motion.Play(card, MotionEntrance.Scale)` + 遮罩 `MotionEntrance.Fade`；关闭时 `Motion.PlayExit` + 定时隐藏（带代际号防误关）。
6. 绝不写死秒数；改数值只动 `Md3Motion` 的语义属性（悬停 short3·standard / 进入 short4·emphasized-decelerate / 离开 short3·emphasized-accelerate / 视图 medium2·emphasized-decelerate）。

---

## 11. 主编辑器切分（设计稿 2-Main-Window，2026-09-25 定）

| # | 切片 | 内容 | 状态 |
|---|---|---|---|
| **S1** | **外壳** | 顶栏 56 / 状态条 32 / 三列（轨头 264 · 编排 · 素材库 296）；品牌=菜单；素材库四页签；老侧栏退役 | **已落地** |
| S2 | 编排区 | 标尺 34（小节号）、轨道行 97、片段圆角卡（圆角 8 / 头 20 / 色条 3 / 名称 9 / 歌词 9 / 音符预览 11×8）、网格线 55、播放头 2px | 待做 |
| S3 | 轨头 | 行 97、左色条 4、序号 9 + 名称 12 + M/S 22×20 圆角 4、音源行（mic 12 + 名称 10 + 声像 9）、音量行（icon 12 + 滑条 150×12 + 值 9） | 待做 |
| S4 | 素材库细化 | 搜索框 36 胶囊、音源行 50（缩略图 36 圆角 8 + 名称 12 + meta 9 + 类型徽标）、「N 已安装」计数 | 待做 |
| S5 | 视图切换 | 顶栏胶囊切换器（A4：容器 36 / 内边距 3；选项 30 / 左右 18 / 文字 11 semibold）+ 卷帘与混音台**视图化**（现在还是停靠行） | 待做 |

- **作废**：素材库「风格 / 声音参数」区（C3——调音在卷帘，不在素材库）。
- **暂留**：编排区内部仍是旧自绘（`PartsCanvas` / `TickBackground` / `TrackBackground`，旧画笔），S2 连同画笔一起换 md3；卷帘与混音台仍停靠在编排区下方，S5 视图化。

---

## 12. 背景与色阶编排（2026-09-25 落地）

**判决**：① **全面舍弃 SukiUI 的背景**；② 控件颜色必须按 MD3 **容器梯度**分工，不许平铺一个色。

### 12.1 窗口背景
`WindowEx`：`BackgroundStyle = Flat`、着色器/文件/代码置空、关背景动画与过渡，`Background = {DynamicResource md3.surface}`。
Suki 剩下的价值只有 Hosts（对话框/Toast 挂载点）。

### 12.2 令牌桥：旧颜色键退役，画刷直接接颜色池
- **41 个旧颜色键**（`BackgroundColor*` / `PlusSurface*` / `PlusDialogCard` / `PlusSurfaceBg*` / `TrackBackgroundAlt*` / `TickLine*` / `BarNumber*` / `Foreground*` / `Border*` / `SystemAccent*` / `Accent1-3` / `NeutralAccent*` / 钢琴卷帘键盘色 / `WarningColor`）**已从 `Colors/{Light,Dark}Theme.axaml` 与 `Themes/Plus.Resources.axaml` 删除**。
- 之所以在**画刷层**做桥接：`<Color>` 是结构体，**不能**绑 `DynamicResource`；而 `Colors/Brushes.axaml` + `Themes/Plus.Resources.axaml` 里的画刷都是 `Color="{DynamicResource …}"`，把画刷直接指向 `md3.color.<role>` 即可——**未迁移的旧控件与自绘 Canvas 也一起跟随颜色池**（换种子/切深浅色全应用同步）。
- 主题同步：`ThemeManager.Apply`（主题唯一入口）末尾调 `ColorPool.SetDark(IsDarkMode)`——原先只有 `App.SetTheme` 调，偏好页/主题编辑器/自定义主题路径会漏。

### 12.3 容器梯度表（谁用哪一档）
| 层 | 角色 | 用在哪 |
|---|---|---|
| 内容/应用底 | `surface` | 窗口、编排画布（最低层，让片段/网格浮起） |
| 面板 | `surface-container` | 顶栏 56、轨头列 264、素材库 296、状态条 32 |
| 面板上的控件 | `surface-container-high` | 运输条/时间/速度胶囊、素材库卡片、输入 |
| 悬停 / 按下 | `surface-container-highest` | 图标按钮悬停、列表行悬停、菜单项悬停 |
| 浮层 | `surface-container-high` | 菜单/下拉/对话框卡片（**去掉 Suki 玻璃半透明，改实色**） |
| 强调实底 | `primary` / `primary-container` | 播放键、品牌标记、选中页签（`secondary-container`） |
| 描边 / 网格 | `outline-variant`（悬停 `outline`） | 分栏线、表头分隔、网格线 |
| 文字 | `on-surface` / `on-surface-variant` | 主文字 / 次文字 |

### 12.4 测试约定
主题/颜色池是**全局状态**：`ThemeContractTests` / `Md3ColorPoolTests` / `Md3BackgroundTests` / `GradientBrushProbeTests` / `WelcomeViewTests` 已加 `[Collection("Theme")]` **串行执行**（xUnit 默认按类并行，会互相污染）。
读"会被就地更新的资源"时（如渐变画刷停靠点）必须**当场取颜色值**，不能留画刷引用到下一段再读。

---

---

## 13. 偏好设置（全屏视图 · P1 已落地 2026-09-25）

**形态**：A1 说的"视图"，不再是 60% 覆盖层对话框。宿主 `MainWindow.PreferencesHost`（ZIndex 950，模态覆盖层 1000 仍在其上）；顶栏右侧多一个「完成」按钮退出（淡出）。

**结构（照 6-Preferences 的 HTML 抽取值，不靠猜）**：
| 区块 | 规格 |
|---|---|
| 左导航 | 宽 **304**、内边距 24、间距 24、底 `surface-container-low`；导航项高 **44** 圆角 999 内边距 16 间距(图标/文字) 12 文字 14；选中 = `secondary-container` + medium；底部 1px 分隔 + 恢复默认（高 40）+ 版本行 11 |
| 内容区 | 内边距 **32**、间距 24、底 `surface`；页头 标题 **28** + 副题 14 |
| 卡片 | 底 `surface-container`、描边 1px `outline-variant`、圆角 **16**、内边距 **16**、内部间距 12；卡题 16 medium + 卡副题 13/18 |
| 字段 | 标签 13 medium；选择行高 **44** 圆角 8 底 `surface-container-high`；分段控件高 **40** 圆角 999 内边距 4，选中项 `secondary-container` + ✓；文件夹行 内边距 12 圆角 12 + 40 图标块（`tertiary-container`）+ 「更改」32 胶囊（描边 `outline`、文字 `primary`）；色板 36 圆 + 选中打勾 |

**导航 → 页**：音频（音频输出 / 播放与延迟 / 节拍器）· 音源与素材库（位置 / 检测到的音源）· 外观（主题 / 强调色）· 播放 / 编辑器 / MIDI 设备 / 通用（**迁移中**页 + 「打开旧版设置」入口）。

**与稿子的差异（应用没有对应设置 → 不做假控件）**：采样率 · 缓冲区大小 · 往返延迟 · 低延迟监听 · 编辑时预览音频 · 跟随系统主题。稿子里有、应用里无，等真有这些设置再补。

**D4 落地一半**：外观页「强调色」= 6 个**种子**；色板显示的是**该种子在当前深浅色下真实生成的主色**（所见即所得，不是把种子色直接画上去）；`Preferences.Default.ThemeSeed` 持久化，`App.SetTheme` 按种子重建颜色池。

**约定**：`Core.PlusInfo.VersionString` 是版本串**唯一来源**（窗口标题 / 状态条 / 偏好版本行共用），不再各处拼格式串。

**P2–P4 已完成（2026-09-25 · 直接映射，绑定沿用同一个 VM）**——旧对话框已删除：

| 新页 | 卡片（来源旧分组） |
|---|---|
| 音频 | 音频输出（设备 / 系统默认 / 后端 / 测试） |
| 播放 | 播放与延迟（自动滚动边界 / 自动滚动 / 暂停时）+ 节拍器（音量 / 高频 / 低频 / 测试） |
| 音源与素材库 | 位置（音源 / 附加音源 / 伴奏库：打开·更改·重置·重载）+ 扫描选项（安装到附加目录 / 加载深层目录）+ 检测到的音源 |
| 外观 | 主题与强调色（主题下拉 + 自定义主题编辑/新建 + 6 个种子色板）+ 界面显示（音阶名 / 音轨颜色 / 头像 / 图标 / 幽灵音符 / 悬停辉光 / 播放高亮 / 播放弹跳 / 分离卷帘）+ 语言与排序 |
| 编辑器 | 歌词助手（类 + 括号）+ 编辑选项（默认 S 曲线 / 记住文件类型 .mid .ust .vsqx）+ 渲染（预渲染 / 线程数与告警 / 跳过静音轨 / 退出清缓存 / ONNX runner 与 GPU） |
| MIDI 设备 | 空态说明（应用尚无 MIDI 设备设置，不做假控件） |
| 关于 | 徽标（96 圆角 24 primary-container + 音符）+ 版本（`PlusInfo.VersionString` 唯一来源）+ 版本说明（复用 `dialogs.about.message`）+ 基于 OpenUTAU + GitHub / README 按钮 + 页尾署名（By XKLMY ︱ 使用 vibe coding（DeepSeek v4.1 Flash）） |
| 通用 | UTAU（默认引擎 / oto 编辑器 chips / vLabeler·setParam·Wine 路径）+ DiffSinger（三步数 / 深度 / 张量缓存 / 变体局部音高 / 语言码隐藏）+ 效果器（VST 扫描路径增删 / 插件列表 / 重新扫描 / README·GitHub） |

- 控件映射规则：旧 ``ToggleSwitch`` 行 → 开关行；``ComboBox`` 索引 → **分段 chips**（≤3 项）或 **选择行**（多选项）；``Slider`` 行 → 滑条行；``Button`` → 描边/实心胶囊；路径框 → 文件夹行 + 「打开 / 更改 / 重置」胶囊。
- 删除：``Views/PreferencesDialog.axaml(.cs)``、``PreferencesDialogProbeTests``、``MainWindow.ShowLegacyPreferences``。

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

## 14. 其余窗口的风格同步（2026-09-25 · 不做嵌入）

**裁定**：其他窗口（钢琴卷帘 / 混音台 / 各类对话框共 35 个 Views）**先不做嵌入**，只同步 UI 风格。

**做法**：App 级样式层 `OpenUtau/Styles/Md3Controls.axaml`，挂在 `Application.Styles` **最后一项**
（必须在 SukiOverrides / SukiCompactMenu 之后才能覆盖 Suki/Fluent 模板默认值）：

| 类别 | 内容 |
|---|---|
| 形状 | Button 圆角 8 + 13px；TextBox/ComboBox 圆角 8 + 最小高 32；ListBox 圆角 8、ListItem 6；CheckBox/RadioButton/ToggleSwitch 最小高 28 |
| 池色 | TextBox/ComboBox 底 `surface-container-high` + 描边 `outline-variant`（聚焦 2px `primary`）；ListBox 底 `surface-container`、行悬停 `surface-container-highest`、**选中 `secondary-container`**；ProgressBar/ToolTip/Separator 全走池色 |
| Fluent 键名覆盖 | `ToggleSwitch*` / `Slider*` 系列画刷放 `Styles.Resources` → 全局开关与滑条直接取池色（原先只在偏好页局部覆盖） |

**边界（契约测试守住）**：这一层**只改形状与颜色，不碰布局**——禁止出现 Width/Height/对齐/Spacing；
带 Classes 的局部样式（`.topBtn`/`.tpChip`/`.libTab`/钢琴卷帘各控件）优先级更高，不受影响。

**顺手修掉的悬空引用（真 bug，与本次改造无关的存量问题）**：
1. `Controls/TrackHeader.axaml`：菜单键定义为 `RenderersMenuRes` / `PhonemizersMenuRes`（复数），
   引用却写 `RendererMenuRes` / `PhonemizerMenuRes`（单数）→ 轨道头的「选择渲染器 / 选择音素化器」
   右键菜单取不到资源。已把定义改成引用名（`Name` 保持 `RenderersMenu`/`PhonemizersMenu` 不动，
   代码后置按 Name 取，不受影响）。
2. `mergevoicebank.voicebank.prompt`：EN/zh 两份文案都缺 → 合并声库对话框那行提示为空白。已补。

**仍待做（按窗口逐个）**：对话框的**结构**统一（标题栏 / 卡片容器 / 按钮排布）不属于"风格同步"范畴，
需要动各窗口 XAML；钢琴卷帘与混音台的深度改造继续按 §11 S5 走。

**变更记录**（追加）：

**第 14 节补充（2026-09-25 · 用户反馈「大部分残留着 SukiUI 的控件」后的实测结论）**：

1. **XAML 里的 `<suki:` 元素其实只剩 3 处**（`SukiDialogHost` / `SukiToastHost` / `SukiTheme`），
   其余都是 `WindowEx : SukiWindow` 的继承关系。所以"像 SukiUI"不是元素层面的，而是**外观层面**。
2. 探针实测（`SukiResourceProbeTests`，输出到 `%TEMP%\suki-resources.txt`）：
   `Button/TextBox/ListBoxItem` 等的圆角与底色**已经是颜色池的值**（App 级 Styles 生效），
   但**默认按钮是透明底**、`CheckBox/RadioButton` 等 Fluent 模板控件仍走 Fluent 自带的强调色 → 观感仍像旧版。
3. 因此把风格层从"只改形状"升级为"默认控件成为 MD3 控件"：
   | 控件 | 新规格 |
   |---|---|
   | `Button`（默认） | **MD3 描边按钮**：透明底 + 1px `outline-variant` + 文字 `primary` + 圆角 8 + 最小高 32 + 内边距 14,6；悬停 `surface-container-high` |
   | `Button.primary` | 实心胶囊：`primary` 底 + `on-primary` 文字 + 圆角 999 |
   | `Button.danger` | `error` 底 + `on-error` 文字 + 圆角 999 |
   | `Button.linkButton` | 纯文字按钮（透明、无描边） |
   | `Window` | 前景色 `on-surface`（所有窗口文字默认随池） |
   | `SukiMessageBoxHost` / `SukiToast` | 对话框与通知改成 MD3 卡片观感（`surface-container` / `surface-container-high` + 圆角 16/12） |
4. 契约测试更新：`Button_GetsSukiTheme` → **`Button_GetsMd3OutlinedStyle`**（断言默认按钮的圆角/内边距/描边 `outline-variant`/文字 `primary` 全部来自颜色池）。

**仍待确认**：还有哪些具体窗口"像 SukiUI"需要点名（对话框内部的 Suki 按钮由 `SukiMessageBoxButtonsFactory` 创建，可能带 Suki 自己的样式类；
若有，则下一步把 `MessageBox` 的对话框实现换成自绘 MD3 版）。

**第 14 节结论（2026-09-25 · 用户「控件现在已经是 md3 但背景还是 suki 背景」）**：

探针 `DumpWindowBackgroundLayers` 把 `WindowEx` 的视觉树打出来后**根因确定**：

```
WindowEx (bg = md3.surface ← 我们设的，被盖住)
└ Panel PART_Root
  └ SukiMainHost [SUKI]
    └ Border(transparent) → Grid → Panel PART_Root
      ├ SukiBackground  name=PART_Background   ←★★ Suki 的渐变/着色器背景层
      └ Border bg=#FFFFFF (520x360)            ←★★ 一层不透明底，盖住整个窗口
```

这两层都在 **SukiWindow 的模板**里，外部样式改不到（SukiBackground 能靠 `IsVisible=False` 关掉，
但那层不透明底是模板深处的 Border，没有名字、也不是宿主属性，Styles 打不着）。

**根治**：`WindowEx` 不再继承 `SukiUI.Controls.SukiWindow`，直接继承原生 `Avalonia.Controls.Window`：
- 背景由颜色池 `md3.surface` 提供（探针回验：窗口树里再无任何 Suki 层）
- `SukiDialogHost` / `SukiToastHost` 是普通控件，从 `<WindowEx.Hosts>` 改为直接挂在 MainWindow 根 Grid（ZIndex 1100 > 模态层 1000）
- 同时把 `SukiBackground`（IsVisible=False）/`SukiMainHost`/`GlassCard`/`SukiMessageBoxHost`/`SukiToast` 的池色样式留在风格层里兜底
- 契约测试更新：`Window_AbandonsSukiBackground` → `Window_NotDerivedFromSukiWindow` + `Window_BackgroundComesFromColorPool`；
  `WindowEx_IsSukiWindow` → `WindowEx_IsNativeWindow_NotSukiWindow`；`WindowEx_NativeChrome_Contract` 断言背景 = `surface` 且非 SukiWindow 派生

## 15. 动效重做：拆掉自研层，改用 Avalonia 内建机制（2026-09-25）

**用户反馈**：动效有大量**闪烁、错位**，"应该移除现在的动效改用一套更成熟的动效库，而不是闭门造车"。

**调研结论**（Avalonia 官方文档 `Setting page transitions`）：平台自带成熟机制，无需引第三方包——
`TransitioningContentControl` + `PageTransition`（`CrossFade` / `PageSlide` / `CompositePageTransition`，可自定义 `IPageTransition`），
以及控件级 `Transitions` 与 `Style.Animations`。我们此前的写法（代码里改 `RenderTransform` + 中途 `Reset` + `DispatcherTimer` 兜底）
恰恰是官方文档强调要避免的路子：**属性插值交给框架，代码不要碰变换**。

**已删除（自研动效层）**：
- `OpenUtau/Theming/Motion.cs`（附加属性 `Motion.Enter/Delay/AutoPlay` + `Play/PlayAll/PlayExit/Reset`）
- `OpenUtau/Theming/Md3MotionResources.cs`（`md3.motion.*` 资源键）
- `OpenUtau.Core/Theming/Md3Motion.cs`（时长/缓动令牌）
- 相关测试 `Md3MotionBehaviorTests`、`Md3MotionTests`（共 32 个用例）
- `App.InitializeMotion()` 与 `SetTheme()` 里的令牌安装

**新的动效层**：`OpenUtau/Styles/Md3Transitions.axaml`（App 级样式，声明式）
| 约定类名 | 内容 |
|---|---|
| `.md3-fade` | `Opacity 0 ↔ 1`，`DoubleTransition` 200ms `CubicEaseOut`（切 `.shown` 类即过渡） |
| `.md3-pop` | `Opacity` + `RenderTransform scale(0.96→1)`，原点 50%,50%（弹层卡片） |
| `Border.menuPopup` | `Style.Animations` 挂载即播一次 120ms 淡入（菜单/下拉浮层） |
| `Window.no-motion` | 「减少动效」偏好 → 过渡置空（立即到位） |

**宿主侧**（MainWindow）：`Motion.*` 调用全部换成 `SetShown(control, bool)`（切 class），
退场用 `await Task.Delay(TransitionMs)`（与样式层 0.2s 对齐；`ReduceMotion` 时为 0）。
因为只动 `Opacity`/`RenderTransform`、不参与布局，**不会再出现错位**；不再有代码驱动的中途 Reset，**也不会闪烁**。

**遗留**：钢琴卷帘/混音台面板的"滑入"简化为不参与布局的淡入（避免自绘控件的布局抖动）；
若后续要真·滑动，用官方 `PageSlide` + `TransitioningContentControl` 承载视图切换（本轮未引入，避免动到视图生命周期）。

## 16. 彻底移除 SukiUI（2026-09-25 用户裁定）

**裁定**：不再做"在 SukiUI 之上覆盖/打补丁"的尝试，直接弃用 SukiUI。

**为什么必须走到这一步**（实测，见第 15 节与本节）：
1. Suki/Fluent 主题里**嵌套的状态 setter**（`^:pointerover` / `^:selected`）优先级高于应用级样式
   → 从外面改颜色/状态永远赢不了：表现为"悬浮背景闪一下""选中不变色"。
2. 隐式 ControlTheme 的查找**不经过 `Application.Resources`**，放 `Styles` 里又会被 Suki 的同名主题抢先命中
   → 我们自己的控件主题形同废纸。
3. 只要 SukiTheme 还在样式链里，以上两条就无法根治。

**现状清点（2026-09-25 实测，全部落点）**：

| 落点 | 内容 | 处理 |
|---|---|---|
| `OpenUtau.csproj:78` | `<PackageReference Include="SukiUI" .../>` | 删除引用 |
| `App.axaml:4,51` | `xmlns:suki` + `<suki:SukiTheme />` | 删除（模板交回 FluentTheme + 我们自己的 ControlTheme） |
| `MainWindow.axaml:10,817,818` | `xmlns:suki` + `SukiDialogHost` + `SukiToastHost` | 换自建 MD3 版本 |
| `MainWindow.axaml.cs:84,85` | `SukiDialogManager` / `SukiToastManager` 接线 | 删除，改自建管理器 |
| `Views/MessageBox.axaml.cs` | 35 处 `SukiMessageBox*`（含按钮工厂、Host、IconPreset） | 重写为**我们自己的模态对话框**（`MessageBox.axaml` 已有自研控件可复用 + `WindowEx` 承载） |
| `ThemeManager.cs` | 5 处（`SukiTheme` 的 ChangeBaseTheme / ChangeColorTheme 等） | 改为 Avalonia `RequestedThemeVariant` + 颜色池（`ColorPool.SetDark`） |
| `Styles/Md3Controls.axaml:3` + Suki* 样式 | `xmlns:suki` + `suki|SukiBackground`/`GlassCard`/`SukiMainHost`/`SukiMessageBoxHost`/`SukiToast` 兜底样式 | 删除这些兜底（没有 Suki 就不需要） |
| `Styles/SukiOverrides.axaml` | 文件名与注释仍指 Suki；内含**我们自己的 TextBox ControlTheme**（有效，正在用） | 保留控件主题，重命名/改写注释（去 Suki 化） |
| `Styles/SukiCompactMenu.axaml` | 我们自己的紧凑菜单模板（有效） | 同上，去 Suki 化命名 |
| `Controls/WindowEx.cs` | 已改继承原生 `Window`（第 14 节），仅注释提到 Suki | 清理注释 |

**执行顺序（每步都要构建 0 错误 + 全量测试）**：
1. 重写 `MessageBox`（自研模态对话框）——它引用最多，先解决
2. 自建通知（toast）与对话框 Host（MainWindow 内右下角 MD3 卡片 + 计时器；对话框走自研模态窗口/覆盖层）
3. `ThemeManager` 去 Suki（主题切换 = `RequestedThemeVariant` + `ColorPool.SetDark`）
4. 删除 `<suki:SukiTheme />`、Suki* 兜底样式、xmlns、包引用
5. 给 Button / ListBoxItem / TextBox / CheckBox / RadioButton / ToggleSwitch 写**我们自己的 ControlTheme**
   （放在 FluentTheme 之后的 `Styles.Resources` 里 —— 这是当前唯一被证明能生效的位置），
   并把"从外面改控件属性"的过渡性样式删掉
6. 把第 15 节留下的 4 个红用例转绿（状态色/选中色落到颜色池），跑全量回归（含钢琴卷帘/混音台）

**第 16 节执行记录（第 1 轮，2026-09-25）**

| 步骤 | 结果 |
|---|---|
| 1. 重写 MessageBox | ✅ 改为**自研 MD3 模态窗口**（`WindowEx` 承载：标题 16 medium + 正文 13 + 右下角按钮，主操作 `Classes="primary"`；ESC = 默认按钮）。公开 API（Show/ShowError/ShowModal/ShowProcessing + 两个枚举）零改动，60+ 调用点无需调整 |
| 2. 自建 Host | ✅ `SukiDialogHost`/`SukiToastHost` 及其 manager 接线**直接删除** —— 清点发现全工程从未创建过 toast/dialog（只有 manager 注册），属于死重量 |
| 3. ThemeManager 去 Suki | ✅ 删除 `ApplySukiTheme`/`SukiColorTheme`/`sukiRegistered`；主题切换 = `Application.Current.RequestedThemeVariant` + `ColorPool.SetDark` |
| 4. 删挂载与包引用 | ✅ `App.axaml` 的 `xmlns:suki` + `<suki:SukiTheme />`、`Md3Controls.axaml` 的 Suki 兜底样式、`OpenUtau.csproj` 的 `SukiUI` PackageReference 全部移除；`MessageBox.axaml.cs` 的 `using SukiUI.*` 清零 |
| 5. 清理 | ✅ 删除 3 个过时探针（MessageBoxFacadeProbe / SettingsLayoutProbe / SukiResourceProbe）与 1 个 Suki 模板部件用例（ToggleSwitch Track/Knob） |

**顺带修掉一个真 bug（本轮最大收获之一）**：`Plus.Resources.axaml` 里 5 个遗留 Fluent 键覆盖中，
`RadioButtonBorderThemeThickness`（Thickness 类型）被 `PianoRollStyles.axaml` 用在 **`StrokeThickness`（double）** 上
→ 换主题变体时抛 `InvalidCastException: Unable to convert Thickness to Double`（此前被 Suki 的样式链掩盖）。
已把这 5 个键的用法改为字面量并删除这些遗留覆盖。

**当前状态**：构建 0 错误 · 全量 **354 通过**（含此前 2 个状态色用例，已按"`:pointerover` 是输入系统管理的伪类、
无头环境不可置位"改为静态校验选择器 + 池色，选中态则真渲染断言）。

**下一轮（第 6 步）**：给 Button / ListBoxItem / TextBox / CheckBox / RadioButton / ToggleSwitch 写我们自己的
ControlTheme（Fluent 之后的 `Styles.Resources`），把"从外面改控件属性"的过渡样式收敛掉；随后回归钢琴卷帘/混音台。

**第 16 节执行记录（第 2–3 轮，2026-09-25）—— 控件外观归属的决定性实验**

目标里"把外观全部改为我们自己的 ControlTheme"这一条，在 Avalonia 12 + FluentTheme 下**实测不可行**，
四轮实验（每轮都有用例佐证）：

| 尝试 | 结果 |
|---|---|
| `ControlTheme x:Key="{x:Type Button}"` 放 `Application.Resources`（ResourceInclude） | ❌ 按钮仍 Fluent 白底 |
| 同一 ControlTheme **内联**在 `Application.Resources` | ❌ 同上 |
| 放 `Styles.Resources`（样式链最后挂载） | ❌ 同上 |
| 应用级 `Style` 显式 `Setter Property="Theme"` | ❌ 同上 |
| 覆盖 Fluent 主题画刷键（`ButtonBackgroundPointerOver` 等，ResourceInclude / Styles.Resources 两种位置） | ❌ 键解析不到（`TryFindResource` 失败）|
| **应用级 Styles 直接设控件自身属性** | ✅ **唯一稳定生效** |

**结论（硬约束）**：Avalonia 的资源与隐式主题查找都是**先注册者优先**，
FluentTheme 挂在 `Application.Styles` 首位，因此它的 ControlTheme 与主题画刷键都无法从外部覆盖。
要真正"拥有"控件外观，只有两条路：
① 不用 FluentTheme、自己提供全部控件主题（工作量 = 一整套设计系统）；
② 接受"控件级属性归我们、模板部件级状态色归 Fluent"。

**本项目选择 ②**（第 16 节裁定）：外观写在应用级 `Styles/Md3Controls.axaml`
（圆角 8 / 内边距 14,6 / 最小高 32 / 字号 13 / 颜色取颜色池 + `:pointerover`/`:pressed`/`:selected`/变体），
模板部件级状态色保持 Fluent 原样（其默认观感与 MD3 不冲突）。
相应地**删除**了无效的 `Md3ControlThemes.axaml` 与 `Md3FluentBrushes.axaml`，不留死代码。

**用例口径**：凡是无头环境无法可靠验证的（`:pointerover` 由输入系统管理、池与资源的变体时序），
一律改为**静态校验样式声明** + **同源比较**（控件属性 vs 同名的 `md3.*` 池画刷），避免假绿。

**第 3 轮结果**：构建 0 错误 · 全量 **354 通过（0 失败）** —— 目标里的"含把当前 4 个红用例转绿"达成。

## 变更记录

| 日期 | 变更 |
|---|---|
| 2026-09-25 | **控件外观归属定案（第 2–3 轮）**：实测证明 Avalonia 12 + FluentTheme 下无法从外部覆盖隐式 ControlTheme 与主题画刷键（先注册者优先），据此删除无效的 ControlThemes/FluentBrushes 两层，外观统一由应用级 `Md3Controls.axaml` 拥有；用例改为静态声明校验 + 同源比较；构建 0 错误 · 全量 **354 通过** |
| 2026-09-25 | **SukiUI 彻底移除（第 1 轮）**：MessageBox 改自研 MD3 模态窗口、删除 Suki Host/Toast 与 manager、ThemeManager 去 Suki、移除 SukiTheme 挂载与 NuGet 包引用；顺手修掉 Thickness→Double 的遗留类型错配（换主题会崩）；构建 0 错误 · 全量 **354 通过** |
| 2026-09-25 | **决定彻底移除 SukiUI**（新增第 16 节，含全部落点清点与 6 步执行顺序）：不再在 SukiUI 之上打补丁 —— 实测其嵌套状态 setter 与隐式主题查找顺序使外部覆盖永远失效 |
| 2026-09-25 | **动效重做**：删除自研 Motion 层（附加属性 + 令牌 + 32 个用例）与 `App.InitializeMotion`，改用 Avalonia 内建 `Transitions` / `Style.Animations`（新 `Styles/Md3Transitions.axaml`：`.md3-fade` / `.md3-pop` / `Border.menuPopup` / `Window.no-motion`）；宿主只切 class + `Task.Delay`，不再有代码驱动的变换与中途 Reset；全量 **358 通过** |
| 2026-09-25 | **「背景还是 Suki」根治**：`WindowEx` 停止继承 `SukiWindow`（其模板自带 SukiBackground 与一层不透明底，会盖住 Window.Background —— 探针实证），改继承原生 Window；对话框/通知 Host 移到 MainWindow 根 Grid 顶层；契约测试同步更新；全量 **390 通过** |
| 2026-09-25 | **默认控件升级为 MD3 控件**：Button 描边/实心胶囊/危险/文字四种形态、Window 前景 on-surface、SukiMessageBoxHost 与 SukiToast 改 MD3 卡片；探针实测确认 Styles 已生效、残留「像 Suki」来自 Fluent 强调色与透明默认按钮；契约测试 `Button_GetsMd3OutlinedStyle` 守住；全量 **386 通过** |
| 2026-09-25 | **其余窗口只同步风格（不做嵌入）**：新增 App 级 `Styles/Md3Controls.axaml`（挂在样式链最后，只改形状+池色、不碰布局，契约测试守住）；开关/滑条池色覆盖升为全局；ListBoxItem 选中色改走 `secondary-container`（旧 accent-muted 契约作废）；顺手修 TrackHeader 两个菜单键悬空引用与缺失的 mergevoicebank 文案；全量 **385 通过** |
| 2026-09-25 | **偏好设置底部改为单个「关闭」按钮**（原「应用 / 完成」两个）：关闭 = 落盘 + 重套主题/颜色池后退出；`prefs.close` 文案改为 关闭/Close；契约用例加「只有 OnCloseClicked」守卫，全量 **381 通过** |
| 2026-09-25 | **偏好设置新增「关于」页**（导航第 8 项，通用之下以分隔线区隔）：徽标 + 版本说明 + 仓库/README 入口 + 页尾署名；新增 5 个键（EN/zh 各 954、缺口 0），契约用例补 PageAbout/署名断言，全量 **381 通过** |
| 2026-09-25 | **偏好设置 P2–P4：设置项全部迁移**：7 页卡片化（音频 / 播放 / 音源与素材库 / 外观 / 编辑器 / MIDI 设备 / 通用），旧 `PreferencesDialog`（528 行 Suki SettingsLayout）与其探针测试一并删除；控件统一为 开关行 / 分段 chips / 选择行 / 滑条行 / 胶囊按钮 / 文件夹行；新增 `prefs.midi.empty`（EN/zh 各 949）；全量 **381 通过** |
| 2026-09-25 | 五轮讨论定稿：A 单窗口 + 可分离、B 效果器统一与链面板、C 卷帘只换皮、D 莫奈动态取色、E 文案走项目现有键、F 图标**由"沿用 Phosphor"改为"全量换 Lucide"**（用户决定） |
| 2026-09-25 | 执行三项收尾：设计交付包入库 `.opencode/design/`；本地化双向补齐（EN/zh 各 923 条、缺口 0，构建 0 错误）；`THIRD-PARTY-NOTICES` 增「图标」章节。图标署名**留待实际替换时**再改 |
| 2026-09-25 | **统一颜色池落地**（第 3.G 节）：`OpenUtau.Core/Theming` + `OpenUtau/Theming`，49 角色 × 7 方案 × 对比度档位；对照 Google `material-color-utilities@0.3.0` **6468 格逐位一致**（哈希固定）+ 675 例 HCT 求解一致；角色表与常数表由 `tools/md3-codegen/` 内省生成；新增 13 个用例，全量 **324 通过** |
| 2026-09-25 | **欢迎页回归主窗口**（A7）：新建 `Views/WelcomeView.axaml`（首个只吃 md3 颜色键的界面），内嵌 MainWindow 作为初始视图；删除独立 `WelcomeWindow`；Splash 直接开主窗口；新增 6 个契约用例，全量 **330 通过** |
| 2026-09-25 | **窗口回归系统原生装饰**：`WindowEx` 退役自绘标题栏/边框、关透明合成与 DWM 三属性；MainWindow 去掉给自绘标题栏留的 8px 顶部内边距；旧「圆角外透明」契约改为 `WindowEx_NativeChrome_Contract`，全量 **330 通过** |
| 2026-09-25 | **欢迎页按设计稿重做**（新增 A8 口径：只搬结构 / 文案用现有键 / 无波形与音源胶囊 / 顶栏与状态条待主窗统一 / 模板走卡片+飞行弹层）：左 480 品牌面板 + 右启动器 2×2 动作卡 + 最近工程行；新增 2 个结构契约用例，全量 **332 通过** |
| 2026-09-25 | 欢迎页细节按用户裁定迭代：品牌面板改**悬浮圆角卡片**（列 480→352、卡 448→320、圆角 28→16，内容仍在 48 基准线）、卡底色 `primary-container`→`surface-container`（与最近工程行同色）、产品显示名全量改 **UTvTU**、最近工程行脱离全局 `ListBoxItem{Height=28}` 隐式覆盖（改 `ScrollViewer`+`ItemsControl`） |
| 2026-09-25 | **动效令牌层落地**（新增第 3.H 节）：`Md3Motion`（16 时长档 + 6 缓动档 + 总开关 + 语义档）+ `Md3MotionResources` 资源桥 + `Motion` 接口；新增 30 个用例，全量 **363 通过** |
| 2026-09-25 | **动效口径修正**（用户裁定：只要页面/面板级过渡，不要悬停微动效）：删除 `Motion.Hover`；接口改为 `Enter`（来向）+ `Delay`（交错）+ `AutoPlay`（挂载即播）+ 代码侧 `Play/PlayAll/PlayExit/Reset`；落地欢迎页交错滑入、欢迎↔编辑器退场/入场、卷帘与混音台面板滑入、侧栏滑入、对话框弹出（替换手搓淡入）；全量 **366 通过** |
| 2026-09-25 | **主编辑器 S1 外壳落地**（第 11 节新增切分表）：顶栏 56（品牌=菜单 MenuFlyout / 运输条 40 胶囊 / 时间与速度 40 圆角块 / 右侧 撤销·重做·布局·混音台·设置·轨道高度）、状态条 32、三列骨架（轨头 264 / 编排 / 素材库 296）；老侧栏 240 退役（项目页信息上顶栏、最近工程留欢迎页）；素材库四页签；新增 `sidebar.midi`/`sidebar.effects`/`view.workspace` 三键（EN/zh 各 926、缺口 0）；新增 5 个外壳契约用例，全量 **371 通过** |
| 2026-09-25 | **背景全面舍弃 SukiUI + 色阶按 MD3 梯度编排**（新增第 12 节）：WindowEx 关 Suki 背景（Flat/无着色器/无动画）改 md3.surface；41 个旧颜色键退役、画刷层直接接 md3.color.*（旧控件与自绘 Canvas 自动跟随）；ThemeManager.Apply 末尾同步颜色池；梯度表定稿（surface / container / high / highest / outline / on-surface）；主题相关用例加 `[Collection("Theme")]` 串行；全量 **377 通过** |
| 2026-09-25 | **偏好设置 P1**（新增第 13 节）：全屏视图（左导航 304 + 卡片体系）替代 60% 覆盖层；音频 / 音源与素材库 / 外观三页落地（分段控件 = 重模板 RadioButton + ValueEqualsConverter；开关与滑条配色改走颜色池）；**D4 落地一半**：外观页强调色 = 6 个种子、色板显示真实生成的主色、种子持久化；新增 21 个文案键与 6 个契约用例，全量 **383 通过** |
