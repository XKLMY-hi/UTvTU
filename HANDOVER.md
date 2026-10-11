# HANDOVER — 新会话交接（2026-10-11 工作区整合交付）

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
