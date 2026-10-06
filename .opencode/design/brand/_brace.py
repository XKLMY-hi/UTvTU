"""W39b 括号重建 + 设计稿校准 + 全套锁定资产重出（一次性脚本，可重跑）。

问题（用户实测）：旧括号把 `{` 挤在标志那张 64 网格里将就 —— 上臂全宽只有 10 单位（比笔画仅宽 1 单位）、
中尖只凸 2 单位、墨迹约 36×18（规范要求 24×44）⇒ 看着就是"一根竖条带个小疙瘩"。

规范：括号墨迹 24×44 装进 28×48 的框，描边 = 高度÷6（≈7.33），R = 描边/2；`}` = `{` 的 flipX。
本脚本：① 从 05-braced-lockup.png 量出设计稿的臂长/尖凸/笔画比；② 用**独立坐标系**重建括号；
③ 新旧数值并列（归一化到墨迹高=100）；④ 重出全部锁定资产 + 新旧对比图。
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _generate as G
import _lockup as L

BRAND = L.BRAND
OUT = L.OUT
SHOTS = L.SHOTS

# ══════════════════════════════════════════════════════════════════
# 1. 新括号几何（**独立 28×48 坐标系**，不再借用标志的 64 网格）
#    墨迹 24×44、笔画 = 44/6 ≈ 7.3333、R = 笔画/2 = 3.6667
# ══════════════════════════════════════════════════════════════════
FRAME_W, FRAME_H = 28.0, 48.0
INK_W, INK_H = 24.0, 44.0
SW = INK_H / 6.0                      # 7.3333
R = SW / 2.0                          # 3.6667
X_INK0, Y_INK0 = (FRAME_W - INK_W) / 2.0, (FRAME_H - INK_H) / 2.0   # 2, 2
X_TIP = X_INK0 + SW / 2.0             # 5.6667 上/下臂尖端（圆头中心）
X_SPINE = X_INK0 + INK_W - SW / 2.0   # 22.3333 竖干中心线（右侧）
Y_TOP = Y_INK0 + SW / 2.0             # 5.6667
Y_BOT = Y_INK0 + INK_H - SW / 2.0     # 42.3333
Y_MID = Y_INK0 + INK_H / 2.0          # 24
ARM_RUN = X_SPINE - X_TIP             # 16.6667 臂的横向长度
NUB_OUT = X_SPINE - X_TIP             # 16.6667 中尖外凸（相对竖干）


def new_brace():
    """`{`：上臂 → 上圆角 → 竖干 → 中尖（左凸，两侧 R 圆角）→ 竖干 → 下圆角 → 下臂。"""
    k = 6.0                            # 中尖斜段的高度半径
    return [
        # 上臂（水平，圆头）
        ("line", (X_TIP, Y_TOP), (X_SPINE - R, Y_TOP), SW),
        # 上圆角：水平 → 竖直
        ("arc", (X_SPINE - R, Y_TOP + R, R), 270, 360, SW),
        # 上竖干
        ("line", (X_SPINE, Y_TOP + R), (X_SPINE, Y_MID - k), SW),
        # 中尖：竖干 → 左凸尖点 → 竖干（斜段，两端 R 圆角）
        ("line", (X_SPINE, Y_MID - k), (X_TIP, Y_MID), SW),
        ("line", (X_TIP, Y_MID), (X_SPINE, Y_MID + k), SW),
        # 下竖干
        ("line", (X_SPINE, Y_MID + k), (X_SPINE, Y_BOT - R), SW),
        # 下圆角：竖直 → 水平
        ("arc", (X_SPINE - R, Y_BOT - R, R), 0, 90, SW),
        # 下臂
        ("line", (X_SPINE - R, Y_BOT), (X_TIP, Y_BOT), SW),
    ]


OLD_BRACE_BOX = 64.0          # 旧括号借用标志的 64 网格
OLD_BRACE = G.brace()

# ══════════════════════════════════════════════════════════════════
# 2. 括号度量（归一化到墨迹高 = 100）—— 设计稿与新旧几何用同一把尺
# ══════════════════════════════════════════════════════════════════
def brace_metrics(mask):
    h, w = len(mask), len(mask[0])
    ys = [y for y in range(h) if any(mask[y])]
    xs = [x for x in range(w) if any(mask[y][x] for y in range(h))]
    if not ys:
        return None
    y0, y1, x0, x1 = ys[0], ys[-1], xs[0], xs[-1]
    ink_h, ink_w = y1 - y0 + 1, x1 - x0 + 1
    stroke = G.median_stroke_px(mask)
    # 臂长：顶部带（上臂中线）的水平延伸
    yb = min(y1, y0 + max(1, int(stroke * 0.5)))
    row = [x for x in range(w) if mask[yb][x]]
    arm = (max(row) - min(row) + 1) if row else 0
    # 中尖外凸：中部带最左墨迹相对竖干（右侧）中心线的距离
    ymid = (y0 + y1) // 2
    band = range(max(y0, ymid - 2), min(y1, ymid + 3))
    mid_min_x = min((x for y in band for x in range(w) if mask[y][x]), default=x1)
    spine_cx = x1 - stroke / 2.0
    nub = spine_cx - mid_min_x
    k = 100.0 / ink_h
    return {
        "ink_wh_px": [ink_w, ink_h], "ink_w_over_h": round(ink_w / ink_h, 3),
        "stroke_px": stroke,
        "ink_h_norm": 100.0,
        "ink_w_norm": round(ink_w * k, 1),
        "stroke_norm": round(stroke * k, 1),          # 笔画 / 墨迹高 ×100
        "stroke_over_h": round(stroke / ink_h, 4),    # 规范要求 1/6 ≈ 0.1667
        "arm_norm": round(arm * k, 1),                # 上臂横向长度
        "arm_over_stroke": round(arm / stroke, 2),    # 臂长 / 笔画（>1 越多越像括号）
        "nub_norm": round(nub * k, 1),                # 中尖外凸（相对竖干）
        "nub_over_stroke": round(nub / stroke, 2),
        "nub_over_ink_w": round(nub / ink_w, 3),
    }


def metrics_of(glyphs, box_units):
    img = G.render(glyphs, 512, "#000000", box_units=box_units)
    return brace_metrics(G.mask_of(img))


def measure_design_sheet():
    """从 05-braced-lockup.png 找出所有"括号形"连通域并量测（取最像 24×44 的那几个）。"""
    img = Image.open(f"{BRAND}/sections/05-braced-lockup.png").convert("RGB")
    W, H = img.size
    px = img.load()
    mask = [[1 if (0.299 * px[x, y][0] + 0.587 * px[x, y][1] + 0.114 * px[x, y][2]) < 150 else 0
             for x in range(W)] for y in range(H)]
    out = []
    for grp in sorted(_groups(mask), key=lambda g: -len(g))[:400]:
        if len(grp) < 3000:
            continue
        gx0 = min(p[0] for p in grp); gx1 = max(p[0] for p in grp)
        gy0 = min(p[1] for p in grp); gy1 = max(p[1] for p in grp)
        w, h = gx1 - gx0 + 1, gy1 - gy0 + 1
        if h < 120 or not (0.40 <= w / h <= 0.70):
            continue
        sub = [[0] * w for _ in range(h)]
        for (x, y) in grp:
            sub[y - gy0][x - gx0] = 1
        m = brace_metrics(sub)
        m["at_px"] = [gx0, gy0, w, h]
        out.append(m)
    return out


def _groups(mask):
    h, w = len(mask), len(mask[0])
    seen = [[0] * w for _ in range(h)]
    for y0 in range(h):
        for x0 in range(w):
            if not mask[y0][x0] or seen[y0][x0]:
                continue
            stack, pts = [(x0, y0)], []
            seen[y0][x0] = 1
            while stack:
                x, y = stack.pop()
                pts.append((x, y))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = 1
                            stack.append((nx, ny))
            yield pts


# ══════════════════════════════════════════════════════════════════
# 3. 出括号资产 + 新旧对比图 + 重出全部锁定资产
# ══════════════════════════════════════════════════════════════════
def main():
    design = measure_design_sheet()
    new_m = metrics_of(new_brace(), FRAME_H)
    old_m = metrics_of(OLD_BRACE, OLD_BRACE_BOX)
    table = {"design_sheet_candidates": design[:4], "new": new_m, "old": old_m}

    def w(name, text):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(text)

    def svg_of(glyphs, box, color="currentColor", flip=False):
        s = G.svg_stroke(glyphs, color=color).replace(
            'viewBox="0 0 64 64" width="64" height="64"', f'viewBox="0 0 {box[0]:g} {box[1]:g}"')
        if flip:
            s = s.replace('<svg xmlns="http://www.w3.org/2000/svg"',
                          f'<svg xmlns="http://www.w3.org/2000/svg" transform="scale(-1,1) translate(-{box[0]:g},0)"', 1)
        return s

    def filled_of(glyphs, box, color="currentColor", flip=False):
        s = G.svg_filled(glyphs, color=color).replace('viewBox="0 0 64 64"', f'viewBox="0 0 {box[0]:g} {box[1]:g}"')
        if flip:
            s = s.replace('<svg xmlns="http://www.w3.org/2000/svg"',
                          f'<svg xmlns="http://www.w3.org/2000/svg" transform="scale(-1,1) translate(-{box[0]:g},0)"', 1)
        return s

    NB = new_brace()
    w("utvtu-brace.svg", svg_of(NB, (FRAME_W, FRAME_H)))
    w("utvtu-brace-flipx.svg", svg_of(NB, (FRAME_W, FRAME_H), flip=True))
    w("utvtu-brace-filled.svg", filled_of(NB, (FRAME_W, FRAME_H)))
    w("utvtu-brace-mono.svg", svg_of(NB, (FRAME_W, FRAME_H)))

    # XAML：把 brand-brace 换成新几何（独立 28×48 坐标系）
    def d_of(glyphs):
        cmds = []
        for g in glyphs:
            if g[0] == "line":
                _, (x1, y1, x2, y2), _s = g
                cmds.append(f"M{x1:.2f},{y1:.2f} L{x2:.2f},{y2:.2f}")
            elif g[0] == "arc":
                _, (cx, cy, r), a0, a1, _s = g
                p0 = (cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0)))
                p1 = (cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1)))
                cmds.append(f"M{p0[0]:.2f},{p0[1]:.2f} A{r:.2f},{r:.2f} 0 0 1 {p1[0]:.2f},{p1[1]:.2f}")
        return " ".join(cmds)

    ax = os.path.join(OUT, "utvtu-brand-geometry.axaml")
    txt = open(ax, encoding="utf-8").read()
    lines = [ln for ln in txt.splitlines() if "brand-brace" not in ln]
    lines.append(f'<!-- 括号：**独立坐标系** 28×48 框、墨迹 24×44、描边 = 44/6 = {SW:.4f}、R = 描边/2 = {R:.4f}；}} = 用 ScaleX=-1 镜像 -->')
    lines.append(f'<StreamGeometry x:Key="brand-brace">{d_of(NB)}</StreamGeometry>')
    w("utvtu-brand-geometry.axaml", "\n".join(lines) + "\n")

    # 新旧对比图（同 cap 并排 + 数值）
    rows = []
    for cap in (20, 28, 44, 64):
        imgs = []
        for gl, box in ((NB, FRAME_H), (OLD_BRACE, OLD_BRACE_BOX)):
            im = G.render(gl, 256, L.C["ink"], box_units=box)
            m = G.mask_of(im)
            ys = [y for y in range(m.__len__()) if any(m[y])]
            xs = [x for x in range(len(m[0])) if any(m[y][x] for y in range(len(m)))]
            im = im.crop((xs[0], ys[0], xs[-1] + 1, ys[-1] + 1))
            sc = cap / im.height
            imgs.append(im.resize((max(1, int(im.width * sc)), cap), Image.LANCZOS))
        rows.append((f"cap {cap}px", imgs,
                     f"新：臂/笔画 {new_m['arm_over_stroke']} · 尖/笔画 {new_m['nub_over_stroke']} · 墨迹比 {new_m['ink_w_over_h']}"
                     f"    旧：臂/笔画 {old_m['arm_over_stroke']} · 尖/笔画 {old_m['nub_over_stroke']} · 墨迹比 {old_m['ink_w_over_h']}"))
    p_cmp = L.sheet(rows, os.path.join(OUT, "brand-brace-new-vs-old.png"),
                    "W39b 括号重建：新（左） vs 旧（右）—— 同 cap 并排", cell=(760, 170))

    # 用新括号重出全部锁定资产
    L.BRA = NB
    L.BRA_BB = L.ink_bbox_units(NB)
    L.BRA_W = FRAME_W
    # 锁定 SVG 里的括号 viewBox 要跟着换（_lockup.lockup_svg 里硬编码 64）
    src = open(os.path.join(HERE, "_lockup.py"), encoding="utf-8").read()
    src = src.replace("'viewBox=\"0 0 64 64\" width=\"64\" height=\"64\"', 'viewBox=\"0 0 64 64\"')",
                      "'viewBox=\"0 0 64 64\" width=\"64\" height=\"64\"', 'viewBox=\"0 0 28 48\"')")
    exec(compile(src, "_lockup_newbox.py", "exec"), L.__dict__)
    L.main()

    import shutil
    shutil.copyfile(p_cmp, os.path.join(SHOTS, os.path.basename(p_cmp)))
    for f in ("brand-lockup-sheet.png", "brand-lockup-ladder.png", "brand-wordmark-compare.png"):
        p = os.path.join(OUT, f)
        if os.path.exists(p):
            shutil.copyfile(p, os.path.join(SHOTS, f))

    with open(os.path.join(OUT, "brand-brace-metrics.json"), "w", encoding="utf-8") as f:
        json.dump(table, f, ensure_ascii=False, indent=1)

    print("=== 设计稿括号候选（归一化：墨迹高=100）===")
    for m in design[:4]:
        print(f"  at={m['at_px']} 墨迹比 {m['ink_w_over_h']} 笔画 {m['stroke_norm']} 臂 {m['arm_norm']} "
              f"(臂/笔画 {m['arm_over_stroke']}) 尖 {m['nub_norm']} (尖/笔画 {m['nub_over_stroke']})")
    print("=== 新几何 ===")
    print(" ", json.dumps(new_m, ensure_ascii=False))
    print("=== 旧几何 ===")
    print(" ", json.dumps(old_m, ensure_ascii=False))


if __name__ == "__main__":
    main()
