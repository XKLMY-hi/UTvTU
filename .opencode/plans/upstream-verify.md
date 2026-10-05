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

---

## 8. 终局抽验（W25/W19/W20/W21/W14-P1C 之后）：`7c25065f`（含 `c193c110`）

> 验证点：`plus-develop` HEAD **`7c25065f`**（= Lead 所述 `c193c110` **+1 个 docs 提交**，`git merge-base --is-ancestor c193c110 HEAD` = 0 ⇒ 代码面就是 `c193c110`）。工具/红线同前（按 PID；`-ExePrefix "…\UTvTU\OpenUtau"`，未触碰别人的树）。

### 8.1 三变体复跑 —— **PASS（数字与 Lead 有 +4 差异，已核）**

| 变体 | 通过 | 失败 | 跳过 | 总计 | 退出码 |
|---|---|---|---|---|---|
| 默认 | **699** | 0 | 0 | 699 | 0 |
| Dark | **699** | 0 | 0 | 699 | 0 |
| Light | **699** | 0 | 0 | 699 | 0 |

- `-t:Rebuild`：**0 错误**。
- ⚠️ 数字口径：Lead 报 **695**，我实测 **699**（三个变体一致）。差异不是我这边多跑/少跑（总计=通过，**0 跳过**）；最可能是你那次数在 W25 用例落地之前，或统计口径差 4 条。**建议以"当前树 699"为准**再对外宣布。
- ✅ **我上轮报的 Dark/Light 各 1 红（`DocManagerExecuteCmdThreadingTests.OffThreadCommand_IsPostedToUiThread_NotExecutedInline`）已消失**：Dark/Light 满载并行各 1 次全绿 ⇒ W23 根治确认 ✔（我上轮那 7 例同类（W25 描述的那批）在本轮三变体里**一例未现** ✔）。

### 8.2 真机：混音台右侧链面板（W19）—— **拖宽/复位 PASS（像素+落盘）；折叠与重启未点**

| 检查 | 实测 | 结论 |
|---|---|---|
| 默认宽度 | 面板 `#1C2024` run **x870..1147 = 278**（+1px 分隔线 ⇒ **279/280**），与 `prefs.MixerChainWidth: 280` 一致；分隔条 1px 线在 **x=865**（`outline-variant`，命中区 7px） | **PASS** |
| 拖宽 | 从线位向左拖：**278 → 308**（run x840..1147），`prefs.MixerChainWidth` → **310.0**（落盘 ✔） | **PASS** |
| 双击复位 | 双击新线位：**308 → 278**（=279/280），`prefs` 回到 **280.0** ✔ | **PASS** |
| 折叠（无夹缝）/ 重启恢复 | **本轮未点到**链面板 chevron（宿主侧 `Button.panelToggle`，我未定位到坐标）；同套机制（同一 `PanelSplitter` + `PanelWidth/PanelShown` 绑定）的"折叠无夹缝 + 折叠态跨重启恢复"已在 §2（轨头/素材库）用像素+prefs 验过 ⇒ 维持 **仅代码审查（W19 接线）+ 同机制已验** | **未验证（本轮）** |
| 手感一致 | 与轨头/素材库同一套：1px `outline-variant` 细线 + 7px 命中区 + 双击复位 + 拖动结束落盘 ✔ | **PASS** |

### 8.3 真机：卷帘底部表达式区（W20，纵向面板）—— **代码审查 PASS；像素/交互未验证**

- **代码审查 PASS**：`PanelSplitter` 新增纵向模式（`PanelSplitter.axaml.cs:68` 可用尺寸取宿主高、`:93-95` `PanelHeight`、`:134` `IsVertical => PanelRow >= 0`）；`PianoRoll.axaml:543-547` 折叠 chevron 走新键 **`panel.collapse.pianoroll.exp`**（EN/ZH 各 1 条 ✔）；持久化 `Preferences.PanelLayout.PianoRollExpHeight = 150` + `PianoRollExpCollapsed = false`（`:470/472`）✔。
- **像素/交互：未验证（我的自动化限制，非产品发现）**：主树真机进了钢琴卷帘视图、能取到面板色 `#262A2E`（y 600..809 段），但我在该区**没能稳定定位纵向分隔条**：按 `y=826` 拖/双击三次，面板与 `prefs`（150/150）**都无变化** ⇒ 落点没命中；受预算限制未再逐像素找线。**建议**：这一条留给用户/下一轮 5 秒手验（拖一下表达式区上边缘、双击复位、折叠看有无夹缝），机制与 8.2 完全同源。

### 8.4 卷帘 W15 行为是否被 W20 破坏 —— **代码审查 PASS + 测试绿；真机像素未做**

- `PianoRoll.axaml.cs:53-67` `SmoothViewport` 接管 h/vScroll 与缩放、`SmoothViewport.cs:87` `ReduceMotion` 立即到位、`PartControl.cs:351-369` 视口指示条拖拽发 `PianoRollViewportScrollEvent`（非命令 ⇒ `Ctrl+Z` 不回退）——**接线未被 W20 改动**（`git diff` 未触及这些行；W20 只加面板宿主与 chevron）。
- 相关用例在本轮 699 全绿里（`WaveformImageTests` / `PartRedrawScopeTests` 等）✔。
- 真机三项（波形不抖/平滑滚动/指示条拖拽）仍 **未验证**（同 §7.3：前置条件可满足，交互链未走）。

### 8.5 对抗审查

| 项 | 结论 |
|---|---|
| `UiThreadAffinity` 口径是否一致 | **PASS**：`OpenUtau/UiThreadAffinity.cs` = **锚定订阅线程**（`owner = Thread.CurrentThread`，`IsOwner` 比线程）+ `Post`（owner 就地执行、否则 `Dispatcher.UIThread.Post` 异步重入队）；注释明确写了"不用 `CheckAccess()`（headless 误判，踩过两次）""不用 `Invoke`（会与 DocManager 锁互锁）"——**与既定口径逐条一致** ✔；4 个订阅者已接入（`ExpSelectorViewModel:33/100`、`NotesViewModel:1115-1127`、`PianoRollViewModel:310-313`、`PianoRoll.axaml.cs:2203-2206`）✔ |
| 守卫是否覆盖"回调在后台线程触发"的**所有**路径 | **部分覆盖（登记残余风险）**：全仓 `ICmdSubscriber` 实现约 18 个，仅上述 4 个走 `UiThreadAffinity`；其余 14 个中 **`TracksViewModel` / `PlaybackViewModel` / `CurveViewModel` / `NotePropertiesControl` 的线程相关行数 = 0**（既无 `Dispatcher` 也无 `CheckAccess`/`Post`），理论上仍可能在"后台线程加载工程"时碰到绑定对象（W25 那 7 例就是这一类，且 commit message 自述"同类缺陷第 4 次收口"）。**建议**：要么给订阅者一个统一基类/包装（`OnNext` 统一过 affinity），要么逐个写清"为什么安全"的注释——否则同类第 5 次复发只是时间问题。其余（`MainWindowViewModel`/`MixerViewModel`/`FxChainViewModel`/`SidebarViewModel`）已有 2–6 处线程处理，`FxChainViewModel` 是 `CheckAccess→Post`（W3 的 I1 修法）✔ |
| `Styles/**` + `App.axaml` 是否仍零改动 | **PASS**：`git diff 6600f417..7c25065f -- OpenUtau/Styles OpenUtau/App.axaml` = **空** ✔ |

### 8.6 收尾状态

- 我的实例已 `stop.ps1 -ExePrefix "…\UTvTU\OpenUtau"`（按 PID 6308）关闭；无残留进程；主题仍 **Dark**（未改）；主树 `git status` 只有 Lead 自己在编辑的 `upstream-triage-2026-10.md`；产品代码零改动；本轮新增截图 `fn-*.png`（12 张）。
- **给 Lead 的两条收尾建议**：① 对外宣布前把用例数口径统一到 **699**（或说明 695 的来源）；② 链面板/表达式区的**折叠**与**表达式区拖高**这两条未像素验，建议人手 1 分钟补（或下一轮我在有预算时补），其余均为 PASS/仅代码审查。
