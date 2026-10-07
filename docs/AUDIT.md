# UTvTU 全面代码审计报告

> 审计日期：2026-07-30
> 审计范围：plus-develop 分支相对 upstream/master 的全部 Plus 改动（118 文件，+7827/-737 行）
> 审计方法：5 个方向并行深度审计（架构分层 / VST 宿主 / Mixer 与 SignalChain / UI 层与测试 / 格式兼容与构建 CI）
>
> **状态：全部完成 ✅**（2026-07-30 同天）
> - 阶段 0 止血修复 9 项
> - 阶段 1 VST 并发安全 + 跨平台降级（含 C++ bridge 重编 + C# DllImport 宽字符同步）
> - 阶段 2 架构分层重构（UCommand + ViewModel + 窗口统一 + ResizerBar）
> - 阶段 3 远期地基（Latency/AudioSettings/Reset 默认/接口桩）
> - B4 进程外 VST 探头 vst_probe.exe
> - 28 个新 xUnit 测试 + 3 个 Claude Skill
> - 6 个 atomic commit 在 plus-develop 分支

---

## 总览：五维健康度

| 维度 | 评级 | 一句话结论 |
|------|------|-----------|
| 架构分层 | 🔴 差 | View 层系统性越权，VST 三个窗口无 ViewModel，命令模式基本缺失 |
| VST 宿主 | 🔴 中下 | 2 个生产级阻塞缺陷 + 无锁队列实现错误 + 无跨平台降级 |
| Mixer/SignalChain | ⚠️ 中 | 功能可用，但导出路径有正确性 bug，远期愿景几乎无扩展点 |
| UI 层 | ⚠️ 中下 | 窗口体系统一一半，抽象缺口是 25 个 fix 提交的根源 |
| 测试覆盖 | 🔴 差 | Plus 新增模块近零覆盖，Avalonia.Headless 引入了却没用 |
| 格式/构建/CI | ⚠️ 中下 | VST 参数会丢、autosave 调错函数、CI 不覆盖 plus-develop |

**整体判断**：功能交付完整、架构方向正确（IEffect 插入 EffectChain、UID 解耦、WindowEx 基类），但实现层在**并发安全、分层纪律、测试兜底**三处存在系统性短板。README 宣称的功能大多"能跑"，但有若干"静默错"的缺陷，且最近 25 个 fix 提交暴露了"靠试错推进、无抽象无测试"的模式。

---

## 一、必须优先修复的缺陷（按危害排序）

### A. 数据正确性 / 数据丢失

#### A1 — `SaveAllStates` 从未被调用（VST 参数保存失效）【严重】
- 位置：`OpenUtau.Core/Vst/VstPluginManager.cs:70` + `OpenUtau.Core/Ustx/UProject.cs` `BeforeSave()`
- 现象：`SaveAllStates(trackNo)` 把活动 effect 的 `SaveState()` 刷新到 `slot.StateData`，但全仓库无任何调用点。`slot.StateData` 只在 `VstTrackInstances.UnloadAtLocked`（卸载时）被更新。`UProject.BeforeSave()` 也不调用它。
- 后果：用户在 VST 原生 GUI 里改了参数后直接保存工程，持久化的是上次加载/卸载时的旧 state（或 null），**参数丢失**。这是 Plus 核心卖点的功能缺陷。
- 建议：在 `UProject.BeforeSave()` 中遍历 tracks 调用 `VstPluginManager.Inst.SaveAllStates(track.TrackNo)`。

#### A2 — 导出含 VST 的实时录制路径有正确性缺陷【严重】
- 位置：`OpenUtau/Views/RenderWindow.axaml.cs:122-145` + `MasterAdapter`
- 现象：导出循环 `while ((totalRead = recorder.Read(buf, 0, buf.Length)) > 0)` 直接拉取 `MasterAdapter`，但 `engine.RenderProject` 内部 `RenderMixdown(wait:false)` 渲染在后台 Task 跑，导出循环启动时源几乎必然未就绪。`MasterAdapter.Read` 在 `!IsReady` 时返回 `count`（静音）而非 0。
- 后果：
  - 导出文件**头部被写入一段静音**，真实音频接在其后，整轨时间轴整体后移。
  - `totalRead` 每轮重新赋值（非累加），`sec = totalRead/44100/2` 永远是单块约 0.046s，**进度条 70→95 段永不推进**。
  - 循环不检查 `_cts`，取消只在 `OnClosed` 里 cancel，循环本身不读 token，可能**死循环**。
  - 拉取速率无节流，而部分旧 VST2 依赖实时时钟，非实时下行为异常。"实时录制"名不副实。
- 建议：导出走 `ExportAdapter`（已有，`!IsReady` 时抛异常而非写静音），渲染全部就绪后再拉取；或给 `MasterAdapter` 增加"等待就绪后再返回"的导出模式；循环内检查取消 token。

#### A3 — autosave/backup 调错函数【严重】
- 位置：`OpenUtau.Core/DocManager.cs:156,185`
- 现象：`CrashSave` 和 `AutoSave` 都调用 `Format.Ustx.AutoSave(...)` 而非 `Format.Ustxp.AutoSave(...)`，但文件名后缀是 `.ustxp`。
- 后果：写出的文件缺少 `ustxpVersion` 字段，语义上是"披着 .ustxp 扩展名的 .ustx 内容"；重载时 `ustxpVersion == null` 被当作 legacy 跳过 Plus 迁移。
- 建议：两处改成 `Format.Ustxp.AutoSave`。`Ustx.Save/Load/AutoSave` 实际已无调用方，可视为死代码删除。

#### A4 — 版本闸口用错常量【严重】
- 位置：`OpenUtau.Core/Format/Ustxp.cs:105`
- 现象：`if (project.ustxVersion > kUstxpVersion)` 把 USTX 基线版本（0.9）和 USTXP 版本（1.0）比较。原版 `Ustx.Load` 用 `kUstxVersion`(0.9)。
- 后果：任何 `ustxVersion <= 1.0` 的文件都能通过，包括未来上游出的 ustx v1.0 文件——它会无声加载但可能含未支持特性。
- 建议：改为 `project.ustxVersion > Ustx.kUstxVersion`。

#### A5 — Base64 state 反序列化无容错【中】
- 位置：`OpenUtau.Core/Vst/VstPluginSlot.cs:22-25`
- 现象：`StateDataBase64` 直接 `Convert.FromBase64String(value)`，损坏的 base64 抛 `FormatException`，未被 try/catch 包裹，会冒泡到 `Yaml.DefaultDeserializer.Deserialize<UProject>` 导致**整个工程加载失败**。
- 建议：setter 内 try/catch，失败时记日志并置 null（降级为"无 state"而非"打不开工程"）。

#### A6 — VST2 UID 用 `string.GetHashCode()` 不可移植【中】
- 位置：`OpenUtau.Core/Vst/VstPluginRegistry.cs:227`
- 现象：`string uid = $"vst2:{dllName.GetHashCode():x8}";`。.NET 文档明确不保证 `GetHashCode` 跨版本/跨机器稳定。
- 后果：同一 VST2 dll 在不同机器扫描出的 UID 不同，`.ustxp` 中保存的 `PluginUid` 在另一台机器无法解析。
- 建议：改用文件内容哈希（xxHash，项目已依赖 `K4os.Hash.xxHash`）或 dll 名+大小+mtime 组合。

### B. 崩溃 / 并发安全

#### B1 — 音频线程 use-after-free 竞态【严重·生产阻塞】
- 位置：`OpenUtau.Core/Vst/VstEffect.cs:61-74` + `VstTrackInstances.cs:80-89`
- 现象：`GetActiveEffects` 返回 `volatile _effects` 数组快照，但数组元素是 `VstEffect` 引用。UI 线程在 `UnloadAtLocked` 里调 `fx.Dispose()`（先 `Activate(false)` 再 `Unload` 释放 native `inst`），音频线程可能正持有同一 `fx` 在 `Process` 里调 `vst_process` → use-after-free。`volatile` 只保证引用可见性，不保证生命周期。
- 后果：用户切换/重载插件时迟早触发崩溃。
- 建议：RCU 风格——`UnloadAt` 先把 `_effects[i]` 置 null 并发布新数组，旧 `VstEffect` 进入"待回收"队列，由 `PlaybackManager` 在 render cycle 结束回调里统一 Dispose（grace period）。

#### B2 — 全局 `g_handler.inst` 路由错误【严重·生产阻塞】
- 位置：`runtimes/vst_bridge/src/vst_bridge.cpp:238-239`
- 现象：`vst_load` 里 `g_handler.inst = inst` 对每个新加载的插件都覆盖。注释说"VST3 guarantees only one plugin calls performEdit at a time"——错误假设。多个插件同时打开编辑器时，每个 controller 都持有 `&g_handler`，所有 `performEdit` 都路由到最后一个 `inst` 的 `paramQueue`。
- 后果：插件 A 的旋钮调节被写进插件 B 的队列（参数串台）。
- 建议：每个 `VstBridgeInstance` 持有自己的 `BridgeCompHandler`（实例字段，非全局），`setComponentHandler` 传实例自己的 handler。

#### B3 — 无锁队列实现错误【严重】
- 位置：`runtimes/vst_bridge/src/vst_bridge.cpp:61-84`
- 现象：README 描述"无锁环形队列(SPSC)"，实际是 `values[4096]` + `dirty[4096]` 两个原子数组的全扫描 drain。问题：
  1. 非 SPSC：`performEdit` 可被多插件编辑器线程并发调用（见 B2），是 MPMC，但 `dirty.exchange` 用 `acquire` 不是 `acq_rel`，多生产者之间无同步。
  2. O(4096) 每 block 扫描：每个 audio block（~23ms@44.1k）全量扫描 4096 个原子变量，缓存行抖动严重，实时线程不可接受。
  3. 内存序不对称：push 用两次 release（values.store + dirty.store）但无顺序约束；读侧可能读到旧 values 配新 dirty。
- 建议：改为真正的 SPSC 环形队列（单生产者单消费者，head/tail 原子索引），或限制为"每实例一个队列 + 单一 GUI 线程"并修正内存序。

#### B4 — 插件扫描在进程内加载 DLL，崩溃防护不足【严重】
- 位置：`OpenUtau.Core/Vst/Vst2Probe.cs` + `VstBridge.Probe` → `vst_probe`（`vst_bridge.cpp:576-603`）
- 现象：`ScanVst3SingleFile` 调 `VstBridge.Probe` → `vst_probe` → `VST3::Hosting::Module::create`，**实际加载 DLL 进进程并调用 factory**。恶意/损坏 VST3 的 DllMain 崩溃直接带崩 OpenUTAU 进程。`ScanDirectory` 的 try/catch 只捕 managed 异常，native 访问违例是 SEH 异常，.NET 5+ 默认不捕 SEH。
- 建议：把 probe 放进独立子进程（`vst_probe.exe`），通过 stdout 读 JSON，崩溃只丢一个插件（业界标准做法，REAPER/Ableton 均如此）。

#### B5 — P/Invoke 字符集未声明（中文路径必崩）【中】
- 位置：`OpenUtau.Core/Vst/VstBridge.cs` 全文件
- 现象：所有 `string` 参数 P/Invoke 默认 ANSI 编组。`vst_load`/`vst_probe` 接收的路径在中文 Windows 上可能含非 ASCII（CLAUDE.md 已警告）。native 侧 `std::string(bundlePath)` 按字节接收，中文被截断/乱码，`Module::create` 失败。
- 建议：改用 `MarshalAs(UnmanagedType.LPWStr)` + native 侧 `wchar_t*`/UTF16→UTF8 转换；或 C# 端先转 UTF-8 按 byte[] 传递。

#### B6 — `vst_process` 无 SEH 防护【中】
- 位置：`runtimes/vst_bridge/src/vst_bridge.cpp:312-353`
- 现象：`inst->processor->process(pd)` 若插件内部崩溃，直接带崩音频线程。
- 建议：Windows 上包 `__try/__except`，崩溃时返回静默并标记插件为 faulty 从链上摘除。

### C. 架构债（影响可维护性与可测试性）

#### C1 — Solo/Mute/VST 插槽增删全部不可撤销【严重】
- 位置：`MixerTrackStrip.axaml.cs:199-204,190-204`、`TrackEffectRack.axaml.cs:250,376`、`VstRackWindow.axaml.cs:47,71-76`
- 现象：
  - `OnSoloClick`：`track.Solo = !track.Solo` 后只 `MessageBus.Current.SendMessage(new TracksSoloEvent(...))`，**不经过 `DocManager.ExecuteCmd`**，ICmdSubscriber 收不到，无撤销。
  - `OnMuteClick`：直接改 `track.Mute/Muted`，仅发 `VolumeChangeNotification`（Silent 通知）和 `TracksMuteEvent`，无 UndoableCommand。
  - VST 插槽 `track.VstSlots.Add(...)`、`slot.Clear()`、`UnloadEffect(...)`：完全无 UCommand，连 Notification 都没有。
- 建议：为 `TrackSoloCommand`/`TrackMuteCommand`/`AddVstSlotCommand`/`RemoveVstSlotCommand`/`LoadVstPluginCommand` 实现 `UCommand` 子类（参考 `AddTrackCommand`），用 `StartUndoGroup/ExecuteCmd/EndUndoGroup` 包裹。

#### C2 — VST 三个窗口无 ViewModel，View 直接操作 Core【中】
- 位置：`VstEditorWindow.axaml.cs:13,26-75`、`VstRackWindow.axaml.cs:20-77,79-132`、`TrackEffectRack.axaml.cs:46-253,376-494`
- 现象：
  - `VstEditorWindow` 直接持有 `private readonly VstEffect _fx;` 并调 `_fx.OpenNativeEditor()`——View 持有 Core 域对象，UI 线程对象与音频线程对象共享无同步封装。
  - 两个 Rack Window 的 code-behind 直接调 `VstPluginManager.Inst.ScanPlugins()`/`LoadEffect`/`UnloadEffect`/`GetEffect`、`VstPluginRegistry.Inst.ScanAll()`、`slot.PluginUid = ...`、`track.VstSlots.Add(...)`。所有 VST 插槽编排发生在 View。
- 建议：抽 `VstRackViewModel`，slot 增删与 plugin load/unload 封装为命令；View 仅绑定 `VstSlots` 集合。`VstEditorViewModel` 封装 `OpenNativeEditor` 与展示字段。

#### C3 — MixerTrackStrip 直接改 UTrack、直接调 DocManager/MessageBus【严重】
- 位置：`OpenUtau/Controls/MixerTrackStrip.axaml.cs:21-237`
- 现象：Control 直接持有 `UTrack? track`，事件处理中直接写 `track.Volume = db`(L131)、`track.Pan = ...`(L59/L74)、`track.Mute = !track.Mute; track.Muted = track.Mute`(L192)、`track.Solo = !track.Solo`(L201)。`ApplyVolume`/`OnMuteClick`/`PanSlider.PropertyChanged` 中直接 `DocManager.Inst.ExecuteCmd(new VolumeChangeNotification(...))` 和 `MessageBus.Current.SendMessage(...)`。`OnFxEntryClick` 在 Control 中 `new Views.TrackEffectRack(track); rack.Show()`。
- 后果：View → Core Model 越层直连，违反 MVVM；`MixerViewModel` 形同虚设，只管集合刷新。
- 建议：引入 `MixerTrackStripViewModel`（或复用 `TrackHeaderViewModel`），所有 `track.*` 写入移到 ViewModel，View 仅通过绑定/命令触发。

#### C4 — 窗口体系二分未统一【高】
- 位置：`OpenUtau/Styles/Styles.axaml:29-35` + 全局窗口
- 现象：12 个窗口用 `WindowEx`+`WindowTitleBar`（MainWindow/MessageBox/MixFxDialog/MixerWindow/PreferencesDialog/RenderWindow/VstEditorWindow/VstRackWindow/TrackEffectRack/DebugWindow/PianoRollDetachedWindow/ThemeEditorWindow），但 **20 个对话框仍是 `: Window`**（LyricsDialog、TypeInDialog、SingerSetupDialog、SingersDialog、TranscribeDialog、TrackSettingsDialog、ExpressionsDialog、MergeVoicebankDialog、NoteDefaultsDialog、PasteParamDialog、PhoneticAssistant、SliderDialog、TimeSignatureDialog、TrackColorDialog、VoiceColorMappingDialog、UpdaterDialog、ExeSetupDialog、EditSubbanksDialog、DsScriptExportDialog、LoadingWindow、SplashWindow）。全局 `Window` 样式设了 `ExtendClientAreaToDecorationsHint=True` + `TransparencyLevelHint=AcrylicBlur`，所以这 20 个 plain Window 也参与扩展客户区但**没有 WindowTitleBar**——标题栏外观/关闭按钮/拖拽行为不一致。
- 建议：要么把剩余 20 个对话框迁移到 `WindowEx`+`WindowTitleBar`，要么全局 `Window` 样式不设 `ExtendClientAreaToDecorationsHint`（让 plain Window 保持原生），仅 `WindowEx` 走自绘路径。当前"全局扩展 + 部分窗口自绘"的混搭是反复 fix 的温床。

#### C5 — `WindowDecorationMargin` 绑定重复 22 处【中】
- 位置：`MessageBox.axaml:14`、`MixFxDialog.axaml:40`、`MixerWindow.axaml:17`、`MainWindow.axaml:22`、`PreferencesDialog.axaml:46` 等 22 个文件
- 现象：每个窗口根容器都要手动绑一遍 `Margin="{Binding $parent.WindowDecorationMargin}"` 避开自绘边框区域。漏抄就内容贴边。
- 建议：在 `WindowEx` 里包一层 `ContentPresenter` 并在基类统一设这个 Margin，子类只填内容；或做 `WindowExContent` 控件。

#### C6 — 混音台拖拽双实现并存，有死代码【高】
- 位置：`OpenUtau/Views/MainWindow.axaml:611-617 vs 626-634` + `MainWindow.axaml.cs:745-790`
- 现象：钢琴卷帘用 `GridSplitter`（配合 `OnSplitterDragStarted/Completed` 显示 tooltip），混音台用**手动 `Border` + PointerPressed/Moved/Released**。git 历史显示混音台曾 4 次尝试 GridSplitter（`6eccd84d`/`4c5ed7cc`/`39c18a8f`/`766546fb`）后放弃改手动拖拽（`0c2db732`）。`OnSplitterDragStarted/Completed` + `ResizeTooltip` 是为钢琴卷帘写的，混音台手动拖拽没有 tooltip。
- 建议：抽 `ResizerBar` 控件（封装 Border+Pointer 三事件+可选 tooltip），两者都用，替掉 GridSplitter + 手写 Pointer 两套。魔数 150 / 范围 120..600 提到常量。

#### C7 — `MixFxSource` 整类死代码，与 `EffectChain` 重复【高】
- 位置：`OpenUtau.Core/SignalChain/MixFxSource.cs` vs `EffectChain.cs`
- 现象：`MixFxSource` 是旧硬编码 EQ→Comp→Reverb 包装器，`EffectChain` 是注释自称的"generalised replacement"。`RenderEngine.RenderMixdown` 只调 `EffectChain.Build`，`MixFxSource.WrapWith` 无任何调用方。两份构建逻辑完全重复。
- 建议：删除 `MixFxSource`。

#### C8 — `MixerTrackStrip` 重复订阅与订阅泄漏【中】
- 位置：`MixerTrackStrip.axaml.cs:72-79` + `42-62`
- 现象：`LoadTrackData` 每次都重新订阅 `PanSlider.PropertyChanged`，`Refresh()` 会再次调用 → N 次刷新后一次滑块拖动触发 N 次 `track.Pan =` 和 N 次 `MessageBus.SendMessage`。另外每条 strip 在构造时 `MessageBus.Listen<VolumeChangeNotification>` 但**从不释放订阅**，strip 在 `RebuildStrips` 里被 `Children.Clear()` 丢弃，订阅 IDisposable 未保存未 dispose。
- 建议：订阅移到构造函数（只做一次），`LoadTrackData` 仅刷新显示值；`MixerTrackStrip` 实现 `IDisposable` 或在 `Unloaded` 释放订阅。

#### C9 — `EffectChain` 从不调 `fx.Reset()`，seek 后残响泄漏【中】
- 位置：`OpenUtau.Core/SignalChain/EffectChain.cs:29-44` + `MasterAdapter.SetPosition`
- 现象：`IEffect` 定义了 `Reset()`（"播放位置跳转时调用"），但 `EffectChain.Mix` 从不调用，`RenderEngine` 在 seek/重新播放时也不调用。Freeverb 延迟线、压缩器包络跟随器、VST 内部状态在 seek 后保留旧尾巴，产生前回声/残响泄漏。
- 建议：在 `MasterAdapter.SetPosition` 或 `PlaybackManager.Seek` 路径上对链上所有 `IEffect` 调 `Reset()`。

#### C10 — `Fader` 声像按 `i % 2` 判定，依赖 buffer 起始偶数对齐【中】
- 位置：`OpenUtau.Core/SignalChain/Fader.cs:48`
- 现象：`buffer[index + i] * scale * (i % 2 == 0 ? volumeLeft : volumeRight)` 假定 `index` 对应左声道。`ExportAdapter`/`RecordingAdapter` 链路里 offset 未必保证偶数对齐。一旦 offset 为奇数，左右声道反相分配。
- 建议：按 `(i + index) % 2` 或显式按帧循环 `for (frame) { left=2*frame; right=2*frame+1; }`。

### D. 工程基础设施

#### D1 — plus-develop 分支 PR 不跑 CI【严重】
- 位置：`.github/workflows/pr-test.yml`
- 现象：`pr-test.yml` 只对 `master` 分支 PR 触发，**plus-develop 分支 PR 不跑测试**。Plus 改动无 CI 门禁。
- 建议：为 plus-develop 加 PR-Test 流水线。

#### D2 — win-x86/arm64 缺 `vst_bridge.dll`【严重】
- 位置：`OpenUtau/OpenUtau.csproj:79-99` + `runtimes/vst_bridge/build.bat`
- 现象：`runtimes/win-x64/native/vst_bridge.dll` 已入库，但 win-x86/win-arm64 目录没有。csproj 的 `RuntimeIdentifier == win-x86/win-arm64` ItemGroup 会复制各自 native 目录，缺 vst_bridge.dll → 这两个架构发布包 VST 功能启动即 `DllNotFoundException`。`build.bat` 硬编码 `-A x64`。
- 建议：补 x86/arm64 构建，或按架构禁用 VST 并提示。

#### D3 — `vst3sdk` 未做 submodule，源码不可复现构建【中】
- 位置：`.gitignore:343` + `runtimes/vst_bridge/CMakeLists.txt:18-25`
- 现象：仓库无 `.gitmodules`，`CMakeLists.txt` 依赖 `../vst3sdk`，fresh clone 后 `build.bat` 必失败。`vst_bridge.dll` 实际上无法从源码复现构建，只能用入库二进制。
- 建议：把 vst3sdk 改为 git submodule，或在 build.bat 里自动 clone。

#### D4 — VstTest 游离于 sln/CI 之外【中】
- 位置：`VstTest/VstTest.csproj` + `VstTest/Program.cs`
- 现象：`OutputType=Exe`，不在 `OpenUtau.sln`（sln 仅 4 个项目），CI 不构建不运行。`Program.cs` 是顶层语句，硬编码查找名为 `"OTT"` 的 VST 插件，依赖本机已安装 OTT + native dll。不是 xUnit 测试，不能被 `dotnet test` 收集。
- 建议：要么升级成 xUnit + mock VstBridge（不依赖真实插件），要么明确标注为 dev-only 手动工具。当前状态易被误判为"VST 有测试覆盖"。

#### D5 — Plus 新增模块近零测试，UI 零测试【严重】
- 位置：`OpenUtau.Test/`
- 现象：
  - Classic/音素化器/Util/Format 覆盖充分（上游遗产）。
  - `UMixFx.cs`、`MixerViewModel.cs`、`VstEffect.cs` 等均无测试。`UMixFx` 是纯数据模型极易测却没测。
  - UI 层零测试，尽管 `OpenUtau.Test.csproj:14` 引用了 `Avalonia.Headless.XUnit`（基础设施已就位却未用）。
  - `SignalChain` 仅 `WaveSourceTest`。
- 建议：优先补 UMixFx 模型单测、MixerViewModel 订阅/RebuildStrips 测试、Avalonia.Headless 锁住 MessageBox 自绘标题栏与 ResizerBar 行为。

---

## 二、各模块详细审计

### 2.1 VST 宿主模块健康度

| 维度 | 评级 | 说明 |
|------|------|------|
| P/Invoke 安全性 | ⚠️ 中下 | 字符集未声明（中文路径必崩）、句柄所有权模糊、SaveState 协议脆弱 |
| 生命周期/并发 | 🔴 危险 | UI Dispose 与 audio Process 存在 use-after-free 竞态；activate 时机过早；全局 handler 路由错乱 |
| 无锁队列 | 🔴 错误实现 | 非环形队列、非 SPSC、内存序不对称、O(4096)/block 扫描 |
| 跨平台 | 🔴 仅 Windows | VstBridge 无 OS 守卫，Mac/Linux 静默 DllNotFound；默认扫描路径全 Windows |
| 崩溃防护 | ⚠️ 不足 | probe 在进程内加载 DLL；process 无 SEH 防护；扫描崩溃会带崩主进程 |
| 乐器过滤 | ⚠️ 可用但保守 | VST3 黑名单 OK，VST2 完全不检测 synth 标志 |
| C++ 质量 | ⚠️ 中等 | 裸指针管理、reset 双调用、手写 JSON；引用计数正确但脆弱 |
| 架构扩展性 | ✅ 良好 | IEffect 抽象、UID 解耦、slot/entry 分离，音源扩展点就位 |

**VST 补充发现**：
- `VstTrackInstances.cs:34 + VstEffect.cs:45-56 + RenderEngine.cs:113` — Setup 硬编码 44100/4096，且 `Setup` 内立即 `Activate(true)`，项目加载时所有插件处于 activated 状态占用音频资源。建议 activate 推迟到首次 Process 前。
- `VstPluginManager.cs:92-93` — `LoadPlugin`/`UnloadPlugin` 是空壳。`VstRackWindow.axaml.cs:125` 调用 `LoadPlugin(slot)` 后以为已加载，实际啥也没干（真正加载在 `TrackEffectRack` 用 `LoadEffect`）。两条路径不一致，VstRackWindow 插槽加载后不会创建实例，渲染时没声音。死代码/半成品。
- `vst_bridge.cpp:128-132` — `vst_get_param_name` 返回 thread_local 静态缓冲区，跨调用立即失效。当前用法安全但脆弱。
- `vst_bridge.cpp:142-156 / 443-466` — `vst_open_editor` 返回的 `IPlugView*` 被 C# 当 IntPtr 接收后丢弃，native 侧存进 `inst->editorView`，两份所有权（隐式契约无文档约束）。
- `VstBridge.cs:185-203 / vst_bridge.cpp:402-421` — SaveState 64KB 试探 + 重试协议脆弱，大型插件状态（Serum/Phase Plant 数 MB）必走两次 native 调用，语义混乱。
- `VstPluginRegistry.cs:243-259` — `ClassifyEffect` VST3 黑名单 OK 但无白名单；VST2 完全不读 `AEffect` 的 `effFlagsIsSynth`，旧版 Synth1 会被当效果加载。
- `vst_bridge.cpp:355-366` — `vst_reset` 调 `setupProcessing` 两次，第一次零初始化有副作用。
- `vst_bridge.cpp:536/556-558` — EditorWinState 引用计数正确但极易出错，建议显式注释引用计数流。
- `CMakeLists.txt:67-69` — 编入 `systemclipboard_win32.cpp`/`threadchecker_win32.cpp` 等未使用文件，增加体积。

**VST 音源支持（近期目标）扩展点评估**：当前过滤逻辑硬编码在 `ClassifyEffect`，`VstEffect.Load` 第 32-34 行显式拒绝 `!IsEffect`。要做音源支持需：新增 `VstInstrument : ISignalSource`（无输入 bus、接收 MIDI event list）、`VstBridge` 新增 `vst_process_midi`、Registry 保留 instrument 记录。扩展点基本留好（`VstPluginEntry.IsEffect` 是 bool），但 `VstEffect.Load` 的硬阻断未来需重构。当前不构成缺陷。

### 2.2 Mixer 与 SignalChain 健康度

| 维度 | 评级 | 说明 |
|------|------|------|
| 数据模型清晰度 | 中 | `UMixFx` 字段平铺、与序列化边界含糊；静音/独奏/音量绕过命令模式 |
| 信号链接口 | 中上 | `ISignalSource`/`IEffect` 接口简洁，但缺 `Reset` 调用链、缺延迟接口、Fader 声像索引脆弱 |
| 渲染集成 | 中下 | 播放路径基本可用；**导出含 VST 的实时录制路径有正确性 bug**；无 PDC |
| ViewModel/UI | 中 | 30fps timer 可用但隐藏态不暂停；master 电平未接；strip 重复订阅与订阅泄漏 |
| 双模式 | 良好 | 重 parent 共享 ViewModel 设计干净；唯一隐患是 strip 生命周期管理 |
| 死代码 | 中 | `MixFxSource` 整类重复且无调用方；`MixerViewModel.MasterVolume`；`UMixFx.Clone` |

**Mixer 补充发现**：
- `UMixFx.cs:8-47 / UTrack.cs:98` — `UMixFx` 无 `.ustxp` 专属标记，旧 .ustx 文件也会写入这些字段，破坏"向后兼容：旧文件 MixFx 为 null"承诺。建议 `null` 与 `Enabled=false` 维持语义等价，或 `AfterLoad` 归一化。`Clone()` 无调用方，死代码。
- `UMixFx.cs:13-15` — 预设名注释说"滑块值才是 DSP 真相"，但 `EffectChain.Build` 里 Comp/Reverb 的 attack/release/width/wet 仍从 `FxPresets.Comp[CompPreset]` 读取，注释与实现不一致。
- `EffectChain.cs:15 / MixFxSource.cs:17 / MasterAdapter.cs:14 / ExportAdapter.cs:13` — 采样率 44100 全面硬编码。
- `MixerControl.axaml.cs:42-47` — 30fps DispatcherTimer 在窗口隐藏后仍运行（`MixerWindow.Closing` 隐藏而非关闭），浪费 CPU 且清掉本应累积的峰值。建议 `IsVisible` 变化时暂停/恢复。
- `MixerViewModel.cs:15` — `MasterVolume` 是 Reactive 属性但全代码无写入方无绑定方，死属性。master 推子/电平表是缺失功能。
- `LevelTracker.cs:13-26 + RenderEngine.cs:99-101` — 电平表 pre-fader，UI 再加 dB 近似不准；Solo 状态下被静音的其他轨道仍会渲染并显示峰值，给用户错觉。
- `MainWindow.axaml.cs:690-736` — 内嵌/分离双模式重 parent 共享 ViewModel 设计正确，无竞态。

### 2.3 架构分层健康度

- **View 层越权最严重的是 MixerTrackStrip 与两个 VST Window**。它们把本应由 ViewModel 承担的职责（模型字段写入、命令调度、窗口编排、Core 服务调用）全部吸收到 code-behind。`MixerViewModel` 形同虚设；VST 三个 View 完全没有对应 ViewModel。
- **命令模式在 Plus 新功能中基本缺失**。Solo/Mute/VST 插槽增删全部直接改模型 + MessageBus 广播，不进 DocManager、不可撤销。Volume/Pan 沿用了 upstream 的"Notification 当命令"既有缺陷（`Notifications.cs:153-173`，Plus 未恶化也未修复）。VST 插槽变更连 Notification 都没有。
- **Core 边界封装不彻底**。P/Invoke 边界（VstBridge）封装干净值得肯定；但 `VstPluginManager`/`VstPluginRegistry`/`VstEffect`/`TrackLevels`/`FxPresets` 这些 Core 实现细节被 View 直接消费。
- **合并友好度尚可**：Mixer/VST/TrackEffectRack 等均为纯新增文件，零冲突风险。冲突集中在已修改的 upstream Core 文件（`RenderEngine`/`DocManager`/`UTrack`/`UProject`/`TrackHeaderViewModel` 等）。`Notifications.cs` 零 diff、`UTrack.cs` 附加式改动是良好示范；`TrackHeaderViewModel` 的 Pan×100 改动是主要纠缠点，建议把 Pan 的 0..100 ↔ -1..1 转换集中到 helper。

### 2.4 UI 层与测试健康度

**UI 层 6/10**：
- 颜色管理（`Colors/{Light,Dark}Theme.axaml` + `Brushes.axaml`）集中资源，Views 硬编码 `#xxxxxx` 零命中，是正面案例。
- 模糊逻辑统一收口在 `WindowEx.ApplyBlurSettings()`，由 `Preferences.EnableBlur`/`BlurMode` 驱动，正面案例。但 `Styles.axaml:34` 全局 `TransparencyLevelHint=AcrylicBlur` 与 WindowEx 职责重叠，plain Window 永远 AcrylicBlur 无法响应偏好。
- `WindowEx(bool enableCustomChrome)` 构造参数是 `31c2daf3` hack 遗留，暴露实现细节。
- `MixFxDialog.axaml:12-37` 内联 5 个样式（`.lbl`/`.section`/`.val`/`Slider`/`.column`），应抽全局。
- `WindowTitleBar.axaml:31-65` 三按钮靠 `Classes="titleBarBtn"`/`closeBtn`，需确认全局唯一定义。
- 最近 25 个 fix 提交约 18 个集中在自绘边框/模糊/混音台拖拽三块——正是 C4/C5/C6 三个缺口造成的。

**测试覆盖 4/10**：
- Classic/音素化器/Util/Format 覆盖充分（上游遗产）。
- Plus 新增 Mixer/UMixFx/VST/SignalChain 扩展近零覆盖，UI 零测试（Avalonia.Headless 已引入未用）。
- `VstTest/` 是伪测试（独立 Exe，不在 sln，依赖 native+真实插件，不能 CI）。

### 2.5 格式兼容与构建 CI 健康度

- .ustxp 加载分支设计合理（统一入口 + `ustxpVersion` 区分 + `IgnoreUnmatchedProperties` 前向兼容），但 A3/A4 削弱可靠性。
- VST 参数持久化存在真实数据丢失风险（A1）。
- native 桥接 dll 仅 win-x64 入库 + 仅 win-x64 构建（D2），CI 无校验。
- vst3sdk 未做 submodule，源码不可复现构建（D3）。
- plus-develop 分支无 PR CI（D1）；vst_bridge 无重建 workflow（对比 Worldline 的成熟模式未复用）。
- VstTest 游离于 sln 与 CI 之外（D4）。

---

## 三、远期愿景的架构缺口（README 路线图）

当前架构支撑现有功能 OK，但 README 远期目标（Aux/侧链/PDC/VCA/自动化）几乎无预留扩展点：

1. **无总线层**：N 轨道 → 1 个 WaveMix 扁平结构。要支持 Aux 发送需引入 `Bus`/`Send` 概念：`ISignalSource` 之上增加 `IAuxSink`，轨道链尾分出 post-fader send 到 Aux 总线再汇入 Master。`EffectChain.extraEffects` 是插入式，不能表达发送式路由。
2. **无 PDC 接口**：`IEffect` 缺 `Latency` 属性，`EffectChain`/`Fader`/`WaveMix` 都不报告延迟。实现 PDC 需每个 `IEffect` 报告 `int LatencySamples`，`RenderEngine` 在每轨道插入 `DelayLine` 对齐到最大延迟。`WaveSource.offset` 已有偏移机制可复用。
3. **无侧链**：`IEffect.Process(buffer, offset, count)` 只接收自己的信号，无侧链输入端口。需 `Process(float[] main, float[] sidechain, ...)` 重载或 `SetSidechainSource(ISignalSource)`。
4. **无 VCA**：`Fader.Scale` 是单轨标量，无 VCA 分组。需 `VcaGroup`，Fader 读取"自身 scale × 所属 VCA 组 scale"。
5. **无自动化**：`Fader.Scale`/`Pan` 是即时值，无时间轴自动化包络。`UTrack` 里无自动化轨道数据模型。
6. **路由不可变**：`RenderEngine.RenderMixdown` 硬编码路由（track→fader→fx→master mix）。要支持用户自定义路由树需把拓扑变成数据驱动的图结构。
7. **采样率硬编码**：44100 散落多处，需集中到 `AudioSettings`。

**建议**：在做远期愿景前先把工程基础（并发安全、测试、分层）补齐，否则在新地基上堆功能会放大风险。

---

## 四、重构优先级建议（分阶段）

按"先止血 → 再夯基础 → 后扩能力"三阶段推进，每阶段可独立交付、可构建可运行。

### 阶段 0：止血修复（高价值低风险）
修掉所有"静默错"的数据正确性 bug：
- A1 BeforeSave 调 SaveAllStates
- A2 导出路径重写（走 ExportAdapter + 等待就绪 + 取消 token）
- A3/A4 autosave 改用 Ustxp.AutoSave + 修版本闸口 + RecoveryProject 路径替换
- A5 Base64 state 容错
- A6 VST2 UID 改 xxHash
- B5 P/Invoke 字符集（中文路径）
- C7 删 MixFxSource 死代码
- C8 MixerTrackStrip 重复订阅与订阅泄漏
- C9 EffectChain.Reset 调用链
- B4 进程外 probe（崩溃隔离）

### 阶段 1：并发安全 + 跨平台降级（VST 模块稳定化）
- B1 RCU 风格 grace period 回收 VstEffect
- B2 per-instance handler
- B3 重写真正的 SPSC 环形队列
- VstBridge 加 `OS.IsWindows()` 守卫 + Registry 按平台选扫描路径
- B6 vst_process SEH 防护
- 修 VstPluginManager LoadPlugin/UnloadPlugin 空壳
- Setup/Activate 时机推迟

### 阶段 2：分层重构 + 测试兜底
- C1 为 Solo/Mute/VST 插槽补 UCommand（可撤销）
- C2 VST 三窗口抽 ViewModel
- C3 MixerTrackStrip VM 化（复用 TrackHeaderViewModel）
- C4/C5/C6 窗口体系统一 + WindowDecorationMargin 收口 + ResizerBar 控件
- D1 plus-develop 加 PR-Test
- D5 补 UMixFx/MixerViewModel 单测 + Avalonia.Headless 锁 UI 行为
- D2/D3 native 构建补齐 + vst3sdk submodule
- D4 VstTest 定位明确

### 阶段 3：远期愿景地基（可选，按 README 路线图）
- IEffect 加 Latency、引入 Bus/Send/Sidechain 接口、路由数据驱动化、采样率集中
- 较大，建议单独立项再审批

---

## 附录：审计 agent 输出索引

本报告由 5 个并行审计 agent 汇总：
1. 架构分层与依赖边界
2. VST3 宿主模块
3. Mixer 与 SignalChain
4. UI 层 churn 与测试覆盖
5. 格式兼容与构建 CI
