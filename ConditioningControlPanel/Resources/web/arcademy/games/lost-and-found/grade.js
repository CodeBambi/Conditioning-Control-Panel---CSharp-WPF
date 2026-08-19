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
 *      expressed as a HARD GATE rather than as weight, because "S requires a 3+
 *      clean streak plus sub-par median and 0-1 misclicks" is a gate, not a curve
 *      (SYNTHESIS #14 blesses per-game hard gates).
 *
 * Pity pulses are deliberately absent from every term: they are a comeback aid,
 * not a tax.
 * ==========================================================================*/

import { FINDS_PER_CLASS, RUBRIC } from './constants.js';
import { clamp01, median as medianOf } from './util.js';

/**
 * Seconds-per-find par. Scales with the two things that actually make a hunt
 * slower - how many candidates there are and how much the board is moving - and
 * is then clamped so a class can never be graded against a par its own time
 * budget cannot reach (the quick-slot / 60s case).
 */
export function parSecFor(o) {
  const opts = o || {};
  const density = Math.max(4, Number(opts.density) || 16);
  const drift = clamp01(opts.drift);
  const finds = Math.max(1, Number(opts.finds) || FINDS_PER_CLASS);
  let par = RUBRIC.PAR_BASE_SEC
    + RUBRIC.PAR_PER_TILE_SEC * density
    + RUBRIC.PAR_PER_DRIFT_SEC * drift;
  const budget = Number(opts.timeBudgetSec);
  if (Number.isFinite(budget) && budget > 0) {
    par = Math.min(par, (budget * RUBRIC.PAR_BUDGET_SHARE) / finds);
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

export function peekScore(peeks) {
  return clamp01(1 - Math.max(0, Number(peeks) || 0) * RUBRIC.PEEK_TAX);
}

export function streakScore(bestCleanStreak) {
  return clamp01(Math.max(0, Number(bestCleanStreak) || 0) / FINDS_PER_CLASS);
}

/**
 * The declared hard gate. FALSE caps the class at A (core/grades.js), which is
 * how "S requires a clean streak, a sub-par median and near-perfect accuracy"
 * gets enforced without this file ever naming the letter S.
 */
export function sGateFor(o) {
  const opts = o || {};
  return !!(
    (Number(opts.bestCleanStreak) || 0) >= RUBRIC.S_GATE_CLEAN_STREAK
    && (Number(opts.medianSec) || Infinity) <= (Number(opts.par) || 0)
    && (Number(opts.misclicks) || 0) <= RUBRIC.S_GATE_MAX_MISCLICKS
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
 * @param {number=} o.timeBudgetSec  omitted / null for zen (no timeout at all)
 * @param {number=} o.jackpots
 * @returns {{composite:number, hardGates:{sGate:boolean}, flavorXp:number,
 *            medianSec:number, par:number, complete:boolean, parts:Object}}
 */
export function scoreClass(o) {
  const opts = o || {};
  const times = Array.isArray(opts.findTimesSec) ? opts.findTimesSec.filter((n) => Number.isFinite(n)) : [];
  const finds = times.length;
  const complete = finds >= FINDS_PER_CLASS;
  const misclicks = Math.max(0, Number(opts.misclicks) || 0);
  const peeks = Math.max(0, Number(opts.peeks) || 0);
  const bestCleanStreak = Math.max(0, Number(opts.bestCleanStreak) || 0);

  const par = parSecFor({
    density: opts.density, drift: opts.drift,
    timeBudgetSec: opts.timeBudgetSec, finds: FINDS_PER_CLASS,
  });
  const medianSec = finds ? medianOf(times) : Infinity;

  const parts = {
    time: finds ? timeScore(medianSec, par) : 0,
    miss: missScore(misclicks, Math.max(1, finds)),
    peek: peekScore(peeks),
    streak: streakScore(bestCleanStreak),
  };
  const w = RUBRIC.WEIGHTS;
  let composite = clamp01(
    w.time * parts.time + w.miss * parts.miss + w.peek * parts.peek + w.streak * parts.streak
  );

  // Timed round timeout with fewer than 5 finds = C floor with partial credit.
  if (!complete) {
    composite = Math.min(
      composite * (finds / FINDS_PER_CLASS),
      RUBRIC.TIMEOUT_COMPOSITE_CEIL
    );
  }

  const flavorXp = Math.min(
    RUBRIC.XP_CAP,
    finds * RUBRIC.XP_PER_FIND + Math.max(0, Number(opts.jackpots) || 0) * RUBRIC.XP_PER_JACKPOT
  );

  return {
    composite,
    hardGates: { sGate: complete && sGateFor({ bestCleanStreak, medianSec, par, misclicks }) },
    flavorXp,
    medianSec: finds ? medianSec : 0,
    par,
    complete,
    finds,
    parts,
  };
}

export default scoreClass;
