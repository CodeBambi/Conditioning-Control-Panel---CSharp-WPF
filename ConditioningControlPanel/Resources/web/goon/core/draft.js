// Draft pool tables + the deterministic endurance ramp — port of Services/GoonGame/GoonDraft.cs.
//
// The draft defines what YOU endure. Every number here is a transcription of the C# balance
// table: the two clients build their ramps independently from the same combined seed, so any
// drift in a constant, a rounding step, or the ORDER of rng draws is a desync.
//
// This file only PLANS cues. Nothing here starts an element — match.js raises them as events.

import { GoonElement, GoonConsts } from './contracts.js';
import { GoonRng, saltSeed } from './rng.js';

/** What a planned cue asks the executor to do. Local-only; never on the wire. */
export const GoonCueAction = Object.freeze({
  Start: 0,      // begin the element (durationMs > 0 for bursts, 0 = sustained until Stop)
  Intensity: 1,  // sustained element only: ramp update
  Stop: 2,
});

export const PICKS_PER_PLAYER = 3;

/** Highest achievable summed risk tier for a legal 3-element draft (BrainDrain + two tier-2s). */
export const MAX_MATCH_RISK_TIER = 7;

/** Rotating pool, v1 = the whole enum. */
export const PoolV1 = Object.freeze([
  GoonElement.Flashes,
  GoonElement.Videos,
  GoonElement.Subliminals,
  GoonElement.Bubbles,
  GoonElement.LockCards,
  GoonElement.ToyPatterns,
  GoonElement.BrainDrain,
  GoonElement.BouncingText,
  GoonElement.Spiral,
]);

/** Sustained elements get an Intensity refresh cue on this cadence. */
const SUSTAINED_RAMP_STEP_MS = 30000;

/** Bursts shorter than this are dropped rather than squeezed against the end of the match. */
const MIN_USEFUL_BURST_MS = 8000;

function profile(o) {
  return Object.freeze({
    element: o.element,
    riskTier: o.riskTier,
    sustained: !!o.sustained,
    entryFraction: o.entryFraction ?? 0,
    minDurationMs: o.minDurationMs ?? 0,
    maxDurationMs: o.maxDurationMs ?? 0,
    earlyGapMs: o.earlyGapMs ?? 0,
    lateGapMs: o.lateGapMs ?? 0,
    intensityStart: o.intensityStart ?? 0,
    intensityEnd: o.intensityEnd ?? 0,
  });
}

const PROFILES = Object.freeze({
  [GoonElement.Flashes]: profile({
    element: GoonElement.Flashes, riskTier: 0, sustained: true,
    entryFraction: 0.00, intensityStart: 0.35, intensityEnd: 0.90,
  }),
  [GoonElement.BouncingText]: profile({
    element: GoonElement.BouncingText, riskTier: 0, sustained: true,
    entryFraction: 0.10, intensityStart: 0.30, intensityEnd: 0.80,
  }),
  [GoonElement.Subliminals]: profile({
    element: GoonElement.Subliminals, riskTier: 1, sustained: true,
    entryFraction: 0.05, intensityStart: 0.40, intensityEnd: 1.00,
  }),
  [GoonElement.Bubbles]: profile({
    element: GoonElement.Bubbles, riskTier: 1, sustained: false,
    entryFraction: 0.12, minDurationMs: 45000, maxDurationMs: 90000,
    earlyGapMs: 210000, lateGapMs: 90000, intensityStart: 0.35, intensityEnd: 0.85,
  }),
  [GoonElement.Videos]: profile({
    element: GoonElement.Videos, riskTier: 2, sustained: false,
    entryFraction: 0.15, minDurationMs: 60000, maxDurationMs: 120000,
    earlyGapMs: 240000, lateGapMs: 120000, intensityStart: 0.50, intensityEnd: 1.00,
  }),
  [GoonElement.LockCards]: profile({
    element: GoonElement.LockCards, riskTier: 2, sustained: false,
    entryFraction: 0.20, minDurationMs: 30000, maxDurationMs: 60000,
    earlyGapMs: 300000, lateGapMs: 150000, intensityStart: 0.40, intensityEnd: 0.90,
  }),
  [GoonElement.ToyPatterns]: profile({
    element: GoonElement.ToyPatterns, riskTier: 2, sustained: false,
    entryFraction: 0.05, minDurationMs: 30000, maxDurationMs: 75000,
    earlyGapMs: 180000, lateGapMs: 75000, intensityStart: 0.30, intensityEnd: 0.85,
  }),
  [GoonElement.BrainDrain]: profile({
    element: GoonElement.BrainDrain, riskTier: 3, sustained: true,
    entryFraction: 0.35, intensityStart: 0.25, intensityEnd: 0.75,
  }),
  // Modelled on BrainDrain — the same sustained shape — but it opens a tenth of the match
  // earlier and tops out lower, so a spiral+drain draft escalates in two visible steps.
  [GoonElement.Spiral]: profile({
    element: GoonElement.Spiral, riskTier: 2, sustained: true,
    entryFraction: 0.25, intensityStart: 0.20, intensityEnd: 0.65,
  }),
});

function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }
function lerp(a, b, t) { return a + (b - a) * clamp(t, 0.0, 1.0); }

/** Unknown element -> the C# fallback profile, never a throw. */
export function profileOf(element) {
  const p = PROFILES[element];
  return p || profile({
    element, riskTier: 1, sustained: true,
    entryFraction: 0.1, intensityStart: 0.3, intensityEnd: 0.8,
  });
}

export function riskTierOf(element) { return profileOf(element).riskTier; }

/** Summed risk tier of a draft — the "riskTier" in the score formula. Duplicates count once. */
export function matchRiskTier(draft) {
  if (!draft) return 0;
  let total = 0;
  for (const e of new Set(draft)) total += riskTierOf(e);
  return clamp(total, 0, MAX_MATCH_RISK_TIER);
}

/** Score multiplier contributed by the draft: 1 + step x tier. */
export function riskMultiplier(tier) {
  return 1.0 + GoonConsts.DraftRiskStep * clamp(tier, 0, MAX_MATCH_RISK_TIER);
}

/**
 * Exactly PICKS_PER_PLAYER distinct elements from the pool.
 * C# `IsValidDraft(picks, out error)` -> {ok, error}.
 */
export function isValidDraft(picks) {
  if (!picks || picks.length !== PICKS_PER_PLAYER) {
    return { ok: false, error: `draft must contain exactly ${PICKS_PER_PLAYER} elements` };
  }
  if (new Set(picks).size !== picks.length) return { ok: false, error: 'draft contains duplicates' };
  for (const p of picks) {
    if (!PoolV1.includes(p)) return { ok: false, error: `element ${p} is not in the v1 pool` };
  }
  return { ok: true, error: '' };
}

/**
 * Plans the whole Live-phase ramp up front from the combined match seed. Each element is salted
 * by its own id (NOT by host/guest), so both players endure the same shape for a shared pick.
 *
 * NOTE: C# BuildRamp takes liveDurationSec (SECONDS), and so does this. Cues carry offsetMs.
 * NOTE: C# List.Sort is UNSTABLE for equal keys; JS Array.prototype.sort is stable. Two cues can
 * only tie when they share an offset AND an action, i.e. different elements — the parity vector
 * agrees with insertion (draft) order, so stability is the safer of the two behaviours here.
 *
 * @param {number[]} draft         element codes
 * @param {bigint} matchSeed       combined seed
 * @param {number} liveDurationSec live phase length in SECONDS
 * @param {(seed:bigint)=>object} [rngFactory] seed -> {nextInt,nextDouble}; defaults to GoonRng
 * @returns {Array<{offsetMs:number, action:number, element:number, intensity:number, durationMs:number}>}
 */
export function buildRamp(draft, matchSeed, liveDurationSec, rngFactory) {
  const cues = [];
  if (!draft || draft.length === 0 || !(liveDurationSec > 0)) return cues;
  const factory = typeof rngFactory === 'function' ? rngFactory : (seed) => new GoonRng(seed);

  const liveMs = Math.trunc(liveDurationSec) * 1000;

  for (const element of new Set(draft)) {
    const p = profileOf(element);
    const rng = factory(saltSeed(matchSeed, element));
    const entry = Math.trunc(liveMs * clamp(p.entryFraction, 0.0, 0.9));

    if (p.sustained) {
      cues.push({ offsetMs: entry, action: GoonCueAction.Start, element, intensity: p.intensityStart, durationMs: 0 });

      for (let t = entry + SUSTAINED_RAMP_STEP_MS; t < liveMs; t += SUSTAINED_RAMP_STEP_MS) {
        const frac = liveMs > entry ? (t - entry) / (liveMs - entry) : 1.0;
        cues.push({
          offsetMs: t,
          action: GoonCueAction.Intensity,
          element,
          intensity: lerp(p.intensityStart, p.intensityEnd, frac),
          durationMs: 0,
        });
      }

      cues.push({ offsetMs: liveMs, action: GoonCueAction.Stop, element, intensity: 0, durationMs: 0 });
      continue;
    }

    // Burst element: shrinking gaps + rising intensity as the match progresses.
    let cursor = entry;
    let guard = 0;
    while (cursor < liveMs && guard++ < 512) {
      const frac = clamp(cursor / liveMs, 0.0, 1.0);
      let duration = p.maxDurationMs > p.minDurationMs
        ? rng.nextInt(p.minDurationMs, p.maxDurationMs + 1)
        : p.minDurationMs;

      if (cursor + duration > liveMs) duration = liveMs - cursor;
      if (duration < MIN_USEFUL_BURST_MS) break;   // NOTE: breaks BEFORE the jitter draw

      const intensity = lerp(p.intensityStart, p.intensityEnd, frac);

      cues.push({ offsetMs: cursor, action: GoonCueAction.Start, element, intensity, durationMs: duration });
      cues.push({ offsetMs: cursor + duration, action: GoonCueAction.Stop, element, intensity: 0, durationMs: 0 });

      const baseGap = lerp(p.earlyGapMs, p.lateGapMs, frac);
      const jitter = 0.75 + rng.nextDouble() * 0.5;              // +-25 %
      cursor += duration + Math.trunc(Math.max(5000, baseGap * jitter));
    }
  }

  // Stop before Start at the same instant, so a re-trigger reads cleanly downstream.
  cues.sort((a, b) => (a.offsetMs - b.offsetMs) || (b.action - a.action));
  return cues;
}
