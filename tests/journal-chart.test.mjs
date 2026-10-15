import test from 'node:test';
import assert from 'node:assert/strict';
import {journalSeries,journalWindow,journalLayout,journalChart,balanceAt} from '../web/journal-chart.js';
import {timeBounds} from '../web/chart-viewport.js';

test('journal chart uses distinct closing times and preserves the selected cumulative balances',()=>{
  const curve=[{date:'2026-10-14',time:'09:30:00',value:100},{date:'2026-10-14',time:'10:45:00',value:70}];
  const points=journalSeries(curve),bounds=timeBounds(points),layout=journalLayout(points,bounds);
  assert.equal(points[0].value,0);assert.equal(points[0].baseline,true);
  assert.ok(layout.x(points[2])>layout.x(points[1]));assert.equal(Date.parse(points[2].time)-Date.parse(points[1].time),75*60_000);
  assert.deepEqual(points.slice(1).map(p=>p.value),[100,70]);assert.deepEqual(curve.map(p=>p.value),[100,70]);
});
test('zoom between trades carries the preceding balance without rebasing or inventing realized changes',()=>{
  const points=journalSeries([{date:'2026-10-14',time:'09:00:00',value:100},{date:'2026-10-14',time:'10:00:00',value:125},{date:'2026-10-14',time:'11:00:00',value:150}]);
  const window={start:Date.parse('2026-10-14T09:30:00Z'),end:Date.parse('2026-10-14T10:30:00Z')};
  assert.deepEqual(journalWindow(points,window).map(p=>p.value),[100,125,125]);
  const gap={start:Date.parse('2026-10-14T10:15:00Z'),end:Date.parse('2026-10-14T10:30:00Z')};
  assert.deepEqual(journalWindow(points,gap).map(p=>p.value),[125,125]);
  assert.equal(balanceAt(points,gap.start).value,125);
});
test('zoom autoscale excludes prior peaks but includes the balance carried into the viewport',()=>{
  const points=journalSeries([{date:'2026-10-13',time:'10:00:00',value:10000},{date:'2026-10-14',time:'09:00:00',value:100},{date:'2026-10-14',time:'10:00:00',value:110}]);
  const window={start:Date.parse('2026-10-14T09:30:00Z'),end:Date.parse('2026-10-14T10:30:00Z')},visible=journalWindow(points,window),layout=journalLayout(visible,window);
  assert.ok(layout.min<100&&layout.min>0);assert.ok(layout.max>110&&layout.max<200);
  assert.equal(layout.first,window.start);assert.equal(layout.span,window.end-window.start);
  const svg=journalChart(points,window);assert.match(svg,/H[0-9.]+V/);assert.doesNotMatch(svg,/NaN|Infinity/);
});
test('empty, single-trade and tied-time curves remain defined',()=>{
  assert.match(journalChart([],null),/No trades/);
  const points=journalSeries([{date:'2026-10-14',time:'10:00:00',value:10},{date:'2026-10-14',time:'10:00:00',value:20}]),bounds=timeBounds(points);
  assert.equal(balanceAt(points,Date.parse(points.at(-1).time)).value,20);
  assert.doesNotMatch(journalChart(points,bounds,520),/NaN|Infinity/);
  const single=journalSeries([{date:'2026-10-14',time:'10:00:00',value:0}]);
  assert.doesNotMatch(journalChart(single,timeBounds(single)),/NaN|Infinity/);
});
