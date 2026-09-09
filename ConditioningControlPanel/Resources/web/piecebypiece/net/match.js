/* ============================================================================
 * net/match.js - one seat at an online game.
 *
 * THE SAME SHAPE AS hotseat.js, deliberately and exactly: rules, clock, start,
 * update, tryMove, takeBack, canPick, legalTargets, resign, plies, turn,
 * isOver, result, autoRemaining. boot.js swaps one for the other and nothing
 * downstream - drag.js, promote.js, hud.js, sfx.js, the whole board - is told
 * which one it got. Every move still lands on the board through the same
 * rules-then-pieces-then-bus path, so the animation, the dust, the thud and
 * the outline all behave the way they always have; the only difference is
 * where half the moves come from.
 *
 * WHAT IS DIFFERENT, AND WHY:
 *
 *   ONE SEAT. `canPick` answers for one colour only, and the camera is set to
 *   that colour once and never swings again. In hotseat the board turns round
 *   because the player does; here he does not.
 *
 *   THE SERVER IS THE REFEREE. This file has a full chess.js in it, and it is
 *   NOT the authority - it is there so the board can draw a legal move the
 *   instant the player makes it, and so an incoming move can be validated
 *   before it is animated. Any disagreement with the server is settled by
 *   throwing our position away and rebuilding from theirs (`resync`). We never
 *   argue; we never decide a result; we never even decide that a flag fell -
 *   `claim_timeout` only asks the server to look at its own clock.
 *
 *   THE CLOCKS ARE DERIVED, NEVER COUNTED. See createRemoteClock below. A
 *   clock that ticked on local wall time would drift against the server's, and
 *   the player would watch his own clock hit zero several seconds before (or
 *   after) the server agreed - which is the one thing a chess clock may not do.
 *
 *   SEQ IS THE WHOLE CONCURRENCY STORY. Every event the match has ever had
 *   carries a seq, contiguous across types. We send the seq we believe we are
 *   at with every move; a 409 means we were wrong, and the answer to a 409 is
 *   always resync, never retry. A gap in the incoming seq means we missed
 *   something, and the answer to that is also resync.
 *
 * OPTIMISM, AND WHAT PAYS FOR IT. A local move is put on the board before the
 * server has seen it, because a board that waits a round trip before the piece
 * moves feels broken. The bill for that is the echo: our own move comes back
 * down the long poll like anybody else's, and applying it twice would corrupt
 * the position. `_pendingUci` is what recognises it - see applyEvent.
 * ==========================================================================*/

import { createRules } from '../game/rules.js';
import { DEFAULT_MS, formatClock } from '../game/clock.js';
import * as defaultApi from './api.js';

/** How often the derived clock repaints. Matches game/clock.js so the HUD looks identical. */
export const TICK_MS = 250;

/** Keep the seat warm this often while the game is live. */
export const HEARTBEAT_MS = 20000;

/** The long poll comes straight back on an error; these are the waits between retries. */
const BACKOFF_MS = [500, 1000, 2000, 4000, 8000, 15000];

const other = (side) => (side === 'w' ? 'b' : 'w');

/** "e2e4", "e7e8q" -> { from, to, promotion }. Null for anything that is not a move. */
export function parseUci(uci) {
  const s = String(uci || '').trim().toLowerCase();
  if (!/^[a-h][1-8][a-h][1-8][qrbn]?$/.test(s)) return null;
  return { from: s.slice(0, 2), to: s.slice(2, 4), promotion: s.length > 4 ? s[4] : null };
}

/** The other direction, for the wire. */
export function toUci(from, to, promotion) {
  return String(from) + String(to) + (promotion ? String(promotion).toLowerCase() : '');
}

/* ---------------------------------------------------------------------------
 * THE DERIVED CLOCK.
 *
 * The server hands us numbers and we do arithmetic on them; we never count
 * time ourselves.
 *
 *     w_ms, b_ms        each side's balance
 *     turn              whose clock is the one running
 *     turn_started_ms   when it started, on the SERVER's timeline
 *     server_now_ms     what the server's timeline read when it answered
 *
 * The offset (server_now_ms minus our own monotonic reading at the moment the
 * answer landed) is what puts the rest on a timeline we can read. Every answer
 * refreshes it, so a machine whose clock is minutes out, or that was suspended
 * and resumed mid-game, converges on the truth at the next poll instead of
 * drifting forever.
 *
 * `now()` is injectable and defaults to performance.now() where it exists.
 * That is not tidiness: Date.now() jumps when the OS syncs its clock or the
 * user changes the timezone, and a jump there would show up as time appearing
 * or vanishing off a running chess clock.
 *
 * THE ONE AMBIGUITY IN THE CONTRACT, AND HOW THIS SURVIVES IT.
 * pbp-online-api.md describes the balances twice. Section 6 (normative) is
 * about the RECORD: it stores "the milliseconds remaining at the moment that
 * side's current turn started". Section 3 is about the WIRE: the clocks in a
 * response are "already discounted by server_now_ms - turn_started_ms for
 * whichever side is to move". Those reconcile - store at turn-start, discount
 * on the way out - and that is the reading this assumes. But section 3's own
 * worked example then prints an UNdiscounted number for the side to move, so
 * one of the three is a typo and there is no way to tell which from here.
 *
 * Guessing wrong is not a rounding error. It is a clock that sawtooths by up to
 * a whole poll interval every time an answer lands, in one direction or the
 * other, on the one number in a chess game nobody will forgive being wrong.
 *
 * So this does not only guess. `stampMs` is "the instant these balances were
 * true", and which instant that is gets MEASURED: across two answers inside the
 * same turn, a server that discounts on the way out reports a FALLING balance
 * for the side to move, and one that hands over the stored number reports the
 * SAME number twice. Either observation settles it for the rest of the match,
 * whichever way the default was pointing.
 *
 * An event's clocks carry no server_now_ms at all (see the contract's event
 * shape) and are stamped at the move they describe, so those are read as
 * at-turn-start whatever the measured mode says.
 * ------------------------------------------------------------------------- */

/** Two answers inside one turn have to be this far apart to be worth comparing. */
const DISCOUNT_PROBE_MS = 250;

export function createRemoteClock({ perSideMs = DEFAULT_MS, now = null } = {}) {
  const clockNow = now || ((typeof performance !== 'undefined' && performance.now)
    ? () => performance.now()
    : () => Date.now());

  const base = { w: perSideMs, b: perSideMs };
  let total = perSideMs;
  let active = null;          // whose clock is running, per the server
  let turnStartedMs = 0;      // server timeline
  let stampMs = 0;            // the server instant `base` is true as at
  let offsetMs = 0;           // server timeline minus ours
  let running = false;
  let synced = false;

  /**
   * 'start' = balances are as at turn_started_ms; 'now' = as at server_now_ms.
   * Starts on 'now' because that is what the contract promises the wire carries,
   * and is corrected by the first measurement that disagrees.
   */
  let mode = 'now';
  let measured = false;
  let probe = null;           // { turn, turnStarted, serverNow, moverBase }

  /** Our best reading of the server's clock, right now. */
  function serverNow() { return clockNow() + offsetMs; }

  /** Fold a fresh `clocks` block in. Anything missing is left as it was. */
  function sync(clocks) {
    if (!clocks || typeof clocks !== 'object') return;

    const hasNow = Number.isFinite(Number(clocks.server_now_ms));
    const nextTurn = (clocks.turn === 'w' || clocks.turn === 'b') ? clocks.turn : active;
    const nextStarted = Number.isFinite(Number(clocks.turn_started_ms))
      ? Number(clocks.turn_started_ms) : turnStartedMs;
    const nextW = Number.isFinite(Number(clocks.w_ms)) ? Math.max(0, Number(clocks.w_ms)) : base.w;
    const nextB = Number.isFinite(Number(clocks.b_ms)) ? Math.max(0, Number(clocks.b_ms)) : base.b;
    const nextMoverBase = nextTurn === 'w' ? nextW : nextB;

    // Which instant are these balances true as at? Measured, not assumed - see
    // the header. Only an answer carrying server_now_ms can say anything, and
    // only when it is the same turn we probed on.
    if (hasNow && !measured && nextTurn) {
      const serverStamp = Number(clocks.server_now_ms);
      if (probe && probe.turn === nextTurn && probe.turnStarted === nextStarted
        && serverStamp - probe.serverNow >= DISCOUNT_PROBE_MS) {
        const fell = probe.moverBase - nextMoverBase;
        // Falling with the clock = the server discounted it for us.
        if (fell >= DISCOUNT_PROBE_MS) { mode = 'now'; measured = true; }
        else if (Math.abs(fell) <= 2) { mode = 'start'; measured = true; }
      }
      if (!probe || probe.turn !== nextTurn || probe.turnStarted !== nextStarted) {
        probe = { turn: nextTurn, turnStarted: nextStarted, serverNow: serverStamp, moverBase: nextMoverBase };
      }
    }

    if (hasNow) offsetMs = Number(clocks.server_now_ms) - clockNow();
    base.w = nextW;
    base.b = nextB;
    active = nextTurn;
    turnStartedMs = nextStarted;
    stampMs = (hasNow && mode === 'now') ? Number(clocks.server_now_ms) : turnStartedMs;
    if (Number.isFinite(Number(clocks.total_ms))) total = Number(clocks.total_ms);
    else total = Math.max(total, base.w, base.b);
    synced = true;
    running = true;
  }

  /**
   * Some answers carry only `server_now_ms` (a long poll that timed out with
   * nothing to report). Taking just that keeps the offset honest through a
   * long think without pretending anything else changed.
   */
  function noteServerNow(ms) {
    if (!Number.isFinite(Number(ms))) return;
    offsetMs = Number(ms) - clockNow();
  }

  function remaining(side) {
    const b = base[side] === undefined ? 0 : base[side];
    if (!running || side !== active) return b;
    return Math.max(0, b - Math.max(0, serverNow() - stampMs));
  }

  /** Freeze where we stand. The game is over; the numbers must stop moving. */
  function stop() {
    if (running) { base.w = remaining('w'); base.b = remaining('b'); }
    running = false;
    active = null;
  }

  return {
    sync,
    noteServerNow,
    remaining,
    stop,
    serverNow,
    snapshot: () => ({ w: remaining('w'), b: remaining('b'), total, active: running ? active : null }),
    isRunning: () => running,
    hasSynced: () => synced,
    /** Which side, if any, has run out on our reading. The SERVER decides the result. */
    flagged: () => {
      if (!running || !active) return null;
      return remaining(active) === 0 ? active : null;
    },
    offset: () => offsetMs,
    /** 'start' or 'now', and whether that was measured or is still the assumption. */
    discountMode: () => ({ mode, measured }),
    // --- surface compatibility with game/clock.js, so nothing downstream cares ---
    // A remote clock is not pressed, credited or reset from this side. These
    // exist so a caller that speaks the hotseat clock's language gets a no-op
    // rather than a TypeError.
    start: () => {},
    press: () => {},
    credit: () => {},
    debit: () => {},
    reset: () => {},
  };
}

/* ---------------------------------------------------------------------------
 * THE DRIVER
 * ------------------------------------------------------------------------- */

/**
 * @param {object} o
 * @param {object} o.bus            game/events.js bus - the same one the board listens on
 * @param {object} o.board          the board API boot.js builds
 * @param {object} [o.hud]          {w, b, status} elements, as hotseat takes them
 * @param {string} o.matchId
 * @param {'w'|'b'} [o.color]       which seat this client has, if the lobby knew
 * @param {object} [o.initial]      a GET match payload we already have, to save a round trip
 * @param {object} [o.api]          net/api.js, swappable for tests
 * @param {() => number} [o.now]    monotonic clock, for the derived clocks
 * @param {(ms:number) => Promise} [o.wait]  the backoff sleep, injectable for tests
 * @param {boolean} [o.autoStart=true] kick the poll and the heartbeat from start()
 */
export function createOnlineMatch({
  bus, board, hud = null, matchId, color = null, initial = null,
  api = defaultApi, now = null, wait = null, autoStart = true,
}) {
  if (!matchId) throw new Error('createOnlineMatch: matchId is required');

  /**
   * WHICH SEAT WE ARE IN. The lobby usually knows - quick match and accept both
   * answer with a colour - but it does NOT always: an outgoing challenge that
   * somebody accepted comes back as a match id and nothing else, because the
   * challenger asked for `random`. So this starts as a guess and is corrected
   * by the first GET match, which carries `you`. Everything reads `me` rather
   * than closing over the constructor argument, precisely so that correction
   * can land.
   */
  let me = (color === 'w' || color === 'b') ? color : 'w';
  let seatKnown = (color === 'w' || color === 'b');
  const rules = createRules();
  const pieces = board.pieces;
  const clock = createRemoteClock({ now });
  const sleep = wait || ((ms) => new Promise((r) => { const t = setTimeout(r, ms); if (t.unref) t.unref(); }));

  let seq = 0;                 // the last event seq we have accounted for
  let over = null;             // the finished result, hotseat's shape
  let status = 'live';
  let disposed = false;
  let pumping = false;
  let ticker = null;
  let beat = null;
  let pendingUci = null;       // a local move on the board that the server has not confirmed
  let inFlight = false;        // a POST move is out; the seat is locked until it lands
  let claimedTimeout = false;  // claim_timeout is asked once a turn, not once a frame
  let drawOffer = null;        // 'me' | 'them' | null, as the last draw_offer event left it
  let resyncing = false;
  let opponentOnline = true;   // last word from the server, not a guess of ours
  /**
   * When the server last heard from us. The events long poll stamps our
   * heartbeat server-side (contract section 3), so a client that is polling
   * normally never needs to send one - and a match played entirely inside the
   * poll loop should cost zero heartbeat calls. This is what makes that true.
   */
  let lastContactMs = 0;

  /* ----------------------------------------------------------------- paint */

  function clocks() {
    const s = clock.snapshot();
    return { w: s.w, b: s.b, total: s.total };
  }

  function statusLine(end) {
    const who = end.winner === 'w' ? 'white' : 'black';
    const mine = end.winner === me;
    if (end.result === 'checkmate') return mine ? 'checkmate, you win' : 'checkmate, ' + who + ' wins';
    if (end.result === 'flag') return mine ? 'his flag fell, you win' : 'your flag fell';
    if (end.result === 'resign') return mine ? 'he resigned, you win' : 'you resigned';
    if (end.result === 'abandon') return mine ? 'he left, you win' : 'you left';
    if (end.result === 'stalemate') return 'stalemate, a draw';
    return 'a draw by ' + (end.reason || 'agreement');
  }

  function paint() {
    if (!hud) return;
    const s = clock.snapshot();
    hud.w.textContent = formatClock(s.w);
    hud.b.textContent = formatClock(s.b);
    hud.w.classList.toggle('on', s.active === 'w' && !over);
    hud.b.classList.toggle('on', s.active === 'b' && !over);
    if (over) { hud.status.textContent = statusLine(over); return; }
    if (rules.inCheck()) { hud.status.textContent = 'check'; return; }
    hud.status.textContent = rules.turn() === me ? 'your move' : 'waiting for his move';
  }

  function startTicker() {
    if (ticker || disposed) return;
    ticker = setInterval(() => {
      if (over) return;
      bus.emit('clock', clock.snapshot());
      paint();
      // OUR reading says a flag is down. We do not act on that; we ask the
      // server to look at its own clock, once, and wait for its verdict.
      const down = clock.flagged();
      if (down && !claimedTimeout) {
        claimedTimeout = true;
        api.claimTimeout(matchId).catch(() => {});
      }
    }, TICK_MS);
    if (ticker.unref) ticker.unref();
  }

  function startHeartbeat() {
    if (beat || disposed) return;
    beat = setInterval(() => {
      if (over || disposed) return;
      // Only when the poll has gone quiet. A healthy match never gets here.
      if (Date.now() - lastContactMs < HEARTBEAT_MS) return;
      lastContactMs = Date.now();
      api.heartbeat(matchId).catch(() => {});
    }, HEARTBEAT_MS);
    if (beat.unref) beat.unref();
  }

  function stopTimers() {
    if (ticker) { clearInterval(ticker); ticker = null; }
    if (beat) { clearInterval(beat); beat = null; }
  }

  /* --------------------------------------------------------------- endings */

  function finish(end) {
    if (over) return;
    over = end;
    status = 'over';
    clock.stop();
    stopTimers();
    paint();
    bus.emit('gameover', { result: end.result, winner: end.winner === undefined ? null : end.winner });
  }

  /**
   * The contract's nine `result.reason` values, mapped onto the endings the
   * board and the HUD already know how to draw. The mapping lives here and only
   * here - hotseat's vocabulary is what the rest of the page speaks, and
   * translating at the door is what keeps every listener downstream ignorant of
   * whether this game was played online.
   */
  const REASON_KIND = {
    checkmate: 'checkmate',
    stalemate: 'stalemate',
    insufficient_material: 'draw',
    threefold: 'draw',
    fifty_move: 'draw',
    agreement: 'draw',
    draw: 'draw',
    resign: 'resign',
    timeout: 'flag',
    abandon: 'abandon',
  };

  /**
   * The server's `status`/`result` fields, turned into hotseat's ending shape.
   * A finished match resyncs into a finished board - reconnecting to a game you
   * already lost must not put you back in a live seat.
   */
  function endingFrom(state) {
    const st = String((state && state.status) || '').toLowerCase();
    if (!st || st === 'live' || st === 'active' || st === 'playing') return null;
    const r = (state && state.result) || {};
    const winner = (r.winner === 'w' || r.winner === 'b') ? r.winner : null;
    const reason = String(r.reason || r.result || r.type || st).toLowerCase();
    return {
      result: REASON_KIND[reason] || (winner ? 'resign' : 'draw'),
      winner,
      reason,
      /** Elo movement, when the match was rated. Null otherwise; the HUD may show it. */
      ratingDelta: r.rating_delta || null,
      endedMs: Number.isFinite(Number(r.ended_ms)) ? Number(r.ended_ms) : null,
    };
  }

  /* ------------------------------------------------------- moving the men */

  /**
   * Put a ply on the board. Lifted from hotseat.tryMove on purpose: the men,
   * the rook that castles with the king, the man who came off, the buzz on a
   * check and every bus event are all identical, because they have to be - the
   * board has one way of showing a move and this is it.
   *
   * Returns the referee's record, or null when the ply is not legal HERE, which
   * for a remote move means our position has drifted and the caller resyncs.
   */
  function playMove(from, to, promotion) {
    const played = rules.move(from, to, promotion || 'q');
    if (!played) return null;

    if (played.capturedSquare && played.capturedSquare !== to) pieces.remove(played.capturedSquare);
    pieces.move(from, to);
    const hop = rules.rookHop(played);
    if (hop) pieces.move(hop.from, hop.to);
    if (played.promotion) pieces.setPosition(rules.position());

    if (played.captured) {
      bus.emit('capture', {
        by: played.side,
        piece: played.captured,
        square: played.capturedSquare,
        victimSide: played.capturedSide,
      });
    }
    if (played.check) {
      if (board.buzzCheck) board.buzzCheck(played.to);
      bus.emit('check', { side: played.turn });
    }

    // The referee may see a mate or a stalemate before the server's event says
    // so. The board shows it either way; the server's word still arrives and
    // finish() is idempotent, so the two never fight.
    const end = rules.result();
    if (end) finish(end);
    else bus.emit('turn', { side: played.turn, ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    paint();
    return played;
  }

  /* --------------------------------------------------------------- resync */

  /**
   * Throw our position away and rebuild from the server's. The one answer to
   * every disagreement: a 409, a gap in the seq, a move that will not play, a
   * poll that came back with something we cannot read, a reconnection.
   *
   * The moves list is REPLAYED rather than the fen simply loaded, so the
   * referee keeps its history - threefold repetition needs it, and so does the
   * move list in the HUD. The fen is the check on that replay and the fallback
   * when it does not agree.
   */
  async function resync() {
    if (disposed || resyncing) return false;
    resyncing = true;
    try {
      const res = await api.match(matchId);
      if (!res.ok || !res.data) return false;
      applyState(res.data);
      return true;
    } finally { resyncing = false; }
  }

  /** Adopt a whole GET match payload. Also the way the first position arrives. */
  function applyState(state) {
    if (!state || disposed) return;

    // THE SEAT, FROM THE SERVER. `you` is authoritative and settles the case the
    // lobby cannot answer: an outgoing challenge accepted at `color: random`
    // hands back a match id with no colour attached to it.
    if (state.you === 'w' || state.you === 'b') {
      const changed = state.you !== me;
      me = state.you;
      seatKnown = true;
      if (changed) bus.emit('seat', { color: me });
    }
    if (state.white || state.black) {
      bus.emit('players', {
        white: state.white || null,
        black: state.black || null,
        me,
        opponent: (me === 'w' ? state.black : state.white) || null,
      });
    }

    const moves = Array.isArray(state.moves) ? state.moves : [];
    let rebuilt = true;
    try {
      rules.chess.reset();
      for (const uci of moves) {
        const m = parseUci(uci);
        if (!m || !rules.move(m.from, m.to, m.promotion || 'q')) { rebuilt = false; break; }
      }
    } catch (err) { rebuilt = false; }
    // Either the replay fell over, or it landed somewhere the server does not
    // recognise. Either way the fen wins - a board that disagrees with the
    // server about where the men are is worse than one with no history.
    if (state.fen && (!rebuilt || rules.fen() !== state.fen)) {
      try { rules.chess.load(state.fen); } catch (err) { /* keep what we have */ }
    }

    pieces.setPosition(rules.position());
    if (Number.isFinite(Number(state.seq))) seq = Number(state.seq);
    pendingUci = null;
    claimedTimeout = false;
    clock.sync(state.clocks);

    // A draw offer survives a disconnect (it is on the match record), so a
    // reconnecting client has to find it again or the offer sits there unseen.
    const offer = (state.draw_offer === 'w' || state.draw_offer === 'b') ? state.draw_offer : null;
    if (offer !== (drawOffer === 'me' ? me : (drawOffer === 'them' ? other(me) : null))) {
      drawOffer = offer ? (offer === me ? 'me' : 'them') : null;
      if (offer) bus.emit('draw-offer', { by: offer, mine: offer === me });
    }

    // The seat's own side, once. The camera does not swing in an online game:
    // the player looks down the board from where he is sitting, all game.
    board.setSide(me, true);

    const end = endingFrom(state);
    if (end) { finish(end); return; }

    bus.emit('resync', { seq, ply: rules.ply(), fen: rules.fen(), turn: rules.turn() });
    bus.emit('turn', { side: rules.turn(), ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    paint();
  }

  /* --------------------------------------------------------------- events */

  /**
   * One event off the long poll. Returns 'ok' when it was accounted for, or
   * 'resync' when the caller must rebuild.
   */
  function applyEvent(ev) {
    if (!ev || typeof ev !== 'object') return 'ok';
    const evSeq = Number(ev.seq);
    if (!Number.isFinite(evSeq)) return 'resync';
    if (evSeq <= seq) return 'ok';           // already accounted for; the echo of our own move
    if (evSeq > seq + 1) return 'resync';    // a gap: something happened that we never saw

    /**
     * An ending event carries the server's whole `result` block, and that is
     * always preferred: it is the same object GET match would give us, so a
     * game that ends down the poll and one that ends on a reconnect finish
     * through one code path and read identically. The fallback below it is for
     * an event that arrived with only a `by`.
     */
    const endOf = (fallback) => endingFrom({
      status: 'done',
      result: (ev.result && typeof ev.result === 'object') ? ev.result : fallback,
    });

    switch (String(ev.type || '')) {
      case 'move': {
        // Our own move, coming back down the poll before (or instead of) the
        // POST's answer. It is already on the board; take the seq and nothing
        // else, or the position gets it twice.
        if (pendingUci && String(ev.uci || '').toLowerCase() === pendingUci) {
          pendingUci = null;
        } else {
          const m = parseUci(ev.uci);
          if (!m) return 'resync';
          if (!playMove(m.from, m.to, m.promotion)) return 'resync';
          // The event carries the position it produced. If our referee landed
          // somewhere else, one of us is wrong and it is not the server.
          if (typeof ev.fen === 'string' && ev.fen && rules.fen() !== ev.fen) return 'resync';
        }
        // A move clears any standing offer server-side (it cannot survive a
        // change of position), so it clears here too.
        drawOffer = null;
        break;
      }
      case 'resign': {
        const by = (ev.by === 'w' || ev.by === 'b') ? ev.by : (ev.side === 'w' || ev.side === 'b' ? ev.side : other(me));
        finish(endOf({ winner: other(by), reason: 'resign' }));
        break;
      }
      case 'draw_offer': {
        const by = (ev.by === 'w' || ev.by === 'b') ? ev.by : other(me);
        drawOffer = by === me ? 'me' : 'them';
        bus.emit('draw-offer', { by, mine: by === me });
        break;
      }
      case 'draw_decline': {
        drawOffer = null;
        bus.emit('draw-decline', { by: ev.by || null });
        break;
      }
      case 'draw': {
        drawOffer = null;
        finish(endOf({ winner: null, reason: ev.reason || 'agreement' }));
        break;
      }
      case 'timeout': {
        const side = (ev.side === 'w' || ev.side === 'b') ? ev.side : (ev.loser === 'w' || ev.loser === 'b' ? ev.loser : null);
        finish(endOf({ winner: side ? other(side) : null, reason: 'timeout' }));
        break;
      }
      case 'abandon': {
        const side = (ev.side === 'w' || ev.side === 'b') ? ev.side : null;
        finish(endOf({ winner: side ? other(side) : null, reason: 'abandon' }));
        break;
      }
      default:
        // An event type this build does not know. Its seq still counts, so the
        // gap check keeps working and a newer server cannot wedge an older page.
        bus.emit('match-event', ev);
        break;
    }
    seq = evSeq;
    if (ev.clocks) clock.sync(ev.clocks);
    // A new turn is a new chance for a flag to fall, and a fresh right to say so.
    claimedTimeout = false;
    return 'ok';
  }

  /** A whole `events` answer. */
  function applyEnvelope(data) {
    if (!data || typeof data !== 'object') return 'resync';
    if (Number.isFinite(Number(data.server_now_ms))) clock.noteServerNow(Number(data.server_now_ms));
    if (data.opponent_online !== undefined) {
      const on = data.opponent_online !== false;
      if (on !== opponentOnline) { opponentOnline = on; bus.emit('opponent', { online: on }); }
    }
    const list = Array.isArray(data.events) ? data.events : [];
    // Ordered by seq before anything is applied: the gap check is only worth
    // having if the stream it checks is actually in order.
    const sorted = list.slice().sort((a, b) => Number(a && a.seq) - Number(b && b.seq));
    for (const ev of sorted) {
      if (applyEvent(ev) === 'resync') return 'resync';
      if (over) break;      // a result ends the stream; anything after it is noise
    }
    if (data.clocks) clock.sync(data.clocks);
    return 'ok';
  }

  /**
   * The long poll, forever, until the game ends or the seat is disposed. It is
   * a loop rather than a chain of timeouts so there is exactly one of it: two
   * pumps against the same `seq` would each apply half the events.
   */
  async function pump() {
    if (pumping || disposed) return;
    pumping = true;
    let miss = 0;
    try {
      while (!disposed && !over) {
        const res = await api.events(matchId, seq);
        if (disposed || over) break;
        if (res.ok) {
          miss = 0;
          lastContactMs = Date.now();     // the poll stamped our heartbeat server-side
          if (applyEnvelope(res.data) === 'resync') { await resync(); }
          continue;
        }
        // A stale or unknown seq is not a network problem - the server is
        // telling us our idea of the match is wrong, and that is a resync.
        if (res.error === api.ApiError.Stale || res.error === api.ApiError.NotFound) {
          if (await resync()) { miss = 0; continue; }
        }
        const backoff = BACKOFF_MS[Math.min(miss, BACKOFF_MS.length - 1)];
        miss++;
        bus.emit('net', { state: 'retrying', error: res.error, attempt: miss, inMs: backoff });
        await sleep(backoff);
        if (disposed || over) break;
        // Coming back from a gap in the connection, the board may be several
        // plies behind. Ask for the whole thing rather than for events since a
        // seq that may itself have expired.
        if (miss >= 2) await resync();
      }
    } finally { pumping = false; }
  }

  /* ------------------------------------------------------- the seat's moves */

  function canPick(square) {
    if (over || !square || inFlight) return false;
    if (rules.turn() !== me) return false;
    const man = rules.pieceAt(square);
    return !!man && man.color === me;
  }

  function legalTargets(square) { return canPick(square) ? rules.targets(square) : []; }

  /**
   * The player's own move. Goes on the board first and on the wire second, and
   * the seat is locked (`inFlight`) until the wire answers - two moves in
   * flight against one seq is exactly the race the seq is there to prevent.
   */
  function tryMove(from, to, promotion = 'q') {
    if (over || inFlight) return null;
    if (rules.turn() !== me) return null;

    const seqAtSend = seq;
    const played = playMove(from, to, promotion);
    if (!played) return null;

    const uci = toUci(played.from, played.to, played.promotion);
    pendingUci = uci;
    inFlight = true;

    (async () => {
      const res = await api.move(matchId, seqAtSend, uci);
      inFlight = false;
      if (disposed) return;
      lastContactMs = Date.now();
      if (res.ok && res.data) {
        if (Number.isFinite(Number(res.data.seq))) seq = Math.max(seq, Number(res.data.seq));
        pendingUci = null;
        clock.sync(res.data.clocks);
        // The move that finished the game finishes it here too, in the same
        // answer, without waiting for the poll to come round.
        const end = endingFrom(res.data);
        if (end) { finish(end); return; }
        paint();
        return;
      }
      // Refused, or never answered. Either way the board in front of the
      // player may be showing a move that did not happen, so the position
      // stops being ours and becomes the server's again.
      pendingUci = null;
      bus.emit('net', { state: 'move-refused', error: res.error, uci, serverError: res.serverError || null });

      // A 409 comes back WITH the whole current state (contract section 3), so
      // the common refusal costs no extra round trip: the state in the refusal
      // IS the resync. Only a refusal that arrived without one has to go and
      // ask.
      if (res.data && typeof res.data.fen === 'string' && Array.isArray(res.data.moves)) {
        applyState(res.data);
        return;
      }
      const ok = await resync();
      if (!ok && !disposed) { await sleep(BACKOFF_MS[0]); resync(); }
    })();

    return played;
  }

  /* ------------------------------------------------------------- the verbs */

  function resign() {
    if (over) return;
    // Not applied locally: resigning is the server's to record, and a client
    // that ended its own game would be deciding a result.
    api.resign(matchId).catch(() => {});
  }

  function offerDraw() { if (!over) api.draw(matchId, 'offer').catch(() => {}); }
  function acceptDraw() { if (!over) api.draw(matchId, 'accept').catch(() => {}); }
  function declineDraw() { if (!over) api.draw(matchId, 'decline').catch(() => {}); }

  /* --------------------------------------------------------------- lifecycle */

  function start() {
    board.setSide(me, true);
    if (initial) applyState(initial);
    else pieces.setPosition(rules.position());
    paint();
    startTicker();
    if (!autoStart) return;
    startHeartbeat();
    // The first thing that happens is a resync, even with an `initial` in hand:
    // whatever we were handed by the lobby is already a moment old, and the
    // clocks in it are the ones the board is about to draw.
    (async () => {
      await resync();
      if (!disposed) pump();
    })();
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    stopTimers();
    clock.stop();
  }

  return {
    // --- the hotseat surface, verbatim ---
    rules,
    clock,
    start,
    /** Nothing to step: an online game has no demo mode and no local auto-play. */
    update() {},
    tryMove,
    /** There are no take-backs online. Answering null is what makes drag.js boing. */
    takeBack: () => null,
    canPick,
    legalTargets,
    resign,
    plies: () => rules.ply(),
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => 0,

    // --- what only an online seat has ---
    // GETTERS, not values: the seat can be corrected by the first GET match
    // (see `me`), and a UI holding a stale copy would draw the wrong board.
    /** 'w' or 'b' - the one seat this client is sitting in. */
    get color() { return me; },
    /** Whether that came from the server or is still the lobby's guess. */
    get seatKnown() { return seatKnown; },
    /** Which sides this client may move. boot.js emits it as the `local` event. */
    get seats() { return [me]; },
    matchId,
    offerDraw,
    acceptDraw,
    declineDraw,
    claimTimeout: () => api.claimTimeout(matchId).catch(() => {}),
    /** 'me' when we offered, 'them' when he did, null when nothing stands. */
    drawOffer: () => drawOffer,
    /** The server's last word on whether he is still there. */
    opponentOnline: () => opponentOnline,
    seq: () => seq,
    status: () => status,
    dispose,

    // --- seams the smoke run drives; not part of the driver's public life ---
    _resync: resync,
    _pump: pump,
    _applyEnvelope: applyEnvelope,
    _applyState: applyState,
    _inFlight: () => inFlight,
  };
}
