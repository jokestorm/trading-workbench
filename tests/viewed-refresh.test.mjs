import test from 'node:test';
import assert from 'node:assert/strict';
import {createViewedRefresh} from '../web/viewed-refresh.js';

const flush=()=>new Promise(resolve=>setImmediate(resolve));
function fixture(refresh){
  let key='/MESZ26',shown=true,pending=null;
  const errors=[],delays=[];
  const poller=createViewedRefresh({selection:()=>key,visible:()=>shown,refresh,onError:error=>errors.push(error.message),schedule:(fn,delay)=>{pending=fn;delays.push(delay);return 1;},unschedule:()=>{pending=null;}});
  return {poller,errors,delays,select:value=>{key=value;poller.update();},show:value=>{shown=value;poller.update();},tick:()=>{const fn=pending;pending=null;fn?.();}};
}
test('viewed refresh targets only the selected contract on a one-second timer',async()=>{
  const calls=[],f=fixture(async key=>calls.push(key));
  f.poller.update();await flush();f.tick();await flush();
  assert.deepEqual(calls,['/MESZ26','/MESZ26']);assert.deepEqual(f.delays,[1000,1000]);
  f.select('/MGCZ26');await flush();assert.equal(calls.at(-1),'/MGCZ26');f.poller.dispose();
});
test('hidden views do not fetch, abort active requests and resume when viewed',async()=>{
  const calls=[];let finish;
  const f=fixture((key,signal)=>{calls.push({key,signal});return new Promise(resolve=>{finish=resolve;});});
  f.show(false);f.tick();await flush();assert.equal(calls.length,0);
  f.show(true);assert.equal(calls.length,1);
  f.show(false);assert.equal(calls[0].signal.aborted,true);
  finish();await flush();f.tick();assert.equal(calls.length,1);
  f.show(true);assert.equal(calls.length,2);finish();await flush();f.poller.dispose();
});
test('slow requests never overlap and changing selection invalidates the old request',async()=>{
  const calls=[];let finish;
  const f=fixture((key,signal)=>{calls.push({key,signal});return new Promise(resolve=>{finish=resolve;});});
  f.poller.update();f.poller.update();f.tick();assert.equal(calls.length,1);
  f.select('/MCLX26');assert.equal(calls[0].signal.aborted,true);f.tick();assert.equal(calls.length,1);
  finish();await flush();f.tick();assert.equal(calls[1].key,'/MCLX26');
  f.poller.dispose();assert.equal(calls[1].signal.aborted,true);finish();await flush();f.tick();assert.equal(calls.length,2);
});
test('refresh failures remain visible and the next refresh can recover',async()=>{
  let count=0;const f=fixture(async()=>{if(++count===1)throw new Error('Connection unavailable');});
  f.poller.update();await flush();assert.deepEqual(f.errors,['Connection unavailable']);
  f.tick();await flush();assert.equal(count,2);f.poller.dispose();
});
