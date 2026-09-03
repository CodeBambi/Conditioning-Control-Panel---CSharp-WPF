/* ============================================================================
 * games/lost-and-found/grade.js - the game-specific INPUTS to the shared rubric.
 *
 * PURE FUNCTIONS. This file computes a weighted composite (0..1) and one declared
 * hard gate; it does NOT know what an S is. Letters, A-caps and zen all belong to
 * core/grades.js, and the XP numbers belong to C# - a game that mapped its own
 * composite to a letter, or paid its own XP, would be breaking two laws at once
 * (SYNTHESIS #4, BUILD-CONTRACT §8).
 *
 * The four inputs are exactly the ones the dossier declares:
 *   1. median time-to-find, normalised against a tier par that scales with
 *      density AND drift (so tiers compare fairly);
 *   2. misclick rate (misclicks / finds);
 *   3. peek count (a soft tax - the hard part of peek is the shell's flat A-cap);
 *   4. best clean streak (no misclick, no peek) as the S differentiator, which is
 *      expressed as a HARD GATE rather than as weight, because "S requires a
 *      clean streak plus sub-par median and near-perfect accuracy" is a gate,
 *      not a curve (SYNTHESIS #14 blesses per-game hard gates).
 *
 * Pity pulses are deliberately absent from every term: they are a comeback aid,
 * not a tax.
 *
 * NOTHING HERE KNOWS WHAT A TIER IS (2026-08-24, the class-length wave). The
 * class is 13-26 finds depending on grade tier now, so every threshold that used
 * to be a flat count is derived from ONE number the caller passes, `findsTarget`
 * - which keeps this file tier-agnostic exactly the way it is density-agnostic.
 * A caller that omits it is graded against tier 1's class, never against 5.
 * ==========================================================================*/

import { findsForTier, RUBRIC } from './constants.js';
import { clamp01, median as medianOf } from './util.js';

/** The finds this class was asking for. Junk answers tier 1's count. */
function targetFinds(n) {
  const v = Math.round(Number(n));
  return Number.isFinite(v) && v > 0 ? v : findsForTier(1);
}

/** The clean streak the S gate demands of a class this long (see RUBRIC). */
export function sGateStreakFor(findsTarget) {
  return Math.max(
    RUBRIC.S_GATE_CLEAN_STREAK_MIN,
    Math.round(targetFinds(findsTarget) * RUBRIC.S_GATE_CLEAN_STREAK_RATIO)
  );
}

/** The misclicks the S gate forgives in a class this long. */
export function sGateMisclicksFor(findsTarget) {
  return Math.max(
    RUBRIC.S_GATE_MISCLICKS_MIN,
    Math.round(targetFinds(findsTarget) * RUBRIC.S_GATE_MISCLICK_RATE)
  );
}

/** The clean streak that scores a full 1.0 on the streak term (affine - see
 *  RUBRIC.STREAK_FULL_BASE for why it is not a ratio). */
export function streakFullFor(findsTarget) {
  return Math.max(
    RUBRIC.STREAK_FULL_MIN,
    Math.round(RUBRIC.STREAK_FULL_BASE + RUBRIC.STREAK_FULL_PER_FIND * targetFinds(findsTarget))
  );
}

/** The peek count that zeroes the peek term in a class this long. */
export function peekZeroFor(findsTarget) {
  return Math.max(
    RUBRIC.PEEK_ZERO_MIN,
    Math.round(targetFinds(findsTarget) * RUBRIC.PEEK_ZERO_RATIO)
  );
}

/**
 * Seconds-per-find par. Scales with the two things that actually make a hunt
 * slower - how many candidates there are and how much the board is moving - and
 * is then clamped so a class can never be graded against a par its own time
 * budget cannot reach (the quick-slot / 60s case, and now also the ordinary
 * case: 13-26 finds into 300s is a much tighter clamp than 5 into 120s ever
 * was, and at every tier it is the clamp - not the density term - that wins).
 * `finds` is the finds the class ASKED for, never the finds it got.
 */
export function parSecFor(o) {
  const opts = o || {};
  const density = Math.max(4, Number(opts.density) || 16);
  const drift = clamp01(opts.drift);
  const finds = Math.max(1, Number(opts.finds) || findsForTier(1));
  let par = RUBRIC.PAR_BASE_SEC
    + RUBRIC.PAR_PER_TILE_SEC * density
    + RUBRIC.PAR_PER_DRIFT_SEC * drift;
  const budget = Number(opts.timeBudgetSec);
  if (Number.isFinite(budget) && budget > 0) {
    // THE BUDGET IS NOT ALL HUNTING TIME. Every find spends a found ceremony
    // the player cannot hunt through, so the clamp divides what is LEFT after
    // the ceremonies - otherwise a class of 26 finds is graded against a par
    // that a player hunting exactly at par would still miss the bell hitting.
    const cer = Number.isFinite(Number(opts.ceremonySec))
      ? Math.max(0, Number(opts.ceremonySec)) : RUBRIC.PAR_CEREMONY_SEC;
    const hunting = Math.max(budget * 0.25, budget - finds * cer);
    par = Math.min(par, (hunting * RUBRIC.PAR_BUDGET_SHARE) / finds);
  }
  return Math.max(1.5, par);
}

/** timeScore: .6*par -> 1.0, par -> ~.78, 1.6*par -> ~.44, 2.4*par -> 0. */
export function timeScore(medianSec, par) {
  const p = Math.max(0.1, Number(par) || 1);
  const m = Math.max(0, Number(medianSec) || 0);
  return clamp01((RUBRIC.TIME_ZERO_MULT * p - m) / (RUBRIC.TIME_SPAN_MULT * p));
}

export function missScore(misclicks, finds) {
  const f = Math.max(1, Number(finds) || 1);
  const rate = Math.max(0, Number(misclicks) || 0) / f;
  return clamp01(1 - rate / RUBRIC.MISS_TOLERANCE);
}

/** The tax zeroes the term at peekZeroFor(findsTarget) peeks - a share of the
 *  class, not a flat three, or a 4-minute class would be a fixed -0.10 the
 *  moment the player peeked twice. */
export function peekScore(peeks, findsTarget) {
  const zero = peekZeroFor(findsTarget);
  return clamp01(1 - Math.max(0, Number(peeks) || 0) / zero);
}

export function streakScore(bestCleanStreak, findsTarget) {
  return clamp01(Math.max(0, Number(bestCleanStreak) || 0) / streakFullFor(findsTarget));
}

/**
 * The declared hard gate. FALSE caps the class at A (core/grades.js), which is
 * how "S requires a clean streak, a sub-par median and near-perfect accuracy"
 * gets enforced without this file ever naming the letter S. Both counted halves
 * scale with the length of the class the player was actually asked to play.
 */
export function sGateFor(o) {
  const opts = o || {};
  return !!(
    (Number(opts.bestCleanStreak) || 0) >= sGateStreakFor(opts.findsTarget)
    && (Number(opts.medianSec) || Infinity) <= (Number(opts.par) || 0)
    && (Number(opts.misclicks) || 0) <= sGateMisclicksFor(opts.findsTarget)
  );
}

/**
 * The whole rubric input for one class.
 * @param {Object} o
 * @param {number[]} o.findTimesSec  seconds-to-find, one per find
 * @param {number} o.misclicks
 * @param {number} o.peeks
 * @param {number} o.bestCleanStreak
 * @param {number} o.density
 * @param {number} o.drift
 * @param {number} o.findsTarget     the finds THIS class asked for (per tier)
 * @param {number=} o.ceremonySec    found-ceremony length, for the par clamp
 * @param {number=} o.timeBudgetSec  omitted / null for zen (no timeout at all)
 * @param {number=} o.jackpots
 * @returns {{composite:number, hardGates:{sGate:boolean}, flavorXp:number,
 *            medianSec:number, par:number, complete:boolean, parts:Object}}
 */
export function scoreClass(o) {
  const opts = o || {};
  const times = Array.isArray(opts.findTimesSec) ? opts.findTimesSec.filter((n) => Number.isFinite(n)) : [];
  const finds = times.length;
  const findsTarget = targetFinds(opts.findsTarget);
  const complete = finds >= findsTarget;
  const misclicks = Math.max(0, Number(opts.misclicks) || 0);
  const peeks = Math.max(0, Number(opts.peeks) || 0);
  const bestCleanStreak = Math.max(0, Number(opts.bestCleanStreak) || 0);

  const par = parSecFor({
    density: opts.density, drift: opts.drift, ceremonySec: opts.ceremonySec,
    timeBudgetSec: opts.timeBudgetSec, finds: findsTarget,
  });
  const medianSec = finds ? medianOf(times) : Infinity;

  const parts = {
    time: finds ? timeScore(medianSec, par) : 0,
    miss: missScore(misclicks, Math.max(1, finds)),
    peek: peekScore(peeks, findsTarget),
    streak: streakScore(bestCleanStreak, findsTarget),
  };
  const w = RUBRIC.WEIGHTS;
  let composite = clamp01(
    w.time * parts.time + w.miss * parts.miss + w.peek * parts.peek + w.streak * parts.streak
  );

  /* THE BELL WITH PARTIAL FINDS. Not a fail and not a flat C - see
     RUBRIC.TIMEOUT_FULL_CREDIT_RATIO. Above the credit line the composite is
     untouched (the class was played, the bell merely arrived); below it, credit
     falls away in proportion to how much of the class went unplayed. S is still
     unreachable, because the sGate below requires `complete`. */
  const completion = complete ? 1 : clamp01(
    (finds / findsTarget) / Math.max(0.01, RUBRIC.TIMEOUT_FULL_CREDIT_RATIO)
  );
  if (!complete) {
    composite = Math.min(composite * completion, RUBRIC.TIMEOUT_COMPOSITE_CEIL);
  }

  /* XP is FLAT PER FIND and capped app-wide, so it saturates part-way through a
     13-26 find class (find 8 with no jackpots). Deliberate - see RUBRIC. */
  const flavorXp = Math.min(
    RUBRIC.XP_CAP,
    finds * RUBRIC.XP_PER_FIND + Math.max(0, Number(opts.jackpots) || 0) * RUBRIC.XP_PER_JACKPOT
  );

  return {
    composite,
    hardGates: {
      sGate: complete && sGateFor({ bestCleanStreak, medianSec, par, misclicks, findsTarget }),
    },
    flavorXp,
    medianSec: finds ? medianSec : 0,
    par,
    complete,
    completion,
    finds,
    findsTarget,
    parts,
  };
}

export default scoreClass;
