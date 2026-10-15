import { fetchApp } from './http.js';

const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money=n=>new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(n);
export function schwabStatementRequest(fileName,csv) {
  if(!csv.replace(/^\uFEFF/,'').toLowerCase().startsWith('account statement for')||!csv.includes('Futures Statements'))
    throw new Error('Choose your Schwab / thinkorswim account statement CSV with the Futures Statements section.');
  return {fileName,csv,columns:{},account:'Schwab futures',assetType:'Futures',multiplier:1,sourceTimeZone:'Eastern'};
}

export function mountImport({onImported}) {
  const dialog=document.createElement('dialog');dialog.id='import-dialog';
  dialog.innerHTML=`<div class="dialog-top"><div><div class="eyebrow">SCHWAB STATEMENT</div><h2>Import your trades</h2></div><button id="close-import" class="quiet-button" type="button" aria-label="Close import">✕</button></div><p class="small muted">Choose your Schwab / thinkorswim account statement CSV. Eastern time, futures point values, and fees are handled automatically. Trades already imported are skipped.</p><form id="import-form"><label class="statement-file">Statement CSV<input id="csv-file" type="file" accept=".csv,text/csv" required></label><div id="import-status" class="small muted" role="status" aria-live="polite"></div><div id="import-error" role="alert"></div><div id="import-preview"></div><div class="form-actions"><button id="commit-import" class="primary-button" type="submit" disabled>Import statement</button></div></form>`;
  document.body.append(dialog);
  const $=id=>dialog.querySelector('#'+id);
  let token=null,version=0,controller=null,committing=false;
  function cancelPreview(){version++;controller?.abort();controller=null;token=null;$('commit-import').disabled=true;}
  function close(){if(committing)return;cancelPreview();$('csv-file').value='';$('import-status').textContent='';$('import-error').textContent='';$('import-preview').replaceChildren();dialog.close();}
  document.getElementById('import-csv').addEventListener('click',()=>dialog.showModal());
  $('close-import').addEventListener('click',close);
  dialog.addEventListener('cancel',event=>{event.preventDefault();close();});
  $('csv-file').addEventListener('change',async()=>{
    cancelPreview();const current=version;
    $('import-error').textContent='';$('import-preview').replaceChildren();$('import-status').textContent='';
    try {
      const file=$('csv-file').files[0];if(!file)return;
      if(file.size>2_000_000)throw new Error('Choose a CSV smaller than 2 MB.');
      $('import-status').textContent='Checking statement…';
      const csv=await file.text();if(current!==version)return;
      const request=schwabStatementRequest(file.name,csv);
      controller=new AbortController();
      const response=await fetchApp('/api/imports/preview',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request),signal:controller.signal});
      const result=await response.json();if(current!==version)return;
      if(!response.ok)throw new Error(result.message??'Could not read this statement.');
      if(!result.reconciliation)throw new Error('This file did not match the supported Schwab futures statement format.');
      const r=result.reconciliation,errors=result.errors??[];
      $('import-status').textContent=errors.length?'Statement needs attention. Nothing has been imported.':'Statement checked · Eastern time';
      $('import-preview').innerHTML=`<div class="import-totals"><strong>${result.trades.length} new trades</strong><span>${result.duplicates} duplicates skipped</span><strong>${money(result.netPnl)} new net P/L</strong></div>${(result.warnings??[]).map(w=>`<p class="import-warning">${escape(w)}</p>`).join('')}${errors.length?`<div class="import-errors">${errors.map(error=>`<p>${error.row?'Row '+error.row+': ':''}${escape(error.message)}</p>`).join('')}</div>`:''}<p class="${r.reconciled&&!r.cashBalanceChanges?.length?'import-success':'import-warning'}">${r.reconciled?(r.cashBalanceChanges?.length?'✓ Trade P/L reconciled · review cash adjustment':'✓ Statement reconciled'):'Statement reconciliation failed'} · ${r.executions} executions · ${r.closedTrades} closed trades</p><p class="small muted">${result.openPositions?.length??0} open positions saved separately from closed P/L.</p><details class="statement-details"><summary>Statement details</summary><div class="detail-grid">${[['Closed gross P/L',r.closedGross],['Closed fees',r.closedFees],['Closed net P/L',r.closedNet],['Period futures cash result',r.periodLedgerNet],['Ending futures cash',r.closingFuturesCash],['Reported cash sweeps',r.sweeps],['Unitemized cash adjustments',(r.cashBalanceChanges??[]).reduce((sum,change)=>sum+change.amount,0)]].map(([label,value])=>`<div><small>${label}</small>${money(value)}</div>`).join('')}</div><div class="table-wrap"><table><thead><tr><th>Instrument</th><th>Direction</th><th>Closed (ET)</th><th>Fees</th><th>Net P/L</th></tr></thead><tbody>${result.trades.map(({trade:t})=>`<tr><td>${escape(t.symbol)}</td><td>${escape(t.direction)}</td><td>${escape(t.date)} ${escape(t.time)}</td><td>${money(t.fees)}</td><td>${money(t.netPnl)}</td></tr>`).join('')}</tbody></table></div>${result.openPositions?.length?`<h3>Open positions · statement snapshot</h3><div class="table-wrap"><table><thead><tr><th>Instrument</th><th>Signed qty</th><th>Average entry</th><th>Entry fees</th></tr></thead><tbody>${result.openPositions.map(p=>`<tr><td>${escape(p.symbol)}</td><td>${p.quantity}</td><td>${p.averageEntry}</td><td>${money(p.entryFees)}</td></tr>`).join('')}</tbody></table></div>`:''}</details>`;
      if(!errors.length&&r.reconciled){token=result.token;$('commit-import').disabled=false;}
    }catch(error){if(current!==version||error.name==='AbortError')return;$('import-status').textContent='';$('import-error').textContent=error.message;}
  });
  $('import-form').addEventListener('submit',async event=>{
    event.preventDefault();if(!token||committing)return;
    committing=true;$('commit-import').disabled=true;$('csv-file').disabled=true;$('close-import').disabled=true;$('import-error').textContent='';
    try {
      const response=await fetchApp('/api/imports/commit',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token})});
      const result=await response.json();if(!response.ok)throw new Error(result.message??'Import failed.');
      token=null;$('csv-file').value='';$('import-preview').replaceChildren();
      $('import-status').textContent=`Imported ${result.imported} trades · ${result.duplicates} duplicates skipped · ${result.openPositions??0} open position snapshots saved.`;
      await onImported();
    }catch(error){$('import-error').textContent=error.message;}
    finally{committing=false;$('commit-import').disabled=!token;$('csv-file').disabled=false;$('close-import').disabled=false;}
  });
}
