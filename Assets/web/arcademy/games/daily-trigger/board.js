/* ============================================================================
 * games/daily-trigger/board.js - THE BOARD RULES. Pure functions, no DOM.
 *
 * MARKS NEVER LIE (dossier's honesty law): this file is the only place that
 * decides hit/near/miss, effects may occlude a mark but never repaint one, and
 * the mark keys are the internal ones - `mark_hit` / `mark_near` / `mark_miss`.
 * Every mark is double-coded in the UI (colour PLUS a stamped glyph) so colour is
 * never the only channel; that is always on, not a setting.
 *
 * Duplicate letters follow the classic two-pass rule: exact positions claim
 * their letter first, then the leftovers feed 'near' left-to-right. Getting this
 * wrong is the single most-reported bug in every Wordle clone ever written.
 * ==========================================================================*/

export const HIT = 'hit';
export const NEAR = 'near';
export const MISS = 'miss';

/** hit > near > miss > unknown. Used for the keyboard's best-known state. */
const RANK = { hit: 3, near: 2, miss: 1 };

/**
 * Mark one guess against the answer.
 * @param {string} guess  same length as answer, a-z
 * @param {string} answer a-z
 * @returns {string[]} one of HIT/NEAR/MISS per position
 */
export function markGuess(guess, answer) {
  const g = String(guess || '').toLowerCase();
  const a = String(answer || '').toLowerCase();
  const n = g.length;
  const marks = new Array(n).fill(MISS);
  if (!n || a.length !== n) return marks;

  /* pass 1: exact positions consume their letter */
  const left = Object.create(null);
  for (let i = 0; i < n; i++) {
    if (g[i] === a[i]) marks[i] = HIT;
    else left[a[i]] = (left[a[i]] || 0) + 1;
  }
  /* pass 2: leftovers, left to right */
  for (let i = 0; i < n; i++) {
    if (marks[i] === HIT) continue;
    const c = g[i];
    if (left[c] > 0) { marks[i] = NEAR; left[c] -= 1; }
  }
  return marks;
}

/** True when every position is a hit. */
export function isSolved(marks) {
  return Array.isArray(marks) && marks.length > 0 && marks.every((m) => m === HIT);
}

/** Hits in a row - the near-miss tease fires at length-1. */
export function hitCount(marks) {
  return (marks || []).reduce((n, m) => n + (m === HIT ? 1 : 0), 0);
}

/** "One letter away": all but exactly one position are hits. */
export function isNearMiss(marks) {
  if (!Array.isArray(marks) || marks.length < 3) return false;
  return hitCount(marks) === marks.length - 1;
}

/**
 * Fold a committed row into the keyboard's best-known letter state.
 * @param {Object} state  letter -> mark (mutated copy returned)
 */
export function foldKeyboard(state, guess, marks) {
  const out = Object.assign(Object.create(null), state || null);
  const g = String(guess || '').toLowerCase();
  for (let i = 0; i < g.length; i++) {
    const c = g[i];
    const next = marks[i];
    if (!next) continue;
    if (!out[c] || (RANK[next] || 0) > (RANK[out[c]] || 0)) out[c] = next;
  }
  return out;
}

/**
 * Hard mode (the classic contract): a revealed hit must be reused in place, and
 * a revealed near letter must appear somewhere. Count-aware on nears so a single
 * revealed 'e' does not demand two.
 *
 * @param {string} guess
 * @param {Array<{guess:string, marks:string[]}>} history committed rows
 * @returns {null|{reason:'hard_hit'|'hard_near', index?:number, letter:string}}
 */
export function hardModeViolation(guess, history) {
  const g = String(guess || '').toLowerCase();
  const rows = Array.isArray(history) ? history : [];
  if (!rows.length) return null;

  /* known hits by position, and the max count of each known-present letter */
  const hits = Object.create(null);
  const need = Object.create(null);
  for (const row of rows) {
    const rg = String((row && row.guess) || '').toLowerCase();
    const marks = (row && row.marks) || [];
    const seen = Object.create(null);
    for (let i = 0; i < marks.length; i++) {
      if (marks[i] === HIT) { hits[i] = rg[i]; seen[rg[i]] = (seen[rg[i]] || 0) + 1; }
      else if (marks[i] === NEAR) seen[rg[i]] = (seen[rg[i]] || 0) + 1;
    }
    for (const c of Object.keys(seen)) need[c] = Math.max(need[c] || 0, seen[c]);
  }

  for (const k of Object.keys(hits)) {
    const i = Number(k);
    if (g[i] !== hits[k]) return { reason: 'hard_hit', index: i, letter: hits[k] };
  }
  const have = Object.create(null);
  for (const c of g) have[c] = (have[c] || 0) + 1;
  for (const c of Object.keys(need)) {
    if ((have[c] || 0) < need[c]) return { reason: 'hard_near', letter: c };
  }
  return null;
}

/* ----------------------------------------------------------------------------
 * KEYBOARD LAYOUTS (per-game setting dt_keyboard_layout)
 * -------------------------------------------------------------------------- */
export const LAYOUTS = Object.freeze({
  qwerty: ['qwertyuiop', 'asdfghjkl', 'zxcvbnm'],
  azerty: ['azertyuiop', 'qsdfghjklm', 'wxcvbn'],
  qwertz: ['qwertzuiop', 'asdfghjkl', 'yxcvbnm'],
  alphabetical: ['abcdefghi', 'jklmnopqr', 'stuvwxyz'],
});

/** Layout rows for a setting value, with a defensive qwerty fallback. */
export function layoutRows(name) {
  const k = String(name || '').toLowerCase();
  return (LAYOUTS[k] || LAYOUTS.qwerty).map((r) => r.split(''));
}

/**
 * Auto-detect from the browser locale (the setting's default is 'auto'). French
 * locales get azerty, German/Swiss qwertz, everyone else qwerty.
 */
export function autoLayout(locale) {
  const l = String(locale || '').toLowerCase();
  if (/^fr\b|^fr-/.test(l)) return 'azerty';
  if (/^de\b|^de-|^ch\b|^de-ch/.test(l)) return 'qwertz';
  return 'qwerty';
}

/* ----------------------------------------------------------------------------
 * GROUPS (phrase days render two word-groups with a free gap)
 * -------------------------------------------------------------------------- */
/**
 * Flat cell descriptors for one row: {index, group, gapAfter}. Spaces are NOT
 * cells - they are gaps, auto-marked and never guessed.
 * @param {string[]} groups
 */
export function cellPlan(groups) {
  const gs = (Array.isArray(groups) ? groups : []).map((g) => String(g || ''));
  const plan = [];
  let index = 0;
  gs.forEach((g, gi) => {
    for (let i = 0; i < g.length; i++) {
      plan.push({ index, group: gi, gapAfter: i === g.length - 1 && gi < gs.length - 1 });
      index += 1;
    }
  });
  return plan;
}

export default { markGuess, isSolved, isNearMiss, foldKeyboard, hardModeViolation, layoutRows, cellPlan };
