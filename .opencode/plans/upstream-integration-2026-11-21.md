# 上游核心与体验更新整合（2026-11-21，已完成）

用户明确授权“都加上都加上”。基于 `cc111206`、上游增量范围 `a9547895`（`0.1.577.2-alpha`）→ `08222cb7c48c4ab47faa34a6efc34e2c00898a1e`（`0.1.577.11-alpha`），共审查 34 项上游提交，**全部 20 项适用更新已按 UTvTU 本地架构适配落地**，14 项非 Windows / 已重构替代 / Crowdin 全量覆盖项按既定策略跳过。G 盘工作区、`plus-develop`、`origin` 与 `upstream` 均已核实，`master` 未动。

## 产品提交（6 个原子提交）

1. **`8d7b7fc5` 核心小修与多语种音素器**（`14aa2053` + `b6df0744` + `e26be813` + `6f9260e5` + `652d264b` + `f4030be5` + `942be21d`）
   - Windows DirectML 通过 `D3DKMT` 排除间接显示虚拟显卡，并保护 `devices.Count == 0` 时的空回退；新增 `WindowsGpuDetectorTest`。
   - MusicXML 导入跟随小节内 `divisions` 变化、忽略无 `tempo` 的 `<sound>` 元素。
   - 西班牙语 VCCV 音素器 `ccv/cccv/_cc_` 修复；`VoicebankPublisher` 严格规范 `SingerType`。
   - 新增 DiffSinger 芬兰语/乌克兰语音素器与 `UkrainianG2p`（含 `g2p-uk.zip`）及中英文语言键。
2. **`5e836d63` DiffSinger 精度对齐与预渲染挂起优化**（`70952206` + `1788a9f6` + `7461b699`）
   - `DiffSingerBasePhonemizer` 词时长预算改为累计取整；`DiffSingerUtils` 将尾部 `SP` 独立成词，与 `.ds` 导出共享 `ApplyVarianceDelta` 方差截断。
   - 通过 `RenderEngine.CancelPreRender()`（`RenderCancellationLease.TryRetire`）与 `PlaybackManager.HoldPreRender()` 保持现有播放/合成静态门面契约，在 `LoadRenderedPitch`、`RefreshRealCurves` 与 `PhonemizerRunner` 运行期间挂起后台预渲染，并将批量写音高合并为单条 `MergedSetCurveCommand`；`UPart.Validate` 重复音素索引检查改为线性 `HashSet`。
3. **`15397e81` 无歌手轨道正弦波回退发声**（`a7318ec6` + `ce24f9e2`）
   - 新增 `SineRenderer`（适配本地 `IRenderer` 签名），支持按音符独立发声、延音链合并、PITD/DYN 曲线及 `PhraseSource` 后台快照；在平面设置页（`PageGeneral`）新增开关。
4. **`fba4a305` 钢琴卷帘编辑交互 & MD3 歌手选择器增强**（`0863a23d` + `f920e242` + `2005b967` + `70ec00af` + `637f5d3e`）
   - 钢琴卷帘支持按住 `S` 键在鼠标悬停位置分割音符（适配 UTvTU `DocumentEditGesture` 及 `CancelActiveInput` 清理）；音符右键菜单新增「合并音符（`Ctrl+U`）」；恢复曲线笔工具按住 `Shift` 绘制水平直线。
   - 在 UTvTU 的 MD3 虚拟列表 `SingerPicker` 中接入按 `SingerType` 筛选（仅多类型时显示）与「移除歌手」按钮。
5. **`b1a68ead` 轨道片段乐句级渲染进度条**（`bc8b914f` + `02fc0ab9`）
   - 经 `RenderNotificationPublisher` 在预渲染与播放渲染时投递 `RebuildPhraseBlocksNotification` 与 `PhraseRenderStateNotification`；在 `PartControl` 中基于 MD3 动态 `ArrangementVisuals` 以半透明未渲染底色 + 实色已渲染乐句块呈现（含高 DPI `+0.5` 缝隙修复与状态变更检测），修复上游同轨多片段仅重置首片段的 bug；在 `PageAppearance` 接入开关。
6. **`6bd7c3fa` 自定义界面字体**（`be676d86`）
   - 新增 `UiFontManager`，对接 UTvTU 的 `PlusFontFamily` / `ui.fontfamily` 资源，未设置时完全保持默认 HarmonyOS Sans SC，设置后仍保留 HarmonyOS Sans SC 字形回退链；支持 `.ttf`/`.otf` 文件选择、系统字体族与「在音符与音素文字中使用界面字体」开关。

## 跳过项说明（14 项）

- **非 Windows 专属（5 项）**：`635adfda`（Linux README）、`b08770d9`（macOS WebGPU EP）、`b7c35b90` / `c5a847be`（WineRunner）、`e67fc470`（Linux MetaInfo）。
- **本地无对应旧代码或已由 MD3 重构替代（4 项）**：`cf294b8e`（回退上游 `#2388`，本地未引入）、`c46bfc8d`（上游横向 `SingerFlyout` 滚轮宽度）、`167f0173`（上游 DiffSinger 无包络扁平音素条边框）、`a2dadf8f`（上游旧 `PianoRoll` 按钮重叠）。
- **多语言批量导入与 `uk-UA` 注册（5 项）**：`287400c3`、`6d9f58cc`、`08222cb7`、`ad8e6fd2`、`04a4de55`（避免冲刷 UTvTU 自定义中英文词条，所有新增功能词条已精准补齐中英文）。

## 构建与验收证据

- **SDK 与环境**：本机已升级至 **SDK 9.0.321**（已更新 `.dsh/packaging/sdk9/global.json` 并完成还原）；单节点 / 2 处理器 / 768 MiB 堆。
- **构建结果（最终 commit6 全量 4 项目）**：
  - `OpenUtau.Core`：0 错 / 1201 警告（27.43s）
  - `OpenUtau.Plugin.Builtin`：0 错 / 583 警告（14.73s）
  - `OpenUtau`：0 错 / 3 既有 AVLN3001 警告（49.64s）
  - `OpenUtau.Test`：0 错 / 153 警告（26.81s）
- **产物 SHA256 一致性（主程序 Debug 输出 ↔ 测试输出全部一致）**：
  - `OpenUtau.dll`：`A9D07AEFE280F41320019C7C43AE822FD1DA67F039548E91D2E084FC2503E2DC`
  - `OpenUtau.Core.dll`：`6756DA950966424A4DF3571A76E1CCA018C74862787830DE5BA838619AA2828C`
  - `OpenUtau.Plugin.Builtin.dll`：`418C06CD94AC0688FFC182D5ABFFCCF9C035EA2E9981E977FD2E7130255F5B93`
- **回归测试结果（共 71 个不同用例全绿，无跳过）**：
  - 核心组 35/35（5.81s）：`WindowsGpuDetectorTest`（2）、`MusicXMLTest`（11）、`DiffSingerWordDivTest`（3）、`DiffSingerVarianceDeltaTest`（3）、`PitchWriteEquivalenceTest`（2）、`RenderCancellationLeaseTests`（7）、`SineFallbackTest`（7）。
  - UI / 应用组 36/36：`PartRenderProgressTests`（1）、`PartRedrawScopeTests`（2）、`PreferencesViewTests`（6）、`PreferencesFormAndWelcomeMotionTests`（5）、`SingerPickerTests`（5）、`PitchToolInputTests`（9）、`UiFontManagerTests`（2）、`WorkspacePageLayoutTests`（6）——（全部通过）。
- **未执行范围**：未启动本轮主程序、未自截图、未做真实声库听感/GPU 压力测试、未重打正式安装器、未推送至 GitHub。UI 观感（轨道进度条透明度对比、歌手选择器类型筛选胶囊、自定义字体设置项与实际字体切换）交由用户实机验收。
