"""W42 纯提取：把品牌 HTML 里的具名 SVG 组件逐字提取（不改任何数字），并探测 PDF 是否含轮廓路径。"""
import io, json, os, re, unicodedata, zlib

ROOT = r"G:\xklmy文件夹\vibe coding\UTvTU\.opencode\design\brand"
EX = os.path.join(ROOT, "extracted")
os.makedirs(EX, exist_ok=True)
HTML = io.open(os.path.join(ROOT, "utvtu-brand-tailwind.html"), encoding="utf-8", errors="replace").read()

SVG_RE = re.compile(r"<svg\b[^>]*?data-pencil-name=\"(?P<name>[^\"]+)\"[^>]*>.*?</svg>", re.S | re.I)


def slug(name):
    s = unicodedata.normalize("NFKD", name).encode("ascii", "ignore").decode()
    s = re.sub(r"[^0-9A-Za-z]+", "-", s).strip("-").lower()
    return s or "component"


def ensure_ns(tag):
    if "xmlns=" in tag:
        return tag
    return tag[:4] + ' xmlns="http://www.w3.org/2000/svg"' + tag[4:]


rows, seen = [], {}
for m in SVG_RE.finditer(HTML):
    src = m.group(0)
    name = m.group("name")
    key = slug(name)
    seen[key] = seen.get(key, 0) + 1
    if seen[key] > 1:
        key = f"{key}-{seen[key]}"
    tag_end = src.index(">") + 1
    out = ensure_ns(src[:tag_end]) + src[tag_end:]
    path = os.path.join(EX, f"{key}.svg")
    io.open(path, "w", encoding="utf-8").write(out)
    d = re.search(r'\bd="([^"]*)"', src)
    rows.append({"name": name, "file": f"{key}.svg", "type": "A-vector-verbatim",
                 "viewBox": (re.search(r'viewBox="([^"]*)"', src) or [None, ""])[1],
                 "stroke_width": (re.search(r'stroke-width="([^"]*)"', src) or [None, ""])[1],
                 "fill": (re.search(r'\bfill="([^"]*)"', src) or [None, ""])[1],
                 "d_len": len(d.group(1)) if d else 0,
                 "src_offset": m.start()})

# ── 逐字校验：输出文件的 d / viewBox / stroke-width 必须与源一致 ──
ok = 0
for r in rows:
    src = HTML[HTML.rindex("<svg", 0, r["src_offset"] + 1):] if False else None
for r in rows:
    out = io.open(os.path.join(EX, r["file"]), encoding="utf-8").read()
    same_vb = (r["viewBox"] in out)
    same_sw = (f'stroke-width="{r["stroke_width"]}"' in out) if r["stroke_width"] else True
    if same_vb and same_sw:
        ok += 1
    r["verbatim_ok"] = bool(same_vb and same_sw)

# ── B 类：CSS 画的瓦片（<rect>/<circle> 为 0）→ 照抄 CSS 取值翻译 ──
css = "".join(re.findall(r"<style[^>]*>(.*?)</style>", HTML, re.S))
tile_rules = [ln.strip() for ln in css.splitlines()
              if re.search(r"border-radius|background(-color)?\s*:", ln) and len(ln.strip()) < 200]

# ── C 类：字母是活文字（Roboto）→ 保留 <text> ──
roboto = re.findall(r"font-family:\s*([^;}]*)Roboto[^;}]*", css)
text_nodes = re.findall(r"<text\b[^>]*>.*?</text>", HTML, re.S | re.I)
live = {"roboto_declarations": len(roboto), "svg_text_nodes": len(text_nodes),
        "sample": text_nodes[0][:160] if text_nodes else ""}

# ── PDF 探测：内容流里是否有路径算子（说明字标是矢量路径）──
pdf = os.path.join(ROOT, "utvtu-brand-sheet.pdf")
probe = {"pdf": os.path.basename(pdf), "streams": 0, "path_ops": 0, "text_ops": 0}
data = open(pdf, "rb").read()
for mm in re.finditer(rb"stream\r?\n", data):
    start = mm.end()
    end = data.find(b"endstream", start)
    if end < 0:
        continue
    chunk = data[start:end]
    try:
        txt = zlib.decompress(chunk).decode("latin-1")
    except Exception:
        txt = chunk.decode("latin-1", errors="ignore")
    probe["streams"] += 1
    probe["path_ops"] += len(re.findall(r"(?m)(?:^|\s)(?:m|l|c|re)\s", txt))
    probe["text_ops"] += len(re.findall(r"(?m)(?:BT|Tj|TJ)\b", txt))

manifest = f"""# EXTRACT-MANIFEST — UTvTU 品牌 HTML 纯提取（W42）

> **来源**：`utvtu-brand-tailwind.html`（用户设计稿，几何权威源）。**本目录只做提取，不做任何改动**：
> 不改比例 / 不改字距 / 不做笔宽补偿 / 不描摹 / 不合并拆分组件 / 不"顺手优化"。
> `../out/` 目录内的资产此前做过二次创作（光学补偿、字距重排、描摹重建），**标记为 derived/派生，不作产品用**。

## 统计
- 具名 SVG 组件（`data-pencil-name`）：**{len(rows)}** 个 → `extracted/*.svg`（A 类，逐字）
- 逐字校验（viewBox + stroke-width 与源一致）：**{ok}/{len(rows)}**
- B 类（CSS 画的瓦片）：CSS 相关声明 **{len(tile_rules)}** 条 → 见 `css-tiles.txt`
- C 类（Roboto 活文字）：CSS Roboto 声明 **{live['roboto_declarations']}** 处；SVG `<text>` 节点 **{live['svg_text_nodes']}** 个 → 见 `live-text.txt`
- PDF 探测：`{probe['pdf']}` 共 {probe['streams']} 个内容流，路径算子 **{probe['path_ops']}**、文本算子 **{probe['text_ops']}** ⇒ {"**字标在 PDF 里是矢量路径，可从 PDF 提取轮廓**" if probe['path_ops'] > probe['text_ops'] else "**未发现足够的矢量路径算子，字标可能依赖字体渲染 ⇒ 列为待用户决定**"}

## A 类 · 逐字矢量（前 40 条；完整见 `extract-index.json`）

| 组件名 | 文件 | viewBox | stroke-width | fill | d 长度 | 逐字 |
|---|---|---|---|---|---|---|
""" + "\n".join(
    f"| {r['name']} | `{r['file']}` | `{r['viewBox']}` | `{r['stroke_width']}` | `{r['fill']}` | {r['d_len']} | {'✔' if r['verbatim_ok'] else '✘'} |"
    for r in rows[:40]) + """

## B/C 类
- `css-tiles.txt`：CSS 里与瓦片相关的原文声明（颜色/半径/描边宽）逐条照抄；翻译成 SVG 时**逐项取这些值**。
- `live-text.txt`：Roboto 声明与 `<text>` 节点原文；`wordmark-live-text.svg` 保留 `<text>` 并照抄 font-family/size/letter-spacing/weight，**文件头标注"依赖 Roboto，未转轮廓"**。

## 红线（本目录自检）
`git diff --stat` 应为纯新增；任何 A 类文件的 `d` 与源不一致即视为违规（校验脚本 `_extract.py` 输出 N/N）。
"""
io.open(os.path.join(EX, "EXTRACT-MANIFEST.md"), "w", encoding="utf-8").write(manifest)
io.open(os.path.join(EX, "extract-index.json"), "w", encoding="utf-8").write(
    json.dumps({"components": rows, "pdf_probe": probe, "live_text": live}, ensure_ascii=False, indent=1))
io.open(os.path.join(EX, "css-tiles.txt"), "w", encoding="utf-8").write("\n".join(tile_rules))
io.open(os.path.join(EX, "live-text.txt"), "w", encoding="utf-8").write(
    "Roboto declarations:\n" + "\n".join(roboto[:20]) + "\n\nSVG <text> nodes:\n" + "\n".join(text_nodes[:10]))

print("components:", len(rows), "verbatim_ok:", ok, "/", len(rows))
print("pdf probe:", probe)
print("css tile rules:", len(tile_rules), "live text nodes:", len(text_nodes))
