/* ============================================================================
 * games/misdirection/grade.js - the composite MISDIRECTION hands to the ONE
 * shared rubric (core/grades.js via ctx.endClass). PURE.
 *
 * A game never grades itself: this file turns the table's inputs into a 0..1
 * composite, a declared hard gate and the flavour XP, and the SHELL maps the
 * composite to S/A/B/C and applies every cap.
 *
 * ---------------------------------------------------------------------------
 * GREED IS SCORED UPWARD ONLY (the contract's ruling on the dossier's open
 * question). The grade is BUILT out of accuracy and latency alone:
 *
 *   base   = .65 x occlusion-weighted accuracy  +  .35 x latency
 *   ride   = up to +.10, scaled by the deepest ride actually BANKED
 *   composite = clamp01(base + ride)
 *
 * A player who never stakes a thing can still reach 1.00 on base alone, and a
 * player who rides five deep and busts loses NOTHING from the grade - the pot
 * is a separate, additive economy. That is the only shape that satisfies both
 * "confirm the rubric barrier accepts an upward-only input" and "a cautious
 * player is not punished for caution".
 *
 * OCCLUSION WEIGHTING (dossier): a pick on a shuffle whose scheduled
 * distraction load exceeded the grade baseline weighs 1.5x; a clean remedial
 * round weighs 0.75x; everything else weighs 1. Rounds VOIDED by a suspend
 * (panic, a mandatory video, an audio-only flip) are excluded from every
 * denominator - they are never dealt into this file at all.
 *
 * HARD GATE - `sGate` is the critic's top fix, priced: if the class ever hid a
 * link (a glitch swap or a blackout beat that actually covered the target's
 * move) and the player never once called one of those rounds correctly, the
 * class caps at A. S certifies tracking THROUGH deception, never a lucky
 * guess. A tier-1 class hides nothing, so the gate passes trivially there.
 *
 * FLAVOUR XP: the BANKED pot, converted at a fixed rate and capped at 15
 * (the shell's own cap). Game-owned, never composite; the XP table is C#'s.
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_ACCURACY: 0.65,
  W_LATENCY: 0.35,
  /** The whole of the greed bonus. Additive, so it can only ever help. */
  RIDE_BONUS_MAX: 0.10,
  /** Deepest banked ride that earns the full bonus (mirrors PLAYTEST.RIDE_CAP). */
  RIDE_BONUS_FULL: 5,

  /** Weight of a pick under above-baseline distraction load. */
  W_HEAVY: 1.5,
  /** Weight of a pick in a clean remedial round. */
  W_REMEDIAL: 0.75,
  /** Weight of an ordinary pick. */
  W_PLAIN: 1,

  /** The honest pick window (ms). Anything at or past it is a timeout. */
  WINDOW_MS: 4000,
  /** A mean pick at or under this earns the full latency term. */
  LATENCY_FLOOR_MS: 700,

  /** Banked pot per point of flavour XP, and the shell's cap. */
  FLAVOR_PER_XP: 2,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }

/** The rubric weight of one round. */
export function weightOf(round) {
  if (!round) return GRADE.W_PLAIN;
  if (round.remedial) return GRADE.W_REMEDIAL;
  if (round.heavy) return GRADE.W_HEAVY;
  return GRADE.W_PLAIN;
}

/**
 * @param {Object} i
 *   rounds       [{correct, latencyMs, heavy, remedial, blind, voided}] - the
 *                graded rounds in order. `voided` rows are ignored here as
 *                well as by the caller, so a double-count is impossible.
 *   deepestBanked  deepest ride depth that was actually BANKED (0..5)
 *   gradeTier      1..4 (carried for reporting; the rubric is tier-free)
 * @returns {{composite, base, ride, terms, counts}}
 */
export function compositeFor(i = {}) {
  const rounds = (Array.isArray(i.rounds) ? i.rounds : []).filter((r) => r && !r.voided);

  let wSum = 0;
  let wHit = 0;
  let latSum = 0;
  let latN = 0;
  let hits = 0;
  let blindDealt = 0;
  let blindHits = 0;
  for (const r of rounds) {
    const w = weightOf(r);
    wSum += w;
    if (r.correct) {
      wHit += w;
      hits += 1;
      const ms = Number(r.latencyMs);
      if (Number.isFinite(ms) && ms >= 0) { latSum += Math.min(GRADE.WINDOW_MS, ms); latN += 1; }
    }
    if (r.blind) {
      blindDealt += 1;
      if (r.correct) blindHits += 1;
    }
  }

  const accuracy = wSum > 0 ? clamp01(wHit / wSum) : 0;
  const meanMs = latN > 0 ? (latSum / latN) : GRADE.WINDOW_MS;
  const span = Math.max(1, GRADE.WINDOW_MS - GRADE.LATENCY_FLOOR_MS);
  /* No correct pick at all = no latency evidence = no latency credit. */
  const latency = latN > 0 ? clamp01((GRADE.WINDOW_MS - meanMs) / span) : 0;

  const base = GRADE.W_ACCURACY * accuracy + GRADE.W_LATENCY * latency;
  const deepest = Math.max(0, Math.round(Number(i.deepestBanked) || 0));
  const ride = GRADE.RIDE_BONUS_MAX * clamp01(deepest / GRADE.RIDE_BONUS_FULL);

  return {
    composite: clamp01(base + ride),
    base: clamp01(base),
    ride,
    terms: { accuracy, latency },
    counts: {
      graded: rounds.length,
      hits,
      meanLatencyMs: latN > 0 ? Math.round(meanMs) : null,
      weighted: wSum,
      weightedHits: wHit,
      blindDealt,
      blindHits,
      deepestBanked: deepest,
    },
  };
}

/**
 * Declared gates. `sGate` false caps the class at A.
 * @param {number} blindDealt rounds that actually hid a link
 * @param {number} blindHits  those the player called correctly
 */
export function hardGates(blindDealt, blindHits) {
  const dealt = Math.max(0, Math.round(Number(blindDealt) || 0));
  const hit = Math.max(0, Math.round(Number(blindHits) || 0));
  return { sGate: dealt === 0 ? true : hit > 0 };
}

/** The banked pot, converted and capped. Never negative, never over 15. */
export function flavorXp(banked) {
  const b = Math.max(0, Number(banked) || 0);
  return Math.max(0, Math.min(GRADE.FLAVOR_CAP, Math.floor(b / GRADE.FLAVOR_PER_XP)));
}

export default { compositeFor, hardGates, flavorXp, weightOf, GRADE };
