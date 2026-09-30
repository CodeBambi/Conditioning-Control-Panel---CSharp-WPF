/* ============================================================================
 * ui/nightProgress.js - how many matches this browser has seen to the end.
 *
 * Game Night gates the game cards (duels) on it: they only start dropping from
 * the player's SECOND finished match, so a first match asks nothing new. This
 * file is the one owner of the count; the duel lane reads finishedMatches() and
 * the recap calls noteMatchFinished() once per match.
 *
 * Local only, never sent. Every storage touch is guarded: a private window, a
 * full quota or a node test all read 0 and write nothing, and none of that may
 * ever break the recap it is called from.
 * ==========================================================================*/

const KEY = 'goon.night.finished.v1';
const CAP = 1000000;

function store() {
  try { return (typeof localStorage !== 'undefined') ? localStorage : null; } catch (_e) { return null; }
}

/** @returns {number} finished matches on this device, 0 when unknown. */
export function finishedMatches() {
  try {
    const s = store();
    if (!s) return 0;
    const n = Math.floor(Number(s.getItem(KEY)));
    return Number.isFinite(n) && n > 0 ? Math.min(n, CAP) : 0;
  } catch (_e) { return 0; }
}

/** Count one more finished match. @returns {number} the new count (0 when storage is gone). */
export function noteMatchFinished() {
  try {
    const s = store();
    if (!s) return 0;
    const next = Math.min(finishedMatches() + 1, CAP);
    s.setItem(KEY, String(next));
    return next;
  } catch (_e) { return 0; }
}
