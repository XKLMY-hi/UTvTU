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


## D 类 · 光栅提取（裁图，非描摹）—— 用户裁决：字标不再追矢量

| 文件 | 内容 | 来源与裁切 | 倍率/cap | 备注 |
|---|---|---|---|---|
| `wordmark-alpha@1x.png` | 字标 alpha 蒙版（RGB=0,0,0 / A=墨迹不透明度） | `sections/02-wordmark.png`，crop box **(x140, y269, x2224, y1676)**（贴紧墨迹，左右上下 0 留白） | cap **100px**（[148, 100]） | alpha 由"离底暗度"线性映射，**保留抗锯齿半透明**（半透明像素 0 个），未锐化、未重绘 |
| `wordmark-alpha@2x.png` | 同上 | 同上 | cap **200px**（[296, 200]） | LANCZOS 重采样 |
| `wordmark-alpha@4x.png` | 同上 | 同上 | cap **400px**（[592, 400]） | 源 cap = **1407px**，故 4x 仍为降采样、无插值伪影 |
| `_selftest-wordmark-{ink,white,brand}.png` | 取色自证（深墨/反白/品牌紫） | 由 @1x 蒙版染色 | — | 证明同一蒙版可用于浅底/深底/品牌色 |

**边缘自证**：四角 alpha = **[0, 0, 0, 0]**（全 0 ⇒ 无背景残留）；外框 1px 最大 alpha = **0**。
