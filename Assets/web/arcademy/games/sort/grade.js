/* ============================================================================
 * games/sort/grade.js - the composite SORT hands to the ONE shared rubric
 * (core/grades.js, through ctx.endClass). PURE.
 *
 * A class never grades itself. This file turns the room's ledger into a 0..1
 * composite, a declared hard gate and the flavour XP; the SHELL maps the
 * composite onto S/A/B/C and applies every cap.
 *
 * THE COMPOSITE
 *   .55 accuracy      correct swipes over swipes taken. Passes are NOT swipes -
 *                     letting a ring close is the pressure valve and it may
 *                     never look like an error on the ledger.
 *   .30 tempo         best rung reached against THIS TIER's cap. Tempo is the
 *                     half of the class the player drives, and a tier-1 player
 *                     who caps at rung 5 has driven it as hard as it goes.
 *   .15 PERFECT share PERFECTs over correct swipes: the last 40% of the ring is
 *                     where the room wants you living.
 *
 * S GATE: accuracy >= .95 AND best rung >= cap - 1. Speed alone cannot buy an
 * S (you can hold rung 8 while getting one in ten wrong), and neither can care
 * alone (a perfectly accurate player who never let the chain build was never
 * playing the game the room is about).
 *
 * FLAVOUR XP: sorted / 5, cap 15. Game-owned; the XP table itself is C#'s.
 *
 * THE CLASS LENGTH IS NOT IN THIS FILE. Every term above is a RATIO of the
 * class's own ledger (correct/swipes, bestRung/rungCap, perfect/correct), so
 * the 120 -> 180 budget move needs nothing here and cannot tilt a grade: a
 * longer class gives a player more swipes to be accurate over, never an easier
 * bar. The one absolute is the flavour XP cap, and it stays 15 deliberately -
 * the XP economy is not part of the class-length wave, and a 120s class already
 * reached the cap at 75 sorted cards.
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_ACCURACY: 0.55,
  W_TEMPO: 0.30,
  W_PERFECT: 0.15,
  /** The S gate's accuracy floor. */
  S_ACCURACY: 0.95,
  /** The S gate's tempo floor, as a distance below the tier cap. */
  S_RUNG_SLACK: 1,
  FLAVOR_PER: 5,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function intOf(v) { const n = Math.round(Number(v)); return Number.isFinite(n) && n > 0 ? n : 0; }

/**
 * @param {Object} i
 *   correct    clean swipes
 *   wrong      wrong swipes (honoured, never blocked, always counted)
 *   perfect    swipes inside the gold arc
 *   bestRung   the highest rung the class stood on
 *   rungCap    the tier's rung ceiling (chain.capForTier)
 * @returns {{composite:number, terms:Object, accuracy:number, swipes:number}}
 */
export function compositeFor(i = {}) {
  const correct = intOf(i.correct);
  const wrong = intOf(i.wrong);
  const swipes = correct + wrong;
  const perfect = Math.min(correct, intOf(i.perfect));
  const cap = Math.max(1, intOf(i.rungCap) || 8);
  const bestRung = Math.min(cap, intOf(i.bestRung));

  const accuracy = swipes > 0 ? correct / swipes : 0;
  const tempo = clamp01(bestRung / cap);
  const perfectShare = correct > 0 ? perfect / correct : 0;

  const composite = clamp01(
    GRADE.W_ACCURACY * clamp01(accuracy)
    + GRADE.W_TEMPO * tempo
    + GRADE.W_PERFECT * clamp01(perfectShare)
  );
  return {
    composite,
    accuracy,
    swipes,
    terms: { accuracy: clamp01(accuracy), tempo, perfect: clamp01(perfectShare) },
  };
}

/**
 * The declared hard gate. A dropped gate caps the class at A (the shell's law,
 * not ours) - it is never a fail.
 */
export function hardGates(i = {}) {
  const correct = intOf(i.correct);
  const wrong = intOf(i.wrong);
  const swipes = correct + wrong;
  const cap = Math.max(1, intOf(i.rungCap) || 8);
  const bestRung = Math.min(cap, intOf(i.bestRung));
  const accuracy = swipes > 0 ? correct / swipes : 0;
  const sGate = swipes > 0
    && accuracy >= GRADE.S_ACCURACY
    && bestRung >= Math.max(0, cap - GRADE.S_RUNG_SLACK);
  return { sGate: !!sGate };
}

/** sorted / 5, cap 15. "sorted" is every card that left the stack on a swipe. */
export function flavorXp(sorted) {
  return Math.min(GRADE.FLAVOR_CAP, Math.floor(intOf(sorted) / GRADE.FLAVOR_PER));
}

/**
 * The ticket: four numbers, and they are the four the player was actually
 * playing for. Nothing derived, nothing rounded into a lie.
 */
export function ticketFor(i = {}) {
  return {
    sorted: intOf(i.correct) + intOf(i.wrong),
    correct: intOf(i.correct),
    wrong: intOf(i.wrong),
    passed: intOf(i.passed),
    perfect: intOf(i.perfect),
    longestChain: intOf(i.longestChain),
    topRung: Math.min(Math.max(1, intOf(i.rungCap) || 8), intOf(i.bestRung)),
  };
}

/** Everything the class hands the shell, in one call. */
export function gradeClass(i = {}) {
  const comp = compositeFor(i);
  return {
    composite: comp.composite,
    terms: comp.terms,
    accuracy: comp.accuracy,
    swipes: comp.swipes,
    gates: hardGates(i),
    flavorXp: flavorXp(intOf(i.correct) + intOf(i.wrong)),
    ticket: ticketFor(i),
  };
}

export default { GRADE, compositeFor, hardGates, flavorXp, ticketFor, gradeClass };
