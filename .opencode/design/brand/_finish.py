"""W39d 收尾：源切角 v 的字距微调复测 + 阶梯确认 + 全套资产重出 + 高倍对照。"""
import json, os, sys, shutil
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import _generate as G, _lockup as L, _source as S

OUT, SHOTS, C = L.OUT, L.SHOTS, G.C


def spaced(delta):
    """把 v 两侧的 T（第 2、4 个字）向外各移 delta 单位（cap=100 空间）。"""
    letters = [dict(l) for l in S.WM]
    for i in (1, 3):
        d = -delta if i == 1 else delta
        letters[i]["pts"] = [(x + d, y) for x, y in letters[i]["pts"]]
    return letters


def measure(letters, cap):
    im = L.render_wordmark(cap, "#000000", letters=letters)
    m = G.mask_of(im)
    return {"cap": cap, "components": len(G.components(m)), "min_gap_px": G.min_gap_px(m),
            "stroke_px": G.median_stroke_px(m)}


def main():
    grid = {}
    best = None
    for delta in (0.0, 1.0, 2.0, 3.0):
        ls = spaced(delta)
        rows = [measure(ls, c) for c in (14, 16, 20, 28)]
        grid[f"delta_{delta:g}"] = rows
        score = sum(min(r["components"], 5) for r in rows) + (10 if all(r["components"] >= 5 for r in rows[1:]) else 0)
        if best is None or score > best[0]:
            best = (score, delta, ls)
    _, delta, letters = best
    S.WM = letters
    L.WORDMARK = letters

    # ── 全套资产重出（3×3 + 阶梯 + 新旧括号）──
    rows_33 = []
    for name in S.LOCKUPS:
        imgs = []
        for fin in S.FINISHES:
            im = S.compose(name, fin, 44)
            imgs.append(im)
            for tag, cap in (("1x", 24), ("2x", 48)):
                S.compose(name, fin, cap).save(os.path.join(OUT, f"utvtu-lockup-{name}-{fin}@{tag}.png"))
            with open(os.path.join(OUT, f"utvtu-lockup-{name}-{fin}.svg"), "w", encoding="utf-8") as f:
                f.write(S.lockup_svg(name, fin))
        rows_33.append(({"signature": "{ 脸 UTvTU }", "display": "{ UTvTU }", "short": "{ 脸 }"}[name], imgs,
                        "finish：ink · reversed · branded（括号品牌色）"))
    p1 = L.sheet(rows_33, os.path.join(OUT, "brand-lockup-sheet.png"),
                 "W39d 三锁定 × 三 finish（cap 44px，源几何 + 源切角 v，字距微调 %.0f 单位）" % delta)
    ladder = [(f"cap {c}px", [S.compose(n, "ink", c) for n in ("signature", "display", "short")]
               + [L.render_wordmark(c, C["ink"], letters=letters)],
               "字标实测：" + " · ".join(f"cap{r['cap']}={r['components']}域/间距{r['min_gap_px']}px" for r in grid[f"delta_{delta:g}"]))
              for c in (14, 16, 20, 28)]
    p2 = L.sheet(ladder, os.path.join(OUT, "brand-lockup-ladder.png"),
                 "W39d 尺寸阶梯（字距微调 %.0f 单位后）" % delta)

    # ── 高倍对照：源切角 v（填充/描边）与字标 8× ──
    Z = 8
    v_fill = S.render_fill_paths(S.parse_path(S.V_FILL_D), S.V_BOX, 14 * Z, C["ink"])
    v_stroke = S.render_stroke(S.parse_path(S.V_STROKE_D), S.V_BOX, 2.2, 14 * Z, C["ink"])
    wm28 = L.render_wordmark(28, C["ink"], letters=letters).resize((L.render_wordmark(28, C["ink"], letters=letters).width * Z,
                                                                  28 * Z), Image.NEAREST)
    canvas = Image.new("RGB", (760, 380), C["surface_light"])
    d = ImageDraw.Draw(canvas)
    d.text((16, 12), "W39d 8× 健全性对照：源切角 v（填充/描边） + 字标（源 v + 描摹 U/T，字距微调后）", font=L.FS(14), fill=C["on_surface_light"])
    canvas.paste(v_fill, (30, 60), v_fill)
    canvas.paste(v_stroke, (160, 60), v_stroke)
    canvas.paste(wm28, (30, 120), wm28)
    d.text((30, 44), "填充版 (9×14)", font=L.FS(11), fill=C["on_surface_variant_light"])
    d.text((160, 44), "描边版", font=L.FS(11), fill=C["on_surface_variant_light"])
    p3 = os.path.join(OUT, "brand-verify-v-and-wordmark-8x.png")
    canvas.save(p3)

    for p in (p1, p2, p3):
        shutil.copyfile(p, os.path.join(SHOTS, os.path.basename(p)))
    with open(os.path.join(OUT, "brand-wordmark-spacing.json"), "w", encoding="utf-8") as f:
        json.dump({"chosen_delta": delta, "grid": grid,
                   "note": "v 换成源切角 v 后，把两侧 T 向外移 chosen_delta 单位（cap=100 空间）"}, f,
                  ensure_ascii=False, indent=1)
    for k, v in grid.items():
        print(k, " ".join(f"cap{r['cap']}:{r['components']}域/{r['min_gap_px']}px" for r in v))
    print("chosen delta =", delta)


if __name__ == "__main__":
    main()
