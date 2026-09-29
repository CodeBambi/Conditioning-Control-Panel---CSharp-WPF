// node smoke/replay-shots-smoke.mjs - the replay cameras: fitted framing, the in-front test,
// the shot bank's motion and the per-replay shot choice. Pure maths from board/replay-frame.js.
import assert from 'node:assert/strict';
import { SHOT_BANK, FRAME, lensDir, basis, projectSpheres, fitFrame, clampLens, hides, motion, fovFor, pickShots, seeded }
  from '../board/replay-frame.js';

const near = (a, b, e = 1e-6) => Math.abs(a - b) < e;
const dN = [0, 0, -1], perp = [1, 0, 0];
const man = (x, z, h = .7) => [[x, .18, z, .3], [x, h / 2, z, .31], [x, h, z, .24]];
const pair = [...man(0, 0), ...man(0, -1, .9)];
const panels = { big: { w: .6, h: 1 }, side: { w: .44, h: .5 }, corner: { w: .36, h: .4 }, tall: { w: 1, h: .6 } };

// The lens looks along its own axis; the axes are orthonormal.
{
  const b = basis([3, 2, 1], [0, .5, 0]);
  assert.ok(near(Math.hypot(...b.fwd), 1) && near(Math.hypot(...b.right), 1) && near(Math.hypot(...b.up), 1), 'unit axes');
  assert.ok(Math.abs(b.fwd[0] * b.right[0] + b.fwd[1] * b.right[1] + b.fwd[2] * b.right[2]) < 1e-9, 'right is across the look');
  assert.ok(b.up[1] > 0, 'up is up');
  const d = lensDir(.3, 0, dN, perp);
  assert.ok(d[0] > .9 && d[1] > 0, 'az 0 stands on the player side');
  const chase = lensDir(.3, Math.PI / 2, dN, perp);
  assert.ok(chase[2] > .9, '+az swings round behind the attacker');
}

// Fitted framing: centred in the panel, filling the planned share, for every shot, panel and aspect.
for (const [pname, panel] of Object.entries(panels)) for (const aspect of [1.6, .625]) for (const [name, spec] of Object.entries(SHOT_BANK)) {
  const dir = lensDir((spec.el[0] + spec.el[1]) / 2, (spec.az[0] + spec.az[1]) / 2, dN, perp);
  const fit = fitFrame({ spheres: pair, dir, fov: spec.fov, aspect, panel, share: spec.share });
  const cam = { pos: fit.pos, ...basis(fit.pos, fit.target), fov: spec.fov, aspect };
  const { rect, near: n } = projectSpheres(pair, cam, panel);
  const tag = `${name} ${pname} ${aspect}`;
  assert.ok(rect, `${tag}: the subject is in front of the lens`);
  if (fit.dist > FRAME.minDist + 1e-6 && fit.dist < FRAME.maxDist - 1e-6) {
    assert.ok(Math.abs((rect[0] + rect[2]) / 2) < .03 && Math.abs((rect[1] + rect[3]) / 2) < .03, `${tag}: centred (${rect.map(v => v.toFixed(2))})`);
    const ext = Math.max(rect[2] - rect[0], rect[3] - rect[1]) / 2;
    assert.ok(Math.abs(ext - spec.share) < .03, `${tag}: fills its share (${ext.toFixed(3)} vs ${spec.share})`);
  }
  assert.ok(rect[0] > -1 && rect[2] < 1 && rect[1] > -1 && rect[3] < 1, `${tag}: all of it inside the panel`);
  assert.ok(n > .3, `${tag}: the lens is not in a man`);
  // With a centre, the centre is on the axis and nothing leaves the panel.
  const c = [0, .5, -.5];
  const f2 = fitFrame({ spheres: pair, dir, fov: spec.fov, aspect, panel, share: spec.share, centre: c });
  const cam2 = { pos: f2.pos, ...basis(f2.pos, f2.target), fov: spec.fov, aspect };
  const cp = projectSpheres([[...c, 0]], cam2, panel).rect;
  assert.ok(Math.abs(cp[0]) < 1e-6 && Math.abs(cp[1]) < 1e-6, `${tag}: the action centre sits on the axis`);
  const r2 = projectSpheres(pair, cam2, panel).rect;
  if (f2.dist < FRAME.maxDist - 1e-6) assert.ok(Math.max(-r2[0], r2[2], -r2[1], r2[3]) <= spec.share + .03, `${tag}: centred fit keeps the subject in`);
}

// A tighter shot stands nearer (or narrower) than a wider one on the same subject.
{
  const dist = name => { const s = SHOT_BANK[name]; return fitFrame({ spheres: pair, dir: lensDir(.3, 0, dN, perp), fov: 40, aspect: 1.6, panel: panels.big, share: s.share }).dist; };
  assert.ok(dist('close') < dist('wide'), 'close is nearer than wide');
}

// Lens clamps: never under the board, never out of the room.
assert.ok(clampLens([0, -2, 0])[1] >= FRAME.floorY, 'lens stays above the board');
assert.ok(Math.hypot(...clampLens([90, 3, 90])) <= FRAME.maxDist + 4 + 1e-9, 'lens stays in the room');

// The in-front test: a man between lens and action goes; one behind it stays; one at the lens goes.
{
  const pos = [0, 1, 6], cam = { pos, ...basis(pos, [0, .4, 0]), fov: 36, aspect: 1.6 };
  const panel = panels.big;
  const subject = projectSpheres(man(0, 0), cam, panel);
  const col = (x, z) => ({ base: [x, 0, z], top: [x, .7, z], r: .3 });
  assert.equal(hides(col(0, 3), cam, panel, subject), true, 'a man in front of the action is hidden');
  assert.equal(hides(col(0, -2.5), cam, panel, subject), false, 'a man behind the action stays');
  assert.equal(hides(col(4, 3), cam, panel, subject), false, 'a man off to the side stays');
  assert.equal(hides(col(0, 6.2), cam, panel, subject), true, 'a man at the lens is hidden');
  assert.equal(hides(col(0, 9), cam, panel, subject), false, 'a man behind the lens stays');
}

// Motion: continuous, small, and each kind does its thing.
for (const kind of ['push', 'orbit', 'dolly', 'drift', 'spin']) {
  let prev = motion(kind, -2, .3);
  for (let u = -2; u <= 2; u += .02) {
    const m = motion(kind, u, .3);
    assert.ok(Math.abs(m.dist - prev.dist) < .03 && Math.abs(m.az - prev.az) < .03 && Math.abs(m.fov - prev.fov) < .03, `${kind}: smooth at ${u.toFixed(2)}`);
    assert.ok(m.dist > .6 && m.dist < 1.7 && Math.abs(m.az) < .5 && Math.abs(m.el) < .2, `${kind}: stays near the fit`);
    prev = m;
  }
}
assert.ok(motion('push', .8).dist < motion('push', -1).dist, 'push closes in on the hit');
{
  // dolly zoom: distance up, fov down, subject size (dist * tan) held
  const a = motion('dolly', -.5), b = motion('dolly', .6);
  assert.ok(b.dist > a.dist && b.fov < a.fov, 'dolly backs off and narrows in');
  const size = m => m.dist * Math.tan(fovFor(30, m.fov) * Math.PI / 360);
  assert.ok(Math.abs(size(b) / size(a) - 1) < .1, 'dolly holds the subject size');
}
assert.ok(motion('orbit', 1, .2).az * motion('orbit', 1, .8).az < 0, 'the seed turns the orbit either way');

// Shot choice: one per panel, no two sizes alike, a trio gets context, a fling gets its flight.
const infos = [null, { piece: 'p', impact: 'squash' }, { piece: 'n', impact: 'squash' }, { piece: 'b', impact: 'slap' },
  { piece: 'r', impact: 'fling' }, { piece: 'q', impact: 'slap' }, { piece: 'k', impact: 'squash' }];
const seen = new Set();
for (let s = 1; s < 400; s++) {
  const random = seeded(s);
  for (const info of infos) for (const layout of ['trio', 'duo', 'corner']) {
    const shots = pickShots(layout, info, random);
    const n = { trio: 3, duo: 2, corner: 1 }[layout];
    const tag = `${layout} ${info?.piece} seed ${s}: ${shots.map(x => x.name)}`;
    assert.equal(shots.length, n, `${tag}: one per panel`);
    assert.equal(new Set(shots.map(x => x.size)).size, n, `${tag}: no two panels the same size`);
    for (const x of shots) {
      assert.ok(SHOT_BANK[x.name], `${tag}: a known shot`);
      assert.ok(x.seed >= 0 && x.seed < 1 && Number.isFinite(x.az) && Number.isFinite(x.el), `${tag}: numbers`);
      seen.add(x.name);
    }
    if (layout === 'corner') assert.ok(!['xclose', 'wide', 'top'].includes(shots[0].name), `${tag}: the inset reads at a glance`);
    if (layout === 'trio') assert.ok(shots.some(x => x.size >= 3), `${tag}: a trio has a context shot`);
    if (info?.impact === 'fling' && n > 1) assert.ok(shots.some(x => SHOT_BANK[x.name].flight), `${tag}: a fling shows the flight`);
  }
}
assert.equal(seen.size, Object.keys(SHOT_BANK).length, 'every shot in the bank gets used');
{
  // variety: over many replays panel 0 is not always the same shot
  const random = seeded(7), firsts = new Set();
  for (let i = 0; i < 60; i++) firsts.add(pickShots('trio', { piece: 'p' }, random)[0].name);
  assert.ok(firsts.size >= 2, 'panel 0 varies');
}

console.log('replay-shots smoke: ok');
