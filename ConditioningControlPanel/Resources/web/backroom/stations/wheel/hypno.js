/* hypno.js - the Daily Daze v3 trance, PURE (CONTRACT 10.13.F): the long last turn's time warp, the quiet room's
 * colour curve, the taffy shear, the Loom hub's angle and the moire rim. No DOM, no three, no clock of its own:
 * scene.js feeds it the frame's numbers and draws what comes back, so node:test holds every curve to the mockup
 * (hypno-spins-v3.html, the wheel section).
 *
 * Angles: `rotorZ` is wheel_rotor.rotation.z (anticlockwise on screen as it grows, three's +Z faces the camera).
 * A clockwise screen angle is -rotorZ. Law 3 (handedness): the Loom hub always turns clockwise, by the wheel's speed
 * in either direction plus a slow drift at rest, so its arms lead at the rim and it reads as pulling inward; physical trailing shapes
 * (the taffy slices, their smear ghosts) bend AGAINST their motion, a = base - k * u. */

import { easeOutQuart } from './wheel.js';

export const TAU = Math.PI * 2;
const clamp01 = v => (Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0);
const lerp = (a, b, t) => a + (b - a) * t;

/* ------------------------------------------------------------ the long last turn */

/** Under this planned speed (rad/s) the landing's clock slows down. */
export const LAST_TURN_SPEED = 1.6;
/** The slowest the clock runs: a third of real time, or 0.6 under Calm and reduced motion (law 6). */
export const SLOW_FLOOR = Object.freeze({ normal: 0.32, calm: 0.6 });
/** How fast the time scale follows its target, and the stage dim its own (per second, as the mockup eases). */
export const WARP_RATE = 6;
export const DIM_RATE = 2.5;

/** The time scale at planned speed `speed` (rad/s): lerp(floor, 1, clamp(speed / 1.6)). */
export function timeScale(speed, { calm = false } = {}) {
  const floor = calm ? SLOW_FLOOR.calm : SLOW_FLOOR.normal;
  return lerp(floor, 1, clamp01(Math.abs(Number(speed)) / LAST_TURN_SPEED));
}

/** The planned rotor speed (rad/s) of a wheel.js landing plan `elapsedMs` into it: the quartic's derivative. */
export function planSpeed(plan, elapsedMs) {
  if (!plan || !(plan.ms > 0)) return 0;
  const u = clamp01(elapsedMs / plan.ms);
  return (Math.abs(plan.to - plan.from) * 4 * (1 - u) ** 3 / plan.ms) * 1000;
}

/**
 * One frame of the landing plan's warped clock. The plan itself (from, to, ms, the landing angle and slice) never
 * changes: only how fast its own clock is read. `warp` = { elapsed (plan ms), scale } from the last frame.
 * Returns the next warp plus `slowing` (the long last turn is on) and the planned `speed`.
 */
export function warpStep(warp, dtMs, plan, { calm = false, on = true } = {}) {
  const w = warp || { elapsed: 0, scale: 1 };
  const dt = Math.max(0, Number(dtMs) || 0);
  const speed = planSpeed(plan, w.elapsed);
  const slowing = !!on && speed < LAST_TURN_SPEED && w.elapsed < plan.ms;
  const target = slowing ? timeScale(speed, { calm }) : 1;
  const scale = w.scale + (target - w.scale) * Math.min(1, (dt / 1000) * WARP_RATE);
  return { elapsed: Math.min(plan.ms, w.elapsed + dt * scale), scale, slowing, speed };
}

/** The rotor rotation at a warped plan clock (wheel.js rotationAt with the clock already warped). */
export const warpedRotation = (plan, elapsed) => plan.from + (plan.to - plan.from) * easeOutQuart(elapsed / plan.ms);

/** The stage dim eases toward 1 while slowing, back to 0 after (per `dtMs`). */
export const stepDim = (dim, slowing, dtMs) => dim + ((slowing ? 1 : 0) - dim) * Math.min(1, (Math.max(0, dtMs) / 1000) * DIM_RATE);
/** The in-station edge vignette's alpha: 0.65 x dim x k. It stays with the tunnel gate off (10.13.A). */
export const edgeAlpha = (dim, k = 1) => 0.65 * clamp01(dim) * k;
/** The "s l o w l y" caption's alpha, as the mockup: 0.7 x dim, hidden under 0.05. */
export const captionAlpha = dim => (dim > 0.05 ? 0.7 * clamp01(dim) : 0);

/* ------------------------------------------------------------------ quiet room */

export const QUIET = Object.freeze({ grey: '#2a2238', max: 0.78, rise: 3, gain: 1.6, backAt: 1.1, spread: 1.6, backS: 0.8 });

/** Angular distance of two angles as 0 (same) .. 1 (opposite). */
export function distance01(a, b) {
  const d = Math.abs((((a - b) % TAU) + TAU) % TAU);
  return Math.min(d, TAU - d) / Math.PI;
}

/**
 * How far a slice that did NOT land mixes toward the grey, `since` seconds after the landing, `dist` its angular
 * distance from the landed slice (distance01), k = strengthK. Greys at 3/s up to min(0.78, k x 1.6); colour flows
 * back from 1.1 s, the nearest slices first (1.6 s across the half circle), each over 0.8 s.
 */
export function quietMix(since, dist, k = 1) {
  if (!(since >= 0)) return 0;
  const back = clamp01((since - QUIET.backAt - clamp01(dist) * QUIET.spread) / QUIET.backS);
  return Math.min(QUIET.max, clamp01(since * QUIET.rise) * (1 - back) * k * QUIET.gain);
}
/** The quiet room is over once every slice has its colour back. */
export const quietDone = since => since >= QUIET.backAt + QUIET.spread + QUIET.backS;
/** The landed slice's mint outline, pulsing as the mockup (held at 0.6 when still). */
export const outlineAlpha = (since, still = false) => (still ? 0.6 : 0.45 + 0.35 * Math.sin(since * 3) ** 2);

/** '#rrggbb' -> [r, g, b] in 0..1. */
export function rgb01(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(String(hex));
  const n = m ? parseInt(m[1], 16) : 0;
  return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255];
}
/** Mix two '#rrggbb' colours by t, as [r, g, b] in 0..1. */
export function mixRgb(a, b, t) {
  const A = rgb01(a), B = rgb01(b), q = clamp01(t);
  return A.map((v, i) => v + (B[i] - v) * q);
}

/* ------------------------------------------------------------------ taffy slices */

export const TAFFY = Object.freeze({ perSpeed: 0.11, max: 1.9, rate: 2.5, curve: 1.25, ghosts: 4, ghostAlpha: 0.16, ghostLagS: 1 / 60 });

/** The shear (rad at the rim) for rotor speed `speed` rad/s: min(speed x 0.11, 1.9) x k. */
export const taffyShear = (speed, k = 1) => Math.min(Math.abs(Number(speed) || 0) * TAFFY.perSpeed, TAFFY.max) * k;
/** The shear eases toward its target at 2.5/s. */
export const stepShear = (cur, want, dtMs) => cur + (want - cur) * Math.min(1, (Math.max(0, dtMs) / 1000) * TAFFY.rate);

/** u across the slice face: 0 at the hub edge, 1 at the rim (runtime slices span 0.185 .. 0.711). */
export const SLICE_R = Object.freeze({ inner: 0.185, outer: 0.711 });
export const sliceU = r => clamp01((r - SLICE_R.inner) / (SLICE_R.outer - SLICE_R.inner));

/**
 * Law 3 on a slice point: the angle offset (rotor radians) at u for a signed shear. `sign` is the rotor's turning
 * direction (+1 when rotation.z grows): the rim lags behind, a = -sign x shear x u^1.25.
 */
export const trailOffset = (shear, u, sign) => -(sign < 0 ? -1 : 1) * shear * clamp01(u) ** TAFFY.curve;

/** The smear ghosts' rotor rotations: where the wheel was 2..5 frames (at 60 Hz) ago at angular velocity `omega`. */
export function ghostRotations(rotorZ, omega, n = TAFFY.ghosts) {
  return Array.from({ length: n }, (_, i) => rotorZ - omega * (i + 2) * TAFFY.ghostLagS);
}

/* ------------------------------------------------------------------ Loom hub + moire rim */

/** Idle drift of the hub spiral, rad/s clockwise, so it keeps pulling inward at rest. */
export const HUB_DRIFT = 0.35;
/** How much of the wheel's own speed the hub takes (the mockup's `vel * 0.9`). */
export const HUB_FOLLOW = 0.9;
/**
 * One frame of the hub's Loom angle (clockwise screen radians). It ACCUMULATES the wheel's speed as a magnitude,
 * so it turns clockwise whichever way the wheel goes: a player can fling the wheel anticlockwise, and a hub read off
 * -rotation.z would then run backward with its arms still leading at the rim and push outward (law 3). `speed` is
 * the rotor's rad/s (either sign), `scale` the long last turn's time scale, which slows the drift as the mockup's
 * warped clock does: hubRot += (|vel| x 0.9 + 0.35) x dt.
 */
export const stepHub = (rot, speed, dtS, scale = 1) =>
  (Number(rot) || 0) + (Math.abs(Number(speed) || 0) * HUB_FOLLOW + HUB_DRIFT * clamp01(scale)) * Math.max(0, Number(dtS) || 0);

export const MOIRE = Object.freeze({ lines: 60, gold: '#e8c27a', goldAlpha: 0.75, mint: '#5fffd0', mintAlpha: 0.55 });
/** The two moire rings' rotations: one at the rotor angle, one at 0.9 x angle + 0.3. */
export const moireRotations = rotorZ => [rotorZ, 0.9 * rotorZ + 0.3];
/** 60 radial line segments between radii ri and ro, as [x0, y0, x1, y1, ...] (z left to the caller). */
export function moireSegments(ri, ro, lines = MOIRE.lines) {
  const out = [];
  for (let k = 0; k < lines; k++) { const a = (k * TAU) / lines, c = Math.cos(a), s = Math.sin(a); out.push(c * ri, s * ri, c * ro, s * ro); }
  return out;
}

/** What the page draws for a dress: Full-only texture (moire, taffy), the hub (Loom or brass star), k. */
export function dressOf({ intensity = 'normal', reduced = false, gates = null } = {}) {
  const calm = !!reduced || intensity === 'calm';
  const full = intensity === 'full' && !reduced;
  const spiral = !(gates && gates.spiral === false);
  return { full, calm, k: calm ? 0.5 : 1, hub: spiral ? 'loom' : 'star', moire: full, taffy: full };
}
