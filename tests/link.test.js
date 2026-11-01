const fs=require('fs');
const path=require('path');
const html=fs.readFileSync(path.join(__dirname,'..','src','hud.html'),'utf8');
const src=html.match(/<script>([\s\S]*?)<\/script>/)[1];

// pull the real functions out of the page source so we test shipped code, not a copy
const grab=n=>{
  const i=src.indexOf('function '+n+'(');
  if(i<0)throw new Error('not found: '+n);
  let d=0,j=src.indexOf('{',i);
  for(let k=j;k<src.length;k++){
    if(src[k]==='{')d++;
    else if(src[k]==='}'){d--;if(d===0)return src.slice(i,k+1);}
  }
};
const code=grab('payloadAge')+'\n'+grab('ageText');

const NOW=Date.UTC(2026,0,1,12,0,0);
const FakeDate={now:()=>NOW};
const fn=new Function('Date','Math','isFinite',code+';return {payloadAge,ageText};')(FakeDate,Math,isFinite);

let fails=0;
const eq=(label,got,want)=>{
  const ok=got===want;
  if(!ok)fails++;
  console.log((ok?'PASS':'FAIL')+'  '+label+'  got='+got+' want='+want);
};

const STALE=2*60*1000;

eq('same instant -> 0ms',           fn.payloadAge(NOW), 0);
eq('30s ago',                       fn.payloadAge(NOW-30000), 30000);
eq('2min ago is stale',             fn.payloadAge(NOW-120000)>=STALE, true);
eq('90s ago is NOT stale',          fn.payloadAge(NOW-90000)>=STALE, false);
eq('3h ago',                        fn.payloadAge(NOW-3*3600000), 3*3600000);
// game clock slightly ahead of the browser's must not read as negative age
eq('future stamp clamps to 0',      fn.payloadAge(NOW+5000), 0);

// missing / malformed values must degrade to "unknown" (null), never NaN
eq('null',                          fn.payloadAge(null), null);
eq('undefined',                     fn.payloadAge(undefined), null);
eq('string',                        fn.payloadAge('12:00:00'), null);
eq('NaN',                           fn.payloadAge(NaN), null);

eq('ageText 2m',                    fn.ageText(120000), '2m');
eq('ageText 59m',                   fn.ageText(59*60000), '59m');
eq('ageText 60m',                   fn.ageText(60*60000), '1h 0m');
eq('ageText 90m',                   fn.ageText(90*60000), '1h 30m');
eq('ageText 3h5m',                  fn.ageText((3*60+5)*60000), '3h 5m');

console.log(fails?('\n'+fails+' FAILURES'):'\nall green');
process.exit(fails?1:0);
