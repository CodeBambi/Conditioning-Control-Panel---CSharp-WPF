/* ============================================================================
 * games/composure/solver.js - COMPOSURE's baseline solver. PURE: no DOM, no
 * clock, no ctx, no rng.
 *
 * IT EXISTS FOR TWO REASONS, and neither of them is playing the game for you:
 *   1. THE PAR TABLE. grade.js scores moves against a baseline, and a baseline
 *      pulled out of the air is a rubric that lies. `solve(state).length` is
 *      the number the par table is derived from (dossier open question 3,
 *      closed by the contract: CORE owns the baseline solver).
 *   2. THE SKILL-FLOOR RESCUE. The critic's top fix is law - after 20 stuck
 *      seconds the class lights the solver's next move and caps its own grade
 *      (hardGates.sGate = false). `nextMove(state)` is that light.
 *
 * TWO SOLVERS, chosen by size:
 *   3x3   OPTIMAL. IDA* on manhattan + linear conflict, with a bidirectional
 *         BFS backstop (the 8-puzzle's whole state space is 181440, so the
 *         backstop is bounded and cannot hang). The par table for the 3x3
 *         board is therefore the true god number for that scramble.
 *   4x4   HUMAN. Row-by-row / column-by-column reduction to a 3x3, which is
 *   5x5   then solved optimally. Not optimal overall - it is a BASELINE, the
 *         move count a careful player would actually spend - and it always
 *         terminates, which matters more here than optimality.
 *
 * THE REDUCTION walks a line's tiles home one at a time and its LAST TWO
 * together, each walk a complete BFS over (tracked tile positions + gap
 * position) inside the unlocked region - which is what makes it immune to the
 * dead-end deadlock a greedy placer walks into. See the long note above
 * `route()`. After each line the region shrinks; at 3x3 the optimal solver
 * finishes the job.
 *
 * A MOVE, everywhere in this file, is the POSITION OF THE TILE THAT SLIDES -
 * which is exactly what a click on the board is, so a hint is a cell, not a
 * gesture. `toMoves()` re-plays a path to hand back {pos, id, dir}.
 *
 * EVERY entry point is null-safe and total: a solver that cannot answer
 * returns null, and the class then runs with no par and no hint rather than
 * throwing inside a class the player is in the middle of.
 * ==========================================================================*/

import {
  BLANK, cloneState, createState, isSolved, neighbours, rowOf, colOf, posOf, dirBetween,
} from './board.js';

/** IDA* abort ceiling. A random 8-puzzle finishes four orders of magnitude
 *  under this; crossing it means something is wrong, not that we should hang. */
const NODE_CAP = 4000000;
/** The 8-puzzle's reachable half-space. The BFS backstop can never exceed it. */
const BFS_CAP = 200000;
/** Loop guard - a reduction that runs past this is a bug, not a puzzle. */
const LINE_GUARD = 64;

/* ==========================================================================
 * small helpers over a raw cells array
 * ======================================================================== */
function solvedArr(cells) {
  for (let i = 0; i < cells.length - 1; i++) if (cells[i] !== i) return false;
  return cells[cells.length - 1] === BLANK;
}

/* ==========================================================================
 * THE OPTIMAL 3x3
 * ======================================================================== */
const N3 = 3;
const NB3 = (() => {
  const out = [];
  for (let i = 0; i < 9; i++) out.push(neighbours(N3, i));
  return out;
})();

/** manhattan + 2 x linear conflicts (an admissible, much sharper heuristic). */
function h3(cells) {
  let sum = 0;
  for (let i = 0; i < 9; i++) {
    const id = cells[i];
    if (id === BLANK) continue;
    sum += Math.abs(rowOf(N3, i) - rowOf(N3, id)) + Math.abs(colOf(N3, i) - colOf(N3, id));
  }
  let conflicts = 0;
  for (let line = 0; line < N3; line++) {
    for (let a = 0; a < N3; a++) {
      for (let b = a + 1; b < N3; b++) {
        const ra = cells[posOf(N3, line, a)]; const rb = cells[posOf(N3, line, b)];
        if (ra !== BLANK && rb !== BLANK
          && rowOf(N3, ra) === line && rowOf(N3, rb) === line
          && colOf(N3, ra) > colOf(N3, rb)) conflicts += 1;
        const ca = cells[posOf(N3, a, line)]; const cb = cells[posOf(N3, b, line)];
        if (ca !== BLANK && cb !== BLANK
          && colOf(N3, ca) === line && colOf(N3, cb) === line
          && rowOf(N3, ca) > rowOf(N3, cb)) conflicts += 1;
      }
    }
  }
  return sum + 2 * conflicts;
}

/** IDA*. Returns the optimal path (tile positions) or null if it aborted. */
function ida3(start) {
  const state = start.slice();
  if (solvedArr(state)) return [];
  let blank = state.indexOf(BLANK);
  const path = [];
  let nodes = 0;
  let aborted = false;

  function dfs(g, bound, prevBlank) {
    const f = g + h3(state);
    if (f > bound) return f;
    if (solvedArr(state)) return -1;
    nodes += 1;
    if (nodes > NODE_CAP) { aborted = true; return -1e9; }
    let min = Infinity;
    for (const nb of NB3[blank]) {
      if (nb === prevBlank) continue;
      const pb = blank;
      const tile = state[nb];
      state[pb] = tile; state[nb] = BLANK; blank = nb;
      path.push(nb);
      const r = dfs(g + 1, bound, pb);
      if (r === -1) return -1;
      if (r === -1e9) return -1e9;
      if (r < min) min = r;
      path.pop();
      state[nb] = tile; state[pb] = BLANK; blank = pb;
    }
    return min;
  }

  let bound = h3(state);
  for (let round = 0; round < 64; round++) {
    const r = dfs(0, bound, -1);
    if (r === -1) return path.slice();
    if (aborted) return null;
    if (!Number.isFinite(r)) return null;
    bound = r;
  }
  return null;
}

/** The bounded backstop: plain BFS over the reachable half-space (<= 181440). */
function bfs3(start) {
  if (solvedArr(start)) return [];
  const key = (a) => a.join(',');
  const seen = new Map();
  seen.set(key(start), null);
  const q = [start.slice()];
  let head = 0;
  while (head < q.length && seen.size < BFS_CAP) {
    const cur = q[head++];
    const blank = cur.indexOf(BLANK);
    for (const nb of NB3[blank]) {
      const next = cur.slice();
      next[blank] = next[nb];
      next[nb] = BLANK;
      const k = key(next);
      if (seen.has(k)) continue;
      seen.set(k, { from: key(cur), pos: nb });
      if (solvedArr(next)) {
        const out = [];
        let step = seen.get(k);
        let at = k;
        while (step) { out.push(step.pos); at = step.from; step = seen.get(at); }
        return out.reverse();
      }
      q.push(next);
    }
  }
  return null;
}

/** The 3x3, optimally. `cells` is a 9-length array of ids 0..7 + BLANK. */
export function solve3x3(cells) {
  if (!Array.isArray(cells) || cells.length !== 9) return null;
  const fast = ida3(cells);
  if (fast) return fast;
  return bfs3(cells);
}

/* ==========================================================================
 * THE REDUCTION (4x4 / 5x5)
 *
 * Line by line - top row, left column, top row, left column - until a 3x3 is
 * left, which the optimal solver finishes. Placed cells are LOCKED: the gap
 * never enters one, so a finished line is never disturbed.
 *
 * HOW A TILE IS WALKED, and why it is a search rather than a heuristic. The
 * obvious method is greedy: BFS the tile toward its home, BFS the gap to the
 * next step of that route, slide, repeat. It reads well and it DEADLOCKS.
 * Locking a line leaves DEAD-END cells (hold the end of a row and the cell
 * beside it has exactly one free neighbour), and a gap parked in one can only
 * leave through the very tile it is pushing - while every way of pushing that
 * tile back out puts the gap straight back in. The two shuffle forever. It cost
 * one board in five, and a board this game cannot solve is a board it must not
 * deal.
 *
 * So the walk is a BFS over the state that actually matters: the positions of
 * the TILES BEING TRACKED plus the position of the GAP. Every other tile in the
 * region is interchangeable - nothing downstream cares which anonymous tile
 * ends up where - so one tile is at worst 25x24 states, and the LAST TWO of a
 * line (searched together, straight to their homes, with no staging dance at
 * all) is at worst 25x24x23. Both are nothing, both are complete, and both are
 * optimal for their sub-problem. A configuration with no answer says so instead
 * of spinning.
 * ======================================================================== */
function work(state) {
  return { n: state.n, cells: state.cells.slice(), blank: state.blank, path: [] };
}

/** Slide the tile at `pos` (which MUST be adjacent to the gap) into the gap. */
function step(w, pos) {
  w.cells[w.blank] = w.cells[pos];
  w.cells[pos] = BLANK;
  w.blank = pos;
  w.path.push(pos);
}

/**
 * Walk one or more TRACKED tiles to their targets without ever moving the gap
 * through `blocked`. BFS over (tracked positions..., gap position).
 *
 * @param {Object} w
 * @param {Array<{id:number,target:number}>} tracked
 * @param {Set<number>} blocked
 */
function route(w, tracked, blocked) {
  const n = w.n;
  const startPos = tracked.map((t) => w.cells.indexOf(t.id));
  const goal = tracked.map((t) => t.target);
  if (startPos.some((p) => p < 0)) throw new Error('cp-solver: a tracked tile is not on the board');
  const done = (list) => list.every((p, i) => p === goal[i]);
  if (done(startPos)) return;

  const key = (list, gap) => list.join(',') + '|' + gap;
  const prev = new Map();
  prev.set(key(startPos, w.blank), null);
  const queue = [[startPos, w.blank]];
  let head = 0;
  let found = null;

  while (head < queue.length && found === null) {
    const cur = queue[head++];
    const list = cur[0];
    const gap = cur[1];
    for (const next of neighbours(n, gap)) {
      if (blocked.has(next)) continue;
      const moved = list.slice();
      const which = list.indexOf(next);
      if (which >= 0) moved[which] = gap;          // the gap swapped with a tracked tile
      const k = key(moved, next);
      if (prev.has(k)) continue;
      prev.set(k, { from: key(list, gap), move: next });
      if (done(moved)) { found = k; break; }
      queue.push([moved, next]);
    }
  }
  if (found === null) throw new Error('cp-solver: no route for tile ' + tracked.map((t) => t.id).join('+'));

  const seq = [];
  let at = found;
  let node = prev.get(at);
  while (node) { seq.push(node.move); at = node.from; node = prev.get(at); }
  seq.reverse();
  for (const pos of seq) step(w, pos);
}

/** Solve row `r` of the region whose left edge is `c0`. Marks the row locked. */
function solveRow(w, r, c0, locked) {
  const n = w.n;
  for (let c = c0; c <= n - 3; c++) {
    const home = posOf(n, r, c);
    route(w, [{ id: home, target: home }], locked);
    locked.add(home);
  }
  /* The last two go home TOGETHER - the pair is one search state, so the
   * staging rotation every naive solver needs (and the dead end it creates)
   * is simply not part of this solver. */
  const p1 = posOf(n, r, n - 2);
  const p2 = posOf(n, r, n - 1);
  route(w, [{ id: p1, target: p1 }, { id: p2, target: p2 }], locked);
  locked.add(p1);
  locked.add(p2);
}

/** Solve column `c` of the region whose top edge is `r0`. Mirrors solveRow. */
function solveCol(w, c, r0, locked) {
  const n = w.n;
  for (let r = r0; r <= n - 3; r++) {
    const home = posOf(n, r, c);
    route(w, [{ id: home, target: home }], locked);
    locked.add(home);
  }
  const p1 = posOf(n, n - 2, c);
  const p2 = posOf(n, n - 1, c);
  route(w, [{ id: p1, target: p1 }, { id: p2, target: p2 }], locked);
  locked.add(p1);
  locked.add(p2);
}

/** The remaining 3x3 at (r0,c0), relabelled into its own coordinates. */
function finish3x3(w, r0, c0) {
  const n = w.n;
  const sub = [];
  for (let r = 0; r < 3; r++) {
    for (let c = 0; c < 3; c++) {
      const id = w.cells[posOf(n, r0 + r, c0 + c)];
      if (id === BLANK) { sub.push(BLANK); continue; }
      const hr = rowOf(n, id) - r0;
      const hc = colOf(n, id) - c0;
      if (hr < 0 || hc < 0 || hr > 2 || hc > 2) throw new Error('cp-solver: a placed tile escaped the region');
      sub.push(hr * 3 + hc);
    }
  }
  const path = solve3x3(sub);
  if (!path) return false;
  for (const sp of path) step(w, posOf(n, r0 + Math.floor(sp / 3), c0 + (sp % 3)));
  return true;
}

/** The row/column reduction, then the optimal 3x3. Returns a path or null. */
function reduce(state) {
  const w = work(state);
  const locked = new Set();
  let r0 = 0;
  let c0 = 0;
  let guard = 0;
  while ((w.n - r0) > 3 || (w.n - c0) > 3) {
    guard += 1;
    if (guard > LINE_GUARD) return null;
    if ((w.n - r0) >= (w.n - c0)) { solveRow(w, r0, c0, locked); r0 += 1; }
    else { solveCol(w, c0, r0, locked); c0 += 1; }
  }
  if (!finish3x3(w, r0, c0)) return null;
  return w.path;
}

/* ==========================================================================
 * PUBLIC SURFACE
 * ======================================================================== */
/** Re-play a path of tile positions into {pos, id, dir} moves. */
export function toMoves(state, path) {
  const s = cloneState(state);
  const out = [];
  for (const pos of (path || [])) {
    const id = s.cells[pos];
    const dir = dirBetween(s.n, pos, s.blank);
    s.cells[s.blank] = id;
    s.cells[pos] = BLANK;
    s.blank = pos;
    out.push({ pos, id, dir });
  }
  return out;
}

/**
 * The baseline solution for a board.
 * @returns {Array<{pos:number,id:number,dir:string}>|null} null = no answer
 *          (an aborted search or a board this file could not reduce); the
 *          class then runs with no par and no hint, never with a throw.
 */
export function solve(state) {
  if (!state || !Array.isArray(state.cells)) return null;
  try {
    if (isSolved(state)) return [];
    const n = state.n;
    const path = (n === 3) ? solve3x3(state.cells.slice()) : reduce(state);
    if (!path) return null;
    const moves = toMoves(state, path);
    /* Never hand back a "solution" that does not solve: replay it and check. */
    const check = cloneState(state);
    for (const m of moves) {
      if (check.cells[m.pos] !== m.id) return null;
      check.cells[check.blank] = m.id;
      check.cells[m.pos] = BLANK;
      check.blank = m.pos;
    }
    return isSolved(check) ? moves : null;
  } catch (e) {
    return null;
  }
}

/** The one move the rescue lights up, or null. */
export function nextMove(state) {
  const moves = solve(state);
  return (moves && moves.length) ? moves[0] : null;
}

/** The baseline move count par is derived from; -1 when there is no answer. */
export function baselineLength(state) {
  const moves = solve(state);
  return moves ? moves.length : -1;
}

/** Convenience for tests: a baseline straight off a raw cells array. */
export function baselineFor(n, cells) {
  return baselineLength(createState(n, cells));
}

export default { solve, nextMove, baselineLength, baselineFor, solve3x3, toMoves };
