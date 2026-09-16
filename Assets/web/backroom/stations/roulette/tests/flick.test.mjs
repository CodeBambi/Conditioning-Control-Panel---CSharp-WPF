/* flick.test.mjs - THE THROW's arithmetic (flick.js): what counts as a flick, which way it went, and how hard.
 * Law I lives here too: the throw only ever produces a rotor speed, and planRun still lands on the pocket it is
 * handed whatever that speed was. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { FLICK, wrapDelta, flickStart, flickMove, flickSpeed, flickRelease } from '../flick.js';
import { planRun, FEEL } from '../feel.js';

/** A swing of `turn` radians in `steps` samples over `ms`, from angle 0. */
function swing(turn, ms, steps = 8, { hold = 0 } = {}) {
  let g = flickStart(0, 0);
  for (let i = 1; i <= steps; i++) g = flickMove(g, (turn * i) / steps, (ms * i) / steps);
  return flickRelease(g, ms + hold);
}

test('wrapDelta takes the short way round, over the seam too', () => {
  assert.ok(Math.abs(wrapDelta(0.2, 0.1) - 0.1) < 1e-12);
  assert.ok(Math.abs(wrapDelta(-Math.PI + 0.05, Math.PI - 0.05) - 0.1) < 1e-9);
  assert.ok(wrapDelta(Math.PI - 0.05, -Math.PI + 0.05) < 0);
});

test('a drag that hardly moves is not a throw', () => {
  const r = swing(FLICK.MIN_TRAVEL * 0.5, 60);
  assert.equal(r.ok, false);
  assert.equal(r.why, 'short');
  assert.equal(r.rotVel, null);
});

test('a long slow push around the wheel is not a throw either', () => {
  const r = swing(1.2, 6000);       // plenty of travel, nothing like a flick
  assert.equal(r.ok, false);
  assert.equal(r.why, 'slow');
  assert.equal(r.rotVel, null);
});

test('a finger that stops before it lifts leaves the wheel alone', () => {
  const quick = swing(1.2, 120);
  assert.equal(quick.ok, true);
  const stale = swing(1.2, 120, 8, { hold: FLICK.STALE_MS + 40 });
  assert.equal(stale.ok, false);
  assert.equal(stale.why, 'stale');
});

test('a real flick reports its direction: the sign follows the swing, both ways', () => {
  const up = swing(1.2, 120), down = swing(-1.2, 120);
  assert.equal(up.ok, true); assert.equal(down.ok, true);
  assert.equal(Math.sign(up.rotVel), 1);
  assert.equal(Math.sign(down.rotVel), -1);
  assert.ok(Math.abs(Math.abs(up.rotVel) - Math.abs(down.rotVel)) < 1e-9, 'the same swing either way is the same strength');
});

test('a wobble that ends up going one way keeps that way', () => {
  let g = flickStart(0, 0);
  for (const [a, t] of [[-0.05, 10], [-0.02, 20], [0.3, 32], [0.7, 44], [1.1, 56]]) g = flickMove(g, a, t);
  const r = flickRelease(g, 60);
  assert.equal(r.ok, true);
  assert.equal(r.sign, 1);
});

test('strength is clamped into the band, and never leaves it however wild the swipe', () => {
  assert.equal(flickSpeed(0), FLICK.VEL_MIN);
  assert.equal(flickSpeed(FLICK.SOFT), FLICK.VEL_MIN);
  assert.equal(flickSpeed(FLICK.HARD), FLICK.VEL_MAX);
  assert.equal(flickSpeed(FLICK.HARD * 50), FLICK.VEL_MAX);
  assert.equal(flickSpeed(-FLICK.HARD * 50), FLICK.VEL_MAX, 'the magnitude only; the sign is the grab\'s');
  for (const w of [0.002, 0.004, 0.008, 0.02]) {
    const v = flickSpeed(w);
    assert.ok(v >= FLICK.VEL_MIN && v <= FLICK.VEL_MAX, `${w} -> ${v}`);
  }
});

test('harder is faster, and the house kick sits inside the band', () => {
  const soft = swing(0.4, 260), hard = swing(2.4, 110);
  assert.equal(soft.ok, true); assert.equal(hard.ok, true);
  assert.ok(hard.rotVel > soft.rotVel, `${hard.rotVel} > ${soft.rotVel}`);
  assert.ok(FEEL.ROTOR_KICK > FLICK.VEL_MIN && FEEL.ROTOR_KICK < FLICK.VEL_MAX);
});

test('the wheel follows the finger: every sample reports its own turn', () => {
  let g = flickStart(0, 0), sum = 0;
  for (let i = 1; i <= 6; i++) { g = flickMove(g, i * 0.2, i * 12); sum += g.d; }
  assert.ok(Math.abs(sum - 1.2) < 1e-9, 'the turns add up to the swing');
  assert.ok(Math.abs(g.travel - 1.2) < 1e-9);
});

test('LAW I: whatever the throw, the ball still lands in the pocket the server named', () => {
  const speeds = [FLICK.VEL_MIN, -FLICK.VEL_MIN, FLICK.VEL_MAX, -FLICK.VEL_MAX, FEEL.ROTOR_KICK];
  for (const rotVel0 of speeds) {
    for (const index of [0, 7, 18, 36]) {
      const plan = planRun({ index, seed: 4242 + index, rotVel0 });
      assert.equal(plan.index, index, `${rotVel0} -> ${index}`);
      assert.ok(plan.restAt > 0 && plan.restAt < FEEL.SPIN_MS / 1000, `${rotVel0}: the run still fits a spin (${plan.restAt})`);
    }
  }
});

test('LAW I: the run is no longer or shorter for a paying pocket than a losing one', () => {
  const rotVel0 = FLICK.VEL_MAX, seed = 991;
  const first = planRun({ index: 0, seed, rotVel0 });
  for (let index = 1; index < 37; index++) {
    const plan = planRun({ index, seed, rotVel0 });
    assert.equal(plan.restAt, first.restAt, 'the pocket never shows in the timing');
    assert.equal(plan.landAt, first.landAt);
  }
});
