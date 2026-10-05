# `.ustxp` 文件结构审计与优化提案

> **性质**：只读审计 + 方案提案。**未改动任何产品代码**，等用户拍板后再开工。
> **依据**：本地 `upstream/master`（a06ab28b，2026-10-03）与我们的 `try/mx-view @ 705d227b`（基线 `plus-develop @ b944c528`）+ 一次性实验实测。
> **标注约定**：**【实测】**=真正跑代码看到的结果（含数值）；**【源码核实】**=从源码/模型成员集读出的确定事实；**【推断】**=由前两者推出的判断，会明说。
> **实验产物**（保留备查）：`%TEMP%\ustxp-probe\`（`report.md` + `specimen.ustxp` + 两个模拟"原版重存"文件）。复现方法见附录 B。

---

## 0. 一句话结论

`.ustxp` 不是新容器，就是**上游 `.ustx` 的那份 YAML** 多写了 `ustxp_version` 与每轨 `vst_slots`；
它当前最痛的问题不是"不够漂亮"，而是**和原版 OpenUTAU 的双向往返会静默丢数据，且新版本原版保存的文件我们直接打不开**（实测）。
建议按 **C（止血）→ 视用户诉求再决定 A/A+/B** 推进。

---

## 1. 现状解剖

### 1.1 文件形态

| 事实 | 证据 |
|---|---|
| `.ustxp` = `Yaml.DefaultSerializer.Serialize(UProject)` 的纯文本 YAML，**与 `.ustx` 同一份结构** | `OpenUtau.Core/Format/Ustxp.cs:53`（写）、`:94`（读） |
| 保存**恒写 `.ustxp`**（改扩展名）；加载**两条扩展名都走 `Ustxp.Load`** | `Ustxp.cs:43-48`；`Format/Formats.cs:55-66` |
| 全仓所有工程保存/自动保存都经 `Ustxp.*`；`Ustx.Save/AutoSave/Load` **零调用** | `DocManager.cs:170,199,221,223`；【实测】全仓 grep `Ustx.Save(`/`Ustx.Load(` 无命中 |
| 序列化配置：`UnderscoredNamingConvention` + `OmitNull` + `DisableAliases` + `WithQuotingNecessaryStrings` + note/pitch/vibrato/expression 走 flow 风格 | `Core/Util/Yaml.cs:16-27,57-70` |
| 读侧对未知键**静默忽略** | `Yaml.cs:26` `.IgnoreUnmatchedProperties()`；行为**实测**见表 §2-P1 |
| 与上游 YamlDotNet 版本**完全一致**（15.1.2）⇒ 行为测量可直接套用 | 我们的 `OpenUtau.Core.csproj` vs `upstream/master:OpenUtau.Core/OpenUtau.Core.csproj`【源码核实】 |

### 1.2 字段清单（上游字段 vs Plus 新增）

**Plus 新增（上游模型里没有，实测成员集差集）**

| 键（YAML） | 成员 | 定义 | 说明 |
|---|---|---|---|
| `ustxp_version` | `UProject.ustxpVersion` | `Ustx/UProject.cs:45-50` | `"1.0"`，Plus 迁移用；legacy `.ustx` 读到 null |
| `vst_slots[].plugin_uid` / `bypassed` / `state_data_base64` / `slot_index` | `UTrack.VstSlots` / `VstPluginSlot.*` | `Ustx/UTrack.cs:100`；`Vst/VstPluginSlot.cs:13-36` | 每轨 VST 链；状态是 **base64 单行**（`StateData` 本体 `[YamlIgnore]`） |
| `mix_fx.eq_bypassed` / `comp_bypassed` / `reverb_bypassed` | `UMixFx.EqBypassed` 等 3 个只读别名 | `Ustx/UMixFx.cs:21-23` | 与上游的 `*_enabled` 互为反相**派生**属性（冗余键，见 §2-P5） |

**上游字段（我们沿用，Plus 未改语义）**

| 层级 | 键 | 定义 |
|---|---|---|
| 工程 | `name` `comment` `output_dir` `cache_dir` `ustx_version` `expressions` `exp_selectors` `exp_primary` `exp_secondary` `key` `time_signatures` `tempos` `tracks` `voice_parts` `wave_parts` | `Ustx/UProject.cs:38-75` |
| 工程（**obsolete 但仍写**） | `bpm` `beat_per_bar` `beat_unit` | `UProject.cs:53-55`（ustx v0.6 起被 `tempos`/`time_signatures` 取代） |
| 轨道 | `singer` `phonemizer` `renderer_settings` `track_name` `track_color` `mute` `solo` `mix_fx` `volume` `pan` `track_expressions` `voice_color_names` | `Ustx/UTrack.cs:72-106` |
| 片段（voice） | `position` `duration` `track_no` `name` `notes` `curves` | `Ustx/UPart.cs:44,46` |
| 片段（wave） | `relative_path` `file_duration_ms` `skip` `trim` `fadein` `fadeout` | `UPart.cs:339-344` |
| 音符 | `position` `duration` `tone` `lyric` `pitch`（`data[{x,y,shape}]`） `snap_first` `vibrato` `tuning` `phonemizer`(override) `phoneme_expressions` `phoneme_overrides` | `Ustx/UNote.cs:17-28`；形状见 `Test/Core/USTx/UstxYamlTest.cs:32-46` |
| 混音台 | `mix_fx.enabled` `eq_enabled` `comp_enabled` `reverb_enabled` + 三组参数 + `reverb_pre_delay_ms` | `Ustx/UMixFx.cs:9-43` |

### 1.3 **上游有、我们没有**的字段（现成的丢数据位）

【源码核实】用同一套成员提取脚本对比两边模型（`UProject/UTrack/UPart/UNote/UMixFx/USinger/UCurve/UExpression`）：

| 上游键 | 上游成员 | 我们 | 后果 |
|---|---|---|---|
| `expression_graphs` / `default_expression_graphs` | `UProject.expressionGraphs` / `defaultExpressionGraphs` | **无** | 上游 2026 的"表达式图"数据被我们静默忽略并写掉 |
| `expression_graph` | `UTrack.ExpressionGraph` | **无** | 同上（轨道级） |
| `masked_curves` | `UPart.maskedCurves`（`UMaskedCurve`） | **无**（连 `UMaskedCurve.cs` 都没有） | 掩码曲线数据丢失 |

### 1.4 版本与迁移机制

- **双版本字段**：`ustx_version`（上游兼容基线，我们写 `0.9`）+ `ustxp_version`（Plus，`1.0`）。`Ustxp.cs:23,49-50`。
- **迁移写在 `Load` 里的 if 阶梯**：`Ustxp.cs:113-127` 复制了 `USTx.cs:141+` 的 v0.4→v0.9 同一套迁移（**两份拷贝**）；随后 `:132-136` 是 Plus 钩子，而 `RunPlusMigrations` 目前是**空函数**（`:143-145`）。
- **版本墙**：`ustx_version > 0.9` 直接抛"工程比软件新"（`Ustxp.cs:105-110`，`USTx.cs:135-137` 同款）。

### 1.5 其它结构事实（实测）

| 事实 | 数值 |
|---|---|
| 样本工程（2 轨、3 音符、1 波形件、1 个 VST3 槽含 256KB 状态） | **352,551 字节 / 147 行**，其中一行 base64 约 350KB |
| 最小工程 | 42 行 / 567 字节 |
| `serialize → deserialize → serialize` 是否逐字节一致 | **一致（True）** ⇒ 现状对 git diff 相对友好 |
| VST 状态体积代价 | 0 → 2,998B；64KB → 90,407B；256KB → 352,551B；1MB → 1,401,127B（**base64 ×1.34**） |
| 每轨恒写空集合 | `vst_slots: []`、`track_expressions: []`、`voice_color_names: [""]`（2 轨样本里 3 处以上） |
| 键顺序 | 仅 `UPart` 的波部件字段有 `[YamlMember(Order)]`（8 处）；`UProject/UTrack/UNote/UCurve` **0 处** ⇒ 顺序随源码声明顺序，重排字段会整文件重排 |
| 素材 | `ImportAudio()` 只建 `UWavePart{FilePath=原路径}`，**不拷进工程目录**（`OpenUtau/ViewModels/MainWindowViewModel.cs:262-280`）；`relative_path` 在 `BeforeSave` 现算（`UPart.cs:465`）⇒ 外部素材写成 `..\MusicLib\x.wav`（实测） |

---

## 2. 问题清单（按严重度）

### P1（高）跨软件往返：**双向静默丢数据 + 版本墙**（任务书要求实测的第一条）

**实验设计（诚实披露）**：上游 master 是 `net10.0` + 大量缺失包，**离线无法构建原版二进制**；因此没有跑真·原版 OpenUTAU。实际做的是三件可确证的事：
① 用**同版本 YamlDotNet 15.1.2** 实测未知键行为；② 用**上游源码成员集**核实它认识哪些键；③ 按"上游模型会写出的键集合"剥离样本，再用**我们的读侧**实测结果。

| 环节 | 结果 | 证据类型 |
|---|---|---|
| 上游读侧配置 | `UnderscoredNamingConvention + IgnoreUnmatchedProperties()`（上游 master 与我们的 fork 基线都是） | 【源码核实】 |
| 未知键行为（`IgnoreUnmatchedProperties`） | **成功且静默忽略**（`a=1`，未知键无异常） | 【实测】 |
| 对照组（去掉该选项） | 抛 `YamlException: Property 'plus_only_key' not found` | 【实测】 |
| 把样本按"上游键集"剥离后 | 352,551 B → **2,509 B**；`ustxp_version`/`vst_slots`/`state_data_base64`/`eq_bypassed` **全部消失**；`ustx_version`/`voice_parts`/`wave_parts`/`relative_path`/`eq_enabled` 保留 | 【实测】 |
| 我们用 Plus 读上游存出的文件 | **失败**：`MessageCustomizableException`：`Project file is newer than software` | 【实测】 |
| 假设拿掉版本墙再读 | 成功，但 `tracks[0].VstSlots.Count = 0`（原 3 槽含插件状态）；`MixFx.EqEnabled` 等上游字段完好；3 个音符、`relative_path` 完好 | 【实测】 |
| 上游近期版本号 | `0.1.570.11-alpha` 起 `kUstxVersion = 0.10`（`0.1.567 ~ 0.1.570.10` 为 0.9）；最新 `0.1.572.1-alpha` 也是 0.10 | 【源码核实】 |

**结论（分方向）**

1. **Plus → 上游**（用户拿我们的工程给原版用户/自己在原版里打开再存）：
   - 上游能**打开**（不报错），但保存时**静默丢掉**：整条 VST 链（插件 UID + 旁通 + 状态数据，实测样本 352KB→2.5KB）、`ustxp_version`、`*_bypassed` 冗余键。
   - 之后**我们的 Plus 打不开这个文件了**（上游写 `ustx_version: 0.10` > 我们的 `0.9`）⇒ 用户看到的是"文件坏了"，而不是"参数没了"。
   - 触发条件取决于用户的原版版本：**≥ 0.1.570.11-alpha 必触发**；≤ 0.1.570.10 只丢数据、不锁文件。
2. **上游 → Plus**（在较新原版里用了表达式图/掩码曲线后回来）：`expression_graphs` / `default_expression_graphs` / `expression_graph` / `masked_curves` 被我们**静默忽略**，我们一保存就写掉（§1.3）。【实测机制 + 源码核实键集】
3. **影响面**：所有"两个软件都装"的用户（本仓前几轮已确认用户实机装着原版）；数据丢失**无提示、无备份、不可撤销**。
4. **严重度：高**。这是唯一一条会造成"用户数据没了"的问题。

### P2（中高）核心与扩展**没有分层/命名空间**，且上游正在收紧

- Plus 键与上游键平铺在同一层（`ustxp_version` 挨着 `ustx_version`；`vst_slots` 挨着 `volume`）。
- 【源码核实】上游 master 新增了 `OpenUtau.Core/Util/YamlValidator.cs`，诊断种类里明确有 **`UnknownKey`（"A key the app does not read, usually a typo"）**，并已被 `OpenUtau/Controls/YamlEditor.axaml.cs` 使用。今天它主要服务 `character.yaml` 一类文件，未知键只是**警告**（`YamlDiagnostic.IsError=false`）。
- 【推断】一旦上游把未知键用于工程文件的校验/编辑体验，我们的 `ustxp_version`/`vst_slots` 会变成红波浪线甚至硬错误；同时上游每加一个工程字段，我们就会多一处静默丢弃（§1.3 已经发生）。

### P3（中）版本机制：双版本字段 + 迁移两份拷贝 + 空壳钩子 + 死代码 + 无注册表

- 迁移阶梯在 `Ustxp.cs:113-127` 与 `USTx.cs:141-183` **各一份**（复制粘贴，将来修一处漏一处）。
- `Ustx.Save/AutoSave/Load` **全仓零调用**（【实测】grep），是"编译干净但没人用"的重复实现。
- `RunPlusMigrations` 是空壳；`kUstxpVersion = 1.0` 目前只用于"要不要跑 Plus 迁移"。
- 没有显式 schema、没有迁移注册表、没有"未知键保留"策略 ⇒ 版本演进全靠手写 if。

### P4（中）大对象内联：VST 状态是**一行 base64**

- 实测：1MB 插件状态 → 1.4MB 文件，单行长度 ~1.4M 字符。
- 【推断】后果：任何文本编辑器/YAML 高亮/git diff/`git blame` 都会被这一行拖慢；`StateDataBase64` 是**每次保存都重算**（`VstPluginSlot.cs:23-33`），大插件工程的保存耗时与内存峰值随之上升。
- 上游对"大块二进制"没有可参照的做法（它的工程里没有这类数据）。

### P5（低-中）文件内噪音与冗余

- 每轨恒写 `vst_slots: []`、`track_expressions: []`、`voice_color_names: [""]`；`expressions: {}`；obsolete 的 `bpm`/`beat_per_bar`/`beat_unit` 也照样写。
- `eq_enabled` 与 `eq_bypassed` **同时写出**（互反的派生属性）：数据冗余，且第三方脚本若只改其中一个会得到自相矛盾的文件。
- 【实测】加 `DefaultValuesHandling.OmitEmptyCollections` 后同一工程 147→132 行（352,551→352,222 B），用**现有读侧**读回完全成功（读侧字段都有默认值）。

### P6（中）素材（音频等）没有清单、没有打包能力

- 音频只存 `relative_path`，且由 `Path.GetRelativePath` **现算**：外部素材导入后是 `..\MusicLib\outsider.wav`（实测）。工程换目录/换机器即失效；加载时会标记 `Missing` 并在界面显示 `[Missing] name`（`Ustx/UPart.cs:347,402`），但**没有资源清单、没有"工程打包/搬迁"能力**，也没有相对路径失效的批量修复入口。
- 没有"工程 = 一个可搬运目录/包"的概念（没有 manifest、没有 `assets/`、没有增量保存、没有资源校验）。

### P7（低）文档与常量漂移、键顺序无策略

- `UTrack.cs:99` 注释写 VST 槽 "up to 5"，实现默认 **3**（`Vst/VstPluginManager.cs:135`，实测 `CreateDefaultSlots().Count == 3`）。
- `Ustxp.cs:12-21` 的类注释说 "Mixer state / Plus-only expressions"，但 `MixFx` 是**上游字段**、表达式并没有 Plus 专属内容（注释与实现不符）。
- 键顺序无统一策略（§1.5），字段重排会造成整文件 diff 噪音，对多人协作/git 合并不友好。
- 【正面事实】我们自己的读写往返是**逐字节稳定**的（实测 True），所以"结构乱"目前还没演变成"文件自己变脏"。

---

## 3. 候选方案（含取舍）

> 工作量是**估算**（人日，单人在本仓熟练度下）；风险指"上线后出数据事故"的概率×影响。

### 方案 A：保 YAML 内核 + 显式扩展段（`plus:`）

- **做法**：文件仍是单文件 YAML；上游字段原样保留在最外层，Plus 数据集中到顶层 `plus:` 子树（`plus.vst_slots`、`plus.format_version`…）；加载时读 `plus:`，保存时写 `plus:`；新增"另存为纯净 `.ustx`"（剥离 `plus:`）入口。
- **改动面**：`Ustxp.cs`（读写 + DTO）、`Yaml.cs`（如需保留未知键）、`UMixFx`/`UTrack`（键路径迁移）、菜单/FilePicker（导出）、测试。
- **向后兼容**：旧 `.ustxp`（键在顶层）必须能读 → 需要一个"顶层旧键 → `plus:`"的兼容读；保存后文件即迁移。
- **跨软件互操作**：上游仍能打开（`plus:` 被忽略），但仍**会丢** `plus:` 段（上游重写整文件）⇒ 只解决"结构清爽 + 可剥离导出"，**不解决"上游重存不丢 Plus 数据"**。
- **工作量**：3–5 人日。**风险**：中（键路径迁移 + 兼容读易漏字段）。

### 方案 A+：A + **侧车文件**（推荐用于"上游重存不丢"）

- **做法**：主文件 `foo.ustxp`（YAML，含上游字段 + 可选 `plus:` 段）+ 侧车 `foo.ustxp.plus.yaml`（Plus 专属：`vst_slots`、插件状态、Plus 版本、未来的 UI 状态）。保存时两份都写；加载时以主文件为准、按需合并侧车（用主文件里的 `plus_carrier_id`/时间戳做关联校验）。
- **改动面**：A 全部 + 主/侧车一致性规则 + "另存/移动工程"时的联动 + 自动保存/崩溃恢复（两份都要原子写）+ 清理逻辑（孤立侧车）。
- **向后兼容**：旧单文件 `.ustxp` 读进来 → 下次保存拆成两份（要提示？）。
- **跨软件互操作**：上游重存主文件**不会**动侧车 ⇒ **Plus 数据保住**；上游用户拿到主文件能正常用（没有 VST 链）。
- **工作量**：4–7 人日。**风险**：中（两份文件的一致性与"用户手工删了一份"的兜底）。

### 方案 B：容器化（zip：`manifest.json` + `project.ustx.yaml` + `plus.yaml` + `assets/`）

- **做法**：`.ustxp` 变 zip 包；内含纯净 ustx（上游可直接用）、`plus.yaml`、素材原件；对上层暴露"打开/保存/另存纯净 ustx/打包素材"。
- **改动面**：格式读写全面重写、文件选择器/拖放/最近工程、自动保存与崩溃恢复（zip 原子替换）、增量保存、素材拷贝策略、导出、测试；`File.Exists` 类判断全部要改。
- **向后兼容**：旧单文件 `.ustxp` 要能读（识别"不是 zip"→ 走旧读法）。
- **跨软件互操作**：**上游直接打不开 `.ustxp`**（需我们导出纯 `.ustx`）⇒ 互操作从"隐式"变成"显式一步"。
- **工作量**：8–15 人日。**风险**：高（保存路径全改，崩溃恢复与素材拷贝是数据事故高发区）。

### 方案 C：保守——不动文件形态，只做"止血 + 卫生"（推荐第一步）

包含 6 项彼此独立、都可单测的小改：

1. **未知键透传**（保上游新字段）：读时保留未识别键（YAML 节点级"影子副本"），保存时原样写回 ⇒ 修掉 §1.3 的丢失（`expression_graphs`/`masked_curves` 等不再被我们吃掉）。
2. **版本墙放宽**：`ustx_version` 比我们新时由"抛错"改为"警告 + 尽力读 + 备份原文件"，并把 `kUstxVersion` 跟到 0.10（含迁移注册表）。
3. **"另存为纯净 `.ustx`"**：剥离所有 Plus 键后写出，供上游用户使用（当前**完全没有这条路径**——"导出 USTX"菜单实际导出的是 UTAU `.ust`，见 `MainWindow.axaml.cs:OnMenuExportUst`）。
4. **冗余/噪音清理**：`*_bypassed` 改为只读不写（或直接删属性、保留读兼容）；`vst_slots` 空列表不写；去掉 obsolete 三键的写出（保留读，用于老文件迁移）。
5. **迁移注册表**：把两份重复的迁移阶梯收敛到一处（`Ustx.Save/AutoSave/Load` 死代码删除或标注为唯一实现），Plus 迁移钩子补上版本表。
6. **大块状态可选外置**（只做"能外置"的开关，不强制）：`state_data_base64` 超过阈值时改为侧车 `*.vststate/<track>-<slot>.bin`（与 A+ 的侧车共用机制）。
- **工作量**：2–3 人日（1+2+3 是主体，4–6 各半天）。**风险**：低-中（未知键透传要动读路径，需重点测试；其余是局部小改）。

### 取舍对照

| 维度 | A 单文件+`plus:` | A+ 侧车 | B 容器 | C 止血 |
|---|---|---|---|---|
| 上游能否直接打开 | ✅ | ✅ | ❌（要导出） | ✅（不变） |
| 上游重存后 Plus 数据是否保住 | ❌ | **✅** | ✅ | ❌（但提供"纯净导出"避免误解） |
| 上游新字段是否会被我们吃掉 | 需透传 | 需透传 | ✅（纯净 ustx 原样进容器） | **✅（透传）** |
| 结构清爽/可读 | ✅ | ✅ | ✅✅ | ⚪（只去噪音） |
| 素材打包/迁移 | ❌ | ⚪ | ✅✅ | ❌ |
| 改动面 | 中 | 中 | 大 | 小 |
| 工作量（估） | 3–5 人日 | 4–7 人日 | 8–15 人日 | **2–3 人日** |
| 风险 | 中 | 中 | 高 | 低-中 |

---

## 4. 推荐意见

**推荐路线：先 C（止血），再按用户诉求决定是否 A/A+/B。**

理由：
1. C 里有两项是**当前唯一会造成数据损失**的修复（① 上游新字段不再被我们吃掉；③ 用户有安全交付给上游用户的路径），而且**不改文件形态**——对已有工程零迁移风险、对用户零学习成本。
2. "打不开"这条（版本墙）是**硬故障**，且随着原版升级必然发生，必须最先修。
3. A/B 的价值（结构清爽、容器化、素材打包）**取决于用户到底哪里不满意**——"ustxp 文件结构需要优化"这句话太模糊：若他要的是"和原版来回用不丢"，单文件方案 A **做不到**（上游会重写整文件），必须 A+ 侧车或 B 容器；若他要的是"文件别那么乱/别那么大"，C+A 就够。**这一点必须先问清楚再动手**。

若用户明确"要能和原版来回用且不丢 Plus 数据" ⇒ **A+（侧车）** 是性价比最高的形态（保留单文件互操作 + Plus 数据集中在侧车，上游动不到）。
若用户要"工程随素材一起搬走" ⇒ 才值得上 **B（容器）**，并接受"上游不能直接打开、必须导出纯净 ustx"。

**明确不做（本轮/本提案不涉及）**：不改 `.ust` 导出、不动音源（音源包）格式、不做 FL 式内部布局保存、不做工程级版本控制/云同步。

---

## 5. 需要用户拍板的问题（≤5 条，每条给默认建议）

| # | 问题 | 默认建议 |
|---|---|---|
| Q1 | 你说的"结构需要优化"，最在意哪一条？(a) 与原版互操作不丢数据 (b) 文件可读/可 diff (c) 体积（VST 状态那行 base64） (d) 素材能随工程打包搬走 (e) 未来加字段不混乱 | **默认按 (a) 优先**，其次 (b)(e) |
| Q2 | 是否接受"主文件 + 侧车文件"（`foo.ustxp` + `foo.ustxp.plus.yaml`）？ | **接受**（这是"上游重存不丢 Plus 数据"的唯一低成本解；不接受就只能走 B 容器） |
| Q3 | 是否要 zip 容器（代价：原版**打不开** `.ustxp`，必须"导出纯净 ustx"给它）？ | **暂不做**；先做 C+可能的 A+ |
| Q4 | 原版新版（`0.1.570.11+`，`ustx_version 0.10`）存的工程在我们这里"打不开"：选"A 跟上 0.10 迁移"还是"B 只警告 + 尽力读"？ | **两者都要**：先把 `kUstxVersion` 跟到 0.10 并补迁移，同时把"比我们新"从抛错改为警告 + 尽力读 + 自动备份 |
| Q5 | Plus 专属字段是否集中到 `plus:` 段（会改键路径、需要一次性迁移）？ | **做**，但排在 C 之后（作为 A 的第一步）；迁移必须"旧键能读、新键能写、双向不丢" |

---

## 6. 迁移与测试策略

### 6.1 迁移原则

1. **只加不减、先读后写**：任何键路径调整都先实现"旧键 → 新键"的读兼容，再改写出；写出后**不删除**旧键的读取支持（至少保留两个大版本）。
2. **迁移必须可重入**（幂等）：对同一个文件连续迁移两次结果一致（现有 Plus 迁移钩子就是这个语义）。
3. **迁移前自动备份**：任何"格式升级/版本比我们新"的场景，先把原文件复制为 `*.bak-<时间戳>`（P1 的版本墙场景尤其需要）。
4. **一次只迁移一个版本**：迁移注册表 `from → to` 列表，禁止跨版本跳跃式 if。

### 6.2 测试用例清单（每条都可落成 xUnit 用例）

| # | 用例 | 断言 |
|---|---|---|
| T1 | 旧 `.ustx`（v0.4/0.5/0.6/0.7/0.9 各一份样本）→ 读 → 写 → 再读 | 上游字段逐键保留；迁移后 `ustx_version == 最新`；无异常 |
| T2 | 我们保存的 `.ustxp` → 用"上游键集剥离器"转成上游文件 → 我们读 | 读成功；**丢失清单 == 预期 Plus 键集**（把 P1 的行为固化成回归基线，避免"悄悄多丢一个键"） |
| T3 | 上游文件（含 `expression_graphs`/`masked_curves`/`expression_graph`）→ 读 → 写 | **透传落地后**：这些键逐字节仍在（当前会丢 ⇒ 用例先红后绿） |
| T4 | `ustx_version: 0.10` 的工程 | 新策略下：不抛错、给出警告、生成 `.bak`、能读到 tracks（当前抛错 ⇒ 先红后绿） |
| T5 | 迁移注册表逐跳 | v0.4→0.10 每一跳单独用例（`acc`→`atk`、`...`→`+`、`tempos/timeSignatures` 生成、`expSelectors` 扩容） |
| T6 | 体积/完整性与大状态 | 256KB 状态保存后体积 ≤ 阈值；`state_data_base64` 故意写坏（非法 base64）→ 丢弃 + 警告不崩 |
| T7 | 素材可移植 | 把工程目录整体拷到新路径 → `relative_path` 仍解析到音频；外部素材导入的新策略（拷贝进 `assets/` 或提示）有用例 |
| T8 | 双向往返矩阵 | Plus→上游(剥离)→Plus：按最终方案断言"预期丢失集"或"完全不丢"（A+ 侧车方案下必须不丢） |
| T9 | 空集合/别名/遗留键 | 缺 `vst_slots`/`track_expressions` 的文件读回来默认值正确；`eq_bypassed` 单写不写 `eq_enabled` 时语义正确 |
| T10 | 自动保存/崩溃恢复 | `-autosave.ustxp`/`-backup.ustxp`（`Formats.cs:126` 的命名替换）与主文件一致；侧车方案下两份都原子替换 |

### 6.3 验收口径（给验证者）

- 每个方案落地后都要有**独立验证**：`verify` 用真实文件做"Plus 存 → 上游读（有网络/实机时用真原版二进制复核）→ 上游存 → Plus 读"的闭环，并核对丢失清单与本文件 §2-P1 的基线一致或更好。
- 本提案的**实测样本已留档**：`%TEMP%\ustxp-probe\specimen.ustxp` 与 `upstream-resaved-*.ustxp`，可直接拿原版 OpenUTAU 打开复核（这是补上"真原版二进制"验证的最短路径）。

---

## 附录 A：实测原始数据（摘要）

```
样本：specimen.ustxp  352,551 B / 147 行（含 256KB VST 状态，单行 base64）
模拟"原版重存"：upstream-resaved-v0.10.ustxp  2,509 B
键行数对比（我们的样本 → 原版重存后）：
  ustx_version 1 → 1（值被改成 "0.10"）
  ustxp_version 1 → 0
  vst_slots 2 → 0
  state_data_base64 1 → 0
  eq_bypassed 1 → 0
  eq_enabled 1 → 1
  voice_parts 1 → 1 ; wave_parts 1 → 1 ; relative_path 1 → 1
Plus 读 upstream-resaved-v0.10.ustxp：MessageCustomizableException（Project file is newer than software）
Plus 读 upstream-resaved-v0.9.ustxp：成功；VstSlots.Count 0；EqEnabled/别名正常；notes 3；relative_path 保留
未知键：上游配置成功静默忽略 / 严格配置抛 YamlException（两库同为 YamlDotNet 15.1.2）
往返：serialize→deserialize→serialize 文本一致 = True
体积：0B→2,998 / 64KB→90,407 / 256KB→352,551 / 1MB→1,401,127（×1.34）
OmitEmptyCollections：147→132 行、352,551→352,222 B，现有读侧读回成功
素材：外部音频 → relative_path: ..\MusicLib\outsider.wav
默认值：CreateDefaultSlots().Count = 3（注释写 "up to 5"）；最小工程 42 行 / 567 B
```

## 附录 B：复现方法（实验脚本要点）

1. 临时用例加进 `OpenUtau.Test`（跑完删除，**不改产品代码**），用 `Yaml.DefaultSerializer` 序列化一个含 `VstSlots`/`ustxpVersion`/`MixFx` 的 `UProject`（`project.BeforeSave()` 会填充 `voice_parts`/`wave_parts`）。
2. 未知键行为：`DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)` ± `.IgnoreUnmatchedProperties()` 反序列化同一份带未知键的 YAML。
3. "原版重存"模拟：按上游模型不认识的键（`ustxp_version` / `vst_slots` 键与其块序列 / `*_bypassed`）从文本剥离（注意 YamlDotNet 把块序列写在**父键同一缩进**上），再把 `ustx_version` 盖成 `0.10`/`0.9`，然后用 `Ustxp.Load` 读。
4. 本轮实验产物在 `%TEMP%\ustxp-probe\`；如需原版二进制复核，直接把 `specimen.ustxp` 与两个 `upstream-resaved-*.ustxp` 交给实机。

---

## 变更记录

- 2026-10-04：首版。只读审计（源码核实 + 一次性实验实测），未改动任何产品代码；实验临时文件已删除，`try/mx-view` 工作树保持干净（构建 0 错误 / 全量 451 通过）。
