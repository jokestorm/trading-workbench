import {fetchApp} from './http.js';
export function journalSyncMessage(result){
  const waiting=result.needsReconciliation?.length?' Needs a matching statement trade for: '+result.needsReconciliation.join(', ')+'.':'';
  return `${result.imported} new trades · ${result.matched} statement matches · ${result.duplicates} duplicates skipped.${waiting}`;
}
export function mountJournalSync({onSynced}){
  const button=document.createElement('button');button.type='button';button.className='quiet-button';button.id='sync-schwab-journal';button.textContent='Sync Schwab trades';document.querySelector('.journal-toolbar .toolbar-actions').prepend(button);
  const status=document.createElement('p');status.className='small muted';status.setAttribute('role','status');document.querySelector('.journal-toolbar').after(status);
  button.addEventListener('click',async()=>{
    if(button.disabled)return;button.disabled=true;status.textContent='Syncing futures executions and checking statement overlap…';
    try{
      const response=await fetchApp('/api/schwab/journal/sync',{method:'POST',signal:AbortSignal.timeout(35000)});const result=await response.json();
      if(!response.ok)throw Error(result.message??'Schwab sync failed.');status.textContent=journalSyncMessage(result);await onSynced();
    }catch(error){status.textContent=error.name==='TimeoutError'?'Sync timed out. Check Schwab sign-in and retry; duplicates are protected.':error.message;}
    finally{button.disabled=false;}
  });
}
