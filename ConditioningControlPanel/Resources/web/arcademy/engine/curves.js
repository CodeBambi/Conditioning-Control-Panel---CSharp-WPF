/* ============================================================================
 * engine/curves.js — PURE math for the Distraction Engine.
 *
 * Channel vector (already clamped to caps) -> real render units, plus the
 * difficulty model ported from DTRH: the per-phase SAWTOOTH `intensityCurve`
 * and the PLAIN-SHARE ramp (.80 -> .30 effect-free share, so effects stay rare
 * at the top instead of wall-to-wall).
 *
 * Everything here is pure and node-testable; the live DOM path calls exactly
 * these functions, so the tested math IS the shipped math (intake's rule).
 * ==========================================================================*/

import { clamp01, smoothstep, lerp } from '../core/caps.js';

/* ---- cadence endpoints (ms) — Intake's, verbatim ------------------------- */
export const FLASH_MS  = Object.freeze({ slow: 1600, fast: 110 });
export const SUB_MS    = Object.freeze({ slow: 2600, fast: 360 });
export const BUBBLE_MS = Object.freeze({ slow: 2200, fast: 260 });

/** Comfort ceiling on any full-screen wash alpha so cap=1 is bright, not blinding. */
export const FLASH_ALPHA_CEIL = 0.85;

/** Node budgets. flash_burst: 20 live desktop / 3 on a coarse-pointer-or-low-motion
 *  device (DTRH FLASH_LIVE_CAP). gif_burst 10, gif_rain 14 (Intake MAX_NODES).
 *  Every kind now has a *Lite twin (mobile perf, 2026-08-25), picked the same
 *  way flashBurstLite always was - `ctx.lite()` (coarse pointer OR
 *  motionLevel <= 1) at the consumption site - so a phone spends fewer live
 *  nodes per effect while a desktop budget is untouched. */
export const NODE_CAPS = Object.freeze({
  flashBurst: 20, flashBurstLite: 3,
  gifBurst: 10, gifBurstLite: 4,
  gifRain: 14, gifRainLite: 6,
  bubbles: 18, bubblesLite: 8,
  ambient: 24, ambientLite: 10,
  subFlash: 6, subFlashLite: 3,
});

/** DTRH's sidechain duck hierarchy — one policy, emitted as duck requests. */
export const DUCK = Object.freeze({ voice: 0.4, spotlight: 0.25, voiceUnderSpotlight: 0.15 });

const finiteInterval = (active, rate, slow, fast) => (active ? lerp(slow, fast, clamp01(rate)) : Infinity);

/** bgIntensity -> row_drift numbers (sweep rate + peak amplitude). Pure. */
export function driftSpec(bgIntensity) {
  const b = clamp01(bgIntensity);
  return { hz: 0.02 + b * 0.10, px: Math.round(4 + b * 46) };
}

/** bubbleRate -> bubble_field cadence + alpha (Intake's 2200->260ms band). Pure. */
export function bubbleSpec(bubbleRate) {
  const r = clamp01(bubbleRate);
  return {
    on: r > 0.001,
    cadenceMs: r > 0.001 ? lerp(BUBBLE_MS.slow, BUBBLE_MS.fast, r) : Infinity,
    alpha: r > 0.001 ? (0.10 + r * 0.40) : 0,
  };
}

/**
 * Clamped channels -> concrete render numbers.
 * @param {import('../core/caps.js').Channels} ch ALREADY clampToCaps'd.
 */
export function channelsToVisual(ch) {
  const flashRate    = clamp01(ch && ch.flashRate);
  const flashOpacity = clamp01(ch && ch.flashOpacity);
  const subDensity   = clamp01(ch && ch.subDensity);
  const bubbleRate   = clamp01(ch && ch.bubbleRate);
  const bg           = clamp01(ch && ch.bgIntensity);

  const flashOn  = flashRate  > 0.001;
  const subOn    = subDensity > 0.001;
  const bubbleOn = bubbleRate > 0.001;

  return {
    flashOn,
    flashMs:     finiteInterval(flashOn, flashRate, FLASH_MS.slow, FLASH_MS.fast),
    flashAlpha:  flashOpacity * FLASH_ALPHA_CEIL,
    subOn,
    subMs:       finiteInterval(subOn, subDensity, SUB_MS.slow, SUB_MS.fast),
    subAlpha:    subOn ? (0.05 + subDensity * 0.35) : 0,
    bubbleOn,
    bubbleMs:    finiteInterval(bubbleOn, bubbleRate, BUBBLE_MS.slow, BUBBLE_MS.fast),
    bubbleAlpha: bubbleOn ? (0.10 + bubbleRate * 0.40) : 0,
    // background dressing: drift speed/amplitude, ambient density, crt level
    driftHz:     driftSpec(bg).hz,          // cycles/sec of a row-drift sweep
    driftPx:     driftSpec(bg).px,          // peak drift amplitude in px
    ambientOn:   bg > 0.02,
    ambientDensity: bg,
    crtLevel:    bg,
  };
}

/* ---- the DTRH difficulty model ------------------------------------------- */

/** DTRH's four region bands (game/regions.js), verbatim: each phase starts
 *  HIGHER than the last but eases up within itself, so a class breathes. */
export const PHASE_BANDS = Object.freeze([
  Object.freeze({ start: 0.10, peak: 0.42 }),
  Object.freeze({ start: 0.28, peak: 0.64 }),
  Object.freeze({ start: 0.46, peak: 0.84 }),
  Object.freeze({ start: 0.60, peak: 1.00 }),
]);

/**
 * intensityCurve(progress01, opts) -> 0..1 heat.
 *
 * Called with ONE argument it treats `progress01` as whole-class progress,
 * derives which band it lives in and eases from that band's start to its peak —
 * the sawtooth. Pass `{ phaseIndex, local }` to drive a band explicitly (a game
 * that owns discrete phases, e.g. Deja Vu's attempts), or `{ lift }` for DTRH's
 * endless-lap lift (each lap rides the band hotter, cap 0.42).
 *
 * @param {number} progress01 0..1 progress through the class (ignored when
 *                            opts.phaseIndex + opts.local are supplied)
 * @param {{bands?:Array, phaseIndex?:number, local?:number, lift?:number}} [opts]
 */
export function intensityCurve(progress01, opts = {}) {
  const bands = (opts.bands && opts.bands.length) ? opts.bands : PHASE_BANDS;
  const lift = clamp01(opts.lift || 0) * 0.42;
  let idx;
  let local;
  if (Number.isFinite(opts.phaseIndex)) {
    idx = Math.min(bands.length - 1, Math.max(0, Math.round(opts.phaseIndex)));
    local = clamp01(opts.local == null ? progress01 : opts.local);
  } else {
    const p = clamp01(progress01);
    const span = 1 / bands.length;
    idx = Math.min(bands.length - 1, Math.floor(p / span));
    local = clamp01((p - idx * span) / span);
  }
  const band = bands[idx];
  return clamp01(band.start + lift + (band.peak - band.start) * smoothstep(local));
}

/** Which band index a whole-class progress lands in (helper for HUD/telemetry). */
export function phaseIndexFor(progress01, bands = PHASE_BANDS) {
  const span = 1 / bands.length;
  return Math.min(bands.length - 1, Math.floor(clamp01(progress01) / span));
}

/**
 * PLAIN-SHARE RAMP (DTRH PLAIN_BUBBLE_CHANCE_EARLY .80 -> PLAIN_BUBBLE_CHANCE .30).
 * The share of beats/spawns that must stay effect-free. Effects are RARE at the
 * top, not wall-to-wall. `floor` lets a game raise the busiest end (Impulse
 * Control tier 4 uses .45) — it can never go below 0.
 */
export function plainShare(intensity01, floor = 0.30, early = 0.80) {
  const i = clamp01(intensity01);
  return clamp01(early + (clamp01(floor) - early) * i);
}

/** True when this beat should stay effect-free, given a 0..1 roll. Pure. */
export function isPlainBeat(intensity01, rand, floor, early) {
  return clamp01(rand) < plainShare(intensity01, floor, early);
}

/* ---- one-shot specs (ported from intake render/effects.js) ---------------- */

/** gif/flash burst numbers from a 0..1 intensity. */
export function gifBurstSpec(intensity) {
  const i = clamp01(intensity);
  return {
    count:   2 + Math.round(i * 2),        // 2..4 nodes
    holdMs:  600 + Math.round(i * 600),    // 600..1200
    sizePx:  Math.round(120 + i * 150),    // 120..270 box edge
    enterMs: 200,
    exitMs:  280,
  };
}

/** How many nodes one burst spills at a given heat: a WINDOW rolled inside, so
 *  two bursts at the same heat rarely match (reads as a spill, not a counter). */
export function burstCountForHeat(heat, rand = 0.5) {
  const d = clamp01(heat);
  const lo = 1 + Math.round(d * 4);
  const hi = Math.max(lo, 1 + Math.round(d * 9));
  return Math.min(hi, lo + Math.floor(clamp01(rand) * (hi - lo + 1)));
}

/** Burst opacity ladder by heat (Intake's per-band ladder, mapped to heat). */
export function burstOpacityForHeat(heat) {
  const d = clamp01(heat);
  if (d >= 0.72) return 0.75;
  if (d >= 0.42) return 0.50;
  if (d >= 0.18) return 0.30;
  return 0.15;
}

/** gif_rain window (DTRH gifCascade: ~6s, 1.67 spawns/sec, 2.4-3.8s falls). */
export function gifRainSpec(intensity, durationMult = 1) {
  const i = clamp01(intensity);
  return {
    durationMs: Math.round(6000 * Math.min(3, Math.max(0.5, durationMult))),
    gapMs:      Math.round(1000 / (1.0 + i * 0.9)),   // 1.0..1.9 spawns/sec
    fallMsMin:  2400,
    fallMsMax:  3800,
    max:        NODE_CAPS.gifRain,
  };
}

/** wash hold: DTRH holdOn envelope (opacity 0.25-0.70 / drain 0.35-0.62, 1.5-4.5s). */
export function washSpec(kind, intensity, durationMult = 1) {
  const i = clamp01(intensity);
  const dm = Math.min(10, Math.max(0.1, durationMult));
  const lo = kind === 'braindrain' ? 0.35 : 0.25;
  const hi = kind === 'braindrain' ? 0.62 : 0.70;
  return { alpha: lo + (hi - lo) * i, holdMs: Math.round((1500 + 3000 * i) * dm) };
}

/** sub_flash blip: DTRH 320-560ms, half image / half word. */
export function subFlashSpec(intensity) {
  const i = clamp01(intensity);
  return { durMs: Math.round(320 + 240 * i), alpha: clamp01(0.35 + 0.55 * i) };
}

/** glitch_swap transition: DTRH glitch shudder timing, capped hard at 12s. */
export function glitchSpec(intensity, seconds = 0.6, durationMult = 1) {
  const i = clamp01(intensity);
  const ms = Math.min(12000, Math.max(120, seconds * 1000 * Math.max(0.1, durationMult)));
  return { durMs: Math.round(ms), shudder: clamp01(0.25 + 0.75 * i), midpointMs: Math.round(ms * 0.45) };
}

/** streak meter: hidden under 2, 10 segments, glow ramps to the cap. */
export function streakMeterSpec(streak) {
  const s = Math.max(0, streak | 0);
  return { visible: s >= 2, lit: Math.min(s, 10), segments: 10, glow: clamp01((s - 2) / 8) };
}

/** jackpot ceremony numbers. */
export function jackpotSpec(intensity) {
  const i = clamp01(intensity);
  return {
    dimMs: 250, shimmerMs: 2000,
    bursts: 3 + Math.round(i * 2),
    particlesPerBurst: Math.round(10 + i * 14),
    spotlightMs: Math.round(1500 + i * 500),
  };
}

/** near-miss tease numbers (barely-there by design). */
export function nearMissSpec(intensity) {
  const i = clamp01(intensity);
  return { alpha: 0.05 + i * 0.10, durMs: 400, particles: 3 + Math.round(i * 2) };
}

/** ambient_field density -> node count for a DOM particle layer. */
export function ambientSpec(density, kindCount = 16) {
  const d = clamp01(density);
  if (d <= 0.02) return { on: false, count: 0, alpha: 0 };
  return {
    on: true,
    count: Math.max(1, Math.min(NODE_CAPS.ambient, Math.round(kindCount * (0.25 + 0.75 * d)))),
    alpha: 0.12 + d * 0.28,
  };
}

export default { channelsToVisual, intensityCurve, plainShare };
