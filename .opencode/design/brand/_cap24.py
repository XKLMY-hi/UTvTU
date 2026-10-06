"""W39e 补测：cap 20/24/28 的域数、最小间距，以及"哪一对相邻字符相接、缝隙多少"。"""
import json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import _generate as G, _lockup as L, _source as S

OUT = L.OUT
WM = S.WM
NAMES = ["U", "T", "v", "T", "U"]


def cols(img):
    m = G.mask_of(img)
    h, w = len(m), len(m[0])
    return [any(m[y][x] for y in range(h)) for x in range(w)]


def report(cap):
    img = L.render_wordmark(cap, "#000000", letters=WM)
    m = G.mask_of(img)
    comps = G.components(m)
    proj = cols(img)
    # 空列段（= 字符之间的缝隙）
    runs, run, start = [], 0, None
    for x, v in enumerate(proj):
        if not v:
            if run == 0:
                start = x
            run += 1
        elif run:
            runs.append((start, run))
            run = 0
    if run:
        runs.append((start, run))
    # 期望字符位置（归一化 → 像素）
    sc = 0
    letters = [(min(p[0] for p in l["pts"]), max(p[0] for p in l["pts"])) for l in WM]
    sc = cap / 100.0
    boxes = [(a * sc, b * sc) for a, b in letters]
    # 找出"应有缝隙但空列为 0"的相邻对
    touching = []
    for i in range(4):
        gap_zone = (boxes[i][1], boxes[i + 1][0])
        if gap_zone[1] - gap_zone[0] <= 0:
            touching.append((NAMES[i], NAMES[i + 1], 0.0, "bbox 重叠"))
            continue
        inside = [r for r in runs if r[0] >= gap_zone[0] - 1 and r[0] + r[1] <= gap_zone[1] + 1]
        gap = max((r[1] for r in inside), default=0)
        if gap < 1:
            touching.append((NAMES[i], NAMES[i + 1], round(gap_zone[1] - gap_zone[0], 2), "相接"))
    return {"cap": cap, "components": len(comps), "min_gap_px": G.min_gap_px(m),
            "stroke_px": G.median_stroke_px(m), "empty_runs": runs,
            "touching_pairs": touching}


res = [report(c) for c in (14, 16, 20, 24, 28, 48)]
with open(os.path.join(OUT, "brand-wordmark-cap24.json"), "w", encoding="utf-8") as f:
    json.dump(res, f, ensure_ascii=False, indent=1)
for r in res:
    print(f"cap {r['cap']:2d}: {r['components']} 域 · 最小间距 {r['min_gap_px']}px · 笔宽 {r['stroke_px']}px · "
          f"相接对 {r['touching_pairs']}")
