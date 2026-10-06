# UTvTU 品牌资产 · 冻结清单（W39f）

> **几何已冻结**（源路径 verbatim）。若几何有变，**消费方必须重取**对应文件；本轮之后我只在你要求时才动。

## 冻结文件

| 文件 | 内容 | 消费方 |
|---|---|---|
| `out/utvtu-brand-lockup.axaml` | **单文件几何全集**：`brand-mark` / `brand-brace` / `brand-brace-flipx`（程序化镜像）/ `brand-v-chevron` / `brand-v-chevron-stroke` / `brand-wordmark`（描摹 U/T + 源 v）+ 三式锁定放置说明 | fx-ui（启动窗/欢迎页/关于页/顶栏）、实现者 |
| `out/utvtu-brand-geometry.axaml` | 同源几何（含 `brand-brace` 源路径版）；**与上表内容重叠**，任选其一即可（推荐 lockup 那份） | fx-ui |
| `out/utvtu.ico` | 多尺寸 16/24/32/48/64/128/256，**逐尺寸渲染**（源几何，描边语义） | fx-ctl（窗口/任务栏/文件关联） |
| `out/utvtu-icon-16.png` `/24` `/32` | 逐尺寸瓦片（源几何） | fx-ctl |
| `out/utvtu-splash@1x.png` `/@2x.png` | 480×320 / 960×640 闪屏（源几何 `{ 脸 }` + 字标轮廓） | fx-ctl（启动窗） |
| `out/brand-icon-ladder.png` | 图标阶梯 256→16 × primary/ink/tonal | 设计/评审 |
| `out/utvtu-lockup-*.svg`（9） | 3 式 × 3 finish（ink/reversed/branded） | 物料/文档/网页 |
| `out/utvtu-lockup-*@1x/@2x.png`（18） | 同上栅格（cap 24/48） | 物料/文档 |
| `out/utvtu-mark-source.svg` / `utvtu-brace-source.svg` / `-source-flipx.svg` / `utvtu-v-chevron-{fill,stroke}.svg` | 源几何 SVG（描边语义） | 设计/网页 |
| `out/utvtu-wordmark.svg` / `-mono.svg` | 字标轮廓 SVG（Fill） | 物料 |
| `out/brand-lockup-sheet.png` / `brand-lockup-ladder.png` / `brand-brace-new-vs-old.png` | 评审图 | 用户/评审 |
| `out/brand-verify-*.png` | 健全性叠图（**不是逼近栅格**） | 评审 |
| `out/brand-source-metrics.json` / `brand-source-raster-metrics.json` / `brand-wordmark-spacing.json` / `brand-wordmark-cap24.json` | 全部实测数值（源几何） | 评审/实现 |

## 已作废（不要引用）
`out/utvtu-mark.svg`、`utvtu-mark-*.svg`（描摹+描边转填充系列）、`utvtu-brace.svg`（旧描摹版）、
`brand-metrics.json`（在错误几何上测的）、`brand-optical-compensation-*.png`（错误几何上的补偿）、
`brand-variants-*.png`（早期简化变体）—— 保留仅为历史记录。

## 变更流程
1. 任何几何改动 ⇒ 我重出 `utvtu-brand-lockup.axaml` + 全部栅格资产，并**主动通知 fx-ctl / fx-ui**；
2. 仅文案/阶梯改动（如 cap 阈值）⇒ 不动文件，无需重取；
3. 消费方若发现文件时间戳早于 `utvtu-brand-tailwind.html` 的几何口径，以本清单为准来找我。
