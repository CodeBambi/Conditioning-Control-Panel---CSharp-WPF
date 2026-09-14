import test from 'node:test';
import assert from 'node:assert/strict';
import { PACE, ANTICIPATION, reelStopMs, reelsMs, outcomeMs } from '../pace.js';
import { anticipation } from '../feel.js';

test('one outcome takes about 4 s: reels stop left to right, each thud 340 ms, then reveal and breath', () => {
  assert.equal(PACE.THUD_MS, 340, 'THE THUD is House Book, not pace');
  assert.deepEqual([0, 1, 2].map(i => reelStopMs(i)), [1800, 2180, 2560]);
  assert.equal(reelsMs(), 2900);
  assert.ok(outcomeMs() >= 3800 && outcomeMs() <= 4400 && PACE.SPIN_MS > PACE.DECEL_MS, `outcomeMs ${outcomeMs()}`);
});

/* ---- A1 THE ANTICIPATION REEL (CONTRACT 10.15): reel 3's hold on a live pair. ---- */

// The mock server's table v5 strips (mock-server.js STRIPS), 13 cells a reel.
const STRIPS = [
  ['gif0', 'spiral0', 'sub0', 'gif1', 'emi', 'spiral1', 'gif2', 'sub1', 'spiral2', 'gif3', 'sub2', 'sub3', 'melt'],
  ['sub1', 'gif2', 'spiral1', 'melt', 'gif0', 'sub3', 'emi', 'spiral2', 'gif3', 'sub0', 'spiral0', 'gif1', 'sub2'],
  ['spiral2', 'gif3', 'sub2', 'gif1', 'spiral0', 'emi', 'sub0', 'gif0', 'melt', 'sub3', 'gif2', 'spiral1', 'sub1'],
];
const at = (r, k) => STRIPS[r][k];
/** An outcome the way the tape carries it: stops per reel, the symbols they land on. */
const out = (stops, line = 'none', extra = {}) =>
  ({ stops, symbols: stops.map((k, r) => at(r, k)), line, pay: line === 'none' ? 0 : 3, meltLeft: 0, ...extra });
/** reel 3's stop index for a symbol id. */
const k2 = id => STRIPS[2].indexOf(id);
const k0 = id => STRIPS[0].indexOf(id), k1 = id => STRIPS[1].indexOf(id);

test('A1 tiers: a frozen table next to PACE', () => {
  assert.deepEqual(Object.keys(ANTICIPATION), ['none', 'gif', 'spiral', 'sub', 'emi']);
  assert.ok(Object.isFrozen(ANTICIPATION));
  assert.deepEqual([ANTICIPATION.gif, ANTICIPATION.spiral, ANTICIPATION.sub, ANTICIPATION.emi], [900, 1100, 1100, 1400]);
});

test('A1 pace: PACE is untouched, reel 3 alone takes the hold, the outcome grows by exactly it', () => {
  assert.deepEqual([0, 1, 2].map(i => reelStopMs(i)), [1800, 2180, 2560], 'no hold, the pace of 10.11');
  assert.deepEqual([0, 1, 2].map(i => reelStopMs(i, PACE, 900)), [1800, 2180, 3460]);
  assert.equal(reelsMs(PACE, 900), 3800);
  assert.equal(outcomeMs(PACE, 900) - outcomeMs(), 900);
  assert.equal(outcomeMs(PACE, 1400) - outcomeMs(), 1400);
  assert.equal(reelStopMs(2, PACE, -50), reelStopMs(2), 'a negative hold is no hold');
  assert.equal(reelStopMs(2, PACE, 1400) - reelStopMs(1, PACE, 1400), PACE.STAGGER_MS + 1400);
});

test('A1 costs about 0.2 s a spin on the v5 strips: a live pair is about 1 in 6', () => {
  let pairs = 0, held = 0;
  for (let a = 0; a < 13; a++) for (let b = 0; b < 13; b++) {
    const o = { stops: [a, b, 0], symbols: [at(0, a), at(1, b), at(2, 0)], line: 'none', pay: 0 };
    const ant = anticipation(o, STRIPS);
    if (ant.holdMs) { pairs++; held += ant.holdMs; }
  }
  const rate = pairs / 169, mean = held / 169;
  assert.ok(rate > 0.16 && rate < 0.19, `live pair rate ${rate.toFixed(4)}`);
  assert.ok(mean > 150 && mean < 250, `mean hold ${mean.toFixed(1)} ms`);
});
