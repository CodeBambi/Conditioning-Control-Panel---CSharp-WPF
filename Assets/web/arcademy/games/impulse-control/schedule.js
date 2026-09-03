/* ============================================================================
 * games/impulse-control/schedule.js - THE DROP TUBE's bubble plan. PURE.
 *
 * One seeded run of the tube: which bubbles fall, in what order, how fast the
 * slide is at that point of the class, and how long each reveal window stays
 * open. No DOM, no clock, no ctx - the scratch harness drives this directly.
 *
 * THE RAMP (owner spec): the slide down the spiral starts at 2000ms and
 * tightens to 500ms by the end of the class. Progress drives the ramp; the
 * grade tier does NOT touch the slide (everyone feels the same tube) - tier
 * tightens the REVEAL WINDOW and raises the denied share instead, so a Year 4
 * class is harder at the moment of truth, not in transit.
 *
 * PLAN GUARANTEES (all tested):
 *   - the first TEACH_GOOD bubbles are good (the class teaches by doing),
 *   - never more than 2 denied in a row,
 *   - the plan fits the time budget under the worst case (every window runs
 *     to its full length),
 *   - the same seed always deals the same plan.
 * ==========================================================================*/

/* ------------------------- the constants block (playtest-tunable) -------- */
export const SLIDE_START_MS = 2000;   // owner spec: slow entry
export const SLIDE_END_MS = 500;      // owner spec: end-of-class drop
export const DENIED_HOLD_MS = 2000;   // owner spec: the X must survive 2s
export const GOOD_WINDOW_BASE_MS = 1600;   // tier 1 reveal window
export const GOOD_WINDOW_TIER_MS = 100;    // -100ms per tier above 1
export const GAP_MS = 350;            // basin settle between bubbles
export const LOAD_MS = 260;           // the mouth "load" beat before the slide
export const TEACH_GOOD = 2;          // first bubbles are always poppable
export const DENIED_BASE = 0.20;      // tier 1 denied share
export const DENIED_TIER = 0.03;      // +3% per tier above 1
export const MAX_DENIED_RUN = 2;

export const FLAVORS = Object.freeze(['flash', 'spiral', 'sub']);

/** Smoothstep - the ramp eases in and out instead of jerking linear. */
export function ease(p) {
  const x = p < 0 ? 0 : p > 1 ? 1 : p;
  return x * x * (3 - 2 * x);
}

/** Slide duration at class progress p (0..1). 2000 -> 500, eased. */
export function slideMsAt(p) {
  return Math.round(SLIDE_START_MS - (SLIDE_START_MS - SLIDE_END_MS) * ease(p));
}

/** The reveal window for a good bubble at this tier. */
export function goodWindowMs(gradeTier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1)));
  return GOOD_WINDOW_BASE_MS - GOOD_WINDOW_TIER_MS * (t - 1);
}

/** Denied share at this tier (clamped sane). */
export function deniedShare(gradeTier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1)));
  const s = DENIED_BASE + DENIED_TIER * (t - 1);
  return s < 0 ? 0 : s > 0.4 ? 0.4 : s;
}

/**
 * Deal the class's bubble plan.
 * @param {Object} o {rng, gradeTier, timeBudgetSec}
 * @returns {{bubbles:Array, counts:{total,good,denied,byFlavor}}}
 *
 * Each bubble: { i, kind:'good'|'denied', flavor:'flash'|'spiral'|'sub'|null,
 *               slideMs, windowMs, gapMs, progress }
 */
export function buildPlan(o = {}) {
  const rng = typeof o.rng === 'function' ? o.rng : Math.random;
  const tier = Math.max(1, Math.min(4, Math.round(Number(o.gradeTier) || 1)));
  const budgetMs = Math.max(45, Math.min(300, Number(o.timeBudgetSec) || 90)) * 1000;
  const share = deniedShare(tier);
  const winGood = goodWindowMs(tier);

  /* First pass: how many bubbles fit? Worst case per bubble is
     load + slide + full window + gap; slide shrinks with progress, so solve
     iteratively against a provisional count and then rebuild with the final. */
  let total = 8;
  for (let iter = 0; iter < 6; iter++) {
    let t = 0; let n = 0;
    while (t < budgetMs - 2500) {
      const p = total <= 1 ? 0 : n / (total - 1);
      const slide = slideMsAt(p);
      const win = Math.max(winGood, DENIED_HOLD_MS); // worst case either kind
      t += LOAD_MS + slide + win + GAP_MS;
      if (t >= budgetMs - 2500) break;
      n++;
    }
    if (n === total || n < 1) { total = Math.max(1, n); break; }
    total = Math.max(1, n);
  }

  /* Second pass: deal kinds and flavors. */
  const bubbles = [];
  let deniedRun = 0;
  let deniedCount = 0;
  const flavorBag = [];                  // rotate so flavors stay balanced
  const counts = { total, good: 0, denied: 0, byFlavor: { flash: 0, spiral: 0, sub: 0 } };
  for (let i = 0; i < total; i++) {
    const progress = total <= 1 ? 0 : i / (total - 1);
    let kind = 'good';
    if (i >= TEACH_GOOD && deniedRun < MAX_DENIED_RUN) {
      /* keep the realised share near the target as the plan fills */
      const want = share * (i + 1);
      const bias = want - deniedCount;   // >0: behind target, more likely denied
      if (rng() < share + bias * 0.35) kind = 'denied';
    }
    let flavor = null;
    if (kind === 'denied') {
      deniedRun++; deniedCount++; counts.denied++;
    } else {
      deniedRun = 0; counts.good++;
      if (!flavorBag.length) {
        const shuffled = FLAVORS.slice();
        for (let j = shuffled.length - 1; j > 0; j--) {
          const k = Math.floor(rng() * (j + 1));
          const tmp = shuffled[j]; shuffled[j] = shuffled[k]; shuffled[k] = tmp;
        }
        flavorBag.push(...shuffled);
      }
      flavor = flavorBag.shift();
      counts.byFlavor[flavor]++;
    }
    bubbles.push({
      i, kind, flavor,
      slideMs: slideMsAt(progress),
      windowMs: kind === 'denied' ? DENIED_HOLD_MS : winGood,
      gapMs: GAP_MS,
      progress,
    });
  }

  /* A plan with zero denied teaches nothing - force the last-third bubble
     furthest from a denied neighbour to flip (only when the plan is long
     enough to afford it). */
  if (counts.denied === 0 && total >= 6) {
    const at = Math.max(TEACH_GOOD, total - 3);
    const b = bubbles[at];
    if (b.flavor) counts.byFlavor[b.flavor]--;
    b.kind = 'denied'; b.flavor = null; b.windowMs = DENIED_HOLD_MS;
    counts.denied = 1; counts.good -= 1;
  }

  return { bubbles, counts };
}
