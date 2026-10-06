"""W39g 修复 brand-wordmark 坐标系混用（U/T 归一化空间 vs v 源像素空间）+ 加健全性断言 + 出图自证。"""
import io, importlib, os, sys
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
p = os.path.join(HERE, "_source.py")
s = io.open(p, encoding="utf-8").read()

# ── 1. 修 wordmark_letters_with_source_v：v 必须用**归一化** bbox 对齐（原来误用源像素 bbox ⇒ 尺度差 ~6.9×）──
old_start = s.find("def wordmark_letters_with_source_v():")
old_end = s.find("# ══", old_start)
new_fn = '''def wordmark_letters_with_source_v():
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
        assert b[0] >= x0 - 2 and b[1] >= y0 - 2 and b[2] <= x1 + 2 and b[3] <= y1 + 2, \\
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


'''
s = s[:old_start] + new_fn + s[old_end:]

# ── 2. 在 main() 里调用断言（合成 WM 之后）──
s = s.replace('WM = wordmark_letters_with_source_v()',
              'WM = wordmark_letters_with_source_v()\nWM_SANITY = assert_wordmark_sane(WM, expect_w=377.33)')
s = s.replace('table["overlay_iou"] = io', 'table["wordmark_sanity"] = WM_SANITY\ntable["overlay_iou"] = io')
io.open(p, "w", encoding="utf-8").write(s)
print("patched _source.py")

# ── 3. 跑：源几何 → 资产；栅格（含 splash）──
import _source as S
importlib.reload(S)
S.main()
import _raster as R
importlib.reload(R)
R.main()

# ── 4. 出图自证：字标 1:1 + 8× + splash 1x ──
OUT = R.OUT
wm48 = S.L.render_wordmark(48, R.C["ink"], letters=S.WM)
wm48.save(os.path.join(OUT, "brand-verify-wordmark-1to1.png"))
z = wm48.crop((0, 0, min(160, wm48.width), wm48.height)).resize(
    (min(160, wm48.width) * 4, wm48.height * 4), Image.NEAREST)
canvas = Image.new("RGB", (max(700, z.width + 40), z.height + 60), R.C["surface_light"])
canvas.paste(z, (20, 44))
ImageDraw.Draw(canvas).text((20, 14), "字标 4× 局部（源切角 v + 描摹 U/T，同一坐标系）", font=R.L.FS(14),
                            fill=R.C["on_surface_light"])
canvas.save(os.path.join(OUT, "brand-verify-wordmark-zoom.png"))
sp = Image.open(os.path.join(OUT, "utvtu-splash@1x.png"))
sp.save(os.path.join(OUT, "brand-verify-splash-1x.png"))
print("wordmark sanity:", S.WM_SANITY)
print("wordmark px:", wm48.size, "splash px:", sp.size)
