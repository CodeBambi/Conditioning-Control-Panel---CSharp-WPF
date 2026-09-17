import test from 'node:test';
import assert from 'node:assert/strict';
import { siliconeRebound, SILICONE_SETTLE_MS } from './lever-return.js';
test('custom lever makes one small overshoot and a faster tiny correction', () => {
  for (const age of [-420, -1, 0, SILICONE_SETTLE_MS, 500]) assert.equal(siliconeRebound(age), 0);
  assert.equal(siliconeRebound(27.5), -0.09);
  assert.equal(siliconeRebound(72.5), 0.006);
  assert.ok(Math.abs(siliconeRebound(55)) < 1e-12);
  for(let ms=1;ms<90;ms++) assert.ok(Math.abs(siliconeRebound(ms))<=0.09);
});
