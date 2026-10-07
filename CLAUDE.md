# CLAUDE.md — UTvTU

这是 **UTvTU** 项目，基于原版 [OpenUTAU](https://github.com/openutau/OpenUtau) 的分支版本。

## 项目概述

UTvTU 是开源歌声合成平台 OpenUTAU 的增强分支，目标：
- UI/UX 改进优化
- 新合成引擎和功能
- 中文本地化增强
- 综合性改进

## 技术栈

| 技术 | 用途 |
|------|------|
| **.NET 8.0** / C# 12 | 运行时和语言 |
| **Avalonia UI 12.1.0** | 跨平台桌面 UI 框架（2026-08-01 从 11.2.4 升级） |
| **SukiUI 7.0.2-nightly** | 主题+控件库（渐进接管中：阶段 A 主题/菜单/浮层完成，B-D 进行中） |
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

## SukiUI 现状与约定（2026-08-02）

- **主题挂载**：`<suki:SukiTheme />` 在 App.axaml Styles 尾部（SukiTheme 自身是 IStyle）；尺寸/字体收敛在 `Styles/SukiOverrides.axaml`（HarmonyOS 13px、TextBlock/Label/Expander Foreground 显式绑定 TextFillColorPrimaryBrush 防暗色黑字）
- **紧凑菜单**：`Styles/SukiCompactMenu.axaml`（Menu 模板=纯 ItemsPresenter；MenuItem 弹出方向由控件逻辑按层级设，顶栏一级走 MenuItemTopLevel + ItemContainerTheme；右键一级/二级硬编码 RightEdgeAlignedTop；Popup 内容 Border+ItemsPresenter，**禁 TemplateBinding Items**）
- **玻璃浮层**：对话框/弹出卡片 = 半透明（PlusDialogCard 令牌）+ 主内容 BlurEffect；**内容面仍全实色**
- **主题切换顺序铁律**：ThemeManager.ApplySukiTheme 先 ChangeBaseTheme 后 ChangeColorTheme（后者会被前者重置）
- **窗口迁移**：阶段 B 进行中——WindowEx 将改继承 SukiWindow（删 WindowDrawnDecorations 自绘边框）；SukiWindow 派生窗口 x:Name 字段不填充，需 FindControl 兜底
- 详细踩坑记录在记忆 [[sukiui-replacement]]（SukiUI 7.x API 全集）

## 界面自检流程（视觉验证闭环）

本项目是桌面程序，修改 UI（`Views/*.axaml`、`Styles/`、`Controls/`）后需要视觉验证。

**约定（2026-08-02 用户规定）：一般情况下不使用自截图**；当需要截图参考时由**用户主动提供**截图（用户提供图片 → 用 image-recognize skill 识别）。

识别工具（用户提供截图时使用）：
- `python "...\image-recognize\recognize.py" <图片路径>`：识别图片内容（布局/颜色/坐标/文字）
- `auto-look.py --window OpenUTAU --list / --hwnd <句柄>`：用户需要指定窗口截图时用
- 模型为智谱 `glm-4v-flash`（免费），配置在全局 skill 的 config.json
- 注意：截图内容会发送到智谱服务器，注意图片内容
