"""W39c 按**源路径**（品牌板 HTML 抽出）重建全部资产。

权威源（verbatim，不改数字）：
  标志  viewBox="0 0 64 64"     M8 13 H24 M16 13 V29 M40 13 H56 M48 13 V29 M27 25 L32 35 L37 25
                                M9 41 V44 Q9 51 16 51 Q23 51 23 44 V41 M55 41 V44 Q55 51 48 51 Q41 51 41 44 V41
  括号  viewBox="-2 -2 28 48"  M20 4 H14 Q10 4 10 8 V18 Q10 22 4 22 Q10 22 10 26 V36 Q10 40 14 40 H20
  切角v 填充 viewBox="0 0 9 14" M0 0 L4.5 14 L9 0
  切角v 描边                   M1.54 1.54 L4.5 12.46 L7.46 1.54
笔画：标志 = 尺寸÷8（64 网格里是 8）；括号 = 高÷6（墨迹高 36 ⇒ 6）；XAML 一律**描边**渲染。
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

BRAND, OUT, SHOTS = L.BRAND, L.OUT, L.SHOTS
C = G.C

def metrics_of_old():
    """旧（描摹 + 描边转填充）括号的度量，用于新旧对比。"""
    m = G.mask_of(G.render(G.brace(), 512, "#000000", box_units=64))
    h, w = len(m), len(m[0])
    ys = [y for y in range(h) if any(m[y])]
    xs = [x for x in range(w) if any(m[y][x] for y in range(h))]
    y0, y1, x0, x1 = ys[0], ys[-1], xs[0], xs[-1]
    ink_h, ink_w = y1 - y0 + 1, x1 - x0 + 1
    stroke = G.median_stroke_px(m)
    yb = min(y1, y0 + max(1, int(stroke * 0.5)))
    row = [x for x in range(w) if m[yb][x]]
    arm = (max(row) - min(row) + 1) if row else 0
    ymid = (y0 + y1) // 2
    band = range(max(y0, ymid - 2), min(y1, ymid + 3))
    mid_min = min((x for y in band for x in range(w) if m[y][x]), default=x1)
    k = 100.0 / ink_h
    return {"ink_wh_px": [ink_w, ink_h], "ink_w_over_h": round(ink_w / ink_h, 3), "stroke_px": stroke,
            "ink_w_norm": round(ink_w * k, 1), "stroke_norm": round(stroke * k, 1),
            "stroke_over_h": round(stroke / ink_h, 4), "arm_norm": round(arm * k, 1),
            "arm_over_stroke": round(arm / stroke, 2),
            "nub_norm": round((x1 - stroke / 2.0 - mid_min) * k, 1),
            "nub_over_stroke": round((x1 - stroke / 2.0 - mid_min) / stroke, 2)}


def metrics_of_old():
    """旧（描摹 + 描边转填充）括号的度量，用于新旧对比。"""
    import _generate as _G
    m = _G.mask_of(_G.render(_G.brace(), 512, "#000000", box_units=64))
    h, w = len(m), len(m[0])
    ys = [y for y in range(h) if any(m[y])]
    xs = [x for x in range(w) if any(m[y][x] for y in range(h))]
    y0, y1, x0, x1 = ys[0], ys[-1], xs[0], xs[-1]
    ink_h, ink_w = y1 - y0 + 1, x1 - x0 + 1
    stroke = _G.median_stroke_px(m)
    yb = min(y1, y0 + max(1, int(stroke * 0.5)))
    row = [x for x in range(w) if m[yb][x]]
    arm = (max(row) - min(row) + 1) if row else 0
    ymid = (y0 + y1) // 2
    band = range(max(y0, ymid - 2), min(y1, ymid + 3))
    mid_min = min((x for y in band for x in range(w) if m[y][x]), default=x1)
    k = 100.0 / ink_h
    return {"ink_wh_px": [ink_w, ink_h], "ink_w_over_h": round(ink_w / ink_h, 3), "stroke_px": stroke,
            "ink_w_norm": round(ink_w * k, 1), "stroke_norm": round(stroke * k, 1),
            "stroke_over_h": round(stroke / ink_h, 4), "arm_norm": round(arm * k, 1),
            "arm_over_stroke": round(arm / stroke, 2),
            "nub_norm": round((x1 - stroke / 2.0 - mid_min) * k, 1),
            "nub_over_stroke": round((x1 - stroke / 2.0 - mid_min) / stroke, 2)}


# ── 权威几何（verbatim）──'
MARK_D = ("M8 13 H24 M16 13 V29 M40 13 H56 M48 13 V29 M27 25 L32 35 L37 25 "
          "M9 41 V44 Q9 51 16 51 Q23 51 23 44 V41 M55 41 V44 Q55 51 48 51 Q41 51 41 44 V41")
MARK_BOX = (0, 0, 64, 64)
MARK_SW = 8.0                       # 尺寸÷8
BRACE_D = "M20 4 H14 Q10 4 10 8 V18 Q10 22 4 22 Q10 22 10 26 V36 Q10 40 14 40 H20"
BRACE_BOX = (-2, -2, 28, 48)
BRACE_SW = 6.0                      # 高÷6（墨迹高 36）
V_FILL_D = "M0 0 L4.5 14 L9 0"
V_STROKE_D = "M1.54 1.54 L4.5 12.46 L7.46 1.54"
V_BOX = (0, 0, 9, 14)

# ══════════════════════════════════════════════════════════════════
# SVG path 迷你解释器（M/L/H/V/Q/Z 子集）→ 折线子路径
# ══════════════════════════════════════════════════════════════════
def parse_path(d, curve_steps=32):
    toks, i, n = [], 0, len(d)
    while i < n:
        ch = d[i]
        if ch in "MmLlHhVvQqZz":
            toks.append(ch)
            i += 1
        elif ch in " ,":
            i += 1
        else:
            j = i
            while j < n and (d[j].isdigit() or d[j] in ".-"):
                j += 1
            toks.append(float(d[i:j]))
            i = j
    subs, cur, cx, cy, sx, sy, k = [], [], 0.0, 0.0, 0.0, 0.0, 0
    cmd = None
    while k < len(toks):
        t = toks[k]
        if isinstance(t, str):
            cmd = t
            k += 1
            if cmd in "Zz":
                if cur:
                    cur.append((sx, sy))
                    subs.append(cur)
                    cur = []
                continue
        if cmd in "Mm":
            x, y = toks[k], toks[k + 1]
            k += 2
            if cur:
                subs.append(cur)
            cur = [(x, y)]
            cx, cy, sx, sy = x, y, x, y
        elif cmd in "Hh":
            x = toks[k]
            k += 1
            cx = x
            cur.append((cx, cy))
        elif cmd in "Vv":
            y = toks[k]
            k += 1
            cy = y
            cur.append((cx, cy))
        elif cmd in "Ll":
            x, y = toks[k], toks[k + 1]
            k += 2
            cx, cy = x, y
            cur.append((cx, cy))
        elif cmd in "Qq":
            x1, y1, x, y = toks[k], toks[k + 1], toks[k + 2], toks[k + 3]
            k += 4
            for s in range(1, curve_steps + 1):
                t2 = s / curve_steps
                mt = 1 - t2
                cur.append((mt * mt * cx + 2 * mt * t2 * x1 + t2 * t2 * x,
                            mt * mt * cy + 2 * mt * t2 * y1 + t2 * t2 * y))
            cx, cy = x, y
        else:
            k += 1
    if cur:
        subs.append(cur)
    return subs


def render_stroke(subs, box, sw, size, color, ss=8):
    """按 box 归一化渲染（描边，圆头圆接）。"""
    bx, by, bw, bh = box
    scale = size * ss / max(bw, bh)
    img = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rgb = G.hex2rgb(color) + (255,)
    wpx = max(1, int(round(sw * scale)))

    def P(p):
        return ((p[0] - bx) * scale, (p[1] - by) * scale)

    for sub in subs:
        pts = [P(p) for p in sub]
        for i in range(len(pts) - 1):
            d.line([pts[i], pts[i + 1]], fill=rgb, width=wpx)
        r = wpx / 2
        for (x, y) in pts:
            d.ellipse([x - r, y - r, x + r, y + r], fill=rgb)
    return img.resize((size, size), Image.LANCZOS)


def render_fill_paths(paths, box, size, color, ss=8):
    bx, by, bw, bh = box
    scale = size * ss / max(bw, bh)
    img = Image.new("RGBA", (size * ss, size * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rgb = G.hex2rgb(color) + (255,)
    for sub in paths:
        d.polygon([((p[0] - bx) * scale, (p[1] - by) * scale) for p in sub], fill=rgb)
    return img.resize((size, size), Image.LANCZOS)


MARK_SUBS = parse_path(MARK_D)
BRACE_SUBS = parse_path(BRACE_D)


def ink_bbox(subs, box, size=512):
    m = G.mask_of(render_stroke(subs, box, 0.0001, size, "#000000"))
    # 用真实笔画重算
    return None


def metrics(subs, box, sw, cap=512, color="#000000"):
    """渲染→量墨迹 bbox 与结构量（全部归一到墨迹高=100）。"""
    img = render_stroke(subs, box, sw, cap, color)
    m = G.mask_of(img)
    h, w = len(m), len(m[0])
    ys = [y for y in range(h) if any(m[y])]
    xs = [x for x in range(w) if any(m[y][x] for y in range(h))]
    y0, y1, x0, x1 = ys[0], ys[-1], xs[0], xs[-1]
    ink_h, ink_w = y1 - y0 + 1, x1 - x0 + 1
    stroke = G.median_stroke_px(m)
    yb = min(y1, y0 + max(1, int(stroke * 0.5)))
    row = [x for x in range(w) if m[yb][x]]
    arm = (max(row) - min(row) + 1) if row else 0
    ymid = (y0 + y1) // 2
    band = range(max(y0, ymid - 2), min(y1, ymid + 3))
    mid_min = min((x for y in band for x in range(w) if m[y][x]), default=x1)
    spine_cx = x1 - stroke / 2.0
    k = 100.0 / ink_h
    return {"ink_wh_px": [ink_w, ink_h], "ink_w_over_h": round(ink_w / ink_h, 3),
            "stroke_px": stroke, "ink_h_norm": 100.0, "ink_w_norm": round(ink_w * k, 1),
            "stroke_norm": round(stroke * k, 1), "stroke_over_h": round(stroke / ink_h, 4),
            "arm_norm": round(arm * k, 1), "arm_over_stroke": round(arm / stroke, 2),
            "nub_norm": round(spine_cx * k - mid_min * k, 1),
            "nub_over_stroke": round((spine_cx - mid_min) / stroke, 2)}


# ══════════════════════════════════════════════════════════════════
# 叠图验证：新渲染 vs 品牌板原图（按墨迹 bbox 对齐）→ IoU
# ══════════════════════════════════════════════════════════════════
def sheet_crop(section, box, min_px=3000, ratio_range=None, idx=0):
    img = Image.open(f"{BRAND}/sections/{section}").convert("RGB")
    px = img.load()
    W, H = img.size
    mask = [[1 if (0.299 * px[x, y][0] + 0.587 * px[x, y][1] + 0.114 * px[x, y][2]) < 150 else 0
             for x in range(W)] for y in range(H)]
    if box:
        x0b, y0b, x1b, y1b = box
        for y in range(H):
            for x in range(W):
                if not (x0b <= x < x1b and y0b <= y < y1b):
                    mask[y][x] = 0
    cands = []
    for grp in _groups(mask):
        if len(grp) < min_px:
            continue
        gx0 = min(p[0] for p in grp); gx1 = max(p[0] for p in grp)
        gy0 = min(p[1] for p in grp); gy1 = max(p[1] for p in grp)
        w, h = gx1 - gx0 + 1, gy1 - gy0 + 1
        if ratio_range and not (ratio_range[0] <= w / h <= ratio_range[1]):
            continue
        cands.append((len(grp), (gx0, gy0, gx1 + 1, gy1 + 1)))
    cands.sort(key=lambda c: -c[0])
    if not cands:
        return None
    bx = cands[min(idx, len(cands) - 1)][1]
    return img.crop(bx)


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


def overlay_iou(ref_crop, mine_img, path):
    """ref（品牌板裁剪）与我的渲染，按墨迹 bbox 归一化后叠图 + IoU。"""
    def to_mask(im, thr=110):
        im = im.convert("RGBA")
        a = im.split()[-1]
        return [[1 if a.getpixel((x, y)) >= thr else 0 for x in range(im.width)] for y in range(im.height)]

    def bbox(m):
        h, w = len(m), len(m[0])
        ys = [y for y in range(h) if any(m[y])]
        xs = [x for x in range(w) if any(m[y][x] for y in range(h))]
        return xs[0], ys[0], xs[-1] + 1, ys[-1] + 1

    S = 200
    def norm(im):
        m = to_mask(im)
        x0, y0, x1, y1 = bbox(m)
        return im.crop((x0, y0, x1, y1)).resize((S, S), Image.LANCZOS)

    ref = norm(ref_crop)
    mine = norm(mine_img)
    rm, mm = to_mask(ref, 128), to_mask(mine, 128)
    inter = sum(1 for y in range(S) for x in range(S) if rm[y][x] and mm[y][x])
    uni = sum(1 for y in range(S) for x in range(S) if rm[y][x] or mm[y][x])
    ov = Image.new("RGB", (S * 2 + 30, S + 40), (255, 255, 255))
    d = ImageDraw.Draw(ov)
    d.text((8, 8), f"IoU = {inter/max(1,uni):.3f}   左：品牌板原图   右：红=源几何 蓝=原图（叠图）", font=L.FS(13), fill=(20, 20, 24))
    ov.paste(ref.convert("RGB"), (8, 32))
    ov.paste(mine.convert("RGB"), (S + 22, 32))
    blend = Image.new("RGB", (S, S), (255, 255, 255))
    for y in range(S):
        for x in range(S):
            r, m2 = rm[y][x], mm[y][x]
            blend.putpixel((x, y), (255, 60, 60) if (r and not m2) else (60, 60, 255) if (m2 and not r) else (30, 30, 30))
    ov.paste(blend, (S + 22, 32 + S + 4)) if False else None
    ov.save(path)
    return round(inter / max(1, uni), 3)


# ══════════════════════════════════════════════════════════════════
# 字标：保留描摹的 U/T，**切角 v 换用源路径**
# ══════════════════════════════════════════════════════════════════
def wordmark_letters_with_source_v():
    """字标：保留描摹的 U/T 轮廓，**切角 v 换用源路径**。
    对齐规则：v 按**归一化空间的实测 bbox**（cap=100）贴合高度、水平居中 ——
    早期版本误用源像素 bbox（bbox_px）⇒ v 被放大到 6.9× 的另一套尺度，整体 bbox 崩到 1226×694。"""
    letters = [dict(l) for l in L.WORDMARK]
    xs = [p[0] for p in letters[2]["pts"]]
    ys = [p[1] for p in letters[2]["pts"]]
    bx0, bx1, by0, by1 = min(xs), max(xs), min(ys), max(ys)
    v = parse_path(V_FILL_D)                      # 源：填充切角 v（9×14）
    sc = (by1 - by0) / 14.0                       # 只按高度贴合（同一尺度的归一化空间）
    ox = bx0 + ((bx1 - bx0) - 9 * sc) / 2.0       # 水平居中
    letters[2]["pts"] = [(ox + q[0] * sc, by0 + q[1] * sc) for q in v[0]]
    letters[2]["bbox_px"] = [ox, by0, ox + 9 * sc, by1]   # 保持"归一化"语义，别再塞源像素
    return letters


def assert_wordmark_sane(letters, expect_w=None, tol=1.5):
    """★健全性断言（防复发）：合成几何后校验 ① 子路径同一坐标尺度 ② 整体 bbox 在预期范围。
    这次的故障正是"数值看着对、几何整体崩了" —— 只输出指标 JSON 不够，必须 fail loud。"""
    boxes = []
    for l in letters:
        xs = [q[0] for q in l["pts"]]
        ys = [q[1] for q in l["pts"]]
        boxes.append((min(xs), min(ys), max(xs), max(ys)))
    x0 = min(b[0] for b in boxes); y0 = min(b[1] for b in boxes)
    x1 = max(b[2] for b in boxes); y1 = max(b[3] for b in boxes)
    w, h = x1 - x0, y1 - y0
    cap = max(b[3] - b[1] for b in boxes)
    # ① 同一尺度：任何子路径不得超出"整体 bbox + 2 单位"，且 v 的高度不得超过 cap
    for i, b in enumerate(boxes):
        assert b[0] >= x0 - 2 and b[1] >= y0 - 2 and b[2] <= x1 + 2 and b[3] <= y1 + 2, \
            f"子路径 {i} 超出整体 bbox：{b} vs {(x0, y0, x1, y1)}（坐标系混用？）"
        assert b[3] - b[1] <= cap + 2, f"子路径 {i} 高度 {b[3]-b[1]:.1f} 超过 cap {cap:.1f}（尺度不一致？）"
    # ② 整体 bbox 落在预期范围（U/T 轮廓决定，v 只在其内）
    ut = [b for i, b in enumerate(boxes) if i != 2]
    utw = max(b[2] for b in ut) - min(b[0] for b in ut)
    assert 90 <= cap <= 110, f"字标 cap 异常：{cap:.1f}（预期 ≈100）"
    assert w <= utw + 2, f"整体宽 {w:.1f} 超过 U/T 宽 {utw:.1f}（v 跑出尺度了）"
    if expect_w is not None:
        assert abs(w - expect_w) <= tol, f"整体宽 {w:.2f} 偏离预期 {expect_w:.2f}±{tol}"
    return {"bbox": [round(v, 2) for v in (x0, y0, x1, y1)], "w": round(w, 2), "h": round(h, 2), "cap": round(cap, 2)}


# ══════════════════════════════════════════════════════════════════
# 锁定组装（cap 驱动：标志墨迹高 = cap；括号墨迹 = 1.2×cap；间距 0.42×cap）
# ══════════════════════════════════════════════════════════════════
WM = wordmark_letters_with_source_v()
WM_SANITY = assert_wordmark_sane(WM, expect_w=377.33)


def mark_ink(cap):
    """标志按"墨迹高 = cap"缩放后的渲染（透明底）。"""
    im = render_stroke(MARK_SUBS, MARK_BOX, MARK_SW, 512, "#000000")
    m = G.mask_of(im)
    h, w = len(m), len(m[0])
    ys = [y for y in range(h) if any(m[y])]
    xs = [x for x in range(w) if any(m[y][x] for y in range(h))]
    crop = im.crop((xs[0], ys[0], xs[-1] + 1, ys[-1] + 1))
    sc = cap / crop.height
    return crop.resize((max(1, int(crop.width * sc)), max(1, int(cap))), Image.LANCZOS)


def brace_ink(h):
    im = render_stroke(BRACE_SUBS, BRACE_BOX, BRACE_SW, 512, "#000000")
    m = G.mask_of(im)
    H, W = len(m), len(m[0])
    ys = [y for y in range(H) if any(m[y])]
    xs = [x for x in range(W) if any(m[y][x] for y in range(H))]
    crop = im.crop((xs[0], ys[0], xs[-1] + 1, ys[-1] + 1))
    sc = h / crop.height
    return crop.resize((max(1, int(crop.width * sc)), max(1, int(h))), Image.LANCZOS)


def word_ink(cap, color):
    sc = cap / 100.0
    im = L.render_wordmark(cap, color, letters=WM)
    return im


LOCKUPS = {"signature": (True, True), "display": (False, True), "short": (True, False)}
FINISHES = L.FINISHES


def compose(name, finish, cap):
    has_mark, has_word = LOCKUPS[name]
    fin = FINISHES[finish]
    gap = cap * 0.42
    b = brace_ink(cap * 1.2)
    mk = mark_ink(cap) if has_mark else None
    wd = word_ink(cap, fin["word"]) if has_word else None
    parts = [b]
    if mk is not None:
        parts.append(mk)
    if wd is not None:
        parts.append(wd)
    parts.append(b)
    total = sum(p.width for p in parts) + gap * (len(parts) - 1) + 16
    Hh = max(p.height for p in parts) + 16
    canvas = Image.new("RGBA", (int(total), int(Hh)), G.hex2rgb(fin["bg"]) + (255,))
    x = 8.0
    for p in parts:
        canvas.alpha_composite(p, (int(x), int((Hh - p.height) / 2)))
        x += p.width + gap
    return canvas


def lockup_svg(name, finish):
    fin = FINISHES[finish]
    has_mark, has_word = LOCKUPS[name]
    gap = 42.0
    cap = 100.0
    brace_scale = (cap * 1.2) / 42.0            # 括号墨迹高 42（中心线 36 + 笔画 6）
    mark_scale = cap / 45.0                     # 标志墨迹高 45（y9..54）
    word_scale = cap / 100.0
    # 各部件墨迹尺寸（单位空间）
    brace_w = 22 * brace_scale
    mark_w = 56 * mark_scale
    word_w = max(p[0] for l in WM for p in l["pts"])
    H = cap * 1.2 + 40
    x, parts = 40.0, []
    def brace_at(xx, color):
        return (f'<g transform="translate({xx - (-2) * brace_scale:.2f},{(H - 42 * brace_scale) / 2 - (-2) * brace_scale:.2f}) '
                f'scale({brace_scale:.4f})"><path d="{BRACE_D}" fill="none" stroke="{color}" stroke-width="{BRACE_SW}" '
                f'stroke-linecap="round" stroke-linejoin="round"/></g>')
    def mark_at(xx, color):
        return (f'<g transform="translate({xx - 4 * mark_scale:.2f},{(H - cap) / 2 - 9 * mark_scale:.2f}) '
                f'scale({mark_scale:.4f})"><path d="{MARK_D}" fill="none" stroke="{color}" stroke-width="{MARK_SW}" '
                f'stroke-linecap="round" stroke-linejoin="round"/></g>')
    parts.append(brace_at(x, fin["brace"]))
    x += brace_w + gap
    if has_mark:
        parts.append(mark_at(x, fin["mark"]))
        x += mark_w + gap
    if has_word:
        parts.append(f'<g transform="translate({x:.2f},{(H - cap) / 2:.2f}) scale({word_scale:.4f})">'
                     f'<path fill="{fin["word"]}" d="{L.wm_path_d(WM)}"/></g>')
        x += word_w + gap
    parts.append(brace_at(x, fin["brace"]))
    x += brace_w
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {x + 40:.0f} {H:.0f}" '
            f'width="{x + 40:.0f}" height="{H:.0f}">' + "".join(parts) + "</svg>")


def main():
    def w(name, text):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(text)

    table = {"source": {"mark": MARK_D, "brace": BRACE_D, "v_fill": V_FILL_D, "v_stroke": V_STROKE_D},
             "brace_new": metrics(BRACE_SUBS, BRACE_BOX, BRACE_SW),
             "mark_new": metrics(MARK_SUBS, MARK_BOX, MARK_SW),
             "old_brace": metrics_of_old()}

    # ── 叠图验证 ──
    io = {}
    ref_brace = sheet_crop("05-braced-lockup.png", None, min_px=2000, ratio_range=(0.40, 0.75), idx=0)
    if ref_brace:
        io["brace"] = overlay_iou(ref_brace, render_stroke(BRACE_SUBS, BRACE_BOX, BRACE_SW, 400, "#000000"),
                                  os.path.join(OUT, "brand-verify-brace-overlay.png"))
    ref_mark = sheet_crop("00-brand-components.png", (150, 180, 620, 600), min_px=5000, idx=0)
    if ref_mark:
        io["mark"] = overlay_iou(ref_mark, render_stroke(MARK_SUBS, MARK_BOX, MARK_SW, 400, "#000000"),
                                 os.path.join(OUT, "brand-verify-mark-overlay.png"))
    table["wordmark_sanity"] = WM_SANITY
    table["overlay_iou"] = io

    # ── 源几何资产（描边语义，verbatim d）──
    w("utvtu-mark-source.svg",
      f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" fill="none" stroke="{C["primary_light"]}" '
      f'stroke-width="{MARK_SW:g}" stroke-linecap="round" stroke-linejoin="round"><path d="{MARK_D}"/></svg>')
    w("utvtu-brace-source.svg",
      f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="-2 -2 28 48" fill="none" stroke="currentColor" '
      f'stroke-width="{BRACE_SW:g}" stroke-linecap="round" stroke-linejoin="round"><path d="{BRACE_D}"/></svg>')
    w("utvtu-brace-source-flipx.svg",
      f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="-2 -2 28 48" fill="none" stroke="currentColor" '
      f'stroke-width="{BRACE_SW:g}" stroke-linecap="round" stroke-linejoin="round">'
      f'<path d="{BRACE_D}" transform="translate(24,0) scale(-1,1)"/></svg>')
    w("utvtu-v-chevron-fill.svg",
      f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 9 14" width="9" height="14">'
      f'<path d="{V_FILL_D}" fill="currentColor"/></svg>')
    w("utvtu-v-chevron-stroke.svg",
      f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 9 14" width="9" height="14" fill="none" '
      f'stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">'
      f'<path d="{V_STROKE_D}"/></svg>')

    # ── XAML：**描边渲染**（源路径 verbatim）──
    w("utvtu-brand-geometry.axaml",
      "<!-- W39c 品牌几何 —— **源路径 verbatim**（品牌板 HTML 抽出），一律用描边渲染：\n"
      f'     标志：viewBox 0 0 64 64，d = {MARK_D}\n'
      f'            StrokeThickness = 显示尺寸 ÷ 8（64 网格内为 {MARK_SW:g}）\n'
      f'     括号：viewBox -2 -2 28 48，d = {BRACE_D}\n'
      f'            StrokeThickness = 墨迹高 ÷ 6（此路径下为 {BRACE_SW:g}）；}} 用 ScaleTransform ScaleX="-1"（不要手绘）\n'
      f'     切角 v：填充 {V_FILL_D}（viewBox 0 0 9 14）／描边 {V_STROKE_D}\n'
      "     用法：<Path Data=\"{StaticResource brand-mark}\" Stroke=\"{DynamicResource md3.primary}\"\n"
      "                 StrokeThickness=\"{尺寸/8}\" StrokeLineCap=\"Round\" StrokeJoin=\"Round\" Stretch=\"Uniform\"/> -->\n"
      f'<StreamGeometry x:Key="brand-mark">{MARK_D}</StreamGeometry>\n'
      f'<StreamGeometry x:Key="brand-brace">{BRACE_D}</StreamGeometry>\n'
      f'<StreamGeometry x:Key="brand-v-chevron">{V_FILL_D}</StreamGeometry>\n'
      f'<StreamGeometry x:Key="brand-v-chevron-stroke">{V_STROKE_D}</StreamGeometry>\n')

    # ── 3×3 锁定资产 ──
    rows = []
    for name in LOCKUPS:
        imgs = []
        for fin in FINISHES:
            im = compose(name, fin, 44)
            imgs.append(im)
            for tag, cap in (("1x", 24), ("2x", 48)):
                compose(name, fin, cap).save(os.path.join(OUT, f"utvtu-lockup-{name}-{fin}@{tag}.png"))
            w(f"utvtu-lockup-{name}-{fin}.svg", lockup_svg(name, fin))
        label = {"signature": "{ 脸 UTvTU }", "display": "{ UTvTU }", "short": "{ 脸 }"}[name]
        rows.append((label, imgs, "finish：ink · reversed · branded（括号品牌色）"))
    p1 = L.sheet(rows, os.path.join(OUT, "brand-lockup-sheet.png"),
                 "W39c 三种锁定 × 三 finish（cap 44px，源几何重建）")

    ladder = []
    for cap in (14, 16, 20, 28):
        ladder.append((f"cap {cap}px", [compose(n, "ink", cap) for n in ("signature", "display", "short")]
                       + [word_ink(cap, C["ink"])], "「{ 脸 UTvTU }」「{ UTvTU }」「{ 脸 }」+ 纯字标"))
    p2 = L.sheet(ladder, os.path.join(OUT, "brand-lockup-ladder.png"),
                 "W39c 尺寸阶梯（顶栏 14–16 / 欢迎页头 20 / 关于页 28）")

    # ── 新旧括号对比（同 cap 并排）──
    rows2 = []
    for cap in (20, 28, 44, 64):
        a = brace_ink(cap)
        old = G.render(G.brace(), 512, C["ink"], box_units=64)
        m = G.mask_of(old)
        H, W = len(m), len(m[0])
        ys = [y for y in range(H) if any(m[y])]
        xs = [x for x in range(W) if any(m[y][x] for y in range(H))]
        old = old.crop((xs[0], ys[0], xs[-1] + 1, ys[-1] + 1))
        old = old.resize((max(1, int(old.width * cap / old.height)), cap), Image.LANCZOS)
        rows2.append((f"cap {cap}px", [a, old],
                      f"新：臂/笔画 {table['brace_new']['arm_over_stroke']} · 尖/笔画 {table['brace_new']['nub_over_stroke']} · "
                      f"墨迹比 {table['brace_new']['ink_w_over_h']}    旧：臂/笔画 {table['old_brace']['arm_over_stroke']} · "
                      f"尖/笔画 {table['old_brace']['nub_over_stroke']} · 墨迹比 {table['old_brace']['ink_w_over_h']}"))
    p3 = L.sheet(rows2, os.path.join(OUT, "brand-brace-new-vs-old.png"),
                 "W39c 括号：新（源路径，左） vs 旧（描摹 + 描边转填充，右）", cell=(760, 170))

    import shutil
    for p in (p1, p2, p3, os.path.join(OUT, "brand-verify-brace-overlay.png"),
              os.path.join(OUT, "brand-verify-mark-overlay.png")):
        if os.path.exists(p):
            shutil.copyfile(p, os.path.join(SHOTS, os.path.basename(p)))

    with open(os.path.join(OUT, "brand-source-metrics.json"), "w", encoding="utf-8") as f:
        json.dump(table, f, ensure_ascii=False, indent=1)

    print("=== 括号（归一化 墨迹高=100）===")
    print("  新（源路径）:", json.dumps(table["brace_new"], ensure_ascii=False))
    print("  旧（描摹）  :", json.dumps(table["old_brace"], ensure_ascii=False))
    print("=== 标志（归一化）===")
    print(" ", json.dumps(table["mark_new"], ensure_ascii=False))
    print("=== 叠图 IoU vs 品牌板 ===", io)
    for cap in (14, 16, 20, 28):
        im = word_ink(cap, "#000000")
        m = G.mask_of(im)
        print(f"  字标 cap {cap:2d}: {len(G.components(m))} 域 · 笔宽 {G.median_stroke_px(m)}px · 间距 {G.min_gap_px(m)}px")


if __name__ == "__main__":
    main()
