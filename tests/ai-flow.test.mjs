import test from 'node:test';
import assert from 'node:assert/strict';
import {createAnalysisRuns,activeRun} from '../web/analysis-runs.js';
function harness(request,onCompleted=async()=>{}){
 const states=[],timers=[];
 const runs=createAnalysisRuns({request,onCompleted,onChange:state=>states.push(state),schedule:fn=>{timers.push(fn);return timers.length;},unschedule:()=>{}});
 return {runs,states,timers};
}
test('Analyze submits one request without browser-side refresh or preview ordering',async()=>{
 const input={kind:'saved-market',symbol:'/MESZ26',position:{state:'FLAT'}},calls=[];
 const {runs,states}=harness(async(path,body)=>{calls.push({path,body});return{id:'a',state:'refreshing',active:true};});
 await runs.start(input);await runs.start({kind:'journal'});
 assert.deepEqual(calls,[{path:'ai/runs',body:input}]);assert.equal(states.at(-1).job.id,'a');
});
test('reopening observes active work and its saved completion without resubmitting',async()=>{
 const calls=[],finished=[];let job={id:'existing',state:'analyzing',active:true};
 const {runs}=harness(async path=>{calls.push(path);return {run:job};},async value=>finished.push(value.resultId));
 await runs.check();job={id:'existing',state:'completed',resultId:'saved'};await runs.check();await runs.check();
 assert.deepEqual(calls,['ai/runs/current','ai/runs/current','ai/runs/current']);assert.deepEqual(finished,['saved']);
 runs.dispose();assert.equal(activeRun(job),false);
});
test('connection failure preserves active status; explicit Cancel is a separate request',async()=>{
 let failing=false;const calls=[];
 const {runs,states,timers}=harness(async path=>{calls.push(path);if(failing)throw new Error('Offline');return path.endsWith('/cancel')?{id:'a',state:'canceled'}:{run:{id:'a',state:'analyzing',active:true}};});
 await runs.check();failing=true;await runs.check();
 assert.equal(states.at(-1).job.id,'a');assert.equal(states.at(-1).error,'Offline');assert.ok(timers.length);
 failing=false;await runs.cancel();assert.equal(states.at(-1).job.state,'canceled');assert.equal(calls.at(-1),'ai/runs/a/cancel');
});
test('a stale poll cannot overwrite a newly admitted run',async()=>{
 let resolve;const {runs,states}=harness(path=>path==='ai/runs/current'?new Promise(r=>resolve=r):Promise.resolve({id:'new',state:'preparing',active:true}));
 const check=runs.check();await runs.start({kind:'journal'});resolve({run:{id:'old',state:'completed'}});await check;
 assert.equal(states.at(-1).job.id,'new');
});
test('interrupted runs remain retryable and closing the observer never cancels server work',async()=>{
 const calls=[];const {runs,states}=harness(async path=>{calls.push(path);return{run:{id:'old',state:'interrupted'}};});
 await runs.check();assert.equal(activeRun(states.at(-1).job),false);runs.dispose();await runs.check();assert.deepEqual(calls,['ai/runs/current']);
});
