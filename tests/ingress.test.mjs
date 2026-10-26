import test from 'node:test';
import assert from 'node:assert/strict';
import {appUrl,readAppJson} from '../web/http.js';

test('assets and API URLs retain the ingress prefix including query strings', () => {
  const base = 'https://ha.example/api/hassio_ingress/token/';
  for (const path of ['/api/trades', '/api/trades/export?symbol=MES', '/api/backup']) {
    assert.equal(appUrl(path, base), base + path.slice(1));
  }
});
test('desktop hosting keeps API URLs at the app root', () => {
  assert.equal(appUrl('/api/trades', 'http://127.0.0.1:5175/'), 'http://127.0.0.1:5175/api/trades');
});
test('scheme-relative URLs cannot escape the ingress origin', () => {
  assert.throws(() => appUrl('//other.example/api'));
});

test('plain-text and HTML ingress failures retain HTTP status without leaking response bodies',async()=>{
  for(const status of [401,403,404,500,502,503,504]){
    await assert.rejects(readAppJson(new Response(`${status}: PRIVATE gateway details`,{status}),'Analysis request'),error=>error.message.includes(`HTTP ${status}`)&&!error.message.includes('PRIVATE')&&!error.message.includes('Unexpected non-whitespace'));
  }
  await assert.rejects(readAppJson(new Response('<html>PRIVATE details</html>',{status:502})),/HTTP 502/);
});

test('application JSON errors stay actionable and valid successful JSON remains unchanged',async()=>{
  await assert.rejects(readAppJson(Response.json({message:'Authorize Schwab first.'},{status:400})),/Authorize Schwab first.*HTTP 400/);
  assert.deepEqual(await readAppJson(Response.json({run:null})),{run:null});
  await assert.rejects(readAppJson(new Response('200: Not JSON',{status:200})),/invalid JSON/);
});
