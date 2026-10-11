// Isolated headless design rendering only. Does not launch or capture the Avalonia product.
const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url'),crypto=require('node:crypto'),{execFileSync}=require('node:child_process');
const runtime=path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const {chromium}=require(require.resolve('playwright',{paths:[runtime]}));
(async()=>{
 const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--disable-extensions']});
 try{
 const page=await browser.newPage({viewport:{width:1440,height:900}}),errors=[],captures=[];page.on('pageerror',e=>errors.push(String(e)));
 const base=pathToFileURL(path.join(__dirname,'index.html')).href,go=async query=>{await page.goto(base+'?'+query);await page.evaluate(()=>document.fonts.ready);check(await page.evaluate(()=>[...document.images].every(img=>img.complete&&img.naturalWidth>0)),'Broken image resource');};
 const capture=async name=>{await page.screenshot({path:path.join(__dirname,name+'.png')});captures.push(name+'.png');};
 const check=(condition,message)=>{if(!condition)throw Error(message);};
 const palettes=JSON.parse(fs.readFileSync(path.join(__dirname,'palettes.json'),'utf8')),geometry=JSON.parse(fs.readFileSync(path.join(__dirname,'geometry.json'),'utf8'));
 check(palettes.length===6&&palettes.every(p=>Object.keys(p.roles).length===49),'Missing true scheme roles');
 const computed=[];
 for(const [name,seed] of [['purple','#6750A4'],['green','#006C4C'],['amber','#8F4E00']])for(const dark of [false,true]){
  await go('seed='+encodeURIComponent(seed)+'&theme='+(dark?'dark':'light'));await capture('welcome-'+name+'-'+(dark?'dark':'light')+'-1440');
  const actual=await page.evaluate(()=>({primary:getComputedStyle(document.documentElement).getPropertyValue('--primary').trim(),surface:getComputedStyle(document.documentElement).getPropertyValue('--surface').trim()}));const palette=palettes.find(p=>p.seed===seed&&p.dark===dark);check(actual.primary===palette.roles.primary&&actual.surface===palette.roles.surface,'Rendered scheme role drift');computed.push({seed,dark,...actual});
 }
 await page.locator('#search').fill('不存在');const searchEmpty=await page.locator('#recent-empty').isVisible()&&(await page.locator('#empty-title').innerText()).includes('匹配');check(searchEmpty,'No-match state missing');
 await page.locator('#search').fill('');await page.locator('#empty-toggle').click();check((await page.locator('#empty-title').innerText()).includes('还没有'),'Empty/no-match distinction missing');await capture('welcome-empty-amber-dark-1440');
 for(const next of ['new','open','templates','recent']){await page.locator('[data-page='+next+']').click();check(await page.locator('#page-'+next).isVisible(),'Page missing '+next);}
 await page.setViewportSize({width:1440,height:1320});await go('scene=gallery');await capture('three-seed-gallery-1440');
 await page.setViewportSize({width:800,height:650});await go('theme=dark');await capture('welcome-purple-dark-800');const narrow=await page.evaluate(()=>({viewport:innerWidth,documentWidth:document.documentElement.scrollWidth,heroWidth:document.querySelector('.hero').getBoundingClientRect().width,contentWidth:document.querySelector('.content').getBoundingClientRect().width,scrollable:document.querySelector('.page-content').scrollHeight>document.querySelector('.page-content').clientHeight}));check(narrow.documentWidth===800,'800 width overflow');
 await page.setViewportSize({width:640,height:650});await go('theme=light');const compact=await page.evaluate(()=>({viewport:innerWidth,documentWidth:document.documentElement.scrollWidth,heroWidth:document.querySelector('.hero').getBoundingClientRect().width}));check(compact.documentWidth===640,'640 width overflow');
 const source=fs.readFileSync(path.join(__dirname,'../../../OpenUtau/Assets/Icons.axaml'),'utf8');const natives=new Map([...source.matchAll(/<StreamGeometry x:Key="([^"]+)">([^<]+)<\/StreamGeometry>/g)].map(m=>[m[1],m[2]]));const symbols=await page.evaluate(()=>[...document.querySelectorAll('#native-icons symbol')].map(el=>({id:el.id,viewBox:el.getAttribute('viewBox'),d:el.querySelector('path').getAttribute('d')})));check(symbols.every(s=>s.viewBox==='0 0 256 256'&&natives.get(s.id)===s.d),'Native icon path mismatch');
 const material=fs.readFileSync(path.join(__dirname,'../../../OpenUtau/Controls/MaterialShape.cs'),'utf8');check(Object.entries(geometry.paths).every(([name,d])=>material.includes('const string '+name+'Path = "'+d+'"')),'Product shape path mismatch');
 const old=execFileSync('git',['show','196922bd4886e1940075db1f60c4923b48c7c51d:OpenUtau/Views/WelcomeView.axaml'],{cwd:path.join(__dirname,'../../..'),encoding:'utf8'}),current=fs.readFileSync(path.join(__dirname,'../../../OpenUtau/Views/WelcomeView.axaml'),'utf8');
 const contracts=xml=>{xml=xml.replace(/\r\n/g,"\n");return ({names:[...xml.matchAll(/x:Name="([^"]+)"/g)].map(m=>m[1]).sort(),events:[...xml.matchAll(/Click="([^"]+)"/g)].map(m=>m[1]).sort(),bindings:[...xml.matchAll(/\{Binding [^}]+\}/g)].map(m=>m[0]).filter(m=>!m.includes('$parent')).sort(),brand:xml.slice(xml.indexOf('<Canvas Width="118.43"'),xml.indexOf('</Canvas>')+9)});};
 const before=contracts(old),after=contracts(current);check(JSON.stringify(before)===JSON.stringify(after),'Welcome production behavior/brand contract changed');
 const report={renderedAt:new Date().toISOString(),browser:await browser.version(),captures,computed,schemeCount:6,rolesPerScheme:49,searchEmpty,narrow,compact,nativeIconsVerified:symbols.length,shapeProductPathsIdentical:true,welcomeBaseline:'196922bd4886e1940075db1f60c4923b48c7c51d',welcomeNamesRetained:after.names.length,welcomeClickBindingsRetained:after.events.length,brandSourceUnchanged:true,pageErrors:errors};
 fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify(report,null,2)+'\n');check(errors.length===0,errors.join('\n'));console.log(JSON.stringify(report,null,2));
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});