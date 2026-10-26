import {fetchApp,readAppJson} from './http.js?v=http-errors-1';

const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const format=value=>escape(value).replace(/`([^`\n]+)`/g,'<code>$1</code>').replace(/\*\*([^*\n]+)\*\*/g,'<strong>$1</strong>');
function clientId(){
  // Available on ordinary HTTP Home Assistant origins as well as HTTPS.
  const bytes=globalThis.crypto.getRandomValues(new Uint8Array(16));bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;
  const hex=Array.from(bytes,byte=>byte.toString(16).padStart(2,'0')).join('');
  return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;
}
export function renderTraderMessages(messages){
  if(!messages.length)return '<div class="trader-empty"><h2>Talk through the trade.</h2><p>Ask about a setup, challenge the caller’s reasoning, or look back at your results. Trader can read the same transactions and market data as Workbench.</p></div>';
  return messages.map(m=>`<article class="trader-message trader-${m.role==='user'?'user':'assistant'}"><div class="trader-message-meta">${m.role==='user'?'You':'Trader'}${m.symbol?` · ${escape(m.symbol)}`:''} <time>${escape(new Date(m.time).toLocaleTimeString('en-US',{timeZone:'America/New_York',hour:'numeric',minute:'2-digit'}))} ET</time></div><div class="trader-message-text">${format(m.text)}</div>${m.status==='interrupted'?'<small class="muted">Reply interrupted</small>':''}</article>`).join('');
}
// Loading and observing never start inference. Only a new user message does.
export function observeTrader({load,onState,onError=()=>{},visible=()=>true,schedule=setTimeout,unschedule=clearTimeout}){
  let timer=null,reading=false,disposed=false;
  async function update(){
    unschedule(timer);if(disposed||reading)return;reading=true;let busy=false;
    try{if(visible()){const state=await load();if(!disposed){busy=state.busy;onState(state);}}}catch(error){if(!disposed)onError(error);}
    finally{reading=false;if(!disposed)timer=schedule(update,busy?700:6000);}
  }
  return {update,dispose(){disposed=true;unschedule(timer);}};
}
export function mountTraderChat(){
  const nav=document.createElement('button');nav.className='nav-item';nav.dataset.tab='trader';nav.innerHTML='<span>◌</span> Trader chat';document.querySelector('nav[aria-label="Main navigation"] [data-tab="settings"]').before(nav);
  const section=document.createElement('section');section.id='trader';section.hidden=true;section.setAttribute('aria-label','Trader chat');
  section.innerHTML=`<div class="page-heading"><div><div class="eyebrow">YOUR TRADING CONVERSATION</div><h1>Trader chat</h1><p>Discuss the market, your trades, and the caller’s reasoning.</p></div><a class="quiet-button" href="#market">Open market monitor</a></div>
    <article class="panel trader-panel"><div class="trader-session"><span id="trader-model">ChatGPT connection · shared plan usage</span><span class="small muted">Conversation saved on this instance</span></div><div id="trader-messages" class="trader-messages" aria-label="Conversation"></div><div id="trader-activity" role="status" class="small muted"></div><div id="trader-error" role="alert"></div><form id="trader-form"><label class="trader-scope">Discussing<select id="trader-symbol"><option value="">Account & all markets</option></select></label><label class="trader-input-label" for="trader-input">Message Trader</label><textarea id="trader-input" rows="3" maxlength="12000" placeholder="What are you seeing right now?" required></textarea><div class="trader-compose-actions"><span class="small muted">Enter to send · Shift+Enter for a new line</span><button class="quiet-button" type="button" id="trader-stop" hidden>Stop reply</button><button class="primary-button" id="trader-send">Send</button></div></form></article>`;
  const footer=document.querySelector('main > footer');if(footer)footer.before(section);else document.querySelector('main').append(section);
  const messages=section.querySelector('#trader-messages'),input=section.querySelector('#trader-input'),send=section.querySelector('#trader-send'),stop=section.querySelector('#trader-stop'),error=section.querySelector('#trader-error'),activity=section.querySelector('#trader-activity'),symbol=section.querySelector('#trader-symbol');
  let revision=-1,busy=false,pending=null,sending=false;
  function apply(state){
    busy=state.busy;send.disabled=busy||sending;stop.hidden=!busy;activity.textContent=state.activity??'';error.textContent=state.error??'';
    section.querySelector('#trader-model').textContent=`${state.model??'Codex'} · ChatGPT plan`;
    if(state.revision!==revision){const nearBottom=messages.scrollHeight-messages.scrollTop-messages.clientHeight<100;messages.innerHTML=renderTraderMessages(state.messages??[]);revision=state.revision;if(nearBottom||busy)messages.scrollTop=messages.scrollHeight;}
  }
  async function read(path,options){const response=await fetchApp(path,{signal:AbortSignal.timeout(15000),...options});return readAppJson(response,'Trader request');}
  const observer=observeTrader({load:()=>read('/api/ai/trader'),onState:apply,onError:e=>{error.textContent=e.message;},visible:()=>!document.hidden&&!section.hidden});
  async function loadProducts(){
    try{const current=await read('/api/ai/monitor');const previous=symbol.value;symbol.replaceChildren(new Option('Account & all markets',''));for(const market of current.markets??[])symbol.add(new Option(market.symbol,market.symbol));if([...symbol.options].some(o=>o.value===previous))symbol.value=previous;}catch(e){error.textContent=e.message;}
  }
  section.querySelector('#trader-form').addEventListener('submit',async event=>{
    event.preventDefault();const text=input.value.trim();if(!text||busy||sending)return;
    sending=true;send.disabled=true;error.textContent='';
    try{
      const selectedSymbol=symbol.value||null;
      if(!pending||pending.text!==text||pending.symbol!==selectedSymbol)pending={clientId:clientId(),text,symbol:selectedSymbol};
      const state=await read('/api/ai/trader/messages',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(pending)});input.value='';pending=null;apply(state);observer.update();
    }catch(e){error.textContent=e.message;}finally{sending=false;send.disabled=busy;}
  });
  input.addEventListener('keydown',event=>{if(event.key==='Enter'&&!event.shiftKey&&!event.isComposing){event.preventDefault();section.querySelector('form').requestSubmit();}});
  stop.addEventListener('click',async()=>{stop.disabled=true;try{apply(await read('/api/ai/trader/stop',{method:'POST'}));observer.update();}catch(e){error.textContent=e.message;}finally{stop.disabled=false;}});
  function visibility(){if(!section.hidden&&!document.hidden){observer.update();loadProducts();}}
  new MutationObserver(visibility).observe(section,{attributes:true,attributeFilter:['hidden']});document.addEventListener('visibilitychange',visibility);
  window.addEventListener('pagehide',()=>observer.dispose());messages.innerHTML=renderTraderMessages([]);
}
