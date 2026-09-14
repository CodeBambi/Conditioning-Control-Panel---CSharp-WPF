import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  TAU, LAST_TURN_SPEED, SLOW_FLOOR, timeScale, planSpeed, warpStep, warpedRotation, stepDim, edgeAlpha, captionAlpha,
  QUIET, distance01, quietMix, quietDone, outlineAlpha, mixRgb, taffyShear, stepShear, sliceU, trailOffset, ghostRotations,
  HUB_DRIFT, HUB_FOLLOW, stepHub, hubStill, moireRotations, moireSegments, dressOf,
} from '../hypno.js';
import { planLanding, rotationAt } from '../wheel.js';
import { strengthK } from '../../../shared/hypno/moments.js';

const near = (a, b, eps = 1e-9) => Math.abs(a - b) <= eps;

test('the long last turn: a third of real time at rest speed, full speed from 1.6 rad/s, Calm floors at 0.6', () => {
  assert.equal(LAST_TURN_SPEED, 1.6);
  assert.equal(timeScale(0), SLOW_FLOOR.normal);
  assert.equal(timeScale(0, { calm: true }), SLOW_FLOOR.calm);
  assert.ok(near(timeScale(0.8), 0.32 + 0.68 * 0.5));
  assert.equal(timeScale(1.6), 1);
  assert.equal(timeScale(12), 1);
  assert.ok(near(timeScale(-0.8), timeScale(0.8)), 'direction does not matter');
});

test('the warp only reads the plan clock slower: same path, same landing, a longer walk', () => {
  const plan = { ...planLanding({ from: 0.3, omega: -0.011, landing: 2.1 }), start: 0 };
  assert.ok(planSpeed(plan, 0) > 9 && near(planSpeed(plan, plan.ms), 0));
  let w = null, t = 0, slowFrames = 0, prevRot = plan.from, monotone = true;
  while (!w || w.elapsed < plan.ms) {
    w = warpStep(w, 16, plan); t += 16;
    if (w.slowing) slowFrames++;
    const r = warpedRotation(plan, w.elapsed);
    if (Math.sign(plan.to - plan.from) * (r - prevRot) < -1e-12) monotone = false;
    prevRot = r;
    assert.ok(w.scale >= SLOW_FLOOR.normal - 1e-9 && w.scale <= 1 + 1e-9);
    if (t > 60000) break;
  }
  assert.ok(monotone, 'never turns back');
  assert.equal(warpedRotation(plan, w.elapsed), plan.to, 'rests exactly where the plan does');
  assert.equal(warpedRotation(plan, plan.ms), rotationAt(plan, plan.ms));
  assert.ok(t > plan.ms * 1.4 && t < plan.ms * 3.5, `warped ${t} ms vs planned ${Math.round(plan.ms)} ms`);
  assert.ok(slowFrames > 60, 'the last turn runs for a while');
  const calm = (() => { let q = null, n = 0; while (!q || q.elapsed < plan.ms) { q = warpStep(q, 16, plan, { calm: true }); n += 16; } return n; })();
  assert.ok(calm < t, 'Calm raises the floor, so the walk is shorter');
  const off = (() => { let q = null, n = 0; while (!q || q.elapsed < plan.ms) { q = warpStep(q, 16, plan, { on: false }); n += 16; } return n; })();
  assert.ok(Math.abs(off - plan.ms) <= 16, 'off: real time');
});

test('stage dim and caption follow the slow turn', () => {
  let d = 0;
  for (let i = 0; i < 120; i++) d = stepDim(d, true, 16);
  assert.ok(d > 0.95);
  for (let i = 0; i < 180; i++) d = stepDim(d, false, 16);
  assert.ok(d < 0.01);
  assert.equal(edgeAlpha(1, 1), 0.65);
  assert.equal(edgeAlpha(1, 0.5), 0.325);
  assert.equal(captionAlpha(0.04), 0);
  assert.ok(near(captionAlpha(1), 0.7));
});

test('quiet room: others grey fast, capped at 0.78, colour flows back nearest first from 1.1 s over 0.8 s', () => {
  assert.equal(quietMix(-1, 0.5), 0);
  assert.equal(quietMix(0, 0.5), 0);
  assert.equal(quietMix(0.5, 1), QUIET.max, 'full strength caps at 0.78');
  assert.ok(near(quietMix(0.5, 1, 0.5), 0.78), 'Calm: 0.5 x 1.6 = 0.8 is over the cap, so Calm peaks at the same 0.78 and k only slows the ramp');
  assert.ok(near(quietMix(0.1, 1, 0.5), 0.3 * 0.5 * 1.6));
  assert.ok(quietMix(2, 0.1) < quietMix(2, 0.9), 'the nearest slice gets its colour back first');
  assert.equal(quietMix(3.6, 1), 0);
  assert.ok(quietDone(3.5) && !quietDone(3.4));
  assert.ok(near(distance01(0.2, 0.2 + Math.PI), 1) && near(distance01(-0.1, TAU - 0.1), 0));
  assert.equal(outlineAlpha(0, true), 0.6);
  for (let s = 0; s < 4; s += 0.1) { const a = outlineAlpha(s); assert.ok(a >= 0.45 - 1e-9 && a <= 0.8 + 1e-9); }
  const g = mixRgb('#ff0000', QUIET.grey, 1);
  assert.deepEqual(g.map(v => Math.round(v * 255)), [0x2a, 0x22, 0x38]);
});

test('taffy: shear by speed, capped, and it TRAILS (law 3)', () => {
  assert.ok(near(taffyShear(10), 1.1));
  assert.equal(taffyShear(30), 1.9);
  assert.equal(taffyShear(30, 0.5), 0.95);
  assert.ok(near(stepShear(0, 1, 400), 1));
  assert.equal(sliceU(0.185), 0);
  assert.equal(sliceU(0.711), 1);
  // turning with rotation.z growing (anticlockwise on screen): the rim point sits at a SMALLER rotor angle, behind.
  assert.ok(trailOffset(1, 1, +1) < 0 && trailOffset(1, 0, +1) === 0);
  assert.ok(trailOffset(1, 1, -1) > 0, 'turning the other way bends the other way');
  assert.ok(Math.abs(trailOffset(1, 0.5, 1)) < Math.abs(trailOffset(1, 1, 1)), 'more at the rim than near the hub');
  const g = ghostRotations(1, 12);
  assert.equal(g.length, 4);
  assert.ok(g.every((r, i) => r < 1 && (i === 0 || r < g[i - 1])), 'ghosts sit where the wheel has been');
  assert.ok(ghostRotations(1, -12).every(r => r > 1));
});

test('Loom hub turns clockwise whichever way the wheel turns, and drifts clockwise at rest', () => {
  assert.equal(HUB_DRIFT, 0.35);
  assert.equal(HUB_FOLLOW, 0.9);
  assert.ok(near(stepHub(0, 0, 1) - 0, 0.35), 'at rest it still turns clockwise');
  assert.ok(near(stepHub(1, 10, 0.1), 1 + (9 + 0.35) * 0.1), 'a clockwise wheel adds 0.9 x its speed');
  assert.equal(stepHub(1, -10, 0.1), stepHub(1, 10, 0.1), 'an anticlockwise fling turns the hub the same clockwise way');
  let rot = 0;
  for (const v of [-30, -12, -1.2, 0, 4, 30]) { const next = stepHub(rot, v, 1 / 60); assert.ok(next > rot, `never backward at ${v} rad/s`); rot = next; }
  assert.ok(near(stepHub(0, 0, 1, 0.32), 0.35 * 0.32), 'the long last turn slows the drift with its clock');
  assert.equal(stepHub(2, 5, -1), 2, 'no negative frame');
  assert.ok(near(stepHub(0, 0, 1, 1, 0.5), 0.35 * 0.5), 'Calm (k 0.5): the hub keeps drifting clockwise at half strength');
  assert.ok(near(stepHub(0, 10, 0.1, 1, 0.5), (9 + 0.35) * 0.1 * 0.5), 'Calm halves the follow too');
  assert.ok(stepHub(0, 0, 1, 1, 0.5) > 0, 'Calm never stills it');
  const [a, b] = moireRotations(2);
  assert.equal(a, 2);
  assert.ok(near(b, 2.1));
  const seg = moireSegments(0.72, 0.76);
  assert.equal(seg.length, 240);
  assert.ok(near(Math.hypot(seg[0], seg[1]), 0.72) && near(Math.hypot(seg[2], seg[3]), 0.76));
});

test('only OS reduced motion (or the app Motion Off) holds the hub still; Calm and MotionLevel Reduced keep it turning', () => {
  assert.equal(hubStill({ osReduced: true }), true, 'prefers-reduced-motion stills it');
  assert.equal(hubStill({ motion: 'off' }), true, 'Motion Off stills it');
  assert.equal(hubStill({ motion: 'reduced' }), false, 'MotionLevel Reduced (Calm) keeps it turning');
  assert.equal(hubStill({ motion: 'full' }), false);
  assert.equal(hubStill(), false);
});

test('dress: moire and taffy are Full only, the hub is a brass star with spiral off, k matches the kit', () => {
  assert.deepEqual(dressOf({ intensity: 'full' }), { full: true, calm: false, k: 1, hub: 'loom', moire: true, taffy: true });
  assert.equal(dressOf({ intensity: 'normal' }).moire, false);
  assert.equal(dressOf({ intensity: 'full', reduced: true }).taffy, false);
  assert.equal(dressOf({ gates: { spiral: false } }).hub, 'star');
  for (const s of [{ intensity: 'calm' }, { intensity: 'normal' }, { intensity: 'full' }, { intensity: 'full', reduced: true }]) {
    assert.equal(dressOf(s).k, strengthK(s));
  }
});
