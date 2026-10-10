// Render only this new HTML design. No product window or user project is opened.
// Usage: node render.cjs
const path = require('node:path');
const fs = require('node:fs');
const {pathToFileURL} = require('node:url');
const packageRoot = process.env.UTVTU_DESIGN_NODE_MODULES || path.join(process.env.USERPROFILE, '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const {chromium} = require(require.resolve('playwright', {paths:[packageRoot, __dirname]}));
const browserPath = process.env.UTVTU_DESIGN_BROWSER || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
(async () => {
  const browser = await chromium.launch({executablePath:browserPath,headless:true});
  const page = await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
  const errors=[];
  page.on('pageerror',error=>errors.push(String(error)));
  const base = pathToFileURL(path.join(__dirname,'index.html')).href;
  const captures=[];
  for(const theme of ['dark','light']) {
    for(const view of ['arrange','piano','mixer']) {
      await page.goto(base+`?view=${view}&theme=${theme}`);
      await page.evaluate(()=>document.fonts.ready);
      await page.waitForFunction(()=>document.querySelector('.view:not([hidden])').clientHeight > 100);
      const filename=`${view}-${theme}-1440.png`;
      await page.screenshot({path:path.join(__dirname,filename)});
      captures.push({filename,view,theme,width:1440,height:900});
    }
  }
  await page.setViewportSize({width:1024,height:768});
  await page.goto(base+'?view=piano&theme=dark');
  await page.screenshot({path:path.join(__dirname,'piano-dark-1024.png')});
  captures.push({filename:'piano-dark-1024.png',view:'piano',theme:'dark',width:1024,height:768});
  const narrow = await page.evaluate(()=>({documentWidth:document.documentElement.scrollWidth,viewportWidth:innerWidth,centerWidth:document.querySelector('.center').clientWidth,rightHidden:getComputedStyle(document.querySelector('.right')).visibility==='hidden'}));
  await page.locator('#toggle-right').click();
  const narrowReveal=await page.locator('.right').isVisible();
  await page.setViewportSize({width:1440,height:900});
  await page.goto(base);
  await page.getByRole('tab',{name:/混音/}).click();
  await page.getByRole('button',{name:'主唱静音',exact:true}).click();
  const dirtyOnMute=await page.locator('#save-state').innerText();
  await page.locator('#undo').click();
  const muteUndo=await page.getByRole('button',{name:'主唱静音',exact:true}).getAttribute('aria-pressed');
  await page.locator('#redo').click();
  const muteRedo=await page.getByRole('button',{name:'主唱静音',exact:true}).getAttribute('aria-pressed');
  await page.locator('#save').click();
  const saved=await page.locator('#save-state').innerText();
  await page.locator('[data-fader="0"]').evaluate(el=>{el.value='55';el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}))});
  const volumeEdited=await page.locator('#inspector-db').inputValue();
  const volumeDirty=await page.locator('#save-state').innerText();
  await page.locator('#undo').click();
  const volumeUndo=await page.locator('#inspector-db').inputValue();
  await page.locator('#toggle-left').click();
  await page.locator('#toggle-right').click();
  const collapsed=await page.locator('#workspace').getAttribute('class');
  await page.keyboard.press('Control+2');
  const shortcutView=await page.locator('#view-piano').isVisible();
  await page.locator('#theme').click();
  const themeToggle=await page.locator('html').getAttribute('data-theme');
  await page.locator('#export').click();
  const dialog=await page.locator('#export-dialog').isVisible();
  await page.keyboard.press('Escape');
  const report={renderedAt:new Date().toISOString(),browser:await browser.version(),captures,narrow,interactions:{dirtyOnMute,muteUndo,muteRedo,saved,volumeEdited,volumeDirty,volumeUndo,collapsed,shortcutView,themeToggle,dialog,narrowReveal},pageErrors:errors};
  if(errors.length||dirtyOnMute!=='未保存'||muteUndo!=='false'||muteRedo!=='true'||saved!=='已保存'||volumeDirty!=='未保存'||volumeUndo!=='-3.2'||!narrowReveal||!shortcutView||themeToggle!=='light'||!dialog||narrow.documentWidth!==narrow.viewportWidth){await browser.close();throw Error(JSON.stringify(report,null,2))}
  fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify(report,null,2)+'\n');
  await browser.close();
  console.log(JSON.stringify(report,null,2));
})().catch(error=>{console.error(error);process.exitCode=1});
