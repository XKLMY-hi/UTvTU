import importlib, io, os, sys
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)

# 1) 修 _source.py 里被补丁打断的缩进（table["overlay_iou"] 掉到 0 列）
p = os.path.join(HERE, "_source.py")
s = io.open(p, encoding="utf-8").read()
s = s.replace('\ntable["overlay_iou"] = io\n', '\n    table["overlay_iou"] = io\n')
io.open(p, "w", encoding="utf-8").write(s)

import _source as S
importlib.reload(S)
S.main()
import _raster as R
importlib.reload(R)
R.main()

OUT = R.OUT
wm48 = S.L.render_wordmark(48, R.C["ink"], letters=S.WM)
wm48.save(os.path.join(OUT, "brand-verify-wordmark-1to1.png"))
z = wm48.crop((0, 0, min(150, wm48.width), wm48.height))
z = z.resize((z.width * 4, z.height * 4), Image.NEAREST)
canvas = Image.new("RGB", (max(720, z.width + 40), z.height + 60), R.C["surface_light"])
canvas.paste(z, (20, 44))
ImageDraw.Draw(canvas).text((20, 14), "字标 4× 局部：源切角 v + 描摹 U/T（同一坐标系）", font=R.L.FS(14),
                            fill=R.C["on_surface_light"])
canvas.save(os.path.join(OUT, "brand-verify-wordmark-zoom.png"))
sp = Image.open(os.path.join(OUT, "utvtu-splash@1x.png"))
sp.save(os.path.join(OUT, "brand-verify-splash-1x.png"))
print("SANITY:", S.WM_SANITY)
print("wordmark px:", wm48.size, "| splash px:", sp.size)
