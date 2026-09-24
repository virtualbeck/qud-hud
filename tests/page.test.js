const fs=require('fs'),path=require('path'),http=require('http'),{JSDOM,VirtualConsole}=require('jsdom');
const os=require('os');
const SRC=path.resolve(__dirname,'..');
const VER=fs.readFileSync(SRC+'/VERSION','utf8').trim();
// Each run works in its own temporary folder, so nothing is written into the repo.
const DIR=fs.mkdtempSync(path.join(os.tmpdir(),'qudhud-'));
process.on('exit',()=>{try{fs.rmSync(DIR,{recursive:true,force:true})}catch(e){}});
const page=fs.readFileSync(SRC+'/src/hud.html','utf8').split('__VERSION__').join(VER);
fs.writeFileSync(path.join(DIR,'hud.html'),page);

const pad=n=>String(n).padStart(2,'0');
function stampAgo(sec){
  const d=new Date(Date.now()-sec*1000);
  return pad(d.getHours())+':'+pad(d.getMinutes())+':'+pad(d.getSeconds());
}
const DATA={
  player:{name:'{{W|Tester}}',genotype:'True Kin',subtype:'Greybeard',level:7,xp:1500,xpNext:2000,xpPrev:1200,hp:30,hpMax:60},
  zone:{name:'Joppa',depth:0},clock:{time:'morning',day:'12th',month:'Ut yar'},
  attributes:[{label:'STR',value:18,base:16,mod:1},{label:'AGI',value:14,base:16,mod:-1}],
  combat:{av:5,dv:6,ma:4,quickness:100,moveSpeed:100},
  resist:{heat:0,cold:0,acid:0,elec:0},points:{ap:1,sp:60,mp:0},
  survival:{water:'quenched',food:'not hungry',drams:5,temp:25,flame:350,freeze:-100,weight:50,maxWeight:200},
  effects:[{name:'{{r|Bleeding}}',class:'Bleeding',duration:5,details:'losing blood',negative:true,disease:false}],
  abilities:[{name:'Sprint',enabled:true,cooldown:true,cooldownTurns:12,toggleable:false,toggled:false},
             {name:'Regenerate',enabled:true,cooldown:false,cooldownTurns:0,toggleable:true,toggled:true}],
  gear:{missing:['left hand'],issues:[{item:'vinewood bow',issue:'{{m|fungal infection}}'}],
        cells:[{item:'laser rifle',charge:200,max:1000},{item:'flashlight',empty:true}]},
  hostiles:[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',hp:10,hpMax:12}],
  alerts:[{sev:3,text:'1 hostile adjacent to you'},{sev:2,text:'Hit points low: 30 / 60'}]
};
function writeData(ageSec,seq){
  fs.writeFileSync(path.join(DIR,'hud_data.js'),
    'window.QUD_HUD='+JSON.stringify({version:VER,seq:seq,stamp:stampAgo(ageSec),data:DATA})+';\n');
}

let fails=0;
const check=(label,ok,extra)=>{if(!ok)fails++;console.log((ok?'PASS':'FAIL')+'  '+label+(extra?'  -> '+extra:''));};

const server=http.createServer((req,res)=>{
  const f=path.join(DIR,req.url.split('?')[0].replace(/^\/+/,'')||'hud.html');
  fs.readFile(f,(e,b)=>{
    if(e){res.writeHead(404);res.end('no');return;}
    res.writeHead(200,{'Content-Type':f.endsWith('.js')?'text/javascript':'text/html','Cache-Control':'no-store'});
    res.end(b);
  });
});
let BASE;
// A whitespace character right after a CSS hex escape is eaten as the escape's delimiter, so
// content:'\\25CF ' renders the glyph with no space after it. Guard against it coming back.
{
  const bad=[...page.matchAll(/content:\s*'((?:[^'\\]|\\.)*)'/g)]
    .filter(m=>/\\[0-9a-fA-F]{1,6}[ \t]/.test(m[1]));
  if(bad.length){console.log('FAIL  swallowed space in: '+bad.map(m=>m[0]).join(', '));process.exitCode=1;}
  else console.log('PASS  no CSS escape swallows its trailing space');
}

// Markup emits an inline colour that beats the stylesheet, so a pure white in the palette cannot
// be toned down by the --white token. Keep both free of it.
{
  const hits=[...page.matchAll(/#ffffff|#fff\b/gi)].map(m=>m[0]);
  if(hits.length){console.log('FAIL  pure white still present: '+hits.join(', '));process.exitCode=1;}
  else console.log('PASS  no pure white in the stylesheet or the palette');
}

async function load(opts={}){
  const errs=[];
  const vc=new VirtualConsole();
  vc.on('jsdomError',e=>{
    if(/Not implemented: HTMLCanvasElement/.test(e.message))return;   // jsdom has no canvas
    errs.push('jsdomError: '+e.message);
  });
  vc.on('error',(...a)=>errs.push('console.error: '+a.join(' ')));
  const html=fs.readFileSync(path.join(DIR,'hud.html'),'utf8');
  const dom=new JSDOM(html,{url:BASE+(opts.path||'/hud.html'),runScripts:'dangerously',resources:'usable',
    pretendToBeVisual:true,virtualConsole:vc,
    beforeParse(w){if(opts.store)Object.keys(opts.store).forEach(k=>w.localStorage.setItem(k,opts.store[k]));}});
  await new Promise(r=>setTimeout(r,600));
  return {dom,errs,d:dom.window.document};
}

(async()=>{
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  BASE='http://127.0.0.1:'+server.address().port;
  // --- fresh data: link should be green and the HUD should render
  writeData(0,1);
  let {dom,errs,d}=await load();
  const link=d.getElementById('link');
  check('no script/console errors on load',errs.length===0,errs.join(' | '));
  check('waiting screen dismissed',!d.body.classList.contains('waiting'));
  check('link is green (on)',link.className==='on',JSON.stringify(link.className));
  check('link text says Linked',/^Linked\. Last change/.test(link.textContent),JSON.stringify(link.textContent));
  check('player name rendered',/Tester/.test(d.getElementById('who').textContent));
  check('hostile rendered',/snapjaw scavenger/.test(d.getElementById('foes').textContent));
  check('direction arrow rendered',/↗/.test(d.getElementById('foes').textContent));
  check('effects rendered',/Bleeding/.test(d.getElementById('effects').textContent));
  check('abilities rendered',/Sprint/.test(d.getElementById('abilities').textContent));
  check('gear rendered',/laser rifle/.test(d.getElementById('gear').textContent));
  check('local water alert fired',/Fresh water low: 5 drams/.test(d.getElementById('warnBody').textContent));
  check('title shows critical prefix',/^!! /.test(d.title),JSON.stringify(d.title));
  check('grips added to panels',d.querySelectorAll('.grip').length>0,d.querySelectorAll('.grip').length+' grips');
  // carry round-trip: a payload that survived JSON in sessionStorage must still render
  const carried=JSON.parse(JSON.stringify({version:VER,seq:1,stamp:stampAgo(0),data:DATA}));
  dom.window.__qudhud.render(carried);
  check('carried payload re-renders',/Tester/.test(d.getElementById('who').textContent));
  dom.window.close();

  // --- stale data: game exited hours ago, file still on disk
  writeData(3*3600,1);
  ({dom,errs,d}=await load());
  const l2=d.getElementById('link');
  check('stale: no errors',errs.length===0,errs.join(' | '));
  check('stale: link is amber',l2.className==='stale',JSON.stringify(l2.className));
  check('stale: text reports age',/^No updates for 3h 0m\./.test(l2.textContent),JSON.stringify(l2.textContent));
  check('stale: data still rendered',/Tester/.test(d.getElementById('who').textContent));
  dom.window.close();

  // --- just under the threshold stays green
  writeData(110,1);
  ({dom,errs,d}=await load());
  check('110s old still green',d.getElementById('link').className==='on',JSON.stringify(d.getElementById('link').className));
  dom.window.close();

  // --- just over the threshold goes amber
  writeData(130,1);
  ({dom,errs,d}=await load());
  check('130s old goes amber',d.getElementById('link').className==='stale',JSON.stringify(d.getElementById('link').className));
  dom.window.close();

  // --- missing data file: red, waiting
  fs.unlinkSync(path.join(DIR,'hud_data.js'));
  ({dom,errs,d}=await load());
  const l3=d.getElementById('link');
  check('missing file: link is red',l3.className==='off',JSON.stringify(l3.className));
  check('missing file: waiting screen shown',d.body.classList.contains('waiting'));
  check('missing file: text is waiting msg',/Waiting for Caves of Qud/.test(l3.textContent),JSON.stringify(l3.textContent));
  dom.window.close();

  // --- armour and dodge carry the game's marks
  writeData(0,1);
  ({dom,errs,d}=await load());
  const combat=d.getElementById('combat');
  check('AV value is marked',combat.querySelectorAll('.stats .v.av').length===1);
  check('DV value is marked',combat.querySelectorAll('.stats .v.dv').length===1);
  check('only AV and DV are marked',combat.querySelectorAll('.stats .v.av,.stats .v.dv').length===2);
  check('AV and DV still show their numbers',
        combat.querySelector('.v.av').textContent==='5'&&combat.querySelector('.v.dv').textContent==='6',
        combat.querySelector('.v.av').textContent+'/'+combat.querySelector('.v.dv').textContent);
  check('AV and DV keep their text labels',/AV/.test(combat.textContent)&&/DV/.test(combat.textContent));
  check('resistances are not marked',
        [].slice.call(combat.querySelectorAll('.res .v')).every(n=>!n.classList.contains('av')&&!n.classList.contains('dv')));
  check('combat panel: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // --- health colour comes from the game, not from a percentage
  DATA.hostiles=[{name:'snapjaw brute',level:1,rating:'{{G|Trivial}}',distance:3,dir:'N',
                  dangerRank:0,exact:true,hp:13,hpMax:25,hcol:'W'}];
  DATA.player={name:'{{W|Tester}}',genotype:'True Kin',subtype:'Greybeard',level:7,xp:1500,
               xpNext:2000,xpPrev:1200,exact:true,hp:13,hpMax:25,hcol:'W'};
  writeData(0,1);
  ({dom,errs,d}=await load());
  let hpn=d.getElementById('foes').querySelector('.hpnum');
  check('13/25 uses the game colour, not the 50% rule',
        /#cfc041/.test(hpn.getAttribute('style')),hpn.getAttribute('style'));
  check('the bar matches the figures',
        /#cfc041/.test(d.getElementById('foes').querySelector('.bar.hp i').getAttribute('style')));
  check('player hp uses the game colour too',
        /#cfc041/.test(d.getElementById('vitals').querySelector('b').getAttribute('style')),
        d.getElementById('vitals').querySelector('b').getAttribute('style'));
  dom.window.close();

  // an unknown or missing colour code must fall back rather than render nothing
  DATA.hostiles=[{name:'snapjaw brute',level:1,rating:'{{G|Trivial}}',distance:3,dir:'N',
                  dangerRank:0,exact:true,hp:13,hpMax:25}];
  writeData(0,2);
  ({dom,errs,d}=await load());
  hpn=d.getElementById('foes').querySelector('.hpnum');
  check('missing colour falls back to a real colour',/color:#[0-9a-f]{6}/.test(hpn.getAttribute('style')),
        hpn.getAttribute('style'));
  check('missing colour: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  DATA.player={name:'{{W|Tester}}',genotype:'True Kin',subtype:'Greybeard',level:7,xp:1500,
               xpNext:2000,xpPrev:1200,exact:true,hp:30,hpMax:60};

  // --- hostile sort toggle. The mod sends them nearest first, already broken by danger then hp.
  DATA.hostiles=[
    {name:'near-weak',  level:1,rating:'{{G|Trivial}}',    distance:1,dir:'N', dangerRank:3,exact:true,hp:2, hpMax:2},
    {name:'mid-tough-a',level:5,rating:'{{W|Tough}}',      distance:4,dir:'E', dangerRank:1,exact:true,hp:30,hpMax:40},
    {name:'mid-tough-b',level:5,rating:'{{W|Tough}}',      distance:4,dir:'S', dangerRank:2,exact:true,hp:8, hpMax:40},
    {name:'far-deadly', level:9,rating:'{{R|Impossible}}', distance:9,dir:'W', dangerRank:0,exact:true,hp:60,hpMax:60}];
  const names=el=>[].slice.call(el.querySelectorAll('.foe .nm, .foe')).filter(n=>n.classList.contains('foe'))
                    .map(n=>n.textContent.split(' L')[0].trim());

  writeData(0,1);
  ({dom,errs,d}=await load());
  let srt=d.getElementById('foes');
  check('sort toggle is present',srt.querySelectorAll('.sortbtn').length===1);
  check('default label reads Nearest',/Sort: Nearest/.test(srt.textContent),
        JSON.stringify(srt.querySelector('.sortbtn').textContent));
  check('default order is nearest first',names(srt).join(),
        'near-weak,mid-tough-a,mid-tough-b,far-deadly');
  // flip to dangerous
  srt.querySelector('.sortbtn').click();
  srt=d.getElementById('foes');
  check('label flips to Dangerous',/Sort: Dangerous/.test(srt.textContent));
  check('dangerous order follows the rank the mod sent',names(srt).join(),
        'far-deadly,mid-tough-a,mid-tough-b,near-weak');
  check('choice persisted',dom.window.localStorage.getItem('qudhud.foeSort')==='danger');
  // flip back
  srt.querySelector('.sortbtn').click();
  check('flips back to nearest',names(d.getElementById('foes')).join(),
        'near-weak,mid-tough-a,mid-tough-b,far-deadly');
  check('sort toggle: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // the saved choice is applied on load
  ({dom,errs,d}=await load({store:{'qudhud.foeSort':'danger'}}));
  check('saved choice applied on load',/Sort: Dangerous/.test(d.getElementById('foes').textContent));
  check('saved choice orders the list',names(d.getElementById('foes')).join(),
        'far-deadly,mid-tough-a,mid-tough-b,near-weak');
  dom.window.close();

  // a rubbish stored value must fall back to nearest rather than breaking the panel
  ({dom,errs,d}=await load({store:{'qudhud.foeSort':'sideways'}}));
  check('bad stored value falls back to nearest',/Sort: Nearest/.test(d.getElementById('foes').textContent));
  dom.window.close();

  // no toggle when there is nothing to sort
  DATA.hostiles=[];
  writeData(0,2);
  ({dom,errs,d}=await load());
  check('no toggle when no hostiles',d.getElementById('foes').querySelectorAll('.sortbtn').length===0);
  check('empty state still shown',/No hostiles in sight/.test(d.getElementById('foes').textContent));
  dom.window.close();
  DATA.hostiles=[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',dangerRank:0,exact:true,hp:10,hpMax:12}];

  // --- the sort picks its own 15: dangerous creatures beyond the fifteenth nearest must still show
  {
    // 20 hostiles, as the mod sends them: the nearest 15 plus the most dangerous 15. The five
    // dangerous ones are the furthest away, so a list cut in nearest order would never show them.
    const hs=[];
    for(let i=0;i<20;i++){
      const deadly=i>=15;
      hs.push({name:(deadly?'deadly-':'snapjaw-')+i,level:deadly?20:1,
               rating:deadly?'{{R|Impossible}}':'{{G|Trivial}}',distance:i+1,dir:'N',
               nearRank:i,dangerRank:deadly?i-15:i+5,exact:false,health:'{{G|Perfect}}'});
    }
    DATA.hostiles=hs;
    writeData(0,1);
    const foeNames=el=>[].slice.call(el.querySelectorAll('.foe')).map(n=>n.textContent.split(' L')[0].trim());
    ({dom,errs,d}=await load({store:{'qudhud.foeSort':'near'}}));
    let shown=foeNames(d.getElementById('foes'));
    check('Nearest shows 15',shown.length===15,shown.length+' shown');
    check('Nearest shows the 15 nearest',shown[0]==='snapjaw-0'&&shown[14]==='snapjaw-14',shown[0]+'..'+shown[14]);
    check('Nearest leaves out the far dangerous ones',!shown.some(n=>n.startsWith('deadly')));
    dom.window.close();
    ({dom,errs,d}=await load({store:{'qudhud.foeSort':'danger'}}));
    shown=foeNames(d.getElementById('foes'));
    check('Dangerous shows 15',shown.length===15,shown.length+' shown');
    check('Dangerous leads with the far dangerous ones',
          shown.slice(0,5).every(n=>n.startsWith('deadly')),shown.slice(0,5).join());
    check('Dangerous drops the least dangerous snapjaws',
          !shown.includes('snapjaw-14')&&!shown.includes('snapjaw-10'));
    check('sort cap: no errors',errs.length===0,errs.join(' | '));
    dom.window.close();
    DATA.hostiles=[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',dangerRank:0,exact:true,hp:10,hpMax:12}];
  }

  // --- abilities can hide the ones that are simply ready
  {
    DATA.abilities=[
      {name:'Sprint',enabled:true,cooldown:true,cooldownTurns:12,toggleable:false,toggled:false},
      {name:'Force Bubble',enabled:true,cooldown:false,cooldownTurns:0,toggleable:true,toggled:false},
      {name:'Discharge',enabled:false,cooldown:false,cooldownTurns:0,toggleable:false,toggled:false},
      {name:'Jump',enabled:true,cooldown:false,cooldownTurns:0,toggleable:false,toggled:false},
      {name:'Berate',enabled:true,cooldown:false,cooldownTurns:0,toggleable:false,toggled:false}];
    writeData(0,1);
    ({dom,errs,d}=await load());
    let ab=d.getElementById('abilities').textContent;
    check('abilities: all shown by default',/Jump/.test(ab)&&/Berate/.test(ab)&&/Sprint/.test(ab));
    check('abilities: option is off by default',
          d.querySelector('.tog[data-opt=abilQuiet]').getAttribute('aria-checked')==='false');
    d.querySelector('.tog[data-opt=abilQuiet]').click();
    ab=d.getElementById('abilities').textContent;
    check('abilities: ready ones hidden once switched on',!/Jump/.test(ab)&&!/Berate/.test(ab),JSON.stringify(ab));
    check('abilities: cooldown kept',/Sprint/.test(ab));
    check('abilities: toggle kept',/Force Bubble/.test(ab));
    check('abilities: disabled kept',/Discharge/.test(ab));
    check('abilities: choice persisted',JSON.parse(dom.window.localStorage.getItem('qudhud.opts')).abilQuiet===true);
    dom.window.close();

    DATA.abilities=[{name:'Jump',enabled:true,cooldown:false,cooldownTurns:0,toggleable:false,toggled:false}];
    writeData(0,2);
    ({dom,errs,d}=await load({store:{'qudhud.opts':JSON.stringify({abilQuiet:true})}}));
    check('abilities: all ready shows a message, not an empty panel',
          /All abilities ready/.test(d.getElementById('abilities').textContent));
    check('abilities: saved option leaves water settings at their defaults',
          d.querySelector('.tog[data-opt=waterWarn]').getAttribute('aria-checked')==='true');
    check('abilities: no errors',errs.length===0,errs.join(' | '));
    dom.window.close();
    DATA.abilities=[{name:'Sprint',enabled:true,cooldown:true,cooldownTurns:12,toggleable:false,toggled:false},
                    {name:'Regenerate',enabled:true,cooldown:false,cooldownTurns:0,toggleable:true,toggled:true}];
  }

  // --- companions: the same condition readout as hostiles, and nothing at all when out of sight
  {
    DATA.companions=[
      {name:'Mehmet',level:12,seen:true,distance:1,dir:'W',exact:false,health:'{{W|Injured}}',
       effects:[{name:'{{r|bleeding}}',negative:true,disease:false}]},
      {name:'turret',level:30,seen:true,distance:4,dir:'N',exact:true,hp:9,hpMax:20,hcol:'W'},
      {name:'Warden Yrame',level:20,seen:false}];
    writeData(0,1);
    ({dom,errs,d}=await load());
    const pals=d.getElementById('pals'),items=pals.querySelectorAll('.pal');
    check('companions: panel is on the page',!!d.querySelector('[data-panel=pals]'));
    check('companions: every companion listed',items.length===3,items.length+' listed');
    check('companions: health word shown',/Injured/.test(items[0].textContent));
    check('companions: their effects shown',/bleeding/.test(items[0].textContent)&&items[0].querySelectorAll('.fx.bad').length===1);
    check('companions: an adjacent companion is not marked as a threat',
          !pals.querySelector('.adj')&&/adjacent/.test(items[0].textContent));
    check('companions: exact figures when scannable',/9\/20/.test(items[1].textContent)&&items[1].querySelectorAll('.bar.hp').length===1);
    check('companions: figures use the game colour',/#cfc041/.test(items[1].querySelector('.hpnum').getAttribute('style')));
    check('companions: out of sight says so',/out of sight/.test(items[2].textContent));
    check('companions: out of sight shows no distance',!/away|adjacent/.test(items[2].textContent));
    check('companions: out of sight shows no condition',
          !items[2].querySelector('.hpline,.hpw,.foefx'));
    check('companions: listed in Options',!!d.querySelector('[data-panel-tog=pals]'));
    check('companions: no errors',errs.length===0,errs.join(' | '));
    dom.window.close();

    DATA.companions=[];
    writeData(0,2);
    ({dom,errs,d}=await load());
    check('companions: none says so',/No companions in this zone/.test(d.getElementById('pals').textContent));
    dom.window.close();

    DATA.companions=null;
    writeData(0,3);
    ({dom,errs,d}=await load());
    check('companions: unavailable says so',/unavailable/.test(d.getElementById('pals').textContent));
    check('companions: unavailable breaks nothing else',/Tester/.test(d.getElementById('who').textContent)&&errs.length===0);
    dom.window.close();
    delete DATA.companions;
  }

  // --- the message log: newest last, coloured by the game's markup, and never read as HTML
  {
    DATA.messages=['You hit the snapjaw.','{{R|The snapjaw bites you!}}',
                   'A name like <img src=x onerror="window.__pwned=1"> stays text.','You feel hungry.'];
    writeData(0,1);
    ({dom,errs,d}=await load());
    const box=d.getElementById('msgs'),rows=box.querySelectorAll('.msg');
    check('messages: panel is on the page',!!d.querySelector('[data-panel=msgs]'));
    check('messages: every line shown',rows.length===4,rows.length+' lines');
    check('messages: in order, newest last',
          rows[0].textContent==='You hit the snapjaw.'&&rows[3].textContent==='You feel hungry.');
    check('messages: only the newest is marked latest',
          rows[3].classList.contains('latest')&&box.querySelectorAll('.latest').length===1);
    check('messages: game colours are rendered',/<span style="color:/.test(rows[1].innerHTML));
    check('messages: no raw markup leaks',!/\{\{/.test(box.textContent));
    check('messages: HTML in a message is shown as text',
          box.querySelectorAll('img').length===0&&/<img src=x/.test(rows[2].textContent));
    check('messages: nothing in a message runs',dom.window.__pwned===undefined);
    check('messages: listed in Options',!!d.querySelector('[data-panel-tog=msgs]'));
    check('messages: no errors',errs.length===0,errs.join(' | '));
    dom.window.close();

    DATA.messages=[];
    writeData(0,2);
    ({dom,errs,d}=await load());
    check('messages: none yet says so',/No messages yet/.test(d.getElementById('msgs').textContent));
    dom.window.close();

    DATA.messages=null;
    writeData(0,3);
    ({dom,errs,d}=await load());
    check('messages: unavailable says so',/unavailable/.test(d.getElementById('msgs').textContent));
    dom.window.close();
    delete DATA.messages;
  }

  // --- the minimap: decoding, what the character knows, and a painter that survives no canvas
  {
    // a 6x2 zone. Row 0: wall, dagger, open ground, then three unexplored cells.
    // Row 1: ground, ground and a wall only remembered, then three cells of open ground in view.
    // c is "K c - [space x3]" then "w w K - - -"; v marks which cells are in view right now.
    DATA.map={w:6,h:2,px:2,py:0,c:'Kc- 3w2K-3',v:'V3.6V3'};
    writeData(0,1);
    ({dom,errs,d}=await load());
    const w=dom.window;
    // the same literal the C# side is tested against in SurroundingsHarness.cs, so the two agree
    check('minimap: decodes exactly what the mod encodes',w.__qudhud.unrle('k4-y 2').join('')==='kkkk-y  ');
    check('minimap: run-length decoding',
          w.__qudhud.unrle('K3-y12').join('')==='KKK-'+'y'.repeat(12)&&w.__qudhud.unrle('').length===0);
    const cells=w.__qudhud.minimapCells(DATA.map);
    const at=(x,y)=>cells.find(k=>k.x===x&&k.y===y);
    check('minimap: unexplored cells are not painted',!at(3,0)&&!at(4,0)&&!at(5,0),JSON.stringify(cells.map(k=>k.x+','+k.y)));
    check('minimap: a cell in view uses its object colour',at(1,0).col==='#40a4b9'&&at(1,0).seen);
    check('minimap: open ground has its own colour',at(2,0).col==='#0e3230');
    check('minimap: remembered cells are marked as not in view',!at(0,1).seen&&!at(2,1).seen,
          JSON.stringify([at(0,1),at(2,1)]));
    check('minimap: in view again after',at(3,1).seen&&at(4,1).seen&&at(5,1).seen);
    check('minimap: a canvas is placed in the panel',!!d.querySelector('#map canvas'));
    check('minimap: sized to the zone, cells taller than wide',
          /px$/.test(d.querySelector('#map canvas').style.width)&&
          parseInt(d.querySelector('#map canvas').style.height)*6>parseInt(d.querySelector('#map canvas').style.width)*2);
    check('minimap: no errors with no canvas support',errs.length===0,errs.join(' | '));
    dom.window.close();

    DATA.map=null;
    writeData(0,2);
    ({dom,errs,d}=await load());
    check('minimap: unavailable says so',/Minimap unavailable/.test(d.getElementById('map').textContent));
    dom.window.close();
    delete DATA.map;
  }

  // --- nearby objects, and the filters the game's own window offers
  {
    DATA.nearby=[
      {name:'bronze dagger',kind:'item',col:'c',distance:0,dir:'here'},
      {name:'dromad merchant',kind:'creature',col:'W',distance:2,dir:'E'},
      {name:'witchwood tree',kind:'plant',col:'g',distance:3,dir:'N'},
      {name:'pool of <b>salt</b>',kind:'liquid',col:'b',distance:4,dir:'S'},
      {name:'{{W|chest}}',kind:'container',col:'w',distance:5,dir:'W'}];
    writeData(0,1);
    ({dom,errs,d}=await load());
    let near=d.getElementById('near'),rows=near.querySelectorAll('.row');
    check('nearby: panel is on the page',!!d.querySelector('[data-panel=near]'));
    check('nearby: everything listed by default',rows.length===5,rows.length+' rows');
    check('nearby: something underfoot reads "here"',/^here/.test(rows[0].querySelector('.dist').textContent),
          rows[0].querySelector('.dist').textContent);
    check('nearby: distance and direction otherwise',/2 away/.test(rows[1].textContent)&&/→/.test(rows[1].textContent));
    check('nearby: each carries its colour mark',
          /#40a4b9/.test(rows[0].querySelector('.mark').getAttribute('style')));
    check('nearby: names keep game colours',/<span style="color:/.test(rows[4].innerHTML)&&!/\{\{/.test(near.textContent));
    check('nearby: a name is shown as text, never markup',
          near.querySelectorAll('b').length===0&&/<b>salt<\/b>/.test(near.textContent));
    check('nearby: no errors',errs.length===0,errs.join(' | '));
    // the takeable-only filter, switched on from Options
    d.querySelector('.tog[data-opt=nearTakeable]').click();
    rows=d.getElementById('near').querySelectorAll('.row');
    check('nearby: takeable only leaves just the items',rows.length===1&&/bronze dagger/.test(rows[0].textContent),rows.length+' rows');
    dom.window.close();

    ({dom,errs,d}=await load({store:{'qudhud.opts':JSON.stringify({nearPlants:false,nearLiquids:false})}}));
    near=d.getElementById('near');
    check('nearby: plants and pools can be hidden',
          !/witchwood/.test(near.textContent)&&!/salt/.test(near.textContent)&&/merchant/.test(near.textContent));
    dom.window.close();

    DATA.nearby=[{name:'witchwood tree',kind:'plant',col:'g',distance:3,dir:'N'}];
    writeData(0,2);
    ({dom,errs,d}=await load({store:{'qudhud.opts':JSON.stringify({nearPlants:false})}}));
    check('nearby: filtered to nothing says why',/matches the filters/.test(d.getElementById('near').textContent));
    dom.window.close();

    DATA.nearby=[];
    writeData(0,3);
    ({dom,errs,d}=await load());
    check('nearby: nothing says so',/Nothing of note nearby/.test(d.getElementById('near').textContent));
    dom.window.close();

    DATA.nearby=null;
    writeData(0,4);
    ({dom,errs,d}=await load());
    check('nearby: unavailable says so',/unavailable/.test(d.getElementById('near').textContent));
    dom.window.close();
    delete DATA.nearby;
  }

  // --- the three windows the game docks together share a column
  {
    writeData(0,1);
    ({dom,errs,d}=await load());
    const c3=[].slice.call(d.querySelectorAll('[data-zone=c3] > .panel')).map(n=>n.dataset.panel).join();
    check('layout: minimap, nearby and messages share the third column',c3==='map,near,msgs',c3);
    dom.window.close();
  }

  // --- dismissable reminders in Pressing matters: per character, per kind, until spent
  {
    const tester=DATA.player;
    const stored=()=>JSON.parse(win.localStorage.getItem('qudhud.dismissed')||'[]');
    DATA.alerts=[{sev:3,text:'1 hostile adjacent to you'},
                 {sev:1,text:'160 unspent skill points',dis:'sp'},
                 {sev:1,text:'2 unspent mutation points',dis:'mp'}];
    DATA.survival={};                                  // no water warning in the way
    writeData(0,1);
    ({dom,errs,d}=await load());
    let warn=d.getElementById('warnBody'), win=dom.window;
    check('dismissable alerts get a button',warn.querySelectorAll('.drop').length===2,
          warn.querySelectorAll('.drop').length+' buttons');
    check('non-dismissable alert has none',
          warn.querySelectorAll('li').length===3&&warn.querySelectorAll('.drop').length===2);
    warn.querySelector('[data-key="Tester|sp"]').click();
    warn=d.getElementById('warnBody');
    check('dismissed alert disappears',!/unspent skill points/.test(warn.textContent));
    check('other alerts remain',/hostile adjacent/.test(warn.textContent)&&/2 unspent mutation/.test(warn.textContent));
    check('dismissal persisted, keyed by character and kind',stored().join()==='Tester|sp',stored().join());
    dom.window.close();
    const saved=JSON.stringify(['Tester|sp']);

    // reported: levelling up brought it back. The number changes, the dismissal must not lapse.
    DATA.alerts=[{sev:1,text:'170 unspent skill points',dis:'sp'}];
    writeData(0,2);
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':saved}}));
    win=dom.window;
    check('levelling up does not bring it back',!/170 unspent skill points/.test(d.getElementById('warnBody').textContent));
    check('levelling up keeps the dismissal',stored().join()==='Tester|sp',stored().join());
    dom.window.close();

    // reported: another character in between brought all of them back
    DATA.player=Object.assign({},tester,{name:'Mehmet'});
    DATA.alerts=[];
    writeData(0,3);
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':saved}}));
    win=dom.window;
    check('another character with no points leaves it alone',stored().join()==='Tester|sp',stored().join());
    dom.window.close();
    DATA.player=tester;
    DATA.alerts=[{sev:1,text:'175 unspent skill points',dis:'sp'}];
    writeData(0,4);
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':saved}}));
    check('back on the first character it is still dismissed',!/unspent skill points/.test(d.getElementById('warnBody').textContent));
    dom.window.close();

    // spending them is what lets it lapse, so banking points again later is reminded afresh
    DATA.alerts=[];
    writeData(0,5);
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':saved}}));
    win=dom.window;
    check('spending the points lets the dismissal lapse',stored().length===0,stored().join());
    dom.window.close();
    DATA.alerts=[{sev:1,text:'60 unspent skill points',dis:'sp'}];
    writeData(0,6);
    ({dom,errs,d}=await load());
    check('banking points again brings it back',/60 unspent skill points/.test(d.getElementById('warnBody').textContent));
    dom.window.close();

    // a dismissal from an older version was keyed on wording and can never match, so it is dropped
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':JSON.stringify(['160 unspent skill points'])}}));
    win=dom.window;
    check('old wording-keyed entries are dropped',stored().length===0,stored().join());
    dom.window.close();

    // dismissing everything leaves the quiet state, not an empty list
    ({dom,errs,d}=await load({store:{'qudhud.dismissed':JSON.stringify(['Tester|sp'])}}));
    check('all dismissed shows the quiet state',/Nothing pressing/.test(d.getElementById('warnBody').textContent));
    check('dismissal does not drop the critical title prefix',!/^!! /.test(d.title),JSON.stringify(d.title));
    check('dismissals: no errors',errs.length===0,errs.join(' | '));
    dom.window.close();
  }
  DATA.alerts=[{sev:3,text:'1 hostile adjacent to you'},{sev:2,text:'Hit points low: 30 / 60'}];
  DATA.survival={water:'quenched',food:'not hungry',drams:5,temp:25,flame:350,freeze:-100,weight:50,maxWeight:200};

  // --- hostile readout: numbers before the bar, and status effects in both modes
  DATA.hostiles=[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',
    exact:true,hp:4,hpMax:15,
    effects:[{name:'{{r|bleeding}}',negative:true,disease:false},
             {name:'{{G|regenerating}}',negative:false,disease:false}]}];
  writeData(0,1);
  ({dom,errs,d}=await load());
  let foe=d.getElementById('foes');
  check('exact: numbers are shown',/4\/15/.test(foe.textContent),JSON.stringify(foe.textContent.trim()));
  check('exact: numbers come before the bar',
        foe.querySelector('.hpline').firstElementChild.classList.contains('hpnum'));
  check('exact: bar still drawn beside them',foe.querySelectorAll('.hpline .bar.hp').length===1);
  check('exact: numbers are coloured',/color:/.test(foe.querySelector('.hpnum').getAttribute('style')),
        foe.querySelector('.hpnum').getAttribute('style'));
  check('exact: effects shown',/bleeding/.test(foe.textContent)&&/regenerating/.test(foe.textContent));
  check('exact: harmful effect marked bad',foe.querySelectorAll('.foefx .fx.bad').length===1);
  check('exact: helpful effect marked good',foe.querySelectorAll('.foefx .fx.good').length===1);
  check('exact: effect colours from qud markup',
        /<span style="color:/.test(foe.querySelector('.foefx .fx .nm').innerHTML));
  check('exact: no raw markup leaks',!/\{\{/.test(foe.textContent));
  dom.window.close();

  // effects must also appear when health is only a word
  DATA.hostiles=[{name:'chrome pyramid',level:9,rating:'{{r|Very Tough}}',distance:5,dir:'S',
    exact:false,health:'{{o|Wounded}}',
    effects:[{name:'{{m|glotrot}}',negative:true,disease:true}]}];
  writeData(0,2);
  ({dom,errs,d}=await load());
  foe=d.getElementById('foes');
  check('worded: effects shown too',/glotrot/.test(foe.textContent));
  check('worded: disease marked as such',foe.querySelectorAll('.foefx .fx.dis').length===1);
  check('worded: still no numbers or bar',
        !/\d+\/\d+/.test(foe.textContent)&&foe.querySelectorAll('.bar').length===0);
  check('worded: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // a hostile with no effects must not render an empty block
  DATA.hostiles=[{name:'snapjaw',level:1,rating:'',distance:2,dir:'N',exact:true,hp:9,hpMax:9}];
  writeData(0,3);
  ({dom,errs,d}=await load());
  check('no effects: no empty effects block',d.getElementById('foes').querySelectorAll('.foefx').length===0);
  check('no effects: full health reads 9/9',/9\/9/.test(d.getElementById('foes').textContent));
  dom.window.close();
  DATA.hostiles=[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',exact:true,hp:10,hpMax:12}];

  // --- player's own hit points follow the same rule (nerve poppy hides the numbers)
  // exactly what the mod sends for a nerve poppy (Analgesia) character: WoundLevel already
  // carries its own colour, and no figures are present at all
  DATA.player={name:'{{W|Tester}}',genotype:'True Kin',subtype:'Greybeard',level:7,xp:1500,
               xpNext:2000,xpPrev:1200,exact:false,health:'{{Y|Perfect}}'};
  writeData(0,1);
  ({dom,errs,d}=await load());
  let vit=d.getElementById('vitals');
  check('player word shown when numbers are hidden',/Perfect/.test(vit.textContent),JSON.stringify(vit.textContent));
  check('player: no hp numbers leak',!/\b\d+\s*\/\s*\d+/.test(vit.textContent),JSON.stringify(vit.textContent));
  check('player: no hp bar drawn',vit.querySelectorAll('.bar').length===0);
  check('player word is coloured',/<span style="color:/.test(vit.querySelector('.hpword').innerHTML));
  check('player: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // and the numbers come back when the character can read them
  DATA.player={name:'{{W|Tester}}',genotype:'True Kin',subtype:'Greybeard',level:7,xp:1500,
               xpNext:2000,xpPrev:1200,exact:true,hp:30,hpMax:60};
  writeData(0,2);
  ({dom,errs,d}=await load());
  vit=d.getElementById('vitals');
  check('player numbers shown when readable',/30/.test(vit.textContent)&&/60/.test(vit.textContent));
  check('player hp bar drawn',vit.querySelectorAll('.bar').length===1);
  dom.window.close();

  // --- hostile health: word by default, bar only with a scanner
  DATA.hostiles=[
    {name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',exact:false,health:'{{o|Wounded}}'},
    {name:'chrome pyramid',level:9,rating:'{{r|Very Tough}}',distance:5,dir:'S',exact:false,health:'{{G|Perfect}}'}
  ];
  writeData(0,1);
  ({dom,errs,d}=await load());
  let foes=d.getElementById('foes');
  check('no scanner: health word is shown',/Wounded/.test(foes.textContent));
  check('no scanner: second word shown',/Perfect/.test(foes.textContent));
  check('no scanner: NO health bar is drawn',foes.querySelectorAll('.bar.hp').length===0,
        foes.querySelectorAll('.bar.hp').length+' bars');
  check('no scanner: word is coloured by qud markup',
        /<span style="color:/.test(foes.querySelector('.hpw').innerHTML));
  check('no scanner: no raw markup leaks through',!/\{\{/.test(foes.textContent));
  check('no scanner: page reports no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // with a scanner the mod sends real numbers and the bar comes back
  DATA.hostiles=[
    {name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',exact:true,hp:4,hpMax:12},
    {name:'chrome pyramid',level:9,rating:'{{r|Very Tough}}',distance:5,dir:'S',exact:true,hp:60,hpMax:60}
  ];
  writeData(0,2);
  ({dom,errs,d}=await load());
  foes=d.getElementById('foes');
  check('scanner: bars are drawn',foes.querySelectorAll('.bar.hp').length===2,
        foes.querySelectorAll('.bar.hp').length+' bars');
  check('scanner: no health word shown',foes.querySelectorAll('.hpw').length===0);
  check('scanner: bar width reflects exact hp',
        /width:33.3%/.test(foes.querySelector('.bar.hp i').getAttribute('style')),
        foes.querySelector('.bar.hp i').getAttribute('style'));
  dom.window.close();

  // a mixed list (bioscanner sees the creature but not the robot) must render both forms
  DATA.hostiles=[
    {name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',exact:true,hp:4,hpMax:12},
    {name:'chrome pyramid',level:9,rating:'{{r|Very Tough}}',distance:5,dir:'S',exact:false,health:'{{g|Fine}}'}
  ];
  writeData(0,3);
  ({dom,errs,d}=await load());
  foes=d.getElementById('foes');
  check('mixed: one bar and one word',
        foes.querySelectorAll('.bar.hp').length===1&&foes.querySelectorAll('.hpw').length===1,
        foes.querySelectorAll('.bar.hp').length+' bars, '+foes.querySelectorAll('.hpw').length+' words');
  check('mixed: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // a hostile with neither (older/odd payload) must not break the panel
  DATA.hostiles=[{name:'mystery',level:1,rating:'',distance:3,dir:'N',exact:false}];
  writeData(0,4);
  ({dom,errs,d}=await load());
  check('missing health data renders the name without crashing',
        /mystery/.test(d.getElementById('foes').textContent)&&errs.length===0,errs.join(' | '));
  dom.window.close();
  DATA.hostiles=[{name:'snapjaw scavenger',level:3,rating:'{{g|Easy}}',distance:1,dir:'NE',exact:true,hp:10,hpMax:12}];

  // --- stale dimming, panel hiding, fault reporting
  writeData(0,1);
  ({dom,errs,d}=await load());
  check('fresh data is not dimmed',!d.body.classList.contains('stale'));
  const panelCount=d.querySelectorAll('.panel[data-panel]').length;
  check('every panel has a hide button',d.querySelectorAll('.hide').length===panelCount,
        d.querySelectorAll('.hide').length+' of '+panelCount);
  check('Options lists every panel',d.querySelectorAll('[data-panel-tog]').length===panelCount,
        d.querySelectorAll('[data-panel-tog]').length+' of '+panelCount);
  check('water toggle untouched by panel toggles',
        d.querySelector('.tog[data-opt=waterWarn]').getAttribute('aria-checked')==='true');
  check('no panel hidden by default',d.querySelectorAll('.panel.off').length===0);
  // hide the Gear panel via its x button
  d.querySelector('[data-panel=gear] .hide').click();
  check('x button hides its panel',d.querySelector('[data-panel=gear]').classList.contains('off'));
  check('hidden panel is persisted',
        JSON.parse(dom.window.localStorage.getItem('qudhud.hidden')||'[]').indexOf('gear')>=0);
  check('Options toggle reflects the hidden panel',
        d.querySelector('[data-panel-tog=gear]').getAttribute('aria-checked')==='false');
  // bring it back from Options
  d.querySelector('[data-panel-tog=gear]').click();
  check('Options toggle restores the panel',!d.querySelector('[data-panel=gear]').classList.contains('off'));
  check('restore clears it from storage',
        JSON.parse(dom.window.localStorage.getItem('qudhud.hidden')||'[]').indexOf('gear')<0);
  dom.window.close();

  // hidden panels must be restored from storage on load, and must not hold a column open.
  // Hide everything that starts in the first column, whatever that currently is.
  ({dom,errs,d}=await load());
  const firstCol=[].slice.call(d.querySelectorAll('[data-zone=c1] > .panel[data-panel]')).map(n=>n.dataset.panel);
  dom.window.close();
  ({dom,errs,d}=await load({store:{'qudhud.hidden':JSON.stringify(firstCol)}}));
  check('hidden panels restored from storage',d.querySelectorAll('.panel.off').length===firstCol.length,
        d.querySelectorAll('.panel.off').length+' hidden');
  check('a column of only hidden panels collapses',
        d.querySelector('[data-zone=c1]').classList.contains('vacant'));
  check('hidden panels load without errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // stale data must dim the readings
  writeData(3*3600,1);
  ({dom,errs,d}=await load());
  check('stale data dims the page',d.body.classList.contains('stale'));
  dom.window.close();

  // a panel that throws must be named in the footer and logged only once
  writeData(0,1);
  ({dom,errs,d}=await load());
  const w=dom.window;
  let logged=0;
  w.console.error=()=>{logged++;};
  w.__qudhud.render({version:VER,seq:99,stamp:'12:00:00',
    data:{...DATA,attributes:{broken:true}}});   // attributes must be an array; .map will throw
  check('broken panel is named in the footer',/Attributes/.test(d.getElementById('broke').textContent),
        JSON.stringify(d.getElementById('broke').textContent));
  check('footer marker is visible',!d.getElementById('broke').hidden);
  check('fault logged once',logged===1,logged+' logs');
  for(let i=0;i<5;i++)w.__qudhud.render({version:VER,seq:100+i,stamp:'12:00:00',
    data:{...DATA,attributes:{broken:true}}});
  check('repeat faults are not logged again',logged===1,logged+' logs after 6 renders');
  w.__qudhud.render({version:VER,seq:200,stamp:'12:00:00',data:DATA});  // healthy payload
  check('recovered panel clears the footer',d.getElementById('broke').hidden);
  check('other panels still drew while one was broken',/Tester/.test(d.getElementById('who').textContent));
  dom.window.close();

  // --- a Flatpak browser opens the page through the document portal, which hands over this one
  // file and not its folder, so hud_data.js never loads. The page must say so, not wait forever.
  writeData(0,1);
  ({dom,errs,d}=await load());
  check('ordinary address: no sandbox notice',d.getElementById('sandboxed').hidden);
  dom.window.close();
  ({dom,errs,d}=await load({path:'/run/user/1000/doc/8f3a2c1d/hud.html'}));
  check('portal address: still waiting, data unreachable',d.body.classList.contains('waiting'));
  check('portal address: sandbox notice shown',!d.getElementById('sandboxed').hidden);
  check('portal address: notice gives the override command',
        /flatpak override --user --filesystem=~\/QudHUD:ro/.test(d.getElementById('sandboxed').textContent));
  check('portal address: footer names the cause',/sandboxed/.test(d.getElementById('link').textContent),
        JSON.stringify(d.getElementById('link').textContent));
  errs=errs.filter(e=>!/Could not load script: .*hud_data\.js/.test(e));   // that failure is the point
  check('portal address: no other errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  // --- storage paths: a saved layout must be reapplied, and options must persist
  writeData(0,1);
  ({dom,errs,d}=await load({store:{'qudhud.layout':JSON.stringify({top:[],c1:['gear'],c2:[],c3:['warn','foes']}),
                                   'qudhud.opts':JSON.stringify({waterWarn:false,waterMin:25}),
                                   'qudhud.scale':'1.5'}}));
  check('saved layout: gear moved to column 1',
        d.querySelector('[data-zone=c1]').querySelector('[data-panel=gear]')!==null);
  check('saved layout: warn moved to column 3',
        d.querySelector('[data-zone=c3]').querySelector('[data-panel=warn]')!==null);
  check('saved scale applied',d.documentElement.style.getPropertyValue('--scale')==='1.5',
        JSON.stringify(d.documentElement.style.getPropertyValue('--scale')));
  check('saved opts applied to toggle',
        d.querySelector('.tog[data-opt=waterWarn]').getAttribute('aria-checked')==='false');
  check('water warning suppressed when opted out',
        !/Fresh water low/.test(d.getElementById('warnBody').textContent));
  check('storage load: no errors',errs.length===0,errs.join(' | '));
  dom.window.close();

  server.close();
  console.log(fails?('\n'+fails+' FAILURES'):'\nall green');
  process.exit(fails||process.exitCode?1:0);
})();
