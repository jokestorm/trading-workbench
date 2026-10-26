import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {journalSyncMessage,mountJournalSync} from '../web/journal-sync.js';
const flush=()=>new Promise(resolve=>setImmediate(resolve));
test('journal sync reports reconciliation requirements without implying unmatched trades imported',()=>{
 const text=journalSyncMessage({imported:2,matched:1,duplicates:3,needsReconciliation:['MGCZ26']});
 assert.match(text,/2 new trades/);assert.match(text,/1 statement matches/);assert.match(text,/3 duplicates/);assert.match(text,/MGCZ26/);
});
test('journal sync is explicit, serializes clicks and refreshes saved trades only after success',async()=>{
 const {document}=parseHTML('<html><body><div class="journal-toolbar"><div class="toolbar-actions"></div></div></body></html>');
 const previous={document:globalThis.document,fetch:globalThis.fetch};let complete,requests=0,refreshes=0,fail=false;
 globalThis.document=document;globalThis.fetch=async(url,options)=>{
  requests++;assert.ok(url.endsWith('/api/schwab/journal/sync'));assert.equal(options.method,'POST');
  if(fail)return{ok:false,json:async()=>({message:'Futures fees are missing. No trades were written.'})};
  await new Promise(resolve=>complete=resolve);return{ok:true,json:async()=>({imported:1,matched:1,duplicates:1,needsReconciliation:[]})};
 };
 try{
  mountJournalSync({onSynced:()=>{refreshes++;}});assert.equal(requests,0);
  const button=document.getElementById('sync-schwab-journal');button.click();button.click();assert.equal(requests,1);assert.equal(button.disabled,true);
  complete();await flush();assert.equal(refreshes,1);assert.equal(button.disabled,false);assert.match(document.querySelector('[role="status"]').textContent,/1 new trades/);
  fail=true;button.click();await flush();assert.equal(refreshes,1);assert.match(document.querySelector('[role="status"]').textContent,/fees are missing/);assert.equal(button.disabled,false);
 }finally{Object.assign(globalThis,previous);}
});
