# .ci/build-tools — 云端 Linux 编译 + Avalonia 截图工具链

这个目录只服务于 `.github/workflows/ui-preview.yml`，**不参与产品构建**。

## 为什么在这里

在低配 Android 设备（proot Ubuntu）上直接 `dotnet build OpenUtau.csproj` 会在主工程的
Avalonia XAML 编译阶段被系统内存管理杀掉（实测 RSS 峰值约 5.2 GB，可用仅 2.4 GB），
所以编译与出图交给 GitHub Actions 的 ubuntu-latest 跑，产物以 artifact 形式取回。

## 组成

| 文件 | 作用 |
|---|---|
| `bootstrap-recovery.py` | 下载并校验 .NET SDK 9.0.316（linux-x64）与 Avalonia.Headless 12.1.0，编译 32 MiB 线程栈的 CoreCLR 宿主 |
| `dotnet-thread-host.c` | 用 32 MiB 线程栈承载 CoreCLR，绕开默认 8 MiB 栈在 MSBuild/Roslyn 深递归下爆栈 |
| `BuildDriver.cs` / `compile-driver.py` | 用 csc 直接编出 MSBuild 驱动（避开 `dotnet` muxer 的平台探测问题） |
| `csc-wrapper` | 让 MSBuild 走上面的宿主跑 Roslyn，而不是默认 csc |
| `run-build.py` | 统一注入 TMPDIR / GC 硬上限 / 单进程 / 关共享编译等环境后调用 BuildDriver |
| `prepare-preferences-polish.py` | 把主程序输出目录软链成捕获程序的运行目录，并用 csc 编出捕获 exe |
| `CapturePreferencesPolish.cs` | Avalonia.Headless + 真实 Skia 绘制，逐页截图并做 329 项几何/契约检查 |
| `run-preferences-preview.py` | 一键：准备 → 运行捕获 → 输出 PNG 与 `validation.json` |
| `repair-onnx-cache.py` | 修 NuGet HTTP 缓存里被截断的 ONNX 包 |

## 产物

`preferences-preview/` 下是 9 个设置页 × dark/light/narrow 三种布局的截图、
滚动到底部的补充截图、英文外观页截图，以及 `validation.json`（检查项清单）。

## 注意

- 渲染走 Skia 光栅，与 CPU 架构无关；x64 云端的图与 arm64 实机一致（历史对比 35/38 逐像素相同，差异仅来自语言配置）。
- 本工具链**只覆盖 UI 层**：音频设备、VST 扫描、文件系统写操作、设置保存都不会真正执行。
- Windows 目标的编译校验在 workflow 的 `windows-compile` job 里，与本目录无关。
