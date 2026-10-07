# SYNC.md — 与上游同步策略

## 远程配置

| 远程 | URL | 用途 |
|------|-----|------|
| `upstream` | `https://github.com/openutau/OpenUtau.git` | 官方源仓库 |
| `origin` | （待配置） | Plus 远程仓库 |

## 分支策略

```
upstream/master  ←─── 定期同步
       │
       ▼
    master (本地镜像，不动任何代码)
       │
       ▼
  plus-develop (Plus 所有改动在这里)
```

## 同步步骤

### 1. 拉取上游更新

```bash
git checkout master
git fetch upstream
git merge upstream/master
```

### 2. 合并到 Plus

```bash
git checkout plus-develop
git merge master
```

### 3. 处理冲突

如果出现冲突，优先关注以下目录/文件（Plus 改动区域）：

## Plus 独有改动追踪

| 文件 | 改动类型 | 说明 |
|------|----------|------|
| `OpenUtau/App.axaml` | 品牌 | 应用名称改为 "UTvTU" |
| `OpenUtau/ViewModels/MainWindowViewModel.cs` | 品牌 | AppVersion 改为 "UTvTU" |
| `OpenUtau/OpenUtau.csproj` | 品牌 | CFBundleName 等 macOS 信息 |
| `OpenUtau/Strings/Strings.axaml` | 品牌 | 英文界面字符串中的品牌名 |
| `OpenUtau/Strings/Strings.zh-CN.axaml` | 品牌+本地化 | 中文界面字符串中的品牌名 |
| `CLAUDE.md` | 文档 | 项目文档 |
| `README-PLUS.md` | 文档 | Plus 项目说明 |
| `SYNC.md` | 文档 | 本文件 |

## 冲突预防

1. **避免修改上游文件的命名空间和核心类名**（如 `OpenUtau.Core`、`OpenUtau.App` 等）
2. **品牌改动集中在资源文件和入口点**，不触及核心逻辑
3. **新功能尽量通过插件系统添加**，减少对核心代码的侵入
4. **中文本地化改进**建议通过 Crowdin 贡献回上游，减少维护负担

## 版本号规范

建议使用上游版本号 + Plus 后缀：
- 例：上游 `v0.1.565` → Plus `v0.1.565-plus.1`
- 在下一次上游合并时，重置 Plus 后缀计数
