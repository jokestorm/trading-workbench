import {fetchApp} from './http.js';

async function request(path,body,method='PUT'){
  const response=await fetchApp('/api/ai/prompts'+path,body===undefined?{cache:'no-store'}:{method,headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  const data=await response.json();if(!response.ok)throw new Error(data.message??'Could not update prompts.');return data;
}
export async function mountPromptSettings(container,{api=request}={}){
  container.textContent='Loading prompts…';
  try{
    const views=await api('');container.replaceChildren();
    for(const initial of views){
      let saved=initial,basedOnVersion=initial.basedOnVersion??initial.defaultVersion,busy=false;
      const panel=document.createElement('details');panel.className='prompt-editor panel';panel.open=initial.kind==='market';
      panel.innerHTML='<summary></summary><div class="prompt-content"><p class="prompt-source small muted"></p><p class="prompt-update" role="status" hidden></p><label class="prompt-label">Your prompt<textarea class="prompt-text" rows="16" maxlength="24000" spellcheck="false"></textarea></label><p class="small muted">Trade or market data is attached automatically. Changes apply to your next analysis.</p><div class="prompt-actions"><button class="primary-button prompt-save" type="button">Save custom</button><button class="quiet-button prompt-default" type="button">Use latest default</button><button class="quiet-button prompt-reset" type="button">Reset edits</button></div><p class="prompt-message" role="status"></p><details class="prompt-compare"><summary>Compare with latest app default</summary><p class="small muted">Compare your editor above with the current app default below. Copy the changes you want, then save.</p><label class="prompt-label">Latest app default<textarea class="prompt-baseline" rows="16" readonly spellcheck="false"></textarea></label><label class="small muted"><input class="prompt-reviewed" type="checkbox"> I incorporated the changes I want from this default</label><button class="quiet-button prompt-copy" type="button">Copy default to editor</button></details></div>';
      panel.querySelector('summary').textContent=initial.kind==='market'?'Market analysis':'Journal review';
      const editor=panel.querySelector('.prompt-text'),baseline=panel.querySelector('.prompt-baseline'),message=panel.querySelector('.prompt-message'),reviewed=panel.querySelector('.prompt-reviewed');
      function show(){
        basedOnVersion=saved.basedOnVersion??saved.defaultVersion;editor.value=saved.customText??saved.defaultText;baseline.value=saved.defaultText;reviewed.checked=false;
        panel.querySelector('.prompt-source').textContent=`Active: ${saved.activeSource==='custom'?'Your custom prompt':'App default'} · Latest default: ${saved.defaultVersion}${saved.basedOnVersion?' · Custom based on: '+saved.basedOnVersion:''}`;
        const update=panel.querySelector('.prompt-update');update.hidden=!saved.updateAvailable;update.textContent='New default available. Your custom prompt is unchanged. Compare below to keep selected changes or use the latest default.';
      }
      async function change(action){
        if(busy)return;busy=true;message.textContent='Saving…';panel.querySelectorAll('button').forEach(button=>button.disabled=true);editor.disabled=true;reviewed.disabled=true;
        try{saved=await action();show();message.textContent=saved.activeSource==='custom'?'Custom prompt saved.':'Latest app default active.';}
        catch(error){message.textContent=error.message;}
        finally{busy=false;panel.querySelectorAll('button').forEach(button=>button.disabled=false);editor.disabled=false;reviewed.disabled=false;}
      }
      panel.querySelector('.prompt-save').addEventListener('click',()=>change(()=>api('/'+saved.kind,{text:editor.value,basedOnVersion:reviewed.checked?saved.defaultVersion:basedOnVersion,revision:saved.revision})));
      panel.querySelector('.prompt-default').addEventListener('click',()=>change(()=>api('/'+saved.kind+'/default',{revision:saved.revision},'POST')));
      panel.querySelector('.prompt-reset').addEventListener('click',()=>{if(!busy){show();message.textContent='Unsaved edits reset.';}});
      panel.querySelector('.prompt-copy').addEventListener('click',()=>{if(!busy){editor.value=saved.defaultText;basedOnVersion=saved.defaultVersion;reviewed.checked=true;message.textContent='Default copied. Edit it and save custom, or use the latest default directly.';}});
      show();container.append(panel);
    }
  }catch(error){container.textContent=error.message;}
}
