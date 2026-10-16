# VST GUI 默认保持在主窗口上方

用户确认保留原生浮窗，不实现内嵌。采用Windows owner关系，插件默认高于UTvTU主窗口，主窗口最小化时插件隐藏、恢复时显示；没有全局Topmost标志。机架和诊断面板两个打开入口均传递主窗口HWND。

产品提交`e5bff785`。Core新增带owner的异步打开重载，保留既有无参数CLR入口与旧原生导出；IVstBridge默认实现兼容旧桥。原生桥校验owner必须为本进程顶层窗口；首次创建使用owner，重复打开复用窗口并更新归属。createView/attached/窗口消息/removed继续在共享VST STA执行，没有把插件代码移回Avalonia UI线程。

真实探针发现跨线程owner销毁后插件窗口可能残留，不能仅依赖窗口归属自动销毁。原生桥增加仅当前进程/owner线程的销毁事件钩子，事件回调在VST线程异步投递WM_CLOSE；销毁时解除钩子，WM_DESTROY统一执行已附着视图的removed，防止绕过WM_CLOSE时漏清理。无需轮询或让主窗口同步等待插件释放。

## 轻量验证

- SDK9.0.317、单节点、禁用共享编译/项目引用重编译，复用依赖。Core 16.87秒、0错1066既有警告；主程序34.70秒、0错3既有警告；测试15.91秒、0错152警告。原生桥Release增量构建成功（日志仍有C4819代码页警告）。
- 25个不同回归全绿：宿主响应/线程7项，生命周期及界面归属18项；两组串行，4.9/7.8秒，峰值53/130MB；保持30秒/384MB护栏。
- 单个真实Persistent Q原生窗口探针：3.2秒、峰值121MB、exit0。验证归属、点击主窗口后仍在上方、最小化隐藏/恢复显示、重复打开复用句柄、非法owner不破坏窗口、关闭再开、owner销毁清理、使用新owner重开、卸载插件关闭编辑器且保留owner。
- 探针初版确实在“owner销毁清理”失败，补事件钩子后最终通过；没有把此前失败当成成功。没有全套测试、实际歌曲合成、音频压力或自截图；本轮单实例窗口检查不代表所有插件兼容。
- [验证清单](vst-owned-editor-2026-10-16/validation.json)记录TRX/哈希。复现材料为同目录的`native-owner-probe.cs.txt`、`native-owner-probe.csproj.txt`、`Guard-OwnerProbe.ps1`：复制前两者到`.dsh/vst-owned-20261016/probe/Program.cs`与`OwnerProbe.csproj`，用SDK9构建，将本轮桥DLL放到探针exe旁，使用护栏启动。只管理独立OwnerProbe进程，不操作用户主程序；只需已安装Persistent Q，不打开用户工程。

## 交付

标准主程序目录已同步产品/Core/原生桥，旧文件备份`.dsh/vst-owned-20261016/previous-main/`。已无参数启动PID16664（2026-10-16T22:35:41.3587796+08:00），响应正常、窗口有效、启动ERR/FTL0；身份记录`.dsh/workspace-preview.json`。未推送GitHub。

产品SHA256 `C3E81B555C4EDC1E1203EA377040CD72FAA86AD4AB5BA31C94D094B4A243677A`；Core `97E615F7D81F9A0C9810379EB1617CABCBC8C8716FB25A7AB87D48F685820EFB`；桥 `9483EB15A8D1E985DF764CF8323D9AAB9B6A8B211B0BCF80F8D28CE088B60BEC`。

用户实机查看：打开实际工程的VST GUI，点击编辑器/混音台后确认浮窗仍在上方；最小化恢复主程序；单独关闭并重新打开插件GUI。
