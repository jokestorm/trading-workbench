const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number=value=>Number.isFinite(value)?new Intl.NumberFormat('en-US',{maximumFractionDigits:6}).format(value):'—';
const money=value=>Number.isFinite(value)?new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(value):'—';
export function renderStatementPositions(positions){
  return `<details class="historical-positions"><summary>Historical CSV positions · ${positions.length}</summary><p class="small muted">Positions left open in imported CSV history. These are historical records, not your current broker holdings. Use Market monitor → Schwab transaction position for the latest derived estimate.</p><div class="table-wrap"><table><thead><tr><th>Account</th><th>Instrument</th><th>Direction</th><th class="numeric">Qty</th><th class="numeric">Imported average entry</th><th>Latest recorded fill (ET)</th><th class="numeric">Entry fees</th></tr></thead><tbody>${positions.map(p=>{
    const latest=(p.executions??[]).map(fill=>fill.timestamp).filter(Boolean).sort().at(-1);
    return `<tr><td>${escape(p.account)}</td><td>${escape(p.symbol)}</td><td>${p.quantity>0?'LONG':'SHORT'}</td><td class="numeric">${number(Math.abs(p.quantity))}</td><td class="numeric">${number(p.averageEntry)}</td><td>${escape(latest?latest.replace('T',' '):'Unavailable')}</td><td class="numeric">${money(p.entryFees)}</td></tr>`;
  }).join('')}</tbody></table></div></details>`;
}
