import test from 'node:test';
import assert from 'node:assert/strict';
import {indicatorOverlays,renderTechnicals} from '../web/technical-indicators.js';
import {priceChart,chartLayout} from '../web/market-monitor.js';
const now=Date.parse('2026-10-21T14:00:00Z');
const report={barsUsed:60,barsBeforeGap:10,asOf:new Date(now).toISOString(),values:{ema9:101,ema21:100,sma20:100,sma50:99,rsi14:60,macd:.5,macdSignal:.4,macdHistogram:.1,bollingerLower:98,bollingerUpper:103,atr14:1,relativeVolume20:2,support20:98,resistance20:102},signals:[{name:'EMA 9/21',direction:'UP',description:'Bullish cross on the latest closed minute'}],series:[{time:new Date(now-60000).toISOString(),ema9:101,ema21:100,upperBand:103,lowerBand:98}]};
test('technical panel shows readings, provenance and observed signals for closed bars',()=>{
  const html=renderTechnicals(report,{realtime:true,quoteTime:new Date(now).toISOString()},{now});
  for(const text of ['Latest closed-bar readings','EMA 9 / 21','RSI 14','MACD','Bollinger','ATR 14','Relative volume','Bullish cross','reset after a history gap'])assert.ok(html.includes(text),text);
  assert.doesNotMatch(html,/BUY|SELL/);
});
test('stale or unverified data is labeled historical and quote-only mode cannot claim indicators',()=>{
  for(const quote of [null,{realtime:false,quoteTime:new Date(now).toISOString()},{realtime:true,quoteTime:new Date(now-180000).toISOString()}])assert.match(renderTechnicals(report,quote,{now}),/Historical readings/);
  assert.match(renderTechnicals(report,null,{source:'quotes',now}),/need OHLCV bars/);
  assert.match(renderTechnicals(null,null,{now}),/No completed bars/);
  const escaped=renderTechnicals({...report,signals:[{name:'<img>',description:'<script>',direction:'UP'}]},null,{now});assert.doesNotMatch(escaped,/<img|<script>/);
});
test('overlay controls and time zoom only include visible indicator prices in autoscale',()=>{
  const overlays=indicatorOverlays(report,{ema:true,bands:true});assert.equal(overlays.length,4);
  const time=report.series[0].time,points=[{time,value:100}],window={start:Date.parse(time),end:Date.parse(time)+60000};
  const layout=chartLayout(points,window,1000,null,overlays);assert.ok(layout.max>103);assert.ok(layout.min<98);
  const svg=priceChart(points,'bars',window,1000,null,overlays);assert.equal((svg.match(/class="indicator-overlay"/g)??[]).length,4);assert.doesNotMatch(svg,/NaN/);
  assert.deepEqual(indicatorOverlays(report,{ema:false,bands:false}),[]);
  const outside=overlays.map(line=>({...line,points:line.points.map(p=>({...p,time:new Date(now+60000).toISOString()}))}));
  assert.equal(chartLayout(points,window,1000,null,outside).max,chartLayout(points,window).max);
});
