import test from 'node:test';
import assert from 'node:assert/strict';
import {series,priceChart,quoteMarker,marketBounds,chartLayout} from '../web/market-monitor.js';

test('live quote marker updates independently without appending quotes to historical bars',()=>{
 const points=[{time:'2026-10-12T10:00:00Z',value:100},{time:'2026-10-12T10:01:00Z',value:101}],before=structuredClone(points);
 const now=Date.parse('2026-10-12T10:01:30Z'),marker=quoteMarker({quoteTime:new Date(now).toISOString(),last:105,realtime:true},now);
 const bounds=marketBounds(points,marker),layout=chartLayout(points,bounds,1000,marker),svg=priceChart(points,'bars',bounds,1000,marker);
 assert.equal(bounds.end,now);assert.ok(layout.max>105);assert.ok(layout.y(105)>layout.top);
 assert.match(svg,/live-price-marker/);assert.match(svg,/>LIVE</);assert.match(svg,/>105</);assert.match(svg,/06:01:30 AM ET/);
 const later=priceChart(points,'bars',bounds,1000,{...marker,value:106});assert.match(later,/>106</);
 assert.equal((svg.match(/<path d="([^"]+)/)[1].match(/[ML]/g)??[]).length,2);
 assert.equal((later.match(/<path d="([^"]+)/)[1].match(/[ML]/g)??[]).length,2);
 assert.deepEqual(points,before);
});

test('historical zoom excludes the live marker and its price from autoscale',()=>{
 const points=[{time:'2026-10-12T10:00:00Z',value:100},{time:'2026-10-12T10:01:00Z',value:101}];
 const window={start:Date.parse(points[0].time),end:Date.parse(points[1].time)},marker={time:'2026-10-12T10:02:00Z',value:999,label:'Live'};
 assert.equal(chartLayout(points,window,1000,marker).max,chartLayout(points,window).max);
 assert.doesNotMatch(priceChart(points,'bars',window,1000,marker),/live-price-marker|>999</);
 assert.deepEqual(window,{start:Date.parse(points[0].time),end:Date.parse(points[1].time)});
});

test('delayed, stale and unverified quotes are never labeled live',()=>{
 const now=Date.parse('2026-10-12T10:02:00Z'),quote={quoteTime:new Date(now).toISOString(),last:100};
 assert.equal(quoteMarker({...quote,realtime:false},now).label,'Delayed');
 assert.equal(quoteMarker(quote,now).label,'Unverified');
 assert.equal(quoteMarker({...quote,realtime:true},now+121000).label,'Stale');
 for(const patch of [{last:null},{last:NaN},{quoteTime:null},{quoteTime:'bad'},{quoteTime:new Date(now+60000).toISOString()}])assert.equal(quoteMarker({...quote,...patch},now),null);
});

test('a quote can be displayed without historical bars and never creates a bar path',()=>{
 const marker={time:'2026-10-12T10:02:00Z',value:100,label:'Live'};
 const svg=priceChart([],'bars',marketBounds([],marker),1000,marker);
 assert.match(svg,/live-price-marker/);assert.doesNotMatch(svg,/NaN|d="M/);
 assert.doesNotMatch(priceChart([{time:marker.time,value:100}],'quotes',null,1000,marker),/live-price-marker/);
});
test('chart sources do not turn quote snapshots into bars',()=>{const h={bars:[{time:'2026-10-12T10:00:00Z',close:101}],quotes:[{quoteTime:null,fetchedAt:'2026-10-12T10:01:00Z',last:102},{last:null}]};assert.deepEqual(series(h,'bars'),[{time:h.bars[0].time,value:101}]);assert.deepEqual(series(h,'quotes'),[{time:h.quotes[0].fetchedAt,value:102}]);});
test('historical chart leaves missing-minute gaps and handles one point',()=>{const points=[{time:'2026-10-12T10:00:00Z',value:100},{time:'2026-10-12T11:00:00Z',value:102}];assert.equal((priceChart(points,'bars').match(/<path /g)??[]).length,2);assert.match(priceChart([points[0]],'quotes'),/<circle/);assert.doesNotMatch(priceChart([points[0]],'quotes'),/NaN/);});

test('cursor readout selects actual observations across gaps without inventing prices',async()=>{
 const {nearestPoint}=await import('../web/market-monitor.js');
 const points=[{time:'2026-10-12T10:00:00Z',value:100},{time:'2026-10-12T11:00:00Z',value:102}];
 assert.equal(nearestPoint(points,Date.parse('2026-10-12T10:10:00Z')),points[0]);
 assert.equal(nearestPoint(points,Date.parse('2026-10-12T10:50:00Z')),points[1]);
 assert.equal(nearestPoint(points,Date.parse('2026-10-12T12:00:00Z')),points[1]);
});
test('chart scales remain readable for constant and very narrow price ranges',async()=>{
 const {chartLayout}=await import('../web/market-monitor.js');
 const points=[{time:'2026-10-12T10:00:00Z',value:93.28},{time:'2026-10-12T10:01:00Z',value:93.28}];
 const layout=chartLayout(points);assert.ok(layout.min<93.28&&layout.max>93.28);assert.ok(Number.isFinite(layout.y(93.28)));assert.match(priceChart(points,'bars'),/EASTERN TIME/);assert.match(priceChart(points,'bars'),/chart-cursor/);
});
