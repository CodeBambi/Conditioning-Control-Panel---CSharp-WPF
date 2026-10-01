import { Chess } from '../vendor/chess.js';

/**
 * The computer's five strengths, weakest first (owner, 2026-09-30: "add difficulty levels").
 * depth - full plies searched; quiet - chase captures past the last ply, so it stops leaving a
 * piece hanging one move after the horizon (the early "sacrifices" testers saw at Club);
 * budget - ms the worker may spend; window + variety - how far below the best a move may score
 * and still be picked, and among how many; slip - the chance of a plain careless move.
 */
export const LEVELS = Object.freeze({
  beginner: { label: 'Beginner', blurb: 'misses a lot', depth: 1, quiet: false, budget: 150, window: 220, variety: 6, slip: .3 },
  relaxed: { label: 'Relaxed', blurb: 'a gentle warm-up', depth: 1, quiet: true, budget: 250, window: 90, variety: 3, slip: .08 },
  club: { label: 'Club', blurb: 'looks one reply ahead', depth: 2, quiet: true, budget: 600, window: 25, variety: 2, slip: 0 },
  sharp: { label: 'Sharp', blurb: 'plans further ahead', depth: 4, quiet: true, budget: 1500, window: 0, variety: 1, slip: 0 },
  master: { label: 'Master', blurb: 'thinks it all through', depth: 6, quiet: true, budget: 3200, window: 0, variety: 1, slip: 0 },
});
export const LEVEL_ORDER = Object.freeze(['beginner', 'relaxed', 'club', 'sharp', 'master']);
export const levelOf = level => (LEVELS[level] ? level : 'club');

const VALUE = { p: 100, n: 320, b: 335, r: 500, q: 900, k: 0 };
const QUIET_PLIES = 4;
const FILES = 'abcdefgh';
// Search runs on chess.js's own internal moves (vendored and pinned at 1.4.0): the public
// verbose moves build SAN plus a FEN before and after for every move generated, which made
// each node dozens of times slower than the search itself and left Club unable to finish
// one ply with captures read out. Squares are 0x88 indexes: a8 = 0, h1 = 119.
const square = i => FILES[i & 15] + (8 - (i >> 4));
const moveOf = m => ({ from: square(m.from), to: square(m.to), ...(m.promotion ? { promotion: m.promotion } : {}) });
function evaluate(chess) {
  let score = 0;
  const board = chess._board;
  for (let i = 0; i < 128; i++) {
    if (i & 0x88) { i += 7; continue; }
    const p = board[i];
    if (!p) continue;
    const file = i & 15, rank = 7 - (i >> 4);
    const center = 3.5 - (Math.abs(file - 3.5) + Math.abs(rank - 3.5)) / 2;
    const advance = p.color === 'w' ? rank : 7 - rank;
    // Pawns earn their advance in the middle; a flank push for its own sake reads as weak play.
    const activity = p.type === 'p' ? advance * (file >= 2 && file <= 5 ? 8 : 3) + center * 3 : p.type === 'n' || p.type === 'b' ? center * 18 : p.type === 'k' ? -center * 8 : center * 4;
    score += (p.color === 'w' ? 1 : -1) * (VALUE[p.type] + activity);
  }
  return score * (chess._turn === 'w' ? 1 : -1);
}
const priority = m => (m.captured ? 10 * VALUE[m.captured] - VALUE[m.piece] : 0) + (VALUE[m.promotion] || 0);
const byPriority = (a, b) => priority(b) - priority(a);

/** Iterative search runs in a worker. Only completed depths replace the last answer. */
export function chooseMove({ moves = [], fen, level = 'club' } = {}, now = () => performance.now(), random = Math.random) {
  const chess = fen ? new Chess(fen) : new Chess();
  for (const move of moves) chess.move(move);
  if (chess.isGameOver()) return null;
  const tuning = LEVELS[levelOf(level)], deadline = now() + tuning.budget;
  const timeout = {};
  let nodes = 0;
  const tick = () => { if ((++nodes & 15) === 0 && now() >= deadline) throw timeout; };
  const play = m => chess._makeMove(m), unplay = () => chess._undoMove();
  // A position already seen twice in the real game is a draw on the third visit.
  const repeated = () => chess._getPositionCount(chess._hash) >= 2 || chess._halfMoves >= 100;
  // Captures only, standing pat on the static score: a trade is read to its end.
  function quiesce(alpha, beta, ply, left) {
    tick();
    const stand = evaluate(chess);
    if (stand >= beta || left <= 0) return stand;
    if (stand > alpha) alpha = stand;
    const all = chess._moves({ legal: true });
    if (!all.length) return chess.isCheck() ? -100000 + ply : 0;
    const loud = all.filter(m => m.captured || m.promotion).sort(byPriority);
    for (const move of loud) {
      // Even winning this piece outright could not lift the score to alpha: skip it.
      if (!move.promotion && stand + VALUE[move.captured] + 200 < alpha) continue;
      play(move);
      let score;
      try { score = -quiesce(-beta, -alpha, ply + 1, left - 1); } finally { unplay(); }
      if (score >= beta) return score;
      if (score > alpha) alpha = score;
    }
    return alpha;
  }
  function search(depth, alpha, beta, ply) {
    tick();
    if (repeated()) return 0;
    const all = chess._moves({ legal: true });
    if (!all.length) return chess.isCheck() ? -100000 + ply : 0;
    if (depth <= 0) return tuning.quiet ? quiesce(alpha, beta, ply, QUIET_PLIES) : evaluate(chess);
    let best = -Infinity;
    for (const move of all.sort(byPriority)) {
      play(move);
      let score;
      try { score = -search(depth - 1, -beta, -alpha, ply + 1); } finally { unplay(); }
      best = Math.max(best, score); alpha = Math.max(alpha, score);
      if (alpha >= beta) break;
    }
    return best;
  }
  // Each root move is searched against the best so far minus the level's window: moves
  // that could still be picked get an exact score, the rest only need to be shown worse.
  let root = chess._moves({ legal: true }).sort(byPriority), best = root[0], last = null;
  for (let depth = 1; depth <= tuning.depth; depth++) {
    const scores = [];
    let top = -Infinity;
    try {
      for (const move of root) {
        if (now() >= deadline) throw timeout;
        play(move);
        let score;
        try { score = -search(depth - 1, -Infinity, -(top - tuning.window), 1); } finally { unplay(); }
        scores.push({ move, score });
        top = Math.max(top, score);
      }
    } catch (e) {
      if (e !== timeout) throw e;
      // An unfinished first ply still beats the plain move order it would otherwise fall back on.
      if (depth === 1 && scores.length) last = scores.sort((a, b) => b.score - a.score), best = last[0].move;
      break;
    }
    scores.sort((a, b) => b.score - a.score);
    last = scores;
    const choices = scores.filter(s => scores[0].score - s.score <= tuning.window).slice(0, tuning.variety);
    best = choices[Math.min(choices.length - 1, Math.floor(random() * choices.length))].move;
    root = scores.map(s => s.move);
    if (scores[0].score > 99000) break;
  }
  // A careless move now and then, never one that walks into mate and never past a mate of its own.
  if (tuning.slip && last && last[0].score < 99000 && random() < tuning.slip) {
    const safe = last.filter(s => s.score > -99000);
    if (safe.length) best = safe[Math.floor(random() * safe.length)].move;
  }
  return moveOf(best);
}

/**
 * Grade one move for the IQ score (game/iq.js): how many centipawns it gave away against the best
 * move in the same position. The same evaluate and quiescence the computer plays with, so "best"
 * means what this engine can see and no more. Iterative to `depth` inside `budget` ms; the deepest
 * finished pass wins. The played move is searched first with a full window, so its score is exact;
 * the rest only have to beat the best so far. A mate either way is clamped to `mateCp`, so a missed
 * mate costs the cap rather than a number nobody can read. Null when the move is not legal here.
 */
export const GRADE = Object.freeze({ depth: 3, budget: 400, mateCp: 2000 });
export function gradeMove({ fen, move, depth = GRADE.depth, budget = GRADE.budget } = {}, now = () => performance.now()) {
  const chess = fen ? new Chess(fen) : new Chess();
  const all = chess._moves({ legal: true });
  const want = move || {};
  const played = all.find(m => square(m.from) === want.from && square(m.to) === want.to
    && (!m.promotion || m.promotion === (want.promotion || 'q')));
  if (!played) return null;
  const san = m => chess._moveToSan(m, all);
  if (all.length === 1) return { best: san(played), cp: 0, depth: 0 };
  const deadline = now() + budget, timeout = {};
  let nodes = 0;
  const tick = () => { if ((++nodes & 15) === 0 && now() >= deadline) throw timeout; };
  const play = m => chess._makeMove(m), unplay = () => chess._undoMove();
  function quiesce(alpha, beta, ply, left) {
    tick();
    const stand = evaluate(chess);
    if (stand >= beta || left <= 0) return stand;
    if (stand > alpha) alpha = stand;
    const moves = chess._moves({ legal: true });
    if (!moves.length) return chess.isCheck() ? -100000 + ply : 0;
    for (const m of moves.filter(x => x.captured || x.promotion).sort(byPriority)) {
      if (!m.promotion && stand + VALUE[m.captured] + 200 < alpha) continue;
      play(m);
      let score;
      try { score = -quiesce(-beta, -alpha, ply + 1, left - 1); } finally { unplay(); }
      if (score >= beta) return score;
      if (score > alpha) alpha = score;
    }
    return alpha;
  }
  function search(d, alpha, beta, ply) {
    tick();
    const moves = chess._moves({ legal: true });
    if (!moves.length) return chess.isCheck() ? -100000 + ply : 0;
    if (d <= 0) return quiesce(alpha, beta, ply, QUIET_PLIES);
    let best = -Infinity;
    for (const m of moves.sort(byPriority)) {
      play(m);
      let score;
      try { score = -search(d - 1, -beta, -alpha, ply + 1); } finally { unplay(); }
      if (score > best) best = score;
      if (score > alpha) alpha = score;
      if (alpha >= beta) break;
    }
    return best;
  }
  const order = [played, ...all.filter(m => m !== played).sort(byPriority)];
  let done = null;
  for (let d = 1; d <= depth; d++) {
    let top = -Infinity, best = null, mine = 0;
    try {
      for (const m of order) {
        play(m);
        let score;
        try { score = -search(d - 1, -Infinity, -top, 1); } finally { unplay(); }
        if (m === played) mine = score;
        if (score > top) { top = score; best = m; }
      }
    } catch (e) {
      if (e !== timeout) throw e;
      break;
    }
    done = { top, best, mine, d };
    // A mate is in sight. Stop only once the played move mates too: a slower
    // mate (Rb1, Kg8, Rb8#) only shows a pass or two deeper, and it is no blunder.
    if (top > 99000 && mine > 99000) break;
  }
  if (!done) return null;
  const clamp = s => Math.max(-GRADE.mateCp, Math.min(GRADE.mateCp, s));
  const cp = done.best === played ? 0 : Math.max(0, Math.round(clamp(done.top) - clamp(done.mine)));
  return { best: san(done.best), cp, depth: done.d };
}

/**
 * How long the computer sits over the board after the turn card, in seconds (owner, 2026-09-30:
 * "they happen too fast like the AI wasnt thinking"). The worker has usually answered long
 * before; this is the pause a player reads as thinking. A stronger level takes longer, a
 * forced move is quick, and a running clock is never spent on theatre it cannot afford.
 */
export const THINK = Object.freeze({ beginner: [.8, 1.6], relaxed: [1, 2], club: [1.2, 2.4], sharp: [1.5, 3], master: [1.8, 3.6] });
export function thinkSeconds({ level = 'club', legal = 20, clock = null, side = null, random = Math.random } = {}) {
  const [lo, hi] = THINK[levelOf(level)];
  let s = legal <= 1 ? .45 : (lo + (hi - lo) * random()) * (legal < 6 ? .6 : 1);
  const state = clock?.snapshot?.();
  if (state && !state.untimed && state.total !== 0) {
    const least = Math.min(state.w ?? Infinity, state.b ?? Infinity);
    if (least < 30000) return 0;
    const own = side && Number.isFinite(state[side]) ? state[side] : least;
    s = Math.min(s, own / 1000 * .015);
  }
  return Math.max(0, s);
}
