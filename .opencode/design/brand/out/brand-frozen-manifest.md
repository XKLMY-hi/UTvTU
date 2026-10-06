# UTvTU 品牌资产 · 冻结清单（W39f）—— ⚠️ **本目录为派生研究，已废弃**

> ## ⚠️ 单一来源声明（2026-10 更新，以此为准）
> **产品几何的唯一权威来源 = `.opencode/design/brand/extracted/**`**（用户设计稿的**提取**件，
> 规范化后的形态；见 `extracted/EXTRACT-MANIFEST.md`）。
> **本目录（`out/`）全部为派生研究记录**（光学补偿 / 描摹重建 / 早期变体），**仅供查阅历史与对比**，
> **不得作为消费来源**。原先的"几何已冻结、消费方必须重取本目录"口径**作废**。
>
> 已确认产品侧（`OpenUtau/**`）**零引用**本目录：`Icons.axaml` / `SplashWindow.axaml` 等一律引用
> `extracted/**`。历史上把本目录当权威的就是下面第 9 行那条 ⇒ 已改写。

## 已废弃（不得消费）

| 文件 | 原描述 | 现状 |
|---|---|---|
| `_obsolete/utvtu-brand-lockup.axaml`（原 `utvtu-brand-lockup.axaml`） | "单文件几何全集"，指给 fx-ui（启动窗/欢迎页/关于页/顶栏）与实现者 | **已移入 `_obsolete/` 并在文件头标记废弃**：`brand-wordmark` 是旧版描摹字标（1418 字符），不是产品定稿 |
| 其内 `brand-brace-flipx` | 程序化镜像键 | **不进接入**（产品侧用 `brand-brace` + `ScaleX(-1)`，不留预翻转派生键） |
| 其内 `brand-v-chevron-stroke` | 切角 v 的描边版（预烘焙坐标） | **不进接入**（产品侧 `brand-v-chevron` = 源 `M0 0 L4.5 14 L9 0`，由消费方单独描边渲染） |
| 其内 `brand-wordmark-monoline` | 单线字标（空值） | **不进接入**（产品侧 `brand-wordmark` = `extracted/wordmark-ut.svg` 的 4 段 `d` 逐字拼接） |
| `utvtu-brand-geometry.axaml` | "同源几何，与上表重叠，任选其一（推荐 lockup 那份）" | **同样不再作为来源**；如需 XAML 几何请从 `extracted/**` 现取 |

## 本目录其余内容：研究记录（一眼可辨，不属接入件）

- `brand-*.png`（光学补偿对比、图标阶梯、锁定阶梯、`brand-verify-*` 健全性叠图、`brand-brace-new-vs-old`）——
  评审/历史用图；
- `utvtu-lockup-*.svg` / `@1x` / `@2x`（3 式 × 3 finish）—— 物料/文档可用的**早期派生图**，与 `extracted/` 定稿
  不同源，若要正式物料请以 `extracted/**` 重出；
- `utvtu.ico` / `utvtu-icon-*` / `utvtu-splash@*` —— 早期栅格；
- `brand-*.json`（metrics / spacing / cap24 / source-metrics）—— 实测数值记录（注意：`brand-metrics.json`
  在错误几何上测的，`brand-wordmark-cap24.json` 只含小尺寸可读性指标、**不含**字标宽与 v 定位）。

## 变更流程（新口径）

1. **几何改动只在 `extracted/` 侧发生**（提取器 + 规范化脚本，见其 manifest 的管线顺序）；
2. 本目录**不再重出**任何资产，也不再"通知消费方重取本目录"；
3. 消费方若需要 XAML/几何数字，一律取 `extracted/wordmark-ut.svg`、`extracted/wordmark-v-stroke.svg`、
   `extracted/v-chevron.svg`、`extracted/brace-open.svg`、`extracted/mark*.svg`。
