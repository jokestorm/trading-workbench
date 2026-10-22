import test from 'node:test';
import assert from 'node:assert/strict';
import {renderBrokerPosition,usableBrokerPosition} from '../web/broker-positions.js';
const now=Date.parse('2026-10-22T14:00:00Z');
const position={accountId:'fixture',accountLabel:'Account 1',symbol:'/MESZ26',state:'LONG',quantity:2,averageEntry:100.375,lastFill:'2026-10-22T13:50:00Z',message:'Estimate from available transactions.'};
const report={status:'ready',lastSynced:new Date(now).toISOString(),positions:[position]};
test('transaction position separates sync time from fills and requires explicit Use',()=>{
 const html=renderBrokerPosition(report,'/MESZ26','',now);
 for(const text of ['LONG 2 @ 100.375','Last synced:','Last fill:','Use in analysis','Estimate from available','New fills may not be posted'])assert.ok(html.includes(text),text);
 assert.equal(usableBrokerPosition(report,position,now),true);
 assert.match(renderBrokerPosition(report,'/MGCZ26','',now),/No derived position/);
 assert.match(renderBrokerPosition(report,'/MGCZ26','',now),/does not mean flat/);
});
test('stale failed unknown and absent estimates cannot be used as confirmed holdings',()=>{
 for(const candidate of [{...report,status:'failed'},{...report,lastSynced:'2026-10-22T13:00:00Z'},{...report,lastSynced:'2026-10-22T15:00:00Z'}])assert.equal(usableBrokerPosition(candidate,position,now),false);
 assert.equal(usableBrokerPosition(report,{...position,state:'UNKNOWN'},now),false);
 const html=renderBrokerPosition({...report,status:'failed',message:'<script>'},'/MESZ26','',now);assert.match(html,/broker-use[^>]*disabled/);assert.doesNotMatch(html,/<script>/);
 const accounts=renderBrokerPosition({...report,positions:[position,{...position,accountId:'other',accountLabel:'Account 2',state:'SHORT'}]},'/MESZ26','other',now);assert.match(accounts,/SHORT/);assert.match(accounts,/broker-account/);
});
