// Physical juice remains cosmetic, bounded, brief and quiet under reduced motion.
import assert from 'node:assert/strict';
import { createRideFeel } from '../emiPoses.js';
const sample = (feel, seconds, ctx = {}) => {
  let state;
  for (let i = 0; i < Math.round(seconds * 120); i++) state = feel.update(1 / 120, ctx);
  return state;
};
const positive = createRideFeel(), negative = createRideFeel();
sample(positive, 1, { drift: true, driftCharge: 1, driftSide: 1 });
sample(negative, 1, { drift: true, driftCharge: 1, driftSide: -1 });
assert.ok(positive.state.teaZ > 0.04, 'charged drift banks the tea visibly');
assert.ok(positive.state.antenna < -0.15, 'antenna takes tension before release');
assert.ok(Math.abs(positive.state.roll + negative.state.roll) < 1e-9);
positive.react('release', { tier: 3, side: 1 });
negative.react('release', { tier: 3, side: -1 });
sample(positive, 0.05); sample(negative, 0.05);
assert.ok(positive.state.antenna < -0.2, 'release streams the antenna farther back in its first 50 ms');
sample(positive, 0.05); sample(negative, 0.05);
assert.ok(positive.state.spin > 2, 'saucer delivers a short spin accent');
assert.ok(Math.abs(positive.state.roll + negative.state.roll) < 1e-9);
assert.ok(positive.state.ripples[0].opacity > 0);
assert.equal(positive.state.ripples[1].opacity, 0, 'second ripple follows the first');
sample(positive, 0.12); assert.ok(positive.state.ripples[1].opacity > 0);
const high = createRideFeel(), low = createRideFeel();
high.react('landing', { impact: 1, clean: true }); low.react('landing', { impact: 0.25, clean: true });
sample(high, 0.1); sample(low, 0.1);
assert.ok(high.state.teaX > low.state.teaX, 'hard contact displaces more tea');
assert.equal(high.state.teaZ, 0, 'clean landing has no sideways jolt');
const kerb = createRideFeel(); kerb.react('landing', { impact: 1, clean: false, side: -1 });
sample(kerb, 0.1); assert.ok(kerb.state.teaZ < 0, 'kerbed contact follows its actual side');
for (let i = 0; i < 100; i++) high.react('landing', { impact: 1, clean: false });
for (let i = 0; i < 180; i++) {
  const s = high.update(1 / 120);
  assert.ok(Math.abs(s.teaX) <= 0.08 && Math.abs(s.teaZ) <= 0.09);
  assert.ok(Math.abs(s.antenna) <= 0.4 && Math.abs(s.roll) <= 0.07);
  assert.ok(s.ripples.every(r => r.radius <= 0.441));
}
assert.ok(Math.abs(high.state.antenna) < 0.005, 'secondary motion settles within 1.5 seconds');
assert.ok(high.state.ripples.every(r => r.opacity === 0));
for (const quiet of [createRideFeel({ reducedMotion: true }), createRideFeel()]) {
  quiet.react('release', { tier: 3 }); quiet.react('landing', { impact: 1 });
  const s = quiet.update(5, { drift: true, driftCharge: 1 });
  assert.equal(s.roll + s.teaX + s.teaZ + s.antenna + s.spin, 0);
  assert.ok(s.ripples.every(r => r.opacity === 0));
  sample(quiet, 0.2); assert.equal(quiet.state.antenna, 0, 'no delayed replay after settling');
}
const reduced = createRideFeel({ reducedMotion: true });
reduced.react('release', { tier: 3 }); sample(reduced, 1, { drift: true, driftCharge: 1 });
assert.equal(reduced.state.antenna, 0); assert.equal(reduced.state.spin, 0);
console.log('ride-feel-check: all good');
