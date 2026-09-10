/* ============================================================================
 * ramp/schedule.js - heat -> what fires, and what stays on.
 *
 * The shape is the Arcademy Distraction Engine's (engine/curves.js +
 * engine/schedule.js): a cadence band per one-shot kind interpolated by heat, a
 * seeded RNG so a replay is a replay, and the plainShare ramp so effects stay a
 * SPILL at low heat instead of a metronome. Sustained layers are not rolled -
 * they unlock at fixed meter thresholds and their strength tracks the meter.
 *
 * Pure except for the clock the caller passes in: `tick(now, heat, meter)`.
 * ==========================================================================*/

import { RAMP_TUNING, clamp01, lerp, plainShare } from './meter.js';

/** mulberry32 - a tiny seeded PRNG so smoke tests replay exactly. */
export function makeRng(seed) {
  let a = 0;
  const s = String(seed == null ? Math.random() : seed);
  for (let i = 0; i < s.length; i++) { a = (a * 31 + s.charCodeAt(i)) >>> 0; }
  return function next() {
    a = (a + 0x6D2B79F5) >>> 0;
    let x = a;
    x = Math.imul(x ^ (x >>> 15), 1 | x);
    x = (x + Math.imul(x ^ (x >>> 7), 61 | x)) ^ x;
    return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
  };
}

/** ms between spawns of `kind` at this heat (Infinity when the kind is idle). */
export function cadenceMs(kind, heat, tuning = RAMP_TUNING) {
  const band = tuning[kind];
  if (!band) return Infinity;
  return lerp(band.slowMs, band.fastMs, clamp01(heat));
}

/**
 * The sustained stack for a meter value: which layers are on, and how hard.
 *
 * `opts.cardLive` says a video card is currently over the board. The veils step
 * back while it is, so the card is the thing you cannot see past, rather than
 * the last straw on top of two other walls.
 */
export function sustainedFor(meter, tuning = RAMP_TUNING, opts = {}) {
  const m = clamp01(meter);
  const u = tuning.unlock;
  // each layer's own 0..1 progress from its unlock point up to a full meter
  const ramp = (from) => (m <= from ? 0 : clamp01((m - from) / Math.max(0.01, 1 - from)));
  const meltR = ramp(u.melt);
  const blurR = ramp(u.blur);
  const spiralR = ramp(u.spiral);
  const overR = ramp(u.overlay);

  // the two full-screen veils share one budget: they are the only layers that
  // can hide the board outright, and at a full meter they would otherwise sum
  // past opaque. Damped further while a card is up.
  const damp = opts.cardLive ? tuning.cardVeilDamp : 1;
  let spiralA = (m >= u.spiral ? lerp(tuning.spiral.minAlpha, tuning.spiral.maxAlpha, spiralR) : 0) * damp;
  let overA = (m >= u.overlay ? lerp(tuning.overlay.minAlpha, tuning.overlay.maxAlpha, overR) : 0) * damp;
  const veil = spiralA + overA;
  if (veil > tuning.veilBudget) {
    const k = tuning.veilBudget / veil;
    spiralA *= k; overA *= k;
  }
  return {
    melt: {
      on: m >= u.melt,
      // the melt is a full-screen wash too, so it steps back with the veils:
      // three transparent things stacked stop being transparent
      alpha: (m >= u.melt ? lerp(tuning.melt.minAlpha, tuning.melt.maxAlpha, meltR) : 0) * damp,
    },
    blur: {
      on: m >= u.blur,
      // HARD CAP: never past blurMaxPx, whatever the meter says
      px: m >= u.blur ? Math.min(tuning.blurMaxPx, tuning.blurMaxPx * blurR) : 0,
    },
    spiral: {
      on: m >= u.spiral,
      alpha: spiralA,
      holdMs: Math.round(lerp(tuning.spiral.minHoldMs, tuning.spiral.maxHoldMs, spiralR)),
    },
    overlay: {
      on: m >= u.overlay,
      alpha: overA,
    },
  };
}

/** How long the video card sticks over the board at this meter. */
export function videoHoldMs(meter, tuning = RAMP_TUNING) {
  const v = tuning.videoCard;
  return Math.round(lerp(v.minHoldSec, v.maxHoldSec, clamp01(meter)) * 1000);
}

/**
 * createSchedule({ tuning, seed })
 *
 * tick(now, heat, meter) -> { fire: ['flash', ...], sustained: {...} }
 *
 * Cadence is accumulated per kind (never a setInterval), so a paused tab or a
 * long frame catches up by at most one spawn instead of dumping a backlog.
 */
export function createSchedule({ tuning = RAMP_TUNING, seed = 'pbp' } = {}) {
  const rng = makeRng(seed);
  const kinds = ['flash', 'gifRain'];
  const nextAt = { flash: 0, gifRain: 0 };
  let started = false;

  function reset(now = 0) {
    started = false;
    for (const k of kinds) nextAt[k] = now;
  }

  function tick(now, heat, meter, opts) {
    const h = clamp01(heat);
    const fire = [];
    if (!started) { started = true; for (const k of kinds) nextAt[k] = now + cadenceMs(k, h, tuning); }
    for (const k of kinds) {
      const gap = cadenceMs(k, h, tuning);
      if (!Number.isFinite(gap)) { nextAt[k] = now + 1000; continue; }
      if (now >= nextAt[k]) {
        // plainShare: the busier it gets, the fewer beats stay empty
        if (rng() >= plainShare(h, tuning)) fire.push(k);
        nextAt[k] = now + gap;
      }
      // a heat drop should pull the next spawn in, not strand it in the future
      if (nextAt[k] - now > gap) nextAt[k] = now + gap;
    }
    return { fire, sustained: sustainedFor(meter == null ? h : meter, tuning, opts), heat: h };
  }

  /** A capture kick: an immediate burst, bigger for the side that took a piece. */
  function burstFor(role, heat, tuning2 = tuning) {
    const h = clamp01(heat);
    const base = role === 'taker' ? 2 : 1;
    return {
      flashes: base + Math.round(h * (role === 'taker' ? 3 : 2)),
      gifs: role === 'taker' ? 1 + Math.round(h * 3) : Math.round(h * 2),
      shakeMs: Math.round(lerp(160, 420, h)) * (role === 'taker' ? 1 : 0.6),
      tuning: tuning2,
    };
  }

  return { tick, reset, burstFor, rng };
}

export default createSchedule;
