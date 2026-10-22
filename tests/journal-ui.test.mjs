import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parseHTML} from 'linkedom';
import {mountReports,renderReports} from '../web/reports.js';
import {summarize} from '../web/analytics.js';
import {renderJournalMetrics} from '../web/journal-metrics.js';
import {renderStatementPositions} from '../web/statement-positions.js';

test('older backend summaries reconcile gross from net plus fees without showing NaN',()=>{
  const html=renderJournalMetrics({net:463.17,fees:159.58,count:16,wins:10,losses:6,breakEven:0,winRate:.625,expectancy:28.948125,drawdown:100});
  assert.match(html,/\+\$622\.75/);assert.doesNotMatch(html,/NaN|undefined/);
});
test('historical CSV positions are collapsed and dated instead of appearing to be current holdings',()=>{
  const html=renderStatementPositions([{account:'Fixture',symbol:'MESZ26',quantity:2,averageEntry:7881.25,entryFees:2,executions:[{timestamp:'2026-10-06T13:10:17'},{timestamp:'2026-10-06T14:09:08'}]}]);
  assert.match(html,/Historical CSV positions/);assert.match(html,/not your current broker holdings/);assert.match(html,/2026-10-06 14:09:08/);assert.match(html,/Imported average entry/);assert.match(html,/7,881\.25/);assert.doesNotMatch(html,/<details[^>]*\bopen\b/);
});

test('gross P/L and fees have separate boxes while net remains explicit in reports',()=>{
  const summary=summarize([{id:'fixture',date:'2026-10-15',symbol:'MES',grossPnl:100,netPnl:85,fees:15,risk:null}]);
  const {document}=parseHTML('<html><body><div id="metrics"></div><article class="trade-panel"></article></body></html>');
  const previous=globalThis.document;globalThis.document=document;
  try{
    document.getElementById('metrics').innerHTML=renderJournalMetrics(summary);
    const cards=document.querySelectorAll('.metric');
    assert.equal(cards[0].querySelector('.metric-label').textContent,'Gross P/L');
    assert.equal(cards[0].querySelector('.metric-value').textContent,'+$100.00');
    assert.doesNotMatch(cards[0].textContent,/\$15|\$85/);
    assert.equal(cards[1].querySelector('.metric-label').textContent,'Fees & commissions');
    assert.equal(cards[1].querySelector('.metric-value').textContent,'$15.00');
    mountReports();renderReports(summary);
    assert.match(document.getElementById('range-statistics').textContent,/Net P\/L after fees\$85.00/);
    assert.equal(summary.gross-summary.fees,summary.net);
  }finally{globalThis.document=previous;}
});

test('journal renders reports without setup controls, required fields or categories',async()=>{
  const {document}=parseHTML(await readFile(new URL('../web/index.html',import.meta.url),'utf8'));
  const previous=globalThis.document;globalThis.document=document;
  try{
    assert.equal(document.querySelector('#setup,[name="setup"],#setup-chart'),null);
    assert.doesNotMatch(document.getElementById('filters').textContent,/setup/i);
    assert.doesNotMatch(document.querySelector('.trade-panel thead').textContent,/setup/i);
    mountReports();
    const summary=summarize([{id:'fixture',symbol:'MES',date:'2026-10-15',time:'10:00',netPnl:25,fees:2,risk:null}]);
    renderReports(summary);
    assert.match(document.getElementById('instrument-report').textContent,/MES/);
    assert.match(document.getElementById('daily-report').textContent,/2026-10-15/);
    assert.equal(document.querySelectorAll('.chart-layout > article').length,1);
    assert.equal('setups' in summary,false);
  }finally{globalThis.document=previous;}
});
