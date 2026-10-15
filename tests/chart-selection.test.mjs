import test from 'node:test';
import assert from 'node:assert/strict';
import {selectChartContract} from '../web/market-monitor.js';

const selections=[{requested:'/MGC',symbol:'/MGCZ26'},{requested:'/MES',symbol:'/MESZ26'},{requested:'/MCL',symbol:'/MCLX26'}];
test('chart defaults to the resolved MES contract regardless of watchlist ordering',()=>{
  assert.equal(selectChartContract(selections),'/MESZ26');
});
test('refreshing contracts preserves a manual chart selection and follows its requested root on rollover',()=>{
  assert.equal(selectChartContract(selections,'/MGCZ26','/MGC'),'/MGCZ26');
  assert.equal(selectChartContract([{requested:'/MES',symbol:'/MESH27'},{requested:'/MGC',symbol:'/MGCG27'}],'/MGCZ26','/MGC'),'/MGCG27');
});
test('unavailable contracts are excluded and an empty watchlist yields no chart selection',()=>{
  assert.equal(selectChartContract([{requested:'/MES',symbol:'/MES',error:'Unavailable'},selections[0]]),'/MGCZ26');
  assert.equal(selectChartContract([]),'');
});
