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
 * is. Then the room's answers: the capture dip starts at dipTo and is home
 * by dipMs; the warmth is flat up to warmFrom and climbs to 1 without a step
 * back; the breath is flat under breathAt and never deeper than breathDepth
 * over it, on the vignette's own period; the game-over drain knows a win
 * from a draw and lights the spot only for a winner; the shaft's level warms,
 * drains and is capped from straight above. Exits non-zero on the first
 * failure.
 * ==========================================================================*/

import {
  ROOM as T, domeColorAt, yawFor, glowTargets, createTweens, easeOutCubic,
  warmthFor, breathFor, overTargets, shaftLevel,
} from '../board/roomMath.js';
import { FRAME, fovForAspect, fitScaleFor } from '../board/frame.js';

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

// --- the frame: the whole board on any screen --------------------------------
{
  const wide = 1280 / 860;
  ok(fovForAspect(wide) === FRAME.fovMin, 'the wide window keeps the 40 degree lens');
  ok(fitScaleFor(wide, fovForAspect(wide)) === 1, 'and its presets stand where they always did');
  const phone = 390 / 844;
  const fov = fovForAspect(phone);
  ok(fov === FRAME.fovMax, 'a phone held upright opens the lens to the cap');
  const fit = fitScaleFor(phone, fov);
  ok(fit > 1.5 && fit < 2.5, 'and stands the presets well back (' + fit.toFixed(2) + 'x)');
  const half = Math.atan(Math.tan((fov * Math.PI) / 360) * phone);
  ok(FRAME.base * fit * Math.tan(half) >= FRAME.reach * FRAME.margin - 1e-9, 'the a and h files are both inside the picture');
  const square = fovForAspect(1);
  ok(square > FRAME.fovMin && square < FRAME.fovMax, 'a square window sits between the two');
  let prevFit = fitScaleFor(0.4, fovForAspect(0.4));
  for (let a = 0.45; a <= 2.2; a += 0.05) {
    const f = fitScaleFor(a, fovForAspect(a));
    ok(f <= prevFit + 1e-9, 'a wider window never stands further back at ' + a.toFixed(2));
    prevFit = f;
  }
  ok(fitScaleFor(NaN, NaN) >= 1 && Number.isFinite(fovForAspect(undefined)), 'garbage in, a sane lens out');
}

// --- the capture dip ---------------------------------------------------------
{
  ok(T.dipMs === 440, 'the dip takes 440 ms');
  ok(T.dipTo > 0 && T.dipTo < 1, 'and goes to a share of the key, not off');
  const tw = createTweens();
  const dip = { value: 1 };
  dip.value = T.dipTo;                                    // the frame the man comes off
  tw.to(dip, 'value', 1, T.dipMs, easeOutCubic);
  ok(dip.value === T.dipTo, 'the dip starts at dipTo');
  let t = 0, prevV = dip.value;
  while (tw.count()) {
    tw.update(1 / 60); t += 1000 / 60;
    ok(dip.value >= prevV - 1e-9 && dip.value <= 1 + 1e-9, 'the key only recovers on the way back at ' + t.toFixed(0) + ' ms');
    prevV = dip.value;
  }
  ok(dip.value === 1, 'and is back at 1');
  ok(t <= T.dipMs + 1000 / 60 + 1e-6, 'by dipMs (+ one frame)');
  // a second capture mid-recovery restarts it from dipTo, one tween, no stack
  const tw2 = createTweens();
  const d2 = { value: T.dipTo };
  tw2.to(d2, 'value', 1, T.dipMs, easeOutCubic);
  for (let i = 0; i < 6; i++) tw2.update(1 / 60);
  ok(d2.value > T.dipTo, 'part way home');
  d2.value = T.dipTo; tw2.to(d2, 'value', 1, T.dipMs, easeOutCubic);
  ok(d2.value === T.dipTo && tw2.count() === 1, 'a second capture restarts the dip and never stacks');
}

// --- the warmth -------------------------------------------------------------
{
  ok(near(T.warmFrom, 0.25), 'the room stays plum for the first quarter of the meter');
  ok(warmthFor(0) === 0 && warmthFor(T.warmFrom) === 0 && warmthFor(T.warmFrom / 2) === 0, 'no warmth at or below warmFrom');
  ok(near(warmthFor(1), 1), 'full warmth at a full meter');
  ok(warmthFor(-1) === 0 && near(warmthFor(2), 1) && warmthFor(NaN) === 0, 'garbage in, a clamped warmth out');
  let prevW = 0;
  for (let m = 0; m <= 1.0001; m += 0.01) {
    const w = warmthFor(m);
    ok(w >= prevW - 1e-12 && w >= 0 && w <= 1, 'the warmth only climbs at meter ' + m.toFixed(2));
    prevW = w;
  }
  ok(warmthFor(0.95) > warmthFor(0.7) && warmthFor(0.7) > warmthFor(0.4), 'and climbs where the ramp does');
  // the warm horizon is pinker than the plum one and still a horizon (dark)
  const rgb = (hex) => [((hex >> 16) & 255) / 255, ((hex >> 8) & 255) / 255, (hex & 255) / 255];
  ok(pink(rgb(T.warmHorizon)) > pink(rgb(T.horizon)), 'the warm horizon is pinker than the plum');
  ok(lum(rgb(T.warmHorizon)) < 0.25, 'and still dark enough to be a horizon');
  ok(pink(rgb(T.warmHaze)) > pink(rgb(T.haze)), 'the warm haze is pinker than the haze');
  // the ease toward a 200 ms step is longer than the step, so no step shows
  ok(T.warmMs > 200, 'the warmth eases over longer than the ramp\'s 200 ms step');
}

// --- the breath -------------------------------------------------------------
{
  ok(near(T.breathAt, 0.7), 'the room breathes from 0.7 (= hud.js vigBreathe)');
  ok(T.breathMs === 3600, 'on the vignette\'s 3600 ms cycle');
  ok(T.breathDepth > 0 && T.breathDepth < 0.3, 'and only a little');
  for (let t = 0; t <= 2 * T.breathMs; t += 50) {
    ok(breathFor(0, t) === 0 && breathFor(0.5, t) === 0 && breathFor(T.breathAt - 0.001, t) === 0, 'no breath under breathAt at ' + t + ' ms');
    const b = breathFor(0.9, t);
    ok(b <= 0 && b >= -T.breathDepth - 1e-12, 'over it, a dip of at most breathDepth at ' + t + ' ms');
  }
  ok(near(breathFor(T.breathAt, 0), 0) && near(breathFor(T.breathAt, T.breathMs), 0), 'the swell is at rest at the start and end of a cycle');
  ok(near(breathFor(1, T.breathMs / 2), -T.breathDepth), 'and fully in at half a cycle');
  ok(near(breathFor(1, T.breathMs / 4), -T.breathDepth / 2), 'ease-in-out: half way in at a quarter cycle');
  ok(breathFor(NaN, 100) === 0 && Number.isFinite(breathFor(1, -100)), 'garbage in, a flat breath out');
}

// --- game over: the drain -----------------------------------------------------
{
  ok(T.overMs === 1200, 'the drain takes 1200 ms (= the ramp\'s sigh-out)');
  ok(T.overLevel < T.overDraw && T.overDraw < 1, 'a win drains deeper than a draw, and a draw still dims');
  ok(T.overRim > T.overLevel && T.overRim < 1, 'the rim keeps more than the key, and still dims');
  const live = overTargets(null, null);
  ok(live.key === 1 && live.fill === 1 && live.hemi === 1 && live.rim === 1 && live.spot === false, 'no result: the live room, no spot');
  const win = overTargets('checkmate', 'w');
  ok(win.key === T.overLevel && win.fill === T.overLevel && win.hemi === T.overLevel, 'a mate drains key, fill and hemisphere to overLevel');
  ok(win.rim === T.overRim && win.spot === true, 'the rim to overRim, and the spot lights');
  ok(overTargets('flag', 'b').spot === true && overTargets('resign', 'b').spot === true, 'a flag or a resignation has a winner too');
  for (const [r, wn] of [['stalemate', null], ['draw', null], ['stalemate', 'w'], ['draw', 'b'], ['checkmate', null]]) {
    const d = overTargets(r, wn);
    ok(d.key === T.overDraw && d.fill === T.overDraw && d.hemi === T.overDraw && d.rim === T.overDraw, 'a draw (' + r + '/' + wn + ') only dims to overDraw');
    ok(d.spot === false, 'and lights no spot');
  }
  // the drain's timeline: down to the level inside overMs, never below it
  const tw = createTweens();
  const drain = { key: 1, spot: 0 };
  tw.to(drain, 'key', win.key, T.overMs, easeOutCubic);
  tw.to(drain, 'spot', 1, T.overMs, easeOutCubic);
  let t = 0;
  while (tw.count()) {
    tw.update(1 / 60); t += 1000 / 60;
    ok(drain.key >= win.key - 1e-9 && drain.key <= 1 + 1e-9 && drain.spot >= 0 && drain.spot <= 1 + 1e-9, 'the drain stays inside its bounds at ' + t.toFixed(0) + ' ms');
  }
  ok(drain.key === win.key && drain.spot === 1 && t <= T.overMs + 1000 / 60 + 1e-6, 'and lands by overMs');
  // a take-back: back to 1 over overBackMs, from where it is
  tw.to(drain, 'key', 1, T.overBackMs, easeOutCubic);
  tw.update(0);
  ok(near(drain.key, win.key) && tw.count() === 1, 'the way back starts from the drained level');
  while (tw.count()) tw.update(1 / 60);
  ok(drain.key === 1, 'and lands at the live level');
  ok(T.overBackMs > 0 && T.overBackMs < T.overMs, 'the way back is quicker than the drain');
}

// --- the shaft --------------------------------------------------------------
{
  ok(T.shaftAlpha > 0 && T.shaftAlpha <= 0.1, 'the shaft is air, never a beam');
  ok(T.shaftTip < T.shaftRadius && T.shaftLength > 0, 'narrow at the light, wide over the board');
  ok(T.shaftRise < T.shaftFadeTip && T.shaftFadeTip < 1, 'lit between the rise and the tip fade');
  ok(near(shaftLevel(), T.shaftAlpha), 'at rest, level, live: the table\'s peak');
  ok(near(shaftLevel({ warmth: 1 }), T.shaftAlpha * (1 + T.shaftWarm)), 'full warmth brightens it by shaftWarm');
  ok(near(shaftLevel({ drain: T.overLevel }), T.shaftAlpha * T.overLevel), 'and the drain takes it down with the key');
  const normal = 7 / Math.hypot(9.7, 7);                 // -forward.y from the normal preset
  ok(near(shaftLevel({ topness: normal }), T.shaftAlpha), 'the normal preset sees it whole');
  ok(near(shaftLevel({ topness: 1 }), T.shaftAlpha * T.shaftTopKeep), 'straight down it is capped to shaftTopKeep');
  ok(shaftLevel({ topness: Math.cos(2.5 * Math.PI / 180) }) < T.shaftAlpha * (T.shaftTopKeep + 0.05), 'and the top preset is near the cap');
  let prevL = shaftLevel({ topness: 0 });
  for (let v = 0; v <= 1.0001; v += 0.01) {
    const l = shaftLevel({ topness: v });
    ok(l <= prevL + 1e-12, 'a steeper look only dims it at ' + v.toFixed(2));
    prevL = l;
  }
  ok(shaftLevel({ warmth: 5, drain: 5, topness: -5 }) <= T.shaftAlpha * (1 + T.shaftWarm) + 1e-12, 'garbage in, a bounded level out');
}

console.log('room smoke: ' + passed + ' checks passed');
