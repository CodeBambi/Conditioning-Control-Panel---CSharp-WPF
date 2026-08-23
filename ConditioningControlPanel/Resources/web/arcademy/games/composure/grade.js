/* ============================================================================
 * games/composure/grade.js - the COMPOSURE composite, the PLAYTEST dials and
 * the seeded class plan. PURE: no DOM, no clock, no ctx.
 *
 * A game never grades itself. This file turns the class's honest ledger into a
 * 0..1 composite, one declared hard gate and the flavour XP; the SHELL maps
 * that to S/A/B/C through core/grades.js and applies every cap (peek is the
 * shell's, always - CLAUDE.md trap 9).
 *
 * THE COMPOSITE. Composure, not speed:
 *   .45 PROGRESS   how much of the picture came back. Solved = 1. Otherwise a
 *                  blend of distance-closed (manhattan against the scramble's
 *                  own starting distance) and pieces-home, so a player who
 *                  reorganised the board without locking anything still scores
 *                  what they actually did.
 *   .25 PACE       progress against the BASELINE SOLVER's move count (par):
 *                  clamp(progress x par / moves). Solving in par flat = 1;
 *                  solving in twice par = .5; being ahead of the baseline's
 *                  rate part-way through also reads 1. There is no clock term
 *                  anywhere - a 120s class already ends when the bell says so.
 *   .30 CALM       1 minus the panic rate: backtracks (a slide that undoes the
 *                  slide before it) and THRASH (a backtrack made while a wash
 *                  was burying the board) per move. Thrash costs double - the
 *                  whole game is "keep sliding from memory".
 *   x .92 ^ assists   each skill-floor rescue EPISODE taxes the composite. A
 *                  rescue also fails the declared sGate, so the shell caps the
 *                  class at A whatever this number says. Both, deliberately:
 *                  the tax is honest, the gate is the promise.
 *
 * PAR. `parMoves = ceil(baseline x PAR_MULT[tier])`, never below the baseline
 * itself. The baseline comes from solver.js, which is OPTIMAL on 3x3 and a
 * careful-human reduction on 4x4/5x5 - so par is a real number about this
 * scramble, not a table someone guessed. PAR_MULT tightens with the year.
 *
 * ZEN never reaches any of this: `ctx.endClass({zen:true, ...})` short-circuits
 * to 'pass' in core/grades.js (DECISIONS #1).
 *
 * THE PLAN. `buildPlan()` deals the class's effect schedule off the seed
 * (Law V, append-only draw order): the wash windows that bury the board, the
 * sub_flash cadence, the heat ceiling and the audio ceiling. Dials first,
 * difficulty second - tiers 1-3 only raise dials; the board itself grows at
 * tier 3 and the scramble deepens at 3-4.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

/* --------------------------------------------------------------------------
 * THE CONSTANTS BLOCK (playtest-tunable, one place)
 * ----------------------------------------------------------------------- */
export const PLAYTEST = Object.freeze({
  /* ---- the board ---- */
  /** Timed grid by grade tier (the contract: 1-2 -> 3x3, 3-4 -> 4x4). */
  GRID_BY_TIER: Object.freeze({ 1: 3, 2: 3, 3: 4, 4: 4 }),
  /** Scramble depth. >0 = that many seeded legal slides (shallow); 0 = a full
   *  seeded permutation with a parity repair (deep). Classic difficulty, so
   *  it only moves at the top tiers. */
  SCRAMBLE_WALK: Object.freeze({ 1: 40, 2: 90, 3: 0, 4: 0 }),
  /** Zen is always the deep deal - a zen board the player chose is not a test. */
  ZEN_WALK: 0,

  /* ---- par ---- */
  PAR_MULT: Object.freeze({ 1: 2.2, 2: 1.9, 3: 1.7, 4: 1.5 }),
  /** Used only when the solver could not answer (par unknown): moves-per-tile. */
  PAR_FALLBACK_PER_TILE: 7,

  /* ---- the composite ---- */
  W_PROGRESS: 0.45,
  W_PACE: 0.25,
  W_CALM: 0.30,
  /** Inside PROGRESS: distance-closed vs pieces-home. */
  PROGRESS_MH_SHARE: 0.65,
  /** Panic budget: this share of moves may be backtracks before CALM hits 0. */
  CALM_TOL: 0.35,
  BACKTRACK_COST: 1,
  THRASH_COST: 2,
  /** Nobody is judged on a panic rate out of three moves. */
  CALM_MIN_MOVES: 8,
  ASSIST_TAX: 0.92,

  /* ---- flavour XP (game-owned; the XP TABLE itself is C#'s) ---- */
  FLAVOR_SOLVE: 5,
  FLAVOR_BEST: 5,
  FLAVOR_UNDER_PAR: 5,
  FLAVOR_CAP: 15,

  /* ---- heat ---- */
  HEAT_CAP: Object.freeze({ 1: 0.45, 2: 0.65, 3: 0.85, 4: 1.0 }),
  HEAT_FLOOR: 0.12,
  HEAT_GAMMA: 1.1,
  /** Zen breathes: the whole ladder is halved and never reaches the ceiling. */
  ZEN_HEAT_MULT: 0.5,

  /* ---- audio ---- */
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),

  /* ---- the washes (the class's own effect, CORE-fired) ---- */
  WASH_COUNT: Object.freeze({ 1: 1, 2: 3, 3: 5, 4: 7 }),
  WASH_MS: Object.freeze({ 1: 2600, 2: 3400, 3: 4200, 4: 5000 }),
  WASH_ALPHA: Object.freeze({ 1: 0.18, 2: 0.28, 3: 0.38, 4: 0.5 }),
  /** DELIBERATELY NOT 'spiral'. The engine keeps ONE wash element PER VARIANT
   *  and pressure.js holds the spiral one with `sustainForever`; a LOWER-alpha
   *  re-trigger of that variant is the step-down idiom and would END the deck's
   *  held wheel (CLAUDE.md trap 33). The class's own burial uses the three
   *  variants no deck holds. */
  WASH_VARIANTS: Object.freeze(['pink', 'drain', 'sublim']),
  /** The whisper-out: the last slice of a wash is re-triggered at this share of
   *  its alpha. NEVER stop('wash') mid-class (CLAUDE.md trap 33). */
  WASH_STEPDOWN: 0.35,
  WASH_STEPDOWN_SHARE: 0.35,
  /** The first wash never lands before the player has had the board a while. */
  WASH_FIRST_MS: 12000,
  WASH_TAIL_MS: 8000,
  ZEN_WASH_COUNT: 2,
  ZEN_WASH_ALPHA: 0.16,
  ZEN_WASH_MS: 4000,

  /* ---- the other dials ---- */
  SUB_FLASH_MS: Object.freeze({ 1: 0, 2: 0, 3: 7000, 4: 5200 }),
  SUB_VARIANTS: Object.freeze(['whisper', 'centre', 'scatter']),
  CADENCE_JITTER: 0.35,
  CADENCE_MIN_MULT: 0.5,
  JITTER_LEN: 48,
  /** bubble_field arms from this tier, row_drift (on the static floor) from this one. */
  BUBBLE_TIER: 3,
  ROW_DRIFT_TIER: 4,

  /* ---- THE SKILL-FLOOR RESCUE (the critic's top fix, and it is law) ---- */
  /** No new piece home for this long -> light the baseline's next move. */
  RESCUE_MS: 20000,
  RESCUE_TICK_MS: 500,
  /** A lit hint stays lit this long if the player does nothing with it. */
  RESCUE_HOLD_MS: 6000,
  /** Zen never nags: the rescue is silent there (no gate to fail either). */
  RESCUE_IN_ZEN: false,

  /* ---- tempo ---- */
  MOVE_MS: 150,
  MOVE_MS_REDUCED: 60,
  QUEUE_SLOTS: 1,
  QUEUE_DRAIN_MS: 0,
  SWIPE_PX: 24,
  LOCK_STREAK_CAP: 8,
  STALL_TICK_MS: 500,
  BELL_WARN_SEC: 20,
  BRIEF_MS: 1600,
  BRIEF_MS_REDUCED: 900,
  /** The solve: seams dissolve and the clip plays clean before the end card. */
  SOLVE_PLAY_MS: 3000,
  SOLVE_PLAY_MS_REDUCED: 1500,
  CEREMONY_MS: 2400,
  CEREMONY_MS_REDUCED: 1300,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1400,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
export function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/** The timed board for a year. Zen's board is the player's own setting. */
export function gridForTier(gradeTier) { return PLAYTEST.GRID_BY_TIER[tierOf(gradeTier)] || 3; }

/** Seeded slides for a tier's scramble; 0 = full permutation + parity repair. */
export function scrambleWalkFor(gradeTier, zen) {
  return zen ? PLAYTEST.ZEN_WALK : (PLAYTEST.SCRAMBLE_WALK[tierOf(gradeTier)] || 0);
}

/**
 * Par, from the solver's own baseline.
 * @param {number} baseline  solver.baselineLength(state), or <0 when unknown
 * @param {number} gradeTier
 * @param {number} n
 */
export function parFor(baseline, gradeTier, n) {
  const tier = tierOf(gradeTier);
  const size = Math.max(3, Math.min(5, Math.round(Number(n) || 3)));
  const base = Number(baseline);
  if (!Number.isFinite(base) || base <= 0) {
    return Math.max(1, Math.round((size * size - 1) * PLAYTEST.PAR_FALLBACK_PER_TILE));
  }
  return Math.max(Math.round(base), Math.ceil(base * PLAYTEST.PAR_MULT[tier]));
}

/**
 * The composite.
 * @param {Object} i
 *   gradeTier      1..4
 *   solved         the picture came back whole
 *   n              board size
 *   moves          real slides (a press into a wall is not a move)
 *   par            parFor(...)
 *   manhattanStart / manhattanNow   the scramble's distance, then and now
 *   locked / tiles                  pieces home, out of n*n-1
 *   backtracks     slides that undid the slide before them
 *   thrash         those of them made while a wash was up
 *   assists        rescue EPISODES (not hint repaints)
 * @returns {{composite:number, terms:Object, tax:number, raw:number, par:number}}
 */
export function compositeFor(i = {}) {
  const solved = !!i.solved;
  const moves = Math.max(0, Math.round(Number(i.moves) || 0));
  const par = Math.max(1, Math.round(Number(i.par) || 1));
  const tiles = Math.max(1, Math.round(Number(i.tiles) || 1));
  const locked = Math.max(0, Math.min(tiles, Math.round(Number(i.locked) || 0)));
  const mhStart = Math.max(0, Number(i.manhattanStart) || 0);
  const mhNow = Math.max(0, Number(i.manhattanNow) || 0);

  const closed = mhStart > 0 ? clamp01((mhStart - mhNow) / mhStart) : (solved ? 1 : 0);
  const home = clamp01(locked / tiles);
  const progress = solved
    ? 1
    : clamp01(PLAYTEST.PROGRESS_MH_SHARE * closed + (1 - PLAYTEST.PROGRESS_MH_SHARE) * home);

  const pace = moves > 0 ? clamp01((progress * par) / moves) : 0;

  const backtracks = Math.max(0, Math.round(Number(i.backtracks) || 0));
  const thrash = Math.max(0, Math.round(Number(i.thrash) || 0));
  const denom = Math.max(PLAYTEST.CALM_MIN_MOVES, moves);
  const panic = (PLAYTEST.BACKTRACK_COST * backtracks + PLAYTEST.THRASH_COST * thrash) / denom;
  const calm = clamp01(1 - panic / PLAYTEST.CALM_TOL);

  const raw = PLAYTEST.W_PROGRESS * progress + PLAYTEST.W_PACE * pace + PLAYTEST.W_CALM * calm;
  const assists = Math.max(0, Math.round(Number(i.assists) || 0));
  const tax = Math.pow(PLAYTEST.ASSIST_TAX, assists);

  return {
    composite: clamp01(raw * tax),
    raw: clamp01(raw),
    tax,
    par,
    terms: { progress, pace, calm, closed, home },
  };
}

/**
 * Declared hard gates. `sGate` is the rescue promise: a class that took the
 * skill-floor assist may not exceed A, whatever the composite says. Nothing
 * else in this game gates - the board size is the year's, not a choice.
 */
export function hardGates(rescueUsed) {
  return { sGate: !rescueUsed };
}

/** +5 solved, +5 a new personal best on this board, +5 inside par. Cap 15. */
export function flavorXp(i = {}) {
  if (!i.solved) return 0;
  let xp = PLAYTEST.FLAVOR_SOLVE;
  const before = Number(i.bestMovesBefore);
  const moves = Math.max(1, Math.round(Number(i.moves) || 1));
  if (!Number.isFinite(before) || before <= 0 || moves < before) xp += PLAYTEST.FLAVOR_BEST;
  if (moves <= Math.max(1, Math.round(Number(i.par) || 1))) xp += PLAYTEST.FLAVOR_UNDER_PAR;
  return Math.min(PLAYTEST.FLAVOR_CAP, xp);
}

/** The heat ladder: pieces home, curved, capped by the year (and halved in zen). */
export function heatFor(lockedFrac, gradeTier, zen) {
  const tier = tierOf(gradeTier);
  const cap = PLAYTEST.HEAT_CAP[tier] * (zen ? PLAYTEST.ZEN_HEAT_MULT : 1);
  const f = clamp01(lockedFrac);
  const h = PLAYTEST.HEAT_FLOOR + (cap - PLAYTEST.HEAT_FLOOR) * Math.pow(f, PLAYTEST.HEAT_GAMMA);
  return clamp01(Math.min(cap, h));
}

/** Heat-shortened, jittered cadence (the Deep End's shape). */
export function cadenceMs(baseMs, heat, jitter) {
  const base = Math.max(0, Number(baseMs) || 0);
  if (base <= 0) return 0;
  const h = clamp01(heat);
  const mult = 1 - (1 - PLAYTEST.CADENCE_MIN_MULT) * h;
  const j = 1 + (Number(jitter) || 0);
  return Math.max(600, Math.round(base * mult * j));
}

/**
 * The class's seeded plan. Draw order is APPEND-ONLY: a new draw goes at the
 * END so older seeds keep the show they had.
 *
 * @param {Object} o {seed, gradeTier, n, zen, timeBudgetSec}
 */
export function buildPlan(o = {}) {
  const tier = tierOf(o.gradeTier);
  const zen = !!o.zen;
  const seed = String(o.seed == null ? 'composure' : o.seed);
  const rng = makeRng(seed + '|cp-plan');
  const budgetMs = Math.max(20000, (Number(o.timeBudgetSec) || 120) * 1000);

  /* 1. the wash windows - the burial the whole game is about. Dealt across the
   *    budget between a lead-in and a tail so no wash lands on the bell. */
  const count = zen ? PLAYTEST.ZEN_WASH_COUNT : (PLAYTEST.WASH_COUNT[tier] || 1);
  const washMs = zen ? PLAYTEST.ZEN_WASH_MS : (PLAYTEST.WASH_MS[tier] || 2600);
  const alpha = zen ? PLAYTEST.ZEN_WASH_ALPHA : (PLAYTEST.WASH_ALPHA[tier] || 0.2);
  const first = PLAYTEST.WASH_FIRST_MS;
  const span = Math.max(1000, budgetMs - first - PLAYTEST.WASH_TAIL_MS);
  const washes = [];
  for (let k = 0; k < count; k++) {
    const slot = count > 1 ? (k / count) : 0;
    const jitter = (rng() - 0.5) * (span / Math.max(1, count)) * 0.6;
    washes.push({
      atMs: Math.max(first, Math.round(first + slot * span + jitter)),
      ms: Math.round(washMs * (0.85 + rng() * 0.3)),
      alpha,
      variant: PLAYTEST.WASH_VARIANTS[Math.floor(rng() * PLAYTEST.WASH_VARIANTS.length)],
    });
  }
  washes.sort((a, b) => a.atMs - b.atMs);

  /* 2. the sub_flash cadence (tier 3+, never in zen) + its jitter stream */
  const subFlashMs = zen ? 0 : (PLAYTEST.SUB_FLASH_MS[tier] || 0);
  const subJitter = [];
  for (let k = 0; k < PLAYTEST.JITTER_LEN; k++) subJitter.push((rng() - 0.5) * 2 * PLAYTEST.CADENCE_JITTER);
  const subVariants = [];
  for (let k = 0; k < 8; k++) subVariants.push(PLAYTEST.SUB_VARIANTS[Math.floor(rng() * PLAYTEST.SUB_VARIANTS.length)]);

  return Object.freeze({
    tier,
    zen,
    washes: Object.freeze(washes),
    washStepdown: PLAYTEST.WASH_STEPDOWN,
    washStepdownShare: PLAYTEST.WASH_STEPDOWN_SHARE,
    subFlashMs,
    subJitter: Object.freeze(subJitter),
    subVariants: Object.freeze(subVariants),
    bubbles: !zen && tier >= PLAYTEST.BUBBLE_TIER,
    rowDrift: !zen && tier >= PLAYTEST.ROW_DRIFT_TIER,
    heatCap: PLAYTEST.HEAT_CAP[tier] * (zen ? PLAYTEST.ZEN_HEAT_MULT : 1),
    audioCeil: PLAYTEST.AUDIO_CEIL[tier] * (zen ? 0.7 : 1),
    bellWarnSec: PLAYTEST.BELL_WARN_SEC,
  });
}

export default {
  PLAYTEST, compositeFor, hardGates, flavorXp, parFor, heatFor, cadenceMs, buildPlan,
  gridForTier, scrambleWalkFor, clamp01, tierOf,
};
