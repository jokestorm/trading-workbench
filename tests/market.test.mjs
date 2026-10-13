import test from 'node:test';
import assert from 'node:assert/strict';
import {series,priceChart} from '../web/market-monitor.js';
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
