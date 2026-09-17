import test from 'node:test';
import assert from 'node:assert/strict';
import { siliconeRebound, SILICONE_SETTLE_MS } from './lever-return.js';
test('custom lever makes one small overshoot and a faster tiny correction', () => {
  for (const age of [-420, -1, 0, SILICONE_SETTLE_MS, 500]) assert.equal(siliconeRebound(age), 0);
  assert.equal(siliconeRebound(55), -0.06);
  assert.equal(siliconeRebound(145), 0.006);
  assert.ok(Math.abs(siliconeRebound(110)) < 1e-12);
  for(let ms=1;ms<180;ms++) assert.ok(Math.abs(siliconeRebound(ms))<=0.06);
});
