# -*- coding: utf-8 -*-
"""W43 数值自证：我的提取 vs 设计稿参考 的墨迹重合度（按列带分解，定位差异）。"""
import importlib.util
import sys
import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")

spec = importlib.util.spec_from_file_location(
    "w43", r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\extracted\_w43_type3_wordmark.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

# 我的：cap=100 渲染图（去掉 12px 边距后再取墨迹框）
mine = np.asarray(Image.open(m.OUT / "wordmark-outline-cap100.png").convert("L")).astype(int)
mi = mine < 200
ys, xs = np.nonzero(mi)
mine_ink = mi[ys.min():ys.max() + 1, xs.min():xs.max() + 1]

# 参考：大字号那一行
ref = Image.open(m.BRAND / "sections" / "02-wordmark.png").convert("RGB")
a = np.asarray(ref).astype(int)
ink = a.mean(axis=2) < 128
band = ink[200:900, 60:2280]
row_ink = band.sum(axis=1)
best, y = (0, 0), 0
while y < len(row_ink):
    if row_ink[y] > 0:
        y0 = y
        while y < len(row_ink) and row_ink[y] > 0:
            y += 1
        if y - y0 > best[1] - best[0]:
            best = (y0, y)
    else:
        y += 1
ty0, ty1 = best[0] + 200, best[1] + 200
cols = ink[ty0:ty1, 60:2280].sum(axis=0)
nz = np.nonzero(cols)[0]
ref_ink = ink[ty0:ty1, nz.min() + 60:nz.max() + 1 + 60]

print(f"我的墨迹 {mine_ink.shape[1]}×{mine_ink.shape[0]}  参考墨迹 {ref_ink.shape[1]}×{ref_ink.shape[0]}")
print(f"宽高比 我={mine_ink.shape[1]/mine_ink.shape[0]:.4f}  参考={ref_ink.shape[1]/ref_ink.shape[0]:.4f}")

# 参考按高度缩放到我的尺寸（保持它自己的宽高比），再居中并排比较
h = mine_ink.shape[0]
w = int(round(ref_ink.shape[1] * h / ref_ink.shape[0]))
ref_img = Image.fromarray((ref_ink * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)
ref_m = np.asarray(ref_img).astype(int) > 127

# 水平方向：按各自的墨迹中轴对齐（右列 1:1）
canvas_w = max(mine_ink.shape[1], w)
A = np.zeros((h, canvas_w), bool)
B = np.zeros((h, canvas_w), bool)
A[:, (canvas_w - mine_ink.shape[1]) // 2:(canvas_w - mine_ink.shape[1]) // 2 + mine_ink.shape[1]] = mine_ink
B[:, (canvas_w - w) // 2:(canvas_w - w) // 2 + w] = ref_m
inter = (A & B).sum()
union = (A | B).sum()
print(f"\n整体 IoU（按高对齐+居中）= {inter/union:.4f}   我={A.sum()} px  参考={B.sum()} px")

print("\n=== 按列带分解（每 10% 宽一段）===")
for i in range(10):
    x0 = int(canvas_w * i / 10); x1 = int(canvas_w * (i + 1) / 10)
    a_s, b_s = A[:, x0:x1], B[:, x0:x1]
    u = (a_s | b_s).sum()
    print(f"  段{i}: IoU={(a_s & b_s).sum()/u if u else 0:.3f}  我={a_s.sum():5d} 参考={b_s.sum():5d}")

# 每段的水平质心（看是否错位）
print("\n=== 列质心（我 vs 参考，归一化 x）===")
for name, M in (("我", A), ("参考", B)):
    colsum = M.sum(axis=0)
    idx = np.nonzero(colsum)[0]
    print(f"  {name}: x {idx.min()}/{canvas_w} .. {idx.max()}/{canvas_w} 质心={np.average(np.arange(canvas_w), weights=colsum):.1f}")
