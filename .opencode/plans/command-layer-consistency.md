# 快捷键「显示 ↔ 行为」一致性清单（W28 / M09 阶段 1 交付物）

> 基线：`try/cmd` @ `deb92ed2`（`plus-develop` 上第一个本卡提交之前的状态为准做对照）。
> 方法：**只读**逐条比对 `OpenUtau/Views/MainWindow.axaml` 的 `InputGesture=`（只管显示）
> 与 `MainWindow.axaml.cs` 的 `OnKeyDown`（`:1516` 起）+ `MapGlobalShortcut`/`HandleGlobalShortcut`（只管行为）。
> 结论分三类：✅ 一致 · ⚠️ 重复定义（行为正确但两处各写一遍）· ❌ 显示与行为不符（按下无反应）。
>
> **重要前提（实测）**：全仓**没有** `<KeyBinding>` / `KeyBindings` 集合（`git grep` 零命中），
> 因此 Avalonia `MenuItem.InputGesture` **纯显示**、不会自动注册快捷键。凡是菜单写了手势、
> 而 `OnKeyDown` 没处理的，用户按下去**什么都不会发生**。

## 1. 菜单显示的 9 条 `InputGesture`（逐条对齐）

| # | 菜单显示 | 菜单项（i18n 键） | 旧行为（switch / 全局表） | 判定 | 本卡处置 |
|---|---|---|---|---|---|
| 1 | `Ctrl+N` | `menu.file.new` | `cmdKey` 分支 `case Key.N: NewProject()` | ✅ 一致 | 注册表 `file.new` |
| 2 | `Ctrl+O` | `menu.file.open` | `cmdKey` 分支 `case Key.O: Open()` | ✅ 一致 | `file.open` |
| 3 | `Ctrl+S` | `menu.file.save` | **两处**：`MapGlobalShortcut` → `Save`（先命中并 return）＋ `cmdKey` 分支 `case Key.S` ⇒ **后者是死代码** | ⚠️ 重复定义 | `file.save`（`PreFocus`）；死分支已删 |
| 4 | `Ctrl+Shift+S` | `menu.file.saveas` | `cmdKey\|Shift` 分支 `case Key.S: SaveAs()` | ✅ 一致 | `file.saveas` |
| 5 | `Ctrl+Shift+R` | `menu.file.render` | **无任何处理**：`cmdKey\|Shift` 分支只有 `Z`/`S`，全仓 `case Key.R` 零命中 | ❌ **死键（按下无反应）** | 注册表 `file.render` ⇒ **已修** |
| 6 | `Ctrl+Z` | 撤销（`menu.edit.undo`，Header 走 `{Binding UndoText}`） | `cmdKey` 分支 `case Key.Z: Undo()` | ✅ 一致 | `edit.undo`（带 `CanExecute`） |
| 7 | `Ctrl+Y` | 重做（`menu.edit.redo`） | `cmdKey` 分支 `case Key.Y: Redo()`；**另有 `Ctrl+Shift+Z` 也重做但菜单不显示** | ✅ 一致（别名未显示） | `edit.redo` + `edit.redo.alt`（别名显式登记） |
| 8 | `F11` | `menu.tools.fullscreen` | `None` 分支 `case Key.F11: OnMenuFullScreen` | ✅ 一致 | `tools.fullscreen` |
| 9 | `Ctrl+M` | `menu.tools.mixer` | **两处**：`MapGlobalShortcut` → `ToggleMixer`（先命中并 return）＋ `cmdKey` 分支 `case Key.M` ⇒ **后者是死代码** | ⚠️ 重复定义 | `tools.mixer`（`PreFocus`）；死分支已删 |

**小计**：9 条中 **6 条一致**、**2 条重复定义（#3 #9）**、**1 条死键（#5）**。

## 2. 反向：行为存在、但菜单**不显示**（共 13 条）

用户"能按出来但界面上找不到"的快捷键 —— 这是可发现性问题，清单照列（本卡不改菜单文案，
只把它们纳入注册表以便总览暴露）：

| 手势 | 行为 | 旧实现位置 |
|---|---|---|
| `Ctrl+W` | 分离 / 贴合混音台 | `MapGlobalShortcut` → `ToggleMixerAttachment` |
| `Ctrl+A` | 全选片段 | `cmdKey` 分支 `case Key.A` |
| `Ctrl+X` | 剪切片段 | `cmdKey` 分支 `case Key.X` |
| `Ctrl+C` | 复制片段 | `cmdKey` 分支 `case Key.C` |
| `Ctrl+V` | 粘贴片段 | `cmdKey` 分支 `case Key.V` |
| `Delete` | 删除选中片段 | `None` 分支 `case Key.Delete` |
| `Space` | 播放 / 暂停 | `None` 分支 `case Key.Space` |
| `Home` | 回到开头 | `None` 分支 `case Key.Home` |
| `End` | 跳到末尾 | `None` 分支 `case Key.End`（有片段时才动） |
| `Shift+S` | 独奏选中片段所在轨道 | `Shift` 分支 `case Key.S` |
| `Shift+M` | 静音选中片段所在轨道 | `Shift` 分支 `case Key.M` |
| `Alt+F4` | 退出应用 | `Alt` 分支 `case Key.F4` |
| `Esc` | 关闭遮罩（欢迎页 / 偏好设置） | `OnKeyDown` 开头的遮罩分支 |

## 3. 结构性问题（比单条不一致更值得记）

1. **同一张表写两遍**：`InputGesture=`（显示）与 `OnKeyDown`（行为）互不相干，
   改一处忘一处；#3/#9 已经因为"全局表短路"变成死代码而无人察觉。
2. **全局表与焦点检查的顺序不对称**（这是**有意**的，现已显式化为 `PreFocus`）：
   `Ctrl+M` / `Ctrl+W` / `Ctrl+S` 在**焦点检查之前**分发（卷帘持有焦点时也能触发），
   其余快捷键在焦点检查**之后**（卷帘可优先消费 `Ctrl+Z` 等）。
   这个不对称过去只存在于代码顺序里，很容易被重构破坏 —— 现在由注册表的 `PreFocus` 字段表达。
3. **快捷键无总览、无自定义**：`KeyGesture` 只出现在菜单显示与 3 处动态菜单项，
   没有任何配置/序列化入口 ⇒ 用户无法查、也无法改。

## 4. 本卡如何让上面这些**不再复发**（可验证）

- **单一事实来源**：`OpenUtau/Commands/CommandRegistry.cs`。菜单显示与键盘分发都读它；
  `MapGlobalShortcut` 也已改为注册表投影（不再手写第二张表）。
- **守卫用例** `EveryDisplayedInputGesture_ExistsInTheRegistry`：
  `MainWindow.axaml` 里每个 `InputGesture` 都必须能在注册表里查到同手势命令
  ⇒ #5 那种"菜单写着、按下没反应"的回归**会直接把测试打红**。
- **守卫用例** `NoTwoCommandsShareTheSameGesture`：同一手势绑多个命令即失败（总览也会标红章）。
- **守卫用例** `EveryNameKey_ExistsInBothLanguages`：显示名键 EN/zh 必须成对。
- **用户可见**：`工具 → 快捷键总览`（只读、可搜索、按分组、自动冲突标记）。

## 5. 仍未做（阶段 2/3，登记）

- 改绑 + 持久化（`Preferences`）与"恢复默认"；
- 命令面板（`Ctrl+Shift+P` 式）；
- 把**菜单项**也改为从注册表生成（现在只有快捷键涉及的命令进了表；
  菜单其余项仍是 XAML 硬编码，随分组迁移逐步纳入 —— 见卡内"每组一次提交"约束）；
- 卷帘/钢琴卷帘内部的视图级快捷键（`Ctrl+F` 搜索音符等）**有意不入本表**：它们是视图私有的，
  由视图自己处理；若将来要做"全局快捷键总览的完整视图"，需要一个 `CommandScope.PianoRoll` 分区。
