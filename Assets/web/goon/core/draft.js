// Draft agreement tables + the deterministic endurance ramp — port of Services/GoonGame/GoonDraft.cs.
//
// REDESIGN (2026-08-03). The draft is no longer "pick three things YOU endure":
//   1. Each player toggles which elements they ALLOW. The effective pool is the INTERSECTION of
//      both allowed sets (see sharedPool) — a mutual agreement, not two private loadouts.
//   2. The ramp is ROLLED from that shared pool with the shared match seed, so BOTH players get
//      the same elements at the same instants. buildRamp is a pure function of
//      (pool, matchSeed, liveDurationSec) — nothing about it depends on who is asking.
//   3. Bubbles are an ALWAYS-ON baseline: never toggled, never rolled, always emitted from t=0 to
//      the end of the match with an intensity that climbs 0.15 -> 1.00. Its enum code is
//      unchanged and frozen; it is simply not part of the toggle/roll set any more.
//
// Every number here is a transcription of the C# balance table: the two clients build the ramp
// independently from the same combined seed, so any drift in a constant, a rounding step, or the
// ORDER/COUNT of rng draws is a desync. Integer maths is used wherever a fraction would otherwise
// have to round identically in two languages.
//
// This file only PLANS cues. Nothing here starts an element — match.js raises them as events.

import { GoonElement, GoonConsts } from './contracts.js';
import { GoonRng, saltSeed } from './rng.js';

/** What a planned cue asks the executor to do. Local-only; never on the wire. */
export const GoonCueAction = Object.freeze({
  Start: 0,      // begin the element (durationMs > 0 for segments, 0 = sustained until Stop)
  Intensity: 1,  // ramp update
  Stop: 2,
});

/**
 * LEGACY. The pre-agreement draft size. Nothing in the engine enforces it any more; it is kept
 * exported because ui/soloDriver.js and older tooling still import it.
 */
export const PICKS_PER_PLAYER = 3;

/** Fewest elements a player may allow, and the smallest workable intersection. */
export const MIN_ALLOWED_ELEMENTS = 2;

/** Highest achievable summed risk tier (BrainDrain + two tier-2s). Unchanged: the score clamp. */
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

/**
 * The one element that is never toggled and never rolled: it runs for the whole match, for both
 * players, and its intensity IS the match clock. (The Bubbles PAYLOAD — the throwable swarm — is
 * a different thing entirely and is untouched by this.)
 */
export const ALWAYS_ON_ELEMENT = GoonElement.Bubbles;

/** Always-on band: barely there at the whistle, unmissable at the end. */
const ALWAYS_ON_INTENSITY_START = 0.15;
const ALWAYS_ON_INTENSITY_END = 1.00;

/** What the agreement screen may toggle: the pool minus the always-on element. */
export const TogglePool = Object.freeze(PoolV1.filter((e) => e !== ALWAYS_ON_ELEMENT));

/** Sustained elements get an Intensity refresh cue on this cadence. */
const SUSTAINED_RAMP_STEP_MS = 30000;

/** Segments shorter than this are dropped rather than squeezed against the end of the match. */
const MIN_USEFUL_BURST_MS = 8000;

// ---------------------------------------------------------------- rolled-ramp tuning
// All integer milliseconds on purpose: `passes`, `stride` and `segLen` must come out bit-identical
// in C# and JS, and integer division is the only arithmetic that trivially does.

/** How much wall time one element-slot wants. passes is chosen to land near this. */
const ROLL_TARGET_STRIDE_MS = 30000;
/** A segment covers this many slots, i.e. how many elements overlap on average. */
const ROLL_SEGMENT_OVERLAP = 2;
const ROLL_MIN_STRIDE_MS = 6000;
const ROLL_MIN_SEGMENT_MS = 12000;
const ROLL_MAX_SEGMENT_MS = 120000;
/** Slot start jitter, percent of a stride, +-. Integer percent so both languages agree. */
const ROLL_JITTER_PCT = 35;
/** Hard ceiling on rolled segments — a 60-minute match with a 2-element pool must still terminate. */
const ROLL_MAX_SEGMENTS = 512;

/**
 * Reserved salt code for the ramp roll's sub-stream. Deliberately outside GoonElement so the roll
 * can never collide with a per-element stream (and so adding element 9 never perturbs it).
 */
const RAMP_ROLL_SALT_CODE = 1000;

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

// Risk tiers and intensity bands are UNCHANGED by the redesign (ui/strings.js carries a duplicate
// of the risk table that selftest-hud cross-checks against riskTierOf — do not drift it).
// entryFraction now orders the OPENING pass instead of gating a single entry instant; the numbers
// keep their meaning (0.00 = happy to lead, 0.35 = a closer).
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
  // earlier and tops out lower, so a spiral+drain pool escalates in two visible steps.
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

/** Summed risk tier of a pool — the "riskTier" in the score formula. Duplicates count once. */
export function matchRiskTier(draft) {
  if (!draft) return 0;
  let total = 0;
  for (const e of new Set(draft)) total += riskTierOf(e);
  return clamp(total, 0, MAX_MATCH_RISK_TIER);
}

/** Score multiplier contributed by the pool: 1 + step x tier. */
export function riskMultiplier(tier) {
  return 1.0 + GoonConsts.DraftRiskStep * clamp(tier, 0, MAX_MATCH_RISK_TIER);
}

// ------------------------------------------------------------------ the agreement

/**
 * Canonical form of an allowed set: distinct, in the v1 pool, always-on element removed, sorted
 * ASCENDING. Canonical because both engines must derive the same pool from the same two sets with
 * no ordering to agree on.
 */
export function normalizeAllowed(allowed) {
  const seen = new Set();
  const out = [];
  for (const raw of allowed || []) {
    const e = Number(raw);
    if (!Number.isInteger(e)) continue;
    if (e === ALWAYS_ON_ELEMENT) continue;      // never toggled, never rolled
    if (!PoolV1.includes(e)) continue;
    if (seen.has(e)) continue;
    seen.add(e);
    out.push(e);
  }
  out.sort((a, b) => a - b);
  return out;
}

/** Default: everything this pairing can actually run is ON. */
export function defaultAllowed(available) {
  return normalizeAllowed(available && available.length ? available : PoolV1);
}

/** The effective pool: what BOTH players allow. */
export function sharedPool(mine, theirs) {
  const a = normalizeAllowed(mine);
  const b = new Set(normalizeAllowed(theirs));
  return a.filter((e) => b.has(e));
}

/** A player may not confirm with fewer than MIN_ALLOWED_ELEMENTS on. */
export function isValidAllowed(allowed) {
  const n = normalizeAllowed(allowed);
  if (n.length < MIN_ALLOWED_ELEMENTS) {
    return { ok: false, error: `keep at least ${MIN_ALLOWED_ELEMENTS} effects switched on` };
  }
  return { ok: true, error: '' };
}

/** ...and the two of you have to leave at least that many in common. */
export function isValidSharedPool(pool) {
  const n = normalizeAllowed(pool);
  if (n.length < MIN_ALLOWED_ELEMENTS) {
    return {
      ok: false,
      error: `you two only agree on ${n.length} effect${n.length === 1 ? '' : 's'} - open one more up`,
    };
  }
  return { ok: true, error: '' };
}

/**
 * LEGACY validity check for the old three-pick draft. Retained so nothing that still calls it
 * breaks; the engine no longer uses it.
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

// ------------------------------------------------------------------ the ramp

function pushSegment(cues, element, startMs, endMs, iStart, iEnd, liveMs, sustained) {
  // Intensity is a pure function of GLOBAL match progress, so every element escalates toward the
  // end no matter which slots the roll gave it.
  const at = (t) => lerp(iStart, iEnd, liveMs > 0 ? t / liveMs : 1.0);

  cues.push({
    offsetMs: startMs,
    action: GoonCueAction.Start,
    element,
    intensity: at(startMs),
    durationMs: sustained ? 0 : (endMs - startMs),
  });

  for (let t = startMs + SUSTAINED_RAMP_STEP_MS; t < endMs; t += SUSTAINED_RAMP_STEP_MS) {
    cues.push({ offsetMs: t, action: GoonCueAction.Intensity, element, intensity: at(t), durationMs: 0 });
  }

  cues.push({ offsetMs: endMs, action: GoonCueAction.Stop, element, intensity: 0, durationMs: 0 });
}

/**
 * The roll. Consumes the rng in a FIXED order — (K-1) nextInt draws per pass for the shuffle, then
 * exactly one nextDouble per slot for the jitter, whether or not the slot survives the clamp. Any
 * change to that order or count is a desync, not a tuning tweak.
 */
function rollSegments(cues, roll, matchSeed, liveMs, factory) {
  const k = roll.length;
  const rng = factory(saltSeed(matchSeed, RAMP_ROLL_SALT_CODE));

  const target = k * ROLL_TARGET_STRIDE_MS;
  let passes = Math.max(1, Math.floor((liveMs + Math.floor(target / 2)) / target));
  if (passes * k > ROLL_MAX_SEGMENTS) passes = Math.max(1, Math.floor(ROLL_MAX_SEGMENTS / k));

  const stride = Math.max(ROLL_MIN_STRIDE_MS, Math.floor(liveMs / (passes * k)));
  const jitterMax = Math.floor((stride * ROLL_JITTER_PCT) / 100);

  let segLen = clamp(stride * ROLL_SEGMENT_OVERLAP, ROLL_MIN_SEGMENT_MS, ROLL_MAX_SEGMENT_MS);
  // Two segments of the SAME element are at least `separation` slots apart (see the pass-boundary
  // swap below); keep them from overlapping each other, because a Stop landing inside the next
  // Start would silently kill the element early and eat the time it was owed.
  const separation = k >= 2 ? 2 : 1;
  const sameElementCap = separation * stride - 2 * jitterMax - 1000;
  if (segLen > sameElementCap) segLen = sameElementCap;
  if (segLen < MIN_USEFUL_BURST_MS) segLen = MIN_USEFUL_BURST_MS;

  let lastOfPrevPass = -1;
  for (let p = 0; p < passes; p++) {
    const pass = roll.slice();
    // Fisher-Yates, identical to GoonRng.shuffle — inlined because rngFactory only promises
    // nextInt/nextDouble.
    for (let i = pass.length - 1; i > 0; i--) {
      const j = rng.nextInt(0, i + 1);
      const tmp = pass[i]; pass[i] = pass[j]; pass[j] = tmp;
    }
    // Opening pass only: a closer must not open the match. Stable sort by entryFraction, so ties
    // keep the order the roll gave them. (C# uses LINQ OrderBy, which is also stable.)
    if (p === 0) pass.sort((a, b) => profileOf(a).entryFraction - profileOf(b).entryFraction);
    // Each element appears once per pass, but a pass BOUNDARY can hand it two adjacent slots.
    // One deterministic swap (no rng draw) guarantees the two-slot separation the cap assumes.
    if (k >= 2 && pass[0] === lastOfPrevPass) {
      const tmp = pass[0]; pass[0] = pass[1]; pass[1] = tmp;
    }
    lastOfPrevPass = pass[k - 1];

    for (let i = 0; i < k; i++) {
      const slot = p * k + i;
      const jitter = Math.trunc((rng.nextDouble() * 2 - 1) * jitterMax);

      let start = slot * stride + jitter;
      if (start > liveMs - MIN_USEFUL_BURST_MS) start = liveMs - MIN_USEFUL_BURST_MS;
      if (start < 0) start = 0;

      let end = start + segLen;
      if (end > liveMs) end = liveMs;
      if (end - start < MIN_USEFUL_BURST_MS) continue;

      const prof = profileOf(pass[i]);
      pushSegment(cues, pass[i], start, end, prof.intensityStart, prof.intensityEnd, liveMs, false);
    }
  }
}

/** Total order. Stop before Intensity before Start at one instant; element/duration break the rest. */
function compareCues(a, b) {
  if (a.offsetMs !== b.offsetMs) return a.offsetMs - b.offsetMs;
  if (a.action !== b.action) return b.action - a.action;
  if (a.element !== b.element) return a.element - b.element;
  return a.durationMs - b.durationMs;
}

/**
 * Plans the whole Live-phase ramp up front from the SHARED pool and the combined match seed. The
 * result is identical on both machines by construction — nothing here reads host/guest, the local
 * player, or anything but its three arguments.
 *
 * NOTE: C# BuildRamp takes liveDurationSec (SECONDS), and so does this. Cues carry offsetMs.
 *
 * @param {number[]} pool          shared allowed elements (order irrelevant; normalized here)
 * @param {bigint} matchSeed       combined seed
 * @param {number} liveDurationSec live phase length in SECONDS
 * @param {(seed:bigint)=>object} [rngFactory] seed -> {nextInt,nextDouble}; defaults to GoonRng
 * @returns {Array<{offsetMs:number, action:number, element:number, intensity:number, durationMs:number}>}
 */
export function buildRamp(pool, matchSeed, liveDurationSec, rngFactory) {
  const cues = [];
  if (!(liveDurationSec > 0)) return cues;
  const factory = typeof rngFactory === 'function' ? rngFactory : (seed) => new GoonRng(seed);

  const liveMs = Math.trunc(liveDurationSec) * 1000;

  // 1. The always-on baseline. Not rolled, not toggleable, not optional: t=0 -> end, 0.15 -> 1.00.
  pushSegment(cues, ALWAYS_ON_ELEMENT, 0, liveMs,
    ALWAYS_ON_INTENSITY_START, ALWAYS_ON_INTENSITY_END, liveMs, true);

  // 2. The rolled schedule over the shared pool.
  const roll = normalizeAllowed(pool);
  if (roll.length > 0) rollSegments(cues, roll, matchSeed, liveMs, factory);

  cues.sort(compareCues);
  return cues;
}
