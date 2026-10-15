import test from 'node:test';
import assert from 'node:assert/strict';
import {timeBounds, clampWindow, zoomWindow, selectWindow, pointsInWindow, bindChartNavigation} from '../web/chart-viewport.js';
import {chartLayout, priceChart} from '../web/market-monitor.js';
const start=Date.parse('2026-10-14T14:00:00Z');
const bounds={start,end:start+60*60_000};

test('wheel zoom stays anchored at the pointer and zoom-out stops at loaded history',()=>{
  const window={start:start+10*60_000,end:start+50*60_000},anchor=start+20*60_000;
  const zoomed=zoomWindow(window,bounds,anchor,.5);
  assert.equal((anchor-zoomed.start)/(zoomed.end-zoomed.start),.25);
  assert.equal(zoomed.end-zoomed.start,20*60_000);
  assert.deepEqual(zoomWindow(zoomed,bounds,anchor,100),bounds);
});
test('reverse dragging selects the same interval and tiny selections retain a minute',()=>{
  assert.deepEqual(selectWindow(start+40*60_000,start+20*60_000,bounds),{start:start+20*60_000,end:start+40*60_000});
  assert.equal(selectWindow(start+1,start+2,bounds).end-selectWindow(start+1,start+2,bounds).start,60_000);
  assert.deepEqual(clampWindow({start:bounds.end-1,end:bounds.end+5*60_000},bounds),{start:bounds.end-(5*60_000+1),end:bounds.end});
});
test('price autoscale excludes outliers outside the selected time while preserving exact time bounds',()=>{
  const points=[{time:new Date(start).toISOString(),value:10_000},{time:new Date(start+10*60_000).toISOString(),value:100},{time:new Date(start+11*60_000).toISOString(),value:101}];
  const window={start:start+9*60_000,end:start+12*60_000},visible=pointsInWindow(points,window),layout=chartLayout(visible,window);
  assert.equal(visible.length,2);assert.ok(layout.max<102);assert.ok(layout.min<100);
  assert.equal(layout.first,window.start);assert.equal(layout.span,3*60_000);
  assert.doesNotMatch(priceChart(visible,'bars',window),/NaN|Infinity/);
  assert.equal(pointsInWindow(points,{start:start+20*60_000,end:start+21*60_000}).length,0);
  assert.equal(timeBounds([points[0]]).end-start,60_000);
});

function gestureHarness(){
  const events={},attrs={},windows=[],capture=new Set(),layout={w:1000,h:390,left:80,right:85,top:25,bottom:55,first:start,span:60*60_000};
  const svg={getBoundingClientRect:()=>({left:0,top:0,width:1000,height:390})};
  const selection={setAttribute:(k,v)=>attrs[k]=v,removeAttribute:k=>delete attrs[k]};
  const container={querySelector:q=>q==='svg'?svg:q==='.chart-selection'?selection:null,addEventListener:(k,fn)=>events[k]=fn,classList:{add(){},remove(){}},setPointerCapture:id=>capture.add(id),hasPointerCapture:id=>capture.has(id),releasePointerCapture:id=>capture.delete(id)};
  const cancel=bindChartNavigation(container,{getState:()=>({layout,window:bounds,bounds}),onWindow:w=>windows.push(w),onHover(){},onLeave(){}});
  const pointer=(x,extra={})=>({pointerId:1,isPrimary:true,button:0,clientX:x,clientY:150,...extra});
  return {events,attrs,windows,capture,cancel,pointer};
}
test('drag gesture shows selection, captures the pointer and zooms only on release',()=>{
  const h=gestureHarness();h.events.pointerdown(h.pointer(700));h.events.pointermove(h.pointer(300));
  assert.equal(h.attrs.width,400);assert.equal(h.windows.length,0);assert.ok(h.capture.has(1));
  h.events.pointerup(h.pointer(300));assert.equal(h.windows.length,1);assert.equal(h.capture.size,0);
  assert.ok(h.windows[0].start<h.windows[0].end);assert.equal(h.attrs.hidden,'');
  h.events.pointerdown(h.pointer(300));h.events.pointercancel();h.events.pointerup(h.pointer(600));
  assert.equal(h.windows.length,1);
});
test('wheel is consumed over the plot, leaves page scrolling available outside it and ignores active drags',()=>{
  const h=gestureHarness();let prevented=0;
  const wheel=x=>({...h.pointer(x),deltaY:-100,deltaMode:0,preventDefault:()=>prevented++});
  h.events.wheel(wheel(10));assert.equal(prevented,0);assert.equal(h.windows.length,0);
  h.events.wheel(wheel(500));assert.equal(prevented,1);assert.equal(h.windows.length,1);
  assert.ok(h.windows[0].end-h.windows[0].start<bounds.end-bounds.start);
  h.events.pointerdown(h.pointer(200));h.events.wheel(wheel(500));assert.equal(prevented,1);
  h.cancel();assert.equal(h.capture.size,0);
});
