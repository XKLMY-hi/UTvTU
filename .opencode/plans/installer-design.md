# UTvTU 安装程序 —— 设计规格（2026-10-07，Lead）

> 用户原话：「是时候发一个安装版了！用 MD3 设计一个全新的安装程序，主题色蓝色，不需要动态色彩，
> 所以不用弄颜色池什么的，只需要暗亮两色的编码颜色写死就行，颜色梯度直接从主程序拿，
> 然后安装程序尽量简单，且能够正常覆盖之前的 OpenUTAU Plus，因为数据路径基本不变，兼容应该很容易」

## 1. 技术选型（已定）

**自研 Avalonia 安装器** `installer/UTvTU.Installer/`（`net8.0-windows`、`win-x64`、self-contained、single-file）。
理由：只有自研才能做出 MD3 观感；不引入第三方安装框架（Inno/NSIS 的 Win32 皮肤做不出 MD3）。
**不复用主程序的 `OpenUtau` 工程**（避免把整个 app 拖进来）；安装器自带最小 MD3 控件与调色板。

## 2. 颜色（用户明确：不做动态色彩/色池）

- **seed = Material Blue `0xFF0B57D0`**（可换，换 seed 只是重跑生成工具）。
- **用主程序同一个算法生成一次，然后把值写死**：`OpenUtau.Core.Theming.Md3SchemeColors.Create(seed, Md3SchemeVariant.TonalSpot, isDark)`；
  生成工具放 `OpenUtau.Test/Tools/Md3PaletteDump.cs`（照 `WelcomeWaveformGenerator` 的 env-gated 套路，
  `OPENUTAU_GEN_MD3_PALETTE=1` 触发，输出 C# 常量代码），产物落到安装器工程里的
  `InstallerPalette.cs`（`Light` / `Dark` 两个静态 `IReadOnlyDictionary<Md3Role, Color>`）。
- 安装器只需**最小角色集**（其余不生成）：
  `Surface / SurfaceContainerLow / SurfaceContainer / SurfaceContainerHigh / SurfaceContainerHighest /
  OnSurface / OnSurfaceVariant / Outline / OutlineVariant / Primary / OnPrimary / PrimaryContainer /
  OnPrimaryContainer / SecondaryContainer / OnSecondaryContainer / Error / OnError / ErrorContainer / OnErrorContainer`
- 跟随系统深浅：`Application.Current.RequestedThemeVariant`（不做用户可选的色相/对比度）。

## 3. 页面（4 步，尽量简单）

| 步 | 内容 |
|---|---|
| ① 欢迎 | 品牌锁定（`brand-mark` + `brand-wordmark` + v 描边，几何与主窗一致）+ 版本号 + 「安装 / 取消」 |
| ② 位置 | 安装目录（默认值见 §4.1）+ 检测到旧版时的提示「将覆盖 `<路径>` 上的既有安装；**用户数据不受影响**」 |
| ③ 安装中 | 进度条 + 阶段文本（解包 / 写快捷方式 / 注册）；可展开日志（默认收起） |
| ④ 完成 | 勾选「创建桌面快捷方式」（默认勾）、「立即启动 UTvTU」（默认勾）+ 「完成」 |

## 4. 安装行为

### 4.1 默认安装目录（避免装成两份）
按序探测，命中即用：
1. `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\UTvTU` → `InstallLocation`；
2. `HKLM|HKCU\...\Uninstall\OpenUtau` → `InstallLocation`（**旧版 OpenUTAU Plus 就是注册成 `OpenUtau` 的**，见 §5）；
3. 兜底 `%LOCALAPPDATA%\Programs\UTvTU`（**仅当前用户，不需要管理员**）。

### 4.2 权限
- 安装器以 **asInvoker** 启动；
- 目标目录在 `%ProgramFiles%`（旧版默认在 `$PROGRAMFILES64\OpenUtau`）⇒ 安装阶段**以 `runas` 重新拉起自身**请求提权（一次 UAC）；否则全程免 UAC。

### 4.3 文件
- 解包 `payload.zip`（发布产物）到安装目录；**同名覆盖**；
- **绝不删除用户数据**：数据目录（`OpenUtau Plus`，`PathManager` 决定，可能在 `%APPDATA%`/exe 旁）与
  安装目录内的 `Backups/`、`UCache/`、`Cache/`、`*.ustx` 等**一律保留**；
- 旧版遗留的**程序文件**（上一版 exe/dll）在同一目录时被覆盖即可；不做"清理未知文件"以避免误删用户东西。

### 4.4 快捷方式
- 创建：开始菜单 `UTvTU.lnk` +（可选）桌面 `UTvTU.lnk` → `<安装目录>\OpenUtau.exe`；
- **清理旧版同名**：`OpenUtau.lnk`、`OpenUTAU Plus.lnk`（仅当它们指向本安装目录，避免误删上游 OpenUTAU 的快捷方式）。

### 4.5 注册表
- 写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\UTvTU`：
  `DisplayName=UTvTU`、`DisplayVersion`、`Publisher=XKLMY-hi`、`DisplayIcon`、`InstallLocation`、
  `UninstallString="<安装目录>\UTvTU-Uninstall.exe"`、`URLInfoAbout=https://github.com/XKLMY-hi/UTvTU`、`NoModify=1`、`NoRepair=1`；
- **清理旧键**`...\Uninstall\OpenUtau`：仅当其 `InstallLocation` == 我们的安装目录时才删（**否则会误删用户真正的上游 OpenUTAU** ⚠）；
- 文件关联 `.ustx`（HKCU\Software\Classes）默认勾选，可关。

### 4.6 卸载
- 安装器 exe 自身复制为 `<安装目录>\UTvTU-Uninstall.exe`，`--uninstall` 走卸载流程；
- 删程序文件 + 快捷方式 + 注册表键；**弹一个勾选「同时删除用户数据（工程/设置/缓存）」默认不勾**；
- 卸载器同样 MD3 外观（同一套页面/调色板，简化为一页确认 + 进度）。

## 5. 兼容旧版 OpenUTAU Plus（关键事实）

- 旧仓库 `XKLMY-hi/OpenUTAU-Plus` 的 `OpenUtau.nsi` 定义：`PRODUCT_NAME "OpenUtau"`、
  `InstallDir "$PROGRAMFILES64\OpenUtau"`、注册表键 `...\Uninstall\OpenUtau`、快捷方式 `OpenUtau.lnk`。
  ⇒ **旧版是"顶着 OpenUtau 名字"装的**，所以升级检测与清理必须按 `InstallLocation` 精确比对（§4.5）。
- **数据路径不变**（同名同位置）⇒ 只要不动数据目录，旧版设置/工程/声库立即可用 ✓（用户判断正确）。

## 6. 打包链路

`scripts/build-installer.ps1`：
1. `dotnet publish OpenUtau -c Release -r win-x64 --self-contained true -o bin\win-x64`
   （沙箱里可能因 VstProbe RID / 遥测写入失败 ⇒ **用户实机跑最稳**，脚本里写明）；
2. 压缩 `bin\win-x64` → `payload.zip`；
3. `dotnet publish installer\UTvTU.Installer -c Release -r win-x64 --self-contained true`
   ⇒ 产出 `UTvTU-Setup-x.y.z.exe`（与 `payload.zip` 同目录，**先做同目录方案**，简单可迭代；
   将来要单文件再内嵌 payload）。

## 7. 验收（谁交谁自证 + 独立复核）

- 构建 0 错误 + `ui-lint` 0/0（若安装器工程纳入 lint 范围）+ 安装器自身**无新增测试红**；
- **真机截图**：4 页 × 亮/暗 各一组（用 `.dsh/fx/capture.ps1` + `click.ps1`，本会话已验证可用）；
- **干净安装**：装到空目录 ⇒ 启动成功、快捷方式/注册表/卸载项齐全；
- **覆盖安装在旧版之上**：装到旧目录 ⇒ 快捷方式不重复、注册表只剩 `UTvTU` 一条（旧 `OpenUtau` 键被清）、
  **用户数据原样可用**（打开旧工程/声库列表仍在）；
- **卸载**：文件/快捷方式/注册表清干净，数据目录保留（默认不勾删除）；
- **不改主程序代码**（除必要时新增 `installer/` 与 `scripts/`）；主程序闸门（构建 0 + 全量 809 + lint 0/0）不受影响。

## 8. 分工

| 角色 | 人 | 内容 |
|---|---|---|
| 实现（单写者） | m2-view | §2 生成工具 + 安装器工程 + 安装/卸载/覆盖逻辑 + 自带截图 |
| 发布/打包 | Lead | publish 链路、`payload.zip`、最终 Setup 产物 |
| 独立验证 | fx-verify | 覆盖安装与卸载的真机复核（只读 + 受控写）、断言 ↔ 实现对照 |
