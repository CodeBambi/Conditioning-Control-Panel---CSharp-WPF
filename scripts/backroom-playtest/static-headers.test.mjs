import test from 'node:test';
import assert from 'node:assert/strict';
import { withStaticCaching } from './static-headers.mjs';

test('mutable static assets revalidate while robots and private headers survive', () => {
  const api = { source: '/api/(.*)', headers: [{ key: 'Cache-Control', value: 'private, no-store' }] };
  const server = { source: '/backroom-srv/(.*)', headers: [{ key: 'Cache-Control', value: 'no-store' }] };
  const original = { functions: { 'api/clip.js': { maxDuration: 60 } }, headers: [api, server,
    { source: '/backroom/(.*)', headers: [{ key: 'X-Robots-Tag', value: 'noindex' }, { key: 'cache-control', value: 'no-store' }] }] };
  const result = withStaticCaching(original);
  assert.equal(result.headers[0], api);
  assert.equal(result.headers[1], server);
  assert.deepEqual(result.functions, original.functions);
  assert.deepEqual(result.headers[2].headers, [{ key: 'X-Robots-Tag', value: 'noindex' }, { key: 'Cache-Control', value: 'public, max-age=0, must-revalidate' }]);
  assert.equal(original.headers[2].headers[1].value, 'no-store');
  assert.deepEqual(withStaticCaching(result), result);
  assert.equal(result.headers.some(rule => rule.source === '/(.*)'), false);
});
