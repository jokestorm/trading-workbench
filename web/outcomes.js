import { fetchApp } from './http.js';
const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number=v=>v===null?'—':new Intl.NumberFormat('en-US',{maximumFractionDigits:4}).format(v);
const time=v=>v?new Intl.DateTimeFormat('en-US',{timeZone:'America/New_York',dateStyle:'short',timeStyle:'medium'}).format(new Date(v))+' ET':'—';
async function request(url,body){const r=await fetchApp(url,body===undefined?{cache:'no-store'}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await r.json();if(!r.ok)throw new Error(data.message??'Follow-up request failed.');return data;}
export function mountOutcomes(run,container){
 if(run.kind!=='saved-market')return;
 const panel=document.createElement('section');panel.className='outcomes-panel';panel.innerHTML='<div class="followup-heading"><h3>Price follow-up</h3><button class="quiet-button refresh-followup" type="button">Update follow-up</button></div><div class="followup-result" role="status"></div>';
 container.append(panel);const result=panel.querySelector('.followup-result'),button=panel.querySelector('button');let busy=false;
 async function check(update){if(busy)return;busy=true;button.disabled=true;result.textContent=update?'Updating prices…':'Loading follow-up…';try{
   if(update)await request('/api/market/'+(run.snapshot.bars?'backfill':'capture'),{symbol:run.snapshot.symbol});
   const report=await request('/api/ai/outcomes?'+new URLSearchParams({id:run.id}));
   result.innerHTML=`<p class="small muted">${escape(report.symbol)} · reference ${number(report.referencePrice)} at ${escape(time(report.referenceAt))}</p><div class="table-wrap"><table><thead><tr><th>After</th><th>Status</th><th class="numeric">Price</th><th class="numeric">Change</th><th class="numeric">Change %</th></tr></thead><tbody>${report.windows.map(w=>`<tr><td>${w.minutes} min</td><td>${escape(w.status.replaceAll('-',' '))}</td><td class="numeric">${number(w.price)}</td><td class="numeric ${w.change<0?'negative':w.change>0?'positive':''}">${number(w.change)}</td><td class="numeric">${number(w.changePercent)}${w.changePercent===null?'':'%'}</td></tr>`).join('')}</tbody></table></div><details><summary>Timing & data coverage</summary><p>${escape(report.note)}</p><p>Analysis completed ${escape(time(report.analysisAvailableAt))}. Reference status: ${escape(report.referenceStatus.replaceAll('-',' '))}.</p>${report.windows.map(w=>`<p>${w.minutes} min: observed ${escape(time(w.observedAt))} · ${w.observations} observations · ${w.status==='observed'?(w.completeMinuteCoverage?'complete minutes':report.source==='bars'?'gaps / incomplete':'sampled quotes'):'awaiting data'}.</p>`).join('')}<p>Price changes only; no trade execution or fees. Updating follow-up does not rerun AI.</p></details>`;
  }catch(e){result.textContent=e.message;}finally{busy=false;button.disabled=false;}}
 button.addEventListener('click',()=>check(true));check(false);
}
