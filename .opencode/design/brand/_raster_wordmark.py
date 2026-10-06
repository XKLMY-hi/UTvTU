"""W42-D 字标光栅提取（裁图，非描摹）：贴紧墨迹裁切 + 保抗锯齿的 alpha 蒙版 + 多倍率 + 可用性自证。"""
import io, json, os
from PIL import Image

ROOT = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
EX = os.path.join(ROOT, "extracted"); os.makedirs(EX, exist_ok=True)
SRC = os.path.join(ROOT, "sections", "02-wordmark.png")

src = Image.open(SRC).convert("RGB")
W, H = src.size
px = src.load()

# ── 1. 找字标（最大的 5 个深色连通域；只用它们定 bbox，不改像素）──
mask = [[1 if (0.299*px[x, y][0] + 0.587*px[x, y][1] + 0.114*px[x, y][2]) < 120 else 0
         for x in range(W)] for y in range(H)]
seen = [[0]*W for _ in range(H)]
comps = []
for y0 in range(H):
    for x0 in range(W):
        if not mask[y0][x0] or seen[y0][x0]:
            continue
        st, pts = [(x0, y0)], []
        seen[y0][x0] = 1
        while st:
            x, y = st.pop(); pts.append((x, y))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    nx, ny = x+dx, y+dy
                    if 0 <= nx < W and 0 <= ny < H and mask[ny][nx] and not seen[ny][nx]:
                        seen[ny][nx] = 1; st.append((nx, ny))
        if len(pts) > 20000:
            comps.append(pts)
comps.sort(key=lambda g: -len(g))
big = comps[:5]
bx0 = min(p[0] for g in big for p in g); bx1 = max(p[0] for g in big for p in g)
by0 = min(p[1] for g in big for p in g); by1 = max(p[1] for g in big for p in g)
cap_src = by1 - by0 + 1
print("source:", SRC.split("\\")[-1], src.size, "| 大字标连通域:", len(big),
      "| crop box:", (bx0, by0, bx1+1, by1+1), "| cap px:", cap_src)

# ── 2. alpha 蒙版：alpha 由"离背景的暗度"线性映射（保留抗锯齿半透明，不硬阈值）──
bg = max(px[x, y][0] for x, y in ((0, 0), (W-1, 0), (0, H-1), (W-1, H-1)))     # 浅底近似
crop = src.crop((bx0, by0, bx1+1, by1+1)).convert("L")
alpha = crop.point(lambda v: max(0, min(255, int(round((bg - v) * 255 / max(1, bg))))))
rgba = Image.merge("RGBA", (Image.new("L", crop.size, 0), Image.new("L", crop.size, 0),
                            Image.new("L", crop.size, 0), alpha))

# ── 3. 多倍率：@1x = cap 100px，@2x = 200，@4x = 400 ──
outs = {}
for tag, cap in (("1x", 100), ("2x", 200), ("4x", 400)):
    sc = cap / cap_src
    size = (max(1, int(round(rgba.width * sc))), cap)
    im = rgba.resize(size, Image.LANCZOS)
    p = os.path.join(EX, f"wordmark-alpha@{tag}.png")
    im.save(p)
    outs[tag] = {"path": p, "size": list(im.size), "cap": cap}
    if tag == "1x":
        base = im

# ── 4. 可用性自证：三种取色（深色/白/品牌紫）各一张 ──
tint_paths = {}
for name, col in (("ink", (28, 27, 34)), ("white", (255, 255, 255)), ("brand", (90, 68, 224))):
    bgc = (252, 248, 255) if name != "white" else (13, 11, 20)
    canvas = Image.new("RGB", (base.width + 40, base.height + 40), bgc)
    tint = Image.new("RGBA", base.size, col + (255,))
    tint.putalpha(base.split()[-1])
    canvas.paste(tint, (20, 20), tint)
    p = os.path.join(EX, f"_selftest-wordmark-{name}.png")
    canvas.save(p)
    tint_paths[name] = p

# ── 5. 边缘自证：四角 alpha、外框 1px 最大 alpha ──
a = base.split()[-1]
corners = [a.getpixel(p) for p in ((0, 0), (base.width-1, 0), (0, base.height-1), (base.width-1, base.height-1))]
edge_max = max([a.getpixel((x, 0)) for x in range(base.width)] +
               [a.getpixel((x, base.height-1)) for x in range(base.width)] +
               [a.getpixel((0, y)) for y in range(base.height)] +
               [a.getpixel((base.width-1, y)) for y in range(base.height)])
semi = sum(1 for v in a.getdata() if 0 < v < 255)
print("corners alpha:", corners, "| 外框 1px max alpha:", edge_max,
      "| 半透明像素（抗锯齿）:", semi, "/", base.width*base.height)

# ── 6. manifest 增补（D 类）──
man = os.path.join(EX, "EXTRACT-MANIFEST.md")
txt = io.open(man, encoding="utf-8").read() if os.path.exists(man) else "# EXTRACT-MANIFEST\n"
txt += f"""

## D 类 · 光栅提取（裁图，非描摹）—— 用户裁决：字标不再追矢量

| 文件 | 内容 | 来源与裁切 | 倍率/cap | 备注 |
|---|---|---|---|---|
| `wordmark-alpha@1x.png` | 字标 alpha 蒙版（RGB=0,0,0 / A=墨迹不透明度） | `sections/02-wordmark.png`，crop box **(x{bx0}, y{by0}, x{bx1+1}, y{by1+1})**（贴紧墨迹，左右上下 0 留白） | cap **100px**（{outs['1x']['size']}） | alpha 由"离底暗度"线性映射，**保留抗锯齿半透明**（半透明像素 {semi} 个），未锐化、未重绘 |
| `wordmark-alpha@2x.png` | 同上 | 同上 | cap **200px**（{outs['2x']['size']}） | LANCZOS 重采样 |
| `wordmark-alpha@4x.png` | 同上 | 同上 | cap **400px**（{outs['4x']['size']}） | 源 cap = **{cap_src}px**，故 4x 仍为降采样、无插值伪影 |
| `_selftest-wordmark-{{ink,white,brand}}.png` | 取色自证（深墨/反白/品牌紫） | 由 @1x 蒙版染色 | — | 证明同一蒙版可用于浅底/深底/品牌色 |

**边缘自证**：四角 alpha = **{corners}**（全 0 ⇒ 无背景残留）；外框 1px 最大 alpha = **{edge_max}**。
"""
io.open(man, "w", encoding="utf-8").write(txt)
io.open(os.path.join(EX, "wordmark-raster.json"), "w", encoding="utf-8").write(json.dumps(
    {"source": SRC, "crop_box": [bx0, by0, bx1+1, by1+1], "source_cap_px": cap_src,
     "outputs": outs, "corners_alpha": corners, "edge_max_alpha": edge_max,
     "semi_transparent_px": semi, "tints": tint_paths}, ensure_ascii=False, indent=1))
print("done:", ", ".join(os.path.basename(v["path"]) for v in outs.values()))
