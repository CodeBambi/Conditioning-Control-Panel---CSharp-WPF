// Driving state survives reactions; animation time is independent of frame rate.
import assert from 'node:assert/strict';
import { AnimationSpring, createPoseLayer, drivingPose, resolvePose } from '../emiPoses.js';

const vector = (x = 0) => ({ x, y: x, z: x, set(x, y, z) { Object.assign(this, { x, y, z }); } });
function model() {
  const nodes = new Map(['shoulderL', 'shoulderR', 'footL', 'footR', 'ant0'].map(name =>
    [name, { rotation: vector(), scale: vector(1) }]));
  return { rotation: vector(), position: vector(), scale: vector(1), nodes, getObjectByName: n => nodes.get(n) };
}
const upright = { up: { y: 1 }, right: { y: 0 }, tangent: { y: 0 } };
const inverted = { inverted: true, up: { y: -1 }, right: { y: -0.4 }, tangent: { y: 0.3 } };
function tick(layer, m, dt, t, ctx = upright) {
  m.nodes.get('ant0').rotation.set(0, 0, 0); // The mood is written once before each frame.
  layer.update(dt, { ...ctx, t });
}
function run(layer, m, sec, fps = 60, ctx = upright) {
  for (let i = 1; i <= Math.round(sec * fps); i++) tick(layer, m, 1 / fps, i / fps, ctx);
}

// Long tier-3 drifts stay posed; side changes are reflected without another tier event.
{
  const m = model(), p = createPoseLayer(m);
  p.setBase('drift', { side: 1, tier: 3 });
  run(p, m, 5);
  assert.equal(p.name, 'drift');
  assert.ok(m.rotation.z < -0.2);
  p.setBase('drift', { side: -1, tier: 3 });
  run(p, m, 2);
  assert.ok(m.rotation.z > 0.2);
  p.setBase('cruise'); run(p, m, 2);
  assert.equal(p.name, 'cruise');
  assert.ok(Math.abs(m.rotation.z) < 0.001);
}

// An item temporarily interrupts inversion, then restores it. State changes during the
// reaction also win over the old base, rather than restoring the state at reaction start.
{
  const m = model(), p = createPoseLayer(m);
  p.setBase('tuck'); p.set('grab'); run(p, m, 0.7, 60, inverted);
  assert.equal(p.name, 'tuck');
  p.set('cheer'); p.setBase('air'); run(p, m, 1.5);
  assert.equal(p.name, 'air');
  p.set('landing');
  assert.equal(p.set('grab'), false, 'a same-frame pickup cannot erase touchdown');
  p.setBase('cruise'); run(p, m, 0.4);
  assert.equal(p.name, 'cruise');
}

// The short squash and rebound phases consume the same time at 10, 30, 60 and 120 fps.
const samples = [];
for (const fps of [10, 30, 60, 120]) {
  const m = model(), p = createPoseLayer(m);
  p.setBase('drift', { side: 1, tier: 2 }); p.set('boost');
  run(p, m, 0.2, fps); assert.equal(p.name, 'boostOut');
  samples.push(m.scale.y);
  run(p, m, 0.5, fps); assert.equal(p.name, 'drift');
}
assert.ok(Math.max(...samples) - Math.min(...samples) < 0.002);

// A stalled frame settles once instead of replaying a cheer or integrating unstable springs.
{
  const m = model(), p = createPoseLayer(m);
  p.setBase('tuck'); p.set('cheer'); tick(p, m, 5, 5, inverted);
  assert.equal(p.name, 'tuck');
  assert.equal(m.rotation.x, resolvePose('tuck').root.tilt);
  assert.ok(Number.isFinite(m.nodes.get('ant0').rotation.x));
  p.set('grab'); p.settle(inverted); assert.equal(p.name, 'tuck');
}

// Reduced motion keeps the actual driving gesture but has no lift, squash or idle bob.
{
  const m = model(), p = createPoseLayer(m, { reducedMotion: true });
  p.setBase('tuck'); tick(p, m, 1 / 60, 0.975, inverted);
  assert.equal(m.rotation.x, resolvePose('tuck').root.tilt);
  assert.equal(m.nodes.get('shoulderL').rotation.x, resolvePose('tuck').shoulderL[0]);
  assert.equal(m.position.y, 0); assert.equal(m.scale.y, 1);
  p.set('landing'); tick(p, m, 1 / 60, 1);
  assert.equal(m.position.y, 0); assert.equal(m.scale.y, 1);
  p.setBase('cruise'); run(p, m, 1);
  tick(p, m, 1 / 60, 0.975); assert.equal(m.position.y, 0);
  const regular = createPoseLayer(m); regular.set('cruise', { amp: 0 });
  tick(regular, m, 1 / 60, 0.975); assert.equal(m.position.y, 0);
}

// The shared spring retains overshoot, is frame-consistent and snaps quietly after a gap.
{
  const a = new AnimationSpring(), b = new AnimationSpring(); let peak = 0;
  for (let i = 0; i < 120; i++) { peak = Math.max(peak, a.step(1, 1 / 120, 16, 0.35)); }
  for (let i = 0; i < 10; i++) b.step(1, 0.1, 16, 0.35);
  assert.ok(peak > 1.1); assert.ok(Math.abs(a.x - b.x) < 0.00001);
  b.step(0, 5, 22, 0.45); assert.equal(b.x, 0); assert.equal(b.v, 0);
}
assert.equal(drivingPose({ ...inverted, airborne: true, drift: true }).name, 'tuck');
assert.equal(drivingPose({ airborne: true, drift: true }).name, 'air');
assert.deepEqual(drivingPose({ drift: true, driftSide: -1, driftTier: 3 }),
  { name: 'drift', opts: { side: -1, tier: 3 } });
console.log('animation-foundations-check: all good');
