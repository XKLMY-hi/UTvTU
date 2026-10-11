# UTvTU MD3 设计风格审查 · 2026-10-11

用户要求审查控件、设计、动效与动态取色，并获取 Google 几何设计实例。审查以产品提交 `dc220e2e` 为基础；当前分支 plus-develop，实际路径与 origin 已核实。本次只整理审查和官方参考资料，不修改产品或执行重构。

## 判断

UTvTU 已有真实的 MD3 动态配色基础。主要缺口在颜色角色如何服务操作层级、控件如何共同表达状态，以及形状与动效如何形成统一规则。现有 DAW 的紧凑布局和编辑功能值得保留，在其上收敛设计系统，比再一次逐页改皮肤更合适。

MD3 的动态色彩、常规控件规范与 M3 Expressive 的新形状和物理动效需要分别审查。代码中的 `Md3SchemeVariant.Expressive` 只是一种配色方案，选择它不会自动获得 Expressive 的控件、布局或动效。

## 已有基础

- `OpenUtau.Core/Theming/` 包含 HCT/CAM16、动态色角色、方案变体和对比度计算；`Md3RoleTable.g.cs:1` 标明来自 Google material-color-utilities **0.3.0** 的 49 个角色。`tools/md3-codegen/` 有生成工具，测试已有 Google 参考 oracle。现有来源需要保留，升级时再核对参考版本。
- `OpenUtau/Theming/Md3ColorPool.cs` 计算明暗方案；`Md3ThemeResources` 安装动态资源。`Colors/Brushes.axaml` 中许多兼容键已映射到 MD3 角色，不能把它们都当作固定旧色。
- `Styles/DawControls.axaml:24` 已有状态层、悬停 / 按下反馈和焦点环；欢迎页强调按钮、渲染主动作、导航选中与 MixFx 的分模块色彩已有较好的局部实现。下一步应扩大共同规则的覆盖范围。

## 发现与证据

| 优先级 | 问题 | 源码证据与影响 | 推荐处理 |
|---|---|---|---|
| P1 | 动态色来源与个性化入口不足 | `App.axaml.cs:119`、`ViewModels/PreferencesViewModel.cs:148–159` 固定 Tonal Spot 与六个种子选项；Core 支持更多方案和 ContrastLevel，产品调用没有开放。尚未发现壁纸 / 图像量化与取色来源选择入口。 | 默认品牌种子；可选用户色 / Windows 强调色。图片取色后续明确授权选择来源，保存稳定主题，不随音源选中自动跳色。 |
| P1 | 操作强调缺少全局语义 | `Styles/ToolWorkspace.axaml:3–25` 把工具按钮收为同类文字主题、圆角 999，toolAction 使用中性容器。主要确认、普通操作和工具动作不能仅靠同一填充表达。已有 Render / Welcome 的强调变体是可复用起点。 | 收敛 filled / tonal / outlined / text / icon / toggle 语义，再分别支持 DAW 紧凑密度与页面密度。每个任务流明确主要动作和取消。 |
| P1 | 减少动效覆盖不完整 | `Controls/WindowEx.cs:22,40` 在构造时设置 no-motion；`Md3Transitions.axaml:57` 只关闭 fade/pop 的 Transitions。该文件菜单 Style.Animations 和 `Md3SelectionThemes.axaml` 的控件过渡不在这条规则内。浮层宿主自身已检查减少动效。 | 建立共同动效策略，覆盖菜单、选择控件、视图和任务反馈；偏好修改能作用到现存窗口。当前是源码覆盖缺口，尚未做实机复现。 |
| P2 | 状态反馈在多个主题间分叉 | `DawControls.axaml:36–40` 使用前景状态层；`Md3ControlThemes.axaml:35` 有另一套刷子过渡、填充替换与 opacity 反馈；WorkspacePages 又有局部主题。 | 用同一状态模型管理 hover / pressed / selected / focus / disabled；变体改变语义配色与尺寸，避免复制模板。 |
| P2 | 形状与文字尺度散落 | 三组 XAML 直接出现大量 8 / 12 / 999 圆角和局部字号；没有形成共享的形状 / 标题 / 正文 / 标签角色。未发现 MaterialShapes 或 Morph 实现。 | 定义按用途划分的形状、排版和密度令牌，再引入有意义的几何图形。不要让每个控件都变成大胶囊。 |
| P2 | 动效有实现，缺少分类与连续性 | `Md3Transitions.axaml:21–23` 定义 fast/base/view 三个令牌，仓库搜索仅命中定义；实际过渡仍写死 200ms。主要弹层使用淡入 / 缩放，菜单另有 120ms；控件还存在滑块等过渡。尚无 spatial / effects 分类与统一速度策略，底部工具收起直接改行高。 | 先统一时长 / 策略与减少动效，再补面板展开、导航与任务状态的空间连续性。使用低弹性的 Standard 思路，评估 Avalonia 实现，不盲目加入每帧布局变化。 |

本次统计扫描 Views **38**、Controls **20**、Styles **13** 个 XAML。Views 中直接 primary 引用 34 次，secondary-container 3 次、tertiary-container 2 次、primary-container 0 次；Controls 中存在 primary-container，Styles 也有大量导航 secondary-container。说明页面直接表达偏向中性色与 primary，但不能据此断言整个运行界面没有容器色。

这些计数去除了注释，仅包含直接 `md3.*` 引用，**排除了兼容别名、C# 绘制与模板最终值**，不能当作覆盖率、像素面积、对比度或界面色彩丰富度。运行时视觉由用户实机检查，本次不自行截产品图。

## Google 官方实例与对应价值

| 官方资料 | 实际取得内容 | UTvTU 可参考的设计关系 |
|---|---|---|
| [Reply / M3 in Compose](https://developer.android.com/develop/ui/compose/designsystems/material3) | 完整邮件应用原图、默认 / 动态配色对照原图；[完整应用源码](https://github.com/android/compose-samples/tree/main/Reply) | 同一结构使用成套颜色角色；主操作、选中项与内容面分别承担层级。Android 12 壁纸机制仅是平台实例。 |
| [MaterialShapes](https://developer.android.com/reference/kotlin/androidx/compose/material3/MaterialShapes) | 35 图形官方原图、[原生 Kotlin 源码快照](../design/md3-style-audit-2026-10-11/assets/MaterialShapes.kt.txt) | 归一化图形和 Morph 入口；原生参数控制几何，不从 PNG 描摹。API 标注实验性。 |
| [Google Expressive 研究](https://design.google/library/expressive-material-design-google-research?pubDate=20250521) | 界面案例、发送动作前后原图链接 | 强调层级帮助识别主要操作；表达强度必须匹配任务。研究结论不能直接当作 DAW 的可用性证明。 |
| [Google motion](https://m3.material.io/styles/motion) | 官方物理动效规范与实例入口 | spatial / effects 各有 fast / default / slow；Standard 适合工具性产品，弹性更少。 |
| [颜色角色](https://m3.material.io/styles/color/the-color-system) / [动态色实践](https://codelabs.developers.google.com/codelabs/apply-dynamic-color) | 官方角色、色调关系与接入指导 | 设计依据是角色及其明度关系，而不是把每个表面都染成同一种强调色。 |
| [按钮组](https://m3.material.io/components/button-groups/overview) / [Material Symbols](https://developers.google.com/fonts/docs/material_symbols) | 官方组件与图标规范入口 | 分组关系、选择状态与光学尺寸。无需为了参考 Google 而全部替换项目现有图标。 |

原图及几何源码的出处、抓取日期、尺寸与 SHA-256 在参考板目录保存。HTML + PNG 是官方参考汇编；不是新产品方案。研究文章图片仅链接 Google 原地址。没有复制第三方仿稿或把自制插图标成 Google 官方实例。

## 推荐的 DAW 方向

| 区域 | 动态色与形状 | 动效 |
|---|---|---|
| 时间轴 / 卷帘 / 自动化 / 数值编辑 | 中性内容面、清晰选中和焦点；轨道、音高、告警及电平具有独立数据语义。时间几何保持精确。 | 拖拽、旋钮与读数即时反馈；不弹跳、延迟或平滑真实音频读数。 |
| 工具栏 / 导航 / 偏好 / 资源 | 统一控件变体；分组容器、选中项与主要操作使用成对角色。圆角和密度由用途决定。 | 短效果变化；面板切换有明确方向和空间关系，保持焦点及操作目标稳定。 |
| 欢迎 / 空状态 / 渲染结果 | 可引入少量官方几何语法；品牌图形保留自身原生路径。大面积形状服务状态，避免堆叠装饰。 | 克制的进入与状态过渡；任务完成一次反馈，后台推理期间避免持续高开销装饰动画。 |

配色源是“由什么生成主题”；语义角色是“主题如何分配到操作和内容”。两者都需处理。Windows 动态色不要求复制 Android 壁纸机制，建议先提供稳定、可解释的来源政策。

## 下一轮实施顺序与验收

1. **共同令牌和控件图库**：用现有真实 ColorPool 生成三种种子的明 / 暗角色对照；先展示主要 / 次要 / 普通 / 危险动作、图标按钮、文本输入、菜单、导航、焦点与禁用态的 HTML + PNG。固定轨道与电平语义，验证成对颜色和正文对比度，不能从本次静态计数宣称已合格。
2. **主题来源和语义变体**：开放明确的取色来源，集中主题重建；收敛默认 / DAW 紧凑尺寸，并逐页使用变体，优先工具确认与渲染流。原操作、快捷键、编辑模型和无障碍入口保留。
3. **共同 motion 策略**：空间 / 效果分类和统一速度；覆盖全局减少动效、快速反复开关、动画中断、焦点恢复。若需物理弹簧，先做单控件受限验证，避免重现自定义 Transform 重置闪烁事故。
4. **表达图形落到有限场景**：先欢迎 / 空状态 / 渲染结果。用原生源码参数或官方原生矢量实现，提供几何对照；禁止直接描摹官方 PNG、禁止擅改品牌描边。

产品修改后继续单节点构建与相关轻量检查，交用户实机看界面；不为此次资料审查跑全量测试、启动新推理或重建产品。参考 HTML 仅用一个独立 headless 浏览器渲染，确认原图加载、哈希、本地链接与 1440 / 800 无溢出。

## 交付

- [官方参考板 HTML](../design/md3-style-audit-2026-10-11/index.html)
- [官方参考板 PNG](../design/md3-style-audit-2026-10-11/reference-board.png)
- [出处与原图哈希](../design/md3-style-audit-2026-10-11/references.json)
- [可复现静态统计](../design/md3-style-audit-2026-10-11/source-metrics.json)
- [轻量参考板校验](../design/md3-style-audit-2026-10-11/validation.json)
