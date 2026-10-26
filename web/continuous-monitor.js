import {fetchApp} from './http.js';

const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const stamp=value=>value?new Date(value).toLocaleString('en-US',{timeZone:'America/New_York',hour:'numeric',minute:'2-digit',second:'2-digit'})+' ET':'—';
const number=value=>value==null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:6}).format(value);
export function renderBrokerExecutions(report){
  const accounts=report?.history?.accounts??[];
  const fills=accounts.flatMap(account=>account.fills.map(fill=>({...fill,account:account.label}))).sort((a,b)=>Date.parse(b.time)-Date.parse(a.time)).slice(0,30);
  return `<p class="small muted">${escape(report?.message??'Sync Schwab transactions to read fills.')} · Last synced ${escape(stamp(report?.lastSynced))}${report?.status==='failed'?' · Historical results':''}</p>${fills.length?`<div class="table-wrap"><table><thead><tr><th>Time ET</th><th>Account</th><th>Contract</th><th>Fill</th><th>Quantity</th><th>Price</th><th>Order</th></tr></thead><tbody>${fills.map(fill=>`<tr><td>${escape(new Date(fill.time).toLocaleString('en-US',{timeZone:'America/New_York'}))}</td><td>${escape(fill.account)}</td><td>${escape(fill.symbol)}</td><td>${fill.amount>0?'BUY':'SELL'}</td><td>${number(Math.abs(fill.amount))}</td><td>${number(fill.price)}</td><td>${escape(fill.order)}</td></tr>`).join('')}</tbody></table></div>`:'<p class="small muted">No futures transaction fills returned.</p>'}`;
}
export function renderContinuousMarkets(state,now=Date.now()){
  if(!state?.markets?.length)return '<p class="muted">Discovering active futures products automatically…</p>';
  const engaged=state.markets.filter(m=>m.exposure.exitRequired||['LONG','SHORT'].includes(m.exposure.position.state)||m.symbol===state.lastProductChoice?.symbol||m.symbol===state.jobSymbol);
  const shown=engaged.length?engaged:state.markets.slice(0,3);
  return (state.markets.length>shown.length?`<p class="small muted">Watching ${state.markets.length} products. Current trade calls appear below.</p>`:'')+shown.map(m=>{
    const p=m.exposure,quote=m.quote,age=quote?.quoteTime?now-Date.parse(quote.quoteTime):Infinity;
    const fresh=quote?.realtime===true&&age>=-5000&&age<=30000;
    const expired=m.latest&&now>Date.parse(m.latest.validUntil);
    // Suppress a cached server call if it expires before the next UI refresh.
    const decision=!fresh&&!p.exitRequired?{action:'WAIT',reason:'Fresh realtime quote unavailable.'}:expired&&!p.exitRequired?{action:'WAIT',reason:m.awaitingExecution?'Call expired. Confirm the actual fill or confirm flat.':'Call expired; monitoring for a fresh setup.'}:m.currentDecision;
    const levels=decision?.entry!=null?`<div class="decision-levels">${[['Entry',decision.entry],['Stop',decision.stop],['Target',decision.target]].map(([label,value])=>`<div><span>${label}</span><strong>${number(value)}</strong></div>`).join('')}</div>`:'';
    return `<article class="continuous-card${p.exitRequired?' exit-required':''}"><div class="broker-heading"><h3>${escape(m.symbol)}</h3><span class="demo-badge">${p.exitRequired?'EXIT REQUIRED':fresh?'LIVE':'UNVERIFIED / STALE'}</span></div><p class="small muted">${escape(p.position.state)}${p.position.quantity?' · '+number(p.position.quantity)+' contracts @ '+number(p.position.averageEntry):''} · ${p.source==="transactions"?(p.positionBasis==="transaction-window"?"transaction estimate synced ":"transaction fills synced ")+escape(stamp(state.transactions?.lastSynced)):"starting position confirmed "+escape(stamp(p.confirmedAt))}</p><strong class="continuous-action">${escape(decision?.action??'WAIT')}</strong><p>${escape(decision?.reason??'Waiting for analysis.')}</p>${levels}${p.stop!=null?`<p class="small">Committed stop ${number(p.stop)} · target ${number(p.target)} · expiry ${escape(stamp(p.expiresAt))}</p>`:''}${m.awaitingExecution?'<p class="small continuous-reconcile">Waiting for broker execution fills. Confirm flat if you skipped this call.</p>':''}<p class="small muted">Last ${number(quote?.last)} · quote ${escape(stamp(quote?.quoteTime))} · checked ${escape(stamp(m.checkedAt))} · bars through ${escape(stamp(m.barThrough))}</p><p class="small muted">${escape(m.error??m.trigger??'Observing market conditions.')}</p><button class="quiet-button confirm-market" type="button" data-symbol="${escape(m.symbol)}">Check / update position</button></article>`;
  }).join('');
}
export function mountContinuousMonitor(){
  const panel=document.createElement('article');panel.className='panel continuous-monitor';
  panel.innerHTML=`<div class="panel-heading"><div><h2>Futures trade caller</h2><p>Watching micro futures · entries, holds and exits</p></div><span class="monitor-state demo-badge">LOADING</span></div><div class="connection-content"><div class="connection-actions"><button class="primary-button monitor-toggle" type="button" disabled>Start trade calls</button><span class="monitor-progress small muted" role="status"></span></div><p class="small muted">Scans your contracts continuously and calls AI when price action changes. Open trades get priority. Each call gives an action, prices and a short reason. Runs while this page is closed.</p><p class="monitor-profit-target small muted"></p><p class="monitor-feedback small" role="status"></p><div class="continuous-markets"></div><details class="monitor-position-editor"><summary>Confirm actual thinkorswim position</summary><p class="small muted">The trader initializes positions from transaction history and follows new fills automatically. Use this form to correct a position or trade levels.</p><form class="continuous-position"><div class="quote-controls"><label>Contract<select name="symbol" required></select></label><label>Actual position<select name="side"><option value="UNKNOWN">Unknown</option><option value="FLAT">Flat / skipped call</option><option value="LONG">Long</option><option value="SHORT">Short</option></select></label><label class="open-field">Contracts<input name="quantity" type="number" min="1" max="10000" step="1" value="1"></label><label class="open-field">Average fill<input name="entry" type="number" min="0.000001" step="any"></label><label class="open-field">Trade stop<input name="stop" type="number" min="0.000001" step="any"></label><label class="open-field">Target (optional)<input name="target" type="number" min="0.000001" step="any"></label><label class="open-field">Exit after minutes (optional)<input name="minutes" type="number" min="1" max="180" step="1"></label></div><p class="position-expiry small muted"></p><button class="quiet-button" type="submit">Confirm checked position</button></form></details><details class="monitor-universe"><summary>Contracts & scan settings</summary><p class="small muted">Use dated futures symbols supported by your Schwab feed. Verify dollar value per full price point and minimum tick size with the exchange. Monitoring uses up to 32 contracts; actual scan cadence depends on broker response time.</p><div class="monitor-instruments table-wrap"></div><form class="monitor-settings"><div class="quote-controls"><label>Scan interval (seconds)<input name="pollSeconds" type="number" min="10" max="300" value="15" required></label><label>AI cooldown per contract (seconds)<input name="aiCooldownSeconds" type="number" min="30" max="3600" value="90" required></label><label>Opportunity review (seconds)<input name="reviewSeconds" type="number" min="30" max="3600" value="300" required></label><label>Daily reporting target ($)<input name="dailyProfitTarget" type="number" min="1" max="1000000000" value="1000" required></label><button class="quiet-button" type="submit">Save contracts & settings</button></div></form></details><details class="monitor-memory"><summary>Recent trade-scope memory</summary><div></div></details></div>`;
  document.getElementById('market-panels').before(panel);
  const find=selector=>panel.querySelector(selector),position=find('.continuous-position'),settings=find('.monitor-settings');
  const brokerHistory=document.createElement('details');brokerHistory.className='monitor-broker-orders';brokerHistory.innerHTML='<summary>Transaction fills</summary><div class="connection-actions"><button class="quiet-button sync-order-fills" type="button">Sync fills</button></div><div class="broker-fill-history"></div>';
  find('.continuous-markets').after(brokerHistory);
  const accountLabel=document.createElement('label');accountLabel.innerHTML='Follow broker account<select name="brokerAccountId"><option value="">Automatic — single linked account</option></select>';
  settings.querySelector('.quote-controls').prepend(accountLabel);
  find('.monitor-universe > p').textContent='The trader discovers active micro contracts, loads their specifications, and compares products across markets. Open positions stay monitored through contract rollovers.';
  settings.querySelector('button[type="submit"]').textContent='Save scan settings';
  const selection=document.createElement('p');selection.className='monitor-product-choice';find('.continuous-markets').before(selection);
  const field=(form,name)=>form.querySelector(`[name="${name}"]`);
  let state=null,busy=false,reading=false,revision=0,timer=null,disposed=false,loaded=false,positionVersion=0,positionExpiry=null,productSignature='';
  async function api(path='',body){
    const response=await fetchApp('/api/ai/monitor'+path,{method:body===undefined?'GET':'POST',headers:body===undefined?undefined:{'Content-Type':'application/json'},body:body===undefined?undefined:JSON.stringify(body),cache:'no-store',signal:AbortSignal.timeout(15000)});
    const data=await response.json();if(!response.ok)throw new Error(data.message??'Monitor request failed.');return data;
  }
  function renderDraft(){
    const specs=state.settings.instruments;
    find('.monitor-instruments').innerHTML=(state.universe?`<p class="small muted">${escape(state.universe.message)}${state.universe.unavailable.length?' Unavailable: '+state.universe.unavailable.map(escape).join(', '):''}</p>`:'')+(specs.length?`<table><thead><tr><th>Active contract</th><th>$ / point</th><th>Tick size</th></tr></thead><tbody>${specs.map(spec=>`<tr><td>${escape(spec.symbol)}</td><td>${number(spec.pointValue)}</td><td>${number(spec.tickSize)}</td></tr>`).join('')}</tbody></table>`:'<p class="small muted">Discovering products…</p>');
  }
  function positionFields(){
    const open=['LONG','SHORT'].includes(field(position,'side').value);
    for(const label of position.querySelectorAll('.open-field'))label.hidden=!open;
    for(const name of ['quantity','entry','stop'])field(position,name).required=open;
  }
  function loadPosition(symbol){
    const exposure=state?.markets.find(m=>m.symbol===symbol)?.exposure;if(!exposure)return;
    field(position,'symbol').value=symbol;field(position,'side').value=exposure.position.state;
    field(position,'quantity').value=exposure.position.quantity??1;field(position,'entry').value=exposure.position.averageEntry??'';
    field(position,'stop').value=exposure.stop??'';field(position,'target').value=exposure.target??'';field(position,'minutes').value='';
    field(position,'minutes').disabled=!!exposure.expiresAt;positionExpiry=exposure.expiresAt;positionVersion=exposure.version;
    find('.position-expiry').textContent=positionExpiry?'Committed expiry '+stamp(positionExpiry)+'; it cannot be extended.':'';positionFields();
  }
  function apply(next,reset=false){
    const previous=state;state=next;
    find('.monitor-state').textContent=state.settings.enabled?'MONITORING':'STOPPED';
    find('.monitor-toggle').textContent=state.settings.enabled?'Stop trade calls':'Start trade calls';find('.monitor-toggle').disabled=busy;
    find('.monitor-progress').textContent=state.message??'';
    find('.monitor-profit-target').textContent='Daily reporting target: '+new Intl.NumberFormat('en-US',{style:'currency',currency:'USD',maximumFractionDigits:0}).format(state.settings.dailyProfitTarget??1000)+'; this does not force entries, increase exposure, or stop trading when reached.';
    find('.continuous-markets').innerHTML=renderContinuousMarkets(state);
    selection.textContent=state.lastProductChoice?'AI product choice: '+state.lastProductChoice.symbol+' · '+state.lastProductChoice.reason:'The trader will compare ready products and choose where to trade.';
    renderDraft();
    const chartSymbol=state.jobSymbol??state.lastProductChoice?.symbol,chartExposure=state.markets.find(m=>m.symbol===chartSymbol)?.exposure;
    const nextProductSignature=state.settings.instruments.map(s=>s.symbol).join('|')+'|'+(chartSymbol??'')+'|'+(chartExposure?.version??'');
    if(nextProductSignature!==productSignature){productSignature=nextProductSignature;window.dispatchEvent(new window.CustomEvent('trader-products',{detail:{selected:chartSymbol,position:chartExposure?.position}}));}
    find('.broker-fill-history').innerHTML=renderBrokerExecutions(state.transactions);
    const accounts=state.transactions?.history?.accounts??[],accountField=field(settings,'brokerAccountId'),signature=accounts.map(a=>a.id).join('|');
    if(accountField.dataset.accounts!==signature){
      const selected=loaded?accountField.value:state.settings.brokerAccountId??'';
      accountField.innerHTML='<option value="">Automatic — single linked account</option>'+accounts.map(a=>`<option value="${escape(a.id)}">${escape(a.label)}</option>`).join('')+(selected&&!accounts.some(a=>a.id===selected)?`<option value="${escape(selected)}">Saved account</option>`:'');
      accountField.value=selected;accountField.dataset.accounts=signature;
    }
    for(const button of panel.querySelectorAll('.confirm-market'))button.addEventListener('click',()=>{find('.monitor-position-editor').open=true;loadPosition(button.dataset.symbol);position.scrollIntoView?.({behavior:'smooth',block:'nearest'});});
    const selector=field(position,'symbol'),selected=selector.value;
    if(!previous||previous.markets.map(m=>m.symbol).join()!==state.markets.map(m=>m.symbol).join()){
      selector.innerHTML=state.markets.map(m=>`<option value="${escape(m.symbol)}">${escape(m.symbol)}</option>`).join('');
      loadPosition(state.markets.some(m=>m.symbol===selected)?selected:state.markets[0]?.symbol);
    }
    if(!loaded||reset){
      for(const name of ['pollSeconds','aiCooldownSeconds','reviewSeconds','dailyProfitTarget'])field(settings,name).value=state.settings[name];
      loaded=true;
      accountField.value=state.settings.brokerAccountId??'';
    }
    find('.monitor-memory>div').innerHTML=state.memory.slice(-20).reverse().map(item=>`<p class="small"><span class="muted">${escape(stamp(item.time))} · ${escape(item.symbol)} · ${escape(item.kind)}</span><br>${escape(item.decision?.action??'')} ${escape(item.decision?.reason??item.detail)}</p>`).join('')||'<p class="small muted">No recent observations saved in trade-scope memory.</p>';
  }
  async function action(work){
    if(busy)return;busy=true;revision++;clearTimeout(timer);find('.monitor-feedback').textContent='';
    const buttons=[...panel.querySelectorAll('button')];buttons.forEach(b=>b.disabled=true);
    try{await work();}catch(error){find('.monitor-feedback').textContent=error.message;}
    finally{busy=false;buttons.forEach(b=>b.disabled=false);find('.monitor-toggle').disabled=!state;later();}
  }
  const edit=enabled=>({revision:state.settings.revision,enabled,instruments:state.settings.instruments,pollSeconds:Number(field(settings,'pollSeconds').value),aiCooldownSeconds:Number(field(settings,'aiCooldownSeconds').value),reviewSeconds:Number(field(settings,'reviewSeconds').value),dailyProfitTarget:Number(field(settings,'dailyProfitTarget').value),brokerAccountId:field(settings,'brokerAccountId').value||null});
  find('.sync-order-fills').addEventListener('click',()=>action(async()=>{
    const response=await fetchApp('/api/schwab/transactions/sync',{method:'POST',signal:AbortSignal.timeout(25000),cache:'no-store'});
    const report=await response.json();if(!response.ok)throw new Error(report.message??'Transaction sync failed.');
    apply(await api());find('.monitor-feedback').textContent=report.message;
  }));
  find('.monitor-toggle').addEventListener('click',()=>action(async()=>apply(await api('/settings',edit(!state.settings.enabled)),true)));
  settings.addEventListener('submit',event=>{event.preventDefault();action(async()=>apply(await api('/settings',edit(state.settings.enabled)),true));});
  field(position,'symbol').addEventListener('change',()=>loadPosition(field(position,'symbol').value));field(position,'side').addEventListener('change',positionFields);
  position.addEventListener('submit',event=>{
    event.preventDefault();action(async()=>{
      const symbol=field(position,'symbol').value,side=field(position,'side').value,open=['LONG','SHORT'].includes(side),minutes=Number(field(position,'minutes').value);
      const next=await api('/position',{symbol,version:positionVersion,position:{state:side,quantity:open?Number(field(position,'quantity').value):null,averageEntry:open?Number(field(position,'entry').value):null},stop:open?Number(field(position,'stop').value):null,target:open&&field(position,'target').value?Number(field(position,'target').value):null,expiresAt:open?(positionExpiry??(minutes>0?new Date(Date.now()+minutes*60000).toISOString():null)):null});
      apply(next);loadPosition(symbol);find('.monitor-feedback').textContent='Position recorded. The next call will use this trade context.';
    });
  });
  function later(){clearTimeout(timer);if(!disposed)timer=setTimeout(poll,2000);}
  async function poll(){
    if(disposed||busy||reading)return;
    reading=true;const current=revision;
    try{if(!document.hidden&&!document.getElementById('market').hidden){const next=await api();if(current===revision&&!disposed)apply(next);}}
    catch(error){find('.monitor-feedback').textContent=error.message+' Displayed observations are from the last successful read.';}
    finally{reading=false;later();}
  }
  document.addEventListener('visibilitychange',()=>{if(!document.hidden){clearTimeout(timer);poll();}});
  window.addEventListener('pagehide',()=>{disposed=true;clearTimeout(timer);});
  window.addEventListener('pageshow',()=>{disposed=false;poll();});
  positionFields();poll();return panel;
}
