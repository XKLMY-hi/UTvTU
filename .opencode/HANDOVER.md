# 会话交接（2026-09-19 起）

> **恢复方式（新会话）**：工作目录设为 **`G:\xklmy文件夹\vibe coding\UTvTU`**（主战场），先
> `git fetch origin && git merge --ff-only origin/plus-develop` 对齐，然后按顺序读：
> `.opencode/memory/MEMORY.md` → `.opencode/memory/utvtu-migration.md` → `.opencode/HANDOVER.md`（本文）→ 需要时再看 `.opencode/plans/*.md`。

## 一句话状态

项目已更名迁移为 **UTvTU**（仓库 `XKLMY-hi/UTvTU`，旧仓库 OpenUTAU-Plus 撤包归档）；上游定向移植（节拍器 + 钢琴窗 A1-A10/B1-B2）与上游 A 类修复全部完成；合成/渲染管线已做最小解耦（接缝显式化）。**测试基线 310 全绿**，构建在本机 0 错误。

当前提交：`2155207d`（两个工作区与两个仓库均已对齐）

## 环境（别再用旧会话的命令）

| 项 | 值 |
|---|---|
| 主工作区 | `G:\xklmy文件夹\vibe coding\UTvTU`（新）；`G:\xklmy文件夹\vibe coding\OpenUTAU Plus`（旧，保留为镜像） |
| dotnet | 系统 `C:\Program Files\dotnet`（SDK 8/9/10，默认 10.0.400 编 net8.0 正常），**无需** DOTNET_ROOT |
| NuGet | 离线，缓存 `C:\Users\XKLMY\.nuget\packages`；restore 必须 `--ignore-failed-sources` |
| 真机能力 | **有 1 个歌姬**（真实合成可实测）、**VST 可测**（扫本机目录）；我**无法手操 OUP 界面** |
| 构建/测试 | 见 `.opencode/memory/env-refresh-2026-09-g-drive.md`（含 obj 只读、BOM 等坑） |

```powershell
dotnet restore OpenUtau.sln -m:1 -p:TreatWarningsAsErrors=false --ignore-failed-sources
dotnet build OpenUtau.sln --no-restore -m:1 -p:RuntimeIdentifiers= -p:UsedAvaloniaProducts=   # 先关掉运行中的 OpenUtau.exe，否则 dll 被锁
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build                                      # 基线 310
.\OpenUtau\bin\Debug\net8.0-windows\OpenUtau.exe
```

## git 约定（两工作区均已加固，勿破坏）

- `origin` = `XKLMY-hi/UTvTU`（日常推送）；`upstream` = `openutau/OpenUtau`（**push URL 已禁**，只 fetch）；旧工作区另有 `old-origin` = OpenUTAU-Plus（归档，新工作区已移除）
- `master` 的 upstream 跟踪**已解除**（防误推官方仓库）
- 推送需 `git -c http.sslVerify=false push origin plus-develop`（本机证书链缺失）
- 忘了 git 已改名时用 `git remote -v` 自查；`gh` **不带 `--repo` 默认认 upstream**，查本仓库必须显式指定

## 本阶段已完成（按时间倒序）

1. **迁移 UTvTU**（`6c4f0207` 及之后）
   - 新仓库全历史、旧仓库撤包（releases=0/tags=0）+ README 迁移横幅
   - 撤下"生成 release / 生成安装包"的东西：`build.yml`、`packaging/{OpenUtauPlus.iss, build-installer.ps1, ChineseSimplified.isl}`
   - 程序内 URL 改指 UTvTU；`UpdaterViewModel.UpdateCheckEnabled()` 暂返回 false（**新仓库首发后要改回 true**）
   - 本地工作区复制到新路径，两个工作区对齐
2. **上游 A 类修复 8 项**（`06e16a3b` / `46cac57c` / `f9a78d37`）：Worldline 缓存串行化、播放启动顺序、关闭前停播、音素化字典同步加载（+进度条）、工厂线程安全、父级表达式边界、ClassicSinger FreeMemory、KoreanCV 判空与 UST 标记解析、oto 固定写 0
3. **合成/渲染管线接缝**（`88944390`）：`RenderEngine` 六个静态门面 + `RenderGate` 归位 `OpenUtau.Audio`；14 条契约测试锁定"运输/导出层不得持合成内部状态"
4. **钢琴窗上游移植收官**：A1-A10（含 A8 弹跳/A9 光晕/A10 显示范围高亮）+ B1 Alt 拖拽复制 + B2 曲线编辑工具扩展（UCurve.ReplaceRange，新增 10 例测试）

## ⏸️ 已叫停 / 不要主动做

- **音频后端重构**（用户："后端先停一下，能用就行"）：接缝保留但不继续换合成实现、不做 A/B 比对。续接点见 `.opencode/plans/audio-pipeline-seam.md` 顶部状态说明
- **更名范围**（用户强调"非常重要，和后面操作一起做"）：代码与文案**仍是 OpenUTAU Plus**。扫描结果与分期建议见 `utvtu-migration.md`；⛔ `runtimes/vst3sdk/**/plus.svg` 是 SDK 自带文件不可改名

## 待用户决定 / 待办

1. **旧仓库是否在 GitHub 上 archive**（README 横幅已加，仓库仍可写）
2. **更新检查闸门恢复**：等 UTvTU 首发 release → `UpdateCheckEnabled()` 改回 true
3. **A8-A10 / B1 / B2 的实机验收**（用户口头说过"没啥问题"，未逐条过）；另 A1 的钢琴窗关闭按钮在**工具栏右端**（上游在右上角），用户尚未表态
4. **前端待办池**（用户此前挂着未选）：右键菜单幽灵弹窗（`context-menu-debug.md`，根因线索 OverlayLayer 缺失）、`ui-redesign-phase.md` 里"遗留小 bug（用户说以后修）"待用户点名

## 用户口径备忘

- 中文沟通；commit 用中文；原子化提交；每步构建 0 错误 + 测试全绿 → 实机预览闸门
- 偏好直接推荐而非罗列选项；认可时会回"可以 / 没啥问题"
- 只删东西时按明确口径执行（例如"只删生成 release 和生成安装包的东西"），**多删了要能一键找回**（用 `git checkout HEAD -- <path>`）
