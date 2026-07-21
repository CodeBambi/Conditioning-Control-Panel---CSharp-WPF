/* ============================================================================
 * spiralLog.js — THE RUN'S SPIRALS, kept so the end card can weave them again.
 *
 * WHY: since core/palette.js landed, every spiral the run mounts is built from
 * the 2-4 colours the player themselves named in Calibration. That makes the
 * spirals PERSONAL to the run — so the outro shows them back as a small gallery
 * (boot.js runOutro), and each one can be kept (THE LOOM) or exported (a PNG).
 *
 * HOW: both spiral mount paths already funnel their params through their own
 * copy of freshSpiralParams() — render/beats.js (mountLoomSpiral: the bubble
 * backdrop + the two fullscreen loom layers) and render/effects.js (the loom
 * garnish). Recording THERE, at roll time, means we capture exactly the params
 * that were rendered instead of trying to reconstruct a look after the fact.
 *
 * WHY A MODULE SINGLETON: same seam problem core/palette.js has — the producers
 * live in render/, the consumer lives in boot.js, and there is exactly one run
 * per page. boot.js calls resetSpiralLog() at run start, beside resetPalette().
 *
 * CAP: MAX_KEPT (8) entries — the run's spirals, not a frame log. We keep the
 * FIRST MAX_KEPT (arrival order = the descent's own order) and keep counting, so
 * the card can honestly say "8 of 11 woven". Params are stored by deep copy: the
 * loom modules never mutate them, but a snapshot can't be spoiled by a caller.
 *
 * MUST NOT THROW AT IMPORT (a throw = silent infinite loader; DtRH gotcha).
 * No DOM access anywhere in this file.
 * ==========================================================================*/

/** Spirals kept for the recap gallery. */
export const MAX_KEPT = 8;

const log = {
  kept: [],   // [{ params, at }] in arrival order, <= MAX_KEPT
  total: 0,   // spirals the run rolled (may exceed kept.length)
};

/** Wipe the log. boot.js calls this once per run, before the beat loop. */
export function resetSpiralLog() {
  log.kept.length = 0;
  log.total = 0;
}

/**
 * Record one rolled spiral. Called from render/beats.js + render/effects.js
 * inside freshSpiralParams(), i.e. once per mount. Never throws, and always
 * returns `params` unchanged so it can sit inline on a return path.
 */
export function recordSpiral(params) {
  try {
    if (!params || typeof params !== 'object') return params;
    log.total += 1;
    if (log.kept.length < MAX_KEPT) {
      log.kept.push({ params: JSON.parse(JSON.stringify(params)), at: Date.now() });
    }
  } catch (_e) { /* a snapshot failure must never cost a spiral its mount */ }
  return params;
}

/** The kept spirals, in arrival order (fresh array, fresh params copies). */
export function recordedSpirals() {
  const out = [];
  for (const e of log.kept) {
    try { out.push({ params: JSON.parse(JSON.stringify(e.params)), at: e.at }); }
    catch (_e) { /* skip an unclonable entry rather than fail the gallery */ }
  }
  return out;
}

/** How many spirals the run rolled in total (>= recordedSpirals().length). */
export function spiralCount() { return log.total; }
