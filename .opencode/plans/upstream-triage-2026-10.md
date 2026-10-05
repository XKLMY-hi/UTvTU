# 上游合并分诊（2026-10）— Lead 战略

> 侦察：`upstream/master` 领先 `plus-develop` **232 提交 / 524 文件**；merge-base `29e0e16d`；我们领先上游 **388 提交**。
> 结论先行：**不做整体合并**（上游有暗色主题重铸、Strings 重排、Views/Controls 大改，与我们 MD3 设计体系全面打架；整体合并会付出远大于收益的冲突代价）。
> 改为**按领域选择性摘取**：Core/引擎/渲染/线程安全/工程格式优先（分歧面小、价值高），UI 只取与设计体系无关的**行为与交互能力**，纯外观/文案/CI 一律不取。

## 0. 分诊原则

| 判据 | 取 | 不取 |
|---|---|---|
| 位置 | `OpenUtau.Core/**`（引擎/DSP/渲染/模型/线程安全/格式）、测试基础设施、可交互行为 | 纯外观、主题色板、Strings 文案、CI/构建脚本、图标微调 |
| 冲突面 | 与我们的分歧文件（`Views/MainWindow`、`Controls/Mixer*`、`Controls/FxChain*`、`Styles/**`、`Strings/**`）无关 | 必须改写我们已重构的同类文件才能落地 |
| 价值 | 修真实缺陷 / 提升性能 / 增加能力 | 与 Plus 已有实现重复、或仅为上游自身重构 |

**流程铁律**：先查再取（我们 388 提交里可能已有等价实现）→ 单条或小批 cherry-pick（`-x` 保留溯源）→ 冲突大时**读实现后按我们的结构重写**，绝不把上游 UI/主题代码整体带进来 → 构建 0 错误 + 三变体全量绿 + 能测的给**数值证据**（复用 W7 音频测量装置）。

## 1. 已分配给两条线执行

### W14（fx-core）Core / 引擎 / 渲染 / 模型 — 高价值集中区

| 组 | 提交（摘） | 价值判断 |
|---|---|---|
| 内置引擎与重采样 | `95f0ad73` 内置 hifisampler、`d6519728` Worldline auto gain、`d30dc489` 曲线驱动语音效果、`dc69572f` glottal Rd 取代 tension、`ed8e5369` 修 preutterance 加长崩溃 | **必取**：直接提升音质与稳定性，与 UI 无关 |
| Worldline 重写/修复 | `04c4e610`/`ebd4fc25`/`dbfd997c` Rebuild、`ea3e92f7` R1.1 渲染器、`601c58c4` resampler 移植 C#、`ba3ddd64` caller-owned buffers、`2f7601c8` 末帧重复与曲线拟合、`177ac9a1` pYIN f0 回退、`2c423739` breathiness 跳变、`c5cc0b6c` 元音不加速、`b27daf21` ShiftGender 越界、`03a2833f` IndexOutOfRange、`5b16bb8e` 删无用原生码 | **评估后取**：`b27daf21`/`03a2833f` 是崩溃修复必取；两个 Rebuild 与 C# 移植涉及**原生库/二进制**，交 Lead 裁决（见 §3） |
| 渲染管线与线程安全 | `17bf25e7` 短语构建移出 UI 线程（不可变快照）、`7c68a087` 预算短语布局 + 合并投影读、`f773f377` 文档驱动波形读 + 渲染安全、`6196917f` 冻结槽位播放规划、`1d115473` 优先级渲染调度、`832aea2c` **波形通知不再来自后台线程**、`5d17f141`/`9138af6e` 缓存读写串行、`4be726c5` 同步写缓存、`f6b0b132` 削减快照分配、`bfb01058` 内存泄漏 | **必取**：`832aea2c` 与我们刚修的 I1 同类（后台线程改 UI）——上游的解法值得比照；其余是性能/正确性 |
| 短语/曲线正确性 | `f32e4ab5` 渲染器 padding 合并短语、`38401382` 短语间隙静音、`4696de48` 合并短语间隙音高尖刺、`2a1c8d5f` 渲染后刷新真实曲线、`d7c6aac7` 每帧校验编辑、`ec7ba520` MergePhrasesSec 设置 | **取**：直接影响听感正确性 |
| 表达式模型 | `4a3a5362`/`d01afa28`/`378dc0ea`/`4d38507b`/`5700f9e6`/`8da8bb24`/`ff6614a2`/`b4c84add`/`608a321d`/`29e1681a` 表达式图（驱动曲线/音高/音素值/masked curve/编辑器/导入导出）、`ca49f064` 解锁内置表达式 min/max、`19a297a0` `UExpressionDescriptor.Equals` | **评估**：能力价值高，但会动模型 + 序列化（与 W8 的 `.ustxp` 演进强相关）⇒ 先给方案与冲突面，别贸然全取；`19a297a0`/`ca49f064` 小而独立，可直接取 |
| Core 缺陷修复 | `a2cf1b25` singer 二次保存、`9699944e` mute 崩溃、`d53af641`/`c5977c13` 选择歌手崩溃、`80fc0a16`/`820928a5` 偏好崩溃与重存、`e34dbb43` 首次播放被立即停止、`69171824` ReactiveUI bug、`5170ebe2`/`db9065b1` Avalonia 修复与优化、`c74e89fb` ComboBox 焦点、`0e74b8d2` 无子库色校验崩溃、`0963623d` tempo 钳制、`07f4bdc4` TimeAxis 非有限 tempo、`47a7af0d` preutter=0 的 NaN 包络、`56eafb70` oto 空白输出 0、`57567b5b` legacy 校验、`6348ca0d` 未定义曲线、`81228720` NotePresets JSON、`df0555d8` Lang ID、`a46de4e0` UstFlagParser、`7684d706` 音素化工厂线程安全、`83e02c7e` oto 原子快照、`7a083786` ClassicSinger FreeMemory、`cd8375e8` 音素时长错误残留、`2c283d2b` 字典/Alt/voice color/ToneShift、`49daf1ed` Phonemizer 父表达式 getter、`d5a5d2c0` resizeNeighbor 初始化、`7f4768ed` 包络逻辑 | **必取（逐条判重）**：全是缺陷修复，风险低、收益直接 |
| 依赖/平台 | `9a2bc125` DirectML 钉版本修原生崩溃、`56436e18` Linux CUDA、`60a6c197` SDL 音频后端、`5bebe0ce` Linux/MacOS wavtool、`9df74a3f` 包体积优化、`42d24d4a` crypto XML | **评估**：`9a2bc125` 必取（崩溃）；平台类看是否与我们发布目标一致 |
| Phonemizer/字典 | `03864bf1` EN2JA 反向索引、`d0e3b6ab` ThaiVCCV、`99f1def2` 中文 VCV、`3306fdf6` presamp、`b863f7be` presamp 编码、`37ed7d72` yaml/presamp watcher、`c02084c4` 菲律宾语、`3602d9a1` KoreanCV、`6430a2bf` ん→N、`c22cae33` 跳过 legacy 插件引用的 DLL | **取**：多为纯新增/定点修复，与 UI 无关 |

### W15（m3-chain）钢琴卷帘 / 编排区视图 — 用户可感知收益最高

| 组 | 提交 | 价值判断 |
|---|---|---|
| 交互 | `9caec1a6` 拖视口指示条滚动、`1c43dc2b` 滚轮平滑滚动/缩放、`0c934958` Alt 拖复制音符、`7a67e052` PitchPointTool、`2645b69a` 曲线编辑工具扩展、`59000b1c` 刀工具继承表达式、`e2bad613` 音高覆盖模式、`64fedd61` EditTools 快捷键、`4941bf21` Ctrl+左键选择与歌词框、`afbdedf8`/`57738e37` 音符移动/刀工具钳制、`994d55a1` 复制粘贴、`ea676948` EN2JA 合并 | **取** |
| 渲染/性能 | `96473fa5` 只重绘可视部分、`ffcf2748` 滚动时波形同步稳定、`f05244c0` 波形裁剪、`1437d5e2` 峰值反转修复、`eaaf2e88` 真实曲线填充锚定画布底、`81637a33` 显示范围内高亮部件、`2b03ad56` 撤销画布短语边界 | **取** |
| 主题相关 | `a06ab28b` 暗色下渲染音高线提亮 | **只取算法/常量，颜色映射到 `md3.*`**；映射不了就跳过 |
| 需协调 | `a7b2ccb6`/`26eead42`/`7d1c32b9` TrackHeader 微调、`5f14dd89` 运输条图标对齐 | 我们的轨头/运输条已重构 ⇒ 逐个判断，必要时由 Lead 转给布局线 |

## 2. 本轮**不取**（判定为无价值或负价值）

| 类别 | 提交（例） | 理由 |
|---|---|---|
| 主题/外观重铸 | `08d9c690` 暗色主题紫调重铸、`c18b87ab` 更名、`fd4fc950`/`68a3bd97` 音符悬停光晕、`868658d1` 节拍器图标、`5f14dd89` 图标对齐 | 我们的 MD3 色池/控件语言是自研的；取外观 = 打架 |
| 文案/翻译 | `27b09aa7` Update translations、`336359d0` sync strings、`e908f918` 翻译修复、`0db2dcd2` 土耳其语 | 我们的 `Strings/*` 已按标记区块重组；取会破坏键对齐 |
| 构建/CI/仓库 | `e9141c2a` 迁 slnx、`bbfb26dd`/`fe42e443`/`069520c0`/`3f213e89`/`f590e083`/`4b1c4605`/`4febd495`/`138e3af3`/`14d4eadf`/`7240914a`/`1b9403f0`/`54f46933`/`549794a5`/`c6d17ae2`/`5afde866`/`e5ed0aae`/`3ce3d39a`/`e1200ffd`/`a5712ef7`/`a6ab9f0a`/`ccc796cb`/`4650bec0`/`8c0dc400`/`2615a257` | 与我们的仓库/发布流程无关；`slnx` 还会破坏既有脚本 |
| 空主题提交 | `a255be42`/`31c7c5d4`/`d0e3b6ab`(标题 "1")、`0d752259`/`900bb050`/`2dcd7e62` | 无信息量 |
| 已被我们吸收 | `30d09962` 实时效果机架 | **上轮已摘取**（我们据此做了 B2 内化与链面板） |

## 3. 需 Lead 裁决的高风险项

1. **原生库/二进制**：`04c4e610`/`ebd4fc25`/`dbfd997c` Rebuild Worldline、`601c58c4` C# 移植 ⇒ 涉及 `runtimes/**/*.dll` 替换。我们已实证**经典渲染端到端可用**（worldline.dll 随仓库分发、F0 实测 439.49Hz）⇒ 替换二进制必须先有"回退到旧库"的方案 + W7 装置的数值对照。
2. **`535e6857` Newtonsoft.Json → System.Text.Json**：跨全仓的大迁移，会与我们的 VST/偏好序列化路径冲突；价值（体积/一致性）低于风险 ⇒ **本轮不取**，登记。
3. **`e9141c2a` slnx**：不取（见 §2）。
4. **表达式图整套**：动模型 + 序列化（`.ustxp` 演进尚未定案，见 W8 审计）⇒ 先出方案，等用户对 `.ustxp` 形态拍板后一起做。
5. **UI 行为类新功能（未分配，下一波候选）**：`6d1d1f2f` 轨道可重排、`23779f5d` 节拍器、`eaee391a` 播放循环开关、`66e31073` Toast 通知、`a14212cd` 歌词音标提示、`17cb6642` **SVP(SynthV) 工程导入**、`7ef99328` `.m4a` 导入、`ec6c4b2e`/`ce996bac`/`c3cb8c91` 歌手对话框（flyout 搜索 / 编辑 character.yaml）、`d4206745`+`84344cbd`+`ea50e20b` DAW 集成与桥接向导、`312e3d25` 引擎包一键安装、`a4d41398` **headless UI 冒烟测试**（测试基础设施，建议提升优先级）、`ab771e1e` DEBUG 开发者工具、`bed088a4` 播放音符弹跳。
   ⇒ 其中 `a4d41398`（headless 冒烟）与 `17cb6642`（SVP 导入）我建议**下一波优先**。

## 4. 分工与验收

| 线 | 负责 | 树 | 交付 |
|---|---|---|---|
| W14 | Core/引擎/渲染/模型 | `UTvTU-up-core`（`try/up-core`） | 分诊表 + 取值 + 数值证据（W7 装置） |
| W15 | 卷帘/编排区 | `UTvTU-up-piano`（`try/up-piano`） | 分诊表 + 取值 + 交互证据 |
| W16 | 面板折叠/调宽（并行，用户痛点） | `UTvTU-panel`（`try/panel`） | 面板系统 + 布局持久化 + 截图 |
| W17 | 产品设计（未来结构缺失） | 主工作区（只写文档） | `product-design-2026-10.md` ✅ |

**验收**：构建 0 错误 + 三变体全量绿（基线 **587**）+ 数值/交互证据；由 Lead 汇总本文件为最终判断，并交独立验证抽查关键项（尤其是引擎类改动的听感/数值回归）。

## 5. 执行记录（滚动）

| 批次 | 内容 | 证据 |
|---|---|---|
| W14 第一批（9 提交，已并 `d079acff`） | `19a297a0` `81228720` `df0555d8` `47a7af0d` `cd8375e8` `6348ca0d` `07f4bdc4` `0963623d` + 缓存 per-path 锁 | `plus-develop` 上构建 0 错误、全量 **614/0** |
| W14 第二批（6 提交，已并 `5736b281`） | `b863f7be` presamp 编码、`3306fdf6` presamp 修复+测试、`c22cae33` 跳过 legacy 插件 DLL、`7f4768ed` 包络逻辑、`83e02c7e`+`bfb01058`+`d53af641`+`fe0894d3` 手工重写（oto 原子快照/真释放/空歌手名/乱序音素告警）、W7 证据套件 | `plus-develop` 上 **624/0**；本树三变体 624/0 ×3 |
| W15 卷帘（已并 `6600f417`，6 提交） | `aa68bf30` **修波形峰值上下镜像**（`0.5f + s*0.5f` —— **我们与上游同源的 bug**，独立提交）、`d20b63ac` 波形时间网格+缓存（**按我们 `part.Mix` 重写**，因上游版建在 Core 投影 seam 上）、`036c8699` 只重绘打开部件、`65b73d15` 交互小件（刀工具继承表达式 / 音高覆盖命中 / Ctrl+左键 / 守卫）、`c7ea656b` 平滑滚动（`SmoothViewport`，去偏好外壳、复用我们的 `ReduceMotion`）、`3c93134b` 拖视口指示条（自包含改写，入口原在禁改的 MainWindow） | 三变体 **602/0** ×3；性能实测：波形 25 帧重混 **25 → 5**、卷帘每帧部件重绘 **3 → 1** |
| W15 判"重复/不适用"（6 条） | `0c934958`（Alt 拖复制）、`afbdedf8`（Ctrl 移动钳制）、`64fedd61`（EditTools 快捷键）**逐行核对早已吸收**；`2b03ad56` 是 revert（我们没取它 revert 的东西）；`eaaf2e88` 前提不存在（我们没有 real-curve fill 块）；`26eead42` 我们的 Thumb 不绑颜色 | — |
| W16 面板系统（已并 `6600f417`，14 文件） | `Controls/PanelSplitter`（1px 轨 + 7px 命中区 + 双击复位 + **意图值/有效值分离**）+ 轨头 248 / 素材库 272 可拖宽可折叠 + `Preferences.Default.PanelLayout` + 顶栏「布局」/工具菜单/「重置面板布局」+ 窗口 MinWidth 800/640；配方 `.opencode/plans/panel-recipe.md` | 三变体 **605/0** ×3；真机像素取证 14 张（247/271 → 折叠 → 重启恢复 → 拖 272→356 → 重启恢复 → 重置） |
| W16 新增契约（自测发现） | `ReservedWidth()` 必须按 `PanelColumn` **升序**给优先级（左面板用当前有效宽、右面板只用 Min），否则两面板互相按当前宽夹紧会**两态振荡**并报 `Infinite layout loop detected` | 已写成回归断言 |
| 合并后终局（`6600f417`） | W14 三批 + W15 + W16 全并 | 构建 **0 错误**、全量 **662/0**（默认变体，Lead 实测） |
| 独立验证（W22，已完成） | fx-verify：默认 662/0 ✔；**Dark/Light 各 661/1 ✗**（`DocManagerExecuteCmdThreadingTests` 跨 collection 全局态竞态）；面板系统真机逐条 PASS（247/271 · 拖宽落盘 · 双击复位 · 折叠无夹缝 · 跨重启保持 · 窄窗自动收窄 · D9 未破坏）；卷帘波形镜像修复有独立数值用例 PASS；W14 两条数值证据独立复现 PASS；`Styles/**`+`App.axaml` 零改动 ✔；hover/重置菜单 = 仅代码审查 | 报告 `.opencode/plans/upstream-verify.md`（20 张 `up-*.png`） |
| W23 三条 flake 收口（已并 `20fa4bfd`） | ① `DocManager` 线程契约：根因是直接改进程级全局（实测队列 5 条只有 1 条是自己的哨兵）⇒ `DocManagerTestSetup.EnterScopedDispatcher()` 注入确定性入队通道 + 保存/恢复三项全局态，断言改哨兵计数（**2/2 红 → 5/5 绿**）；② `CacheLock` A/B：断言的是"装置有没有抢到竞争窗口"（非产品行为）⇒ 硬断言只留"共享锁 0 异常/0 不一致"，复现降为诊断（**~1/3 → 0/5**）；③ VST 用例：裸 `new UTrack()` TrackNo=0 与并行用例共用键 + `ClearAll()` 全表清空 + Bridge 不还原 ⇒ 专属 trackNo + 定点 `RemoveTrack` + 还原 Bridge + 进程级互斥闸（**1 次红 → 5/5 绿**，附 `VstGlobalStateHazardTest` 定点证明） | fx-core 树 **632/0 × 5 次**（Dark×2 + Light×2 + 默认） |
| W19 混音台链面板接入面板系统（已并 `b83f458d`） | `MixerControl` 列 `*,Auto,Auto,Auto`；五值 `PanelColumn=3/264/480/280/CenterMin=320/Invert=true`；宿主绑 `PanelWidth`/`PanelShown`；折叠入口=宿主侧 chevron（不碰 `FxChainPanel`）；13 例布局断言；真机：拖宽 657→615、折叠后**同位置竖线扫描 0 命中**、重启恢复折叠态与宽度、双击复位 | `%TEMP%\w19shots\` |
| W20 卷帘行面板（已并 `169ae3a9`，4 提交） | `PanelSplitter` **纵向模式**（列方向零改动、单独提交）+ Preferences 两字段 + 卷帘表达式区接线 + 用例幂等化；`CollapseThreshold=80`（理由：24px 选择器行 + 至少一条泳道）；折叠后空间全给中央画布（+157 实测）；`PanelLayoutTests` 28/28（18 列 + 7 纵向 + 3 真实控件） | Light **672/672**；真机三图交 verify（需 GUI 拖拽输入） |
| W21 主窗侧三项（已并 `454cd327`） | **取**编排区平滑滚动（复用 `ReduceMotion`，四个 glide；上游只给 TimelineCanvas，我们把轨头画布的 Shift/Alt/Cmd 三分支全接上）；**不取** `81637a33`（逐文件核对已由 W15 落地，重复）；**取其实质** `5f14dd89`（我们重构后无该缺陷类 ⇒ 改结构契约断言 + 真机像素：五字形中心同一水平线 y=27.9、节距 40px、偏差 ≤1px） | 默认 **667/667**；真机 A/B 像素对照（平滑 vs `ReduceMotion` 立即到位，最终位置差异 0） |
| ⚠️ 合并后 7 例确定性红（`169ae3a9` 状态） | 三变体一致 **686/7/693**；栈 = `ExpSelectorViewModel.OnListChange()`（`:98`）在**非属主线程**改绑定集合 ⇒ `Dispatcher.VerifyAccess`。**产品侧潜伏缺陷**（`ICmdSubscriber` 无亲和守卫，与 I1/MixerControl/FxChainPanel 同族）+ **测试侧可达性**（W20 真实控件用例留下仍订阅的 VM 实例） | 已派 W25（m3-chain）：产品侧按"锚定所属线程 + Post"加守卫 + 审计同类；测试侧释放订阅；判据三变体全量 0 失败 |

### 5.1 W14 的数值证据（真编译 A/B，W7 装置实测）

| 项 | 修复前 | 修复后 |
|---|---|---|
| oto 真释放（`83e02c7e`+`bfb01058`） | `Otos` 恒 **5**（`loaded=false` 假释放，内存不落） | **5 → 0 → 5**（释放后可重载且命中） |
| 快照重构回归 | — | 释放→重载后经典端到端渲染 tone 62 = **293.42 Hz**（与 W7 基线一致 ⇒ 未改行为） |
| preutter=0 NaN 包络（`47a7af0d`/`f7c86f08`） | `overlap=NaN`；**1324/26460 样本非有限（5.0%）**，峰值 0.180 | `overlap=2.9167`；**非有限 0 个**，峰值 0.110 |
| 缓存 per-path 锁（`5d17f141`/`9138af6e`） | 拆分锁：并发 40 轮 **40 次 IOException** | 共享锁：**0 异常**、内容不一致 0 次 |

### 5.2 W14 判"重复"（我们已有等价实现 ⇒ 不取，先查再取的成果）

`7684d706`（`PhonemizerFactory` 已 ConcurrentDictionary+GetOrAdd）、`a46de4e0`、`e34dbb43`、`4be726c5`、`9138af6e` 的 WorldlineRenderer 部分、`9a2bc125`（已是 DirectML 1.23.0）、`56eafb70`、`7a083786`。

### 5.3 W14 判"结构性不适用"

- `ed8e5369`（崩溃点在**上游 C# 移植版** `SynthSegment`；我们是原生 P/Invoke，无该类 ⇒ 需按同语义在调用侧做等价防护）。
- `f6b0b132`/`ec7ba520`/`17bf25e7`/`7c68a087`/`6196917f`/`1d115473`（全在上游 `Core/Pipeline/*` 冻结槽位传输层，与我们 RenderEngine/RenderGate/短语缓存结构冲突）。
- 表达式图整套（`29e1681a` 等）：动模型 + 序列化，与 `.ustxp` 演进强相关 ⇒ 本轮只出方案。

### 5.4 线程口径对比（`832aea2c`）——**保留我们的做法**

上游：7 个 Core 文件里逐点 `Task.Factory.StartNew(..., DocManager.Inst.MainScheduler)`。
我们：`DocManager.ExecuteCmd` 守卫统一锚定所属线程（非主线程则 `Log.Warning` + `PostOnUIThread`）+ 可注入。
**采纳我们的口径**，理由：① 逐点包装是调用点责任，漏一处即复发（我们刚修的两处崩溃正是此类）；② `MainScheduler` 为 null 时 `StartNew(..., null)` 直接抛；③ 每通知多一次 Task 分配。
**同时吸收上游意图**：给守卫加"`PostOnUIThread == null` 时就地执行 + 记 warning"的 2 行加固（已批准实施）。
