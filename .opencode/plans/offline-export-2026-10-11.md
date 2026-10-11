# 独立插件离线导出（2026-10-11）

用户认可渲染页后，要求把离线导出作为重要能力开始实现。产品最终提交 `a79f817d`，分支 `plus-develop`，未推送。本记录优先于前一份渲染工作区记录中的默认静音设备导出及旧直接混音入口描述。

## 最终行为

- 渲染页新增处理方式，默认「离线（更快）」，可主动选择「实时兼容」。切换混音/分轨或效果开关不会改写选择，摘要显示选定方式；运行时锁定该字段。
- 含 VST 的离线导出直接消费音频块并写文件，既不创建 WasapiOut，也不替换或停止主播放设备，没有实时节奏等待。最终 WAV 仍使用原子的编码/重采样管线。
- 实时兼容继续保留独立静音设备的消费路径，但同样使用作业独立的插件实例。没有活动 VST 时不需要设备时钟约束，直接消费文件。
- 旧「导出混音」菜单也迁移到默认离线，保留 float32、当前内部采样率、主输出增益语义。旧 RecordMixdown/RecordExport API 签名仍在，显式录制调用保持兼容；旧干分轨入口本身已经离线。
- 不自动回退：插件拒绝离线设置、状态恢复、初始化或处理失败，明确报错并保留上一份文件。用户可以手动选择实时兼容再试。

## 插件和音频链

- `ExportVstSession` 在启动时一次捕获选中轨道的 VST 槽位、当前插件状态与编辑器参数，之后逐文件创建新的 VstEffect；这些实例不进入播放注册表。
- 参数按真正的 VST ParamID 捕获，而非假设 ID 连续；只读输出参数不恢复。原生桥的独立一次性参数队列不会因超过实时队列容量而漏掉参数。
- 状态恢复、Setup、激活之后以零样本块刷新参数，再查询延迟。失败、取消、成功都释放作业实例；Load 失败时同样回收已经创建的专用线程。
- `IVstExportBridge` 是新增能力接口，旧 IVstBridge 和旧播放/测试桥签名不变。生产桥调用新增 C ABI；原生 Setup/Process 正确使用 kOffline 或 kRealtime，Reset 保留处理模式，并落实 setProcessing 生命周期。
- 每块提供绝对采样位置、连续采样位置、精确四分音符位置、BPM、拍号及播放标志；使用启动时捕获的 TimeAxis，不随后来的实时节奏编辑改变。
- `ExportTrackSource` 从工程起点预滚独立处理链，再按每轨串联插件总延迟读前面的音频，丢弃初始化延迟后对齐混音。选区前的处理历史及最后的延迟样本都被保留；每个预滚块可取消。
- 固定交付结束后，已有输入边界继续向效果链提供零输入，消费选定尾音。输出范围/总长度不因为 PDC 增长；动态改变延迟时终止交付，避免默默输出错位轨道。
- 文件输出采样率仍是交付重采样规格；内部音频链和插件处理当前使用 AudioSettings 的 44100 Hz。没有宣称切换文件规格同时改变全工程 DSP 采样率。

原生实现参照 [VST3 处理模式](https://steinbergmedia.github.io/vst3_doc/vstinterfaces/namespaceSteinberg_1_1Vst.html)、[处理调用顺序](https://steinbergmedia.github.io/vst3_dev_portal/pages/Technical%2BDocumentation/Workflow%2BDiagrams/Audio%2BProcessor%2BCall%2BSequence.html) 和 [ProcessContext](https://steinbergmedia.github.io/vst3_doc/vstinterfaces/structSteinberg_1_1Vst_1_1ProcessContext.html)。

## 原子提交

1. `2167c9de`：独立插件离线链、原生处理模式、状态/参数捕获、时间上下文、预滚与分轨 PDC、回归。
2. `673fc8c4`：默认离线、手动实时兼容 UI 及行为测试。
3. `a79f817d`：旧直接混音入口迁移、精确音乐时间、动态延迟检查与对应回归。

## 轻量验证

遵循用户的设备负载要求：MSBuild / 原生 CMake 单节点；测试组逐一使用 Test-Light 的 30 秒/384 MB 护栏，未运行全量测试、真实音源推理或自行截图。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| Windows C++ 原生桥 Release | 通过；已更新受版本控制的 DLL | `.dsh/native-offline-configure.log`、`.dsh/native-offline-build2.log` |
| SDK 9.0.317 测试项目及产品依赖 | build2 通过，历史警告保留 | `.dsh/offline-export-build2.log` |
| 独立离线 VST、真实交付文件和新原生 ABI | 23 通过，约 6.3 秒，峰值 67 MB | `.dsh/light-test-results/73c1ff9b3f5b43de93f50d49f5a7d3a8.trx` |
| 真实渲染窗口、方式选择、输入校验和作业所有权 | 24 通过，约 10.1 秒，峰值 135 MB | `.dsh/light-test-results/c33a96d068db4ca2879d64ebd43488fb.trx` |
| 旧 VST 分块/分配、导出所有权和取消源 | 27 通过，约 6.7 秒，峰值 63 MB | `.dsh/light-test-results/c861153ca59a45f6995553da0d7ca374.trx` |
| UI lint / diff | ERROR 0 / WARN 0；空白检查通过 | `.dsh/fx/ui-lint.ps1` / `git diff --check` |
| Windows 启动、加载两轨工程和退出 | 响应正常，12 秒峰值 251 MB，ERR/FTL 0，exit 0 | `.dsh/offline-startup.log`、`.dsh/offline-preview.json` |

三组 74 项互不重叠；不要累计前序重复组。窗口及旧链组在 build1 通过，build2 只改离线链的时间精度/延迟检查和直接混音入口，23 项新链组在最终 build2 重验。运行时源 DLL、产品和测试目录的 DLL SHA256 一致。

关键断言：捕获编辑器增益后再修改实时参数不会改变导出；只释放新插件，原播放实例仍在；播放设备对象保持；不同延迟轨道首尾脉冲完全对齐；设置/恢复/处理错误和预滚取消保留旧文件；Tempo map 后续编辑不改变时间上下文，音乐位置保留小数；动态延迟变化拒绝发布；直接菜单确实写出离线 float WAV。

原生 ABI 测试实际调用新 DLL，验证导出入口存在且拒绝无效句柄。DSP 整体处理采用可控模拟桥；这项证据不能冒充真实插件的离线音质或兼容性验收。

Windows smoke PID 9400，启动 `2026-10-11T12:53:30.4171025+08:00`，已正常退出。旧用户预览 16244 在本轮构建前已经退出；本轮没有留存预览进程。

## 仍需区分的边界

- 未进行真实 VST/设备试听、长时间歌声合成或大工程速度基准；不承诺每个插件均支持离线模式，也不声称原生插件兼容性已经全部验证。
- 当前原生处理仍以立体声音频效果为范围，未新增多声道/辅助侧链路由、插件乐器或宿主自动化轨。明确的尾音时长继续由用户选择。
- 动态改变延迟会报错停止，没有实现运行中重建/重新对齐。离线导出不会自动换实时，用户选择保留可见。
- 捕获插件、时间轴和交付配置已经实现；完整不可变歌声合成输入、播放/导出独立合成调度器及全局插件销毁仍属后续架构工作。
