import test from 'node:test';
import assert from 'node:assert/strict';
import {renderMarketBrief,observeMarketBrief} from '../web/market-brief.js';

test('living market brief renders one compact summary with independent checked and changed timestamps',()=>{
  const html=renderMarketBrief({content:{outlook:'Mixed',points:['Stocks higher','Rates steady','Oil lower'],nextEvent:'Fed remarks at 1 PM ET'},lastChecked:'2026-10-21T14:00:00Z',lastChanged:'2026-10-21T13:00:00Z',message:'Checked; no material changes.'});
  for(const text of ['Markets & commodities','Mixed','Stocks higher','Watch next:','Last checked:','Last changed:','no material changes'])assert.ok(html.includes(text));
  assert.equal((html.match(/<li>/g)??[]).length,3);
  assert.match(renderMarketBrief(null),/first scheduled check/);
  assert.doesNotMatch(renderMarketBrief({content:{outlook:'<img>',points:['<script>'],nextEvent:'<iframe>'},message:'<svg>'}),/<img>|<script>|<iframe>|<svg>/);
});
test('brief observation reads stored state only while viewed and aborts hidden requests',async()=>{
  let visible=true,reads=0,rendered=0,timer,activeSignal,finish;
  const observer=observeMarketBrief({visible:()=>visible,load:signal=>{reads++;activeSignal=signal;return new Promise(resolve=>finish=resolve);},render:()=>rendered++,onError:()=>assert.fail('aborted request'),schedule:(fn,ms)=>{assert.equal(ms,30000);timer=fn;return 1;},unschedule:()=>{}});
  observer.update();observer.update();assert.equal(reads,1);
  visible=false;observer.update();assert.equal(activeSignal.aborted,true);finish({});await new Promise(resolve=>setImmediate(resolve));assert.equal(rendered,0);
  await timer();assert.equal(reads,1);
  visible=true;observer.update();assert.equal(reads,2);finish({});await new Promise(resolve=>setImmediate(resolve));assert.equal(rendered,1);
  observer.dispose();
});
