import io, os, re
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_frozen.py")
s = io.open(p, encoding="utf-8").read()

# f-string → 普通字符串（注释里有大量 { } 会与 f-string 语法冲突），随后用占位符替换回真值
s = s.replace('    head = f"""<ResourceDictionary', '    head = """<ResourceDictionary', 1)

inject = '''
    head = (head
            .replace("{S.MARK_D}", S.MARK_D)
            .replace("{S.BRACE_D}", S.BRACE_D)
            .replace("{mirror_d(S.BRACE_D)}", mirror_d(S.BRACE_D))
            .replace("{S.V_FILL_D}", S.V_FILL_D)
            .replace("{S.V_STROKE_D}", S.V_STROKE_D)
            .replace("{wm}", wm)
            .replace('{(max(p[0] for l in S.WM for p in l["pts"])):.2f}',
                     "%.2f" % max(p[0] for l in S.WM for p in l["pts"]))
            .replace('{(max(p[0] for l in S.WM for p in l["pts"])) / 100:.4f}',
                     "%.4f" % (max(p[0] for l in S.WM for p in l["pts"]) / 100))
            .replace('{L.wm_path_d(L.MONO and []) if False else ""}', "")
            .replace("{{", "{").replace("}}", "}"))
'''
anchor = '    io.open(os.path.join(OUT, "utvtu-brand-lockup.axaml"), "w", encoding="utf-8").write(head)'
s = s.replace(anchor, inject + anchor, 1)
io.open(p, "w", encoding="utf-8").write(s)
print("patched")
