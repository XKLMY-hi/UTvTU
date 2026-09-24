// 内省参考实现，生成机器可读的角色规格表（palette/tone 规则/背景/对比曲线/toneDeltaPair）
const fs = require('fs');
const m = require('@material/material-color-utilities');

const schemes = {
    TonalSpot: m.SchemeTonalSpot, Vibrant: m.SchemeVibrant, Expressive: m.SchemeExpressive,
    Content: m.SchemeContent, Fidelity: m.SchemeFidelity, Monochrome: m.SchemeMonochrome,
    Neutral: m.SchemeNeutral, Rainbow: m.SchemeRainbow, FruitSalad: m.SchemeFruitSalad,
};
const PAL_KEYS = ['primaryPalette', 'secondaryPalette', 'tertiaryPalette', 'neutralPalette', 'neutralVariantPalette', 'errorPalette'];
const SKIP = ['length', 'name', 'prototype', 'highestSurface', 'contentAccentToneDelta'];
const roles = Object.getOwnPropertyNames(m.MaterialDynamicColors)
    .filter(k => !SKIP.includes(k))
    .filter(k => m.MaterialDynamicColors[k] instanceof m.DynamicColor);

// 用不同源色探测 tone 规则（源 tone 分别为 ~12 / ~40 / ~72）
const probeSeeds = [0xff1c1b1f, 0xff6750a4, 0xfff0e0b0];
const probeSeedsHex = probeSeeds.map(s => m.hexFromArgb(s));

const spec = {};
for (const r of roles) {
    const dc = m.MaterialDynamicColors[r];
    const s0 = new schemes.TonalSpot(m.Hct.fromInt(0xff6750a4), false, 0);
    // 色板键：用对象同一性判定
    const pal = PAL_KEYS.find(k => s0[k] === dc.palette(s0)) || 'UNKNOWN';
    const tones = {};   // variant|dark -> 各探针源色的 tone
    for (const vn of Object.keys(schemes)) {
        for (const dark of [false, true]) {
            const key = `${vn}|${dark ? 'dark' : 'light'}`;
            tones[key] = probeSeeds.map(seed => {
                const s = new schemes[vn](m.Hct.fromInt(seed), dark, 0);
                const t = dc.tone(s);
                return Number.isInteger(t) ? t : +t.toFixed(4);
            });
        }
    }
    const cc = dc.contrastCurve;
    const tdp = dc.toneDeltaPair ? dc.toneDeltaPair(s0) : null;
    const bg = dc.background ? dc.background(s0) : null;
    spec[r] = {
        palette: pal.replace('Palette', ''),
        isBackground: !!dc.isBackground,
        background: bg ? bg.name : null,
        contrastCurve: cc ? { low: cc.low, normal: cc.normal, medium: cc.medium, high: cc.high } : null,
        toneDeltaPair: tdp ? {
            roleA: tdp.roleA.name, roleB: tdp.roleB.name, delta: tdp.delta,
            polarity: tdp.polarity, stayTogether: tdp.stayTogether,
        } : null,
        tones,
    };
}

fs.writeFileSync('role-spec.json', JSON.stringify(spec, null, 1));

// 打印 tone 规则摘要（TonalSpot / Monochrome / Content 三档 × light/dark × 三个源色）
console.log('角色数：', roles.length);
console.log('探针源色 tone：', probeSeeds.map(s => m.Hct.fromInt(s).tone.toFixed(2)).join(' / '));
console.log('\n角色'.padEnd(26) + 'palette'.padEnd(16) + 'bg?'.padEnd(5) + 'tone[TonalSpot L/D]'.padEnd(22) + 'tone[Monochrome L/D]'.padEnd(24) + 'tone[Content L/D]');
for (const r of roles) {
    const s = spec[r];
    const f = k => s.tones[k].join('/');
    console.log(r.padEnd(26) + s.palette.padEnd(16) + (s.isBackground ? 'Y' : '').padEnd(5)
        + f('TonalSpot|light').padEnd(22) + f('Monochrome|light').padEnd(24) + f('Content|light'));
}
