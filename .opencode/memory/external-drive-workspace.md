---
name: external-drive-workspace
description: 工作区在移动硬盘上，盘符随时会变 — 每次开工先确认实际路径
metadata:
  node_type: memory
  type: project
---

# 移动硬盘盘符漂移（2026-09-21 用户强调）

整个工作区在**移动硬盘**上开发，盘符随插拔/系统分配变化（已出现过 D: / E: / G: 下的同一路径 `xklmy文件夹\vibe coding\...`）。

## 硬性约定

- **每次工作开始前先确认实际工作区路径**，不要硬编码或沿用上会话的盘符（如 HANDOVER 里的 `G:\...` 可能已不可达）
- 确认方法：列出当前盘符下的 `xklmy文件夹\vibe coding`；用 `git remote -v` 核实（origin = `XKLMY-hi/UTvTU`，旧工作区另有 `old-origin`）
- 两个工作区都受影响：旧镜像 `OpenUTAU Plus`（**2026-09-21 起已弃用：只读、不再写入**）、新主战场 `UTvTU`（唯一写入目标）

**Why:** 盘符漂移曾导致上会话命令（`E:\tools\dotnet`、`G:\...`）全部失效，恢复会话时在白找路径。
**How to apply:** 新会话第一步先定位实际盘符与工作区，再执行构建/推送等操作。相关 [[env-refresh-2026-09-g-drive]]、[[utvtu-migration]]。

## git 属主不同：UTvTU 工作区每条 git 命令要带 safe.directory（2026-09-21）

`UTvTU` 目录属主与当前 shell 账号不一致（robocopy 复制遗留），git 报 `detected dubious ownership ...` 并拒绝操作。未写入持久 config，命令加：

```bash
git -c safe.directory="<盘符>:/xklmy文件夹/vibe coding/UTvTU" status
```

（嫌麻烦可 `git config --global --add safe.directory '<路径>'` 一次，但盘符漂移后路径失效需重加；旧工作区 OUP 的路径已在 `<盘符>:\home\.gitconfig` 里豁免过。）
