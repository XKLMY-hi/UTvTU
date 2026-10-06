import io, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
p = os.path.join(HERE, "_source.py")
src = open(p, encoding="utf-8").read()

# 1) 修掉上一步被 PowerShell 破坏的那一行（把坏块整体删掉）
bad_start = src.find("'def metrics_of_old():")
if bad_start != -1:
    # 找到该行行首
    line_start = src.rfind("\n", 0, bad_start) + 1
    line_end = src.find("\n", bad_start)
    src = src[:line_start] + src[line_end + 1:]

if "def metrics_of_old(" not in src:
    helper = '''

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


'''
    anchor = "# ── 权威几何（verbatim）──"
    src = src.replace(anchor, helper.lstrip("\n") + anchor, 1)

src = src.replace('"old_brace": {}}', '"old_brace": metrics_of_old()}')
open(p, "w", encoding="utf-8").write(src)
print("patched", len(src))
