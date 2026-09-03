/* ============================================================================
 * games/echo/grade.js - the composite ECHO hands to the ONE shared rubric
 * (core/grades.js via ctx.endClass). PURE.
 *
 * A game never grades itself: this file turns the class's inputs into a 0..1
 * composite, a declared hard gate and the flavour XP, and the SHELL maps the
 * composite to S / A / B / C (>= .92 S / .75 A / .50 B) and applies every cap.
 *
 * THE COMPOSITE (the dossier's grading inputs, weighted per the contract):
 *   .50 length     the LONGEST CLEARED echo against the tier's S length
 *                  ({1:8, 2:10, 3:12, 4:14}) - the primary input
 *   .20 accuracy   correct presses / total presses
 *   .15 tempo      mean per-press latency normalised to the playback step; at
 *                  or under the step it is full marks, at TEMPO_FLOOR steps it
 *                  is zero. `echoPlaybackTempo` never shipped as a setting, so
 *                  there is no tempo_assist A-cap to raise here.
 *   .15 decoys     decoys resisted / decoys presented; NEUTRAL (a full term)
 *                  when the tier presented none, so a tier-1 class is never
 *                  quietly graded against a mechanic it has not met.
 *
 * HARD GATE: `sGate` is false when the ENCORE was used. The dossier's rule is
 * "S requires no encore"; a failed declared gate caps the class at A in
 * core/grades.js, which is exactly that rule and nothing harsher.
 *
 * FLAVOUR XP: +3 per cleared length above the tier's par, cap 15. Game-owned,
 * never part of the composite (the XP table itself is C#'s).
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_LENGTH: 0.50,
  W_ACCURACY: 0.20,
  W_TEMPO: 0.15,
  W_DECOY: 0.15,

  /** Longest cleared echo that earns the full length term, per gradeTier. */
  S_LEN: Object.freeze({ 1: 8, 2: 10, 3: 12, 4: 14 }),
  /** The tier's par length - flavour XP starts above this. */
  PAR_LEN: Object.freeze({ 1: 6, 2: 7, 3: 9, 4: 11 }),
  /** The length term is measured from here (the shortest sequence dealt). */
  LEN_BASE: 3,

  /** Mean latency / step at or under this = the full tempo term. */
  TEMPO_TARGET: 1.0,
  /** ... and at or over this = zero. */
  TEMPO_FLOOR: 2.4,

  FLAVOR_PER_LEN: 3,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/**
 * @param {Object} i
 *   gradeTier         1..4
 *   bestLen           longest sequence CLEARED this class (0 if none)
 *   correct           correct presses this class
 *   presses           total presses this class (correct + wrong)
 *   meanLatencyMs     mean gap between presses inside an input turn
 *   stepMs            the tier's playback step (the normaliser)
 *   decoysPresented   decoys the player actually reached
 *   decoysResisted    of those, the ones not taken
 * @returns {{composite:number, terms:Object, sLen:number}}
 */
export function compositeFor(i = {}) {
  const tier = tierOf(i.gradeTier);
  const sLen = GRADE.S_LEN[tier];
  const best = Math.max(0, Math.floor(Number(i.bestLen) || 0));
  const span = Math.max(1, sLen - GRADE.LEN_BASE);
  const length = clamp01((best - GRADE.LEN_BASE) / span);

  const presses = Math.max(0, Math.floor(Number(i.presses) || 0));
  const correct = Math.max(0, Math.min(presses, Math.floor(Number(i.correct) || 0)));
  const accuracy = presses > 0 ? clamp01(correct / presses) : 0;

  const stepMs = Math.max(1, Number(i.stepMs) || 1);
  const mean = Number(i.meanLatencyMs);
  let tempo = 0;
  if (!Number.isFinite(mean) || mean <= 0) {
    // No timed press at all (a class that never got an input turn in): the
    // term is zero, not a free pass - the length term already carries that.
    tempo = 0;
  } else {
    const r = mean / stepMs;
    tempo = clamp01((GRADE.TEMPO_FLOOR - r) / Math.max(0.0001, GRADE.TEMPO_FLOOR - GRADE.TEMPO_TARGET));
  }

  const shown = Math.max(0, Math.floor(Number(i.decoysPresented) || 0));
  const kept = Math.max(0, Math.min(shown, Math.floor(Number(i.decoysResisted) || 0)));
  const decoys = shown > 0 ? clamp01(kept / shown) : 1;

  const composite = clamp01(
    GRADE.W_LENGTH * length
    + GRADE.W_ACCURACY * accuracy
    + GRADE.W_TEMPO * tempo
    + GRADE.W_DECOY * decoys,
  );
  return { composite, terms: { length, accuracy, tempo, decoys }, sLen };
}

/** Declared gates: an S requires a class with no encore (the dossier's rule). */
export function hardGates(encoreUsed) {
  return { sGate: !encoreUsed };
}

/** +3 per cleared length above the tier's par, cap 15. */
export function flavorXp(bestLen, gradeTier) {
  const tier = tierOf(gradeTier);
  const par = GRADE.PAR_LEN[tier];
  const best = Math.max(0, Math.floor(Number(bestLen) || 0));
  return Math.min(GRADE.FLAVOR_CAP, Math.max(0, best - par) * GRADE.FLAVOR_PER_LEN);
}

export default { compositeFor, hardGates, flavorXp, GRADE, clamp01 };
