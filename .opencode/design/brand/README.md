# UTvTU — 品牌视觉识别 (Brand Identity)

导出时间：导出自 pen.dev 画板 `UTvTU Brand System`

---

## 名字的由来（设计的核心）

**UTvTU = UTAU + TvT（哭泣颜文字）**

整个标志就是把名字画成一张脸，同时读作 UTAU：

| 字形 | 角色 |
| --- | --- |
| **T** ×2 | 哭泣的**眼睛**——横杠是眼，竖干是**泪流** |
| **v** | 颜文字的**嘴巴** |
| **U** ×2 | 从眼角落下的**泪滴** |

`{}` 把这张脸括起来，既是颜文字的括号，也像代码块 —— 合成器 / 编程工具的语言。

---

## 核心组件

### 1. 标志「Mark / Signal」
- 几何单线（monoline），**64 单位网格**，居中于画布
- 规则：**描边 = 尺寸 ÷ 8**（12.5% 笔宽，任何尺寸下视觉重量一致）
- 5 个字形在 32 px 下仍互不粘连（已做像素级连通域校验）

### 2. 括号「Brace / { }」
- 墨迹 24 × 44 装进 28 × 48 的框，**描边 = 高度 ÷ 6**（与 Roboto Bold 字干重量匹配）
- **`}` = 同一节点 `flipX` 镜像**，不要手绘镜像副本

### 3. 字标（Wordmark）
- Roboto Bold，字距 −0.026 em
- 小写 **v** 不是字体字形，而是几何切角（chevron），**精确坐落在大写线与基线上**，与标志的嘴同一个模块语言

### 4. 锁定（Lockup）规则
- 标志墨迹高度 = 字标大写高度
- 括号墨迹 ≈ 1.2 × 大写高度
- 行内用 `alignItems: center`，标志 / 括号都对齐字标的大写带

---

## 色彩令牌（MD3，含浅/深色主题）

| 令牌 | 浅色 | 深色 | 用途 |
| --- | --- | --- | --- |
| `$brand-primary` | `#5A44E0` | `#C4B4FF` | 标志、括号、强调 |
| `$brand-on-primary` | `#FFFFFF` | `#24005E` | 主色底上的墨色 |
| `$brand-primary-container` | `#E5DEFF` | `#4029C6` | 色调锁定底 |
| `$brand-on-primary-container` | `#1B0B5E` | `#E5DEFF` | 色调底上的墨色 |
| `$brand-accent` | `#00A892` | `#3FDCC4` | 图案带点缀 |
| `$brand-surface` | `#FCF8FF` | `#131218` | 页面底 |
| `$brand-surface-container` | `#F3EEFB` | `#1F1D26` | 卡片 / 区块底 |
| `$brand-surface-variant` | `#E7E0F0` | `#2B2933` | 次表面 |
| `$brand-on-surface` | `#1C1B22` | `#E7E1E9` | 正文 / 字标 |
| `$brand-on-surface-variant` | `#48454F` | `#CAC4D4` | 次要文字 |
| `$brand-outline` / `-variant` | `#79747E` / `#CAC4D4` | `#948F99` / `#49454F` | 描边、辅助线 |
| `$brand-ink` | `#0D0B14` | — | 反白底 |

字体：`$font-display` / `$font-body` = Roboto，`$font-mono` = Roboto Mono

---

## 文件清单

```
utvtu-brand-sheet.png        整张品牌板（2528 × 2955 @1.5x）
utvtu-brand-sheet.pdf        整张品牌板 + 组件页（多页矢量）
utvtu-brand-tailwind.html    品牌板 → HTML（Tailwind CDN 版，需联网加载字体）
utvtu-lockup-css.html        锁定表 → HTML（纯 CSS 版）

sections/
  00-brand-components.png    可复用组件源（标志、括号）
  01-app-icon.png            App 图标系统：主图标、自适应分层、尺寸阶梯 128→16
  02-wordmark.png            字标：主字标、技术全大写版、堆叠版、色调检查
  03-mark-variations.png     标志构造图、7 种色彩角色、清晰度条、图案带
  04-lockup-and-construction.png  主锁定、反白/色调/单色、留白、堆叠、商标规范
  05-braced-lockup.png       括号锁定：{ UTvTU }、{ 脸 UTvTU }、{ 脸 }、构造规范、尺寸阶梯
```

---

## 使用规范

1. **禁止**重绘、旋转、描边化、拉伸标志与括号。
2. 标志只能整体缩放，描边按 `尺寸 ÷ 8` 同步；括号按 `高度 ÷ 6`。
3. 反白 / 色调 / 单色锁定为**单色锁定** —— 标志、括号、字标用同一墨色。
4. 留白 = 标志宽度的 1/2（见留白规范块）。
5. 商标声明 `™` 落在大写线上，不得下移到基线。
6. 小尺寸（≤ 24 px）只使用**无括号的纯脸**；`{}` 属于锁定 / 签名层面的组合。
