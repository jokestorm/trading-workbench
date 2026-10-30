const idle={state:'idle',message:'Sign in with ChatGPT to use Trader chat.'};
export function deviceUrl(value){
  try{const url=new URL(value);return url.protocol==='https:'&&url.hostname==='auth.openai.com'&&!url.port&&!url.username&&!url.password&&!url.search&&!url.hash&&url.pathname.replace(/\/$/,'')==='/codex/device'?url.href:null;}catch{return null;}
}
export function mountCodexLogin(containers,{request,onReady}){
  let status={installed:false,subscriptionLogin:false,message:'Checking ChatGPT login…'},login=idle,busy=false,timer=null,polling=false,revision=0;
  const views=containers.map(container=>{
    container.innerHTML='<summary>ChatGPT connection</summary><p class="codex-state small muted" role="status" aria-live="polite"></p><div class="connection-actions"><button class="primary-button codex-signin" type="button">Sign in with ChatGPT</button><button class="quiet-button codex-check" type="button">Check connection</button><button class="quiet-button codex-signout" type="button" hidden>Sign out</button><button class="quiet-button codex-cancel" type="button" hidden>Cancel sign-in</button></div><div class="codex-device" hidden><p>Open <a class="codex-link" target="_blank" rel="noopener noreferrer">OpenAI sign-in</a> and enter this one-time code:</p><input class="codex-code" readonly aria-label="One-time ChatGPT sign-in code" autocomplete="off" spellcheck="false"><p class="small muted">Keep this app running while you sign in. If device login is unavailable, enable it in your ChatGPT security settings.</p></div>';
    return {container,message:container.querySelector('.codex-state'),signin:container.querySelector('.codex-signin'),check:container.querySelector('.codex-check'),signout:container.querySelector('.codex-signout'),cancel:container.querySelector('.codex-cancel'),device:container.querySelector('.codex-device'),link:container.querySelector('.codex-link'),code:container.querySelector('.codex-code')};
  });
  function render(){
    const pending=login.state==='pending',connected=!pending&&status.subscriptionLogin,url=deviceUrl(login.verificationUrl);
    onReady(connected&&!busy,connected?'ChatGPT connected':pending?'Complete ChatGPT sign-in':status.message);
    for(const view of views){
      view.message.textContent=pending||login.state==='failed'?login.message:status.message;
      view.signin.hidden=connected||pending;view.signout.hidden=!connected;view.cancel.hidden=!pending;
      for(const button of [view.signin,view.check,view.signout,view.cancel])button.disabled=busy;
      view.signin.disabled=busy||!status.installed;
      view.device.hidden=!pending||!url;
      if(pending&&url){view.link.href=url;view.code.value=login.userCode??'';view.container.open=true;}
      else{view.link.removeAttribute('href');view.code.value='';}
      if(!connected)view.container.open=true;
    }
  }
  function schedule(){clearTimeout(timer);if(login.state==='pending')timer=setTimeout(poll,document.hidden?10_000:2_000);}
  async function check(){
    const current=++revision;
    try{
      const [nextStatus,nextLogin]=await Promise.all([request('ai/status'),request('ai/login')]);
      if(current!==revision)return;
      status=nextStatus;login=nextLogin;render();schedule();
    }catch(error){if(current===revision){status={...status,subscriptionLogin:false,message:error.message};render();}}
  }
  async function poll(){
    if(polling||busy)return schedule();polling=true;const current=revision;
    try{
      const next=await request('ai/login');if(current!==revision)return;
      login=next;if(login.state!=='pending'){await check();return;}render();schedule();
    }catch(error){if(current===revision){login={state:'failed',message:error.message};render();}}
    finally{polling=false;}
  }
  async function act(path){
    if(busy)return;busy=true;revision++;clearTimeout(timer);render();
    try{
      login=await request(path,{});status={...status,subscriptionLogin:false,message:login.message};
      if(login.state!=='pending')await check();
    }catch(error){login={state:'failed',message:error.message};}
    finally{busy=false;render();schedule();}
  }
  for(const view of views){
    view.signin.addEventListener('click',()=>act('ai/login/start'));
    view.signout.addEventListener('click',()=>act('ai/logout'));
    view.cancel.addEventListener('click',()=>act('ai/login/cancel'));
    view.check.addEventListener('click',check);
    view.code.addEventListener('click',()=>view.code.select());
  }
  document.addEventListener('visibilitychange',()=>{if(!document.hidden&&login.state==='pending'){clearTimeout(timer);poll();}});
  render();check();
}
