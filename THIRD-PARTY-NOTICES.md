# Third-Party Notices — UTvTU

UTvTU（原 OpenUTAU Plus）基于 OpenUTAU，并使用下列组件与资源。主项目 [LICENSE.txt](LICENSE.txt) 保留 OpenUTAU / StAkira 的版权声明；各第三方组件的版权和许可独立保留。

本文件按 2026-10-17 的工程声明、对应 NuGet 包元数据、随附许可文件和原生依赖配置整理。下表同时包含运行、构建、测试及条件引用，**不表示每个组件都包含在每一份 Windows 安装包中**。传递依赖及原生库的附加许可仍以具体发行包的原始声明为准。

## 直接 NuGet 引用

包名链接指向工程所引用的具体版本。无 SPDX 表达式的旧包提供原始声明入口，不将未知许可推定为 MIT。

| 组件 | 工程声明版本 | 许可 / 声明 |
|---|---|---|
| [Avalonia](https://www.nuget.org/packages/Avalonia/12.1.0) | 12.1.0 | MIT |
| [Avalonia.Controls.ColorPicker](https://www.nuget.org/packages/Avalonia.Controls.ColorPicker/12.1.0) | 12.1.0 | MIT |
| [Avalonia.Controls.DataGrid](https://www.nuget.org/packages/Avalonia.Controls.DataGrid/12.1.0) | 12.1.0 | MIT |
| [Avalonia.Desktop](https://www.nuget.org/packages/Avalonia.Desktop/12.1.0) | 12.1.0 | MIT |
| [Avalonia.HarfBuzz](https://www.nuget.org/packages/Avalonia.HarfBuzz/12.1.0) | 12.1.0 | MIT |
| [Avalonia.Headless.XUnit](https://www.nuget.org/packages/Avalonia.Headless.XUnit/12.1.0) | 12.1.0 | MIT |
| [Avalonia.Themes.Fluent](https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.0) | 12.1.0 | MIT |
| [BunLabs.NAudio.Flac](https://www.nuget.org/packages/BunLabs.NAudio.Flac/2.0.1) | 2.0.1 | MS-PL |
| [Concentus.OggFile](https://www.nuget.org/packages/Concentus.OggFile/1.0.6) | 1.0.6 | MS-PL |
| [coverlet.collector](https://www.nuget.org/packages/coverlet.collector/6.0.2) | 6.0.2 | MIT |
| [csharp-kana](https://www.nuget.org/packages/csharp-kana/1.0.2) | 1.0.2 | Apache-2.0 |
| [csharp-pinyin](https://www.nuget.org/packages/csharp-pinyin/1.0.0) | 1.0.0 | Apache-2.0 |
| [Dotnet.Bundle](https://www.nuget.org/packages/Dotnet.Bundle/0.9.13) | 0.9.13 | 见该版本项目声明 |
| [Ignore](https://www.nuget.org/packages/Ignore/0.1.50) | 0.1.50 | 见该版本项目声明 |
| [K4os.Hash.xxHash](https://www.nuget.org/packages/K4os.Hash.xxHash/1.0.8) | 1.0.8 | [项目许可](https://github.com/MiloszKrajewski/K4os.Hash.xxHash/blob/master/LICENSE) |
| [Melanchall.DryWetMidi](https://www.nuget.org/packages/Melanchall.DryWetMidi/7.2.0) | 7.2.0 | MIT |
| [Microsoft.ML.OnnxRuntime](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.23.2) | 1.23.2 | 见该版本项目声明 |
| [Microsoft.ML.OnnxRuntime.DirectML](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.DirectML/1.23.0) | 1.23.0 | MIT（包内 LICENSE） |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.11.1) | 17.11.1 | MIT |
| [NAudio](https://www.nuget.org/packages/NAudio/2.2.1) | 2.2.1 | MIT（包内 license.txt） |
| [NAudio.Core](https://www.nuget.org/packages/NAudio.Core/2.2.1) | 2.2.1 | MIT |
| [NAudio.Vorbis](https://www.nuget.org/packages/NAudio.Vorbis/1.5.0) | 1.5.0 | MIT |
| [NeoLua](https://www.nuget.org/packages/NeoLua/1.3.19) | 1.3.19 | Apache-2.0 |
| [NetMQ](https://www.nuget.org/packages/NetMQ/4.0.1.13) | 4.0.1.13 | [LGPL-3.0 及项目声明](https://github.com/zeromq/netmq/blob/4.0.1.13/COPYING.LESSER) |
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3) | 13.0.3 | MIT |
| [NLayer.NAudioSupport](https://www.nuget.org/packages/NLayer.NAudioSupport/1.4.0) | 1.4.0 | MIT |
| [NumSharp](https://www.nuget.org/packages/NumSharp/0.30.0) | 0.30.0 | Apache-2.0（包内 LICENSE） |
| [NWaves](https://www.nuget.org/packages/NWaves/0.9.6) | 0.9.6 | MIT |
| [ReactiveUI.Avalonia](https://www.nuget.org/packages/ReactiveUI.Avalonia/14.7.1) | 14.7.1 | MIT |
| [ReactiveUI.Fody](https://www.nuget.org/packages/ReactiveUI.Fody/19.5.41) | 19.5.41 | MIT |
| [Serilog](https://www.nuget.org/packages/Serilog/4.1.0) | 4.1.0 | Apache-2.0 |
| [Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console/6.0.0) | 6.0.0 | Apache-2.0 |
| [Serilog.Sinks.Debug](https://www.nuget.org/packages/Serilog.Sinks.Debug/3.0.0) | 3.0.0 | Apache-2.0 |
| [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/6.0.0) | 6.0.0 | Apache-2.0 |
| [SharpCompress](https://www.nuget.org/packages/SharpCompress/0.48.1) | 0.48.1 | MIT |
| [System.Buffers](https://www.nuget.org/packages/System.Buffers/4.6.0) | 4.6.0 | MIT |
| [System.ComponentModel.Annotations](https://www.nuget.org/packages/System.ComponentModel.Annotations/5.0.0) | 5.0.0 | MIT |
| [System.IO.Packaging](https://www.nuget.org/packages/System.IO.Packaging/9.0.2) | 9.0.2 | MIT |
| [System.Text.Encoding.CodePages](https://www.nuget.org/packages/System.Text.Encoding.CodePages/9.0.2) | 9.0.2 | MIT |
| [System.Text.Json](https://www.nuget.org/packages/System.Text.Json/9.0.2) | 9.0.2 | MIT |
| [TextCopy](https://www.nuget.org/packages/TextCopy/6.2.1) | 6.2.1 | MIT |
| [UTF.Unknown](https://www.nuget.org/packages/UTF.Unknown/2.5.1) | 2.5.1 | [MPL-1.1 声明](https://github.com/CharsetDetector/UTF-unknown/blob/master/license/MPL-1.1.txt) |
| [WanaKana-net](https://www.nuget.org/packages/WanaKana-net/1.0.0) | 1.0.0 | MIT |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 3.1.5 | Apache-2.0 |
| [xunit.v3.assert](https://www.nuget.org/packages/xunit.v3.assert/3.2.2) | 3.2.2 | Apache-2.0 |
| [xunit.v3.core](https://www.nuget.org/packages/xunit.v3.core/3.2.2) | 3.2.2 | Apache-2.0 |
| [YamlDotNet](https://www.nuget.org/packages/YamlDotNet/15.1.2) | 15.1.2 | MIT（包内 LICENSE.txt） |

## 原生音频、算法与 SDK

| 组件 / 来源 | 用途与声明 |
|---|---|
| [OpenUTAU / Worldline](https://github.com/openutau/OpenUtau)，本仓 [cpp/worldline](cpp/worldline) | 上游原生歌声合成；保留主项目及源文件中的作者和版权声明 |
| [WORLD](https://github.com/mmorise/World)、[libgvps](https://github.com/Sleepwalking/libgvps)、[libpyin](https://github.com/Sleepwalking/libpyin) | 合成与分析依赖；具体固定来源见 [cpp/WORKSPACE.bazel](cpp/WORKSPACE.bazel)，许可见各项目 |
| [libnpy](https://github.com/llohse/libnpy)、[spline](https://github.com/ttk592/spline) | 数据读写与插值；固定来源及随附声明见原生配置与各项目 |
| [miniaudio](https://github.com/mackron/miniaudio) | 原生音频设备与处理；保留其公共领域 / MIT-0 等原始许可选择与文件声明 |
| [Abseil](https://github.com/abseil/abseil-cpp)、[xxHash](https://github.com/Cyan4973/xxHash)、[GoogleTest](https://github.com/google/googletest) | 原生基础与测试；版本配置见 [cpp/MODULE.bazel](cpp/MODULE.bazel)，各自许可见项目 |
| [Steinberg VST3 SDK](https://github.com/steinbergmedia/vst3sdk) | VST3 桥接；当前使用的 SDK 根许可为 [MIT](https://github.com/steinbergmedia/vst3sdk/blob/master/LICENSE.txt)，保留 Steinberg Media Technologies GmbH 署名 |
| Microsoft Visual C++ Runtime | Windows 原生依赖所需的微软可再分发运行时，按对应 Visual Studio Redist 许可分发，并非本项目的 MIT 源码 |

Steinberg 的名称和 VST 相关商标归相应权利人；使用 SDK 不代表获得其背书。SDK 源码目录不纳入本仓 Git，原生桥构建说明见 [runtimes/vst_bridge/CMakeLists.txt](runtimes/vst_bridge/CMakeLists.txt)。

## 设计、颜色与字体

| 来源 | 用途与声明 |
|---|---|
| Google [Material Design](https://m3.material.io/) 与 [Material Color Utilities](https://github.com/material-foundation/material-color-utilities) | 设计与 HCT / CAM16 颜色算法参考；Material Color Utilities 使用 Apache-2.0，保留原始作者与许可来源 |
| [AndroidX](https://github.com/androidx/androidx) MaterialShapes、graphics-shapes、状态令牌 | 几何参数和交互状态参考；原始快照与 Apache-2.0 来源记录见设计目录中的[原生几何说明](.opencode/design/md3-rebuild-2026-10-11/README.md) |
| [Phosphor Icons](https://github.com/phosphor-icons/core) | MIT；产品路径与来源注释在 [Icons.axaml](OpenUtau/Assets/Icons.axaml) |
| **Huawei HarmonyOS Sans SC** | 使用 **HarmonyOS Sans Fonts License Agreement**，并非 OFL；完整文本见 [字体许可](OpenUtau/Assets/Fonts/LICENSE.txt)。允许按协议将未修改字体随应用分发，保留作者与协议，不能将字体单独再分发或转售 |

## 模型、词典与社区资源

- [CREPE](https://github.com/marl/crepe)：Jong Wook Kim 与贡献者；相关资源的 MIT 声明见 [OpenUtau.Core/Analysis/Crepe/LICENSE.txt](OpenUtau.Core/Analysis/Crepe/LICENSE.txt)。
- 音素化器、G2P、词典及语言资源延续 OpenUTAU 上游来源；保留资源自身的署名与条款，感谢其作者和维护者。
- DiffSinger、ENUNU、Vogen 等引擎相关模型与声库不因主程序开源而自动改用 MIT；下载和使用的具体模型遵循提供方许可。
- 声库录音、角色形象、第三方插件及截图所示内容归各创作者，项目鸣谢不改变其使用条件。

## 历史组件

历史版本曾使用 SukiUI，相关贡献仍在 README 鸣谢中保留；当前工程不将它列为直接运行依赖。Dotnet.Bundle 仍是工程中保留的打包工具，主要用于既有 macOS 配置；当前新增开发仍以 Windows 为目标。

完整产品与社区致谢见 [README.md](README.md)。
