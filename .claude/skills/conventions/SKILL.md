# conventions — UTvTU 开发铁律

动代码前必读。

## 分层红线
- **View 不直接改 Core 模型** — 不走 track.Volume= / track.Pan= / slot.PluginUid=
- **状态变更走 DocManager.Inst.ExecuteCmd(UCommand)** — 可撤销 + 通知 ICmdSubscriber
- **新建 ViewModel 继承 ViewModelBase，属性用 [Reactive]**
- **Window 继承 WindowEx（自绘），对话框继承 Window（原生）**

## VST 铁律
- **VstEffect 构造函数注入 IVstBridge**（默认 RealVstBridge.Instance，测试用 FakeVstBridge）
- **生命周期**：Load → Setup → 首次 Process 调 Activate → ... → Dispose
- **Dispose 必须在 RCU grace period 后**：先发新效果数组，音频线程切换后 FlushPendingDispose()
- **DllImport 字符集**：当前 DLL 是 ANSI，勿加 CharSet.Unicode（待 C++ 重建后同步启用）

## 测试铁律
- **每步改动后跑 verify** — 不只靠 dotnet build/test
- **手写 Fake（不用 Moq）** — 参考 FakeVstBridge / FakeEffect
- **headless UI 用 [AvaloniaFact]** — 参考 MixerTrackStripTest
- **DocManager 测试用 DocManagerTestSetup.RunOnCurrentThread()**

## 提交铁律
- 中文 message，格式：`类型: 简述\n\n详情`
- 原子化，每提交可构建可运行
- 分支：`plus-develop`
