# 实时效果机架 —— 独立验证报告（fx-verify）

> 验证者：`fx-verify`（独立于四名实现者）
> 阶段一：静态对抗审查（只读，**未跑 dotnet**） 2026-10
> 集成树：`G:\xklmy文件夹\vibe coding\UTvTU-fx-core`，HEAD `21255585`
> （= 冻结 `e8a93cdc` + fx-core 5 提交 + fx-ctl merge；工作树 clean，fx-ui/fx-rack **未**并入）
> 规划：`.opencode/plans/effects-rack-adoption.md`
> 本文件由 `fx-verify` 独占；阶段二章节在 Lead 通知合并完成后再补。

## 0. 方法、范围与免责

- 全部结论均来自**我自己**读源码/读 diff/读测试得到的证据（附 `文件:行号`），未采信任何实现者口头结论；
  实现者的自述仅用于**定位**要审的代码。
- 本轮改动文件集（`git log --stat e8a93cdc~1..21255585` 实证）：
  - fx-core：`Core/SignalChain/MixFxSource.cs`（新增 270 行 + 两次 perf 提交）、`Core/Ustx/UMixFx.cs`、
    `Effects/{BiquadEQ,Freeverb,SimpleCompressor}.cs`、`Render/RenderEngine.cs`、`SignalChain/EffectChain.cs`、
    `Export/ExportSession.cs` + 测试 2 个；
  - fx-ctl：`App.axaml`、`Styles/Md3SelectionThemes.axaml`（新增 548 行）、`Styles/Styles.axaml`（8 行：BOM + fader 收窄）
    + 测试 2 个（`Md3SelectionThemeTests.cs` 460 行 / 14 例，`Md3ControlThemeTests.cs` 13 行）。
  - **明确非本轮**：`ViewModels/TrackHeaderViewModel.cs`、`Views/MixFxDialog.axaml(.cs)`、`ViewModels/MixFxViewModel.cs`、
    `Views/TrackEffectRack.*`、`Controls/TrackHeader.axaml` 等均为基线文件（fx-ui 尚未合并）。报告中凡涉及这些文件的
    结论一律标注【既有】。
- **免责**：阶段一不跑 `dotnet`（Lead 指定），所以"测试通过"类结论只作为**存在性/内容性证据**引用，
  我自己的构建与测试复跑在阶段二；凡我未能静态确定的，一律标 `未验证`。

## 1. 结论总表（task-7 的 12 条）

| # | 审查项 | 结论 | 一句话 |
|---|---|---|---|
| A1 | 音频线程读 `UTrack.MixFx` 的撕裂读/整体替换 | **OK**（+低风险） | 每块只取一次局部引用；整对象替换原子；就地改字段可产生"混合参数单块"，最坏 1 块 |
| A2 | 零分配声明真实性 / EnsureCapacity 几何 | **OK**（+备注） | 稳态无分配、几何扩容正确；`CreateWaveFile16` 的块大小注释不准确（导出首块可能一次性大扩容，非音频线程） |
| A3 | 交叉淡化边界（奇数样本/跨块/FadeFrames） | **OK** | 逐帧推进、尾样按当前 g；DSP 自身也只在整帧内处理 → 尾样恒等式，无残留；count 实际恒为帧整数倍 |
| A4 | 首次 Sync 前 `applied` 误判 / WrapWith Clone 时点 | **OK**（+低风险） | 构造器必 Sync → `configured` 恒先置真；Clone 在建链时点；Clone 非原子（低风险） |
| A5 | 采样率可变性 / 链是否重建 | **OK**（+前瞻风险） | `Configure` 全仓仅 `Program.cs:27` 一处、`private set` → 运行期恒定；`RenderWindow` 的采样率选择是**死 UI** |
| A6 | 旁通残留 / seek 清残留（含 Freeverb 尾音） | **OK**（依赖附A L1） | 模块/主开关淡出后 Reset、位置不连续自愈、Freeverb 全状态覆盖；`WaveMix` 不转发 `Reset`（L1 既有） |
| A7 | 旧 `EqBypassed` 迁移 / 双键往返幂等 | **OK**（+2 低风险） | 反向别名 + 测试证明；双键并存幂等；两键取值冲突时静默取后者（无校验） |
| A8 | 播放中改 MixFx 不触发重渲染/重启 | **Core OK；UI 契约缺陷** | Core 无触发点；但 `MixFxViewModel.Apply()` 与轨道头 fx 按钮**漏 `MarkProjectModified()`** → 机架设置可能静默不落盘【既有，建议本轮修】 |
| B9 | 全仓 `/template/` 补丁 × 新模板部件名核对 | **OK** | 14 处逐条核对：**无一处因改名静默失效**；1 处 `CheckOuterEllipse` 属另一模板作用域（易误判） |
| B10 | 应用级 setter 压住新主题 | **风险（低）** | `.fader`/`.menu` 收窄正确；仍有 3 处等价双写/死写（主题被同值应用级遮蔽）→ 登记清理；`CheckBox{MinWidth=28}` 需目视 |
| B11 | `:focus-visible`/`:disabled`/不定态 vs 契约断言 | **风险（低）** | CheckBox/RadioButton 焦点环有实装+运行时断言；**ToggleSwitch/Slider/ProgressBar 无焦点视觉部件**；1 条契约断言是文本级弱断言 |
| B12 | Md3Controls 失效 Fluent 键覆盖 → 视觉双写 | **OK（登记清理）** | 残留的 `RadioButtonOuterEllipse*` 键**仍被 PianoRollStyles 引用，不可删**；双写清单见 B10；旧契约测试未被削弱（是扩充） |

**缺陷 × 1（D1，UI 写入契约，既有但被本轮放大）**、**风险 × 7（R1–R7，均为低）**，其余 OK。
未发现会破坏构建、破坏既有 `/template/` 补丁、或使机架功能不可用的静态缺陷。

## 2. A 组：Core 音频路径（fx-core）

### A1 音频线程读 `UTrack.MixFx`：撕裂读 / 半组参数 / 引用整体替换 —— OK（+低风险）

证据：
- 每块**只取一次**引用到局部，块内所有读值都走这个局部：
  `OpenUtau.Core/SignalChain/MixFxSource.cs:121-125`
  ```csharp
  var fx = getFx();                    // 每块一次
  if (fx != null) { Sync(fx); }
  float masterTarget = fx != null && fx.Enabled ? 1f : 0f;
  ```
  闭包本身在构造期建立、无捕获分配：`MixFxSource.cs:299`（`() => snapshot`）、`:309`（`() => track.MixFx`）。
- 引用整体替换路径（UI 侧实证）：`OpenUtau/ViewModels/TrackHeaderViewModel.cs:190`
  `track.MixFx = new UMixFx { Enabled = true };`、`OpenUtau/ViewModels/MixFxViewModel.cs:292` `track.MixFx = BuildUMixFx();`
  → 引用写原子（x64/arm64 对齐 8 字节），块内只读一次局部 ⇒ **不会读到半个对象、不会块中途换对象**。
- 就地改字段路径：`TrackHeaderViewModel.cs:188`（`track.MixFx.Enabled = enabled`）、
  `OpenUtau/Views/TrackEffectRack.axaml.cs:543-549`（预设一次性写 Low/Mid/High 4 个字段）。
  `Sync` 对同一对象**读两遍**（`SameParams` → `CopyParams`）：`MixFxSource.cs:232-236`、`:255-281`
  ⇒ 两次读之间被 UI 改动时，`applied` 里会是"两代参数混合"，`SameParams` 也可能比较掉一代。
  影响面 = **1 个音频块**（512 帧 ≈ 11.6ms）的参数混搭或配置晚一块生效，不崩、不爆音（参数都是合法值）。
  → **风险（低）**，非本轮引入（就地写是既有 UI 习惯）。fx-ui 采用"整对象替换"是正确姿势，建议作为约定固化。
- `OpenUtau.Core/Ustx/UTrack.cs:98` `public UMixFx MixFx { get; set; }` 无 `volatile`/无锁 → 严格意义数据竞争；
  与附A L2（可空标注）同源。x64 上不会撕裂；不建议在无锁前提下改语义。

### A2 零分配声明 / EnsureCapacity 扩容几何 —— OK（+1 处注释不准确）

- 稳态处理路径 `MixFxSource.cs:111-164` 逐句过：无 `new`、无装箱、无闭包、无 LINQ。
  `Sync` 的 `FxPresets.Comp/Reverb.TryGetValue(..., out var struct)`（`:240`、`:246`）返回 struct，不装箱；
  `Math.Clamp/Array.Clear/Array.Copy` 无分配。
- `EnsureCapacity`（`:167-178`）：`while (capacity < count) capacity <<= 1;` → 2 的幂几何扩容，
  一次同时替换 `scratch/masterDry/stageDry` 三个数组 ⇒ 整个会话 O(log n) 次分配，稳态零分配。
- 预分配（`:87-91`）：`max(4096, max(BlockSize=4096, 44100/10=4410)) * Channels(2) = 8820`，
  **恰好等于** 44.1k 立体声 100ms = 8820 样本；测试 `OpenUtau.Test/Core/SignalChain/MixFxSourceTest.cs:417-437`
  （`Assert.True(block > 8192)` + 首块零分配）与 `:394-414`（稳态 64 块零分配，且中途持续改参数走 `Sync→Configure`）钉住。
- 播放后端实测块大小来自设备周期：`OpenUtau.Core/Audio/MiniAudioOutput.cs:131-138`
  `int samples = (int)(channels * frame_count); n = sampleProvider.Read(temp, 0, samples);` → 典型 ≤ 2048 < 8820 ✔。
- **备注（我未能完全证实的一处）**：注释把 100ms 说成 `CreateWaveFile16 的块`（`:44`、`:68`），
  但 `CreateWaveFile16` 的块大小由 NAudio 内部缓冲决定（`Export/ExportSession.cs:74`），
  历史实现为 `AverageBytesPerSecond * 4`（≈4s）。本沙箱网络不可达，**无法取 NAudio 2.2.1 源码核实**。
  若如此，导出首个块 ≈352800 样本 → 一次几何扩容到 564480（3×2.2MB，一次性），
  **发生在导出线程而非音频回调**，之后稳态零分配 ⇒ 不推翻"音频线程零分配"，但注释表述不准确。
  列入阶段二实测（导出一次整曲即可观察）。
- 顺带（非本类）：内层 `Fader.Mix` 按需 `new float[count]`（`SignalChain/Fader.cs:34-36`，非几何）属既有实现，
  不在本轮声明范围内。

### A3 交叉淡化边界：奇数样本 / 跨块连续性 / FadeFrames —— OK

- 循环按**帧**推进：`MixFxSource.cs:212-222`
  ```csharp
  for (; i + channels <= count; i += channels) {          // 每帧推进一次 g
      if (g < target) g = Math.Min(target, g + fadeStep); else if (g > target) g = Math.Max(target, g - fadeStep);
      for (int c = 0; c < channels; c++) { ... }
  }
  for (; i < count; i++) { ... }                           // 尾样用当前 g（不再推进）
  ```
  （上游桩描述里的 `i + 1 < count` 已被改成按帧的 `i + channels <= count`，语义更强，不是缺陷。）
- `count` 非声道整数倍时**不会漏处理湿信号**：三个 DSP 自身也只处理整数帧
  （`Effects/BiquadEQ.cs:85-96` `int frames = count / channels;`；`Effects/SimpleCompressor.cs:78`；
  `Effects/Freeverb.cs:119-123` `if (channels != 2) return; int frames = count / 2;`）
  ⇒ 尾样从未被处理，`wet == dry`，`Crossfade` 退化为恒等式。**结论：尾样残样按当前 g 处理是安全的**。
- 前提是否恒成立：`count` 实际恒为"帧数 × 声道" ——
  `MasterAdapter.Read` 直接透传后端 count（`SignalChain/MasterAdapter.cs:22-33`）、
  后端 count = `channels * frame_count`（`Audio/MiniAudioOutput.cs:132`）、
  导出 `ExportAdapter.Read` 由 NAudio 传"帧数×声道"（`SignalChain/ExportAdapter.cs:17-28`）⇒ 恒成立 ✔。
- 跨块连续：四个 gain 是实例字段（`:61-64`），不在块边界复位；单次淡化 `Round(44100*15/1000)=662` 帧
  ≈1.3 个 512 块，跨块平滑 ✔。测试用"淡化头 50 帧仍是湿声包络 / 662 帧后回到干声包络"钉住
  （`MixFxSourceTest.cs:223-241`），无爆音判据用直流信号 + 相邻样本差 <0.01（`:188-221`）。
- FadeFrames 随采样率换算：`fadeStep = 1f / Math.Max(1, (int)Math.Round(SampleRate * FadeMs / 1000.0))`
  （`:83`）→ 44.1k = 1/662；测试里的期望值同源（`:235`）✔。

### A4 首次 Sync 前 `applied` 初值 / WrapWith Clone 时点 —— OK（+低风险）

- `applied`（`:57`）默认值会不会让 `IsAnythingEnabled` 误判？
  - 唯一消费者是 `IsAnythingEnabled`（`:284-288`），它带 `configured &&` 短路；
  - 构造器在 `fx != null` 时立刻 `Sync(fx)` 并置 `configured = true`（`:93-100`）；
  - `WrapWith` 先判 `fx == null || !fx.Enabled` 才建包装器（`:295-297`）⇒ 进构造器时 `fx` 必非空
    ⇒ `configured` 在第一块之前必为真 ⇒ **不存在"因初值导致 WrapWith 误判为空转、把效果整条丢掉"的路径** ✔
    （反向也成立：`Sync` 的 `if (configured && SameParams(...))` 首次必配）。
- `WrapWith` 的 Clone 时点（`:298-300`）：在 `RenderEngine.BuildTrackOutputs` 建链时克隆一次
  （`Core/Render/RenderEngine.cs:257-266`）；`RenderMixdown` 是"先给所有轨道建链、再渲染"
  （`RenderEngine.cs:108-130`）⇒ 同一次导出的所有轨道取**同一时刻**快照，导出确定 ✔
  （测试 `ExportSnapshot_IgnoresLaterEdits` `MixFxSourceTest.cs:281-304`：导出中改参/关总开关后仍出湿声）。
- 备注（低风险，见 R3）：`UMixFx.Clone()`（`Core/Ustx/UMixFx.cs:45-55`）是 16 字段逐拷，非原子；
  若克隆瞬间 UI 正在就地改同一对象，快照可能是"半组参数"。影响 = 一次导出的参数不典型，不崩。

### A5 采样率可变性 / 已构造 DSP 是否失效 / 会话是否重建链 —— OK（+前瞻风险 R1）

- `Core/SignalChain/AudioSettings.cs:11-21`：三个属性都是 `private set`，唯一写入口 `Configure`；
  全仓唯一调用点 `OpenUtau/Program.cs:27` `AudioSettings.Configure(44100, 2);`（启动一次）
  ⇒ **运行期不会变**，`MixFxSource` 构造期捕获的 `channels/fadeStep/DSP/scratch` 不会失效 ✔。
- `WrapLive`/`WrapWith` 每次建链都新建实例（`:294-310`），而每次 `Play()` 都会重建整条链
  （`Core/PlaybackManager.cs:569-598` → `RenderEngine.RenderProject` → `BuildTrackOutputs`）⇒
  "下次播放"自动取新格式 ✔。
- **死 UI（登记）**：`OpenUtau/Views/RenderWindow.axaml:146-150` 有 `SampleRateCombo`（44100/48000），
  但 `RenderWindow.axaml.cs` **零引用**（全仓 grep `SampleRateCombo` 只命中 axaml 本身）⇒ 该选择器目前不起作用，
  也不写 `AudioSettings`。它同时是"采样率可变"唯一看起来存在的入口 → 将来若接上，必须：
  ① 改率时 `StopPlayback()` 并重建链；② 注意 `MixFxSource` 的静态 `SampleRate`（`:20`）与实例字段（`:82-88`）
  会给 UI 与 DSP 两个不同的事实（曲线屏按新率、DSP 按旧率）——届时需把静态常量改为实例属性或整体重建。

### A6 旁通残留：模块关闭 / seek / 循环回跳 / Freeverb 尾音 —— OK（依赖附A L1）

- 模块关闭：`RunStage`（`:186-202`）在 gain 淡到 0 时 `effect.Reset()`；
  主开关关闭：`Mix`（`:151-157`）淡到 0 时 `ResetEffects()`；且从下一块起快路径生效（`:131-136` 条件
  `masterGain == 0f && masterTarget == 0f`）⇒ **不会每块重复重置**。
- 状态覆盖面：`ResetEffects`（`:180-184`）= EQ（biquad x1/x2/y1/y2，`BiquadEQ.cs:73-79`）
  + 压缩（`envDb`，`SimpleCompressor.cs:50-52`）+ 混响（8×2 comb + 4×2 allpass + `preDelayBuf` + `preDelayIdx`，
  `Freeverb.cs:106-113`）⇒ **Freeverb 尾音（含预延迟线）在内** ✔。
- 位置不连续自愈：`Mix`（`:116-118`）`if (hasMixed && position != nextPosition) ResetEffects();`；
  测试 `Seek_ClearsDspResidue`（`MixFxSourceTest.cs:442-463`）用"跳转链 vs 全新链逐样本相等"钉住 ✔。
- **依赖 L1（既有缺口，非本轮引入）**：`MasterAdapter.SetPosition` 里 `source.Reset()`
  （`MasterAdapter.cs:65-70`）的下游是 `WaveMix`（`RenderEngine.cs:173`），而 `WaveMix` **没有覆写 `Reset()`**
  （`SignalChain/WaveMix.cs:1-22`，落到 `ISignalSource.Reset` 默认空实现 `ISignalSource.cs:23`）
  ⇒ 轨道级 `MixFxSource`/`EffectChain` 收不到这次 Reset。当前每次 Play/Seek/循环回跳都重建链
  （`PlaybackManager.cs:606-617` 循环回跳走 `Play`），所以**不产生可听 bug**；
  这也意味着 `MixFxSource` 的位置连续性自愈实际很少有机会触发（单会话内位置单调）。
  L1 修复（`WaveMix.Reset()` 下传）时建议一并复核自愈路径是否还需要。

### A7 旧 `EqBypassed: true` 迁移 / 双键往返幂等 —— OK（+2 低风险）

- 反向别名实现：`Core/Ustx/UMixFx.cs:14-23`
  ```csharp
  public bool EqEnabled { get; set; } = true;                     // 新键，默认 true
  public bool EqBypassed { get => !EqEnabled; set => EqEnabled = !value; }   // 旧键 = 反向别名
  ```
- 迁移：测试 `OpenUtau.Test/Core/USTx/UMixFxTest.cs:60-72` 用只含 `eq_bypassed/comp_bypassed/reverb_bypassed`
  的 YAML 反序列化，断言 `EqEnabled == false / CompEnabled == true / ReverbEnabled == false` ✔。
- 并存幂等：序列化**同时**写出两套键且取值恒互反 ⇒ YamlDotNet 按文档顺序施加 setter 的结果与顺序无关；
  测试 `:75-95` 断言 `eq_enabled: false` 与 `eq_bypassed: true` 同时存在，并断言
  `Serialize(Deserialize(y)) == y`（真幂等）+ `Clone()` 走规范键 ✔。
- 低风险备注 ①：若某文件两套键**取值互相矛盾**（手改/第三方生成），加载结果取决于文档顺序，无校验、静默。
- 低风险备注 ②：本仓写出的 ustx 会多 3 个旧键；上游 OU（`30d09962` 后只有 `EqEnabled`、无 `EqBypassed`）
  读到未知键的行为取决于其反序列化器的 unmatched-property 策略——本树无上游源码，**未验证**；
  若需与上游互传工程，阶段二可加一条双向往返检查（当前不是验收项）。

### A8 播放中改 MixFx 不触发重渲染/重启 —— Core OK；UI 写入契约缺陷（D1）

- **Core 侧无触发点**：`Core/PlaybackManager.cs:767-810 OnNext` 只处理
  `SeekPlayPosTickNotification / VolumeChangeNotification / PanChangeNotification / MasterVolumeChangeNotification /
  Bpm·TimeSig 系列 / LoadProjectNotification`，**没有任何 MixFx 分支** ⇒ 改参数不会走 `Play`/`Render`；
  `MixFxMode.Live` 每块读参（`MixFxSource.cs:121-125`）→ 同一链实例即时可听。
  单元证明：`MixFxSourceTest.cs:308-322`（`Live_WiringFollowsTrackEditsWithoutRebuild`）、
  `:324-337`（`Live_HandlesMixFxCreatedWhilePlaying`：播放中从 `MixFx == null` 首次开启也出声）。
  端到端（UI 旋钮 → 不重启播放）需阶段二实机确认。
- UI 通知路径：`OpenUtau/Views/TrackEffectRack.axaml.cs:552-556`
  `DocManager.Inst.MarkProjectModified(); MessageBus.Current.SendMessage(new MixFxChangedNotification(...));`
  —— `MarkProjectModified`（`Core/DocManager.cs:144-149`）只置 `Project.Saved=false` + autosave 哨兵，不广播渲染命令；
  `MixFxChangedNotification` 只被 `OpenUtau/Controls/TrackHeaderCanvas.cs:101` 用于重绘 ✔ 不触发重渲染。
- **缺陷 D1【既有，建议本轮修】**：另两条 MixFx 写入路径**漏掉** `MarkProjectModified()`：
  - `OpenUtau/ViewModels/MixFxViewModel.cs:290-296 Apply()`：`track.MixFx = BuildUMixFx();` 之后无标记；
  - `OpenUtau/ViewModels/TrackHeaderViewModel.cs:185-192`（轨道头 fx 按钮）：
    `track.MixFx.Enabled = enabled;` / `track.MixFx = new UMixFx { Enabled = true };` 之后无标记，
    也没有 `MixFxChangedNotification`（⇒ 混音台 fx 指示不刷新，`MixerTrackStrip.axaml.cs:136-141` 只在 `LoadTrackData` 刷新）。
  这直接违反 `DocManager.cs:139-149` 的**明文契约**：*"MixFx 滑杆等直接改模型（不进 undo 队列）的路径必须调用：
  否则 ChangesSaved 误判 true → 退出不提示保存、30s autosave 跳过 → 静默丢 mixer 改动。"*
  ⇒ 用户把机架打开后退出，设置可能静默丢失。对比同文件里 Volume/Pan/Mute/Solo 都调用了（`:134/142/170/175/183`）。
  归属：fx-ui 写入范围（`TrackHeaderViewModel.cs` 就在其任务范围，fx-ui 的重绘正是改这个文件），**建议随 T3 一并修**。
  > **阶段二后续（2026-10-04 补记）**：该缺陷已在 fx-ui 的追加提交中修复（`MixFxViewModel.cs:350-363` 的 `Apply/MarkModifiedIfDirty` + `MixFxDialog.axaml.cs:81-84` + `TrackHeaderViewModel.cs:196-203` 补 `MarkProjectModified()` 与 `MixFxChangedNotification`），并经我的**严协议真机复验 PASS**（对照组 6×skipped vs 实验组 autosave + 内容 `eq_low_db: 7.2` + 退出提示「当前工程有未保存改动」）——见 §6.2 D。此处阶段一的判定描述保留原样，供追溯。

## 3. B 组：样式接管（fx-ctl）

### 前置事实（我的独立取证，后面所有结论都建立在这两条上）

1. **挂载链**：`OpenUtau/App.axaml:43-60` 顺序为
   `FluentTheme → DataGrid/ColorPicker Fluent → Styles.axaml → Md3InputThemes → Md3Menus → Md3Controls →
   Md3Transitions → Md3ControlThemes → Md3SelectionThemes(最后)`；
   `Md3SelectionThemes.axaml:41-548` 把 5 个 `ControlTheme` 与"装主题"的 5 条应用级 Style 放在**同一文件**
   （跨字典 `StaticResource` 解析不到，`Md3SelectionThemeTests.cs:113-135` 用文本断言钉住该范式与注册顺序）✔。
2. **优先级规则（我按仓库自身测试反推出的实证规则，而非猜测）**：
   *应用级 Style setter 压过 ControlTheme setter；同类来源中后注册者胜*。
   - 正证 A：`Styles.axaml:356-370` 的 `CheckBox.menu`（应用级）把 `Border#NormalRectangle` 的
     BorderThickness 置 0/背景置透明，而主题 `:99-102` 给 `:checked` 的是 primary 实底 —— 运行时断言
     `Md3SelectionThemeTests.cs:220-233` 读到的是 0/Transparent ⇒ 应用级胜 ✔。
   - 正证 B：`Styles.axaml:512-575` 的 `Slider.fader` 用**应用级 Template**，主题里也有 `Template` ——
     `Md3SelectionThemeTests.cs:407-422` 断言 fader 仍是 16×6 胶囊（应用级胜）✔。
   - 正证 C：`Styles.axaml:390-393`（ProgressBar 背景 Transparent / 前景 AccentBrush1）被**后注册**的
     `Md3Controls.axaml:92-97`（池色）覆盖 ⇒ 应用级之间后注册者胜；测试 `:353-385` 读到池色 ✔。

### B9 全仓 `/template/` 补丁 × 新模板部件名 —— OK（14 处逐条核对，无静默失效）

先给覆盖面：全仓 `*.axaml` 的 `/template/` 命中 172 处；其中**涉及本轮 5 类控件**的共 14 个补丁点，
另有 5 处属别的控件（ComboBox/Button/TextBox/MenuItem/ListBoxItem）不受影响。
另外我 grep 了 `*.cs`（`OpenUtau/` 全树）对
`NormalRectangle|CheckGlyph|PART_MovingKnobs|SwitchKnobBounds|PART_Indicator|PART_DecreaseButton|OuterEllipse|PART_Track|IndeterminateProgressBarIndicator`
的引用 = **0 命中** ⇒ 部件名契约只存在于 XAML 补丁与本仓测试，没有第三方代码后置依赖（唯一例外是 Avalonia 自身控件实现）。

| # | 补丁位置 | 引用部件 | 新模板是否有同名同类型 | 结论 |
|---|---|---|---|---|
| 1 | `Styles/Styles.axaml:356-370`（`CheckBox.menu`） | `Border#NormalRectangle`、`Path#CheckGlyph` | ✔ 模板 `:59`（Border）/`:62`（Path），勾形 Data 与 Fluent 同款（Material 24dp 勾） | **OK**，且有运行时断言（`Md3SelectionThemeTests.cs:220-233`） |
| 2 | `Controls/PianoRoll.axaml:237-251`（`Menu.Styles`，管 18 个菜单勾选框 `:271-361`） | 同上 | ✔ | **OK**（无自动化断言 → 阶段二目视） |
| 3 | `Styles/PianoRollStyles.axaml:8-16`（`CheckBox:checked`） | `Border#NormalRectangle` | ✔ | **OK**：补丁把选中盒染成 `SelectedTrackAccent*`，与主题 `:99-102` 同属性冲突时应用级胜 ⇒ 语义保持 |
| 4 | `Styles/PianoRollStyles.axaml:194-204`（`ToggleSwitch:checked`） | `Border#SwitchKnobBounds` | ✔ 模板 `:255` | **OK**（这条正是"改名就静默坏"的典型，保留同名是对的） |
| 5 | `Styles/PianoRollStyles.axaml:168-188`（`Slider.fader`） | `RepeatButton#PART_DecreaseButton`、`Thumb`（类型选择器） | ✔ fader 模板 `Styles.axaml:532/554` | **OK**（fader 走自己的模板，新主题不参与；与本轮无变化） |
| 6 | `Styles/Styles.axaml:580`（`Slider.fader:pointerover`） | `RepeatButton#PART_DecreaseButton` | ✔ | **OK**：K6 处置正确 —— 已收窄到 `.fader`，普通 Slider 不再被 `AccentBrush2` 压住主题状态色（`Styles.axaml:576-582` 注释即此意） |
| 7 | `Styles/PianoRollStyles.axaml:264-275`（`RadioButton`） | `Ellipse#OuterEllipse`、**`Ellipse#CheckOuterEllipse`**、`Ellipse#CheckGlyph` | `CheckOuterEllipse` 在新主题里**不存在**，但该补丁作用域是 `PianoRollStyles` 自己的 `RadioButton` 模板（`:206-263`，三个同名 Ellipse 齐全） | **OK（易误判）**：不是新主题漏配；说明见 R6（那 4 个子树的 RadioButton 仍走旧模板） |
| 8 | `Views/PreferencesView.axaml:127-133`（`RadioButton.chip`） | `Border#PART_ChipBg`、`Path#PART_ChipCheck` | 在**视图内自定义** `.chip` 模板里（`:113-125` 的 115/118 行） | **OK**：视图级 Template 胜应用级 Theme ⇒ 11 个 `Classes="chip"` 仍生效；但 `PreferencesViewTests.cs:44` 只做 XAML 文本断言（弱）→ 阶段二目视 |
| 9 | `Controls/ExpSelector.axaml:102-108` | `Border#Background`、`Border#DropDownOverlay` | ComboBox **本轮未接管**（全仓无 `TargetType="ComboBox"` 的 ControlTheme；`Md3InputThemes` 只管 TextBox）→ 仍走 Fluent | **OK**（同理 `Styles.axaml:330 ComboBox /template/ Path#DropDownGlyph`） |
| 10 | `Controls/TrackHeader.axaml:41-56` | `RepeatButton#PART_DecreaseButton`、`Thumb`、`Thumb /template/ Border` | ✔ fader 模板 | **OK** |
| 11 | `Controls/ViewScaler.axaml:17-23` | `Button /template/ ContentPresenter#PART_ContentPresenter` | Button 主题属上一轮 `Md3ControlThemes.axaml`，其模板确有 `PART_ContentPresenter`（该文件 33/39、92/96 行） | **OK** |
| 12 | `Styles/Styles.axaml:342-347`、`Views/TrackColorDialog.axaml:30` | `ListBoxItem /template/ ContentPresenter` | 上一轮 `Md3ListBoxItemTheme`，本轮未改 | **OK** |
| 13 | `Views/RenderWindow.axaml:69-73`（`ProgressBar.renderProgress`） | 无 `/template/`，只改 Height/CornerRadius/Foreground | 新模板 `PART_Indicator` 用 `{TemplateBinding Foreground}` ⇒ 语义仍成立 | **OK**（颜色非池键，见 R7） |
| 14 | `Views/PreferencesView.axaml:247-251`（`ToggleSwitch` On/Off 空串） | `PART_On/OffContentPresenter` | ✔ 模板 `:241/:246` | **OK**，且 `Md3SelectionThemeTests.cs:424-443` 用"与 Fluent 对照"钉住未设 On/OffContent 时行为一致 |

模板完整性还有一条**既有自动化保护**：`OpenUtau.Test/App/ThemeContractTests.cs:228-249`
（`InputControls_ApplyTemplateWithoutError`）对 TextBox/ComboBox/ToggleSwitch/CheckBox/RadioButton/Slider/ProgressBar
逐个 `ApplyTemplate()`（缺必选 PART 会抛异常）⇒ 新模板不会缺 Avalonia 侧必需部件。

### B10 应用级 setter 压住新主题 —— 风险（低）：3 处双写/死写 + 1 处需目视

**完整清单**（我在全仓 grep 了 `<Style Selector="(CheckBox|RadioButton|ToggleSwitch|Slider|ProgressBar)…"` 共 40 处，
逐处判断作用域与冲突）：

| 位置 | 内容 | 对新主题的影响 | 判定 |
|---|---|---|---|
| `Styles/Styles.axaml:512-575` `Slider.fader` | Template + Foreground(AccentBrush1) + Background | 压住主题的 Template/Foreground/Background（**有意为之**） | 保留，已被测试钉住（`Md3SelectionThemeTests.cs:407-422`） |
| `Styles/Styles.axaml:580` | `.fader:pointerover PART_DecreaseButton` | 已收窄，不再压普通 Slider | 正确处置 ✔ |
| `Styles/Styles.axaml:390-393` `ProgressBar` | `Background=Transparent`、`Foreground=AccentBrush1`（**非池色**） | 被后注册的 `Md3Controls.axaml:92-97`（池色）整体覆盖 ⇒ 这层是**死写** | 登记清理（删前确认无窗口依赖透明轨道底） |
| `Md3Controls.axaml:69-73` `CheckBox, RadioButton` | FontSize 13 / MinHeight 28 / Padding 8,0,0,0 | 与主题 `:45-52`、`:147-153` **同值** ⇒ 等价双写 | 无害，登记 |
| `Md3Controls.axaml:74-77` `ToggleSwitch` | MinHeight 28 / Margin 0 | 同值双写 | 无害 |
| `Md3Controls.axaml:87-91` `Slider` | Margin 0 / MinHeight 20 / Foreground md3.primary | 与主题 `:366-368` 同值 ⇒ 主题的 `Foreground` setter 是死写 | 保留（双保险），登记 |
| `Md3Controls.axaml:92-97` `ProgressBar` | MinHeight 6 / CornerRadius 999 / Foreground · Background = 池色 | 与主题 `:439-442` 同值 ⇒ 主题的这两个 setter 是死写（**生效的是应用级**） | 保留，登记 |
| `Styles/Styles.axaml:350-354` `CheckBox` | MinHeight 28、**MinWidth 28**、FontSize 13 | 主题未设 MinWidth ⇒ 应用级 28 生效 | **需目视**：`.menu`/无内容勾选框的最小宽度是 28（18 盒 + 10 余量），可能让菜单勾号列比 Fluent 时期偏宽 |
| `Styles/Styles.axaml:627-631` `RadioButton` | FontSize 13 / Margin 0,2 / MinHeight 28 | 主题未设 Margin ⇒ 生效，无冲突 | OK |
| `Md3InputThemes.axaml:138-141` `Slider` | FontFamily/FontSize | 字体层，不冲突 | OK |
| 视图级（作用域受限，逐窗口） | `PianoRollStyles.axaml:168/194/206`（fader/ToggleSwitch/**RadioButton 模板**）、`TrackHeader.axaml:39`（fader）、`NotePropertiesControl.axaml:16`（fader Focusable）、`PreferencesView.axaml:107/215/247`（chip/rowSlider/ToggleSwitch）、`MixFxDialog.axaml:28/41`（Slider Margin/Foreground/Background）、`RenderWindow.axaml:34/69`、`TrackEffectRack.axaml:74`（Height/Margin）、`TranscribeDialog.axaml:17`/`DsScriptExportDialog.axaml:17`（CheckBox Foreground → 模板用 `TemplateBinding Foreground`，**仍然生效**，是文字色） | 除 `PianoRollStyles` 的 RadioButton 模板外均无跨控件副作用 | 见 R6（范围不完整） |

结论：**没有功能性破坏**；存在 3 处"写了不生效/等价双写"（登记清理）+ 1 处 MinWidth 需目视。

### B11 `:focus-visible` / `:disabled` / 不定态 与契约断言 —— 风险（低）

- **有实装且有运行时断言**：CheckBox 的 `PART_FocusRing`（模板 `:70-71` + `:133-139`）与 RadioButton 的同名部件
  （`:164-165` + `:207-213`）；`Md3SelectionThemeTests.cs:390-403` 用 `box.Focus(NavigationMethod.Tab)`
  证明"鼠标点击不亮、键盘焦点亮"，`:256-258`/`:210-213` 钉住 `:disabled` 0.38。
- **缺焦点视觉部件**：ToggleSwitch（模板 `:226-269`）、Slider（`:370-415`）、ProgressBar（`:444-477`）
  都没有 `PART_FocusRing` 或 `FocusAdorner`，主题里也没有针对它们的 `:focus-visible` ⇒
  这三类控件的键盘焦点**可能没有可见反馈**（Fluent 下是否提供，我无法静态确定）→ **未验证，需实机 Tab 核对**（R4）。
- **`:disabled`**：CheckBox/RadioButton/ToggleSwitch/Slider 有（`:140-142`、`:214-216`、`:357-359`、`:432-434`）；
  **ProgressBar 没有**（进度条一般不可交互，可接受）。
- **不定态动画**：由**主题**提供（`:498-523` 两条 `Style.Animations`，1.8s + 0.45s 错峰，`TranslateTransform.X`），
  并有运行时断言证明真的在动（`:374-381` 用 `AvaloniaHeadlessPlatform.ForceRenderTimerTick(30)`）✔；
  但见 R5：行程硬编码 −80→320 / −180→420，与轨道宽度无关。
- **弱断言（非阻塞）**：`Md3SelectionThemeTests.cs:452-458 ThemeFile_DeclaresFocusAndDisabledStates`
  只 `Assert.Contains`"文件文本里出现过 `:focus-visible`/`:disabled`/`PART_FocusRing`" ——
  只要出现过一次就通过，无法证明"每个控件都有"。建议后续给这三类控件补实装或把断言改成
  "逐控件 × 逐状态"（本轮不阻塞）。

### B12 Md3Controls 失效 Fluent 键覆盖 → 视觉双写 —— OK（登记清理项）

- 实际残留的 Fluent 画刷键只有 4 个：`OpenUtau/Colors/Brushes.axaml:187-194`
  （`RadioButtonOuterEllipseFill/Stroke/FillPointerOver/StrokePointerOver`），
  而它们**仍被 `Styles/PianoRollStyles.axaml:224-225/233-235/243-245/264-274` 的 RadioButton 模板引用**
  ⇒ **不能删**（删了那 4 个子树的单选圈会掉色）。`ThemeContractTests.cs:74-76` 也把这几把键列为必须提供 ✔。
  除此之外全仓没有针对这批控件的 Fluent 键覆盖（grep 过 `Brushes.axaml`/`DarkTheme.axaml`）。
- 由此，#12 的"失效 Fluent 键覆盖"在本树上表现为**几何/池色的等价双写**（清单见 B10 表），
  而非失效的 Fluent 键 ⇒ 登记为可清理项即可，本轮不动。
- 契约测试**没有被削弱**：`git diff 21255585^1 21255585 -- OpenUtau.Test/App/Md3ControlThemeTests.cs` 显示
  旧断言（`Md3Controls` 六组选择器仍在，第 143 行）**原样保留**，只是注释改口径并**新增**了
  "Md3SelectionThemes 里有 5 个 TargetType + 5 条同文件挂载 Style"的断言（`:146-153`）⇒ 是扩充，不是放宽 ✔。

## 4. 缺陷 / 风险清单（按处理建议排序）

| ID | 级别 | 内容 | 证据 | 归属 / 建议 |
|---|---|---|---|---|
| **D1** | **缺陷**【既有，本轮放大】 | 机架写入模型后**不标脏**：`MixFxViewModel.Apply()` 与轨道头 fx 按钮都漏 `MarkProjectModified()` ⇒ 退出不提示保存 / 30s autosave 跳过 ⇒ **机架设置静默丢失**；轨道头路径也不发 `MixFxChangedNotification` ⇒ 混音台 fx 指示不刷新 | `OpenUtau/ViewModels/MixFxViewModel.cs:290-296`、`OpenUtau/ViewModels/TrackHeaderViewModel.cs:185-192`；契约原文 `Core/DocManager.cs:139-149`；对照正确实现 `Views/TrackEffectRack.axaml.cs:552-556`；混音台只在加载时刷新 `Controls/MixerTrackStrip.axaml.cs:136-141` | **fx-ui**（`TrackHeaderViewModel.cs` 正在其写入范围）：两处各加一行 `DocManager.Inst.MarkProjectModified()` + 按需广播通知。阶段二由我验证"打开机架→改参→关窗→是否提示保存" |
| R1 | 风险（前瞻，当前不成立） | 采样率若将来可变：`MixFxSource` 构造期捕获格式（静态 `SampleRate` 与实例 DSP 会给出两套事实），`WaveSource/Fader/MasterAdapter/PlaybackManager` 动态读格式 ⇒ 播放中改率会混链。另 `RenderWindow.axaml:146-150 SampleRateCombo` 是**误导性死 UI** | `Core/SignalChain/AudioSettings.cs:11-21`、`OpenUtau/Program.cs:27`、`MixFxSource.cs:20/81-91`、`Views/RenderWindow.axaml:146-150`（代码后置零引用） | 登记；接 Preferences 时必须 StopPlayback + 整链重建，并处理静态/实例两套事实 |
| R2 | 风险（低） | §5.3"全程无 44100 硬编码"**近似成立**：`Freeverb` 内仍有 44100（`DecaySeconds` 归一化、`Process` 的 `channels != 2` 早退、构造函数默认值）⇒ 非 44.1k 时曲线屏数据会偏、非立体声时混响静默不生效 | `Effects/Freeverb.cs:47`、`:98`、`:119-123`；`Effects/BiquadEQ.cs:27`、`Effects/SimpleCompressor.cs:29`（默认参数，但 `MixFxSource.cs:84-86` 显式传 AudioSettings ✔） | 登记（当前 rate 恒 44100 无影响）；若要严格达标，把 `DecaySeconds` 加 sampleRate 参数 |
| R3 | 风险（低） | `UMixFx.Clone()` 与 `Sync` 的读值非原子 ⇒ 导出快照可能是"半组参数"（一次导出）/单块混搭（播放 ≤11.6ms） | `Core/Ustx/UMixFx.cs:45-55`、`MixFxSource.cs:232-236/255-281` | 登记；约定 UI 整对象替换（fx-ui 已是） |
| R4 | 风险（低） | ToggleSwitch / Slider / ProgressBar 无焦点视觉部件（CheckBox/RadioButton 有）；Fluent 下是否有未验证 | `Styles/Md3SelectionThemes.axaml:226-269/370-415/444-477`（无 `PART_FocusRing`） | 阶段二用 Tab 键核对；若确实丢反馈，补 `FocusAdorner` 或焦点环 |
| R5 | 风险（低） | 不定态进度条行程硬编码（−80→320、−180→420），与控件宽度无关 ⇒ 宽进度条可能"扫不满/长空隙" | `Styles/Md3SelectionThemes.axaml:500-523`；使用点 `Views/MainWindow.axaml:797`（Height 4）、`Views/SplashWindow.axaml:26`、`Controls/PianoRoll.axaml:698`、`Views/RenderWindow.axaml:218` | 阶段二目视（主窗状态栏 / 闪屏 / 导出窗口） |
| R6 | 风险（低，范围问题） | 本轮 RadioButton 接管在**钢琴卷帘 + LyricsDialog + LyricsReplaceDialog + ExpressionsDialog** 四个子树里看不到：这些文件 include 了 `Styles/PianoRollStyles.axaml`，其视图级 `RadioButton` 直接换了 Template（14px 椭圆 + `SelectedTrackAccent` 语义） | `Styles/PianoRollStyles.axaml:206-276`；include 点 `Controls/PianoRoll.axaml:12`、`Views/LyricsDialog.axaml:12`、`Views/LyricsReplaceDialog.axaml:11`、`Views/ExpressionsDialog.axaml:18` | 需 Lead/用户确认是否接受"这 4 处保持旧外观"（我判断可接受：语义是轨道强调色） |
| R7 | 风险（低） | `RenderWindow` 导出进度条前景走非池键 `AccentBrush1`（视图级）⇒ 可能偏离 `md3.primary`；同窗口 `RadioButton.renderOption` 只改字号（外观随新主题） | `Views/RenderWindow.axaml:69-73`、`:34-37` | 阶段二目视 +（若需严格）换成 `md3.primary` |
| R8 | 备注（A2） | `CreateWaveFile16` 的块大小注释不准确（100ms 之说仅对 WASAPI shared / 录制路径成立）；导出首块可能触发一次性几何扩容（**非音频线程**） | `MixFxSource.cs:44/68`、`Export/ExportSession.cs:74` | 阶段二实测导出一次；注释可改口径 |
| R9 | 备注（A7） | 两套模块开关键取值矛盾时静默取文档后者；上游 OU 读本仓 ustx 的未知键策略未验证 | `Core/Ustx/UMixFx.cs:18-23`、`OpenUtau.Test/Core/USTx/UMixFxTest.cs:75-95` | 登记（可选：加一条双向往返测试） |
| R10 | 备注（A6/L1） | `WaveMix` 不转发 `Reset()` ⇒ `MixFxSource` 的位置连续性自愈在"同链复用"前无机会生效 | `SignalChain/WaveMix.cs:1-22`、`ISignalSource.cs:23`、`MasterAdapter.cs:65-70`、`RenderEngine.cs:173` | 附A L1 已登记；L1 修复时复核 |
| R11 | 备注（B11） | 契约测试 `ThemeFile_DeclaresFocusAndDisabledStates` 是文本级弱断言 | `Md3SelectionThemeTests.cs:452-458` | 可选加固 |

## 5. 阶段二待办（等 Lead 通知"已合并"后执行）

**A. 独立复跑（我自己跑，不与他人并发）**
1. `git -C <集成树> log --oneline -1` + `git status --short`（确认 Lead 已合完、工作树状态已知）。
2. `dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=`（记录退出码/错误数，期望 0）。
3. `dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build`（记录命令/退出码/通过数；与基线 355、以及本轮 388 对比；
   合并 fx-ui/fx-rack 后应 ≥ 388）。
4. `pwsh -File ".dsh\fx\checks.ps1" -Tree <集成树>`（键对齐 / 硬编码色值）。

**B. computer use 视觉验证**（严格按 PID：只启动/关闭我们自己的构建产物；**严禁按进程名杀**）
- `launch.ps1 -WaitSeconds 10` → 核对 PID/PATH（PATH 必须是工作树）；`shot_pid.py --pid <PID> --list` 核对标题；
  `--all --outdir .dsh\fx\shots` 抓主窗 + 弹窗；`read_image` + `auto-look.py --check` 双通道看图；结束 `stop.ps1`。
- 机架：轨道头 fx 按钮 / 右键轨道 →「效果器…」→ 三面板成型、非模态（主窗仍可操作）、曲线屏随旋钮变化、
  模块开关变暗、总电源、实色无玻璃、深浅色跟随、**文案无 `mixfx.*` 原始键**。

**C. 本轮样式接管的靶点（B9/B10/R4-R7 的目视清单，我按代码推出的"新主题首次露面处"）**
1. **偏好设置页 4–5 组 `RadioButton.chip` 分段控件**（播放后端 / 自动滚动 / 起始时间锁定 / 音名 / 引擎）：
   选中项底色是否仍为 `md3.secondary-container`、✓ 是否出现（场景 8，无自动化断言）；`Slider.rowSlider`、`ToggleSwitch`（空 On/Off 文案）同页扫一眼。
2. **钢琴卷帘菜单 18 个勾选框**（`Controls/PianoRoll.axaml:271-361`）+ 歌手对话框 `CheckBox.menu`（`Views/SingersDialog.axaml:88`）：
   只显示勾不显示框、勾色 = 轨道强调色；顺带看勾号列对齐（MinWidth 28，见 B10）。
3. **钢琴卷帘 ToggleSwitch 开态轨道**是否仍是 `SelectedTrackAccent`（场景 4）。
4. **`.fader` 推子**（混音台 / 轨道头 / 音符属性 / Transcribe 对话框）：16×6 胶囊 + hover 提亮（场景 5/6/10）。
5. **导出窗口**（`Views/RenderWindow.axaml`）：进度条（确定态 + 不定态扫过，R5/R7）、`RadioButton.renderOption` 外观。
6. **主窗状态栏 / 启动闪屏 / 钢琴卷帘**进度条：确定态与不定态（R5）。
7. **Tab 键焦点**：CheckBox 出现 primary 焦点环；ToggleSwitch/Slider 是否有可见焦点（R4）。
8. **深浅色切换**后新主题颜色全部跟随（池重建）。
9. **D1 验证**：打开机架 → 改一个参数 → 关窗 → 看是否提示保存/autosave 是否落盘；轨道头 fx 按钮开/关后混音台 fx 指示是否同步。
10. 顺带：导出一次整曲，观察首块是否出现一次性大扩容（R8，可选，看日志/内存曲线）。

**D. 阶段二报告**：PASS/FAIL + 证据（截图绝对路径、命令、退出码、日志片段、我实际看到的现象），
并明确区分「已验证（我自己跑出来的）」「仅代码审查」「未验证/需用户实机确认」。

## 附：本报告的取证命令（可复现）

```powershell
$r = "G:\xklmy文件夹\vibe coding\UTvTU-fx-core"
git -C $r log --oneline -12
git -C $r log --stat --format="=== %h %s" e8a93cdc~1..21255585      # 本轮改动文件集（区分既有/本轮）
git -C $r show 2d1c55a0 -- OpenUtau.Core/Render/RenderEngine.cs      # 三态接线与旧 applyMixFx 语义对照
git -C $r diff 21255585^1 21255585 -- OpenUtau/Styles/Styles.axaml OpenUtau/App.axaml
git -C $r diff 21255585^1 21255585 -- OpenUtau.Test/App/Md3ControlThemeTests.cs   # 契约是否被放宽
```

---

# 阶段二：独立复跑 + computer use 视觉验证

> 我在**集成树**上独立复跑构建与测试，并用 `.dsh/fx/` 的按 PID 工具启动**我们自己的**构建做界面验证。
> 全程未按进程名杀进程（只用 `launch.ps1` / `stop.ps1` / 精确 PID）。
> 证据分级：**已验证**（我自己跑/看到）· **仅代码审查**（静态）· **未验证**（未取得证据）。

## 6.0 阶段二总结论（三轮合并，最终）

| 项 | 结论 | 依据 |
|---|---|---|
| 构建 0 错误 | **PASS** | 增量 + `-t:Rebuild` 均 0 错误（第一轮 72edf3de）；Lead 干净重跑 ec85d3f8 / 5f3799ae 亦 0 错误 |
| 全量测试 | **PASS** | 我：355→429(72edf3de)→431(a3a1fd5f) 均 0 失败；Lead 干净重跑 a3a1fd5f 433/433/432（Light 那 1 例为**测试隔离问题**）、最终 5f3799ae **438/438/438 三变体全绿** |
| 三面板机架（成型/曲线屏/模块变暗/总电源/实色/无缺键） | **PASS** | 第一轮 3 张深色截图 + 第二轮 `r2-rack-dark-normal.png` + 第三轮 `r3-rack-dark.png`（无回退） |
| 非模态 | **PASS** | 主窗在机架打开时可点可切页、机架不关（交互验证）+ 代码 `Show(owner)` |
| ESC/取消 = 还原 | **PASS** | 第二轮 `rack-after-esc-reopen.png`（低频回到 0.0 dB、模块回到开） |
| 控件陈列室五节 | **PASS** | `shot-pid8696-hwnd3212126.png` + 首屏 |
| 偏好页选择类控件（深浅色） | **PASS** | `prefs-appearance-*.png`、`prefs-light.png`、`prefs-dark-restored2.png` |
| **D1 标脏/落盘/退出提示** | **PASS**（第二轮严协议） | 对照组 6×skipped vs 实验组 `Autosave` + 内容 `eq_low_db: 7.2` + 退出提示截图 |
| hover 辉光移除 + 8% 加深 | **PASS** | 像素级：填充 (207,189,254)→(192,174,235)，无光晕 |
| Tab 焦点环 | **部分** | 机架开关环**已验证**；偏好页开关/滑条与 ProgressBar **未验证**（见 §6.2 C） |
| 主按钮**深色前景**（T9/T10） | **PASS**（第三轮 5f3799ae 复测） | 像素级：`确定` 文字 = **`#36275D` = `md3.on-primary`**，对 `md3.primary` 底 **7.70:1**（旧白字 1.70:1）；导出窗口主按钮同值；普通/描边按钮仍 `on-surface` 14.35:1 —— 见 §6.5 |
| `ListBoxItem` 选中行文字（T10-B） | **文字 PASS + 1 项登记** | Light 选中行文字实测 `#4D4458` ≈ `md3.on-secondary-container` ✔（此前恒 `on-surface`）；但**局部**对比度实测仅 **3.49:1**（`Styles.axaml:303-305` 旧 `/template/` 补丁在主题胶囊内又刷一层）→ 登记清理项 —— 见 §6.5 D |
| 旧 `/template/` 补丁无静默回归 | **PASS**（静态 14 处 + 运行时：`.chip`、`.menu` 契约、`.fader`） | 阶段一 §B9 + 偏好页 chip 实测 |
| 偏好页分段控件文案截断 | **既有问题**（非本轮） | `prefs-light-chips-zoom.png` |

## 6.1 第一轮：集成树 `72edf3de`（= fx-core + fx-ctl + fx-ui 6 + fx-rack 4 + 3 merge）

### A. 构建与测试 —— 已验证（PASS）

| 项 | 命令 | 结果 |
|---|---|---|
| 忠实构建（增量） | `dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=` | **0 错误 / 0 警告**，exit 0，4.88s |
| 从零重建 | 同上加 `-t:Rebuild` | **0 错误**，1592 警告，exit 0，1m50s。警告全部在 `OpenUtau.Test`（CS8632 可空注释 + CS0105 重复 using），因测试工程未开 `<Nullable>` ⇒ 非本轮引入、不影响 `TreatWarningsAsErrors` 的产品工程 |
| 全量测试 | `dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build` | **429 通过 / 0 失败 / 0 跳过**，exit 0，2m51s；**跑了 2 次结果相同** ⇒ 与 Lead 的 429 独立吻合（355 基线 + 19 fx-core + 14 fx-ctl + 24 fx-ui + 17 fx-rack） |

### B. 机架三面板 —— 已验证（PASS）

启动：`pwsh -File .dsh\fx\launch.ps1 -WaitSeconds 12` → `PID=8696`
`PATH=G:\xklmy文件夹\vibe coding\UTvTU-fx-core\OpenUtau\bin\Debug\net8.0-windows\OpenUtau.exe`（**属我们的工作树**，启动前无同名进程在跑 ⇒ 无歧义）
`TITLE=UTvTU v0.1.568.2 p0.0.3`；`shot_pid.py --pid 8696 --list` 核对通过。
打开方式：**轨道头 fx 按钮**（窗口标题 `试听效果` = `mixfx.caption` zh ✔ 非键名）。

| 检查点 | 我的观察 | 证据 |
|---|---|---|
| 三面板成型 | EQ / 压缩器 / 混响 三张卡并排，各带模块强调色条、电源开关、曲线屏、旋钮组（EQ 4 旋钮 + 压缩 3 + 混响 4） | `shot-pid8696-hwnd2294562.png`（sha256 `76e757f3…cf85`，**注**：该文件随后被同路径后续截图覆盖，见 §6.3） |
| 曲线屏随旋钮变化 | 拖 EQ「低频」旋钮：读数 `0.0 dB → +7.4 dB`，**EQ 曲线屏低频段同步抬起** | 同上路径（sha256 `85353f0b…2ed5`） |
| 模块开关变暗 | 关 EQ 模块电源 → **EQ 曲线屏整体变暗**，压缩器/混响两卡不受影响（模块隔离成立） | 同上路径（sha256 `4d50a70f…c0c4`） |
| 总电源开关 | 顶栏「电源」开关可切换（关→灰 / 开→primary 实底圆钮） | 同上两张 |
| 实色无玻璃 | 卡片/窗底/曲线屏全为实色，无半透明、无背景模糊 | 全部机架截图 |
| 文案无缺键 | 轨道 / 电源 / 干声 / 预设库 / 选择已保存的预设... / 保存... / 删除 / 确定 / 取消 / 应用默认设置 / 导出缩混时应用效果 / 播放中改动实时可听；取消或按 Esc 恢复到打开本窗口时的设置。 全部正常中文，**无 `mixfx.*` 键名** | 同上 |
| **非模态（交互验证）** | 机架开着时点主窗「素材库 → 效果器」页签：**主窗成为前台（`GetForegroundWindow()==主窗 hwnd`）并成功切页**，机架窗口仍在 ⇒ 非模态成立 | 主窗截图（效果器页签高亮）+ `--list` 两窗口并存；代码侧 `MixFxDialog.axaml.cs:67 dialog.Show(owner)`（非 `ShowDialog`） |

**ESC = 还原**（已验证 PASS）：改参数（低频 +7.4 dB、EQ 模块关）→ 按 **ESC** → 机架关闭 → 重开：**低频回到 0.0 dB、EQ 模块回到开、曲线回到中性** ⇒ 恢复到打开时快照成立。证据 `rack-after-esc-reopen.png`；代码 `MixFxViewModel.cs:339-345 Revert()`。

### C. 控件陈列室 —— 已验证（PASS）

品牌菜单 → 工具 →「控件陈列室」→ 窗口 `控件陈列室` 打开，五节齐全：
`电源开关`（28px 圆钮 + 3 态样例 + 6 态冻结条）· `分段选择器`（EQ/压缩器/混响、开/关 + 6 态条）· `参数读数`（中频增益/中频频率/湿声比例 + 禁用态 + 6 态条）· `模块面板`（默认/悬停/禁用/旁通四张卡，旁通卡带「旁通」徽标）· `机架列表`（Lead Vocal 五行：EQ/压缩器/混响/OTT/空槽位 + 拖拽把手 + 开/关标记；右侧 Empty Track 空态文案）。
无空白、无错位、无文字截断；配色全走色池、实色。
证据：`shot-pid8696-hwnd3212126.png`（滚动态；首屏 sha256 `f5a50f90…cecf`，我读图时确认）。

### D. 偏好设置页选择类控件（深浅色）—— 已验证（PASS）+ 1 处既有布局问题

Dark（`prefs-appearance-notab.png`、`prefs-appearance-tab5.png`）与 Light（`prefs-light.png`）两套主题下：
- `RadioButton.chip` 分段控件：选中 = `secondary-container` 胶囊 + ✓ 勾（补丁 `PART_ChipBg`/`PART_ChipCheck` **仍然生效** ⇒ B9 场景 8 运行时确认）；
- `ToggleSwitch`：开 = primary 轨道 + on-primary 圆钮 / 关 = 灰轨 + outline 描边（新主题生效）；
- `Slider`：细轨 + 圆钮（新主题生效，`rowSlider` 的 Height 覆盖未破坏模板）；
- 色板圆钮与按钮颜色随主题正确切换；`关闭` 主按钮在 Light 为紫底白字（正确），在 Dark 为浅紫底**白字**（对比度问题，与 §G 同源）；
- **主题已还原为 Dark**（用户原值）：`prefs-dark-restored2.png`。

⚠️ **既有布局问题（非本轮，登记）**：`偏好设置 → 外观 / 回放` 的分段控件在一行内溢出卡片宽度，**中间 chip 文案被截断**（`唱名（do re mi fa sol la ti）` 只显示到 `sol`；`简谱（1 2 3 4 5 6 7）` 收尾括号被吃）；深浅色都存在。证据 `prefs-light-chips-zoom.png`（3× 放大）。建议：分段控件允许换行，或卡片加宽/文案缩短。

**Tab 焦点环（B11）**：按 Tab×5 + UI Automation 探针（`AutomationElement.FocusedElement`）尝试定位焦点，**未观察到可见焦点环**，但**未能确定当时焦点控件**（探针只拿到 Window 级）⇒ 判为**未验证**，本轮不给"有/无"结论；第二轮（fx-ctl 补焦点环后）用同一方法复核。

### E. D1 端到端 —— **第一轮结论经 Lead 复核后降级为"部分有效"，最终定性待第二轮**

**Lead 复核的两点，我已独立核实源码并接受修订**：
1. `DocManager.cs:131-137` 的 `ChangesSaved` 第一个合取项是
   `(Project.Saved || (Project.tracks.Count <= 1 && Project.parts.Count == 0)) && …`
   ⇒ **在「≤1 轨且 0 片段」的空工程上它恒为 true** ⇒ 无论是否标脏，退出都不提示保存。
   **我的退出观察用的正是空工程（1 轨 0 片段）⇒ 该条证据无效**，不能据此判 D1 FAIL。
2. 自动保存间隔 30 s（`MainWindow.axaml.cs:107-111`；我的日志亦呈 30 s 等距）⇒ 观察窗口需 ≥65 s 才可靠。
   **澄清**：我的观察窗口实际是 `14:03:23 → 14:10:53` 共 **16 次 tick（≈7.5 分钟）**，不是 30 秒，故"期间没有任何 `Autosave`"这一条不受此影响。

**仍然有效的那一半（独立于 `ChangesSaved`）**：`AutoSave()` 的跳过条件**只有** `undoQueue.LastOrDefault() == autosavedPoint`（`DocManager.cs:181`），而 `MarkProjectModified()` 会把 `autosavedPoint` 换成新对象（`:144-149`）⇒ **只要它被调用，下一个 30 s tick 必定是 `Autosave <path>` 而不是 `Autosave skipped.`**。实测日志原文：

```
14:02:53.761 [INF] Autosave  G:\…\net8.0-windows\Backups\Untitled-autosave.ustxp.    ← 新建工程后哨兵生效（唯一一次）
14:03:23.872 [INF] Autosave skipped.
…（连续 16 次，覆盖我全部机架编辑：拖旋钮 / 关模块开关 / 换预设）…
14:10:53.982 [INF] Autosave skipped.
14:11:12.687 [INF] Exiting.
14:11:12.687 [INF] Exited.
```

⇒ **机架编辑（拖旋钮 / 模块开关 / 预设）没有触发 `MarkProjectModified()`**；但"退出会静默丢改动"这一**推论在空工程上不成立**（被第 1 条掩盖）⇒ 第一轮只能给定性：**"标脏未发生"有日志证据；"后果严重性"未验证**。代码侧看似正确（`MixFxViewModel.cs:173-178` 属性变更 → 写模型 + `dirty=true`；`:359-363 MarkModifiedIfDirty`；`MixFxDialog.axaml.cs:81-84` 关窗补标记）⇒ 运行时 `dirty` 很可能未置位。

**第二轮复验协议（Lead 指定，逐条执行，非空工程）**：
① 打开/新建**非空工程**（≥2 轨，或 1 轨 + ≥1 片段）→ 使 `ChangesSaved` 的"空工程豁免"失效；
② 记录日志文件与最后一行时间戳 → **拖一个旋钮**（读数与曲线均须变）→ 点机架**标题栏 X** 关闭；
③ **等 ≥65 s**（跨 2 个 tick）→ 在 `Logs\log*.txt` 找 `Autosave <path>`（非 `skipped`）；
④ 再点主窗关闭按钮 → 应出现保存提示（`ChangesSaved == false` 的等价表现）；
⑤ **对照组**：不动旋钮直接关窗 + 等 ≥65 s → 应仍为 `Autosave skipped.`。

**📌 独立观察（既有设计，非本轮引入；Lead 要求登记进规划附A）**：**空工程（≤1 轨且 0 片段）上 `ChangesSaved` 恒为 true**（`DocManager.cs:133`）⇒ 新建工程里的 mixer / MixFx / 推子改动**不会触发退出提示**，只能依赖 30 s autosave 兜底；与"标脏是否发生"是两个独立问题（前者是既有豁免条款，后者是本轮要查的链路）。

**其它日志观察（已验证）**：整段会话除 `[ERR] Failed to scan samples directory …\Samples`（缺 Samples 目录，环境问题，非本轮）外**无任何 `[ERR]/[FTL]`**；`调试窗口`（`shot-pid8696-hwnd2098238.png`）与该日志一致。

### F. B10 / R6（Lead 点名的两条目视结论）

- **B10（`Styles.axaml:350-354` 的 `CheckBox{MinWidth=28}` 对菜单勾号列宽度）**：**仅代码审查 = 无可见影响**。列宽由应用级 `MinWidth=28` 决定（本轮未改）；本轮只把勾选框盒 20×20（Fluent）→ 18×18（自有模板），而 `.menu` 补丁把盒做成"不可见、只留 12px 勾" ⇒ 勾号在 28px 列内居中位置理论偏移 1px，肉眼不可辨。**未取得可视证据**（空工程下 Ctrl+F 未能唤出搜索栏、钢琴卷帘菜单点不开；`pianoroll-menu-attempt-failed.png` 为尝试记录）。第二轮可改从「工具 → 歌手...」或已有工程的钢琴卷帘菜单取图。
- **R6（4 处视图级 RadioButton 模板）**：**仅代码审查，可定性**。全仓 `<RadioButton` 只出现在 `Controls/SearchBar.axaml`、`Views/RenderWindow.axaml`、`Views/PreferencesView.axaml`、`Views/TranscribeDialog.axaml`；**`LyricsDialog` / `LyricsReplaceDialog` / `ExpressionsDialog` 里根本没有 RadioButton**（仅 include 了 `PianoRollStyles.axaml`）⇒ 这 3 处**无任何可见影响**。只有钢琴卷帘搜索栏（`SearchBar.axaml:8,10`）的 2 个 RadioButton 仍走视图级 14px 模板（`PianoRollStyles.axaml:206-276`；应用级 Style 的 `Template` 优先于应用级 `Theme`，阶段一已用 `.fader` 用例实证）⇒ 该处接管**不可见，但外观与改动前一致，不构成回归**。**未取得可视证据**（搜索栏未唤出）。

### G. flaky 测试 `Button_PrimaryVariant_IsFilledPill` 独立定性 —— 已验证（可稳定复现）

静态根因：`Styles.axaml:160` 应用级 `Button.primary { Foreground = PlusBrushTextOnAccent }`，而 `PlusBrushTextOnAccent` 的颜色 = `PlusTextOnAccent` = **#ffffff**（`LightTheme.axaml:43` / `DarkTheme.axaml:43` / `Plus.Resources.axaml:43` 三处都是白）⇒ 控件前景**与变体无关恒为白**；断言却比池角色 `md3.on-primary`（Dark 下 `#36275d`）⇒ **只有池处于浅色时才可能通过**。池变体由全局可变字段 `ThemeManager.IsDarkMode` 决定（`ThemeManager.cs:16`；`Md3ControlThemeTests.cs:39 Host()` → `ColorPool.Initialize(..., ThemeManager.IsDarkMode)`）。本机启动默认 **Dark**（用户偏好）。

四次实验（同一 `72edf3de` 树、`--no-build`、详细日志）：

| # | `--filter` | 结果 |
|---|---|---|
| A | `FullyQualifiedName~Md3ControlThemeTests` | **FAIL** exit 1：`Expected: #ff36275d / Actual: White` |
| B | `~GradientBrushProbeTests\|~Md3ControlThemeTests` | **FAIL** exit 1（该类仍先跑） |
| C | `~Md3ColorPoolTests\|~Md3ControlThemeTests` | **FAIL** exit 1 |
| D | `~ThemeContractTests\|~Md3ControlThemeTests` | **PASS** exit 0（前置主题用例把池置回浅色） |
| 全量 | 无 filter | **PASS 429/0**（2 次） |

⇒ **"进程全局主题状态 + 执行顺序"依赖型 flaky：与用例内容无关、与运行方式有关**；单跑必红、全量恰好绿。修复方向正确；对照组可用新增的 `OPENUTAU_TEST_THEME=Dark|Light` 在第二轮复核。

### H. 第一轮"未验证 / 需用户实机确认"清单

1. Tab 焦点环在 ToggleSwitch / Slider / ProgressBar 上的有/无（B11）——第二轮复核。
2. B10 菜单勾号列的**实图**（未能打开含 `.menu` 勾号的菜单）。
3. R6 中钢琴卷帘搜索栏 RadioButton 的**实图**（静态结论已足以定"无回归"）。
4. ProgressBar 在**实机**的确定态/不定态（偏好页无进度条；触发导出/下载才上屏）——仅 headless 契约测试覆盖。
5. 播放中调参**音频侧**实时可听（我未接音频播放）——Core 侧由 `MixFxSourceTest` 覆盖，端到端交用户实机。

## 6.2 第二轮：集成树 `a3a1fd5f` / `ec85d3f8`（增量复检 + D1 严协议）

### A. 我的独立测试复跑（`a3a1fd5f`；**参考值**，见污染说明）

| 运行 | 变体 | 结果 |
|---|---|---|
| 全量默认 | （默认） | **431 通过 / 0 失败**，exit 0，2m46s |
| 全量 Dark | `OPENUTAU_TEST_THEME=Dark` | **431 / 0**，exit 0，2m14s |
| 全量 Light | `OPENUTAU_TEST_THEME=Light` | **431 / 0**，exit 0，2m13s |
| 单跑 `Md3ControlThemeTests` | Dark | **5 / 0**，exit 0 |
| 单跑 `Md3ControlThemeTests` | Light | **5 / 0**，exit 0 |

**污染说明（诚实披露）**：Lead 在 14:21 前后对同一棵树跑 `dotnet build`，与我的 `dotnet test --no-build` 撞了文件锁（MSB3027/MSB3021）⇒ `bin` 可能只更新了一部分。我的 5 次运行全部 exit 0、计数一致，未观察到异常，但**口径以 Lead 的干净重跑为准**：`ec85d3f8` + `-t:Rebuild` → 默认 **433/0**、Dark **433/0**、Light **432/1**（唯一失败 `MixerTrackStripTest.RepeatedRefresh_StillSendsSinglePanNotification`，Lead 已定性为**测试隔离问题**：该用例监听全部 `PanChangeNotification` 并断言恰好 1 条，而 fx-ui 新用例构造 `TrackHeaderViewModel` 会写回 Volume/Pan 发出全局通知 → 全量并行时偶发计数 2；**非产品缺陷、非本轮引入**，fx-ui 领工修）。

**flaky 测试对照（有效结论）**：阶段二第一轮那条"单跑必红"的选区 `Md3ControlThemeTests` 在 Dark/Light 下**都通过**（5/5），配合 Lead 的 Dark 全量 433/0 ⇒ **T8-A/T8-B 把 flaky 钉死了**：断言不再依赖进程当前的全局主题状态。

### B. 机架观感未回退 + 主按钮文字色（**新发现：T8-A 未达效**）

- **三面板无回退**（已验证）：`r2-rack-dark-normal.png` —— EQ/压缩器/混响三卡、曲线屏、旋钮组、预设、开关、文案与第一轮一致；`轨道/电源/干声/预设库/确定/取消` 全为中文键值，无 `mixfx.*` 原文。
- **hover 反馈符合预期**（已验证，像素级）：`r2-ok-button-normal.png` vs `r2-ok-button-hover.png`，同一 pill 区域内填充色 **`(207,189,254) → (192,174,235)`** = 亮度 ×0.928 ≈ `Opacity 0.92`（T8-A 的"8% 加深"）✔；hover 图里**看不到任何辉光/BoxShadow 光晕** ✔（旧 `PlusGlowPrimary` 已移除）。
- **❗主按钮文字色：T8-A 的修复没有到达渲染层**（已验证，像素级 + 源码根因）
  - 实测（`确定性`）：`确定` 按钮 pill 填充 = **(207,189,254) = `md3.primary`（Dark 池 #D0BCFF）**；pill 内**文字像素 = (230,224,233)**，全 pill 内**没有任何 lum<150 的像素** ⇒ 文字色是 **`md3.on-surface`（Dark #E6E1E9）**，**不是** 主题里的 `md3.on-primary`（Dark #36275D）。对比度 ≈ **1.28:1**（白字压浅紫，仍不可读）。
  - 根因链（两处叠加，均为**既有**写法）：
    1. `Styles/Md3ControlThemes.axaml:43-47`：Button 模板的 `ContentPresenter` **没有 `Foreground="{TemplateBinding Foreground}"`** ⇒ Button 的 `Foreground` 传不到内容；
    2. `Styles/Md3InputThemes.axaml:119-122`：应用级 `TextBlock { Foreground = TextFillColorPrimaryBrush }`，而 `Colors/Brushes.axaml:26` 把它绑到 **`md3.color.on-surface`** ⇒ 生成的 TextBlock 拿到显式样式值（优先级高于继承），所以**按钮文字恒定 = `md3.on-surface`**。
  - 影响面：**所有 `Button` 的文字色都无视 `Button.Foreground`** ⇒ 主题里 base `md3.primary`、`^.primary` 的 `md3.on-primary`、`^.danger` 的 `md3.on-error` 在渲染层全部无效（`.primary`/`.danger` 的文字色只被 headless 契约测试按**属性**断言，所以测试绿而肉眼可见的问题仍在）。
  - 建议修法（两行级）：① 模板 ContentPresenter 加 `Foreground="{TemplateBinding Foreground}"`；② 同时把主题 base 的 `Foreground` 从 `md3.primary` 改成 `md3.on-surface`（否则普通按钮文字会变紫 —— 这正说明现在这个 base 值也是死写）。改完必须**重新目视** primary/danger 按钮（契约测试断言的是属性，截图才是判据）。
- **danger 按钮**：全仓（XAML + C#）**无 `Classes="danger"` 用法** ⇒ 真机无实例，仅由 headless 契约测试覆盖（**未验证（无实例）**）。

### C. Tab 焦点环（T8-C）—— 部分已验

| 控件 | 结论 | 证据 |
|---|---|---|
| 机架开关（`ToggleButton.md3switch`，fx-ui 的环） | **可见（有）** | UI Automation 逐步定位焦点（tab1=导出缩混时应用效果、tab3=电源），对焦点控件裁 4×：**未勾选态**的 `电源` 轨道盒出现 **primary 描边 2px** + Avalonia 白框焦点提示（`r2-focus-switch-3-zoom.png`）；勾选态因轨道填充本身就是 primary，同色描边不可辨（`r2-focus-switch-1-zoom.png`）——属预期，非缺陷 |
| 机架 Tab 序（顺带） | 已验证 | `导出缩混时应用效果 → 应用默认设置 → 电源 → 预设库 ComboBox → 保存... → 取消 → …`（模块电源开关在 DockPanel 的 Bottom 之后，位于末段） |
| 偏好页 ToggleSwitch / Slider（fx-ctl 的环） | **未验证** | 偏好页是主窗内 overlay，UI Automation 把焦点一律报成 `MainWindow`（Tab 14 次探针全部如此）⇒ 无法确定焦点控件；**仅代码审查**：`e6941ea7` 给 ToggleSwitch/Slider 加了 `PART_FocusRing` + `:focus-visible`（Slider 的环放在轨道上，注释说明"ControlTheme 选择器只允许一次 `/template/` 穿越"），另有 headless 契约用例 |
| ProgressBar（fx-ctl 的环） | **未验证（设计上不可达）** | 模板注释明确"进度条默认 `Focusable=false`（非交互指示器，不该进 Tab 序），视图显式打开 Focusable 时此环生效"；偏好页无进度条实例 |

### D. D1 严协议（非空工程 + ≥65 s + 对照组）—— **PASS（已验证）**

工程：`新建` + 顶栏「添加轨道」→ **2 轨**（`r2-02tracks.png`）⇒ 绕开 `ChangesSaved` 的"≤1 轨且 0 片段"豁免。

**对照组**（开机架、**不改任何参数**、点标题栏 X 关窗）：
```
14:42:40.403 Autosave …\Backups\Untitled-autosave.ustxp.   ← 加成轨道（可撤销命令）落盘，作为基线
14:43:10.562 / 14:43:40.573 / 14:44:10.584 / 14:44:40.591 / 14:45:10.600 / 14:45:40.610  Autosave skipped.（6 次）
```
⇒ 不动参数 = 不标脏 ✔（6 个 tick 全部 skipped，跨越 >150 s）。

**实验组**（开机架 → 拖 EQ「低频」旋钮到读数变化 → 点标题栏 X 关窗）：
```
14:46:10.610 Autosave skipped.        ← 编辑发生在此 tick 之后
14:46:40.611 Autosave …\Backups\Untitled-autosave.ustxp.    ← 下一个 tick 立即落盘（非 skipped）
14:46:40.620 Autosaved …
14:47:10.624 Autosave skipped.
```
- autosave 文件：**5042 B @14:42:40 → 5564 B @14:46:40**，内容含 **`mix_fx:` + `eq_low_db: 7.199999999999999`**（正是我拖动后的值）+ `eq_enabled: true` ⇒ **编辑进模型 → 标脏 → 落盘**全链成立 ✔（比只看日志行更强，满足 Lead 追加的判定）。
- **退出提示**：随后点主窗关闭按钮 → 出现「退出 UTVTU / **当前工程有未保存改动。是否保存？** 是·否·取消」（`r2-d1-exit-prompt.png`）✔ ⇒ `ChangesSaved == false` 生效。
- **异常扫描**（Lead 追加的诊断）：编辑时刻附近**无 `[ERR]/[FTL]/[WRN]/Exception`**；全会话唯一 ERR 是 `Failed to scan samples directory …\Samples`（缺 Samples 目录，环境问题，出现于每次启动）⇒ 排除"`BuildUMixFx()` 抛异常导致 `dirty=true` 不执行"这条假说 ✔。
- **结论：D1 在真机 PASS**（标脏 + 落盘 + 退出提示三项齐备）。进程内的 `MixFxDialogTests.cs:267 EndToEnd_RackEdit_MarksProjectUnsaved_ViaDocManager`（真实 2 轨 + 对照组）与此一致。

### E. **第一轮 D1 结论作废的原因（自查 + Lead 复核）**

第一轮"16 次 skipped ⇒ 标脏未发生"的推理有两个漏洞，我在第二轮用时间线对上了：
1. **空工程掩盖后果**：`DocManager.cs:133` 的 `ChangesSaved` 在"≤1 轨且 0 片段"时恒真 ⇒ 第一轮"退出无提示"完全不能作为证据（已作废）；
2. **观察窗口错位（关键）**：第一轮我对机架的最后一次编辑 + 点 X 关窗发生在 `14:10:5x–14:11:0x`，而**下一个 30 s tick 是 14:11:10 —— 应用在 14:11:12 就退出了**，tick 根本没跑到 ⇒ 那次"没有 Autosave"是**必然**的，与标脏无关。更早的编辑（14:04–14:08）则因为**当时机架一直开着、从未走"关窗"路径**（第一次关窗走的是 ESC/Revert，按设计不标脏）而同样不会标脏。
⇒ 第一轮的观察设计有缺陷，结论作废；第二轮严协议（对照组 + 非空工程 + ≥65 s + 内容比对）给出 PASS。

### F. 第二轮"未验证 / 需用户实机确认"

1. **偏好页** ToggleSwitch / Slider 的 Tab 焦点环（overlay 内 UIA 不暴露焦点；仅 static + headless）。
2. **ProgressBar** 的焦点环（默认 `Focusable=false`，设计上不进 Tab 序）。
3. **danger 按钮**视觉（全仓无实例）。
4. **主按钮文字色修复后**的效果（本轮实测仍是 `md3.on-surface`；修 ContentPresenter 的 Foreground 转发后需再看一眼）。
5. 播放中调参的音频侧实时可听（我未接音频播放；Core 侧由 `MixFxSourceTest` 覆盖）。

## 6.3 证据说明（诚实披露）

- 第一轮机架的**深色**截图（三面板 / 旋钮联动 / 模块变暗）当时保存在 `shots\shot-pid8696-hwnd2294562.png`，随后被同路径后续截图**覆盖**；磁盘上现存的是 Light 版本。上文 **sha256 是我读图时该文件的实际哈希**，作为"当时确实看到该画面"的记录；第二轮以不覆盖的文件名重建深色机架证据。
- `prefs-appearance-tab5.png` 与 `prefs-appearance-notab.png` 的差异肉眼不可辨，与"焦点控件未知"一致 ⇒ 不作为结论依据。
- `Light` 主题下机架曲线屏一度显示为深色，经**强制重排后立即变浅色** ⇒ 判定为"窗口最小化→恢复"的**陈旧重绘**，**不是缺陷**（对照 `rack-light.png` vs `rack-light-repaint.png`）。
- `.dsh/fx/shots/` 已被 gitignore，不污染仓库；截图抢过一次前台焦点，收工已关闭我们自己的进程（`stop.ps1` 回显「PID 8696 已不在运行」，且 `Get-Process -Name OpenUtau` 无任何进程）把现场还给用户。

## 6.4 第二轮证据清单（`.dsh/fx/shots/`）

| 文件 | 内容 |
|---|---|
| `r2-02tracks.png` | 非空工程：Track1 + Track2（绕开空工程豁免） |
| `r2-rack-dark-normal.png` | 机架深色无回退（三面板 + 主按钮非 hover 态） |
| `r2-rack-knob-dragged.png` | 拖低频旋钮后的机架（编辑前状态留档） |
| `r2-ok-button-normal.png` / `r2-ok-button-hover.png` | 主按钮 4× 裁剪：文字色 / hover 加深对比 |
| `r2-focus-switch-1.png(-zoom)` / `r2-focus-switch-3.png(-zoom)` | 机架开关 Tab 焦点环（勾选态 / 未勾选态） |
| `r2-d1-exit-prompt.png` | D1：退出时「当前工程有未保存改动。是否保存？」 |
| `r2-rack-tab-focus.png`、`r2-state-check.png`、`r2-mixer-check.png` | 过程记录（Tab 序 / 偏好页 / 混音台面板） |

**收工状态**：`stop.ps1` → 「已关闭 PID 8724（…\UTvTU-fx-core\…\OpenUtau.exe）」；`Get-Process -Name OpenUtau` 无输出（**用户的原版 OU 未受影响**，全程未按进程名操作）。用户偏好主题已还原为 **Dark**（其原值）。产品代码/分支/工作树未被本次验证修改（唯一写入：本报告 + `.dsh/fx/shots/` 截图）。

---

## 6.5 第三轮：`5f3799ae` 像素级复测（T9 文字转发 / T10 对比度收尾）—— **PASS**

> 树：`try/fx-core` HEAD `5f3799ae`，工作树干净，Lead 已 `-t:Rebuild`（exe 2026-10-04 15:23:05）。
> 启动：`launch.ps1` → `PID=15988`，`PATH=…\UTvTU-fx-core\OpenUtau\bin\Debug\net8.0-windows\OpenUtau.exe`（我们的树）✔。
> 方法：截图后**直接在原图取像素**（非放大图），用 WCAG 相对亮度算对比度。

### A. 机架主按钮「确定」（判据项）—— PASS

| 量 | 实测值 | 备注 |
|---|---|---|
| pill 填充 | `#CFBDFE` (207,189,254) = **`md3.primary`（Dark 池）** | 与池角色完全一致 |
| 文字核心色 | **`#36275D` (54,39,93) = `md3.on-primary`（Dark 池）** | 池精确值，n=10（含 `#3A275D` 等 1px 邻域） |
| **对比度** | **7.70 : 1** | 修复前（白字 `#FFFFFF` 压 `#CFBDFE`）= **1.70 : 1** ✔ 与 Lead/T10 报告数字一致 |
| 视觉 | 深紫文字清晰压在浅紫 pill 上 | `r3-rack-dark.png` |

### B. 普通 / 描边按钮未受影响（未变紫、未发灰失控）—— PASS

| 按钮 | 文字核心色 | 对底对比度 | 判定 |
|---|---|---|---|
| `取消`（outlined，底 `#141218`） | `#E6E0E9` = `md3.on-surface` | 14.35 : 1 | ✔ 与修复前一致 |
| `应用默认设置`（plain，底 `#141218`） | `#E6E0E9` = `md3.on-surface` | 14.35 : 1 | ✔ 与修复前一致 |

### C. 机架三面板无回退 —— PASS

`r3-rack-dark.png`：EQ / 压缩器 / 混响 三卡、曲线屏、旋钮组、模块开关、预设、顶/底栏与中文文案（`轨道/电源/预设库/确定/取消`，无 `mixfx.*` 原文）与第二轮一致；实色无玻璃。

### D. 导出窗口主按钮（`RenderWindow.axaml` `.renderBtn`）—— PASS

打开路径：品牌菜单 →「文件 → 导出音频 → 渲染...」（**注意 `Ctrl+Shift+R` 未生效**：`InputGesture` 只作菜单显示，未绑 KeyBinding；窗口标题 `渲染...`）。

| 量 | 实测值 |
|---|---|
| `开始渲染` pill 填充 | `#CFBDFE` = `md3.primary` |
| 文字核心色 | **`#36275D` = `md3.on-primary`**（n=129，主簇） |
| **对比度** | **7.70 : 1**（旧白字 1.70:1）✔ |
| `打开文件夹`（secondaryBtn） | `#E6E0E9` = `on-surface`，CR 14.35:1 ✔ |
| 同窗 RadioButton 文案 | `#E6E0E9` = `on-surface` ✔（新 RadioButton 主题在外） |

### E. `ListBoxItem` 选中行（Light）—— 文字色 PASS，**局部对比度登记为清理项**

对象：`视图 → 项目 → 表情...` 的表达式列表（`Views/ExpressionsDialog.axaml:30` 的 ListBox，首行 `dynamics (curve)` 默认选中）；主题切到 **Light**（`r3-prefs-light.png`）。

| 量 | 实测值 | 判定 |
|---|---|---|
| 选中行**文字核心色** | **`#4D4458`** ≈ `md3.on-secondary-container`（池值 `#4A4458`） | ✔ **T10-B 转发生效**（修复前无论是否选中恒为 `on-surface` `#1D1B20`） |
| 选中行主题胶囊底 | `#E8DEF8` = `md3.secondary-container` | ✔ |
| 普通行文字 | 近 `#1D1B20` = `on-surface`（对 `#F2ECF4` 底 ≈14.7:1） | ✔ |
| ⚠️ **文字所在处的实际局部底色** | **`#A699C3`**（比主题胶囊更暗的一层） | **局部对比度仅 3.49:1**（不是契约测试按池色算的 7.19:1） |

**根因（静态可证）**：`Styles/Styles.axaml:303-305` 仍保留应用级补丁
`ListBoxItem:selected /template/ ContentPresenter { Background = AccentBrush1Semi }` ——
它在 Md3ListBoxItemTheme 自己的 `secondary-container` 胶囊**内部**又刷了一层半透明强调色，于是文字实际压在 `#A699C3` 上。这与阶段一 B9/B10 记录的"应用级 `/template/` 补丁与主题双重绘制"是同一类问题，属 T9/T10 同类清扫的**残余一处**。证据：`r3-listbox-light-zoom.png`（5× 放大：外圈浅紫胶囊 + 内层深紫块 + 深靛灰文字）。
**结论**：文字色本身已正确（可读、观感柔），但**对比度低于 WCAG AA 正文 4.5:1**；建议作为独立清理项（删掉 `Styles.axaml:303-305` 一条即可，主题已有 `:selected` 配色）——不阻塞本轮，**仅登记**，是否需要修由 Lead 裁决（属 fx-ctl 写入范围，我未改动）。

### F. 收工与证据

- `stop.ps1` → 「已关闭 PID 15988（…\UTvTU-fx-core\…\OpenUtau.exe）」；`Get-Process -Name OpenUtau` 无输出 ✔（用户原版 OU 未受影响）。
- **主题已还原为 Dark**（用户原值），数值核验：`r3-dark-restored.png` 的背景像素 `#141218` / `#1D1B20`（暗色池 surface）✔。
- 第三轮证据（`.dsh/fx/shots/`）：`r3-rack-dark.png`（机架 Dark，无回退）、`r3-renderwindow.png`（导出窗口主按钮）、`r3-prefs-light.png`（Light 生效）、`r3-expressions-light.png` + `r3-listbox-light-zoom.png` / `r3-listbox-light-normal-zoom.png`（选中/普通行对照）、`r3-dark-restored.png`（主题还原）、`r3-menu-file.png` / `r3-menu-export.png` / `r3-menu-project.png`（菜单导航留档）。
- **过程教训（记入流程）**：机架是 `Show(owner)` 的非模态窗且覆盖偏好页内容区，我第一次切换主题的点击落在机架内部，**误把机架「压缩器」预设切成了 Off**（`r3-menu-project.png` 可见）。该改动**未保存、未落盘**（随后用 `stop.ps1` 强关，未写任何工程文件）。后续自动化：**点击遮挡区域前先最小化其它窗口**（本轮已改为先 `SW_MINIMIZE` 机架再操作偏好页）。
