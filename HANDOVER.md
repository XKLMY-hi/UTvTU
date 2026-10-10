# HANDOVER — 新会话交接（2026-10-10 收口）

> **新会话第一件事：读这个文件。** 它包含路径核实方法、分支/工作树实况、已完成与未完成、验证命令与基线、项目铁律、已知陷阱。
> 读完即可开工，**不要重新探索仓库**（本仓库的队友反复因"从零摸索"烧完上下文）。

---

## 1. 工作区与远端（**先核实路径**）

⚠️ 工作区在**移动硬盘**上，盘符会变（历史出现 D:/E:/F:/G:）。**每次会话开始先核实**：

```powershell
pwd                                    # 期望：<盘符>:\xklmy文件夹\vibe coding\UTvTU
git remote -v                          # 期望见下面两个 remote
git log --oneline -1                   # 期望 HEAD 见 §2
```

| remote | URL | 用途 |
|---|---|---|
| `origin` | https://github.com/XKLMY-hi/UTvTU.git | **推送目标**（push） |
| `upstream` | https://github.com/openutau/OpenUtau.git | 上游，**只 fetch**（push 已禁用） |

网络不稳（push 常 `Connection was reset`）⇒ 重试 3–5 次；用户开加速器后一次成功。

## 2. 分支模型与当前状态

| 分支 | 角色 | 当前 |
|---|---|---|
| `master` | 与 `upstream/master` 同步的镜像 | `29e0e16d`（落后 plus-develop 615，正常） |
| **`plus-develop`** | **所有 UTvTU 开发都在这里** | **`d89cf1b8`，已推 origin，与远端一致** |
| `try/*`（20 条） | 历史特性/验证分支 | **仅 2 条有未并入提交**（见下） |

**未并入的提交（全部实况，`git log plus-develop..<branch>` 实测）**：
- `try/fx-ctl` → `e1cdf019` "feat(welcome): S1 重建骨架"（**已被主树 W50 重建取代**，无需并入）
- `try/mx-strip` → `e8c88475` "merge(plan): 并入 plus-develop 作 S1'a 基线"（纯合并提交，无独有内容）

⇒ **没有遗留的有价值未合并工作**；其余 `try/*` 全部已并入 `plus-develop`。

## 3. 工作树（`git worktree list` 实测）

| 目录 | 分支 | HEAD |
|---|---|---|
| `UTvTU`（**主树，在此工作**） | `plus-develop` | `d89cf1b8` |
| `UTvTU-cmd` | `try/cmd` | `75212caf` |
| `UTvTU-fx-ctl` | `try/fx-ctl` | `e1cdf019`（+1 未并入，见 §2） |
| `UTvTU-mx-int` | `try/mx-int` | `3de31e8a` |
| `UTvTU-mx-strip` | `try/mx-strip` | `e8c88475` |
| `UTvTU-up-core` | `try/up-core` | `5d622255` |
| `UTvTU-up-piano` | `try/up-piano` | `e985eb65` |
| `UTvTU-ustxp` | `try/ustxp` | `62f2b151` |
| `UTvTU-welcome` | `try/welcome` | `a382eaf7` |

**磁盘上还有 `UTvTU-mx-lib` 目录，但不在 worktree 列表里** ⇒ 残留目录（其分支 `try/mx-lib` 已并入），可安全删除或 `git worktree prune` 后处理。

> ⚠️ **教训（本次会话的真实误判）**：队友可能在**自己的工作树**里干活，主树看不到 ⇒ 判定"某队友零产出"前**必须先 `git worktree list` + 逐个 `git log`**，否则会误停正在干活的人。

## 4. 最近完成（本次会话，均已推）

| 提交 | 内容 |
|---|---|
| `d89cf1b8` | 本交接文档 + 状态外化 |
| `9349d35a` | 欢迎页独立复核三修（选中药丸文字色 / 波形带弹性 / `railLink` 字号）+ 防回潮断言 |
| `ff66e3cc` | 搜索语义抽成纯函数 `WelcomeArt.FilterRecent` + 8 断言 |
| `ee8652cf` | **欢迎页 W50 重建为 MD3**（304 药丸导航 / 页头 28+14 / 最近行 56 / assist chip / 主题按钮 / 空态容器） |
| `b607c57a` | AGENTS.md 并行协作铁律（禁破坏性撤销 / 绿灯即提交 / dirty 先问 / 一文件一写入者） |
| `e4787ed5` | 欢迎页搜索 VM 层 + 41 个双语键（检查点提交，抢救事故幸存成果） |
| `d84d6b0c` | 左导航词汇提取到应用级共享层 |
| `64933387` | 「不碰布局」契约精确化（只约束裸元素选择器） |
| `24b95d32`/`096399dc`/`239d8924`/`2cfea6d0`… | **自研 MD3 安装程序**（单文件 227.5 MB，载荷内嵌；覆盖旧版 OpenUTAU Plus） |
| `ca280779` | README 加「下载」段 |

**发布**：GitHub Release **`UTvTU-26.10.7-150135`**（tag 同名）挂单文件安装器 `UTvTU-26.10.7-150135.exe`（238,515,604 B，SHA256 `6b6ced14…ac6d`）。
发布命名约定：**`UTvTU-<yy.M.d>-<HHmmss>`**（构建时刻版本号，csproj 顶层 PropertyGroup 求值；见 §8）。

## 5. 未完成（任务板实况）

| 任务 | 内容 | 状态 | 负责人 | 备注 |
|---|---|---|---|---|
| `task-37` | W8 `.ustxp` 格式止血（**P0 丢数据级**：上游重存丢 Plus 字段） | in_progress | m2-view | 树 `UTvTU-ustxp`（`try/ustxp`，已并入部分） |
| `task-26` | W14 上游 Core/引擎选择性合并（232 提交分诊） | in_progress | fx-core | 树 `UTvTU-up-core` |
| `task-19` | W8 `.ustxp` 结构审计（只读 + 提案） | pending | 未派 | 产出 `.opencode/plans/ustxp-format-audit.md` |
| `task-21` | W9 修崩溃：分离按钮 `InvalidateArrange on wrong LayoutManager`（整机退出级） | pending | 未派 | 需真机复现 |
| `task-3`/`task-4` | effect-rack 历史集成/验证 | pending | — | 疑似过时，动前复核 |

## 6. 构建 / 测试 / 校验（**离线沙箱专用开关，缺一即假失败**）

```powershell
# 1) 还原（顺序不可反：sln 先、VstProbe 后）
dotnet restore OpenUtau.sln -m:1 -p:TreatWarningsAsErrors=false --ignore-failed-sources
dotnet restore VstProbe\VstProbe.csproj -m:1 -p:RuntimeIdentifiers= -p:TreatWarningsAsErrors=false --ignore-failed-sources
# 2) 构建（--no-restore 保 TreatWarningsAsErrors 原样；必须 -m:1 + UsedAvaloniaProducts=）
dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=
# 3) 测试（判据：全绿；需完整权限，普通沙箱会 Win32Exception(5)）
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build
# 4) UI 规范检查
pwsh -File .dsh\fx\ui-lint.ps1 -Root "<仓库绝对路径>"
```

**当前基线**：构建 **0 错误** ｜ 全量 **813/813** ｜ `ui-lint` **ERROR 0 / WARN 0**。
每次改动后的判据看「**个错误**」行（Avalonia XAML 错形如 `: Avalonia error AVLN1001:`）。
⚠️ 已知偶发：全量曾出现 **1 次单例失败、重跑即过**（未定名，历史上 Light 变体有 flake）。

## 7. 真机验证与截图工具（`.dsh/fx/`，全部可用）

| 工具 | 用途 |
|---|---|
| `launch.ps1 -Exe <exe>` | 按**精确路径**启动（单实例按 exe 路径判，多树可共存） |
| `capture.ps1 -ProcessId <pid> -Out <png>` | PrintWindow 截窗口，**不需要前台** |
| `click.ps1 -ProcessId <pid> -X -Y` | 窗口相对点击（`-Screen` 表示屏幕绝对） |
| `sendkeys.ps1 -ProcessId <pid> -Text "…"` | ⚠ **打不进非前台窗口**（实测）⇒ 优先用 setvalue |
| **`setvalue.ps1 -ProcessId <pid> -Value "…"`** | **UIA `ValuePattern` 直接给文本框设值**（可靠替代打字） |
| **`resize.ps1 -ProcessId <pid> -Width -Height [-X -Y]`** | Win32 `MoveWindow`，做**最小窗口**验证 |
| `close.ps1 -ProcessId <pid>` | **必须用它优雅关闭**（否则下次出现"异常恢复"横幅） |
| `uia.ps1 -Mode list\|invoke [-Filter -Rect]` | 读 UIA 树 / 调用元素（Avalonia 菜单项常无 InvokePattern） |
| `ui-lint.ps1` / `checks.ps1` | UI 规范 / 综合检查 |

**截图存放**：`.dsh/fx/shots/**`（gitignore）。本会话产出：`welcome/w50-*.png`（欢迎页 6 张 + 窄窗）、`installer/*.png`。
应用日志：各树 `OpenUtau\bin\Debug\net8.0-windows\Logs\log*.txt`。

## 8. 项目铁律与用户偏好（**先读，别踩**）

**平台范围**：**Windows 是唯一目标平台**（用户：「跨平台我觉得目前不是重点」）⇒ 不再投入 macOS/Linux；新功能只需在 Windows 验证。

**设计稿格式**：用户交稿一律「**图像 + HTML**」；**HTML 是几何与令牌的权威源**（`<svg viewBox><path d=…>` 逐字引用），禁止从 PNG 描摹、禁止"描边→填充"式转换。

**并行协作铁律（AGENTS.md，事故后新增）**：① 禁 `git checkout -- <file>` / `reset --hard` / `stash` / `clean` 用于撤销自己的改动（**本仓曾因此丢掉一整版页面**）；撤销只能精确反向编辑，或先 `git diff` 确认归属；② **每完成一个绿灯步骤立刻 commit**，绝不把成品留在未提交状态；③ 动手前 `git status --short`，文件 dirty 且含他人改动 ⇒ 先问 Lead；④ **同一文件同一时刻只有一个写入者**。

**版本号**：`UTvTU-<yy.M.d>-<HHmmss>`，写进 `InformationalVersion`。⚠ 两个坑：必须在**项目求值阶段**（顶层 `PropertyGroup` 的属性函数）赋值（放进 Target 会被 `GenerateAssemblyInfo` 提前捕获 ⇒ 不生效）；且必须 `<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`（否则 SDK 追加 `+<git sha>` 盖掉）。

**用户工作方式（原话）**：
- 「**切分就是，我说做什么你做什么**」⇒ 不要擅自扩大动作范围（本会话因"没先给方案就实现"被批评两次）
- 「**一般修改你让我看就行了，当我要你自迭代的时候再自己看**」⇒ 常规改完**交他实机看**；他明确说"自迭代"才自己反复截图核对
- 「**可以用 computer use 来验证结果**」⇒ 验证允许真机操作
- 「**大哥你省点token啊**」⇒ 输出克制，别贴长清单
- 让他**拍板**时给**明确推荐**（他讨厌开放式提问）

**其它**：仓库**公开**（`XKLMY-hi/UTvTU`），旧仓 `XKLMY-hi/OpenUTAU-Plus` 为历史存档；用户数据目录是 `OpenUtau Plus`（**绝不改名**，改名=丢用户数据）。

## 9. 已知陷阱清单（实测）

1. **应用级样式压过一切**：`Styles.axaml:110 TextBlock{FontSize 13}` 与 `Md3InputThemes` 的 `TextBlock{Foreground}` 是**显式赋值 ⇒ 压过继承**。凡"设在 Button/容器上、期望子元素跟随"的字号/前景**都无效**，必须 `<Style Selector="X TextBlock">` 显式声明。
   **仍被压掉的点位（用户 2026-10-10 裁决"其他都别动"，未修）**：`MainWindow.axaml` `Button.viewTab` 11→**13**、`Button.libTab` 10→**13**（顶栏，最显眼）；`RenderWindow.axaml` `RadioButton.renderOption` 12→13、`Button.secondaryBtn` 11→13；`MixFxDialog.axaml` `ToggleButton.md3switch` 12→13；低-中：`Styles.axaml` `MenuItem` 12、`Md3InputThemes` Menu/Tab。
   ⚠ `MainWindow.axaml` 是 `task-37`（m2-view）名义写范围 ⇒ 动前协调。
2. **UI 控件属性只能在 UI 线程读**：后台线程读 `CheckBox.IsChecked` 会 `InvalidOperationException`（安装器实测）⇒ 切线程前先取到局部变量。
3. **`Watermark` 已废弃**（Avalonia 12）⇒ 用 `PlaceholderText`。
4. **最近列表禁用 `ListBox`**：全局隐式 `ListBoxItem{Height=28}` 会把 56 行夹扁 ⇒ 用 `ScrollViewer + ItemsControl`。
5. **单文件安装器的自删护栏**：从别处运行 `--uninstall` 时，自删逻辑会删掉"运行中的那个 exe"（构建产物）⇒ 仅在"自身位于被卸载目录内"时自删。
6. **GitHub 单文件上限 100 MB**：`dist/payload.zip`（183 MB）曾导致 push 被拒 ⇒ 已 gitignore，分发只出单文件安装器。
7. **队友上限 8 已满、且无删除/重置工具** ⇒ 新活只能：Lead 自己做（最可靠）／派最轻的队友／用户手动腾位后 spawn。
8. **`gh` 默认会解析到 `upstream`**（本仓有两个 remote）⇒ `gh release create` 等必须显式 `--repo XKLMY-hi/UTvTU`。

## 10. 建议的接手顺序

1. **`task-37`** `.ustxp` 丢数据级 P0（用户工程文件安全）—— 属 m2-view 名义范围，先协调或 Lead 接
2. **`task-21`** 分离按钮崩溃（整机退出级）
3. **`task-26`** 上游 Core 选择性合并（价值最高、工作量最大）
4. **§9.1 字号陷阱 5 处**（体验细节，需用户再授权）
5. 清理：`UTvTU-mx-lib` 残留目录、§2 两条无价值未并入分支
