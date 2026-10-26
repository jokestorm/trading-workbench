import test from 'node:test';
import assert from 'node:assert/strict';
import {renderTraderMessages,observeTrader,mountTraderChat} from '../web/trader-chat.js';
import {parseHTML} from 'linkedom';

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

test('chat sends over ordinary HTTP without crypto.randomUUID and releases the disabled button',async()=>{
  const {window,document}=parseHTML('<html><body><nav aria-label="Main navigation"><button data-tab="settings">Settings</button></nav><main><footer>Workbench</footer></main></body></html>');
  const originals=Object.fromEntries(['window','document','fetch','setTimeout','clearTimeout','MutationObserver'].map(key=>[key,globalThis[key]]));
  const cryptoDescriptor=Object.getOwnPropertyDescriptor(globalThis,'crypto'),secureCrypto=globalThis.crypto;
  const sent=[];let gatewayFailure=false;
  Object.assign(globalThis,{window,document,MutationObserver:window.MutationObserver,setTimeout:()=>1,clearTimeout:()=>{},fetch:async(url,options)=>{
    if(new URL(url).pathname.endsWith('/messages')){
      sent.push(JSON.parse(options.body));assert.ok(options.signal instanceof AbortSignal,'requests must have a timeout signal');
      if(gatewayFailure)return new Response('502: Bad Gateway',{status:502});
    }
    return Response.json({busy:false,messages:[],model:'fixture',revision:1});
  }});
  Object.defineProperty(globalThis,'crypto',{configurable:true,value:{getRandomValues:secureCrypto.getRandomValues.bind(secureCrypto)}});
  const flush=()=>new Promise(resolve=>setImmediate(resolve));
  try{
    mountTraderChat();const input=document.getElementById('trader-input'),send=document.getElementById('trader-send'),form=document.getElementById('trader-form');
    input.value='Hey, what are you seeing?';form.dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();await flush();
    assert.equal(send.disabled,false,'Send must recover after submission');assert.equal(sent.length,1,'the message must reach the server over HTTP');
    assert.match(sent[0].clientId,/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/);
    input.value='Keep this draft';gatewayFailure=true;form.dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();await flush();
    assert.equal(send.disabled,false);assert.equal(input.value,'Keep this draft');assert.match(document.getElementById('trader-error').textContent,/HTTP 502/);
    gatewayFailure=false;form.dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();await flush();
    assert.equal(sent[1].clientId,sent[2].clientId,'a retry must retain the same deduplication id');assert.equal(send.disabled,false);
    globalThis.crypto.getRandomValues=()=>{throw new Error('Browser random source unavailable');};input.value='Keep this too';
    form.dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();await flush();
    assert.equal(send.disabled,false,'an error preparing a message must unlock Send');assert.equal(input.value,'Keep this too');assert.equal(sent.length,3);
  }finally{Object.assign(globalThis,originals);Object.defineProperty(globalThis,'crypto',cryptoDescriptor);}
});
