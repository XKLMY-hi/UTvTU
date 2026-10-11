// Headless rendering of this new design only; never attaches to the product.
const fs = require('node:fs');
const path = require('node:path');
const {pathToFileURL} = require('node:url');
const packageRoot = path.join(process.env.USERPROFILE, '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const {chromium} = require(require.resolve('playwright', {paths:[packageRoot]}));
(async()=>{
 const browser=await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--disable-extensions']});
 const page=await browser.newPage({viewport:{width:1440,height:900}});
 const errors=[];page.on('pageerror',error=>errors.push(String(error)));
 const base=pathToFileURL(path.join(__dirname,'index.html')).href;
 const captures=[];
 for(const view of ['render','resources','context','overlays']){
  await page.setViewportSize({width:1440,height:view==='render'?1080:900});
  await page.goto(base+'?view='+view);await page.evaluate(()=>document.fonts.ready);
  const filename=view+'-dark-1440.png';await page.screenshot({path:path.join(__dirname,filename)});captures.push(filename);
  if(view==='context'){
   for(const tool of ['expressions','lyrics','track']){
    await page.locator('[data-tool="'+tool+'"]').click();
    const toolFile='context-'+tool+'-dark-1440.png';await page.screenshot({path:path.join(__dirname,toolFile)});captures.push(toolFile);
   }
  }
 }
 await page.setViewportSize({width:1440,height:1080});await page.goto(base+'?view=render&theme=light');await page.evaluate(()=>document.fonts.ready);
 await page.screenshot({path:path.join(__dirname,'render-light-1440.png')});captures.push('render-light-1440.png');
 await page.setViewportSize({width:800,height:650});await page.goto(base+'?view=render');await page.evaluate(()=>document.fonts.ready);
 await page.screenshot({path:path.join(__dirname,'render-dark-800.png')});captures.push('render-dark-800.png');
 const narrow=await page.evaluate(()=>({documentWidth:document.documentElement.scrollWidth,viewportWidth:innerWidth,bodyScrollable:document.querySelector('#view-render .scroll').scrollHeight>document.querySelector('#view-render .scroll').clientHeight,footerBottom:document.querySelector('.job-bar').getBoundingClientRect().bottom,statusTop:document.querySelector('.status').getBoundingClientRect().top}));
 await page.locator('#method').selectOption({label:'实时兼容'});
 const summaryMode=await page.locator('#method-summary').innerText();
 await page.getByRole('button',{name:'自定义',exact:true}).click();
 const customRangeVisible=await page.locator('#custom-range').isVisible();
 await page.locator('#start').click();const inputLocked=await page.locator('#method').isDisabled();
 await page.locator('#cancel').click();const inputRestored=await page.locator('#method').isEnabled();
 await page.setViewportSize({width:1440,height:900});await page.goto(base+'?view=resources');
 await page.locator('#resource-search').fill('弦月');
 const matchingRows=await page.locator('#singer-list .resource-row:visible').count();
 await page.locator('#resource-search').fill('不存在');const emptyState=await page.locator('#resource-empty').isVisible();
 await page.goto(base+'?view=context');
 const dockInitial=await page.evaluate(()=>({height:document.querySelector('#bottom-dock').getBoundingClientRect().height,headHeight:document.querySelector('.dock-head').getBoundingClientRect().height,width:document.querySelector('#bottom-dock').getBoundingClientRect().width,editorHeight:document.querySelector('.context-top').getBoundingClientRect().height}));
 await page.locator('#dock-splitter').focus();await page.keyboard.press('ArrowUp');
 const dockResized=await page.locator('#bottom-dock').evaluate(el=>el.getBoundingClientRect().height);
 await page.getByRole('tab',{name:'表达式',exact:true}).click();const expressionTool=await page.locator('#dock-expressions').isVisible();
 await page.getByRole('tab',{name:'歌词',exact:true}).click();const lyricsTool=await page.locator('#dock-lyrics').isVisible();
 await page.getByRole('tab',{name:'轨道设置',exact:true}).click();const trackTool=await page.locator('#dock-track').isVisible();
 await page.getByRole('tab',{name:'效果',exact:true}).click();
 await page.locator('#dock-collapse').click();const dockCollapsed=await page.locator('#bottom-dock').evaluate(el=>el.getBoundingClientRect().height);
 await page.locator('#dock-collapse').click();
 await page.locator('#rename-open').click();
 const popupFocused=await page.locator('#lyric').evaluate(el=>el===document.activeElement);
 await page.keyboard.press('Escape');const focusRestored=await page.locator('#rename-open').evaluate(el=>el===document.activeElement);
 const report={renderedAt:new Date().toISOString(),browser:await browser.version(),captures,narrow,summaryMode,customRangeVisible,inputLocked,inputRestored,matchingRows,emptyState,dockInitial,dockResized,dockCollapsed,expressionTool,lyricsTool,trackTool,popupFocused,focusRestored,pageErrors:errors};
 await browser.close();fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify(report,null,2)+'\n');
 if(errors.length||narrow.documentWidth!==800||narrow.footerBottom>narrow.statusTop+1||summaryMode!=='实时兼容'||!customRangeVisible||!inputLocked||!inputRestored||matchingRows!==1||!emptyState||dockInitial.width!==1440||dockInitial.headHeight!==40||dockInitial.editorHeight<170||Math.abs(dockInitial.height-387)>1||dockResized<=dockInitial.height||dockCollapsed!==40||!expressionTool||!lyricsTool||!trackTool||!popupFocused||!focusRestored)throw Error(JSON.stringify(report));
 console.log(JSON.stringify(report,null,2));
})().catch(error=>{console.error(error);process.exitCode=1});
