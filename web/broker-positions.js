import {fetchApp} from './http.js';
import {observeMarketBrief} from './market-brief.js?v=ha-0.1.13';
const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const time=value=>value?new Date(value).toLocaleString('en-US',{timeZone:'America/New_York',month:'short',day:'numeric',hour:'numeric',minute:'2-digit'})+' ET':'—';
export function usableBrokerPosition(report,position,now=Date.now()){
  return !!(report?.status==='ready'&&Date.parse(report.lastSynced)<=now+5000&&now-Date.parse(report.lastSynced)<=90000&&position&&(position.state==='FLAT'||(['LONG','SHORT'].includes(position.state)&&Number.isInteger(position.quantity)&&position.quantity>0&&Number.isFinite(position.averageEntry)&&position.averageEntry>0)));
}
export function renderBrokerPosition(report,symbol,accountId='',now=Date.now()){
  const matches=(report?.positions??[]).filter(p=>p.symbol===symbol);
  const position=matches.find(p=>p.accountId===accountId)??matches[0];
  const fresh=usableBrokerPosition(report,position,now);
  const options=matches.length>1?`<label>Account<select class="broker-account">${matches.map(p=>`<option value="${escape(p.accountId)}" ${p.accountId===position?.accountId?'selected':''}>${escape(p.accountLabel)}</option>`).join('')}</select></label>`:'';
  const quantity=position?.quantity??'',average=position?.averageEntry==null?'':' @ '+new Intl.NumberFormat('en-US',{maximumFractionDigits:6}).format(position.averageEntry);
  return `<div class="broker-heading"><strong>Schwab transaction position</strong><button class="quiet-button broker-sync" type="button">Sync</button></div>${options}<p>${position?`<strong>${escape(symbol)} · ${escape(position.state)} ${escape(quantity)}${escape(average)}</strong>`:'No derived position for '+escape(symbol||'the selected contract')+'.'}</p><p class="small muted">${escape(position?.message??report?.message??'Sync Accounts and Trading to estimate your position.')}</p><p class="small muted">Last synced: ${escape(time(report?.lastSynced))} · Last fill: ${escape(time(position?.lastFill))}</p>${report?.status==='failed'?`<p class="small muted" role="status">Sync failed: ${escape(report.message)} Previous values are historical.</p>`:''}<button class="quiet-button broker-use" type="button" ${fresh?'':'disabled'}>Use in analysis — confirms this matches my position</button><p class="small muted broker-position-message" role="status">${fresh?'Check against your broker. New fills may not be posted yet.':'Sync and verify before using. No returned position does not mean flat.'}</p>`;
}
export function mountBrokerPositions(container,{symbol,apply}){
  const panel=document.createElement('section');panel.className='broker-position';container.append(panel);
  let report=null,account='',pageActive=true;
  const current=()=>{const positions=(report?.positions??[]).filter(p=>p.symbol===symbol());return positions.find(p=>p.accountId===account)??positions[0];};
  function draw(){panel.innerHTML=renderBrokerPosition(report,symbol(),account);panel.querySelector('.broker-account')?.addEventListener('change',event=>{account=event.target.value;draw();});panel.querySelector('.broker-use').addEventListener('click',()=>{const position=current();if(!usableBrokerPosition(report,position))return;apply({state:position.state,quantity:position.quantity,averageEntry:position.averageEntry});panel.querySelector('.broker-position-message').textContent='Copied to My position. Update or sync again after trading; analysis uses those controls.';});panel.querySelector('.broker-sync').addEventListener('click',()=>observer.update());}
  const observer=observeMarketBrief({
    visible:()=>pageActive&&!document.hidden&&!document.getElementById('market').hidden,
    load:async signal=>{const response=await fetchApp('/api/schwab/positions/sync',{method:'POST',signal});if(!response.ok)throw Error('Could not sync Schwab transactions.');return response.json();},
    render:state=>{report=state;draw();},
    onError:()=>{report=report?{...report,status:'failed',message:'Could not sync transactions.'}:null;draw();panel.querySelector('.broker-position-message').textContent='Could not sync transactions. Check Schwab sign-in and Accounts and Trading access.';}
  });
  for(const id of ['monitor-symbol'])document.getElementById(id).addEventListener('change',()=>{draw();observer.update();});
  window.addEventListener('market-context',()=>{draw();observer.update();});
  document.addEventListener('visibilitychange',()=>observer.update());
  new MutationObserver(()=>observer.update()).observe(document.getElementById('market'),{attributes:true,attributeFilter:['hidden']});
  window.addEventListener('pagehide',()=>{pageActive=false;observer.update();});
  window.addEventListener('pageshow',()=>{pageActive=true;observer.update();});
  draw();observer.update();
}
