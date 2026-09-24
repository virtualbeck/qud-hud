// Runs every suite and fails if any does. From this folder: npm install, then npm test.
const {spawnSync}=require('child_process'),path=require('path');

const failed=[];
function run(label,cmd,args){
  console.log('\n== '+label+' ==');
  const r=spawnSync(cmd,args,{stdio:'inherit'});
  if(r.status!==0)failed.push(label);
}

for(const f of ['link.test.js','page.test.js','rebuild.test.js'])
  run(f,process.execPath,[path.join(__dirname,f)]);

// build.py is Python. Windows usually calls the interpreter python or py, elsewhere python3.
const py=['python3','python','py'].find(c=>{
  try{return spawnSync(c,['--version']).status===0;}catch(e){return false;}
});
if(py)run('test_build.py',py,[path.join(__dirname,'test_build.py')]);
else{console.log('\n== test_build.py ==\nFAIL  no Python on PATH');failed.push('test_build.py');}

console.log(failed.length?'\nFAILED: '+failed.join(', '):'\nall suites passed');
process.exit(failed.length?1:0);
