# 上游更新审查（2026-10-17）

结论：继续选择性移植，不整体合并 `upstream/master`。优先吸收缺陷修复和合成兼容能力，保留 UTvTU 的 MD3、VST 机架、导出、安装和播放结构。

## 范围与证据

- 工作区已核实为 `G:/xklmy文件夹/vibe coding/UTvTU`，`plus-develop`，审查起点 `38c27d49`，工作树干净。
- `git fetch upstream master` 将远端跟踪引用从 `a06ab28b0ae011bd0b85926f3044572be46a7a63` 更新到 `fd8c843e323262a57f3da2d86fbb740369216dc3`（最新提交2026-10-16）。本次新增31个提交、84个文件。
- 与共同祖先 `29e0e16d` 相比，上游有263个提交不在本地祖先链中。**这不是263个尚未吸收的功能**：此前许多修复通过 cherry-pick 或按本地架构重写，不能仅凭提交计数判缺失。旧分诊及执行记录见 [upstream-triage-2026-10.md](upstream-triage-2026-10.md)。
- 本轮逐项分诊新增31个提交；关键修复对照本地函数、前置类及历史移植记录。全部补丁进行了只读 `git apply --check`：6项文本可应用，25项需要适配。**可应用仅表示文本匹配，不表示编译、音频行为或性能已验收。** 详见 [31项清单](upstream-review-2026-10-17/commits.json)。
- Git拉取及GitHub API已确认合并提交。网页抓取可能缓存旧PR状态，合并状态以实际 fetched commit 与 GitHub API 为准。未改产品源码、未切换或合并分支、未替换DLL、未构建或运行音频测试。

## 最值得吸收的内容

| 优先级 | 上游提交 | 本地核对与建议 |
|---|---|---|
| 高：稳定性 | [`799ba8de` / #2494](https://github.com/openutau/OpenUtau/commit/799ba8de27c19fb305b1d3c989f1823ad4a8e605) + [`4798078a` / #2503](https://github.com/openutau/OpenUtau/commit/4798078af356ee53011d554eb605195b50305fbe) | DirectML创建、推理与释放统一同步；显式持有并释放SessionOptions。当前Onnx工厂直接传临时options，DiffSinger创建器没有统一SessionLock，渲染/释放主要各锁模型对象，有移植价值。先补旧`f39fc706`的每歌手生命周期保护，再按本地模型覆盖DirectML调用，统一锁顺序，保留CPU行为。不引入本地不存在的Hnsep/Hifi/GAME后端。这里是代码审查，未复现本机GPU崩溃。 |
| 高：兼容性 | [`fd8c843e` / #2451](https://github.com/openutau/OpenUtau/commit/fd8c843e323262a57f3da2d86fbb740369216dc3) | 新DiffSinger分组时长模型需要`word_div`/`word_dur`。本地仅给linguistic提供这两个张量，duration未提供，DsConfig也无`dur_type`。移植`rel`/`abs`/未声明时探测输入的逻辑，保留严格输入校验和旧模型路径；配置编辑器的YamlValues等前置类本地不存在，不照搬界面注解。 |
| 高：明确本地缺陷 | [`8fdb4e9b`](https://github.com/openutau/OpenUtau/commit/8fdb4e9b03e8f4d8dc73822cf7be7ad9d972c603) | SearchBar每次Show新建订阅DocManager的VM，关闭/ESC仅隐藏，旧VM仍在LoadPart后Search/Next。补丁文本可应用，修复与MD3外观无关；补关闭、ESC、反复打开及载入片段的轻量回归。 |
| 高：撤回有问题的旧移植 | [`fa2a670c` / #2516](https://github.com/openutau/OpenUtau/commit/fa2a670c14a4329eac413b76e28732c1c20e43d4) | 上游撤回ThaiVCCV #2461，PR指出原更新破坏泰语音素化。本地先前已吸收#2461，当前文件与此次revert的父版本一致；因此这条回退对本地适用，应纳入首批并用泰语样例确认。 |
| 高：英语音素化 | [`8879fbed` / #2517](https://github.com/openutau/OpenUtau/commit/8879fbedc313805c801c7810b4d4ddb5c77f1e74) → [`7b6cae8b` / #2527](https://github.com/openutau/OpenUtau/commit/7b6cae8b314c1be06efa5cf72cac8f532ff33daf) | C+V别名及过激fallback修复应按顺序成组移植。本地EnglishCpV与第一条父版本完全一致；后一条包含三类声库夹具及测试，不应只取前一条的过渡fallback。约6.8万新增行大多是两份文本字典，不是等量的业务重构。 |
| 高：英语/英转日 | [`5714d927` / #2531](https://github.com/openutau/OpenUtau/commit/5714d927d05fb6052529716480806f78df8d3fa2) + [`f923284d` / #2498](https://github.com/openutau/OpenUtau/commit/f923284d7cbfb4db6ac4b72deef5483ea90f3e82) | VV双元音、Arpasing fallback顺序及EN2JA元音/辅音簇处理。文本可应用；EN2JA仍有本地额外修改，保留测试钩子，按音素化器小组验收，不能整体覆盖插件文件。 |
| 中：第三方渲染器 | [`2913eeb9`](https://github.com/openutau/OpenUtau/commit/2913eeb963bade267f3c1ea953321417c45742ac) | 防外部重采样器与内置同名而覆盖映射。本地也用名称字典覆盖写入，值得改；本地没有Hifisampler，只围绕已有内置集合移植。 |
| 中：第三方渲染器 | [`8945ee38` / #1768](https://github.com/openutau/OpenUtau/commit/8945ee38832203caafccbd85ab4375783dc630f1) + [`5c3c83bb`](https://github.com/openutau/OpenUtau/commit/5c3c83bbeced96718e85e4a96d127de3287cd14b) | 允许manifest声明附加分析缓存文件。必须同时使用后者的空安全读取，保留现有CopySourceTemp/CopyBackMetaFiles入口及共享缓存锁。当前本地未引入该`files`功能，也没有前者造成的强转空引用，因此后者不是本地现成崩溃修复，而是引入功能时必须携带的修正。 |
| 中：编辑体验 | [`87e2b6bb` / #2491](https://github.com/openutau/OpenUtau/commit/87e2b6bbd09fdb0d722d46d1eb4d3ca90209b4d8) | 音高点工具支持右键取消音符选择。本地右键工具判断仍缺PitchPointTool；按本地手势和菜单流程加一项，不复制上游整个控件。 |
| 中：传统声库调声 | [`b30d3347`](https://github.com/openutau/OpenUtau/commit/b30d33473b9d211ce59fb49d8f8255847639e5dc) | preutter覆盖可向前延伸到采样文件起点。文本可应用，但修改offset/consonant/cutoff及直通片段，属于音色与边界行为变化。须单独验证velocity、负cutoff、文件起点及现有原生Worldline防护；不与简单UI修复混一批。 |
| 后续：VOICEVOX | `9a5be152` / #2501 + `0a7cde87` / #2509 | 音高加载范围、静音padding和合成帧对齐有价值。本地VOICEVOX较上游更早，缺部分渲染/表达式图契约，需提取共同时间轴语义并保留本地IRenderer，不能直接摘两个commit。 |
| 后续：英语VCCV | `9fe0e923` / #2497 | 有实际音素化改进，但本地文件与上游前置版本有分歧，并有Plus测试钩子；按函数移植并同步模板，独立专项验证。 |

## 暂缓或不直接取的新增项

| 提交 | 决定与原因 |
|---|---|
| `dc9270f8` 播放/预渲染同通道 | 上游针对MixPlanner发布相互接管。本地用WaveMix/RenderGate/RenderCancellationLease、独立导出及播放中跳过预渲染，结构不同；保留意图作为并发回归参考，不直接改现有稳定管线。 |
| `4ef547c6` SVP音频载入修复 | 本地无SVP导入器且Formats未注册SVP。先引入旧`17cb6642`及对应格式入口，再带上这条修复和缺失音频样例，属于新增格式能力。 |
| `99f2f25f` 缺失包一键安装 | 本地缺PackageRequirements及部分新后端。适合以后接入当前资源中心，不能移入上游MainWindow的大段交互来替换现有宿主。 |
| `13184085` 菲律宾语G2P | 目标`sh`赋值分支本地不存在，旧规则使用`sy`，不是简单的一行本地修复；如要统一新版规则，先引入前置变化并用词例确认，当前不摘。 |
| `e92d5e56` 默认WORLDLINE-R1.1 | 本地没有上游新C# R1.1/Hifisampler实现，不应加一个不能正确落地的默认选项。原生重构属于独立事项。 |
| `89095ff5` 波形新画法、`4bd4d129` 视口hover、`7f3d7c1b` 轨头rounding | 相关控件本地已重写；波形还有上游WavePeaks等前置类。只有用户提出具体交互问题时再取行为或算法。 |
| `8e788382`、`c0c7bd55`、`67dbf355` 主题/边框、`f62465c3` 偏好页 | 维持当前MD3色池和共享控件，不引入上游静态色板或旧偏好布局。 |
| `4c955fb9` 菜单警告入口 | 重复声库等警告有产品价值，但涉及上游警告模型与主窗口布局，之后接入本地资源中心/通知入口，当前不整体移植。 |
| `11ea1717` 更新通道 | 当前UTvTU发布源、版本标签与上游不同，不照搬其stable/beta/alpha策略。另有本地必须单独修复的更新问题，见下节。 |

## 本地额外发现：更新检查尚未恢复

`OpenUtau/ViewModels/UpdaterViewModel.cs` 的 `UpdateCheckEnabled()` 仍返回false，理由是迁移后尚无Release；现在已有正式Release，该理由已过时。即使只打开开关，当前比较器仍按旧`0.1.568-plus...`/程序集版本解析，不支持当前`UTvTU-26.10.17-013015`构建时间标签。

建议单独修复“启用更新检查 + 时间标签解析/比较 + GitHub正式版筛选 + 离线错误处理”，继续使用`XKLMY-hi/UTvTU`发布源，不能只合并上游更新器或把开关改true就宣称已完成。本轮只登记，没有修改已发布版本。

## 建议实施顺序与轻量验收

1. 小范围明确修复：查找栏、ThaiVCCV回退、英语C+V两条成组、双元音/EN2JA、同名重采样器及音高点右键。保留上游署名和来源；按文件逐步提交，运行相关小组，不跑全套。
2. 单独完成DiffSinger/ONNX稳定性及新时长模型兼容：先确定统一生命周期与锁顺序，再加options持有和duration输入契约，分别提交；使用最小模型/合成夹具做创建、推理、释放和配置回归，真实声库验证另记，不能把旧测试基线当本轮结果。
3. 重采样器manifest扩展与preutter行为分别验收；再考虑VOICEVOX、SVP、新包安装交互。
4. 本地更新器修复独立于上游摘取；当前正式Release不变，完成后再发布修订版本。

保持用户要求的轻量测试：默认每组30秒/384MiB、串行；构建沿用SDK9、单节点/两处理器，不运行全套、长时合成或自行截图。
