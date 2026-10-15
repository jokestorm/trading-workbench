export const activeRun=job=>job?.active===true;

// One observer for both workspaces. Reconnecting only reads status; page
// teardown never cancels or resubmits the admitted run.
export function createAnalysisRuns({request,onChange,onCompleted=async()=>{},schedule=setTimeout,unschedule=clearTimeout}){
  let job=null,busy=false,checking=false,revision=0,timer=null,disposed=false,error='';
  const delivered=new Set();
  const render=()=>onChange({job,busy,error});
  function later(){unschedule(timer);if(!disposed)timer=schedule(check,activeRun(job)?2_000:10_000);}
  async function apply(next){
    job=next;error='';render();
    if(job?.state==='completed'&&!delivered.has(job.id)){
      await onCompleted(job);delivered.add(job.id);
    }
  }
  async function check(){
    if(disposed)return;
    if(checking||busy)return later();
    checking=true;const current=revision;
    try{const {run}=await request('ai/runs/current');if(current===revision&&!disposed)await apply(run);}
    catch(e){if(current===revision&&!disposed){error=e.message;render();}}
    finally{checking=false;later();}
  }
  async function mutate(path,input){
    if(busy||disposed)return;
    busy=true;revision++;error='';unschedule(timer);render();
    try{await apply(await request(path,input));}
    catch(e){error=e.message;render();}
    finally{busy=false;render();later();}
  }
  return {
    check,
    start(input){if(activeRun(job)){render();return Promise.resolve(job);}return mutate('ai/runs',input);},
    cancel(){if(activeRun(job))return mutate('ai/runs/'+job.id+'/cancel',{});},
    dispose(){disposed=true;revision++;unschedule(timer);}
  };
}
