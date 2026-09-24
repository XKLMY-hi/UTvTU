# Third-Party Notices — OpenUTAU Plus

OpenUTAU Plus 使用以下第三方组件。本文件随安装包分发（安装目录内 `THIRD-PARTY-NOTICES.md`）。

主项目许可证见 [LICENSE.txt](LICENSE.txt)（MIT，含上游 OpenUTAU Copyright (c) 2014 StAkira）。

## .NET / UI 框架

| 组件 | 许可证 | 用途 |
|------|--------|------|
| Avalonia（含 Desktop/HarfBuzz/DataGrid 等） | MIT | 跨平台 UI 框架 |
| SukiUI | MIT | 主题与控件库 |
| ReactiveUI / ReactiveUI.Avalonia / Fody | MIT | MVVM 框架 |
| SharpCompress | MIT | 压缩包解压 |
| TextCopy | MIT | 剪贴板 |
| System.Text.Json / System.IO.Packaging 等 | MIT | .NET 运行时组件 |

## 音频 / 渲染 / AI

| 组件 | 许可证 | 用途 |
|------|--------|------|
| NAudio（含 NAudio.Core/Vorbis/Flac 支持） | MIT | 音频播放/格式 |
| NWaves | MIT | 音频分析与 FFT |
| Melanchall.DryWetMidi | MIT | MIDI 处理 |
| SkiaSharp / HarfBuzz | MIT | 渲染与文本整形 |
| Microsoft.ML.OnnxRuntime / DirectML | MIT | ONNX 推理（DiffSinger 等） |
| worldline / miniaudio | MIT / Public Domain | 音频设备 |
| BunLabs.NAudio.Flac / Concentus.OggFile / NLayer.NAudioSupport | MIT | 音频格式支持 |

## 语言 / 文本 / 序列化

| 组件 | 许可证 | 用途 |
|------|--------|------|
| YamlDotNet | MIT | USTX 序列化 |
| Newtonsoft.Json | MIT | JSON |
| Serilog | Apache-2.0 | 结构化日志 |
| WanaKana-net / csharp-kana / csharp-pinyin | MIT | 假名/拼音处理 |
| UTF.Unknown | MIT | 编码检测 |
| K4os.Hash.xxHash | MIT | 哈希 |
| NumSharp / NeoLua / NetMQ | MIT | 科学计算/脚本/消息 |

## 测试

| 组件 | 许可证 | 用途 |
|------|--------|------|
| xUnit | Apache-2.0 | 单元测试 |

## 原生 SDK（非开源）

| 组件 | 许可证 | 用途 |
|------|--------|------|
| **Steinberg VST3 SDK**（`runtimes/vst3sdk/`） | **专有（Steinberg 许可协议，见 runtimes/vst3sdk/LICENSE.txt）** | VST3 插件宿主桥接（vst_bridge.dll）。仅编译分发，不随源码再分发。 |

## 字体

| 组件 | 许可证 | 用途 |
|------|--------|------|
| **HarmonyOS Sans SC**（Regular/Medium/Bold/Light，随包分发） | 华为 HarmonyOS Sans 免费商用授权（见 `Assets/Fonts/LICENSE.txt`） | 应用内嵌字体——**允许打包进应用分发，禁止单独转售字体文件** |

字体经 Avalonia 资源嵌入应用（`OpenUtau/Assets/Fonts/`），系统未安装时仍完整呈现；许可文本随包分发。

## 图标

| 组件 | 许可证 | 用途 |
|------|--------|------|
| **Phosphor Icons**（实心圆润，256px 网格，55 个图标，`OpenUtau/Assets/Icons.axaml`） | MIT（https://phosphoricons.com） | 界面图标 |
| *Lucide*（描边风格，ISC，https://lucide.dev） | ISC | **计划替换**：新 UI 重做时将整体换为 Lucide 并全量替换上述图标；届时本表与 `Icons.axaml` 头部署名同步更新（见 `.opencode/plans/ui-rework-decisions.md` 第 3.F 节） |

---

*本清单依据各组件官方许可文件核对（2026-08）。NuGet 包内均含各自 LICENSE 文本。*
