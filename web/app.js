import {mountChatConnection} from './chat-connection.js?v=chat-only-1';
import {mountTraderChat} from './trader-chat.js?v=chat-only-1';
import {mountAccountSync,observeAccountDashboard,renderAccountSource,renderAccountPositions} from './account-dashboard.js?v=account-1';
import {renderJournalMetrics} from './journal-metrics.js?v=ha-0.1.13';
import { fetchApp, appUrl } from './http.js';
import { mountJournalChart } from './journal-chart.js?v=ha-0.1.13';
import { filterTrades, netPnl, summarize, dateRange, easternToday } from './analytics.js?v=ha-0.1.13';
import { mountImport } from './import.js?v=ha-0.1.13';
import { mountReports, renderReports } from './reports.js?v=ha-0.1.13';
import { mountMarket } from './market.js?v=ha-0.1.13';
import { mountMonitor } from './market-monitor.js?v=chat-only-1';

const $ = id => document.getElementById(id);
const money = (n, signed=false) => `${n < 0 ? '−' : signed && n > 0 ? '+' : ''}${new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(Math.abs(n))}`;
const number = n => new Intl.NumberFormat('en-US',{maximumFractionDigits:2}).format(n);
const tone = n => n < 0 ? 'negative' : n > 0 ? 'positive' : '';
const escape = s => String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

let trades = [], savedTrades = [], openPositions=[], selectedTrade = null, editingId = null, ready = false, hostReady=false;
let renderVersion=0;
mountReports();
mountTraderChat();
mountChatConnection();
const journalChart=mountJournalChart($('equity-chart'));
mountMarket();
mountMonitor();
const reportStatus=document.createElement('div');reportStatus.id='report-status';reportStatus.setAttribute('role','status');$('filters').after(reportStatus);
const accountSource=document.createElement('p');accountSource.id='account-source';accountSource.className='small muted';accountSource.setAttribute('role','status');document.querySelector('.journal-toolbar').after(accountSource);
const csvArchive=document.createElement('details');csvArchive.className='panel historical-positions';csvArchive.innerHTML='<summary>CSV archive tools</summary><p>CSV imports and manual archive records are stored separately from the transaction-based account dashboard.</p><div class="connection-actions"></div>';
csvArchive.querySelector('.connection-actions').append($('import-csv'),$('add-trade'));$('settings').append(csvArchive);
const exportLink=document.createElement('a');exportLink.id='export-trades';exportLink.className='quiet-button';exportLink.textContent='Export filtered CSV';exportLink.setAttribute('download','');$('backup').before(exportLink);
exportLink.addEventListener('click',e=>{if(exportLink.getAttribute('aria-disabled')==='true')e.preventDefault();});
$('period').replaceChildren(...[['all','All dates'],['today','Today'],['yesterday','Yesterday'],['week','This week'],['month','This month'],['30days','Last 30 days'],['custom','Custom range']].map(([value,label])=>new Option(label,value)));
const positionsPanel=document.createElement('article');positionsPanel.className='panel positions-panel';positionsPanel.id='open-position-panel';positionsPanel.hidden=true;$('metrics').after(positionsPanel);
const accountLabel=document.createElement('label');accountLabel.textContent='Account';
const accountSelect=document.createElement('select');accountSelect.id='account';accountLabel.append(accountSelect);
$('symbol').closest('label').before(accountLabel);
mountImport({onImported:async()=>{await loadSaved();resetFilters();}});
mountAccountSync({onSynced:async()=>{await loadSaved();trades=savedTrades;refreshOptions();await render();}});
function refreshOptions() {
  for(const [id,key,title] of [['account','account','All accounts'],['symbol','symbol','All instruments']]) {
    const previous=$(id).value; $(id).replaceChildren(new Option(title,''));
    const records=[...trades,...openPositions];
    for(const value of [...new Set(records.map(t=>t[key]??'Personal'))].sort()) $(id).add(new Option(value,value));
    if([...$(id).options].some(o=>o.value===previous))$(id).value=previous;
  }
}
async function loadSaved() {
  const response = await fetchApp('/api/account/dashboard',{cache:'no-store'});if(!response.ok)throw new Error('Could not load the transaction-based account dashboard.');
  applyAccount(await response.json());
}
function applyAccount(state){savedTrades=state.trades??[];openPositions=state.openPositions??[];ready=!!state.syncedAt;accountSource.innerHTML=renderAccountSource(state);}
function resetFilters() {
  trades=savedTrades;
  $('add-trade').disabled=!hostReady; $('backup').hidden=!hostReady; $('import-csv').disabled=!hostReady;
  document.querySelector('header .demo-badge').textContent='ACCOUNT';
  document.querySelector('.sidebar-bottom .small').textContent='Schwab transaction history';
  $('period').value='all';$('start').value='';$('end').value='';
  for(const id of ['account','symbol','direction'])$(id).value='';refreshOptions();render();
}
for (const button of document.querySelectorAll('[data-tab]')) button.addEventListener('click',()=>{
  const tab = button.dataset.tab; location.hash=tab; document.querySelector('header .demo-badge').textContent=tab==='settings'?'SETTINGS':tab==='market'?'MARKET':tab==='trader'?'TRADER':'ACCOUNT';
  for(const id of ['journal','market','trader','settings'])$(id).hidden=tab!==id;
  $('breadcrumb').textContent = tab==='settings'?'SETTINGS':tab==='journal'?'ACCOUNT DASHBOARD':tab==='trader'?'TRADER CHAT':'MARKET MONITOR';
  document.querySelectorAll('[data-tab]').forEach(b=>{ b.classList.toggle('active',b===button); b.setAttribute('aria-current',b===button?'page':'false'); });
});

async function render() {
  const version=++renderVersion;
  const filters=Object.fromEntries(['start','end','account','symbol','direction'].map(k=>[k,$(k).value]));
  const invalid=filters.start && filters.end && filters.start>filters.end;
  $('filter-error').textContent=invalid?'The start date must be on or before the end date.':'';
  const selected=invalid?[]:filterTrades(trades,filters);let summary=summarize(selected);
  exportLink.hidden=!ready;exportLink.setAttribute('aria-disabled','true');
  const query=new URLSearchParams(Object.entries(filters).filter(([,value])=>value));
  reportStatus.textContent=invalid?'':ready?'Updating selected range…':'';
  if(ready&&!invalid) {
    try {const response=await fetchApp(`/api/account/analytics?${query}`);if(!response.ok)throw new Error('Could not calculate the selected range.');summary=await response.json();if(version!==renderVersion)return;reportStatus.textContent='Schwab transactions · selected closed trades · net P/L after fees';exportLink.href=appUrl(`/api/account/trades/export?${query}`);exportLink.setAttribute('aria-disabled','false');}
    catch(error){if(version!==renderVersion)return;reportStatus.textContent=`${error.message} Showing calculations from loaded trades; export is unavailable.`;}
  }
  if(version!==renderVersion)return;
  const positions=openPositions.filter(p=>(!filters.account||p.account===filters.account)&&(!filters.symbol||p.symbol===filters.symbol));
  positionsPanel.hidden=positions.length===0;
  positionsPanel.innerHTML=renderAccountPositions(positions);
  $('metrics').innerHTML=renderJournalMetrics(summary);
  journalChart.update(summary.curve,query.toString()); $('chart-count').textContent=`${summary.count} trades`; $('trade-count').textContent=summary.count;
  renderReports(summary);
  $('trade-rows').innerHTML=selected.length?[...selected].reverse().map(t=>`<tr><td class="date-cell">${t.date.slice(0,10)}<small>${t.time}</small></td><td><strong>${escape(t.symbol)}</strong></td><td><span class="direction ${t.direction==='SHORT'?'short':''}">${t.direction}</span></td><td class="numeric">${t.quantity}</td><td class="numeric">${number(t.entry)}</td><td class="numeric">${number(t.exit)}</td><td class="numeric muted">${money(t.fees)}</td><td class="numeric ${tone(netPnl(t))}">${money(netPnl(t),true)}</td><td><button class="detail-button" data-trade="${t.id}" aria-label="Review ${escape(t.symbol)} trade on ${t.date} at ${t.time}">↗</button></td></tr>`).join(''):`<tr><td colspan="9" class="empty">${savedTrades.length===0?'No closed trades in the available Schwab transaction history.':'No matching trades. Try another date range or reset the filters.'}</td></tr>`;
}
$('filters').addEventListener('submit',e=>e.preventDefault());
for (const id of ['start','end','account','symbol','direction']) $(id).addEventListener('change',()=>{if(id==='start'||id==='end')$('period').value='custom';render();});
$('period').addEventListener('change',()=>{ const anchor=easternToday();const dates=dateRange($('period').value,anchor);if(dates){[$('start').value,$('end').value]=dates;render();} });
$('reset').addEventListener('click',()=>resetFilters());
$('trade-rows').addEventListener('click',async e=>{ const button=e.target.closest('[data-trade]');if(!button)return; const t=trades.find(t=>t.id===button.dataset.trade);
  selectedTrade=t;$('edit-trade').hidden=false;$('detail-mode').textContent=`${t.source??'Manual'} · ${t.account??'Personal'}`;
  $('edit-trade').textContent=['CSV','Schwab API'].includes(t.source)?'Edit journal notes':'Edit trade';
  $('trade-detail').innerHTML=`<h1>${escape(t.symbol)}</h1><p>${t.date} · ${t.time} ET · ${t.direction}</p><div class="metric-value ${tone(netPnl(t))}">${money(netPnl(t),true)}</div><div class="detail-grid">${[['Quantity',t.quantity],['Fees',money(t.fees)],['Entry',number(t.entry)],['Exit',number(t.exit)],['Planned risk',t.risk?money(t.risk):'Not recorded'],['Net R',t.risk?(netPnl(t)/t.risk).toFixed(2)+'R':'Not available']].map(([k,v])=>`<div><small>${k}</small>${escape(v)}</div>`).join('')}</div><p class="trade-note">${escape(t.notes)}</p>`; $('trade-dialog').showModal();
  {const section=document.createElement('div');section.className='fills-preview';section.textContent='Loading allocated fills…';$('trade-detail').append(section);try{const response=await fetchApp(`/api/account/trades/${t.id}/fills`);if(!response.ok)throw new Error();const fills=await response.json();section.innerHTML=`<h2>Allocated fills</h2><div class="table-wrap"><table><thead><tr><th>Role</th><th>Time ET</th><th class="numeric">Qty</th><th class="numeric">Price</th><th class="numeric">Fees</th></tr></thead><tbody>${fills.map(({role,execution:f})=>`<tr><td>${escape(role)}${f.externalId?.startsWith('tos:cash-settlement:')?' · Cash settlement':''}</td><td>${escape((f.timestamp??f.localTimeEastern??'').replace('T',' '))}</td><td class="numeric">${number(f.quantity)}</td><td class="numeric">${number(f.price)}</td><td class="numeric">${money(f.fees)}</td></tr>`).join('')}</tbody></table></div>${['CSV','Schwab API'].includes(t.source)?'<p class="small muted">A reversal execution may be split across two trades. Accounting comes from the allocated fills; average prices are for display.</p>':''}`;}catch{section.textContent='Could not load fills.';}}
});
$('close-dialog').addEventListener('click',()=>$('trade-dialog').close());
$('trade-dialog').addEventListener('click',e=>{if(e.target===$('trade-dialog')){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)e.target.close();}});
const form=$('trade-form');
const field=name=>form.elements.namedItem(name);
let presets=[];
function openEntry(trade=null) {
  editingId=trade?.id??null;form.reset();$('form-error').textContent='';$('entry-title').textContent=trade?'Edit completed trade':'Add completed trade';
  if(trade){for(const key of ['symbol','direction','quantity','openedAt','entry','exit','multiplier','fees','notes','risk'])field(key).value=trade[key]??'';field('closedAt').value=`${trade.date}T${trade.time}`;}
  const match=trade?presets.find(p=>p.assetType===trade.assetType&&p.pointValue===trade.multiplier&&(p.root==='EQUITY'||trade.symbol.replace(/^\//,'').startsWith(p.root))):null;
  $('instrument-preset').value=trade?(match?.root??'CUSTOM'):'EQUITY';field('multiplier').readOnly=$('instrument-preset').value==='EQUITY';
  const imported=['CSV','Schwab API'].includes(trade?.source);
  for(const key of ['symbol','quantity','openedAt','closedAt','entry','exit','multiplier','fees'])field(key).readOnly=imported||(key==='multiplier'&&$('instrument-preset').value==='EQUITY');
  for(const key of ['openedAt','closedAt'])field(key).step='1';
  field('direction').disabled=imported;$('instrument-preset').disabled=imported;
  $('entry-title').textContent=imported?'Edit imported trade journal':trade?'Edit completed trade':'Add completed trade';
  $('entry-dialog').querySelector(':scope > p').textContent=imported?'Imported accounting is read-only. Edit notes and initial planned risk here. All displayed times are Eastern.':'Manual entry supports one entry and exit in USD. All times are Eastern. Use execution CSV import for scaling and partial exits.';
  $('entry-dialog').showModal();field('symbol').focus();
}
$('add-trade').addEventListener('click',()=>openEntry());
$('edit-trade').addEventListener('click',()=>{$('trade-dialog').close();openEntry(selectedTrade);});
for(const id of ['close-entry','cancel-entry'])$(id).addEventListener('click',()=>$('entry-dialog').close());
$('instrument-preset').addEventListener('change',()=>{const preset=presets.find(p=>p.root===$('instrument-preset').value);field('multiplier').readOnly=preset?.assetType==='Equity';if(preset)field('multiplier').value=preset.pointValue;});
form.addEventListener('submit',async e=>{
  e.preventDefault();$('save-trade').disabled=true;$('form-error').textContent='';
  const payload=Object.fromEntries(new FormData(form));
  [payload.date,payload.time]=payload.closedAt.split('T');delete payload.closedAt;
  for(const key of ['quantity','entry','exit','multiplier','fees'])payload[key]=Number(payload[key]);
  payload.risk=payload.risk?Number(payload.risk):null;payload.assetType=$('instrument-preset').value==='EQUITY'?'Equity':'Futures';
  try {
    const response=await fetchApp(editingId?`/api/account/trades/${editingId}`:'/api/trades',{method:editingId?'PUT':'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
    if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.errors?Object.values(problem.errors).flat().join(' '):'Could not save the trade.');}
    await response.json();await loadSaved();trades=savedTrades;
    $('entry-dialog').close();refreshOptions();$('period').value='all';$('start').value='';$('end').value='';for(const id of ['account','symbol','direction'])$(id).value='';render();
  }catch(error){$('form-error').textContent=error.message;}finally{$('save-trade').disabled=false;}
});
try { const response=await fetchApp('/api/status');if(!response.ok)throw new Error('Start the .NET host to use saved trades.');hostReady=true;presets=(await response.json()).instruments;for(const preset of presets){if(![...$('instrument-preset').options].some(o=>o.value===preset.root))$('instrument-preset').add(new Option(`${preset.root} · $${preset.pointValue} / point`,preset.root));}await loadSaved(); }
catch(error){$('load-error').textContent=`${error.message} Check that the local host is running.`;}
resetFilters();

const accountObserver=observeAccountDashboard({load:async()=>{const response=await fetchApp('/api/account/dashboard',{cache:'no-store'});if(!response.ok)throw new Error('Could not refresh account transactions.');return response.json();},visible:()=>!document.hidden&&!$('journal').hidden,onStatus:state=>{accountSource.innerHTML=renderAccountSource(state);},onChange:async state=>{applyAccount(state);trades=savedTrades;refreshOptions();await render();},onError:error=>{accountSource.textContent=error.message+' Displayed data is the last transaction-based snapshot.';}});
document.addEventListener('visibilitychange',()=>accountObserver.update());
new MutationObserver(()=>accountObserver.update()).observe($('journal'),{attributes:true,attributeFilter:['hidden']});
window.addEventListener('pagehide',()=>accountObserver.dispose());
accountObserver.update();
