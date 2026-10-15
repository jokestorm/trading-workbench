// A single in-flight refresh follows the visible selection. Hidden pages and
// selection changes abort old requests; late responses cannot own the new view.
export function createViewedRefresh({selection,visible,refresh,onError=()=>{},schedule=setTimeout,unschedule=clearTimeout}) {
  let timer=null,active=null,disposed=false;
  function later(){unschedule(timer);if(!disposed)timer=schedule(tick,1000);}
  async function tick(){
    timer=null;
    const key=visible()?selection():'';
    if(!key||disposed){active?.controller.abort();later();return;}
    if(active){later();return;}
    const run={key,controller:new AbortController()};active=run;
    try{await refresh(key,run.controller.signal);}
    catch(error){if(!run.controller.signal.aborted)onError(error);}
    finally{if(active===run)active=null;if(!disposed)later();}
  }
  function update(){
    unschedule(timer);
    if(active&&(!visible()||active.key!==selection()))active.controller.abort();
    if(!active)tick();else later();
  }
  return {update,dispose(){disposed=true;unschedule(timer);active?.controller.abort();}};
}
