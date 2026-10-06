"""W37 品牌资产生成器（一次性脚本，可重跑）。

输入：品牌规范（64 网格 / 描边=尺寸÷8 / 括号墨迹 24×44 装 28×48 描边=高÷6）
输出：`.
  .opencode/design/brand/out/` 下的 SVG / XAML / ICO / 闪屏 / 对比图 / 度量 JSON
几何全部以 **64 网格单位**定义（原点 = 设计框左上角），由 W37 量测反解（1 单位 = 6px @384px）。
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

BRAND = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
OUT = os.path.join(BRAND, "out")
SHOTS = r"G:\xklmy文件夹\vibe coding\UTvTU\.dsh\fx\shots"
os.makedirs(OUT, exist_ok=True)
os.makedirs(SHOTS, exist_ok=True)

# ── 品牌色（README 令牌表）──
C = {
    "primary_light": "#5A44E0", "primary_dark": "#C4B4FF",
    "on_primary_light": "#FFFFFF", "on_primary_dark": "#24005E",
    "primary_container_light": "#E5DEFF", "primary_container_dark": "#4029C6",
    "on_primary_container_light": "#1B0B5E", "on_primary_container_dark": "#E5DEFF",
    "accent_light": "#00A892", "accent_dark": "#3FDCC4",
    "surface_light": "#FCF8FF", "surface_dark": "#131218",
    "surface_container_light": "#F3EEFB", "surface_container_dark": "#1F1D26",
    "surface_variant_light": "#E7E0F0", "surface_variant_dark": "#2B2933",
    "on_surface_light": "#1C1B22", "on_surface_dark": "#E7E1E9",
    "on_surface_variant_light": "#48454F", "on_surface_variant_dark": "#CAC4D4",
    "outline_light": "#79747E", "outline_dark": "#948F99",
    "outline_variant_light": "#CAC4D4", "outline_variant_dark": "#49454F",
    "ink": "#0D0B14",
}

# ══════════════════════════════════════════════════════════════════
# 几何：64 网格单位。笔宽 8（= 尺寸÷8）。由量测反解：
#   T 左 bbox x4..28 y9..33；T 右 x36..60；v x23..41 y21..39；U 左 x5..27 y37..55；
#   U 右 x37..59；描边 48px @384px ⇒ 8 单位；1 单位 = 6px。
# ══════════════════════════════════════════════════════════════════
W = 8.0  # 主笔宽（单位）

MASTER = [
    ("line", (4, 13, 28, 13), W),          # T 左：横杠（眼）
    ("line", (16, 13, 16, 29), W),         # T 左：竖干（泪流）
    ("line", (36, 13, 60, 13), W),         # T 右：横杠
    ("line", (48, 13, 48, 29), W),         # T 右：竖干
    ("poly", [(27, 25), (32, 35), (37, 25)], W),   # v：嘴
    ("u", (9, 41, 23, 44), W),             # U 左：泪滴（两臂 + 半圆底）
    ("u", (41, 41, 55, 44), W),            # U 右
]

# ① 小尺寸简化变体
SMALL_A = [  # TvT（去泪滴）—— 整体垂直居中：原墨水 y9..39 → 平移到 17..47
    ("line", (4, 21, 28, 21), W),
    ("line", (16, 21, 16, 37), W),
    ("line", (36, 21, 60, 21), W),
    ("line", (48, 21, 48, 37), W),
    ("poly", [(27, 33), (32, 43), (37, 33)], W),
]
SMALL_B = [  # 单眼 + 嘴（去第二只眼与两滴泪）
    ("line", (20, 9, 44, 9), W),
    ("line", (32, 9, 32, 25), W),
    ("poly", [(27, 33), (32, 43), (37, 33)], W),
]
SMALL_C = [  # 我的方案：TvT + 单泪（泪移到中轴下方）
    ("line", (4, 9, 28, 9), W),
    ("line", (16, 9, 16, 25), W),
    ("line", (36, 9, 60, 9), W),
    ("line", (48, 9, 48, 25), W),
    ("poly", [(27, 25), (32, 35), (37, 25)], W),
    ("drop", (32, 55, 14, 7.0)),           # 中轴单泪：滴形
]

# ── 实测驱动的「小尺寸光学档」：主标志在 32px 只剩 4 个连通域（v 臂与 T 干粘连），
#    16px 全部并成 1 团 ⇒ 必须为 ≤32px 单独绘制：字形放大 + 间距 ≥12 单位。
T32 = [
    ("line", (2, 15, 26, 15), W),
    ("line", (14, 15, 14, 31), W),
    ("line", (38, 15, 62, 15), W),
    ("line", (50, 15, 50, 31), W),
    ("poly", [(28, 28), (32, 36), (36, 28)], W),
    ("drop", (14, 52, 13, 6.5)),
]
T16 = [
    ("poly", [(24, 12), (32, 24), (40, 12)], W),
    ("drop", (32, 57, 16, 8.0)),
]
T16B = [
    ("line", (6, 20, 26, 20), W),
    ("line", (16, 20, 16, 34), W),
    ("line", (38, 20, 58, 20), W),
    ("line", (48, 20, 48, 34), W),
]

# ② 泪滴位置变体（均保持 TvT 主脸）
TEAR_A = MASTER                            # 对照：U 在竖干正下方
TEAR_B = [g for g in MASTER if g[0] != "u"] + [  # 移到外眼角（横杠外端下方）成"滴"形
    ("line", (7, 17, 7, 26), 3.5),         # 细连接（眼角 → 滴）
    ("drop", (7, 38, 15, 7.5)),
    ("drop", (57, 38, 15, 7.5)),
]
TEAR_C = [g for g in MASTER if g[0] != "u"] + [  # 维持 U 位置 + 细连接线
    ("line", (16, 33, 16, 40), 4.0),
    ("line", (48, 33, 48, 40), 4.0),
    ("u", (9, 41, 23, 44), W),
    ("u", (41, 41, 55, 44), W),
]

# 括号：墨迹 24×44 装 28×48，描边 = 高÷6。在 64 网格里按 48 单位高居中放置。
def brace():
    h = 48.0                     # 框高（单位）
    sw = h / 6.0                 # 描边 = 高÷6 = 8
    x0 = (64 - 28) / 2.0         # 框左
    y0 = (64 - h) / 2.0          # 框上
    mx = x0 + 9.0                # 主竖干中心线（偏右）
    nub = x0 + 1.0 + sw / 2      # 中突左端
    top = y0 + sw / 2 + 1
    bot = y0 + h - sw / 2 - 1
    mid = y0 + h / 2
    r = 5.0
    return [
        ("arc", (mx, top + r, r), 180, 360, sw),
        ("line", (mx, top + r, mx, mid - r), sw),
        ("arc", (nub + r, mid, r), 90, 270, sw),
        ("line", (nub + r, mid, mx - r * 0.2, mid), sw),
        ("arc", (mx, bot - r, r), 0, 180, sw),
        ("line", (mx, mid + r, mx, bot - r), sw),
    ]

# ══════════════════════════════════════════════════════════════════
# 光学补偿（用户裁决：**字形拓扑不动**，只允许调笔宽比 / 字距 / 描边→填充）
#   → 同一套 5 字形 + 对称两滴，仅把"笔宽相对比例"放细、把关键空隙拉开。
# ══════════════════════════════════════════════════════════════════
def rescale_stroke(glyphs, w):
    """把所有笔画的笔宽改成 w（几何中心线不动）。"""
    out = []
    for g in glyphs:
        if g[0] in ("line", "poly"):
            out.append((g[0], g[1], w))
        elif g[0] == "arc":
            out.append(("arc", g[1], g[2], g[3], w))
        elif g[0] == "u":
            out.append(("u", g[1], w))
        else:
            out.append(g)
    return out


def reflow(glyphs, v_inset=0.0, v_up=0.0, u_down=0.0):
    """字距微调：v 收窄（v_inset，两侧各收）/ 上移（v_up）、两滴 U 下移（u_down）。
    注意：**保留每个字形自己的笔宽**（早期版本这里默认 w=8，把"细笔"补偿整个吃掉 —— 已修）。"""
    out = []
    for g in glyphs:
        if g[0] == "poly":                       # v 嘴
            pts = [(x + (v_inset if x < 32 else -v_inset), y - v_up) for x, y in g[1]]
            out.append(("poly", pts, g[2]))
        elif g[0] == "u":                        # 两滴 U
            x1, ytop, x2, yc = g[1]
            out.append(("u", (x1, ytop + u_down, x2, yc + u_down), g[2]))
        elif g[0] == "line":
            out.append(("line", g[1], g[2]))
        else:
            out.append(g)
    return out


OC_A = rescale_stroke(MASTER, 6.0)                                        # 只放细笔宽（÷10.7）
OC_B = reflow(rescale_stroke(MASTER, 6.0), v_inset=1.0, v_up=1.0, u_down=2.0)   # 细笔 + 微距
OC_C = reflow(rescale_stroke(MASTER, 5.0), v_inset=2.0, v_up=1.0, u_down=3.0)   # 更细 + 更大间距
# OC_D：只针对"32px 的绑定约束"——v 臂与 T 干之间那条 3 单位缝隙，不动 U 的位置
OC_D = reflow(rescale_stroke(MASTER, 6.0), v_inset=1.5, v_up=1.0, u_down=0.0)

# ══════════════════════════════════════════════════════════════════
# 渲染：8× 超采样 → BOX/LANCZOS 降采样（真实像素）
# ══════════════════════════════════════════════════════════════════
SS = 8


def hex2rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def render(glyphs, size, color, bg=None, ss=SS, pad_units=0.0, box_units=64.0):
    """把 64 网格几何渲染成 size×size 的真实像素图（RGBA）。"""
    scale = size * ss / box_units
    canvas = Image.new("RGBA", (size * ss, size * ss), bg if bg else (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)
    rgb = hex2rgb(color) + (255,)
    off = pad_units * scale

    def P(x, y):
        return (x * scale + off, y * scale + off)

    def stroke(pts, w):
        wpx = max(1, int(round(w * scale)))
        for i in range(len(pts) - 1):
            d.line([P(*pts[i]), P(*pts[i + 1])], fill=rgb, width=wpx)
        r = wpx / 2.0
        for (x, y) in pts:
            cx, cy = P(x, y)
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=rgb)

    for g in glyphs:
        kind = g[0]
        if kind == "line":
            _, (x1, y1, x2, y2), w = g
            stroke([(x1, y1), (x2, y2)], w)
        elif kind == "poly":
            _, pts, w = g
            stroke(pts, w)
        elif kind == "arc":
            _, (cx, cy, r), a0, a1, w = g
            n = max(8, int(abs(a1 - a0) / 6))
            pts = [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
                    cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]
            stroke(pts, w)
        elif kind == "u":                      # 两臂 + 半圆底
            _, (x1, ytop, x2, yc), w = g
            r = (x2 - x1) / 2.0
            stroke([(x1, ytop), (x1, yc)], w)
            stroke([(x2, ytop), (x2, yc)], w)
            n = 24
            pts = [(x1 + r - r * math.cos(math.pi * i / n), yc + r * math.sin(math.pi * i / n)) for i in range(n + 1)]
            stroke(pts, w)
        elif kind == "drop":                   # 泪滴：上尖下圆
            _, (cx, cy, wd, r) = g
            rr = r * scale
            ccy = cy * scale + off
            ccx = cx * scale + off
            d.ellipse([ccx - rr, ccy - rr * 0.6, ccx + rr, ccy + rr * 1.4], fill=rgb)
            h = wd * 1.1 * scale
            d.polygon([P(cx, cy - h * 0.75), P(cx - rr * 0.98, ccy), P(cx + rr * 0.98, ccy)], fill=rgb)
    return canvas.resize((size, size), Image.LANCZOS)


# ══════════════════════════════════════════════════════════════════
# 度量：连通域 / 墨迹占比 / 1px 腐蚀后是否仍分离
# ══════════════════════════════════════════════════════════════════
def mask_of(img, thr=110):
    a = img.split()[-1]
    return [[1 if a.getpixel((x, y)) >= thr else 0 for x in range(img.width)] for y in range(img.height)]


def components(m, conn=8):
    h, w = len(m), len(m[0])
    seen = [[0] * w for _ in range(h)]
    sizes = []
    for y0 in range(h):
        for x0 in range(w):
            if not m[y0][x0] or seen[y0][x0]:
                continue
            stack, n = [(x0, y0)], 0
            seen[y0][x0] = 1
            while stack:
                x, y = stack.pop()
                n += 1
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        if not conn and abs(dx) + abs(dy) == 2:
                            continue
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and m[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = 1
                            stack.append((nx, ny))
            sizes.append(n)
    return sorted(sizes, reverse=True)


def erode(m):
    h, w = len(m), len(m[0])
    out = [[0] * w for _ in range(h)]
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            if m[y][x] and all(m[y + dy][x + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1)):
                out[y][x] = 1
    return out


def measure(glyphs, size, color="#000000"):
    img = render(glyphs, size, color)
    m = mask_of(img)
    comps = components(m)
    ink = sum(sum(r) for r in m)
    gap = min_gap_px(m)
    sw = median_stroke_px(m)
    return {"size": size, "components": len(comps), "component_px": comps[:6],
            "ink_pct": round(100.0 * ink / (size * size), 2),
            "min_gap_px": gap, "stroke_px": sw,
            "legible": len(comps) >= 2 and gap >= 1.5 and sw >= 2}


def comp_pixels(m):
    """每个连通域的像素列表（供最小间距计算）。"""
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
            out.append(pts)
    return sorted(out, key=len, reverse=True)


def min_gap_px(m):
    """最近两个连通域之间的最小欧氏间距（px）—— 可辨性硬指标。"""
    groups = comp_pixels(m)
    if len(groups) < 2:
        return 0.0
    best = 1e9
    for i in range(len(groups)):
        for j in range(i + 1, len(groups)):
            for (x1, y1) in groups[i]:
                for (x2, y2) in groups[j]:
                    d = ((x1 - x2) ** 2 + (y1 - y2) ** 2) ** 0.5
                    if d < best:
                        best = d
                        if best <= 1.0:
                            return 1.0
    return round(best, 2)


def median_stroke_px(m):
    """游程中位数 ≈ 主笔宽（px）。"""
    runs = []
    for row in m:
        run = 0
        for v in row + [0]:
            if v:
                run += 1
            elif run:
                runs.append(run)
                run = 0
    runs.sort()
    return runs[len(runs) // 2] if runs else 0


# ══════════════════════════════════════════════════════════════════
# 对比图
# ══════════════════════════════════════════════════════════════════
def font(sz):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\segoeui.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def sheet(rows, path, sizes=(16, 24, 32, 48, 64), title="", note_col=True, dark=False):
    """rows = [(label, glyphs, note)] —— 每行：标签 + 各真实尺寸 + 16px 放大 8× + 度量"""
    bg = C["surface_dark"] if dark else C["surface_light"]
    fg = C["on_surface_dark"] if dark else C["on_surface_light"]
    sub = C["on_surface_variant_dark"] if dark else C["on_surface_variant_light"]
    ink = C["primary_dark"] if dark else C["primary_light"]
    pad, label_w = 20, 250
    zoom = 8
    col_w = [max(s, 64) + 24 for s in sizes] + [16 * zoom + 24, 250]
    W_img = label_w + sum(col_w) + pad
    row_h = [96] * len(rows)
    H_img = 64 + sum(row_h) + pad
    img = Image.new("RGB", (W_img, H_img), bg)
    d = ImageDraw.Draw(img)
    d.text((pad, 16), title, font=font(20), fill=fg)
    x = label_w
    for i, s in enumerate(sizes):
        d.text((x + 12, 40), f"{s}px", font=font(13), fill=sub)
        x += col_w[i]
    d.text((x + 12, 40), "16px ×8（放大看像素）", font=font(13), fill=sub)
    x += col_w[-2]
    if note_col:
        d.text((x + 12, 40), "度量（真实像素）", font=font(13), fill=sub)

    y = 64
    for label, glyphs, note in rows:
        d.text((pad, y + 8), label, font=font(15), fill=fg)
        x = label_w
        for i, s in enumerate(sizes):
            g = render(glyphs, s, ink)
            img.paste(g, (x + (col_w[i] - s) // 2, y + (88 - s) // 2), g)
            x += col_w[i]
        z = render(glyphs, 16, ink).resize((16 * zoom, 16 * zoom), Image.NEAREST)
        img.paste(z, (x + 12, y + (88 - 16 * zoom) // 2), z)
        x += col_w[-2]
        if note_col:
            d.multiline_text((x + 12, y + 10), note, font=font(12), fill=sub, spacing=3)
        d.line([(pad, y + 88), (W_img - pad, y + 88)], fill=C["outline_variant_dark"] if dark else C["outline_variant_light"])
        y += row_h[0]
    img.save(path)
    return path


def metrics_note(glyphs, expected=5):
    ms = [measure(glyphs, s) for s in (16, 24, 32, 48, 64)]
    m16, m24, m32 = ms[0], ms[1], ms[2]
    def line(m):
        return (f"{m['size']}px: 分离 {m['components']}/{expected} 字形 · 墨迹 {m['ink_pct']}% · "
                f"间距 {m['min_gap_px']}px · 笔宽 {m['stroke_px']}px"
                f" → {'全分离 OK' if m['components'] >= expected else ('部分可辨' if m['components'] >= 3 else '粘连 NG')}")
    return line(m16) + "\n" + line(m24) + "\n" + line(m32), ms


# ══════════════════════════════════════════════════════════════════
# SVG / XAML / ICO / 闪屏
# ══════════════════════════════════════════════════════════════════
def svg_stroke(glyphs, size=64, color="currentColor", sw_scale=1.0):
    parts = []
    for g in glyphs:
        kind = g[0]
        if kind == "line":
            _, (x1, y1, x2, y2), w = g
            parts.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke-width="{w*sw_scale}"/>')
        elif kind == "poly":
            _, pts, w = g
            p = " ".join(f"{x},{y}" for x, y in pts)
            parts.append(f'<polyline points="{p}" stroke-width="{w*sw_scale}"/>')
        elif kind == "arc":
            _, (cx, cy, r), a0, a1, w = g
            x1, y1 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
            x2, y2 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
            large = 1 if abs(a1 - a0) > 180 else 0
            parts.append(f'<path d="M {x1:.2f} {y1:.2f} A {r} {r} 0 {large} 1 {x2:.2f} {y2:.2f}" stroke-width="{w*sw_scale}"/>')
        elif kind == "u":
            _, (x1, ytop, x2, yc), w = g
            r = (x2 - x1) / 2
            parts.append(f'<path d="M {x1} {ytop} L {x1} {yc} A {r} {r} 0 0 0 {x2} {yc} L {x2} {ytop}" stroke-width="{w*sw_scale}"/>')
        elif kind == "drop":
            _, (cx, cy, wd, r) = g
            rr = r
            parts.append(f'<path d="M {cx} {cy-wd*0.75} L {cx-rr*0.98} {cy} A {rr} {rr*0.8} 0 1 0 {cx+rr*0.98} {cy} Z" stroke="none"/>')
    head = (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="{size}" height="{size}" '
            f'fill="none" stroke="{color}" stroke-width="8" stroke-linecap="round" stroke-linejoin="round">')
    return head + "".join(parts) + "</svg>"


def svg_filled(glyphs, size=64, color="currentColor"):
    """填充化：每条笔画展开成"体育场"轮廓（两条 180° 弧 + 两条边），nonzero 填充。"""
    paths = []
    def stadium(x1, y1, x2, y2, w):
        r = w / 2.0
        dx, dy = x2 - x1, y2 - y1
        L = math.hypot(dx, dy) or 1e-6
        nx, ny = -dy / L * r, dx / L * r
        return (f'M {x1+nx:.2f} {y1+ny:.2f} L {x2+nx:.2f} {y2+ny:.2f} '
                f'A {r} {r} 0 0 1 {x2-nx:.2f} {y2-ny:.2f} L {x1-nx:.2f} {y1-ny:.2f} '
                f'A {r} {r} 0 0 1 {x1+nx:.2f} {y1+ny:.2f} Z')
    for g in glyphs:
        kind = g[0]
        if kind == "line":
            _, (x1, y1, x2, y2), w = g
            paths.append(stadium(x1, y1, x2, y2, w))
        elif kind == "poly":
            _, pts, w = g
            for i in range(len(pts) - 1):
                paths.append(stadium(*pts[i], *pts[i + 1], w))
        elif kind == "arc":
            _, (cx, cy, r), a0, a1, w = g
            n = max(8, int(abs(a1 - a0) / 8))
            pts = [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
                    cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]
            for i in range(len(pts) - 1):
                paths.append(stadium(*pts[i], *pts[i + 1], w))
        elif kind == "u":
            _, (x1, ytop, x2, yc), w = g
            r = (x2 - x1) / 2
            paths.append(stadium(x1, ytop, x1, yc, w))
            paths.append(stadium(x2, ytop, x2, yc, w))
            n = 24
            pts = [(x1 + r - r * math.cos(math.pi * i / n), yc + r * math.sin(math.pi * i / n)) for i in range(n + 1)]
            for i in range(len(pts) - 1):
                paths.append(stadium(*pts[i], *pts[i + 1], w))
        elif kind == "drop":
            _, (cx, cy, wd, r) = g
            paths.append(f'M {cx} {cy-wd*0.75} L {cx-r*0.98} {cy} '
                         f'A {r} {r*0.8} 0 1 0 {cx+r*0.98} {cy} Z')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="{size}" height="{size}">'
            f'<path fill="{color}" fill-rule="nonzero" d="{" ".join(paths)}"/></svg>')


def xaml_snippet():
    def d_of(glyphs):
        cmds = []
        for g in glyphs:
            kind = g[0]
            if kind == "line":
                _, (x1, y1, x2, y2), _w = g
                cmds.append(f"M{x1},{y1} L{x2},{y2}")
            elif kind == "poly":
                _, pts, _w = g
                cmds.append("M" + " L".join(f"{x},{y}" for x, y in pts))
            elif kind == "u":
                _, (x1, ytop, x2, yc), _w = g
                r = (x2 - x1) / 2
                cmds.append(f"M{x1},{ytop} L{x1},{yc} A{r},{r} 0 0 0 {x2},{yc} L{x2},{ytop}")
            elif kind == "arc":
                _, (cx, cy, r), a0, a1, _w = g
                x1, y1 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
                x2, y2 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
                large = 1 if abs(a1 - a0) > 180 else 0
                cmds.append(f"M{x1:.1f},{y1:.1f} A{r},{r} 0 {large} 1 {x2:.1f},{y2:.1f}")
        return " ".join(cmds)
    return f'''<!-- W37 品牌标志几何（64 网格，描边 = 尺寸÷8）。用法：
     <Path Data="{{StaticResource brand-mark}}" Stroke="{{DynamicResource brand.primary}}"
           StrokeThickness="8" StrokeLineCap="Round" StrokeJoin="Round" Width="64" Height="64"
           Stretch="Uniform"/>  （尺寸任意，描边按"÷8"同步换算：Thickness = 显示尺寸 / 8） -->
<StreamGeometry x:Key="brand-mark">{d_of(MASTER)}</StreamGeometry>
<StreamGeometry x:Key="brand-mark-small">{d_of(SMALL_C)}</StreamGeometry>
<StreamGeometry x:Key="brand-brace">{d_of(brace())}</StreamGeometry>
'''


def tile(size, variant, finish="primary"):
    """App 图标瓦片：squircle + 反白标志。48px 以下自动用简化变体。"""
    bg, mk = {
        "primary": (C["primary_light"], "#FFFFFF"),
        "ink": (C["ink"], "#FFFFFF"),
        "tonal": (C["primary_container_light"], C["on_primary_container_light"]),
    }[finish]
    ss = 4
    s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(s * 0.2237)  # squircle 近似（iOS 圆角比例）
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=hex2rgb(bg) + (255,))
    inner = int(s * 0.78)
    mark = render(variant, inner, mk, ss=SS)
    ox = (s - inner) // 2
    img.paste(mark, (ox, ox), mark)
    return img.resize((size, size), Image.LANCZOS)


def splash(w, h, variant):
    img = Image.new("RGB", (w, h), C["surface_light"])
    d = ImageDraw.Draw(img)
    m = int(min(w, h) * 0.30)
    mark = render(variant, m, C["primary_light"], ss=SS)
    img.paste(mark, ((w - m) // 2, int(h * 0.30)), mark)
    b = render(brace(), int(m * 1.35), C["primary_light"], ss=SS)
    bs = b.width
    img.paste(b, (int(w * 0.5 - bs - m * 0.72), int(h * 0.30) - int(m * 0.17)), b)
    img.paste(b, (int(w * 0.5 + m * 0.72), int(h * 0.30) - int(m * 0.17)), b)
    d.text((int(w * 0.5 - 92), int(h * 0.68)), "UTvTU", font=font(int(h * 0.11)), fill=C["on_surface_light"])
    return img


# ══════════════════════════════════════════════════════════════════
# 对比度（WCAG 2.1）
# ══════════════════════════════════════════════════════════════════
def lum(h):
    def ch(v):
        v /= 255.0
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = hex2rgb(h)
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)


def contrast(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


CONTRAST_ROWS = [
    ("Primary 标志 / 浅底", C["primary_light"], C["surface_light"], "标志主色"),
    ("Primary 标志 / 浅卡片", C["primary_light"], C["surface_container_light"], "卡片上"),
    ("Primary 标志 / 深底", C["primary_dark"], C["surface_dark"], "深色主题"),
    ("Primary 标志 / 深卡片", C["primary_dark"], C["surface_container_dark"], "深色卡片"),
    ("反白标志 / Primary 底", "#FFFFFF", C["primary_light"], "主图标 finish"),
    ("反白标志 / Ink 底", "#FFFFFF", C["ink"], "Ink·dark finish"),
    ("深紫标志 / Tonal 底", C["on_primary_container_light"], C["primary_container_light"], "Tonal finish"),
    ("深色 Tonal 标志 / 深 Tonal 底", C["on_primary_container_dark"], C["primary_container_dark"], "深色 Tonal"),
    ("on-surface / surface（浅）", C["on_surface_light"], C["surface_light"], "正文"),
    ("on-surface / surface（深）", C["on_surface_dark"], C["surface_dark"], "正文"),
    ("Accent teal / 浅底", C["accent_light"], C["surface_light"], "图案点缀（不进 UI）"),
    ("Accent teal / 深底", C["accent_dark"], C["surface_dark"], "图案点缀（不进 UI）"),
]


def main():
    data = {"geometry_units": {"grid": 64, "stroke": W, "unit_px_at_384": 6.0,
                               "mark_ink_bbox_units": [4, 9, 60, 55]},
            "variants": {}, "contrast": []}

    # ══ 用户裁决（2026-10）：以原版几何为准 —— 5 字形完整脸 + 对称两滴 U。══
    #    唯一允许的调整是"光学补偿"：不改字形拓扑，只调笔宽比 / 字距 / 描边→填充。
    variants = {
        "ORIG_8": ("原版基线：笔宽 = 尺寸÷8（8 单位）", MASTER),
        "OC_A6": ("光学补偿 A：笔宽 ÷10.7（6 单位）", OC_A),
        "OC_B6_sp": ("光学补偿 B：6 单位 + 字距微增（v 收 2、U 下移 2）", OC_B),
        "OC_C5_sp": ("★光学补偿 C（推荐 ≤32px）：5 单位 + 字距再增（v 收 4、U 下移 3）", OC_C),
        "OC_D6_v": ("对照 D：6 单位 + 只拉 v↔T 干缝隙", OC_D),
    }
    # 附录数据（不入资产清单、不出图）：简化变体与泪滴位置备选的实测值
    appendix = {
        "A_TvT": ("附录①A 去泪滴 TvT", SMALL_A),
        "B_single_eye": ("附录①B 只留 v + 单 T", SMALL_B),
        "C_TvT_one_tear": ("附录①C TvT + 中轴单泪", SMALL_C),
        "TA_current": ("附录②A 对照：U 在竖干正下方", TEAR_A),
        "TB_outer_drop": ("附录②B 移到外眼角（滴形）", TEAR_B),
        "TC_connector": ("附录②C 维持位置 + 细连接线", TEAR_C),
        "T32_optical": ("附录 32px 光学档（TvT 扩距 + 单泪）", T32),
        "T16_twoeyes": ("附录 16px 两 T", T16B),
    }
    for key, (label, gl) in variants.items():
        data["variants"][key] = {"label": label, "expected_components": 5,
                                 "metrics": [measure(gl, s) for s in (16, 24, 32, 48, 64)]}
    data["appendix_simplified"] = {
        key: {"label": label, "metrics": [measure(gl, s) for s in (16, 24, 32, 48, 64)]}
        for key, (label, gl) in appendix.items()}

    # ── 对比图：光学补偿前后（原版几何）──
    order = ("ORIG_8", "OC_A6", "OC_B6_sp", "OC_C5_sp", "OC_D6_v")
    rows_optical = [(variants[k][0], variants[k][1], metrics_note(variants[k][1], expected=5)[0]) for k in order]
    p1 = sheet(rows_optical, os.path.join(OUT, "brand-optical-compensation-light.png"),
               title="W37 光学补偿前后 —— 原版几何不变，只调笔宽比/字距（真实像素 16/24/32/48/64）")
    p2 = sheet(rows_optical, os.path.join(OUT, "brand-optical-compensation-dark.png"), dark=True,
               title="W37 光学补偿前后（深底）")

    # 附录图（留档，不进资产清单）：简化变体与泪滴位置
    app_rows = [(appendix[k][0], appendix[k][1], metrics_note(appendix[k][1])[0])
                for k in ("A_TvT", "B_single_eye", "C_TvT_one_tear",
                          "TA_current", "TB_outer_drop", "TC_connector")]
    p3 = sheet(app_rows, os.path.join(OUT, "appendix-variants-light.png"),
               title="附录（用户已裁决维持原版，仅留档）：简化变体 + 泪滴位置备选")
    p4 = None

    # ── 图标阶梯（全部用原版几何；16px 作为已知取舍一起展示）──
    ladder_sizes = [256, 128, 64, 48, 32, 24, 16]
    finishes = ["primary", "ink", "tonal"]
    cellw, cellh = 300, 340
    li = Image.new("RGB", (cellw * len(ladder_sizes) + 40, cellh * 3 + 60), C["surface_light"])
    dl = ImageDraw.Draw(li)
    dl.text((20, 16), "W37 App 图标阶梯 —— 原版几何（5 字形 + 对称两滴），16px 为已知取舍",
            font=font(20), fill=C["on_surface_light"])
    for r, fin in enumerate(finishes):
        for c, s in enumerate(ladder_sizes):
            # 光学补偿只用于 16–32px 的"渲染笔宽"，字形不变
            v = MASTER
            t = tile(s, v, fin)
            x = 20 + c * cellw + (cellw - 40 - s) // 2
            y = 60 + r * cellh + (240 - s) // 2
            li.paste(t, (x, y), t)
            dl.text((20 + c * cellw + 120, 60 + r * cellh + 256), f"{s}px · {fin}", font=font(13),
                    fill=C["on_surface_variant_light"])
    p5 = os.path.join(OUT, "brand-icon-ladder.png")
    li.save(p5)

    # ── 资产文件（**全部按原版几何**；简化变体不产出）──
    def w(name, text):
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
            f.write(text)

    w("utvtu-mark.svg", svg_stroke(MASTER, color=C["primary_light"]))
    w("utvtu-mark-dark.svg", svg_stroke(MASTER, color=C["primary_dark"]))
    w("utvtu-mark-mono.svg", svg_stroke(MASTER, color="currentColor"))
    w("utvtu-mark-filled.svg", svg_filled(MASTER, color="currentColor"))
    w("utvtu-mark-optical.svg", svg_stroke(OC_C, color="currentColor", sw_scale=1.0)
      .replace('stroke-width="8"', 'stroke-width="6"'))
    w("utvtu-mark-optical-filled.svg", svg_filled(OC_C, color="currentColor"))
    w("utvtu-brace.svg", svg_stroke(brace(), color="currentColor"))
    w("utvtu-brace-flipx.svg",
      svg_stroke(brace(), color="currentColor").replace(
          '<svg xmlns="http://www.w3.org/2000/svg"',
          '<svg xmlns="http://www.w3.org/2000/svg"').replace(
          'stroke-width="8"', 'stroke-width="8" transform="scale(-1,1) translate(-64,0)"', 1))
    w("utvtu-brace-filled.svg", svg_filled(brace(), color="currentColor"))
    w("utvtu-brand-geometry.axaml", xaml_snippet())

    # 多尺寸 ICO：**逐尺寸渲染**（不靠单图缩放），全部用原版几何
    ico_imgs = [tile(s, MASTER, "primary") for s in (16, 24, 32, 48, 64, 128, 256)]
    ico_imgs[-1].save(os.path.join(OUT, "utvtu.ico"), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)],
                      append_images=ico_imgs[:-1])
    for s in (16, 24, 32):
        tile(s, MASTER, "primary").save(os.path.join(OUT, f"utvtu-icon-{s}.png"))


    # 闪屏 1x/2x
    splash(480, 320, MASTER).save(os.path.join(OUT, "utvtu-splash@1x.png"))
    splash(960, 640, MASTER).save(os.path.join(OUT, "utvtu-splash@2x.png"))

    # 对比度
    for label, fg, bg, note in CONTRAST_ROWS:
        r = contrast(fg, bg)
        data["contrast"].append({"label": label, "fg": fg, "bg": bg, "ratio": round(r, 2),
                                 "pass_4_5": r >= 4.5, "pass_3_0": r >= 3.0, "note": note})

    # 复制对比图到 .dsh/fx/shots（Lead 指定位置）
    import shutil
    shots = {}
    for p in [x for x in (p1, p2, p3, p4, p5) if x]:
        base = os.path.basename(p)
        dst = os.path.join(SHOTS, base if base.startswith("brand-") else "brand-" + base)
        shutil.copyfile(p, dst)
        shots[os.path.basename(p)] = dst
    data["outputs"] = {"sheets": [os.path.basename(p) for p in (p1, p2, p3, p4, p5) if p], "shots": shots}

    with open(os.path.join(OUT, "brand-metrics.json"), "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)

    # 控制台摘要
    for key, v in data["variants"].items():
        m16, m32 = v["metrics"][0], v["metrics"][2]
        print(f'{key:16s} 16px: n={m16["components"]} ink={m16["ink_pct"]:5.2f}% gap={m16["min_gap_px"]:4.2f} '
              f'sw={m16["stroke_px"]:2d} {"LEGIBLE" if m16["legible"] else "NG"} | '
              f'32px: n={m32["components"]} gap={m32["min_gap_px"]:4.2f} sw={m32["stroke_px"]:2d} '
              f'{"LEGIBLE" if m32["legible"] else "NG"}')
    print("\n对比度：")
    for c in data["contrast"]:
        flag = "PASS" if c["pass_4_5"] else ("3:1-only" if c["pass_3_0"] else "FAIL")
        print(f'  {flag} {c["ratio"]:5.2f}:1  {c["label"]}')
    print("\n输出目录：", OUT)


if __name__ == "__main__":
    main()
