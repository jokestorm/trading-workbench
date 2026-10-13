import test from 'node:test';
import assert from 'node:assert/strict';
import {appUrl} from '../web/http.js';

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
