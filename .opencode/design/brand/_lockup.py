"""W39 横向商标（锁定）资产生成器（一次性脚本，可重跑）。

A. 字标转矢量轮廓：**没有 Roboto**（系统与仓库都没有）⇒ 不能"用字体导出轮廓"。
   改为从品牌板的字标渲染图**描摹真实轮廓**（连通域 + Moore 边界追踪 + RDP 简化），
   得到与用户设计逐像素一致的可缩放几何（比"改用别的字体排一遍"忠实得多）。
B. 三种锁定 × 三 finish → SVG + XAML + PNG@1x/@2x
C. 应用内变体（括号走 md3.primary、字标走 md3.on-surface）+ 尺寸阶梯 + 最小尺寸规则
D. 令牌与接入清单写进 .opencode/plans/brand-lockup.md
"""
import json
import math
import os
import sys
from collections import deque

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import _generate as G  # 复用：颜色令牌、标志几何、括号几何、渲染器、SVG 工具

BRAND = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
OUT = os.path.join(BRAND, "out")
SHOTS = r"G:\xklmy文件夹\vibe coding\UTvTU\.dsh\fx\shots"
os.makedirs(OUT, exist_ok=True)
os.makedirs(SHOTS, exist_ok=True)

C = G.C
CAP = 100.0          # 归一化空间：大写高 = 100 单位
FS = G.font

# ══════════════════════════════════════════════════════════════════
# 1. 描摹字标轮廓
# ══════════════════════════════════════════════════════════════════
def load_ink(path, box, thr=140):
    img = Image.open(path).convert("RGB").crop(box)
    w, h = img.size
    px = img.load()
    m = [[1 if (0.299 * px[x, y][0] + 0.587 * px[x, y][1] + 0.114 * px[x, y][2]) < thr else 0
          for x in range(w)] for y in range(h)]
    return m, img


def comps(m, min_px=800):
    h, w = len(m), len(m[0])
    seen = [[0] * w for _ in range(h)]
    out = []
    for y0 in range(h):
        for x0 in range(w):
            if not m[y0][x0] or seen[y0][x0]:
                continue
            stack, pts = [(x0, y0)], []
            seen[y0][x0] = 1
            while stack:
                x, y = stack.pop()
                pts.append((x, y))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and m[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = 1
                            stack.append((nx, ny))
            if len(pts) >= min_px:
                out.append(pts)
    return out


DIRS = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]


def trace_contour(pts):
    """Moore 邻域边界追踪（外轮廓；本字标 5 字形均无内孔 ⇒ 只需外轮廓）。"""
    S = set(pts)
    start = min(S, key=lambda p: (p[1], p[0]))
    contour = [start]
    cur, bdir = start, 4                      # 从左方进入
    for _ in range(len(S) * 8 + 16):
        found = None
        for k in range(8):
            d = (bdir + 1 + k) % 8            # 顺时针找下一个墨迹邻居
            nxt = (cur[0] + DIRS[d][0], cur[1] + DIRS[d][1])
            if nxt in S:
                found, nd = nxt, d
                break
        if found is None:
            break
        contour.append(found)
        bdir = (nd + 4) % 8                   # 回退方向 = 来时反向
        cur = found
        if cur == start and len(contour) > 4:
            break
    return contour


def rdp(points, eps):
    if len(points) < 3:
        return points
    (x1, y1), (x2, y2) = points[0], points[-1]
    dmax, idx = 0.0, 0
    for i in range(1, len(points) - 1):
        x0, y0 = points[i]
        num = abs((y2 - y1) * x0 - (x2 - x1) * y0 + x2 * y1 - y2 * x1)
        den = math.hypot(y2 - y1, x2 - x1) or 1e-9
        d = num / den
        if d > dmax:
            dmax, idx = d, i
    if dmax > eps:
        return rdp(points[:idx + 1], eps)[:-1] + rdp(points[idx:], eps)
    return [points[0], points[-1]]


def rdp_closed(points, eps):
    """闭合轮廓的 RDP：必须先从"离起点最远的点"切开，否则首尾同点 ⇒ 弦退化 ⇒ 整条被压成 2 点。"""
    pts = points[:-1] if len(points) > 1 and points[0] == points[-1] else points
    if len(pts) < 4:
        return pts
    sx, sy = pts[0]
    far = max(range(len(pts)), key=lambda i: (pts[i][0] - sx) ** 2 + (pts[i][1] - sy) ** 2)
    a = rdp(pts[:far + 1], eps)
    b = rdp(pts[far:] + [pts[0]], eps)
    return a[:-1] + b


def build_wordmark():
    m, img = load_ink(f"{BRAND}/sections/02-wordmark.png", (60, 120, 2300, 900))
    groups = comps(m)
    groups = sorted(groups, key=lambda g: min(p[0] for p in g))
    xs = [p[0] for g in groups for p in g]
    ys = [p[1] for g in groups for p in g]
    base = max(ys)                                     # 基线 = 墨迹最低点
    cap = base - min(ys)                               # 大写高
    x0 = min(xs)
    scale = CAP / cap                                  # → 归一化：cap = 100
    letters = []
    for g in groups:
        c = rdp_closed(trace_contour(g), 1.0)
        letters.append({
            "bbox_px": [min(p[0] for p in g), min(p[1] for p in g),
                        max(p[0] for p in g), max(p[1] for p in g)],
            "pts": [((x - x0) * scale, (y - min(ys)) * scale) for x, y in c],
        })
    stats = {
        "source_px": {"cap": cap, "baseline_y": base, "ink_w": max(xs) - x0, "letters": len(groups)},
        "letter_bboxes_norm": [[round((b[0] - x0) * scale, 2), round((b[1] - min(ys)) * scale, 2),
                                round((b[2] - x0) * scale, 2), round((b[3] - min(ys)) * scale, 2)]
                               for b in (l["bbox_px"] for l in letters)],
        "advance_norm": round((max(xs) - x0) * scale, 2),
        "stem_norm": None,
    }
    # 字干宽度（取第一条 U 左侧竖干的水平游程中位数）
    g0 = groups[0]
    midy = (min(p[1] for p in g0) + max(p[1] for p in g0)) // 2
    row = sorted(x for (x, y) in g0 if y == midy)
    runs, run, prev = [], 1, row[0]
    for x in row[1:]:
        if x == prev + 1:
            run += 1
        else:
            runs.append(run)
            run = 1
        prev = x
    runs.append(run)
    stem_px = sorted(runs)[len(runs) // 2]
    stats["stem_norm"] = round(stem_px * scale, 2)
    stats["cap_units"] = CAP
    return letters, stats


WORDMARK, WM_STATS = build_wordmark()


def wm_path_d(letters=None, prec=2):
    letters = letters or WORDMARK
    out = []
    for l in letters:
        p = l["pts"]
        out.append("M" + " L".join(f"{x:.{prec}f},{y:.{prec}f}" for x, y in p) + " Z")
    return " ".join(out)


def wm_svg(color="currentColor"):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" '
            f'preserveAspectRatio="xMinYMin meet" width="100" height="100">'
            f'<g transform="translate(0,0)"><path fill="{color}" fill-rule="nonzero" d="{wm_path_d()}"/></g></svg>')


# ── 自绘单线候选（对比用）：同标志的等宽单线语言 ──
def monoline_wordmark():
    """U T v T U，笔宽 = cap×0.125（与标志 ÷8 同比例），圆头圆接。"""
    w = CAP * 0.125
    r = w / 2
    cap = CAP
    x = 0.0
    g = []
    adv = {}

    def U(x0):
        bw, top, bot = cap * 0.72, 0.0, cap - r
        g.append(("line", (x0 + r, top), (x0 + r, bot - (bw / 2 - r)), w))
        g.append(("line", (x0 + bw - r, top), (x0 + bw - r, bot - (bw / 2 - r)), w))
        rr = (bw - w) / 2
        n = 24
        pts = [(x0 + bw / 2 - rr * math.cos(math.pi * i / n), bot - rr + rr * math.sin(math.pi * i / n))
               for i in range(n + 1)]
        for i in range(len(pts) - 1):
            g.append(("line", pts[i], pts[i + 1], w))
        return bw

    def T(x0):
        bw, top = cap * 0.78, r
        g.append(("line", (x0 + r, top), (x0 + bw - r, top), w))
        g.append(("line", (x0 + bw / 2, top), (x0 + bw / 2, cap - r), w))
        return bw

    def v(x0):
        bw, xh = cap * 0.62, cap * 0.62
        g.append(("line", (x0 + r, cap - xh + r), (x0 + bw / 2, cap - r), w))
        g.append(("line", (x0 + bw / 2, cap - r), (x0 + bw - r, cap - xh + r), w))
        return bw

    for fn, gapk in ((U, 0.10), (T, 0.12), (v, 0.12), (T, 0.12), (U, 0.0)):
        adv[fn.__name__] = x
        wdt = fn(x)
        x += wdt + cap * gapk
    return g, x


MONO, MONO_W = monoline_wordmark()


# ══════════════════════════════════════════════════════════════════
# 2. 量的度量（真实像素）
# ══════════════════════════════════════════════════════════════════
def render_wordmark(cap_px, color, letters=None):
    """把归一化（cap=100）的字标渲染成 cap_px 高的位图。"""
    letters = letters or WORDMARK
    scale = cap_px / CAP
    w = int(max(p[0] for l in letters for p in l["pts"]) * scale) + 8
    h = int(cap_px) + 8
    ss = 8
    img = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rgb = G.hex2rgb(color) + (255,) if color.startswith("#") else (20, 20, 24, 255)
    for l in letters:
        pts = [(x * scale * ss + 4 * ss, y * scale * ss + 4 * ss) for x, y in l["pts"]]
        d.polygon(pts, fill=rgb)
    return img.resize((w, h), Image.LANCZOS)


def render_mono(cap_px, color=None):
    scale = cap_px / CAP
    pad = 4
    w = int(MONO_W * scale) + pad * 2
    h = int(cap_px) + pad * 2
    ss = 8
    img = Image.new("RGBA", (w * ss, h * ss), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rgb = (20, 20, 24, 255)
    for kind, a, b, sw in MONO:
        wpx = max(1, int(round(sw * scale * ss)))
        d.line([(a[0] * scale * ss + pad * ss, a[1] * scale * ss + pad * ss),
                (b[0] * scale * ss + pad * ss, b[1] * scale * ss + pad * ss)], fill=rgb, width=wpx)
        for (x, y) in (a, b):
            cx, cy = x * scale * ss + pad * ss, y * scale * ss + pad * ss
            rr = wpx / 2
            d.ellipse([cx - rr, cy - rr, cx + rr, cy + rr], fill=rgb)
    return img.resize((w, h), Image.LANCZOS)


def wm_metrics(cap_px, mono=False):
    img = render_mono(cap_px) if mono else render_wordmark(cap_px, "#141418")
    m = G.mask_of(img)
    return {"cap": cap_px, "components": len(G.components(m)), "ink_pct": round(
        100.0 * sum(sum(r) for r in m) / (img.width * img.height), 1),
        "stroke_px": G.median_stroke_px(m), "min_gap_px": G.min_gap_px(m),
        "width_px": img.width}


# ══════════════════════════════════════════════════════════════════
# 3. 锁定组装
# ══════════════════════════════════════════════════════════════════
BRA = G.brace()          # 64 网格里的括号几何


def ink_bbox_units(glyphs, size=512):
    img = G.render(glyphs, size, "#000000")
    m = G.mask_of(img)
    xs = [x for y in range(size) for x in range(size) if m[y][x]]
    ys = [y for y in range(size) for x in range(size) if m[y][x]]
    if not xs:
        return None
    u = size / 64.0
    return [min(xs) / u, min(ys) / u, (max(xs) + 1) / u, (max(ys) + 1) / u]


MARK_BB = ink_bbox_units(G.MASTER)
BRA_BB = ink_bbox_units(BRA)
MARK_W = 64.0
BRA_W = 64.0

LOCKUPS = {                      # 名称 → (含标志?, 含字标?)
    "signature": (True, True),   # { 脸 UTvTU }
    "display": (False, True),    # { UTvTU }
    "short": (True, False),      # { 脸 }
}
FINISHES = {
    "ink":      {"brace": "#1C1B22", "mark": "#1C1B22", "word": "#1C1B22", "bg": "#FCF8FF"},
    "reversed": {"brace": "#FFFFFF", "mark": "#FFFFFF", "word": "#FFFFFF", "bg": "#0D0B14"},
    "branded":  {"brace": "#5A44E0", "mark": "#1C1B22", "word": "#1C1B22", "bg": "#FCF8FF"},
}


def compose(name, finish, cap_px, ss=8, bg=None):
    """按规范组装：标志墨迹高 = cap；括号墨迹 = 1.2×cap；都居中于大写带。"""
    has_mark, has_word = LOCKUPS[name]
    fin = FINISHES[finish]
    gap = cap_px * 0.42
    mark_h = cap_px
    brace_h = cap_px * 1.2
    mark_scale = mark_h / (MARK_BB[3] - MARK_BB[1])
    brace_scale = brace_h / (BRA_BB[3] - BRA_BB[1])
    mark_w = (MARK_BB[2] - MARK_BB[0]) * mark_scale
    brace_w = (BRA_BB[2] - BRA_BB[0]) * brace_scale
    word_w = max(p[0] for l in WORDMARK for p in l["pts"]) * cap_px / CAP
    total = 2 * brace_w + 2 * gap + (mark_w if has_mark else 0) + (gap if has_mark and has_word else 0) + (word_w if has_word else 0)
    W = int(total) + 16
    H = int(brace_h) + 16
    # 部件各自已超采样渲染；这里按 1× 合成（早期版本在 8× 画布上用 1× 坐标 ⇒ 内容缩到角落）
    canvas = Image.new("RGBA", (W, H), G.hex2rgb(bg or fin["bg"]) + (255,))
    cy = H / 2.0
    x = 8.0

    def paste_part(img_part, color, target_h, at, y_center):
        sc = target_h / img_part.height
        nw, nh = max(1, int(img_part.width * sc)), max(1, int(target_h))
        p = img_part.resize((nw, nh), Image.LANCZOS)
        canvas.alpha_composite(p, (int(at), int(y_center - nh / 2.0)))
        return nw

    mark_img = G.render(G.MASTER, 256, fin["mark"], bg=(0, 0, 0, 0))
    brace_img = G.render(BRA, 256, fin["brace"], bg=(0, 0, 0, 0)).crop(
        (int(BRA_BB[0] / 64 * 256), int(BRA_BB[1] / 64 * 256),
         int(BRA_BB[2] / 64 * 256), int(BRA_BB[3] / 64 * 256)))
    # 括号：上
    x += paste_part(brace_img, fin["brace"], int(brace_h), x, cy)
    x += gap
    if has_mark:
        # 标志墨迹需与 cap 带对齐：渲染图含整 64 网格，先裁到墨迹再缩放
        crop = mark_img.crop((int(MARK_BB[0] / 64 * 256), int(MARK_BB[1] / 64 * 256),
                             int(MARK_BB[2] / 64 * 256), int(MARK_BB[3] / 64 * 256)))
        x += paste_part(crop, fin["mark"], int(mark_h), x, cy)
        x += gap
    if has_word:
        wm = render_wordmark(cap_px, fin["word"])
        sc = cap_px / WM_STATS["source_px"]["cap"] * (WM_STATS["source_px"]["cap"] / CAP)
        canvas.alpha_composite(wm, (int(x) - 4, int(cy - cap_px / 2.0 - 4)))
        x += wm.width - 8
        x += gap
    x -= gap
    x += gap
    x += paste_part(brace_img, fin["brace"], int(brace_h), x, cy)
    return canvas.resize((W, H), Image.LANCZOS)


def lockup_svg(name, finish):
    """输出 SVG（标志/括号用描边几何，字标用描边轮廓），带对齐变换。"""
    fin = FINISHES[finish]
    has_mark, has_word = LOCKUPS[name]
    gap = 42.0
    mark_h, brace_h = CAP, CAP * 1.2
    mk = G.svg_stroke(G.MASTER, color=fin["mark"]).replace(
        'viewBox="0 0 64 64" width="64" height="64"', 'viewBox="0 0 64 64"')
    br = G.svg_stroke(BRA, color=fin["brace"]).replace(
        'viewBox="0 0 64 64" width="64" height="64"', 'viewBox="0 0 64 64"')
    # 用 group transform 把每个部件摆到 (x, 基线) —— 数字由归一化几何算出
    mark_scale = mark_h / (MARK_BB[3] - MARK_BB[1])
    brace_scale = brace_h / (BRA_BB[3] - BRA_BB[1])
    mark_w = (MARK_BB[2] - MARK_BB[0]) * mark_scale
    brace_w = (BRA_BB[2] - BRA_BB[0]) * brace_scale
    word_w = max(p[0] for l in WORDMARK for p in l["pts"])
    total = 2 * brace_w + 2 * gap + (mark_w + gap if has_mark else 0) + (word_w if has_word else 0)
    H = brace_h + 40
    top = (H - brace_h) / 2
    parts, x = [], 40.0
    parts.append(f'<g transform="translate({x - BRA_BB[0]*brace_scale:.2f},{top - BRA_BB[1]*brace_scale:.2f}) '
                 f'scale({brace_scale:.4f})">{br}</g>')
    x += brace_w + gap
    if has_mark:
        my = (H - mark_h) / 2 - MARK_BB[1] * mark_scale
        parts.append(f'<g transform="translate({x - MARK_BB[0]*mark_scale:.2f},{my:.2f}) scale({mark_scale:.4f})">{mk}</g>')
        x += mark_w + gap
    if has_word:
        wy = (H - CAP) / 2
        parts.append(f'<g transform="translate({x:.2f},{wy:.2f}) scale({CAP/100:.4f})">'
                     f'<path fill="{fin["word"]}" d="{wm_path_d()}"/></g>')
        x += word_w + gap
    parts.append(f'<g transform="translate({x - BRA_BB[0]*brace_scale:.2f},{top - BRA_BB[1]*brace_scale:.2f}) '
                 f'scale({brace_scale:.4f})">{br}</g>')
    x += brace_w
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {x + 40:.0f} {H:.0f}" '
            f'width="{x + 40:.0f}" height="{H:.0f}">' + "".join(parts) + "</svg>")


def sheet(rows, path, title, dark=False, cell=(760, 200)):
    bg = C["surface_dark"] if dark else C["surface_light"]
    fg = C["on_surface_dark"] if dark else C["on_surface_light"]
    sub = C["on_surface_variant_dark"] if dark else C["on_surface_variant_light"]
    W = cell[0] * 3 + 40
    H = 90 + cell[1] * len(rows) + 20
    img = Image.new("RGB", (W, H), bg)
    d = ImageDraw.Draw(img)
    d.text((20, 20), title, font=FS(20), fill=fg)
    for i, (label, imgs, note) in enumerate(rows):
        y = 80 + i * cell[1]
        d.text((20, y + 4), label, font=FS(15), fill=fg)
        d.text((20, y + 26), note, font=FS(11), fill=sub)
        for j, im in enumerate(imgs):
            cx = 380 + j * 300
            img.paste(im, (cx, y + (cell[1] - im.height) // 2), im)
        d.line([(20, y + cell[1] - 6), (W - 20, y + cell[1] - 6)], fill=sub)
    img.save(path)
    return path


def main():
    data = {"wordmark": {"stats": WM_STATS,
                         "metrics": {f"cap{c}": {"outline": wm_metrics(c), "monoline": wm_metrics(c, mono=True)}
                                     for c in (12, 14, 16, 20, 28, 48)},
                         "source": "traced from sections/02-wordmark.png (Roboto Bold 渲染图) —— 仓库/系统均无 Roboto 字体文件"},
            "lockups": {}, "finishes": {}}

    # ── A. 字标资产 ──
    def w(name, text):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(text)

    w("utvtu-wordmark.svg", wm_svg(C["ink"]))
    w("utvtu-wordmark-mono.svg", wm_svg("currentColor"))
    w("utvtu-wordmark-monoline.svg",
      G.svg_stroke([("line", (a[0], a[1], b[0], b[1]), sw) for _k, a, b, sw in MONO], color="currentColor")
      .replace('viewBox="0 0 64 64" width="64" height="64"', f'viewBox="0 0 {MONO_W:.1f} {CAP:.0f}"'))
    mono_d = " ".join(f"M{a[0]:.1f},{a[1]:.1f} L{b[0]:.1f},{b[1]:.1f}" for _k, a, b, _sw in MONO)
    w("utvtu-brand-lockup.axaml",
      "<!-- W39 字标与锁定几何。\n"
      "     字标（描摹轮廓）：归一化空间 = 大写高 100 单位、基线 y=100、左缘 x=0，用 Fill 填充：\n"
      "       <Path Data=\"{StaticResource brand-wordmark}\" Fill=\"{DynamicResource brand.ink}\"\n"
      "             Width=\"{cap * W/100}\" Height=\"{cap}\" Stretch=\"Uniform\"/>\n"
      "     自绘单线备选：同一归一化空间，用 Stroke（StrokeThickness = cap × 0.125，圆头圆接）。\n"
      "     标志/括号几何见 utvtu-brand-geometry.axaml（64 网格）。 -->\n"
      f'<StreamGeometry x:Key="brand-wordmark">{wm_path_d(prec=2)}</StreamGeometry>\n'
      f'<StreamGeometry x:Key="brand-wordmark-monoline">{mono_d}</StreamGeometry>\n')

    # ── B. 三锁定 × 三 finish ──
    rows = []
    for name in LOCKUPS:
        imgs = []
        for fin in FINISHES:
            im = compose(name, fin, 44)
            imgs.append(im)
            for tag, cap in (("1x", 24), ("2x", 48)):
                compose(name, fin, cap).save(os.path.join(OUT, f"utvtu-lockup-{name}-{fin}@{tag}.png"))
            w(f"utvtu-lockup-{name}-{fin}.svg", lockup_svg(name, fin))
            data["lockups"][f"{name}-{fin}"] = {"svg": f"utvtu-lockup-{name}-{fin}.svg"}
        rows.append((f"{{ 脸 UTvTU }}".replace("{ 脸 UTvTU }", {"signature": "{ 脸 UTvTU }", "display": "{ UTvTU }", "short": "{ 脸 }"}[name]),
                     imgs, "finish：ink · reversed · branded（括号品牌色）"))
    p1 = sheet(rows, os.path.join(OUT, "brand-lockup-sheet.png"), "W39 三种锁定 × 三 finish（cap 44px 真实渲染）")

    # ── C. 应用内变体 + 尺寸阶梯 ──
    ladder_rows = []
    for cap in (14, 16, 20, 28):
        imgs = []
        for name in ("signature", "display", "short"):
            im = compose(name, "ink", cap)
            imgs.append(im)
        imgs.append(render_wordmark(cap, C["ink"]))
        ladder_rows.append((f"cap {cap}px", imgs, "「{ 脸 UTvTU }」「{ UTvTU }」「{ 脸 }」+ 纯字标"))
    p2 = sheet(ladder_rows, os.path.join(OUT, "brand-lockup-ladder.png"), "W39 尺寸阶梯（顶栏 14–16 / 欢迎页头 20 / 关于页 28）")

    # ── 字标对比图：描摹轮廓 vs 自绘单线 ──
    cmp_rows = []
    for cap in (12, 14, 16, 20, 28, 48):
        cmp_rows.append((f"cap {cap}px",
                         [render_wordmark(cap, C["ink"]), render_mono(cap)],
                         f"轮廓：{wm_metrics(cap)['components']} 域 · 笔宽 {wm_metrics(cap)['stroke_px']}px    "
                         f"单线：{wm_metrics(cap, True)['components']} 域 · 笔宽 {wm_metrics(cap, True)['stroke_px']}px"))
    p3 = sheet(cmp_rows, os.path.join(OUT, "brand-wordmark-compare.png"),
               "W39 字标：品牌板描摹轮廓（左） vs 自绘单线（右）", cell=(760, 170))

    for p in (p1, p2, p3):
        base = os.path.basename(p)
        import shutil
        shutil.copyfile(p, os.path.join(SHOTS, base))

    with open(os.path.join(OUT, "brand-lockup-metrics.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)

    print("wordmark stats:", json.dumps(WM_STATS, ensure_ascii=False))
    for cap in (14, 16, 20, 28):
        o, mo = wm_metrics(cap), wm_metrics(cap, True)
        print(f"cap {cap:2d}: 轮廓 {o['components']}域 笔宽{o['stroke_px']}px 墨迹{o['ink_pct']}% | "
              f"单线 {mo['components']}域 笔宽{mo['stroke_px']}px 墨迹{mo['ink_pct']}%")
    print("lockup 资产：", len([1 for f in os.listdir(OUT) if f.startswith("utvtu-lockup")]), "个")


if __name__ == "__main__":
    main()
