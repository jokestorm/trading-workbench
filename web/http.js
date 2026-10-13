// document.baseURI includes Home Assistant's ingress prefix via the injected base.
// Ordinary desktop hosting still resolves to the application root.
export function appUrl(path, base = globalThis.document?.baseURI ?? 'http://localhost/') {
  if (!path.startsWith('/') || path.startsWith('//')) throw new Error('Expected an application path.');
  return new URL('.' + path, base).href;
}
export function fetchApp(path, options) { return fetch(appUrl(path), options); }
