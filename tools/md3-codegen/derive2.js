// 反推 v2：跨全部种子取交集消歧；排除 *PaletteKeyColor（诊断用，非 UI 角色）
const fs = require('fs');
const m = require('@material/material-color-utilities');

const schemes = {
    TonalSpot: m.SchemeTonalSpot, Vibrant: m.SchemeVibrant, Expressive: m.SchemeExpressive,
    Content: m.SchemeContent, Fidelity: m.SchemeFidelity, Monochrome: m.SchemeMonochrome,
    Neutral: m.SchemeNeutral, Rainbow: m.SchemeRainbow, FruitSalad: m.SchemeFruitSalad,
};
const seeds = [0xff6750a4, 0xff4285f4, 0xff00695c, 0xffb3261e, 0xff1c1b1f, 0xffffffff, 0xff000000, 0xff7d5260, 0xff6fdbcb];
const SKIP = ['length', 'name', 'prototype', 'highestSurface', 'contentAccentToneDelta'];
const roles = Object.getOwnPropertyNames(m.MaterialDynamicColors)
    .filter(k => !SKIP.includes(k))
    .filter(k => m.MaterialDynamicColors[k] instanceof m.DynamicColor)
    .filter(k => !k.endsWith('PaletteKeyColor'));      // 诊断角色，不进我们的颜色池
const PALETTES = ['neutralPalette', 'neutralVariantPalette', 'primaryPalette', 'secondaryPalette', 'tertiaryPalette', 'errorPalette'];

// ① 色板定义
const paletteDefs = {};
for (const vn of Object.keys(schemes)) {
    paletteDefs[vn] = {};
    for (const dark of [false, true]) {
        const s = new schemes[vn](m.Hct.fromInt(0xff6750a4), dark, 0);
        const def = {};
        for (const p of PALETTES) def[p.replace('Palette', '')] = { hue: +s[p].hue.toFixed(6), chroma: +s[p].chroma.toFixed(6) };
        paletteDefs[vn][dark ? 'dark' : 'light'] = def;
    }
}

// ② 角色 → (palette, tone)：逐种子取「全部命中集合」，再跨种子取交集
const roleTable = {};
const unresolved = [];
for (const role of roles) {
    roleTable[role] = {};
    for (const vn of Object.keys(schemes)) {
        roleTable[role][vn] = {};
        for (const dark of [false, true]) {
            let common = null;
            for (const seed of seeds) {
                const s = new schemes[vn](m.Hct.fromInt(seed), dark, 0);
                const hex = m.hexFromArgb(m.MaterialDynamicColors[role].getArgb(s));
                const hits = new Set();
                for (const p of PALETTES) {
                    const pal = s[p];
                    for (let t = 0; t <= 100; t++) if (m.hexFromArgb(pal.tone(t)) === hex) hits.add(`${p.replace('Palette', '')}:${t}`);
                }
                common = common === null ? hits : new Set([...common].filter(x => hits.has(x)));
            }
            const vals = [...(common || [])];
            if (vals.length === 0) { unresolved.push(`${role}/${vn}/${dark ? 'dark' : 'light'}`); roleTable[role][vn][dark ? 'dark' : 'light'] = 'UNRESOLVED'; }
            else roleTable[role][vn][dark ? 'dark' : 'light'] = vals[0];   // 交集内任一命中在全部种子上行为一致
        }
    }
}

fs.writeFileSync('palette-defs.json', JSON.stringify(paletteDefs, null, 1));
fs.writeFileSync('role-table.json', JSON.stringify(roleTable, null, 1));

console.log('角色数：', roles.length, '；未解析：', unresolved.length);
if (unresolved.length) console.log(unresolved.join('\n'));
console.log('\n=== 色板定义（TonalSpot）===');
console.log('light', JSON.stringify(paletteDefs.TonalSpot.light));
console.log('dark ', JSON.stringify(paletteDefs.TonalSpot.dark));
console.log('\n=== 各方案差异（light / primary.chroma, tertiary.hue 相对源 hue）===');
const srcHue = paletteDefs.TonalSpot.light.primary.hue;
for (const vn of Object.keys(schemes)) {
    const d = paletteDefs[vn].light, dd = paletteDefs[vn].dark;
    console.log(`${vn.padEnd(11)} primary(c=${d.primary.chroma}/${dd.primary.chroma}) secondary(c=${d.secondary.chroma}) tertiary(h+${(d.tertiary.hue - srcHue).toFixed(1)},c=${d.tertiary.chroma}) neutral(c=${d.neutral.chroma}/${dd.neutral.chroma}) nv(c=${d.neutralVariant.chroma}/${dd.neutralVariant.chroma})`);
}
console.log('\n=== 角色表样例（TonalSpot light / dark）===');
for (const r of ['primary', 'onPrimary', 'primaryContainer', 'onPrimaryContainer', 'surface', 'surfaceContainerLowest', 'surfaceContainerLow', 'surfaceContainer', 'surfaceContainerHigh', 'surfaceContainerHighest', 'onSurface', 'onSurfaceVariant', 'outline', 'outlineVariant', 'inverseSurface', 'inverseOnSurface', 'inversePrimary', 'shadow', 'scrim', 'surfaceTint', 'error', 'onError', 'errorContainer', 'onErrorContainer'])
    console.log(`  ${r.padEnd(24)} light=${String(roleTable[r].TonalSpot.light).padEnd(22)} dark=${roleTable[r].TonalSpot.dark}`);
