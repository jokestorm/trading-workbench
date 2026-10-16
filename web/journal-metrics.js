const money=(value,signed=false)=>`${value<0?'−':signed&&value>0?'+':''}${new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(Math.abs(value))}`;
const tone=value=>value<0?'negative':value>0?'positive':'';

export function renderJournalMetrics(summary){
  const cards=[
    ['Gross P/L',money(summary.gross,true),`${summary.count} closed trades · before fees`,tone(summary.gross)],
    ['Fees & commissions',money(summary.fees),'Closed-trade costs for this range',''],
    ['Win rate',summary.winRate===null?'—':`${(summary.winRate*100).toFixed(1)}%`,`${summary.wins} wins / ${summary.losses} losses / ${summary.breakEven} flat · after fees`,''],
    ['Expectancy',summary.expectancy===null?'—':money(summary.expectancy,true),'Average net P/L per trade',summary.expectancy===null?'':tone(summary.expectancy)],
    ['Max drawdown',money(summary.drawdown),'Selected net P/L curve · after fees','']
  ];
  return cards.map(([label,value,caption,color])=>`<article class="metric"><div class="metric-label">${label}</div><div class="metric-value ${color}">${value}</div><div class="metric-caption">${caption}</div></article>`).join('');
}
