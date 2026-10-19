import {createViewedRefresh} from './viewed-refresh.js';
import { fetchApp } from './http.js';
import {timeBounds, clampWindow, zoomWindow, pointsInWindow, bindChartNavigation} from './chart-viewport.js?v=ha-0.1.9';
const $=id=>document.getElementById(id),escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const price=n=>n===null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:4}).format(n);
const time=v=>new Intl.DateTimeFormat('en-US',{timeZone:'America/New_York',month:'short',day:'numeric',hour:'2-digit',minute:'2-digit',second:'2-digit'}).format(new Date(v))+' ET';
async function api(path,body,signal){const r=await fetchApp('/api/market/'+path,body===undefined?{signal}:{signal,method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw new Error(data.message??'Market request failed.');return data;}
export function series(history,source){return source==='bars'?history.bars.map(b=>({time:b.time,value:b.close})):history.quotes.filter(q=>q.last!==null).map(q=>({time:q.quoteTime??q.fetchedAt,value:q.last}));}
export function quoteMarker(quote,now=Date.now()){
 if(!quote||!Number.isFinite(quote.last)||!quote.quoteTime)return null;
 const timestamp=Date.parse(quote.quoteTime);if(!Number.isFinite(timestamp)||timestamp>now+5000)return null;
 return {time:quote.quoteTime,value:quote.last,label:now-timestamp>120000?'Stale':quote.realtime===true?'Live':quote.realtime===false?'Delayed':'Unverified'};
}
function markerInWindow(marker,window){return marker&&(!window||(+new Date(marker.time)>=window.start&&+new Date(marker.time)<=window.end))?marker:null;}
export function marketBounds(points,marker=null){
 const bounds=timeBounds(points);if(!marker)return bounds;
 const timestamp=+new Date(marker.time);
 return bounds?{start:Math.min(bounds.start,timestamp),end:Math.max(bounds.end,timestamp)}:timeBounds([marker]);
}
export function chartLayout(points,window=null,width=1000,marker=null){
 const shown=markerInWindow(marker,window),observations=shown?[...points,shown]:points;
 const w=width,h=390,left=80,right=85,top=25,bottom=55,values=observations.map(p=>p.value),low=Math.min(...values),high=Math.max(...values),pad=Math.max((high-low)*.15,Math.abs(high)*.0001,.02),min=low-pad,max=high+pad,first=window?.start??Math.min(...observations.map(p=>+new Date(p.time))),last=window?.end??Math.max(...observations.map(p=>+new Date(p.time))),span=Math.max(60000,last-first);
 return {w,h,left,right,top,bottom,min,max,first,span,x:p=>left+(+new Date(p.time)-first)/span*(w-left-right),y:v=>top+(max-v)/(max-min)*(h-top-bottom)};
}
export function nearestPoint(points,timestamp){let low=0,high=points.length-1;while(low<high){const mid=Math.floor((low+high)/2);if(+new Date(points[mid].time)<timestamp)low=mid+1;else high=mid;}const previous=points[Math.max(0,low-1)],next=points[low];return Math.abs(+new Date(previous.time)-timestamp)<=Math.abs(+new Date(next.time)-timestamp)?previous:next;}
export function priceChart(points,source,window=null,width=1000,marker=null){
 marker=source==='bars'?markerInWindow(marker,window):null;
 if(!points.length&&!marker)return '<div class="empty">No saved prices yet. Click Update prices or Analyze.</div>';
 const {w,h,left,right,top,bottom,min,max,first,span,x,y}=chartLayout(points,window,width,marker);
 const paths=[];let path='';points.forEach((p,i)=>{if(source==='bars'&&i&&+new Date(p.time)-+new Date(points[i-1].time)>90000){paths.push(path);path='';}path+=`${path?'L':'M'}${x(p)},${y(p.value)} `;});paths.push(path);
 const grid=Array.from({length:6},(_,i)=>min+(max-min)*i/5).map(v=>`<line x1="${left}" x2="${w-right}" y1="${y(v)}" y2="${y(v)}" stroke="#2e3c50"/><text x="${left-12}" y="${y(v)+5}" text-anchor="end" fill="#becbdf" font-size="14">${new Intl.NumberFormat('en-US',{minimumFractionDigits:2,maximumFractionDigits:2}).format(v)}</text>`).join('');
 const ticks=Array.from({length:w<700?3:5},(_,i)=>{const count=w<700?2:4,t=first+span*i/count,px=left+(w-left-right)*i/count,label=new Intl.DateTimeFormat('en-US',{timeZone:'America/New_York',...(span>86400000?{month:'short',day:'numeric'}:{}),hour:'numeric',minute:'2-digit'}).format(t);return `<line x1="${px}" x2="${px}" y1="${top}" y2="${h-bottom}" stroke="#233044" stroke-dasharray="3 6"/><text x="${px}" y="${h-22}" text-anchor="${i===0?'start':i===(w<700?2:4)?'end':'middle'}" fill="#becbdf" font-size="13">${escape(label)}</text>`;}).join('');
 const last=points.at(-1),lastY=last?y(last.value):null;
 const historicalLast=last?`<circle cx="${x(last)}" cy="${lastY}" r="4" fill="#77d6b0"/>${marker?'':`<line x1="${left}" x2="${w-right}" y1="${lastY}" y2="${lastY}" stroke="#77d6b0" stroke-dasharray="5 5" opacity=".55"/><rect x="${w-right+5}" y="${lastY-13}" width="78" height="26" rx="4" fill="#214b40"/><text x="${w-right+44}" y="${lastY+5}" text-anchor="middle" fill="#d4ffed" font-size="13">${price(last.value)}</text>`}`:'';
 const live=marker?`<g class="live-price-marker"><title>${escape(marker.label)} quote · ${escape(time(marker.time))} · ${price(marker.value)}</title><line x1="${left}" x2="${w-right}" y1="${y(marker.value)}" y2="${y(marker.value)}" stroke="#f2c879" stroke-dasharray="3 5"/><circle cx="${x(marker)}" cy="${y(marker.value)}" r="5" fill="#f2c879"/><rect x="${w-right+5}" y="${y(marker.value)-22}" width="78" height="42" rx="4" fill="#594521"/><text x="${w-right+44}" y="${y(marker.value)-6}" text-anchor="middle" fill="#ffe5a5" font-size="11">${escape(marker.label.toUpperCase())}</text><text x="${w-right+44}" y="${y(marker.value)+11}" text-anchor="middle" fill="#fff0ce" font-size="13">${price(marker.value)}</text></g>`:'';
 return `<svg viewBox="0 0 ${w} ${h}" role="img" aria-label="${source==='bars'?'Minute closing prices':'Sampled quotes'}; Eastern time on horizontal axis and price on vertical axis">${grid}${ticks}${paths.filter(Boolean).map(p=>`<path d="${p}" fill="none" stroke="#77d6b0" stroke-width="2.5" stroke-linejoin="round"/>`).join('')}${historicalLast}${live}<text x="${left}" y="17" fill="#becbdf" font-size="12">PRICE</text><text x="${w-right}" y="${h-3}" text-anchor="end" fill="#becbdf" font-size="12">EASTERN TIME</text><rect class="chart-selection" x="${left}" y="${top}" width="0" height="${h-top-bottom}" fill="#81b7ff" fill-opacity=".2" stroke="#81b7ff" pointer-events="none" hidden/><g class="chart-cursor" hidden><line y1="${top}" y2="${h-bottom}" stroke="#f2c879" stroke-dasharray="4 4"/><circle r="5" fill="#f2c879" stroke="#101b29" stroke-width="2"/></g></svg>`;
}
export function selectChartContract(selections,previous='',requested='/MES'){
 const valid=selections.filter(item=>!item.error);
 return valid.find(item=>item.symbol===previous)?.symbol??valid.find(item=>item.requested===requested)?.symbol??valid.find(item=>item.requested==='/MES')?.symbol??valid[0]?.symbol??'';
}
export async function mountMonitor(){
 const panel=document.createElement('article');panel.className='panel monitor-panel';panel.innerHTML=`<div class="panel-heading"><div><h2>Chart & signal</h2><p>Choose a contract. Analyze the chart. Act on your signal.</p></div></div><div class="connection-content"><div class="quote-controls"><label>Instrument<select id="monitor-symbol"></select></label><label>Range<select id="monitor-days"><option value="1" selected>24 hours</option><option value="3">3 days</option><option value="7">7 days</option></select></label><button id="monitor-refresh" class="quiet-button" type="button">Update prices</button><div id="monitor-analysis-action" class="chart-analysis-actions"></div></div><div id="monitor-position"></div><div id="monitor-message" role="status"></div><div id="monitor-quotes"></div><div class="chart-tools"><button id="chart-zoom-in" class="quiet-button" type="button">Zoom in</button><button id="chart-zoom-out" class="quiet-button" type="button">Zoom out</button><button id="chart-reset" class="quiet-button" type="button">Full range</button><span id="chart-window" class="small muted"></span></div><div id="monitor-chart" class="price-chart"></div><p id="monitor-hover" class="chart-readout">Drag across a time range to zoom · scroll to zoom at the pointer · double-click to reset.</p><p id="monitor-range" class="small muted"></p><div id="monitor-analysis"></div><details><summary>Watchlist & chart options</summary><form id="monitor-watchlist" class="quote-controls"><label>Watchlist symbols<input id="monitor-symbols" placeholder="/MES, /MGC, /MCL"></label><button class="quiet-button" type="submit">Save</button></form><div class="quote-controls"><label>Chart data<select id="monitor-source"><option value="bars">Minute bars</option><option value="quotes">Quote samples</option></select></label></div><p id="monitor-provenance" class="small muted"></p></details></div>`;
 document.querySelector('#market-panels').before(panel);
 let selectedRequest='/MES';
 let symbols=[],selections=[],busy=false,version=0,allPoints=[],visiblePoints=[],viewWindow=null,customWindow=false,visibleLayout=null,loadedKey='',fullRange=false;const latest=new Map();
 function options(){const selected=$('monitor-symbol').value;symbols=selections.filter(s=>!s.error).map(s=>s.symbol);$('monitor-symbol').replaceChildren(...selections.filter(s=>!s.error).map(s=>new Option(s.automatic?`${s.requested.slice(1)} · ${s.symbol} (auto)`:s.symbol,s.symbol)));$('monitor-symbol').value=selectChartContract(selections,selected,selectedRequest);if($('monitor-symbol').value!==selected){version++;allPoints=[];customWindow=false;fullRange=false;viewWindow=null;draw();rows();}$('monitor-symbols').value=selections.map(s=>s.requested).join(', ');window.dispatchEvent(new CustomEvent('market-context'));}
 function rows(){ $('monitor-quotes').innerHTML=symbols.filter(s=>s===$('monitor-symbol').value).map(symbol=>{const q=latest.get(symbol);return `<div class="watch-row"><div><strong>${escape(symbol)}</strong><p class="small muted">${q?`${q.realtime===true?'Realtime':q.realtime===false?'Delayed':'Realtime unknown'} · ${q.quoteTime?escape(time(q.quoteTime)):'Quote timestamp unknown'}`:'No saved quote in the last 7 days'}</p></div><div class="watch-price">${q?price(q.last):'—'}<p class="small muted">${q?'Fetched '+escape(time(q.fetchedAt)):''}</p></div></div>`;}).join('');}
 function currentMarker(){return $('monitor-source').value==='bars'?quoteMarker(latest.get($('monitor-symbol').value)):null;}
 function draw(){
  if($('monitor-chart').classList.contains('selecting'))return;
  cancelDrag();
  const marker=currentMarker(),bounds=marketBounds(allPoints,marker);
  if(bounds)viewWindow=fullRange?bounds:customWindow&&viewWindow?clampWindow(viewWindow,bounds):clampWindow({start:bounds.end-4*3600000,end:bounds.end},bounds);
  else viewWindow=null;
  visiblePoints=viewWindow?pointsInWindow(allPoints,viewWindow):[];
  const width=Math.max(520,Math.min(1000,$('monitor-chart').clientWidth||1000));
  visibleLayout=visiblePoints.length||markerInWindow(marker,viewWindow)?chartLayout(visiblePoints,viewWindow,width,marker):null;
  const source=$('monitor-source').value;
  $('monitor-chart').innerHTML=priceChart(visiblePoints,source,viewWindow,width,marker);
  $('chart-window').textContent=viewWindow?time(viewWindow.start)+' → '+time(viewWindow.end):'';
  $('monitor-hover').textContent='Drag across a time range to zoom · scroll to zoom at the pointer · double-click to reset.';
  const first=visiblePoints[0]?.value,last=visiblePoints.at(-1)?.value;
  $('monitor-range').textContent=visiblePoints.length?visiblePoints.length+' '+(source==='bars'?'minute closes':'quote samples')+' · first '+price(first)+' → last '+price(last)+' · change '+price(last-first)+' ('+(first?((last-first)/first*100).toFixed(2):'—')+'%)':'';
  if(allPoints.length&&!visiblePoints.length&&!markerInWindow(marker,viewWindow))$('monitor-chart').innerHTML='<div class="empty">No observations in this time range. Use Zoom out or Full range.</div>';
 }
 async function chart(signal){
  const current=++version,symbol=$('monitor-symbol').value;
  if(!symbol){allPoints=[];draw();return;}
  const source=$('monitor-source').value;
  try{
   const history=await api('history?'+new URLSearchParams({symbol,days:$('monitor-days').value}),undefined,signal);
   if(current!==version)return;
   const key=symbol+'|'+source+'|'+$('monitor-days').value;
   if(key!==loadedKey){customWindow=false;fullRange=false;viewWindow=null;loadedKey=key;}
   allPoints=series(history,source);draw();
   $('monitor-provenance').textContent=source==='bars'?'Closed one-minute bars returned by Schwab. Gaps remain unfilled. Latest saved history may be older than the current session.':'Irregular last-price samples captured by this app. Lines connect samples; the market path between them is unknown. Not OHLCV bars.';
  }catch(e){if(current===version&&!signal?.aborted)$('monitor-message').textContent=e.message;}
 }
 async function resolve(){selections=await api('contracts');options();const errors=selections.filter(s=>s.error);if(errors.length)$('monitor-message').textContent=errors.map(s=>`${s.requested}: ${s.error}`).join(' · ');}
 async function load(){await resolve();const symbol=$('monitor-symbol').value;if(symbol){const h=await api('history?'+new URLSearchParams({symbol,days:'7'}));const q=h.quotes.at(-1);if(q)latest.set(symbol,q);}rows();await chart();}
 async function work(fn){if(busy)return;busy=true;const controls=[...panel.querySelectorAll('#monitor-refresh,#monitor-watchlist button,.chart-tools button')];controls.forEach(c=>c.disabled=true);try{await fn();}catch(e){$('monitor-message').textContent=e.message;}finally{busy=false;controls.forEach(c=>c.disabled=false);}}
 $('monitor-watchlist').addEventListener('submit',e=>{e.preventDefault();const list=$('monitor-symbols').value.split(',').map(s=>s.trim()).filter(Boolean);work(async()=>{await api('watchlist',{symbols:list});await load();});});
 $('monitor-refresh').addEventListener('click',()=>work(async()=>{await resolve();const symbol=$('monitor-symbol').value;if(!symbol)throw new Error('Add a valid instrument to your watchlist first.');$('monitor-message').textContent='Updating prices…';if($('monitor-source').value==='bars')await api('backfill',{symbol});const q=await api('capture',{symbol});latest.set(symbol,q);rows();await chart();$('monitor-message').textContent='Prices updated.';}));
 for(const id of ['monitor-symbol','monitor-source','monitor-days'])$(id).addEventListener('change',()=>{if(id==='monitor-symbol')selectedRequest=selections.find(item=>item.symbol===$('monitor-symbol').value)?.requested??'/MES';customWindow=false;fullRange=false;viewWindow=null;allPoints=[];draw();rows();chart();poller.update();});
 window.addEventListener('market-prices-updated',()=>chart());
 function setWindow(window){viewWindow=window;customWindow=true;fullRange=false;draw();}
 const cancelDrag=bindChartNavigation($('monitor-chart'),{
  getState:()=>({layout:visibleLayout,window:viewWindow,bounds:marketBounds(allPoints,currentMarker())}),
  onWindow:setWindow,
  onReset:()=>{customWindow=false;fullRange=false;viewWindow=null;draw();},
  onHover:timestamp=>{
   if(!visiblePoints.length)return;
   const p=nearestPoint(visiblePoints,timestamp),svg=$('monitor-chart').querySelector('svg'),cursor=svg?.querySelector('.chart-cursor');
   if(!cursor)return;
   cursor.removeAttribute('hidden');
   const line=cursor.querySelector('line'),dot=cursor.querySelector('circle');
   line.setAttribute('x1',visibleLayout.x(p));line.setAttribute('x2',visibleLayout.x(p));
   dot.setAttribute('cx',visibleLayout.x(p));dot.setAttribute('cy',visibleLayout.y(p.value));
   $('monitor-hover').textContent=time(p.time)+' · '+price(p.value)+($('monitor-source').value==='bars'?' minute close':' observed quote');
  },
  onLeave:()=>$('monitor-chart').querySelector('.chart-cursor')?.setAttribute('hidden','')
 });
 for(const [id,factor] of [['chart-zoom-in',.5],['chart-zoom-out',2]])$(id).addEventListener('click',()=>{const bounds=marketBounds(allPoints,currentMarker());if(bounds&&viewWindow)setWindow(zoomWindow(viewWindow,bounds,(viewWindow.start+viewWindow.end)/2,factor));});
 $('chart-reset').addEventListener('click',()=>{const bounds=marketBounds(allPoints,currentMarker());if(bounds){fullRange=true;viewWindow=bounds;draw();}});
 let lastWidth=0;
 if(globalThis.ResizeObserver)new ResizeObserver(entries=>{const width=entries[0].contentRect.width;if(width&&width!==lastWidth){lastWidth=width;if(allPoints.length||currentMarker())draw();}}).observe($('monitor-chart'));
 let pageActive=true;
 const barRefresh=new Map();
 const poller=createViewedRefresh({
  selection:()=>[$('monitor-symbol').value,$('monitor-source').value,$('monitor-days').value].join('|'),
  visible:()=>pageActive&&!document.hidden&&!$('market').hidden&&!!$('monitor-symbol').value,
  refresh:async(key,signal)=>{
   if(busy)return;
   const [symbol,source,days]=key.split('|');
   const q=await api('capture',{symbol},signal);
   if(signal.aborted)return;
   latest.set(symbol,q);rows();
   $('monitor-message').textContent='Live prices · selected contract updates every second while viewed.';
   if(source==='bars'){
    draw();
    const minute=Math.floor(Date.now()/60000);
    if(barRefresh.get(symbol)===minute)return;
    barRefresh.set(symbol,minute);
    await api('backfill',{symbol},signal);
    if(signal.aborted)return;
    await chart(signal);
   }else{
    if(loadedKey!==key){await chart(signal);return;}
    if(q.last!==null){
     const point={time:q.quoteTime??q.fetchedAt,value:q.last};
     if(!allPoints.some(p=>p.time===point.time&&p.value===point.value))allPoints.push(point);
     allPoints=allPoints.filter(p=>+new Date(p.time)>=Date.now()-Number(days)*86400000).sort((a,b)=>+new Date(a.time)-+new Date(b.time)).slice(-12000);
    }
    if(!$('monitor-chart').classList.contains('selecting'))draw();
   }
   if(!signal.aborted)$('monitor-message').textContent='Live prices · selected contract updates every second while viewed.';
  },
  onError:error=>{$('monitor-message').textContent=error.message;}
 });
 document.addEventListener('visibilitychange',()=>poller.update());
 new MutationObserver(()=>poller.update()).observe($('market'),{attributes:true,attributeFilter:['hidden']});
 window.addEventListener('pagehide',()=>{pageActive=false;poller.update();});
 window.addEventListener('pageshow',()=>{pageActive=true;poller.update();});
 try{await load();}catch(e){$('monitor-message').textContent=e.message;}
 poller.update();
}
