/* ============================================================================
 * smoke/room-smoke.mjs - node checks for the room's numbers.
 * No browser and no dependencies: run it with
 *
 *   node smoke/room-smoke.mjs
 *
 * Pins board/roomMath.js: the dome is plum at the horizon, pinker just above
 * it and black overhead; the rig stands behind the mover; the turn tell hits
 * full on frame one and rests at glowRest inside glowMs while the other edge
 * waits; a tween never overshoots and a re-aimed tween starts from where it
 * is. Exits non-zero on the first failure.
 * ==========================================================================*/

import { ROOM as T, domeColorAt, yawFor, glowTargets, createTweens, easeOutCubic } from '../board/roomMath.js';

let passed = 0;
function ok(cond, what) {
  if (!cond) { console.log('FAIL ' + what); process.exit(1); }
  passed++;
}
const lum = (c) => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
const pink = (c) => c[0] - c[1];
const near = (a, b, eps = 1e-6) => Math.abs(a - b) < eps;

// --- the dome ---------------------------------------------------------------
const atHorizon = domeColorAt(0);
const atHaze = domeColorAt(T.hazeAt);
const atZenith = domeColorAt(1);
const below = domeColorAt(-1);
ok(near(atHorizon[0], ((T.horizon >> 16) & 255) / 255), 'the horizon is the horizon colour');
ok(pink(atHaze) > pink(atHorizon), 'the haze is pinker than the horizon');
ok(lum(atZenith) < lum(atHaze) && lum(atZenith) < lum(atHorizon), 'overhead is the darkest');
ok(lum(below) < lum(atHorizon), 'below the horizon is darker than the horizon');
let prev = lum(atHaze);
for (let h = T.hazeAt; h <= 1; h += 0.02) {
  const l = lum(domeColorAt(h));
  ok(l <= prev + 1e-9, 'the dome only darkens above the haze at ' + h.toFixed(2));
  prev = l;
}
ok(T.fogNear < T.fogFar && T.fogFar < T.floorSize / 2, 'the fog ends before the floor does');
ok(T.domeRadius < 120, 'the dome is inside the camera far plane');

// --- the lean ---------------------------------------------------------------
ok(yawFor('w') === 0, 'white keeps the rig where scene.js put it');
ok(near(yawFor('b'), Math.PI), 'black gets the rig swung round');
ok(yawFor(undefined) === 0, 'anything else reads as white');

// --- the tell: targets ------------------------------------------------------
const w = glowTargets('w');
const b = glowTargets('b');
const over = glowTargets(null);
ok(w.w === T.glowRest && w.b === T.glowOff, 'white to move: white edge rests lit, black waits');
ok(b.b === T.glowRest && b.w === T.glowOff, 'black to move: black edge rests lit, white waits');
ok(over.w === T.glowOver && over.b === T.glowOver, 'game over: both edges dim alike');
ok(T.glowHit > T.glowRest && T.glowRest > T.glowOver && T.glowOver > T.glowOff, 'hit > rest > over > off');

// --- the tell: timeline -----------------------------------------------------
{
  const tw = createTweens();
  const mat = { opacity: T.glowOff };
  mat.opacity = T.glowHit;                       // frame one
  tw.to(mat, 'opacity', T.glowRest, T.glowMs, easeOutCubic);
  ok(mat.opacity === T.glowHit, 'full on the first frame');
  let lo = Infinity, hi = -Infinity, t = 0;
  while (tw.count()) {
    tw.update(1 / 60); t += 1000 / 60;
    lo = Math.min(lo, mat.opacity); hi = Math.max(hi, mat.opacity);
  }
  ok(mat.opacity === T.glowRest, 'and resting at glowRest at the end');
  ok(lo >= T.glowRest - 1e-9 && hi <= T.glowHit + 1e-9, 'never outside hit..rest on the way');
  ok(t <= T.glowMs + 1000 / 60 + 1e-6, 'done inside glowMs (+ one frame)');
  // half way through the ease, it is already most of the way down
  const tw2 = createTweens();
  const m2 = { opacity: T.glowHit };
  tw2.to(m2, 'opacity', T.glowRest, T.glowMs);
  tw2.update(T.glowMs / 2000);
  ok(m2.opacity < T.glowHit - 0.8 * (T.glowHit - T.glowRest), 'an ease-out: most of the drop is in the first half');
}

// --- the lean: timeline, and a re-aim mid-swing -----------------------------
{
  const tw = createTweens();
  const yaw = { value: 0 };
  tw.to(yaw, 'value', yawFor('b'), T.leanMs);
  for (let i = 0; i < 10; i++) tw.update(1 / 60);       // 167 ms in
  const midway = yaw.value;
  ok(midway > 0 && midway < Math.PI, 'mid-swing the rig is between the two');
  tw.to(yaw, 'value', yawFor('w'), T.leanMs);           // a take-back: aim home
  ok(tw.count() === 1, 'a re-aim replaces the tween, never stacks one');
  tw.update(0);
  ok(near(yaw.value, midway), 'and starts from where the rig is');
  while (tw.count()) tw.update(1 / 60);
  ok(yaw.value === 0, 'then lands home');
  tw.to(yaw, 'value', Math.PI, 0);
  ok(yaw.value === Math.PI && tw.count() === 0, 'a zero-length tween lands at once (reduced motion)');
  tw.to(yaw, 'value', 0, T.leanMs);
  tw.settle();
  ok(yaw.value === 0 && tw.count() === 0, 'settle jumps to the end');
}

console.log('room smoke: ' + passed + ' checks passed');
