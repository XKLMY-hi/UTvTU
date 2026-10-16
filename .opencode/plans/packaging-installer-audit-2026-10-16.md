# Windows 打包器与安装器审查（2026-10-16）

审查基线：`1495dc6403a0bdee13412af1eb087aa21334f8ae`，`plus-develop`。本轮检查当前自研安装链 `scripts/build-installer.ps1` → `installer/UTvTU.Installer`，同时对照主程序数据目录、VstProbe及旧发布入口。没有修改产品代码，没有运行打包脚本、完整安装/卸载、提权或强制关闭主程序。

## 已确认的问题（按处理优先级）

### 1. P0：默认卸载会删除用户工程、声库、备份与设置

`InstallerCore.cs:346–369`先对安装目录执行`GetFiles(..., AllDirectories)`并逐文件删除；`removeData`到397行才参与判断，`KeepOnUninstall`也没有参与前面的删除。任何放在安装目录里的`Backups`、`Singers`、`prefs.json`、用户工程，都会在“不删除用户数据”模式下被删。后面的保留分支不能恢复文件。

**实际复现**：直接链接未修改的InstallerCore，创建六份合成文件，调用`Uninstall(dir, false, progress)`，在40%进度、进入快捷方式/注册表阶段之前抛异常停止。六份文件全部消失。测试严格限制在`.dsh/installer-audit-20261016/sandbox/uninstall`。

此外，卸载接受任意`--dir`，没有安装身份/文件清单校验；旧卸载器清注册表时也不核对自己的目录是否仍为当前注册位置。须改为只删除已安装清单内的程序文件、验证安装身份、保护未知文件和数据目录；不应按整个目录递归删除。

### 2. P1：运行打包脚本会直接强杀所有同名应用

`build-installer.ps1:29–34`按进程名枚举`OpenUtau`和`UTvTU-Setup`，一律`Stop-Process -Force`，没有核对exe路径、保存状态或用户是否仍在工作。不同工作树的UTvTU及真正的上游OpenUTAU都会匹配，`-SkipPublish`也会执行这一段。

这是打包前就可能造成未保存工程丢失的问题；本轮仅审查源码，没有执行它。应使用隔离发布目录，遇到目标文件占用时退出并说明，不强杀应用。

### 3. P1：新安装器和主程序对“已安装”及用户数据位置的约定不一致

主程序`PathManager.cs:44–54`仅用exe旁的`installed.txt`决定模式：有标记→`Documents/OpenUtau Plus`；无标记→直接在exe目录存数据。新安装器和打包脚本均未创建该标记（旧NSIS在`OpenUtau.nsi:85`附近创建）。干净安装因此会进入便携模式；安装在受保护目录时还会影响设置/日志/声库写入。

同时`InstallerCore.UserDataDir():337–344`却查`AppData/Roaming/OpenUtau Plus`，不存在就返回`SelfDir/OpenUtau Plus`，既不是主程序安装模式的数据目录，也不是实际便携模式根目录。勾选删除数据会找错目录，不能以文档里的“数据路径不变”作为验证。

**实际复现**：对干净的合成payload执行真实Install的文件阶段，在84%进入快捷方式/注册表前停止；目标未出现标记。后续源码也没有写标记的动作。应统一数据路径契约，并保留旧便携数据的明确迁移规则。

### 4. P1：旧版本检测会把真正的上游OpenUTAU当成可覆盖的旧Plus

`InstallerCore.cs:110–136`把`.../Uninstall/OpenUtau`的任何注册项当作旧Plus，并直接采用其路径为默认安装位置；没有检查exe产品身份。后续`WriteUninstallEntry:256–266`仅因目标路径相同就删除旧键，因此前面的自动选址会使“同路径”保护失效：原版OpenUTAU也可能被默认覆盖、清掉注册项。

应先确认是UTvTU/旧Plus，无法确认的上游安装只提示共存，不能自动作为覆盖目标。

### 5. P1：ZIP路径边界校验可以被相邻目录绕过

`InstallerCore.cs:225–234`使用`target.StartsWith(Path.GetFullPath(dir))`，没有补路径分隔符。例如目标`.../target`下的`../target-neighbor/leak.txt`归一化后仍通过检查，可以写到安装目录之外。快捷方式清理及自删范围判断也有同类前缀检查。

**实际复现**：合成ZIP中的该条目成功写入隔离区的`target-neighbor`。当前载荷通常内嵌，外部payload也存在回退入口；无论来源都必须校验完整目录边界，遇到非法条目整体拒绝，而非静默跳过。

### 6. P1：覆盖安装失败会留下新旧版本混合；取消/关闭也没有事务保障

`InstallerCore.cs:188–234`逐文件直接覆盖正式目录，没有预检、暂存、备份或回滚。某个DLL被占用或解压失败时，之前替换的文件保留新版本，其余仍是旧版。

**实际复现**：目标有“旧exe”和锁定的“旧dll”，payload按序提供“新exe”和“新dll”；第二条抛IOException后，exe已变新、dll仍旧。

界面`MainWindow.axaml.cs:32,150–173`的取消直接Close，后台使用`CancellationToken.None`。窗口关闭没有正在操作时的保护。异常后step仍为2，重新启用的“安装中”按钮落入`OnNext`的default分支，甚至可能启动半更新程序。须明确安装状态、暂存/提交/回滚和关闭行为。

### 7. P1：VST探针的RID构建路径与收集路径不匹配

`OpenUtau.csproj:84–90`对子项目Build，却固定从`VstProbe/bin/Release/net8.0/vst_probe.*`收集；主程序publish传入`RuntimeIdentifier=win-x64`时，子项目继承RID。

**MSBuild只求值核实**：实际TargetPath为`VstProbe/bin/Release/net8.0/win-x64/vst_probe.dll`，与glob不一致。干净环境可能漏收集，存在非RID旧产物时可能收集旧探针。当前又只在Build后Copy，没有完整的发布清单和关键文件验收。

应使用子项目返回的准确产物并加入publish项，验收`vst_probe.exe/.dll/.deps.json/.runtimeconfig.json`及所需运行时，同时核对VST桥、worldline、两个新UI库。这里没有执行完整publish，因此不把路径核实写成“已验证最终安装包内容”。

### 8. P1：卸载权限不足或文件被占用时仍会报告成功

界面`RunUninstall:176–188`没有安装流程里的提权处理。`InstallerCore.cs:356–368`删除失败后忽略`MoveFileEx`返回值，后续仍清注册信息并报告100%。针对Program Files或运行中程序，可以出现程序仍在、卸载入口已消失、界面却说完成的状态。`Install:207–211`也吞掉卸载器复制的IOException，可能安装成功却没有可用卸载器。

须先检查权限及占用，返回明确结果；未删除文件/待重启都不能按完整成功处理。

### 9. P2：界面、命令行和提权参数没有统一

- 界面声明关联`.ustx / .ustxp`（MainWindow.axaml:58），实现只写`.ustx`（InstallerCore:270–279）。
- QuietUninstallString写`--quiet`（251行），ParseArgs只接受`--silent`（71行）。**实际复现**：解析`--quiet`后Silent=false。
- 提权重开只传`--elevated/--dir/--uninstall`，没有保留静默、桌面快捷方式和关联选择；新进程重新打开向导，原选择丢失。静默入口本身也没有权限处理。
- 卸载进度阶段`GoTo(2)`仍保留卸载页和可点击的卸载按钮（MainWindow.axaml.cs:88–96,176–185），没有防重入。

应统一选项模型和状态机，命令行与GUI共用同一套执行服务。

### 10. P2：发布缺少可验证的版本与产物身份

- 主程序在求值时生成自己的时间戳，脚本压完payload才生成安装器时间戳并写注册表，二者通常不是同一版本。SkipPublish可以把旧二进制打成新时间戳安装包。
- 脚本复用固定`bin/win-x64`并压缩所有文件，没有全新暂存目录、来源commit、文件清单、哈希或关键依赖检查；残留文件可能进入载荷。
- VstProbe restore退出码未独立检查；没有主动选择约定的SDK9、单节点及资源限制。安装器publish失败时内嵌payload没有finally清理，直接构建安装器又允许没有payload的正式形态。
- 当前自研安装器没有进入PR测试/发布链；保留的AppVeyor仍走旧NSIS/OpenUtau身份，PR workflow只声明6/8SDK，和本仓SDK9约定不一致。非Windows配置本轮仅记录，不作修改。

建议整个发布只生成一次版本标识，携带来源commit和文件哈希，产物通过检查后再命名/输出；SkipPublish也须验证已有产物身份。

## 证据与边界

探针只链接原版InstallerCore.cs，目标均为已核实工作区内的新隔离目录，删除/覆盖仅涉及合成文件。实际Uninstall在40%之前停止、Install在84%之前停止，未调用快捷方式、注册表或自删；没有访问真实工程，没有完整安装/卸载，没有UAC，没有实际发布。

- SDK9.0.317独立小工程构建：0错0警告，5.76秒；没有重编译主程序或跑全套。
- 探针：exit0，0.397秒，峰值25MB；30秒/384MB护栏未触发。它证明缺陷存在，不是宣称安装验收通过。
- [复现结果](packaging-installer-audit-2026-10-16/findings.json)、[资源统计](packaging-installer-audit-2026-10-16/summary.json)、[RID求值](packaging-installer-audit-2026-10-16/probe-output-properties.json)。
- 同目录`probe.cs.txt`及`probe.csproj.txt`可分别复制到`.dsh/installer-audit-20261016/probe/Program.cs`、`Audit.csproj`复现。探针要求sandbox尚不存在，拒绝复用已有目录；本轮没有清理它。旧结果位于`.dsh/installer-audit-20261016/`。

建议修复顺序：**数据保留与目标身份 → 路径边界 → 安装事务与权限 → 发布依赖/版本清单 → 向导交互和视觉**。目前应先修这些安装机制，再调整安装器外观。
