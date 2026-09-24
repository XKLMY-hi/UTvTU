// 生成 C# 移植用的对照数据（oracle）——直接调用谷歌参考实现 material-color-utilities@0.3.0
// 用法：node gen-oracle.js   （在 .dsh/mcu 下运行）
const fs = require('fs');
const crypto = require('crypto');
const m = require('@material/material-color-utilities');

const SKIP = ['length', 'name', 'prototype', 'highestSurface', 'contentAccentToneDelta'];
const roles = Object.getOwnPropertyNames(m.MaterialDynamicColors)
    .filter(k => !SKIP.includes(k))
    .filter(k => m.MaterialDynamicColors[k] instanceof m.DynamicColor)
    .filter(k => !k.endsWith('PaletteKeyColor'));

const seeds = [0xff6750a4, 0xff4285f4, 0xff00695c, 0xffb3261e, 0xff1c1b1f, 0xffffffff, 0xff000000, 0xff7d5260, 0xff6fdbcb];
const schemes = {
    TonalSpot: m.SchemeTonalSpot, Vibrant: m.SchemeVibrant, Expressive: m.SchemeExpressive,
    Monochrome: m.SchemeMonochrome, Neutral: m.SchemeNeutral, Rainbow: m.SchemeRainbow, FruitSalad: m.SchemeFruitSalad,
};

const cases = [];
for (const seed of seeds)
    for (const name of Object.keys(schemes))
        for (const dark of [false, true])
            cases.push({ seed, scheme: name, dark, contrast: 0 });
// 对比度档位（默认种子 + TonalSpot）
for (const contrast of [-1, -0.5, 0.5, 1]) cases.push({ seed: 0xff6750a4, scheme: 'TonalSpot', dark: false, contrast });
for (const contrast of [-1, 1]) cases.push({ seed: 0xff6750a4, scheme: 'TonalSpot', dark: true, contrast });

const full = [];
const lines = [];
for (const c of cases) {
    const sch = new schemes[c.scheme](m.Hct.fromInt(c.seed), c.dark, c.contrast);
    const vals = {};
    const parts = [];
    for (const r of roles) {
        const hex = m.hexFromArgb(m.MaterialDynamicColors[r].getArgb(sch));
        vals[r] = hex;
        parts.push(hex);
    }
    full.push(Object.assign({}, c, { seedHex: m.hexFromArgb(c.seed), roles: vals }));
    lines.push(`${m.hexFromArgb(c.seed)}|${c.scheme}|${c.dark ? 1 : 0}|${c.contrast}|${parts.join(',')}`);
}

const canon = lines.join('\n');
const sha256 = crypto.createHash('sha256').update(canon, 'utf8').digest('hex');

const pick = (seedHex, scheme, dark, keys) => {
    const c = full.find(x => x.seedHex === seedHex && x.scheme === scheme && x.dark === dark && x.contrast === 0);
    const o = {};
    for (const k of keys) o[k] = c.roles[k];
    return o;
};
const goldens = {
    'TonalSpot-light-6750a4': pick('#6750a4', 'TonalSpot', false,
        ['primary', 'onPrimary', 'primaryContainer', 'onPrimaryContainer', 'surface', 'onSurface', 'surfaceContainerHighest', 'onSurfaceVariant', 'outline', 'outlineVariant', 'inversePrimary', 'error']),
    'TonalSpot-dark-6750a4': pick('#6750a4', 'TonalSpot', true,
        ['primary', 'onPrimary', 'primaryContainer', 'surface', 'onSurface', 'surfaceContainerHighest', 'outline']),
    'Monochrome-light-6750a4': pick('#6750a4', 'Monochrome', false, ['primary', 'surface', 'onSurface']),
};

fs.writeFileSync('oracle.json', JSON.stringify(full));
fs.writeFileSync('oracle-canon.txt', canon);
fs.writeFileSync('oracle-meta.json', JSON.stringify({
    source: '@material/material-color-utilities@0.3.0',
    roleOrder: roles,
    caseCount: cases.length,
    sha256,
    cases: cases.map(c => `${m.hexFromArgb(c.seed)}|${c.scheme}|${c.dark ? 1 : 0}|${c.contrast}`),
    goldens,
}, null, 1));

console.log(`roles=${roles.length} cases=${cases.length} sha256=${sha256}`);
console.log('goldens TonalSpot light #6750a4:', JSON.stringify(goldens['TonalSpot-light-6750a4']));
