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

## A″ 类 · EvenOdd 载体适配的**拆件版**（W43 追加，当前有效）

**为什么必须拆**：`StreamGeometry` 无公开 `FillRule`、默认 **EvenOdd**；`Icons.axaml` 的字符串写法给不出
FillRule；消费侧 `Path` 也没有 per-use FillRule。而 PDF 里字标**两件都不是** EvenOdd 天然安全：

· **T（`g38`）** 的 CharProc 用**两个互相重叠的矩形**画字身与横杠（nonzero 下并集才是 T）⇒ EvenOdd 会把
  重叠区挖空（实测 **656 px** 差异）。已**精确重划为 3 个互不重叠子矩形**（纯分区，几何零改动：
  与原 NonZero 渲染**逐像素相等，0 px**）。
· **切角 v** 的填充轮廓是自交单环 ⇒ EvenOdd 会出现贯穿细缝 ⇒ 改用**描边**表达（描边不经过填充规则）。

| 产物 | 内容 |
|---|---|
| `wordmark-ut.svg` | U/T 四段（T 已重划）；`fill-rule="evenodd"`；U 的 `d` 逐字 verbatim |
| `wordmark-v-stroke.svg` | v 描边版：`stroke-width=61`、round join/cap、中心线写在文件头 |
| `wordmark-ut-evenodd-vs-nonzero.png` | 自证①：原样 EvenOdd（656 px 差）/ 原样 NonZero / 重划后 EvenOdd（0 px 差） |
| `wordmark-split-evenodd.png` | 自证②：拆件合并（**EvenOdd 下**）的完整字标 |
| `wordmark-split-vs-02-wordmark.png` | 自证②：与设计稿同尺度叠图（黑=仅我 / 红=仅参考 / 蓝=重合） |
| `wordmark-split-report.json` | 全部数字（含 cap=100 归一化定位表与 EvenOdd 自证数字） |
| `_w43_split.py` | 拆件脚本（重叠环重划 + 描边拟合 + 三张自证） |

**v 的定位与描边数字（本次实测；与 Lead 的换算逐项一致）**

- **stroke-width = 61.00**：由"与 PDF 轮廓做对称差拟合"独立量出，**残差 0.411%**；与 HTML 资产
  `v-chevron.svg` 声明的 `stroke-width="61"` + `vector-effect="non-scaling-stroke"` **一致 ⇒ 同一实例**。
- 中心线（本目录 SVG 坐标）= **(463.01, 100.71) → (508.51, 242.71) → (554.01, 100.71)**；
  页面空间 = (1821.00, 2509.00) → (1866.50, 2367.00) → (1912.00, 2509.00)。
- v 墨迹 = **152×203 pt**（页面 1790.50,2336.50 .. 1942.50,2539.50）；基线 y=2336.00；cap=273.71。
- **cap=100 归一化**（unit = cap/100 = 2.7371 pt）：左缘偏移 **158.02**（= 字标墨迹宽的 **41.89%**）、
  v 宽 **55.53**（**14.72%**）、v 高 **74.17**（**x高/cap = 0.7417**）、v 底距基线 **+0.18**、
  字标墨迹宽 **377.19**（宽/cap = **3.7719**；与"宽/墨迹高" 3.7208 差一个 U 碗下伸 3.76）。
- 合并验证（U/T EvenOdd + v 描边，**EvenOdd 下**）vs `sections/02-wordmark.png`：**IoU = 0.9095**
  （单件填充基准 0.9125，差 0.3% = v 模型残差 0.41% + 二值化边缘差）。
- 既有资产核对：`out/brand-wordmark-cap24.json` 只含小尺寸**可读性**指标（components / min_gap_px /
  stroke_px / empty_runs / touching_pairs），**不含**字标宽与 v 定位 ⇒ 定位数字只能取 PDF 提取值（上表）。
- v 的"填充版 / 修绕向版"**不需要**（描边路线已从根上绕开自交）。

### A″-1 · **表示已规范化**（W43 二次追加；几何未改）

问题（fx-ctl 接线时发现）：`wordmark-*.svg` 里的 `d` 原是 **PDF 内容流语法**（`x y m` / 6 数 `c` / `h`，
操作数在前），且 U 靠自身 `matrix`、T 靠顶层 `g` ⇒ **不是合法 SVG**，任何 SVG 渲染器都会解析错或报错。

已做（脚本 `_w43_normalize_svg.py`，只改表示）：
1. **展平**：`g`/`matrix` 全部乘进坐标 ⇒ 三个文件里 `matrix(` = 0、`<g ` = 0，坐标即 viewBox 坐标；
2. **语法**：所有 `d` 改为标准 SVG（`M/L/C/Z`，绝对坐标、参数按 SVG 顺序）⇒ 可直接逐字对拷进
   `Icons.axaml` 的 Geometry 字符串（fx-ctl 的防漂移用例可回到"逐字一致"）；
3. **自证（硬数字）**：逐子路径每段曲线取 64 点比较"展平前（按真实变换）vs 展平后（解析 `d`）"⇒
   最大偏差 **6.91e-07 单位**（ut 件 **5.69e-07**，阈值 0.01）；`d` 的非 SVG 命令数 = **0**（10 条 path 全过
   命令/参数个数校验）；展平前后**逐像素渲染差异 = 0 px**（`wordmark-normalized-selfproof.png`）；
   整体墨迹 bbox = **(0, 0) .. (1032.4137, 277.4707)**，与源 viewBox 偏差 **0.0000**；
4. **其它提取件审计**：`extracted/` 里其余 **93 个 `.svg` / 93 条 `d` 全部合法**（来自 HTML，本来就是
   SVG 语法；例：`v-chevron.svg` 的 `d = "M0 0 L4.5 14 L9 0"`）⇒ 无需规范化。

**管线顺序（重要）**：`_w43_type3_wordmark.py extract` → `_w43_split.py` → **`_w43_normalize_svg.py` 必须最后跑**
（前两者按 PDF 语法写文件；规范化后才是可交付形态）。



