import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {mountChatConnection} from '../web/chat-connection.js';

test('chat connection checks authentication without starting inference or scheduling idle calls',async()=>{
  const {document}=parseHTML('<html><head><base href="http://localhost/"></head><body><details id="chat-connection"></details></body></html>');
  const original={document:globalThis.document,fetch:globalThis.fetch,setTimeout:globalThis.setTimeout};
  const requests=[];let timers=0;
  Object.assign(globalThis,{document,setTimeout:()=>{timers++;return 1;},fetch:async(url,options)=>{
    const path=new URL(url).pathname;requests.push(path);
    assert.equal(options.method,undefined);
    return Response.json(path.endsWith('/status')?{installed:true,subscriptionLogin:true,message:'Connected'}:{state:'idle'});
  }});
  try{
    mountChatConnection();await new Promise(resolve=>setImmediate(resolve));
    assert.deepEqual(requests.sort(),['/api/ai/login','/api/ai/status']);
    assert.equal(timers,0);
    assert.equal(document.querySelector('.codex-signin').hidden,true);
    assert.equal(document.querySelector('.codex-signout').hidden,false);
  }finally{Object.assign(globalThis,original);}
});
