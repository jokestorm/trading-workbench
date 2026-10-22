import test from 'node:test';
import assert from 'node:assert/strict';
import {updateMarketWindow,selectMarketWindow} from '../web/market-viewport.js';
import {zoomWindow} from '../web/chart-viewport.js';
import {marketBounds,quoteMarker,priceChart} from '../web/market-monitor.js';
const start=Date.parse('2026-10-22T13:00:00Z'),minute=60000;
const bounds={start,end:start+60*minute};
test('zoom at the newest edge preserves its duration while following each new quote',()=>{
 const previous={start,end:bounds.end};
 const zoomed=zoomWindow(previous,bounds,bounds.end-2*minute,.25);
 const selected=selectMarketWindow(zoomed,previous,bounds,{source:'wheel',nearRight:true},true);
 const span=zoomed.end-zoomed.start;
 assert.equal(selected.following,true);assert.equal(selected.window.end,bounds.end);
 for(const delta of [1000,2000,minute,30*minute]){
  const next=updateMarketWindow(selected.window,{...bounds,end:bounds.end+delta},selected.following);
  assert.equal(next.end,bounds.end+delta);assert.equal(next.end-next.start,span);
 }
});
test('historical selections stay anchored and zooming their right edge does not jump to the present',()=>{
 const historical={start:start+10*minute,end:start+20*minute};
 const selected=selectMarketWindow(historical,bounds,bounds,{source:'drag',nearRight:false},true);
 assert.equal(selected.following,false);
 assert.deepEqual(updateMarketWindow(selected.window,{...bounds,end:bounds.end+10*minute},false),historical);
 const zoomed=zoomWindow(historical,bounds,historical.end,.5);
 assert.equal(selectMarketWindow(zoomed,historical,bounds,{source:'wheel',nearRight:true},false).following,false);
});
test('right-edge drag and zoom buttons follow latest; moving into history stops following',()=>{
 const dragged={start:bounds.end-5*minute,end:bounds.end-10000};
 const selected=selectMarketWindow(dragged,bounds,bounds,{source:'drag',nearRight:true},true);
 assert.equal(selected.following,true);assert.equal(selected.window.end,bounds.end);
 const clicked=selectMarketWindow(zoomWindow(selected.window,bounds,(selected.window.start+selected.window.end)/2,.5),selected.window,bounds,{source:'button',nearRight:true},true);
 assert.equal(clicked.following,true);assert.equal(clicked.window.end-clicked.window.start,(dragged.end-dragged.start)/2);
 assert.equal(selectMarketWindow({start,end:start+minute},clicked.window,bounds,{source:'drag',nearRight:false},true).following,false);
});
test('following keeps the live marker visible, clamps shorter history, and handles no observations',()=>{
 const points=[{time:new Date(start).toISOString(),value:100},{time:new Date(bounds.end).toISOString(),value:101}];
 const marker=quoteMarker({last:102,quoteTime:new Date(bounds.end+1000).toISOString(),realtime:true},bounds.end+1000);
 const advanced=marketBounds(points,marker),window=updateMarketWindow({start:bounds.end-5*minute,end:bounds.end},advanced,true);
 assert.equal(window.end,Date.parse(marker.time));assert.match(priceChart(points.filter(p=>Date.parse(p.time)>=window.start),'bars',window,1000,marker),/live-price-marker/);
 assert.deepEqual(updateMarketWindow(window,{start:bounds.end,end:bounds.end+minute},true),{start:bounds.end,end:bounds.end+minute});
 assert.equal(updateMarketWindow(window,null,true),null);
});
