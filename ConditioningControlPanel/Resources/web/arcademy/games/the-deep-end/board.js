/* ============================================================================
 * games/the-deep-end/board.js - the 2048 board model. PURE: no DOM, no clock,
 * no ctx. The scratch harness drives this directly and asserts whole dives.
 *
 * A tile is {id, tier, r, c, silt}. Tiers are INTEGERS tier_1..tier_11 and the
 * whole file compares integers only - the display names live in the lexicon
 * (lex.js) and nothing here ever reads one. SILT is tier 0 with silt:true: it
 * slides like any tile, never merges with anything (not even silt), never
 * sinks, and only an airlock draw removes it.
 *
 * RULES (classic 2048, pinned by the contract):
 *   - one merge per tile per move; merges resolve TOWARD the move direction
 *     (the tile nearer the edge is the survivor, the other is the victim);
 *   - a move that slides nothing is a no-op: no spawn, no swipe counted;
 *   - tier_11 never merges (the ceiling ends the class before it matters);
 *   - spawn = ONE tile per real move, drawn from the seeded spawn stream.
 *
 * SEEDING (Law V): `makeRng(seed + '|de-spawn')`, append-only. Every spawn
 * draws exactly THREE rolls (cell, kind, tier) whatever branch it takes, so
 * a future branch can never shift the tiles an older seed dealt. Same seed +
 * same move list = same boards, which the harness asserts.
 *
 * THE EXHALE GUARANTEE: a spawn flagged `exhale` lands next to a tile it can
 * merge with when any such cell exists - its tier is the LOWEST mergeable
 * neighbour, so mercy is a fit, never a gift of depth.
 *
 * WHAT THIS FILE NEVER OWNS: heat, grades, effects, the DOM, the clock. It
 * does not know a class exists.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const TIER_MIN = 1;
export const TIER_MAX = 11;
export const SILT_TIER = 0;

/** Baseline spawn table: tier_1 90% / tier_2 10% (classic 2/4 odds). */
export const SPAWN_TABLE_BASE = Object.freeze([[1, 0.90], [2, 0.10]]);

export const DIRS = Object.freeze({
  up: Object.freeze({ dr: -1, dc: 0 }),
  down: Object.freeze({ dr: 1, dc: 0 }),
  left: Object.freeze({ dr: 0, dc: -1 }),
  right: Object.freeze({ dr: 0, dc: 1 }),
});

/* ----------------------------------------------------------------------------
 * CONSTRUCTION
 * -------------------------------------------------------------------------- */
/**
 * @param {number} n     4 | 5
 * @param {string} seed  the class seed (the spawn stream is scoped here)
 */
export function createBoard(n, seed) {
  const size = n === 5 ? 5 : 4;
  return {
    n: size,
    tiles: [],
    nextId: 1,
    rng: makeRng(String(seed == null ? '' : seed) + '|de-spawn'),
    spawned: 0,
    draws: 0,
  };
}

/** Drop every tile but keep the id counter and the spawn stream (a resurface
 *  is a fresh BOARD, not a fresh seed - the day's script stays the script). */
export function drain(board) {
  board.tiles.length = 0;
  return board;
}

/* ----------------------------------------------------------------------------
 * READS
 * -------------------------------------------------------------------------- */
export function grid(board) {
  const n = board.n;
  const g = [];
  for (let r = 0; r < n; r++) { g.push(new Array(n).fill(null)); }
  for (const t of board.tiles) g[t.r][t.c] = t;
  return g;
}

export function cellAt(board, r, c) {
  if (r < 0 || c < 0 || r >= board.n || c >= board.n) return undefined;   // off-board
  for (const t of board.tiles) if (t.r === r && t.c === c) return t;
  return null;
}

export function occupancy(board) { return board.tiles.length; }

export function siltCount(board) {
  let k = 0;
  for (const t of board.tiles) if (t.silt) k += 1;
  return k;
}

/** The deepest real tier on the board (0 when only silt / nothing). */
export function deepest(board) {
  let d = 0;
  for (const t of board.tiles) if (!t.silt && t.tier > d) d = t.tier;
  return d;
}

export function emptyCells(board) {
  const g = grid(board);
  const out = [];
  for (let r = 0; r < board.n; r++) for (let c = 0; c < board.n; c++) if (!g[r][c]) out.push({ r, c });
  return out;
}

function canMerge(a, b) {
  return !!a && !!b && !a.silt && !b.silt && a.tier === b.tier && a.tier < TIER_MAX;
}

/** True when some move would merge something. */
export function canMergeAny(board) {
  const g = grid(board);
  const n = board.n;
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      const t = g[r][c];
      if (!t) continue;
      if (c + 1 < n && canMerge(t, g[r][c + 1])) return true;
      if (r + 1 < n && canMerge(t, g[r + 1][c])) return true;
    }
  }
  return false;
}

/** Locked = full AND no merge anywhere (the dive is over). */
export function isLocked(board) {
  return board.tiles.length >= board.n * board.n && !canMergeAny(board);
}

/**
 * The strain: two tiles of the DEEPEST tier that almost meet - diagonal
 * neighbours, or in one line with only unmergeable tiles packed between them.
 * Returns [a, b] or null. Deterministic scan order, so the same board always
 * names the same pair (the casino's almost() must not flicker between pairs).
 */
export function strainPair(board) {
  const d = deepest(board);
  if (d < 2) return null;
  const deep = board.tiles.filter((t) => !t.silt && t.tier === d);
  if (deep.length < 2) return null;
  const g = grid(board);
  for (let i = 0; i < deep.length; i++) {
    for (let j = i + 1; j < deep.length; j++) {
      const a = deep[i]; const b = deep[j];
      const dr = Math.abs(a.r - b.r); const dc = Math.abs(a.c - b.c);
      if (dr === 1 && dc === 1) return [a, b];                  // corner to corner
      if (dr === 0 && dc > 1) {
        let blocked = true;
        const lo = Math.min(a.c, b.c); const hi = Math.max(a.c, b.c);
        for (let c = lo + 1; c < hi; c++) { const m = g[a.r][c]; if (!m || canMerge(m, a)) { blocked = false; break; } }
        if (blocked) return [a, b];
      }
      if (dc === 0 && dr > 1) {
        let blocked = true;
        const lo = Math.min(a.r, b.r); const hi = Math.max(a.r, b.r);
        for (let r = lo + 1; r < hi; r++) { const m = g[r][a.c]; if (!m || canMerge(m, a)) { blocked = false; break; } }
        if (blocked) return [a, b];
      }
    }
  }
  return null;
}

/** One line per row: 't' for silt, '.' for empty, the tier otherwise. */
export function serialize(board) {
  const g = grid(board);
  return g.map((row) => row.map((t) => (!t ? '.' : t.silt ? 's' : String(t.tier))).join(' ')).join('\n');
}

/* ----------------------------------------------------------------------------
 * THE MOVE
 * -------------------------------------------------------------------------- */
/**
 * Slide + merge in one direction. Mutates the board.
 * @returns {{moved:boolean, moves:Array, merges:Array, score:number}}
 *   moves:  [{id, from:{r,c}, to:{r,c}}]          every tile that changed cell
 *   merges: [{id, victimId, tier, r, c, link}]    survivor id, new tier, 1-based link
 */
export function move(board, dir) {
  const d = DIRS[dir];
  if (!d) return { moved: false, moves: [], merges: [], score: 0 };
  const n = board.n;
  const g = grid(board);
  const moves = [];
  const merges = [];
  const removed = new Set();
  let score = 0;
  let link = 0;

  for (let k = 0; k < n; k++) {
    /* the line, ordered from the edge the tiles slide toward */
    const line = [];
    for (let i = 0; i < n; i++) {
      let r; let c;
      if (d.dc !== 0) { r = k; c = d.dc < 0 ? i : n - 1 - i; }
      else { c = k; r = d.dr < 0 ? i : n - 1 - i; }
      const t = g[r][c];
      if (t) line.push(t);
    }
    const out = [];
    let i = 0;
    while (i < line.length) {
      const a = line[i];
      const b = line[i + 1];
      if (b && canMerge(a, b)) {
        link += 1;
        a.tier += 1;
        score += Math.pow(2, a.tier);
        removed.add(b.id);
        out.push({ tile: a, victim: b });
        i += 2;
      } else {
        out.push({ tile: a, victim: null });
        i += 1;
      }
    }
    /* place: slot j from the edge */
    for (let j = 0; j < out.length; j++) {
      let r; let c;
      if (d.dc !== 0) { r = k; c = d.dc < 0 ? j : n - 1 - j; }
      else { c = k; r = d.dr < 0 ? j : n - 1 - j; }
      const { tile, victim } = out[j];
      if (tile.r !== r || tile.c !== c) {
        moves.push({ id: tile.id, from: { r: tile.r, c: tile.c }, to: { r, c } });
        tile.r = r; tile.c = c;
      }
      if (victim) {
        moves.push({ id: victim.id, from: { r: victim.r, c: victim.c }, to: { r, c }, victim: true });
        victim.r = r; victim.c = c;
        merges.push({ id: tile.id, victimId: victim.id, tier: tile.tier, r, c, link: merges.length + 1 });
      }
    }
  }
  if (removed.size) board.tiles = board.tiles.filter((t) => !removed.has(t.id));
  return { moved: moves.length > 0 || merges.length > 0, moves, merges, score };
}

/* ----------------------------------------------------------------------------
 * THE SPAWN (three rolls, always, in this order: cell, kind, tier)
 * -------------------------------------------------------------------------- */
function rollTier(table, r) {
  const rows = Array.isArray(table) && table.length ? table : SPAWN_TABLE_BASE;
  let acc = 0;
  for (const [tier, w] of rows) {
    acc += Number(w) || 0;
    if (r < acc) return tier;
  }
  return rows[rows.length - 1][0];
}

/**
 * @param {Object} board
 * @param {Object=} o
 *   table?: [[tier, weight], ...]   spawn table (default SPAWN_TABLE_BASE)
 *   siltChance?: 0..1               tier-4 dial; 0 = never
 *   siltMax?: number                never more silt than this on the board
 *   airlockChance?: 0..1            with silt present, dissolve the oldest instead
 *   exhale?: boolean                the guarantee: land next to an equal tier
 * @returns {{tile:Object|null, silt:boolean, airlock:Object|null, exhaled:boolean}}
 */
export function spawn(board, o = {}) {
  const rCell = board.rng();
  const rKind = board.rng();
  const rTier = board.rng();
  board.draws += 3;

  const empties = emptyCells(board);
  const result = { tile: null, silt: false, airlock: null, exhaled: false };

  /* airlock: the room lets one silt tile go (the dossier's rare mercy draw) */
  const silts = board.tiles.filter((t) => t.silt);
  const airlockChance = Number(o.airlockChance) || 0;
  if (silts.length && airlockChance > 0 && rKind < airlockChance) {
    const gone = silts[0];
    board.tiles = board.tiles.filter((t) => t !== gone);
    result.airlock = gone;
    empties.push({ r: gone.r, c: gone.c });
  }
  if (!empties.length) return result;

  /* the exhale: any empty cell with a mergeable neighbour is a candidate */
  if (o.exhale) {
    const g = grid(board);
    const cands = [];
    for (const e of empties) {
      let best = 0;
      for (const [dr, dc] of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
        const nb = (g[e.r + dr] || [])[e.c + dc];
        if (nb && !nb.silt && nb.tier < TIER_MAX && (best === 0 || nb.tier < best)) best = nb.tier;
      }
      if (best > 0) cands.push({ r: e.r, c: e.c, tier: best });
    }
    if (cands.length) {
      const pick = cands[Math.min(cands.length - 1, Math.floor(rCell * cands.length))];
      const tile = { id: board.nextId++, tier: pick.tier, r: pick.r, c: pick.c, silt: false };
      board.tiles.push(tile);
      board.spawned += 1;
      result.tile = tile;
      result.exhaled = true;
      return result;
    }
  }

  const cell = empties[Math.min(empties.length - 1, Math.floor(rCell * empties.length))];
  const siltChance = Number(o.siltChance) || 0;
  const siltMax = o.siltMax == null ? 2 : Math.max(0, o.siltMax | 0);
  const silt = !result.airlock && siltChance > 0 && siltCount(board) < siltMax && rKind >= 1 - siltChance;
  const tile = silt
    ? { id: board.nextId++, tier: SILT_TIER, r: cell.r, c: cell.c, silt: true }
    : { id: board.nextId++, tier: rollTier(o.table, rTier), r: cell.r, c: cell.c, silt: false };
  board.tiles.push(tile);
  board.spawned += 1;
  result.tile = tile;
  result.silt = silt;
  return result;
}

/** A fresh dive: two opening tiles from the table (never silt, never exhale). */
export function openingSpawn(board, table) {
  const out = [];
  for (let i = 0; i < 2; i++) {
    const r = spawn(board, { table, siltChance: 0, airlockChance: 0 });
    if (r.tile) out.push(r.tile);
  }
  return out;
}

export default { createBoard, move, spawn, openingSpawn, isLocked, deepest, occupancy, strainPair, serialize };
