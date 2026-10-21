import {fetchApp} from './http.js';

const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const stamp=value=>value?new Date(value).toLocaleString('en-US',{timeZone:'America/New_York',month:'short',day:'numeric',hour:'numeric',minute:'2-digit'})+' ET':'—';
export function renderMarketBrief(state){
  const content=state?.content;
  return `<div class="panel-heading"><div><h2>Market brief</h2><p>Markets & commodities · hourly, weekdays 8 AM–3 PM Eastern</p></div></div><div class="connection-content">${content?`<strong class="brief-outlook">${escape(content.outlook)}</strong><ul class="brief-points">${content.points.map(point=>`<li>${escape(point)}</li>`).join('')}</ul><p><strong>Watch next:</strong> ${escape(content.nextEvent)}</p>`:'<p class="muted">The first scheduled check will create your brief. Connect ChatGPT in AI settings to enable research.</p>'}<p class="small muted">Last checked: ${escape(stamp(state?.lastChecked))} · Last changed: ${escape(stamp(state?.lastChanged))}</p><p class="small muted" role="status">${escape(state?.message??'Loading brief…')}</p></div>`;
}
// Polling only reads the saved brief. Server scheduling does not depend on viewers.
export function observeMarketBrief({visible,load,render,onError,schedule=setTimeout,unschedule=clearTimeout}){
  let timer,active=null,disposed=false;
  async function tick(){
    if(disposed||active)return;
    if(visible()){
      const controller=new AbortController();active=controller;
      try{const state=await load(controller.signal);if(!controller.signal.aborted)render(state);}
      catch(error){if(!controller.signal.aborted)onError(error);}
      finally{active=null;}
    }
    if(!disposed)timer=schedule(tick,30000);
  }
  return {
    update(){unschedule(timer);if(!visible())active?.abort();tick();},
    dispose(){disposed=true;unschedule(timer);active?.abort();}
  };
}
export function mountMarketBrief(){
  const panel=document.createElement('article');panel.className='panel market-brief';panel.innerHTML=renderMarketBrief(null);
  document.querySelector('#market-panels').before(panel);
  let pageActive=true;
  const observer=observeMarketBrief({
    visible:()=>pageActive&&!document.hidden&&!document.getElementById('market').hidden,
    load:async signal=>{const response=await fetchApp('/api/ai/market-brief',{signal,cache:'no-store'});if(!response.ok)throw new Error('Could not load the market brief.');return response.json();},
    render:state=>{panel.innerHTML=renderMarketBrief(state);},
    onError:()=>{panel.querySelector('[role="status"]').textContent='Could not refresh the brief. Displayed text is from the last successful load.';}
  });
  document.addEventListener('visibilitychange',()=>observer.update());
  new MutationObserver(()=>observer.update()).observe(document.getElementById('market'),{attributes:true,attributeFilter:['hidden']});
  window.addEventListener('pagehide',()=>{pageActive=false;observer.update();});
  window.addEventListener('pageshow',()=>{pageActive=true;observer.update();});
  observer.update();
}
