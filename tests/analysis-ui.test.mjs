import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {mountAI} from '../web/ai.js';

const flush=()=>new Promise(resolve=>setImmediate(resolve));

test('market Analyze reads relocated position controls and submits the displayed contract',async()=>{
  const {window,document}=parseHTML(`<html><body>
    <section id="market"></section>
    <select id="monitor-symbol"><option value="/MESZ26" selected>MES</option><option value="/MGCZ26">MGC</option></select>
    <select id="monitor-source"><option value="bars" selected>Bars</option></select>
    <select id="monitor-days"><option value="1" selected>24 hours</option></select>
    <div id="monitor-analysis"></div><div id="monitor-analysis-action"></div><div id="monitor-position"></div>
    <div class="journal-toolbar"><div class="toolbar-actions"></div></div><article class="trade-panel"></article>
  </body></html>`);
  // LinkeDOM implements select.value's getter; supply the browser's setter.
  const selectPrototype=Object.getPrototypeOf(document.querySelector('select'));
  const descriptor=Object.getOwnPropertyDescriptor(selectPrototype,'value');
  Object.defineProperty(selectPrototype,'value',{...descriptor,set(value){for(const option of this.querySelectorAll('option'))option.removeAttribute('selected');this.querySelectorAll('option').forEach(option=>{if(option.value===value)option.setAttribute('selected','');});}});
  const originals=Object.fromEntries(['window','document','fetch','setTimeout','clearTimeout','MutationObserver'].map(key=>[key,globalThis[key]]));
  const calls=[],timers=new Map();let nextTimer=0,gatewayFailure=true;
  Object.assign(globalThis,{window,document,MutationObserver:window.MutationObserver,setTimeout:fn=>{timers.set(++nextTimer,fn);return nextTimer;},clearTimeout:id=>timers.delete(id),fetch:async(url,options)=>{
    const path=new URL(url).pathname,body=options?.body?JSON.parse(options.body):undefined;calls.push({path,body});
    if(path.endsWith('/current')&&gatewayFailure)return new Response('502: Bad Gateway',{status:502,headers:{'Content-Type':'text/plain'}});
    const data=path.endsWith('/positions/sync')?{status:'ready',lastSynced:new Date().toISOString(),positions:[{accountId:'fixture',accountLabel:'Account 1',symbol:'/MESZ26',state:'LONG',quantity:1,averageEntry:5999}]}:path.endsWith('/status')?{installed:true,subscriptionLogin:true,message:'Connected'}:path.endsWith('/login')?{state:'connected'}:path.endsWith('/history')?[]:path.endsWith('/current')?{run:null}:path.endsWith('/runs')?{id:'fixture',kind:body.kind,state:'failed',active:false,message:'Fixture complete'}:null;
    assert.notEqual(data,null,'unexpected API request '+path);return{ok:true,json:async()=>data};
  }});
  try{
    mountAI({getFilters:()=>({symbol:'MES'})});await flush();
    assert.match(document.querySelector('#market-signal-panel .analysis-message').textContent,/HTTP 502/,'a gateway response should expose the HTTP failure, not a JSON parser exception');
    gatewayFailure=false;
    const button=document.querySelector('#monitor-analysis-action .analyze');
    assert.equal(button.disabled,false);
    assert.equal(document.querySelector('#market-signal-panel .position-state'),null,'position controls really moved out of the panel');
    assert.doesNotThrow(()=>button.click(),'Analyze must work after controls are moved beside the chart');await flush();
    const submissions=()=>calls.filter(call=>call.path.endsWith('/runs'));
    assert.equal(submissions().length,1);
    assert.equal(submissions()[0].body.symbol,'/MESZ26');
    assert.equal(submissions()[0].body.position.state,'FLAT');
    const state=document.querySelector('#monitor-position .position-state');
    state.value='LONG';document.querySelector('.position-quantity').value='2';document.querySelector('.position-average').value='6000';
    state.dispatchEvent(new window.Event('change'));button.click();await flush();
    assert.deepEqual(submissions()[1].body.position,{state:'LONG',quantity:2,averageEntry:6000});
    const symbol=document.getElementById('monitor-symbol');symbol.value='/MGCZ26';symbol.dispatchEvent(new window.Event('change'));await flush();
    assert.equal(state.value,'FLAT');button.click();await flush();
    assert.equal(submissions()[2].body.symbol,'/MGCZ26');assert.equal(submissions()[2].body.position.state,'FLAT');
    symbol.value='/MESZ26';symbol.dispatchEvent(new window.Event('change'));await flush();assert.equal(state.value,'LONG');
    document.querySelector('.journal-analysis-actions .analyze').click();await flush();
    assert.equal(submissions()[3].body.kind,'journal');assert.deepEqual(submissions()[3].body.filters,{symbol:'MES'});
    document.querySelector('.broker-use').click();button.click();await flush();
    assert.deepEqual(submissions()[4].body.position,{state:'LONG',quantity:1,averageEntry:5999},'derived estimates enter analysis only after the user chooses Use');
  }finally{Object.assign(globalThis,originals);Object.defineProperty(selectPrototype,'value',descriptor);}
});
