const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number=value=>value==null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:4}).format(value);
export function indicatorOverlays(report,{ema=true,bands=false}={}){
  const fields=[...(ema?[['ema9','EMA 9','#8db7ff'],['ema21','EMA 21','#dd9ef5']]:[]),...(bands?[['upperBand','BB upper','#708399'],['lowerBand','BB lower','#708399']]:[])];
  return fields.map(([key,label,color])=>({label,color,points:(report?.series??[]).filter(p=>p[key]!=null).map(p=>({time:p.time,value:p[key]}))}));
}
export function renderTechnicals(report,quote,{source='bars',now=Date.now()}={}){
  if(source!=='bars')return '<p class="small muted">Technical indicators need OHLCV bars. Select Minute bars above.</p>';
  if(!report?.values)return '<p class="small muted">No completed bars available for technical indicators yet.</p>';
  const v=report.values,age=now-Date.parse(report.asOf),quoteAge=now-Date.parse(quote?.quoteTime);
  const fresh=Number.isFinite(age)&&age>=-5000&&age<=120000&&quote?.realtime===true&&Number.isFinite(quoteAge)&&quoteAge>=-5000&&quoteAge<=120000;
  const alignment=(fast,slow)=>fast==null||slow==null?'Warming up':fast>slow?'Bullish alignment':fast<slow?'Bearish alignment':'Flat alignment';
  const cards=[
    ['EMA 9 / 21',`${number(v.ema9)} / ${number(v.ema21)}`,alignment(v.ema9,v.ema21)],
    ['SMA 20 / 50',`${number(v.sma20)} / ${number(v.sma50)}`,alignment(v.sma20,v.sma50)],
    ['RSI 14',number(v.rsi14),v.rsi14==null?'Needs 15 bars':v.rsi14>=70?'Overbought':v.rsi14<=30?'Oversold':v.rsi14>50?'Above midpoint':v.rsi14<50?'Below midpoint':'Neutral'],
    ['MACD 12 / 26 / 9',number(v.macdHistogram),`Histogram · line ${number(v.macd)} / signal ${number(v.macdSignal)}`],
    ['Bollinger 20 / 2',`${number(v.bollingerLower)} – ${number(v.bollingerUpper)}`,'Lower / upper bands'],
    ['ATR 14',number(v.atr14),'Typical one-minute range · points'],
    ['Relative volume',v.relativeVolume20==null?'—':number(v.relativeVolume20)+'×','Latest / preceding 20-bar average'],
    ['20-bar levels',`${number(v.support20)} / ${number(v.resistance20)}`,'Support / resistance · prior bars']
  ];
  const timestamp=new Date(report.asOf).toLocaleString('en-US',{timeZone:'America/New_York'});
  return `<div class="technical-heading"><h3>Technical indicators</h3><span class="small muted">${fresh?'Latest closed-bar readings':'Historical readings · fresh realtime data unverified'}</span></div><p class="small muted">${escape(timestamp)} ET · ${report.barsUsed} consecutive one-minute bars${report.barsBeforeGap?' · reset after a history gap':''} · indicators use latest bars, independent of chart zoom.</p><div class="technical-grid">${cards.map(([label,value,note])=>`<article><span>${escape(label)}</span><strong>${escape(value)}</strong><small>${escape(note)}</small></article>`).join('')}</div><div class="technical-signals"><strong>Observed signals</strong>${report.signals.length?report.signals.map(signal=>`<p class="technical-signal ${signal.direction==='UP'?'positive':signal.direction==='DOWN'?'negative':''}">${escape(signal.name)} · ${escape(signal.description)}</p>`).join(''):'<p class="small muted">No crossover, breakout, RSI extreme, or volume spike on the latest closed bar.</p>'}</div>`;
}
