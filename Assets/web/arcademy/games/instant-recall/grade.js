/* ============================================================================
 * games/instant-recall/grade.js - the composite INSTANT RECALL hands to the ONE
 * shared rubric (core/grades.js, via ctx.endClass). PURE.
 *
 * A game never grades itself: this file turns the vigil's inputs into a 0..1
 * composite, a declared hard gate and the flavour XP, and the SHELL maps the
 * composite to S/A/B/C (>= .92 S / .75 A / .50 B) and applies every cap.
 *
 * THE COMPOSITE (dossier "Grading", contract "template-level accuracy weighted
 * by window, decoy resistance, latency"):
 *   .55  accuracy   per-question credit, WEIGHTED BY WINDOW - a 4s question is
 *                   worth 1.5x a 6s one, so a tier-4 class is not silently
 *                   easier to ace than a tier-1 one. Library-agnostic: it scores
 *                   TEMPLATES, never which asset the provider happened to deal.
 *   .20  latency    mean commit time as a fraction of the question's own window.
 *                   TIMEOUTS ARE EXCLUDED FROM THE MEAN (a 6s non-answer would
 *                   otherwise read as a slow answer) and scored as misses above.
 *   .15  resistance fraction of decoy-plant exposures NOT taken. Scored 1.0 when
 *                   plants are disabled by tier or reduced motion, so
 *                   accessibility never costs a grade.
 *   .10  streak     longest run of fully-correct stops / total stops.
 *
 * HARD RULES the dossier pins, and where each one lives:
 *   "S requires every stop fully correct"  -> hardGates.sGate (the shell caps a
 *                                             failed gate at A).
 *   "any timed-out question caps at B"     -> TIMEOUT_CEIL here. core/grades.js
 *                                             only knows A-caps, so the honest
 *                                             way to express a B ceiling is to
 *                                             clamp the composite just under the
 *                                             A threshold. It never PROMOTES.
 *   "a voided stop scores C for that stop, -> VOID_CREDIT (partial credit, never
 *    but never fails the class"                zero, never a fail).
 *   comeback hook                          -> `correctedWeight`: ONE missed stop
 *                                             per class may be restored to full
 *                                             credit by getting the NEXT stop
 *                                             fully right. It cannot launder an
 *                                             S: a corrected stop was still not
 *                                             fully correct, so sGate stays down.
 *
 * BOTH OF THOSE WERE WRITTEN FOR A 2-4 STOP CLASS AND ARE NOW RATIOS (the
 * five-a-minute cadence, 2026-08-23). A gate counted in absolute stops does not
 * mean the same thing at ten stops as at two: "every stop fully correct" over
 * ten questions is a different ask from over two, and one blanked question in
 * ten capping the whole class at B punishes a fumble rather than a lapse. So
 * both allowances are `floor(n * 0.12)` of the class's OWN size - which is 0 at
 * 2-8 (the dossier's literal rule, unchanged for any short class) and exactly 1
 * at 9-16. The gates still tighten with the class, they just no longer tighten
 * because the class got longer.
 *
 * FLAVOUR XP: a small tick per bait dodged plus one per clean stop, cap 15
 * (the shell clamps at 15 too - BUILD-CONTRACT §8). Never composite.
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_ACCURACY: 0.55,
  W_LATENCY: 0.20,
  W_RESIST: 0.15,
  W_STREAK: 0.10,

  /** Latency anchors, as a fraction of the question's own window. */
  LAT_FAST: 0.35,     // at or under -> full marks
  LAT_SLOW: 0.95,     // at or over  -> nothing

  /** Any timed-out question clamps the class here (just under the A threshold),
   *  once the class's own allowance is spent. */
  TIMEOUT_CEIL: 0.74,
  /** The fraction of a class's stops that may miss and still open the S gate,
   *  and the fraction of its questions that may blank before the B ceiling
   *  falls. Floored, so a 2-8 stop class gets 0 (the original rule verbatim)
   *  and a 9-16 stop class gets exactly 1. */
  S_MISS_FRACTION: 0.12,
  TIMEOUT_ALLOWANCE_FRACTION: 0.12,
  /** An escape-guard-voided question: C for that question, never zero. */
  VOID_CREDIT: 0.4,

  FLAVOR_PER_PLANT: 2,
  FLAVOR_PER_CLEAN_STOP: 1,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }

/** How many stops a class of this size may miss and still open the S gate. */
export function allowedMisses(totalStops) {
  const n = Math.max(0, Math.round(Number(totalStops) || 0));
  return Math.floor(n * GRADE.S_MISS_FRACTION);
}
/** How many questions a class of this size may blank before the B ceiling. */
export function allowedBlanks(totalQuestions) {
  const n = Math.max(0, Math.round(Number(totalQuestions) || 0));
  return Math.floor(n * GRADE.TIMEOUT_ALLOWANCE_FRACTION);
}

/**
 * @param {Object} i
 *   gradeTier        1..4
 *   questions        [{ correct, weight, windowMs, latencyMs, timedOut, voided }]
 *   stops            [{ fullyCorrect, voided }]
 *   plantExposures   how many plants actually fired at the player
 *   plantsResisted   how many of those did not take
 *   plantsEnabled    false when tier / reduced motion disabled them (=> 1.0)
 *   correctedWeight  weighted credit restored by the comeback hook
 *   bestRun          longest run of fully-correct stops
 * @returns {{composite:number, raw:number, terms:Object, capped:boolean, weight:number}}
 */
export function compositeFor(i = {}) {
  const qs = Array.isArray(i.questions) ? i.questions : [];
  const stops = Array.isArray(i.stops) ? i.stops : [];

  /* ---- accuracy: weighted by window ---------------------------------- */
  let wSum = 0;
  let credit = 0;
  let timeouts = 0;
  let voids = 0;
  for (const q of qs) {
    const w = Math.max(0, Number(q.weight) || 0);
    if (!w) continue;
    wSum += w;
    if (q.voided) { credit += w * GRADE.VOID_CREDIT; voids += 1; continue; }
    if (q.timedOut) { timeouts += 1; continue; }
    if (q.correct) credit += w;
  }
  const restored = Math.max(0, Number(i.correctedWeight) || 0);
  const accuracy = wSum > 0 ? clamp01((credit + restored) / wSum) : 0;

  /* ---- latency: mean fraction of the window, timeouts excluded -------- */
  let latSum = 0;
  let latN = 0;
  for (const q of qs) {
    if (q.timedOut || q.voided) continue;
    const win = Math.max(1, Number(q.windowMs) || 1);
    const ms = Math.max(0, Number(q.latencyMs) || 0);
    latSum += Math.min(1.5, ms / win);
    latN += 1;
  }
  const meanF = latN > 0 ? latSum / latN : 1;
  const latency = latN > 0
    ? clamp01((GRADE.LAT_SLOW - meanF) / (GRADE.LAT_SLOW - GRADE.LAT_FAST))
    : 0;

  /* ---- decoy resistance ---------------------------------------------- */
  const exposures = Math.max(0, Math.round(Number(i.plantExposures) || 0));
  const resistedN = Math.max(0, Math.round(Number(i.plantsResisted) || 0));
  const resistance = (i.plantsEnabled === false || exposures === 0)
    ? 1
    : clamp01(resistedN / exposures);

  /* ---- vigil streak --------------------------------------------------- */
  const totalStops = stops.length;
  const best = Math.max(0, Math.round(Number(i.bestRun) || 0));
  const streak = totalStops > 0 ? clamp01(best / totalStops) : 0;

  const raw = GRADE.W_ACCURACY * accuracy + GRADE.W_LATENCY * latency
    + GRADE.W_RESIST * resistance + GRADE.W_STREAK * streak;

  let composite = clamp01(raw);
  let capped = false;
  const blanksOk = allowedBlanks(qs.length);
  if (timeouts > blanksOk && composite > GRADE.TIMEOUT_CEIL) { composite = GRADE.TIMEOUT_CEIL; capped = true; }

  return {
    composite,
    raw: clamp01(raw),
    capped,
    weight: wSum,
    terms: { accuracy, latency, resistance, streak },
    counts: { questions: qs.length, timeouts, voids, stops: totalStops, exposures, resisted: resistedN },
  };
}

/**
 * Declared gates. The dossier's S rule scaled to the class's own length: a
 * clean sheet, less `allowedMisses()` - 0 for any class of 8 stops or fewer
 * (the original rule, verbatim), 1 for the 9-11 stop classes the cadence now
 * deals. A class with no stops (impossible under the final-stop guarantee, but
 * the gate must still answer) cannot earn an S.
 */
export function hardGates(stops) {
  const list = Array.isArray(stops) ? stops : [];
  if (!list.length) return { sGate: false };
  const missed = list.filter((s) => !s || !s.fullyCorrect || s.voided).length;
  return { sGate: missed <= allowedMisses(list.length) };
}

/** A tick per bait dodged plus one per clean stop, cap 15. */
export function flavorXp(plantsResisted, cleanStops) {
  const p = Math.max(0, Math.round(Number(plantsResisted) || 0));
  const c = Math.max(0, Math.round(Number(cleanStops) || 0));
  return Math.min(GRADE.FLAVOR_CAP, p * GRADE.FLAVOR_PER_PLANT + c * GRADE.FLAVOR_PER_CLEAN_STOP);
}

export default { compositeFor, hardGates, flavorXp, allowedMisses, allowedBlanks, GRADE };
