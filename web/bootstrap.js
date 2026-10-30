try {
  await import('./app.js?v=chat-only-1');
  function openSection(){
    const section=location.hash.slice(1);
    if(['market','trader','settings','journal'].includes(section)&&document.getElementById(section)?.hidden)document.querySelector(`[data-tab="${section}"]`).click();
  }
  openSection();
  window.addEventListener('hashchange',openSection);
} catch(error) {
  const message=document.getElementById('load-error');
  message.textContent=`The workbench could not load: ${error.message}. Reload this page to retry.`;
  document.getElementById('journal').hidden=false;
  document.getElementById('market').hidden=true;
  message.style.cssText='padding:16px;margin-bottom:20px;border:1px solid #ee939c;color:#ee939c';
}
