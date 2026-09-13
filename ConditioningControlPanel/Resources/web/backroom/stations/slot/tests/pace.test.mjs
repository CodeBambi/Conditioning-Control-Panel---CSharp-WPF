import test from 'node:test';
import assert from 'node:assert/strict';
import { PACE, reelStopMs, reelsMs, outcomeMs } from '../pace.js';

test('one outcome takes about 4 s: reels stop left to right, each thud 340 ms, then reveal and breath', () => {
  assert.equal(PACE.THUD_MS, 340, 'THE THUD is House Book, not pace');
  assert.deepEqual([0, 1, 2].map(i => reelStopMs(i)), [1800, 2180, 2560]);
  assert.equal(reelsMs(), 2900);
  assert.ok(outcomeMs() >= 3800 && outcomeMs() <= 4400 && PACE.SPIN_MS > PACE.DECEL_MS, `outcomeMs ${outcomeMs()}`);
});
