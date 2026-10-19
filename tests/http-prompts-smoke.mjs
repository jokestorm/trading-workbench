// Starts its own isolated host; never touches the personal journal or invokes AI.
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdtemp} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';

const dll=resolve(process.argv[2]??'data/prompt-settings-final/TradingWorkbench.dll');
const directory=await mkdtemp(join(tmpdir(),'prompt-http-'));
let host;
async function start(){
  host=spawn('dotnet',[dll,'--urls','http://127.0.0.1:0','--DataDirectory',directory],{stdio:['ignore','pipe','pipe']});
  return new Promise((ready,reject)=>{
    const timer=setTimeout(()=>reject(new Error('Isolated host did not start')),20000);let output='';
    host.stdout.on('data',chunk=>{output+=chunk;const match=output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);if(match){clearTimeout(timer);ready(match[1]);}});
    host.once('error',error=>{clearTimeout(timer);reject(error);});
    host.once('exit',()=>{clearTimeout(timer);reject(new Error('Isolated host exited before startup'));});
  });
}
async function stop(){if(host&&!host.killed){const stopped=new Promise(resolve=>host.once('exit',resolve));host.kill();await stopped;}}
try{
  let base=await start();
  const all=await (await fetch(base+'/api/ai/prompts')).json();assert.equal(all.length,2);
  const market=all.find(p=>p.kind==='market');assert.equal(market.activeSource,'default');
  const put=await fetch(base+'/api/ai/prompts/market',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:'Synthetic custom market prompt',basedOnVersion:market.defaultVersion,revision:market.revision})});
  assert.equal(put.status,200);const saved=await put.json();assert.equal(saved.customText,'Synthetic custom market prompt');
  const stale=await fetch(base+'/api/ai/prompts/market/default',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({revision:market.revision})});assert.equal(stale.status,400);
  const crossOrigin=await fetch(base+'/api/ai/prompts/journal/default',{method:'POST',headers:{'Content-Type':'application/json',Origin:'https://other.invalid'},body:JSON.stringify({revision:'fixture'})});assert.equal(crossOrigin.status,403);
  await stop();base=await start();
  const persisted=(await (await fetch(base+'/api/ai/prompts')).json()).find(p=>p.kind==='market');assert.equal(persisted.customText,saved.customText);
  const reset=await fetch(base+'/api/ai/prompts/market/default',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({revision:persisted.revision})});assert.equal(reset.status,200);assert.equal((await reset.json()).customText,null);
  console.log('PASS isolated HTTP prompt save, stale-edit rejection, origin protection, restart persistence and default adoption');
}finally{await stop();}
