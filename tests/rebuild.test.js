const fs=require('fs'),path=require('path'),http=require('http'),{JSDOM,VirtualConsole}=require('jsdom');
const os=require('os');
const SRC=path.resolve(__dirname,'..');
const VER=fs.readFileSync(SRC+'/VERSION','utf8').trim();
// Each run works in its own temporary folder, so nothing is written into the repo.
const DIR=fs.mkdtempSync(path.join(os.tmpdir(),'qudhud-'));
process.on('exit',()=>{try{fs.rmSync(DIR,{recursive:true,force:true})}catch(e){}});
const page=fs.readFileSync(SRC+'/src/hud.html','utf8')
  .split('__VERSION__').join(VER)
  .replace('FLUSH_MS=5*60*1000','FLUSH_MS=200')
  .replace('FLUSH_MAX=15*60*1000','FLUSH_MAX=1200')
  .replace('},30000);','},50);');
if(!/FLUSH_MS=200/.test(page)||/},30000\);/.test(page))throw new Error('constant patch failed');
fs.writeFileSync(path.join(DIR,'hud.html'),page);

// served over http so the window gets a real origin; on file:// jsdom makes storage throw,
// which Chrome does not do - the page's own try/catch would hide the carry never working.
const server=http.createServer((req,res)=>{
  const f=path.join(DIR,req.url.split('?')[0].replace(/^\/+/,'')||'hud.html');
  fs.readFile(f,(e,b)=>{
    if(e){res.writeHead(404);res.end('no');return;}
    res.writeHead(200,{'Content-Type':f.endsWith('.js')?'text/javascript':'text/html','Cache-Control':'no-store'});
    res.end(b);
  });
});
const pad=n=>String(n).padStart(2,'0');
const stampAgo=sec=>{const d=new Date(Date.now()-sec*1000);return pad(d.getHours())+':'+pad(d.getMinutes())+':'+pad(d.getSeconds());};
const now=()=>stampAgo(0);
const DATA={player:{name:'Tester',level:7,hp:30,hpMax:60},zone:{name:'Joppa'},clock:{},
  attributes:[],combat:{},resist:{},points:{},survival:{},effects:[],abilities:[],
  gear:{missing:[],issues:[],cells:[]},hostiles:[],alerts:[]};
const payload=(seq,ageSec)=>({version:VER,seq:seq,stamp:stampAgo(ageSec||0),data:DATA});
const writeData=(seq,ageSec)=>fs.writeFileSync(path.join(DIR,'hud_data.js'),'window.QUD_HUD='+JSON.stringify(payload(seq,ageSec))+';\n');
const rmData=()=>{try{fs.unlinkSync(path.join(DIR,'hud_data.js'))}catch(e){}};

let fails=0;
const check=(l,ok,x)=>{if(!ok)fails++;console.log((ok?'PASS':'FAIL')+'  '+l+(x?'  -> '+x:''));};

let BASE;
async function load(opts={}){
  const vc=new VirtualConsole();const errs=[],reloads=[];
  vc.on('jsdomError',e=>{
    if(/Not implemented: navigation/.test(e.message))reloads.push(Date.now());
    else errs.push(e.message);
  });
  vc.on('error',(...a)=>errs.push('console.error: '+a.join(' ')));
  const html=fs.readFileSync(path.join(DIR,'hud.html'),'utf8');
  const dom=new JSDOM(html,{url:BASE+'/hud.html',runScripts:'dangerously',resources:'usable',
    pretendToBeVisual:true,virtualConsole:vc,
    beforeParse(w){if(opts.carry!==undefined){w.sessionStorage.setItem('qudhud.carry',
      typeof opts.carry==='string'?opts.carry:JSON.stringify(opts.carry));}}});
  await new Promise(r=>setTimeout(r,opts.wait||600));
  return {dom,d:dom.window.document,w:dom.window,errs,reloads};
}

(async()=>{
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  BASE='http://127.0.0.1:'+server.address().port;

  // 1. a stale link is the moment the flush waits for
  writeData(1, 3*3600);
  let {dom,d,w,errs,reloads}=await load({wait:700});
  check('flush: no unexpected errors',errs.length===0,errs.join(' | '));
  check('flush: reload was called',reloads.length>=1,reloads.length+' reload(s)');
  const stash=JSON.parse(w.sessionStorage.getItem('qudhud.carry')||'null');
  check('flush: carry written to sessionStorage',!!stash);
  check('flush: carry holds the payload',!!(stash&&stash.p&&stash.p.data&&stash.p.data.player.name==='Tester'));
  check('flush: carry holds a timestamp',!!(stash&&typeof stash.at==='number'));
  check('flush: carry holds scroll position',!!(stash&&typeof stash.y==='number'),stash&&'y='+stash.y);
  dom.window.close();

  // 2. a fresh carry paints immediately with NO data file -> no waiting-screen flash
  rmData();
  ({dom,d,w,errs,reloads}=await load({carry:{at:Date.now(),y:0,p:payload(9)},wait:100}));
  check('restore: waiting screen never shown',!d.body.classList.contains('waiting'));
  check('restore: content painted from carry',/Tester/.test(d.getElementById('who').textContent));
  check('restore: carry consumed, not left behind',w.sessionStorage.getItem('qudhud.carry')===null);
  dom.window.close();

  // 3. a stale carry (tab reopened much later) must be ignored
  ({dom,d,w,errs,reloads}=await load({carry:{at:Date.now()-60000,y:0,p:payload(9)},wait:400}));
  check('stale carry ignored',d.body.classList.contains('waiting'));
  check('stale carry: nothing painted',!/Tester/.test(d.getElementById('who').textContent));
  dom.window.close();

  // 4. a corrupt carry must not break the page
  writeData(3);
  ({dom,d,w,errs,reloads}=await load({carry:'not json at all',wait:400}));
  check('corrupt carry: live data still renders',/Tester/.test(d.getElementById('who').textContent));
  check('corrupt carry: page still loads',!!d.getElementById('link'));
  check('corrupt carry: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // 5. the flush must not yank the UI while Options is open
  writeData(2, 3*3600);
  ({dom,d,w,errs,reloads}=await load({wait:120}));
  d.getElementById('opts').hidden=false;
  const before=reloads.length;
  await new Promise(r=>setTimeout(r,500));
  check('flush deferred while Options open',reloads.length===before,'before='+before+' after='+reloads.length);
  d.getElementById('opts').hidden=true;
  await new Promise(r=>setTimeout(r,300));
  check('flush resumes once Options closed',reloads.length>before,'before='+before+' after='+reloads.length);
  dom.window.close();

  // 6. the idle quip must survive a rebuild instead of re-rolling every 5 minutes
  writeData(4, 3*3600);   // stale, so the flush is allowed to fire
  ({dom,d,w,errs,reloads}=await load({wait:700}));
  const shown=d.querySelector('.quip')&&d.querySelector('.quip').textContent;
  check('an idle quip is shown',!!shown,JSON.stringify(shown));
  const stash2=JSON.parse(w.sessionStorage.getItem('qudhud.carry')||'null');
  check('quip is stashed with the carry',stash2&&stash2.q===shown,stash2&&JSON.stringify(stash2.q));
  dom.window.close();
  // feed that carry back in: the same line must come back, not a fresh random one
  rmData();
  ({dom,d,w,errs,reloads}=await load({carry:{at:Date.now(),y:0,q:shown,p:payload(4)},wait:100}));
  const after=d.querySelector('.quip')&&d.querySelector('.quip').textContent;
  check('same quip after the rebuild',after===shown,JSON.stringify(after));
  dom.window.close();

  // 7. while data is still arriving the flush waits, rather than landing mid-fight
  writeData(5);                                   // fresh stamp, so the link stays green
  ({dom,d,w,errs,reloads}=await load({wait:800})); // past FLUSH_MS (200) but under FLUSH_MAX (1200)
  check('no rebuild while the link is live',reloads.length===0,reloads.length+' reload(s)');
  check('page is not stale during that wait',!d.body.classList.contains('stale'));
  // ...but it cannot be deferred forever
  await new Promise(r=>setTimeout(r,900));         // now past FLUSH_MAX
  check('rebuild happens anyway past the ceiling',reloads.length>=1,reloads.length+' reload(s)');
  dom.window.close();

  server.close();
  console.log(fails?('\n'+fails+' FAILURES'):'\nall green');
  process.exit(fails||process.exitCode?1:0);
})();
