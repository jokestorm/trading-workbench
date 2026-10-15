const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const price=value=>new Intl.NumberFormat('en-US',{maximumFractionDigits:4}).format(value);
const time=value=>new Date(value).toLocaleString('en-US',{timeZone:'America/New_York'})+' ET';

export function renderMarketSignal(run){
  const {snapshot,result}=run,d=result.decision;
  const heading=`<p class="small muted">${escape(snapshot.symbol??'Market')} · ${escape(time(run.createdAt))}</p>`;
  if(!d)return heading+'<div class="decision-call"><strong>WAIT</strong><p>No saved signal. Run Analyze for a new signal.</p></div>';
  const actions={BUY:'BUY — open long',SELL_SHORT:'SELL — open short',SELL_CLOSE:'SELL — close your long',BUY_COVER:'BUY — cover your short',HOLD:'HOLD',WAIT:'WAIT'};
  const trends={UP:'↑ UP',DOWN:'↓ DOWN',SIDEWAYS:'↔ SIDEWAYS',UNCLEAR:'UNCLEAR'};
  const p=snapshot.position;
  const position=p?`${p.state}${p.quantity?' · '+p.quantity+' contracts':''}${p.averageEntry!=null?' @ '+price(p.averageEntry):''}`:'Unknown';
  const levels=d.guarded?[]:[['Entry',d.entry],['Stop',d.stop],['Target',d.target]].filter(([,value])=>value!=null);
  const risk=!d.guarded&&d.entry!=null&&d.stop!=null&&snapshot.pointValue?Math.abs(d.entry-d.stop)*snapshot.pointValue:null;
  return heading+`<div class="decision-call"><p class="signal-trend">Trend: ${escape(trends[d.trend]??'UNCLEAR')}</p><strong>${escape(actions[d.action]??'WAIT')}</strong><p>${escape(d.reason)}</p><p class="small muted">Your position: ${escape(position)}</p>${levels.length?`<div class="decision-levels">${levels.map(([label,value])=>`<div><span>${label}</span><strong>${price(value)}</strong></div>`).join('')}</div>`:''}${risk!=null?`<p class="small muted">Stop-distance risk: $${risk.toFixed(2)} / contract</p>`:''}${!d.guarded&&result.invalidation?`<p class="small muted">Invalid if: ${escape(result.invalidation)}</p>`:''}</div>`;
}
