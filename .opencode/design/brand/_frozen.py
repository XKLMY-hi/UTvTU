# [已废弃 / DERIVED-OBSOLETE] 本脚本属`派生时代`（光学补偿 / 描摹重建）的产物：它生成的 out/** 资产
# （含 utvtu-brand-lockup.axaml = 旧版描摹字标）**已废弃，不得作为消费来源**；重跑只为复现历史。
# 唯一权威来源 = .opencode/design/brand/extracted/**（提取 + 规范化；见其 EXTRACT-MANIFEST.md 顶部声明）。
"""W39f 产出冻结版单文件几何 axaml + manifest。"""
import io, json, math, os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import _generate as G, _lockup as L, _source as S

OUT = L.OUT


def mirror_d(d):
    """把括号路径在 x 上镜像：SVG 用 translate(24,0) scale(-1,1) ⇒ x' = 24 - x。程序化镜像，不手绘。"""
    import re
    def rep(m):
        x = float(m.group(1))
        return f"{24 - x:g}"
    return re.sub(r"(?<![\d.])(-?\d+(?:\.\d+)?)(?=\s)", lambda m: rep(m), d)


def main():
    wm = L.wm_path_d(S.WM, prec=2)
    head = """<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <!-- ════════════════════════════════════════════════════════════════════════════════
       UTvTU 品牌几何 · 冻结版（W39f）—— 一个文件给全，**实现者只接这一个文件**

       ① 几何来源（**权威源，verbatim**）：`.opencode/design/brand/utvtu-brand-tailwind.html`
          · 标志 viewBox="0 0 64 64"     ← M8 13 H24 M16 13 V29 M40 13 H56 M48 13 V29
                                          M27 25 L32 35 L37 25
                                          M9 41 V44 Q9 51 16 51 Q23 51 23 44 V41
                                          M55 41 V44 Q55 51 48 51 Q41 51 41 44 V41
          · 括号 viewBox="-2 -2 28 48"  ← M20 4 H14 Q10 4 10 8 V18 Q10 22 4 22 Q10 22 10 26 V36 Q10 40 14 40 H20
          · 切角 v 填充 viewBox="0 0 9 14" ← M0 0 L4.5 14 L9 0 ／ 描边 ← M1.54 1.54 L4.5 12.46 L7.46 1.54
          · 字标 brand-wordmark = **描摹的 U/T 轮廓 + 源切角 v**（仓库/系统无 Roboto，字体导出不可行；
            字距 delta=0，W39d 实测最优）

       ② 铁律：**禁止从 PNG 描摹**（拿不到 HTML 才退化并标注来源）；**禁止"描边→填充"式转换**
          （品牌括号就是被它弄坏的：偏移 ±笔画/2 后脊区与尖区重叠、尖被吞）。

       ③ **XAML 一律描边渲染**：Path + Stroke + StrokeThickness + StrokeLineCap/Join=Round。
          · 标志：StrokeThickness = 显示尺寸 ÷ 8（64 网格内为 8）
          · 括号：StrokeThickness = 括号墨迹高 ÷ 6（本路径下为 6）
          · 切角 v：小尺寸用 brand-v-chevron（填充），大尺寸可用 brand-v-chevron-stroke（描边，厚 2.2@9×14）
          · 字标：**Fill**（轮廓几何，不是描边）

       ④ 尺寸阶梯（源几何实测）：≥28px 完整锁定（舒适）· 24–27px 完整但紧排 · 14–23px 去括号 · <14px 只留标志
          （标志自身：÷8 原版 ≥32px 5/5 域；16–24px 用光学补偿档 = 笔宽 6 单位 ≈ 尺寸÷10.7，16px 起 5/5 域）

       ⑤ 取色两套：**品牌物料**括号固定 `brand.primary`（浅 #5A44E0／深 #C4B4FF）、标志/字标 `brand.ink` 或反白；
          **应用内**括号 `md3.primary`、字标 `md3.on-surface`（跟随用户色池）。teal `brand.accent` 只进图案/印刷。
       ════════════════════════════════════════════════════════════════════════════════ -->

  <!-- 标志（脸）：viewBox 0 0 64 64，描边 8（= 尺寸÷8），StrokeLineCap/Join=Round -->
  <StreamGeometry x:Key="brand-mark">{S.MARK_D}</StreamGeometry>

  <!-- 括号 {{ ：viewBox -2 -2 28 48，描边 6（= 墨迹高÷6） -->
  <StreamGeometry x:Key="brand-brace">{S.BRACE_D}</StreamGeometry>
  <!-- 括号 }} ：同一几何**程序化镜像**（x' = 24 − x，等价 SVG translate(24,0) scale(-1,1)）—— 不要手绘 -->
  <StreamGeometry x:Key="brand-brace-flipx">{mirror_d(S.BRACE_D)}</StreamGeometry>

  <!-- 切角 v（源路径 verbatim）：填充版 viewBox 0 0 9 14 -->
  <StreamGeometry x:Key="brand-v-chevron">{S.V_FILL_D}</StreamGeometry>
  <!-- 切角 v 描边版（同一 viewBox），StrokeThickness = 2.2 × (字标 x 高 / 14) -->
  <StreamGeometry x:Key="brand-v-chevron-stroke">{S.V_STROKE_D}</StreamGeometry>

  <!-- 字标 UTvTU = 描摹 U/T 轮廓 + 源切角 v。归一化：**大写高 cap = 100 单位、基线 y = 100、左缘 x = 0**，
       总宽 {(max(p[0] for l in S.WM for p in l["pts"])):.2f} 单位。用 Fill 填充：
         <Path Data="{{StaticResource brand-wordmark}}" Fill="{{DynamicResource md3.on-surface}}"
               Width="{{cap * {(max(p[0] for l in S.WM for p in l['pts'])) / 100:.4f}}}" Height="{{cap}}" Stretch="Uniform"/> -->
  <StreamGeometry x:Key="brand-wordmark">{wm}</StreamGeometry>

  <!-- 单线备选字标（实验用，不进接入清单）：StrokeThickness = cap × 0.125，圆头圆接 -->
  <StreamGeometry x:Key="brand-wordmark-monoline">{L.wm_path_d(L.MONO and []) if False else ""}</StreamGeometry>

  <!-- ══ 三式锁定（组合放置：cap 为唯一驱动值，全部按 cap=100 给出坐标，实际缩放 = cap/100）══
       对齐规则：标志墨迹高 = cap；括号墨迹高 = 1.2 × cap；所有部件**居中对齐大写带**（字标基线 = cap 基线）；
       部件间距 = 0.42 × cap（单位空间里即 42）。

       signature = { 脸 UTvTU }（主签名：关于页/安装包/物料页脚）
       <Canvas Width="{{W}}" Height="{{132}}" ClipToBounds="False">
         <Path Data="{{StaticResource brand-brace}}" Stroke="{{DynamicResource brand.primary}}"
               StrokeThickness="6" StrokeLineCap="Round" StrokeJoin="Round"
               Width="22" Height="46.8" Stretch="Uniform"
               Canvas.Left="0" Canvas.Top="37.6"
               RenderTransform="translate(...)"/>  ... 见 brand-lockup.md §B（数值表）
         <Path Data="{{StaticResource brand-mark}}" ... Width="56" Height="100" Stretch="Uniform" Canvas.Left="58" Canvas.Top="11"/>
         <Path Data="{{StaticResource brand-wordmark}}" Fill="..." Width="377.33" Height="100" Stretch="Uniform" Canvas.Left="156" Canvas.Top="11"/>
         <Path Data="{{StaticResource brand-brace-flipx}}" ... Canvas.Left="575.3" Canvas.Top="37.6"/>
       </Canvas>
       display   = { UTvTU }（显示级：欢迎页头/闪屏副标）→ 同上，去掉 brand-mark 一项
       short     = { 脸 }（短式：顶栏/状态栏）→ 同上，去掉 brand-wordmark 一项
       （精确左偏移请由实现按 cap 驱动计算：brace_w = 22×(cap/42)、mark_w = 56×(cap/45)、word_w = 3.7733×cap、
         间距 = 0.42×cap；这样任意 cap 都不会错位。）
       ═════════════════════════════════════════════════════════════════════════════════ -->
</ResourceDictionary>
"""

    head = (head
            .replace("{S.MARK_D}", S.MARK_D)
            .replace("{S.BRACE_D}", S.BRACE_D)
            .replace("{mirror_d(S.BRACE_D)}", mirror_d(S.BRACE_D))
            .replace("{S.V_FILL_D}", S.V_FILL_D)
            .replace("{S.V_STROKE_D}", S.V_STROKE_D)
            .replace("{wm}", wm)
            .replace('{(max(p[0] for l in S.WM for p in l["pts"])):.2f}',
                     "%.2f" % max(p[0] for l in S.WM for p in l["pts"]))
            .replace('{(max(p[0] for l in S.WM for p in l["pts"])) / 100:.4f}',
                     "%.4f" % (max(p[0] for l in S.WM for p in l["pts"]) / 100))
            .replace('{L.wm_path_d(L.MONO and []) if False else ""}', "")
            .replace("{{", "{").replace("}}", "}"))
    io.open(os.path.join(OUT, "utvtu-brand-lockup.axaml"), "w", encoding="utf-8").write(head)  # 已废弃：只为复现历史

    manifest = f"""# UTvTU 品牌资产 · 冻结清单（W39f）

> **几何已冻结**（源路径 verbatim）。若几何有变，**消费方必须重取**对应文件；本轮之后我只在你要求时才动。

## 冻结文件

| 文件 | 内容 | 消费方 |
|---|---|---|
| `out/_obsolete/utvtu-brand-lockup.axaml`（**已废弃，不得消费**） | 旧版描摹字标：`brand-mark` / `brand-brace` / `brand-brace-flipx`（程序化镜像）/ `brand-v-chevron` / `brand-v-chevron-stroke` / `brand-wordmark`（描摹 U/T + 源 v）+ 三式锁定放置说明 | fx-ui（启动窗/欢迎页/关于页/顶栏）、实现者 |
| `out/utvtu-brand-geometry.axaml` | 同源几何（含 `brand-brace` 源路径版）；**与上表内容重叠**，任选其一即可（推荐 lockup 那份） | fx-ui |
| `out/utvtu.ico` | 多尺寸 16/24/32/48/64/128/256，**逐尺寸渲染**（源几何，描边语义） | fx-ctl（窗口/任务栏/文件关联） |
| `out/utvtu-icon-16.png` `/24` `/32` | 逐尺寸瓦片（源几何） | fx-ctl |
| `out/utvtu-splash@1x.png` `/@2x.png` | 480×320 / 960×640 闪屏（源几何 `{{ 脸 }}` + 字标轮廓） | fx-ctl（启动窗） |
| `out/brand-icon-ladder.png` | 图标阶梯 256→16 × primary/ink/tonal | 设计/评审 |
| `out/utvtu-lockup-*.svg`（9） | 3 式 × 3 finish（ink/reversed/branded） | 物料/文档/网页 |
| `out/utvtu-lockup-*@1x/@2x.png`（18） | 同上栅格（cap 24/48） | 物料/文档 |
| `out/utvtu-mark-source.svg` / `utvtu-brace-source.svg` / `-source-flipx.svg` / `utvtu-v-chevron-{{fill,stroke}}.svg` | 源几何 SVG（描边语义） | 设计/网页 |
| `out/utvtu-wordmark.svg` / `-mono.svg` | 字标轮廓 SVG（Fill） | 物料 |
| `out/brand-lockup-sheet.png` / `brand-lockup-ladder.png` / `brand-brace-new-vs-old.png` | 评审图 | 用户/评审 |
| `out/brand-verify-*.png` | 健全性叠图（**不是逼近栅格**） | 评审 |
| `out/brand-source-metrics.json` / `brand-source-raster-metrics.json` / `brand-wordmark-spacing.json` / `brand-wordmark-cap24.json` | 全部实测数值（源几何） | 评审/实现 |

## 已作废（不要引用）
`out/utvtu-mark.svg`、`utvtu-mark-*.svg`（描摹+描边转填充系列）、`utvtu-brace.svg`（旧描摹版）、
`brand-metrics.json`（在错误几何上测的）、`brand-optical-compensation-*.png`（错误几何上的补偿）、
`brand-variants-*.png`（早期简化变体）—— 保留仅为历史记录。

## 变更流程
1. ~~任何几何改动 ⇒ 我重出 `utvtu-brand-lockup.axaml`~~ **已废弃**：几何只在 `extracted/` 侧变动，消费方一律取 `extracted/**`；
2. 仅文案/阶梯改动（如 cap 阈值）⇒ 不动文件，无需重取；
3. 消费方若发现文件时间戳早于 `utvtu-brand-tailwind.html` 的几何口径，以本清单为准来找我。
"""
    io.open(os.path.join(OUT, "brand-frozen-manifest.md"), "w", encoding="utf-8").write(manifest)
    print("wrote:", os.path.join(OUT, "utvtu-brand-lockup.axaml"))  # 已废弃
    print("wrote:", os.path.join(OUT, "brand-frozen-manifest.md"))


main()
