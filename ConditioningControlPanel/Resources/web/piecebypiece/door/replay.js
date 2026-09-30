import { Chess } from '../vendor/chess.js';
const positionOf = chess => Object.fromEntries(chess.board().flat().filter(Boolean).map(p => [p.square, { type: p.type, side: p.color }]));

/** Decode the recorded moves once. A damaged tail stops at the last legal position. */
export function buildReplay(record) {
  const chess = record.fen ? new Chess(record.fen) : new Chess();
  const positions = [positionOf(chess)], moves = [], steps = [null];
  for (const san of record.moves || []) {
    let m;
    try { m = chess.move(san); } catch { break; }
    if (!m) break;
    moves.push(m.san); steps.push(m); positions.push(positionOf(chess));
  }
  return { positions, moves, steps, i: 0, timer: null, side: record.me === 'b' ? 'b' : 'w' };
}

/** Forward moves use the live piece hooks, including the same capture choreography. */
export function showReplayStep(board, replay, index, animate = false) {
  const next = Math.max(0, Math.min(replay.moves.length, index));
  const move = replay.steps[next];
  board.anim?.skip?.();
  if (animate && next === replay.i + 1 && move) {
    const flags = String(move.flags || '');
    if (flags.includes('e')) board.pieces.remove(move.to[0] + move.from[1]);
    const castle = flags.includes('k') || flags.includes('q');
    board.pieces.move(move.from, move.to, { castle });
    if (flags.includes('k')) board.pieces.move('h' + move.from[1], 'f' + move.from[1], { castle });
    if (flags.includes('q')) board.pieces.move('a' + move.from[1], 'd' + move.from[1], { castle });
    if (move.promotion) board.pieces.setPosition(replay.positions[next]);
  } else board.pieces.setPosition(replay.positions[next]);
  replay.i = next;
  board.drag?.markers?.setLastMove(move?.from || null, move?.to || null);
  board.setSide(replay.side, true);
}

export function resultLine(record) {
  const result = record.result || {};
  const who = record.me ? (result.winner === record.me ? 'You won' : 'You lost') : result.winner === 'w' ? 'White won' : 'Black won';
  const reason = result.reason || result.result;
  const why = { checkmate: 'checkmate', resign: 'resignation', resignation: 'resignation', flag: 'time', timeout: 'time', abandon: 'opponent departure', agreement: 'agreement', stalemate: 'stalemate', insufficient: 'insufficient material', insufficient_material: 'insufficient material', repetition: 'threefold repetition', threefold: 'threefold repetition', fifty: 'the fifty-move rule', fifty_move: 'the fifty-move rule' }[reason] || String(reason || '').replace(/_/g, ' ');
  if (!result.winner) return why && why !== 'draw' ? 'Draw by ' + why : 'Draw';
  if (reason === 'abandon') return who + ' after a player left';
  return who + (why ? (why === 'time' ? ' on time' : ' by ' + why) : '');
}
