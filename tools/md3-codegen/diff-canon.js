// 逐格对比：参考实现 canonical（oracle-canon.txt） vs C# 侧导出（md3-csharp-canon.txt）
const fs = require('fs');
const path = require('path');

const refPath = path.join(__dirname, 'oracle-canon.txt');
const csPath = process.argv[2] || path.join(__dirname, '..', '..', 'OpenUtau.Test', 'bin', 'Debug', 'net8.0-windows', 'md3-csharp-canon.txt');
const meta = JSON.parse(fs.readFileSync(path.join(__dirname, 'oracle-meta.json'), 'utf8'));
const roles = meta.roleOrder;

const refLines = fs.readFileSync(refPath, 'utf8').split('\n');
const csLines = fs.readFileSync(csPath, 'utf8').split('\n');
if (refLines.length !== csLines.length) console.log(`⚠ 行数不同：ref=${refLines.length} cs=${csLines.length}`);

const mismatches = [];
const roleCount = {};
let exact = 0, total = 0;
for (let i = 0; i < Math.min(refLines.length, csLines.length); i++) {
    const a = refLines[i].split('|'), b = csLines[i].split('|');
    const aVals = a[4].split(','), bVals = b[4].split(',');
    for (let j = 0; j < aVals.length; j++) {
        total++;
        if (aVals[j] === bVals[j]) { exact++; continue; }
        const role = roles[j];
        roleCount[role] = (roleCount[role] || 0) + 1;
        if (mismatches.length < 25) mismatches.push(`${a[0]}|${a[1]}|${a[2] === '1' ? 'dark' : 'light'}|c${a[3]}  ${role}: 谷歌=${aVals[j]} 我们=${bVals[j]}`);
    }
}
console.log(`总格数 ${total}，一致 ${exact}（${(exact / total * 100).toFixed(2)}%），不一致 ${total - exact}`);
console.log('\n按角色统计不一致次数：');
Object.entries(roleCount).sort((x, y) => y[1] - x[1]).forEach(([r, n]) => console.log(`  ${r.padEnd(26)} ${n}`));
console.log('\n前 25 个不一致样例：');
console.log(mismatches.join('\n'));
