---
name: verify
description: UTvTU 运行验证——构建/启动/驱动 Avalonia 桌面应用验证运行时行为。每步改动后运行：dotnet build、启动应用、检查 Logs 中 [ERR]/[FTL]，按清单验证保存加载、导出渲染、混音台、效果复位、自动保存。验证本仓库代码改动后是否正常工作时使用。
---

# verify — UTvTU

Build/launch/drive for verifying runtime behavior of the Avalonia desktop app.

## Build & Launch

```powershell
dotnet build
dotnet run --project OpenUtau
# App starts as a Windows desktop window. Logs at:
# OpenUtau/bin/Debug/net8.0-windows/Logs/log{date}.txt
```

## Drive

- **Startup**: Launch and check `Logs/log{date}.txt` — look for [ERR]/[FTL]/Exception
- **Save/load**: Open app → Save project → check .ustxp has `ustxpVersion: 1.0` + VST state
- **Export**: Export window → Render → verify progress bar advances and cancel works
- **Mixer**: Ctrl+M → drag faders, mute/solo, open FX rack → verify no duplicate events
- **Effects reset**: Play → seek → verify no reverb tails / artifacts
- **Autosave**: Wait 30s idle → check log for "Autosave" line (should use Ustxp.AutoSave)

## Gotchas

- Windows only for VST features (native vst_bridge.dll in runtimes/win-x64/native)
- Non-ASCII paths may cause resampler issues (AGENTS.md warning)
- Must have .NET 8.0 Runtime
- GUI requires interactive Windows session (no headless/CI UI testing setup yet)
