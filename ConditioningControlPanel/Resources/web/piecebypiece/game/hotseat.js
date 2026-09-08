/* ============================================================================
 * game/hotseat.js - two players, one screen, one board.
 *
 * Owns the whole match: the referee (rules.js), the two clocks (clock.js), the
 * men on the board and every event the effects layer listens to. The camera
 * swings to whoever is to move, so the player in the seat always looks down the
 * board from their own side.
 * ==========================================================================*/

import { createRules } from './rules.js';
import { createClock, DEFAULT_MS, formatClock } from './clock.js';

export function createHotseat({ bus, board, hud = null, clockMs = DEFAULT_MS, fen, auto = 0, autoDelay = 0.45 }) {
  const rules = createRules(fen);
  const pieces = board.pieces;
  let over = null;
  let autoLeft = Math.max(0, Number(auto) || 0);
  let autoWait = 0.6;

  const clock = createClock({
    perSideMs: clockMs,
    onTick: (snap) => { bus.emit('clock', snap); paint(); },
    onFlag: (side) => finish({ result: 'flag', winner: side === 'w' ? 'b' : 'w', reason: 'flag' }),
  });

  function clocks() {
    const s = clock.snapshot();
    return { w: s.w, b: s.b, total: s.total };
  }

  function paint() {
    if (!hud) return;
    const s = clock.snapshot();
    hud.w.textContent = formatClock(s.w);
    hud.b.textContent = formatClock(s.b);
    hud.w.classList.toggle('on', s.active === 'w' && !over);
    hud.b.classList.toggle('on', s.active === 'b' && !over);
    if (over) hud.status.textContent = statusLine(over);
    else hud.status.textContent = rules.inCheck() ? 'check' : (rules.turn() === 'w' ? 'white to move' : 'black to move');
  }

  function statusLine(end) {
    const who = end.winner === 'w' ? 'white' : 'black';
    if (end.result === 'checkmate') return 'checkmate, ' + who + ' wins';
    if (end.result === 'flag') return 'time, ' + who + ' wins';
    if (end.result === 'resign') return who + ' wins by resignation';
    if (end.result === 'stalemate') return 'stalemate, a draw';
    return 'a draw by ' + (end.reason || 'agreement');
  }

  function finish(end) {
    if (over) return;
    over = end;
    autoLeft = 0;
    clock.stop();
    paint();
    bus.emit('gameover', { result: end.result, winner: end.winner ?? null });
  }

  /** Whose men the player in the seat may pick up right now. */
  function canPick(square) {
    if (over || !square) return false;
    const man = rules.pieceAt(square);
    return !!man && man.color === rules.turn();
  }

  function legalTargets(square) {
    return canPick(square) ? rules.targets(square) : [];
  }

  function tryMove(from, to, promotion = 'q') {
    if (over) return null;
    const played = rules.move(from, to, promotion);
    if (!played) return null;

    // The men follow the referee.
    if (played.capturedSquare && played.capturedSquare !== to) pieces.remove(played.capturedSquare);
    pieces.move(from, to);
    const hop = rules.rookHop(played);
    if (hop) pieces.move(hop.from, hop.to);
    if (played.promotion) pieces.setPosition(rules.position());

    if (played.captured) {
      bus.emit('capture', {
        by: played.side,
        piece: played.captured,          // the man that came off
        square: played.capturedSquare,
        victimSide: played.capturedSide,
      });
    }
    if (played.check) {
      if (board.buzzCheck) board.buzzCheck(played.to);   // the man giving check rattles
      bus.emit('check', { side: played.turn });
    }

    const end = rules.result();
    if (end) {
      finish(end);
    } else {
      clock.press(played.turn);
      board.setSide(played.turn);
      bus.emit('turn', { side: played.turn, ply: rules.ply(), clocks: clocks(), total: clockMs });
    }
    paint();
    return played;
  }

  function resign(side) {
    finish({ result: 'resign', winner: side === 'w' ? 'b' : 'w', reason: 'resignation' });
  }

  /** Demo and smoke helper: play n random legal moves, then sit still. */
  function update(dt) {
    if (autoLeft <= 0 || over) return;
    autoWait -= dt;
    if (autoWait > 0) return;
    autoWait = autoDelay;
    const all = rules.chess.moves({ verbose: true });
    if (all.length === 0) { autoLeft = 0; return; }
    const pick = all[Math.floor(Math.random() * all.length)];
    autoLeft--;
    tryMove(pick.from, pick.to, 'q');
  }

  function start() {
    pieces.setPosition(rules.position());
    board.setSide(rules.turn(), true);
    clock.start(rules.turn());
    paint();
    bus.emit('turn', { side: rules.turn(), ply: rules.ply(), clocks: clocks(), total: clockMs });
  }

  return {
    rules, clock, start, update, tryMove, canPick, legalTargets, resign,
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => autoLeft,
  };
}
