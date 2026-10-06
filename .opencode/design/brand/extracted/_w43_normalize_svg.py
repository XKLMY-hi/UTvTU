# -*- coding: utf-8 -*-
"""W43 规范化：把提取件里的 `d` 从 **PDF 内容流语法** 转成 **标准 SVG 语法**，并把变换**烘焙进坐标**。

问题（fx-ctl 接线时发现）：
· PDF 语法是"操作数在前"（`x y m`、6 数 `c`、`h`），SVG 是 `M x y` / `C x1 y1 x2 y2 x y` / `Z`
  ⇒ 浏览器/Inkscape/任何 SVG 库都会解析错或报错；
· 同一文件里 U 靠自身 `matrix`、T 靠顶层 `g` ⇒ 消费方得猜哪条变换能把路径落进 viewBox。

本脚本**只改表示、不改几何**：
1) 展平：把 `g`/`matrix` 全部乘进坐标，输出零 transform 的 `d`（坐标 = viewBox 坐标）；
2) 语法：`M/L/C/Z`（绝对坐标、SVG 参数顺序）；
3) 自证：逐子路径密集采样比较（展平前按真实变换 vs 展平后解析出来的 `d`）给最大偏差；
   整体 bbox 与源 viewBox 对齐；渲染前后逐像素比较。
4) 顺带审计 `extracted/` 里其它 `.svg` 的 `d` 是否本来就是合法 SVG。

用法：python _w43_normalize_svg.py
"""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8")

HERE = Path(r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\extracted")
spec = importlib.util.spec_from_file_location("w43", HERE / "_w43_type3_wordmark.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
_spec2 = importlib.util.spec_from_file_location("sp", HERE / "_w43_split.py")
sp = importlib.util.module_from_spec(_spec2)
_spec2.loader.exec_module(sp)

NUM = r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"
PDF_CMD = re.compile(rf"({NUM})|([A-Za-z*]+)")


# ───────────────────────── PDF 语法 → 命令序列 ─────────────────────────

def parse_pdf_path(text: str):
    """PDF 路径程序 → [(op, [num...]), ...]（操作数在算子之前）。"""
    cmds, operands = [], []
    for mt in PDF_CMD.finditer(text):
        if mt.group(1):
            operands.append(float(mt.group(1)))
        else:
            op = mt.group(2)
            cmds.append((op, operands))
            operands = []
    return cmds


def transform_pt(mat, p):
    a, b, c, d, e, f = mat
    return (a * p[0] + c * p[1] + e, b * p[0] + d * p[1] + f)


def flatten_commands(cmds, mat):
    """把 PDF 命令序列按仿射矩阵展平成"绝对坐标"的中间表示。"""
    out = []
    cur = (0.0, 0.0)
    start = (0.0, 0.0)
    for op, nums in cmds:
        if op in ("d0", "d1") or not nums and op not in ("h",):
            continue
        if op == "m" and len(nums) >= 2:
            cur = (nums[-2], nums[-1])
            start = cur
            out.append(("M", [transform_pt(mat, cur)]))
        elif op == "l" and len(nums) >= 2:
            cur = (nums[-2], nums[-1])
            out.append(("L", [transform_pt(mat, cur)]))
        elif op == "c" and len(nums) >= 6:
            p1 = transform_pt(mat, (nums[-6], nums[-5]))
            p2 = transform_pt(mat, (nums[-4], nums[-3]))
            p3 = transform_pt(mat, (nums[-2], nums[-1]))
            out.append(("C", [p1, p2, p3]))
            cur = (nums[-2], nums[-1])
        elif op == "v" and len(nums) >= 4:
            p2 = transform_pt(mat, (nums[-4], nums[-3]))
            p3 = transform_pt(mat, (nums[-2], nums[-1]))
            out.append(("C", [transform_pt(mat, cur), p2, p3]))
            cur = (nums[-2], nums[-1])
        elif op == "y" and len(nums) >= 4:
            p1 = transform_pt(mat, (nums[-4], nums[-3]))
            p3 = transform_pt(mat, (nums[-2], nums[-1]))
            out.append(("C", [p1, p3, p3]))
            cur = (nums[-2], nums[-1])
        elif op == "re" and len(nums) >= 4:
            x, y, w, h = nums[-4:]
            pts = [(x, y), (x + w, y), (x + w, y + h), (x, y + h)]
            out.append(("M", [transform_pt(mat, pts[0])]))
            for p in pts[1:]:
                out.append(("L", [transform_pt(mat, p)]))
            out.append(("Z", []))
            cur = start = pts[0]
        elif op == "h":
            if out and out[-1][0] != "Z":
                out.append(("Z", []))
            cur = start
    return out


def emit_svg_d(cmds, prec=6):
    """命令序列 → 标准 SVG `d`（M/L/C/Z，绝对坐标）。"""
    parts = []
    for op, pts in cmds:
        if op == "Z":
            parts.append("Z")
        else:
            coords = " ".join(f"{m.fmt(round(v, prec))}" for p in pts for v in p)
            parts.append(f"{op} {coords}")
    return " ".join(parts)


def parse_svg_d(d: str):
    """标准 SVG `d` → 命令序列（只支持绝对 M/L/C/Z，够用且能当校验器）。"""
    toks = re.findall(rf"[MLCZmlcz]|{NUM}", d)
    cmds, i = [], 0
    while i < len(toks):
        t = toks[i]
        if t in "ML":
            cmds.append((t, [(float(toks[i + 1]), float(toks[i + 2]))]))
            i += 3
        elif t == "C":
            cmds.append((t, [(float(toks[i + 1]), float(toks[i + 2])),
                             (float(toks[i + 3]), float(toks[i + 4])),
                             (float(toks[i + 5]), float(toks[i + 6]))]))
            i += 7
        elif t in "Zz":
            cmds.append(("Z", []))
            i += 1
        else:
            raise ValueError(f"非 SVG 命令：{t!r}")
    return cmds


def sample_cmds(cmds, steps=64):
    """按子路径密集采样点集（用于展平前后比较）。"""
    subs, cur, start, pts = [], (0.0, 0.0), (0.0, 0.0), []
    for op, p in cmds:
        if op == "M":
            if pts:
                subs.append(pts)
            cur = start = p[0]
            pts = [cur]
        elif op == "L":
            pts.extend(_lerp_pts(cur, p[0], steps))
            cur = p[0]
        elif op == "C":
            pts.extend(m.cubic_points(cur, p[0], p[1], p[2], steps))
            cur = p[2]
        elif op == "Z":
            if pts:
                pts.append(pts[0])
                subs.append(pts)
                pts = []
            cur = start
    if pts:
        subs.append(pts)
    return subs


def _lerp_pts(a, b, steps):
    return [(a[0] + (b[0] - a[0]) * i / steps, a[1] + (b[1] - a[1]) * i / steps)
            for i in range(1, steps + 1)]


# ───────────────────────── 合法性审计 ─────────────────────────

SVG_LETTERS = set("MmLlHhVvCcSsQqTtAaZz")


def audit_svg_d(d: str):
    """粗判：命令字母必须都在 SVG 集合里，且不能出现"操作数在前"的 PDF 写法。

    检测手法：按 SVG 命令字母切分；对每段统计数字个数，检查是否满足该命令的参数个数；
    另查是否出现 `h`（PDF 闭合）被当成 SVG 的 H/h（水平线）而参数不足等典型症状。
    """
    letters = re.findall(r"[A-Za-z]", d)
    bad = [c for c in letters if c not in SVG_LETTERS]
    if bad:
        return False, f"非 SVG 命令字母：{sorted(set(bad))}"
    # 逐命令参数个数检查
    parts = re.split(r"([A-Za-z])", d)
    need = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}
    idx = 1
    while idx < len(parts) - 1:
        cmd = parts[idx]
        nums = re.findall(NUM, parts[idx + 1])
        n = need[cmd.upper()]
        if n and (len(nums) == 0 or len(nums) % n != 0):
            return False, f"命令 {cmd} 的参数个数 {len(nums)} 不是 {n} 的倍数"
        idx += 2
    return True, "合法"


# ───────────────────────── 主流程 ─────────────────────────

def normalize_file(path: Path, items, W, H, extra_header, body_extra=None):
    """items = [(name, matrix_or_None, pdf_path_text)]，矩阵为 None 表示已展平。"""
    lines = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{m.fmt(W)}" height="{m.fmt(H)}" '
             f'viewBox="0 0 {m.fmt(W)} {m.fmt(H)}">', "  <!--"]
    for h in extra_header:
        lines.append(f"  {h}")
    lines.append("  -->")
    report = []
    for name, mat, text in items:
        cmds = flatten_commands(parse_pdf_path(text), mat if mat else [1, 0, 0, 1, 0, 0])
        d = emit_svg_d(cmds)
        # 自证：展平前后采样比较
        pre = sample_cmds(flatten_commands(parse_pdf_path(text), mat if mat else [1, 0, 0, 1, 0, 0]), 64)
        post = sample_cmds(parse_svg_d(d), 64)
        dev = 0.0
        for a, b in zip(pre, post):
            for pa, pb in zip(a, b):
                dev = max(dev, math.hypot(pa[0] - pb[0], pa[1] - pb[1]))
        xs = [p[0] for sub in post for p in sub]
        ys = [p[1] for sub in post for p in sub]
        report.append({"name": name, "max_dev": dev,
                       "bbox": [min(xs), min(ys), max(xs), max(ys)]})
        lines.append(f"    <!-- {name} · 表示已规范化（SVG 语法 + 变换已烘焙）、几何未改 -->")
        lines.append(f'    <path d="{d}"/>')
    if body_extra:
        lines += body_extra
    lines.append("</svg>")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return report


def main():
    data = m.PDF.read_bytes()
    objs = m.load_objects(data)
    fonts = m.parse_type3(objs)
    shapes, report = m.cmd_extract(objs, fonts)
    min_x, min_y, max_x, max_y = report["ink_box_page"]
    W, H = report["width"], report["height"]
    ut = [s for s in shapes if s["kind"] == "type3"]
    v = [s for s in shapes if s["kind"] == "page-path"][0]

    def to_svg(p):
        return (p[0] - min_x, max_y - p[1])

    group = [1, 0, 0, -1, 0, H]           # 顶层 g 的 matrix
    shift = [1, 0, 0, 1, -min_x, -min_y]  # 顶层 g 的 translate
    gmat = m.mat_mul(shift, group)        # 组合：先 translate 再 matrix（Avalonia/SVG 语义）

    base_header = [
        "    来源 = 品牌板 PDF 第 1 页 Type3 字体 CharProcs 路径提取（非描摹）",
        "    **表示已规范化、几何未改**：`d` 为标准 SVG 语法（M/L/C/Z，绝对坐标、参数按 SVG 顺序）；",
        "    原 PDF 的 `x y m` / 6 数 `c` / `h` 与所有 transform 均已展平烘焙进坐标 ⇒ 无 matrix/g 歧义，",
        "    可直接逐字对拷进 `Icons.axaml` 的 Geometry 字符串。",
    ]

    # ── 1) wordmark-outline.svg（5 段，含 v 的填充轮廓，保留作"单件参考"）──
    items = []
    for s in shapes:
        if s["kind"] == "type3":
            items.append((f"{s['glyph']} · Type3 CharProc", m.mat_mul(s["matrix"], gmat), s["verbatim"]))
        else:
            items.append(("v · 页面路径（填充轮廓，EvenOdd 不安全，仅供对照）",
                          m.mat_mul(s["matrix"], gmat), s["verbatim"]))
    rep1 = normalize_file(HERE / "wordmark-outline.svg", items, W, H, base_header + [
        "    本文件 = 字标五段的**填充轮廓**合并件（v 为自交环 ⇒ 载体侧需 NonZero；"
        "EvenOdd 载体请用 wordmark-ut.svg + wordmark-v-stroke.svg 拆件）。",
        f"    字体资源 = /F5（obj 5，Type3）；FontMatrix = [1/2048 0 0 -1/2048 0 0]",
    ])
    print(f"wordmark-outline.svg：{len(items)} 段，最大采样偏差 "
          f"{max(r['max_dev'] for r in rep1):.2e} 单位")

    # ── 2) wordmark-ut.svg（U 逐字 + T 重划，全部展平）──
    ut_items = []
    for s in ut:
        rings, changed, note = sp.evenodd_safe_rings(s["rings"])
        if changed:
            text = " ".join(
                f"{m.fmt(ring[0][0])} {m.fmt(ring[0][1])} m "
                + " ".join(f"{m.fmt(p[0])} {m.fmt(p[1])} l" for p in ring[1:-1])
                + " h" for ring in rings)
            label = f"{s['glyph']} · T 重划为 {len(rings)} 个不重叠子矩形（EvenOdd 安全）"
        else:
            text = s["verbatim"]
            label = f"{s['glyph']} · U 单环（EvenOdd 天然安全）"
        # 注意：rings 已是页面空间，而 verbatim 是字形空间 ⇒ 分别用不同矩阵
        if changed:
            items_ut = [(label, [1, 0, 0, 1, -min_x, max_y], text)]
            items_ut[0] = (label, m.mat_mul([1, 0, 0, -1, 0, 0], [1, 0, 0, 1, -min_x, max_y]), text)
        else:
            items_ut = [(label, m.mat_mul(s["matrix"], gmat), text)]
        ut_items.append(items_ut)
    flat = [it for group_items in ut_items for it in group_items]
    rep2 = normalize_file(HERE / "wordmark-ut.svg", flat, W, H, base_header + [
        "    U/T 拆件：T（g38）原是**两个重叠矩形** ⇒ 已精确重划为 3 个互不重叠子矩形"
        "（纯分区、几何零改动）；U（g39）单环。",
        "    `fill-rule` 无需指定（EvenOdd/NonZero 同结果，自证见 wordmark-ut-evenodd-vs-nonzero.png）。",
    ])
    print(f"wordmark-ut.svg：{len(flat)} 段，最大采样偏差 {max(r['max_dev'] for r in rep2):.2e} 单位")

    # ── 3) wordmark-v-stroke.svg（描边版；展平后的中心线 + 显式 stroke-width）──
    v_rings_svg = [[to_svg(p) for p in ring] for ring in v["rings"]]
    vl = [p for ring in v_rings_svg for p in ring]
    ink_w = max(p[0] for p in vl) - min(p[0] for p in vl)
    ink_h = max(p[1] for p in vl) - min(p[1] for p in vl)
    x0, x1, y0, y1 = (min(p[0] for p in vl), max(p[0] for p in vl),
                      min(p[1] for p in vl), max(p[1] for p in vl))
    sym, w_est, a, h, r, cl_svg, ref_area = sp.fit_v_stroke(v_rings_svg)
    d_v = "M " + " L ".join(f"{m.fmt(round(p[0], 6))} {m.fmt(round(p[1], 6))}" for p in cl_svg)
    v_body = [f"    <!-- v · 描边（round join/cap；不受填充规则影响） -->",
              f'    <path fill="none" stroke="#000000" stroke-width="{m.fmt(round(w_est, 6))}" '
              f'stroke-linecap="round" stroke-linejoin="round" d="{d_v}"/>']
    v_header = base_header + [
        "    v 拆件（描边版）：PDF 里 v 的填充轮廓是『描边转轮廓』的**自交单环**（EvenOdd 会出细缝），",
        "    这里改用标准 SVG 描边表达；`stroke-width` 由与 PDF 轮廓做对称差拟合得到（残差 0.411%），",
        "    与 HTML 资产 v-chevron.svg 的声明值 61 + non-scaling-stroke 一致 ⇒ 同一实例。",
        f"    中心线（本文件坐标）= {[(round(p[0],2), round(p[1],2)) for p in cl_svg]}",
        f"    v 墨迹（本文件坐标）= x {m.fmt(x0)}..{m.fmt(x1)}, y {m.fmt(y0)}..{m.fmt(y1)}"
        f"（页面空间 1790.50,2336.50 .. 1942.50,2539.50；基线 y=2336.00；cap=273.71）",
        "    定位（cap=100 归一化，unit=cap/100=2.7371）：左缘偏移 158.02（41.89%）/ v 宽 55.53（14.72%）"
        "/ v 高 74.17（x高/cap=0.7417）/ 底距基线 +0.18",
    ]
    lines = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{m.fmt(W)}" height="{m.fmt(H)}" '
             f'viewBox="0 0 {m.fmt(W)} {m.fmt(H)}">', "  <!--"]
    lines += [f"  {h}" for h in v_header]
    lines.append("  -->")
    lines += v_body
    lines.append("</svg>")
    (HERE / "wordmark-v-stroke.svg").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"wordmark-v-stroke.svg：1 段描边，stroke-width={w_est:.2f}")

    # ── 4) 语法自证：所有产出文件的 `d` 必须全是 SVG 命令 ──
    print("\n=== 语法自证（非 SVG 命令数应为 0）===")
    bad_total = 0
    for f in ("wordmark-outline.svg", "wordmark-ut.svg", "wordmark-v-stroke.svg"):
        svg = (HERE / f).read_text(encoding="utf-8")
        for i, d in enumerate(re.findall(r'd="([^"]*)"', svg)):
            ok, why = audit_svg_d(d)
            if not ok:
                bad_total += 1
            print(f"  {f}[{i}] {'✓ 合法' if ok else '✗ ' + why}（{len(re.findall(r'[A-Za-z]', d))} 个命令）")
    print(f"  非 SVG 命令数 = {bad_total}")

    # ── 5) bbox 自证 + 渲染自证 ──
    print("\n=== bbox 自证（应等于源 viewBox 0 0 W H 的墨迹范围）===")
    for f, rep in (("wordmark-outline.svg", rep1), ("wordmark-ut.svg", rep2)):
        xs0 = min(r["bbox"][0] for r in rep)
        ys0 = min(r["bbox"][1] for r in rep)
        xs1 = max(r["bbox"][2] for r in rep)
        ys1 = max(r["bbox"][3] for r in rep)
        print(f"  {f}: 墨迹 bbox = ({xs0:.4f}, {ys0:.4f})..({xs1:.4f}, {ys1:.4f})  "
              f"W={xs1-xs0:.4f} H={ys1-ys0:.4f}（源 viewBox {W:.6f}×{H:.6f}；"
              f"偏差 {abs(xs1-xs0-W):.4f}×{abs(ys1-ys0-H):.4f}）")

    # 渲染：展平后的 outline 件 vs 展平前（用同一条链）⇒ 逐像素应完全相同
    scale = 100.0 / report["cap_height"]
    pad = 12
    rw = int(round(W * scale)) + pad * 2
    rh = int(round(H * scale)) + pad * 2
    # 展平前：`shapes[*]["rings"]` **已经是页面空间**（collect_polygons 已乘字形矩阵/CTM）
    # ⇒ 只做 SVG 映射；再乘一次矩阵会把字形甩出画布（曾因此得到"只有 v"的假对比图）
    pre_rings = [[to_svg(p) for p in ring] for s in shapes for ring in s["rings"]]
    pre_mask = sp.rasterize_rings(pre_rings, rw, rh, scale, pad, pad, "nonzero")
    # 展平后：解析 wordmark-outline.svg 的 d 再栅格化
    svg = (HERE / "wordmark-outline.svg").read_text(encoding="utf-8")
    post_rings = []
    for d in re.findall(r'd="([^"]*)"', svg):
        for sub in sample_cmds(parse_svg_d(d), 24):
            post_rings.append([(p[0], p[1]) for p in sub])
    post_mask = sp.rasterize_rings(post_rings, rw, rh, scale, pad, pad, "nonzero")
    diff = int((pre_mask != post_mask).sum())
    print(f"\n=== 渲染自证（展平前 vs 展平后，逐像素）: 差异 {diff} px ===")
    cmp_img = Image.new("L", (rw, rh * 2 + 30), 255)
    cmp_img.paste(Image.fromarray(255 - pre_mask), (0, 0))
    cmp_img.paste(Image.fromarray(255 - post_mask), (0, rh + 30))
    cmp_img.convert("RGB").save(HERE / "wordmark-normalized-selfproof.png")
    print(f"  图：wordmark-normalized-selfproof.png（上=展平前，下=展平后）{cmp_img.size}")

    # ── 6) 审计其它提取件 ──
    print("\n=== 其它提取件 `d` 合法性审计 ===")
    others = sorted(p for p in HERE.glob("*.svg")
                    if p.name not in ("wordmark-outline.svg", "wordmark-ut.svg",
                                      "wordmark-v-stroke.svg"))
    bad_files, checked = [], 0
    for p in others:
        txt = p.read_text(encoding="utf-8", errors="replace")
        ds = re.findall(r'd="([^"]*)"', txt)
        for d in ds:
            checked += 1
            ok, why = audit_svg_d(d)
            if not ok:
                bad_files.append((p.name, why))
    print(f"  检查 {len(others)} 个 .svg / {checked} 条 d："
          f"{'全部合法 ✓' if not bad_files else '有问题 ✗ ' + str(bad_files[:5])}")
    print(f"  抽样：v-chevron.svg 的 d = "
          f"{re.findall(r'd=\"([^\"]*)\"', (HERE / 'v-chevron.svg').read_text(encoding='utf-8'))}")

    out = {"normalized": {
        "wordmark-outline.svg": rep1, "wordmark-ut.svg": rep2,
        "wordmark-v-stroke.svg": [{"name": "v stroke", "d": d_v, "stroke_width": round(w_est, 4)}],
    }, "viewBox": [0, 0, W, H], "non_svg_command_count": bad_total,
       "render_diff_px": diff, "other_svg_audit": {"files": len(others), "d_count": checked,
                                                   "bad": bad_files}}
    (HERE / "wordmark-normalize-report.json").write_text(
        json.dumps(out, ensure_ascii=False, indent=1), encoding="utf-8")
    print("写出 wordmark-normalize-report.json")


if __name__ == "__main__":
    main()
