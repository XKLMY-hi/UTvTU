# -*- coding: utf-8 -*-
"""W43 侦察：定位 PDF 里的 Type3 字体字典，看 CharProcs 是**路径**还是**位图**。

只读，不写任何文件（除 stdout）。
"""
import re
import sys
import zlib
from pathlib import Path

PDF = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
    r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand\utvtu-brand-sheet.pdf")

data = PDF.read_bytes()
print(f"PDF {PDF.name}: {len(data)} bytes")

# ── 1. 找所有 Type3 字体字典（明文对象：N 0 obj ... endobj）──
obj_re = re.compile(rb"(\d+)\s+0\s+obj\b(.*?)\bendobj", re.S)
objs = {int(m.group(1)): m.group(2) for m in obj_re.finditer(data)}
print(f"明文对象数: {len(objs)}  对象号范围: {min(objs)}..{max(objs)}")

type3 = [(n, body) for n, body in objs.items() if b"/Type3" in body]
print(f"\n=== Type3 字体字典: {len(type3)} 个 ===")
for n, body in type3:
    def one(key, pat=rb"/%s\s*([^\s/\]>]+)"):
        m = re.search(pat % key, body)
        return m.group(1).decode("latin-1") if m else None
    cps = re.search(rb"/CharProcs\s+(\d+)\s+0\s+R", body)
    enc = re.search(rb"/Encoding\s+(\d+)\s+0\s+R", body)
    fm = re.search(rb"/FontMatrix\s*\[([^\]]*)\]", body)
    widths = re.search(rb"/Widths\s*(\[[^\]]*\]|\d+\s+0\s+R)", body, re.S)
    first = re.search(rb"/FirstChar\s+(\d+)", body)
    last = re.search(rb"/LastChar\s+(\d+)", body)
    fbbox = re.search(rb"/FontBBox\s*\[([^\]]*)\]", body)
    res = re.search(rb"/Resources\s*<<(.*?)>>", body, re.S)
    print(f"\nobj {n}:")
    print(f"  FontName   = {one(b'FontName')}")
    print(f"  FontMatrix = {fm.group(1).decode() if fm else None}")
    print(f"  FontBBox   = {fbbox.group(1).decode() if fbbox else None}")
    print(f"  First/Last = {first.group(1).decode() if first else '?'}/"
          f"{last.group(1).decode() if last else '?'}")
    print(f"  Widths     = {(widths.group(1)[:120].decode('latin-1') if widths else None)}")
    print(f"  CharProcs  = obj {cps.group(1).decode() if cps else None}")
    print(f"  Encoding   = obj {enc.group(1).decode() if enc else None}")
    if res:
        print(f"  Resources  = <<{res.group(1).decode('latin-1')[:120]}>>")

# ── 2. 看 CharProcs 里的算子：路径 vs 位图 ──
print("\n=== CharProcs 内容抽样（判定 路径 / 位图）===")
for n, body in type3:
    cps = re.search(rb"/CharProcs\s+(\d+)\s+0\s+R", body)
    if not cps:
        continue
    cp_obj = objs.get(int(cps.group(1)), b"")
    head = cp_obj[:200].decode("latin-1", "replace")
    print(f"\nobj {cps.group(1).decode()} (charprocs dict) 头 200 字符:\n  {head}")
    # 取第一个流对象，解压看算子
    for m in list(re.finditer(rb"/(\w+)\s+(\d+)\s+0\s+R", cp_obj))[:3]:
        gname, gon = m.group(1).decode(), int(m.group(2))
        gbody = objs.get(gon, b"")
        sm = re.search(rb"stream\r?\n(.*?)\r?\nendstream", gbody, re.S)
        raw = sm.group(1) if sm else b""
        try:
            txt = zlib.decompress(raw).decode("latin-1")
        except Exception:
            txt = raw.decode("latin-1", "replace")
        ops = re.findall(r"\b(d0|d1|m|l|c|v|y|h|f|f\*|B|Do|BI|re|S)\b", txt)
        from collections import Counter
        print(f"  glyph {gname:<12} obj {gon:<5} stream={len(raw):>6}B 解压={len(txt):>6}B "
              f"算子计数={dict(Counter(ops))}")
        print(f"    头 160: {txt[:160]!r}")
