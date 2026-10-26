import {fetchApp} from './http.js';

const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const time=value=>value?new Date(value).toLocaleString('en-US',{timeZone:'America/New_York'} )+' ET':'Not synced';
const day=value=>value?new Date(value).toLocaleDateString('en-US',{timeZone:'America/New_York'}):'—';
const number=value=>new Intl.NumberFormat('en-US',{maximumFractionDigits:6}).format(value);
export function renderAccountSource(state){
  return `<strong>Schwab transactions</strong> · ${state?.syncedAt?`${number(state.fillCount??0)} fills · ${number(state.trades?.length??0)} closed trades · fetched window ${escape(day(state.historyFrom))}–${escape(day(state.historyThrough))} · synced ${escape(time(state.syncedAt))}`:'Waiting for transaction history'}${state?.status!=='ready'?`<p>${escape(state?.message??'Connecting to Schwab…')}${state?.syncedAt?' Showing the last transaction-derived dashboard.':''}</p>`:''}`;
}
export function renderAccountPositions(positions){
  return `<div class="panel-heading"><div><h2>Open futures cycles</h2><p>Derived from transactions · closed-trade totals update when each cycle is flat</p></div></div><div class="table-wrap"><table><thead><tr><th>Account</th><th>Contract</th><th>Direction</th><th>Quantity</th><th>Average entry</th><th>Remaining entry fees</th></tr></thead><tbody>${positions.map(p=>`<tr><td>${escape(p.account)}</td><td>${escape(p.symbol)}</td><td>${p.quantity>0?'LONG':'SHORT'}</td><td>${number(Math.abs(p.quantity))}</td><td>${number(p.averageEntry)}</td><td>${number(p.entryFees)}</td></tr>`).join('')}</tbody></table></div>`;
}
export function observeAccountDashboard({load,onChange,onStatus=()=>{},onError=()=>{},visible=()=>true,schedule=setTimeout,unschedule=clearTimeout}){
  let timer=null,reading=false,disposed=false,revision=null;
  async function poll(){
    if(disposed||reading)return;
    reading=true;
    try{
      if(visible()){
        const state=await load();if(disposed)return;
        onStatus(state);
        if(state.revision!==revision){await onChange(state);revision=state.revision;}
      }
    }catch(error){if(!disposed)onError(error);}
    finally{reading=false;if(!disposed){unschedule(timer);timer=schedule(poll,15000);}}
  }
  return {update(){unschedule(timer);return poll();},dispose(){disposed=true;unschedule(timer);}};
}
export function mountAccountSync({onSynced}){
  const button=document.createElement('button');button.type='button';button.className='quiet-button';button.id='sync-account-dashboard';button.textContent='Refresh transactions';document.querySelector('.journal-toolbar .toolbar-actions').prepend(button);
  const status=document.createElement('p');status.className='small muted';status.setAttribute('role','status');document.querySelector('.journal-toolbar').after(status);
  button.addEventListener('click',async()=>{
    if(button.disabled)return;button.disabled=true;status.textContent='Updating account from Schwab transactions…';
    try{
      const response=await fetchApp('/api/account/sync',{method:'POST',signal:AbortSignal.timeout(35000)});const result=await response.json();
      if(!response.ok||result.status!=='ready')throw new Error(result.message??'Transaction sync failed.');
      status.textContent='Account dashboard updated from transaction history.';await onSynced(result);
    }catch(error){status.textContent=error.message;}
    finally{button.disabled=false;}
  });
}
