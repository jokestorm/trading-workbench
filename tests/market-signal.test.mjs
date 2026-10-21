import test from 'node:test';
import assert from 'node:assert/strict';
import {renderMarketSignal} from '../web/market-signal.js';
const run={createdAt:'2026-10-14T14:00:00Z',snapshot:{symbol:'/MESZ26',pointValue:5,position:{state:'FLAT'}},result:{summary:'Long analysis',assessment:'Lengthy assessment',evidence:['Source https://example.invalid'],limitations:['Research unavailable'],nextSteps:['Long explanation'],invalidation:'Price breaks below 99',decision:{trend:'UP',action:'BUY',reason:'Buy above 100.',entry:100,stop:99,target:102}}};
test('market result shows a compact direction and entry plan without research or lengthy review sections',()=>{
 const html=renderMarketSignal(run);
 for(const expected of ['Trend: ↑ UP','BUY — open long','Your position: FLAT','Buy above 100.','Entry','Stop','Target','$5.00 / contract'])assert.ok(html.includes(expected),expected);
 for(const omitted of ['Lengthy assessment','Source','example.invalid','Research unavailable','Long explanation','What to watch'])assert.ok(!html.includes(omitted),omitted);
});
test('an exit signal refers to the position used and missing old trend values remain unclear',()=>{
 const html=renderMarketSignal({...run,snapshot:{...run.snapshot,position:{state:'LONG',quantity:2,averageEntry:100.25}},result:{...run.result,decision:{action:'SELL_CLOSE',reason:'Momentum turned down.'}}});
 assert.match(html,/Trend: UNCLEAR/);assert.match(html,/SELL — close your long/);assert.match(html,/LONG · 2 contracts @ 100.25/);
});
test('guarded WAIT suppresses entry levels and escapes model text',()=>{
 const html=renderMarketSignal({...run,result:{...run.result,decision:{...run.result.decision,action:'WAIT',guarded:true,trend:null,reason:'<img src=x onerror=alert(1)>'}}});
 assert.match(html,/Trend: UNCLEAR/);assert.match(html,/&lt;img/);assert.doesNotMatch(html,/<img|decision-levels|\$5.00/);
 assert.match(html,/Blocked by an app data or position check/);
});
test('model NEXT calls remain distinct from app-blocked WAIT results',()=>{
 const html=renderMarketSignal({...run,result:{...run.result,decision:{action:'WAIT',reason:'NEXT: Close below 100 within five minutes.',trend:'DOWN',guarded:false}}});
 assert.match(html,/NEXT: Close below 100/);assert.doesNotMatch(html,/Blocked by an app/);
});
