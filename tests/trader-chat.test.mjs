import test from 'node:test';
import assert from 'node:assert/strict';
import {renderTraderMessages,observeTrader} from '../web/trader-chat.js';

test('trader transcript escapes messages and identifies contract context and interrupted replies',()=>{
  const html=renderTraderMessages([{role:'user',text:'<script>bad()</script>',time:'2026-10-26T15:00:00Z',symbol:'/MESZ26',status:'completed'},{role:'assistant',text:'Saved',time:'2026-10-26T15:00:01Z',status:'interrupted'}]);
  assert.ok(html.includes('&lt;script&gt;'));assert.ok(!html.includes('<script>'));assert.ok(html.includes('/MESZ26'));assert.ok(html.includes('Reply interrupted'));assert.ok(renderTraderMessages([]).includes('Talk through the trade'));
});
test('trader observer only reads state and increases observation frequency while streaming',async()=>{
  let reads=0,callback,delay,shown=0;const observer=observeTrader({load:async()=>{reads++;return {busy:true};},onState:()=>shown++,schedule:(f,ms)=>{callback=f;delay=ms;return 1;},unschedule:()=>{}});
  await observer.update();assert.equal(reads,1);assert.equal(shown,1);assert.equal(delay,700);await callback();assert.equal(reads,2);observer.dispose();await callback();assert.equal(reads,2);
});
test('hidden trader views do not make requests and failed observation does not create a reply',async()=>{
  let visible=false,reads=0,errors=0;const observer=observeTrader({visible:()=>visible,load:async()=>{reads++;throw new Error('offline');},onState:()=>assert.fail(),onError:()=>errors++,schedule:()=>1,unschedule:()=>{}});
  await observer.update();assert.equal(reads,0);visible=true;await observer.update();assert.equal(reads,1);assert.equal(errors,1);observer.dispose();
});
