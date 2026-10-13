// Run against an isolated database, never against the personal journal.
import assert from 'node:assert/strict';
const base='http://127.0.0.1:5174';
const input={symbol:'MESZ26',assetType:'Futures',openedAt:'2026-10-06T09:30',date:'2026-10-06',time:'10:00',direction:'LONG',quantity:2,entry:6000,exit:6004,multiplier:5,fees:3.24,risk:null,setup:'Pullback',notes:'API fixture'};
const headers={'Content-Type':'application/json'};
for(const asset of ['/','/app.js','/journal.css'])assert.equal((await fetch(base+asset)).status,200);
const created=await fetch(base+'/api/trades',{method:'POST',headers,body:JSON.stringify(input)});assert.equal(created.status,201);
const trade=await created.json();assert.equal(trade.netPnl,36.76);assert.equal(trade.rMultiple,null);
const edited=await fetch(base+'/api/trades/'+trade.id,{method:'PUT',headers,body:JSON.stringify({...input,exit:6005,notes:'Changed'})});assert.equal(edited.status,200);assert.equal((await edited.json()).netPnl,46.76);
const listed=await (await fetch(base+'/api/trades')).json();assert.equal(listed.find(t=>t.id===trade.id).notes,'Changed');
assert.equal((await fetch(base+'/api/trades',{method:'POST',headers,body:JSON.stringify({...input,quantity:-2})})).status,400);
assert.equal((await fetch(base+'/api/trades',{method:'POST',headers:{...headers,Origin:'https://example.com'},body:JSON.stringify(input)})).status,403);
const backup=await fetch(base+'/api/backup');assert.equal(backup.status,200);assert.equal(Buffer.from(await backup.arrayBuffer()).subarray(0,15).toString(),'SQLite format 3');
console.log('PASS HTTP assets, create/edit/list, decimal P/L, missing risk, validation, origin protection, SQLite backup');
