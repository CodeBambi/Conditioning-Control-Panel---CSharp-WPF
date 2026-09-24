/* ============================================================================
 * ui/duel/board.js - the game night duel board. PURE: no DOM, no clock.
 *
 * SOURCE: a copy of the pure 2048 rules in
 *   Resources/web/arcademy/games/the-deep-end/board.js
 * trimmed to what a duel needs (no silt, no airlock, no exhale, no dev board).
 * The goon page is deployed standalone and cannot import outside goon/, so the
 * rules live here twice. If the classic rules change there, change them here.
 *
 * RULES (classic 2048): one merge per tile per move, merges resolve toward the
 * move direction, a move that slides nothing is a no-op (no spawn), one spawn
 * per real move, tier 11 never merges.
 *
 * SEEDING: the spawn stream is a GoonRng (core/rng.js), so both players deal
 * from the same stream for the same duel seed. Every spawn draws exactly THREE
 * rolls (cell, kind, tier), the source file's rule, so the stream never shifts.
 * ==========================================================================*/

import { GoonRng } from '../../core/rng.js';

export const TIER_MAX = 11;
export const SPAWN_TABLE = Object.freeze([[1, 0.90], [2, 0.10]]);

const DIRS = Object.freeze({
  up: { dr: -1, dc: 0 },
  down: { dr: 1, dc: 0 },
  left: { dr: 0, dc: -1 },
  right: { dr: 0, dc: 1 },
});

/** @param {bigint} seed duel seed (see duelSeed in ui/duel/rules.js) */
export function createBoard(seed) {
  const rng = new GoonRng(seed);
  return { n: 4, tiles: [], nextId: 1, rng: () => rng.nextDouble(), score: 0, moves: 0 };
}

export function grid(board) {
  const g = [];
  for (let r = 0; r < board.n; r++) g.push(new Array(board.n).fill(null));
  for (const t of board.tiles) g[t.r][t.c] = t;
  return g;
}

export function emptyCells(board) {
  const g = grid(board);
  const out = [];
  for (let r = 0; r < board.n; r++) for (let c = 0; c < board.n; c++) if (!g[r][c]) out.push({ r, c });
  return out;
}

function canMerge(a, b) {
  return !!a && !!b && a.tier === b.tier && a.tier < TIER_MAX;
}

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

export function isLocked(board) {
  return board.tiles.length >= board.n * board.n && !canMergeAny(board);
}

/** Highest tier on the board (tier 1 = 2, tier 11 = 2048). */
export function deepest(board) {
  let d = 0;
  for (const t of board.tiles) if (t.tier > d) d = t.tier;
  return d;
}

/** Slide + merge. Mutates. @returns {{moved:boolean, merges:Array, score:number}} */
export function move(board, dir) {
  const d = DIRS[dir];
  if (!d) return { moved: false, merges: [], score: 0 };
  const n = board.n;
  const g = grid(board);
  const merges = [];
  const removed = new Set();
  let moved = false;
  let score = 0;

  for (let k = 0; k < n; k++) {
    const line = [];
    for (let i = 0; i < n; i++) {
      let r; let c;
      if (d.dc !== 0) { r = k; c = d.dc < 0 ? i : n - 1 - i; } else { c = k; r = d.dr < 0 ? i : n - 1 - i; }
      if (g[r][c]) line.push(g[r][c]);
    }
    const out = [];
    let i = 0;
    while (i < line.length) {
      const a = line[i];
      const b = line[i + 1];
      if (b && canMerge(a, b)) {
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
    for (let j = 0; j < out.length; j++) {
      let r; let c;
      if (d.dc !== 0) { r = k; c = d.dc < 0 ? j : n - 1 - j; } else { c = k; r = d.dr < 0 ? j : n - 1 - j; }
      const { tile, victim } = out[j];
      if (tile.r !== r || tile.c !== c) { tile.r = r; tile.c = c; moved = true; }
      if (victim) {
        moved = true;
        merges.push({ id: tile.id, tier: tile.tier, r, c });
      }
    }
  }
  if (removed.size) board.tiles = board.tiles.filter((t) => !removed.has(t.id));
  if (moved) { board.score += score; board.moves += 1; }
  return { moved, merges, score };
}

function rollTier(r) {
  let acc = 0;
  for (const [tier, w] of SPAWN_TABLE) { acc += w; if (r < acc) return tier; }
  return SPAWN_TABLE[SPAWN_TABLE.length - 1][0];
}

/** One tile from the seeded stream. Three draws, always. */
export function spawn(board) {
  const rCell = board.rng();
  board.rng();                 // kind roll: unused here, drawn so the stream matches the source
  const rTier = board.rng();
  const empties = emptyCells(board);
  if (!empties.length) return null;
  const cell = empties[Math.min(empties.length - 1, Math.floor(rCell * empties.length))];
  const tile = { id: board.nextId++, tier: rollTier(rTier), r: cell.r, c: cell.c };
  board.tiles.push(tile);
  return tile;
}

export function openingSpawn(board) {
  return [spawn(board), spawn(board)].filter(Boolean);
}

/** One player input: move, and spawn only when something moved. */
export function play(board, dir) {
  const res = move(board, dir);
  if (res.moved) res.spawned = spawn(board);
  return res;
}

/** Board as text, for tests and logs. */
export function serialize(board) {
  return grid(board).map((row) => row.map((t) => (t ? String(t.tier) : '.')).join(' ')).join('\n');
}
