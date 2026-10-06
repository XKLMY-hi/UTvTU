# -*- coding: utf-8 -*-
"""W43：从品牌板 PDF 的 **Type3 字体 CharProcs** 提取字标轮廓（非描摹）。

用法：
  python _w43_type3_wordmark.py probe     # 侦察：Type3 字体 / CharProcs 是路径还是位图
  python _w43_type3_wordmark.py find-run  # 在第 1 页内容流里定位字标那段 BT..ET
  python _w43_type3_wordmark.py extract   # 产出 wordmark-outline.svg + 数字 + 自证图
  python _w43_type3_wordmark.py verify    # 校验 SVG 路径与 CharProcs 逐字一致

约定：本脚本**只读** PDF，只写 extracted/ 下的产物。不做描摹、不做重排、不调字距。
"""
import json
import math
import re
import sys
import zlib
from collections import Counter
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

BRAND = Path(r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand")
PDF = BRAND / "utvtu-brand-sheet.pdf"
OUT = BRAND / "extracted"
WORDMARK_BOX = (1351.0, 2332.0, 2397.0, 2619.0)   # Lead 实测的字标定位框（PDF 点，原点左下）

# ───────────────────────── PDF 底层 ─────────────────────────

def load_objects(data: bytes):
    """明文对象表：N 0 obj ... endobj（本 PDF /ObjStm = 0）。"""
    objs = {}
    for m in re.finditer(rb"(?<![0-9])(\d+)\s+0\s+obj\b(.*?)\bendobj", data, re.S):
        objs[int(m.group(1))] = m.group(2)
    return objs


def stream_of(obj_body: bytes) -> bytes:
    """取流对象解压后的内容（/FlateDecode；无 filter 时原样）。"""
    sm = re.search(rb"stream\r?\n(.*?)\r?\nendstream", obj_body, re.S)
    if not sm:
        return b""
    raw = sm.group(1)
    if b"/FlateDecode" in obj_body:
        try:
            return zlib.decompress(raw)
        except zlib.error:
            return raw
    return raw


def inline_dict(obj_body: bytes, key: bytes) -> bytes | None:
    """取 `/key << ... >>`（含嵌套 << >> 的括号配平）。"""
    m = re.search(rb"/" + key + rb"\s*<<", obj_body)
    if not m:
        return None
    i = m.end() - 2
    depth = 0
    while i < len(obj_body):
        if obj_body[i:i + 2] == b"<<":
            depth += 1
            i += 2
        elif obj_body[i:i + 2] == b">>":
            depth -= 1
            i += 2
            if depth == 0:
                return obj_body[m.end():i - 2]
        else:
            i += 1
    return None


def ref_num(obj_body: bytes, key: bytes):
    m = re.search(rb"/" + key + rb"\s+(\d+)\s+0\s+R", obj_body)
    if m:
        return int(m.group(1))
    m = re.search(rb"/" + key + rb"\s+<<", obj_body)
    return None


def numbers(blob: bytes):
    return [float(x) for x in re.findall(rb"[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?\d+)?", blob)]


def parse_type3(objs):
    """所有 Type3 字体字典 → 结构化信息。"""
    fonts = {}
    for n, body in objs.items():
        if b"/Type3" not in body:
            continue
        fm = re.search(rb"/FontMatrix\s*\[([^\]]*)\]", body)
        fbbox = re.search(rb"/FontBBox\s*\[([^\]]*)\]", body)
        widths = re.search(rb"/Widths\s*\[([^\]]*)\]", body, re.S)
        diffs_blob = inline_dict(body, b"Encoding") or b""
        dm = re.search(rb"/Differences\s*\[([^\]]*)\]", diffs_blob, re.S)
        cp_blob = inline_dict(body, b"CharProcs") or b""
        charprocs = {m.group(1).decode("latin-1"): int(m.group(2))
                     for m in re.finditer(rb"/([^\s/<>]+)\s+(\d+)\s+0\s+R", cp_blob)}
        # Differences → {code: glyphname}
        enc = {}
        if dm:
            code = 0
            for tok in dm.group(1).split():
                if tok.startswith(b"/"):
                    enc[code] = tok[1:].decode("latin-1")
                    code += 1
                else:
                    code = int(tok)
        fonts[n] = {
            "obj": n,
            "font_name": (re.search(rb"/FontName\s*/([^\s/<>]+)", body) or [None, None])[1],
            "font_matrix": numbers(fm.group(1)) if fm else [0.001, 0, 0, 0.001, 0, 0],
            "font_bbox": numbers(fbbox.group(1)) if fbbox else None,
            "widths": [int(float(x)) for x in widths.group(1).split()] if widths else [],
            "first_char": int(re.search(rb"/FirstChar\s+(\d+)", body).group(1)),
            "encoding": enc,
            "charprocs": charprocs,
        }
    return fonts


# ───────────────────────── 内容流解析 ─────────────────────────

TOKEN = re.compile(rb"""
      (?P<num>[-+]?[0-9]*\.?[0-9]+)
    | (?P<name>/[^\s/\[\]<>()]+)
    | (?P<str>\((?:[^()\\]|\\.)*\))
    | (?P<hex><[0-9A-Fa-f\s]*>)
    | (?P<arr>[\[\]])
    | (?P<op>[A-Za-z*'"]+)
""", re.X | re.S)


def parse_content(content: bytes):
    """把内容流切成 (operands, op) 序列。"""
    ops = []
    operands = []
    for m in TOKEN.finditer(content):
        kind = m.lastgroup
        tok = m.group()
        if kind == "op":
            ops.append((operands, tok))
            operands = []
        else:
            operands.append((kind, tok))
    return ops


def text_runs(ops, fonts, page_font_map):
    """遍历内容流，返回文本段：{font, tm, glyphs:[(code, advance)], bbox}。"""
    runs = []
    ctm = [1, 0, 0, 1, 0, 0]
    stack = []
    tm = [1, 0, 0, 1, 0, 0]
    tlm = list(tm)
    font = None
    font_size = 0.0
    tc = tw = 0.0
    tz = 1.0
    leading = 0.0
    for operands, op in ops:
        nums = [float(tok) for k, tok in operands if k == "num"]
        if op == b"q":
            stack.append(list(ctm))
        elif op == b"Q":
            if stack:
                ctm = stack.pop()
        elif op == b"cm" and len(nums) == 6:
            ctm = mat_mul(nums, ctm)
        elif op == b"BT":
            tm = [1, 0, 0, 1, 0, 0]
            tlm = list(tm)
        elif op == b"Tf":
            names = [tok for k, tok in operands if k == "name"]
            if names:
                res = names[0][1:].decode("latin-1")
                font = page_font_map.get(res)
                if font is None:  # /F3 也可能是 /F3 12 Tf 里的镜像名
                    font = page_font_map.get(res.lstrip("/"))
                font_size = nums[-1] if nums else 0.0
        elif op == b"Td" and len(nums) == 2:
            tlm = mat_mul([1, 0, 0, 1, nums[0], nums[1]], tlm)
            tm = list(tlm)
        elif op == b"TD" and len(nums) == 2:
            leading = -nums[1]
            tlm = mat_mul([1, 0, 0, 1, nums[0], nums[1]], tlm)
            tm = list(tlm)
        elif op == b"Tm" and len(nums) == 6:
            tm = nums
            tlm = list(tm)
        elif op == b"T*":
            tlm = mat_mul([1, 0, 0, 1, 0, -leading], tlm)
            tm = list(tlm)
        elif op == b"TL" and nums:
            leading = nums[0]
        elif op == b"Tc" and nums:
            tc = nums[0]
        elif op == b"Tw" and nums:
            tw = nums[0]
        elif op == b"Tz" and nums:
            tz = nums[0] / 100.0
        elif op in (b"Tj", b"'", b'"'):
            if op in (b"'", b'"'):
                tlm = mat_mul([1, 0, 0, 1, 0, -leading], tlm)
                tm = list(tlm)
            strs = [tok for k, tok in operands if k == "str"]
            hexs = [tok for k, tok in operands if k == "hex"]
            for s in strs:
                runs.append(make_run(s, tm, ctm, font, font_size, tc, tw, tz, fonts))
            for h in hexs:
                runs.append(make_run(h, tm, ctm, font, font_size, tc, tw, tz, fonts))
        elif op == b"TJ":
            for k, t in operands:
                if k == "str":
                    runs.append(make_run(t, tm, ctm, font, font_size, tc, tw, tz, fonts))
                elif k == "num":
                    # 数字 = 千分之一 em 的间距调整（原样遵循，不额外调字距）
                    tm = mat_mul([1, 0, 0, 1, -float(t) / 1000.0 * font_size * tz, 0], tm)
    return runs


def mat_mul(a, b):
    a0, a1, a2, a3, a4, a5 = a
    b0, b1, b2, b3, b4, b5 = b
    return [a0 * b0 + a1 * b2, a0 * b1 + a1 * b3,
            a2 * b0 + a3 * b2, a2 * b1 + a3 * b3,
            a4 * b0 + a5 * b2 + b4, a4 * b1 + a5 * b3 + b5]


def decode_pdf_string(tok: bytes) -> list[int]:
    """( ... ) 或 < ... > → 字节序列。"""
    if tok.startswith(b"("):
        s = tok[1:-1]
        out = bytearray()
        i = 0
        while i < len(s):
            c = s[i]
            if c == 0x5C and i + 1 < len(s):      # backslash
                nxt = s[i + 1]
                mapping = {0x6E: 10, 0x72: 13, 0x74: 9, 0x62: 8, 0x66: 12}
                if nxt in mapping:
                    out.append(mapping[nxt]); i += 2; continue
                if 0x30 <= nxt <= 0x37:
                    j = i + 1
                    oct_digits = b""
                    while j < len(s) and len(oct_digits) < 3 and 0x30 <= s[j] <= 0x37:
                        oct_digits += bytes([s[j]]); j += 1
                    out.append(int(oct_digits, 8) & 0xFF); i = j; continue
                out.append(nxt); i += 2; continue
            out.append(c); i += 1
        return list(out)
    h = re.sub(rb"\s", b"", tok[1:-1])
    if len(h) % 2:
        h += b"0"
    return [int(h[i:i + 2], 16) for i in range(0, len(h), 2)]


def make_run(tok, tm, ctm, font, font_size, tc, tw, tz, fonts):
    codes = decode_pdf_string(tok)
    return {"tm": list(tm), "ctm": list(ctm), "font": font, "font_size": font_size,
            "codes": codes, "tc": tc, "tw": tw, "tz": tz}


# ───────────────────────── 命令 ─────────────────────────

def cmd_probe(objs, fonts):
    print(f"明文对象 {len(objs)} 个；Type3 字体 {len(fonts)} 个")
    for n, f in sorted(fonts.items()):
        cp = f["charprocs"]
        print(f"\nobj {n}: FontMatrix={f['font_matrix']} FontBBox={f['font_bbox']} "
              f"FirstChar={f['first_char']} Widths={len(f['widths'])} "
              f"Differences={len(f['encoding'])} CharProcs={len(cp)}")
        kinds = Counter()
        for gname, gon in list(cp.items()):
            txt = stream_of(objs.get(gon, b"")).decode("latin-1", "replace")
            for op in re.findall(r"\b(d0|d1|m|l|c|v|y|h|f\*|f|Do|BI|ID|EI|re|W\*?|n|S|B\*?)\b", txt):
                kinds[op] += 1
        print(f"  全部 CharProc 算子合计: {dict(kinds)}")
        is_bitmap = ("BI" in kinds) or ("Do" in kinds and "m" not in kinds)
        print(f"  判定: {'位图（失败分支）' if is_bitmap else '路径轮廓 ✓'}")
        gname, gon = next(iter(cp.items()))
        txt = stream_of(objs.get(gon, b"")).decode("latin-1", "replace")
        print(f"  样例字形 {gname} (obj {gon}) 头 200: {txt[:200]!r}")


def find_page_and_runs(objs, fonts, quiet=False):
    # 第 1 页：找 /Type /Page 且 MediaBox 2528x2955
    page_obj = None
    page_font_map = {}
    for n, body in objs.items():
        if b"/Type /Page" in body and b"/Contents" in body and b"2528" in body:
            page_obj = n
            res = inline_dict(body, b"Resources") or body
            fm = inline_dict(res, b"Font")
            if fm:
                for m in re.finditer(rb"/([^\s/<>]+)\s+(\d+)\s+0\s+R", fm):
                    name, fon = m.group(1).decode("latin-1"), int(m.group(2))
                    if fon in fonts:
                        page_font_map[name] = fonts[fon]
            break
    content_obj = ref_num(objs[page_obj], b"Contents")
    content = stream_of(objs[content_obj])
    if not quiet:
        print(f"第 1 页 = obj {page_obj}，内容流 = obj {content_obj}（{len(content)} 字节解压后）")
        print(f"页面字体资源: {list(page_font_map)}")
    ops = parse_content(content)
    runs = text_runs(ops, fonts, page_font_map)
    if quiet:
        return runs, page_font_map
    print(f"算子总数 {len(ops)}；文本段 {len(runs)} 个")
    hits = [r for r in runs if run_intersects(r, fonts)]
    print(f"落在字标框 {WORDMARK_BOX} 内的文本段: {len(hits)}")
    for r in hits:
        f = r["font"]
        names = [f["encoding"].get(c, f"g?{c}") if f else "?" for c in r["codes"]]
        print(f"  font=obj{f['obj'] if f else None} size={r['font_size']} Tm={r['tm']} "
              f"codes={r['codes']} 字形名={names}")
    return runs, hits, content


def run_intersects(run, fonts):
    box = run_bbox(run)
    if box is None:
        return False
    x0, y0, x1, y1 = box
    bx0, by0, bx1, by1 = WORDMARK_BOX
    return not (x1 < bx0 or x0 > bx1 or y1 < by0 or y0 > by1)


def run_bbox(run):
    f = run["font"]
    if f is None:
        return None
    widths = f["widths"]
    first = f["first_char"]
    size = run["font_size"]
    fx = f["font_matrix"][0] * size          # FontMatrix 已含 1/2048
    total = 0.0
    for c in run["codes"]:
        w = widths[c - first] if 0 <= c - first < len(widths) else 0
        total += (w * fx + run["tc"]) * run["tz"]
    tm = mat_mul(run["tm"], run["ctm"])
    # 文本基线两端点
    p0 = (tm[4], tm[5])
    p1 = (tm[0] * total + tm[4], tm[1] * total + tm[5])
    asc = f["font_bbox"][3] * f["font_matrix"][3] * size if f["font_bbox"] else size
    desc = f["font_bbox"][1] * f["font_matrix"][3] * size if f["font_bbox"] else 0
    ys = [p0[1], p1[1]]
    return (min(p0[0], p1[0]), min(ys) + desc, max(p0[0], p1[0]), max(ys) + asc)


# ───────────────────────── 字形路径 → 精确墨迹框 ─────────────────────────

PATH_TOKEN = re.compile(rb"[-+]?[0-9]*\.?[0-9]+|[A-Za-z*]+")


def glyph_path_ops(objs, obj_num):
    """CharProc 流 → (operands, op) 列表 + d0/d1 里的字形宽。"""
    txt = stream_of(objs.get(obj_num, b"")).decode("latin-1", "replace")
    toks = PATH_TOKEN.findall(txt.encode("latin-1"))
    ops = []
    operands = []
    for t in toks:
        if re.fullmatch(rb"[-+]?[0-9]*\.?[0-9]+", t):
            operands.append(float(t))
        else:
            ops.append((operands, t.decode("latin-1")))
            operands = []
    return ops


def cubic_points(p0, p1, p2, p3, steps=24):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        mt = 1 - t
        x = mt**3 * p0[0] + 3 * mt**2 * t * p1[0] + 3 * mt * t**2 * p2[0] + t**3 * p3[0]
        y = mt**3 * p0[1] + 3 * mt**2 * t * p1[1] + 3 * mt * t**2 * p2[1] + t**3 * p3[1]
        pts.append((x, y))
    return pts


def glyph_points(ops):
    """把路径算子跑一遍，返回 (点列表, 是否含闭合子路径)。"""
    pts = []
    cur = (0.0, 0.0)
    start = (0.0, 0.0)
    width = None
    for operands, op in ops:
        if op in ("d0", "d1"):
            width = operands[0] if operands else None
            if op == "d1" and len(operands) >= 5:
                pts.append((operands[1], operands[2]))
                pts.append((operands[3], operands[4]))
        elif op == "m" and len(operands) >= 2:
            cur = (operands[-2], operands[-1])
            start = cur
            pts.append(cur)
        elif op == "l" and len(operands) >= 2:
            cur = (operands[-2], operands[-1])
            pts.append(cur)
        elif op == "c" and len(operands) >= 6:
            p1 = (operands[-6], operands[-5])
            p2 = (operands[-4], operands[-3])
            p3 = (operands[-2], operands[-1])
            pts.extend(cubic_points(cur, p1, p2, p3))
            cur = p3
        elif op == "v" and len(operands) >= 4:
            p2 = (operands[-4], operands[-3])
            p3 = (operands[-2], operands[-1])
            pts.extend(cubic_points(cur, cur, p2, p3))
            cur = p3
        elif op == "y" and len(operands) >= 4:
            p1 = (operands[-4], operands[-3])
            p3 = (operands[-2], operands[-1])
            pts.extend(cubic_points(cur, p1, p3, p3))
            cur = p3
        elif op == "re" and len(operands) >= 4:
            x, y, w, h = operands[-4:]
            pts.extend([(x, y), (x + w, y), (x + w, y + h), (x, y + h)])
            cur = (x, y)
            start = cur
        elif op == "h":
            cur = start
            pts.append(cur)
    return pts, width


def glyph_ink_box(font, gname, objs):
    if gname not in font["charprocs"]:
        return None
    pts, _ = glyph_points(glyph_path_ops(objs, font["charprocs"][gname]))
    if not pts:
        return None
    return (min(p[0] for p in pts), min(p[1] for p in pts),
            max(p[0] for p in pts), max(p[1] for p in pts))


def run_glyph_layout(run, objs):
    """按 FontMatrix + Tm + Widths 逐步进，给出每个字形的用户空间原点与变换。"""
    f = run["font"]
    fm = f["font_matrix"]
    size = run["font_size"]
    tz = run["tz"]
    m = mat_mul(run["tm"], run["ctm"])            # 文本 → 用户空间
    out = []
    pen = 0.0
    for c in run["codes"]:
        gname = f["encoding"].get(c, f"g{c}")
        w = f["widths"][c - f["first_char"]] if 0 <= c - f["first_char"] < len(f["widths"]) else 0
        # 字形空间 → 文本空间：FontMatrix（含 1/2048 与 y 翻转），再乘字号
        gm = [fm[0] * size, fm[1] * size, fm[2] * size, fm[3] * size,
              fm[4] * size + pen * tz, fm[5] * size]
        g2u = mat_mul(gm, m)
        box = glyph_ink_box(f, gname, objs)
        user_box = None
        if box:
            corners = [(box[0], box[1]), (box[2], box[1]), (box[2], box[3]), (box[0], box[3])]
            tp = [(g2u[0] * x + g2u[2] * y + g2u[4], g2u[1] * x + g2u[3] * y + g2u[5]) for x, y in corners]
            user_box = (min(p[0] for p in tp), min(p[1] for p in tp),
                        max(p[0] for p in tp), max(p[1] for p in tp))
        out.append({"code": c, "glyph": gname, "width_units": w,
                    "advance_text": w * fm[0] * size * tz, "matrix": g2u, "ink_box": user_box,
                    "glyph_box": box})
        pen += w * fm[0] * size + run["tc"]
    return out


def cmd_scan(objs, fonts):
    runs, _ = find_page_and_runs(objs, fonts, quiet=True)
    bx0, by0, bx1, by1 = WORDMARK_BOX
    print(f"\n=== 与字标框重叠的候选段（按覆盖面积排序）===")
    cands = []
    for r in runs:
        if r["font"] is None:
            continue
        layout = run_glyph_layout(r, objs)
        boxes = [g["ink_box"] for g in layout if g["ink_box"]]
        if not boxes:
            continue
        ux0 = min(b[0] for b in boxes); uy0 = min(b[1] for b in boxes)
        ux1 = max(b[2] for b in boxes); uy1 = max(b[3] for b in boxes)
        ox = max(0.0, min(ux1, bx1) - max(ux0, bx0))
        oy = max(0.0, min(uy1, by1) - max(uy0, by0))
        if ox > 0 and oy > 0:
            cands.append((ox * oy, r, layout, (ux0, uy0, ux1, uy1)))
    cands.sort(key=lambda t: -t[0])
    for area, r, layout, ubox in cands[:12]:
        names = " ".join(g["glyph"] for g in layout)
        print(f"\n重叠={area:,.0f}  font=obj{r['font']['obj']} size={r['font_size']} "
              f"字形数={len(layout)} 用户空间墨迹框={tuple(round(v, 2) for v in ubox)}")
        print(f"  字形名: {names}")
        print(f"  Tm={[round(v, 4) for v in r['tm']]}  CTM={[round(v, 4) for v in r['ctm']]}")
        for g in layout:
            ib = g["ink_box"]
            print(f"    {g['glyph']:<5} 宽={g['width_units']:>6} 文本步进={g['advance_text']:>9.3f} "
                  f"墨迹框={tuple(round(v, 1) for v in ib) if ib else None}")
    return cands



def cmd_chevron(objs, fonts):
    """在字标行里找**非文本**的那段几何（小写 v 是切角 chevron，不是字体字形）。"""
    runs, _ = find_page_and_runs(objs, fonts, quiet=True)
    page = None
    for n, body in objs.items():
        if b"/Type /Page" in body and b"/Contents" in body and b"2528" in body:
            page = n
            break
    content = stream_of(objs[ref_num(objs[page], b"Contents")])
    ops = parse_content(content)
    ctm = [1, 0, 0, 1, 0, 0]
    stack = []
    path = []          # 用户空间点
    subpaths = []
    cur = None
    start_ops = None
    win = (1805.0, 2300.0, 1955.0, 2660.0)
    lit = (1.0, 0.0, 0.0, 1.0, 1336.0, 2618.0)   # 字标所在的一半的 CTM
    found = []
    for operands, op in ops:
        nums = [float(tok) for k, tok in operands if k == "num"]
        if op == b"q":
            stack.append(list(ctm))
        elif op == b"Q":
            if stack:
                ctm = stack.pop()
        elif op == b"cm" and len(nums) == 6:
            ctm = mat_mul(nums, ctm)
        elif op == b"m" and len(nums) >= 2:
            if path:
                subpaths.append(path)
            path = []
            cur = (nums[-2], nums[-1])
            path.append(cur)
        elif op == b"l" and len(nums) >= 2:
            cur = (nums[-2], nums[-1])
            path.append(cur)
        elif op == b"c" and len(nums) >= 6:
            p1 = (nums[-6], nums[-5]); p2 = (nums[-4], nums[-3]); p3 = (nums[-2], nums[-1])
            path.extend(cubic_points(cur, p1, p2, p3))
            cur = p3
        elif op == b"h":
            if path:
                path.append(path[0])
        elif op in (b"f", b"f*", b"B", b"B*", b"S", b"s", b"b", b"b*", b"n"):
            if path:
                subpaths.append(path)
            if subpaths:
                allpts = [p for sp in subpaths for p in sp]
                tp = [(ctm[0] * x + ctm[2] * y + ctm[4], ctm[1] * x + ctm[3] * y + ctm[5])
                      for x, y in allpts]
                bx = (min(p[0] for p in tp), min(p[1] for p in tp),
                      max(p[0] for p in tp), max(p[1] for p in tp))
                if bx[0] < win[2] and bx[2] > win[0] and bx[1] < win[3] and bx[3] > win[1] \
                        and op in (b"f", b"f*", b"B", b"B*"):
                    # 只看字标那一半的 CTM（左半/右半各一次）
                    ux = (bx[0] - lit[4], bx[2] - lit[4])
                    if 1805 < ux[0] and ux[1] < 1955:
                        found.append({"op": op.decode(), "bbox": bx, "subpaths": len(subpaths),
                                      "points": len(allpts), "ctm": list(ctm),
                                      "geom": [(round(p[0], 2), round(p[1], 2)) for p in allpts]})
            path = []
            subpaths = []
    print(f"候选 chevron 段 {len(found)} 个：")
    for f in found:
        print(f"  op={f['op']} bbox={tuple(round(v, 2) for v in f['bbox'])} 子路径={f['subpaths']} "
              f"点数={f['points']} CTM={[round(v, 3) for v in f['ctm']]}")
        if f["points"] <= 12:
            print(f"    几何: {f['geom']}")
    return found


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "probe"
    data = PDF.read_bytes()
    objs = load_objects(data)
    fonts = parse_type3(objs)
    if cmd == "probe":
        cmd_probe(objs, fonts)
    elif cmd == "find-run":
        find_page_and_runs(objs, fonts)
    elif cmd == "scan":
        cmd_scan(objs, fonts)
    elif cmd == "chevron":
        cmd_chevron(objs, fonts)
    else:
        print(f"未知命令 {cmd}")


if __name__ == "__main__":
    main()
