import test from 'node:test';
import assert from 'node:assert/strict';
import {schwabStatementRequest} from '../web/import.js';

test('Schwab statement import fixes Eastern time and preserves the account label used for duplicate detection',()=>{
  const csv='\uFEFFAccount Statement for SAMPLE\r\nFutures Statements\r\n';
  const request=schwabStatementRequest('statement.csv',csv);
  assert.equal(request.sourceTimeZone,'Eastern');assert.equal(request.account,'Schwab futures');
  assert.equal(request.assetType,'Futures');assert.deepEqual(request.columns,{});
  assert.equal(request.csv,csv);assert.equal(request.fileName,'statement.csv');
});
test('generic executions and equity-only statements do not enter the futures statement flow',()=>{
  for(const csv of ['Timestamp,Symbol,Side\n2026-10-14,MES,BUY','Account Statement for SAMPLE\nCash Balance'])
    assert.throws(()=>schwabStatementRequest('statement.csv',csv),/Futures Statements/);
});
