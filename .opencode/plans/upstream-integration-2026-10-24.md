# 上游核心更新整合（2026-10-24，进行中）

用户已明确授权“总之就是开始合并”。基于`72a5393d`、上游固定`a9547895f93e23371949a12d34e98deb5dad0bda`，按[审查范围与引擎补充](upstream-review-2026-10-24.md)适配。G盘工作区、plus-develop、origin已核；保留UTvTU界面、VST、外部采样调度、离线导出、命令编辑会话。不是整体merge：只读merge-tree显示大量UI/音频架构冲突，未进入merge状态。

## 已完成

- `27f24adb`：空音色映射保护、完整滑音预设点的捕获/应用克隆、VCCV别名优先级、Shift表达式条件、轨头数字定位/全选/捕获、wavtool循环提前取消。保留本地编辑会话，一次预设一次撤销。20项轻量全绿：小修+预设3（8.6秒/125MiB）、音高工具6（14.4秒/235MiB）、英语VCCV11（14秒/119MiB）。
- `624c1d46`：WORLDLINE R1.1、内置hifisampler、hnsep/HnsepCache、glottal Rd、TorchFile、C# WORLD重采样与调用方buffer原生接口，配套win-x64 DLL和C++源码。精确R1.1分派，兼容构造version1/2；共享R图槽位接口为后续准备。接Classic采样器/NoWrapperScript/声学曲线/GRWC与缓存hash（含各曲线长度标记），保留外部采样调度/取消、同名保护与per-path缓存锁。ONNX会话统一DML→实例锁，R2临时会话确定性释放；16bit缓存分块写入避免全长byte缓冲。其他平台既有二进制未动。
- 引擎74项轻量全绿：WorldlineR11 4、HifiDsp10、HifiFeatures16、HifiTiming9、HifiRd3、HifiPost3、GlottalRd17、Hnsep4、HnsepCache3、TorchFile3、原WorldlinePreutter端到端2。最大13.6秒/100MiB；30秒/384MiB串行护栏未改。R11噪声合成和旧R两项为真实native调用，hnsep与声码器DSP用小夹具/替身掩码，未安装真实模型或试听。
- SDK9.0.319，2处理器/单节点/768MiB堆，四项目顺序构建。引擎最终Core0错1170既有警告21.72秒、插件0错569警告12.99秒、Main0错3既有警告40.06秒、Test0错152既有警告25.44秒。首次迁移的Wave写入与IsCpuRunner差异已适配，不交付失败产物。日志`.dsh/upstream-integration-20261024/`；构建脚本build.ps1，每组测试TRX在日志尾。

## 必须继续完成

- [ ] ENUNU三条与必要分句接口；协议/路径/帧边界/取消/旧服务回退小检查。
- [ ] SBP公共层与语言更新；修复YAML迁移失败覆盖，补精简词典/临时目录检查。
- [ ] JS音素器、稳定ID、包生命周期与开发者日志/模板/热重载；后台加载，保留现有UI投递与Fody。
- [ ] 表达式节点图完整前置与最新交互增强；工程存储、求值、轨道设置、导入导出、RPIT/PITO、现有音高手势与后台构建适配。
- [ ] 收尾原生ABI打包验证、许可/鸣谢与双语说明、最后构建及分组轻量回归/哈希/机器记录。

本轮不启动用户程序、不自行截图、不安装真实模型、不跑全套/压力、不自行推送或发布。标准Debug已更新至引擎批次，但不称最终整合完成。后续始终核实际工作路径；不要重跑small-fixes.py/engines.py覆盖已完成的适配。
