import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {renderAccountSource,renderAccountPositions,observeAccountDashboard,mountAccountSync} from '../web/account-dashboard.js';
const flush=()=>new Promise(resolve=>setImmediate(resolve));
test('account source labels transaction coverage and open cycles without CSV accounting claims',()=>{
 const html=renderAccountSource({status:'ready',syncedAt:'2026-10-26T14:00:00Z',historyFrom:'2026-08-28T14:00:00Z',historyThrough:'2026-10-26T14:00:00Z',fillCount:104,trades:[{},{}]});
 assert.match(html,/Schwab transactions/);assert.match(html,/104 fills/);assert.match(html,/2 closed trades/);assert.match(html,/8\/28\/2026/);
 assert.match(renderAccountSource({status:'failed',message:'<script>',syncedAt:'2026-10-26T14:00:00Z'}),/last transaction-derived dashboard/);assert.doesNotMatch(renderAccountSource({status:'failed',message:'<script>'}),/<script>/);
 const positions=renderAccountPositions([{account:'Account 1',symbol:'MGCZ26',quantity:-2,averageEntry:2000,entryFees:3}]);
 assert.match(positions,/SHORT/);assert.match(positions,/Open futures cycles/);assert.doesNotMatch(positions,/Historical CSV/);
});
test('account observation updates changed transactions while preserving filters and chart state on unchanged polls',async()=>{
 let state={revision:'one',trades:[{source:'Schwab API'}]},timer,changes=0,statuses=0,errors=0,reads=0;
 const observer=observeAccountDashboard({load:async()=>{reads++;return state;},onChange:next=>{changes++;assert.ok(next.trades.every(t=>t.source==='Schwab API'));},onStatus:()=>statuses++,onError:()=>errors++,schedule:fn=>{timer=fn;return 1;},unschedule:()=>{}});
 await observer.update();await timer();assert.equal(changes,1);assert.equal(statuses,2);
 state={revision:'two',trades:[{source:'Schwab API'},{source:'Schwab API'}]};await timer();assert.equal(changes,2);
 observer.dispose();await timer();assert.equal(reads,3);assert.equal(errors,0);
});
test('account refresh invokes the transaction dashboard endpoint and does not report failed accounting as success',async()=>{
 const {document}=parseHTML('<html><body><div class="journal-toolbar"><div class="toolbar-actions"></div></div></body></html>');
 const previous={document:globalThis.document,fetch:globalThis.fetch};let refreshed=0,failed=false;
 Object.assign(globalThis,{document,fetch:async(url,options)=>{assert.ok(url.endsWith('/api/account/sync'));assert.equal(options.method,'POST');return{ok:true,json:async()=>failed?{status:'failed',message:'Transaction fees missing'}:{status:'ready'}};}});
 try{mountAccountSync({onSynced:()=>refreshed++});const button=document.getElementById('sync-account-dashboard');button.click();await flush();assert.equal(refreshed,1);failed=true;button.click();await flush();assert.equal(refreshed,1);assert.match(document.querySelector('[role="status"]').textContent,/Transaction fees missing/);}
 finally{Object.assign(globalThis,previous);}
});
