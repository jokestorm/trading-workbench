import {mountCodexLogin} from './codex-login.js?v=chat-only-1';
import {fetchApp,readAppJson} from './http.js?v=http-errors-1';

export function mountChatConnection(){
  mountCodexLogin([document.getElementById('chat-connection')],{
    request:async(path,body)=>{
      const response=await fetchApp('/api/'+path,{signal:AbortSignal.timeout(15000),...(body===undefined?{cache:'no-store'}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})});
      return readAppJson(response,'ChatGPT connection');
    },
    onReady:()=>{}
  });
}
