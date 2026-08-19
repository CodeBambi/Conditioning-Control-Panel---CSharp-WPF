/* ============================================================================
 * games/impulse-control/stream.js - the assessment schedule. PURE (no DOM).
 *
 * One class = one seeded stream. Because the seed is the shell's
 * `<utcDateSeed>|impulse_control|t<tier>`, every player faces the IDENTICAL
 * assessment that day (the dossier's social hook) and a replay is the same test.
 *
 * SHAPE (dossier "Frame"), scaled to fit classSpec.timeBudgetSec:
 *   baseline   ~12s, 8 stimuli, EFFECT-FREE (the honest control + the FTUE)
 *   block 1..3 ~equal slices, the stream proper, breather between
 *   hold       ~15s composure hold: rapid fire, 70% NO-GO, dials at their peak
 *
 * TIER DISCIPLINE (GROUND-RULES §6 - effects ARE the difficulty). The classic
 * knobs in TIERS move LAST and least: response window 900->550ms, NO-GO share
 * .20->.40, twin similarity .5->.9. What actually moves first is `heat` and the
 * lie dials in lies.js. Tier 1 is an honest test: plainShare 1 = no lies at all.
 *
 * RHYTHM THEFT is a stream knob, not an engine effect: `jitter` widens with tier
 * so no cadence can form and the other lies have arrhythmic ground to exploit.
 *
 * DETERMINISM. Everything here is derived from the injected rng, in a fixed
 * order, so the same seed yields byte-identical records. Response timing never
 * feeds back into the schedule (a fast player faces the same cues as a slow one);
 * only how MANY of them get played depends on the clock.
 * ==========================================================================*/

/** Per-tier dials. `heat` is the engine scalar; the rest are the classic knobs. */
export const TIERS = Object.freeze({
  1: Object.freeze({ windowMs: 900, nogoShare: 0.20, similarity: 0.5, jitter: [700, 1300], plainFloor: 1.00, heat: 0.18 }),
  2: Object.freeze({ windowMs: 900, nogoShare: 0.25, similarity: 0.6, jitter: [550, 1500], plainFloor: 0.80, heat: 0.38 }),
  3: Object.freeze({ windowMs: 750, nogoShare: 0.30, similarity: 0.7, jitter: [450, 1650], plainFloor: 0.62, heat: 0.62 }),
  4: Object.freeze({ windowMs: 550, nogoShare: 0.40, similarity: 0.9, jitter: [400, 1800], plainFloor: 0.45, heat: 0.85 }),
});

export const BASELINE_STIMULI = 8;
export const BASELINE_WINDOW_MS = 1200;
export const BASELINE_JITTER = Object.freeze([800, 1300]);
export const BASELINE_NOGO = 0.25;        // a calibration block still needs a reason to withhold
export const HOLD_NOGO_SHARE = 0.70;      // dossier: the finale is a restraint test
export const HOLD_JITTER = Object.freeze([300, 720]);
export const BREATHER_MS = 3000;
export const LATE_GRACE_MS = 240;         // in-aperture presses after the window close
export const FEEDBACK_GAP_MS = 260;       // dissolve + feedback before the next foreperiod

/** Phase keys, in order. `assess` covers the three graded blocks. */
export const PHASES = Object.freeze(['baseline', 'assess', 'hold', 'debrief']);

export function tierDials(gradeTier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1)));
  return TIERS[t];
}

function clamp(n, lo, hi) { return n < lo ? lo : n > hi ? hi : n; }

/**
 * The phase plan (durations only) for a time budget.
 * Baseline and hold are fixed costs; the three blocks split what is left.
 */
export function buildPlan(timeBudgetSec) {
  const budgetMs = clamp(Math.round((Number(timeBudgetSec) || 90) * 1000), 45000, 300000);
  const baselineMs = clamp(Math.round(budgetMs * 0.16), 12000, 20000);
  const holdMs = clamp(Math.round(budgetMs * 0.17), 10000, 22000);
  const breathers = 2 * BREATHER_MS;
  const blocksMs = Math.max(9000, budgetMs - baselineMs - holdMs - breathers);
  const blockMs = Math.round(blocksMs / 3);
  return {
    budgetMs,
    baselineMs,
    blockMs,
    breatherMs: BREATHER_MS,
    holdMs,
    totalMs: baselineMs + blockMs * 3 + breathers + holdMs,
  };
}

/**
 * NO-GO placement: a shuffled bag under two authored constraints -
 * the first stimulus of a phase is always a GO (the player must be shown the
 * mapping before they are trapped by it), and never three NO-GOs in a row
 * (a run of withholds trains the wrong reflex and reads as a dead aperture).
 */
export function placeNogo(count, share, rand) {
  const n = Math.max(0, Math.min(count, Math.round(count * clamp(share, 0, 1))));
  const bag = new Array(count).fill(false);
  for (let i = 0; i < n; i++) bag[i] = true;
  // Fisher-Yates on the seeded stream.
  for (let i = bag.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.floor(rand() * (i + 1)));
    const t = bag[i]; bag[i] = bag[j]; bag[j] = t;
  }
  if (count > 0 && bag[0]) {
    const swap = bag.findIndex((v, i) => i > 0 && !v);
    if (swap > 0) { bag[0] = false; bag[swap] = true; }
  }
  for (let i = 2; i < bag.length; i++) {
    if (!(bag[i] && bag[i - 1] && bag[i - 2])) continue;
    // Move it to a slot with no NO-GO neighbour, so the repair cannot make a new
    // run somewhere else. With nowhere safe to put it, drop it (the share is a
    // preference; "never three withholds in a row" is the invariant).
    let moved = false;
    for (let j = 1; j < bag.length; j++) {
      if (bag[j] || bag[j - 1] || bag[j + 1]) continue;
      bag[i] = false; bag[j] = true; moved = true; break;
    }
    if (!moved) bag[i] = false;
  }
  return bag;
}

/**
 * Build the whole stream.
 * @param {Object} o
 * @param {Function} o.rng                 seeded 0..1 stream (REQUIRED for determinism)
 * @param {number} o.gradeTier             1..4
 * @param {number} o.timeBudgetSec
 * @returns {{tier:number, dials:Object, plan:Object, records:Array, counts:Object}}
 *
 * A record is the abstract stimulus the dossier specifies, plus scheduling:
 *   { i, phase:'baseline'|'assess'|'hold', block:0..3, cls:'go'|'nogo',
 *     similarity, foreperiodMs, windowMs, presentMs, lieEligible, isFirst }
 * `render` is NOT decided here - stimset.js dresses the record at paint time, so
 * a missing word pool or media pool cannot invalidate the schedule.
 */
export function buildStream({ rng, gradeTier, timeBudgetSec } = {}) {
  const rand = typeof rng === 'function' ? rng : Math.random;
  const tier = Math.max(1, Math.min(4, Math.round(Number(gradeTier) || 1)));
  const dials = tierDials(tier);
  const plan = buildPlan(timeBudgetSec);
  const records = [];
  let i = 0;

  const meanForeperiod = (lo, hi) => (lo + hi) / 2;
  const foreperiod = (lo, hi) => Math.round(lo + rand() * (hi - lo));

  /** How many stimuli fit a phase slice, at that phase's mean trial cost. */
  function countFor(durMs, jitter, windowMs) {
    const cost = meanForeperiod(jitter[0], jitter[1]) + windowMs * 0.7 + FEEDBACK_GAP_MS;
    return Math.max(3, Math.round(durMs / cost));
  }

  function emit(phase, block, count, opts) {
    const bag = placeNogo(count, opts.nogoShare, rand);
    for (let k = 0; k < count; k++) {
      const cls = bag[k] ? 'nogo' : 'go';
      // The plain-share ramp (DTRH): lies stay EVENTS, not weather. Share eases
      // from .85 toward the tier's floor across the graded blocks.
      const plainShare = opts.plainShare == null ? 1 : opts.plainShare;
      const lieEligible = plainShare >= 1 ? false : rand() > plainShare;
      records.push({
        i: i++,
        phase,
        block,
        cls,
        similarity: opts.similarity,
        foreperiodMs: foreperiod(opts.jitter[0], opts.jitter[1]),
        windowMs: opts.windowMs,
        presentMs: opts.windowMs + LATE_GRACE_MS,
        lieEligible,
        isFirst: k === 0,
      });
    }
  }

  /* ---- 1. baseline block: effect-free, generous, unscored --------------- */
  emit('baseline', 0, BASELINE_STIMULI, {
    nogoShare: BASELINE_NOGO,
    similarity: 0.35,                       // calibration twins are obvious on purpose
    jitter: BASELINE_JITTER,
    windowMs: BASELINE_WINDOW_MS,
    plainShare: 1,                          // no lies, ever, in the honest control
  });

  /* ---- 2. three assessment blocks -------------------------------------- */
  for (let b = 1; b <= 3; b++) {
    const p = (b - 1) / 2;                                     // 0, .5, 1
    const plainShare = 0.85 + (dials.plainFloor - 0.85) * p;
    // NO-GO share ramps 20%->40% of the way toward the tier ceiling across blocks
    // (classic knob: it rises only after the effect dials, which heat handles).
    const nogoShare = dials.nogoShare * (0.85 + 0.15 * p);
    emit('assess', b, countFor(plan.blockMs, dials.jitter, dials.windowMs), {
      nogoShare,
      similarity: dials.similarity * (0.9 + 0.1 * p),
      jitter: dials.jitter,
      windowMs: dials.windowMs,
      plainShare: dials.plainFloor >= 1 ? 1 : plainShare,
    });
  }

  /* ---- 3. composure hold ----------------------------------------------- */
  emit('hold', 4, countFor(plan.holdMs, HOLD_JITTER, dials.windowMs), {
    nogoShare: HOLD_NOGO_SHARE,
    similarity: dials.similarity,             // max similarity for the tier
    jitter: HOLD_JITTER,
    windowMs: dials.windowMs,
    plainShare: dials.plainFloor >= 1 ? 1 : Math.max(0.3, dials.plainFloor - 0.1),
  });

  const counts = records.reduce((acc, r) => {
    acc.total++;
    if (r.cls === 'go') acc.go++; else acc.nogo++;
    if (r.lieEligible) acc.lieEligible++;
    acc.byPhase[r.phase] = (acc.byPhase[r.phase] || 0) + 1;
    return acc;
  }, { total: 0, go: 0, nogo: 0, lieEligible: 0, byPhase: {} });

  return { tier, dials, plan, records, counts };
}

/** Records of one phase (and block, when given). */
export function slice(records, phase, block) {
  return (records || []).filter((r) => r.phase === phase && (block == null || r.block === block));
}

export default buildStream;
