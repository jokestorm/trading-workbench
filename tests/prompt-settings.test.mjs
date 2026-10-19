import test from 'node:test';
import assert from 'node:assert/strict';
import {parseHTML} from 'linkedom';
import {mountPromptSettings} from '../web/prompt-settings.js';

const flush=()=>new Promise(resolve=>setImmediate(resolve));
test('prompt editor compares new defaults, preserves drafts on failure and explicitly adopts defaults',async()=>{
  const {document}=parseHTML('<html><body><div id="prompts"></div></body></html>');
  const previous=globalThis.document;globalThis.document=document;
  const initial={kind:'market',defaultText:'New market default',defaultVersion:'market-v2',customText:'My <custom> prompt',basedOnVersion:'market-v1',revision:'saved-1',updateAvailable:true,activeSource:'custom'};
  const calls=[];let fail=false,current={...initial};
  const api=async(path,body,method)=>{
    calls.push({path,body,method});if(!path)return [current,{kind:'journal',defaultText:'Journal default',defaultVersion:'journal-v1',customText:null,basedOnVersion:null,revision:'default:journal-v1',updateAvailable:false,activeSource:'default'}];
    if(fail)throw new Error('Settings changed. Reload before saving.');
    if(method==='POST')return current={...current,customText:null,basedOnVersion:null,revision:'default:market-v2',updateAvailable:false,activeSource:'default'};
    return current={...current,customText:body.text,basedOnVersion:body.basedOnVersion,revision:'saved-2',updateAvailable:body.basedOnVersion!==current.defaultVersion,activeSource:'custom'};
  };
  try{
    await mountPromptSettings(document.getElementById('prompts'),{api});
    const [market,journal]=document.querySelectorAll('.prompt-editor'),editor=market.querySelector('.prompt-text');
    assert.equal(editor.value,initial.customText);assert.equal(market.querySelector('custom'),null);
    assert.equal(market.querySelector('.prompt-update').hidden,false);
    assert.equal(market.querySelector('.prompt-baseline').value,'New market default');
    assert.equal(journal.querySelector('.prompt-text').value,'Journal default');
    editor.value='Unsaved';market.querySelector('.prompt-reset').click();assert.equal(editor.value,initial.customText);assert.equal(calls.length,1);
    market.querySelector('.prompt-copy').click();assert.equal(editor.value,'New market default');
    editor.value+=' plus my edits';market.querySelector('.prompt-save').click();await flush();
    assert.deepEqual(calls.at(-1).body,{text:'New market default plus my edits',basedOnVersion:'market-v2',revision:'saved-1'});
    assert.equal(market.querySelector('.prompt-update').hidden,true);
    fail=true;editor.value='Keep this draft';market.querySelector('.prompt-save').click();await flush();
    assert.equal(editor.value,'Keep this draft');assert.match(market.querySelector('.prompt-message').textContent,/Reload/);
    fail=false;market.querySelector('.prompt-default').click();await flush();
    assert.equal(calls.at(-1).method,'POST');assert.equal(calls.at(-1).path,'/market/default');assert.equal(editor.value,'New market default');
    assert.match(market.querySelector('.prompt-source').textContent,/Active: App default/);
    assert.equal(journal.querySelector('.prompt-text').value,'Journal default');
  }finally{globalThis.document=previous;}
});
