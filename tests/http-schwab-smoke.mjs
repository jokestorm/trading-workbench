// Requires the isolated server on port 5174. Uses fixture credentials; no Schwab network calls.
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const base='http://127.0.0.1:5174';
const post=async(path,body,origin)=>{const response=await fetch(base+'/api/schwab/'+path,{method:'POST',headers:{'Content-Type':'application/json',...(origin?{Origin:origin}:{})},body:JSON.stringify(body)});return{status:response.status,body:await response.json().catch(()=>null),cache:response.headers.get('cache-control')};};
const before=await (await fetch(base+'/api/trades')).json();
assert.equal((await post('settings',{appKey:'fixture-app-key',appSecret:'fixture-app-secret',callbackUrl:'https://example.com/'})).status,400);
const saved=await post('settings',{appKey:'fixture-app-key',appSecret:'fixture-app-secret',callbackUrl:'https://127.0.0.1:8182'});assert.equal(saved.status,200);assert.equal(saved.body.configured,true);assert.equal(saved.body.connected,false);assert.equal(saved.cache,'no-store');assert.ok(!JSON.stringify(saved.body).includes('fixture-app-secret'));
const authorized=await post('authorize',{});assert.equal(authorized.status,200);const url=new URL(authorized.body.url);assert.equal(url.hostname,'api.schwabapi.com');assert.equal(url.searchParams.get('redirect_uri'),'https://127.0.0.1:8182');assert.ok(url.searchParams.get('state'));
assert.equal((await post('callback',{url:'https://127.0.0.1:8182/?code=fixture&state=wrong'})).status,400);
assert.equal((await post('quote',{symbol:'AAPL'})).status,400);
assert.equal((await post('authorize',{},'https://example.com')).status,403);
assert.equal((await post('disconnect',{})).status,200);
const vault=await readFile('data/schwab-http-validation/secrets/schwab.protected','utf8');assert.ok(!vault.includes('fixture-app-secret'));
assert.deepEqual(await (await fetch(base+'/api/trades')).json(),before);
for(const asset of ['/market.js','/market.css'])assert.equal((await fetch(base+asset)).status,200);
console.log('PASS HTTP encrypted settings, authorization URL, state rejection, same-origin protection, disconnected quotes and journal isolation');
