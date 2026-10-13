import { fetchApp } from './http.js';
const $=id=>document.getElementById(id);
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const decimal=n=>n===null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:4}).format(n);
const eastern=value=>value?new Intl.DateTimeFormat('en-US',{timeZone:'America/New_York',dateStyle:'medium',timeStyle:'medium'}).format(new Date(value))+' ET':'Not supplied';
async function request(path,body) {
  const response=await fetchApp('/api/schwab/'+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  const result=await response.json().catch(()=>({message:'The connection host returned an unexpected response.'}));
  if(!response.ok)throw new Error(result.message??'Could not complete the connection request.');return result;
}
export async function mountMarket() {
  const panel=document.createElement('article');panel.className='panel schwab-panel';
  panel.innerHTML=`<div class="panel-heading"><div><h2>Schwab connection</h2><p>One quote on demand · verify equity and futures access</p></div><span id="schwab-state" class="demo-badge">NOT CONFIGURED</span></div>
    <div class="connection-content"><p class="small muted">If your Schwab app is awaiting approval, save its settings now and authorize once approved. Market Data access and futures symbols will be checked with actual requests.</p>
    <details id="schwab-settings-details" open><summary>App settings</summary><form id="schwab-settings"><div class="entry-grid"><label>App key<input name="appKey" autocomplete="off" maxlength="256" placeholder="Schwab app key"></label><label>App secret<input name="appSecret" type="password" autocomplete="new-password" maxlength="256" placeholder="Schwab app secret"></label><label class="full-width">Registered callback URL<input name="callbackUrl" type="url" required placeholder="https://127.0.0.1:8182"></label></div><p class="small muted">Use the exact registered HTTPS loopback callback. Saved keys and tokens stay encrypted on this computer. Leave key or secret blank to keep the saved value.</p><div class="form-actions"><button class="quiet-button" type="submit">Save settings</button></div></form></details>
    <div class="connection-actions"><button id="schwab-authorize" class="primary-button" type="button" disabled>Start authorization</button><button id="schwab-disconnect" class="quiet-button" type="button" disabled>Disconnect</button><span id="schwab-token-status" class="small muted"></span></div>
    <div id="schwab-auth-step" hidden><p class="small">1. <a id="schwab-auth-link" target="_blank" rel="noopener noreferrer">Open Schwab authorization</a> and sign in directly with Schwab. This link expires in 10 minutes.</p><p class="small muted">2. After approval, copy the complete callback URL from the address bar, even if that local page cannot load. Paste it below and finish here. Keep this app running during authorization.</p><form id="schwab-callback"><label class="small muted">Callback URL<input id="schwab-returned-url" type="password" autocomplete="off" required placeholder="https://127.0.0.1:8182/?code=…&state=…"></label><button class="primary-button" type="submit">Finish authorization</button></form></div>
    <div id="schwab-message" role="status" aria-live="polite"></div>
    <form id="schwab-quote" class="quote-controls"><label>Verify one symbol<input id="schwab-symbol" required maxlength="32" value="AAPL" placeholder="AAPL or /MESZ26"></label><button id="schwab-fetch" class="primary-button" type="submit" disabled>Fetch quote</button><span class="small muted">Try an equity first, then a dated futures contract.</span></form><div id="schwab-quote-result"><p class="small muted">No quote fetched yet. Choose an instrument in the Prices panel.</p></div></div>`;
  const connection=document.createElement('details');connection.className='schwab-settings-section';connection.innerHTML='<summary>Schwab connection & settings</summary>';connection.append(panel);document.querySelector('#market-panels').after(connection);
  document.querySelector('.market-status').innerHTML='<span id="market-connection-summary">Schwab not connected</span><span class="small muted">Market prices and AI analysis</span>';
  const settings=$('schwab-settings');let busy=false;
  function showStatus(status) { connection.open=!status.connected;
    $('schwab-state').textContent=status.connected?'AUTHORIZED':status.configured?'SETTINGS SAVED':'NOT CONFIGURED';
    $('schwab-authorize').disabled=!status.configured;$('schwab-disconnect').disabled=!status.connected;$('schwab-fetch').disabled=!status.connected;
    $('market-connection-summary').textContent=status.connected?'Schwab connected':status.configured?'Schwab configured · authorization required':'Schwab not configured';
    settings.elements.callbackUrl.value=status.callbackUrl;
    settings.elements.appKey.placeholder=status.configured?`Saved key ending ${status.appKeySuffix} · blank keeps it`:'Schwab app key';
    settings.elements.appSecret.placeholder=status.configured?'Saved secret · blank keeps it':'Schwab app secret';
    $('schwab-settings-details').open=!status.configured;
    $('schwab-token-status').textContent=status.accessExpiresAt?`Access token expires ${eastern(status.accessExpiresAt)}; refresh is automatic when fetching.`:'';
  }
  async function action(work) {
    if(busy)return;busy=true;$('schwab-message').textContent='Working…';panel.setAttribute('aria-busy','true');
    const controls=[...panel.querySelectorAll('button,input')],disabled=controls.map(c=>c.disabled);controls.forEach(c=>c.disabled=true);
    try{await work();}catch(error){$('schwab-message').textContent=error.message;}
    finally{controls.forEach((c,i)=>c.disabled=disabled[i]);busy=false;panel.removeAttribute('aria-busy');try{showStatus(await request('status'));}catch(error){$('schwab-message').textContent=error.message;}}
  }
  settings.addEventListener('submit',e=>{e.preventDefault();const body=Object.fromEntries(new FormData(settings));action(async()=>{await request('settings',body);settings.elements.appKey.value='';settings.elements.appSecret.value='';$('schwab-auth-step').hidden=true;$('schwab-quote-result').textContent='Fetch a quote after authorization.';$('schwab-message').textContent='Settings saved locally. Authorize after Schwab approves your app.';});});
  $('schwab-authorize').addEventListener('click',()=>action(async()=>{const result=await request('authorize',{});$('schwab-auth-link').href=result.url;$('schwab-returned-url').value='';$('schwab-auth-step').hidden=false;$('schwab-message').textContent='Open the Schwab link, then return here with the callback URL.';}));
  $('schwab-callback').addEventListener('submit',e=>{e.preventDefault();const url=$('schwab-returned-url').value;action(async()=>{try{await request('callback',{url});$('schwab-auth-step').hidden=true;$('schwab-message').textContent='Authorized. Fetch AAPL to verify Market Data access.';}finally{$('schwab-returned-url').value='';}});});
  $('schwab-disconnect').addEventListener('click',()=>action(async()=>{await request('disconnect',{});$('schwab-auth-step').hidden=true;$('schwab-quote-result').textContent='Disconnected. Local tokens removed; app settings retained.';$('schwab-message').textContent='Disconnected locally. This does not revoke consent in Schwab.';}));
  $('schwab-quote').addEventListener('submit',e=>{e.preventDefault();const symbol=$('schwab-symbol').value;action(async()=>{const quote=await request('quote',{symbol});const age=quote.quoteTime?Math.max(0,Math.floor((new Date(quote.fetchedAt)-new Date(quote.quoteTime))/1000)):null;
    $('schwab-quote-result').innerHTML=`<div class="live-quote"><div><h2>${escape(quote.symbol)}</h2><span class="demo-badge">SCHWAB · ${escape(quote.assetType)}</span><p>${quote.realtime===true?'Realtime entitlement reported':quote.realtime===false?'Delayed quote':'Realtime status not supplied'}</p></div><div class="metric-value">${decimal(quote.last)}</div><div class="quote-fields">${[['Bid / ask',`${decimal(quote.bid)} / ${decimal(quote.ask)}`],['Change',`${decimal(quote.change)} (${decimal(quote.changePercent)}%)`],['Quote timestamp',eastern(quote.quoteTime)],['Fetched at',eastern(quote.fetchedAt)],['Quote age when fetched',age===null?'Unknown':`${age} seconds`]].map(([label,value])=>`<div><span class="small muted">${label}</span><strong>${escape(value)}</strong></div>`).join('')}</div><p class="small muted">A snapshot from this request. No streaming or background polling yet; market closure can make quote timestamps older.</p></div>`;
    $('schwab-message').textContent='Quote received. Fetch again to update.';});});
  try{showStatus(await request('status'));}catch(error){$('schwab-message').textContent=error.message;}
}
