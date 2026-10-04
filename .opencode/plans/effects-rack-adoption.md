# 实时效果器机架移植规划（上游 30d09962 → OpenUTAU Plus）

> 立项：2026-10（用户指令「你看着办吧，写规划弄点子代理，联合执行」）
> 目标提交：上游 `30d09962` *Redesign Track Polish as a live, non-modal effects rack*（2026-09-28，+1497/−203）

## 0. 一句话目标

把「**边播边调 + 模块电源开关 + 曲线屏旋钮面板**」这套实时效果器机架装进 Plus，
用 MD3 规范重绘，且与 Plus 自有的 `IEffect`/VST 效果链**共存于同一条链路**，
最终让「试听效果」达到内置插件体系的完成度。

## 1. 路线决策：剥离式移植（不搬上游传输层）

勘探结论（实证）：

| 问题 | 结论 |
|---|---|
| 上游新 `MixFxSource` 是否依赖上游传输层（`MixPlanner`/`SampleSlot`/`WaveSource` 重写/`MasterAdapter` 改造）？ | **不依赖**。它只是 `ISignalSource` 包装器：`IsReady` 透传 + `Mix` 内 `buffer[index+i] += scratch[i]`（加法混音），只用到 `IEffect`/`FxPresets`/`UMixFx`/`UTrack` |
| 我们的 `IEffect` 是否兼容？ | **完全兼容**：`Process`/`Reset`/`IsBypassed` 全都有（比上游还多一个 `LatencySamples`） |
| 我们的混音约定是否相同？ | **相同**：`EffectChain` 也是「渲染进 scratch → 逐个效果 → 加法写回」 |
| 我们三个内置 DSP（EQ/压缩/混响）是否有本地改动？ | **零改动**（自基线 29e0e16d 起无人动过）→ 可直接整文件取用上游版本 |
| `MixFxSource.cs` 现状 | 我们**删掉了它**（`fd702e05` 架构重构）→ 属于"重新引入"，不是"覆盖" |
| `RenderEngine.cs` 冲突 | 大（我们 +389 行）→ 只改 `BuildTrackOutputs` 的接线，不动我们的其余重构 |
| 结论 | **走剥离式移植**：取上游的效果层（DSP 改动 + MixFxSource 实现 + UI 三件套），接进**我们自己的**传输层。**不**引入上游 `MixPlanner`/`SampleSlot` 传输重写（会与我们的 VST 导出/短语缓存/RenderGate 冲突并丢失已完成工作） |

## 2. 交付物与分支

```
plus-develop (aac89adc)
   └─ try/fx-core        ← 冻结提交 e8a93cdc（Lead）：接口 + DSP 就绪，四分支共同基线
        ├─ try/fx-core  (UTvTU-fx-core)  Core 实时机架实现 + 测试            [fx-core]
        ├─ try/fx-ui    (UTvTU-fx-ui)    三面板机架 MD3 重绘                  [fx-ui]
        ├─ try/fx-ctl   (UTvTU-fx-ctl)   选择类控件自有 ControlTheme          [fx-ctl]
        └─ try/fx-rack  (UTvTU-fx-rack)  新控件家族 + 控件陈列室              [fx-rack]
             merge ↓（Lead 按 T6→T7→T3→T2 顺序合并，文件基本互斥）
        try/fx-core（集成）
             ↓ 全部绿 + 独立验证通过
        plus-develop（Lead 决定是否快进合并）
```

工作树：
- `G:\xklmy文件夹\vibe coding\UTvTU-fx-core`（`try/fx-core`）
- `G:\xklmy文件夹\vibe coding\UTvTU-fx-ui`（`try/fx-ui`）
- `G:\xklmy文件夹\vibe coding\UTvTU-fx-ctl`（`try/fx-ctl`）
- `G:\xklmy文件夹\vibe coding\UTvTU-fx-rack`（`try/fx-rack`）
- **主工作区 `UTvTU`（plus-develop）在联合执行期间只读**，任何人不得在其中写入。

已验证基线（主库 aac89adc）：`dotnet build` 0 错误；`dotnet test` **355 通过 / 0 失败**（3 分 14 秒）。SDK 10.0.400 实证可用（无需 global.json）。

## 3. 接口冻结（Lead 已完成，两个写入方共同基线，不得改动）

| 文件 | 冻结内容 | 说明 |
|---|---|---|
| `Core/SignalChain/Effects/BiquadEQ.cs` | `ResponseDb(freq)` + `MagnitudeDb(w)` + 脱离旁通时 `Reset()` | 整文件取上游，曲线屏的数据源 |
| `Core/SignalChain/Effects/Freeverb.cs` | `DecaySeconds(roomsize, damp)` + 脱离旁通时 `Reset()` | 同上 |
| `Core/SignalChain/Effects/SimpleCompressor.cs` | `CurveGainDb(...)`/`StaticGainDb(...)` 抽取 + 脱离旁通时 `Reset()` | 同上 |
| `Core/Ustx/UMixFx.cs` | 新增 `EqEnabled/CompEnabled/ReverbEnabled`（默认 true）；旧键 `EqBypassed/...` 保留为**反向别名**（读写即迁移，两套键取值恒互反 → 加载幂等）；`Clone()` 改用规范键 | 旧 Plus 工程 ustx 自动迁移，上游工程 ustx 直接兼容 |
| `Core/SignalChain/MixFxSource.cs` | **常量桩**：`SampleRate`/`Channels`（跟随 `AudioSettings`，不硬编码 44100）、`EqMidQ` | 让 UI 分支可独立编译；`partial class`，实现体由 fx-core 补齐 |

`IEffect.cs` **保持我们的版本不动**（我们多了 `LatencySamples`，上游版更瘦，不回退）。

## 4. 任务板

| ID | 任务 | 负责人 | 写入范围 | 依赖 |
|---|---|---|---|---|
| T1 | 接口冻结提交 | Lead | 见 §3 | — |
| T2 | Core 实时机架实现 + 测试 | fx-core | `OpenUtau.Core/SignalChain/MixFxSource.cs`、`SignalChain/EffectChain.cs`、`Render/RenderEngine.cs`、`Export/ExportSession.cs`、`PlaybackManager.cs`、`OpenUtau.Test/Core/**` | T1 |
| T3 | 三面板机架 UI + MD3 重绘 | fx-ui | `OpenUtau/Controls/Knob.cs`、`Controls/MixFxDisplays.cs`、`Views/MixFxDialog.*`、`ViewModels/MixFxViewModel.cs`、`ViewModels/TrackHeaderViewModel.cs`、`Strings/*.axaml` | T1 |
| T6 | 选择类控件接管自有 ControlTheme | fx-ctl | `OpenUtau/Styles/**`、`App.axaml`、`OpenUtau.Test/App/**` | T1 |
| T7 | 新控件家族 + 控件陈列室 | fx-rack | 新增 `Controls/{Md3PowerSwitch,Md3SegmentedControl,Md3NumericReadout,FxModuleCard,FxRackPanel}.cs`、`Views/ControlGalleryWindow.*`、`Views/MainWindow.axaml(.cs)`、`Strings/*.axaml`、`OpenUtau.Test/App/**` | T1 |
| T4 | 合并 + 集成构建 | Lead | 合并提交、冲突解决 | T2,T3,T6,T7 |
| T5 | 独立验证（含 computer use 视觉验证） | fx-verify | 只读 + 报告 | T4 |

### 4.1 设计权变更（2026-10，用户指令）

> "设计稿已无参考价值，你可以排几个代理去设计新控件"

- Pen 设计交付包（原 `C:\Users\XKLMY\Desktop\UTVTU-设计交付`）**已失效/不可定位**，不再作为验收依据；基准改为**我们自己的 MD3 令牌体系**：`Styles/Md3ControlThemes.axaml`（自有 ControlTheme 范式）+ `Styles/Md3Controls.axaml` + `Theming/Md3ColorPool.cs`（`md3.color.*`）+ `Controls/WindowEx.cs`。
- **控件语言由我们自定**（T6/T7 即为此立项）：选择类控件自有 ControlTheme；机架控件家族（电源开关/分段选择器/数值读数/模块面板/机架列表）由 fx-rack 设计并做陈列室供用户审阅。
- 写入范围据此调整：`Styles/**` + `App.axaml` 归 fx-ctl；`Strings/**` 由 fx-ui 与 fx-rack 共写，**各自加在文件末尾的独立标记区块**，合并时两块都保留；机架自身的视觉样式由 fx-ui 内联在 `MixFxDialog.axaml` 的 `Styles` 块内（不新增 Styles 文件）。

## 5. T2 验收标准（Core）

1. `MixFxSource` 实现：逐音频块从 `UTrack.MixFx` 取参（**音频线程零分配**）；主开关/模块开关**交叉淡化**（~15ms @44.1k）杜绝爆音；模块脱离旁通时清残留状态；`WrapWith` 在「无效果/总开关关/全模块空转」时**原样返回内层源**。
2. 接线：播放走 `WrapLive`（旋钮即时可听），导出走 `WrapWith(snapshot)`（导出确定、不随窗口拖动漂移），`applyMixFx:false` 干轨语义不变；**VST 链仍在其上正常工作**（`EffectChain` 只承载 VST，不再重复构建内置三件套）。
3. `AudioSettings.SampleRate/Channels` 为准，全程无 44100 硬编码。
4. 测试：移植 `MixFxSourceTest` 并新增——(a) 播放中改参数下一块生效；(b) 关主开关后回到干声且无爆音（连续帧幅度差有界）；(c) 模块开关只影响该模块；(d) 导出快照不随后续改动；(e) 空转轨道原样透传；(f) 旧 ustx `EqBypassed: true` → `EqEnabled == false` 迁移；(g) 播放中改 MixFx 不触发重渲染/重启（可行则测，不可行则代码审查 + 交用户实机确认）。
5. `dotnet build` 0 错误（`TreatWarningsAsErrors` 原样）；全量 `dotnet test` 绿。

## 6. T3 验收标准（UI）

1. 三个面板（EQ / 压缩 / 混响），每块 = **电源开关 + 曲线屏 + 旋钮组**；曲线屏由 DSP 自身数学绘制（`ResponseDb`/`CurveGainDb`/`DecaySeconds`）。
2. **MD3 重绘**：颜色只取 `md3.color.*` 色池键（**禁止硬编码色值**）；圆角/内边距/字号按既有令牌；**禁止玻璃/半透明**（2026-09 决定：对话框必须实色）；`WindowEx` 原生装饰约定；`no-motion` 时不动画。
3. **不依赖设计稿**（用户已宣布其失效）：以 `md3.color.*` 色池 + 既有卡片/圆角/字号令牌为准，新控件外观由我们自行设计；报告给出「控件 → 模板/绘制结构 → 用到的 md3 键」清单。
4. **非模态**：打开后主窗仍可操作；ESC/取消 = 恢复到打开时的设置；关闭 = 保留。
5. 保留 Plus 既有增强（`TrackHeaderViewModel`/对话框的 VST 机架接线、我们的字符串键），不得因取上游版而丢失。
6. 文案全部走键，EN/zh 双侧同步（当前各 955 键，新增后仍须相等）。
7. `dotnet build` 0 错误 + 相关测试绿 + 硬编码色值 grep 为 0。

### 6.1 新控件语言：实证结论与踩坑（供后续复用）

来自 fx-rack（新控件家族）与 fx-ctl（选择类控件接管）的实测结论，**后续写控件/主题直接照此办理**：

| # | 结论 | 说明 |
|---|---|---|
| K1 | **自绘控件不要走 ControlTheme 查找** | `Template = new FuncControlTemplate<T>(...)`（命名空间 `Avalonia.Controls.Templates`）绕开"隐式主题被 FluentTheme 抢先命中"与"跨字典 StaticResource 解析不到"；ControlTheme 路由仅用于**能改主题的控件**（fx-ctl 那批）。 |
| K2 | **代码里造 `GradientStop` + `DynamicResource` = 透明** | `GradientStop` 不是 Visual、没有资源宿主，动态资源解析为空且不重试。变通：实色，或把颜色键用法放在 XAML/资源字典（有宿主）里。 |
| K3 | **刷色不能在模板构筑期做** | 此时部件还没挂进视觉树，DynamicResource 解析为空且不重试；也不能只在"可见时"刷（未刷的 TextBlock 会继承 Fluent 默认前景，越出颜色池）。**在每个控件的 `ApplyState` 里无条件刷全部部件**，并用运行时契约测试把整棵控件树的画刷/前景与颜色池角色比对。 |
| K4 | **接管模板后要自己补 Fluent 的隐式行为** | 不定态进度条滚动动画、开关的开/关交叉淡入、ToggleSwitch 圆钮位移协议（`Canvas.Left = 轨道宽 − 画布宽`，即 40−20=20，几何与协议必须成对改）——这些原本属于 Fluent 主题而非控件代码，接管后不补就会丢；用 `ForceRenderTimerTick` 证明确实在动。 |
| K5 | **保留 Fluent 部件名是一种兼容策略** | 别处（钢琴卷帘菜单勾号、`.fader`、导出进度条）有按部件名打的 `/template/` 补丁；改名会**静默破坏**它们。改名前先全仓 grep 部件名，并加回归测试。 |
| K6 | **应用级 setter 会压住 ControlTheme setter** | 同一属性两个来源 → 悬浮/选中态闪烁那类 bug。收窄应用级选择器（如把 `Slider:pointerover …PART_DecreaseButton` 限定为 `Slider.fader:…`）时，必须确认原语义确实是给那个类用的，并验证原用法行为不变。 |
| K7 | **同名部件会互相串** | 跨控件复用 `PART_Body` 之类名字会让视觉树查找/测试取到错对象 → 部件名要带控件语义（如 `PART_Circle`）。 |
| K8 | **`no-motion` 要沿视觉树找** | 自绘控件的过渡开关需自己实现（`Md3RackKit.MotionEnabled` 沿视觉树找 `.no-motion` 类）；已知缺口见附A L3。 |
| K9 | **契约测试可能用反射取方法** | `AudioSeamContractTests` 用 `GetMethod("RenderMixdown", Public｜Static)`（无参类型）→ **加 public static 重载会 `AmbiguousMatchException` 弄红契约**；新能力应做成实例方法或改 contract 测试（fx-core 选了前者）。 |

## 7. T5 独立验证（fx-verify）

- 独立复跑：`dotnet build` + 全量 `dotnet test`，与基线（355 通过）对比。
- 对抗性审查：音频线程读取 `UTrack.MixFx` 的线程安全与撕裂读；每块分配；`IsAnythingEnabled` 首次 `Sync` 前是否误判；交叉淡化边界（`count` 非 2 倍数、`FadeFrames` 跨块）；`AudioSettings` 非 44.1k；导出确定性；旁通状态残留。
- 契约测试：MD3 风格契约、本地化键对齐、ustx 迁移往返。
- **computer use 视觉验证**：从集成工作树构建产物启动**我们自己的** `OpenUtau.exe`（只按精确路径/PID 关闭，**严禁按进程名杀**，严禁影响用户前台的原版 OU），用 `C:\Users\XKLMY\.claude\skills\image-recognize\auto-look.py` 截图、`recognize.py`/读图核对：三面板是否成型、电源开关/曲线屏/旋钮是否工作、配色是否 MD3 色池、有无玻璃、非模态是否成立。
- 输出 PASS/FAIL + 证据（截图路径、日志片段、命令与退出码）。

## 附A. 遗留项登记（本轮不做，独立小任务）

| # | 项 | 来源 | 一行修法 / 说明 |
|---|---|---|---|
| L1 | **VST 链 seek 残留**：`EffectChain` 内 VST 状态在 seek 收不到 `Reset`（`WaveMix` 不下传 Reset） | fx-core | `WaveMix` 覆写 `Reset()` 向实现 Reset 的子源下传。**既有问题**（非本次引入）；MixFxSource 已自带位置连续性清残留，本轮机架不受影响。Lead 裁决：集成阶段不动共享核心文件 |
| L2 | **`UTrack.MixFx` 缺可空标注**（`UMixFx` 而非 `UMixFx?`，与同文件注释"null = no FX"及全仓 `track.MixFx?.` 用法矛盾） | fx-ui | `OpenUtau.Core.csproj` 是 `<Nullable>enable</Nullable>`；改成 `UMixFx?` 会让所有直写 `track.MixFx.X` 冒警告，在 `TreatWarningsAsErrors` 下连锁风险 → 需连同调用点收敛一起做。当前唯一代价：UI 侧一处 `original!` |
| L3 | **ReduceMotion 不覆盖控件内部过渡**：`Window.no-motion` 只置空 `.md3-fade`/`.md3-pop`；新控件的开关位移/色变/圆钮放大/进度条滚动不受控 | fx-ctl | 与既有 `Md3ButtonTheme` 同源；要压掉需在主题外 `/template/` 打补丁，撞"不从外部改别人模板"铁律 → 需统一改造 |
| L4 | **`Md3Controls.axaml` 里 `ToggleSwitch*`/`Slider*` 等 Fluent 画刷键覆盖已失效**（这批控件改由自有 ControlTheme 接管） | fx-ctl | 保留是为不影响 ColorPicker 等仍走 Fluent 模板的控件；清理可作独立小任务 |
| L5 | **`Strings.zh-CN.axaml` 有 35 个注释掉的 `x:Key`**（非重复定义，唯一键集合双向差集 0） | fx-ui 实证 | 纯卫生问题：清理注释或保留均可 |
| L6 | 垂直 `Slider` 未实现（Fluent 12 模板同样只有水平；全仓无 `Orientation="Vertical"` 使用） | fx-ctl | 将来需要时补 |
| L7 | `d30dc489`「内置 resampler 声效随曲线驱动」（Hifisampler 曲线，13 文件 +455） | 勘探 | 独立课题：与效果机架同源但互不依赖 |
| L8 | 把机架折进「素材库 → 效果器」页签、统一 VST + 内置模块排序 UI（设计决策 B6） | 规划 §8 | 待本轮机架落地并实机确认后再做；fx-rack 的 `FxRackPanel` 已给出只读设计稿与接线点 |

## 附B. 执行记录（滚动更新）

| 阶段 | 结果 |
|---|---|
| T1 冻结提交 | `e8a93cdc`（3 个 DSP 文件整取上游 + `UMixFx` 模块开关/反向别名 + `MixFxSource` 常量桩） |
| 基线 | 主库 `aac89adc`：构建 0 错误；全量 **355 通过 / 0 失败**（3 分 14 秒）；SDK 10.0.400 实证可用 |
| T2 Core（fx-core） | 5 提交（`f6b3b415`）：MixFxSource +284、三态接线、EffectChain 收敛为 VST、测试 +19 → **374 通过 / 0 失败** |
| T6 控件（fx-ctl） | 2 提交（`3b7cc8b5`）：`Md3SelectionThemes.axaml` 548 行 5 个 ControlTheme + 14 例契约 → **369 通过 / 0 失败** |
| T3 机架 UI（fx-ui） | 3 提交（`e7ab1709`）主体完成（Knob 275 + 曲线屏 355 + 对话框 480±），**补测试与标脏修正中** |
| T7 新控件（fx-rack） | 4 提交（`0d6e7b49`）：控件家族五件套 2280 行 + 陈列室 517 行 + 菜单入口 + 17 例契约 |
| T4 集成（已完成合并） | 3 个 merge：`21255585`（fx-ctl）→ `e0c84dca`（fx-rack）→ `72edf3de`（fx-ui，字符串区块并集解冲突）；集成树 HEAD **`72edf3de`**，构建 **0 错误** + 全量 **429 通过 / 0 失败**（355+19+14+24+17 精确吻合）；EN/ZH 唯一键各 990、差集 0 |
| T5 独立验证 | 阶段一（静态对抗审查）完成：无阻塞缺陷，1 个既有缺陷 D1 已被 fx-ui 修复；阶段二进行中（视觉验证已完成一轮：三面板/陈列室/偏好页均成型；D1 真机判定待严协议复验） |
| T8 深色池修复 | fx-ctl 3 提交（`e6941ea7`）：删掉压住 ControlTheme 的 9 条 `/template/` 方块补丁 + 3 个变体色块（`.primary/.outline/.danger/.clear` 收归主题，消除字面量 `White`/`#c62828`/`#484868` 与固定辉光）；contract 测试改变体确定性 + 新增 `OPENUTAU_TEST_THEME=Light\|Dark` 会话钩子；ToggleSwitch/Slider/ProgressBar 补 `:focus-visible` 焦点环（B11） |
| 追加提交合并 | fx-rack 注释订正 → fx-ui 开关焦点环 → fx-ui D1 端到端回归测试（`MixFxDialogTests.cs:267`，真实 2 轨工程 + 对照组）；集成树 HEAD **`ec85d3f8`** |
| 干净重跑（`ec85d3f8`） | `-t:Rebuild` + 默认/Dark/Light 三遍全量；此前 `a3a1fd5f` 已实测 **431 通过 / 0 失败 × 三变体**，且 `Md3ControlThemeTests` 单跑在 Dark 下由"必红"转为 5/0（T8-A 行为成立） |
| T5 阶段二（独立验证） | **D1 严协议 PASS**：非空 2 轨工程 → 对照组 6 次 `Autosave skipped.`；实验组改参后 `Autosave` 写入 `Untitled-autosave.ustxp`（5042B→5564B，内容含 `eq_low_db: 7.2`）→ 退出出现「当前工程有未保存改动。是否保存？」；编辑时刻无异常 ⇒ 第一轮"标脏未发生"结论**作废**（空工程豁免 + 观察窗口错位导致假阴性）。机架/陈列室/偏好页/hover/焦点环（机架开关）均 PASS |
| T9（已修） | **Button 文字色转发**：只加 `TemplateBinding Foreground` **不够**（应用级 `TextBlock{Foreground}` 显式样式截断继承）→ 补 `RecognizesAccessKey="True"` 让内容走 `AccessText`（Fluent 原生写法）才真正到渲染层；base 前景取 `md3.on-surface`（保持用户已确认观感，MD3 规范态 `md3.primary` 一行可切）；两池对比度 filled primary **1.28:1 → 6.46(Light)/7.70(Dark)**；连带修 `linkButton` 选择器（内容变 AccessText 后原 `TextBlock` 规则失效） |
| T10（已修） | T9 暴露出的连带问题：`RenderWindow.axaml:51 .renderBtn` 池 primary 底 + 固定白字（深色池 **1.70:1** → 改 `md3.on-primary` 后 6.46/7.70）；`Md3ListBoxItemTheme` 同类转发缺失（选中行文字此前恒为 on-surface）→ 修后选中行 on-secondary-container/secondary-container = **7.19/7.19**；AccessText 助记键语义**接受**（es-MX 19 / nl-NL 17 个含 `_` 的值语义变化，EN/zh-CN 无影响） |
| 追加提交合并（第二批） | fx-ui 测试隔离（`d0881370`：`MixerTrackStripTest` 两处订阅按 `TrackNo` 过滤 + 自查出 `MixFxDialogTests` 同类隐患，全局监听共 3 处全部处理）→ 集成树 HEAD **`5f3799ae`** |
| T5 阶段三（像素复测） | **PASS**：机架「确定」与导出窗口主按钮文字 = `#36275D` = `md3.on-primary`，对比度 **7.70:1**（修复前 1.70:1）；普通/描边按钮仍 `md3.on-surface` 14.35:1 未变紫；机架三面板无回退。附带发现：`Styles.axaml:303-305` 的 `ListBoxItem:selected /template/ ContentPresenter{AccentBrush1Semi}` 在主题胶囊内再叠一层 `#A699C3` → 选中行局部对比度 **3.49:1**（同一类"应用级补丁压 ControlTheme"残余） |
| T11（Lead 收尾） | 删除该 `:selected` 补丁（主题 `^:selected` 已提供 secondary-container / on-secondary-container，7.19:1）；同行 `:pointerover` 补丁**保留**（无缺陷证据，避免再引入未验证的视觉变更），登记为后续候选清理 |

### 附A.1 对比度收尾清单（T10-D 只报告未改，归后续）

| 项 | 位置 | 问题 | 建议 |
|---|---|---|---|
| MenuItem 前景是**死值** | `Md3Menus.axaml:36/77` 硬绑 `md3.on-surface`；`Styles.axaml:70-77` 的 hover/selected 固定 `PlusBrushAccent`(#B0C4DE) 从未生效 | 菜单 hover/选中文字永远是 on-surface，配色意图未实现 | 二选一：模板改用池角色 `^:pointerover /template/ ContentPresenter`，或删掉死 setter（纯清理、零可见变化） |
| `.muteOn` | `MixerTrackStrip.axaml:15-17`、`MasterStrip.axaml:14-16` | #E53935 + White ≈ **4.0:1**，非池色 | 收归 `md3.error`/`md3.on-error` |
| 轨号徽章 | `TrackHeader.axaml:120-125` | 固定 White 压用户自选轨道色 → 浅色轨道下可低至 **≈1.2:1** | 按轨道色亮度动态算 on-color |
| 其它 | `Styles.axaml:142` `SelectionForegroundBrush=White`（可接受）；标题栏关闭图标 White（刻意保留）；`PlusBrushTextOnAccent` 现仅剩定义（可清理）；`Md3InputThemes` 的 TextBox 仍用 legacy `PlusTextPrimary`（两池对比度够，可池化） | — | 登记 |

### 附B.1 联合作业的流程教训（Lead 记录）

| # | 教训 | 处置 |
|---|---|---|
| P1 | **同一棵树的并发 dotnet 会互相锁 DLL**：验证者 `dotnet test` 与 Lead `dotnet build` 撞车 → MSB3027/MSB3021（6 个"错误"其实是文件锁，不是代码错误）→ 输出目录可能半更新、测试结论不可信 | 集成期明确"单写者 + 单 dotnet 使用者"：谁跑 dotnet 谁声明；换手前必须等对方说"已停"。**看到 MSB3027/3021 先查 `testhost`/`OpenUtau.Test` 进程归属，别当成编译错误** |
| P2 | 同名进程风险（原版 OU 与我们的构建同名 `OpenUtau.exe`） | 一切启动/关闭走 `.dsh/fx/launch.ps1` / `stop.ps1`（按精确路径 + PID），**禁止** `Get-Process OpenUtau \| Stop-Process` |
| P3 | 空工程掩盖标脏结论：`DocManager.ChangesSaved` 有"≤1 轨且 0 片段即视为已保存"条款 | 所有"是否提示保存/是否 autosave"的验证必须用**非空工程**（≥2 轨或 1 轨+≥1 片段） |
| P4 | 自动保存间隔 30s（`MainWindow.axaml.cs:108`） | 真机验证须等 **≥65 秒**（跨 2 个 tick），或直接比对备份文件时间戳/内容，别只看日志行 |
| P5 | 验收依赖时序 = 脆弱 | 关键行为要有**不依赖时序的跨层测试**（如 D1 的 `EndToEnd_RackEdit_MarksProjectUnsaved_ViaDocManager`：VM → DocManager 状态断言） |

## 8. 不做（本轮）

- 上游传输层重写（`MixPlanner`/`SampleSlot`/`WaveSource`/`MasterAdapter` 重构）——与我们的 VST/RenderGate/短语缓存工作冲突。
- `d30dc489`「内置 resampler 声效随曲线驱动」（Hifisampler 曲线）——独立课题，登记为后续。
- 把机架折进「素材库 → 效果器」页签、统一 VST+内置排序 UI —— 设计决策 B6，待机架落地后再做。
- 上游其余 232 个提交的整体合并（本轮只吃效果器这一条线）。

## 9. 合并与回滚

- 合并前：T2/T3 各自分支构建 + 测试绿。
- 合并后：Lead 在集成工作树跑全量构建 + 测试 + 契约，再交 fx-verify 独立验证。
- **决定规则**：构建 0 错误 + 全量测试绿 + 验证签名 → 快进合并进 `plus-develop`；任一不满足 → 保留在 `try/*` 分支并在报告中给出阻塞点。**不推送远端**（除非用户明确要求）。
- 回滚：`plus-develop` 未合并则零影响；已合并则 `git revert` 或 `git reset` 到 `aac89adc`。
