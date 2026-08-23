/* ============================================================================
 * games/anomaly/rounds.js - ANOMALY's seeded round plan. PURE.
 *
 * No DOM, no clock, no ctx. One class seed in, the whole show out: the grid
 * size, every round's anomaly KIND and DELTA MAGNITUDE, where the odd tile
 * starts, how long the round is, the glitch_swap RELOCATION schedule (when it
 * moves and, in preference order, where to), the drift dial and the ambience
 * cadences. Same seed = same show; a retake replays it exactly (Law V).
 *
 * DRAW ORDER IS APPEND-ONLY. New draws go at the END of buildPlan (and at the
 * end of a round's own block) so an older seed keeps the plan it had.
 *
 * ---------------------------------------------------------------------------
 * THE CRITIC'S TOP FIX IS LAW (dossier, 7/10): the top tiers get harder through
 * RELOCATIONS, DRIFT and DECOY PRESSURE, never through a delta approaching
 * invisibility. Every magnitude in DELTA stays at or above PERCEPT_FLOOR for
 * its kind at every tier - the scratch suite asserts that table cell by cell.
 * A tier-4 hue shift is still 28 degrees; what changed is that the tile now
 * moves twice under a glitch while the rows drift and the wash tints the lot.
 *
 * PERCEPTIBILITY, WHERE THE FLOORS COME FROM (all conservative, i.e. well
 * above the literature's just-noticeable difference for a ~120px tile):
 *   hue     22 deg   (JND for a saturated patch is ~2-5 deg; 22 is obvious)
 *   mirror  binary   (the floor kind: survives reduced motion AND colourblind)
 *   scale   0.09     (9% linear = ~19% area)
 *   rotate  5.5 deg  (a tilted frame edge reads at ~2 deg)
 *   blur    1.0 px
 *   bright  0.13     (13% luminance step)
 *   speed   0.30     (playbackRate ratio delta - video kinds only)
 *   frame   0.30 s   (currentTime offset - video kinds only)
 *
 * VIDEO KINDS (speed / frame) are dealt ONLY when the grid is <= 3 wide
 * (VIDEO_GRID_MAX) - the L&F 30Hz lock: N playing <video> elements pin the
 * page's compositor. index.js additionally requires the round's url to BE a
 * video (mediaEl semantics) before it uses the round's `altKind`; the img-safe
 * `kind` is always dealt and is always a valid answer, so a gif-only or
 * still-only pool simply never sees a speed round.
 *
 * NEVER-EMPTY FLOOR (`an_kinds`): 'gentle' narrows to hue/mirror/scale and
 * reduced motion narrows to hue/mirror; if any intersection came out empty the
 * pool falls back to [FLOOR_KIND] = mirror. The pool can never be empty (the
 * DTRH runVariantsOff contract, quoted by the dossier).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

/* ------------------------------------------------------------------ dials */
export const PLAYTEST = Object.freeze({
  /** Grid width by grade tier (3x3 -> 5x5). Dossier: 4x4 arrives at tier 2. */
  GRID_BY_TIER: Object.freeze({ 1: 3, 2: 4, 3: 5, 4: 5 }),
  /** Portability: a coarse pointer caps the grid at 4x4 (64px minimum tile). */
  GRID_MAX_COARSE: 4,
  /** playbackRate / currentTime kinds need <video>; N videos lock the page to
   *  30Hz (the L&F trap), so they are dealt only at or below this grid width. */
  VIDEO_GRID_MAX: 3,

  /** Round length window (ms) by tier - the 6-12s quick-fire cadence. */
  ROUND_MS: Object.freeze({
    1: Object.freeze([10000, 12000]),
    2: Object.freeze([8500, 11000]),
    3: Object.freeze([7000, 9500]),
    4: Object.freeze([6000, 8000]),
  }),
  /** Reduced motion holds a static frame: give the eye a little longer. */
  ROUND_MS_REDUCED_MULT: 1.15,

  /** glitch_swap relocations per round by tier. TIER 4 CAPS AT 2 (ruling 1). */
  RELOC_BY_TIER: Object.freeze({ 1: 0, 2: 0, 3: 1, 4: 2 }),
  RELOC_CAP: 2,
  RELOC_FIRST_MS: 1800,        // never before the player has looked once
  RELOC_TAIL_MS: 1500,         // never inside the last moment of the round
  RELOC_MIN_GAP_MS: 1400,
  RELOC_JITTER_MS: 350,

  /** A wrong tap burns round time; the ghost-outline near-miss refunds. */
  WRONG_BURN_MS: 800,
  GHOST_REFUND_MS: 1000,

  /** The correct-first-tap ceremony is under half a second, by contract. */
  ADVANCE_MS: 380,
  ADVANCE_MS_REDUCED: 220,
  /** A whiffed round shows the answer for this long before the next grid. */
  WHIFF_HOLD_MS: 1100,
  WHIFF_HOLD_MS_REDUCED: 600,

  BRIEF_MS: 1600,
  BRIEF_MS_REDUCED: 700,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1200,
  CEREMONY_MS: 900,
  CEREMONY_MS_REDUCED: 450,
  /** The bell's last seconds go gold. */
  BELL_WARN_SEC: 15,

  /** Heat = the streak ladder, capped by the grade tier (DE/IC pattern). */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1 }),
  HEAT_FLOOR: 0.12,
  HEAT_STEP: 0.09,
  /** audio_trigger level ceilings by tier (the engine clamps again). */
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
  /** The chime ratchet: +1 semitone per streak link, capped (the IC ladder). */
  PITCH_CAP_SEMITONES: 7,

  /** sub_flash cadence (ms) by tier at heat 0; heat shortens it. */
  SUB_FLASH_MS: Object.freeze({ 1: 0, 2: 8000, 3: 6000, 4: 4500 }),
  CADENCE_MIN_MULT: 0.45,
  CADENCE_JITTER: 0.35,
  JITTER_LEN: 48,

  /** Ambience arms at these tiers. */
  WASH_TIER: 2,               // short wash pulses
  WASH_HOLD_TIER: 4,          // tier 4 holds a low-alpha wash instead
  BUBBLE_TIER: 2,             // bubble_field decoys
  DRIFT_TIER: 2,              // row_drift under the cursor
  DRIFT: Object.freeze({
    2: Object.freeze({ amplitudeMult: 0.35, variant: 'sway', speedMult: 0.8 }),
    3: Object.freeze({ amplitudeMult: 0.55, variant: 'sway', speedMult: 1 }),
    4: Object.freeze({ amplitudeMult: 0.7, variant: 'slide', speedMult: 1.15 }),
  }),

  /** Comeback hook: two whiffed rounds in a row force ONE breather round. */
  BREATHER_AFTER_WHIFFS: 2,
  /** The grid frame ignites at this streak. */
  STREAK_LIT: 5,
  /** A find under this is "fast" (and pays flavour XP). */
  SUBSECOND_MS: 1000,

  STALL_TICK_MS: 700,
  /** Rounds dealt = what the budget can hold, padded, then capped. */
  COUNT_PAD: 4,
  COUNT_MAX: 24,
  COUNT_MIN: 8,
});

/* ------------------------------------------------------------------ kinds */
/** Kinds that work on an <img> (a gif or a still) - always dealable. */
export const IMG_KINDS = Object.freeze(['hue', 'mirror', 'scale', 'rotate', 'blur', 'bright']);
/** Kinds that need a <video> element (mediaEl semantics) AND a small grid. */
export const VIDEO_KINDS = Object.freeze(['speed', 'frame']);
/** `an_kinds: 'gentle'` - the comfort pool (no tilt, no blur, no light). */
export const GENTLE_KINDS = Object.freeze(['hue', 'mirror', 'scale']);
/** Reduced motion: static-frame rounds, hue/mirror only (dossier). */
export const REDUCED_KINDS = Object.freeze(['hue', 'mirror']);
/** The never-empty floor: it survives reduced motion and colourblindness. */
export const FLOOR_KIND = 'mirror';

/** Perceptibility floors. No DELTA cell may sit below its kind's floor. */
export const PERCEPT_FLOOR = Object.freeze({
  hue: 22, mirror: 1, scale: 0.09, rotate: 5.5, blur: 1, bright: 0.13, speed: 0.3, frame: 0.3,
});

/**
 * Delta MAGNITUDE by kind and grade tier. Units:
 *   hue     degrees of hue-rotate
 *   mirror  binary (1 = scaleX(-1))
 *   scale   linear offset from 1 (0.12 = 1.12x or 1/1.12)
 *   rotate  degrees
 *   blur    pixels (additive only - there is no negative blur)
 *   bright  offset from 1
 *   speed   playbackRate offset from 1
 *   frame   currentTime offset in seconds
 * The ladder narrows with the tier but NEVER below PERCEPT_FLOOR - that is the
 * critic's top fix expressed as data.
 */
export const DELTA = Object.freeze({
  hue: Object.freeze({ 1: 64, 2: 48, 3: 34, 4: 28 }),
  mirror: Object.freeze({ 1: 1, 2: 1, 3: 1, 4: 1 }),
  scale: Object.freeze({ 1: 0.22, 2: 0.18, 3: 0.14, 4: 0.12 }),
  rotate: Object.freeze({ 1: 14, 2: 11, 3: 8.5, 4: 7 }),
  blur: Object.freeze({ 1: 2.6, 2: 2.1, 3: 1.6, 4: 1.35 }),
  bright: Object.freeze({ 1: 0.34, 2: 0.28, 3: 0.22, 4: 0.18 }),
  speed: Object.freeze({ 1: 0.7, 2: 0.55, 3: 0.45, 4: 0.38 }),
  frame: Object.freeze({ 1: 0.9, 2: 0.7, 3: 0.55, 4: 0.45 }),
});

/* --------------------------------------------------------------- helpers */
export function clampTier(v) { return Math.max(1, Math.min(4, Math.round(Number(v) || 1))); }
export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(a, b, t) { return a + (b - a) * t; }

/** Grid width for a tier; a coarse pointer caps it (portability, dossier). */
export function gridFor(gradeTier, coarse) {
  const n = PLAYTEST.GRID_BY_TIER[clampTier(gradeTier)] || 3;
  return coarse ? Math.min(PLAYTEST.GRID_MAX_COARSE, n) : n;
}

/**
 * The img-safe kind pool for this class. NEVER EMPTY (FLOOR_KIND is the floor).
 * `mode` is the `an_kinds` setting ('all' | 'gentle'); anything else = 'all'.
 */
export function kindPool({ mode, reduced } = {}) {
  const gentle = String(mode || 'all').toLowerCase() === 'gentle';
  let pool = (gentle ? GENTLE_KINDS : IMG_KINDS).slice();
  if (reduced) pool = pool.filter((k) => REDUCED_KINDS.indexOf(k) >= 0);
  if (!pool.length) pool = [FLOOR_KIND];
  return pool;
}

/** The video kind pool - empty unless the grid is small and motion is allowed. */
export function videoKindPool({ mode, reduced, n } = {}) {
  if (reduced) return [];
  if (String(mode || 'all').toLowerCase() === 'gentle') return [];
  if (!(Number(n) > 0) || Number(n) > PLAYTEST.VIDEO_GRID_MAX) return [];
  return VIDEO_KINDS.slice();
}

/** The magnitude for a kind at a tier, floored at PERCEPT_FLOOR. */
export function deltaFor(kind, gradeTier) {
  const row = DELTA[kind];
  if (!row) return 0;
  const raw = Number(row[clampTier(gradeTier)]) || 0;
  const floor = Number(PERCEPT_FLOOR[kind]) || 0;
  return Math.max(floor, raw);
}

/** The baseline face style EVERY tile wears - same shape, identity values, so
 *  the odd tile is not the only element with a filter (and therefore not the
 *  only one with its own render surface: `filter:none` rasterises differently,
 *  which would be a tell in itself). index.js writes these inline. */
export const BASE_FACE = Object.freeze({
  filter: 'hue-rotate(0deg) blur(0px) brightness(1)',
  transform: 'rotate(0deg) scale(1) scaleX(1)',
  rate: 1,
  offset: 0,
});

/**
 * The odd tile's inline style for a kind/delta/direction. PURE - returns the
 * same four fields BASE_FACE carries so the caller never branches.
 *   dir  +1 | -1 (which way the delta leans; blur is additive either way)
 */
export function faceStyle(kind, delta, dir) {
  const d = Math.abs(Number(delta) || 0);
  const s = Number(dir) < 0 ? -1 : 1;
  let hue = 0; let blur = 0; let bright = 1;
  let rot = 0; let scale = 1; let mirror = 1;
  let rate = 1; let offset = 0;
  switch (kind) {
    case 'hue': hue = s * d; break;
    case 'mirror': mirror = -1; break;
    case 'scale': scale = s > 0 ? (1 + d) : (1 / (1 + d)); break;
    case 'rotate': rot = s * d; break;
    case 'blur': blur = d; break;
    case 'bright': bright = s > 0 ? (1 + d) : Math.max(0.05, 1 - d); break;
    case 'speed': rate = s > 0 ? (1 + d) : (1 / (1 + d)); break;
    case 'frame': offset = d; break;
    default: break;
  }
  return {
    filter: 'hue-rotate(' + round2(hue) + 'deg) blur(' + round2(blur) + 'px) brightness(' + round3(bright) + ')',
    transform: 'rotate(' + round2(rot) + 'deg) scale(' + round3(scale) + ') scaleX(' + mirror + ')',
    rate: round3(rate),
    offset: round3(offset),
  };
}
function round2(v) { return Math.round(Number(v) * 100) / 100; }
function round3(v) { return Math.round(Number(v) * 1000) / 1000; }

/** Heat from the class's own ladder: the streak, capped by the grade tier. */
export function heatFor(streak, gradeTier) {
  const tier = clampTier(gradeTier);
  const cap = PLAYTEST.HEAT_CAP[tier];
  const h = PLAYTEST.HEAT_FLOOR + Math.max(0, Number(streak) || 0) * PLAYTEST.HEAT_STEP;
  return Math.max(0, Math.min(cap, h));
}

/** Heat-shortened, seed-jittered cadence (the DE schedule.js helper). */
export function cadenceMs(baseMs, heat, jitter) {
  const base = Math.max(0, Number(baseMs) || 0);
  if (!base) return 0;
  const h = clamp01(heat);
  const shortened = base * lerp(1, PLAYTEST.CADENCE_MIN_MULT, h);
  const j = 1 + (Number(jitter) || 0) * PLAYTEST.CADENCE_JITTER;
  return Math.max(600, Math.round(shortened * j));
}

/** The chime pitch for a streak: +1 semitone per link, capped. */
export function pitchFor(streak) {
  const n = Math.max(0, Math.min(PLAYTEST.PITCH_CAP_SEMITONES, Math.round(Number(streak) || 0)));
  return Math.round(Math.pow(2, n / 12) * 1000) / 1000;
}

/** Seeded Fisher-Yates over 0..cells-1 (the relocation preference order). */
function shuffledIndices(cells, rng) {
  const a = [];
  for (let i = 0; i < cells; i++) a.push(i);
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.floor(rng() * (i + 1)));
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

/** Relocation times inside a round: k slots, spaced, jittered, clamped. */
function relocTimes(k, durationMs, rng) {
  const out = [];
  if (k <= 0) return out;
  const first = PLAYTEST.RELOC_FIRST_MS;
  const last = Math.max(first, durationMs - PLAYTEST.RELOC_TAIL_MS);
  if (last <= first) return out;
  for (let i = 0; i < k; i++) {
    const t = lerp(first, last, (i + 1) / (k + 1));
    const jitter = (rng() * 2 - 1) * PLAYTEST.RELOC_JITTER_MS;
    let at = Math.round(t + jitter);
    if (at < first) at = first;
    if (at > last) at = last;
    if (out.length && at - out[out.length - 1] < PLAYTEST.RELOC_MIN_GAP_MS) {
      at = out[out.length - 1] + PLAYTEST.RELOC_MIN_GAP_MS;
    }
    if (at > last) break;
    out.push(at);
  }
  return out;
}

/**
 * THE PLAN.
 * @param {Object} o
 *   seed           the class seed (Law V)
 *   gradeTier      1..4
 *   timeBudgetSec  the class bell (90 by contract)
 *   kindsMode      'all' | 'gentle'   (the `an_kinds` setting)
 *   reduced        reducedMotion
 *   coarse         coarse pointer (caps the grid)
 * @returns a frozen plan; `rounds` is a plain array of frozen rounds.
 */
export function buildPlan(o = {}) {
  const tier = clampTier(o.gradeTier);
  const reduced = !!o.reduced;
  const mode = String(o.kindsMode || 'all').toLowerCase() === 'gentle' ? 'gentle' : 'all';
  const n = gridFor(tier, !!o.coarse);
  const cells = n * n;
  const seed = String(o.seed == null ? 'anomaly' : o.seed);
  const rng = makeRng(seed + '|an-plan');

  const kinds = kindPool({ mode, reduced });
  const videoKinds = videoKindPool({ mode, reduced, n });

  const win = PLAYTEST.ROUND_MS[tier];
  const lo = Math.round(win[0] * (reduced ? PLAYTEST.ROUND_MS_REDUCED_MULT : 1));
  const hi = Math.round(win[1] * (reduced ? PLAYTEST.ROUND_MS_REDUCED_MULT : 1));

  const budgetMs = Math.max(20000, (Number(o.timeBudgetSec) || 90) * 1000);
  const count = Math.max(
    PLAYTEST.COUNT_MIN,
    Math.min(PLAYTEST.COUNT_MAX, Math.ceil(budgetMs / lo) + PLAYTEST.COUNT_PAD),
  );

  /* relocations are OFF under reduced motion (no glitch, no drift) */
  const relocPerRound = reduced ? 0 : Math.min(PLAYTEST.RELOC_CAP, PLAYTEST.RELOC_BY_TIER[tier] || 0);

  const rounds = [];
  let lastKind = '';
  let lastOdd = -1;
  for (let i = 0; i < count; i++) {
    /* --- append-only draw order inside a round ------------------------- */
    let kind = pickNoRepeat(kinds, lastKind, rng);
    lastKind = kind;
    const dir = rng() < 0.5 ? -1 : 1;
    let oddIndex = Math.floor(rng() * cells);
    if (cells > 1 && oddIndex === lastOdd) oddIndex = (oddIndex + 1 + Math.floor(rng() * (cells - 1))) % cells;
    lastOdd = oddIndex;
    const durationMs = Math.round(lerp(lo, hi, rng()));
    const times = relocTimes(relocPerRound, durationMs, rng);
    const order = shuffledIndices(cells, rng);
    /* the video alternative: dealt only when the pool exists; index.js uses it
     * ONLY when this round's url really is a <video> (mediaEl semantics). */
    const altKind = videoKinds.length ? videoKinds[Math.floor(rng() * videoKinds.length)] : null;
    const altDir = rng() < 0.5 ? -1 : 1;

    rounds.push(Object.freeze({
      i,
      n,
      cells,
      kind,
      delta: deltaFor(kind, tier),
      dir,
      altKind,
      altDelta: altKind ? deltaFor(altKind, tier) : 0,
      altDir,
      oddIndex,
      durationMs,
      relocations: Object.freeze(times.map((at, k) => Object.freeze({
        at,
        /* preference order: index.js takes the first entry that is neither the
         * current odd tile nor an eliminated tile - a plan cannot know which
         * tiles the player has burned, and a relocation onto a dead tile would
         * be a ledger lie (Law I). */
        order: Object.freeze(rotate(order, k + 1)),
      }))),
      breather: false,
    }));
  }

  const drift = (!reduced && tier >= PLAYTEST.DRIFT_TIER) ? PLAYTEST.DRIFT[tier] || null : null;
  const subJitter = [];
  for (let i = 0; i < PLAYTEST.JITTER_LEN; i++) subJitter.push(rng() * 2 - 1);

  return Object.freeze({
    seed,
    tier,
    n,
    cells,
    reduced,
    coarse: !!o.coarse,
    kindsMode: mode,
    kinds: Object.freeze(kinds.slice()),
    videoKinds: Object.freeze(videoKinds.slice()),
    count,
    rounds,
    relocPerRound,
    budgetMs,
    heatCap: PLAYTEST.HEAT_CAP[tier],
    audioCeil: PLAYTEST.AUDIO_CEIL[tier],
    subFlashMs: reduced ? 0 : (PLAYTEST.SUB_FLASH_MS[tier] || 0),
    subJitter: Object.freeze(subJitter),
    wash: !reduced && tier >= PLAYTEST.WASH_TIER,
    washHold: !reduced && tier >= PLAYTEST.WASH_HOLD_TIER,
    bubbles: !reduced && tier >= PLAYTEST.BUBBLE_TIER,
    drift,
  });
}

/** Rotate an array by k - a per-relocation preference order off ONE shuffle. */
function rotate(list, k) {
  const a = list.slice();
  const s = ((k % a.length) + a.length) % a.length;
  return a.slice(s).concat(a.slice(0, s));
}

/** No-repeat-last pick (the engine's variant-pool law, applied to kinds). */
function pickNoRepeat(pool, last, rng) {
  if (!pool.length) return FLOOR_KIND;
  if (pool.length === 1) return pool[0];
  const usable = pool.filter((k) => k !== last);
  const list = usable.length ? usable : pool;
  return list[Math.min(list.length - 1, Math.floor(rng() * list.length))];
}

/**
 * THE BREATHER (the dossier's comeback hook): two whiffed rounds in a row and
 * the next round is dealt from the gentle pool at TIER-1 magnitude with no
 * relocations. PURE - the caller decides when, this decides what.
 */
export function asBreather(round, plan) {
  if (!round) return round;
  const pool = kindPool({ mode: 'gentle', reduced: plan && plan.reduced });
  const kind = pool.indexOf(round.kind) >= 0 ? round.kind : pool[0];
  return Object.freeze(Object.assign({}, round, {
    kind,
    delta: deltaFor(kind, 1),
    altKind: null,
    altDelta: 0,
    relocations: Object.freeze([]),
    durationMs: Math.round(round.durationMs * 1.2),
    breather: true,
  }));
}

/**
 * Which relocation target to use, given the plan's preference order and what
 * the runtime knows. PURE. Returns -1 when nothing is eligible (the caller
 * then simply does not relocate - a swap that cannot land is not a swap).
 */
export function relocationTarget(order, currentOdd, eliminated) {
  const dead = eliminated instanceof Set ? eliminated : new Set(eliminated || []);
  for (const i of (order || [])) {
    if (i === currentOdd) continue;
    if (dead.has(i)) continue;
    return i;
  }
  return -1;
}

export default {
  PLAYTEST, IMG_KINDS, VIDEO_KINDS, GENTLE_KINDS, REDUCED_KINDS, FLOOR_KIND,
  PERCEPT_FLOOR, DELTA, BASE_FACE,
  clampTier, clamp01, gridFor, kindPool, videoKindPool, deltaFor, faceStyle,
  heatFor, cadenceMs, pitchFor, buildPlan, asBreather, relocationTarget,
};
