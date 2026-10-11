// Only renders this reference HTML in a new isolated browser; never captures the product.
const fs = require('node:fs'), path = require('node:path'), {pathToFileURL} = require('node:url');
const runtime = path.join(process.env.USERPROFILE, '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const {chromium} = require(require.resolve('playwright', {paths:[runtime]}));
(async () => {
  const browser = await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',headless:true,args:['--disable-extensions']});
  try {
    const page = await browser.newPage({viewport:{width:1440,height:900}}), errors=[];
    page.on('pageerror', e=>errors.push(String(e)));
    // Offline reference board. All actual <img> elements must use collected local originals.
    await page.route('https://**/*', route=>route.abort());
    await page.goto(pathToFileURL(path.join(__dirname,'index.html')).href);
    await page.evaluate(()=>document.fonts.ready);
    const images = await page.evaluate(()=>[...document.images].map(el=>({src:el.getAttribute('src'),loaded:el.complete&&el.naturalWidth>0,width:el.naturalWidth,height:el.naturalHeight})));
    if(images.length!==3||images.some(el=>!el.loaded||!el.src.startsWith('assets/'))) throw Error('Official image unavailable');
    const localLinks=await page.evaluate(()=>[...document.querySelectorAll('a[href]')].map(el=>el.getAttribute('href')).filter(x=>!/^https:/.test(x)));
    for(const href of localLinks) if(!fs.existsSync(path.resolve(__dirname,href))) throw Error('Missing local link: '+href);
    await page.screenshot({path:path.join(__dirname,'reference-board.png'),fullPage:true});
    const wide=await page.evaluate(()=>({width:innerWidth,scrollWidth:document.documentElement.scrollWidth,height:document.documentElement.scrollHeight}));
    await page.setViewportSize({width:800,height:700});
    const narrow=await page.evaluate(()=>({width:innerWidth,scrollWidth:document.documentElement.scrollWidth}));
    if(wide.scrollWidth!==wide.width||narrow.scrollWidth!==narrow.width||errors.length) throw Error('Layout overflow or browser error');
    const refs=JSON.parse(fs.readFileSync(path.join(__dirname,'references.json'),'utf8'));
    const crypto=require('node:crypto');
    for(const ref of refs.filter(x=>x.file)){
      const data=fs.readFileSync(path.join(__dirname,'assets',ref.file));
      if(crypto.createHash('sha256').update(data).digest('hex')!==ref.sha256)throw Error('Original changed: '+ref.file);
    }
    const native=JSON.parse(fs.readFileSync(path.join(__dirname,'shape-native-source.json'),'utf8'));
    if(crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,'assets/MaterialShapes.kt.txt'))).digest('hex')!==native.sha256)throw Error('Native source changed');
    const report={renderedAt:new Date().toISOString(),browser:await browser.version(),images,originalImageHashesVerified:3,nativeSourceHashVerified:true,localLinksVerified:localLinks.length,wide,narrow,pageErrors:errors,productCaptured:false};
    fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify(report,null,2)+'\n');
    console.log(JSON.stringify(report,null,2));
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
