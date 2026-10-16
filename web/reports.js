const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money=n=>new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(n);
const tone=n=>n<0?'negative':n>0?'positive':'';
const empty='<p class="small muted">No matching closed trades.</p>';
function bars(items,distribution=false) {
  const scale=Math.max(1,...items.map(g=>Math.abs(distribution?g.count:g.net)));
  return items.map(g=>{const value=distribution?g.count:g.net,color=distribution?(g.kind==='loss'?'var(--red)':g.kind==='win'?'var(--green)':'var(--muted)'):value<0?'var(--red)':'var(--green)';
    return `<div class="breakdown-row" title="${escape(distribution?g.range:`${g.key}: ${g.count} trades · fees ${money(g.fees)}`)}"><div class="breakdown-row-head"><span>${escape(distribution?g.label:g.key)}${distribution?'':` <small class="muted">· ${g.count}</small>`}</span><span class="${distribution?'':tone(value)}">${distribution?value:money(value)}</span></div><div class="bar-track"><div class="bar-fill" style="width:${Math.abs(value)/scale*100}%;background:${color}"></div></div></div>`;}).join('');
}
export function mountReports() {
  const section=document.createElement('section');section.className='reports';section.setAttribute('aria-label','Selected range reports');
  section.innerHTML='<div id="range-statistics" class="range-statistics"></div><div class="report-grid"><article class="panel"><div class="panel-heading"><div><h2>Daily net P/L</h2><p>Closed dates in Eastern time · days with trades</p></div></div><div id="daily-report" class="report-scroll"></div></article><article class="panel"><div class="panel-heading"><div><h2>By instrument</h2><p>Realized results after fees</p></div></div><div id="instrument-report" class="report-scroll"></div></article><article class="panel"><div class="panel-heading"><div><h2>Trade P/L distribution</h2><p>Trade counts · fixed dollar ranges</p></div></div><div id="distribution-report"></div></article></div><p class="small muted">Dates filter by trade closure. Equity and drawdown start at $0 for this range. R uses net P/L divided by recorded initial risk; missing risk is excluded.</p>';
  document.querySelector('.trade-panel').before(section);
}
export function renderReports(s) {
  const optional=(v,format=money)=>v===null?'—':format(v),r=v=>`${v.toFixed(2)}R`;
  const stats=[['Net P/L after fees',money(s.net),'Gross P/L less fees and commissions'],['Profit factor',s.profitFactorIsInfinite?'∞':optional(s.profitFactor,v=>v.toFixed(2)),'Winning net / losing net'],['Average winner / loser',`${optional(s.averageWinner)} / ${optional(s.averageLoser)}`,'Break-even trades excluded'],['Current drawdown',money(s.currentDrawdown),'From the selected range peak'],['Mean / median R',`${optional(s.averageR,r)} / ${optional(s.medianR,r)}`,`${s.riskCount} of ${s.count} trades have risk (${s.riskCoverage===null?'—':(s.riskCoverage*100).toFixed(0)+'%'})`]];
  document.getElementById('range-statistics').innerHTML=stats.map(([label,value,note])=>`<div><span class="small muted">${label}</span><strong>${value}</strong><span class="small muted">${note}</span></div>`).join('');
  document.getElementById('daily-report').innerHTML=s.count?bars(s.daily):empty;
  document.getElementById('instrument-report').innerHTML=s.count?bars(s.instruments):empty;
  document.getElementById('distribution-report').innerHTML=s.count?bars(s.distribution,true):empty;
}
