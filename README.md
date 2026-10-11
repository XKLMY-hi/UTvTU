# UTvTU

[English](README-en.md)

**UTvTU 是基于 [OpenUTAU](https://github.com/openutau/OpenUtau) 的人声制作工作站**，将歌声编辑、合成、混音与音频交付放进同一套工作流程。项目在现有 UTvTU 上持续重构，并以上游 OpenUTAU 为技术参考。

当前面向 **Windows 10 / 11 x64**。界面采用自有 Material Design 3 主题与控件，兼顾动态配色、统一交互和 DAW 编辑所需的信息密度。

> 项目前身为 **OpenUTAU Plus**，2026-10 更名为 UTvTU。旧仓库 [OpenUTAU-Plus](https://github.com/XKLMY-hi/OpenUTAU-Plus) 保留为历史存档；用户数据目录仍为 `OpenUtau Plus`，更名无需迁移数据。

## 下载与安装

从 [GitHub Releases](https://github.com/XKLMY-hi/UTvTU/releases) 下载发布版本。本文介绍 `plus-develop` 开发分支的当前能力，已发布安装包的功能以对应版本说明为准。

- 安装包为 `UTvTU-<版本>.exe`，自带运行时，无需另装 .NET。
- 默认按用户安装；检测到旧版 OpenUTAU Plus 时沿用原安装路径，保留用户数据、工程和声库。
- 卸载入口为安装目录中的 `UTvTU-Uninstall.exe`，默认保留用户数据。

## 制作流程

1. 从欢迎页创建工程或打开最近工程，安装并选择所需声库。
2. 在编排区组织轨道与伴奏，在钢琴卷帘编辑音符、歌词、音高和表情。
3. 在混音台调整音量、声像、静音与独奏，使用内置效果器或 VST3 效果器处理人声。
4. 打开渲染交付页，选择轨道、时间范围和文件规格，导出混音或独立分轨。

## 主要功能

### 歌声编辑与工程

- 保留 OpenUTAU 的歌声编辑、音素化与合成工作流，支持经典 UTAU 声库与 DiffSinger 等引擎。
- 编排、钢琴卷帘和混音台在主窗口内切换；钢琴卷帘与混音台可分离为独立窗口。
- 使用 `.ustxp` 保存 UTvTU 工程及 VST 插件状态，兼容 `.ustx`；保留上游提供的工程导入能力。
- 轨道与效果链编辑接入撤销 / 重做。

### 混音与效果

- 轨道推子、声像、静音 / 独奏、实时电平表和主输出控制。
- 内置 EQ、压缩器、混响，支持实时调节、旁通及效果面板收起。
- VST3 音频效果器扫描、加载、原生编辑器与参数状态保存；效果链支持增删、旁通和重排。
- 素材库提供声库、伴奏与效果器浏览、搜索及相应试听 / 拖入操作。

### 渲染与音频交付

- **默认离线导出**，使用独立导出插件实例处理音频；无需通过播放设备静音录制完成交付。
- 支持立体声混音与分轨导出，选择整曲、循环区间或自定义范围，配置轨道和效果尾音。
- 支持 WAV 16-bit、24-bit、32-bit float，以及 44.1 / 48 / 96 kHz 文件采样率。
- 可选择包含 VST、内置效果、轨道音量 / 声像和主输出设置；提供进度、取消及输出文件夹入口。
- 对不支持离线处理的插件，可手动选择实时兼容模式；失败时明确报错，不自动切换处理方式。

文件采样率属于交付规格，合成与效果链仍按工程内部采样率处理。离线导出包含所选轨道的插件延迟对齐；播放中的全局延迟补偿仍待完善。

### Material Design 3 界面

- HCT / CAM16 动态角色色，支持自定义种子色或读取 Windows 强调色，并提供深浅主题、配色方案和对比度设置。
- 按角色组织容器层级、圆角、描边与控件状态；编辑器保留紧凑的 DAW 控件规格。
- 统一按钮、输入、选择、滑条、菜单与浮层的视觉和焦点行为，支持减少动态效果。
- 欢迎页与偏好设置采用统一页面布局；渲染、资源和依赖包管理整合进主窗口工具工作区。
- 上下文编辑工具使用底部工具区或主窗口浮层；原生 VST 编辑器与系统文件选择器使用独立窗口。
- 资源中心的声库列表显示真实声库头像，缺失头像时使用默认图标。

## 当前范围与后续方向

当前优先完善 **Windows 人声制作流程**、稳定性、工程兼容和控件交互。macOS / Linux 暂不作为新增开发和验证的目标平台。

| 当前边界 | 说明 |
|---|---|
| VST 音源 | 尚不支持将 VST 合成器或采样器作为工程音源；当前宿主面向音频效果器 |
| 路由 | Aux / 发送轨、侧链与轨道编组尚待实现 |
| 延迟补偿 | 离线交付已有轨道对齐；完整播放 PDC 尚待实现 |
| 插件兼容 | 离线支持取决于插件；并非所有第三方插件都已验证 |
| 测量 | 主输出未实现的响度指标显示为空，不代表已有完整母带测量功能 |

后续方向包括发送与侧链路由、完整插件延迟补偿、轨道编组和 MIDI 控制映射。具体实施以实际开发与验证结果为准。

## 从源码运行

需要 **.NET SDK 9** 构建，应用目标运行时为 .NET 8。Avalonia 12 分析器需要新版 Roslyn，不能仅安装 .NET 8 Runtime 或将 SDK 固定到 8.x。

```powershell
git clone --branch plus-develop https://github.com/XKLMY-hi/UTvTU.git
cd UTvTU
dotnet restore OpenUtau.sln -m:1
dotnet build OpenUtau.sln --no-restore -m:1
dotnet run --project OpenUtau --no-build
```

首次启动后按所用合成引擎安装声库与依赖。仓库包含 Windows 原生运行库，普通 C# / UI 开发无需重新构建 C++ 桥接。

## 开发与验证

轻量验证按相关测试小组串行执行。先构建测试项目，再使用带时间和内存上限的脚本：

```powershell
dotnet build OpenUtau.Test/OpenUtau.Test.csproj --no-restore -m:1
pwsh -File scripts/Test-Light.ps1 -Filter 'FullyQualifiedName~ResourceCenterTests'
```

脚本默认每组最多 30 秒、测试进程 384 MB，并关闭测试集合并行。仅修改文档时检查链接与 Markdown，无需启动应用或运行音频测试。

修改 VST 桥接 C++ 时，需要 Visual Studio 2022 的“使用 C++ 的桌面开发”工作负载，再运行：

```powershell
cd runtimes/vst_bridge
./build.bat
```

- [AGENTS.md](AGENTS.md)：工作区约定、构建注意事项与验证要求。
- [HANDOVER.md](HANDOVER.md)：接手记录与当前开发状态。
- [离线导出说明](.opencode/plans/offline-export-2026-10-11.md)：处理模式、插件生命周期及验证边界。
- [工具整合说明](.opencode/plans/workspace-integration-2026-10-11.md)：主窗口工具与会话管理。
- [MD3 重构记录](.opencode/plans/md3-rebuild-2026-10-11.md)：设计来源、覆盖范围与轻量验证。

## 项目结构

```text
OpenUtau.sln
├── OpenUtau/                 # Avalonia UI、视图与控件
│   ├── Views/                # 主窗口、编辑器、工具与偏好页面
│   ├── ViewModels/           # MVVM
│   ├── Controls/             # DAW 与通用控件
│   ├── Styles/               # 控件主题、尺寸、形状与动效
│   └── Theming/              # MD3 角色色与主题配置
├── OpenUtau.Core/            # 工程模型、合成、音频链、导出与 VST 宿主
├── OpenUtau.Plugin.Builtin/  # 内置音素化器
├── OpenUtau.Test/            # 单元与界面契约测试
├── VstProbe/                 # 进程外插件探测
├── runtimes/                 # 原生库与 VST3 桥接源码
└── scripts/                  # 开发与轻量验证脚本
```

## 技术栈

| 层 | 技术 |
|---|---|
| 运行时 / 构建 | .NET 8 / C#，.NET SDK 9 构建 |
| 界面 | Avalonia 12.1.0、ReactiveUI.Avalonia 14.7.1、自有 MD3 主题与控件 |
| 音频 | NAudio / MiniAudio、NWaves、自有信号链 |
| 插件宿主 | C++ / Steinberg VST3 SDK，进程外扫描探针 |
| 合成推理 | ONNX Runtime |
| 数据与日志 | YamlDotNet、Newtonsoft.Json、Serilog |
| 测试 | xUnit v3 |

## 使用的开源库

本项目的构建离不开以下开源项目，在此致谢。

### 核心

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [Avalonia UI](https://avaloniaui.net/) | 12.1.0 | MIT | 跨平台 UI 框架 |
| [ReactiveUI.Avalonia](https://github.com/reactiveui/ReactiveUI) | 14.7.1 | MIT | MVVM 响应式框架（Avalonia 12 兼容线） |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Windows 音频播放与处理 |
| [NWaves](https://github.com/ar1st0crat/NWaves) | 0.9.6 | MIT | 音频信号处理 / DSP |
| [ONNX Runtime](https://onnxruntime.ai/) | 1.23 | MIT | 机器学习推理引擎 |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | 15.1 | MIT | USTX 项目文件序列化 |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0 | MIT | JSON 序列化 |
| [Serilog](https://serilog.net/) | 4.1 | Apache-2.0 | 结构化日志 |

### 音频格式

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [NAudio.Vorbis](https://github.com/naudio/Vorbis) | 1.5.0 | MIT | Ogg Vorbis 解码 |
| [BunLabs.NAudio.Flac](https://github.com/BunLabs/NAudio.Flac) | 2.0.1 | MIT | FLAC 解码 |
| [NLayer](https://github.com/naudio/NLayer) | 1.4.0 | MIT | MP3 解码 |
| [Concentus.OggFile](https://github.com/lostromb/concentus) | 1.0.6 | Apache-2.0 | Opus 编码 |

### UI / 设计

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [HarmonyOS Sans SC](https://developer.harmonyos.com/) | — | OFL | 全局 UI 字体 |
| [Phosphor](https://phosphoricons.com) | — | MIT | 界面图标 |
| [Dotnet.Bundle](https://github.com/egramtel/dotnet-bundle) | 0.9.13 | MIT | macOS 应用打包 |

### 文件格式 / MIDI

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [DryWetMidi](https://github.com/melanchall/drywetmidi) | 7.2.0 | MIT | MIDI 文件读写 |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.48.1 | MIT | 压缩包解压 |

### 语言 / 音素处理

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [csharp-pinyin](https://github.com/poychang/csharp-pinyin) | 1.0.0 | MIT | 汉字转拼音 |
| [csharp-kana](https://github.com/poychang/csharp-kana) | 1.0.2 | MIT | 假名转换 |
| [WanaKana-net](https://github.com/MartinZikmund/WanaKana-net) | 1.0.0 | MIT | 日文假名处理 |
| [UTF.Unknown](https://github.com/CharsetDetector/UTF-unknown) | 2.5.1 | MIT | 文本编码检测 |

### 工具

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [TextCopy](https://github.com/CopyText/TextCopy) | 6.2.1 | MIT | 跨平台剪贴板 |
| [K4os.Hash.xxHash](https://github.com/k4os/K4os.Hash.xxHash) | 1.0.8 | MIT | 高速哈希 |
| [Ignore](https://github.com/nicoco007/Ignore) | 0.1.50 | MIT | .gitignore 规则解析 |
| [NumSharp](https://github.com/SciSharp/NumSharp) | 0.30.0 | Apache-2.0 | 数值计算 |
| [NeoLua](https://github.com/neolithos/NeoLua) | 1.3.19 | Apache-2.0 | Lua 脚本引擎 |
| [NetMQ](https://github.com/zeromq/netmq) | 4.0.1 | LGPL-3.0 | 进程间通信 |

### C++ 原生 (VST3 桥接)

| 库 | 版本 | 许可 | 用途 |
|----|------|------|------|
| [VST3 SDK](https://github.com/steinbergmedia/vst3sdk) | 3.8.0 | MIT / GPL-3 | VST3 宿主桥接 |
| [Worldline](https://github.com/stakira/OpenUtau) | — | MIT | 原生音频渲染引擎 |

### 设计资源

| 资源 | 许可 | 来源 |
|------|------|------|
| Phosphor | MIT | https://phosphoricons.com |
| HarmonyOS Sans SC | OFL | https://developer.harmonyos.com/ |

---

## 许可证

基于 [OpenUTAU](https://github.com/openutau/OpenUtau)，MIT License。

VST3 桥接基于 [Steinberg VST3 SDK v3.8.0](https://github.com/steinbergmedia/vst3sdk)，MIT / GPL-3 双许可（本项目使用 MIT 许可部分）。

Phosphor 图标使用 MIT License。

---

## 致谢

- [OpenUTAU](https://github.com/openutau/OpenUtau) 原版项目及全体贡献者
- Steinberg 提供 VST3 SDK
- 歌声合成社区
- 本项目以 **Vibe Coding** 方式开发 —— 借助 AI 辅助架构设计、代码编写与调试

---

**By XKLMY · Vibe Coding**
