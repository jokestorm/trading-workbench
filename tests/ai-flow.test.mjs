import test from 'node:test';
import assert from 'node:assert/strict';
import {runAnalysis} from '../web/ai.js';
test('one market action updates the selected instrument then sends only the prepared token for analysis',async()=>{
 const input={kind:'saved-market',symbol:'/MESZ26',source:'bars',days:3},calls=[],events=[],signal=new AbortController().signal;
 const run=await runAnalysis(input,{signal,onMarketUpdated:()=>events.push('updated'),request:async(path,body,receivedSignal)=>{assert.equal(receivedSignal,signal);calls.push({path,body});if(path==='ai/preview')return{token:'snapshot-token'};return{id:'result'};}});
 assert.equal(run.id,'result');assert.deepEqual(calls,[{path:'market/backfill',body:{symbol:'/MESZ26'}},{path:'market/capture',body:{symbol:'/MESZ26'}},{path:'ai/preview',body:input},{path:'ai/analyze',body:{token:'snapshot-token'}}]);assert.deepEqual(events,['updated']);
});
test('a market update failure never starts AI',async()=>{
 const calls=[];await assert.rejects(runAnalysis({kind:'saved-market',symbol:'AAPL',source:'quotes'},{request:async path=>{calls.push(path);throw new Error('Schwab disconnected');}}),/disconnected/);assert.deepEqual(calls,['market/capture']);
});
test('journal analysis stays local until the AI call and never fetches market data',async()=>{
 const calls=[];await runAnalysis({kind:'journal',filters:{symbol:'AAPL'}},{request:async path=>{calls.push(path);return{token:'journal-token'};}});assert.deepEqual(calls,['ai/preview','ai/analyze']);
});
