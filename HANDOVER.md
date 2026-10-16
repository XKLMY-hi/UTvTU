# HANDOVER — 新会话交接（2026-10-16 统一底部效果器机架）

## 2026-10-16 机架按钮反馈修复（最新）

- 用户截图中左右移动显示`OpenUtau.App.Controls.DawIcon`，同时内置效果无法找到移除。根因是图标按钮误用仅文字的DawTextButtonTheme，类型名挤出移除入口；已改用支持ContentPresenter的DawButtonTheme，移动按钮固定28宽，文字移除保留文字模板；内置效果新增右键移除。
- 产品与回归提交`d70f0b01`，本地未推送。新增10项实际控件回归（两种卡宽/两种高度、三种内置鼠标点击与菜单移除、撤销恢复）；本轮共36项轻量检查通过，串行30秒/384MB，峰值193MB。主程序增量构建0错3既有警告，测试0错151警告，复用上轮Core；无全套/真实合成/真实插件压力/自截图。[本轮验证](.opencode/plans/unified-effect-rack-2026-10-16/footer-fix-validation.json)。
- 标准主程序已更新，无参数启动PID12932、2026-10-16T22:21:19.4813345+08:00；启动检查见验证JSON及`.dsh/workspace-preview.json`。产品SHA256 `06E3395BB91FC1F3D94E9A4673828E5CCBCCAAE01BD031CFF3D6052120BC6B3B`，Core/原生桥不变。旧DLL备份`.dsh/effect-rack-actions-20261016/previous-main/`，本轮日志同目录。继续交用户实机看底部左右箭头、移除和右键菜单；下文旧PID/产品哈希是历史。

## 2026-10-16 统一效果器机架（最新状态，优先于下文）

- 用户已反馈前一版简易工作流验证通过、基本可用；随后确认把内置三效果拆成独立实例，与VST统一进入底部横向机架，主编辑器/混音台共用。第三方VST保留名称卡片、双击原生GUI的方式，未做通用参数面板。
- 已完成产品提交`43d055b5`、`35ce1379`与回归`96067d08`，plus-develop本地提交，**未推送GitHub**。[完整实现与验收](.opencode/plans/unified-effect-rack-2026-10-16.md)。当前工作区仍为G:/xklmy文件夹/vibe coding/UTvTU，origin XKLMY-hi/UTvTU；下轮仍须核实移动盘。
- 轨道FX、混音台工具栏和每轨“FX · 数量”进入同一个工具会话；原混音台右侧链列已腾出。底栏继续拖高/收起/展开，内置306宽、VST224宽、长链横向滚动，内容区裁切。内置曲线/旋钮/工厂及用户预设保留；VST名称/状态/旁通/原生GUI/右键替换与删除保留。
- 新UTrackEffect实例有稳定ID和独立参数，三类内置均可重复添加、删除和跨类型排序；标题拖动/左右按钮/Alt+左右键重排。操作走可撤销且不影响歌声合成的TrackMixCommand，关闭/ESC保留实时编辑。
- UTrack.EffectRack=null保留旧固定链，空列表是明确空机架；读取/开机架不写默认效果，第一次编辑才物化，撤销可回旧模型。旧总旁通/参数/顺序保留；实时图在编辑线程准备并于块边界切换，参数更新保留DSP及界面实例，稳态回调零分配。VST槽索引/实例不随排序互换，实例表支持并发读取，延迟销毁及共享STA修复继续有效。
- 配置式导出冻结同一条链顺序与独立参数，内置/VST筛选保留；USTXP保存新链，兼容USTX导出剔除effect_rack。旧MixFxDialog.Open转到共享工具；旧类型/ChainPanel属性保留兼容，但产品主流程不再使用旧竖排链。
- **141个互不重复轻量检查全绿**：音频/模型33、机架控件17、归属20、预设/编辑会话29、导出22、VST20；六组串行，5.7–11秒、峰值56–176MB，30秒/384MB护栏未放宽。[验证JSON](.opencode/plans/unified-effect-rack-2026-10-16/validation.json)含TRX与最终哈希。未跑全套、真实推理、真实插件压力或自截图；前一轮24实例结果不能当成本轮新机架实机压力结果。
- SDK9.0.317单节点。build9完整依赖成功；build13再次编译G2P资源发生OOM，失败产物未交付。最后复用通过回归的Core/依赖，禁共享编译服务，仅增量编译主程序build16（0错3既有警告、34.30秒）/测试build19（0错151警告、18.21秒），避免再次全量编译大资源。选择器仍在`.dsh/imports/utvtu-dawui-library-update-1/sdk9-host`；输出`.dsh/effect-rack-20261016/build/`。
- 标准主程序已更新并**无参数启动PID21036**、2026-10-16T22:09:07.7618044+08:00；响应/窗口正常、启动ERR/FTL0。产品SHA256 `3AC3807077A1D5B87F5A422FB1B426300BE4298BD1168DE0C63C0CDE171C40CE`；Core `A287526CE13267560FD94FE2585959193486FF4BCED56A2383E45A13D1A7D348`；桥仍`0CAF08E9B28464937B539A9FF976D17121946CA82092748F9DAC53D745EA1BA4`。旧二进制备份`.dsh/effect-rack-20261016/previous-main/`。身份记录`.dsh/workspace-preview.json`，操作重核exe+StartTime，不强杀未保存实例。
- 下一步交用户看实际机架、VST原生界面与听感；默认不自行截图。下文旧“内置固定DSP顺序”“右侧链宿主”“仅两条几何断言更新”等为历史状态，以本节为准。

## 2026-10-16 渲染、VST与窗口恢复（最新状态，优先于下文）

- 用户报告第三方渲染器/大范围音符预渲染卡顿、Persistent Q等GUI打不开、播放或预渲染时最小化恢复随机冻结。追问“双实例够吗”后已扩展验证；完整证据与边界见[稳定性修复报告](.opencode/plans/stability-2026-10-16.md)。
- 产品已本地提交`5e120c75`、`6f734083`、`fe2d05a2`、`89c59519`；保留缓存包已接受的UI，无产品XAML改动，**未推送GitHub**。当前plus-develop，实际G:/xklmy文件夹/vibe coding/UTvTU、origin XKLMY-hi/UTvTU已重核；下轮仍须核实移动盘。
- 经典渲染使用跨乐句/跨周期全局并发预算（最高8且保留CPU余量）、后台准备、外部进程取消/超时清理及BelowNormal优先级；缓存同路径锁覆盖读/生成/清理，防部分WAV与重复生成。
- VST改为跨所有实例共享STA GUI线程；真实Persistent Q分散线程双实例挂起已复现。打开/状态请求直接排队；setActive/Reset在宿主线程。桥修GUI再次打开、尺寸请求及句柄清理；错误在线程本地产生处捕获。槽位替换/状态写回同时验证实例和UID。
- 自动保存VST状态有全工程100ms预算，忙时保留上一份状态继续保存；不强杀原生线程。Windows设备位置快照用TryEnter，UI不等待设备Stop/Dispose锁；最小化跳过画面通知，传输继续，修启动标记/热缓存及循环重启竞争。原ProcessRunner/UpdatePlayPos/SaveAllStates的CLR入口保留。
- **86个互不重复轻量回归全绿**，5组串行，7.0–19.5秒、峰值56–166MB，保持30秒/384MB护栏；无全套、真实声库推理、长时间音频压力或自截图。[验证JSON](.opencode/plans/stability-2026-10-16/validation.json)含最终TRX、统计及哈希。
- 最终真实探针通过产品VstPluginManager管理8轨×3槽=24个Persistent Q/OTT/TDR Nova实例、4个同时打开的GUI、并发短静音缓冲/状态调用及两轮重载；exit0、6.6秒、峰值377MB，未放宽护栏。不是实际8轨歌曲播放，不代表容量上限或所有插件兼容。源码/护栏已存报告旁，原生日志在`.dsh/stability-20261016/`。
- SDK9.0.317单节点构建；默认SDK10未使用。选择器仍在忽略目录`.dsh/imports/utvtu-dawui-library-update-1/sdk9-host/global.json`，不在根目录钉SDK8。完整build7 0错1791警告；最终Core build14/测试build15均0错，桥Release构建通过。
- 标准`OpenUtau/bin/Debug/net8.0-windows/`已同步最终产品；旧实例正常退出，新主程序**无参数**启动PID12060、2026-10-16T20:16:00.5937296+08:00，响应/窗口正常、启动ERR/FTL0。产品DLL SHA256 `A7AFB29F20BC8F2CDBA1E1527FD749EF5BEC5CB45E0428C1817E04EF4533F32E`；Core `908CBC2F4C8CF58B187BC3424EE4EBA4AA80B3413FB794C578CDF9C6291800D5`；桥 `0CAF08E9B28464937B539A9FF976D17121946CA82092748F9DAC53D745EA1BA4`。身份记录`.dsh/workspace-preview.json`，操作须重核exe+StartTime；不得强杀未保存实例。
- 仍需用户原工程验证冷缓存交互和随机最小化恢复问题，不能宣称穷尽所有冻结。进程内永久挂起的第三方原生调用仍无法安全强杀，本轮未实现进程隔离。旧[VST线程记忆](.opencode/memory/vst-gui-thread-fix.md)已加更正，历史“每实例一线程”结论不再适用。下文PID/哈希与“未测试VST”等仅为历史。

## 2026-10-16 缓存包 Git 合并（最新状态）

- 用户要求将 `D:/xklmy文件夹/LocalSend/utvtu-dawui-library-update-1.zip` 的 Git 合并到本地。已核实工作区仍为 `G:/xklmy文件夹/vibe coding/UTvTU`，origin 为 XKLMY-hi/UTvTU；移动盘下轮仍须核实。
- 本地 `plus-develop` 从 `862c6a57` **快进至 `7e68f94418b95b89440df95c7e0150f10adbcf23`**，完整保留包内20次提交，无冲突，没有覆盖式拷贝或应用重叠补丁。本次只合并本地，**未推送 GitHub**；此前10/11成果及README更新已推送，下方旧段落的“未推送”是历史状态。
- 包来源 SHA256：`9E027A9BD724E546B74BA2B66926AA53B62E2885A9C0B5D27560C803A7B294B1`。包内320项sha256清单全通过，bundle verify通过；121份最终源码逐一比对一致（仅归一化Windows换行），13个迁出旧路径均已移除。
- 导入前备份分支：`codex/backup-before-dawui-import-20261016`；导入引用：`refs/remotes/recovery/dawui-library-20261016`。bundle和包说明保留在忽略目录`.dsh/imports/utvtu-dawui-library-update-1/`，包内说明仅作恢复资料，没有执行其中的Linux恢复脚本。
- 累计变更包括欢迎/偏好/资源/导出/编排/钢琴窗/混音台视觉，以及两个新库：`UTvTU.Md3`承载原HCT/CAM16引擎，`UTvTU.DawUI`承载共享颜色池、主题和首批控件。旧命名空间和兼容入口保留。新库API及边界见`UTvTU.DawUI/README.md`；包内Linux运行报告不等于本机Windows运行验收。
- 本机默认SDK现为10.0.400，但本轮按约定使用**SDK9.0.317**：只在上述忽略目录的`sdk9-host/global.json`选择SDK，没有在仓库根目录加global.json。主程序和新库restore通过；Windows主程序及项目依赖**单节点编译通过，0错误/1638警告，用时2分42.80秒**，日志`build-windows.log`。使用`RuntimeIdentifiers=`、`UsedAvaloniaProducts=`和独立OutDir；VstProbe单独restore后随主程序构建。
- 编译产物在`.dsh/imports/utvtu-dawui-library-update-1/build/`。用户随后要求启动，已将完整产物（含新增两库及deps文件）同步到标准`OpenUtau/bin/Debug/net8.0-windows/`，主程序/两库/Core/Plugin/运行配置与源产物SHA256逐一一致；从标准目录**无参数启动PID18636**，响应正常、窗口句柄有效、启动日志0 ERR/FTL。产品SHA256为`375661D4BA67D0AAEAD9D191BF0F72CCD9336F8EDEC4DB53B2CD90C8129A744B`，身份记录在`.dsh/workspace-preview.json`，下次须重核exe+StartTime。未自截图、未跑全量测试/真实合成/VST；下方10/11的PID和哈希均为历史记录。


## 2026-10-11 截图反馈修复（最新状态）

- 用户指出控件背景溢出、资源声库列表应显示真实图标。产品已提交 **`a04807e9` / `1e6b6cc5`**，plus-develop，未推送；[修复与验证](.opencode/plans/control-background-avatar-2026-10-11.md)。
- WorkspaceToolView 取消方形页面默认底色，BPM 等嵌入浮层共用宿主卡片表面；完整页显式背景保留。声库侧栏12圆角裁切、去重复列表框；原生勾选子路径取代分段中带方框图标。
- SingerAvatar 只为实现的行解码72宽/36格真实 AvatarData 或 Avatar 文件；不 EnsureLoaded，不启动合成/全局订阅/计时器。缺失或读取失败默认图标，回收释放，AvatarRevision 同步加载/刷新/设置头像。USinger 列表和筛选选择语义不变。
- 实际重新挂载查出并修复 OtoPlot 卸载后留已释放位图引用的问题；清引用/缓存，加载时按现有WaveFile恢复必要频谱。
- 保持用户主程序打开，在独立OutDir构建：SDK9完整引用build4 0错148警告，测试收尾build5 0错145警告。**54项**两组轻量全绿，16.2/15.9秒、225/192MB，仍30秒/384MB/串行。UI lint0/0、diff通过；头像headless只验绑定与生命周期，不冒称像素/真实解码，未自截图、全套、推理或音频压力。
- 原主程序目录的 OpenUtau.dll 已原子替换为测试过的新文件，原二进制备份在`.dsh/controls-polish-previous-binaries/`；embedded调试符号无需单独pdb。标准测试目录也更新，产品DLL哈希同为E08F01A86E183DD846C6B2C7B344DB36F967C9D5801918F8E6A17B8B9043A2B8。
- 用户处理未保存提示并正常关闭旧实例后，已从原目录**无参数启动更新版PID12096**；响应正常、窗口句柄有效、启动日志0 ERR/FTL，运行产品哈希E08F01A86E183DD846C6B2C7B344DB36F967C9D5801918F8E6A17B8B9043A2B8，NeedsRestartForUpdate=false（`.dsh/workspace-preview.json`）。PID只记录本次启动，后续须核实exe+StartTime；不要预览工程/测试参数，退出时仍不得强杀未保存实例。
- 本轮仍核实G:/xklmy文件夹/vibe coding/UTvTU及origin；移动盘下轮仍须重新核实。新二进制已打开真机主窗，默认交用户看，未自行截图。

## 2026-10-11 全界面MD3重构（最新请求，已交付）

- 用户授权“按MD3真实设计重构所有界面”；产品已完成 `53adc142` + `87ce1a33` + **`f4c6e524`**，设计 `34dde3f8`，plus-develop，未推送。[完整实施与验证](.opencode/plans/md3-rebuild-2026-10-11.md)。本段取代下方“尚未实施”“只审查”“浮层200ms”等旧状态。
- 共同Theming/Md3Tokens：形状、排版、间距、状态透明度、Standard spring；按钮/输入/选择/列表/菜单/反馈与71份XAML覆盖，44份直接用共同令牌，其余继承。页面40高、DAW32/24等紧凑密度保留；已修42高/160宽窄轨道溢出。
- 偏好→外观增加Windows强调色/自定义HEX、TonalSpot/Vibrant/Neutral和对比度；Windows在启动/主题改变/点击刷新时读DWM，尚无系统颜色常驻监听。HCT/CAM16+49角色保留，修池重建丢订阅与静态投影先于角色生成。主/次/危险操作配对、rich-content图标与文字转发、选中分段配对统一。
- 欢迎Arch/Square原生Google几何用于页头/空状态；品牌原路径与音乐数据颜色保留。`MaterialShape`无定时器/主题或文档订阅。HTML+9PNG在`.opencode/design/md3-rebuild-2026-10-11/`，HTML/SVG几何权威、PNG是设计稿而非产品截图。
- 效果动效damping1、空间.9，官方Standard stiffness原值、有限过渡0.001收束；Avalonia原生负责时钟/打断，不冒称Compose引擎。状态取StateTokens v0_210 .08/.10/.10/.16，源码/许可/hash落盘。浮层28圆角/surface-container-high/level3/32%遮罩、3DIP背景模糊，关闭后还焦点。减少动效实时覆盖已有窗口/菜单/效果架/音符光晕和播放弹跳；静态播放高亮保留。
- SDK9.0.317单节点完整依赖build8通过（0错148警告）；**214项互不重复轻量回归**10组串行通过，10–15.8秒、峰值139–301MB，保护仍30秒/384MB。相关最终修正组已复跑；合并组超30秒后拆组，没有放宽保护。详见`product-validation.json`里的TRX/用例/哈希。UI lint ERROR0/WARN0、diff通过。未跑全套/真实推理/插件试听/压力，不自行产品截图。
- Windows隐藏启动smoke：2026-10-11 15:41:17+08、本树PID10628，两轨加载/响应/关闭exit0、峰值256MB、ERR/FTL0，**已退出，无预览进程**。`.dsh/workspace-preview.json`更新；下方PID12360是旧状态，不能复用。任何进程操作必须重核exe+StartTime。
- 本轮工作区G:/xklmy文件夹/vibe coding/UTvTU、origin XKLMY-hi/UTvTU已核实，下轮移动盘仍须核实。默认交用户实机看，不自截图；用户若下一步要“启动”则启动本树最新exe。

## 2026-10-11 MD3设计风格审查（最新请求）

- 用户要求审查控件、设计、动效与动态取色，并获取Google几何设计实例。本轮**只做审查和资料交付**，未修改产品，未启动新重构、构建或产品测试。
- 完整结论：[MD3设计审查](.opencode/plans/md3-style-audit-2026-10-11.md)。现有HCT/CAM16和49角色是可保留的真实MD3底层；问题集中在来源入口、颜色与操作语义、控件主题分叉、散落的形状/排版参数及未收敛的动效策略。
- 确认产品固定TonalSpot、六种种子；Core的Expressive变体仅为配色，不能等同M3 Expressive完整系统。Md3MotionFast/Base/View仅有定义，实际时长写死；no-motion样式只覆盖fade/pop，菜单与控件过渡覆盖不足。均为源码发现，不假称完成运行时对比度/像素审查。
- Google官方参考板：`.opencode/design/md3-style-audit-2026-10-11/index.html` + `reference-board.png`。已取得Reply完整实例、默认/动态主题对照、35种MaterialShapes官方原图和原生Kotlin几何快照；研究前后案例/动效/按钮组/图标均附官方链接。出处、原图和源码SHA-256、许可与日期已落盘，不描摹PNG。
- 参考板单页独立headless浏览器离线渲染，3原图与源码哈希核对、4本地链接、1440/800无溢出、pageErrors0。PNG是参考汇编，不是UTvTU产品截图或新界面设计稿；未抓产品图。轻量资料脚本不运行推理/测试。
- 推荐下一轮先共同控件图库+主题角色/密度/形状令牌，再主题来源与操作变体、共同motion/reduced-motion策略，最后欢迎/空状态/渲染结果局部表达几何；DAW数据颜色、精确编辑几何与即时反馈保持稳定。方案细节与验收见审查报告，尚未实施。
- 上轮用户“启动看看”之后已显示本树预览：`.dsh/workspace-preview.json`记录PID12360、启动14:11:46.866+08、refactor-smoke.ustxp；本轮没有关闭或重启。下方“无预览进程”仅是更早smoke结束时状态；后续操作必须重核exe路径+启动时间，不能直接沿用PID。
- 本轮实际路径G:/xklmy文件夹/vibe coding/UTvTU、origin XKLMY-hi/UTvTU、plus-develop已核实；下轮仍需核实移动盘路径。产品基线dc220e2e，前次docs e6f2dcc7；参考资料审查独立提交，未推送。

## 2026-10-11 收起、资源中心与浮层效果（最高优先级）

- 用户反馈三项：内置三效果器面板缺少一键收起、资源中心视觉需加强、小浮窗模糊和动画未同步。
  已完成产品提交 `25fe2a0e` / **`dc220e2e`**，设计提交 `ea12c0eb`，plus-develop，未推送。
  详见[资源与浮层反馈修复](.opencode/plans/workspace-polish-2026-10-11.md)。
  本段取代下方「未实现40高收起」「窄窗隐藏子音源摘要」及原启动smoke状态。
- 底部工具标题增加独立「收起/展开」；收起到40高、保留工具实例与编辑值，恢复此前高度。
  关闭仍结束会话，效果器快照/撤销语义不变；并未新增四工具缓存页签。
- 资源中心：可搜索音源列表、当前对象详情、OTO/子音源/信息三页；编辑、试听、高级菜单全部保留。
  搜索不自动换音源；OTO仍12列/32高/真实右键，波形与表格之间可调，虚拟化继续有效。
  窄窗使用原生音源ComboBox（并非设计演示的抽屉），制作导航800以下收为152宽。
  包页有搜索、全部/已安装/可更新筛选、真实数量/空状态；注册表离线仍显示本地包。
  修复初始音源高级菜单命令未绑定、OTO重置显示旧列表、切音源未重用别名筛选。
- 浮层背景统一到WorkspaceSurface：3DIP模糊、约28%遮罩、沿用MD3的200ms淡入/淡出与缩放。
  背景包括编辑器/欢迎/偏好/资源；关闭退场后才启用背景并还焦点，嵌套层与旧Overlay共用阻塞状态。
  迟到动画不得隐藏新浮层，ReduceMotion立即关闭。图标和富内容按钮使用ContentPresenter主题。
- 设计HTML+PNG在`.opencode/design/workspace-polish-2026-10-11/`，HTML几何/现有动态角色权威。
  PNG是设计稿，非产品截图；产品仍使用原生图标几何，不描摹。生产与示意差异见计划。
- SDK9.0.317单节点增量构建build5全绿（BuildProjectReferences=false沿用此前已构建依赖）。
  产品与测试目录OpenUtau.dll SHA256相同：AAEB2D70E6934C6A5C9497B90A27DB925DA61518795FC57B87C3F45C397BA7AC。
  **36项轻量检查全绿**：21资源/嵌套宿主/浮层（15.5秒、214MB）+15效果器（10.3秒、134MB）。
  未跑全量、真实音源/插件试听或推理；UI lint ERROR0/WARN0、diff检查通过。
- Windows隐藏启动smoke：14:09:26+08、本树PID12660，两轨工程加载/响应/关闭exit0、12秒峰值255MB，
  ERR/FTL0，已退出。`.dsh/workspace-preview.json`/`workspace-startup.log`记录已更新。
  无预览进程；未自行截图。请用户实机看收起/恢复、资源搜索与标签、浮层开关和减少动效。
- 实际路径G:/xklmy文件夹/vibe coding/UTvTU与origin已核实；下一会话仍需重核移动盘。

## 2026-10-11 主窗口工具整合与视觉完成（最高优先级）

- **最终产品提交`34ff35d5`**，plus-develop，未推送。详见
  [工作区整合与验证](.opencode/plans/workspace-integration-2026-10-11.md)。本段优先于下方独立渲染窗口描述。
- 用户授权整合零碎窗口并同步视觉；轻量测试、Windows、交给用户实机看UI继续有效。
- `15219c6c`/`9cb01312`：新增WorkspaceToolView、主窗工具路由、ToolWorkspaceView和嵌套WorkspaceDialogHost。
  渲染/音源/依赖包进入制作工作区；效果/表达式/歌词/轨道设置进入可调全宽底部工具区；
  其余输入/颜色/拍号/默认值/音源辅助/转录配置/加载/消息等进入窗口内浮层，真实模型和功能保留。
  单次ShowDialog由关闭完成，隐藏切页不销毁；渲染和资源页保留配置/搜索，工程替换退休旧渲染和上下文。
- 音源页重排操作、表格、波形；窄窗收起子音源摘要而保留编辑入口。表格有限高度并保持虚拟化；
  `342db45e`轨道设置双列自适应、独立滚动和固定确认按钮。`34ff35d5`图标保留原生填充语义。
- 歌词预览不再长期持有全局撤销组，取消只恢复自己拥有的值并保留外部编辑；表达式/歌词替换捕获工程与目标。
  导出期间编辑、资源修改、偏好、分离卷帘/混音台及编辑快捷键/拖入均受保护，可返回渲染取消。
  分离窗口辅助工具回到主工作区，不改变其原生窗口所有权。
- `1c52522e`：旧主题创建/编辑入口撤下；源码与已有配置保留，主题/强调色仍可选择。
  快捷键总览并入偏好、沿用真实注册表。插件GUI、可分离卷帘/混音台和系统文件选择器保留原生窗口。
- **视觉两件套**：`.opencode/design/workspace-integration-2026-10-11/index.html`及同目录PNG。
  HTML权威，PNG为设计稿而非产品截图；浏览器交互检查通过、pageErrors0。
  产品目前一个上下文工具按原入口打开，尚未实现设计示意的四工具缓存页签/收起40高模式；
  产品保留真实音源表与效果旋钮，不能把设计示例资源详情数据库等当成新增能力。
- SDK9.0.317单节点完整依赖构建build10、图标收尾增量build11全绿，产品/测试DLL哈希一致。
  **70项互不重叠轻量回归**：30渲染/效果/事务 + 12宿主/状态 + 28偏好/外壳/焦点/寿命，
  各组约13–15秒、峰值170/157/176MB；末尾另复验宿主12项（170MB/11.6秒），重复不相加。
  TRX与曾修复的断言/队列问题见记录；未跑全量。UI lint ERROR0/WARN0、diff检查通过。
- **Windows启动smoke**：13:39:08+08，本树PID18872，两轨工程加载、响应正常、12秒峰值252MB，
  ERR/FTL0、正常exit0，已退出。`.dsh/workspace-preview.json`/`workspace-startup.log`。
  没有留预览进程；之后任何PID操作仍须核实路径和启动时间。最终关闭图标绑定另以真实宿主回归验证。
- 未自行截图、未跑真实音源推理/插件试听/大工程压力；实机检查入口：制作工作区、资源页、底部工具及嵌套浮层。
  原有包安装/发布/合并后台任务没有新增全局取消调度；长期架构剩余项仍有效。
- 当前路径G:/xklmy文件夹/vibe coding/UTvTU和origin XKLMY-hi/UTvTU已核实，下轮移动盘仍须重核。

## 2026-10-11 12:55 离线导出实现（最高优先级）

- **产品最终提交 `a79f817d`**，分支 `plus-develop`，未推送。详见
  [独立插件离线导出实施记录](.opencode/plans/offline-export-2026-10-11.md)。本段优先于下方默认设备导出描述。
- 用户要求离线导出重要且开始实现；轻量测试、Windows、用户实机看 UI 的约定继续有效。
- `2167c9de`：ConfiguredExportSession 默认直接离线处理，含 VST 时也不需要声卡、不停止播放。
  ExportVstSession 一次捕获当前插件状态与编辑器参数、独立创建实例，不借用播放注册表的插件。
  C++ 桥新增离线/实时 Setup 与 Process、精确 ParamID 参数捕获/恢复、时间上下文和延迟查询，
  setProcessing 生命周期与保留模式的 Reset 已落实；受版本控制的 win-x64 DLL 已更新。
- 每轨独立预滚和 PDC 保证首尾样本对齐、选区前处理历史及尾音。失败/取消均释放作业实例，
  原子交付保留旧文件。插件处理错误不会默默输出干声；动态延迟变化终止，避免发布错位音频。
- `673fc8c4`：渲染页增加默认「离线（更快）」/手动「实时兼容」，摘要与计划接通；无自动回退。
  实时兼容保留静音设备消费方式，但也使用独立插件实例。
- `a79f817d`：旧「导出混音」菜单也迁移到离线，保留 float32/内部采样率/主输出语义。
  下方旧直接混音菜单仍录制的说明失效；旧 RecordMixdown API 签名和显式录制行为仍保留兼容。
- **构建全绿**：SDK 9.0.317 `.dsh/offline-export-build2.log`；CMake/MSVC Release
  `.dsh/native-offline-build2.log`（单节点）。产品/测试/仓库源 DLL 哈希相同。
- **74 项互不重叠轻量回归通过**：新离线链/文件/原生 ABI 23、真实窗口/输入/作业 24、
  旧 VST/所有权/取消 27；分组峰值 67/135/63 MB。完整 TRX 见记录，未跑全量。
  UI lint ERROR 0 / WARN 0、diff 检查通过。原生 ABI 用新 DLL，DSP 回归为可控模拟桥。
- **Windows smoke**：12:53:30 +08，本树 PID 9400，响应正常，两轨工程加载、12 秒峰值251 MB，
  ERR/FTL 0，正常 exit 0，已退出。`.dsh/offline-preview.json` / `.dsh/offline-startup.log`。
  用户上次预览 PID 16244 本轮构建前已退出。任何 PID 操作都须重新核实精确路径和启动时间。
- 原生插件实际音质/兼容性、真实音源长合成、大工程速度基准尚未验收；没有自行截图。
  内部 DSP 当前仍44100 Hz，输出采样率通过文件重采样实现；没有新增多声道/辅助侧链路由。
- 完整不可变合成输入、独立合成调度器、全局插件销毁仍为长期工作；
  **插件独立实例/参数快照/捕获时间轴和基本 PDC 已完成**，不要下轮再当未实现列出。
- 本轮重新核实 `G:/xklmy文件夹/vibe coding/UTvTU`、origin XKLMY-hi/UTvTU，移动盘下轮仍须核实。

## 2026-10-11 12:15 渲染页功能与视觉完成（最高优先级）

- **产品最终提交 `d3eafe1f`**，分支 `plus-develop`，未推送。本段优先于下方夜间历史；
  最新记录见 [渲染工作区实施与验证](.opencode/plans/render-workspace-2026-10-11.md)。
- 用户已认可欢迎/偏好效果，本轮要求补全渲染页并重设计视觉。**轻量测试偏好继续有效**。
- `bc8a7b92`、`391510c2`：新增 ConfiguredExportSession / ExportDeliveryPlan / ExportAudioOptions，
  混音和分轨均支持选择轨道、整曲/选区/自定义范围、PCM16/PCM24/float32、三档输出采样率、
  内置效果/VST/推子选择和 0–30 秒尾音。混音可含主输出，分轨不含；实际编码/重采样生效。
  捕获交付路径/身份与增益/内置效果参数；轨道/VST 槽位变化拒绝发布；取消失败保留旧文件。
- `c510aaa2`：RenderWindow + 局部 RenderWorkspace 样式，36 DIP 字段、胶囊选择、轻分隔线、
  统一滚动设置及固定底部作业区；真实摘要、校验、开始/取消、进度与打开目录均接通，中英文同步。
  **下方 `9e986b1f` 的渲染页固定规格/锁选项已被本轮取代**。旧直接导出菜单/API 仍保持原规格兼容。
- `d3eafe1f`：建导出链后 Reset 清空 VST 时间域残留；按 Setup 最大帧数分块处理，避免原生桥截断大块
  导致尾段干声；复用缓冲保持稳态零分配；中间文件删除失败不遮盖作业结果。
- SDK 9.0.317 build6 通过：`.dsh/render-workspace-build6.log`，0 错误，有历史警告。
  轻量回归 **46 后端/取消/窗口作业 + 12 模拟 VST + 3 真实 Avalonia 渲染窗口/模板**，互不重叠。
  分组峰值 68/54/134 MB，全部通过；完整 TRX 与限制见记录。UI lint ERROR 0 / WARN 0，diff 检查通过。
- **Windows smoke**：12:12:12 +08，本树 PID 480，加载两轨工程，响应正常，12 秒峰值 247 MB，
  ERR/FTL 0、正常 exit 0，已退出。`.dsh/render-preview.json` / `.dsh/render-startup.log`。
  上次用户预览 PID 2100 本轮开始时也已退出；当前没有本轮留存的预览，任何 PID 操作都须重新核实。
- 用户实机入口：文件 → 渲染…，或主窗口工具栏渲染按钮。没有自行截图、真实歌声推理、VST/设备试听；
  真实导出测试用约 100 ms 导入音频检查文件，模拟桥检查帧边界，不能冒充原生听觉验收。
- 长期架构剩余项仍有效：完整不可变合成输入、独立播放/导出计划器、全局原生插件销毁等。
  本轮仅捕获交付配置与轨道部分参数，不要描述为整个工程模型已经不可变。
- 本轮路径重新核实为 `G:/xklmy文件夹/vibe coding/UTvTU`、origin XKLMY-hi/UTvTU；移动盘下轮仍须核实。

## 2026-10-11 06:06 夜间重构收尾（最高优先级）

- **最终产品提交 `9e986b1f`**，分支 `plus-develop`，尚未推送；本段优先于下方历史状态。
  本段后还有交接文档提交，不代表产品另有变动。
  完整实施及分批 TRX 见 [夜间重构记录](.opencode/plans/overnight-refactor-2026-10-11.md)。
  用户睡前授权自主推进，明确 **轻量测试**；不得默认恢复全量测试或截图自迭代。
- 欢迎/偏好 `62ba1849`：共用 WorkspacePages，232 DIP 导航、36 DIP 字段、22/13 标题层级，
  六个自适应区域保留原控件及真实绑定；`09802d87` 修复动态语言标题，`dc6ab925` 收口音源重载失败。
  `a414fbe6` 只修剪两类文案资源首尾格式空白，解决无头文本布局失控。
- 控件 `b51100e0`、`af8b3b26`：效果行、表达式、卷帘工具、混音动作和展开标签统一 DawIcon/DawButton，
  字体、几何中心、焦点、命中区域落实到真实控件；保持 GitHub 原版胶囊/圆角及品牌原生描边。
- 检查器 `5f9c7c3a`、`f9a6d8ee`、`20746cdd`：数值、滑块、下拉、预设、颤音、曲线形状和音素化器
  全部捕获工程/片段/选择，通过自己的编辑会话提交、取消及恢复；撤销刷新不反写。
  `8532d10a` 修复颤音音量负值范围，`3f75744c` 原子保存音符预设。
- 事务 `27bb42d6`、`b936f930`、`a50fd18e`、`9e8d5c94`：片段/音符/混音共用 DocumentEditGesture，
  拒绝外部事务与旧目标；保存封口后可续开。`5204125a` 区分新片段添加与已有片段修改，修复复制丢片段。
  历史 **No active UndoGroup Move parts** 对应生产路径已经迁移，未真机拖动验收。
  `59fa0fbd`：PointerCaptureLost 是 **Direct**，必须接到真实画布/Thumb/Knob。
- 轨道与机架 `86a25dd6`、`d60995e2`：轨道增删移动复制、命名/颜色/设置/歌手/音素化器/渲染器
  均捕获目标且拥有事务；机架行与槽位回调检查对象身份，独立预览不写工程历史。
  复制预先捕获片段且独立 Clone 渲染设置，支持 AddTrack 通知同步销毁旧视图。
  混音最终销毁不允许延迟重建复活，拒绝输入后推子/声像显示和下一次输入起点恢复模型值。
- 寿命 `016a9c71`、`f92d0c6b`：轨头集合与音符画布订阅/动画有挂载身份；检查器 VM 最终可销毁。
  `d60995e2`：轨道头像替换/销毁释放 Bitmap，混音台释放 VM 与机架宿主。
- 命令 `64ae260d`：CommandRegistry 不再持有 Action<MainWindow>，改用显式编辑/窗口接口上下文；
  静音/独奏/首尾定位移入 MainWindowViewModel，快捷键识别、CanExecute 与 Tunnel/Bubble 去重保留。
  `c946c781` 修复 Publish 改写命令组/工程后的校验重入。
- 保存 `f76eb5bd`：工程、偏好统一临时写入/刷盘/原子替换；失败不推进恢复保存点，可重试。
- 合成 `c47cf1d8`、`504a8169`、`1c09c140`、`515c9897`、`b318901e`：结果/通知按工程与修订在 UI 发布，
  取消源等待前后台作业退出才释放，回调异常隔离。`ecdf8a93` 工程切换按同一寿命规则退休播放取消源。
- 交付 `dad16513`、`410b16ce`：WAV 失败/取消保留上一份完整交付；录制使用独立取消源并拒绝重入，
  新取消重载保留旧二进制入口，离线导出不替换播放电平。`0fe717d2` 修复歌声/音频共轨导出丢音频。
  `7186852d` 导出窗口进度/收尾按窗口与作业身份隔离，关闭后迟到回调无效，取消有独立反馈。
  `9e986b1f` 删除无效选项承诺：混音为全轨 float32、含实时效果和推子；分轨为整曲 PCM16 干轨，
  所选未静音轨道，不含效果/轨道推子/主输出。采样率锁定实际值，忙时锁输入，保留分轨选择。
- **SDK 9.0.317 最终构建通过**：`.dsh/overnight-final9-build.log`，测试项目及全部产品依赖。
  轻量小组：检查器 23、命令/快捷键 42、效果/预设 13、轨道/片段 17、导出/恢复 17、混音复验 6、
  最终导出/取消/窗口/模式 30 全绿；另有布局、画布、保存、控件等已绿小组，详见记录。
  表中小组有重复，不能相加冒充独立总数。每组约 5–14 秒，最终组峰值 57 MB，
  轨道组峰值 151 MB；本轮未跑全量，旧 878 项只对应旧 HEAD `ed63eae2`。
  UI lint **ERROR 0 / WARN 0**、diff 空白检查通过。
- `scripts/Test-Light.ps1` 必须明确 filter，默认 30 秒/384 MB，collection 串行，核对本树路径/启动时间、
  完整 TRX/执行计数/失败及致命输出；不要只看适配器 exit 0。
- **Windows smoke 正常退出**：06:05:29 +08:00，本树 exe，PID 11544，两轨无音符工程加载，
  响应正常、12 秒峰值 248 MB、ERR/FTL 0、CloseMainWindow 后 exit 0，未留下预览进程。
  `.dsh/overnight-startup.log` 和 `.dsh/overnight-preview.json` 记录精确身份。
  下方历史 PID 均不能沿用，后续操作须重新核实路径和启动时间。
- 无自行截图、无真实歌声推理/试听/VST/长导出验收。字体/DPI 与听觉结果仍待用户实机查看。
  **长期架构路线未全部完成**：MainWindow/PianoRoll 全面职责拆分、完整不可变合成输入快照、
  播放/导出独立计划器、原生插件销毁及听觉验收。命令注册表解耦与录制取消隔离已经完成，
  不要下轮重复列为未做，也不要把它们描述成整个工作站架构已经完成。
- 工作目录已核实 `G:/xklmy文件夹/vibe coding/UTvTU`、origin XKLMY-hi/UTvTU；移动盘下轮仍核实。
  当前 Avalonia 12.1.0、自研 MD3+Fluent、SDK 9；AGENTS 的 SukiUI 描述已过时。

## 2026-10-11 最新状态（优先于下方历史记录）

- **控件内部呈现最新批次 `ed63eae2`**：用户指出图标对齐、文字居中与一致性仍未解决。
  主窗口/轨道头/卷帘工具/混音 M/S 接入 `DawControls.axaml` 共用模板，普通图标使用
  `DawIcon` 按真实矢量边界居中；文字模板显式转发字体/颜色/对齐，避免全局样式侵入，
  并修正奇数高度标签布局取整导致的半 DIP 偏移。保留下述 GitHub 原版胶囊/圆角语言与品牌路径。
  SDK 9.0.317 解决方案构建、**95 项相关检查**、UI lint 零错误/警告通过；
  最终全量 **878 通过、0 失败、0 跳过**（4 分 17 秒）。TRX：
  `.dsh/refactor-test-results/presentation-full-20261011.trx`。
  详情见 [控件内部呈现统一](.opencode/plans/control-presentation-2026-10-11.md)。
  **最新可见预览 PID 19820**，2026-10-11 03:43:59 +08:00 启动，两轨工程加载成功、响应正常、
  启动 ERR/FTL 0，保留供用户查看；`.dsh/presentation-preview.json` 记录精确身份。
  下方旧预览 PID 5544 已退出。未自行截图，字体光栅化/DPI 仍交用户实机看；其余控件族继续推进。
- **最新视觉裁决**：用户查看第三批后认为新风格不如以前，明确选择 **GitHub 原版 UTvTU 的视觉**。
  产品调整 `c0729b83`：恢复运输胶囊、圆形动作、圆角读数、tonal 选中胶囊、轨道头柔和形状，
  保留导航/保存/生命周期与控件输入/撤销修复。四组由 `WorkspaceHeader` 按实际宽度排列，
  宽窗一行 56 DIP 内容顶栏、窄窗多 44 DIP 工具行，分隔线另占 1 DIP；菜单回到品牌按钮单宿主。
  状态栏恢复 32 DIP，轨道头圆角标准 12 / 紧凑 6，替代下方第三批的 4。
  SDK 9 构建、69 项相关检查、全量 **868 通过/0 失败/0 跳过**（5 分 19 秒）、
  UI lint 零错误/警告通过；详情与启动结果见
  [原版视觉重新实现](.opencode/plans/original-visual-2026-10-11.md)。
  **原 `md3-daw-2026-10-11` 的三栏/下划线稿不再是产品的外观验收依据**，不要下轮又迁回去。
  当前 Debug 已可见启动、两轨工程完成加载、响应正常，启动 ERR/FTL 0；PID 5544 保留供用户查看。
  进程身份在 `.dsh/original-visual-preview.json`，后续操作须按 exe 路径与启动时间核实，不能仅靠 PID。
- 旧设计依据：`.opencode/design/UTVTU-设计交付/html/` + `png/` 和 GitHub 基线 `b0eb3278`。
  仍先读 HTML 几何/令牌，再看图；保留现有动态 MD3 主题和真实 DAW 控件语义。
- 用户此前预览（PID 15604，已退出）的日志在移动片段时反复报
  `No active UndoGroup Move parts`；退出正常。**此交互错误尚未修复**，与无交互启动 smoke 分开记录。
- 工作区本次核实为 `G:/xklmy文件夹/vibe coding/UTvTU`，移动盘下次仍须核实目录与 remote。
- 主树仍是 `plus-develop`；首批产品 `6f8998e4`、设计 `09c7f292`、第二批产品 `609af7fd`，
  第三批产品 `f56051df`、资源检查修正 `e6d43978`，随后为进度文档提交。
  尚未推送。下方的 `d89cf1b8` / “与远端一致”是旧会话记录，不代表当前 HEAD。
- 用户要求先回到 GitHub 产品基线 `b0eb3278`；回滚前 18 个提交保存在
  `codex/backup-before-github-rollback-20261011`（`2747decb`），不能整体恢复这些改动。
- 方向：**人声制作工作站**，在 UTvTU 上原位重构；`../UTvTU-rebuild` 只作为上游参考，
  上游提交 `5c3c83bb`，不从零另建产品。Windows 为唯一当前验收平台。
- 用户明确授权直接实施，前后端共同推进，允许一个子 agent 设计紧凑 MD3 视觉，保留 DAW 专业性，
  **不再逐阶段询问审批**。仍交付可查看结果；不自行截图产品界面。
- 用户进一步要求 **控件级前端重构**。第三批已落地轨道头五个独立 ControlTheme、共用
  `DawFader` / `DawValueField`，接入轨道与主输出推子；完善键盘/UI Automation、编辑提交与取消、
  撤销预览失效、重挂读模型、选中反馈和最矮轨头的整卡布局。控件负责输入与呈现，宿主负责命令/音频。
  SDK 9.0.317 构建、新增 16 个控件测试、资源检查 5 项复验通过；最终全量
  **866 通过、0 失败、0 跳过**（3 分 55 秒），UI lint **ERROR 0 / WARN 0**。
  结果 `.dsh/refactor-test-results/controls-final2-20261011.trx`。
- 已实施首批：统一混音编辑会话、命令、拖动撤销、跨视图同步、效果预览/取消、失败保存、旧窗口隔离。
  第二批：应用壳/侧栏紧凑 MD3 视觉、歌声编辑可用入口、旧导航续体失效、偏好/编辑器生命周期、
  失败保存禁止退出与错误成功提示。SDK **9.0.317** 构建成功；首批 **824 测试全绿**。
  第二批新增 26 个用例；针对性复验 36 全绿，最终全量 **850 通过、0 失败、0 跳过**（4 分 5 秒）。
  UI lint **ERROR 0 / WARN 0**；最终结果 `.dsh/refactor-test-results/workspace-final2-20261011.trx`。
- 当前技术栈是 .NET 8 / Avalonia 12.1.0 / 自研 MD3 + Fluent；AGENTS 的 SukiUI 现状描述已过时。
- 视觉子 agent 已交付 `.opencode/design/md3-daw-2026-10-11/` 的 HTML + 7 张 PNG + 规格，
  **应用壳和既有侧栏视觉已迁入产品**（30/60/40/25 DIP）；完整检查器、歌声编辑画布和混音台视觉
  尚未迁入。第三批轨头模板、紧凑动作栏和混音输入控件已迁入；混音表/刻度仍为既有 500 DIP，
  dB 范围 -24..+12，没有套用原型展示范围。HTML 令牌和 SVG 几何为后续迁移依据，品牌路径保持原样。
- Windows 两轨测试工程启动/加载及正常退出成功，第二批启动日志 **ERR/FTL 0**，
  缺少 `Samples` 目录已按空素材库处理，消除首批的目录错误。
  桌面控制工具拒绝读取 OpenUTAU（未获工具授权）；未驱动实机推子或验证真实音频听觉。
  交互行为以 Avalonia 无头测试验证；未实机操作异步导航、分离窗口或听觉/导出验收。
  未截图产品。测试启动实例已正常退出。
- 第三批 Windows 两轨工程亦已正常加载/响应并正常退出，启动/退出日志 ERR/FTL 0。
  `.dsh/controls-20261011-startup.log` 保存本次日志；未实机驱动控件，也未截图产品。
- 下一步先看 [第三批控件实施与验证](.opencode/plans/control-refactor-2026-10-11.md)、
  [第二批实施与验证](.opencode/plans/workspace-session-2026-10-11.md)、
  [首批实施与验证](.opencode/plans/mix-editing-2026-10-11.md)、
  [共同重构路线](.opencode/plans/utvtu-in-place-refactor.md) 与
  [设计规格](.opencode/design/md3-daw-2026-10-11/README.md)，按控件族推进歌声编辑、检查器、效果器，
  结合合成/音频工作流继续职责迁移。TrackHeaderCanvas 的全局订阅寿命、其它数值入口统一仍需审查。
  F05/F06 主要缺陷已处理；完整窗口职责拆分、其余视觉、合成/音频重构、自动保存失败与原子写入
  仍未完成。不要重新从零探索仓库。
- 构建须等并行 XAML 写入全部冻结；本轮曾在样式写入过程中构建，导致资源包偏移失配、
  多个正常 XAML 报无关的根节点/未闭合错误。源码 XML 确认有效后，用 SDK 9 的 `-t:Rebuild`
  刷新生成资源即可，不能以此重写无关视图或运行构建失败后产出的 dll。

本机存在 SDK 10；遵守 SDK 9 构建限制时直接指定 MSBuild，不新增 global.json：

```powershell
dotnet 'C:\Program Files\dotnet\sdk\9.0.317\MSBuild.dll' OpenUtau.sln -restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts= -verbosity:minimal
dotnet test OpenUtau.Test/OpenUtau.Test.csproj --no-build
```

下方保留 2026-10-10 历史资料，涉及分支/HEAD/基线数字时以上述最新状态为准。

> **新会话第一件事：读这个文件。** 它包含路径核实方法、分支/工作树实况、已完成与未完成、验证命令与基线、项目铁律、已知陷阱。
> 读完即可开工，**不要重新探索仓库**（本仓库的队友反复因"从零摸索"烧完上下文）。

---

## 1. 工作区与远端（**先核实路径**）

⚠️ 工作区在**移动硬盘**上，盘符会变（历史出现 D:/E:/F:/G:）。**每次会话开始先核实**：

```powershell
pwd                                    # 期望：<盘符>:\xklmy文件夹\vibe coding\UTvTU
git remote -v                          # 期望见下面两个 remote
git log --oneline -1                   # 期望 HEAD 见 §2
```

| remote | URL | 用途 |
|---|---|---|
| `origin` | https://github.com/XKLMY-hi/UTvTU.git | **推送目标**（push） |
| `upstream` | https://github.com/openutau/OpenUtau.git | 上游，**只 fetch**（push 已禁用） |

网络不稳（push 常 `Connection was reset`）⇒ 重试 3–5 次；用户开加速器后一次成功。

## 2. 分支模型与当前状态

| 分支 | 角色 | 当前 |
|---|---|---|
| `master` | 与 `upstream/master` 同步的镜像 | `29e0e16d`（落后 plus-develop 615，正常） |
| **`plus-develop`** | **所有 UTvTU 开发都在这里** | **`d89cf1b8`，已推 origin，与远端一致** |
| `try/*`（20 条） | 历史特性/验证分支 | **仅 2 条有未并入提交**（见下） |

**未并入的提交（全部实况，`git log plus-develop..<branch>` 实测）**：
- `try/fx-ctl` → `e1cdf019` "feat(welcome): S1 重建骨架"（**已被主树 W50 重建取代**，无需并入）
- `try/mx-strip` → `e8c88475` "merge(plan): 并入 plus-develop 作 S1'a 基线"（纯合并提交，无独有内容）

⇒ **没有遗留的有价值未合并工作**；其余 `try/*` 全部已并入 `plus-develop`。

## 3. 工作树（`git worktree list` 实测）

| 目录 | 分支 | HEAD |
|---|---|---|
| `UTvTU`（**主树，在此工作**） | `plus-develop` | `d89cf1b8` |
| `UTvTU-cmd` | `try/cmd` | `75212caf` |
| `UTvTU-fx-ctl` | `try/fx-ctl` | `e1cdf019`（+1 未并入，见 §2） |
| `UTvTU-mx-int` | `try/mx-int` | `3de31e8a` |
| `UTvTU-mx-strip` | `try/mx-strip` | `e8c88475` |
| `UTvTU-up-core` | `try/up-core` | `5d622255` |
| `UTvTU-up-piano` | `try/up-piano` | `e985eb65` |
| `UTvTU-ustxp` | `try/ustxp` | `62f2b151` |
| `UTvTU-welcome` | `try/welcome` | `a382eaf7` |

**磁盘上还有 `UTvTU-mx-lib` 目录，但不在 worktree 列表里** ⇒ 残留目录（其分支 `try/mx-lib` 已并入），可安全删除或 `git worktree prune` 后处理。

> ⚠️ **教训（本次会话的真实误判）**：队友可能在**自己的工作树**里干活，主树看不到 ⇒ 判定"某队友零产出"前**必须先 `git worktree list` + 逐个 `git log`**，否则会误停正在干活的人。

## 4. 最近完成（本次会话，均已推）

| 提交 | 内容 |
|---|---|
| `d89cf1b8` | 本交接文档 + 状态外化 |
| `9349d35a` | 欢迎页独立复核三修（选中药丸文字色 / 波形带弹性 / `railLink` 字号）+ 防回潮断言 |
| `ff66e3cc` | 搜索语义抽成纯函数 `WelcomeArt.FilterRecent` + 8 断言 |
| `ee8652cf` | **欢迎页 W50 重建为 MD3**（304 药丸导航 / 页头 28+14 / 最近行 56 / assist chip / 主题按钮 / 空态容器） |
| `b607c57a` | AGENTS.md 并行协作铁律（禁破坏性撤销 / 绿灯即提交 / dirty 先问 / 一文件一写入者） |
| `e4787ed5` | 欢迎页搜索 VM 层 + 41 个双语键（检查点提交，抢救事故幸存成果） |
| `d84d6b0c` | 左导航词汇提取到应用级共享层 |
| `64933387` | 「不碰布局」契约精确化（只约束裸元素选择器） |
| `24b95d32`/`096399dc`/`239d8924`/`2cfea6d0`… | **自研 MD3 安装程序**（单文件 227.5 MB，载荷内嵌；覆盖旧版 OpenUTAU Plus） |
| `ca280779` | README 加「下载」段 |

**发布**：GitHub Release **`UTvTU-26.10.7-150135`**（tag 同名）挂单文件安装器 `UTvTU-26.10.7-150135.exe`（238,515,604 B，SHA256 `6b6ced14…ac6d`）。
发布命名约定：**`UTvTU-<yy.M.d>-<HHmmss>`**（构建时刻版本号，csproj 顶层 PropertyGroup 求值；见 §8）。

## 5. 未完成（任务板实况）

| 任务 | 内容 | 状态 | 负责人 | 备注 |
|---|---|---|---|---|
| `task-37` | W8 `.ustxp` 格式止血（**P0 丢数据级**：上游重存丢 Plus 字段） | in_progress | m2-view | 树 `UTvTU-ustxp`（`try/ustxp`，已并入部分） |
| `task-26` | W14 上游 Core/引擎选择性合并（232 提交分诊） | in_progress | fx-core | 树 `UTvTU-up-core` |
| `task-19` | W8 `.ustxp` 结构审计（只读 + 提案） | pending | 未派 | 产出 `.opencode/plans/ustxp-format-audit.md` |
| `task-21` | W9 修崩溃：分离按钮 `InvalidateArrange on wrong LayoutManager`（整机退出级） | pending | 未派 | 需真机复现 |
| `task-3`/`task-4` | effect-rack 历史集成/验证 | pending | — | 疑似过时，动前复核 |

## 6. 构建 / 测试 / 校验（**离线沙箱专用开关，缺一即假失败**）

```powershell
# 1) 还原（顺序不可反：sln 先、VstProbe 后）
dotnet restore OpenUtau.sln -m:1 -p:TreatWarningsAsErrors=false --ignore-failed-sources
dotnet restore VstProbe\VstProbe.csproj -m:1 -p:RuntimeIdentifiers= -p:TreatWarningsAsErrors=false --ignore-failed-sources
# 2) 构建（--no-restore 保 TreatWarningsAsErrors 原样；必须 -m:1 + UsedAvaloniaProducts=）
dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
# 3) 测试（判据：全绿；需完整权限，普通沙箱会 Win32Exception(5)）
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build
# 4) UI 规范检查
pwsh -File .dsh\fx\ui-lint.ps1 -Root "<仓库绝对路径>"
```

**当前基线**：构建 **0 错误** ｜ 全量 **813/813** ｜ `ui-lint` **ERROR 0 / WARN 0**。
每次改动后的判据看「**个错误**」行（Avalonia XAML 错形如 `: Avalonia error AVLN1001:`）。
⚠️ 已知偶发：全量曾出现 **1 次单例失败、重跑即过**（未定名，历史上 Light 变体有 flake）。

## 7. 真机验证与截图工具（`.dsh/fx/`，全部可用）

| 工具 | 用途 |
|---|---|
| `launch.ps1 -Exe <exe>` | 按**精确路径**启动（单实例按 exe 路径判，多树可共存） |
| `capture.ps1 -ProcessId <pid> -Out <png>` | PrintWindow 截窗口，**不需要前台** |
| `click.ps1 -ProcessId <pid> -X -Y` | 窗口相对点击（`-Screen` 表示屏幕绝对） |
| `sendkeys.ps1 -ProcessId <pid> -Text "…"` | ⚠ **打不进非前台窗口**（实测）⇒ 优先用 setvalue |
| **`setvalue.ps1 -ProcessId <pid> -Value "…"`** | **UIA `ValuePattern` 直接给文本框设值**（可靠替代打字） |
| **`resize.ps1 -ProcessId <pid> -Width -Height [-X -Y]`** | Win32 `MoveWindow`，做**最小窗口**验证 |
| `close.ps1 -ProcessId <pid>` | **必须用它优雅关闭**（否则下次出现"异常恢复"横幅） |
| `uia.ps1 -Mode list\|invoke [-Filter -Rect]` | 读 UIA 树 / 调用元素（Avalonia 菜单项常无 InvokePattern） |
| `ui-lint.ps1` / `checks.ps1` | UI 规范 / 综合检查 |

**截图存放**：`.dsh/fx/shots/**`（gitignore）。本会话产出：`welcome/w50-*.png`（欢迎页 6 张 + 窄窗）、`installer/*.png`。
应用日志：各树 `OpenUtau\bin\Debug\net8.0-windows\Logs\log*.txt`。

## 8. 项目铁律与用户偏好（**先读，别踩**）

**平台范围**：**Windows 是唯一目标平台**（用户：「跨平台我觉得目前不是重点」）⇒ 不再投入 macOS/Linux；新功能只需在 Windows 验证。

**设计稿格式**：用户交稿一律「**图像 + HTML**」；**HTML 是几何与令牌的权威源**（`<svg viewBox><path d=…>` 逐字引用），禁止从 PNG 描摹、禁止"描边→填充"式转换。

**并行协作铁律（AGENTS.md，事故后新增）**：① 禁 `git checkout -- <file>` / `reset --hard` / `stash` / `clean` 用于撤销自己的改动（**本仓曾因此丢掉一整版页面**）；撤销只能精确反向编辑，或先 `git diff` 确认归属；② **每完成一个绿灯步骤立刻 commit**，绝不把成品留在未提交状态；③ 动手前 `git status --short`，文件 dirty 且含他人改动 ⇒ 先问 Lead；④ **同一文件同一时刻只有一个写入者**。

**版本号**：`UTvTU-<yy.M.d>-<HHmmss>`，写进 `InformationalVersion`。⚠ 两个坑：必须在**项目求值阶段**（顶层 `PropertyGroup` 的属性函数）赋值（放进 Target 会被 `GenerateAssemblyInfo` 提前捕获 ⇒ 不生效）；且必须 `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`（否则 SDK 追加 `+<git sha>` 盖掉）。

**用户工作方式（原话）**：
- 「**切分就是，我说做什么你做什么**」⇒ 不要擅自扩大动作范围（本会话因"没先给方案就实现"被批评两次）
- 「**一般修改你让我看就行了，当我要你自迭代的时候再自己看**」⇒ 常规改完**交他实机看**；他明确说"自迭代"才自己反复截图核对
- 「**可以用 computer use 来验证结果**」⇒ 验证允许真机操作
- 「**大哥你省点token啊**」⇒ 输出克制，别贴长清单
- 让他**拍板**时给**明确推荐**（他讨厌开放式提问）

**其它**：仓库**公开**（`XKLMY-hi/UTvTU`），旧仓 `XKLMY-hi/OpenUTAU-Plus` 为历史存档；用户数据目录是 `OpenUtau Plus`（**绝不改名**，改名=丢用户数据）。

## 9. 已知陷阱清单（实测）

1. **应用级样式压过一切**：`Styles.axaml:110 TextBlock{FontSize 13}` 与 `Md3InputThemes` 的 `TextBlock{Foreground}` 是**显式赋值 ⇒ 压过继承**。凡"设在 Button/容器上、期望子元素跟随"的字号/前景**都无效**，必须 `<Style Selector="X TextBlock">` 显式声明。
   **仍被压掉的点位（用户 2026-10-10 裁决"其他都别动"，未修）**：`MainWindow.axaml` `Button.viewTab` 11→**13**、`Button.libTab` 10→**13**（顶栏，最显眼）；`RenderWindow.axaml` `RadioButton.renderOption` 12→13、`Button.secondaryBtn` 11→13；`MixFxDialog.axaml` `ToggleButton.md3switch` 12→13；低-中：`Styles.axaml` `MenuItem` 12、`Md3InputThemes` Menu/Tab。
   ⚠ `MainWindow.axaml` 是 `task-37`（m2-view）名义写范围 ⇒ 动前协调。
2. **UI 控件属性只能在 UI 线程读**：后台线程读 `CheckBox.IsChecked` 会 `InvalidOperationException`（安装器实测）⇒ 切线程前先取到局部变量。
3. **`Watermark` 已废弃**（Avalonia 12）⇒ 用 `PlaceholderText`。
4. **最近列表禁用 `ListBox`**：全局隐式 `ListBoxItem{Height=28}` 会把 56 行夹扁 ⇒ 用 `ScrollViewer + ItemsControl`。
5. **单文件安装器的自删护栏**：从别处运行 `--uninstall` 时，自删逻辑会删掉"运行中的那个 exe"（构建产物）⇒ 仅在"自身位于被卸载目录内"时自删。
6. **GitHub 单文件上限 100 MB**：`dist/payload.zip`（183 MB）曾导致 push 被拒 ⇒ 已 gitignore，分发只出单文件安装器。
7. **队友上限 8 已满、且无删除/重置工具** ⇒ 新活只能：Lead 自己做（最可靠）／派最轻的队友／用户手动腾位后 spawn。
8. **`gh` 默认会解析到 `upstream`**（本仓有两个 remote）⇒ `gh release create` 等必须显式 `--repo XKLMY-hi/UTvTU`。

## 10. 建议的接手顺序

1. **`task-37`** `.ustxp` 丢数据级 P0（用户工程文件安全）—— 属 m2-view 名义范围，先协调或 Lead 接
2. **`task-21`** 分离按钮崩溃（整机退出级）
3. **`task-26`** 上游 Core 选择性合并（价值最高、工作量最大）
4. **§9.1 字号陷阱 5 处**（体验细节，需用户再授权）
5. 清理：`UTvTU-mx-lib` 残留目录、§2 两条无价值未并入分支
