# UTvTU

[简体中文](README.md)

**UTvTU is a vocal production workstation based on [OpenUTAU](https://github.com/openutau/OpenUtau)**, bringing vocal editing, synthesis, mixing and audio delivery into one workflow. Development continues on the existing UTvTU codebase, with upstream OpenUTAU as a technical reference.

The current target is **Windows 10 / 11 x64**. Its own Material Design 3 themes and controls combine dynamic colours and consistent interactions with the density needed for DAW editing.

> Formerly **OpenUTAU Plus**, renamed in 2026-10. The [previous repository](https://github.com/XKLMY-hi/OpenUTAU-Plus) remains an archive. The user data directory is still `OpenUtau Plus`; the rename requires no data migration.

## Download and Install

Download published builds from [GitHub Releases](https://github.com/XKLMY-hi/UTvTU/releases). This README describes the current `plus-develop` branch; check each release's notes for the features included in its installer.

- `UTvTU-<version>.exe` includes the runtime; no separate .NET installation is needed.
- Installation is per user by default. A verified UTvTU / OpenUTAU Plus installation can be upgraded while preserving user data, projects and voicebanks.
- Uninstall through Windows Installed Apps or `UTvTU-Uninstall.exe` in the installation directory. Only manifest-owned application files are removed; user data is preserved by default.
- See the [installer documentation](installer/README.md) for packaging and command-line usage. The installer rewrite describes current sources; older downloads retain the behavior of their corresponding release.

## Production Workflow

1. Create or open a project from the welcome page, then install and select a voicebank.
2. Arrange tracks and backing audio; edit notes, lyrics, pitch and expressions in the piano roll.
3. Adjust faders, pan, mute and solo in the mixer, and process vocals with built-in or VST3 effects.
4. Select tracks, a time range and file settings on the render page; export a mixdown or individual stems.

## Features

### Editing and Projects

- OpenUTAU vocal editing, phonemizers and synthesis, including classic UTAU voicebanks and engines such as DiffSinger.
- Arrangement, piano roll and mixer views share the main window. Piano roll and mixer views can also be detached.
- `.ustxp` projects persist UTvTU settings and VST state, with `.ustx` compatibility and upstream project import capabilities.
- Undo / redo for track and effect-chain edits.

### Mixing and Effects

- Track faders, pan, mute / solo, live meters and master output controls.
- Built-in EQ, compressor and reverb with live adjustment, bypass and a collapsible effects panel.
- VST3 audio-effect scanning, loading, native editors and state persistence; chain insertion, removal, bypass and reordering.
- A library for voicebanks, backing audio and effects, with search and applicable audition / drag-and-drop operations.

### Rendering and Delivery

- **Offline export by default**, with separate plugin instances for the export job. Audio delivery does not require silent playback through an output device.
- Stereo mixdown or stems, whole-project / loop / custom ranges, track selection and configurable effect tails.
- WAV 16-bit, 24-bit and 32-bit float; 44.1 / 48 / 96 kHz output sample rates.
- Options for VST, built-in effects, track gain / pan and master settings, plus progress, cancellation and output-folder access.
- A manually selected real-time compatibility mode for plugins that cannot process offline. Failures are reported without automatically changing the processing mode.

Output sample rate is a delivery setting; synthesis and effects use the internal project rate. Offline export aligns selected tracks for plugin latency. Full playback delay compensation remains future work.

### Material Design 3 Interface

- HCT / CAM16 role colours from a custom seed or the Windows accent, with light / dark themes, palette variants and contrast settings.
- Shared container, shape, outline and control-state roles, with compact DAW editor controls.
- Consistent buttons, inputs, selectors, sliders, menus and overlays, including keyboard focus and reduced motion.
- Welcome and preferences pages share the page system; rendering, voicebank resources and package management use the main-window tool workspace.
- Context tools use the bottom panel or main-window overlays. Native VST editors and system file pickers remain separate windows.
- Voicebank lists display real voicebank avatars, with a fallback icon when no avatar is available.

## Current Scope

Development prioritizes the **Windows vocal production workflow**, stability, project compatibility and control interactions. macOS / Linux are outside the current development and validation focus.

| Boundary | Status |
|---|---|
| VST instruments | Synths and samplers cannot be used as project sound sources; the host currently supports audio effects |
| Routing | Aux / send buses, sidechains and track groups remain future work |
| Latency compensation | Offline track alignment exists; complete playback PDC remains future work |
| Plugin compatibility | Offline support depends on the plugin; not all third-party plugins have been validated |
| Metering | Unimplemented loudness metrics are shown as unavailable; full mastering metering is not yet provided |

Future directions include sends and sidechains, full plugin delay compensation, track groups and MIDI control mapping. Availability follows implementation and validation.

## Build and Run

Use **.NET SDK 9** to build. The application targets .NET 8; Avalonia 12 analyzers require a newer Roslyn compiler, so installing only the .NET 8 Runtime or pinning SDK 8.x is insufficient.

```powershell
git clone --branch plus-develop https://github.com/XKLMY-hi/UTvTU.git
cd UTvTU
dotnet restore OpenUtau.sln -m:1
dotnet build OpenUtau.sln --no-restore -m:1
dotnet run --project OpenUtau --no-build
```

Install voicebanks and engine dependencies as needed after launching. Windows native libraries are included; normal C# / UI development does not require rebuilding the C++ bridge.

## Development and Validation

Build the test project, then run a relevant small group with the guarded runner:

```powershell
dotnet build OpenUtau.Test/OpenUtau.Test.csproj --no-restore -m:1
pwsh -File scripts/Test-Light.ps1 -Filter 'FullyQualifiedName~ResourceCenterTests'
```

The default limits are 30 seconds and 384 MB per test process, with test collections run serially. Documentation changes only need Markdown and link checks.

For VST bridge C++ changes, install Visual Studio 2022 with the Desktop development with C++ workload, then run:

```powershell
cd runtimes/vst_bridge
./build.bat
```

- [AGENTS.md](AGENTS.md): workspace rules and build / validation requirements.
- [HANDOVER.md](HANDOVER.md): current development context.
- [Offline export notes](.opencode/plans/offline-export-2026-10-11.md): processing modes, plugin lifetime and validation boundaries.
- [Workspace integration notes](.opencode/plans/workspace-integration-2026-10-11.md): tool hosting and sessions.
- [MD3 implementation notes](.opencode/plans/md3-rebuild-2026-10-11.md): design sources, coverage and validation.

## Project Structure

```text
OpenUtau.sln
├── OpenUtau/                 # Avalonia views, view models, controls and themes
├── OpenUtau.Core/            # Project models, synthesis, audio, export and VST hosting
├── OpenUtau.Plugin.Builtin/  # Built-in phonemizers
├── OpenUtau.Test/            # Unit and UI contract tests
├── VstProbe/                 # Out-of-process plugin discovery
├── runtimes/                 # Native libraries and VST3 bridge source
└── scripts/                  # Development and guarded test scripts
```

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime / build | .NET 8 / C#, built with .NET SDK 9 |
| Interface | Avalonia 12.1.0, ReactiveUI.Avalonia 14.7.1, own MD3 themes and controls |
| Audio | NAudio / MiniAudio, NWaves, own signal chain |
| Plugin hosting | C++ / Steinberg VST3 SDK, out-of-process discovery |
| Synthesis inference | ONNX Runtime |
| Data and logging | YamlDotNet, Newtonsoft.Json, Serilog |
| Tests | xUnit v3 |

## Open Source Libraries

This project is built with the following open source libraries. We are grateful to all maintainers.

### Core

| Library | Version | License | Purpose |
|----|------|------|------|
| [Avalonia UI](https://avaloniaui.net/) | 12.1.0 | MIT | Cross-platform UI framework |
| [ReactiveUI.Avalonia](https://github.com/reactiveui/ReactiveUI) | 14.7.1 | MIT | Reactive MVVM integration |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | Windows audio playback & processing |
| [NWaves](https://github.com/ar1st0crat/NWaves) | 0.9.6 | MIT | Audio signal processing / DSP |
| [ONNX Runtime](https://onnxruntime.ai/) | 1.23 | MIT | Machine learning inference engine |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | 15.1 | MIT | USTX project file serialization |
| [Newtonsoft.Json](https://www.newtonsoft.com/json) | 13.0 | MIT | JSON serialization |
| [Serilog](https://serilog.net/) | 4.1 | Apache-2.0 | Structured logging |

### Audio Formats

| Library | Version | License | Purpose |
|----|------|------|------|
| [NAudio.Vorbis](https://github.com/naudio/Vorbis) | 1.5.0 | MIT | Ogg Vorbis decoding |
| [BunLabs.NAudio.Flac](https://github.com/BunLabs/NAudio.Flac) | 2.0.1 | MIT | FLAC decoding |
| [NLayer](https://github.com/naudio/NLayer) | 1.4.0 | MIT | MP3 decoding |
| [Concentus.OggFile](https://github.com/lostromb/concentus) | 1.0.6 | Apache-2.0 | Opus encoding |

### UI / Design

| Library | Version | License | Purpose |
|----|------|------|------|
| [Phosphor](https://phosphoricons.com) | — | MIT | User interface icons |
| [Dotnet.Bundle](https://github.com/egramtel/dotnet-bundle) | 0.9.13 | MIT | macOS app bundling |

### File Formats / MIDI

| Library | Version | License | Purpose |
|----|------|------|------|
| [DryWetMidi](https://github.com/melanchall/drywetmidi) | 7.2.0 | MIT | MIDI file read/write |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.48.1 | MIT | Archive extraction |

### Language / Phoneme Processing

| Library | Version | License | Purpose |
|----|------|------|------|
| [csharp-pinyin](https://github.com/poychang/csharp-pinyin) | 1.0.0 | MIT | Hanzi to Pinyin conversion |
| [csharp-kana](https://github.com/poychang/csharp-kana) | 1.0.2 | MIT | Kana conversion |
| [WanaKana-net](https://github.com/MartinZikmund/WanaKana-net) | 1.0.0 | MIT | Japanese kana processing |
| [UTF.Unknown](https://github.com/CharsetDetector/UTF-unknown) | 2.5.1 | MIT | Text encoding detection |

### Utilities

| Library | Version | License | Purpose |
|----|------|------|------|
| [TextCopy](https://github.com/CopyText/TextCopy) | 6.2.1 | MIT | Cross-platform clipboard |
| [K4os.Hash.xxHash](https://github.com/k4os/K4os.Hash.xxHash) | 1.0.8 | MIT | Fast hashing |
| [Ignore](https://github.com/nicoco007/Ignore) | 0.1.50 | MIT | .gitignore rule parsing |
| [NumSharp](https://github.com/SciSharp/NumSharp) | 0.30.0 | Apache-2.0 | Numerical computing |
| [NeoLua](https://github.com/neolithos/NeoLua) | 1.3.19 | Apache-2.0 | Lua scripting engine |
| [NetMQ](https://github.com/zeromq/netmq) | 4.0.1 | LGPL-3.0 | Inter-process communication |

### C++ Native (VST3 Bridge)

| Library | Version | License | Purpose |
|----|------|------|------|
| [VST3 SDK](https://github.com/steinbergmedia/vst3sdk) | 3.8.0 | MIT / GPL-3 | VST3 host bridging |
| [Worldline](https://github.com/stakira/OpenUtau) | — | MIT | Native audio rendering engine |

### Design Assets

| Resource | License | Source |
|------|------|------|
| Phosphor | MIT | https://phosphoricons.com |

---

## License

Based on [OpenUTAU](https://github.com/openutau/OpenUtau), MIT License.

The VST3 bridge uses [Steinberg VST3 SDK v3.8.0](https://github.com/steinbergmedia/vst3sdk), dual-licensed MIT / GPL-3 (this project uses the MIT-licensed portions).

Phosphor icons are MIT licensed.

---

## Acknowledgments

- [OpenUTAU](https://github.com/openutau/OpenUtau) original project and all contributors
- Steinberg for the VST3 SDK
- The vocal synthesis community
- This project was developed through **Vibe Coding** — using AI assistance for architecture design, code, and debugging
