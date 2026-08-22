/* ============================================================================
 * games/the-deep-end/schedule.js - THE DEEP END's seeded class plan. PURE.
 *
 * One seeded plan per class: the per-tier dials (heat cap, spawn table, silt,
 * the sub_flash cadence, the row_drift threshold, the shimmer cadence), the
 * exhale threshold for the board size, and the append-only jitter streams the
 * cadences ride so two classes never flash on the same beat but a retake
 * flashes on exactly the beats it did before. No DOM, no clock, no ctx.
 *
 * DIALS FIRST, DIFFICULTY SECOND (GROUND-RULES §6, the contract): tiers 1-3
 * only ever raise effect dials. Tier 4 gets the full dials AND THEN the two
 * classic-difficulty knobs - silt spawns and a leaner low-tier table.
 *
 * SEEDING (Law V): `makeRng(seed + '|de-plan')`. Draw order is append-only:
 * new draws go at the END of buildPlan so older seeds keep their plan.
 *
 * Every number a playtest might touch lives in PLAYTEST below (the Lost &
 * Found precedent): one place, one edit.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { SPAWN_TABLE_BASE, TIER_MAX } from './board.js';

export const PLAYTEST = Object.freeze({
  /** Heat ceiling per grade tier - gradeTier raises the CEILING first. */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1.0 }),
  /** Floor of the heat curve at depthLine 0 (the pool is never still). */
  HEAT_FLOOR: 0.12,
  /** Curve exponent on depthLine (>1 keeps the shallows legible). */
  HEAT_GAMMA: 1.15,

  /** The exhale: occupied cells that trigger it, per board size. */
  EXHALE_FILL: Object.freeze({ 4: 14, 5: 22 }),
  EXHALE_HEAT_MULT: 0.6,
  EXHALE_MS: 10000,

  /** Bell: the last N seconds go gold. */
  BELL_WARN_SEC: 20,

  /** Tier-4 classic difficulty. */
  LEAN_TABLE: Object.freeze([[1, 0.96], [2, 0.04]]),
  SILT_CHANCE: 0.06,
  SILT_MAX: Object.freeze({ 4: 2, 5: 3 }),
  AIRLOCK_CHANCE: 0.05,

  /** sub_flash cadence (ms) by tier at heat 0; heat shortens it to CADENCE_MIN_MULT. */
  SUB_FLASH_MS: Object.freeze({ 1: 0, 2: 9000, 3: 7000, 4: 5200 }),
  /** glitch_swap face-shimmer cadence (ms) by tier. */
  SHIMMER_MS: Object.freeze({ 1: 0, 2: 0, 3: 6500, 4: 4600 }),
  CADENCE_MIN_MULT: 0.45,
  CADENCE_JITTER: 0.35,
  /** Length of each jitter stream dealt up front (a 300s class never runs out). */
  JITTER_LEN: 96,

  /** row_drift arms past this depthLine (tier 3+). */
  ROW_DRIFT_DEPTH: 0.5,

  /** audio_trigger level ceilings by tier (the engine clamps again). */
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),

  /** The move's own tempo (pass 2: 170ms with weight; style.js's slide
   *  transition is tuned to this number - change both). */
  MOVE_MS: 170,
  MOVE_MS_REDUCED: 60,
  /** Pass 2 - THE SLIDE: the trail outlives the slide by this much, the
   *  destination cells blink for LAND_MS, the `slide` cue scales with tiles
   *  moved (1 = BASE + STEP, 4+ = BASE + 4 STEP) and its pitch falls with the
   *  depth line (pitch = 1 - PITCH_DROP x depthLine). */
  SLIDE_TRAIL_MS: 140,
  LAND_MS: 260,
  SLIDE_LV_BASE: 0.14,
  SLIDE_LV_STEP: 0.09,
  SLIDE_PITCH_DROP: 0.45,
  /** Pass 2 - THE WALL: the board shake (class held for BUMP_MS) + the cue. */
  BUMP_MS: 240,
  BUMP_LEVEL: 0.32,
  /** Pass 2 - STUCK: after this stall (board not locked) the cells of every
   *  direction that would move pulse once, for HINT_MS. */
  STUCK_MS: 6000,
  HINT_MS: 1100,
  /** Pass 2 - FACES: past this many tiles wearing an animated face, a new
   *  tier's face is dealt as a still. */
  FACE_CAP: 16,

  /** Pass 3 - THE HAND: the drag distance (px) that leans the tiles all the
   *  way (--de-grab-x/y reach 1 here; style.js multiplies by --de-grab-max). */
  GRAB_PX: 56,
  /** Pass 3 - THE HAND: below this drag (px) the grab has no direction yet -
   *  a press that never travels leans nowhere and lights no arrow. */
  GRAB_DEAD: 6,
  /** Pass 3 - THE QUEUE: one slot, last press wins. A direction pressed while
   *  the MOVE_MS lock holds is remembered instead of eaten; a third press
   *  overwrites the second (the slot never grows past this). 0 turns the queue
   *  off entirely and the lock eats fast presses again, as it did in pass 2. */
  QUEUE_SLOTS: 1,
  /** Pass 3 - THE QUEUE: the queued move fires on the NEXT timer tick after
   *  the lock releases, never synchronously inside the releasing callback. */
  QUEUE_DRAIN_MS: 0,
  DRAIN_MS: 1100,
  DRAIN_MS_REDUCED: 500,
  BRIEF_MS: 1600,
  BRIEF_MS_REDUCED: 900,
  CEREMONY_MS: 2400,
  CEREMONY_MS_REDUCED: 1300,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1400,
  STALL_TICK_MS: 500,
  STRAIN_COOLDOWN_MS: 4000,

  /** Streak meter visible from this chain length; chain links cap here. */
  STREAK_VISIBLE: 2,
  CHAIN_CAP: 8,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }
function sizeOf(size) { return Number(size) === 5 ? 5 : 4; }

/** The board-size setting as the game receives it ('4x4' | '5x5' | 4 | 5). */
export function sizeFromSetting(v) {
  const s = String(v == null ? '' : v).trim();
  if (s === '5x5' || s === '5') return 5;
  return 4;
}

/**
 * THE HEAT CURVE: depthLine (deepestTier / 11) -> heat, capped by the grade
 * tier FIRST. The curve is the game's; the channel vector behind it is the
 * engine's one shared curve (heatToChannels) and is never re-derived here.
 */
export function heatCurve(depthLine, gradeTier) {
  const cap = PLAYTEST.HEAT_CAP[tierOf(gradeTier)];
  const d = clamp01(depthLine);
  const shaped = PLAYTEST.HEAT_FLOOR + (1 - PLAYTEST.HEAT_FLOOR) * Math.pow(d, PLAYTEST.HEAT_GAMMA);
  return clamp01(cap * shaped);
}

export function depthLineFor(deepestTier) {
  return clamp01((Number(deepestTier) || 0) / TIER_MAX);
}

/** Heat-scaled cadence: hotter = sooner, never below CADENCE_MIN_MULT. */
export function cadenceMs(baseMs, heat, jitter01) {
  const base = Number(baseMs) || 0;
  if (base <= 0) return 0;
  const mult = 1 - (1 - PLAYTEST.CADENCE_MIN_MULT) * clamp01(heat);
  const j = 1 + (clamp01(jitter01) * 2 - 1) * PLAYTEST.CADENCE_JITTER;
  return Math.max(400, Math.round(base * mult * j));
}

/**
 * Deal the class plan.
 * @param {Object} o {seed, gradeTier, size:4|5, timeBudgetSec}
 */
export function buildPlan(o = {}) {
  const seed = String(o.seed == null ? '' : o.seed);
  const tier = tierOf(o.gradeTier);
  const n = sizeOf(o.size);
  const rng = makeRng(seed + '|de-plan');
  const budgetSec = Math.max(30, Math.min(600, Number(o.timeBudgetSec) || 300));

  /* ---- draw 1..JITTER_LEN: the sub_flash beat jitter ------------------ */
  const subJitter = [];
  for (let i = 0; i < PLAYTEST.JITTER_LEN; i++) subJitter.push(rng());
  /* ---- draw: the shimmer beat jitter ---------------------------------- */
  const shimmerJitter = [];
  for (let i = 0; i < PLAYTEST.JITTER_LEN; i++) shimmerJitter.push(rng());
  /* ---- draw: sub_flash variant walk (engine variants, no-repeat is theirs) */
  const subVariants = [];
  const pool = ['whisper', 'centre', 'scatter', 'stamp'];
  for (let i = 0; i < PLAYTEST.JITTER_LEN; i++) subVariants.push(pool[Math.floor(rng() * pool.length) % pool.length]);
  /* ---- draw: the reward "current" sweep direction per hit ------------- */
  const currentDir = [];
  for (let i = 0; i < 32; i++) currentDir.push(rng() < 0.5 ? -1 : 1);
  /* (append new draws BELOW this line only) */

  return Object.freeze({
    seed, tier, n, budgetSec,
    heatCap: PLAYTEST.HEAT_CAP[tier],
    audioCeil: PLAYTEST.AUDIO_CEIL[tier],
    exhaleAt: PLAYTEST.EXHALE_FILL[n],
    exhaleHeatMult: PLAYTEST.EXHALE_HEAT_MULT,
    exhaleMs: PLAYTEST.EXHALE_MS,
    bellWarnSec: PLAYTEST.BELL_WARN_SEC,

    /* dials by tier (never difficulty below tier 4) */
    wash: true,
    ambient: true,
    subFlashMs: PLAYTEST.SUB_FLASH_MS[tier],
    rowDrift: tier >= 3,
    rowDriftDepth: PLAYTEST.ROW_DRIFT_DEPTH,
    shimmerMs: PLAYTEST.SHIMMER_MS[tier],

    /* classic difficulty: tier 4 only */
    spawnTable: tier >= 4 ? PLAYTEST.LEAN_TABLE : SPAWN_TABLE_BASE,
    siltChance: tier >= 4 ? PLAYTEST.SILT_CHANCE : 0,
    siltMax: PLAYTEST.SILT_MAX[n],
    airlockChance: tier >= 4 ? PLAYTEST.AIRLOCK_CHANCE : 0,

    /* the seeded streams */
    subJitter, shimmerJitter, subVariants, currentDir,
  });
}

export default buildPlan;
