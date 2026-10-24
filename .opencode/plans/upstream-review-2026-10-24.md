# 上游更新勘察（2026-10-24）

用户要求立即勘察上游重要更新。本轮为源码审查，未合并或修改产品，未构建、运行测试、启动程序、重打包或推送。

实际工作区 `G:/xklmy文件夹/vibe coding/UTvTU`，`plus-develop`，产品基线 `b30909d6`，初始工作树干净。已核 origin 为 XKLMY-hi/UTvTU、upstream 为 openutau/OpenUtau，只执行 upstream fetch。移动盘后续仍须重新核实。

## 确认的更新范围

上次已审查与适配的上游截至 `fd8c843e323262a57f3da2d86fbb740369216dc3`。本次 fetch 后 master 为 `a9547895f93e23371949a12d34e98deb5dad0bda`，提交时间2026-10-21 20:03:11 -07:00（中国时间10月22日11:03）。GitHub compare与本地git一致：**新增14提交，70文件，3786行增加、1023行删除**。

- [完整比较](https://github.com/openutau/OpenUtau/compare/fd8c843e323262a57f3da2d86fbb740369216dc3...a9547895f93e23371949a12d34e98deb5dad0bda)
- [逐提交清单与只读补丁检查](upstream-review-2026-10-24/commits.json)
- tip带`0.1.577.2-alpha`标签；`0.1.576`指向`bc194b6f`，所以不能把此次全部新增能力称为已经进入该正式标签。
- 比较按祖先范围，不能按作者日期过滤：表达式图两条作者日期为10月13日，但提交落在本次范围内。也不能把上游全部非祖先提交算成缺失，上一轮已手工适配多项。

## 重要能力与本地适配点

### JavaScript音素器与开发者工作流：值得引入，需端到端接线

来源`2b68b1da`、`f402f075`。使用Jint4.17.0，将ES模块音素器通过oudep分发，每轨独立实例，无需编译.NET音素器DLL。提供宿主API、类型声明与新建模板，并把调试窗口升级为可筛选日志、创建音素器、监视文件热重载的开发者窗口。

它同时将音素器身份从`GetType().FullName`改为`Phonemizer.Id`/`Factory.Id`；内置类型ID保持原样，脚本为`js:<package-id>/<path>`。本地UTrack保存、轨头切换、音符覆盖名称、默认音素器等仍依赖类型，因此必须覆盖工程保存重开、歌手默认选择、两份JS脚本切换、音符级覆盖；否则同一个.NET宿主类型会混淆不同脚本。

源码提供10秒调用超时、256MiB分配限制、256递归深度、64MiB包内文本读取上限，禁字符串编译，未开启CLR访问，模块根目录受限。**这是进程内限制，并非进程隔离或整个工程的总内存预算**。本地PhonemizerRunner为专用工作线程；脚本初始化、包扫描与热重载不应直接长时间占住UI。包重载通过通知重建实例，要适配现有UI投递、旧任务代次与实例生命周期，保留已有资源中心安装互斥、MD3主题和错误显示。

需定点验证模块import及readText的越界/联接、超时、内存、错误输出；readText显式拒绝包内重解析点，但不能仅凭该检查宣称所有模块导入路径已验证。当前只读审查没有验证沙箱运行。开发者窗口的源生成属性需改成本地ReactiveUI/Fody写法，UI适配当前风格，不覆盖主窗口布局。

### ENUNU三条更新：高价值，合成与音高流程一起适配

来源`9303e64e`/#2548、`af55b9ed`/#2544、`a9547895`/#2545。

- 新EnunuConnection探测服务器能力，ENUNUServer2支持时先`pitch`再`acoustic_f0`，让声学生成跟随编辑器音高；没有lf0条件模型时走原acoustic与editorf0流程，旧服务保留回退。
- 加载音高的帧起点/一帧偏移及清浊区处理改善，能单独请求pitch，不必先完成整句音频合成。本地已有`RenderPitchResult.voiced`与写入掩码，可沿用。
- 声库`model/`中的config/table/hed寻找与相对路径修复；无法映射的歌词送为休止，支持直接输入表内音素。此项四文件补丁可直接通过文本检查，但仍须结合本地模型读取做回归。
- 小于半拍（240tick）的间隔合句，写UST时补R休止，缓存哈希包含音素位置，避免休止长度不同却共用缓存。半拍及以上保持拆分，减少短句GV后滤波过度强调。

**必要前置**：本地IRenderer没有`ShouldMergePhrases`，RenderPhrase.FromPart仍在任何非连续音素处拆句。需要适配更早`f32e4ab5`的分句接口并明确各引擎默认行为；仅复制ENUNU同名方法不会生效。不要因此把全部引擎默认合句方式一起改变。

上游新连接仍有300秒同步请求等待、首次端口探测结果缓存，重连/端口设置留有stage2 TODO；Render和LoadRenderedPitch共享锁。它不是所有渲染卡顿问题的现成解决方案。实施要保持请求在后台，覆盖超时/取消、服务重启及旧服务回退，不在UI等待该锁或网络；缓存格式与旧结果失效需明确处理。

### 多语言音素规则公共层：覆盖广，文件迁移要改良

来源`6164df40`/#2428，10文件，涉及Arpasing+、EN X-SAMPA、英语C+V、德语及西班牙语系列等。核心是符号Ordinal区分大小写、父音符属性映射、边界替换/索引、YAML规则优先级与`GetHardcodedFallback`扩展点；精确音素大小写会影响实际别名选择，值得成组适配。

新实现同时会按版本自动迁移用户全局/声库YAML，合并timings/replacements/fallbacks/symbols等并备份。**发现需修正的具体路径**：`MigrateAndWriteYaml`失败返回false后，`UpdateYamlIfNeeded`仍会备份并用模板覆盖原文件（`File.Copy(...overwrite:true)`后`File.WriteAllBytes`）。不能把“迁移失败”当作“可以覆盖用户规则”。建议失败时保留原文件并报错，写入采用临时文件验证后替换，备份避免复用同名覆盖；用临时目录测损坏YAML、未知字段、用户覆盖优先级、只读与失败恢复。该提交没有新增SBP测试，不能仅依赖其PR说明。

本地上轮适配的C+V/VCCV与固定G2P小夹具应保留；菲律宾语差异仍须按本地真实分支判断，不机械删除旧逻辑。

### 表达式节点图：补追完整前置，不只更新UI

本轮`fca90ceb`、`1f2eb8a4`为图编辑器添加25%–200%缩放、平移、框选/多选、成组移动、复制/剪切/粘贴/复制节点以及手势帮助。**UTvTU目前没有该图编辑器和Core/ExpressionGraph基础**，最新两条不能独立生效。

已补追的重要前置：`4a3a5362`曲线图求值、`4d38507b`逐音素输入输出、`378dc0ea`音高输入输出、`8da8bb24`可撤销编辑器与轨道选择、`5700f9e6`遮罩节点、`608a321d`RPIT/PITO音高分层、`b4c84add`ougraph导入导出、`29e1681a`扩展节点。这些不是新增14条，单独计为所需前置追踪。

它可把曲线、LFO、音符包络、音素值与音高处理组合为可复用图，并随工程保存，适合人声工作站的方向；**它是表达式/音高映射图，不是VST或音频效果链**。实施跨工程格式、后台phrase构建、缓存哈希、命令撤销、轨道设置、钢琴窗显示与音高工具目标，需完整工作包；不要覆盖本地刚修复的S型/正弦/平滑手势、异步代次以及离线导出管线。

## 直接影响现有功能的修复

| 来源 | 结论与本地证据 | 建议 |
|---|---|---|
| `27ecf957`/#2537 | 本地VoiceColorMappingViewModel仍无条件`NewColors[0]`；新声库无subbanks/色列表为空会越界。 | 首批修复，测无颜色、有旧映射、多颜色。 |
| `010d4b12` | 上游完整克隆/恢复PitchPoints并处理只有两点的预设。本地只应用长度和起点、保存也只存两参数。 | 适配NoteProperties现有编辑会话，独立点对象、撤销重做、重复应用，不能照搬旧VM。 |
| `7aa1b1ab`/#2534 | 英语VCCV优先无空格辅音别名，只有缺失时才选带空格别名。 | 小而明确，补同时存在两别名与仅有回退的规则检查。 |
| `33302b3d`/#2525 | 本地移动时仍无条件把直线工具设为ctrlShift，Shift只认直线/画笔；缺少ExpSetValueState.IsCurve区分。 | 修复Shift水平绘制与非Curve表达式条件，保留本地捕获/确认手势。 |
| `bc194b6f` | 本地数值框已有MinWidth0与Padding0，但仍右/居中；打开后未SelectAll，音量标签仍Capture指针。 | 吸收定位、选中与指针行为，保留MD3轨头/紧凑模式/混音手势。 |
| `0eae08c5` | 本地循环缺上游逐项提前退出，但在tempBat锁内写批处理前与外部进程返回后均ThrowIfCancellationRequested，ProcessRunner也传取消token。 | 缓存截断风险已有对应保护；可补循环提前退出，不能宣称此类错误完全未修或把上游NoWrapperScript判断直接覆盖本地类型判断。未实际复现任何截断。 |

## 建议实施顺序

1. 先补空颜色崩溃、滑音预设、VCCV别名、Shift手势、轨头数字编辑这批现有功能修复，逐项轻量验证。
2. ENUNU三条与必要分句前置成组适配，优先做离线协议夹具、旧服务分支、路径与帧边界小检查。
3. SBP按公共层与各语言一起更新，先修失败覆盖用户YAML的问题，回归只用精简词典与临时数据。
4. JavaScript音素器、稳定ID、包生命周期、开发者工具作为一个完整里程碑；不把半接线的脚本入口交付给用户。
5. 表达式节点图完整前置与此次编辑增强另立完整里程碑，保留现有界面与工程向后兼容；不会用两条增量假装已支持图求值。

这是初轮建议；用户随后指出的WORLDLINE R1.1与hifisampler应按下节调整优先级。用户当前要求的勘察已完成，尚未执行本轮引入。

## 补充：WORLDLINE R1.1与内置hifisampler（用户指出后重点补查）

初轮只沿上次同步点后的14条追查，漏将这组更早但本地未实现的核心引擎列入重点。此前把`e92d5e56`默认设置项因“缺后端”排除，只能说明当时未接后端，不能据此把整组能力长期排除。**建议把这个引擎工作包放在第一批小修之后，优先于ENUNU/JS/节点图完整工作包推进。**

主要来源与逐项确认见[引擎补充清单与原生导出记录](upstream-review-2026-10-24/engines.json)。这些都已在`fd8c843e`祖先中，不能把新增14条的计数改成包含它们。

### 两个不同层级的接入入口

- [WORLDLINE R1.1，ea3e92f7](https://github.com/openutau/OpenUtau/commit/ea3e92f76bc5169bdb306bdaf1af7f1fc8b2d1d7)：WorldlineRenderer新增独立version11（旧R为10、R2为20），约5ms分析帧，hnsep谐波分离、WORLD周期成分与连续噪声合成。`Renderers.WORLDLINE_R11`注册后出现在经典声库的渲染器列表；`e92d5e56`补默认渲染器偏好。依赖`hnsep_240512`包。
- [内置hifisampler，95f0ad73](https://github.com/openutau/OpenUtau/commit/95f0ad735b7cdac85630229acab12e28df7c9d4e)：已经是`openhachimi/hifisampler`后端的C#移植与DSP实现，通过`HifisamplerResampler : IResampler`、`ToolsManager.SearchResamplers`挂入**CLASSIC采样器列表**，稳定名称`hifisampler`参与缓存身份。源log-mel拉伸/循环后用pc-nsf-hifigan发声，支持hnsep相关处理。声码器包为`pc_nsf_hifigan_44.1k_hop512_128bin_2025.02`，对应的hnsep特征处理还需分离模型；不能将它归为VST、音素器或只显示占位入口。

两者共用部分分析能力，但不是“R1.1就是hifisampler”；一个是整句renderer，另一个是Classic逐音素resampler。上游原始hifisampler提交对应Apache2.0许可与来源注释，落地时同步鸣谢/随附许可与模型各自声明。

### 确认的本地缺口

1. 本地Renderers只有R/R2，WorldlineRenderer仅接受version1/2；`CreateRenderer`用`StartsWith(WORLDLINE_R.Substring(0,9))`兜底，因此传入`WORLDLINE-R1.1`会静默创建旧R1。必须在前缀判断前精确分派R1.1，保留旧工程名称兼容与独立缓存版本。
2. 本地没有HifisamplerResampler、Hifisampler目录、Hnsep/HnsepCache、Rd分析与TorchFile缓存读写。IResampler也没有上游`NoWrapperScript`能力标记。需确保Classic/SharpWavtool/外置wavtool均不会把内置采样器虚拟FilePath当exe运行，原始音频路径不误换临时副本；保留Windows大小写不敏感的内置名称保护。
3. `d30dc489`给ResamplerItem增加张力、气声、发声、性别、growl曲线采样，并将实际输入写入hash。注册入口之外还要接参数、缓存与GRWC表达式；否则曲线界面可编辑却不会正确作用，或复用旧缓存。
4. `dc69572f`把早期tension处理换为glottal Rd；`0c423cde`增加与hifisampler互通的源文件旁`_hnsep`缓存；`d6519728`统一P自动增益。应采用完成这些修正后的实现，不能仅取最早95f版本。`dd88522b`已将hnsep从初版固定CPU改为遵循ONNX runner并串行保护GPU；本地还须适配已有DML→会话锁顺序及生命周期。
5. 现有资源中心已能处理结构化MissingPackageException，可接hnsep与声码器精确安装入口；不新增另一套下载/安装宿主。本轮没有真实安装或运行模型。

### 原生ABI配套是必要前置

已使用纯Python解析仓库win-x64 DLL的PE导出表，**未加载/执行DLL**。本地SHA256为`5e3d2e714cac24abb6ba1843f5992132757f3f7f20c9e8505a740fd2e86e763c`，上游为`e63f495978ff43ed3b65581a02cb3cdd28074db87e5952e6594945292f10a396`。

- 新增`F0FrameCount`、`HnAnalysisF0In`、`WorldSynthesisContinuousNoise`、`WorldSynthesisSampleCount`。
- 移除`Resample`、`WorldAnalysis`与五项`PhraseSynth*`；本地Worldline.cs仍声明这些旧P/Invoke。
- `601c58c4`已将旧native resampler移至C#；`ba3ddd64`改为调用方持有输出buffer，部分同名API参数ABI也变化。因此**不能直接换新DLL，也不能让新C#调用旧DLL**。需要配套迁移interop、C#重采样实现和Windows native库，验证各调用者（含Vogen/ENUNU的WORLD入口）并同步打包检查。导出名相同本身不证明ABI兼容。
- `04c4e610`提供了带R1.1导出的上游重构建产物；本项目Windows唯一验证目标，无需扩大其他平台投入。

### 更新后的实施划分

先做现有小修；随后按“C#/native ABI配套→共享hnsep/缓存/Rd与会话保护→R1.1与hifisampler真实注册→曲线/参数hash→资源中心缺包与发布检查”推进。表达式图槽位是上游的附带接线，本地尚无图系统，两个引擎本身不必等待整套图编辑器落地。

可先沿用上游DSP/时间映射/增益/Rd/缓存的小型夹具，补本地分派与旧工程、无模型错误、取消和并发缓存检查；真实听感和模型耗时另行实机验收。既有`ed8e5369`延长preutter修复已被本地适配，不覆盖为旧版。本轮只完成源码/PE静态勘察，未做这些实施与运行验证。

## 审查边界

14条均读取文件清单与补丁、并和本地相关代码对照。逐条独立执行只读`git apply --check`：仅`7aa1b1ab`与`af55b9ed`通过，其余12条需要适配或缺前置。通过只说明文本上下文可匹配；未验证编译、运行或互相叠加后的可应用性，不能作为合并绿灯。原补丁和生成脚本保存在忽略目录`.dsh/upstream-review-20261024/`，入库JSON记录不可变SHA、范围、逐项建议与检查结果。

没有修改产品源码与当前DLL、没有切换分支或执行merge/cherry-pick。未启动主程序、自截图或做任何音频压力测试。
