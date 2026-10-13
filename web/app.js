import { fetchApp, appUrl } from './http.js';
import { filterTrades, netPnl, summarize, dateRange, easternToday } from './analytics.js?v=decisions-2';
import { mountImport } from './import.js?v=decisions-2';
import { mountReports, renderReports } from './reports.js?v=decisions-2';
import { mountMarket } from './market.js?v=decisions-2';
import { mountAI } from './ai.js?v=decisions-2';
import { mountMonitor } from './market-monitor.js?v=decisions-2';

const $ = id => document.getElementById(id);
const money = (n, signed=false) => `${n < 0 ? '−' : signed && n > 0 ? '+' : ''}${new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(Math.abs(n))}`;
const number = n => new Intl.NumberFormat('en-US',{maximumFractionDigits:2}).format(n);
const tone = n => n < 0 ? 'negative' : n > 0 ? 'positive' : '';
const escape = s => String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

let trades = [], savedTrades = [], openPositions=[], selectedTrade = null, editingId = null, ready = false;
let renderVersion=0;
mountReports();
mountMarket();
mountMonitor();
const reportStatus=document.createElement('div');reportStatus.id='report-status';reportStatus.setAttribute('role','status');$('filters').after(reportStatus);
const exportLink=document.createElement('a');exportLink.id='export-trades';exportLink.className='quiet-button';exportLink.textContent='Export filtered CSV';exportLink.setAttribute('download','');$('backup').before(exportLink);
exportLink.addEventListener('click',e=>{if(exportLink.getAttribute('aria-disabled')==='true')e.preventDefault();});
$('period').replaceChildren(...[['all','All dates'],['today','Today'],['yesterday','Yesterday'],['week','This week'],['month','This month'],['30days','Last 30 days'],['custom','Custom range']].map(([value,label])=>new Option(label,value)));
const positionsPanel=document.createElement('article');positionsPanel.className='panel positions-panel';positionsPanel.id='open-position-panel';positionsPanel.hidden=true;$('metrics').after(positionsPanel);
const accountLabel=document.createElement('label');accountLabel.textContent='Account';
const accountSelect=document.createElement('select');accountSelect.id='account';accountLabel.append(accountSelect);
$('symbol').closest('label').before(accountLabel);
mountAI({getFilters:()=>Object.fromEntries(['start','end','account','symbol','direction','setup'].map(k=>[k,$(k).value]))});
mountImport({onImported:async()=>{await loadSaved();resetFilters();}});
function refreshOptions() {
  for(const [id,key,title] of [['account','account','All accounts'],['symbol','symbol','All instruments'],['setup','setup','All setups']]) {
    const previous=$(id).value; $(id).replaceChildren(new Option(title,''));
    const records=key==='setup'?trades:[...trades,...openPositions];
    for(const value of [...new Set(records.map(t=>t[key]??'Personal'))].sort()) $(id).add(new Option(value,value));
    if([...$(id).options].some(o=>o.value===previous))$(id).value=previous;
  }
}
async function loadSaved() {
  const response = await fetchApp('/api/trades'); if(!response.ok)throw new Error('Could not load saved trades. Check that the .NET host is running.');
  savedTrades = await response.json(); ready=true;
  const positionsResponse=await fetchApp('/api/positions');if(positionsResponse.ok)openPositions=await positionsResponse.json();
}
function resetFilters() {
  trades=savedTrades;
  $('add-trade').disabled=!ready; $('backup').hidden=!ready; $('import-csv').disabled=!ready;
  document.querySelector('header .demo-badge').textContent='MY TRADES';
  document.querySelector('.sidebar-bottom .small').textContent='Stored on this computer';
  $('period').value='all';$('start').value='';$('end').value='';
  for(const id of ['account','symbol','direction','setup'])$(id).value='';refreshOptions();render();
}
for (const button of document.querySelectorAll('[data-tab]')) button.addEventListener('click',()=>{
  const tab = button.dataset.tab; location.hash=tab; document.querySelector('header .demo-badge').textContent=tab==='market'?'MARKET':'MY TRADES';
  $('journal').hidden = tab !== 'journal'; $('market').hidden = tab !== 'market';
  $('breadcrumb').textContent = tab === 'journal' ? 'TRADE JOURNAL' : 'MARKET MONITOR';
  document.querySelectorAll('[data-tab]').forEach(b=>{ b.classList.toggle('active',b===button); b.setAttribute('aria-current',b===button?'page':'false'); });
});

function chart(curve) {
  if (!curve.length) return '<div class="empty">No trades in this range. Adjust your filters.</div>';
  const width=740,height=220,left=62,right=18,top=15,bottom=35;
  const values=[0,...curve.map(p=>p.value)];
  let min=Math.min(...values),max=Math.max(...values); const span=Math.max(50,max-min); min-=span*.1; max+=span*.1;
  const x=i=>left+i/(values.length-1)*(width-left-right), y=v=>top+(max-v)/(max-min)*(height-top-bottom);
  const points=values.map((v,i)=>`${x(i)},${y(v)}`).join(' ');
  const ticks=Array.from({length:5},(_,i)=>min+(max-min)*i/4).map(v=>`<line x1="${left}" x2="${width-right}" y1="${y(v)}" y2="${y(v)}" stroke="#263143" stroke-dasharray="3 5"/><text x="${left-10}" y="${y(v)+4}" text-anchor="end" fill="#8191a9" font-size="10">${money(v).replace('.00','')}</text>`).join('');
  const first=curve[0].date.slice(5).replace('-','/'),last=curve.at(-1).date.slice(5).replace('-','/');
  return `<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="Cumulative realized net profit and loss, ending at ${escape(money(curve.at(-1).value))}"><defs><linearGradient id="area" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#77d6b0" stop-opacity=".18"/><stop offset="1" stop-color="#77d6b0" stop-opacity="0"/></linearGradient></defs>${ticks}<line x1="${left}" x2="${width-right}" y1="${y(0)}" y2="${y(0)}" stroke="#53617a"/><polygon points="${left},${height-bottom} ${points} ${width-right},${height-bottom}" fill="url(#area)"/><polyline points="${points}" fill="none" stroke="#77d6b0" stroke-width="2.5" stroke-linejoin="round"/>${curve.map((p,i)=>`<circle cx="${x(i+1)}" cy="${y(p.value)}" r="3" fill="#77d6b0"><title>${p.date}: ${money(p.value)}</title></circle>`).join('')}<text x="${left}" y="${height-9}" fill="#8191a9" font-size="10">${first}</text><text x="${width-right}" y="${height-9}" text-anchor="end" fill="#8191a9" font-size="10">${last}</text></svg>`;
}
async function render() {
  const version=++renderVersion;
  const filters=Object.fromEntries(['start','end','account','symbol','direction','setup'].map(k=>[k,$(k).value]));
  const invalid=filters.start && filters.end && filters.start>filters.end;
  $('filter-error').textContent=invalid?'The start date must be on or before the end date.':'';
  const selected=invalid?[]:filterTrades(trades,filters);let summary=summarize(selected);
  exportLink.hidden=!ready;exportLink.setAttribute('aria-disabled','true');
  const query=new URLSearchParams(Object.entries(filters).filter(([,value])=>value));
  reportStatus.textContent=invalid?'':ready?'Updating selected range…':'';
  if(ready&&!invalid) {
    try {const response=await fetchApp(`/api/analytics?${query}`);if(!response.ok)throw new Error('Could not calculate the selected range.');summary=await response.json();if(version!==renderVersion)return;reportStatus.textContent='Selected range · realized net P/L after fees';exportLink.href=appUrl(`/api/trades/export?${query}`);exportLink.setAttribute('aria-disabled','false');}
    catch(error){if(version!==renderVersion)return;reportStatus.textContent=`${error.message} Showing calculations from loaded trades; export is unavailable.`;}
  }
  if(version!==renderVersion)return;
  const positions=openPositions.filter(p=>(!filters.account||p.account===filters.account)&&(!filters.symbol||p.symbol===filters.symbol));
  positionsPanel.hidden=positions.length===0;
  positionsPanel.innerHTML=`<div class="panel-heading"><div><h2>Open positions · statement snapshot</h2><p>Excluded from closed-trade metrics. Latest imported statement; date filters do not change this snapshot.</p></div></div><div class="table-wrap"><table><thead><tr><th>Account</th><th>Instrument</th><th>Direction</th><th class="numeric">Qty</th><th class="numeric">Average entry</th><th class="numeric">Entry fees</th></tr></thead><tbody>${positions.map(p=>`<tr><td>${escape(p.account)}</td><td>${escape(p.symbol)}</td><td>${p.quantity>0?'LONG':'SHORT'}</td><td class="numeric">${Math.abs(p.quantity)}</td><td class="numeric">${number(p.averageEntry)}</td><td class="numeric">${money(p.entryFees)}</td></tr>`).join('')}</tbody></table></div>`;
  const cards=[['Net P/L',money(summary.net,true),`${summary.count} closed trades · ${money(summary.fees)} fees`,tone(summary.net)],['Win rate',summary.winRate===null?'—':`${(summary.winRate*100).toFixed(1)}%`,`${summary.wins} wins / ${summary.losses} losses / ${summary.breakEven} flat`,''],['Expectancy',summary.expectancy===null?'—':money(summary.expectancy,true),'Average net P/L per trade',summary.expectancy===null?'':tone(summary.expectancy)],['Max drawdown',money(summary.drawdown),'Selected realized P/L curve','']];
  $('metrics').innerHTML=cards.map(([label,value,caption,color])=>`<article class="metric"><div class="metric-label">${label}</div><div class="metric-value ${color}">${value}</div><div class="metric-caption">${caption}</div></article>`).join('');
  $('equity-chart').innerHTML=chart(summary.curve); $('chart-count').textContent=`${summary.count} trades`; $('trade-count').textContent=summary.count;
  renderReports(summary);
  $('trade-rows').innerHTML=selected.length?[...selected].reverse().map(t=>`<tr><td class="date-cell">${t.date.slice(0,10)}<small>${t.time}</small></td><td><strong>${escape(t.symbol)}</strong></td><td><span class="direction ${t.direction==='SHORT'?'short':''}">${t.direction}</span></td><td class="setup-tag">${escape(t.setup)}</td><td class="numeric">${t.quantity}</td><td class="numeric">${number(t.entry)}</td><td class="numeric">${number(t.exit)}</td><td class="numeric muted">${money(t.fees)}</td><td class="numeric ${tone(netPnl(t))}">${money(netPnl(t),true)}</td><td><button class="detail-button" data-trade="${t.id}" aria-label="Review ${escape(t.symbol)} trade on ${t.date} at ${t.time}">↗</button></td></tr>`).join(''):`<tr><td colspan="10" class="empty">${savedTrades.length===0?'Your journal is empty. Import a statement or add your first completed trade.':'No matching trades. Try another date range or reset the filters.'}</td></tr>`;
}
$('filters').addEventListener('submit',e=>e.preventDefault());
for (const id of ['start','end','account','symbol','direction','setup']) $(id).addEventListener('change',()=>{if(id==='start'||id==='end')$('period').value='custom';render();});
$('period').addEventListener('change',()=>{ const anchor=easternToday();const dates=dateRange($('period').value,anchor);if(dates){[$('start').value,$('end').value]=dates;render();} });
$('reset').addEventListener('click',()=>resetFilters());
$('trade-rows').addEventListener('click',async e=>{ const button=e.target.closest('[data-trade]');if(!button)return; const t=trades.find(t=>t.id===button.dataset.trade);
  selectedTrade=t;$('edit-trade').hidden=false;$('detail-mode').textContent=`${t.source??'Manual'} · ${t.account??'Personal'}`;
  $('edit-trade').textContent=t.source==='CSV'?'Edit journal notes':'Edit trade';
  $('trade-detail').innerHTML=`<h1>${escape(t.symbol)}</h1><p>${t.date} · ${t.time} ET · ${t.direction} · ${escape(t.setup)}</p><div class="metric-value ${tone(netPnl(t))}">${money(netPnl(t),true)}</div><div class="detail-grid">${[['Quantity',t.quantity],['Fees',money(t.fees)],['Entry',number(t.entry)],['Exit',number(t.exit)],['Planned risk',t.risk?money(t.risk):'Not recorded'],['Net R',t.risk?(netPnl(t)/t.risk).toFixed(2)+'R':'Not available']].map(([k,v])=>`<div><small>${k}</small>${escape(v)}</div>`).join('')}</div><p class="trade-note">${escape(t.notes)}</p>`; $('trade-dialog').showModal();
  {const section=document.createElement('div');section.className='fills-preview';section.textContent='Loading allocated fills…';$('trade-detail').append(section);try{const response=await fetchApp(`/api/trades/${t.id}/fills`);if(!response.ok)throw new Error();const fills=await response.json();section.innerHTML=`<h2>Allocated fills</h2><div class="table-wrap"><table><thead><tr><th>Role</th><th>Time ET</th><th class="numeric">Qty</th><th class="numeric">Price</th><th class="numeric">Fees</th></tr></thead><tbody>${fills.map(({role,execution:f})=>`<tr><td>${escape(role)}</td><td>${escape((f.timestamp??f.localTimeEastern??'').replace('T',' '))}</td><td class="numeric">${number(f.quantity)}</td><td class="numeric">${number(f.price)}</td><td class="numeric">${money(f.fees)}</td></tr>`).join('')}</tbody></table></div>${t.source==='CSV'?'<p class="small muted">A reversal execution may be split across two trades. Accounting comes from the allocated fills; average prices are for display.</p>':''}`;}catch{section.textContent='Could not load fills.';}}
});
$('close-dialog').addEventListener('click',()=>$('trade-dialog').close());
$('trade-dialog').addEventListener('click',e=>{if(e.target===$('trade-dialog')){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)e.target.close();}});
const form=$('trade-form');
const field=name=>form.elements.namedItem(name);
let presets=[];
function openEntry(trade=null) {
  editingId=trade?.id??null;form.reset();$('form-error').textContent='';$('entry-title').textContent=trade?'Edit completed trade':'Add completed trade';
  if(trade){for(const key of ['symbol','direction','quantity','openedAt','entry','exit','multiplier','fees','setup','notes','risk'])field(key).value=trade[key]??'';field('closedAt').value=`${trade.date}T${trade.time}`;}
  const match=trade?presets.find(p=>p.assetType===trade.assetType&&p.pointValue===trade.multiplier&&(p.root==='EQUITY'||trade.symbol.replace(/^\//,'').startsWith(p.root))):null;
  $('instrument-preset').value=trade?(match?.root??'CUSTOM'):'EQUITY';field('multiplier').readOnly=$('instrument-preset').value==='EQUITY';
  const imported=trade?.source==='CSV';
  for(const key of ['symbol','quantity','openedAt','closedAt','entry','exit','multiplier','fees'])field(key).readOnly=imported||(key==='multiplier'&&$('instrument-preset').value==='EQUITY');
  for(const key of ['openedAt','closedAt'])field(key).step='1';
  field('direction').disabled=imported;$('instrument-preset').disabled=imported;
  $('entry-title').textContent=imported?'Edit imported trade journal':trade?'Edit completed trade':'Add completed trade';
  $('entry-dialog').querySelector(':scope > p').textContent=imported?'Imported accounting is read-only. Edit setup, notes and initial planned risk here. All displayed times are Eastern.':'Manual entry supports one entry and exit in USD. All times are Eastern. Use execution CSV import for scaling and partial exits.';
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
    const response=await fetchApp(editingId?`/api/trades/${editingId}`:'/api/trades',{method:editingId?'PUT':'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
    if(!response.ok){const problem=await response.json().catch(()=>null);throw new Error(problem?.errors?Object.values(problem.errors).flat().join(' '):'Could not save the trade.');}
    const saved=await response.json();savedTrades=savedTrades.filter(t=>t.id!==saved.id).concat(saved);trades=savedTrades;
    $('entry-dialog').close();refreshOptions();$('period').value='all';$('start').value='';$('end').value='';for(const id of ['account','symbol','direction','setup'])$(id).value='';render();
  }catch(error){$('form-error').textContent=error.message;}finally{$('save-trade').disabled=false;}
});
try { const response=await fetchApp('/api/status');if(!response.ok)throw new Error('Start the .NET host to use saved trades.');presets=(await response.json()).instruments;for(const preset of presets){if(![...$('instrument-preset').options].some(o=>o.value===preset.root))$('instrument-preset').add(new Option(`${preset.root} · $${preset.pointValue} / point`,preset.root));}await loadSaved(); }
catch(error){$('load-error').textContent=`${error.message} Check that the local host is running.`;}
resetFilters();
