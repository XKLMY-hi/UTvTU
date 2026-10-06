"""W37 一次性几何量测：从品牌板 PNG 反解 64 网格几何（结果写进 .opencode/plans/brand-assets.md）。

只读品牌包，不改任何东西。用连通域 + 游程量出：
  - 主标志 5 字形各自的墨迹包围盒与笔宽（换算到 64 网格）
  - 括号墨迹/框/笔宽
  - App 图标瓦片与标志的占比、安全圈
"""
import json
import sys
from collections import deque

from PIL import Image

BRAND = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
OUT = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\_measure.json"


def load_mask(path, box=None, bg_tol=28):
    """把图片转成"墨迹"布尔掩码：与左上角/白色背景差异大的像素算墨迹。"""
    img = Image.open(path).convert("RGB")
    if box:
        img = img.crop(box)
    w, h = img.size
    px = img.load()
    # 背景色取样自四角的中位数
    corners = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
    bg = tuple(sorted(c[i] for c in corners)[len(corners) // 2] for i in range(3))
    mask = [[False] * w for _ in range(h)]
    for y in range(h):
        row = mask[y]
        for x in range(w):
            r, g, b = px[x, y]
            if abs(r - bg[0]) + abs(g - bg[1]) + abs(b - bg[2]) > bg_tol:
                row[x] = True
    return mask, img


def components(mask):
    """8 连通域，返回每个域的 (像素数, bbox)。"""
    h, w = len(mask), len(mask[0])
    seen = [[False] * w for _ in range(h)]
    out = []
    for y0 in range(h):
        for x0 in range(w):
            if not mask[y0][x0] or seen[y0][x0]:
                continue
            q = deque([(x0, y0)])
            seen[y0][x0] = True
            n = 0
            x1 = x2 = x0
            y1 = y2 = y0
            while q:
                x, y = q.popleft()
                n += 1
                x1, x2 = min(x1, x), max(x2, x)
                y1, y2 = min(y1, y), max(y2, y)
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            q.append((nx, ny))
            out.append({"pixels": n, "bbox": [x1, y1, x2, y2],
                        "w": x2 - x1 + 1, "h": y2 - y1 + 1})
    return sorted(out, key=lambda c: -c["pixels"])


def stroke_width(mask, y, x_from, x_to):
    """在某一行上量最近一段连续墨迹的长度（近似笔宽）。"""
    runs, run = [], 0
    for x in range(x_from, x_to):
        if mask[y][x]:
            run += 1
        elif run:
            runs.append(run)
            run = 0
    if run:
        runs.append(run)
    return runs


def main():
    result = {}

    # ── 1. 主标志（sections/00 上半部）──
    mask, img = load_mask(f"{BRAND}/sections/00-brand-components.png", box=(0, 0, 1280, 700))
    comps = components(mask)
    result["mark_components_raw"] = comps
    result["mark_image_size"] = list(img.size)

    # ── 2. 括号（sections/00 下半部）──
    mask_b, img_b = load_mask(f"{BRAND}/sections/00-brand-components.png", box=(0, 700, 1280, 1280))
    comps_b = components(mask_b)
    result["brace_components_raw"] = comps_b
    result["brace_image_size"] = list(img_b.size)

    # ── 3. App 图标瓦片（sections/01 主图标区域）──
    mask_i, img_i = load_mask(f"{BRAND}/sections/01-app-icon.png", box=(110, 280, 800, 940))
    comps_i = components(mask_i)
    result["icon_components_raw"] = comps_i
    result["icon_image_size"] = list(img_i.size)

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=1)

    print(json.dumps(result, ensure_ascii=False, indent=1)[:4000])


if __name__ == "__main__":
    main()
