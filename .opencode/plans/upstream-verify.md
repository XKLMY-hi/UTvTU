# 上游三线抽查验证（W22 / task-33）—— 独立验证报告

> 对象：主树 `G:\xklmy文件夹\vibe coding\UTvTU`，分支 `plus-develop`，HEAD **`6600f417`**（含 W14 Core 三批 / W15 卷帘 6 提交 / W16 面板系统 14 文件）。
> 方法：`-t:Rebuild` + 三变体全量；computer use **按 PID/hwnd** 像素核对（`launch.ps1` / `shot_pid.py` / `stop.ps1`，**严禁按进程名杀**）；只读代码/`git` 审查；数值证据用 `dotnet test --filter` 详细输出独立复现。
> 量法工具：`C:\Users\XKLMY\AppData\Local\Temp\mx\mxmeasure.py`。证据截图：`.dsh\fx\shots\up-*.png`。
> 我**未改任何产品代码/主题**；`git status` 中仅 Lead 自己在编辑的 `upstream-triage-2026-10.md`。

---

## 0. 结论速览

| # | 项 | 结论 |
|---|---|---|
| 1 | 构建 / 三变体全量 | 构建 **0 错误**；默认 **662/0** ✔；**Dark 661/1 ✗、Light 661/1 ✗**（同一用例，非偶发主题无关 ⇒ 见 §1） |
| 2 | 面板系统（W16） | **主要行为 PASS**：默认 248/272、拖宽→288（持久化）、双击复位→248、折叠无夹缝、折叠态跨重启恢复、窄窗无重叠；**hover 主色未在像素上观测到**、**「重置面板布局」未点**（未验证） |
| 3 | 卷帘（W15） | 波形镜像修复有**独立数值用例**并复跑通过；平滑滚动/ReduceMotion/指示条拖拽**仅代码审查**（本机无音频内容，真机像素未做） |
| 4 | W14 数值证据 | **两条独立复现** ✔（`Otos 5→0→5`；`非有限值 0 个`）；**未发现"进来但没接上"的死代码**；`Styles/**`+`App.axaml` **零改动** ✔ |

---

## 1. 独立复跑：构建 + 三变体（含 1 个确定性失败）

```powershell
cd G:\xklmy文件夹\vibe coding\UTvTU
dotnet build OpenUtau.sln -t:Rebuild --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
=> 已成功生成。 | 0 个错误   exit=0
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build          # 默认
$env:OPENUTAU_TEST_THEME='Dark';  dotnet test ... --no-build
$env:OPENUTAU_TEST_THEME='Light'; dotnet test ... --no-build
```
| 变体 | 通过 | 失败 | 跳过 | 总计 | 退出码 |
|---|---|---|---|---|---|
| 默认 | 662 | 0 | 0 | 662 | 0 |
| **Dark** | 661 | **1** | 0 | 662 | **1** |
| **Light** | 661 | **1** | 0 | 662 | **1** |

失败用例（两个变体同一个）：
`OpenUtau.Test.Core.DocManagerExecuteCmdThreadingTests.OffThreadCommand_IsPostedToUiThread_NotExecutedInline`

**定性（我做的对照实验）**：
- **单独跑该用例类**（`--filter FullyQualifiedName~DocManagerExecuteCmdThreadingTests`，Light 与默认各 1 次）：**2/2 全绿**，输出 `投递数=1，playPosTick=0（应保持 0）` / `在主线程执行投递动作后 playPosTick=4321` / `无投递通道时：异常=无，playPosTick=8765` ✔
- ⇒ **只有满载并行全量时失败**，与主题无关（主题变体只是改变了并行时序）⇒ **跨 collection 全局态竞态**：该用例直接改 `DocManager.mainThread` 与 `PostOnUIThread` 两个**全局**字段，同时其它 collection 仍在并行跑，任何别的用例在此时从后台线程调 `ExecuteCmd` 都会多投递/改 `playPosTick` ⇒ `Assert.Single(queued)` / `Assert.Equal(playPosTickBefore, …)` 失败。属你们说的"**顺序掩盖型**"第二类问题，与 W12 修掉的语言用例不同源。
- **建议（后续提交）**：把该用例与**所有触碰 `DocManager` 全局态**的用例并入同一串行 collection（或给 `PostOnUIThread`/`mainThread` 做注入式 seam，别改全局）；否则全量跑 Dark/Light 时约 2/3 概率红。
- 上一轮我报的 `MixFxSourceTest.Mix_SteadyState_DoesNotAllocate` 本轮 3 次全量**未再出现**。

---

## 2. 面板系统真机验证（W16，用户痛点）

启用实例：主树产物（PID 20008/10872，hwnd 9699630/17303134）。**用户主题/配色未被改动**（收工 `ThemeName: Dark`）。

| 检查 | 实测（像素/落盘值） | 结论 |
|---|---|---|
| 默认列宽 | 轨头面板 `#1C2024` 连续 run **x13..259 = 247**（+1px 分隔线 ⇒ **248**）；库面板 **x1150..1420 = 271**（⇒ **272**）；分隔条可见线 1px `#41474D`（`outline-variant`），命中区 7px | **PASS**（与声明 248/272 一致） |
| 拖宽 | 拖轨头分隔条 +40：面板 **248 → 288**（run 13..299 = 287 + 线）；`prefs.json` 立即写 `PanelLayout.TrackHeaderWidth: 288.0` | **PASS** |
| 双击复位 | 双击分隔条：**288 → 247/248**，`prefs` 被重写回 **248.0** | **PASS** |
| 折叠（chevron） | 点面板头 chevron 后：y=500 从 x=12 起就是**中央区网格**（`#131516/#5E6770/#394147/#101417…`），**面板色 `#1C2024` 完全消失** ⇒ 面板宽 0、中央区吃掉宽度、**无残留夹缝/无 1px 线** | **PASS** |
| 折叠持久化 | `prefs`: `TrackHeaderCollapsed: true`（宽 257.0 保留）；**重启后**再次读 `prefs` 仍为 `true`，进工作区后 y=500 左侧**无 248/257 宽的面板 run** ⇒ 折叠态恢复 | **PASS**（宽度恢复仅在 prefs 层面确认，未在展开态跨重启逐像素复测） |
| 窄窗 1000 | 库面板 **x716..986 = 271** ✔ 无重叠 | **PASS** |
| 窄窗 800 | 库面板自动收到 **x542..799 = 258**，中央区仍完整、**无重叠/无夹缝** | **PASS**（<120px 自动折叠**未验**：主窗 MinWidth=800 下够不到该阈值） |
| hover 变主色 | 样式存在（`PanelSplitter.axaml:23-24 UserControl:pointerover Border.panelSplitterTrack → md3.primary`，含 120ms 过渡），但我 hover（移开再移入、等 1.5s）后线色仍为 `#41474D` | **未观测到**（合成鼠标输入未触发 pointerover）⇒ **仅代码审查** |
| 「重置面板布局」 | 三个入口代码齐备（面板 chevron / 顶栏「布局」flyout `MainWindow.axaml:291-295` / 工具菜单 `:449-453`），但我未点到菜单项 | **未验证** |
| D9（素材库列在混音台常驻） | 切到混音台视图后：右端库面板 run **x1150..1420 = 271** ✔、条体 `#262A2E`（混音台在显示） | **PASS（未被 W16 破坏）** |
| 纪律 | `git diff b944c528..6600f417 -- OpenUtau/Styles OpenUtau/App.axaml` **空** ⇒ 本轮三条线**零改动样式层/App.axaml** | **PASS** |

---

## 3. 卷帘（W15）抽查

| 检查 | 证据 | 结论 |
|---|---|---|
| 波形不再上下镜像 | 修复点 `Controls/WaveformImage.cs:251` `NormalizePeak(sample) => 0.5f - sample * 0.5f`（原 `0.5f + s*0.5f`）；**独立数值用例** `OpenUtau.Test/App/WaveformImageTests.cs:19-37`（`NormalizePeak(1)=0`、`(-1)=1`、`(0)=0.5`、单调递减、`Assert.NotEqual(inverted,…)`"新公式必须不同"）——我复跑该过滤集 **exit 0** | **代码+测试级 PASS**；真机波形像素**未验证**（本机无音频/音符内容） |
| 滚轮平滑滚动 + ReduceMotion | `Controls/PianoRoll.axaml.cs:53-67` 构造 `SmoothViewport` 并接管 hScroll/vScroll/xZoom/yZoom；`SmoothViewport.cs:87` `if (Preferences.Default.ReduceMotion)` 立即到位；`IsWheelStep` 区分精密触控板小数 delta | **仅代码审查**（未做真机手感/像素） |
| 拖视口指示条 + Ctrl+Z 不回退 | `Controls/PartControl.cs:351-369`：拖动发 `PianoRollViewportScrollEvent(…)`（纯滚动事件、非命令 ⇒ 不该进撤销栈） | **仅代码审查** |
| 只重绘可视部件 | 实现者记录：波形 25 帧重混 **25→5**、卷帘每帧部件重绘 **3→1**（本轮我未独立复现性能数字） | **未验证（引用实现者数据）** |

---

## 4. W14 上游摘取抽查：两条数值证据 + 死代码

**独立复现（我自己的 filtered run，exit 0，详细输出）**：
1. **oto 真释放**
   `Otos: 5 → FreeMemory 后 0 → EnsureLoaded 后 5；Subbanks 1；Loaded=False；oto[a] 释放后消失=True 重建后命中=True`
   （用例 `ClassicSinger_FreeMemory_TrulyReleasesSnapshot_AndReloads` 断言 `released==0`——旧实现此处为 5）
2. **preutter=0 的 NaN 包络**
   `phonemes=2：第 2 音素 preutter=0.0000 overlap=2.9167；渲染样本 26460 个，非有限值 0 个，峰值=0.110324`
   （用例 `PreutterZeroOto_WithVeryShortPreviousNote_StaysFinite` 断言 `nan==0`；静态守卫 `OpenUtau.Core/Ustx/UPhoneme.cs` → `double ratio = autoPreutter > 0 ? maxPreutter / autoPreutter : 0d;`，即上游修复的"除零取 0"）
   *注：两个用例都 `Assert.Skip` 于原生 worldline 不可用；本机 `runtimes/win-x64/native/worldline.dll` 存在且**全量 0 skipped** ⇒ 确实真跑了。*

**死代码检查（"上游代码进来但没接上"）**：逐符号查产品调用点——
- `Renderers.GetCacheLock`：**11 处产品调用**（`ClassicRenderer.cs:73`、`ExeWavtool.cs:42,78`、`SharpWavtool.cs:59` 注释…）⇒ per-path 锁已接入读写两侧 ✔
- `USinger.FreeMemory()`：由 `OpenUtau.Core/SingerManager.cs:121` 调用 ✔（不是只给测试用）
- `Presamp`：81 处（完整子系统）✔
- `autoPreutter/maxPreutter`：`UPhoneme.cs:133-143` + `NoteCommands.cs:531-537` + `NoteEditStates.cs:1477` ✔
- `NativeWorldline`：**仅存在于 `OpenUtau.Test/TestSupport`**（测试支撑，非产品代码）⇒ 不算死代码 ✔
⇒ **未发现"进来但没接上"的项**。

**顺带证实 W11/W12**（从用户 `prefs.json` 读到）：`VstScanPathsSeeded: true` + `VstScanPaths = [C:\Program Files\Common Files\VST3, %LOCALAPPDATA%\Programs\Common\VST3]` ✔；`VstCachedPlugins` **28 条**（含 VST3/VST2 混合）✔。

---

## 5. 未验证 / 需实机确认

1. 面板 **hover 变 `md3.primary`**（我的合成输入触发不了 pointerover；请鼠标手试一下分隔条悬停/拖动变色）。
2. **「重置面板布局」**菜单项（顶栏「布局」flyout / 工具菜单）未点到 ⇒ 未验证它真的回到 248/272。
3. **<120px 自动折叠**：主窗 MinWidth=800 下够不到；需在分离窗（MinWidth 640）或极窄宿主里试。
4. 卷帘真机三项（波形镜像、平滑滚动/ReduceMotion、指示条拖拽 + Ctrl+Z）——本机无音频/音符内容；另 `UTvTU-up-piano` 树当时被另一位 teammate 占着在用。
5. 拖宽后**展开态**跨重启的逐像素复测（我只确认了 prefs 值跨重启保持 + 折叠态像素）。

---

## 6. 过程与诚实披露

- 期间同一工作区里出现了**两个实例**：我的（主树，PID 20008）与另一位 teammate 的 `UTvTU-up-piano`（PID 15400）；`.dsh/fx/pid.txt` 被共享地删除/覆盖，导致我两次截图工具报"PID 不匹配"。**我改为按 hwnd+PID 硬编码操作**，收工用精确前缀 `-ExePrefix "…\UTvTU\OpenUtau"`（不含 `UTvTU-up-piano`），**全程未触碰对方实例**（收工时清单里也不再有它——它自己退出的，不是我关的）。
- 我一度把**重启后的欢迎页**当成工作区做了两次测量（误读为"面板未恢复"），发现后重测并更正（§2 的结果是重测后的）。
- 未改主题（Dark 保持）、未改任何产品代码；唯一写入 = 本报告 + `.dsh/fx/shots/up-*.png`（20 张）。
- 证据索引：`up-02-panels.png`（默认 248/272）、`up-03-dragged.png`（288）、`up-06/09`（双击复位）、`up-14-collapsed.png`（折叠无夹缝）、`up-15-w1000.png`、`up-16-w800.png`、`up-19-restored-workspace.png`（重启后折叠态）、`up-20-mixer-d9.png`（混音台 + 库列 271）。

---

## 7. 追加：Lead 指派的两条收口（fx-verify 二次作业）

### 7.1 分隔条 hover / 拖拽变 `md3.primary` —— **headless 伪类注入法在本控件上不生效（已试，附证据）**

按 Lead 指定的方法写了程序化探针（真实 `Window` + 真实布局 + `PanelSlot` 双向绑定；期望值取自 `Application.Current` 的应用资源，不硬编码色值），并按本仓既有做法推进 headless 渲染时钟 `AvaloniaHeadlessPlatform.ForceRenderTimerTick(30)`（因为细线上挂了 `BrushTransition` 0.12s，`PanelSplitter.axaml:17-21`）。

实测（Dark / Light 各复现一遍）：

| 状态 | 注入 | 结果 |
|---|---|---|
| 常态 | — | `md3.outline-variant` ✔（细线精确 **1px**，`Assert.Equal(1.0, track.Width)` 通过） |
| 拖拽中 | `splitter.Classes.Set("dragging", true)` | **未变主色**：Expected `md3.primary`（Dark `#CFBDFE` / Light `#65558F`）／Actual `md3.outline-variant`（Dark `#49454E` / Light `#CAC4CF`） |
| hover | `((IPseudoClasses)splitter.Classes).Set(":pointerover", true)` | 同上，**未变主色** |

- 还做了「立即读 vs 推进时钟后读」两次取色对照：**都不是主色** ⇒ 排除"过渡没跑完"这一解释。
- 差异点：`Md3ControlThemeTests` 里对 **Button 的 ControlTheme** 做伪类注入是可用的；本控件的两条条件选择器写在 **`UserControl.Styles`** 里、主体是后代 `Border.panelSplitterTrack` + **祖先条件**（`UserControl:pointerover` / `UserControl.dragging`）——在这个 headless 夹具下**基态规则生效、祖先条件规则不生效**。本仓测试也**没有** headless 鼠标输入助手（全仓 grep `MouseMove(`/`MouseDown(` = 0），走不了真实输入路径。
- **处置**：探针文件**已删除**（不给共享树留红灯）；`PanelSplitter.axaml:23-28` 维持 **仅代码审查**，hover 腿请人工在真机上把鼠标停到分隔条上看一眼（1 秒）。
- 口径：**"拖拽/悬停变主色"在 headless 拿不到确定性证据**（该模式在本仓夹具的固有限制，非测量方法问题）；而**折叠/宽度/双击复位/持久化/无夹缝**这些主行为已在 §2 用真机像素 + 落盘值验证 ✔。

### 7.2 「重置面板布局」—— **仅代码审查 PASS**

- 入口 → 同一方法：`MainWindow.axaml.cs:1370 OnMenuResetPanelLayout → viewModel.ResetPanelLayout()`；顶栏「布局」flyout（`MainWindow.axaml:291-295`）与工具菜单（`:449-453`）同一 handler；命令入口 `MainWindowViewModel:247 ResetPanelLayoutCommand = ReactiveCommand.Create(ResetPanelLayout)`。
- 行为 → 同一份持久化：`MainWindowViewModel:289 ResetPanelLayout() { TracksPanel.Reset(); LibraryPanel.Reset(); RaisePanelFlags(); PersistPanelLayout(); }`；落盘映射全仓只有一处 `SavePanelLayout(prefs, tracks, library)`（`:260`）⇒ **重置 = 回 `PanelSlot.DefaultWidth`(248/272) 并立即落盘** ✔（与实现者真机取证一致）。

### 7.3 卷帘真机三项 —— 仍 **未验证**，但**前置条件已核清（不是缺音源）**

- 主树产物**有音源**：`OpenUtau\bin\Debug\net8.0-windows\Singers\重音テト OU用日本語統合ライブラリー` ✔，默认渲染器 `WORLDLINE-R`、`runtimes/win-x64/native/worldline.dll` 在 ✔。
- 所以"波形镜像/不抖"**本可验**，但我本轮没走完「新建 → 加轨 → 画音符 → 等渲染 → 卷帘看波形」的交互链（画音符要连续拖拽合成输入，前几轮已证在我的自动化下不可靠；当轮 `UTvTU-up-piano` 树又正被 W20 占用）。
- **留给下一轮/人手的可复现步骤**：主树启动 → 新建 → 加一条轨（歌手选上述音源）→ 工作台画 2–3 音符 → 双击进卷帘 → 等日志渲染/波形生成 → 截图量波形上下包围盒；再滚轮平滑滚动（`ReduceMotion` 开/关各一次）→ 拖视口指示条 → `Ctrl+Z` 确认视口不回退。
- 现状口径：波形镜像 = **代码+单测级 PASS**（`WaveformImageTests`，§3）；平滑滚动/ReduceMotion/指示条拖拽+Ctrl+Z = **仅代码审查**；真机三项 = **未验证（前置条件可满足，本轮未执行）**。

### 7.4 本轮附加说明（共享工作区）

- 我在主树构建时**撞上另一位 teammate 的半成品改动**：`MixerControl.axaml.cs:107-124` 引用 `Preferences.PanelLayoutPreferences.MixerChainWidth/MixerChainCollapsed`，而当时 `Preferences.cs` 还没加这两个字段 ⇒ 我 Dark 那一次 `dotnet test` **编译失败**（不是我的用例、也不是质量回归）；几分钟后 `Preferences.cs:464-465` 补上即恢复 ✔ ⇒ 提醒：共享树上成对改动最好同一次落地。
- 本轮唯一新增文件 = 本报告；探针测试文件已删除；`git status` 仅剩本报告（未跟踪）。
