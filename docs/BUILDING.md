# 构建与开发

[返回项目介绍](../README.md) · [安装与打包](../installer/README.md)

## 环境

- 当前开发和实机验证目标为 Windows x64。
- 使用 **.NET SDK 9** 构建；应用运行时为 .NET 8。Avalonia 12 的分析器需要新版 Roslyn，不要将仓库固定到 SDK 8。
- 普通 C# / UI 开发可使用仓库现有的 Windows 原生运行库。只有修改 VST 桥或 Worldline C++ 时才需要相应原生工具链。

## 获取与运行

```powershell
git clone --branch plus-develop https://github.com/XKLMY-hi/UTvTU.git
cd UTvTU
```

先用 `dotnet --version` 确认当前选中 SDK 9。若安装了多个主版本，可在忽略目录建立选择器，不修改仓库根目录的 SDK 设置：

```powershell
$repo = (Get-Location).Path
$sdkLine = dotnet --list-sdks | Where-Object { $_ -match '^9\.' } | Select-Object -Last 1
if (-not $sdkLine) { throw '请先安装 .NET SDK 9。' }
$sdk9 = ($sdkLine -split ' ')[0]
New-Item -ItemType Directory -Path .dsh/sdk9 -Force | Out-Null
@{ sdk = @{ version = $sdk9; rollForward = 'disable' } } |
    ConvertTo-Json -Depth 3 | Set-Content .dsh/sdk9/global.json -Encoding utf8

$env:DOTNET_PROCESSOR_COUNT = '2'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
Push-Location .dsh/sdk9
try {
    dotnet restore "$repo/OpenUtau/OpenUtau.csproj" -m:1
    dotnet build "$repo/OpenUtau/OpenUtau.csproj" --no-restore -m:1 -p:UseSharedCompilation=false
    dotnet run --project "$repo/OpenUtau/OpenUtau.csproj" --no-build
} finally {
    Pop-Location
}
```

默认输出在 `OpenUtau/bin/Debug/net8.0-windows/`。声库、依赖和重采样器按所用引擎配置。

## 轻量验证

仍在 SDK 9 环境中构建 `OpenUtau.Test/OpenUtau.Test.csproj`，再从仓库根目录运行明确选定的小组：

```powershell
pwsh -NoProfile -File scripts/Test-Light.ps1 -Filter 'FullyQualifiedName~WorkspaceDialogPresentationTests'
```

脚本默认每组 30 秒 / 384 MB，集合串行。根据改动选择相关小组；仅文档修改检查 Markdown、链接和图片，无需构建应用或运行音频测试。

安装器检查与完整安装包的隔离文件流程见[安装器说明](../installer/README.md)。

## 原生组件与发布

VST 桥接需要 Visual Studio 的“使用 C++ 的桌面开发”工具及 VST3 SDK，执行 `runtimes/vst_bridge/build.bat`。Worldline 的构建方法见 [cpp/README.md](../cpp/README.md)。

正式 Windows 安装包使用：

```powershell
pwsh -NoProfile -File scripts/build-installer.ps1
```

该脚本自行选择 SDK 9，并在独立暂存区串行发布、校验和封装；它不会关闭正在运行的应用。生成的 exe、SHA256 和来源清单位于 `dist/`。

## 目录与协作

| 目录 | 内容 |
|---|---|
| `OpenUtau/` | 主程序、视图、ViewModel 和宿主控件 |
| `OpenUtau.Core/` | 工程、合成、信号链、导出与 VST 管理 |
| `OpenUtau.Plugin.Builtin/` | 内置音素化器 |
| `UTvTU.DawUI/` | 共享 Avalonia 控件、主题、动效和令牌 |
| `UTvTU.Md3/` | 独立颜色算法引擎 |
| `VstProbe/` | 进程外插件探测 |
| `installer/` | 安装核心、向导、打包器与检查 |
| `OpenUtau.Test/` | 核心与界面检查 |
| `runtimes/`、`cpp/`、`scripts/` | 原生库、源码与开发脚本 |

协作规则见 [AGENTS.md](../AGENTS.md)，具体接手状态见 [HANDOVER.md](../HANDOVER.md)。`master`保留上游同步用途，`plus-develop`为 UTvTU 开发分支。
