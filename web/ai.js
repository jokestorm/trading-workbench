import { fetchApp } from './http.js';
import { mountOutcomes } from './outcomes.js?v=decisions-2';
const $=id=>document.getElementById(id);
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const time=v=>new Date(v).toLocaleString('en-US',{timeZone:'America/New_York'})+' ET';
async function api(path,body,signal){const r=await fetchApp('/api/'+path,body===undefined?{signal,cache:'no-store'}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal});const data=await r.json();if(!r.ok)throw new Error(data.message??'Analysis request failed.');return data;}
export function mountAI({getFilters}){
 let loginReady=false;
 const market=workspace('saved-market','Market analysis','Analyze');
 document.querySelector('.monitor-panel').after(market.panel);
 const journal=workspace('journal','Trade analysis','Analyze trades');
 const journalDetails=document.createElement('details');journalDetails.className='journal-ai-details';journalDetails.innerHTML='<summary>AI trade analysis</summary>';journalDetails.append(journal.panel);document.querySelector('.trade-panel').after(journalDetails);

 for(const id of ['monitor-symbol','monitor-source','monitor-days'])$(id).addEventListener('change',market.context);
 window.addEventListener('market-context',market.context);
 async function check(){try{const status=await api('ai/status');loginReady=status.subscriptionLogin;for(const w of [market,journal]){w.login.textContent=loginReady?'ChatGPT connected':status.message;w.update();}}catch(e){for(const w of [market,journal])w.login.textContent=e.message;}}
 function workspace(kind,title,action){
  const panel=document.createElement('article');panel.className='panel analysis-panel';panel.innerHTML=`<div class="panel-heading"><div><h2>${title}</h2><p class="analysis-context"></p></div></div><div class="connection-content">${kind==='journal'?'<label class="small muted"><input class="include-notes" type="checkbox"> Include trade notes</label>':''}<p class="small muted">${kind==='saved-market'?'Analyzes the selected instrument using its saved chart data.':'Analyzes saved trades matching your current filters.'} Sends data to OpenAI using your ChatGPT subscription.</p><div class="connection-actions"><button class="primary-button analyze" type="button" disabled>${action}</button><button class="quiet-button cancel" type="button" hidden>Cancel</button><span class="small muted analysis-login">Checking ChatGPT login…</span></div><div class="analysis-message" role="status"></div><div class="analysis-result"><p class="small muted">${kind==='saved-market'?'Your latest analysis and price follow-up will appear here.':'Your trade analysis will appear here.'}</p></div><details class="analysis-history"><summary>Previous analyses</summary><div></div></details><details class="analysis-connection"><summary>Connection</summary><button class="quiet-button check-login" type="button">Check ChatGPT login</button></details></div>`;
  if(kind==='saved-market'){const form=document.createElement('div');form.innerHTML='<div class="quote-controls position-input"><label>My position<select class="position-state"><option value="UNKNOWN">Unknown</option><option value="FLAT">Flat — no position</option><option value="LONG">Long</option><option value="SHORT">Short</option></select></label><label class="position-size" hidden>Contracts<input class="position-quantity" type="number" min="1" step="1" value="1"></label><label class="position-entry" hidden>Average entry<input class="position-average" type="number" min="0.01" step="any" placeholder="Your entry price"></label></div><p class="small muted">Position is entered manually for the selected contract. Update it when you trade.</p>';panel.querySelector('.connection-content').prepend(form);}
  const button=panel.querySelector('.analyze'),login=panel.querySelector('.analysis-login'),message=panel.querySelector('.analysis-message'),result=panel.querySelector('.analysis-result'),cancel=panel.querySelector('.cancel'),historyContainer=panel.querySelector('.analysis-history>div');let busy=false,controller=null;const positions=new Map();let positionSymbol=null;
  function update(){button.disabled=busy||!loginReady||(kind==='saved-market'&&!$('monitor-symbol').value);}
  function context(){if(kind==='saved-market'){const symbol=$('monitor-symbol').value;if(symbol!==positionSymbol){positionSymbol=symbol;const p=positions.get(symbol)??{state:'UNKNOWN',quantity:1,averageEntry:null};panel.querySelector('.position-state').value=p.state;panel.querySelector('.position-quantity').value=p.quantity??1;panel.querySelector('.position-average').value=p.averageEntry??'';positionFields();}}panel.querySelector('.analysis-context').textContent=kind==='saved-market'?`${$('monitor-symbol').value||'Choose an instrument above'} · ${$('monitor-source').value==='bars'?'minute bars':'quote samples'}`:'Current journal filters';update();}
  function positionFields(){if(kind!=='saved-market')return;const active=['LONG','SHORT'].includes(panel.querySelector('.position-state').value);panel.querySelector('.position-size').hidden=!active;panel.querySelector('.position-entry').hidden=!active;}
  function position(){return {state:panel.querySelector('.position-state').value,quantity:Number(panel.querySelector('.position-quantity').value),averageEntry:Number(panel.querySelector('.position-average').value)||null};}
  if(kind==='saved-market')for(const input of panel.querySelectorAll('.position-input input,.position-input select'))input.addEventListener('change',()=>{positions.set(positionSymbol,position());positionFields();});
  function display(run){const r=run.result;result.innerHTML=`<div class="analysis-answer"><p class="small muted">${escape(run.snapshot.symbol??'Trades')} · ${escape(time(run.createdAt))}${run.snapshot.asOf?' · data through '+escape(time(run.snapshot.asOf)):''}</p>${decisionMarkup(r.decision,run.snapshot)}<h3>${escape(r.summary)}</h3><p>${escape(r.assessment)}</p><h4>What to watch</h4><ul>${r.nextSteps.map(s=>`<li>${escape(s)}</li>`).join('')}</ul><details><summary>Reasoning & limitations</summary>${[['Evidence',r.evidence],['Limitations',r.limitations]].map(([label,items])=>`<h4>${label}</h4><ul>${items.map(s=>`<li>${escape(s)}</li>`).join('')}</ul>`).join('')}<h4>${kind==='journal'?'Applicability':'When this view changes'}</h4><p>${escape(r.invalidation)}</p></details></div>`;mountOutcomes(run,result);}
  async function history(showLatest=false){const runs=(await api('ai/history')).filter(r=>r.kind===kind);historyContainer.replaceChildren();if(!runs.length){historyContainer.textContent='No previous analyses.';return;}if(showLatest)display(runs[0]);for(const run of runs){const item=document.createElement('button');item.className='quiet-button history-item';item.textContent=`${run.snapshot.symbol??'Trades'} · ${time(run.createdAt)} · ${run.result.summary}`;item.addEventListener('click',()=>display(run));historyContainer.append(item);}}
  button.addEventListener('click',async()=>{if(busy||!loginReady)return;const input={kind,filters:kind==='journal'?getFilters():undefined,includeNotes:panel.querySelector('.include-notes')?.checked??false,symbol:kind==='saved-market'?$('monitor-symbol').value:undefined,source:$('monitor-source').value,days:Number($('monitor-days').value),position:kind==='saved-market'?position():undefined};busy=true;controller=new AbortController();update();cancel.hidden=false;try{const run=await runAnalysis(input,{signal:controller.signal,onProgress:text=>message.textContent=text,onMarketUpdated:()=>window.dispatchEvent(new CustomEvent('market-prices-updated'))});display(run);message.textContent='Saved.';try{await history();}catch(e){historyContainer.textContent=e.message;}}catch(e){message.textContent=e.name==='AbortError'?'Canceled.':e.message;}finally{busy=false;cancel.hidden=true;update();}});
  cancel.addEventListener('click',()=>controller?.abort());panel.querySelector('.check-login').addEventListener('click',check);context();history(true).catch(e=>historyContainer.textContent=e.message);return{panel,button,login,context,update};
 }
 check();
}
export async function runAnalysis(input,{request=api,signal,onProgress=()=>{},onMarketUpdated=()=>{}}={}){
 if(input.kind==='saved-market'){
  onProgress('Updating prices…');
  await request('market/'+(input.source==='bars'?'backfill':'capture'),{symbol:input.symbol},signal);if(input.source==='bars')await request('market/capture',{symbol:input.symbol},signal);
  onMarketUpdated();
 }
 onProgress('Preparing data…');
 const preview=await request('ai/preview',input,signal);
 onProgress('Analyzing… This can take a few minutes.');
 return request('ai/analyze',{token:preview.token},signal);
}
function decisionMarkup(d,snapshot){
 if(!d)return '';
 const labels={BUY:'BUY — open long',SELL_SHORT:'SELL — open short',SELL_CLOSE:'SELL — close long',BUY_COVER:'BUY — cover short',HOLD:'HOLD',WAIT:'WAIT'};
 const price=v=>v==null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:2}).format(v);
 const risk=d.entry!=null&&d.stop!=null&&snapshot.pointValue?Math.abs(d.entry-d.stop)*snapshot.pointValue:null;
 return `<div class="decision-call"><strong>${escape(labels[d.action]??d.action)}</strong><p>${escape(d.reason)}</p><p class="small muted">Position used: ${escape(snapshot.position?.state??'Unknown')}${snapshot.position?.quantity?' · '+snapshot.position.quantity+' contracts':''} · manual input${d.guarded?' · action withheld by data/position checks':''}</p>${d.entry!=null||d.stop!=null||d.target!=null?`<div class="decision-levels">${[['Entry',d.entry],['Stop',d.stop],['Target',d.target]].map(([label,value])=>`<div><span>${label}</span><strong>${price(value)}</strong></div>`).join('')}</div>`:''}${risk!==null?`<p class="small muted">Entry-to-stop distance: $${risk.toFixed(2)} per contract, before fees and slippage.</p>`:''}<p class="small muted">Recommendation only. No order is placed.</p></div>`;
}
