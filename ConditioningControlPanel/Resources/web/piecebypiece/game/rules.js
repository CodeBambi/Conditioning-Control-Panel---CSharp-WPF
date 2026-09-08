/* ============================================================================
 * game/rules.js - the referee.
 *
 * A thin wrapper over chess.js. Standard chess, no house rules: everything the
 * board and the effects layer know about legality comes from here, so there is
 * exactly one place that decides what a position allows.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';

/** Result of a finished game, or null while it is still on. */
export function readResult(chess) {
  if (chess.isCheckmate()) return { result: 'checkmate', winner: chess.turn() === 'w' ? 'b' : 'w', reason: 'checkmate' };
  if (chess.isStalemate()) return { result: 'stalemate', winner: null, reason: 'stalemate' };
  if (chess.isInsufficientMaterial()) return { result: 'draw', winner: null, reason: 'insufficient material' };
  if (chess.isThreefoldRepetition()) return { result: 'draw', winner: null, reason: 'threefold repetition' };
  if (chess.isDrawByFiftyMoves()) return { result: 'draw', winner: null, reason: 'fifty move rule' };
  if (chess.isDraw()) return { result: 'draw', winner: null, reason: 'draw' };
  return null;
}

export function createRules(fen) {
  const chess = fen ? new Chess(fen) : new Chess();

  /** The position as the board wants it: { e1: {type, side}, ... }. */
  function position() {
    const map = {};
    for (const row of chess.board()) {
      for (const cell of row) {
        if (cell) map[cell.square] = { type: cell.type, side: cell.color };
      }
    }
    return map;
  }

  /** Legal destinations from a square, verbose, for highlighting and dragging. */
  function movesFrom(square) {
    if (!square) return [];
    try { return chess.moves({ square, verbose: true }); } catch { return []; }
  }

  function targets(square) {
    return movesFrom(square).map((m) => m.to);
  }

  function legalMove(from, to) {
    return movesFrom(from).find((m) => m.to === to) || null;
  }

  /**
   * Play a move. Returns null when it is not legal, otherwise a plain record:
   * { from, to, san, piece, side, captured, capturedSide, square, check,
   *   promotion, castle, enPassant }.
   * `captured` is the type of the man that came off, `square` is where it stood
   * (which is not `to` for an en passant capture).
   */
  function move(from, to, promotion = 'q') {
    const legal = legalMove(from, to);
    if (!legal) return null;
    let played;
    try { played = chess.move({ from, to, promotion: legal.promotion ? promotion : undefined }); } catch { return null; }
    if (!played) return null;
    const side = played.color;
    const victimSide = side === 'w' ? 'b' : 'w';
    const enPassant = played.isEnPassant ? played.isEnPassant() : String(played.flags || '').includes('e');
    let capturedSquare = null;
    if (played.captured) {
      capturedSquare = enPassant ? played.to[0] + (side === 'w' ? Number(played.to[1]) - 1 : Number(played.to[1]) + 1) : played.to;
    }
    return {
      from: played.from,
      to: played.to,
      san: played.san,
      piece: played.piece,
      side,
      captured: played.captured || null,
      capturedSide: played.captured ? victimSide : null,
      capturedSquare,
      promotion: played.promotion || null,
      enPassant,
      castle: String(played.flags || '').includes('k') ? 'k' : (String(played.flags || '').includes('q') ? 'q' : null),
      check: chess.isCheck(),
      turn: chess.turn(),
    };
  }

  /** A castling king move drags the rook with it. Returns {from,to} or null. */
  function rookHop(played) {
    if (!played || !played.castle) return null;
    const rank = played.side === 'w' ? '1' : '8';
    return played.castle === 'k'
      ? { from: 'h' + rank, to: 'f' + rank }
      : { from: 'a' + rank, to: 'd' + rank };
  }

  /** One random legal move, for the demo and the smoke run. */
  function randomMove(rand = Math.random) {
    const all = chess.moves({ verbose: true });
    if (all.length === 0) return null;
    const pick = all[Math.floor(rand() * all.length)];
    return move(pick.from, pick.to, 'q');
  }

  return {
    chess,
    position, movesFrom, targets, legalMove, move, rookHop, randomMove,
    turn: () => chess.turn(),
    inCheck: () => chess.isCheck(),
    isOver: () => chess.isGameOver(),
    result: () => readResult(chess),
    fen: () => chess.fen(),
    ply: () => chess.history().length,
    pieceAt: (sq) => chess.get(sq) || null,
  };
}
