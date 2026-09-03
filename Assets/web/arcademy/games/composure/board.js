/* ============================================================================
 * games/composure/board.js - COMPOSURE's pure puzzle state. PURE: no DOM, no
 * clock, no ctx, no engine, no Math.random.
 *
 * THE MODEL. An n x n sliding puzzle with ONE gap.
 *   cells[pos] = tileId            0 .. n*n-2, or BLANK (-1) for the gap
 *   home(tileId) = tileId          tile k belongs in cell k; the gap's home is
 *                                  the last cell, so the solved board reads
 *                                  [0, 1, 2, ... n*n-2, BLANK]
 * A tile's HOME is also which fragment of the picture it carries - index.js
 * turns home into the media's object offset, which is why home is an integer
 * here and never a look.
 *
 * LOCKING IS COSMETIC. `homeMask` / `lockedCount` report which tiles are
 * sitting on their own home cell so the class can snap them, ladder the heat
 * and grade progress. A "locked" tile is NEVER frozen: freezing tiles can make
 * a solvable board unsolvable, and Law I says the ledger is honest. Every
 * legal slide stays legal for the whole class.
 *
 * SOLVABILITY IS A PARITY INVARIANT, and half of all permutations fail it.
 *   n odd  : solvable <=> inversions(tiles) is EVEN
 *   n even : solvable <=> inversions(tiles) + row(gap, from the top, 0-based)
 *                         is ODD
 * `scramble()` therefore deals a permutation, tests it, and REPAIRS an
 * unsolvable one by transposing two tiles (which flips inversion parity and
 * leaves the gap where it is). A board this file hands out is always solvable
 * - the suite asserts it over 300 seeds x {3,4,5} - because a class the player
 * mathematically cannot finish is the one failure this game may not ship.
 *
 * SEEDING (Law V): every draw runs off `makeRng(seed + '|<tag>')` from
 * core/rng.js, in append-only order, so a retake deals the identical board.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

/** The gap. Never a tile id. */
export const BLANK = -1;

/** Directions a TILE travels. 'left' = the tile right of the gap moves left. */
export const DIRS = Object.freeze(['up', 'down', 'left', 'right']);

/** dir -> the delta from the gap to the tile that would fill it. */
const FROM_BLANK = Object.freeze({
  up: { dr: 1, dc: 0 },      // a tile BELOW the gap slides up
  down: { dr: -1, dc: 0 },   // a tile ABOVE the gap slides down
  left: { dr: 0, dc: 1 },    // a tile RIGHT of the gap slides left
  right: { dr: 0, dc: -1 },  // a tile LEFT of the gap slides right
});

export function sizeOf(n) {
  const v = Math.round(Number(n) || 0);
  return v < 3 ? 3 : v > 5 ? 5 : v;
}

/** '4x4' | 4 | '4' -> 4. The zen setting arrives as the string. */
export function sizeFromSetting(v, fallback) {
  const s = String(v == null ? '' : v).trim().toLowerCase();
  const m = /^([345])\s*x\s*\1$/.exec(s);
  if (m) return Number(m[1]);
  const n = Math.round(Number(s));
  if (Number.isFinite(n) && n >= 3 && n <= 5) return n;
  return sizeOf(fallback == null ? 3 : fallback);
}

export function rowOf(n, pos) { return Math.floor(pos / n); }
export function colOf(n, pos) { return pos % n; }
export function posOf(n, r, c) { return r * n + c; }
export function inBounds(n, r, c) { return r >= 0 && c >= 0 && r < n && c < n; }

/** The solved arrangement for an n x n board. */
export function solvedCells(n) {
  const size = sizeOf(n);
  const out = [];
  for (let i = 0; i < size * size - 1; i++) out.push(i);
  out.push(BLANK);
  return out;
}

/**
 * Wrap a cells array as a state. `cells` is COPIED - a state never aliases the
 * array it was built from, so a caller may keep its own.
 */
export function createState(n, cells) {
  const size = sizeOf(n);
  const src = Array.isArray(cells) && cells.length === size * size ? cells.slice() : solvedCells(size);
  return { n: size, cells: src, blank: src.indexOf(BLANK) };
}

export function cloneState(s) {
  return { n: s.n, cells: s.cells.slice(), blank: s.blank };
}

/** Stable text form (tests, diagnostics, the meta row). */
export function serialize(s) {
  return s.n + ':' + s.cells.map((v) => (v === BLANK ? '_' : v)).join(',');
}

/** Positions orthogonally adjacent to `pos`. */
export function neighbours(n, pos) {
  const r = rowOf(n, pos); const c = colOf(n, pos);
  const out = [];
  if (r > 0) out.push(pos - n);
  if (r < n - 1) out.push(pos + n);
  if (c > 0) out.push(pos - 1);
  if (c < n - 1) out.push(pos + 1);
  return out;
}

/** Every tile position that may slide right now (the gap's neighbours). */
export function legalSlides(s) {
  return neighbours(s.n, s.blank);
}

/** Is a slide of the tile at `pos` legal? */
export function canSlide(s, pos) {
  if (!Number.isFinite(pos) || pos < 0 || pos >= s.n * s.n) return false;
  if (s.cells[pos] === BLANK) return false;
  return neighbours(s.n, s.blank).indexOf(pos) >= 0;
}

/** The direction the tile at `pos` would travel (it always travels into the gap). */
export function dirOfSlide(s, pos) {
  if (!canSlide(s, pos)) return '';
  return dirBetween(s.n, pos, s.blank);
}

/** The direction of travel from `from` to the adjacent cell `to`. */
export function dirBetween(n, from, to) {
  const dr = rowOf(n, to) - rowOf(n, from);
  const dc = colOf(n, to) - colOf(n, from);
  if (dr === -1 && dc === 0) return 'up';
  if (dr === 1 && dc === 0) return 'down';
  if (dr === 0 && dc === -1) return 'left';
  if (dr === 0 && dc === 1) return 'right';
  return '';
}

/** The tile position a direction press would slide, or -1 when that wall is solid. */
export function slotForDir(s, dir) {
  const d = FROM_BLANK[String(dir)];
  if (!d) return -1;
  const r = rowOf(s.n, s.blank) + d.dr;
  const c = colOf(s.n, s.blank) + d.dc;
  if (!inBounds(s.n, r, c)) return -1;
  return posOf(s.n, r, c);
}

/**
 * Slide the tile at `pos` into the gap. MUTATES `s`.
 * @returns {{moved:boolean, id:number, from:number, to:number, dir:string}}
 */
export function slide(s, pos) {
  if (!canSlide(s, pos)) return { moved: false, id: BLANK, from: pos, to: pos, dir: '' };
  const id = s.cells[pos];
  const to = s.blank;
  const dir = dirBetween(s.n, pos, to);
  s.cells[to] = id;
  s.cells[pos] = BLANK;
  s.blank = pos;
  return { moved: true, id, from: pos, to, dir };
}

/** Slide by direction of travel. Same result shape as slide(). */
export function slideDir(s, dir) {
  const pos = slotForDir(s, dir);
  if (pos < 0) return { moved: false, id: BLANK, from: -1, to: -1, dir: String(dir || '') };
  return slide(s, pos);
}

export function isSolved(s) {
  for (let i = 0; i < s.cells.length - 1; i++) if (s.cells[i] !== i) return false;
  return s.cells[s.cells.length - 1] === BLANK;
}

/** Per-CELL truth: is the tile sitting there on its own home cell? */
export function homeMask(s) {
  const out = [];
  for (let i = 0; i < s.cells.length; i++) out.push(s.cells[i] !== BLANK && s.cells[i] === i);
  return out;
}

/** How many tiles are home (the gap is never counted). */
export function lockedCount(s) {
  let k = 0;
  for (let i = 0; i < s.cells.length; i++) if (s.cells[i] !== BLANK && s.cells[i] === i) k += 1;
  return k;
}

/** Tiles that could be home, i.e. n*n - 1. */
export function tileCount(n) { const size = sizeOf(n); return size * size - 1; }

/** Sum of every tile's distance from home. 0 == solved; the progress metric. */
export function manhattan(s) {
  let sum = 0;
  for (let i = 0; i < s.cells.length; i++) {
    const id = s.cells[i];
    if (id === BLANK) continue;
    sum += Math.abs(rowOf(s.n, i) - rowOf(s.n, id)) + Math.abs(colOf(s.n, i) - colOf(s.n, id));
  }
  return sum;
}

/** Inversions in the tile sequence (the gap is skipped, not counted as 0). */
export function inversions(cells) {
  const seq = cells.filter((v) => v !== BLANK);
  let inv = 0;
  for (let i = 0; i < seq.length; i++) for (let j = i + 1; j < seq.length; j++) if (seq[i] > seq[j]) inv += 1;
  return inv;
}

/**
 * THE PARITY INVARIANT. Half of all arrangements can never be solved; this is
 * the test that keeps one off the board.
 */
export function isSolvable(cells, n) {
  const size = sizeOf(n);
  const inv = inversions(cells);
  if (size % 2 === 1) return inv % 2 === 0;
  const blankRow = Math.floor(cells.indexOf(BLANK) / size);
  return (inv + blankRow) % 2 === 1;
}

/** Transpose the two lowest-index tiles: flips inversion parity, gap untouched. */
export function repairParity(cells) {
  const out = cells.slice();
  const idx = [];
  for (let i = 0; i < out.length && idx.length < 2; i++) if (out[i] !== BLANK) idx.push(i);
  if (idx.length === 2) { const t = out[idx[0]]; out[idx[0]] = out[idx[1]]; out[idx[1]] = t; }
  return out;
}

/**
 * A seeded, ALWAYS-SOLVABLE scramble.
 *
 * @param {number} n
 * @param {string} seedStr   the class seed, already scoped by the caller
 * @param {Object=} opts
 *   walk  0 = a full permutation (deep, tier 3-4); >0 = that many random legal
 *         slides from solved, never undoing the slide before (shallow, tier 1-2)
 *   minManhattan  re-walk until the board is at least this far from home
 * @returns {number[]} cells
 */
export function scramble(n, seedStr, opts) {
  const size = sizeOf(n);
  const o = opts || {};
  const rng = makeRng(String(seedStr == null ? '' : seedStr));
  const walk = Math.max(0, Math.round(Number(o.walk) || 0));
  const minMh = Number.isFinite(Number(o.minManhattan)) ? Number(o.minManhattan) : size;

  let cells;
  if (walk > 0) {
    cells = walkScramble(size, rng, walk);
  } else {
    cells = permute(size, rng);
    if (!isSolvable(cells, size)) cells = repairParity(cells);
  }

  /* A scramble that landed on (or beside) the solved board is a dead class:
   * top it up with a seeded walk rather than re-rolling, so the draw order
   * stays append-only. */
  let guard = 0;
  while (manhattan(createState(size, cells)) < minMh && guard < 8) {
    guard += 1;
    cells = walkScrambleFrom(size, rng, cells, size * size * 4);
  }
  return cells;
}

/** A seeded permutation of tiles + gap (Fisher-Yates on the whole cell array). */
function permute(n, rng) {
  const a = solvedCells(n);
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.min(i, Math.floor(rng() * (i + 1)));
    const t = a[i]; a[i] = a[j]; a[j] = t;
  }
  return a;
}

function walkScramble(n, rng, steps) {
  return walkScrambleFrom(n, rng, solvedCells(n), steps);
}

/** `steps` random legal slides, never immediately undoing the last one. */
function walkScrambleFrom(n, rng, cells, steps) {
  const s = createState(n, cells);
  let last = -1;
  for (let k = 0; k < steps; k++) {
    const opts = neighbours(n, s.blank).filter((p) => p !== last);
    const list = opts.length ? opts : neighbours(n, s.blank);
    const pick = list[Math.min(list.length - 1, Math.floor(rng() * list.length))];
    last = s.blank;
    slide(s, pick);
  }
  return s.cells;
}

/** The class's board: scramble, then hand back a live state. */
export function dealBoard(n, seedStr, opts) {
  const size = sizeOf(n);
  const cells = scramble(size, seedStr, opts);
  return createState(size, cells);
}

export default {
  BLANK, DIRS, sizeOf, sizeFromSetting, solvedCells, createState, cloneState, serialize,
  neighbours, legalSlides, canSlide, dirOfSlide, dirBetween, slotForDir, slide, slideDir,
  isSolved, homeMask, lockedCount, tileCount, manhattan, inversions, isSolvable, repairParity,
  scramble, dealBoard, rowOf, colOf, posOf,
};
