/* ============================================================================
 * games/composure/grade.js - the COMPOSURE composite, the PLAYTEST dials and
 * the seeded class plan. PURE: no DOM, no clock, no ctx.
 *
 * A game never grades itself. This file turns the class's honest ledger into a
 * 0..1 composite, one declared hard gate and the flavour XP; the SHELL maps
 * that to S/A/B/C through core/grades.js and applies every cap (peek is the
 * shell's, always - CLAUDE.md trap 9).
 *
 * THE CLASS IS MULTI-BOARD (owner ruling 2026-08-24, the class-length wave).
 * A solve BANKS the picture and deals a fresh scramble; the BELL is the one
 * thing that ends a timed class. So the rubric no longer asks "did the one
 * board come back" - it asks "how many pictures did you put back in five
 * minutes", normalised per tier so the S bar means the same thing at every
 * year. See EXPECTED BOARDS below for the gate arithmetic.
 *
 * THE COMPOSITE. Composure, not speed:
 *   .45 PROGRESS   how many BOARDS' worth of picture came back, against what
 *                  this tier is expected to fill the bell with:
 *                    boardsDone = banked + (the live board's own progress)
 *                    progress   = clamp01(boardsDone / expectedBoards)
 *                  The live board's own progress is the old blend of
 *                  distance-closed (manhattan against the scramble's own
 *                  starting distance) and pieces-home, so a player who
 *                  reorganised the board without locking anything still
 *                  scores what they actually did. A board already BANKED is
 *                  worth zero as a live board - counting both would pay for
 *                  the same picture twice.
 *   .25 PACE       moves against the BASELINE SOLVER's move count (par):
 *                  clamp(parEarned / moves), where parEarned is every banked
 *                  board's own par plus the live board's par pro-rated by how
 *                  much of it came back. Identical in meaning to the
 *                  single-board rubric it replaces: solving in par flat = 1,
 *                  in twice par = .5, and being ahead of the baseline's rate
 *                  part-way through also reads 1. There is no clock term
 *                  anywhere - the class already ends when the bell says so.
 *   .30 CALM       1 minus the panic rate: backtracks (a slide that undoes the
 *                  slide before it) and THRASH (a backtrack made while a wash
 *                  was burying the board) per move. Thrash costs double - the
 *                  whole game is "keep sliding from memory". CLASS-cumulative:
 *                  the panic rate is one number for the whole five minutes.
 *   x .92 ^ assists   each skill-floor rescue EPISODE taxes the composite. A
 *                  rescue also fails the declared sGate, so the shell caps the
 *                  class at A whatever this number says. Both, deliberately:
 *                  the tax is honest, the gate is the promise. The tax has a
 *                  FLOOR now (ASSIST_TAX_FLOOR): a 300s class can stack twice
 *                  the episodes a 120s one could, and the tax must not quietly
 *                  become a second gate harsher than the one we declared.
 *
 * EXPECTED BOARDS, and the gate arithmetic. `expectedBoardsFor(tier, budget)`
 * is `round(budgetSec / TYPICAL_SOLVE_SEC[tier])`, so it is derived from the
 * class's own length rather than a table pinned to one budget. At the shipped
 * 300s budget that is 7 / 5 / 3 / 2 boards for years 1-4.
 *
 * With pace and calm both perfect (.25 + .30 = .55) the letter thresholds in
 * core/grades.js (S .92, A .75) become pure progress requirements:
 *     S needs .45 x progress >= .37  ->  progress >= .8222
 *     A needs .45 x progress >= .20  ->  progress >= .4444
 * which is `boardsDone >= .8222 x expected` and `>= .4444 x expected`:
 *
 *     tier  expected   S at boardsDone   A at boardsDone
 *      1       7           5.76             3.11
 *      2       5           4.11             2.22
 *      3       3           2.47             1.33
 *      4       2           1.64             0.89
 *
 * So the old "one clean 40s solve = S" at tier 1 is now SIX clean solves (or
 * five plus three quarters of a sixth), and a year-4 S is two whole 4x4
 * pictures. Anything short of perfect pace or calm raises those bars, which is
 * the point: the bar is boards AND composure, never boards alone.
 *
 * THE BELL IS NOT A FAIL. There is no timeout gate in this file and there may
 * never be one: a timed class ALWAYS ends on the bell now, so "the clock ran
 * out" is the ordinary ending. A player who banked nothing simply scores what
 * their one live board was worth.
 *
 * PAR. `parMoves = ceil(baseline x PAR_MULT[tier])`, never below the baseline
 * itself. The baseline comes from solver.js, which is OPTIMAL on 3x3 and a
 * careful-human reduction on 4x4/5x5 - so par is a real number about this
 * scramble, not a table someone guessed. PAR_MULT tightens with the year, and
 * every board of the class gets its own par (the solver runs on every deal).
 *
 * ZEN never reaches any of this: `ctx.endClass({zen:true, ...})` short-circuits
 * to 'pass' in core/grades.js (DECISIONS #1). Zen banks and re-deals exactly
 * the same way - it just has no bell and no letter.
 *
 * THE PLAN. `buildPlan()` deals the class's effect schedule off the seed
 * (Law V, append-only draw order): the wash windows that bury the board, the
 * sub_flash cadence, the heat ceiling and the audio ceiling. Dials first,
 * difficulty second - tiers 1-3 only raise dials; the board itself grows at
 * tier 3 and the scramble deepens at 3-4 (and, gently, board by board at
 * tiers 1-2 - see SCRAMBLE_WALK_STEP).
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
  /** THE GENTLE ESCALATION (multi-board). Board 1 is the tier's own walk; each
   *  board after it adds this many slides, capped at SCRAMBLE_WALK_MAX. It is
   *  a pure function of the BOARD INDEX, so it is as seeded as the deal it
   *  feeds - a retake replays the same seven boards at the same seven depths.
   *  Tiers 3-4 are absent on purpose: they already deal a full permutation, so
   *  there is nothing left to deepen (scrambleWalkFor short-circuits on 0). */
  SCRAMBLE_WALK_STEP: Object.freeze({ 1: 8, 2: 12 }),
  SCRAMBLE_WALK_MAX: Object.freeze({ 1: 80, 2: 150 }),
  /** Zen is always the deep deal - a zen board the player chose is not a test. */
  ZEN_WALK: 0,

  /* ---- how many boards a class is expected to hold (the S/A normaliser) --- */
  /** Seconds a competent player of that year takes over ONE board, measured
   *  against the tier's own grid and scramble depth (3x3 shallow / 3x3 deep /
   *  4x4 permutation / 4x4 permutation under the heaviest dials). The gate is
   *  `round(budgetSec / this)`, so it tracks the class length instead of
   *  hard-coding 300s: at 300s that is 7 / 5 / 3 / 2 boards. It is deliberately
   *  a touch generous at tiers 1-2, where SCRAMBLE_WALK_STEP makes the later
   *  boards of a class slower than the first. */
  TYPICAL_SOLVE_SEC: Object.freeze({ 1: 42, 2: 58, 3: 100, 4: 140 }),
  /** A sanity ceiling, not a dial: nothing this class deals should ever want
   *  more than a dozen boards' worth of picture out of one bell. */
  EXPECTED_BOARDS_MAX: 12,

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
  /** THE TAX FLOOR. .92^7 = .558, so this is "about seven episodes and then it
   *  stops biting". A 300s class gives a stalling player roughly twice the
   *  chances to trip the 20s rescue that a 120s one did, and the DECLARED gate
   *  (sGate, an A-cap) is what we promised - the tax is flavour on top of it
   *  and must never quietly outrank it. */
  ASSIST_TAX_FLOOR: 0.55,

  /* ---- flavour XP (game-owned; the XP TABLE itself is C#'s) ---- */
  /** +5 for banking at least one picture, +5 for a new personal best on this
   *  board size, +5 for having taken any ONE board inside its own par. Cap 15,
   *  exactly as it was - a five-minute class does not pay five times over. */
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
  /** THE COUNT IS A DENSITY, not a count. This table was tasted at a 120s
   *  class (WASH_BASE_SEC), so buildPlan scales it by budget/120 and the
   *  washes-per-minute a year gets stays what it was: .5 / 1.5 / 2.5 / 3.5.
   *  At the shipped 300s budget that is 3 / 8 / 13 / 18 windows. Left
   *  unscaled, a year-4 class would have gone from a wash every 17s to one
   *  every 40s and the whole "the room buries the board" premise would have
   *  thinned out at exactly the tier that needs it most. */
  WASH_COUNT: Object.freeze({ 1: 1, 2: 3, 3: 5, 4: 7 }),
  WASH_BASE_SEC: 120,
  /** The cap, and it is an INVARIANT rather than a taste: no more than this
   *  share of the class may be under a wash. tier 4 at 300s wants 18 x 5000ms
   *  = 90s of 300s = 30%, so this never binds at the shipped dials - it exists
   *  so a future budget or a fatter WASH_MS cannot bury a whole class. */
  WASH_LOAD_MAX: 0.34,
  /** A hard ceiling on top of the load rule, for the same reason. */
  WASH_COUNT_MAX: 24,
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
  /** No new piece home for this long -> light the baseline's next move. It is
   *  a rate per STUCK EPISODE, not per class, so it does not scale with the
   *  budget - but its bookkeeping is PER BOARD: a fresh deal restarts the
   *  stall clock, because being twenty seconds into a brand new scramble is
   *  not being stuck. */
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
  /** THE BANK BEAT. A solve dissolves the seams and the clip plays clean -
   *  and then the next scramble deals, because a solve no longer ends the
   *  class. Trimmed from 3000/1500 for exactly that reason: this beat is paid
   *  once per SOLVE now, so at tier 1 it runs six or seven times inside one
   *  bell and three seconds of it each time was ~7% of the class. */
  SOLVE_PLAY_MS: 1800,
  SOLVE_PLAY_MS_REDUCED: 900,
  /** ...and then the fresh board settles in. Input stays closed for both. */
  DEAL_MS: 700,
  DEAL_MS_REDUCED: 300,
  CEREMONY_MS: 2400,
  CEREMONY_MS_REDUCED: 1300,
  END_HOLD_MS: 2600,
  END_HOLD_MS_REDUCED: 1400,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
export function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/** The timed board for a year. Zen's board is the player's own setting. */
export function gridForTier(gradeTier) { return PLAYTEST.GRID_BY_TIER[tierOf(gradeTier)] || 3; }

/**
 * Seeded slides for a tier's scramble; 0 = full permutation + parity repair.
 *
 * `boardIndex` is 0-based and is THE gentle escalation: board 1 of a class is
 * exactly the walk this tier always dealt, and each board after it adds
 * SCRAMBLE_WALK_STEP, capped at SCRAMBLE_WALK_MAX. Tiers 3-4 return 0 either
 * way - a full permutation cannot be deepened.
 *
 * @param {number} gradeTier
 * @param {boolean} zen
 * @param {number=} boardIndex  0 = the class's first board
 */
export function scrambleWalkFor(gradeTier, zen, boardIndex) {
  if (zen) return PLAYTEST.ZEN_WALK;
  const tier = tierOf(gradeTier);
  const base = PLAYTEST.SCRAMBLE_WALK[tier] || 0;
  if (base <= 0) return 0;
  const i = Math.max(0, Math.round(Number(boardIndex) || 0));
  if (i === 0) return base;
  const step = PLAYTEST.SCRAMBLE_WALK_STEP[tier] || 0;
  const max = PLAYTEST.SCRAMBLE_WALK_MAX[tier] || base;
  return Math.max(base, Math.min(max, base + step * i));
}

/**
 * How many whole boards this year is expected to fill the bell with - the
 * normaliser the PROGRESS term divides by, and therefore the whole S/A gate.
 * Derived from the class's own budget so a length change moves the bar with
 * it rather than silently making S free (or impossible).
 *
 * @param {number} gradeTier
 * @param {number} timeBudgetSec   the class's budget, in seconds
 * @returns {number} >= 1
 */
export function expectedBoardsFor(gradeTier, timeBudgetSec) {
  const tier = tierOf(gradeTier);
  const sec = Math.max(20, Number(timeBudgetSec) || 0);
  const typical = Math.max(5, PLAYTEST.TYPICAL_SOLVE_SEC[tier] || 60);
  return Math.max(1, Math.min(PLAYTEST.EXPECTED_BOARDS_MAX, Math.round(sec / typical)));
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
 * The composite, over a WHOLE multi-board class.
 *
 * @param {Object} i
 *   gradeTier      1..4
 *   banked         pictures finished and banked this class
 *   expectedBoards expectedBoardsFor(tier, budgetSec) - the S/A normaliser
 *   boardSolved    the LIVE board was whole at the ending (the bell caught the
 *                  bank beat). Its whole is already inside `banked`, so this
 *                  only tells us to score the live board at zero, never twice.
 *   n              board size
 *   moves          real slides across the CLASS (a press into a wall is not one)
 *   par            parFor(...) for the LIVE board
 *   parBanked      the sum of every banked board's own par
 *   manhattanStart / manhattanNow   the LIVE scramble's distance, then and now
 *   locked / tiles                  pieces home on the LIVE board, out of n*n-1
 *   backtracks     slides that undid the slide before them (class total)
 *   thrash         those of them made while a wash was up (class total)
 *   assists        rescue EPISODES across the class (not hint repaints)
 * @returns {{composite:number, terms:Object, tax:number, raw:number, par:number,
 *            parEarned:number, boardsDone:number, expectedBoards:number}}
 */
export function compositeFor(i = {}) {
  const banked = Math.max(0, Math.round(Number(i.banked) || 0));
  const expected = Math.max(1, Math.round(Number(i.expectedBoards) || 1));
  const boardSolved = !!i.boardSolved;
  const moves = Math.max(0, Math.round(Number(i.moves) || 0));
  const par = Math.max(1, Math.round(Number(i.par) || 1));
  const parBanked = Math.max(0, Number(i.parBanked) || 0);
  const tiles = Math.max(1, Math.round(Number(i.tiles) || 1));
  const locked = Math.max(0, Math.min(tiles, Math.round(Number(i.locked) || 0)));
  const mhStart = Math.max(0, Number(i.manhattanStart) || 0);
  const mhNow = Math.max(0, Number(i.manhattanNow) || 0);

  const closed = mhStart > 0 ? clamp01((mhStart - mhNow) / mhStart) : 0;
  const home = clamp01(locked / tiles);
  /* THE LIVE BOARD's own worth, 0..1. A board whose whole is already in
   * `banked` is worth ZERO here - it has been paid for. */
  const live = boardSolved
    ? 0
    : clamp01(PLAYTEST.PROGRESS_MH_SHARE * closed + (1 - PLAYTEST.PROGRESS_MH_SHARE) * home);

  const boardsDone = banked + live;
  const progress = clamp01(boardsDone / expected);

  /* PACE. Every banked board's own par, plus the live board's par pro-rated by
   * how much of it came back, against the class's real slide count. One board
   * solved flat in par and nothing else reads exactly what it read before. */
  const parEarned = parBanked + live * par;
  const pace = moves > 0 ? clamp01(parEarned / moves) : 0;

  const backtracks = Math.max(0, Math.round(Number(i.backtracks) || 0));
  const thrash = Math.max(0, Math.round(Number(i.thrash) || 0));
  const denom = Math.max(PLAYTEST.CALM_MIN_MOVES, moves);
  const panic = (PLAYTEST.BACKTRACK_COST * backtracks + PLAYTEST.THRASH_COST * thrash) / denom;
  const calm = clamp01(1 - panic / PLAYTEST.CALM_TOL);

  const raw = PLAYTEST.W_PROGRESS * progress + PLAYTEST.W_PACE * pace + PLAYTEST.W_CALM * calm;
  const assists = Math.max(0, Math.round(Number(i.assists) || 0));
  const tax = Math.max(PLAYTEST.ASSIST_TAX_FLOOR, Math.pow(PLAYTEST.ASSIST_TAX, assists));

  return {
    composite: clamp01(raw * tax),
    raw: clamp01(raw),
    tax,
    par,
    parEarned,
    boardsDone,
    expectedBoards: expected,
    terms: { progress, pace, calm, closed, home, live, banked },
  };
}

/**
 * Declared hard gates. `sGate` is the rescue promise: a class that took the
 * skill-floor assist may not exceed A, whatever the composite says - and it
 * stays CLASS-sticky across the re-deals, because the promise was about the
 * class, not about one board. Nothing else in this game gates: the board size
 * is the year's, not a choice, and running out the bell is the normal ending
 * of a timed class rather than a failure.
 */
export function hardGates(rescueUsed) {
  return { sGate: !rescueUsed };
}

/**
 * +5 for banking any picture at all, +5 for a new personal best on this board
 * size, +5 for having taken any one board inside its own par. Cap 15.
 *
 * @param {Object} i {banked, bestSolveMoves, bestMovesBefore, underParSolve}
 *   bestSolveMoves  the fewest moves any ONE banked board took this class
 *   underParSolve   at least one banked board came in at or under its own par
 */
export function flavorXp(i = {}) {
  const banked = Math.max(0, Math.round(Number(i.banked) || 0));
  if (banked <= 0) return 0;
  let xp = PLAYTEST.FLAVOR_SOLVE;
  const before = Number(i.bestMovesBefore);
  const best = Math.max(1, Math.round(Number(i.bestSolveMoves) || 1));
  if (!Number.isFinite(before) || before <= 0 || best < before) xp += PLAYTEST.FLAVOR_BEST;
  if (i.underParSolve) xp += PLAYTEST.FLAVOR_UNDER_PAR;
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
  const budgetMs = Math.max(20000, (Number(o.timeBudgetSec) || 300) * 1000);

  /* 1. the wash windows - the burial the whole game is about. Dealt across the
   *    budget between a lead-in and a tail so no wash lands on the bell.
   *
   *    THE COUNT SCALES WITH THE BUDGET. WASH_COUNT was tasted at a 120s class
   *    and the class is 300s now, so a literal count would have thinned the
   *    burial to 40% of its old density and quietly made every year easier.
   *    Scale by budget/WASH_BASE_SEC, then clamp by the LOAD rule (no more
   *    than WASH_LOAD_MAX of the class under a wash) and the hard ceiling.
   *    The spread machinery below needs no change and degrades on its own:
   *    `span` grows with the budget and the per-slot jitter is a fraction of
   *    span/count, so more washes across more wall stay evenly dealt. */
  const washMs = zen ? PLAYTEST.ZEN_WASH_MS : (PLAYTEST.WASH_MS[tier] || 2600);
  const baseCount = zen ? PLAYTEST.ZEN_WASH_COUNT : (PLAYTEST.WASH_COUNT[tier] || 1);
  const budgetScale = budgetMs / (PLAYTEST.WASH_BASE_SEC * 1000);
  const loadCap = Math.max(1, Math.floor((budgetMs * PLAYTEST.WASH_LOAD_MAX) / Math.max(200, washMs)));
  const count = Math.max(1, Math.min(
    PLAYTEST.WASH_COUNT_MAX, loadCap, Math.round(baseCount * budgetScale),
  ));
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
    budgetMs,
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
  gridForTier, scrambleWalkFor, expectedBoardsFor, clamp01, tierOf,
};
