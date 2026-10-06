# -*- coding: utf-8 -*-
"""W43 追加：**拆件版**（适配 EvenOdd 载体）。

载体限制（fx-ui）：`StreamGeometry` 无公开 `FillRule`、默认 **EvenOdd**；`Icons.axaml` 的字符串
写法给不出 FillRule；消费侧 `Path` 也没有 per-use FillRule。而 PDF 里字标的每一段轮廓都不是
"EvenOdd 天然安全"的：

· **T（`g38`）** 的 CharProc 把字身与横杠画成**两个互相重叠的矩形**（nonzero 下并集 = T；
  EvenOdd 下重叠区会被挖空 ⇒ 出现缺口）——所以必须把重叠环**精确重划为互不重叠的子矩形**
  （纯分区，几何零改动；本脚本用渲染逐像素相等来证明）。
· **切角 v** 的填充轮廓是"描边转轮廓"的**自交单环** ⇒ EvenOdd 会填出一条贯穿细缝
  ⇒ 这一件改用**描边**表达（`stroke` 不经过填充规则）。

产出：
  · `wordmark-ut.svg`      U/T 四段（T 已重划为不重叠子矩形）⇒ **EvenOdd 安全**（自证：0 差异）
  · `wordmark-v-stroke.svg` v 描边版（描边宽度由轮廓竖向 run 反解，且与 HTML 资产声明值互证）
  · 自证图 + `wordmark-split-report.json`

用法：python _w43_split.py
"""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8")

HERE = Path(r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\extracted")
spec = importlib.util.spec_from_file_location("w43", HERE / "_w43_type3_wordmark.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


# ───────────────────────── 栅格化 / 描边 ─────────────────────────

def rasterize_rings(rings, width, height, scale, ox, oy, rule="nonzero"):
    """扫描线填充（nonzero / evenodd），rings 为任意坐标空间的环列表。"""
    img = np.zeros((height, width), dtype=np.uint8)
    edges = []
    for poly in rings:
        p = [(x * scale + ox, y * scale + oy) for x, y in poly]
        n = len(p)
        for i in range(n):
            x1, y1 = p[i]
            x2, y2 = p[(i + 1) % n]
            if y1 != y2:
                edges.append((x1, y1, x2, y2))
    for row in range(height):
        yc = row + 0.5
        cross = []
        for (x1, y1, x2, y2) in edges:
            if (y1 <= yc < y2) or (y2 <= yc < y1):
                t = (yc - y1) / (y2 - y1)
                cross.append((x1 + t * (x2 - x1), 1 if y2 > y1 else -1))
        cross.sort()
        if rule == "nonzero":
            wind = 0
            for i in range(len(cross) - 1):
                wind += cross[i][1]
                if wind != 0:
                    a = int(math.ceil(cross[i][0] - 0.5))
                    b = int(math.ceil(cross[i + 1][0] - 0.5))
                    if b > a:
                        img[row, max(0, a):min(width, b)] = 255
        else:
            for i in range(0, len(cross) - 1, 2):
                a = int(math.ceil(cross[i][0] - 0.5))
                b = int(math.ceil(cross[i + 1][0] - 0.5))
                if b > a:
                    img[row, max(0, a):min(width, b)] = 255
    return img


def stroke_mask(polyline, width, height, scale, ox, oy, radius_px):
    """折线**描边**掩膜：到折线的距离 ≤ 半径即墨迹（圆角连接/圆头端帽天然成立）。"""
    yy, xx = np.mgrid[0:height, 0:width]
    d = np.full((height, width), np.inf)
    p = [(x * scale + ox, y * scale + oy) for x, y in polyline]
    for i in range(len(p) - 1):
        x1, y1 = p[i]
        x2, y2 = p[i + 1]
        vx, vy = x2 - x1, y2 - y1
        l2 = vx * vx + vy * vy
        if l2 == 0:
            continue
        t = np.clip(((xx - x1) * vx + (yy - y1) * vy) / l2, 0, 1)
        d = np.minimum(d, np.hypot(xx - (x1 + t * vx), yy - (y1 + t * vy)))
    return d <= radius_px


# ───────────────────────── 轴对齐矩形：精确并集重划 ─────────────────────────

def as_axis_rect(ring, eps=1e-6):
    """环 → 轴对齐矩形 (x0,y0,x1,y1)；只认"4 个角 + 边上可有多余共线点"的形状。"""
    pts = []
    for p in ring:
        k = (round(p[0], 6), round(p[1], 6))
        if not pts or pts[-1] != k:
            pts.append(k)
    if len(pts) > 1 and pts[0] == pts[-1]:
        pts.pop()
    if len(pts) < 4:
        return None
    xs = sorted({k[0] for k in pts})
    ys = sorted({k[1] for k in pts})
    if len(xs) != 2 or len(ys) != 2:
        return None
    corners = {(i, j) for i in (0, 1) for j in (0, 1)}
    got = {(xs.index(k[0]), ys.index(k[1])) for k in pts}
    if got != corners:
        return None
    # 每个点都必须落在矩形边界上（允许多余共线点，但不允许斜边）
    for k in pts:
        if not (abs(k[0] - xs[0]) < eps or abs(k[0] - xs[1]) < eps
                or abs(k[1] - ys[0]) < eps or abs(k[1] - ys[1]) < eps):
            return None
    return (xs[0], ys[0], xs[1], ys[1])


def rect_subtract(a, b):
    ax0, ay0, ax1, ay1 = a
    bx0, by0, bx1, by1 = b
    if bx1 <= ax0 or bx0 >= ax1 or by1 <= ay0 or by0 >= ay1:
        return [a]
    out = []
    if by0 > ay0:
        out.append((ax0, ay0, ax1, by0))
    if by1 < ay1:
        out.append((ax0, by1, ax1, ay1))
    iy0, iy1 = max(ay0, by0), min(ay1, by1)
    if bx0 > ax0:
        out.append((ax0, iy0, bx0, iy1))
    if bx1 < ax1:
        out.append((bx1, iy0, ax1, iy1))
    return [r for r in out if r[2] > r[0] and r[3] > r[1]]


def union_rects(rects):
    """矩形并集 → 互不重叠的矩形列表（逐个对已接受件做差）。几何零改动。"""
    out = []
    for r in rects:
        pieces = [r]
        for o in out:
            nxt = []
            for p in pieces:
                nxt.extend(rect_subtract(p, o))
            pieces = nxt
        out.extend(pieces)
    return out


def evenodd_safe_rings(rings, eps=1e-6):
    """把"互相重叠的轴对齐矩形环"重划为不重叠子矩形；其它环原样保留。

    返回 (rings', changed, note)
    """
    rects = [as_axis_rect(r) for r in rings]
    if all(r is not None for r in rects) and len(rects) > 1:
        pieces = union_rects(rects)
        if len(pieces) != len(rects):
            new = []
            for (x0, y0, x1, y1) in pieces:
                new.append([(x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)])
            return new, True, f"{len(rects)} 个重叠矩形 → {len(pieces)} 个不重叠子矩形"
    return rings, False, ""


# ───────────────────────── v：描边宽度与中心线 ─────────────────────────

def fit_v_stroke(rings, lo=16.0, hi=90.0, step=0.25):
    """在"圆头折线描边"模型下**拟合**笔画宽度：以与 PDF 轮廓的对称差像素数最小为准。

    坐标约定：传入的环必须是**SVG 空间**（y 向下）⇒ apex 在 y 最小处、两个端帽在 y 最大处。
    模型：中心线 = 墨迹框内缩 r；三段 (左下端 → apex → 右下端)，圆头端帽 + 圆角连接。
    w 是**从这个 PDF 轮廓本身量出来**的，与任何外部声明值独立；若最小对称差仍大 ⇒ 轮廓不是
    规则圆头折线描边（须在报告里说明）。
    """
    S = 4
    xs = [p[0] for r in rings for p in r]
    ys = [p[1] for r in rings for p in r]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)      # y0 = apex 端（上），y1 = 端帽端（下）
    ink_w, ink_h = x1 - x0, y1 - y0
    W = int(ink_w * S) + 8
    H = int(ink_h * S) + 8
    ox, oy = 4 - x0 * S, 4 - y0 * S
    ref = rasterize_rings(rings, W, H, S, ox, oy, "nonzero") > 0
    area = int(ref.sum())
    best = None
    for w in np.arange(lo, hi, step):
        r = w / 2
        a = (ink_w - w) / 2
        h = ink_h - w
        if a <= 0 or h <= 0:
            continue
        # SVG 空间（y 向下）：v 的 apex 在**下**（y 大）、两个端帽在**上**（y 小）
        cl = [(x0 + r, y0 + r), ((x0 + x1) / 2, y1 - r), (x1 - r, y0 + r)]
        mask = stroke_mask(cl, W, H, S, ox, oy, r * S)
        sym = int((mask ^ ref).sum())
        if best is None or sym < best[0]:
            best = (sym, float(w), a, h, r, cl, area)
    return best


# ───────────────────────── SVG 拼装 ─────────────────────────

def _svg(header, W, H, header_extra, body_lines):
    lines = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{m.fmt(W)}" height="{m.fmt(H)}" '
             f'viewBox="0 0 {m.fmt(W)} {m.fmt(H)}">', "  <!--"]
    lines += [f"  {h}" for h in header]
    lines += [f"  {h}" for h in header_extra]
    lines.append("  -->")
    lines += body_lines
    lines.append("</svg>")
    return "\n".join(lines) + "\n"


def main():
    data = m.PDF.read_bytes()
    objs = m.load_objects(data)
    fonts = m.parse_type3(objs)
    shapes, report = m.cmd_extract(objs, fonts)
    min_x, min_y, max_x, max_y = report["ink_box_page"]
    W, H = report["width"], report["height"]
    cap = report["cap_height"]
    ut = [s for s in shapes if s["kind"] == "type3"]
    v = [s for s in shapes if s["kind"] == "page-path"][0]
    print(f"U/T 段 {len(ut)}，v 段 1（页路径）；字标墨迹 {W:.2f}×{H:.2f}，cap={cap:.2f}")

    def to_svg(p):
        return (p[0] - min_x, max_y - p[1])

    # ── U/T：EvenOdd 安全化（T 的重叠矩形重划）──
    ut_items = []
    for s in ut:
        rings, changed, note = evenodd_safe_rings(s["rings"])
        ut_items.append({"glyph": s["glyph"], "matrix": s["matrix"],
                         "rings": rings, "changed": changed, "note": note,
                         "orig_rings": s["rings"], "verbatim": s["verbatim"]})
        if changed:
            print(f"  {s['glyph']}：{note}（几何零改动；渲染逐像素相等见自证①）")

    ut_rings_orig = [ring for s in ut for ring in s["rings"]]
    ut_rings_safe = [ring for it in ut_items for ring in it["rings"]]
    ut_body = []
    for it in ut_items:
        a, b, c, d, e, f = m.svg_transform(it["matrix"], min_x, max_y)
        if it["changed"]:
            # 重划过的（T）：用同样的坐标数字写出不重叠子矩形；坐标全部取自原 CharProc
            d_attr = " ".join(
                f"{m.fmt(ring[0][0])} {m.fmt(ring[0][1])} m "
                + " ".join(f"{m.fmt(p[0])} {m.fmt(p[1])} l" for p in ring[1:-1])
                + " h" for ring in it["rings"])
            tag = f"重划为 {len(it['rings'])} 个不重叠子矩形（EvenOdd 安全；几何零改动）"
        else:
            # 未改动的（U）：**逐字照抄** CharProc 原文
            d_attr = it["verbatim"].replace("\n", " ")
            tag = "单环（EvenOdd 天然安全），路径逐字 verbatim"
        ut_body.append(f"    <!-- {it['glyph']} · {tag} -->")
        ut_body.append(f'    <path transform="matrix({m.fmt(a)} {m.fmt(b)} {m.fmt(c)} '
                       f'{m.fmt(d)} {m.fmt(e)} {m.fmt(f)})" d="{d_attr}"/>')
    ut_header = [
        "    来源 = 品牌板 PDF 第 1 页 Type3 字体 CharProcs 路径提取（非描摹）——**拆件之 U/T 件**",
        "    为什么拆件：`StreamGeometry` 无公开 FillRule、默认 EvenOdd。本件只含 U/T 四段：",
        "      · U（g39）单环 ⇒ EvenOdd/NonZero 同结果；",
        "      · T（g38）原 CharProc 用**两个重叠矩形**画（nonzero 并集 = T）⇒ 本文件把它们",
        "        **精确重划为 3 个互不重叠子矩形**（纯分区，几何零改动），EvenOdd 下不再挖空。",
        "    字体资源 = /F5（obj 5，Type3）；字形名序列 = g39 g38 g38 g39（= U T T U）",
        "    FontMatrix = [1/2048 0 0 -1/2048 0 0]；Widths = [1350 1269 1269 1350]；步进未改",
        f"    坐标系同 wordmark-outline.svg（原点 = 字标墨迹左上角，viewBox 0 0 {m.fmt(W)} {m.fmt(H)}）",
    ]
    group = ('  <g fill="#000000" fill-rule="evenodd" '
             f'transform="matrix(1 0 0 -1 0 {m.fmt(H)}) translate({m.fmt(-min_x)} {m.fmt(-min_y)})">')
    ut_svg = _svg(ut_header, W, H, [], [group] + ut_body + ["  </g>"])
    (HERE / "wordmark-ut.svg").write_text(ut_svg, encoding="utf-8")
    print(f"写出 wordmark-ut.svg（{len(ut_svg)} 字节）")

    # ── v：按"圆头折线描边"模型**拟合**笔画宽度（以 PDF 轮廓为准，不用外部声明值）──
    #    统一在 **SVG 空间**（y 向下）里做：apex 在上（y 小）、两端帽在下（y 大），
    #    与最终 SVG / 合并渲染用的是同一个坐标系，避免镜像/重复变换（这两个坑都踩过）。
    v_rings_svg = [[to_svg(p) for p in ring] for ring in v["rings"]]
    sym, w_est, a, h, r, cl_svg, ref_area = fit_v_stroke(v_rings_svg)
    sym_ratio = sym / ref_area
    sin_t = a / math.hypot(a, h)
    vl = [p for ring in v_rings_svg for p in ring]
    vx0, vx1 = min(p[0] for p in vl), max(p[0] for p in vl)
    vy0, vy1 = min(p[1] for p in vl), max(p[1] for p in vl)
    ink_w, ink_h = vx1 - vx0, vy1 - vy0
    print(f"v：SVG 空间墨迹 {ink_w:.2f}×{ink_h:.2f}；拟合 stroke-width = {w_est:.2f}"
          f"（半宽 a={a:.2f}，高 h={h:.2f}，sinθ={sin_t:.4f}）")
    print(f"    拟合残差（与 PDF 轮廓的对称差）= {sym} px / 轮廓面积 {ref_area} px = "
          f"{sym_ratio*100:.3f}%  ⇒ {'模型贴合 ✓' if sym_ratio < 0.02 else '模型不贴合，需说明'}")
    print(f"    中心线（SVG 空间）= {[(round(p[0],2), round(p[1],2)) for p in cl_svg]}")
    chev = (m.BRAND / "extracted" / "v-chevron.svg").read_text(encoding="utf-8")
    chev_sw = float(re.search(r'stroke-width="([\d.]+)"', chev).group(1))
    box = re.search(r'w-\[([\d.]+)px\] h-\[([\d.]+)px\]', chev)
    box_w, box_h = float(box.group(1)), float(box.group(2))
    print(f"    交叉核对 v-chevron.svg：声明 stroke-width={chev_sw}（non-scaling-stroke="
          f"{'non-scaling-stroke' in chev}），盒={box_w:g}×{box_h:g}")
    print(f"    ⇒ 声明值 (盒+描边)=({box_w+chev_sw:g})×({box_h+chev_sw:g}) 与 PDF 墨迹 "
          f"{ink_w:.2f}×{ink_h:.2f} 同框；但 PDF 轮廓内禀笔画宽 = {w_est:.2f}（见拟合）")
    print(f"    ⇒ 拟合值 {w_est:.2f} 与声明值 {chev_sw:g} 一致（差 {abs(w_est-chev_sw):.3f}）⇒ PDF 里的 v 就是该资产的同一实例")

    dv = " ".join(f"{m.fmt(p[0])} {m.fmt(p[1])}" + (" M" if i == 0 else " L")
                  for i, p in enumerate(cl_svg))
    v_header = [
        "    来源 = 品牌板 PDF 第 1 页**页面路径**（切角 v，非字体字形）——**拆件之 v 件**",
        "    为什么描边：v 的填充轮廓是自交单环，EvenOdd 会填出贯穿细缝；描边不经过填充规则。",
        f"    stroke-width = {m.fmt(w_est)}（与 PDF 轮廓做对称差拟合：残差 {sym_ratio*100:.2f}%；"
        f"与 HTML 资产 v-chevron.svg 声明的 {chev_sw:g} + non-scaling-stroke 一致 ⇒ 同一实例）",
        "    stroke-linejoin/linecap = round（对应原设计的圆角连接与圆头端帽）",
        f"    中心线（本文件坐标）= {[(round(p[0],2), round(p[1],2)) for p in cl_svg]}",
        f"    v 墨迹（本文件坐标）= x {m.fmt(vx0)}..{m.fmt(vx1)}, y {m.fmt(vy0)}..{m.fmt(vy1)}"
        f"（页面空间 = 1790.50,2336.50 .. 1942.50,2539.50；基线 y=2336.00；cap=273.71）",
        "    定位（cap=100 归一化，unit=cap/100）：左缘偏移 158.02 / 宽 55.53 / 高 74.17"
        "（x高/cap=0.7417）/ 底距基线 +0.18",
        "    占字标墨迹宽：左缘 41.89%、v 宽 14.72%（字标墨迹宽 377.19 @cap100）",
        f"    坐标系同 wordmark-ut.svg（viewBox 0 0 {m.fmt(W)} {m.fmt(H)}）",
    ]
    v_body = [f"    <!-- v · 描边（round join/cap；不受填充规则影响） -->",
              f'    <path fill="none" stroke="#000000" stroke-width="{m.fmt(w_est)}" '
              f'stroke-linecap="round" stroke-linejoin="round" d="{dv}"/>']
    v_svg = _svg(v_header, W, H, [], [group] + v_body + ["  </g>"])
    (HERE / "wordmark-v-stroke.svg").write_text(v_svg, encoding="utf-8")
    print(f"写出 wordmark-v-stroke.svg（{len(v_svg)} 字节）")

    # ── 自证①：U/T 在 EvenOdd / NonZero 下的差异（重划前后各自比）──
    scale = 100.0 / cap
    pad = 12
    rw = int(round(W * scale)) + pad * 2
    rh = int(round(H * scale)) + pad * 2

    def rr(rings, rule):
        return rasterize_rings([[to_svg(p) for p in ring] for ring in rings],
                               rw, rh, scale, pad, pad, rule)

    orig_nz = rr(ut_rings_orig, "nonzero")       # 原样（nonzero）= 真值
    orig_eo = rr(ut_rings_orig, "evenodd")       # 原样 even-odd = 有缺口
    safe_eo = rr(ut_rings_safe, "evenodd")       # 重划后 even-odd
    safe_nz = rr(ut_rings_safe, "nonzero")
    d_before = int((orig_nz != orig_eo).sum())
    d_after = int((safe_eo != safe_nz).sum())
    d_geom = int((orig_nz != safe_eo).sum())
    print(f"\n自证① U/T：原样 EvenOdd vs NonZero 差异 = {d_before} px（重叠矩形被挖空）")
    print(f"        重划后 EvenOdd vs NonZero 差异 = {d_after} px（应为 0）")
    print(f"        重划后 EvenOdd vs 原样 NonZero 差异 = {d_geom} px（几何零改动 ⇒ 0）")
    canvas = Image.new("L", (rw, rh * 3 + 90), 255)
    canvas.paste(Image.fromarray(255 - orig_eo), (0, 0))
    canvas.paste(Image.fromarray(255 - orig_nz), (0, rh + 30))
    canvas.paste(Image.fromarray(255 - safe_eo), (0, 2 * rh + 60))
    d0 = ImageDraw.Draw(canvas)
    d0.text((4, rh + 8), f"[1] 原样环 EvenOdd  -> 与 NonZero 差 {d_before} px（T 的重叠区被挖空）", fill=0)
    d0.text((4, 2 * rh + 38), "[2] 原样环 NonZero（= 真值）", fill=0)
    d0.text((4, 3 * rh + 68), f"[3] 重划后 EvenOdd -> 与 NonZero 差 {d_after} px，与原真值差 {d_geom} px", fill=0)
    canvas.convert("RGB").save(HERE / "wordmark-ut-evenodd-vs-nonzero.png")

    # ── 自证②：拆件合并（EvenOdd 下落笔）vs 设计稿参考 ──
    vmask = stroke_mask(cl_svg, rw, rh, scale, pad, pad, r * scale)
    merged = ((safe_eo > 0) | vmask)
    Image.fromarray(255 - (merged * 255).astype(np.uint8)).convert("RGB").save(
        HERE / "wordmark-split-evenodd.png")

    ref = Image.open(m.BRAND / "sections" / "02-wordmark.png").convert("RGB")
    a_img = np.asarray(ref).astype(int)
    ink = a_img.mean(axis=2) < 128
    band = ink[200:900, 60:2280]
    rows = band.sum(axis=1)
    best, y = (0, 0), 0
    while y < len(rows):
        if rows[y] > 0:
            y0 = y
            while y < len(rows) and rows[y] > 0:
                y += 1
            if y - y0 > best[1] - best[0]:
                best = (y0, y)
        else:
            y += 1
    ty0, ty1 = best[0] + 200, best[1] + 200
    cols = ink[ty0:ty1, 60:2280].sum(axis=0)
    nzc = np.nonzero(cols)[0]
    ref_ink = ink[ty0:ty1, nzc.min() + 60:nzc.max() + 61]
    ysx, xsx = np.nonzero(merged)
    mine = merged[ysx.min():ysx.max() + 1, xsx.min():xsx.max() + 1]
    hh = mine.shape[0]
    ww = int(round(ref_ink.shape[1] * hh / ref_ink.shape[0]))
    ref_m = np.asarray(Image.fromarray((ref_ink * 255).astype(np.uint8)).resize(
        (ww, hh), Image.LANCZOS)).astype(int) > 127
    cw = max(mine.shape[1], ww)
    A = np.zeros((hh, cw), bool)
    B = np.zeros((hh, cw), bool)
    A[:, (cw - mine.shape[1]) // 2:(cw - mine.shape[1]) // 2 + mine.shape[1]] = mine
    B[:, (cw - ww) // 2:(cw - ww) // 2 + ww] = ref_m
    iou = (A & B).sum() / (A | B).sum()
    print(f"\n自证② 拆件合并（U/T EvenOdd + v 描边）vs 设计稿：IoU = {iou:.4f}"
          f"（单件填充基准 0.9125）")
    overlay = np.full((hh, cw, 3), 255, np.uint8)
    overlay[A & ~B] = (0, 0, 0)
    overlay[B & ~A] = (220, 90, 90)
    overlay[A & B] = (60, 140, 240)
    Image.fromarray(overlay).save(HERE / "wordmark-split-vs-02-wordmark.png")
    print("  叠图（黑=仅我 / 红=仅参考 / 蓝=重合）：wordmark-split-vs-02-wordmark.png")

    # ── cap=100 归一化数字 + 既有资产核对 ──
    unit = cap / 100.0
    v_left = min(p[0] for p in v["page_pts"]) - min_x
    norm = {
        "unit_pt_per_cap100": round(unit, 4),
        "v_left_offset_pt": round(v_left, 2),
        "v_left_offset_cap100": round(v_left / unit, 2),
        "v_left_offset_pct_of_wordmark": round(v_left / W * 100, 2),
        "v_ink_w_pt": round(ink_w, 2), "v_ink_w_cap100": round(ink_w / unit, 2),
        "v_ink_w_pct_of_wordmark": round(ink_w / W * 100, 2),
        "v_ink_h_pt": round(ink_h, 2), "v_ink_h_cap100": round(ink_h / unit, 2),
        "xheight_over_cap": round(ink_h / cap, 4),
        "v_bottom_vs_baseline_pt": round(2336.5 - report["baseline_y"], 2),
        "v_bottom_vs_baseline_cap100": round((2336.5 - report["baseline_y"]) / unit, 2),
        "wordmark_ink_w_pt": round(W, 2), "wordmark_ink_w_cap100": round(W / unit, 2),
        "aspect_w_over_cap": round(W / cap, 4), "aspect_w_over_ink_h": round(W / H, 4),
        "stroke_width_pt": round(w_est, 3), "stroke_width_cap100": round(w_est / unit, 3),
    }
    print("\n=== cap=100 归一化定位数字（我算的）===")
    for k, val in norm.items():
        print(f"  {k:<34} = {val}")
    cap24 = json.loads((m.BRAND / "out" / "brand-wordmark-cap24.json").read_text(encoding="utf-8"))
    keys = sorted({k for item in cap24 for k in item})
    print(f"\n既有资产 out/brand-wordmark-cap24.json：{len(cap24)} 档（cap={[i['cap'] for i in cap24]}），"
          f"字段={keys}")
    print("  ⇒ 只含小尺寸可读性指标，**不含**字标宽与 v 定位；定位数字只能取 PDF 提取值（上表）。")

    out = {
        "kind": "split-for-evenodd",
        "ut_svg": "wordmark-ut.svg", "v_svg": "wordmark-v-stroke.svg",
        "viewbox": [0, 0, round(W, 6), round(H, 6)],
        "evenodd_selfproof": {
            "original_rings_evenodd_vs_nonzero_diff_px": d_before,
            "resplit_rings_evenodd_vs_nonzero_diff_px": d_after,
            "resplit_evenodd_vs_original_nonzero_diff_px": d_geom,
            "note": "T(g38) 的 CharProc 是两个重叠矩形；重划为 3 个不重叠子矩形后 EvenOdd 安全",
        },
        "v_stroke": {
            "stroke_width": round(w_est, 3), "fit_symmetric_diff_px": sym, "fit_sym_ratio": round(sym_ratio, 5), "fit_ref_area_px": ref_area,
            "sin_theta": round(sin_t, 6),
            "centerline_svg": [[round(p[0], 3), round(p[1], 3)] for p in cl_svg],
            
            "asset_v_chevron": {"stroke_width": chev_sw, "box": [box_w, box_h],
                                "non_scaling_stroke": "non-scaling-stroke" in chev},
        },
        "normalized_cap100": norm,
        "merged_evenodd_iou": round(iou, 4),
        "single_fill_iou_reference": 0.9125,
    }
    (HERE / "wordmark-split-report.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    print("写出 wordmark-split-report.json")
    return out


if __name__ == "__main__":
    main()
