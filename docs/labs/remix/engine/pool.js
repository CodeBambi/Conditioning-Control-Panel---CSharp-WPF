/* ============================================================================
 * pool.js - the pool. "Load as many gifs as you like, the room picks."
 *
 * The pool is every file the user has loaded; the canvas holds at most eight
 * of them at a time. pickSet says which ones go on for a roll:
 *
 *   pinned gifs first, in pin order, always;
 *   then, up to `count`, a seeded draw from the rest of the pool.
 *
 * Pure and deterministic from the seed, the pool order, the pins and the
 * count: same code, same pool, same picks. When the whole pool fits (no draw
 * needed) the pool order is kept, so a small pool behaves the way the room
 * always did. The engine never sees the pool itself: the UI keeps it (files,
 * thumbs, pins) and hands the picked ids to setMediaSet + roll.
 * ==========================================================================*/

import { rngFrom } from './rng.js';
import { MAX_TILES } from './layout.js';

/** Most files one pool holds. Thumbs are cheap; decoded strips are not, and only the picks decode. */
export const POOL_CAP = 100;

/** The count the room uses when the user has not touched the stepper: the whole pool, eight at most. */
export function defaultCount(poolSize, pinned = 0) {
  return clampCountFor(Math.min(poolSize | 0, MAX_TILES), poolSize, pinned);
}

/** A count that fits: never under the pins, never over the pool, never over eight, never under one. */
export function clampCountFor(count, poolSize, pinned = 0) {
  const top = Math.max(1, Math.min(MAX_TILES, poolSize | 0));
  const floor = Math.max(1, Math.min(top, pinned | 0));
  const n = Number.isFinite(count) ? Math.round(count) : top;
  return Math.max(floor, Math.min(top, n));
}

/**
 * Which pool ids go on the canvas for this roll.
 *
 * @param {object} args
 * @param {Array<{id:string}|string>} args.pool   the pool, in the user's order
 * @param {string[]} [args.pins]                   pinned ids, in pin order (unknown ids are ignored)
 * @param {number} [args.count]                    how many gifs to use; defaults to the pool size, eight at most
 * @param {number} args.seed                       the roll seed
 * @returns {string[]} ids in slot order: pins first, then the draw
 */
export function pickSet({ pool = [], pins = [], count, seed = 0 } = {}) {
  const ids = pool.map((e) => (typeof e === 'string' ? e : e && e.id)).filter(Boolean);
  if (!ids.length) return [];
  const have = new Set(ids);
  const locked = [];
  for (const id of pins) if (have.has(id) && !locked.includes(id)) locked.push(id);
  const n = clampCountFor(count == null ? defaultCount(ids.length, locked.length) : count, ids.length, locked.length);
  const rest = ids.filter((id) => !locked.includes(id));
  const need = Math.max(0, n - locked.length);
  if (need >= rest.length) return locked.concat(rest);
  const r = rngFrom(seed >>> 0, 'pool:' + ids.length);
  return locked.concat(r.shuffle(rest).slice(0, need));
}

/** True when two sets hold the same ids in the same order. */
export function sameSet(a, b) {
  if (!a || !b || a.length !== b.length) return false;
  for (let i = 0; i < a.length; i++) if (a[i] !== b[i]) return false;
  return true;
}
