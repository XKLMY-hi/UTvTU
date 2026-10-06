"""W39c-2 栅格资产全部按源几何重出 + 光学补偿在**源几何**上重调。"""
import json, os, sys
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import _generate as G, _lockup as L, _source as S

OUT, C = L.OUT, G.C


def tile(size, finish="primary"):
    bg, mk = {"primary": (C["primary_light"], "#FFFFFF"), "ink": (C["ink"], "#FFFFFF"),
              "tonal": (C["primary_container_light"], C["on_primary_container_light"])}[finish]
    ss = 4; s = size * ss
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    G.ImageDraw.Draw(img).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.2237), fill=G.hex2rgb(bg) + (255,))
    inner = int(s * 0.78)
    mark = S.render_stroke(S.MARK_SUBS, S.MARK_BOX, S.MARK_SW, inner, mk, ss=G.SS)
    img.alpha_composite(mark, ((s - inner) // 2, (s - inner) // 2))
    return img.resize((size, size), Image.LANCZOS)


def main():
    # 图标：逐尺寸渲染（源几何）
    sizes = [16, 24, 32, 48, 64, 128, 256]
    imgs = [tile(s) for s in sizes]
    imgs[-1].save(os.path.join(OUT, "utvtu.ico"), sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
    for s in (16, 24, 32):
        tile(s).save(os.path.join(OUT, f"utvtu-icon-{s}.png"))

    # 闪屏 1x/2x（源几何：{ 脸 } + UTvTU 字标轮廓）
    for tag, (w, h) in (("1x", (480, 320)), ("2x", (960, 640))):
        img = Image.new("RGB", (w, h), C["surface_light"])
        cap = int(min(w, h) * 0.14)
        mk = S.mark_ink(cap * 2.2)
        wd = L.render_wordmark(cap, C["ink"], letters=S.WM)
        b = S.brace_ink(cap * 2.6)
        total = b.width * 2 + mk.width + wd.width + cap * 0.9
        x = (w - total) // 2; y = int(h * 0.34)
        for part in (b, mk, wd, b):
            img.paste(part, (int(x), int(y + (mk.height - part.height) / 2)), part)
            x += part.width + cap * 0.3
        img.save(os.path.join(OUT, f"utvtu-splash@{tag}.png"))

    # 图标阶梯（源几何）
    ls = [256, 128, 64, 48, 32, 24, 16]
    cellw, cellh = 300, 340
    li = Image.new("RGB", (cellw * len(ls) + 40, cellh * 3 + 60), C["surface_light"])
    d = G.ImageDraw.Draw(li)
    d.text((20, 16), "W39c App 图标阶梯 —— 源几何（stroke = 尺寸÷8）", font=L.FS(20), fill=C["on_surface_light"])
    for r, fin in enumerate(("primary", "ink", "tonal")):
        for c, s in enumerate(ls):
            t = tile(s, fin)
            li.paste(t, (20 + c * cellw + (cellw - 40 - s) // 2, 60 + r * cellh + (240 - s) // 2), t)
            d.text((20 + c * cellw + 120, 60 + r * cellh + 256), f"{s}px · {fin}", font=L.FS(13),
                   fill=C["on_surface_variant_light"])
    li.save(os.path.join(OUT, "brand-icon-ladder.png"))

    # 光学补偿：在**源几何**上重调（只放细笔宽，字形不动）
    comp = {}
    for sw in (8.0, 7.0, 6.5, 6.0, 5.5, 5.0):
        row = {}
        for size in (16, 24, 32, 48, 64):
            im = S.render_stroke(S.MARK_SUBS, S.MARK_BOX, sw, size, "#000000")
            m = G.mask_of(im)
            row[str(size)] = {"components": len(G.components(m)),
                              "ink_pct": round(100.0 * sum(sum(r) for r in m) / (size * size), 2),
                              "min_gap_px": G.min_gap_px(m), "stroke_px": G.median_stroke_px(m)}
        comp[f"stroke_{sw}"] = row
    old = json.load(open(os.path.join(OUT, "brand-metrics.json"), encoding="utf-8")) if os.path.exists(
        os.path.join(OUT, "brand-metrics.json")) else {}
    with open(os.path.join(OUT, "brand-source-raster-metrics.json"), "w", encoding="utf-8") as f:
        json.dump({"optical_on_source_geometry": comp, "note": "旧 brand-metrics.json 是在错误几何上测的，作废"}, f,
                  ensure_ascii=False, indent=1)
    for k, row in comp.items():
        print(k, " ".join(f"{s}px:{v['components']}域/{v['ink_pct']}%/{v['min_gap_px']}px" for s, v in row.items()))


if __name__ == "__main__":
    main()
