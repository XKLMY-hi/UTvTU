# AGENTS.md — OpenUTAU Plus

> ⚠️ **工作区在移动硬盘上，盘符随时会更改**（历史出现 D:/E:/G: 等不同盘符）。每次工作开始前必须先确认实际工作区路径（用 `git remote -v` + 目录存在性核实），**不要沿用上会话的硬编码盘符**。

这是 **OpenUTAU Plus** 项目，基于原版 [OpenUTAU](https://github.com/openutau/OpenUtau) 的分支版本。

## 项目概述

OpenUTAU Plus 是开源歌声合成平台 OpenUTAU 的增强分支，目标：
- UI/UX 改进优化
- 新合成引擎和功能
- 中文本地化增强
- 综合性改进

## 技术栈

| 技术 | 用途 |
|------|------|
| **.NET 8.0** / C# 12 | 运行时和语言 |
| **Avalonia UI 12.1.0** | 跨平台桌面 UI 框架（2026-08-01 从 11.2.4 升级） |
| **SukiUI 7.0.2-nightly** | 主题+控件库（渐进接管 A-E 已全部收官，UI 改造结束 2026-08-12） |
| **ReactiveUI** + Fody | MVVM 框架（Avalonia 12 用 ReactiveUI.Avalonia 14.7.1 兼容线） |
| **ONNX Runtime** | AI/ML 推理（DiffSinger 等） |
| **NAudio** / MiniAudio | 音频播放 |
| **YamlDotNet** | USTX 项目文件序列化 |
| **Serilog** | 结构化日志 |
| **xUnit** | 单元测试（xunit v3） |

## 项目结构

```
OpenUtau.sln
├── OpenUtau/              # 主应用（Avalonia UI 层）
│   ├── Views/             # 窗口和控件
│   ├── ViewModels/        # MVVM ViewModel
│   ├── Strings/           # 多语言 .axaml 资源文件
│   ├── Colors/            # 主题色（Brushes.axaml 兼容键 / DarkTheme·LightTheme 变体）
│   ├── Styles/            # 样式（SukiOverrides 收敛层 / SukiCompactMenu 紧凑菜单）
│   ├── Controls/          # 自定义控件
│   ├── App.axaml           # 应用入口 + 语言注册
│   └── ThemeManager.cs     # 主题和字符串管理
├── OpenUtau.Core/         # 核心逻辑
│   ├── Ustx/              # 数据模型（UProject, UTrack, UPart, UNote）
│   ├── Format/            # 文件格式支持（USTX, UST, VSQx, MIDI, MusicXML）
│   ├── Render/            # 渲染引擎
│   ├── Api/               # 音素化器插件 API
│   ├── Editing/           # 编辑宏 API
│   ├── Classic/           # 经典 UTAU 引擎支持
│   ├── DiffSinger/        # DiffSinger 神经网络引擎
│   ├── G2p/               # Grapheme-to-Phoneme 转换
│   └── Util/              # 工具（ONNX, Preferences 等）
├── OpenUtau.Plugin.Builtin/  # 内置音素化器插件
└── OpenUtau.Test/         # 单元测试（xUnit）
```

## 常用命令

```bash
# 还原依赖
dotnet restore

# 构建解决方案
dotnet build

# 运行应用
dotnet run --project OpenUtau

# 运行测试
dotnet test

# 发布 Windows x64
dotnet publish OpenUtau -c Release -r win-x64 --self-contained true -o bin/win-x64
```

## Git 分支策略

| 分支 | 用途 |
|------|------|
| `master` | 与上游 `openutau/OpenUtau:master` 保持同步 |
| `plus-develop` | Plus 主开发分支，承载所有 Plus 改动 |

### 与上游同步

```bash
# 拉取上游最新代码
git fetch upstream
git checkout master
git merge upstream/master

# 将上游更新合并到 plus-develop
git checkout plus-develop
git merge master
```

## 本地化

- 使用 Avalonia 资源字典（`.axaml` 文件）
- 英文基础：`OpenUtau/Strings/Strings.axaml`
- 中文翻译：`OpenUtau/Strings/Strings.zh-CN.axaml`
- 字符串通过 `ThemeManager.GetString("key")` 获取
- XAML 中通过 `{DynamicResource key}` 绑定

## 架构模式

- **MVVM**：View (axaml) → ViewModel (.cs) → Model (Core)
- **命令模式**：所有状态变更通过 `DocManager.ExecuteCmd()` 执行 `UCommand`，支持撤销/重做
- **观察者模式**：组件实现 `ICmdSubscriber` 接收变更通知

## 注意事项

- 上游默认分支是 `master`（不是 `main`）
- 部分 C++ 原生代码（Worldline 引擎）需要 Bazel 构建，纯 C# 开发不需要
- Windows 路径中包含非 ASCII 字符可能导致某些 resampler 无法工作
- 代码中的 `OpenUtau` 命名空间保留不变，仅应用名称和显示文字改为 Plus
- **必须用 SDK 9 构建**（本机 9.0.316）：Avalonia 12.1.0 分析器需要 Roslyn 4.14+，钉住 SDK 8 会报 CS9057——不要给本仓库加 global.json 钉 8.0.4xx

## DSH 沙箱环境构建（2026-08-13 接管时实证）

本仓库现由 DeepSeek Harness 接管开发。DSH 沙箱与用户实机 shell 环境不同，构建验证需以下变通（用户实机有网络、无沙箱，仍按 CLAUDE.md 常用命令原样构建）：

- **无外网**：nuget.org 等全部不可达。restore 报 NU1603/NU1900/NU1801，且 `OpenUtau.csproj` 的 `TreatWarningsAsErrors=true` 会把它们升级为错误 → 离线 restore 必须加 `--ignore-failed-sources -p:TreatWarningsAsErrors=false`
- **MSBuild 多节点静默失败**：sln 级 restore/build 默认多节点时子节点失败不上报（表现为"生成失败 0 错误"）→ 一律加 `-m:1` 才能看到真实错误
- **Avalonia 遥测任务**（AvaloniaStatsTask）写 `AppData\Local\AvaloniaUI` 被沙箱拒绝 → MSB4018 → 构建加 `-p:UsedAvaloniaProducts=`
- **VstProbe RID 运行时包**不在本地缓存 → NU1101 → restore/build 加 `-p:RuntimeIdentifiers=`（仅影响 publish 场景；sln restore 会污染 VstProbe assets，须在其后单独 restore VstProbe）
- **测试宿主**需打开父进程句柄（SetParentProcessExitCallback）→ 普通沙箱跑 `dotnet test` 报 Win32Exception(5) 中止 → 测试命令需在完整权限下运行

沙箱内完整验证流程（等效用户实机 `dotnet build && dotnet test`）：

```powershell
# 1. 离线 restore（顺序不可反：sln 先、VstProbe 后）
dotnet restore OpenUtau.sln -m:1 -p:TreatWarningsAsErrors=false --ignore-failed-sources
dotnet restore VstProbe\VstProbe.csproj -m:1 -p:RuntimeIdentifiers= -p:TreatWarningsAsErrors=false --ignore-failed-sources

# 2. 忠实构建（--no-restore 保 TreatWarningsAsErrors 原样）
dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=

# 3. 测试（需完整权限运行，否则 testhost 中止）
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build
```

测试基线（2026-08-13 接管验证）：284 个测试全绿（exit 0）。接管时修复了 3 处脱节：阶段 E 窗口透明契约测试未随设计更新、PluginRunner 固定 `temp.tmp` 并行争用、PluginRunnerTest 异步 void 断言漂移 + Shift-JIS 中文路径期望。

## SukiUI 现状与约定（2026-08-02）

- **主题挂载**：`<suki:SukiTheme />` 在 App.axaml Styles 尾部（SukiTheme 自身是 IStyle）；尺寸/字体收敛在 `Styles/SukiOverrides.axaml`（HarmonyOS 13px、TextBlock/Label/Expander Foreground 显式绑定 TextFillColorPrimaryBrush 防暗色黑字）
- **紧凑菜单**：`Styles/SukiCompactMenu.axaml`（Menu 模板=纯 ItemsPresenter；MenuItem 弹出方向由控件逻辑按层级设，顶栏一级走 MenuItemTopLevel + ItemContainerTheme；右键一级/二级硬编码 RightEdgeAlignedTop；Popup 内容 Border+ItemsPresenter，**禁 TemplateBinding Items**）
- **玻璃浮层**：对话框/弹出卡片 = 半透明（PlusDialogCard 令牌）+ 主内容 BlurEffect；**内容面仍全实色**
- **主题切换顺序铁律**：ThemeManager.ApplySukiTheme 先 ChangeBaseTheme 后 ChangeColorTheme（后者会被前者重置）
- **窗口迁移**：已完成——WindowEx 继承 SukiWindow（34 窗口零 xaml 改动，装饰经基类继承生效；x:Name 坑仅存在于手动 AvaloniaXamlLoader 运行时加载路径，编译路径正常）

## 界面自检流程（视觉验证闭环）

本项目是桌面程序，修改 UI（`Views/*.axaml`、`Styles/`、`Controls/`）后需要视觉验证。

**约定（2026-08-02 用户规定）：一般情况下不使用自截图**；当需要截图参考时由**用户主动提供**截图（用户提供图片 → 用 image-recognize skill 识别）。

识别工具（用户提供截图时使用）：
- `python "...\image-recognize\recognize.py" <图片路径>`：识别图片内容（布局/颜色/坐标/文字）
- `auto-look.py --window OpenUTAU --list / --hwnd <句柄>`：用户需要指定窗口截图时用
- 模型为智谱 `glm-4v-flash`（免费），配置在全局 skill 的 config.json
- 注意：截图内容会发送到智谱服务器，注意图片内容

## 记忆与计划（2026-08-12 从 Claude Code 迁移）

opencode 无内置记忆系统，Claude Code 记忆已迁移到本仓库，涉及历史踩坑/用户偏好/计划时按索引查阅：

- **记忆索引**：`.opencode/memory/MEMORY.md` — 17 份记忆：SukiUI 替换（A-E 全部收官、UI 改造已结束）、Avalonia 12 升级踩坑、VST 专用线程修复、音频管线重构、用户偏好（中文/明确推荐/阶段预览闸门/每步跑 verify）、git SSL 等
- **计划文档**：`.opencode/plans/` — 8 份：Plus 实施计划、UI 完全重铸、SukiUI 替换表、混音台内嵌化、PlusTheme 体系等
- 旧 Claude 会话原文：`~/.claude/projects/D--xklmy----XK-XKLMY----vibe-coding-OpenUTAU-plus/`（jsonl 按需查阅）
