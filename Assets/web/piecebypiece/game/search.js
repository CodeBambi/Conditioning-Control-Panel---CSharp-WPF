import { Chess } from '../vendor/chess.js';

export const LEVELS = Object.freeze({
  relaxed: { label: 'Relaxed', depth: 1, budget: 120, variety: 3 },
  club: { label: 'Club', depth: 2, budget: 350, variety: 1 },
  sharp: { label: 'Sharp', depth: 4, budget: 1100, variety: 1 },
});
const VALUE = { p: 100, n: 320, b: 335, r: 500, q: 900, k: 0 };
const moveOf = m => ({ from: m.from, to: m.to, ...(m.promotion ? { promotion: m.promotion } : {}) });
function evaluate(chess) {
  let score = 0;
  for (const row of chess.board()) for (const p of row) if (p) {
    const file = p.square.charCodeAt(0) - 97, rank = Number(p.square[1]) - 1;
    const center = 3.5 - (Math.abs(file - 3.5) + Math.abs(rank - 3.5)) / 2;
    const advance = p.color === 'w' ? rank : 7 - rank;
    const activity = p.type === 'p' ? advance * 8 + center * 3 : p.type === 'n' || p.type === 'b' ? center * 18 : p.type === 'k' ? -center * 8 : center * 4;
    score += (p.color === 'w' ? 1 : -1) * (VALUE[p.type] + activity);
  }
  return score * (chess.turn() === 'w' ? 1 : -1);
}
function ordered(chess) {
  const priority = m => (m.captured ? 10 * VALUE[m.captured] - VALUE[m.piece] : 0) + (VALUE[m.promotion] || 0) + (m.san.includes('+') ? 30 : 0);
  return chess.moves({ verbose: true }).sort((a, b) => priority(b) - priority(a));
}

/** Iterative search runs in a worker. Only completed depths replace the last answer. */
export function chooseMove({ moves = [], fen, level = 'club' } = {}, now = () => performance.now(), random = Math.random) {
  const chess = fen ? new Chess(fen) : new Chess();
  for (const move of moves) chess.move(move);
  if (chess.isGameOver()) return null;
  const tuning = LEVELS[level] || LEVELS.club, deadline = now() + tuning.budget;
  const timeout = {};
  let nodes = 0;
  function search(depth, alpha, beta, ply) {
    if ((++nodes & 15) === 0 && now() >= deadline) throw timeout;
    if (chess.isCheckmate()) return -100000 + ply;
    if (chess.isDraw()) return 0;
    if (depth <= 0) return evaluate(chess);
    let best = -Infinity;
    for (const move of ordered(chess)) {
      chess.move(moveOf(move));
      let score;
      try { score = -search(depth - 1, -beta, -alpha, ply + 1); } finally { chess.undo(); }
      best = Math.max(best, score); alpha = Math.max(alpha, score);
      if (alpha >= beta) break;
    }
    return best;
  }
  let root = ordered(chess), best = root[0];
  for (let depth = 1; depth <= tuning.depth; depth++) {
    const scores = [];
    try {
      for (const move of root) {
        if (now() >= deadline) throw timeout;
        chess.move(moveOf(move));
        let score;
        try { score = -search(depth - 1, -Infinity, Infinity, 1); } finally { chess.undo(); }
        scores.push({ move, score });
      }
    } catch (e) { if (e !== timeout) throw e; break; }
    scores.sort((a, b) => b.score - a.score);
    const choices = scores.filter(s => scores[0].score - s.score < 90).slice(0, tuning.variety);
    best = choices[Math.min(choices.length - 1, Math.floor(random() * choices.length))].move;
    root = scores.map(s => s.move);
    if (scores[0].score > 99000) break;
  }
  return moveOf(best);
}
