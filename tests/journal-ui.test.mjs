import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parseHTML} from 'linkedom';
import {mountReports,renderReports} from '../web/reports.js';
import {summarize} from '../web/analytics.js';

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
