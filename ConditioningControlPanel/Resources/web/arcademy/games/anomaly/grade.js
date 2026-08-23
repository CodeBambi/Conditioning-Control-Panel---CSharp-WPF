/* ============================================================================
 * games/anomaly/grade.js - the composite ANOMALY hands to the ONE shared
 * rubric (core/grades.js, via ctx.endClass). PURE.
 *
 * A game never grades itself: this file turns the class's inputs into a 0..1
 * composite, a declared hard gate and the flavour XP, and the SHELL maps the
 * composite to S/A/B/C (>= .92 S / .75 A / .50 B) and applies every cap.
 *
 * THE COMPOSITE (the dossier's four inputs, in its own order of importance):
 *   .40 accuracy   FIRST-TAP finds / rounds offered   (the skill being trained)
 *   .25 speed      median time-to-find against the tier's target
 *   .20 clear      rounds cleared / rounds offered    (a whiff is not a wrong tap)
 *   .15 tracking   recovery after a glitch_swap relocation: how many
 *                  relocated rounds were still cleared, and how fast
 *
 * TRACKING WITH NO RELOCATIONS: tiers 1-2 deal none, so there is nothing to
 * recover from. The term then MIRRORS accuracy rather than scoring 0 (which
 * would make an S impossible below tier 3) or 1 (which would gift 15 points
 * for a mechanic the class never showed). Same total, no free ride.
 *
 * HARD GATE: `sGate` is true only on a PERFECT class - every offered round
 * found on the FIRST tap, and at least MIN_ROUNDS_FOR_S of them - which is the
 * dossier's "S requires perfect first-tap accuracy". A failed gate caps at A.
 * The sub-median half of the dossier's S rule is already carried by the speed
 * term, which cannot reach .92 composite on slow finds.
 *
 * FLAVOUR XP: +2 per sub-second find, +1 per relocated round still cleared,
 * cap 15 (the shell clamps to 15 as well). Game-owned, never composite; the
 * XP table itself is C#'s.
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_ACCURACY: 0.4,
  W_SPEED: 0.25,
  W_CLEAR: 0.2,
  W_TRACK: 0.15,

  /** Median time-to-find (ms) that earns the FULL speed term, per gradeTier.
   *  Slower tiers get a bigger grid but a longer round, so the target tightens
   *  only gently: the top tier is meant to be won by tracking, not by twitch. */
  MEDIAN_TARGET_MS: Object.freeze({ 1: 2200, 2: 2000, 3: 1900, 4: 1800 }),
  /** Anything at or under this is a full mark; anything at or over
   *  target x SLOW_MULT scores nothing. */
  FAST_MS: 900,
  SLOW_MULT: 2.4,

  /** Post-relocation recovery target (ms) - the tap AFTER the tile moved. */
  TRACK_TARGET_MS: Object.freeze({ 1: 2600, 2: 2600, 3: 2400, 4: 2200 }),
  TRACK_SPLIT: 0.5,                 // half "did you still find it", half "how fast"

  MIN_ROUNDS_FOR_S: 5,
  FLAVOR_SUBSECOND: 2,
  FLAVOR_TRACK: 1,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }
function n0(v) { return Math.max(0, Number(v) || 0); }

/** A latency term: FAST_MS or better = 1, target x SLOW_MULT or worse = 0. */
export function latencyTerm(ms, targetMs) {
  const t = n0(targetMs);
  const v = Number(ms);
  if (!t || !Number.isFinite(v) || v <= 0) return 0;
  const slow = t * GRADE.SLOW_MULT;
  if (v <= GRADE.FAST_MS) return 1;
  if (v >= slow) return 0;
  return clamp01((slow - v) / (slow - GRADE.FAST_MS));
}

/** The median of a list of latencies (ms). PURE; [] -> 0. */
export function median(list) {
  const a = (list || []).map(Number).filter((v) => Number.isFinite(v) && v > 0).sort((x, y) => x - y);
  if (!a.length) return 0;
  const mid = a.length >> 1;
  return a.length % 2 ? a[mid] : Math.round((a[mid - 1] + a[mid]) / 2);
}

/**
 * @param {Object} i
 *   gradeTier        1..4
 *   roundsOffered    rounds the class actually dealt (a round the bell cut
 *                    short is NOT offered - see index.js)
 *   roundsCleared    rounds where the odd tile was found at all
 *   firstTapFinds    rounds found with NO wrong tap first
 *   findTimes        [ms] time-to-find for every cleared round
 *   relocatedRounds  rounds that carried at least one relocation
 *   relocatedCleared relocated rounds that were still cleared
 *   recoveryTimes    [ms] tap latency measured from the LAST relocation
 * @returns {{composite:number, terms:Object, median:number, raw:number}}
 */
export function compositeFor(i = {}) {
  const tier = tierOf(i.gradeTier);
  const offered = n0(i.roundsOffered);
  const cleared = Math.min(offered, n0(i.roundsCleared));
  const first = Math.min(cleared, n0(i.firstTapFinds));

  const accuracy = offered > 0 ? clamp01(first / offered) : 0;
  const clear = offered > 0 ? clamp01(cleared / offered) : 0;

  const med = median(i.findTimes);
  const speed = latencyTerm(med, GRADE.MEDIAN_TARGET_MS[tier]);

  const relocated = n0(i.relocatedRounds);
  const relocCleared = Math.min(relocated, n0(i.relocatedCleared));
  const recovery = median(i.recoveryTimes);
  const track = relocated > 0
    ? clamp01(GRADE.TRACK_SPLIT * (relocCleared / relocated)
      + (1 - GRADE.TRACK_SPLIT) * latencyTerm(recovery, GRADE.TRACK_TARGET_MS[tier]))
    : accuracy;

  const raw = GRADE.W_ACCURACY * accuracy + GRADE.W_SPEED * speed
    + GRADE.W_CLEAR * clear + GRADE.W_TRACK * track;

  return {
    composite: clamp01(raw),
    raw: clamp01(raw),
    median: med,
    recovery,
    terms: { accuracy, speed, clear, track },
    target: GRADE.MEDIAN_TARGET_MS[tier],
  };
}

/**
 * Declared gates. A perfect class only: every offered round found on the FIRST
 * tap, over at least MIN_ROUNDS_FOR_S rounds. Anything less caps at A.
 */
export function hardGates(i = {}) {
  const offered = n0(i.roundsOffered);
  const first = n0(i.firstTapFinds);
  return { sGate: offered >= GRADE.MIN_ROUNDS_FOR_S && first === offered && offered > 0 };
}

/** +2 per sub-second find, +1 per relocated round still cleared, cap 15. */
export function flavorXp(subSecondFinds, relocatedCleared) {
  const fx = n0(subSecondFinds) * GRADE.FLAVOR_SUBSECOND + n0(relocatedCleared) * GRADE.FLAVOR_TRACK;
  return Math.min(GRADE.FLAVOR_CAP, Math.round(fx));
}

export default { compositeFor, hardGates, flavorXp, latencyTerm, median, clamp01, GRADE };
