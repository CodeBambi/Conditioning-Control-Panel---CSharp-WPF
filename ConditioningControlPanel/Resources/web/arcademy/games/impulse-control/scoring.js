/* ============================================================================
 * games/impulse-control/scoring.js - the game-specific inputs to the ONE shared
 * rubric. PURE (no DOM, no store, no engine).
 *
 * The game NEVER grades itself (core/grades.js owns S/A/B/C). What it owns is
 * (a) a weighted composite 0..1 and (b) the dual hard gate, per the dossier's
 * "Grading (S/A/B/C inputs)" section and SYNTHESIS #14:
 *
 *   composite = .4 restraint + .3 speed + .2 lieResistance + .1 goHitRate
 *   sGate     = falseAlarmRate <= .02  AND  speedIndex <= 1.05  AND  cleanErrors == 0
 *
 * The gate is the brief encoded: speed alone can never buy an S past sloppy
 * restraint, restraint alone can never buy it past hesitation. It is DECLARED on
 * every class (grades.js only counts gates a game declared) so a failed gate caps
 * the letter at A.
 *
 * BASELINE-RELATIVE SPEED (dossier "Baseline-relative scoring"). speedIndex is
 * session median RT / the player's PERSISTED baseline, so the game is fair on any
 * hardware and yesterday-you is the rival. First class has no persisted baseline:
 * it calibrates against its own baseline block (index ~1) and writes the number
 * to the per-game meta store (SYNTHESIS #15). Later classes decay the stored
 * number toward recent form so the bar tracks the player, not a lucky day.
 *
 * The baseline BLOCK is unscored: its RTs feed calibration only, never composite.
 * ==========================================================================*/

export const WEIGHTS = Object.freeze({ restraint: 0.4, speed: 0.3, lieResistance: 0.2, goHit: 0.1 });

/** S-gate thresholds - the dossier's numbers, in one place. */
export const S_GATE = Object.freeze({ maxFalseAlarmRate: 0.02, maxSpeedIndex: 1.05, maxCleanErrors: 0 });

/** Score curves: where a stat stops earning. */
export const CURVES = Object.freeze({
  farZero: 0.20,        // false-alarm rate at which the restraint term hits 0
  speedBest: 0.90,      // speedIndex at or under this = full speed marks
  speedZero: 1.40,      // speedIndex at or over this = no speed marks
  latePenalty: 0.5,     // a late press costs half a miss on the goHit term
});

/** Flavor XP (capped-flavor-bonus pattern; the shell caps the total at 15). */
export const FLAVOR_XP = Object.freeze({ personalBest: 8, zeroFalseAlarms: 7, cap: 15 });

/** Baseline decay: how much a new session moves a stored baseline. */
export const BASELINE = Object.freeze({
  minSamples: 4,           // fewer clean RTs than this = do not trust the session
  freshWeight: 0.25,       // a same-week session moves the baseline this much
  weeklyWeight: 0.25,      // ...plus this per week of staleness, to 1.0
  floorMs: 90,             // nobody's honest median is under this
  ceilMs: 1500,
});

const clamp01 = (n) => {
  const v = Number(n);
  return !Number.isFinite(v) ? 0 : v < 0 ? 0 : v > 1 ? 1 : v;
};
const num = (n, d) => (Number.isFinite(Number(n)) ? Number(n) : d);

/** Median of a numeric list (sorted copy; empty -> null). */
export function median(list) {
  const a = (Array.isArray(list) ? list : [])
    .filter((v) => typeof v === 'number' && Number.isFinite(v))
    .sort((x, y) => x - y);
  if (!a.length) return null;
  const m = a.length >> 1;
  return a.length % 2 ? a[m] : Math.round((a[m - 1] + a[m]) / 2);
}

/**
 * Aggregate the class's response log into the five dossier inputs.
 * @param {Object} tally  {goCount, hits, misses, lates, nogoCount, commissions,
 *                         isiCommissions, lieErrors, lieTrials, cleanErrors,
 *                         cleanTrials, rts:[]}
 * @param {number|null} baselineMs  the PERSISTED baseline (null on a first class)
 * @param {number|null} sessionBaselineMs  the baseline block's median
 */
export function metricsFrom(tally, baselineMs, sessionBaselineMs) {
  const t = tally || {};
  const goCount = Math.max(0, num(t.goCount, 0));
  const nogoCount = Math.max(0, num(t.nogoCount, 0));
  const hits = Math.max(0, num(t.hits, 0));
  const lates = Math.max(0, num(t.lates, 0));
  const commissions = Math.max(0, num(t.commissions, 0));
  const isiCommissions = Math.max(0, num(t.isiCommissions, 0));
  const rts = Array.isArray(t.rts) ? t.rts : [];

  const goHitRate = goCount ? clamp01(hits / goCount) : 1;
  const lateRate = goCount ? clamp01(lates / goCount) : 0;
  // Restraint axis: NO-GO commissions AND presses into the rest gap, over the
  // withhold opportunities. ISI presses have no denominator of their own, so they
  // ride the same one - pressing at nothing is a restraint failure by definition.
  const falseAlarmRate = clamp01((commissions + isiCommissions) / Math.max(1, nogoCount));

  const medianRt = median(rts);
  // First class: no persisted yardstick, so the session's own honest control is
  // the yardstick (index lands near 1 by construction - calibration, not credit).
  const yard = num(baselineMs, 0) > 0 ? Number(baselineMs)
    : (num(sessionBaselineMs, 0) > 0 ? Number(sessionBaselineMs) : null);
  const speedIndex = (medianRt != null && yard) ? medianRt / yard : 1;

  const lieTrials = Math.max(0, num(t.lieTrials, 0));
  const cleanTrials = Math.max(0, num(t.cleanTrials, 0));
  const lieErrors = Math.max(0, num(t.lieErrors, 0));
  const cleanErrors = Math.max(0, num(t.cleanErrors, 0));
  const lieErrRate = lieTrials ? lieErrors / lieTrials : 0;
  const cleanErrRate = cleanTrials ? cleanErrors / cleanTrials : 0;
  // Resistance = how much WORSE the lies made you than your own clean baseline.
  // No lies fired (tier 1) -> nothing to resist -> full marks, never a penalty.
  const lieResistance = lieTrials ? clamp01(1 - Math.max(0, lieErrRate - cleanErrRate)) : 1;

  return {
    goCount, nogoCount, hits, lates, commissions, isiCommissions,
    goHitRate, lateRate, falseAlarmRate, medianRt, baselineMs: yard, speedIndex,
    lieTrials, cleanTrials, lieErrors, cleanErrors, lieErrRate, cleanErrRate, lieResistance,
    bestRt: rts.length ? Math.min(...rts) : null,
  };
}

/** The four weighted terms, each 0..1. */
export function terms(m) {
  const restraint = clamp01(1 - (m.falseAlarmRate / CURVES.farZero));
  const speed = clamp01((CURVES.speedZero - m.speedIndex) / (CURVES.speedZero - CURVES.speedBest));
  const lieResistance = clamp01(m.lieResistance);
  const goHit = clamp01(m.goHitRate - CURVES.latePenalty * m.lateRate);
  return { restraint, speed, lieResistance, goHit };
}

/** The weighted composite 0..1 handed to core/grades.js. */
export function composite(m) {
  const s = terms(m);
  return clamp01(
    s.restraint * WEIGHTS.restraint
    + s.speed * WEIGHTS.speed
    + s.lieResistance * WEIGHTS.lieResistance
    + s.goHit * WEIGHTS.goHit
  );
}

/**
 * The DUAL hard gate (SYNTHESIS #14 made this legal; it is this game's identity).
 * Declared on every class, true only when BOTH axes hold and no clean error was
 * made. `reasons` is what the debrief shows when it failed.
 */
export function sGate(m) {
  const reasons = [];
  if (m.falseAlarmRate > S_GATE.maxFalseAlarmRate) reasons.push('restraint');
  if (m.speedIndex > S_GATE.maxSpeedIndex) reasons.push('speed');
  if (m.cleanErrors > S_GATE.maxCleanErrors) reasons.push('clean_errors');
  return { ok: reasons.length === 0, reasons };
}

/**
 * Capped flavor bonuses that stay INSIDE the class award (SYNTHESIS #4 - the
 * shell owns the XP table; a game may only contribute a capped flavor bonus).
 */
export function flavorXp(m, prevBestMedianMs) {
  let xp = 0;
  const reasons = [];
  const prev = num(prevBestMedianMs, 0);
  if (m.medianRt != null && (!prev || m.medianRt < prev)) {
    xp += FLAVOR_XP.personalBest; reasons.push('personal_best');
  }
  if (m.commissions + m.isiCommissions === 0 && m.nogoCount > 0) {
    xp += FLAVOR_XP.zeroFalseAlarms; reasons.push('zero_false_alarms');
  }
  return { xp: Math.min(FLAVOR_XP.cap, xp), reasons };
}

/**
 * Fold this session into the persisted baseline (weekly decay toward recent form).
 * @param {Object} meta  the per-game meta row {baselineMs, baselineUpdatedAt, ...}
 * @param {number|null} sessionMedianMs  the baseline BLOCK median (the honest control)
 * @param {number} nowMs
 * @param {boolean=} reset  recalibrate: take the session verbatim
 * @returns {{baselineMs:number, weight:number, established:boolean, changed:boolean}|null}
 */
export function foldBaseline(meta, sessionMedianMs, nowMs, reset) {
  const s = num(sessionMedianMs, 0);
  if (!(s > 0)) return null;
  const session = Math.round(Math.max(BASELINE.floorMs, Math.min(BASELINE.ceilMs, s)));
  const m = meta || {};
  const prev = num(m.baselineMs, 0);
  if (reset || !(prev > 0)) {
    return { baselineMs: session, weight: 1, established: !(prev > 0) || !!reset, changed: true };
  }
  const then = num(m.baselineUpdatedAt, 0);
  const weeks = then > 0 ? Math.max(0, (num(nowMs, Date.now()) - then) / 604800000) : 4;
  const weight = Math.max(BASELINE.freshWeight,
    Math.min(1, BASELINE.freshWeight + weeks * BASELINE.weeklyWeight));
  const next = Math.round(prev * (1 - weight) + session * weight);
  return { baselineMs: next, weight, established: false, changed: next !== prev };
}

/**
 * The comeback hook: the ONE stat that slipped, named. Returns a lexicon key so
 * the mod skin owns the words.
 */
export function slipKey(m, gate) {
  const speedOff = m.speedIndex > 1.02;
  const restraintOff = m.falseAlarmRate > S_GATE.maxFalseAlarmRate;
  if (speedOff && restraintOff) return 'ic_slip_both';
  if (speedOff) return 'ic_slip_speed';
  if (restraintOff) return 'ic_slip_restraint';
  return gate && gate.ok ? 'ic_slip_none' : 'ic_slip_none';
}

/** Percent off the personal record, for the slip line ("4% slower than your record"). */
export function offRecordPct(m) {
  if (m.medianRt == null || !m.baselineMs) return 0;
  return Math.round((m.medianRt / m.baselineMs - 1) * 100);
}

export default { metricsFrom, terms, composite, sGate, flavorXp, foldBaseline, slipKey };
