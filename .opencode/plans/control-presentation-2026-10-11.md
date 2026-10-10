# 控件内部呈现统一（2026-10-11）

用户指出旧视觉恢复后，图标对齐、文字居中和控件一致性仍未解决。本轮产品提交 `ed63eae2`，
沿用 GitHub 原版 UTvTU 的胶囊、圆形动作与柔和轨道头；推进主工作区控件内部的呈现职责。

## 根因与实现

- 主窗口视图/素材页签没有完整的内容居中约束，字体与前景容易被应用级 TextBlock 样式覆盖。
  `DawTextButtonTheme` 自有 `PART_Label`，显式转发按钮字体、颜色和内容对齐。
  奇数高度文字行在偶数高度按钮内，布局取整会让中心偏移半 DIP；标签保留小数位置，
  不通过各个按钮的 TranslateTransform 补偿。轨道名称仍按信息语义左对齐、垂直居中。
- 普通图标来自不同坐标系，旧的裸 Path 或手工 Scale/Translate 无法保证图形自身居中。
  `DawIcon` 按真实矢量边界等比适配固定图标格，处理非零/负原点与非方形边界；
  描边固定 1.5 DIP、圆端/圆角。克隆几何并转换副本，不改共享资产。
  主窗口、轨道设置和卷帘工具采用此控件，原始路径保持；品牌与显式 SVG viewBox 资产沿用原渲染器。
- `DawControls.axaml` 提供按钮、文字按钮、工具 Toggle、M/S 共用模板；
  表面、8%/12% 状态层、内焦点环与禁用态由模板负责，宿主只定密度和形状。
  主窗口 36 DIP 圆形动作与卷帘 20 DIP 紧凑工具采用同一呈现契约，保留各自 DAW 密度。
- 轨道 M/S 的裸路径改为同混音条一致的 10 DIP Bold 标签；启用时 M=error、S=primary，
  文字使用对应 on-* 角色。FX 和更多入口转为自有标签；混音条/主输出接入共用文字模板。
- 时间与数值读数显式居中；卷帘网格/调性按钮采用等宽、10 DIP 标签与同一模板。
  点击、命令、菜单宿主、OneWay 轨道状态、编辑/撤销与音频职责未迁入呈现控件。

## 验证

- SDK 9.0.317 解决方案构建通过；产品保留 TreatWarningsAsErrors。
- 相关检查 **95 通过、0 失败、0 跳过**；UI lint **ERROR 0 / WARN 0**，diff 空白检查通过。
  `.dsh/presentation-build.log`、`.dsh/presentation-targeted.log`、
  `.dsh/refactor-test-results/presentation-targeted.trx`。
- 新增 10 项呈现检查：五种真实源资产、负原点/竖线描边、800/1440 DIP 实际顶栏、
  轨道/混音 M 状态与字体一致、卷帘 12 个工具的内图形和文字居中、切换状态不跳动。
  数学检查精度 0.001 DIP，未放宽断言以绕过偏移。
- 默认 headless 路径边界为空，测试只将矢量几何创建交给真实 Skia 后端，保留 headless 字体和位图。
  不能整个替换渲染后端：会混用 HeadlessPlatformTypeface/SkiaTypeface，造成顺序相关失败。
  Avalonia 12 隐藏可变 locator，作用域注册桥仅在测试中使用，结束即恢复；不截产品图。
- 全量 **878 通过、0 失败、0 跳过**（4 分 17 秒）。记录 `.dsh/presentation-full.log`，
  TRX `.dsh/refactor-test-results/presentation-full-20261011.trx`。
- 可见 Windows 预览 PID 19820，启动于 2026-10-11 03:43:59 +08:00；两轨工程完成加载、进程响应正常，
  启动 ERR/FTL 0。记录 `.dsh/presentation-preview.json`、`.dsh/presentation-startup.log`。
  身份仍须按 exe 路径与启动时间核实，不能仅依赖 PID。保留供用户查看。

实机验收重点：运输图标与页签标签的中心、轨道 M/S/FX、卷帘工具开关、混音 M/S。
未截图产品；无头布局/矢量验证不能代替 Windows 字体光栅化、DPI 与音频听觉验收。
本轮统一的是上述工作区控件族，其余对话框、效果器与完整检查器仍需按控件族继续推进。
此前用户移动片段的 `No active UndoGroup Move parts` 仍未修复，不以本轮启动 smoke 掩盖。
