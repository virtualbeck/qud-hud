// Shared fixture: a hud_data.js payload as the mod writes it. "at" is UTC epoch ms, "stamp" is local time.
const pad=n=>String(n).padStart(2,'0');
function stampAgo(sec){
  const d=new Date(Date.now()-sec*1000);
  return pad(d.getHours())+':'+pad(d.getMinutes())+':'+pad(d.getSeconds());
}
const payload=(version,data,seq,ageSec)=>({version,seq,at:Date.now()-(ageSec||0)*1000,stamp:stampAgo(ageSec||0),data});
module.exports={stampAgo,payload};
