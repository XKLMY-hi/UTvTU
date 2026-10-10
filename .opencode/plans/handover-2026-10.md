# 交接状态（2026-10-10 · W50 收口）

> **用途**：队友上下文普遍饱和、无法删除或重置 ⇒ 把"现在在哪、还剩什么、谁还能用"**外化到本文件**。
> **接手任何任务前先读本文件**，不要重新探索 —— 这是本仓库已被反复事故证明过的省上下文方式。

## 0. 代码与发布状态

| 项 | 值 |
|---|---|
| 分支 | `plus-develop`，HEAD **`9349d35a`**（**已推 `origin`**，与远端一致） |
| 闸门 | 构建 **0 错误** ｜ 全量 **813/813** ｜ `ui-lint` **ERROR 0 / WARN 0** |
| 发布 | GitHub Release **`UTvTU-26.10.7-150135`**：单文件安装器（227.5 MB，载荷内嵌） |
| 工作区 | 干净（仅无关的 `screenshots/social/`） |

## 1. 刚完成（W50 欢迎页，队列已清空）

- `ee8652cf` 欢迎页重建为 MD3：304 药丸导航（**应用级共享** `Button.navItem`）/ 页头 28+14 / 最近行 56 / assist chip / 主题按钮 / 空态容器 / 波形装饰带
- `ff66e3cc` 搜索语义抽成纯函数 `WelcomeArt.FilterRecent` + 8 条断言
- `9349d35a` 独立复核（fx-ui）三处修复 + 防回潮断言：选中药丸文字色、波形带宽度弹性、`railLink` 字号
- 真机截图：`.dsh/fx/shots/welcome/w50-*.png`（宽窗 / 最小窗口 / 四页 / 搜索两态）

## 2. 未完成（任务板实况）

| 任务 | 内容 | 状态 | 负责人 | 上下文 |
|---|---|---|---|---|
| `task-26` | W14 上游 Core/引擎选择性合并（232 提交分诊） | **in_progress** | fx-core | ⚠ 饱和 |
| `task-37` | W8 `.ustxp` 格式止血（**P0 丢数据级**：上游重存丢 Plus 字段） | **in_progress** | m2-view | ⚠ 饱和 |
| `task-19` | W8 `.ustxp` 结构审计（只读 + 提案文档） | pending | 未派 | — |
| `task-21` | W9 修崩溃：分离按钮触发 `InvalidateArrange on wrong LayoutManager`（整机退出级） | pending | 未派 | — |
| `task-3` / `task-4` | effect-rack 线的历史集成/验证 | pending | lead | 疑似过时，动前先复核 |

## 3. 已知未修项（**用户 2026-10-10 裁决"其他都别动"**）

**字号陷阱**：应用级 `Styles.axaml:110 TextBlock{FontSize 13}`（与 `Md3InputThemes` 的 `TextBlock{Foreground}`）是**显式赋值 ⇒ 压过继承**。凡"设在容器/按钮上、期望子元素跟随"的字体与前景都**无效**。已修：共享层 `Button.navItem TextBlock{14}`、`.selected TextBlock{on-secondary-container}`、`WelcomeView` 的 `Button.railLink TextBlock{12}`。
**仍被压掉的点位（实测清单）**：

- `MainWindow.axaml` `Button.viewTab` 11 → 实际 **13**（**高**：顶栏视图胶囊）｜`Button.libTab` 10 → **13**
- `RenderWindow.axaml` `RadioButton.renderOption` 12 → 13 ｜`Button.secondaryBtn` 11 → 13
- `MixFxDialog.axaml` `ToggleButton.md3switch` 12 → 13
- 低-中风险（需截图确认）：`Styles.axaml` `MenuItem` 12、`Md3InputThemes` 的 Menu/Tab

修法：每处补 `<Style Selector="X TextBlock"><Setter Property="FontSize" Value="N"/></Style>`。
⚠ `MainWindow.axaml` 是 **m2-view / `task-37` 的名义写范围** ⇒ 动前先协调（一文件一写入者）。

## 4. 已知偶发 flake

全量 813 中曾出现 **1 次单例失败、重跑即过**（未抓到用例名；历史上 Light 变体有已知 flake）。
复现：`dotnet test OpenUtau.Test\OpenUtau.Test.csproj --no-build` 连跑 ≥3 次，记录失败名。

## 5. 队友上下文状况（框架**无**删除/重置工具，且**上限 8 已满**）

| 队友 | 近况 | 建议用法 |
|---|---|---|
| `fx-ui` | 设计/MD3 知识强，耗过两轮实现预算 | ✅ **只读复核**（有值表与清单在脑） |
| `fx-ctl` | 接手 25 分钟**零产出**，被 Lead 停 | ⚠ 不再派实现任务 |
| `fx-core` / `m2-view` | 各自长跑任务在身 | 仅用其 in_progress 任务 |
| `fx-verify` | 专职验证，近期未动 | ✅ 只读验证/对抗审查 |
| `fx-rack` / `m1-strip` / `m3-chain` | 近期未动 | ✅ 相对轻，可派小卡 |

**新活只能三条路**：① Lead 自己做（本仓最可靠的路径）② 派给上表"相对轻"的队友 ③ 若 GUI 提供成员管理，手动腾位后 `spawn`（我无法用工具做）。

## 6. 工具（`.dsh/fx/`，全部可用）

`launch.ps1 -Exe`｜`capture.ps1 -ProcessId -Out`（PrintWindow，**不需要前台**）｜`click.ps1`（窗口相对；`-Screen` 绝对）｜`sendkeys.ps1`（⚠ **非前台窗口打不进**）｜**`setvalue.ps1`（UIA `ValuePattern` 设值，替代打字）**｜**`resize.ps1`（改窗口尺寸 ⇒ 最小窗口验证）**｜`close.ps1`（**必须**用它优雅关闭）｜`uia.ps1 -Mode list|invoke [-Rect]`｜`ui-lint.ps1`｜`checks.ps1`

## 7. 铁律（`AGENTS.md`「并行协作铁律」）

禁破坏性撤销（`git checkout -- <file>` / `reset --hard` / `stash` / `clean`）｜**绿灯即 commit**｜动手前 `git status --short`｜同一文件只有一个写入者。

## 8. 建议接手顺序（若要继续开发）

1. **`task-37`** —— `.ustxp` 丢数据级 P0（影响用户工程文件安全），但属 m2-view 名义范围，需先协调或由 Lead 接
2. **`task-21`** —— 分离按钮崩溃（整机退出级）
3. **`task-26`** —— 上游 Core 摘取（价值最高、工作量最大）
4. **字号陷阱 5 处** —— 体验细节；用户已说"别动"，需其再授权
