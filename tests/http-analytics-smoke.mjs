// Run against an isolated validation server, never the personal journal.
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parseCsv} from '../web/csv.js';
const base='http://127.0.0.1:5174',account='Analytics '+crypto.randomUUID();
const request={fileName:'executions.csv',csv:await readFile(new URL('./fixtures/executions.csv',import.meta.url),'utf8'),columns:{executionId:0,timestamp:1,symbol:2,side:3,quantity:4,price:5,fees:6,assetType:7,multiplier:8},account,assetType:'Futures',multiplier:5};
const post=async(path,body)=>{const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});assert.equal(response.status,200);return response.json();};
const preview=await post('/api/imports/preview',request);assert.deepEqual(preview.errors,[]);await post('/api/imports/commit',{token:preview.token});
const query=new URLSearchParams({account});
const summary=await (await fetch(base+'/api/analytics?'+query)).json();assert.equal(summary.count,2);assert.equal(summary.net,77);assert.equal(summary.fees,8);assert.equal(summary.riskCount,0);assert.equal(summary.averageR,null);assert.equal(summary.profitFactorIsInfinite,true);
for(const groups of [summary.daily,summary.instruments])assert.equal(groups.reduce((n,g)=>n+g.net,0),summary.net);
assert.equal(summary.distribution.reduce((n,g)=>n+g.count,0),summary.count);
const exported=await fetch(base+'/api/trades/export?'+query);assert.match(exported.headers.get('content-disposition'),/trades-filtered.csv/);const rows=parseCsv(await exported.text());assert.equal(rows.length,3);assert.equal(rows.slice(1).reduce((n,r)=>n+Number(r[11]),0),summary.net);
const t=preview.trades[0].trade;query.set('start',t.date);query.set('end',t.date);query.set('direction',t.direction);query.set('symbol',t.symbol);
const filtered=await (await fetch(base+'/api/analytics?'+query)).json();const filteredRows=parseCsv(await (await fetch(base+'/api/trades/export?'+query)).text());assert.equal(filteredRows.length-1,filtered.count);assert.equal(filteredRows.slice(1).reduce((n,r)=>n+Number(r[11]),0),filtered.net);
assert.equal((await fetch(base+'/api/analytics?start=2026-10-07&end=2026-10-01')).status,400);assert.equal((await fetch(base+'/api/trades/export?start=invalid')).status,400);
for(const asset of ['/reports.js','/reports.css'])assert.equal((await fetch(base+asset)).status,200);
console.log('PASS HTTP filtered analytics, matching CSV totals, missing risk, breakdown conservation and invalid-range rejection');
