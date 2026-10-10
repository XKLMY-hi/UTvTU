# UTvTU 前端源码审查与共同重构切片

日期：2026-10-11。产品代码基线：b0eb3278；审查开始时 HEAD 为 c1952b83（此前仅新增路线文档）。
目标：在现有 UTvTU 上重构，定位为“人声制作工作站”，Windows 为当前验收平台。

**实施更新**：产品提交 `6f8998e4` 已修复 F01–F04 所列的主要命令/保存/跨会话路径，
并增加派发期间订阅变动保护；SDK 9 构建与 824 个测试通过。
详见 [实施与验证范围](mix-editing-2026-10-11.md)。实际混音听觉与实机拖动仍未验证，
第二批已处理 F05/F06 的主要实例泄漏与入口反馈，迁入紧凑 MD3 应用壳与侧栏视觉，
并补齐编辑器模型释放、过期导航与失败保存退出语义。
产品提交 `609af7fd`；SDK 9 构建成功，最终 **850 个测试全部通过**，UI lint 零错误/警告。
详见 [工作区实施记录](workspace-session-2026-10-11.md) 和
[设计规格](../design/md3-daw-2026-10-11/README.md)。完整架构拆分、其余编辑器视觉、
自动保存失败结果/原子写入及后续合成/音频切片仍待处理。
第三批 `f56051df` 已将轨道头与混音推子的输入/模板/编辑状态推进到控件级，
修复数字输入取消后误提交、撤销后旧预览恢复值、重挂显示陈旧和最矮轨头整卡溢出。
SDK 9 构建、最终 866 项测试全绿；UI lint 零错误/警告，Windows 启动/退出日志无 ERR/FTL。
详见 [控件实施记录](control-refactor-2026-10-11.md)。
下方保留 `b0eb3278` 初审证据；缺陷描述和行号是该基线的历史状态，不应当作当前源码状态。

## 审查口径

本轮检查主窗口、欢迎页、偏好页、工作区导航与分离宿主、钢琴卷帘编辑与绘制、混音通道条、
效果窗口、命令注册与快捷键、主题和绑定，以及相关测试。沿调用链核对 Core 的撤销、工程切换、
脏状态、保存与乐句校验。

这是源码审查：以下“确认”表示代码路径已核实，没有声称已在实机复现。未启动应用、截图、
测量帧率或运行产品测试；历史交接中的测试数字不作为当前验证结果。只改审查和路线文档。

当前 UI 为自研 MD3 + Fluent 底层主题，README 与 App.axaml 相符；AGENTS 中的 SukiUI 现状段落已过时。
[README](<G:/xklmy文件夹/vibe coding/UTvTU/README.md:8>)；[应用样式挂载](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/App.axaml:41>)。

P1：优先修复的数据或编辑语义缺陷；P2：行为、生命周期或工作流问题；其余明确列为结构债务/待实机验证。

## 一、确认的缺陷

### F01 / P1：混音台音量修改绕过脏状态和自动保存

通道条推子调用 ApplyVolume，直接设置 track.Volume，再发送 VolumeChangeNotification；
没有修改命令或 MarkProjectModified。DocManager 对普通通知只发布，不设置保存状态。
轨道头收到音量通知时设置 _syncing=true，因此其 Volume 订阅也不会补标脏。

触发：先保存工程，再只拖动混音台音量。工程修改未进入正常脏状态路径，关闭提示与自动保存可能跳过这次编辑。
手动保存仍会序列化当前音量；风险在于工程认为没有需要保存的变化。

证据：[推子写入](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Controls/MixerTrackStrip.axaml.cs:318>)；
[模型直写](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/MixerTrackStripViewModel.cs:48>)；
[轨道头接收时抑制写入](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/TrackHeaderViewModel.cs:165>)；
[通知分支](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/DocManager.cs:241>)；
[自动保存跳过条件](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/DocManager.cs:187>)。

声像需要区别处理：TrackHeader 的 Pan 订阅缺少同样的 _syncing 检查，可能通过轨道头回写而间接标脏。
因此不能笼统声称所有声像操作都不会标脏；它暴露的是保存语义依赖另一视图是否存在，且通知接收也会写模型。

共同修复：UI 手势调用同一修改入口；应用层维护修改/保存修订与一次拖动的撤销记录；
视图同步只更新显示。验收包含轨道头挂载与未挂载两种场景、重开文件、关闭提示与自动保存。

### F02 / P1：静音、独奏的撤销记录捕获了新值

ToggleMuteCmd、ToggleSoloCmd 先写 track.Mute / track.Solo，随后才调用 TrackMixCommands。
命令工厂在创建时读取 old，读取到的已经是新值；Unexecute 恢复的仍然是切换后的状态。

证据：[前端执行顺序](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/MixerTrackStripViewModel.cs:27>)；
[命令捕获旧值](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/Commands/TrackMixCommands.cs:34>)。

共同修复：命令成为模型写入的拥有者，执行后再同步界面和听觉状态。
验收包含静音/独奏切换 → 撤销 → 重做，以及轨道头和混音台显示一致。

### F03 / P1：非模态效果预览与保存、取消没有统一事务

MixFxViewModel 在参数变化时立即替换 track.MixFx，但仅保留窗口内部 dirty；
确定/关闭时才调用全局标脏。取消直接把打开时的 original 放回，不建立新的工程修订。

触发：打开机架，只改 EQ 增益等不改变总开关的参数 → 在主窗口保存 → 回机架取消。
保存写入的是预览参数，取消后内存恢复旧参数，取消路径却不标记这次恢复；
可能出现内存与磁盘不同、工程仍显示已保存。窗口打开期间的预览修改也缺少可靠的自动保存语义。

证据：[实时预览写入](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/MixFxViewModel.cs:169>)；
[取消与提交](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/MixFxViewModel.cs:339>)；
[非模态及关闭策略](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MixFxDialog.axaml.cs:19>)；
[保存当前模型并设 Saved](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/Format/Ustxp.cs:44>)。

共同修复：明确“实时编辑”或“可取消预览”的产品契约。
若保留预览，保存、自动保存、导出、关闭与取消必须对预览是否提交有一致定义；
不能仅依赖窗口最终关闭时补标脏。具体交互在切片设计中评审。

### F04 / P2：旧效果窗口关闭时可污染新工程状态

DocManager 先替换 Project，再发布 LoadProjectNotification；
MixFxDialog 收到后异步 Post 关闭窗口。关闭会调用 MarkModifiedIfDirty，
其内部调用无会话参数的 DocManager.Inst.MarkProjectModified，此时指向的是新工程。

触发：在工程 A 的机架内修改参数，保持窗口打开，然后切换到工程 B。
A 窗口的收尾可能把 B 标为未保存；通知还仅携带 TrackNo，缺少工程身份。

证据：[切换顺序](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/DocManager.cs:252>)；
[异步关闭](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MixFxDialog.axaml.cs:92>)；
[关闭补标脏](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MixFxDialog.axaml.cs:73>)；
[全局标脏](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/MixFxViewModel.cs:359>)。

共同修复：窗口和任务绑定工程会话；切换前明确处理预览，切换后拒绝旧会话的修改与通知。
验收：A → B 后执行排队回调，B 的修订、保存状态及同编号轨道不受 A 的收尾影响。

### F05 / P2：反复打开偏好页会保留旧 ViewModel 的全局订阅

ShowPreferences 每次 new PreferencesViewModel。
ViewModel 的 ThemeEditorStateChangedEvent、ThemeChangedEvent 全局 MessageBus 订阅未保存释放句柄，
类也没有 Dispose/Unsubscribe；旧实例仍被消息总线引用，主题变化会让多个旧实例重建色板。

证据：[每次创建](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml.cs:958>)；
[主题编辑订阅](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/PreferencesViewModel.cs:263>)；
[主题变化订阅](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/PreferencesViewModel.cs:357>)。

PreferencesView 自己的 VST 库订阅已有挂载/卸载释放，不能据此推断其 ViewModel 也被释放。
共同修复：明确偏好页实例复用或替换策略；全局订阅由拥有者释放。
验收：反复打开/完成设置后，只剩当前实例响应主题事件，旧实例可回收。

### F06 / P2：钢琴卷帘入口没有表达不可用状态

顶栏钢琴卷帘按钮没有绑定可用条件；pianoRoll 尚未创建时 RequestView 直接 return，
要求用户先双击片段，但按钮本身没有说明。源码注释表明这是既有入口策略，属于工作流缺陷，
不据此认定程序崩溃或回归。

证据：[入口按钮](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml:439>)；
[静默返回](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml.cs:909>)。

共同修复：会话提供可编辑片段/当前片段状态，导航明确显示入口条件，
或让该动作打开选中片段；无可编辑对象时给出可执行的引导。

## 二、结构债务与响应风险

| 编号 | 源码事实与影响 | 前后端配套工作 |
|---|---|---|
| A01 编辑响应 | 音高点每次有效 Pointer Update 都 ExecuteCmd；普通拖动 Begin 不启用延迟校验；DocManager 同步 Validate；UVoicePart 清空并重新构建 renderPhrases。重计算与交互共用 UI 线程，实际耗时未测。 | 前端保留即时手势反馈；核心合并失效、按影响范围校验、构建可取消快照。拖动一次形成一次撤销，最终合成必须对应最后修订。 |
| A02 导航与宿主 | ViewSwitcher 已统一三个工作视图；欢迎/偏好通过独立 IsVisible、chrome 和异步淡出管理；分离/回收、文件操作仍在 MainWindow。WelcomeView、PreferencesView 持有 MainWindow Host。 | 工作区宿主管导航/面板/窗口；工程操作服务管打开保存关闭；显式状态描述覆盖层、选中片段、分离窗口和可执行动作。 |
| A03 生命周期 | NotesCanvas 的全局 NotesRefresh/Selection/PartRefresh 订阅没有释放句柄；PianoRoll.Unsubscribe 释放自身订阅及子控件 DataContext，没有释放这些控件自身的订阅。MainWindow 的 15ms timer 停播时仍发送属性变更。 | 区分临时隐藏、分离重挂、工程切换与永久销毁；逐项确定订阅/计时器拥有者，限制不活动视图工作。不能把每次重挂都当永久 Dispose。 |
| A04 样式边界 | App 同时挂 Fluent、旧 Styles、MD3 控件与主题；主窗局部指定 11/10px，旧全局 TextBlock 显式 13px。自绘音符继续读取 ThemeManager 旧画笔。 | 按模块建立令牌与模板属性传递契约，核对文字内容控件及绘制层的主题语义。是否出现字号偏差需实机验证，未证明所有胶囊文字都错误。 |
| A05 键盘可达性 | 主窗口局部样式把 Button/ToggleButton 全部设 Focusable=false，PanelSplitter 也不可聚焦；快捷键能覆盖部分动作，但不等价于所有控件可由键盘访问。 | 结合编辑器焦点规则定义 Tab 顺序、焦点返回、分隔条替代操作和可访问名称；逐个验证关键工作流，不直接全局翻转 Focusable。 |
| A06 绑定与验证 | AvaloniaUseCompiledBindingsByDefault=false；主窗/卷帘/混音主体缺少类型化绑定。欢迎页存在读取 XAML 文本并断言尺寸/字符串的测试。 | 按模块迁移类型化绑定；设计源契约继续保留，但增加实际行为/模板属性/导航状态验证。源码文本一致不能证明布局、绑定和交互正确。 |

A01：[拖动事务](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/NoteEditStates.cs:66>)；
[音高点更新](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/NoteEditStates.cs:654>)；
[同步校验](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/DocManager.cs:297>)；
[乐句重建](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Core/Ustx/UPart.cs:309>)。
上游参考目录的 DocManager.QueueValidate 已合并到下一帧，位于参考提交的第 388 行；
移植前需检查保存、撤销、切换与刷新时序，不能直接复制。

A02：[工作视图策略](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/ViewModels/ViewSwitcher.cs:57>)；
[偏好覆盖层](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml.cs:958>)；
[欢迎页 Host](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/WelcomeView.axaml.cs:21>)。

A03：[画布订阅](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Controls/NotesCanvas.cs:184>)；
[卷帘释放边界](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Controls/PianoRoll.axaml.cs:187>)；
[主窗轮询](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml.cs:126>)。
当前缓存复用卷帘，不能仅凭缺少 Dispose 就声称每次切换都增加一个实例；
NotesCanvas 的动画 timer 也已有按需要启停，不能说它们始终运行。

A04–A06：[全局文字样式](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Styles/Styles.axaml:110>)；
[按钮模板](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Styles/Md3ControlThemes.axaml:40>)；
[主窗焦点规则](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/Views/MainWindow.axaml:21>)；
[绑定配置](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau/OpenUtau.csproj:14>)；
[欢迎页文本断言](<G:/xklmy文件夹/vibe coding/UTvTU/OpenUtau.Test/App/WelcomeViewTests.cs:73>)。

## 三、应保留的现有成果与本轮排除项

- 已有 CommandRegistry 与快捷键分发，且 TryExecuteShortcut 实际检查 CanExecute。
  注册表注释“仅用于总览”已过时；问题是仍依赖 MainWindow、菜单/入口未完全统一，不是没有命令层。
- 已有 ViewSwitcherPolicy、PanelSplitter 的自适应宽度约束、MixerTrackStrip 的幂等退订与重挂；
  不把现有布局判定为完全没有自适应，也不建议全部推翻。
- DetachAndFlush 已在移出宿主时清空内容并刷新布局；旧文档的 reparent 崩溃不能直接算本轮新确认缺陷，
  仍应进入回归场景。
- 旧备份分支的修复可逐条作为候选；不整批恢复，不以历史“已修复”标注替代当前源码检查。

## 四、共同推进的切片与验收

“前端”指交互、视图、ViewModel 与状态呈现；“后端”指应用操作、工程、合成、音频运行时，
是桌面进程内的职责划分，本路线不引入 Web 服务分层。

| 顺序 | 工作流切片 | 前端交付 | 后端交付 | 共同验收 |
|---|---|---|---|---|
| 0 | 现状基线与交互契约 | 梳理入口、不可用/加载/失败/未保存状态；列出关键实机流程 | 当前构建测试与固定工程/音频样本基线 | 同一场景、同一输入、同一结果，历史数字不代替实际验证 |
| 1（首批建议） | 混音修改 → 保存/取消/切换 | 推子与 M/S 的反馈/撤销、机架预览契约、跨视图同步 | 统一修改事务、工程身份、保存修订；迁移全部入口 | F01–F04；保存重开结果一致，旧窗口不得影响新工程 |
| 2 | 打开工程 → 导航/分离 → 关闭 | 入口可用性、覆盖层/窗口状态、焦点与错误反馈、偏好实例管理 | 工程操作服务、保存结果、会话关闭、订阅/任务释放 | F05/F06；菜单快捷键一致，取消/失败不退出，反复分离回收正常 |
| 3 | 编辑歌声 → 合成 → 试听 | 手势响应、选择/滚动、合成状态与结果对应 | 校验合并、影响范围、快照/缓存/取消、修订匹配 | 拖动最终结果与合成一致；旧结果不得覆盖新工程；采集实际耗时 |
| 4 | 混音 → 导出交付 | 导出范围、进度、取消/失败反馈与试听结果对照 | 播放/导出计划隔离、VST/DSP 状态与安全销毁 | 时序/效果/范围正确；播放并导出、插件增删与工程切换安全 |

每个切片都处理其涉及的样式、绑定、键盘可达性和错误状态；不把前端整体验收拖到核心重构之后。
视觉大改按模块提出具体交互/设计，几何与令牌取用户 HTML；用户实机预览作为阶段验收。
本轮没有选定新视觉稿，也未授权把整个界面改成上游外观。

## 五、后续验证界限

第一批先验证 F01–F04 的行为，再设计混音修改与保存契约。与之同步梳理工作区导航状态图和前端模块边界。
新增测试应验证保存往返、撤销/重做、跨会话回调等行为；性能采样使用固定工程和明确的设备配置。
视觉验收关注深浅主题、文字层级、100%/125%/150% 缩放、窄窗、键盘与分离窗口；
按用户约定交由用户实机查看，除非用户明确要求自迭代，不自行截图。

本报告列的是当前源码问题与重构依据，不是已经完成的修复。
