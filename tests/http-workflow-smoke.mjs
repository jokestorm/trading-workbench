// Run only against a fresh isolated instance with an unavailable Codex executable.
import assert from 'node:assert/strict';
const base=process.argv[2]??'http://127.0.0.1:5189';
const headers={'Content-Type':'application/json'};
const post=(path,body,extra={})=>fetch(base+path,{method:'POST',headers:{...headers,...extra},body:JSON.stringify(body)});
let response=await fetch(base+'/api/ai/runs/current');
assert.equal(response.status,200);assert.equal(response.headers.get('cache-control'),'no-store');assert.deepEqual(await response.json(),{run:null});
assert.equal((await post('/api/ai/runs',{kind:'journal'})).status,400);
const trade={symbol:'MESZ26',assetType:'Futures',openedAt:'2026-10-06T09:30',date:'2026-10-06',time:'10:00',direction:'LONG',quantity:1,entry:100,exit:101,multiplier:5,fees:0,notes:''};
assert.equal((await post('/api/trades',trade)).status,201);
response=await post('/api/ai/runs',{kind:'journal'});assert.equal(response.status,202);const admitted=await response.json();assert.equal(admitted.kind,'journal');
let run;
for(let i=0;i<100;i++){
 ({run}=await (await fetch(base+'/api/ai/runs/current')).json());
 if(run.state==='failed')break;
 await new Promise(resolve=>setTimeout(resolve,50));
}
assert.equal(run.id,admitted.id);assert.equal(run.state,'failed');
assert.equal((await post('/api/ai/runs',{kind:'journal'},{Origin:'https://example.invalid'})).status,403);
assert.equal((await post('/api/ai/runs/'+run.id+'/cancel',{})).status,200);
assert.equal((await post('/api/ai/analyze',{token:'bypass'})).status,404);
assert.deepEqual(await (await fetch(base+'/api/ai/history')).json(),[]);
assert.equal((await fetch(base+'/analysis-runs.js')).status,200);
console.log('PASS workflow HTTP admission, durable failure status, no-store, origin protection, no legacy bypass, and assets');
