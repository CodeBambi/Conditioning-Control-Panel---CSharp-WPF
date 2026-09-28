/* ============================================================================
 * exec/tween.js - the pure motion maths every effect's IN and OUT is built on.
 *
 * Owner, 2026-09-23: "I pop a glitch bubble and we instantly see the fullscreen
 * gif, and the same prob goes for flashes and a lot of more things. We should
 * add animations in and out." Breakout is the case study (backroom/stations/
 * breakout/feedback.js: squashScale, jellyScale, pushInZoom are all small pure
 * curves the renderer samples). This file is the goon twin of that idea:
 *
 *   - easings on u = 0..1 (clamped, total, NaN-safe), incl. an overshoot
 *     (backOut) and an elastic settle for the "lands and settles" beat;
 *   - a damped spring sampled in closed form (no integrator drift);
 *   - toKeyframes(): sample any curve into Web Animations keyframes, so the
 *     DOM side (exec/motion.js) can hand the compositor a transform/opacity
 *     timeline and never run a per-frame JS loop for an entrance;
 *   - createTweens(): a tiny cancel-safe scheduler for the few things that DO
 *     need a per-frame value (the shake), driven by whatever clock you pass.
 *
 * House timings (The Line, AGENTS.md): THUD 340 ms cubic-bezier(.2,1.5,.4,1),
 * SHIVER 250 ms, REVEAL 620 ms. Entrances 150-350 ms, exits 120-250 ms.
 *
 * PURE and import-safe under node: no DOM, no timers at import.
 * ==========================================================================*/

export const TIMING = Object.freeze({
  THUD_MS: 340,
  THUD_EASE: 'cubic-bezier(.2,1.5,.4,1)',
  SHIVER_MS: 250,
  REVEAL_MS: 620,
  IN_MIN_MS: 150,
  IN_MAX_MS: 350,
  OUT_MIN_MS: 120,
  OUT_MAX_MS: 250,
  /** Photosafe: nothing may repeat a visible change faster than this. */
  MAX_FLICKER_HZ: 3,
});

/** Clamp to 0..1; anything that is not a finite number is 0. */
export function clamp01(u) {
  const n = Number(u);
  if (!(n === n) || n <= 0) return 0;
  return n >= 1 ? 1 : n;
}

export const lerp = (a, b, u) => a + (b - a) * u;

/* ------------------------------------------------------------------ easings */

export const linear = (u) => clamp01(u);
export const quadOut = (u) => { const t = clamp01(u); return 1 - (1 - t) * (1 - t); };
export const cubicOut = (u) => { const t = 1 - clamp01(u); return 1 - t * t * t; };
export const cubicIn = (u) => { const t = clamp01(u); return t * t * t; };
export const cubicInOut = (u) => {
  const t = clamp01(u);
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
};
export const expoOut = (u) => { const t = clamp01(u); return t >= 1 ? 1 : 1 - Math.pow(2, -10 * t); };

/** Overshoot then settle. s = 1.70158 is Penner's classic ~10% overshoot. */
export function backOut(u, s = 1.70158) {
  const t = clamp01(u) - 1;
  return 1 + (s + 1) * t * t * t + s * t * t;
}
/** Wind back before going (anticipation). Dips below 0 first. */
export function backIn(u, s = 1.70158) {
  const t = clamp01(u);
  return (s + 1) * t * t * t - s * t * t;
}
/** A springy arrival: rings around 1 and settles. `bounces` half-swings. */
export function elasticOut(u, bounces = 3) {
  const t = clamp01(u);
  if (t === 0 || t === 1) return t;
  return 1 - Math.exp(-6 * t) * Math.cos(t * Math.PI * Math.max(1, bounces));
}

/**
 * Damped spring from 0 to 1, closed form, t in SECONDS. Underdamped when
 * damping < 1 (overshoots), critically damped at 1. Default stiffness/damping
 * give an ~8% overshoot that is at rest by about 0.45 s.
 */
export function spring(t, { stiffness = 260, damping = 0.55 } = {}) {
  const s = Number(t);
  if (!(s > 0)) return 0;
  const w0 = Math.sqrt(Math.max(1, stiffness));
  const z = Math.max(0.05, Math.min(1, damping));
  if (z >= 1) return 1 - (1 + w0 * s) * Math.exp(-w0 * s);
  const wd = w0 * Math.sqrt(1 - z * z);
  const e = Math.exp(-z * w0 * s);
  return 1 - e * (Math.cos(wd * s) + (z * w0 / wd) * Math.sin(wd * s));
}

/** Seconds until a spring stays within `eps` of 1 for good (a sampled bound). */
export function springSettleS(opts, eps = 0.01, maxS = 3) {
  let last = 0;
  for (let s = 0; s <= maxS; s += 0.005) if (Math.abs(1 - spring(s, opts)) > eps) last = s;
  return last;
}

/** A damped wobble for u in 0..1: starts at 1, swings, ends at 0 (Breakout's
 *  brick wobble shape). Multiply by an amplitude. */
export function wobble(u, swings = 3) {
  const t = clamp01(u);
  if (t >= 1) return 0;
  const k = (1 - t) * (1 - t);
  return Math.cos(t * Math.PI * 2 * swings) * k;
}

/* ------------------------------------------------------------- keyframes */

/**
 * Sample a curve into Web Animations keyframes.
 *   toKeyframes(n, (u) => ({ opacity: u, transform: `scale(${...})` }))
 * The mapper receives u = 0..1 (linear time) and returns a keyframe; this adds
 * `offset`. n is clamped to 2..60. The animation itself should run `linear`,
 * because the curve is already baked into the samples.
 */
export function toKeyframes(n, map) {
  const count = Math.max(2, Math.min(60, n | 0));
  const out = [];
  for (let i = 0; i < count; i++) {
    const u = i / (count - 1);
    const k = Object.assign({}, map(u));
    k.offset = Math.round(u * 10000) / 10000;
    out.push(k);
  }
  return out;
}

/**
 * Where an exit must START so it ends by the authored end. An effect's
 * receipt/timer is the authority; motion only borrows the tail of the hold.
 * Returns {atMs, outMs}: a hold too short for a full exit gets a shorter one
 * (at most a third of the hold, so the readable middle survives).
 */
export function exitWindow(holdMs, wantOutMs) {
  const hold = Math.max(0, Number(holdMs) || 0);
  const want = Math.max(0, Number(wantOutMs) || 0);
  const outMs = Math.min(want, Math.max(Math.round(hold / 3), 0), hold);
  return { atMs: Math.max(0, hold - outMs), outMs };
}

/**
 * Shake offsets in REAL px: a decaying random jiggle. Deterministic when you
 * pass an rng. Returns [{x, y}] sampled at `steps` points over the shake.
 * Amplitude decays with (1-u)^2 so the first frames carry the punch.
 */
export function shakePath(px, steps = 8, rng = Math.random) {
  const amp = Math.max(0, Number(px) || 0);
  const n = Math.max(2, Math.min(24, steps | 0));
  const out = [];
  for (let i = 0; i < n; i++) {
    const u = i / (n - 1);
    const k = amp * (1 - u) * (1 - u);
    const x = i === n - 1 ? 0 : (rng() * 2 - 1) * k;
    const y = i === n - 1 ? 0 : (rng() * 2 - 1) * k;
    out.push({ x: Math.round(x * 100) / 100, y: Math.round(y * 100) / 100 });
  }
  return out;
}

/** Evenly spread burst directions with a jitter, for n particles. */
export function burstVectors(n, { minR = 40, maxR = 110, jitter = 0.35, rng = Math.random } = {}) {
  const count = Math.max(0, n | 0);
  const out = [];
  for (let i = 0; i < count; i++) {
    const a = (Math.PI * 2 * i) / Math.max(1, count) + (rng() * 2 - 1) * jitter;
    const r = minR + rng() * Math.max(0, maxR - minR);
    out.push({ dx: Math.round(Math.cos(a) * r), dy: Math.round(Math.sin(a) * r) });
  }
  return out;
}

/* ------------------------------------------------------------- scheduler */

/**
 * A cancel-safe tween scheduler. The clock is yours: call step(nowMs) from a
 * requestAnimationFrame loop (or from a test with fake times).
 *
 *   const tw = createTweens();
 *   const h = tw.add({ ms: 250, ease: cubicOut, onUpdate: (v) => ..., onDone });
 *   tw.step(now)            // advances every live tween, returns live count
 *   h.cancel()              // idempotent; onDone does NOT fire
 *   tw.cancelAll()
 *
 * The first step() a tween sees is its t = 0 (so adding between frames never
 * skips the start). A throwing onUpdate kills only that tween.
 */
export function createTweens() {
  const live = new Set();
  function add({ ms = 250, ease = linear, from = 0, to = 1, onUpdate, onDone, delayMs = 0 } = {}) {
    const rec = { ms: Math.max(1, Number(ms) || 1), ease, from, to, onUpdate, onDone, delayMs: Math.max(0, delayMs || 0), start: null, dead: false };
    live.add(rec);
    return {
      cancel() { rec.dead = true; live.delete(rec); },
      get done() { return rec.dead; },
    };
  }
  function step(nowMs) {
    const now = Number(nowMs) || 0;
    for (const rec of Array.from(live)) {
      if (rec.dead) { live.delete(rec); continue; }
      if (rec.start === null) rec.start = now + rec.delayMs;
      if (now < rec.start) continue;
      const u = clamp01((now - rec.start) / rec.ms);
      try {
        if (typeof rec.onUpdate === 'function') rec.onUpdate(lerp(rec.from, rec.to, rec.ease(u)), u);
      } catch (_e) { rec.dead = true; live.delete(rec); continue; }
      if (u >= 1) {
        rec.dead = true;
        live.delete(rec);
        try { if (typeof rec.onDone === 'function') rec.onDone(); } catch (_e) { /* the tween is over either way */ }
      }
    }
    return live.size;
  }
  return {
    add,
    step,
    cancelAll() { for (const r of live) r.dead = true; live.clear(); },
    get size() { return live.size; },
  };
}

export default { TIMING, clamp01, backOut, elasticOut, spring, toKeyframes, exitWindow, shakePath, createTweens };
