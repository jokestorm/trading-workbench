// document.baseURI includes Home Assistant's ingress prefix via the injected base.
// Ordinary desktop hosting still resolves to the application root.
export function appUrl(path, base = globalThis.document?.baseURI ?? 'http://localhost/') {
  if (!path.startsWith('/') || path.startsWith('//')) throw new Error('Expected an application path.');
  return new URL('.' + path, base).href;
}
export function fetchApp(path, options) { return fetch(appUrl(path), options); }

export async function readAppJson(response,label='Request') {
  const status=`HTTP ${response.status}`;
  const reason=response.status===401||response.status===403?'Reopen Workbench from Home Assistant to renew access.':response.status===404?'The requested Workbench endpoint was not found. Reload the page after updating.':[502,503,504].includes(response.status)?'The gateway could not reach Workbench. Check that the app is running.':response.status>=500?'Check the Workbench app logs for the server error.':'The server did not return the expected response.';
  let data;
  try { data=await response.json(); }
  catch(error) {
    if(error.name==='AbortError')throw error;
    throw new Error(`${label} failed (${status}). ${response.ok?'Workbench returned invalid JSON. Reload the page and try again.':reason}`);
  }
  if(!response.ok)throw new Error(`${data?.message??data?.title??`${label} failed`} (${status}). ${data?.message||data?.title?'':reason}`.trim());
  return data;
}
