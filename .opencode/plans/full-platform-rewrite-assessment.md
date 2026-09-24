# 全平台重构评估（2026-09-21）

> 背景：用户考虑"弃用 .NET/Avalonia 12 并重构前端"，理由是"暂时无法做到真全平台"。
> 本文基于对 UTvTU 仓库的实测盘点（规模/平台耦合/既有文档）与候选栈事实核验，给出结论与路线建议。
> 数据来源与核验时间见文末"附录"。

---

## 0. 结论（TL;DR）

1. **"不能真全平台"的锅不在 Avalonia，而在两个具体缺口**：
   - **HarmonyOS NEXT：整个 .NET 生态都没有官方运行时**——换 UI 框架是必要非充分条件；
   - **移动端（Android/iOS）：Avalonia 官方有平台支持，但本项目零适配**（无触摸 UI、无移动音频/打包）。
2. **不建议推倒重来**。Core 约 **9.8 万行**（占代码量 2/3）是纯逻辑资产（音素化器 2.7 万行、USTX 模型/格式解析、DiffSinger 推理），且已有 win/linux/osx 三平台原生 worldline 库与跨平台 miniaudio 音频；重写 Core 是 1-2 人年量级，收益为负。
3. **按目标分叉**：
   - 只要**桌面三平台（+ARM64）**：**不重构**。修 3 处无守卫的 Windows 耦合 + ARM64 库路径 + VST 平台标记，2-4 周可出 mac/Linux 版本（CI 矩阵已存在）。
   - 要 **HarmonyOS/移动端完整编辑**：应把这件事当 **"移动端新产品立项"**，而不是"前端重构"。一套 UI 覆盖含 HarmonyOS 的栈目前只有 **Flutter（OpenHarmony SIG 分支）** 路线可走；引擎必须 C++/FFI 化，UI 全重写，DiffSinger 可否上 OHOS 需先 PoC。
   - 想保留 .NET 又想要移动：先做 **Avalonia Android/iOS PoC**（官方支持 + 项目已有 NNAPI/`OS.IsAndroid` 分支遗产），用 2-4 周验证成熟度，再决定是否换栈。

---

## 1. "真全平台"缺口定义

先明确目标矩阵（**待用户确认**）。按当前代码事实逐平台核对：

| 目标平台 | 现有 .NET/Avalonia 栈 | 缺口 |
|---|---|---|
| Windows x64/x86/ARM64 | ✅ 能构建运行（主线） | — |
| macOS x64/ARM64 | 🟡 代码/原生库就绪（worldline dylib、CI 矩阵），未发版验证 | 3 处 Windows 耦合需修；VST 桥仅 win-x64；打包未做 |
| Linux x64/ARM64 | 🟡 同上（so 产物齐） | 同上 + 系统依赖/打包 |
| Android | ⚪ Avalonia 官方支持（官网平台页有 iOS & Android），项目零适配 | 触摸交互 UI、音频输出、打包、生命周期 |
| iOS | ⚪ 同上 | 同上 + 苹果分发限制 |
| Web/WASM | ⚪ Avalonia 有 WASM 后端（官网平台页有），项目未用 | 音频延迟/文件系统/VST 无、性能 |
| **HarmonyOS NEXT** | ❌ 无官方 .NET 运行时；Avalonia 无支持 | **整个运行时换掉才可能** |

> 注：Avalonia 12 官方平台页列出 Windows/macOS/Linux/iOS & Android/WASM。也就是说"桌面→移动→Web"在 Avalonia 内其实有官方路线；**唯一彻底出局的是 HarmonyOS**。

---

## 2. 现状盘点（实测数据）

### 2.1 代码规模

| 项目 | 文件数 | 行数 | 说明 |
|---|---:|---:|---|
| OpenUtau（UI 层） | 124 cs + 79 axaml | 27,981 + 21,039 = **49,020** | Views/ViewModels/Controls/Styles/Strings（Strings 13,057 行可复用） |
| OpenUtau.Core | 239 | **70,643** | Ustx/Render/SignalChain/Audio/Vst/Classic/DiffSinger… |
| OpenUtau.Plugin.Builtin | 60 | **27,387** | 音素化器插件，纯逻辑 |
| OpenUtau.Test | 54 | 4,874 | 187 个测试方法 |
| **合计** | 477 | **~152,000**（含 axaml） | UI 占 32%，Core+Plugin 占 65% |

原生资产：worldline 六份产物（win x64/x86/arm64、linux x64/arm64、osx x64/arm64 合一目录）；`vst_bridge.dll` **仅 win-x64**；VST3 SDK 源码 243 MB（本地 vendored、未入库）。

### 2.2 已经就位的跨平台基建（被低估）

- `OpenUtau.Core/Audio/MiniAudioOutput.cs` + worldline（miniaudio）已是音频输出主路径，三平台原生库齐备；NAudio 仅解码/重采样（跨平台部分）。
- ONNX 执行提供器分支完整：Windows→DirectML、macOS→CoreML、Android→NNAPI、其余→CPU（`OpenUtau.Core/Util/Onnx.cs:34-54`）。
- 路径管理三平台：macOS `~/Library`、Linux XDG、Windows 文档目录（`PathManager.cs:16-64`）。
- Wine 支持是一等公民：Linux/mac 可跑 UTAU 经典 exe 引擎（`ExeResampler.cs:69-71`、`ExeWavtool.cs:29-31`）。
- CI 已覆盖 windows/macos-intel/macos-arm/ubuntu（`.github/workflows/pr-test.yml`），worldline 原生构建三平台流水线独立。

### 2.3 真正的硬阻断（不重构也能修，且很小）

| # | 位置 | 问题 |
|---|---|---|
| 1 | `PlaybackManager.cs:443/454` | 试听用 `WaveOutEvent`（NAudio.WinMM），无守卫 |
| 2 | `PlaybackManager.cs:691` | 录制导出用 `WasapiOut`，无守卫（非 Windows 编译失败） |
| 3 | `MidiTonePlayer.cs:18-24` + `PlaybackManager.cs:386` | winmm `DllImport` 且无条件实例化（Linux/macOS 启动即崩） |
| 4 | `LibraryLoader.cs:21-33` | 库路径只写 linux-x64/osx-x64，ARM64 缺口 |
| 5 | `VstPluginRegistry.cs:313-321` | VST 扫描目录写死 Windows |
| 6 | VST 编辑器宿主 | C++ HWND + `VstThread.cs` 消息泵，Windows 专有；mac/Linux 需原生窗口宿主重写（或 VST 标注 Windows-only 特性） |

> 1-3 修掉，Linux/macOS 主程序即可构建运行——**这三处也是 README 里"macOS/Linux 支持 🚧"的实质内容**。

### 2.4 与上游的关系

`plus-develop` 相对 `upstream/master`：**领先 268 / 落后 153 提交，42 处冲突**（merge-base 2026-08-01）。上游已重写音频管线（frozen slot planner、SDL3 后端）。**任何重写 = 永久放弃上游合并**；考虑 UTvTU 更名/撤包已在推进，"脱钩"可能正合意图，但代价要写进决策。

---

## 3. 重写的代价结构

### 3.1 如果 UI 换栈（不换 Core 语言）

新 UI 不可能直接调用 C# Core（除桌面 NativeAOT，移动端不支持）。因此真正的架构是：
```
新 UI ⇄ FFI ⇄ 原生引擎（worldline + 数据模型 + ONNX）
```
需要 C++/Rust 化或重写的部分至少包括：
- USTX 工程模型与编辑命令（Core 中 Ustx/Commands/Editing，约 1 万行）
- 渲染管线/导出/信号链（Render/SignalChain/Export，约 5.5 千行，依赖 worldline C ABI）
- 若保留 DiffSinger：ONNX 会话管理与 13 个推理器（移动端 EP 可行性待验证）
- 经典 UTAU 兼容（exe resampler/wavtool）：移动端只能放弃

**量级判断**：这是 2-4 万行新代码 + 与现有 C# 侧的长期双轨维护，远超"前端重构"。

### 3.2 如果只重写 UI（保留桌面 C# 生态）

仅当目标平台仍在 .NET 覆盖内才有意义（如 Avalonia→MAUI/其它 .NET UI）——对"全平台"问题没有帮助，排除。

### 3.3 工程量粗估（主观，仅供参考）

| 场景 | 内容 | 粗估（单人 + AI 辅助，全职等效） |
|---|---|---|
| 桌面三平台可用 | 修 6 处耦合 + 打包 + 验证 | **2-4 周** |
| Avalonia 移动端 PoC | 触摸 UI 骨架 + 音频 + 一个渲染链路 | **2-4 周**（验证成熟度，非产品） |
| Flutter(OHOS) UI 重写 | 49k 行 UI + 交互重构 | **3-6 个月**（桌面功能对等），移动适配另加 |
| 引擎 C++/FFI 化（含模型/渲染/导出） | 2-4 万行 | **3-6 个月**（与 UI 并行可压缩） |
| 完整移动端产品（含 DiffSinger） | 上述 + ONNX-OHOS + 声库管理 | **6-12 个月+** |

---

## 4. 候选栈对比（事实已核验）

| 栈 | 桌面 3 | Android/iOS | HarmonyOS | 结论 |
|---|---|---|---|---|
| **保留 Avalonia 12** | ✅ | ✅ 官方（官网平台页含 iOS & Android），项目零适配 | ❌ 无 .NET 运行时 | 桌面最优；移动可试 PoC；HarmonyOS 无解 |
| **Flutter** | ✅ | ✅ | ✅（OpenHarmony SIG fork：`openharmony-sig/flutter_flutter`，已迁移 gitcode；支持 `flutter build hap`；生态库部分移植） | **唯一一套 UI 覆盖 HarmonyOS 的成熟路线**；全 UI 重写 + FFI 引擎 |
| **Compose Multiplatform** | ✅ JVM | ✅（iOS stable） | ⚠️ 非官方（Kotlin/Native 官方 target 表无 ohos） | HarmonyOS 上不要指望 |
| **Tauri v2 / Rust + Web** | ✅ | ✅ | ❌ | 复杂 DAW 级 UI 用 Web 技术仍是重写；HarmonyOS 缺席 |
| **Qt 6 / QML** | ✅ | ✅ | ⚠️ 无官方（Qt Wiki 页面仅有 OpenHarmony 简介） | 移动端体验与开发效率不占优 |
| **Electron** | ✅ | ❌ | ❌ | 排除（体积/移动） |
| **ArkTS/ArkUI 原生** | ❌ | ❌ | ✅ 一等公民 | 只能覆盖鸿蒙，等于双端双份 UI |

**引擎侧事实核验**：
- miniaudio：OHOS 后端 issue（mackron/miniaudio#1066）已于 2026-01 关闭为 completed——**音频输出有戏，需按当前版本实测**。
- ONNX Runtime：官方无 OpenHarmony 构建（microsoft/onnxruntime#20895 被 stale 关闭），仅有社区预编译件（GitHub `icehomura/ohos-onnxruntime-libs`，2026-09）。**DiffSinger 上鸿蒙是最大不确定项**。
- worldline 已是 C++（`cpp/` 3,245 行），加 OHOS 构建目标比重新实现合成引擎现实得多。

---

## 5. 路线建议（明确推荐）

### 推荐 A：目标 = 桌面三平台/ARM64（不含 HarmonyOS）
**不重构。** 做"跨平台收尾"：
1. 修 §2.3 的 1-4、6（VST 可先标 Windows-only，后续再补 mac/Linux 宿主）；
2. mac/Linux 打包与实机验证（已有 CI 矩阵与 CFBundle 元数据）；
3. 把 VST/上游合并策略写进 ADR。
> 收益：几天到几周拿到"真桌面全平台"，保住 15 万行资产与上游修复通道。

### 推荐 B：目标 = HarmonyOS（硬需求）
**按"移动端新产品"立项，不做全量重写**，先做垂直切片 PoC（4 周内可判死）：
1. Flutter OHOS 环境打通（DevEco + ohos fork，跑通 HAP）；
2. worldline 编 OHOS `libworldline.so`（miniaudio OHOS 后端）+ FFI 播放一段人声；
3. 最小 USTX 渲染链路（模型子集 + 渲染 + 播放）在 OHOS 跑通；
4. ONNX Runtime 社区件加载 DiffSinger 小模型；失败则移动端砍掉 DiffSinger，只保 Classic/worldline 引擎；
5. 通过后只重写 **UI 层**（Flutter 自绘钢琴窗/混音台），引擎继续用 C++/原生；桌面端保留 Avalonia 不动，两产品共享 `.ustxp` 与原生引擎。
> 若 PoC 失败：移动端退化为"桌面渲染 + 移动预览/播放"配套方案，避免沉没成本。

### 推荐 C：不确定 HarmonyOS，但想要移动
先花 2-4 周做 **Avalonia Android/iOS PoC**（加 `Avalonia.Android`/`Avalonia.iOS` 包、触摸骨架、音频后端）：Avalonia 12 官方支持矩阵已含移动，项目还有 `OS.IsAndroid`/NNAPI 的遗产分支；若成熟度可接受，移动问题不必换栈；不可接受再走 B。

### 无论哪条路线
- **先写 ADR 锁定**：目标平台矩阵、VST 是否桌面-only、是否接受与上游永久分叉；
- **反对大爆炸重写**：以垂直切片验证风险最高项（OHOS 音频、ONNX、复杂 UI 绘制），通过一段迁移一段；
- 顺手清账：THIRD-PARTY-NOTICES 对 VST3 SDK 的许可描述（"专有"）与实际 MIT 许可证矛盾（`runtimes/vst3sdk/LICENSE.txt`），重构时一并订正。

---

## 6. 待用户决策的输入

1. **HarmonyOS NEXT 是硬需求吗？**（决定 A/B/C 分叉）
2. 移动端要**完整编辑**还是**播放/预览/轻编辑**？（完整编辑 = 6-12 个月级；轻量 = 可复用桌面导出，大幅缩短）
3. 是否接受**与上游永久分叉**（目前 153 提交差距，重写后回不去）？
4. 可投入的时间/节奏（是否有发布节点）？

---

## 附录：数据与核验来源

- 代码规模/耦合审计：本仓库实测（`find/wc`、csproj、`docs/AUDIT.md`、grep 平台 API），2026-09-21。
- `docs/AUDIT.md`（2026-07-30）：架构分层、27 项编号问题、四阶段重构（阶段 1 即"跨平台降级"）。
- `.opencode/plans/upstream-audio-synthesis-audit.md`、`audio-pipeline-seam.md`：上游 153 提交领先、音频架构差异、合并需决策且已暂停。
- Avalonia 官网平台页（avaloniaui.net）：官方平台含 Windows/macOS/Linux/iOS & Android/WASM，2026-09-21 核验。
- Kotlin Multiplatform 官方文档：支持平台无 OHOS（Kotlin 2.x 官方 target 表），Compose 稳定面为 Android/iOS/Desktop，Web Beta；2026-09-21 核验。
- Flutter OpenHarmony SIG 仓（gitee `openharmony-sig/flutter_flutter`，已归档并迁往 gitcode）：支持 `flutter build hap`，文档/分支活跃；2026-09-21 核验。
- miniaudio issue #1066（OHOS 后端，2026-01-04 closed completed）；ONNX Runtime issue #20895（OHOS 构建，2025-08 stale 关闭）；社区产物 `icehomura/ohos-onnxruntime-libs`；2026-09-21 核验。
