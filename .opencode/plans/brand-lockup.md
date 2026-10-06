# UTvTU 横向商标（锁定）资产（W39）

> ## ⚠️ 历史文档（2026-10 补注，先读这段）
> **本文档属"派生时代"**（描摹 / 光学补偿口径）。**几何来源已改为 `.opencode/design/brand/extracted/**`**
> （用户设计稿的**提取**件 + 规范化形态；单一来源声明见 `extracted/EXTRACT-MANIFEST.md` 顶部）。
> 文中提到的 **`out/utvtu-brand-lockup.axaml` 仅存历史**：该文件已移入 `out/_obsolete/` 并在文件头标注废弃，
> **不得作为消费来源**（其 `brand-wordmark` 是旧版描摹字标，且含 `brand-brace-flipx` /
> `brand-v-chevron-stroke` / `brand-wordmark-monoline` 三条已不在产品键集的键）。
> 本文的**量测口径、对比结论与实测数字仍可查阅**（作为历史与对比），但**新的接线/取几何请一律取
> `extracted/`**：`wordmark-ut.svg`（U/T 四段）、`wordmark-v-stroke.svg`（切角 v 描边）、
> `v-chevron.svg`、`brace-open.svg`、`mark*.svg`。

> 作者：fx-rack · 基线 `plus-develop` @ `2c3e5583` · **本卡只出资产与清单，不接主树**
> 资产：`.opencode/design/brand/out/`（+ 对比图副本 `.dsh/fx/shots/`）· 生成器：`_lockup.py`（可重跑，复用 `_generate.py`）
> 对比图：`brand-lockup-sheet.png`（3 锁定 × 3 finish）· `brand-lockup-ladder.png`（尺寸阶梯）· `brand-wordmark-compare.png`（轮廓 vs 单线）


---

## A. 字标转矢量轮廓（关键工程点）

**结论：走"描摹品牌板真实轮廓"，不换字体、不重排。**

- 规范是 Roboto Bold −0.026em + 几何切角 v，但**仓库与系统都没有 Roboto 字体文件**（实测 `C:\Windows\Fonts` 无、`OpenUtau/Assets/Fonts` 只有 HarmonyOS Sans）⇒ "用字体导出轮廓"这条路在本机不可行，"改成 HarmonyOS Sans 排一遍"会**改变字形**（违背用户设计）。
- 采用：从品牌板 `sections/02-wordmark.png` 的**字标高分辨率渲染图**上量测 + **Moore 边界追踪 + RDP 简化**（闭合轮廓先从最远点切开，避开退化弦），得到与用户设计**逐像素一致**的矢量轮廓 ✔ 这正是"转轮廓"的本义，且不依赖任何字体文件。
- **归一化空间（供锁定对齐）**：**大写高（cap）= 100 单位**，**基线 y = 100**，左缘 x = 0，字标总宽 **377.33 单位**（advance）。
  - 实测字干宽 **20.66 单位**（= cap 的 20.7%，Roboto Bold 的典型字干）；源图 cap = 547px、墨迹宽 2064px。
  - 逐字包围盒（归一化 x1,y1,x2,y2）：U `0,0,76.6,100` · T `81.72,0,163.07,100` · **v `158.14,25.78,213.53,99.82`**（x-height 25.78 → 74.2 单位，几何切角，与标志的嘴同语言 ✔）· T `214.08,0,295.61,100` · U `300.73,0,377.33,100`
  - ⚠ 注意 v 的 bbox 与左右 T 有 5 单位的**视觉重叠**（切角张开的缘故），不是错误。
- 产出：`utvtu-wordmark.svg` / `-mono.svg`（`currentColor`）/ `utvtu-brand-lockup.axaml` 里的 **`brand-wordmark` StreamGeometry**（**已废弃**：见文首说明，改取 `extracted/`；`Fill` 填充；`Width = cap×3.7733`、`Height = cap`、`Stretch=Uniform`）。

### A′. 轮廓 vs 自绘单线（对比结论，见 `brand-wordmark-compare.png` 与实测）
| cap | 描摹轮廓（用户版） | 自绘单线（我的备选） |
|---|---|---|
| 14px | **3 域**（字间粘连）· 笔宽 3px · 墨迹 26.9% | 5 域 · 笔宽 2px · 17.6% |
| 16px | **4 域** · 4px · 28.6% | 5 域 · 2px · 17.0% |
| 20px | **5 域 ✔** · 4px · 30.9% | 5 域 · 3px · 20.4% |
| 28px | **5 域 ✔** · 6px · 35.0% | 5 域 · 4px · 21.8% |

**判定：采用描摹轮廓（用户版），不改单线。** 理由：① 单线版虽然 14px 就不粘连，但**丢掉了 Roboto 的字形语言**（等于换设计）；② 轮廓版在 **cap ≥20px 即 5/5 全分离**，而 UI 里字标的最小用法本就是 20px（见 §C）；③ 单线版笔宽 2px 在 14/16px 会"发虚"，且与标志等宽线语言重复，反而弱化"标志 = 脸、字标 = 名字"的分工。单线资产仍保留在 `utvtu-wordmark-monoline.svg`（备选/实验用，不进接入清单）。

---

## B. 三种锁定 × 三 finish

对齐规则（规范化）：**标志墨迹高 = cap**；**括号墨迹 = 1.2 × cap**；所有部件**居中对齐于大写带**（括号/标志以 cap 带垂直居中，字标基线 = cap 基线）；部件间距 **0.42 × cap**。

| 锁定 | 组成 | 用途 | SVG / XAML / PNG |
|---|---|---|---|
| **signature** | `{ 脸 UTvTU }` | 主签名（关于页、安装包、物料页脚） | `utvtu-lockup-signature-{ink,reversed,branded}.svg` + `@1x/@2x.png` |
| **display** | `{ UTvTU }` | 显示级（欢迎页头、闪屏副标） | `utvtu-lockup-display-*.svg` + PNG |
| **short** | `{ 脸 }` | 短式（顶栏、状态栏、小空间） | `utvtu-lockup-short-*.svg` + PNG |

**finish**（每式各一套，共 9 SVG + 18 PNG）：
- `ink`：全墨色（`#1C1B22`）on 浅底 `#FCF8FF`
- `reversed`：全反白 on `#0D0B14`（Ink 底）
- `branded`：**括号品牌紫 `#5A44E0`**，标志与字标仍为墨色 —— 用户"小巧思"的落地形态

PNG 规格：`@1x` = cap 24px、`@2x` = cap 48px（按需放大时建议直接用 SVG/StreamGeometry）。

---

## C. 应用内变体 + 尺寸阶梯 + 最小尺寸规则

**应用内 vs 品牌物料（两套取色，几何完全相同）**
| 场景 | 括号 | 标志 / 字标 | 说明 |
|---|---|---|---|
| **品牌物料**（图标 / 闪屏 / 安装包 / 关于页 / 印刷） | **固定品牌紫 `brand.primary`**（浅 `#5A44E0` / 深 `#C4B4FF`） | `brand.ink` / 反白 | 品牌一致性，不随用户主题变化 |
| **应用内**（欢迎页头 / 顶栏 / 面板内品牌位） | **`md3.primary`**（跟随用户色池） | **`md3.on-surface`** | 锁定永远与主题协调；用户换色时括号跟着变，字标保持可读 |

**尺寸阶梯（cap = 字标大写高）与实测可辨性**
| 位置 | cap | 依据 |
|---|---|---|
| 顶栏 | **14–16px** | 实测 cap 14 = 3 域、16 = 4 域（字标开始粘连）⇒ **该档只用 `{ 脸 }` 短式或纯脸**，不用带字标的锁定 |
| 欢迎页头 | **20px** | cap 20 = **5 域全分离** ✔ ⇒ `{ 脸 UTvTU }` 或 `{ UTvTU }` 的最小可用档 |
| 关于页 | **28px** | cap 28 = 5 域、笔宽 6px ✔ 舒适档 |
| 物料 / 安装包 | **≥44px** | 品牌板展示档（本次 sheet 即 cap 44） |

**最小尺寸规则（三档降级，写成可判定规则）**
1. **cap ≥ 20px**：可用完整锁定（`{ 脸 UTvTU }` / `{ UTvTU }`）。
2. **14 ≤ cap < 20px**：**先去括号**（括号 1.2×cap 在 17px 以下最先糊），再**去掉字标**只留脸 + 可选字标在旁（字标 cap<20 实测 3–4 域）。
3. **cap < 14px**：**只留标志（脸）**；≤ 16px 时标志本身也已到"尽力而为"档（见 `brand-assets.md` §3：最小可用 24px / 16px 尽力而为）。

---

## D. 令牌与接入清单

**`brand.*` 令牌（浅/深两套，建议片段，写入 `Colors/**`；本轮未改主树）**：与 `brand-assets.md` §4.2 同一套（`brand.primary` / `on-primary` / `primary-container` / `on-primary-container` / `accent` / `ink`）。策略再述：**标志与小字标固定品牌紫/墨色；界面强调色跟随用户色池（`md3.*`）；teal 只用于图案/印刷（浅底对比度 2.85:1 不达标）**。

**接入清单**
| 位置 | finish | cap | XAML 片段要点 |
|---|---|---|---|
| 欢迎页头 | 应用内（括号 `md3.primary` + 字标 `md3.on-surface`） | 20 | `<Path Data="{StaticResource brand-mark}" Stroke="{DynamicResource md3.primary}" StrokeThickness="3" StrokeLineCap="Round" StrokeJoin="Round"/><Path Data="{StaticResource brand-brace}" .../><Path Data="{StaticResource brand-wordmark}" Fill="{DynamicResource md3.on-surface}"/>` |
| 顶栏 | 应用内 | 14–16 | 只用 `brand-mark`（可选 `brand-brace`），**不带字标** |
| 闪屏 | 品牌物料（固定紫） | ≥44 | 括号 `brand.primary` + 字标 `brand.ink`；与 `utvtu-splash@1x/@2x.png` 同构 |
| 关于页 | 品牌物料 | 28 | `{ 脸 UTvTU }` signature，ink 或 branded |
| 安装包 / 文件关联 / 图标 | 品牌物料 | 图标内按 `brand-assets.md` §2 阶梯 | 括号不进图标；`utvtu.ico` + 关联图沿用 |
| 文档 / 印刷物料页脚 | 品牌物料（branded） | ≥44 | 括号紫 + 墨色字标（用户小巧思的标准形态） |

**实现注意**：① 字标是**填充几何**（不是文本），别退化成 `<TextBlock>`（会依赖字体、失去切角 v）；② 括号 `}` 用 `ScaleTransform ScaleX="-1"`，不手绘；③ 各部件用**同一个 cap 值**驱动（cap → 标志墨迹高、括号 1.2×cap、字标 Height=cap），避免逐处硬编码；④ `ui-lint` 禁止 Views/Controls 写死色 ⇒ 品牌位必须走 `brand.*` / `md3.*` 令牌。

---

## 附 · 交付物与数据
- SVG：`utvtu-wordmark.svg`、`utvtu-wordmark-mono.svg`、`utvtu-wordmark-monoline.svg`、`utvtu-lockup-{signature,display,short}-{ink,reversed,branded}.svg`（9）
- XAML：`utvtu-brand-lockup.axaml`（`brand-wordmark` / `brand-wordmark-monoline` StreamGeometry + 用法注释）—— **已废弃**：文件已移入 `out/_obsolete/`，XAML 几何请从 `extracted/` 现取；标志/括号几何见 `utvtu-brand-geometry.axaml`
- PNG：9 锁定 × `@1x/@2x` = 18；对比图 3 张（sheet / ladder / compare）
- 数据：`brand-lockup-metrics.json`（字标归一化几何、逐 cap 轮廓/单线度量）
- 过程 bug（已修，记录）：闭合轮廓 RDP 需从最远点切开（否则退化弦把整条压成 2 点）；组合图曾在 8× 画布上用 1× 坐标（内容缩到角落）。


---

## W39d 收尾（源几何重测）

- **切角 v 已换源路径**（填充 M0 0 L4.5 14 L9 0／描边 M1.54 1.54 L4.5 12.46 L7.46 1.54）。
- **字距微调复测**（把 v 两侧 T 向外移 0/1/2/3 单位，cap=100 空间）：实测 **delta=0 最优**（cap20=4 域、cap28=4 域），外移反而让 T 与两端 U 相撞 ⇒ **不改字距，保持原字标**。
- **阶梯修正（按源 v 实测）**：cap 14=**2 域** · 16=**3 域** · 20=**4 域** · 28=**4 域** ⇒ 14–19 去括号、<14 只留标志**维持**；但**"cap ≥20 = 完整锁定"需下调为 cap ≥24**（20 档实测仅 4/5，一对相邻字符相接、可读但未全分离），**24 档待补测**。
- **叠图说明（重要）**：几何已是**源路径 verbatim**，叠图只是**健全性检查**，**不是"逼近栅格图"**：rand-verify-brace-overlay.png IoU 0.406、rand-verify-mark-overlay.png 0.533，残差主要来自品牌板原图的**关节圆角与抗锯齿**以及 bbox 对齐方式（PNG 可放大逐节点看）。新增 rand-verify-v-and-wordmark-8x.png（源 v 填充/描边 + 字标 8×）。
- **仍缺**：三式锁定的 8× 叠图（需要从品牌板锁定区块取参考裁剪并对齐，属补充健全性检查）。


## W39e 补测：cap 24 与"相接"归因（最终阶梯）

**实测（源几何 + 源切角 v，cap = 字标大写高）**

| cap | 域数 | 最小间距 | 笔宽 | U↔T 缝隙 | v↔T |
|---|---|---|---|---|---|
| 14 | 2 | 8.0px | 3px | 0.72px | bbox 重叠 |
| 16 | 3 | 2.0px | 3px | 0.82px | bbox 重叠 |
| 20 | 4 | 2.0px | 4px | 1.02px | bbox 重叠 |
| **24** | **3** | 2.0px | **5px** | **1.23px** | bbox 重叠 |
| 28 | 4 | 2.0px | 6px | 1.43px | bbox 重叠 |
| 48 | 4 | 3.0px | 10px | 2.46px | bbox 重叠 |

**归因（重要）**：**"相接"的一对永远是 v↔T**，且**源切角 v 的 bbox 与两侧 T 的 bbox 本就有约 5 单位重叠**（切角张开、顶部更宽）——**这是字标设计的固有紧排特征，从 cap 14 到 cap 48 全程存在**，不是尺寸缺陷。⇒ **"域数/间距"这套判据是给标志的 5 字形定的，不适用于字标**；字标只按"可读性"（笔宽与 U↔T 缝隙）分档。域数在 20→24 还会非单调（4→3，抗锯齿偶然相接），更不能当阈值依据。

**最终阶梯（四档，按可读性）**

| cap | 用法 | 依据 |
|---|---|---|
| **≥28px** | 完整锁定（舒适档） | 28px：笔宽 6px、U↔T 缝 1.43px；48px：笔宽 10px、缝 2.46px |
| **24–27px** | 完整锁定（**紧排**，可选去括号） | 24px：笔宽 5px、缝 1.23px（可读） |
| **14–23px** | **去括号**（{ 脸 UTvTU } → 脸 UTvTU 或 { 脸 }） | 20px：笔宽 4px、缝 1.02px；16px：3px/0.82px |
| **<14px** | **只留标志** | 14px：笔宽 3px、缝 0.72px（字标已不可读） |

**观感判断（我看过 8× 与真实像素图）**：24–28 档的 v↔T 相接**可接受**——观感是"切角 v 顶住两侧 T 的竖干"，正是品牌板里那个紧凑的 UTvTU 样子；相比之下 20 档以下的 U↔T 缝隙掉到 ~1px 才是真正的可读性拐点（字形开始糊在一起）。因此**阈值定在 24（完整）/14（去字标）**，且明确写入"v↔T 重叠属固有特征、非遗漏"。
