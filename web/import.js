import { fetchApp } from './http.js';
import { parseCsv } from './csv.js?v=decisions-2';

const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money=n=>new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(n);
const fields=[['executionId','Execution ID',false,['executionid','execid','fillid','id']],['timestamp','Execution date & time (ET)',true,['timestamp','datetime','executiontime','time','executedat']],['symbol','Instrument symbol',true,['symbol','instrument','contract']],['side','Buy / sell',true,['side','action','buysell']],['quantity','Quantity (positive)',true,['quantity','qty','contracts']],['price','Execution price',true,['price','fillprice','executionprice']],['fees','Total execution fees (positive)',false,['fees','totalfees','commissionfees']],['assetType','Asset type',false,['assettype','type']],['multiplier','Point value',false,['multiplier','pointvalue']]];

export function mountImport({onImported}) {
  const dialog=document.createElement('dialog');dialog.id='import-dialog';
  dialog.innerHTML=`<div class="dialog-top"><div><div class="eyebrow">EXECUTION CSV</div><h2>Review, then import.</h2></div><button id="close-import" class="quiet-button" type="button" aria-label="Close import">✕</button></div><p class="small muted">First row must contain column names. Use executed fills with positive quantities and fees, complete position history, USD prices, and Eastern timestamps. Order requests and date-only transaction records need a different adapter.</p><form id="import-form"><div class="entry-grid"><label class="full-width">CSV file<input id="csv-file" type="file" accept=".csv,text/csv" required></label><label>Account label<input id="import-account" required maxlength="80" value="Personal"></label><label>Default instrument type / point value<select id="import-preset"><option value="EQUITY">Equity · $1 / point</option><option value="MES">MES · $5 / point</option><option value="MNQ">MNQ · $2 / point</option><option value="MCL">MCL · $100 / point</option><option value="ES">ES · $50 / point</option><option value="NQ">NQ · $20 / point</option></select></label></div><p class="small muted">Defaults apply when type or point-value columns are unmapped. Mixed instruments need correct values for each row. Fees must include all commissions and charges in one column.</p><div id="mapping" class="entry-grid"></div><div id="source-preview"></div><label class="import-confirm"><input id="flat-confirm" type="checkbox" required> I confirm the file begins flat for each symbol, uses Eastern Time, and has the correct point values and fees.</label><div class="form-actions"><button id="preview-import" class="primary-button" type="submit" disabled>Preview import</button></div></form><div id="import-error" role="alert"></div><div id="import-preview"></div><div class="form-actions"><button id="commit-import" class="primary-button" type="button" hidden>Import trades</button></div>`;
  document.body.append(dialog);
  dialog.querySelector('.import-confirm').lastChild.textContent=' I confirm this file contains one account, begins flat for each symbol, uses Eastern Time, and has the correct point values and fees.';
  const $=id=>dialog.querySelector('#'+id);let csv='',fileName='',token=null,header=[],request=null,statement=false;
  const zoneLabel=document.createElement('label');zoneLabel.textContent='Statement execution timezone';
  const zone=document.createElement('select');zone.id='import-zone';zone.add(new Option('Eastern / EDT','Eastern'));zone.add(new Option('Central / CDT','Central'));zoneLabel.append(zone);zoneLabel.hidden=true;$('import-preset').closest('label').after(zoneLabel);
  const invalidate=()=>{token=null;request=null;$('import-preview').replaceChildren();$('commit-import').hidden=true;$('import-error').textContent='';};
  document.getElementById('import-csv').addEventListener('click',()=>dialog.showModal());
  $('close-import').addEventListener('click',()=>dialog.close());
  $('import-form').addEventListener('change',invalidate);
  $('csv-file').addEventListener('change',async()=>{
    invalidate();$('mapping').replaceChildren();$('source-preview').replaceChildren();$('preview-import').disabled=true;csv='';
    try {
      const file=$('csv-file').files[0];if(!file)return;if(file.size>2_000_000)throw new Error('Choose a CSV smaller than 2 MB.');
      const text=await file.text();const rows=parseCsv(text);if(rows.length<2)throw new Error('CSV needs a header and at least one execution.');
      csv=text;fileName=file.name;header=rows[0];
      statement=text.replace(/^\uFEFF/,'').startsWith('Account Statement for')&&text.includes('Futures Statements');zoneLabel.hidden=!statement;$('import-preset').closest('label').hidden=statement;
      if(statement){if($('import-account').value==='Personal')$('import-account').value='Schwab futures';$('source-preview').innerHTML='<p class="import-success">thinkorswim account statement detected. Futures fills and both fee columns will be extracted automatically, and matched against Trade History. Open positions and settlement cash are reviewed separately.</p>';dialog.querySelector('.import-confirm').lastChild.textContent=' I confirm the account label and execution timezone are correct. I will review the statement reconciliation before importing.';$('preview-import').disabled=false;return;}
      dialog.querySelector('.import-confirm').lastChild.textContent=' I confirm this file contains one account, begins flat for each symbol, uses Eastern Time, and has the correct point values and fees.';
      for(const [key,title,required,aliases] of fields){const label=document.createElement('label');label.textContent=title+(required?' *':'');const select=document.createElement('select');select.dataset.column=key;select.add(new Option(required?'Choose column':'Not mapped',-1));header.forEach((h,i)=>select.add(new Option(`${i+1}. ${h}`,i)));const guess=header.findIndex(h=>aliases.includes(h.toLowerCase().replace(/[^a-z0-9]/g,'')));if(guess>=0)select.value=guess;label.append(select);$('mapping').append(label);}
      $('source-preview').innerHTML=`<p class="small muted">${rows.length-1} rows · First 3 executions</p><div class="table-wrap"><table><thead><tr>${header.map(h=>`<th>${escape(h)}</th>`).join('')}</tr></thead><tbody>${rows.slice(1,4).map(row=>`<tr>${row.map(v=>`<td>${escape(v)}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
      $('preview-import').disabled=false;
    }catch(error){$('import-error').textContent=error.message;}
  });
  $('import-form').addEventListener('submit',async e=>{
    e.preventDefault();invalidate();$('preview-import').disabled=true;
    try {
      const columns=Object.fromEntries([...dialog.querySelectorAll('[data-column]')].map(s=>[s.dataset.column,Number(s.value)]));
      for(const [key,title,required] of fields)if(!statement&&required&&columns[key]<0)throw new Error(`Map ${title}.`);
      const preset=$('import-preset').value;const multipliers={EQUITY:1,MES:5,MNQ:2,MCL:100,ES:50,NQ:20};
      request={fileName,csv,columns,account:$('import-account').value,assetType:preset==='EQUITY'?'Equity':'Futures',multiplier:multipliers[preset],sourceTimeZone:zone.value};
      const response=await fetchApp('/api/imports/preview',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)});
      const result=await response.json();if(!response.ok)throw new Error(result.message??'Could not preview import.');token=result.token;
      $('import-preview').innerHTML=`<div class="import-totals"><strong>${result.trades.length} new trades</strong><span>${result.duplicates} duplicates skipped</span><strong>${money(result.netPnl)} net P/L</strong></div>${result.warnings.map(w=>`<p class="import-warning">${escape(w)}</p>`).join('')}${result.errors.length?`<div class="import-errors">${result.errors.map(error=>`<p>Row ${error.row}: ${escape(error.message)}</p>`).join('')}</div>`:''}<div class="table-wrap"><table><thead><tr><th>Account</th><th>Instrument</th><th>Direction</th><th>Closed (ET)</th><th class="numeric">Fills</th><th class="numeric">Fees</th><th class="numeric">Net P/L</th></tr></thead><tbody>${result.trades.map(({trade:t,executions})=>`<tr><td>${escape(t.account)}</td><td>${escape(t.symbol)}</td><td>${t.direction}</td><td>${t.date} ${t.time}</td><td class="numeric">${executions.length}</td><td class="numeric">${money(t.fees)}</td><td class="numeric">${money(t.netPnl)}</td></tr>`).join('')}</tbody></table></div>`;
      if(result.reconciliation){const r=result.reconciliation;const summary=document.createElement('div');summary.className='reconciliation';summary.innerHTML=`<h2>${r.reconciled?'✓ Statement reconciled':'Statement needs review'}</h2><p class="small muted">${r.executions} executions → ${r.closedTrades} closed trades. This includes duplicates already saved.</p><div class="detail-grid">${[['Closed gross P/L',r.closedGross],['Closed fees',r.closedFees],['Closed net P/L',r.closedNet],['All execution fees',r.allFees],['Open settlement P/L',r.openSettlementPnl],['Open entry fees',r.openEntryFees],['Period futures cash result',r.periodLedgerNet],['Ending futures cash',r.closingFuturesCash]].map(([label,value])=>`<div><small>${label}</small>${money(value)}</div>`).join('')}</div><p class="small muted">Closed net + open settlement P/L − open entry fees = period futures cash result. Transfers and YTD totals are excluded.</p>`;$('import-preview').prepend(summary);}
      if(result.openPositions?.length){const open=document.createElement('div');open.className='reconciliation';open.innerHTML=`<h2>Open positions · excluded from closed P/L</h2><div class="table-wrap"><table><thead><tr><th>Instrument</th><th>Signed qty</th><th>Average entry</th><th>Entry fees</th></tr></thead><tbody>${result.openPositions.map(p=>`<tr><td>${escape(p.symbol)}</td><td>${p.quantity}</td><td>${p.averageEntry}</td><td>${money(p.entryFees)}</td></tr>`).join('')}</tbody></table></div>`;$('import-preview').append(open);}
      $('commit-import').hidden=result.errors.length>0||(result.trades.length===0&&!result.reconciliation);
      $('commit-import').textContent=`Import ${result.trades.length} trades${result.openPositions?.length?' + open position snapshot':''}`;
    }catch(error){$('import-error').textContent=error.message;}finally{$('preview-import').disabled=!csv;}
  });
  $('commit-import').addEventListener('click',async()=>{
    $('commit-import').disabled=true;$('import-error').textContent='';
    try{
      if(!token)throw new Error('Preview the file again.');
      const response=await fetchApp('/api/imports/commit',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token})});const result=await response.json();
      if(!response.ok)throw new Error(result.message??'Import failed.');
      $('import-preview').innerHTML=`<p class="import-success">Imported ${result.imported} trades. ${result.duplicates} duplicates skipped. ${result.openPositions??0} open position snapshots saved separately. Your source CSV and a pre-import backup are saved locally.</p>`;token=null;$('commit-import').hidden=true;await onImported();
    }catch(error){$('import-error').textContent=error.message;}finally{$('commit-import').disabled=false;}
  });
}
