"""Collect official reference images and reproducible source-only audit counts.
Does not launch, capture, build, test, or modify the product.
"""
from pathlib import Path
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
import hashlib, json, re, urllib.request, html
from PIL import Image

here = Path(__file__).resolve().parent
root = here.parents[2]
assets = here / 'assets'
assets.mkdir(exist_ok=True)

def get(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 UTvTU-design-reference-audit'})
    with urllib.request.urlopen(request, timeout=20) as response:
        data = response.read(12 * 1024 * 1024 + 1)
        if len(data) > 12 * 1024 * 1024: raise ValueError('Reference exceeds size budget')
        return data

references = [
    dict(id='reply', title='Reply · Google官方完整M3实例', page='https://developer.android.com/develop/ui/compose/designsystems/material3', image='https://developer.android.com/static/develop/ui/compose/images/m3-sampleapp.png', file='reply.png'),
    dict(id='dynamic', title='Reply · 默认配色与动态配色对照', page='https://developer.android.com/develop/ui/compose/designsystems/material3#dynamic_color_schemes', image='https://developer.android.com/static/develop/ui/compose/images/m3-dynamic.png', file='reply-dynamic.png'),
]
shape_page = 'https://developer.android.com/reference/kotlin/androidx/compose/material3/MaterialShapes?hl=en'
try:
    source = get(shape_page).decode('utf-8')
    tags = re.findall(r'<img\b[^>]*>', source)
    for tag in tags:
        if re.search(r'alt=["\'][^"\']*Shapes\s+image', tag, re.I):
            src = html.unescape(re.search(r'\bsrc=["\']([^"\']+)', tag).group(1))
            if src.startswith('/'): src = 'https://developer.android.com' + src
            references.append(dict(id='shapes', title='MaterialShapes · Google官方几何图形库', page=shape_page, image=src, file='material-shapes.png'))
            break
    (here/'shape-source-status.json').write_text(json.dumps(dict(page=shape_page, bytes=len(source), imageFound=len(references)>2), indent=2), encoding='utf-8')
except Exception as e:
    (here/'shape-source-status.json').write_text(json.dumps(dict(page=shape_page, error=str(e)), indent=2), encoding='utf-8')

def save(ref):
    try:
        data = get(ref['image'])
        target = assets / ref['file']
        target.write_bytes(data)
        with Image.open(target) as image:
            ref['size'] = list(image.size)
        ref['bytes'] = len(data)
        ref['sha256'] = hashlib.sha256(data).hexdigest()
        ref['attribution'] = 'Google / Android Developers. Original image; see source page for content license (CC BY 4.0 unless otherwise noted).'
    except Exception as e:
        ref['error'] = str(e)
    return ref

with ThreadPoolExecutor(max_workers=2) as executor:
    references = list(executor.map(save, references))

references += [
 dict(id='expressive', title='Google研究 · M3 Expressive界面案例', page='https://design.google/library/expressive-material-design-google-research?pubDate=20250521', image='https://storage.googleapis.com/gd-prod/images/9eb4679b-45ac-4202-9749-deae98c5b904.799a99c1196c2fd4.webp', attribution='Google Design · original image linked from the original article; not a UTvTU design.'),
 dict(id='email-before', title='Google研究 · 发送动作原布局', page='https://design.google/library/expressive-material-design-google-research?pubDate=20250521', image='https://storage.googleapis.com/gd-prod/images/14f564f3-4c8f-4fc1-a111-0e78ee2a6b1a.799a99c1196c2fd4.webp', attribution='Google Design · original image linked from the original article.'),
 dict(id='email-after', title='Google研究 · 发送动作强化布局', page='https://design.google/library/expressive-material-design-google-research?pubDate=20250521', image='https://storage.googleapis.com/gd-prod/images/d651bb02-a503-448d-b70e-968ecfaa266c.799a99c1196c2fd4.webp', attribution='Google Design · original image linked from the original article.'),
]
for reference in references:
    reference['retrievedDate'] = '2026-10-11'
(here/'references.json').write_text(json.dumps(references,ensure_ascii=False,indent=2),encoding='utf-8')

native_url = 'https://raw.githubusercontent.com/androidx/androidx/androidx-main/compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/MaterialShapes.kt'
native = get(native_url)
if b'Copyright' not in native or b'public sealed class MaterialShapes' not in native:
    raise ValueError('Unexpected native geometry source')
(assets/'MaterialShapes.kt.txt').write_bytes(native)
(assets/'MaterialShapes.LICENSE.txt').write_bytes(get('https://www.apache.org/licenses/LICENSE-2.0.txt'))
(here/'shape-native-source.json').write_text(json.dumps(dict(url=native_url, bytes=len(native), sha256=hashlib.sha256(native).hexdigest(), retrievedDate='2026-10-11', license='Apache-2.0; original header retained'),indent=2),encoding='utf-8')

groups = {}
for folder in ['Views','Controls','Styles']:
    roles = Counter(); radii = Counter(); fonts = Counter(); transitions = Counter(); per_file = {}
    files = list((root/'OpenUtau'/folder).rglob('*.axaml'))
    for p in files:
        text = re.sub(r'<!--.*?-->', '', p.read_text(encoding='utf-8-sig'), flags=re.S)
        found = Counter(re.findall(r'\{(?:DynamicResource|StaticResource) md3\.(?:color\.)?([a-z-]+)\}', text))
        roles.update(found)
        radii.update(re.findall(r'(?:Property="CornerRadius" Value|CornerRadius)="([0-9.,]+)"', text))
        fonts.update(re.findall(r'(?:Property="FontSize" Value|FontSize)="([0-9.]+)"', text))
        transitions.update(re.findall(r'Duration="([^"]+)"', text))
        per_file[str(p.relative_to(root)).replace('\\','/')] = dict(roles=dict(found), directRoleReferences=sum(found.values()))
    groups[folder] = dict(files=len(files), roles=dict(roles.most_common()), literalRadii=dict(radii.most_common()), literalFontSizes=dict(fonts.most_common()), literalDurations=dict(transitions.most_common()), perFile=per_file)
metrics = dict(scope='Static XAML source references only, comments removed. Counts are not pixel area, runtime contrast or computed style coverage. C# drawing and legacy aliases are excluded.', groups=groups)
(here/'source-metrics.json').write_text(json.dumps(metrics,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(references=[{k:r.get(k) for k in ['id','file','size','error']} for r in references], groups={g:{k:v[k] for k in ['files','roles','literalRadii','literalDurations']} for g,v in groups.items()}),ensure_ascii=False))
