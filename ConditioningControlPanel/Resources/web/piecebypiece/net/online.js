/* ============================================================================
 * net/online.js - how the online seat gets onto the board that is already there.
 *
 * THE PROBLEM THIS SOLVES. "boot.js can swap drivers" is easy to say and not
 * quite true of the page as built: createDrag, createPromote, createHud and
 * createSfx are all handed `game` ONCE, at construction, and hold that
 * reference for the life of the page. Replacing window.PBP.game would swap the
 * driver for exactly one reader - whoever looked it up last - and leave the
 * mouse still talking to the hotseat.
 *
 * So the thing boot hands out is not a driver, it is a SWITCH: one stable
 * object with the driver's surface that forwards every call to whichever driver
 * is current. Everything downstream keeps its reference forever and none of it
 * ever learns that the game it is drawing changed hands.
 *
 * `rules` and `clock` are getters rather than copied fields, and that is the
 * load-bearing detail: every reader takes them fresh inside a function
 * (drag.js reads game.rules per drag, promote.js reads game.clock per offer,
 * hud.js and sfx.js per frame), so a getter reaches the live driver while a
 * plain property would pin the first one forever.
 * ==========================================================================*/

import { createOnlineMatch } from './match.js';

/**
 * One stable driver-shaped object, forwarding to whichever driver is current.
 * @param {object} initial the driver to start with (the hotseat)
 */
export function createDriverSwitch(initial) {
  let cur = initial;

  return {
    // Fresh every read - see the header.
    get rules() { return cur.rules; },
    get clock() { return cur.clock; },

    start(...a) { return cur.start(...a); },
    update(dt) { return cur.update ? cur.update(dt) : undefined; },
    tryMove(...a) { return cur.tryMove(...a); },
    takeBack(...a) { return cur.takeBack ? cur.takeBack(...a) : null; },
    canPick(...a) { return cur.canPick(...a); },
    legalTargets(...a) { return cur.legalTargets(...a); },
    resign(...a) { return cur.resign(...a); },
    plies() { return cur.plies(); },
    turn() { return cur.turn(); },
    isOver() { return cur.isOver(); },
    result() { return cur.result(); },
    autoRemaining() { return cur.autoRemaining ? cur.autoRemaining() : 0; },
    reset(...a) { return cur.reset ? cur.reset(...a) : undefined; },
    /**
     * The game for the shelf. The door reads this on gameover, so a driver
     * without one hands back an empty game rather than a TypeError - but both
     * drivers have one, and forwarding it is what makes the shelf keep moves.
     */
    record(...a) { return cur.record ? cur.record(...a) : { moves: [], plies: 0, result: null, clocks: null }; },

    // --- what only an online seat answers; a hotseat gets the quiet no-op ---
    // The HUD holds this switch, never the driver, so the draw verbs have to
    // come through here to reach the seat at all.
    offerDraw(...a) { return cur.offerDraw ? cur.offerDraw(...a) : undefined; },
    acceptDraw(...a) { return cur.acceptDraw ? cur.acceptDraw(...a) : undefined; },
    declineDraw(...a) { return cur.declineDraw ? cur.declineDraw(...a) : undefined; },
    /** 'me' | 'them' | null online; null in a hotseat, where nobody offers anybody anything. */
    drawOffer() { return cur.drawOffer ? cur.drawOffer() : null; },
    /** The server's last word on the opponent; a hotseat opponent is always here. */
    opponentOnline() { return cur.opponentOnline ? cur.opponentOnline() : true; },

    /** The driver underneath, for anything that genuinely needs the real one. */
    get current() { return cur; },
    /** Which seats the player may move. Hotseat says both; an online seat says one. */
    get seats() { return cur.seats || ['w', 'b']; },
    /** True while an online driver is in the chair. */
    get isOnline() { return !!cur.matchId; },

    /**
     * Put a different driver in the chair. The outgoing one is disposed, which
     * for an online seat is what stops its poll, its heartbeat and its clock -
     * leaving those running would have two drivers writing to one board.
     */
    switchTo(next) {
      if (!next || next === cur) return cur;
      const old = cur;
      cur = next;
      if (old && typeof old.dispose === 'function') {
        try { old.dispose(); } catch (err) { console.warn('[pbp] old driver dispose threw', err); }
      }
      return cur;
    },

    /**
     * Put the hotseat back. The seat it was holding for the page is the one
     * the page started with, never disposed (a hotseat has nothing to stop),
     * and it is what "play here" after an online game has to be dealt against:
     * reset-and-start on a FINISHED online seat resyncs the finished match and
     * fires its gameover a second time. Idempotent - the hotseat in the chair
     * already is simply left there.
     */
    switchBack() {
      if (cur === initial) return cur;
      const old = cur;
      cur = initial;
      if (old && typeof old.dispose === 'function') {
        try { old.dispose(); } catch (err) { console.warn('[pbp] online driver dispose threw', err); }
      }
      return cur;
    },

    dispose() { if (cur && typeof cur.dispose === 'function') cur.dispose(); },
  };
}

/**
 * Take a Match from the lobby and put an online seat in the chair.
 *
 * `match` is the lobby's shape - `{ id, opponent, side, clockMs, state? }`. The
 * `state` is the GET match payload the lobby already fetched to learn `side`,
 * and passing it through saves the driver's first round trip; it is optional
 * and the driver asks for itself when it is missing.
 *
 * The camera is set to the player's own side and the `local` event is emitted
 * with ONE seat in it, which is what tells the effects layer that only half the
 * board belongs to the person holding the mouse.
 *
 * @returns the online driver, already switched in and started.
 */
export function startOnlineMatch({ bus, board, hud = null, game, match, api = null, now = null }) {
  if (!match || !match.id) throw new Error('startOnlineMatch: a match with an id is required');

  const driver = createOnlineMatch({
    bus,
    board,
    hud,
    matchId: match.id,
    color: (match.side === 'w' || match.side === 'b') ? match.side : null,
    initial: match.state || null,
    api: api || undefined,
    now: now || undefined,
  });

  if (game && typeof game.switchTo === 'function') game.switchTo(driver);
  bus.emit('local', { sides: driver.seats, mode: 'online', match });
  driver.start();
  return driver;
}
