# UI 抽验（W31/W28 + 两条人手项）· fx-verify · 树 `plus-develop @ 548b6c09`（干净）

1. **W31① ToggleButton**：仅代码审查 PASS — `Md3ControlThemes.axaml:165` 新增 `ControlTheme Md3ToggleButtonTheme`（`:216` 挂载），`Styles.axaml:179-205` 的 `.normal/.toolbar` 悬浮/选中底色已改**控件级**状态色、无 `/template/`。**像素未验**（本轮未在真机取到该控件样例）。
2. **W31② MenuFlyoutPresenter 圆角**：仅代码审查 PASS — 真值来自 `Md3Menus.axaml:109` 的 `{x:Type MenuFlyoutPresenter}` 模板 `PART_PopupBorder CornerRadius=12`（原两条应用级补丁已删）。⚠️ 小项：`Styles.axaml:20` 仍留着 `MenuFlyoutPresenter{CornerRadius=8}`（模板不读该属性 ⇒ 目前是死属性；建议 W32 顺手删，免得日后被误当生效值）。
3. **W31③ DataGrid 选中行**：仅代码审查 PASS — 改走主题资源键 `DataGridRowSelectedBackgroundBrush`（池 primary @50%，`Colors/Brushes.axaml`），应用级 `/template/ DataGridFrozenGrid` 补丁已删（`Styles.axaml:320-330` 注释）。像素未验。
4. **W31④ 卷帘音乐滚动条/菜单勾号**：**未验证**（未走到该表面）。
5. **W28 三处旧病（全部真机 PASS）**：`Ctrl+Shift+R` → 新窗口标题 **`渲染...`** 出现（原死键 ✅）；`Ctrl+S` → **只弹一个** `另存为...` 对话框（重复定义只活一支 ✅）；`Ctrl+M` → 视图**单次**切换（工作台↔混音台各一次，无双重 toggle ✅，取样 (700,300) `#262A2E`→`#101417`）。
6. **W28 快捷键总览窗口本身**：**未验证** — 我点开了品牌菜单但没定位到 `工具 → 快捷键总览` 项（窗口期间被最小化+移动，预算耗尽）⇒ 搜索/分组/冲突标记/空态四项**留给下一轮或人手**。
7. **两条人手 5 秒项**：**未验证（本轮未执行）** — 混音台链面板折叠、卷帘表达式区拖高/折叠；`%TEMP%\mxui.py` 已确认可用（PID+客户区坐标的 SendInput 注入器），下轮可直接用；两者机制与 W22 已像素验证的轨头/素材库同一套 `PanelSplitter`。
8. **对抗 1**：`ui-lint.ps1` → **ERROR 0 / WARN 0** ✔；`checks.ps1` → 键对齐 **EN 1057 / ZH 1057，差集 0 PASS**、新增行硬编码色值 **PASS** ✔。
9. **对抗 2**：`Styles.axaml` 的 `/template/` **18 处全是注释**（W31 的"已删除"记录，逐行核对：23/71/190/220/258/279/293-296/326/376/475-477/486/504/531/548）⇒ **真实补丁 0 条** ✔（与 Lead 口径一致）。
10. **防回潮契约测试**（`AppLevelStyles_HaveNoTemplatePatches_OutsideControlThemes`）：**未做临时改行破坏实验**（不在共享树上留风险）⇒ 仅按上述静态结果判定 PASS；若要验"测试真的会红"，建议在**独立工作树**里改一行跑一次再还原。
11. **本轮未重跑全量**（按卡范围；Lead 已报 733/0 + ui-lint 0/0）。**未发现 FAIL。**
12. 收尾：实例已按 PID + 精确前缀 `…\UTvTU\OpenUtau` 关闭（0 残留）；主题仍 **Dark**（未改）；主树 `git status` = 0 项；产品代码零改动；新增截图 `ui-01…ui-06`（6 张，含 `ui-05-tools-menu.png` 菜单留档）。
13. **给 Lead 的两条**：① 宣布前建议人手补"快捷键总览窗口 + 链面板折叠/表达式区拖高"（各 1 分钟，机制均已同源验过）；② `Styles.axaml:20` 那条死属性顺手清掉。
