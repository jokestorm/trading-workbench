import test from 'node:test';
import assert from 'node:assert/strict';
import {deviceUrl,mountCodexLogin} from '../web/codex-login.js';

test('device links accept only the expected HTTPS OpenAI sign-in page',()=>{
  assert.equal(deviceUrl('https://auth.openai.com/codex/device'),'https://auth.openai.com/codex/device');
  for(const url of ['javascript:alert(1)','https://other.example/codex/device','http://auth.openai.com/codex/device','https://auth.openai.com/codex/device?token=fixture','https://auth.openai.com:444/codex/device'])assert.equal(deviceUrl(url),null);
});
class Element{
  constructor(){this.events={};this.hidden=false;this.disabled=false;this.value='';}
  addEventListener(name,fn){this.events[name]=fn;}
  removeAttribute(name){delete this[name];}
  select(){}
  async click(){return this.events.click?.();}
}
function container(){const elements=new Map();return {elements,querySelector(selector){if(!elements.has(selector))elements.set(selector,new Element());return elements.get(selector);}};}
const flush=()=>new Promise(resolve=>setImmediate(resolve));
test('both analysis panels share login progress, completion, sign-out and cancellation',async()=>{
  const original={document:globalThis.document,setTimeout:globalThis.setTimeout,clearTimeout:globalThis.clearTimeout};
  const timers=new Map();let timerId=0;
  globalThis.document={hidden:false,addEventListener(){}};
  globalThis.setTimeout=(fn)=>{timers.set(++timerId,fn);return timerId;};
  globalThis.clearTimeout=id=>timers.delete(id);
  try{
    const panels=[container(),container()],ready=[],calls=[];
    let status={installed:true,subscriptionLogin:false,message:'Sign in with ChatGPT.'},login={state:'idle',message:'Sign in with ChatGPT.'};
    const request=async(path)=>{
      calls.push(path);
      if(path==='ai/status')return status;
      if(path==='ai/login')return login;
      if(path==='ai/login/start')return login={state:'pending',message:'Enter the code.',verificationUrl:'https://auth.openai.com/codex/device',userCode:'ABCD-1234'};
      if(path==='ai/logout'){status={...status,subscriptionLogin:false};return login={state:'idle',message:'Signed out.'};}
      if(path==='ai/login/cancel')return login={state:'idle',message:'Canceled.'};
      throw new Error('Unexpected request');
    };
    mountCodexLogin(panels,{request,onReady:value=>ready.push(value)});await flush();
    assert.equal(ready.at(-1),false);
    await panels[0].querySelector('.codex-signin').click();await flush();
    for(const panel of panels){assert.equal(panel.querySelector('.codex-code').value,'ABCD-1234');assert.equal(panel.querySelector('.codex-device').hidden,false);assert.equal(panel.querySelector('.codex-cancel').hidden,false);}
    status={...status,subscriptionLogin:true,message:'ChatGPT connected.'};login={state:'connected',message:'ChatGPT connected.'};
    const [id,poll]=timers.entries().next().value;timers.delete(id);await poll();await flush();
    assert.equal(ready.at(-1),true);
    for(const panel of panels){assert.equal(panel.querySelector('.codex-code').value,'');assert.equal(panel.querySelector('.codex-device').hidden,true);assert.equal(panel.querySelector('.codex-signout').hidden,false);}
    await panels[1].querySelector('.codex-signout').click();await flush();assert.equal(ready.at(-1),false);
    await panels[1].querySelector('.codex-signin').click();await flush();
    await panels[0].querySelector('.codex-cancel').click();await flush();
    assert.equal(timers.size,0);assert.equal(ready.at(-1),false);
    assert.ok(calls.includes('ai/logout')&&calls.includes('ai/login/cancel'));
    for(const panel of panels)assert.equal(panel.querySelector('.codex-code').value,'');
  }finally{globalThis.document=original.document;globalThis.setTimeout=original.setTimeout;globalThis.clearTimeout=original.clearTimeout;}
});
