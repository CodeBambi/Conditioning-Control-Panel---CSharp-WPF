/* ============================================================================
 * ui/juice.js - the pure maths behind every tween in the Goon Game chrome.
 *
 * NO DOM HERE, ever. This file is curves, springs, schedules and particle
 * descriptors, and nothing else, so node can test every number the page moves
 * by. ui/juiceDom.js is the half that touches elements and calls these.
 *
 * The house timings (from The Line, reused by Breakout and the launcher):
 *   THUD   340 ms  cubic-bezier(.2, 1.5, .4, 1)  a thing lands with weight
 *   SHIVER 250 ms                                a small shake that settles
 *   REVEAL 620 ms                                a thing is uncovered
 * Entrances 150-350 ms, exits 120-250 ms, and every exit goes somewhere.
 *
 * Photosafe: nothing here schedules a change faster than 3 Hz on a large area.
 * The shake is a few real pixels and damps out inside SHIVER.
 * ==========================================================================*/

export const THUD_MS = 340;
export const SHIVER_MS = 250;
export const REVEAL_MS = 620;
export const ENTER_MS = 280;
export const EXIT_MS = 180;
export const THUD_EASE = 'cubic-bezier(.2, 1.5, .4, 1)';
export const OUT_EASE = 'cubic-bezier(.4, 0, 1, 1)';
export const SETTLE_EASE = 'cubic-bezier(.2, .8, .3, 1)';

/** Stagger between siblings, and the ceiling on the whole cascade. */
export const STAGGER_STEP_MS = 45;
export const STAGGER_MAX_MS = 360;

/** Particle ceilings: a lite tier gets half, never a broken effect. */
export const BURST_FULL = 14;
export const BURST_LITE = 6;

export const clamp01 = (t) => (t <= 0 ? 0 : t >= 1 ? 1 : (Number(t) || 0));

/* ------------------------------------------------------------------ easing */

export const ease = Object.freeze({
  linear: (t) => clamp01(t),
  outCubic: (t) => { const u = 1 - clamp01(t); return 1 - u * u * u; },
  inCubic: (t) => { const u = clamp01(t); return u * u * u; },
  outQuart: (t) => { const u = 1 - clamp01(t); return 1 - u * u * u * u; },
  /** Overshoot then settle; s = 1.70158 is the classic "back". */
  outBack: (t, s = 1.70158) => {
    const u = clamp01(t) - 1;
    return 1 + (s + 1) * u * u * u + s * u * u;
  },
});

/**
 * Evaluate a CSS cubic-bezier(x1, y1, x2, y2) at progress x. Newton plus a
 * bisection fallback, the same way browsers do it. Used to keep the JS-driven
 * tweens (count-ups, arcs) on the exact curve the CSS ones use.
 */
export function cubicBezier(x1, y1, x2, y2) {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
  const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
  const sx = (t) => ((ax * t + bx) * t + cx) * t;
  const sy = (t) => ((ay * t + by) * t + cy) * t;
  const dx = (t) => (3 * ax * t + 2 * bx) * t + cx;
  return (x) => {
    const X = clamp01(x);
    if (X === 0 || X === 1) return X;
    let t = X;
    for (let i = 0; i < 8; i++) {
      const err = sx(t) - X;
      if (Math.abs(err) < 1e-6) return sy(t);
      const d = dx(t);
      if (Math.abs(d) < 1e-6) break;
      t -= err / d;
    }
    let lo = 0, hi = 1;
    t = X;
    for (let i = 0; i < 30; i++) {
      const v = sx(t);
      if (Math.abs(v - X) < 1e-6) break;
      if (v < X) lo = t; else hi = t;
      t = (lo + hi) / 2;
    }
    return sy(t);
  };
}

/** The THUD curve as a function (overshoots past 1 around 40% in). */
export const thud = cubicBezier(0.2, 1.5, 0.4, 1);

/* ---------------------------------------------------------------- count up */

/**
 * The value a count-up shows at elapsed ms. Integer, never overshoots the
 * target (a score that reads 104 on the way to 100 is a lie), and lands
 * exactly on `to` at `ms`.
 */
export function countUpValue(from, to, elapsed, ms = 600) {
  const a = Number(from) || 0;
  const b = Number(to) || 0;
  if (!(ms > 0) || elapsed >= ms) return Math.round(b);
  if (elapsed <= 0) return Math.round(a);
  return Math.round(a + (b - a) * ease.outCubic(elapsed / ms));
}

/**
 * How long a count-up from `from` to `to` should take: short for a small bump,
 * longer for a big number, capped so a recap never makes you wait.
 */
export function countUpMs(from, to, { min = 260, max = 1100, perUnit = 9 } = {}) {
  const d = Math.abs((Number(to) || 0) - (Number(from) || 0));
  return Math.round(Math.max(min, Math.min(max, min + d * perUnit)));
}

/* ------------------------------------------------------------------ spring */

/**
 * One semi-implicit Euler step of a damped spring toward `target`.
 * state = {x, v}. Returns a NEW state. dt in seconds, clamped so a tab that
 * slept for a second does not launch the value into orbit.
 */
export function springStep(state, target, dt, { k = 320, c = 22 } = {}) {
  const h = Math.max(0, Math.min(0.05, Number(dt) || 0));
  const x = Number(state && state.x) || 0;
  const v = Number(state && state.v) || 0;
  const a = -k * (x - target) - c * v;
  const nv = v + a * h;
  return { x: x + nv * h, v: nv };
}

/** True once a spring has settled within eps of target (position AND speed). */
export function springSettled(state, target, eps = 0.002) {
  return Math.abs((state.x || 0) - target) < eps && Math.abs(state.v || 0) < eps * 10;
}

/* -------------------------------------------------------------- pop, squash */

/**
 * Keyframes for a pop-in (scale + opacity), as plain objects the Web
 * Animations API takes directly. `calm` = fades only (reduced motion).
 */
export function popInFrames({ from = 0.6, over = 1.08, calm = false } = {}) {
  if (calm) return [{ opacity: 0 }, { opacity: 1 }];
  return [
    { opacity: 0, transform: 'scale(' + from + ')' },
    { opacity: 1, transform: 'scale(' + over + ')', offset: 0.55 },
    { opacity: 1, transform: 'scale(1)' },
  ];
}

/** An exit that goes somewhere: shrinks toward (dx, dy) and fades. */
export function popOutFrames({ dx = 0, dy = -10, to = 0.7, calm = false } = {}) {
  if (calm) return [{ opacity: 1 }, { opacity: 0 }];
  return [
    { opacity: 1, transform: 'translate(0px, 0px) scale(1)' },
    { opacity: 0, transform: 'translate(' + round1(dx) + 'px, ' + round1(dy) + 'px) scale(' + to + ')' },
  ];
}

/**
 * A press squash: wide and short, then a stretch back past rest, then rest.
 * Volume-preserving-ish (sx * sy stays near 1) so it reads as rubber.
 */
export function squashFrames(amount = 0.12, calm = false) {
  if (calm) return [{ opacity: 1 }, { opacity: 0.85 }, { opacity: 1 }];
  const a = Math.max(0, Math.min(0.3, Number(amount) || 0));
  return [
    { transform: 'scale(1, 1)' },
    { transform: 'scale(' + r3(1 + a) + ', ' + r3(1 - a) + ')', offset: 0.3 },
    { transform: 'scale(' + r3(1 - a * 0.45) + ', ' + r3(1 + a * 0.45) + ')', offset: 0.65 },
    { transform: 'scale(1, 1)' },
  ];
}

/**
 * A damped shake in REAL pixels. Returns keyframes. Amplitude decays
 * exponentially; the first swing is `px`, the last is under a pixel. The
 * direction alternates, so the element ends exactly where it started.
 */
export function shakeFrames(px = 4, { swings = 6, rnd = null } = {}) {
  const amp = Math.max(0, Math.min(16, Number(px) || 0));
  const out = [{ transform: 'translate(0px, 0px)' }];
  for (let i = 0; i < swings; i++) {
    const k = Math.pow(0.55, i);
    const sign = i % 2 === 0 ? 1 : -1;
    const jitter = rnd ? (rnd() - 0.5) * 0.6 : 0;
    out.push({ transform: 'translate(' + round1(sign * amp * k) + 'px, ' + round1((jitter + (i % 2 ? 0.4 : -0.4)) * amp * k) + 'px)' });
  }
  out.push({ transform: 'translate(0px, 0px)' });
  return out;
}

/* --------------------------------------------------------------- staggering */

/**
 * Delays for n siblings entering one after another. The step shrinks when
 * there are many, so the cascade never takes longer than `max`.
 */
export function staggerDelays(n, { step = STAGGER_STEP_MS, max = STAGGER_MAX_MS, start = 0 } = {}) {
  const count = Math.max(0, n | 0);
  if (count === 0) return [];
  const s = count > 1 ? Math.min(step, max / (count - 1)) : 0;
  const out = new Array(count);
  for (let i = 0; i < count; i++) out[i] = Math.round(start + i * s);
  return out;
}

/* ------------------------------------------------------------------ bursts */

/** A tiny seeded rng (mulberry32), so a test can pin a burst exactly. */
export function seededRng(seed = 1) {
  let a = (seed >>> 0) || 1;
  return () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** How many particles a burst gets on this tier; calm gets none. */
export function burstCount(want = BURST_FULL, { lite = false, calm = false } = {}) {
  if (calm) return 0;
  const n = Math.max(0, want | 0);
  return lite ? Math.min(n, Math.max(1, Math.round(n * (BURST_LITE / BURST_FULL)))) : Math.min(n, 32);
}

/**
 * Particle descriptors for a radial burst: where each one flies (dx, dy in
 * px), how big it is, how long it lives and how late it starts. Spread evenly
 * round the circle with a little jitter, so a small burst never clumps.
 * `bias` tilts the whole burst (radians); `arc` narrows it to a cone.
 */
export function burstParticles(n, { dist = 90, spread = 60, rnd = Math.random, arc = Math.PI * 2, bias = 0,
  life = 520, sizeMin = 4, sizeMax = 10 } = {}) {
  const count = Math.max(0, n | 0);
  const out = [];
  for (let i = 0; i < count; i++) {
    const base = arc >= Math.PI * 2 - 1e-6 ? (i / count) * arc : (count > 1 ? (i / (count - 1) - 0.5) * arc : 0);
    const a = bias + base + (rnd() - 0.5) * (arc / Math.max(4, count)) * 0.8;
    const d = dist + rnd() * spread;
    out.push({
      dx: round1(Math.cos(a) * d),
      dy: round1(Math.sin(a) * d),
      size: round1(sizeMin + rnd() * (sizeMax - sizeMin)),
      ms: Math.round(life * (0.75 + rnd() * 0.5)),
      delay: Math.round(rnd() * 40),
    });
  }
  return out;
}

/* -------------------------------------------------------------------- arcs */

/**
 * A thrown thing's path: a quadratic bezier from `a` to `b` that lifts by
 * `lift` px (of the distance) at its peak. Returns `steps + 1` points with
 * the scale and rotation to wear at each, for Web Animations keyframes.
 */
export function arcPoints(a, b, { steps = 10, lift = 0.28, spin = 0, scaleFrom = 1, scaleTo = 0.55 } = {}) {
  const ax = Number(a && a.x) || 0, ay = Number(a && a.y) || 0;
  const bx = Number(b && b.x) || 0, by = Number(b && b.y) || 0;
  const dist = Math.hypot(bx - ax, by - ay);
  const mx = (ax + bx) / 2;
  const my = Math.min(ay, by) - dist * lift;
  const n = Math.max(2, steps | 0);
  const out = [];
  for (let i = 0; i <= n; i++) {
    const t = i / n;
    const u = 1 - t;
    out.push({
      t,
      x: round1(u * u * ax + 2 * u * t * mx + t * t * bx),
      y: round1(u * u * ay + 2 * u * t * my + t * t * by),
      scale: r3(scaleFrom + (scaleTo - scaleFrom) * t + Math.sin(t * Math.PI) * 0.18),
      rot: round1(spin * t),
    });
  }
  return out;
}

/* ------------------------------------------------------------------- pitch */

/** C major pentatonic, in semitones above the root. No semitone clashes. */
export const PENTA = Object.freeze([0, 2, 4, 7, 9]);
/** C5, the root the Back Room kit and Breakout share. */
export const ROOT_HZ = 523.2511;

/** The i-th rung of the pentatonic ladder (wraps an octave every five). */
export function pentaSemis(i) {
  const k = Math.max(0, i | 0);
  return PENTA[k % 5] + 12 * Math.floor(k / 5);
}

/** Frequency of a rung, optionally shifted by whole octaves. */
export function pentaHz(i, octave = 0) {
  return ROOT_HZ * Math.pow(2, (pentaSemis(i) + 12 * (octave | 0)) / 12);
}

/**
 * Gain for a rung: tilted down as the ladder climbs so high notes never
 * shout, and always inside the house band 0.03..0.13.
 */
export function rungGain(i, base = 0.09) {
  const g = base * Math.pow(0.93, Math.max(0, i | 0));
  return Math.max(0.03, Math.min(0.13, g));
}

/* ----------------------------------------------------------------- helpers */

function round1(v) { return Math.round(v * 10) / 10; }
function r3(v) { return Math.round(v * 1000) / 1000; }

export default {
  THUD_MS, SHIVER_MS, REVEAL_MS, ENTER_MS, EXIT_MS, THUD_EASE, OUT_EASE, SETTLE_EASE,
  ease, cubicBezier, thud, countUpValue, countUpMs, springStep, springSettled,
  popInFrames, popOutFrames, squashFrames, shakeFrames, staggerDelays,
  seededRng, burstCount, burstParticles, arcPoints, PENTA, ROOT_HZ, pentaSemis, pentaHz, rungGain,
};
