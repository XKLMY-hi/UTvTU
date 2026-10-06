"""W42-1 从 utvtu-brand-sheet.pdf 内容流提取"UTvTU"字标轮廓（非描摹）。"""
import io, json, os, re, zlib
from PIL import Image, ImageDraw

ROOT = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
EX = os.path.join(ROOT, "extracted"); os.makedirs(EX, exist_ok=True)
pdf = open(os.path.join(ROOT, "utvtu-brand-sheet.pdf"), "rb").read()

NUM = r"[-+]?\d*\.?\d+"
TOK = re.compile(rf"({NUM})\s+({NUM})\s+([ml])\b|({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+c\b")


def streams():
    for m in re.finditer(rb"stream\r?\n", pdf):
        s = m.end(); e = pdf.find(b"endstream", s)
        if e < 0:
            continue
        chunk = pdf[s:e]
        try:
            yield zlib.decompress(chunk).decode("latin-1")
        except Exception:
            pass


def paths_in(txt):
    """返回 [(pts, is_closed_bbox)]，pts 为折线点（c 采样 8 段）。"""
    subs, cur = [], None
    for m in TOK.finditer(txt):
        g = m.groups()
        if g[2]:                                     # m / l
            x, y = float(g[0]), float(g[1])
            if g[2] == "m":
                if cur and len(cur) > 2: subs.append(cur)
                cur = [(x, y)]
            elif cur is not None:
                cur.append((x, y))
        else:                                        # c 曲线
            x1, y1, x2, y2, x3, y3 = map(float, g[3:9])
            if cur is None: cur = []
            x0, y0 = cur[-1] if cur else (x1, y1)
            for i in range(1, 9):
                t = i / 8; mt = 1 - t
                cur.append((mt**3*x0 + 3*mt*mt*t*x1 + 3*mt*t*t*x2 + t**3*x3,
                            mt**3*y0 + 3*mt*mt*t*y1 + 3*mt*t*t*y2 + t**3*y3))
    if cur and len(cur) > 2: subs.append(cur)
    return subs


best = None
for idx, txt in enumerate(streams()):
    subs = paths_in(txt)
    if len(subs) < 2:
        continue
    xs = [p[0] for s in subs for p in s]; ys = [p[1] for s in subs for p in s]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    if w <= 300 or h <= 60:
        continue
    ar = w / h
    score = -abs(ar - 3.77) * 100 + len(subs)          # 字标宽高比 ≈ 3.77（377/100）
    if best is None or score > best[0]:
        best = (score, idx, subs, (min(xs), min(ys), max(xs), max(ys), w, h, ar))

if best is None:
    print("no candidate"); raise SystemExit(1)

score, idx, subs, bb = best
x0, y0, x1, y1, w, h, ar = bb
cap = h
sc = 100.0 / cap
subs_n = [[((x - x0) * sc, (y1 - y) * sc) for x, y in s] for s in subs]   # PDF y 轴向上 → 翻转
xs2 = [p[0] for s in subs_n for p in s]; ys2 = [p[1] for s in subs_n for p in s]
W, H = max(xs2), max(ys2)

d = " ".join("M" + " L".join(f"{x:.2f},{y:.2f}" for x, y in s) + " Z" for s in subs_n)
svg = f"""<!-- 来源 = utvtu-brand-sheet.pdf 内容流矢量提取（非描摹、未重排、未调字距、未做任何补偿）
     提取对象：字标 "UTvTU"（PDF 第 {idx} 个可解压内容流；宽高比 {ar:.3f} ≈ 377/100）
     归一化：cap = 100 单位、基线 y = 100、左缘 x = 0；源 bbox（PDF 点）= {w:.2f} × {h:.2f}
     子路径数 = {len(subs_n)}（预期 5 = U T v T U） -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W:.2f} {H:.2f}" width="{W:.2f}" height="{H:.2f}">
  <path fill="currentColor" fill-rule="nonzero" d="{d}"/>
</svg>"""
io.open(os.path.join(EX, "wordmark-outline.svg"), "w", encoding="utf-8").write(svg)

# 自证：1:1 渲染（cap=100）+ 边界框标注
Z = 2
img = Image.new("RGB", (int(W * Z) + 40, int(H * Z) + 60), (252, 248, 255))
dr = ImageDraw.Draw(img)
for s in subs_n:
    dr.polygon([(20 + x * Z, 40 + y * Z) for x, y in s], fill=(28, 27, 34))
dr.text((20, 14), f"PDF 提取字标 1:1（cap=100）· 子路径 {len(subs_n)} · 宽 {W:.1f} · 高 {H:.1f}",
        fill=(28, 27, 34))
img.save(os.path.join(EX, "wordmark-outline-proof.png"))

info = {"stream_index": idx, "subpaths": len(subs_n), "pdf_bbox": [round(v, 2) for v in (w, h)],
        "aspect": round(ar, 3), "normalized": {"w": round(W, 2), "h": round(H, 2), "cap": 100.0},
        "subpath_bboxes": [[round(min(p[0] for p in s), 1), round(min(p[1] for p in s), 1),
                            round(max(p[0] for p in s), 1), round(max(p[1] for p in s), 1)] for s in subs_n]}
io.open(os.path.join(EX, "wordmark-outline.json"), "w", encoding="utf-8").write(json.dumps(info, ensure_ascii=False, indent=1))
print(json.dumps(info, ensure_ascii=False))
