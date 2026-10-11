#!/usr/bin/env python3
"""Source-derived zero-smoothing Material shapes; no raster tracing.

Specializes AndroidX RoundedPolygon's corner cuts and Cubic.circularArc.
Angular profiles are an Avalonia morph adapter, not AndroidX's feature matcher.
"""
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.opencode/design/md3-expressive-sample'
OUT.mkdir(parents=True, exist_ok=True)
COUNT = 192


def add(a, b): return (a[0] + b[0], a[1] + b[1])
def sub(a, b): return (a[0] - b[0], a[1] - b[1])
def mul(a, s): return (a[0] * s, a[1] * s)
def length(a): return math.hypot(*a)
def unit(a): return mul(a, 1 / length(a))
def dot(a, b): return a[0] * b[0] + a[1] * b[1]
def cross(a, b): return a[0] * b[1] - a[1] * b[0]
def rot90(a): return (-a[1], a[0])


def polygon(vertices, radii):
    n = len(vertices)
    corners = []
    for i, p in enumerate(vertices):
        d1 = unit(sub(vertices[(i - 1) % n], p))
        d2 = unit(sub(vertices[(i + 1) % n], p))
        cosine = dot(d1, d2)
        cut = radii[i] * (1 + cosine) / math.sqrt(max(1e-15, 1 - cosine * cosine))
        corners.append((d1, d2, cut))
    ratios = [min(1, length(sub(vertices[(i + 1) % n], vertices[i])) /
                  (corners[i][2] + corners[(i + 1) % n][2])) for i in range(n)]
    arcs = []
    for i, p in enumerate(vertices):
        d1, d2, expected = corners[i]
        cut = expected * min(ratios[(i - 1) % n], ratios[i])
        radius = radii[i] * cut / expected
        center = add(p, mul(unit(add(d1, d2)), math.hypot(radius, cut)))
        a, b = add(p, mul(d1, cut)), add(p, mul(d2, cut))
        u, v = unit(sub(a, center)), unit(sub(b, center))
        cosine = min(1, max(-1, dot(u, v)))
        k = radius * 4 / 3 * (math.sqrt(2 * (1 - cosine)) - math.sqrt(1 - cosine * cosine)) / (1 - cosine)
        if dot(rot90(u), sub(b, center)) < 0: k = -k
        arcs.append((a, add(a, mul(rot90(u), k)), sub(b, mul(rot90(v), k)), b))
    curves = []
    for i, arc in enumerate(arcs):
        curves.append(arc)
        a, b = arc[3], arcs[(i + 1) % n][0]
        curves.append((a, add(a, mul(sub(b, a), 1 / 3)), add(a, mul(sub(b, a), 2 / 3)), b))
    # AndroidX normalized() defaults to control-point bounds.
    xs = [p[0] for c in curves for p in c]; ys = [p[1] for c in curves for p in c]
    side = max(max(xs) - min(xs), max(ys) - min(ys))
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    return [tuple((50 + (x - cx) * 100 / side, 50 + (y - cy) * 100 / side) for x, y in c) for c in curves]


def regular(n, inner, radius, rotation):
    return polygon([(math.cos(rotation + i * math.pi / n) * (1 if i % 2 == 0 else inner),
                     math.sin(rotation + i * math.pi / n) * (1 if i % 2 == 0 else inner))
                    for i in range(n * 2)], [radius] * (n * 2))


def parse(path):
    import re
    tokens = re.findall(r'[MLCZ]|-?\d+(?:\.\d+)?', path)
    i = 0; curves = []; p = start = (0, 0)
    while i < len(tokens):
        command = tokens[i]; i += 1
        if command == 'Z': q = start
        elif command in ('M', 'L'):
            q = (float(tokens[i]), float(tokens[i + 1])); i += 2
            if command == 'M': p = start = q; continue
        else:
            coords = [float(v) for v in tokens[i:i + 6]]; i += 6
            q = tuple(coords[4:6]); curves.append((p, tuple(coords[:2]), tuple(coords[2:4]), q)); p = q; continue
        curves.append((p, add(p, mul(sub(q, p), 1 / 3)), add(p, mul(sub(q, p), 2 / 3)), q)); p = q
    return curves


def point(c, t):
    return tuple((1 - t) ** 3 * c[0][j] + 3 * t * (1 - t) ** 2 * c[1][j] +
                 3 * t * t * (1 - t) * c[2][j] + t ** 3 * c[3][j] for j in (0, 1))


def profile(curves):
    points = [sub(point(c, i / 64), (50, 50)) for c in curves for i in range(64)]
    rays = []
    for i in range(COUNT):
        angle = -math.pi / 2 + i * 2 * math.pi / COUNT
        ray = (math.cos(angle), math.sin(angle)); hits = []
        for j, a in enumerate(points):
            edge = sub(points[(j + 1) % len(points)], a); denominator = cross(ray, edge)
            if abs(denominator) < 1e-12: continue
            distance = cross(a, edge) / denominator; fraction = cross(a, ray) / denominator
            if distance > 0 and -1e-9 <= fraction <= 1 + 1e-9: hits.append(distance)
        assert hits, (i, curves)
        rays.append(min(hits))
    return rays


def fmt(v): return f'{v:.9f}'.rstrip('0').rstrip('.')
def svg_path(curves):
    return 'M ' + ' '.join(fmt(v) for v in curves[0][0]) + ' ' + ' '.join(
        'C ' + ' '.join(fmt(v) for p in c[1:] for v in p) for c in curves) + ' Z'


previous = json.loads((ROOT / '.opencode/design/md3-rebuild-2026-10-11/geometry.json').read_text())
curves = {name: parse(previous['paths'][name]) for name in ['Arch', 'Square']}
k = 4 / 3 * (math.sqrt(2) - 1)
curves['Circle'] = [((50, 0), (50 + 50*k, 0), (100, 50 - 50*k), (100, 50)),
                    ((100, 50), (100, 50 + 50*k), (50 + 50*k, 100), (50, 100)),
                    ((50, 100), (50 - 50*k, 100), (0, 50 + 50*k), (0, 50)),
                    ((0, 50), (0, 50 - 50*k), (50 - 50*k, 0), (50, 0))]
curves['Sunny'] = regular(8, .8, .15, 0)
curves['Cookie7'] = regular(7, .75, .5, -math.pi / 2)
paths = {name: previous['paths'].get(name, svg_path(c)) for name, c in curves.items()}
profiles = {name: profile(c) for name, c in curves.items()}
sources = []
base = ROOT / '.opencode/design/md3-rebuild-2026-10-11'
for item in previous['sources']:
    item = dict(item)
    source = (base / item['file']).resolve()
    item['file'] = str(source.relative_to(ROOT))
    assert hashlib.sha256(source.read_bytes()).hexdigest() == item['sha256']
    sources.append(item)
report = dict(viewBox='0 0 100 100', paths=paths, angularSampleCount=COUNT,
              sourceParameters=dict(Sunny=dict(points=8, innerRadius=.8, rounding=.15, smoothing=0, rotation=0),
                                    Cookie7=dict(points=7, innerRadius=.75, rounding=.5, smoothing=0, rotation=-90)),
              morph='Positive radii at matching polar angles; source path at settled endpoints. Not AndroidX feature matching.',
              sources=sources)
(OUT / 'geometry.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
code = ['// Generated by scripts/generate-expressive-shapes.py; do not edit by hand.',
        '// Derived from AndroidX MaterialShapes/RoundedPolygon/Cubic (Apache-2.0).',
        '// Copyright 2024 The Android Open Source Project.',
        '// Source parameters, URLs and SHA-256: .opencode/design/md3-expressive-sample/geometry.json.',
        'namespace OpenUtau.App.Controls;', 'internal static class MaterialShapeProfiles {',
        f'    internal const int Count = {COUNT};', '    internal static readonly string[] Paths = {']
code += ['        "' + p + '",' for p in paths.values()]
code += ['    };', '    internal static readonly double[][] Radii = {']
for name, values in profiles.items():
    code += [f'        // {name}', '        new double[] {']
    code += ['            ' + ', '.join(fmt(v) for v in values[i:i + 12]) + ',' for i in range(0, COUNT, 12)]
    code += ['        },']
code += ['    };', '}']
(ROOT / 'OpenUtau/Controls/MaterialShapeProfiles.g.cs').write_text('\n'.join(code) + '\n')
(OUT / 'shapes.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 620 120">' + ''.join(
    f'<path transform="translate({i * 124 + 12},10)" d="{p}" fill="#6750A4"/>' for i, p in enumerate(paths.values())) + '</svg>')
print('Generated', len(paths), 'source-derived paths and morph profiles.')
