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
const code=grab('stampAge')+'\n'+grab('ageText');

let fakeNow={h:12,m:0,s:0};
class FakeDate{ getHours(){return fakeNow.h} getMinutes(){return fakeNow.m} getSeconds(){return fakeNow.s} }
const ctx={Date:FakeDate,Math};
const fn=new Function('Date','Math',code+';return {stampAge,ageText};')(FakeDate,Math);

let fails=0;
const eq=(label,got,want)=>{
  const ok=got===want;
  if(!ok)fails++;
  console.log((ok?'PASS':'FAIL')+'  '+label+'  got='+got+' want='+want);
};

const STALE=2*60*1000;

fakeNow={h:12,m:0,s:0};
eq('same second -> 0ms',            fn.stampAge('12:00:00'), 0);
eq('30s ago',                       fn.stampAge('11:59:30'), 30000);
eq('2min ago == threshold',         fn.stampAge('11:58:00'), 120000);
eq('2min ago is stale',             fn.stampAge('11:58:00')>=STALE, true);
eq('90s ago is NOT stale',          fn.stampAge('11:58:30')>=STALE, false);
eq('1-digit hour parses',           fn.stampAge('9:00:00'), 3*3600*1000);

// midnight wrap: game wrote just before midnight, page now just after
fakeNow={h:0,m:0,s:5};
eq('midnight wrap -> 7s',           fn.stampAge('23:59:58'), 7000);
eq('midnight wrap not stale',       fn.stampAge('23:59:58')>=STALE, false);

// malformed / missing stamps must degrade to "unknown" (null), never NaN
fakeNow={h:12,m:0,s:0};
eq('null stamp',                    fn.stampAge(null), null);
eq('undefined stamp',               fn.stampAge(undefined), null);
eq('question mark stamp',           fn.stampAge('?'), null);
eq('empty stamp',                   fn.stampAge(''), null);
eq('garbage stamp',                 fn.stampAge('not a time'), null);
eq('partial stamp',                 fn.stampAge('12:00'), null);

eq('ageText 2m',                    fn.ageText(120000), '2m');
eq('ageText 59m',                   fn.ageText(59*60000), '59m');
eq('ageText 60m',                   fn.ageText(60*60000), '1h 0m');
eq('ageText 90m',                   fn.ageText(90*60000), '1h 30m');
eq('ageText 3h5m',                  fn.ageText((3*60+5)*60000), '3h 5m');

console.log(fails?('\n'+fails+' FAILURES'):'\nall green');
process.exit(fails?1:0);
