# UTVTU — 设计交付包（给实现 Agent）

来源：`untitled.pen`（**UTVTU** 歌声合成工作台，**简体中文**界面，桌面端 1440×900）。
用途：让实现 agent 照着写真实代码。**PNG 是视觉基准，HTML 是结构与尺寸规格，tokens 是主题。**

---

## 1. 文件

```
exports/
├─ png/                        每屏 2× 截图（视觉基准）
│   ├─ 1-Welcome.png           欢迎
│   ├─ 2-Main-Window.png       主窗口（编曲工作台）
│   ├─ 3-Piano-Roll.png        钢琴卷帘
│   ├─ 4-Mixer.png             混音台
│   ├─ 5-VST-Plugin.png        VST 插件
│   └─ 6-Preferences.png       偏好设置
├─ html/
│   ├─ Welcome.html            ← 单屏 HTML（结构 + 尺寸规格，Tailwind 类）
│   ├─ Main-Window.html
│   ├─ Piano-Roll.html
│   ├─ Mixer.html
│   ├─ VST-Plugin.html
│   ├─ Preferences.html
│   ├─ UTVTU-all-tailwind.html  六屏合一（Tailwind）
│   └─ UTVTU-all-css.html       六屏合一（纯 CSS，无 CDN）
├─ tokens.css                  设计令牌（浅色/深色）
├─ tokens.json                 同上，JSON 版
└─ README.md                   本文件
```

---

## 2. HTML 怎么读（重点）

1. **结构地图 = `data-pencil-name`**
   每个元素都带 `data-pencil-name="图层名"`，例如
   `Top App Bar`、`Transport`、`Channel Strip · Teto`、`Insert 1 · EQ`。
   按它建立 DOM 树，而不必靠 class 猜结构。

2. **布局 = Tailwind 工具类，尺寸 = 1440×900 下的绝对像素规格**

   | 设计属性 | HTML / 实现 |
   | --- | --- |
   | `fill_container` | `w-full` / `h-full` / `flex-1` |
   | `fit_content` | `w-fit` / `h-fit` |
   | `layout: vertical` | `flex flex-col` |
   | `layout: horizontal` | `flex flex-row` |
   | `gap` / `padding` | `gap-[Npx]` / `p-[…]` / `px-[…]` |
   | `cornerRadius` | `rounded-[Npx]` |
   | 背景 / 文字色 | `bg-[#…]` / `text-[#…]`（**请换成令牌，见 §3**） |

3. **图标 = 内联 SVG**
   带 `data-icon-name`（如 `play`）与 `data-icon-set="lucide"`。
   可直接复用 path，或改用项目里的图标库。

4. **字体**
   `<link>` 引入 **Noto Sans SC**；元素上是 `font-['Noto_Sans_SC',system-ui,sans-serif]`。
   对应令牌 `--md3-font` / `--md3-font-display` / `--md3-font-mono`。

5. **`data-pencil-id`**（若存在）为图层唯一 id，可用于跨文件引用同一元素。

---

## 3. 设计令牌（tokens.css / tokens.json）

- `:root` = 浅色；`:root[data-theme="dark"], .dark` = 深色。
- 同时包含两套：**语义令牌**（`--background` `--foreground` `--primary` `--card` `--border` `--muted` …）与 **Material 3 令牌**（`--md3-surface` `--md3-on-surface` `--md3-primary` `--md3-space-*` `--md3-radius-*` `--md3-font*`）。
- HTML 里的颜色是**已解析的 hex**，请对照 `tokens.css` 换回令牌。常用对应：

  | hex | 令牌 |
  | --- | --- |
  | `#0E1513` | `--md3-surface` / `--md3-background` |
  | `#1A211F` | `--md3-surface-container` |
  | `#DDE4E1` | `--md3-on-surface` |
  | `#BEC9C5` | `--md3-on-surface-variant` |
  | `#6FDBCB` | `--md3-primary` |
  | `#005046` | `--md3-primary-container` |
  | `#3F4947` | `--md3-outline-variant` |
  | `#FF8400` | `--primary` |

---

## 4. 给实现 Agent 的推荐流程

1. 先看某一屏的 **PNG**（视觉目标）。
2. 读同屏 **HTML**，用 `data-pencil-name` 建结构树，抽出每个区块的尺寸/间距/圆角。
3. 把 **tokens.css** 合入项目主题（CSS 变量 / Tailwind theme / tokens 文件）。
4. 用**项目已有的框架与组件库**实现（React/Vue/…）：把重复区块抽成组件（轨道头、通道条、插入槽、参数行、设置行…）。
5. **不要把 1440×900 的绝对定位照搬**——它是规格，不是约束；按规格实现自适应布局。
6. **一屏一屏做**，每屏完成截图与 PNG 对照验收（对齐、间距、字号、颜色）。

---

## 5. 注意

- HTML 由设计工具自动导出，**重新导出会覆盖**：要改结构请改设计源 `untitled.pen`，而不是直接改 HTML。
- 导出所用 Tailwind 是 **CDN（v3 写法）**；若项目为 **Tailwind v4**，请改写为 v4 语法（`@import "tailwindcss";` 等）。
- 界面文案为**简体中文**；品牌名 `UTVTU` / `UTAU` / `DiffSinger`、技术缩写（`VST3` `ASIO` `MIDI` `EQ` `dB` `kHz`…）、音源名（`Kasane Teto`…）与轨道标识（`TETO_LEAD`…）按设计保留原文，不要翻译。
