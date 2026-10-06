# -*- coding: utf-8 -*-
"""macOS `.icns` 生成：**几何源 = extracted/mark.svg（逐字）**，不描摹、不改设计。

口径与 Windows `.ico` 一致（同 `_generate.py` 的 tile()）：
  · 底：squircle 圆角 = 0.2237 × 尺寸，填**品牌紫** `#5A44E0`；
  · 标：**白色**，放在 0.78 × 尺寸的内框里，描边 = 内框 ÷ 8（规范：描边 = 尺寸÷8），圆头端帽/圆角连接；
  · 4× 超采样后 LANCZOS 降采样。

产出：`extracted/icns/utvtu-<size>.png`（16…1024）+ `extracted/utvtu.icns`；
自证：容器读回（magic/总长/各条目类型与 PNG 尺寸）、256 档主色统计、尺寸-字节表。

用法：python _w43_icns.py
"""
import importlib.util
import json
import re
import struct
import sys
from collections import Counter
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.stdout.reconfigure(encoding="utf-8")

HERE = Path(r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\extracted")
_s = importlib.util.spec_from_file_location("sp", HERE / "_w43_split.py")
sp = importlib.util.module_from_spec(_s)
_s.loader.exec_module(sp)

MARK_SVG = HERE / "mark.svg"
OUT_DIR = HERE / "icns"
SIZES = (16, 32, 64, 128, 256, 512, 1024)
BG = (90, 68, 224)          # #5A44E0 品牌紫（与 Windows ico 实测一致）
FG = (255, 255, 255)
SQUIRCLE = 0.2237           # 与 _generate.py tile() 同口径
INNER = 0.78
SS = 4                      # 超采样倍数

NUM = r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?"
TOK = re.compile(rf"([MmLlHhVvCcSsQqTtAaZz])|({NUM})")


# ───────────────────────── SVG 路径解析（绝对化 + 曲线采样） ─────────────────────────

def parse_svg_path(d):
    """SVG `d` → [(cmd, [点…]), …]（绝对坐标；H/V 转 L，S/T 转 C/Q，A 不支持则报错）。"""
    toks, i = [], 0
    for mt in TOK.finditer(d):
        toks.append(mt.group(1) if mt.group(1) else float(mt.group(2)))
    out, cur, start, prev_c2 = [], (0.0, 0.0), (0.0, 0.0), None
    cmd = None
    while i < len(toks):
        t = toks[i]
        if isinstance(t, str):
            cmd = t
            i += 1
            if cmd in "Zz":
                out.append(("Z", []))
                cur = start
                continue
        nums = []
        while i < len(toks) and not isinstance(toks[i], str):
            nums.append(toks[i])
            i += 1
        rel = cmd.islower()
        C = cmd.upper()
        need = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7}[C]
        for k in range(0, len(nums) - need + 1, need):
            a = nums[k:k + need]
            if C == "M":
                p = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                out.append(("M", [p]))
                cur = start = p
                C = "L"      # 后续隐式 lineto
            elif C == "L":
                p = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                out.append(("L", [p]))
                cur = p
            elif C == "H":
                p = (a[0] + (cur[0] if rel else 0), cur[1])
                out.append(("L", [p]))
                cur = p
            elif C == "V":
                p = (cur[0], a[0] + (cur[1] if rel else 0))
                out.append(("L", [p]))
                cur = p
            elif C == "C":
                p1 = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                p2 = (a[2] + (cur[0] if rel else 0), a[3] + (cur[1] if rel else 0))
                p3 = (a[4] + (cur[0] if rel else 0), a[5] + (cur[1] if rel else 0))
                out.append(("C", [p1, p2, p3]))
                prev_c2, cur = p2, p3
            elif C == "S":
                p1 = (2 * cur[0] - prev_c2[0], 2 * cur[1] - prev_c2[1]) if prev_c2 else cur
                p2 = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                p3 = (a[2] + (cur[0] if rel else 0), a[3] + (cur[1] if rel else 0))
                out.append(("C", [p1, p2, p3]))
                prev_c2, cur = p2, p3
            elif C == "Q":
                q = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                p = (a[2] + (cur[0] if rel else 0), a[3] + (cur[1] if rel else 0))
                out.append(("Q", [q, p]))
                prev_c2, cur = q, p
            elif C == "T":
                q = (2 * cur[0] - prev_c2[0], 2 * cur[1] - prev_c2[1]) if prev_c2 else cur
                p = (a[0] + (cur[0] if rel else 0), a[1] + (cur[1] if rel else 0))
                out.append(("Q", [q, p]))
                prev_c2, cur = q, p
            else:
                raise ValueError("不支持的命令 A（本图形不需要）")
    return out


def to_polylines(cmds, steps=48):
    """命令序列 → 折线列表（圆头描边用）。"""
    lines, cur = [], None
    for cmd, pts in cmds:
        if cmd == "M":
            if cur and len(cur) > 1:
                lines.append(cur)
            cur = [pts[0]]
        elif cmd == "L":
            cur.append(pts[0])
        elif cmd == "C":
            cur.extend(sp.m.cubic_points(cur[-1], pts[0], pts[1], pts[2], steps))
        elif cmd == "Q":
            q, p = pts
            for k in range(1, steps + 1):
                t = k / steps
                mt = 1 - t
                cur.append((mt * mt * cur[-1][0] + 2 * mt * t * q[0] + t * t * p[0],
                            mt * mt * cur[-1][1] + 2 * mt * t * q[1] + t * t * p[1]))
        elif cmd == "Z":
            if cur:
                cur.append(cur[0])
                lines.append(cur)
                cur = None
    if cur and len(cur) > 1:
        lines.append(cur)
    return lines


# ───────────────────────── 瓦片渲染 ─────────────────────────

def render_tile(size, lines, view_w, view_h, stroke_units):
    """squircle 紫底 + 白色描边标志（4× 超采样 → LANCZOS）。

    描边用 PIL 的 line(width, joint="curve")（圆角连接）+ 端帽圆点（圆头端帽），
    比逐段距离场快几个数量级，且在 4× 超采样下足够精确。
    """
    s = size * SS
    tile = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)
    r = int(s * SQUIRCLE)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=BG + (255,))

    inner = int(s * INNER)
    off = (s - inner) // 2
    scale = inner / max(view_w, view_h)
    # 规范口径：**描边 = 尺寸 ÷ 8**（mark.svg 声明 10/64 = 15.6% 是它自己那个 80px 盒的口径；
    # 图标瓦片按品牌规范用 1/8，与 Windows .ico 的白墨占比对齐）
    sw = max(1, int(round(inner / 8.0)))
    layer = Image.new("L", (inner, inner), 0)
    dl = ImageDraw.Draw(layer)
    rad = sw / 2.0
    for line in lines:
        pts = [(x * scale, y * scale) for x, y in line]
        dl.line(pts, fill=255, width=sw, joint="curve")
        for p in (pts[0], pts[-1]):                    # 圆头端帽
            dl.ellipse([p[0] - rad, p[1] - rad, p[0] + rad, p[1] + rad], fill=255)
    tile.paste(Image.new("RGBA", (inner, inner), FG + (255,)), (off, off), layer)
    return tile.resize((size, size), Image.LANCZOS)


# ───────────────────────── ICNS 容器 ─────────────────────────

ICNS_TYPES = [
    ("icp4", 16), ("icp5", 32), ("ic11", 32), ("ic12", 64),
    ("ic07", 128), ("ic08", 256), ("ic13", 256), ("ic09", 512),
    ("ic14", 512), ("ic10", 1024),
]


def build_icns(png_by_size):
    body = b""
    for typ, size in ICNS_TYPES:
        payload = png_by_size[size]
        entry = typ.encode("ascii") + struct.pack(">I", 8 + len(payload)) + payload
        body += entry
    return b"icns" + struct.pack(">I", 8 + len(body)) + body


def read_icns(data: bytes):
    """读回容器结构（自证用）。"""
    assert data[:4] == b"icns", "magic 不是 icns"
    total = struct.unpack(">I", data[4:8])[0]
    entries, off = [], 8
    while off < len(data):
        typ = data[off:off + 4].decode("ascii", "replace")
        ln = struct.unpack(">I", data[off + 4:off + 8])[0]
        payload = data[off + 8:off + ln]
        w = h = None
        if payload[:8] == b"\x89PNG\r\n\x1a\n":
            w, h = struct.unpack(">II", payload[16:24])
        entries.append({"type": typ, "len": ln, "png": (w, h),
                        "bytes": len(payload)})
        off += ln
    return total, entries


def main():
    txt = MARK_SVG.read_text(encoding="utf-8")
    d = re.search(r'd="([^"]*)"', txt).group(1)
    sw = float(re.search(r'stroke-width="([\d.]+)"', txt).group(1))
    view = re.search(r'viewBox="([^"]*)"', txt).group(1).split()
    vw, vh = float(view[2]), float(view[3])
    print(f"几何源 = extracted/mark.svg：viewBox {vw:g}×{vh:g}，stroke-width={sw:g}，"
          f"linecap/linejoin=round（{'linecap=\"round\"' in txt and 'linejoin=\"round\"' in txt}）")
    cmds = parse_svg_path(d)
    lines = to_polylines(cmds, steps=64)
    print(f"路径：{len(cmds)} 条命令 → {len(lines)} 条折线（每段曲线 64 点采样）")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    png_by_size, table = {}, []
    for size in SIZES:
        img = render_tile(size, lines, vw, vh, sw)
        p = OUT_DIR / f"utvtu-{size}.png"
        img.save(p)
        png_by_size[size] = p.read_bytes()
        cnt = Counter(map(tuple, np.asarray(img).reshape(-1, 4)))
        top = [(c, round(n / (size * size) * 100, 1)) for c, n in cnt.most_common(3)]
        table.append({"size": size, "wh": img.size, "bytes": p.stat().st_size,
                      "top_colors": [[list(c), pct] for c, pct in top]})
        print(f"  {size:>4}px: {img.size[0]}×{img.size[1]}  {p.stat().st_size:>7} B  "
              f"主色 {[(tuple(int(x) for x in c), pct) for c, pct in top]}")

    icns = build_icns(png_by_size)
    icns_path = HERE / "utvtu.icns"
    icns_path.write_bytes(icns)
    print(f"\n写出 {icns_path}（{len(icns)} 字节）")

    # 自证①：容器读回
    total, entries = read_icns(icns)
    print(f"\n=== 自证① 容器读回：magic=icns 总长={total}（文件 {len(icns)} 字节，"
          f"{'一致 ✓' if total == len(icns) else '不一致 ✗'}）条目 {len(entries)}/{len(ICNS_TYPES)} ===")
    ok = True
    for e, (typ, size) in zip(entries, ICNS_TYPES):
        good = e["type"] == typ and e["png"] == (size, size) and e["len"] == 8 + e["bytes"]
        ok &= good
        print(f"  {e['type']} 期望 {size}×{size} → 实际 {e['png'][0]}×{e['png'][1]} "
              f"条目长 {e['len']} = 8+{e['bytes']} {'✓' if good else '✗'}")
    print(f"  结构自校验：{'全部一致 ✓' if ok and total == len(icns) else '有问题 ✗'}")

    # 自证②：256 档主色（品牌紫 + 白）
    im256 = Image.open(OUT_DIR / "utvtu-256.png").convert("RGBA")
    cnt = Counter(map(tuple, np.asarray(im256).reshape(-1, 4)))
    top = cnt.most_common(3)
    purple_white = all(c[3] == 0 or (abs(c[0] - BG[0]) <= 2 and abs(c[1] - BG[1]) <= 2
                                    and abs(c[2] - BG[2]) <= 2) or c[:3] == FG
                       for c, _ in top)
    print(f"\n=== 自证② 256 档主色：{[(tuple(int(x) for x in c), round(n/256/256*100,1)) for c, n in top]} "
          f"⇒ {'品牌紫 + 白 ✓' if purple_white else '含其它主色 ✗'} ===")

    # 自证③：尺寸阶梯对照图
    pad = 8
    total_w = sum(s for s in SIZES) + pad * (len(SIZES) + 1)
    ladder = Image.new("RGB", (max(total_w // 2 + 40, 600), 600), (255, 255, 255))
    x = pad
    for size in (16, 32, 64, 128, 256):
        im = Image.open(OUT_DIR / f"utvtu-{size}.png").convert("RGBA")
        ladder.paste(im, (x, pad), im)
        x += im.width + pad
    for size in (512, 1024):
        im = Image.open(OUT_DIR / f"utvtu-{size}.png").convert("RGBA").resize(
            (size // 4, size // 4), Image.LANCZOS)
        ladder.paste(im, (x, pad), im)
        x += im.width + pad
    ladder = ladder.crop((0, 0, min(x + pad, ladder.width), 300))
    ladder.save(HERE / "icns-ladder.png")
    print(f"自证③ 尺寸阶梯对照图：icns-ladder.png {ladder.size}（16/32/64/128/256 原尺寸 + 512/1024 缩 1/4）")

    report = {"source": "extracted/mark.svg（逐字几何）", "viewBox": [vw, vh],
              "stroke_units": sw, "bg": list(BG), "fg": list(FG),
              "squircle": SQUIRCLE, "inner": INNER, "supersample": SS,
              "tiles": table, "icns_bytes": len(icns),
              "entries": entries, "structure_ok": bool(ok and total == len(icns))}
    (HERE / "icns-report.json").write_text(json.dumps(report, ensure_ascii=False, indent=1),
                                           encoding="utf-8")
    print("写出 icns-report.json")
    return report


if __name__ == "__main__":
    main()
