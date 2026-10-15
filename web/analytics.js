// Saved trades use authoritative decimal P/L supplied by the .NET API.
// The fallback is only for synthetic preview records.
export const netPnl = t => Number.isFinite(t.netPnl) ? t.netPnl : Math.round(((t.exit-t.entry)*(t.direction === 'LONG' ? 1 : -1)*t.quantity*t.multiplier-t.fees)*100)/100;
export function filterTrades(trades, filters) {
  return trades.filter(t => (!filters.start || t.date >= filters.start) && (!filters.end || t.date <= filters.end)
    && (!filters.account || (t.account??'Personal')===filters.account) && (!filters.symbol || t.symbol === filters.symbol) && (!filters.direction || t.direction === filters.direction)
    && (!filters.setup || t.setup === filters.setup)).sort((a,b) => `${a.date} ${a.time} ${a.id}`.localeCompare(`${b.date} ${b.time} ${b.id}`));
}
export function summarize(trades) {
  const values = trades.map(netPnl);
  const winners = values.filter(v=>v>0), losers = values.filter(v=>v<0);
  const net = Math.round(values.reduce((a,b)=>a+b,0)*100)/100;
  const grossWins = winners.reduce((a,b)=>a+b,0), grossLosses = -losers.reduce((a,b)=>a+b,0);
  let running = 0, peak = 0, drawdown = 0;
  const curve = trades.map((t,i) => { running = Math.round((running+values[i])*100)/100; peak=Math.max(peak,running); drawdown=Math.max(drawdown,peak-running); return {date:t.date,time:t.time,value:running,drawdown:peak-running}; });
  const average=items=>items.length?items.reduce((a,b)=>a+b,0)/items.length:null;
  const group=key=>[...new Set(trades.map(t=>t[key]))].map(value=>{const items=trades.filter(t=>t[key]===value),v=items.map(netPnl);return {key:value,count:items.length,net:Math.round(v.reduce((a,b)=>a+b,0)*100)/100,fees:items.reduce((s,t)=>s+t.fees,0),expectancy:average(v),winRate:v.filter(x=>x>0).length/items.length};});
  const r=trades.filter(t=>t.risk>0).map(t=>netPnl(t)/t.risk).sort((a,b)=>a-b);
  const labels=['< −1k','−1k…−250','−250…−100','−100…0','$0','0…100','100…250','250…1k','> 1k'];
  const ranges=['P/L < -$1,000','-$1,000 ≤ P/L < -$250','-$250 ≤ P/L < -$100','-$100 ≤ P/L < $0','P/L = $0','$0 < P/L ≤ $100','$100 < P/L ≤ $250','$250 < P/L ≤ $1,000','P/L > $1,000'];
  const counts=Array(9).fill(0);for(const v of values)counts[v< -1000?0:v< -250?1:v< -100?2:v<0?3:v===0?4:v<=100?5:v<=250?6:v<=1000?7:8]++;
  return { net, count:trades.length, wins:winners.length, losses:losers.length, winRate:trades.length ? winners.length/trades.length : null,
    expectancy:trades.length ? net/trades.length : null, profitFactor:grossLosses ? grossWins/grossLosses : null,
    fees:trades.reduce((s,t)=>s+t.fees,0), drawdown, currentDrawdown:peak-running, curve,breakEven:values.filter(v=>v===0).length,
    averageWinner:average(winners),averageLoser:average(losers),profitFactorIsInfinite:!grossLosses&&grossWins>0,
    daily:group('date').sort((a,b)=>a.key.localeCompare(b.key)),instruments:group('symbol').sort((a,b)=>b.net-a.net),setups:group('setup').sort((a,b)=>b.net-a.net),
    distribution:labels.map((label,i)=>({label,range:ranges[i],count:counts[i],kind:i<4?'loss':i===4?'flat':'win'})),
    riskCount:r.length,riskCoverage:trades.length?r.length/trades.length:null,totalR:r.length?r.reduce((a,b)=>a+b,0):null,averageR:average(r),medianR:r.length?(r[Math.floor((r.length-1)/2)]+r[Math.floor(r.length/2)])/2:null };
}

export function easternToday(now=new Date()) {
  const parts=new Intl.DateTimeFormat('en-US',{timeZone:'America/New_York',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(now);
  const part=type=>parts.find(p=>p.type===type).value;return `${part('year')}-${part('month')}-${part('day')}`;
}
export function dateRange(preset,anchor) {
  if(preset==='all')return ['',''];
  const date=new Date(`${anchor}T12:00:00Z`),iso=d=>d.toISOString().slice(0,10),shift=n=>{const d=new Date(date);d.setUTCDate(d.getUTCDate()+n);return iso(d);};
  if(preset==='today')return [anchor,anchor];
  if(preset==='yesterday')return [shift(-1),shift(-1)];
  if(preset==='week')return [shift(-((date.getUTCDay()+6)%7)),anchor];
  if(preset==='month')return [`${anchor.slice(0,7)}-01`,anchor];
  if(preset==='30days')return [shift(-29),anchor];
  return null;
}
