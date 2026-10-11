// Analytic specialization of AndroidX RoundedPolygon/Cubic for two zero-smoothing shapes.
// No raster image is read. Output uses the source's cubic circular-arc formula.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const k=4/3*(Math.sqrt(2)-1),r=100*.2/Math.SQRT2;
const n=x=>Number(x.toFixed(9)).toString();
function roundedRect(top,bottom){return `M 0 ${n(top)} C 0 ${n(top*(1-k))} ${n(top*(1-k))} 0 ${n(top)} 0 L ${n(100-top)} 0 C ${n(100-top*(1-k))} 0 100 ${n(top*(1-k))} 100 ${n(top)} L 100 ${n(100-bottom)} C 100 ${n(100-bottom*(1-k))} ${n(100-bottom*(1-k))} 100 ${n(100-bottom)} 100 L ${n(bottom)} 100 C ${n(bottom*(1-k))} 100 0 ${n(100-bottom*(1-k))} 0 ${n(100-bottom)} Z`;}
const shapes={Arch:roundedRect(50,r),Square:roundedRect(30,30)};
const sources=[['MaterialShapes.kt','../md3-style-audit-2026-10-11/assets/MaterialShapes.kt.txt','https://cs.android.com/androidx/platform/frameworks/support/+/androidx-main:compose/material3/material3/src/commonMain/kotlin/androidx/compose/material3/MaterialShapes.kt'],['RoundedPolygon.kt','assets/RoundedPolygon.kt.txt','https://raw.githubusercontent.com/androidx/androidx/androidx-main/graphics/graphics-shapes/src/commonMain/kotlin/androidx/graphics/shapes/RoundedPolygon.kt'],['Cubic.kt','assets/Cubic.kt.txt','https://raw.githubusercontent.com/androidx/androidx/androidx-main/graphics/graphics-shapes/src/commonMain/kotlin/androidx/graphics/shapes/Cubic.kt']].map(([name,file,url])=>({name,file,url,sha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,file))).digest('hex')}));
// Arch: regular 4-vertex radius=1 polygon, round=[1,1,.2,.2], rotation=-135 degrees.
// Side length=sqrt(2); the top edge scales the two radius-1 cuts to sqrt(2)/2.
// Minimum of both allowed cuts yields upper radius 50 and lower .2/sqrt(2)*100.
// Normalization uses the exact [0,100]^2 cubic control bounds. No smoothing flanks exist.
const report={viewBox:'0 0 100 100',sourceParameters:{Arch:{vertices:4,radii:[1,1,.2,.2],rotation:-135,smoothing:0,normalizedRadii:[50,r]},Square:{width:1,height:1,radius:.3,smoothing:0,normalizedRadii:[30,30]}},arcControlFactor:k,decimalPlaces:9,paths:shapes,sources};
fs.writeFileSync(path.join(__dirname,'geometry.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(__dirname,'assets/selected-shapes.svg'),`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 220 100"><path d="${shapes.Arch}" fill="#6750A4"/><path d="${shapes.Square}" transform="translate(120 0)" fill="#6750A4"/></svg>\n`);
console.log(JSON.stringify(shapes));