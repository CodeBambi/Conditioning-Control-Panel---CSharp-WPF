/* ============================================================================
 * ui/nightProgress.js - the local finished-match count (game night).
 *
 * STUB shipped by the duel lane. The rivalry lane owns this file and its real
 * version replaces this one; the two exports and the storage key are the
 * contract. localStorage, every access in try/catch, safe default 0.
 * ==========================================================================*/

const KEY = 'goon.night.finished.v1';

function store() {
  try { return typeof localStorage !== 'undefined' ? localStorage : null; } catch (_e) { return null; }
}

/** How many matches this player has finished (reached Recap) on this device. */
export function finishedMatches() {
  try {
    const s = store();
    const n = s ? Number(s.getItem(KEY)) : 0;
    return Number.isFinite(n) && n > 0 ? Math.trunc(n) : 0;
  } catch (_e) { return 0; }
}

/** One more finished match. */
export function noteMatchFinished() {
  try {
    const s = store();
    if (s) s.setItem(KEY, String(finishedMatches() + 1));
  } catch (_e) { /* storage refused: the count stays where it was */ }
}
