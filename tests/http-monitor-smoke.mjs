// Run against a fresh, isolated app instance. No credentials or inference required.
import assert from 'node:assert/strict';
const base=process.argv[2]??'http://127.0.0.1:5193';
const post=(path,body,origin)=>fetch(base+'/api/ai/monitor'+path,{method:'POST',headers:{'Content-Type':'application/json',...(origin?{Origin:origin}:{})},body:JSON.stringify(body)});
let response=await fetch(base+'/api/ai/monitor');assert.equal(response.status,200);assert.equal(response.headers.get('cache-control'),'no-store');
const initial=await response.json();assert.equal(initial.settings.enabled,false);assert.equal(initial.markets.length,0);
const settings={revision:initial.settings.revision,enabled:false,instruments:[{symbol:'/MESZ26',pointValue:5,tickSize:.25}],pollSeconds:15,aiCooldownSeconds:90,reviewSeconds:300};
response=await post('/settings',settings);assert.equal(response.status,200);const configured=await response.json();assert.equal(configured.markets[0].exposure.position.state,'UNKNOWN');
assert.equal((await post('/settings',settings)).status,400);
assert.equal((await post('/settings',{...settings,revision:configured.settings.revision,instruments:[{symbol:'/MES',pointValue:5,tickSize:.25}]})).status,400);
assert.equal((await post('/position',{symbol:'/MESZ26',version:0,position:{state:'FLAT'}},'https://example.invalid')).status,403);
response=await post('/position',{symbol:'/MESZ26',version:0,position:{state:'LONG',quantity:1,averageEntry:6000},stop:5990,target:6020});assert.equal(response.status,200);
const confirmed=await response.json();assert.equal(confirmed.markets[0].exposure.version,1);assert.equal(confirmed.markets[0].currentDecision.action,'WAIT');
assert.equal((await post('/position',{symbol:'/MESZ26',version:1,position:{state:'LONG',quantity:1,averageEntry:6000},stop:5980})).status,400);
assert.equal((await post('/position',{symbol:'/MESZ26',version:1,position:{state:'UNKNOWN'}})).status,400);
assert.equal((await post('/settings',{...settings,revision:configured.settings.revision,instruments:[]})).status,400);
assert.equal((await fetch(base+'/continuous-monitor.js')).status,200);
assert.deepEqual(await (await fetch(base+'/api/ai/history')).json(),[]);
console.log('PASS monitor HTTP persistence, versioning, specification validation, stop protection, ingress/origin checks and assets without inference');
