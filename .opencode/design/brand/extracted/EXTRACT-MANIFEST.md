# EXTRACT-MANIFEST — UTvTU 品牌 HTML 纯提取（W42）

> **来源**：`utvtu-brand-tailwind.html`（用户设计稿，几何权威源）。**本目录只做提取，不做任何改动**：
> 不改比例 / 不改字距 / 不做笔宽补偿 / 不描摹 / 不合并拆分组件 / 不"顺手优化"。
> `../out/` 目录内的资产此前做过二次创作（光学补偿、字距重排、描摹重建），**标记为 derived/派生，不作产品用**。

## 统计
- 具名 SVG 组件（`data-pencil-name`）：**107** 个 → `extracted/*.svg`（A 类，逐字）
- 逐字校验（viewBox + stroke-width 与源一致）：**107/107**
- B 类（CSS 画的瓦片）：CSS 相关声明 **0** 条 → 见 `css-tiles.txt`
- C 类（Roboto 活文字）：CSS Roboto 声明 **0** 处；SVG `<text>` 节点 **0** 个 → 见 `live-text.txt`
- PDF 探测：`utvtu-brand-sheet.pdf` 共 313 个内容流，路径算子 **68643**、文本算子 **2523** ⇒ **字标在 PDF 里是矢量路径，可从 PDF 提取轮廓**

## A 类 · 逐字矢量（前 40 条；完整见 `extract-index.json`）

| 组件名 | 文件 | viewBox | stroke-width | fill | d 长度 | 逐字 |
|---|---|---|---|---|---|---|
| Mark 176 | `mark-176.svg` | `0 0 64 64` | `22` | `none` | 143 | ✔ |
| Circle Mark 88 | `circle-mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Rounded square Mark 88 | `rounded-square-mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Finish Primary Mark 88 | `finish-primary-mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Finish Ink · dark Mark 88 | `finish-ink-dark-mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Finish Tonal Mark 88 | `finish-tonal-mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Mark 88 | `mark-88.svg` | `0 0 64 64` | `11` | `none` | 143 | ✔ |
| Mark 70 | `mark-70.svg` | `0 0 64 64` | `8.75` | `none` | 143 | ✔ |
| Mark 35 | `mark-35.svg` | `0 0 64 64` | `4.375` | `none` | 143 | ✔ |
| Mark 26 | `mark-26.svg` | `0 0 64 64` | `3.25` | `none` | 143 | ✔ |
| Mark 18 | `mark-18.svg` | `0 0 64 64` | `2.25` | `none` | 143 | ✔ |
| Mark 9 | `mark-9.svg` | `0 0 64 64` | `1.125` | `none` | 143 | ✔ |
| v / Chevron | `v-chevron.svg` | `0 0 9 14` | `61` | `none` | 17 | ✔ |
| Mark stack | `mark-stack.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark / 240 | `mark-240.svg` | `0 0 64 64` | `30` | `none` | 143 | ✔ |
| Mark 72 | `mark-72.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-2.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-3.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-4.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-5.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-6.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 72 | `mark-72-7.svg` | `0 0 64 64` | `9` | `none` | 143 | ✔ |
| Mark 64 | `mark-64.svg` | `0 0 64 64` | `8` | `none` | 143 | ✔ |
| Mark 32 | `mark-32.svg` | `0 0 64 64` | `4` | `none` | 143 | ✔ |
| Mark 24 | `mark-24.svg` | `0 0 64 64` | `3` | `none` | 143 | ✔ |
| Mark 16 | `mark-16.svg` | `0 0 64 64` | `2` | `none` | 143 | ✔ |
| Mark 64 | `mark-64-2.svg` | `0 0 64 64` | `8` | `none` | 143 | ✔ |
| Mark 32 | `mark-32-2.svg` | `0 0 64 64` | `4` | `none` | 143 | ✔ |
| Mark 24 | `mark-24-2.svg` | `0 0 64 64` | `3` | `none` | 143 | ✔ |
| Mark 16 | `mark-16-2.svg` | `0 0 64 64` | `2` | `none` | 143 | ✔ |
| Band mark 1 | `band-mark-1.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 2 | `band-mark-2.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 3 | `band-mark-3.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 4 | `band-mark-4.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 5 | `band-mark-5.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 6 | `band-mark-6.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 7 | `band-mark-7.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 8 | `band-mark-8.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 9 | `band-mark-9.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |
| Band mark 10 | `band-mark-10.svg` | `0 0 64 64` | `6` | `none` | 143 | ✔ |

## B/C 类
- `css-tiles.txt`：CSS 里与瓦片相关的原文声明（颜色/半径/描边宽）逐条照抄；翻译成 SVG 时**逐项取这些值**。
- `live-text.txt`：Roboto 声明与 `<text>` 节点原文；`wordmark-live-text.svg` 保留 `<text>` 并照抄 font-family/size/letter-spacing/weight，**文件头标注"依赖 Roboto，未转轮廓"**。

## 红线（本目录自检）
`git diff --stat` 应为纯新增；任何 A 类文件的 `d` 与源不一致即视为违规（校验脚本 `_extract.py` 输出 N/N）。


## D 类 · 光栅提取（裁图）—— **❌ 无效待重做**（W43 判定）

> **作废原因**（两轮实测）：裁切框错（`(140,269,2224,1676)` 把下方说明文字与分隔线一起框进来；
> 且"cap 1407px"是把**整段裁切高度**当成了 cap，真实 cap ≈ **547px**）；@1x 蒙版号称"保留抗锯齿"
> 但半透明像素实测 = 0（抗锯齿在映射时被丢掉了）。
> 文件已移入 `_invalid/`，**不得消费**；D 类若要重做，按下方 A′ 的"修正参数"重跑（当前不需要：
> 字标已按 A′ 走通矢量提取）。

| 文件（已移入 `_invalid/`） | 内容 | 原裁切 | 判定 |
|---|---|---|---|
| `wordmark-alpha@1x/@2x/@4x.png` | 字标 alpha 蒙版 | crop box (x140, y269, x2224, y1676) | **无效待重做**（框错 + 抗锯齿丢失） |
| `_selftest-wordmark-{ink,white,brand}.png` | 由上述蒙版染色 | — | **无效待重做**（继承蒙版缺陷） |
| `wordmark-raster.json` / `wordmark-extract-report.json` | 上一轮参数与被误判为字标的那段路径（bbox 1288,1844,2448,2744 = 底板，不是字标） | — | **无效待重做** |

**修正参数（仅当将来需要切图兜底时用，实测）**：`sections/02-wordmark.png` 里大字标墨迹范围 =
**x 140..2205 / y 269..816**（宽 2065 × 高 547，宽高比 3.775）；alpha 用**局部背景采样**线性映射
（`alpha = clamp((bg_local − L)/(bg_local − ink) × 255)`），**不设阈值**以保留边缘半透明。

## A′ 类 · PDF Type3 字标轮廓（W43 新增，**当前有效**）

| 产物 | 内容 |
|---|---|
| `wordmark-outline.svg` | 字标轮廓（Fill / `fill-rule="nonzero"`），**5 段路径全部逐字照抄** PDF 原文 |
| `wordmark-outline-cap100.png` | 自证①：cap=100px 的 1:1 渲染（自有 nonzero 扫描线填充，无第三方渲染器） |
| `wordmark-outline-vs-02-wordmark.png` | 自证②：与 `sections/02-wordmark.png` 大字标行的同尺度对照图 |
| `wordmark-outline-report.json` | 5 段几何的 bbox / 宽 / 步进 / 变换矩阵 / 基线 / cap 数值 |
| `_w43_type3_wordmark.py` | 提取器（`probe` / `find-run` / `scan` / `chevron` / `extract` / `verify` / `selftest`） |
| `_w43_iou_check.py` | 数值自证：本提取 vs 设计稿参考的墨迹 IoU（整体 + 按列带分解） |

**来源与数字**：`utvtu-brand-sheet.pdf` 第 1 页（内容流 obj 11），Type3 字体资源 **`/F5`（obj 5）**，
FontMatrix `[1/2048 0 0 −1/2048 0 0]`，`/CharProcs` 27 个（每个字形的轮廓 = 一段**路径程序**，
非位图 ⇒ 失败分支未触发）。字标 = 4 个 Type3 字形 + 1 段**页面路径**（小写 v 是几何切角，不是字体字形）：

| 序 | 字形名 | 来源 | bbox（PDF 点，y 向上） | 宽 | 高 | Widths | 步进 |
|---|---|---|---|---|---|---|---|
| 1 | `g39` = U | CharProc obj 24 | 1357.99, 2332.24 .. 1567.85, 2609.71 | 209.86 | 277.47 | 1350 | 253.784 |
| 2 | `g38` = T | CharProc obj 23 | 1581.53, 2336.00 .. 1804.73, 2609.71 | 223.21 | 273.71 | 1269 | 238.557 |
| 3 | v（切角） | 页路径（CTM 1,0,0,−1,1821,2509） | 1790.50, 2336.50 .. 1942.50, 2539.50 | 152.00 | 203.00 | — | — |
| 4 | `g38` = T | CharProc obj 23 | 1943.74, 2336.00 .. 2166.95, 2609.71 | 223.21 | 273.71 | 1269 | 238.557 |
| 5 | `g39` = U | CharProc obj 24 | 2180.55, 2332.24 .. 2390.40, 2609.71 | 209.86 | 277.47 | 1350 | 253.784 |

合计墨迹 **1032.41 × 277.47**（宽高比 3.721）；基线 y=2336.00，**cap 高 273.71**（U 碗下伸 3.76）。
逐字校验：`_w43_type3_wordmark.py verify` 输出 **5/5 全部一致**（每条 `<path d>` == CharProc / 页路径原文）。
数值自证：`_w43_iou_check.py` 整体 IoU = **0.9125**（列带最低 0.68，出现在字间细墨带——参考图是
浏览器渲染 + LANCZOS 缩放，二值化后细带对 1px 错位最敏感）。

**两个坑（写下来免得重踩）**：① 切角 v 的轮廓是"描边转轮廓"的**自交单环**，用 even-odd 填充会填成
一条贯穿细缝（实测 IoU 0.855 → nonzero 0.9125）；② PDF 操作数在算子**之前**（`x y m`），切页路径原文时
必须从"上一个算子之后"开始，否则丢掉起点、多出一条横穿字形的假边。

