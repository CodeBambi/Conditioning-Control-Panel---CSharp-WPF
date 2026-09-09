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
  // One entry a ply, so a take-back can put the clocks back where they
  // stood before it. Nothing else reads it.
  const history = [];
  let lastBack = 0;
  const TAKEBACK_GAP_MS = 400;   // two in a row need a breath between them

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
    const before = { w: clock.remaining('w'), b: clock.remaining('b') };
    const played = rules.move(from, to, promotion);
    if (!played) return null;
    history.push({ from: played.from, to: played.to, side: played.side, before });

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

  /**
   * Take the last ply back. Hotseat only, and only while the game is on: after
   * a result there is nothing to undo, the board is a record.
   *
   * The referee goes back a ply, the mover slides home through anim (so the
   * landing dust and the thud play as they would for any move), and whatever
   * else the ply did - a man taken, a rook that castled, a pawn that came back
   * a pawn - is put right by rebuilding from the position. The clocks go back
   * to what they read before the ply, so a take-back costs the mover nothing
   * but the time he spends thinking again.
   */
  function takeBack(now = Date.now()) {
    if (over) return null;
    if (now - lastBack < TAKEBACK_GAP_MS) return null;
    const record = history[history.length - 1] || null;
    const back = rules.undo();
    if (!back) return null;
    history.pop();
    lastBack = now;

    // A promotion undo swaps the man for a pawn, so there is nothing to slide:
    // the rebuild below stands him back on his square.
    if (!back.promotion) {
      pieces.move(back.to, back.from);
      const hop = rules.rookHop(back);
      if (hop) pieces.move(hop.to, hop.from);
    }
    pieces.setPosition(rules.position());

    const side = rules.turn();          // the side that moved has the move again
    if (record) {
      clock.credit('w', record.before.w - clock.remaining('w'));
      clock.credit('b', record.before.b - clock.remaining('b'));
    }
    clock.press(side);
    board.setSide(side);
    bus.emit('takeback', { from: back.from, to: back.to, ply: rules.ply() });
    bus.emit('turn', { side, ply: rules.ply(), clocks: clocks(), total: clockMs });
    paint();
    return back;
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
    rules, clock, start, update, tryMove, takeBack, canPick, legalTargets, resign,
    plies: () => history.length,
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => autoLeft,
  };
}
