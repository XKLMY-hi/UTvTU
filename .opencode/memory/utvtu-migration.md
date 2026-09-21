---
name: utvtu-migration
description: 2026-09-18 项目迁移 — 新仓库 UTvTU（全历史）、旧 OUP 仓库撤包归档、远程改名约定
metadata:
  node_type: memory
  type: project
---

# UTvTU 迁移（2026-09-18）

## 已完成的动作

| 项 | 结果 |
|---|---|
| 新仓库 | **https://github.com/XKLMY-hi/UTvTU**（public，默认分支 `plus-develop`），已推 `plus-develop` + `master`，**保留全部提交历史**（`.git` 128 MB，历史可直接推） |
| 旧仓库 | `XKLMY-hi/OpenUTAU-Plus` → 2 个 Plus release **已删除**、3 个 Plus 标签**已删除**；上游继承来的历史 release 按用户决定**保留**；README 顶部已加**迁移横幅**（只推旧仓库，新仓库无横幅；本地用 revert 抵消，故本地历史多一个 revert 提交但内容与 origin 一致） |
| 本地远程 | `origin` = **UTvTU**（新）；`old-origin` = OpenUTAU-Plus（旧，归档）；`upstream` = openutau/OpenUtau（不变） |
| 部署产物 | `packaging/dist/**`（178 MB 安装包）与 `packaging/publish/**`（约 1 GB 发布产物）已删除（**均未跟踪**，git 历史不受影响） |
| 撤下生成器（commit `6c4f0207`，**两个仓库都已推**） | 删：`.github/workflows/build.yml`（唯一会自动创建 release 的流水线）、`packaging/{OpenUtauPlus.iss, build-installer.ps1, ChineseSimplified.isl}`（安装包生成器与语言包） |
| 迁移期 URL | `UpdaterViewModel`（API/页面 URL → UTvTU，并加 `UpdateCheckEnabled()` 闸门返回 false）、`PreferencesDialog`、`WelcomeWindow`、两个 README 的 clone 地址 |
| **本地工作区**（会话不迁、原目录保留） | 已复制到 **`G:\xklmy文件夹\vibe coding\UTvTU`**（50200 文件 / 4.37 GB，robocopy /MT:16，0 失败）；新工作区**直接构建通过 0 错误**（`obj` 里的旧路径不会绊住 MSBuild，无需先删 obj） |

**用户口径（2026-09-18）**：只删"生成 release"和"生成安装包"的东西。据此**保留**：`pr-test.yml`、`stale.yml`、`worldline-build.yml`（C++ 构建）、`release-cleanup.yml`（只删旧 release、不生成）、`packaging/release-notes-*.md`（文档）。

## git 约定（两个工作区都已按此加固）

| 远程 | 用途 | 约束 |
|---|---|---|
| `origin` = XKLMY-hi/UTvTU | 日常推送目标 | 两个工作区都指向它 |
| `upstream` = openutau/OpenUtau | 只 fetch | **push URL 已改为 `DISABLED_readonly_use_fetch_only`**，防误推官方仓库 |
| `old-origin` = XKLMY-hi/OpenUTAU-Plus | 归档仓库 | 只在旧工作区保留；**新工作区已移除**（新建会话只用新工作区即可） |

- `master` 分支的 upstream 跟踪**已解除**（原本跟踪 `upstream/master`，`git push` 会误推官方）
- 两个工作区当前都在 `f30e119f`、工作树干净（仅未跟踪 `.dsh/`）
- 旧工作区 `G:\xklmy文件夹\vibe coding\OpenUTAU Plus` 保留为**镜像**（沙箱仍以它为根，本会话继续用）；今后优先在新路径开发。**2026-09-21 更新：旧镜像已弃用，只读、不再写入**

## 关键坑

- **`gh` 默认认 upstream 仓库**：不带 `--repo` 时 `gh release list` 读的是 `openutau/OpenUtau`，会看到 33 个上游 release（全是假的"我们的 release"）。**查本仓库必须显式 `--repo XKLMY-hi/<repo>`**。
- 旧仓库的 release 表面上有 2026-09 的 `0.1.570.x-alpha`，那也是 fork 时从上游继承的，不是我们发的。

## 待决/未做

1. **更名范围**（用户强调重要、暂缓，且要求"和后面操作一起做"）：产品显示名/程序集名/命名空间/文件扩展名要改到哪一层。当前代码与文案**仍是 OpenUTAU Plus**，扫描结果：
   - 品牌文本 **157 处 / 53 文件**（`OpenUTAU Plus` / `OpenUtau Plus` / `UTAU Plus`）
   - `PlusInfo.cs`（7 处引用）、`OpenUtau/Themes/Plus.Resources.axaml`
   - ⚠️ `runtimes/vst3sdk/**/plus.svg` 是 VST3 SDK 自带文件，**不可改名**
   - 建议分期：① 用户可见文案+标题+数据目录显示名 ② 仓库侧文案/关于对话框 ③ 程序集名与文件扩展名（要动 VstProbe、单实例匹配、内部路径，风险最高）
2. **更新检查闸门需在首发后打开**：`UpdaterViewModel.UpdateCheckEnabled()` 现在返回 false；UTvTU 发布首个 release 后改回 true（写成方法而非 const 是因为 const 会触发 CS0162 不可达错误，与 `TreatWarningsAsErrors` 冲突）
3. **旧仓库是否在 GitHub 上 archive**：README 横幅已加，仓库尚未归档为只读（等用户决定；归档后 issue/PR 会冻结）
4. 旧仓库仍保留 `release-cleanup.yml`（按"只删生成器"口径未动）；若将来彻底停用可一并撤
5. 本地历史比 origin 多一个 revert 提交（`9f1d4582`，抵消新仓库不需要的横幅）；两边**文件内容一致**，不必处理
