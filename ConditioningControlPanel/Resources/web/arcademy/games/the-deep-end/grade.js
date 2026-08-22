/* ============================================================================
 * games/the-deep-end/grade.js - the composite THE DEEP END hands to the ONE
 * shared rubric (core/grades.js via ctx.endClass). PURE.
 *
 * A game never grades itself: this file turns the dive's inputs into a 0..1
 * composite, a declared hard gate and the flavour XP, and the SHELL maps the
 * composite to S/A/B/C (>= .92 S / .75 A / .50 B) and applies every cap.
 *
 * THE COMPOSITE (pinned by the contract):
 *   .55 depth       best dive's deepest tier against the S threshold per
 *                   gradeTier ({1:7, 2:8, 3:9, 4:10}) - the primary input
 *   .15 chains      chained merges (links beyond the first, per swipe)
 *   .15 efficiency  merges per swipe
 *   .15 survival    board unlocked at the bell, or the ceiling reached
 *   x 0.92 ^ resurfaces   the resurface tax (a locked board is not free)
 *
 * HARD GATE: `sGate` is true only on the 4x4 board. 5x5 is the comfort dial,
 * and the shell caps a failed gate at A - so the 5x5 player can relax without
 * the S being devalued for everyone else.
 *
 * FLAVOUR XP: +3 per NEW lifetime tier reached this class, cap 15. Game-owned,
 * never composite (the XP table itself is C#'s).
 * ==========================================================================*/

export const GRADE = Object.freeze({
  W_DEPTH: 0.55,
  W_CHAINS: 0.15,
  W_EFFICIENCY: 0.15,
  W_SURVIVAL: 0.15,
  RESURFACE_TAX: 0.92,
  /** Best-dive deepest tier that earns the full depth term, per gradeTier. */
  S_TIER: Object.freeze({ 1: 7, 2: 8, 3: 9, 4: 10 }),
  /** Chained links (merges beyond the first in a swipe) for a full chains term. */
  CHAIN_LINKS_TARGET: 10,
  /** Merges per swipe for a full efficiency term (a clean 2048 line runs ~.6). */
  EFFICIENCY_TARGET: 0.7,
  FLAVOR_PER_TIER: 3,
  FLAVOR_CAP: 15,
});

export function clamp01(v) { const n = Number(v); return !Number.isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function tierOf(gradeTier) { return Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1))); }

/**
 * @param {Object} i
 *   gradeTier     1..4
 *   bestDeepest   deepest tier of the best dive (1..11)
 *   chainLinks    sum over swipes of max(0, merges - 1)
 *   merges        total merges this class
 *   swipes        real (non-no-op) moves this class
 *   survived      board unlocked at the bell, or the ceiling reached
 *   resurfaces    locked boards drained and re-dived
 * @returns {{composite:number, terms:Object, tax:number, raw:number}}
 */
export function compositeFor(i = {}) {
  const tier = tierOf(i.gradeTier);
  const sTier = GRADE.S_TIER[tier];
  const best = Math.max(0, Number(i.bestDeepest) || 0);
  const depth = clamp01((best - 1) / Math.max(1, sTier - 1));
  const chains = clamp01((Number(i.chainLinks) || 0) / GRADE.CHAIN_LINKS_TARGET);
  const swipes = Math.max(0, Number(i.swipes) || 0);
  const merges = Math.max(0, Number(i.merges) || 0);
  const efficiency = swipes > 0 ? clamp01((merges / swipes) / GRADE.EFFICIENCY_TARGET) : 0;
  const survival = i.survived ? 1 : 0;
  const raw = GRADE.W_DEPTH * depth + GRADE.W_CHAINS * chains
    + GRADE.W_EFFICIENCY * efficiency + GRADE.W_SURVIVAL * survival;
  const resurfaces = Math.max(0, Math.round(Number(i.resurfaces) || 0));
  const tax = Math.pow(GRADE.RESURFACE_TAX, resurfaces);
  return {
    composite: clamp01(raw * tax),
    raw: clamp01(raw),
    tax,
    terms: { depth, chains, efficiency, survival },
    sTier,
  };
}

/** Declared gates: only the 4x4 board can earn an S (the 5x5 comfort dial caps at A). */
export function hardGates(size) {
  return { sGate: Number(size) === 4 };
}

/** +3 per NEW lifetime tier this class, cap 15. */
export function flavorXp(lifetimeBefore, deepestNow) {
  const before = Math.max(0, Number(lifetimeBefore) || 0);
  const now = Math.max(0, Number(deepestNow) || 0);
  const fresh = Math.max(0, now - before);
  return Math.min(GRADE.FLAVOR_CAP, fresh * GRADE.FLAVOR_PER_TIER);
}

export default { compositeFor, hardGates, flavorXp, GRADE };
