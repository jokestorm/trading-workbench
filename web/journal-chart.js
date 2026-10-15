import {timeBounds, clampWindow, zoomWindow, bindChartNavigation} from './chart-viewport.js?v=ha-0.1.5';

const money=n=>new Intl.NumberFormat('en-US',{style:'currency',currency:'USD'}).format(n);
// Journal dates/times are already Eastern wall-clock values. Project them onto
// UTC coordinates and format in UTC so the browser's timezone cannot shift them.
const label=(t,short=false)=>new Intl.DateTimeFormat('en-US',{timeZone:'UTC',month:'short',day:'numeric',...(short?{}:{hour:'numeric',minute:'2-digit',second:'2-digit'})}).format(t);
export function journalSeries(curve){
  const points=curve.map(p=>({time:new Date(p.date+'T'+(p.time||'00:00:00')+'Z').toISOString(),value:p.value}));
  if(!points.length)return [];
  return [{time:new Date(Date.parse(points[0].time)-60_000).toISOString(),value:0,baseline:true},...points];
}
export function balanceAt(points,timestamp){
  let low=0,high=points.length;
  while(low<high){const mid=(low+high)>>>1;if(Date.parse(points[mid].time)<=timestamp)low=mid+1;else high=mid;}
  return points[Math.max(0,low-1)];
}
export function journalWindow(points,window){
  if(!points.length)return [];
  const carry=balanceAt(points,window.start),visible=points.filter(p=>{const t=Date.parse(p.time);return t>window.start&&t<=window.end;});
  const result=[{...carry,time:new Date(window.start).toISOString(),boundary:true},...visible];
  if(Date.parse(result.at(-1).time)<window.end)result.push({...result.at(-1),time:new Date(window.end).toISOString(),boundary:true});
  return result;
}
export function journalLayout(points,window,width=740){
  const w=width,h=300,left=85,right=25,top=20,bottom=50;
  const values=points.map(p=>p.value),low=Math.min(...values),high=Math.max(...values),pad=Math.max(5,(high-low)*.15),min=low-pad,max=high+pad;
  const first=window.start,span=window.end-window.start;
  return {w,h,left,right,top,bottom,first,span,min,max,x:p=>left+(Date.parse(p.time)-first)/span*(w-left-right),y:value=>top+(max-value)/(max-min)*(h-top-bottom)};
}
export function journalChart(points,window,width=740){
  if(!points.length||!window)return '<div class="empty">No trades in this range. Adjust your filters.</div>';
  const visible=journalWindow(points,window),layout=journalLayout(visible,window,width),{w,h,left,right,top,bottom,min,max,x,y}=layout;
  let path='M'+x(visible[0])+','+y(visible[0].value);
  for(const point of visible.slice(1))path+='H'+x(point)+'V'+y(point.value);
  const grid=Array.from({length:5},(_,i)=>min+(max-min)*i/4).map(v=>`<line x1="${left}" x2="${w-right}" y1="${y(v)}" y2="${y(v)}" stroke="#263143" stroke-dasharray="3 5"/><text x="${left-10}" y="${y(v)+4}" text-anchor="end" fill="#becbdf" font-size="12">${money(v)}</text>`).join('');
  const count=w<650?2:4;
  const ticks=Array.from({length:count+1},(_,i)=>{const t=window.start+(window.end-window.start)*i/count,px=left+(w-left-right)*i/count;const text=window.end-window.start<86400000?new Intl.DateTimeFormat('en-US',{timeZone:'UTC',hour:'numeric',minute:'2-digit'}).format(t):label(t,true);return `<text x="${px}" y="${h-20}" text-anchor="${i===0?'start':i===count?'end':'middle'}" fill="#becbdf" font-size="12">${text}</text>`;}).join('');
  return `<svg viewBox="0 0 ${w} ${h}" role="img" aria-label="Cumulative realized net P/L; Eastern closing times. Drag or scroll to zoom, double-click to reset.">${grid}${ticks}${min<=0&&max>=0?`<line x1="${left}" x2="${w-right}" y1="${y(0)}" y2="${y(0)}" stroke="#53617a"/>`:''}<path d="${path}" fill="none" stroke="#77d6b0" stroke-width="2.5" stroke-linejoin="round"/>${visible.filter(p=>!p.boundary&&!p.baseline).map(p=>`<circle cx="${x(p)}" cy="${y(p.value)}" r="3" fill="#77d6b0"><title>${label(Date.parse(p.time))} ET: ${money(p.value)}</title></circle>`).join('')}<text x="${w-right}" y="${h-3}" text-anchor="end" fill="#becbdf" font-size="11">EASTERN TIME</text><rect class="chart-selection" x="${left}" y="${top}" width="0" height="${h-top-bottom}" fill="#81b7ff" fill-opacity=".2" stroke="#81b7ff" pointer-events="none" hidden/><g class="chart-cursor" hidden><line y1="${top}" y2="${h-bottom}" stroke="#f2c879" stroke-dasharray="4 4"/><circle r="4" fill="#f2c879"/></g></svg>`;
}
export function mountJournalChart(container){
  container.classList.add('journal-equity-chart');
  const controls=document.createElement('div');controls.className='chart-tools journal-chart-tools';
  controls.innerHTML='<button class="quiet-button" type="button" data-zoom="in">Zoom in</button><button class="quiet-button" type="button" data-zoom="out">Zoom out</button><button class="quiet-button" type="button" data-zoom="reset">Full range</button><span class="small muted" data-window></span>';
  container.before(controls);
  const readout=document.createElement('p');readout.className='chart-readout journal-chart-readout';container.after(readout);
  let points=[],window=null,layout=null,filterKey=null,custom=false;
  const hint='Drag to zoom · scroll to zoom at the pointer · double-click to reset.';
  function draw(){
    cancelDrag();const bounds=timeBounds(points);
    window=bounds?(custom&&window?clampWindow(window,bounds):bounds):null;
    const width=Math.max(520,Math.min(1000,container.clientWidth||740));
    layout=window?journalLayout(journalWindow(points,window),window,width):null;
    container.innerHTML=journalChart(points,window,width);readout.textContent=hint;
    controls.querySelector('[data-window]').textContent=window?label(window.start)+' → '+label(window.end)+' ET':'';
    controls.querySelectorAll('button').forEach(b=>b.disabled=!bounds);
  }
  function reset(){custom=false;window=null;draw();}
  function setWindow(value){custom=true;window=value;draw();}
  const cancelDrag=bindChartNavigation(container,{
    getState:()=>({layout,window,bounds:timeBounds(points)}),onWindow:setWindow,onReset:reset,
    onHover:t=>{
      const point=balanceAt(points,t),cursor=container.querySelector('.chart-cursor');if(!cursor)return;
      cursor.removeAttribute('hidden');const px=layout.x({time:new Date(t).toISOString()});
      for(const key of ['x1','x2'])cursor.querySelector('line').setAttribute(key,px);
      cursor.querySelector('circle').setAttribute('cx',px);cursor.querySelector('circle').setAttribute('cy',layout.y(point.value));
      readout.textContent=label(t)+' ET · cumulative P/L '+money(point.value);
    },onLeave:()=>container.querySelector('.chart-cursor')?.setAttribute('hidden','')
  });
  controls.addEventListener('click',event=>{
    const action=event.target.closest('[data-zoom]')?.dataset.zoom;if(!action)return;
    if(action==='reset'){reset();return;}
    const bounds=timeBounds(points);if(bounds&&window)setWindow(zoomWindow(window,bounds,(window.start+window.end)/2,action==='in'?.5:2));
  });
  let lastWidth=0;
  if(globalThis.ResizeObserver)new ResizeObserver(entries=>{const width=entries[0].contentRect.width;if(width&&width!==lastWidth){lastWidth=width;draw();}}).observe(container);
  return {update(curve,key){if(key!==filterKey){custom=false;window=null;filterKey=key;}points=journalSeries(curve);draw();}};
}
