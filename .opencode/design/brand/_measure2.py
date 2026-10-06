"""W37 一次性量测（2/2）：在主标志渲染上取游程，反解 64 网格坐标。"""
import json
from collections import deque

from PIL import Image

BRAND = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
img = Image.open(f"{BRAND}/sections/00-brand-components.png").convert("RGB")
px = img.load()
W, H = img.size

# 品牌紫 #5A44E0
PURPLE = (0x5A, 0x44, 0xE0)


def is_ink(x, y):
    r, g, b = px[x, y]
    return abs(r - PURPLE[0]) + abs(g - PURPLE[1]) + abs(b - PURPLE[2]) < 120


def runs_x(y, x0, x1):
    out, run, start = [], 0, None
    for x in range(x0, x1):
        if is_ink(x, y):
            if run == 0:
                start = x
            run += 1
        elif run:
            out.append((start, x - 1, run))
            run = 0
    if run:
        out.append((start, x1 - 1, run))
    return out


def runs_y(x, y0, y1):
    out, run, start = [], 0, None
    for y in range(y0, y1):
        if is_ink(x, y):
            if run == 0:
                start = y
            run += 1
        elif run:
            out.append((start, y - 1, run))
            run = 0
    if run:
        out.append((start, y1 - 1, run))
    return out


rep = {}
# 主标志整体 bbox（从上次量测：x 216..551, y 246..521）
rep["mark_bbox"] = [216, 246, 551, 521]
# 左 T：竖干中心 x≈287，横杠下方扫笔宽；横杠厚度扫 y=260
rep["T_bar_thickness_y260"] = runs_x(260, 200, 580)
rep["T_stem_at_y340"] = runs_x(340, 200, 580)
rep["T_stem_bottom_x287"] = runs_y(287, 240, 420)
rep["T_bar_span_y250"] = runs_x(250, 200, 580)
# v 嘴：竖直扫中心 x=383
rep["v_center_x383"] = runs_y(383, 300, 440)
rep["v_at_y340"] = runs_x(340, 320, 450)
# 左 U：竖干 x=240（左臂）与 x=335（右臂）
rep["U_left_arm_x240"] = runs_y(240, 400, 540)
rep["U_right_arm_x335"] = runs_y(335, 400, 540)
rep["U_bottom_y500"] = runs_x(500, 200, 580)
rep["U_top_y420"] = runs_x(420, 200, 580)
# 括号（下半部，y 800..1100）：先量 bbox
ys = [y for y in range(700, H) for x in range(260, 460) if is_ink(x, y)]
xs = [x for x in range(200, 520) for y in range(800, 1120) if is_ink(x, y)]
rep["brace_bbox_x"] = [min(xs), max(xs)]
rep["brace_bbox_y"] = [min(ys), max(ys)]
bx0, bx1 = min(xs), max(xs)
by0, by1 = min(ys), max(ys)
mid_y = (by0 + by1) // 2
rep["brace_runs_at_mid"] = runs_x(mid_y, bx0 - 40, bx1 + 40)
rep["brace_runs_at_top"] = runs_x(by0 + 10, bx0 - 40, bx1 + 40)
rep["brace_center_col"] = runs_y((bx0 + bx1) // 2, by0 - 20, by1 + 20)

print(json.dumps(rep, ensure_ascii=False))
