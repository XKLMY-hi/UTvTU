# Google MD3 官方实例参考板

这是源码审查的参考资料，不是 UTvTU 产品截图或下一版设计稿。配套为 `index.html` + `reference-board.png`；完整结论在 [审查报告](../../plans/md3-style-audit-2026-10-11.md)。

## 已取得内容

- Reply 官方邮件应用原图，以及默认 / 动态配色对照原图。
- MaterialShapes 官方 35 图形原图，以及 AndroidX `MaterialShapes.kt` 原生几何源码。
- Google Design 的 Expressive 研究、邮件发送动作前后对照原图链接；动效、按钮组、颜色角色和 Material Symbols 官方入口。

三张本地图片保持原始字节；出处、尺寸、抓取日期、SHA-256 见 `references.json`。Google 文档内容通常为 CC BY 4.0（以来源页例外为准），配套参考板注明归属；AndroidX 源码为 Apache-2.0，原版权头完整保留，许可全文附于 `assets/MaterialShapes.LICENSE.txt`。研究文章图片只链接官方原地址，不作为项目资产发布。

HTML 是参考板布局的权威来源。Google 形状的权威几何来源是原始 Kotlin 参数，不是这张 PNG；本次未描摹或移植。源码取自 AndroidX 活动分支，日期与哈希固定本次快照，未来重新采集可能变化。官方实验性 API 不能等同于 Avalonia 已具备同样能力。

## 轻量复现

1. 用带 Pillow 的 Python 运行 `collect.py`：最多两个并发下载、20 秒网络超时、单响应 12MB 上限；同时扫描三组 XAML。
2. 用内置 Node 运行 `render.cjs`：新建一个无扩展、独立的 headless Edge，仅打开本目录 HTML，一页完成后关闭。主程序不启动、不截图、不重建。

`validation.json` 验证三张原图加载和哈希、源码哈希、本地链接、1440/800 宽布局无溢出及无脚本错误。PNG 是参考板导出图。

`source-metrics.json` 仅统计注释移除后的 XAML 直接引用，排除 C# 绘制与兼容别名，不能作为屏幕面积、实际对比度或运行时覆盖率。统计不是新增产品单元测试。
