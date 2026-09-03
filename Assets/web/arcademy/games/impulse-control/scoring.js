/* ============================================================================
 * games/impulse-control/scoring.js - THE DROP TUBE's ledger. PURE.
 *
 * Points are the in-class currency (the HUD number, the debrief headline, the
 * flavor XP); the COMPOSITE is what grades.js turns into a letter. Both live
 * here so a playtest tune is a one-place edit and the harness can prove the
 * orderings without a DOM.
 *
 * THE SHAPE OF THE GAME (owner spec):
 *   - a good pop scores by reaction speed - faster is worth more, every
 *     successful pop is worth SOMETHING (POINTS_FLOOR),
 *   - clicking the X subtracts a LOT (X_PENALTY) and is the only error,
 *   - a good bubble that drifts away scores 0 and is NOT an error,
 *   - a survived X pays a flat restraint bonus.
 *
 * BASELINE (carried over from the assessment era, SYNTHESIS #15): the first
 * class writes the player's median pop time as baselineMs on the per-game meta
 * store; later classes fold speed against it, so "fast" means fast FOR YOU.
 *
 * THE DUAL S-GATE survives the rework: S demands an untouched X row AND
 * genuine speed. Neither axis can buy the other.
 * ==========================================================================*/

/* ------------------------- the constants block (playtest-tunable) -------- */
export const RT_FLOOR_MS = 180;       // at or under this: a perfect pop
export const POINTS_MAX = 100;        // a perfect pop
export const POINTS_FLOOR = 10;       // any successful pop is worth something
export const X_PENALTY = 250;         // clicking the X subtracts a LOT
export const DENIED_BONUS = 40;       // a survived X pays restraint
export const PERFECT_RT_MS = 260;     // at or under: the PERFECT stamp
export const FAST_RT_MS = 420;        // at or under: the Quick stamp

export const W_SPEED = 0.45;          // composite weights (sum 1.0)
export const W_CATCH = 0.25;
export const W_RESTRAINT = 0.30;

export const SGATE_SPEED_MIN = 0.75;  // the speed half of the dual gate

export const ABS_SPEED_BEST_MS = 320; // no-baseline curve: 1.0 at/below this
export const ABS_SPEED_WORST_MS = 900; //                   0.0 at/above this

export const BASELINE_ALPHA = 0.35;   // fold rate for later classes
export const FLAVOR_XP_PER_POINT = 1 / 25; // score -> bonus XP (shell caps it)

/* ----------------------------------------------------------------- helpers */
export function clamp01(v) {
  const x = Number(v);
  if (!isFinite(x)) return 0;
  return x < 0 ? 0 : x > 1 ? 1 : x;
}

export function median(list) {
  const a = (list || []).filter((v) => isFinite(v)).slice().sort((x, y) => x - y);
  if (!a.length) return null;
  const mid = a.length >> 1;
  return a.length % 2 ? a[mid] : (a[mid - 1] + a[mid]) / 2;
}

/** Points for one good pop at reaction time rt inside window win. */
export function popPoints(rtMs, windowMs) {
  const rt = Math.max(0, Number(rtMs) || 0);
  const win = Math.max(RT_FLOOR_MS + 1, Number(windowMs) || 1600);
  const speed = clamp01((win - rt) / (win - RT_FLOOR_MS));
  return POINTS_FLOOR + Math.round((POINTS_MAX - POINTS_FLOOR) * speed);
}

/** The stamp a pop earns: 'perfect' | 'fast' | 'ok'. */
export function popStamp(rtMs) {
  if (rtMs <= PERFECT_RT_MS) return 'perfect';
  if (rtMs <= FAST_RT_MS) return 'fast';
  return 'ok';
}

/**
 * Fold a session's median pop into the persisted baseline.
 * First class establishes; later classes EWMA toward the session (a player
 * genuinely getting faster drags their own bar down). `force` re-establishes.
 */
export function foldBaseline(meta, sessionMedianMs, force) {
  if (sessionMedianMs == null || !isFinite(sessionMedianMs)) return null;
  const prev = Number(meta && meta.baselineMs) || 0;
  if (!prev || force) {
    return { baselineMs: Math.round(sessionMedianMs), established: true };
  }
  const folded = prev * (1 - BASELINE_ALPHA) + sessionMedianMs * BASELINE_ALPHA;
  return { baselineMs: Math.round(folded), established: false };
}

/** 0..1 speed index for a session median, baseline-relative when one exists. */
export function speedIndex(medianRt, baselineMs) {
  if (medianRt == null || !isFinite(medianRt)) return 0;
  const base = Number(baselineMs) || 0;
  if (base > 0) {
    /* 1.0 at 80% of your baseline, 0.0 at 150% of it. */
    const hi = base * 0.8, lo = base * 1.5;
    return clamp01((lo - medianRt) / (lo - hi));
  }
  return clamp01((ABS_SPEED_WORST_MS - medianRt) / (ABS_SPEED_WORST_MS - ABS_SPEED_BEST_MS));
}

/**
 * The whole ledger for a finished class.
 * @param {Object} tally {goodShown, popped, drifted, deniedShown, deniedHeld,
 *                        xClicked, rts:[], score}
 * @param {Object} meta  persisted per-game meta ({baselineMs?, bestRtMs?})
 * @returns {{medianRt, bestRt, speed, catchRate, restraint, composite,
 *            sGate:{ok, reasons:[]}, score, flavorXp}}
 */
export function ledger(tally, meta) {
  const t = tally || {};
  const rts = (t.rts || []).filter((v) => isFinite(v));
  const medianRt = median(rts);
  const bestRt = rts.length ? Math.min.apply(null, rts) : null;

  const goodShown = Math.max(0, Number(t.goodShown) || 0);
  const deniedShown = Math.max(0, Number(t.deniedShown) || 0);
  const popped = Math.max(0, Number(t.popped) || 0);
  const xClicked = Math.max(0, Number(t.xClicked) || 0);

  const speed = speedIndex(medianRt, meta && meta.baselineMs);
  const catchRate = goodShown > 0 ? clamp01(popped / goodShown) : 0;
  /* Every X click burns restraint hard - two X's on a 3-X night is 0.33,
     not the 0.9 a 20-trial average would launder it to. */
  const restraint = deniedShown > 0 ? clamp01(1 - xClicked / deniedShown) : 1;

  const composite = clamp01(W_SPEED * speed + W_CATCH * catchRate + W_RESTRAINT * restraint);

  const reasons = [];
  if (xClicked > 0) reasons.push('restraint');
  if (speed < SGATE_SPEED_MIN) reasons.push('speed');
  const sGate = { ok: reasons.length === 0, reasons };

  const score = Math.round(Number(t.score) || 0);
  const flavorXp = Math.max(0, Math.round(Math.max(0, score) * FLAVOR_XP_PER_POINT));

  return { medianRt, bestRt, speed, catchRate, restraint, composite, sGate, score, flavorXp };
}
