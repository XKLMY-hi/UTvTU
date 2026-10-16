# UTvTU · Vocal Production Workstation

**Edit, synthesize, mix and deliver vocals in one workspace.**

Built on [OpenUTAU](https://github.com/openutau/OpenUtau), currently targeting **Windows 10 / 11 x64**. Maintained by [XKLMY](https://github.com/XKLMY-hi), formerly OpenUTAU Plus.

[Download](https://github.com/XKLMY-hi/UTvTU/releases) · [Report an issue](https://github.com/XKLMY-hi/UTvTU/issues) · [简体中文](README.md)

## Product Screenshots

![UTvTU welcome page](screenshots/readme-2026-10-17/welcome.png)

<table>
  <tr>
    <td width="50%" align="center"><strong>Arrangement workspace</strong><br><a href="screenshots/readme-2026-10-17/workspace.png"><img src="screenshots/readme-2026-10-17/workspace.png" alt="UTvTU tracks and library" width="640"></a></td>
    <td width="50%" align="center"><strong>Piano roll</strong><br><a href="screenshots/readme-2026-10-17/piano-roll.png"><img src="screenshots/readme-2026-10-17/piano-roll.png" alt="UTvTU note, pitch and expression editing" width="640"></a></td>
  </tr>
  <tr>
    <td width="50%" align="center"><strong>Mixer and effects rack</strong><br><a href="screenshots/readme-2026-10-17/mixer-effects.png"><img src="screenshots/readme-2026-10-17/mixer-effects.png" alt="UTvTU mixer with built-in effects and a VST3 card" width="640"></a></td>
    <td width="50%" align="center"><strong>Rendering and delivery</strong><br><a href="screenshots/readme-2026-10-17/render.png"><img src="screenshots/readme-2026-10-17/render.png" alt="UTvTU offline mixdown and stem export" width="640"></a></td>
  </tr>
</table>

<details>
<summary>More views: mixer, preferences and package manager</summary>

**Mixer**

![UTvTU mixer with per-track FX buttons](screenshots/readme-2026-10-17/mixer.png)

**Preferences**

![UTvTU appearance and dynamic-colour settings](screenshots/readme-2026-10-17/preferences.png)

**Package manager**

![UTvTU dependency-package resource centre](screenshots/readme-2026-10-17/packages.png)

</details>

Screenshots captured on Windows, build `UTvTU-26.10.17-011138`. Click an image in the main gallery to view its full size.

## Features

### Vocal Editing and Synthesis

- Edit notes, lyrics, pitch, portamento, vibrato and expression curves in the piano roll.
- Use OpenUTAU's voicebank, phonemizer and synthesis workflows, including classic UTAU voicebanks, third-party resamplers and engines such as DiffSinger.
- Arrange vocal parts and backing audio across tracks, with undo / redo.
- Save UTvTU projects, effect chains and plugin states as `.ustxp`; retain `.ustx` compatibility and upstream import formats such as UST, MIDI and VSQx.

### A Connected Workspace

Switch between arrangement, piano roll and mixer in the main window, or detach the piano roll and mixer. The welcome page provides new, open, recent-project and template entries. Resources, rendering and preferences share the main-window workspace. Smaller tools and confirmations use embedded overlays with keyboard focus, cancellation and reduced-motion support.

### Shared Effects Rack and Mixer

- Track faders, pan, mute, solo, live meters and master output controls.
- **EQ, compressor and reverb are independent built-in effects**. Add multiple instances, remove, bypass, reorder and use presets.
- The editor and mixer share a horizontal bottom rack. Open it from a track's FX entry, including **FX · count** at the bottom of each mixer strip.
- **VST3 audio effects appear as named cards**. Add, replace, bypass, remove or reorder them; double-click a card to open the plugin's native GUI.
- Plugin windows stay above their UTvTU owner and follow its minimize / restore state, without being globally always-on-top.
- Undo / redo effect-chain edits and save built-in parameters and VST state with the project.

### Offline Rendering and Delivery

Offline export is the default, with separate plugin instances for the export job. Choose a stereo mixdown or individual stems, the whole song, a loop or a custom range, and the required effect tail.

| Setting | Options |
|---|---|
| Format | WAV 16-bit, 24-bit, 32-bit float |
| File sample rate | 44.1 / 48 / 96 kHz |
| Processing | VST, built-in effects, track gain / pan and master settings |
| Task controls | Progress, cancellation and output-folder access |
| Plugin compatibility | Manually selected real-time compatibility mode |

Synthesis and effects use the internal project rate; delivery files can be resampled independently. Offline export aligns selected tracks for plugin latency. Third-party compatibility depends on each plugin's implementation.

### Material Design 3 and DAW Controls

- HCT / CAM16 role colours, custom seeds, the Windows accent, light / dark themes, palette variants and contrast settings.
- Shared buttons, inputs, selectors, knobs, faders, menus and overlays, with compact controls where music editing needs them.
- Consistent primary, secondary, cancel and close controls in embedded dialogs.
- Voicebank search, real avatars, OTO / subbank information, audition controls and dependency-package management in the resource centre.

### Installation and Use

Get installers from [Releases](https://github.com/XKLMY-hi/UTvTU/releases); the runtime is included. The current installer checks package identity and file manifests, rolls back failed or cancelled updates, and preserves user data by default on uninstall. See [installation and packaging](installer/README.md) or [building from source](docs/BUILDING.md).

This page describes `plus-develop`; consult release notes for the features in a downloaded build. Fresh installations use `OpenUtau Plus` under Windows Documents for data. Existing portable installations keep their local data mode.

Windows vocal production is the current focus. VST instruments, aux / send buses, sidechains, track groups and complete playback delay compensation remain future work; offline track alignment is available.

## Acknowledgments

UTvTU builds on upstream code, open standards, design resources and community knowledge. The following credits cover direct dependencies and major technical sources. Versions, licenses and additional notices are recorded in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Projects and Contributors

| Project / contributors | Contribution |
|---|---|
| [OpenUTAU](https://github.com/openutau/OpenUtau), [StAkira](https://github.com/stakira) and [all upstream contributors](https://github.com/openutau/OpenUtau/graphs/contributors) | Vocal editing, project models, synthesis, phonemizers and long-term maintenance |
| [UTAU](https://utau2008.xrea.jp/), Ameya / Ayame and the UTAU community | Voicebanks, resamplers, creative tools and shared experience |
| [UTvTU contributors](https://github.com/XKLMY-hi/UTvTU/graphs/contributors) and earlier OpenUTAU Plus contributors | Branch features, UI, audio workflows, fixes and documentation |
| [DiffSinger](https://github.com/MoonInTheRiver/DiffSinger), [OpenVPI](https://github.com/openvpi) and related maintainers | Neural singing synthesis and its model ecosystem |
| [CREPE](https://github.com/marl/crepe), Jong Wook Kim and contributors | Pitch-analysis implementations and resources |
| Phonemizer, dictionary, language-resource and translation contributors | Pronunciation, compatibility and localization |
| Users sharing reports, test projects and reproduction steps | Real production feedback and compatibility testing |

### Application, UI and Audio Components

| Project | Use |
|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia), including Desktop, Fluent, DataGrid, ColorPicker and HarfBuzz integration | Desktop UI and controls |
| [ReactiveUI](https://github.com/reactiveui/ReactiveUI), [ReactiveUI.Avalonia](https://www.nuget.org/packages/ReactiveUI.Avalonia/14.7.1), [Fody](https://github.com/Fody/Fody) / ReactiveUI.Fody, [Splat](https://github.com/reactiveui/splat), [DynamicData](https://github.com/reactivemarbles/DynamicData), [Reactive Extensions](https://github.com/dotnet/reactive) | Reactive MVVM, collections and property notifications |
| [SkiaSharp](https://github.com/mono/SkiaSharp), [HarfBuzz](https://github.com/harfbuzz/harfbuzz) | Rendering and text shaping |
| [.NET](https://github.com/dotnet/runtime) / System.* | Runtime, base libraries, JSON, encodings and packaging |
| [NAudio](https://github.com/naudio/NAudio), [NAudio.Vorbis](https://github.com/naudio/Vorbis), [NAudio.Flac](https://github.com/BunLabs/NAudio.Flac), [NLayer](https://github.com/naudio/NLayer), [Concentus.OggFile](https://github.com/lostromb/concentus.oggfile) | Audio devices, processing and file formats |
| [NWaves](https://github.com/ar1st0crat/NWaves) | Signal processing and analysis |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) / DirectML | Model inference |
| [DryWetMidi](https://github.com/melanchall/drywetmidi) | MIDI files |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet), [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | Projects and configuration |
| [Serilog](https://github.com/serilog/serilog), with Console / Debug / File sinks | Structured logging |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress), [Ignore](https://github.com/goelhardik/ignore) | Archives and ignore rules |
| [csharp-kana](https://github.com/wolfgitpr/csharp-kana), [csharp-pinyin](https://github.com/wolfgitpr/csharp-pinyin), [WanaKana-net](https://github.com/MartinZikmund/WanaKana-net), [UTF.Unknown](https://github.com/CharsetDetector/UTF-unknown) | Language conversion and encoding detection |
| [K4os.Hash.xxHash](https://github.com/MiloszKrajewski/K4os.Hash.xxHash), [NumSharp](https://github.com/SciSharp/NumSharp), [NeoLua](https://github.com/neolithos/neolua), [NetMQ](https://github.com/zeromq/netmq), [TextCopy](https://github.com/CopyText/TextCopy) | Hashing, numerics, scripting, messaging and clipboard |

### Native Audio and Design Sources

| Project / resource | Use |
|---|---|
| [Steinberg VST3 SDK](https://github.com/steinbergmedia/vst3sdk) | VST3 effect hosting and native-editor bridge |
| [Worldline](cpp/worldline), [WORLD](https://github.com/mmorise/World), [libgvps](https://github.com/Sleepwalking/libgvps), [libpyin](https://github.com/Sleepwalking/libpyin) | Native synthesis and audio analysis |
| [miniaudio](https://github.com/mackron/miniaudio), [libnpy](https://github.com/llohse/libnpy), [spline](https://github.com/ttk592/spline), [Abseil](https://github.com/abseil/abseil-cpp), [xxHash](https://github.com/Cyan4973/xxHash) | Native audio, data and algorithms |
| [Google Material Design](https://m3.material.io/), [Material Color Utilities](https://github.com/material-foundation/material-color-utilities) | MD3 design and HCT / CAM16 colour algorithms |
| [AndroidX](https://github.com/androidx/androidx) MaterialShapes, graphics-shapes and state tokens | Shape geometry, morph parameters and interaction references |
| [Phosphor Icons](https://github.com/phosphor-icons/core) | Interface icons |
| Huawei [HarmonyOS Sans](https://developer.huawei.com/consumer/cn/design/resource/) | Application font, under the [HarmonyOS Sans Fonts License Agreement](OpenUtau/Assets/Fonts/LICENSE.txt) |
| Voicebank, plugin and character-art creators | Their voices, tools and artwork, under their respective ownership and usage terms |

Thanks also to [xUnit](https://github.com/xunit/xunit), [Avalonia.Headless](https://github.com/AvaloniaUI/Avalonia), [Microsoft Test Platform](https://github.com/microsoft/vstest), [coverlet](https://github.com/coverlet-coverage/coverlet), [GoogleTest](https://github.com/google/googletest), [Bazel](https://github.com/bazelbuild/bazel), [CMake](https://github.com/Kitware/CMake) and [GitHub Actions](https://github.com/features/actions) for development and validation tools, and to projects used in earlier development stages, including [SukiUI](https://github.com/kikipoulet/SukiUI) and [Dotnet.Bundle](https://github.com/egramtel/dotnet-bundle).

The main project uses the [MIT License](LICENSE.txt) and retains upstream copyright notices. Third-party code, fonts, voicebanks, models and plugins retain their own licenses.

## Vibe Coding Statement

UTvTU uses **vibe coding / AI-assisted development**. The maintainer supplies product goals, interaction requirements and practical feedback; AI tools assist with design discussions, implementation, refactoring, debugging and documentation.

This describes UTvTU's development process. Existing OpenUTAU code, third-party components and community resources retain their authorship and credits; UTvTU extends and refactors those foundations. The maintainer is responsible for project direction, change selection and release decisions. Feature descriptions follow implementation and validation results.

Reproductions, code reviews and improvements are welcome through [Issues](https://github.com/XKLMY-hi/UTvTU/issues) and pull requests.

**By XKLMY · Built with vibe coding**

## Thanks to OpenUTAU and the UTAU Community

Thank you to StAkira and every OpenUTAU contributor. UTvTU's editing, phonemizers, project formats and synthesis are built on your accumulated work. This project will continue to preserve upstream credits and respect those contributions.

Thank you to the UTAU community's voicebank authors, recording contributors, OTO creators, resampler developers, phonemizer and dictionary maintainers, translators, tutorial writers, vocal editors and musicians. Your shared voices, tools, knowledge and music make singing synthesis a creative practice that people can take part in together.

We hope UTvTU can make editing, mixing and delivery more connected, and help more voices be heard.
