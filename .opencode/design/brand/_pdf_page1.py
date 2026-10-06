"""W42-1b 只解析 PDF 第 1 页 /Contents，在字标 bbox 内按 CTM 过滤分组（不描摹、不改数字）。"""
import io, json, os, re, zlib
from PIL import Image, ImageDraw

ROOT = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
EX = os.path.join(ROOT, "extracted"); os.makedirs(EX, exist_ok=True)
pdf = open(os.path.join(ROOT, "utvtu-brand-sheet.pdf"), "rb").read()
TARGET = (1351.0, 2332.0, 2397.0, 2619.0)          # Lead 实测：PDF 点、原点左下

# ── 1. 定位第 1 页（MediaBox 2528×2955）的 /Contents ──
page = None
for m in re.finditer(rb"(\d+)\s+0\s+obj(.{0,1200}?)endobj", pdf, re.S):
    body = m.group(2)
    if b"/MediaBox" in body and b"2528" in body and b"2955" in body and b"/Contents" in body:
        page = m
        break
if page is None:
    print("page1 not found"); raise SystemExit(1)
cm = re.search(rb"/Contents\s+(\d+)\s+0\s+R", page.group(2))
cobj = int(cm.group(1))
sobj = re.search(rb"(?m)^%d\s+0\s+obj(.*?)endobj" % cobj, pdf, re.S)
if sobj is None:
    sobj = re.search(rb"%d\s+0\s+obj(.*?)endobj" % cobj, pdf, re.S)
raw = sobj.group(1)
st = raw.find(b"stream")
en = raw.find(b"endstream", st)
chunk = raw[st + 6:en].lstrip(b"\r\n")
try:
    content = zlib.decompress(chunk).decode("latin-1")
except Exception:
    content = chunk.decode("latin-1", errors="ignore")
print("page1 contents obj:", cobj, "content bytes:", len(content))

# ── 2. 解析：q/Q + cm 维护 CTM，收集路径子路径 ──
NUM = r"[-+]?\d*\.?\d+"
TOK = re.compile(rf"({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+cm\b|"
                 rf"({NUM})\s+({NUM})\s+([ml])\b|"
                 rf"({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+({NUM})\s+c\b|"
                 rf"(?<![A-Za-z0-9])(q|Q)\b")

def mul(A, B):                                        # 2×3 矩阵相乘（A∘B）
    a1, b1, c1, d1, e1, f1 = A; a2, b2, c2, d2, e2, f2 = B
    return (a1*a2 + c1*b2, b1*a2 + d1*b2, a1*c2 + c1*d2, b1*c2 + d1*d2,
            a1*e2 + c1*f2 + e1, b1*e2 + d1*f2 + f1)

def apply(M, x, y):
    a, b, c, d, e, f = M
    return (a*x + c*y + e, b*x + d*y + f)

ctm, stack = (1, 0, 0, 1, 0, 0), []
subs, cur = [], None
for m in TOK.finditer(content):
    g = m.groups()
    if g[15] == "q":
        stack.append(ctm)
    elif g[15] == "Q":
        ctm = stack.pop() if stack else ctm
    elif g[6] is not None:                            # m / l
        x, y = apply(ctm, float(g[6]), float(g[7]))
        if g[8] == "m":
            if cur and len(cur) > 2: subs.append(cur)
            cur = [(x, y)]
        elif cur is not None:
            cur.append((x, y))
    elif g[9] is not None:                            # c
        p = [apply(ctm, float(g[9 + i * 2]), float(g[10 + i * 2])) for i in range(3)]
        if cur is None: cur = [p[0]]
        x0, y0 = cur[-1]
        for i in range(1, 9):
            t = i / 8; mt = 1 - t
            cur.append((mt**3*x0 + 3*mt*mt*t*p[0][0] + 3*mt*t*t*p[1][0] + t**3*p[2][0],
                        mt**3*y0 + 3*mt*mt*t*p[0][1] + 3*mt*t*t*p[1][1] + t**3*p[2][1]))
    elif g[0] is not None:                            # cm
        ctm = mul(ctm, tuple(float(g[i]) for i in range(6)))
if cur and len(cur) > 2: subs.append(cur)
print("total subpaths in page1:", len(subs))

# ── 3. bbox 过滤（目标框外扩 8pt）──
x0, y0, x1, y1 = TARGET
pad = 8.0
inside = []
for s in subs:
    xs = [p[0] for p in s]; ys = [p[1] for p in s]
    if max(xs) < x0 - pad or min(xs) > x1 + pad or max(ys) < y0 - pad or min(ys) > y1 + pad:
        continue
    inside.append(s)
print("subpaths inside target box:", len(inside))
if inside:
    axs = [p[0] for s in inside for p in s]; ays = [p[1] for s in inside for p in s]
    print("bbox:", [round(v, 1) for v in (min(axs), min(ays), max(axs), max(ays))],
          "w/h:", round(max(axs)-min(axs), 1), round(max(ays)-min(ays), 1),
          "aspect:", round((max(axs)-min(axs))/max(1e-6, max(ays)-min(ays)), 3))

# ── 4. 按 x 间隙聚成字形（间隙 > 12pt 视为断字）──
groups = []
for s in sorted(inside, key=lambda s: min(p[0] for p in s)):
    gx0 = min(p[0] for p in s); gx1 = max(p[0] for p in s)
    if groups and gx0 - max(p[0] for p in groups[-1]) < 12.0:
        groups[-1].extend(s)
    else:
        groups.append(list(s))
print("glyph groups:", len(groups),
      "| widths:", [round(max(p[0] for p in g) - min(p[0] for p in g), 1) for g in groups][:8])

out = {"page_contents_obj": cobj, "total_subpaths": len(subs), "inside": len(inside),
       "groups": len(groups),
       "bbox": [round(v, 2) for v in (min(axs), min(ays), max(axs), max(ays))] if inside else None,
       "group_widths": [round(max(p[0] for p in g) - min(p[0] for p in g), 1) for g in groups]}
io.open(os.path.join(EX, "wordmark-extract-report.json"), "w", encoding="utf-8").write(
    json.dumps(out, ensure_ascii=False, indent=1))

# ── 5. 出候选图（仅当候选 ≤ 12 组时才出，避免假资产）──
if inside and len(groups) <= 12:
    ax0, ay0, ax1, ay1 = min(axs), min(ays), max(axs), max(ays)
    W = int(ax1 - ax0) + 40; H = int(ay1 - ay0) + 40
    img = Image.new("RGB", (W, H), (252, 248, 255))
    d = ImageDraw.Draw(img)
    for s in inside:
        d.polygon([(20 + p[0] - ax0, 20 + (ay1 - p[1])) for p in s], fill=(28, 27, 34))
    img.save(os.path.join(EX, "_candidate-wordmark-from-pdf.png"))
    d2 = " ".join("M" + " L".join(f"{p[0]-ax0:.2f},{ay1-p[1]:.2f}" for p in s) + " Z" for s in inside)
    io.open(os.path.join(EX, "_candidate-wordmark.svg"), "w", encoding="utf-8").write(
        f'<!-- 候选（待人工判定）：来源 = utvtu-brand-sheet.pdf 第 1 页 /Contents obj {cobj}，'
        f'bbox 过滤 {TARGET}，CTM 已应用；未描摹、未重排 -->\n'
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {ax1-ax0:.2f} {ay1-ay0:.2f}">'
        f'<path fill="currentColor" d="{d2}"/></svg>')
    print("candidate artifacts written (marked _candidate-*)")
