# Windows 安装与发布

安装与卸载使用主程序的共享 MD3 控件库、固定蓝色种子和系统深浅主题。安装器不引用音频引擎或主应用项目；`UTvTU.Setup.Core`负责清单、校验和文件事务，`UTvTU.Installer`负责向导、Windows授权与当前用户的系统入口，`UTvTU.Setup.Packager`负责产物验收和单文件封装。

## 构建安装包

在Windows安装.NET SDK 9及Visual Studio C++工具（提供可再分发的x64 VC CRT），运行：

```powershell
pwsh -NoProfile -File scripts/build-installer.ps1
```

脚本自动选择已安装的SDK9，在独立暂存目录串行构建，不关闭应用。主程序、独立卸载器与安装器共用一次版本时间戳；输出`dist/UTvTU-<时间戳>.exe`、SHA256和来源/文件清单JSON。单文件宿主发布后流式附加载荷，附带范围及SHA256校验，启动向导不会把大ZIP加载为程序集资源。分发只需最终exe。

载荷收集前验证自包含运行时、主程序与两个UI库、四个VST探针文件、VST桥接口、worldline、VC CRT、独立卸载器及版本。目录联接、用户数据、安装标记、未登记ZIP条目与错误哈希均拒绝。原`OpenUtau.nsi`只作历史参考，已禁止继续构建；Windows发布入口为GitHub Actions的**Windows installer**手动工作流，PR仅跑安装器轻量检查。旧AppVeyor Windows入口不再构建NSIS包；其他平台配置未改造。

复用已经验收的发布目录时，必须明确指定目录，且对应的应用/安装器源码不能在其来源提交后发生变化；版本与来源记录保持原值：

```powershell
pwsh -NoProfile -File scripts/build-installer.ps1 -SkipPublish -PublishDirectory '<已验证的发布目录>' -OutDir '<新输出目录>'
```

## 安装和数据

- 默认当前用户安装至`%LOCALAPPDATA%/Programs/UTvTU`。只有经exe身份或本产品收据确认的UTvTU/旧Plus才作为升级目标；原版OpenUTAU不自动覆盖。兼容旧NSIS的32/64位登记、缺失InstallLocation、`-plus`版本标识。
- 新安装创建`installed.txt`，数据继续由主程序保存在Windows“文档”目录下的`OpenUtau Plus`。既有便携安装升级保留原地数据模式，不擅自切换目录。
- 文件在同盘暂存区校验完成后才替换。文件占用先报错；失败或取消恢复原文件。只淘汰旧清单中未修改的程序文件，不删除未知文件。
- 默认卸载保留工程、声库、备份、缓存和设置。显式选择清理数据时，只处理记录对应位置里的已知数据类型和目录，不跟随外部搜索路径或目录联接；未知文件仍保留。安装模式的数据目录为当前账户共享，勾选清理会影响使用同一数据目录的其他UTvTU安装，向导会明确显示这一点。
- 无安装清单的历史版本不能按整个目录卸载。先覆盖安装新版建立准确清单，再使用新版卸载入口。便携数据不会因此迁移或被覆盖。
- 安装/卸载任务不强杀程序。受保护位置使用授权子进程，只处理程序文件及同目录、身份已核实的旧机器级登记；HKCU、快捷方式和后续启动留在原用户进程。
- 文件操作与系统入口的结果分别反馈。系统入口失败会显示未完成项，允许重试，不声称已全部成功。修改过的程序文件也保留并列出。

## 卸载入口与命令行

交互卸载可使用Windows“已安装的应用”，或安装目录的`UTvTU-Uninstall.exe`。安装目录入口先把自身复制到专用临时目录后退出，避免锁住将要删除的exe。Windows登记的卸载入口使用当前用户安装管理缓存中的独立卸载器，静默调用可等待实际结果；该管理器不包含整份安装载荷，保留作失败重试使用。

独立安装包支持：

```powershell
& '<完整安装包.exe>' --silent --dir '<专用安装目录>' --no-desktop --no-association
& '<完整安装包.exe>' --silent --uninstall --dir '<已登记安装目录>'
```

参数：`--uninstall`、`--silent`（兼容`--quiet`）、`--dir`、`--no-desktop`、`--no-association`、卸载时显式的`--remove-data`。提权保留全部选项；用户取消授权不会自动重试。

静默退出码：0完整成功，1失败，2文件操作完成但有保留/未完成项目，3取消。安装目录里的exe入口不支持同步静默卸载（自身会锁住待删文件）；自动化请使用Windows登记的`QuietUninstallString`或上面的独立安装包命令。日志放在`%LOCALAPPDATA%/UTvTU/SetupLogs`，不写入受保护的安装目录。

注册`.ustx`和`.ustxp`的“打开方式”，保留既有处理程序及Windows UserChoice，不接管其他应用的默认关联。旧卸载器只在目录和用户身份仍匹配时清理登记，不会移除后来安装的版本。

## 轻量验证

```powershell
dotnet build installer/UTvTU.Setup.Tests -m:1 -p:UseSharedCompilation=false -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
pwsh -NoProfile -File scripts/Test-Setup.ps1
pwsh -NoProfile -File scripts/Test-SetupPackage.ps1 -Installer '<最终完整安装包.exe>'
```

两套脚本默认每组30秒、384MB。单元/无头控件检查串行；实包检查使用临时合成目录和真实发布exe的内部文件任务入口，安装后再卸载并核对合成工程/声库/备份/设置保留，不注册HKCU或创建用户快捷方式，不使用真实工程。超限只终止自己启动的测试进程，先尝试取消恢复。

`--preview`仅查看安装器/卸载器界面，禁用执行操作；不应用于实际安装验收。默认不自行截图，界面观感交用户实机查看。
