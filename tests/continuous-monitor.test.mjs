import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {renderContinuousMarkets,renderBrokerExecutions,mountContinuousMonitor} from '../web/continuous-monitor.js';

const flush=()=>new Promise(resolve=>setImmediate(resolve));
function fixture(){
  const now=new Date().toISOString();
  return {settings:{enabled:false,instruments:[{symbol:'/MESZ26',pointValue:5,tickSize:.25}],pollSeconds:15,aiCooldownSeconds:90,reviewSeconds:300,dailyProfitTarget:1000,revision:'fixture'},markets:[{symbol:'/MESZ26',exposure:{symbol:'/MESZ26',position:{state:'FLAT',quantity:null,averageEntry:null},version:2,scopeId:'fixture',confirmedAt:now,stop:null,target:null,expiresAt:null,exitRequired:false},quote:{last:6000,quoteTime:now,realtime:true},currentDecision:{action:'WAIT',reason:'Watching'},latest:null}],memory:[],message:'Fixture observations'};
}
test('continuous cards expire cached calls, suppress stale quotes, preserve exits and escape all text',()=>{
  const state=fixture(),m=state.markets[0],now=Date.now();
  m.currentDecision={action:'BUY',reason:'NOW: fixture',entry:6000,stop:5995,target:6010};m.latest={validUntil:new Date(now+60000).toISOString()};m.awaitingExecution=true;
  let html=renderContinuousMarkets(state,now);assert.match(html,/>BUY</);assert.match(html,/Waiting for broker execution fills/);assert.match(html,/Entry/);
  html=renderContinuousMarkets(state,now+31000);assert.doesNotMatch(html,/>BUY</);assert.match(html,/Fresh realtime quote unavailable/);
  m.quote.quoteTime=new Date(now+61000).toISOString();html=renderContinuousMarkets(state,now+61000);assert.match(html,/Call expired/);assert.doesNotMatch(html,/>BUY</);
  m.exposure.exitRequired=true;m.currentDecision={action:'SELL_CLOSE',reason:'Stop breached <script>'};m.error='<img src=x>';m.symbol='/MESZ26<svg>';
  html=renderContinuousMarkets(state,now+3600000);assert.match(html,/EXIT REQUIRED/);assert.match(html,/>SELL_CLOSE</);assert.doesNotMatch(html,/<script>|<img|<svg>/);
  assert.match(renderContinuousMarkets(null),/Discovering active futures products automatically/);
});

test('monitor controls persist explicit settings and versioned actual positions without polling over edits',async()=>{
  const {window,document}=parseHTML('<html><body><section id="market"><div id="market-panels"></div></section></body></html>');
  const prototype=Object.getPrototypeOf(document.createElement('select')),descriptor=Object.getOwnPropertyDescriptor(prototype,'value');
  Object.defineProperty(prototype,'value',{...descriptor,set(value){for(const option of this.querySelectorAll('option')){if(option.value===value)option.setAttribute('selected','');else option.removeAttribute('selected');}}});
  const originals=Object.fromEntries(['window','document','fetch','setTimeout','clearTimeout'].map(key=>[key,globalThis[key]]));
  let state=fixture(),timer;const calls=[];
  Object.assign(globalThis,{window,document,setTimeout:fn=>{timer=fn;return 1;},clearTimeout:()=>{},fetch:async(url,options)=>{
    const path=new URL(url).pathname,body=options.body?JSON.parse(options.body):null;calls.push({path,body});
    if(path.endsWith('/settings'))state={...state,settings:{...body,revision:'saved'}};
    if(path.endsWith('/position')){const m=state.markets[0];m.exposure={...m.exposure,position:body.position,version:body.version+1,stop:body.stop,target:body.target,expiresAt:body.expiresAt};}
    return {ok:true,json:async()=>structuredClone(state)};
  }});
  try{
    const products=[];window.addEventListener('trader-products',event=>products.push(event.detail));
    const panel=mountContinuousMonitor();await flush();
    assert.equal(panel.querySelector('.monitor-add'),null,'products never require manual contract entry');
    assert.equal(panel.querySelector('[data-remove]'),null,'the product list is maintained by the trader');
    const field=name=>panel.querySelector(`.continuous-position [name="${name}"]`);
    assert.equal(panel.querySelector('.monitor-toggle').disabled,false);
    panel.querySelector('.monitor-toggle').click();await flush();
    assert.deepEqual(calls.find(c=>c.path.endsWith('/settings')).body,{revision:'fixture',enabled:true,instruments:[{symbol:'/MESZ26',pointValue:5,tickSize:.25}],pollSeconds:15,aiCooldownSeconds:90,reviewSeconds:300,dailyProfitTarget:1000,brokerAccountId:null});
    panel.querySelector('.confirm-market').click();field('side').value='LONG';field('side').dispatchEvent(new window.Event('change'));
    field('quantity').value='2';field('entry').value='6000';field('stop').value='5990';field('target').value='6020';field('minutes').value='10';
    await timer();assert.equal(field('stop').value,'5990','background reads preserve the active position form');
    panel.querySelector('.continuous-position').dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();
    const submitted=calls.find(c=>c.path.endsWith('/position')).body;
    assert.equal(submitted.version,2);assert.deepEqual(submitted.position,{state:'LONG',quantity:2,averageEntry:6000});assert.equal(submitted.stop,5990);assert.equal(submitted.target,6020);
    assert.ok(Date.parse(submitted.expiresAt)>Date.now()+590000);assert.equal(field('minutes').disabled,true);
    field('stop').value='5995';panel.querySelector('.continuous-position').dispatchEvent(new window.Event('submit',{cancelable:true}));await flush();
    const second=calls.filter(c=>c.path.endsWith('/position')).at(-1).body;
    assert.equal(second.version,3);assert.equal(second.expiresAt,submitted.expiresAt,'reconfirmation preserves a committed expiry instead of extending it');
    assert.match(panel.querySelector('.monitor-feedback').textContent,/Position recorded/);
    state.lastProductChoice={symbol:'/MESZ26',reason:'Fixture comparison'};await timer();
    assert.equal(products.at(-1).selected,'/MESZ26');assert.equal(products.at(-1).position.state,'LONG');
    state.markets[0].exposure.position={state:'FLAT',quantity:null,averageEntry:null};state.markets[0].exposure.version++;await timer();
    assert.equal(products.at(-1).position.state,'FLAT','the selected chart receives a changed position without requiring a product switch');
  }finally{window.dispatchEvent(new window.Event('pagehide'));Object.assign(globalThis,originals);Object.defineProperty(prototype,'value',descriptor);}
});

test('broker execution history shows signed actual fills with account and sync provenance',()=>{
  const now=new Date().toISOString();
  const report={status:'ready',message:'Following transactions',lastSynced:now,history:{accounts:[{id:'opaque',label:'Account 1',fills:[{id:'one',order:'123',symbol:'/MESZ26',time:now,amount:2,price:6000.25},{id:'two',order:'124',symbol:'/MESZ26',time:now,amount:-1,price:6002}]}]}};
  const html=renderBrokerExecutions(report);assert.match(html,/BUY/);assert.match(html,/SELL/);assert.match(html,/6,000.25/);assert.match(html,/Account 1/);assert.match(html,/Last synced/);
  report.history.accounts[0].label='<script>';report.history.accounts[0].fills[0].order='<img>';report.status='failed';
  assert.doesNotMatch(renderBrokerExecutions(report),/<script>|<img>/);assert.match(renderBrokerExecutions(report),/Historical results/);
  assert.match(renderBrokerExecutions(null),/No futures transaction fills returned/);
});
