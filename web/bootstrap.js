try {
  await import('./app.js?v=ha-0.1.5');
  function openSection(){
    if(location.hash==='#market')document.querySelector('[data-tab="market"]').click();
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
