# OpenUTAU Plus

An enhanced fork of **OpenUTAU** — a vocal synthesis workstation with DAW-style mixer and VST3 plugin support.

Based on [OpenUTAU](https://github.com/openutau/OpenUtau) (MIT License)

---

## What's New

### 🎨 Material Design 3 System (2026-09 rework)
- Design-pack driven: colours, sizes and structure taken from the delivered specs; MD3 colour pool
  generated from a dynamic seed, container-tier backgrounds, own control themes
- Single-window views (welcome / editor / preferences) sharing one app bar and status bar
- Motion uses Avalonia's built-in Transitions; **the in-house theme system has been removed entirely**

### 🪟 Modern UI Overhaul (2026-08 stage, partly superseded)
- **Avalonia 12.1** — UI framework upgraded from 11.2.4 (rendering architecture rewrite, Skia 3.0, official drawn-decorations API)
- **Self-drawn window chrome** — implemented with the official **WindowDrawnDecorations** API (replacing the hand-rolled title bar): 1px border + shadow layering, unified title bar (icon / title / Min / Max / Close) across 30+ windows
- **Warm-gray dark theme** — custom palette (base `#1e1e28` · surface `#282838` · accent `#c73a3f`)
- **HarmonyOS Sans SC** fonts + **Heroicons Solid** icons + 8px unified corner radius
- **Full Chinese localization**

### 🎚️ DAW-Style Mixer
- Vertical faders, -24dB ~ +12dB
- 30fps real-time peak meters (LevelTracker)
- Mute / Solo buttons with color indicators
- Double-click numeric editing (volume / pan)
- **Ctrl+M** shortcut to toggle

### 🔌 VST3 Audio Effect Support
- Load any VST3 audio effect (compressors, EQs, reverbs, delays, etc.)
- **Native GUI popup** — standalone Win32 window embedding the plugin interface
- **Real-time parameter sync** — GUI knob changes immediately affect audio output
- **Instrument filtering** — automatically excludes synths/samplers (no audio input plugins)
- Up to 8 plugin slots per track, processed in series
- 3 built-in effects: EQ / Compressor / Reverb (based on upstream "Audition Effect", redesigned with collapsible panels and bypass toggles)

### 📦 .ustxp Project Format
- Plus-native format, `ustxpVersion: 1.0`
- VST plugin parameter persistence — re-open a project and all plugin settings are restored
- Backward-compatible with `.ustx`
---

## 2026-08 Framework Upgrade: Avalonia 12

- **Avalonia 12.1.0** (from 11.2.4) — rendering architecture rewrite (significant FPS gains on complex visual trees), Skia 3.0 pipeline, compiled bindings, official drawn window decorations
- **Window chrome rewritten** — the official **WindowDrawnDecorations** API replaces the hand-rolled title bar: 1px border + shadow layering everywhere, border removed on maximize, drag / caption buttons / fullscreen hover bar handled by the framework
- **Dependency migration** — `Avalonia.ReactiveUI` → `ReactiveUI.Avalonia` (Rx-compatible line, zero ViewModel changes), xunit v2 → v3, HarfBuzz text shaping added
- Paves the way for a UI component library (Semi.Avalonia candidate)

---

## Design Goals

OpenUTAU Plus aims to evolve OpenUTAU from a vocal synthesis editor into a **vocal-centric DAW workstation**, enabling the full mixing, mastering, and effects workflow without leaving the application.

### Near-term (v1.x)

- ✅ Mixer console (per-track faders, pan, mute/solo, meters)
- ✅ VST3 audio effect support (loading, GUI, real-time parameters)
- ✅ `.ustxp` project format (VST parameter persistence)
- ✅ Export/render with VST effects (silent-playback recording)
- 🚧 VST instrument support (loading synths/samplers as sound sources)
- 🚧 macOS / Linux cross-platform support

### Long-term Vision

- Send tracks / Aux buses / Sidechain compression
- Plugin delay compensation (PDC)
- Track groups and VCA faders
- MIDI control surface mapping
- Built-in sampler and drum machine

---

## Quick Start

```bash
git clone https://github.com/XKLMY-hi/UTvTU.git
cd UTvTU
dotnet restore
dotnet run --project OpenUtau
```

Requires [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

> **Platform Support**: OpenUTAU Plus is built with C# / Avalonia and runs on Windows / macOS / Linux. However, **the VST3 bridge DLL is currently only compiled for Windows x64**. VST features are unavailable on macOS and Linux for now.

---

## Limitations & Known Gaps

These are design decisions or work-in-progress items. **Please read before using**:

| Limitation | Details |
|------------|---------|
| ~~Export excludes VST effects~~ **✅ Resolved** | Real-time recording mode: silent-playback full signal chain capture, including VST + built-in FX + volume/pan |
| **No VST instrument support** | VST synths/samplers (instrument plugins) cannot be loaded as sound sources. The scanner automatically filters them out |
| **Windows x64 only** | The bridge DLL is compiled for Windows x64 only. macOS / Linux users cannot use VST features |
| **No send/Aux buses** | Aux buses and sidechain compression are not yet implemented |
| **Single-threaded rendering** | The render engine processes tracks serially; parallelization is not yet introduced |

Solutions for all of the above are planned in the [Design Goals](#design-goals) roadmap.

---

## Building the VST Bridge DLL (Optional)

Only needed when modifying bridge C++ code:

```bat
cd runtimes\vst_bridge
build.bat
```

Requires **Visual Studio 2022** (Community Edition is fine) with the "Desktop development with C++" workload.

---

## Architecture

```
C# (Avalonia UI)                         C++ (VST3 Bridge)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

VstPluginRegistry  ─── scans system VST3 directories
       │
VstPluginManager   ─── per-track instance management (UI loads, audio reads-only)
       │
VstEffect : IEffect ─── inserted into EffectChain
       │                    │
       │ [P/Invoke]         │ EffectChain.Mix()
       ▼                    ▼
  VstBridge.cs          fx.Process(scratch)
       │
       ▼ [DllImport]
  vst_bridge.dll (Steinberg VST3 SDK v3.8.0)
       │
       ├─ IComponent / IAudioProcessor ── audio processing
       ├─ IEditController / createView ── native GUI
       └─ performEdit → atomic queue → inputParameterChanges
```

### Parameter Flow

```
Plugin GUI knob turn
  → Controller::setParamNormalized()
    → BridgeCompHandler::performEdit()
      → atomic ring buffer (lock-free, SPSC)
        → consumed by vst_process()
          → ProcessData::inputParameterChanges
            → IAudioProcessor::process() → real-time audio change ✅
```

---

## Project Structure

```
OpenUtau.sln
├── OpenUtau/               # Main app UI (Avalonia)
│   ├── Views/              # MainWindow, MixerWindow, VstEditorWindow, etc.
│   ├── ViewModels/         # MVVM
│   └── Controls/           # Custom controls (MixerTrackStrip, etc.)
├── OpenUtau.Core/          # Core logic
│   ├── Ustx/               # Data model
│   ├── Render/             # Render engine
│   ├── SignalChain/        # Audio signal chain (EffectChain, LevelTracker, Fader)
│   └── Vst/                # VST host (VstEffect, VstBridge, VstPluginManager)
├── OpenUtau.Plugin.Builtin/
├── OpenUtau.Test/
├── runtimes/               # Native platform libraries
│   ├── vst_bridge/         # C++ bridge project (CMake)
│   └── win-x64/native/     # vst_bridge.dll + worldline.dll
└── VstTest/                # VST compatibility test suite
```

---

## Development

```bash
dotnet build                                  # Build all
dotnet run --project OpenUtau                 # Run
dotnet test                                   # Unit tests
dotnet run --project VstTest                  # VST compatibility tests
```

See [CLAUDE.md](./CLAUDE.md) for the full architecture and development guide.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 8.0 / C# 12 |
| UI Framework | Avalonia 12.x + ReactiveUI MVVM |
| Audio Playback | NAudio (WASAPI) / MiniAudio |
| Audio DSP | NWaves |
| Signal Chain | Custom ISignalSource / IEffect interface |
| VST3 Host | C++ / Steinberg VST3 SDK v3.8.0 |
| AI Inference | ONNX Runtime (DirectML GPU acceleration) |
| Icons | Lucide Icons |
| Serialization | YamlDotNet / Newtonsoft.Json |
| Logging | Serilog |
| Testing | xUnit |

---

## Open Source Libraries

This project is built with the following open source libraries. We are grateful to all maintainers.

### Core

| Library | Version | License | Purpose |
|----|------|------|------|
| [Avalonia UI](https://avaloniaui.net/) | 12.1.0 | MIT | Cross-platform UI framework |
| [ReactiveUI](https://www.reactiveui.net/) | 19.5 | MIT | MVVM reactive framework |
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
| [Heroicons](https://heroicons.com/) | — | MIT | User interface icons |
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
| Lucide Icons | ISC | https://lucide.dev/ |

---

## License

Based on [OpenUTAU](https://github.com/openutau/OpenUtau), MIT License.

The VST3 bridge uses [Steinberg VST3 SDK v3.8.0](https://github.com/steinbergmedia/vst3sdk), dual-licensed MIT / GPL-3 (this project uses the MIT-licensed portions).

Lucide icons are ISC licensed.

---

## Acknowledgments

- [OpenUTAU](https://github.com/openutau/OpenUtau) original project and all contributors
- Steinberg for the VST3 SDK
- The vocal synthesis community
- This project was developed through **Vibe Coding** — using DeepSeek V4 Pro AI to assist with architecture design, code generation, and debugging
