/* ============================================================================
 * net/watch.js - a seat in the stands: one public online game, watched.
 *
 * THE SAME SHAPE AS net/match.js and game/hotseat.js (rules, clock, start,
 * update, tryMove, takeBack, canPick, legalTargets, resign, record, plies, turn,
 * isOver, result), so the driver switch in net/online.js can put it in the chair
 * and nothing downstream - the board, the HUD, the sound - is told it changed.
 * What differs, and why:
 *
 *   NOBODY HERE MOVES. canPick is always false and tryMove always refused, so
 *   drag.js never lifts a man. No resign, no draw, no claim_timeout and no
 *   heartbeat: a spectator is not a seat and the server never counts him as one.
 *
 *   TEN SECONDS LATE, ON PURPOSE (owner, 2026-10-05). The watch routes hand
 *   back the game cut off `delay_ms` behind the live one, so nobody can coach a
 *   player over Discord. The clocks are derived exactly as an online seat's are
 *   (createRemoteClock), on the DELAYED timeline: every answer's server_now_ms
 *   is read as server_now_ms - delay_ms, which is the instant the board shows.
 *
 *   BOTH SIDES ARE GRADED. `seats` is empty (nobody here owns a colour) and
 *   `graded` is both, which game/iq.js reads off the `local` event: the stands
 *   see both players' IQ, worked out on this page from the move list. No IQ
 *   ever travels over the wire, and a player never sees the opponent's.
 *
 *   SEQ IS STILL THE WHOLE STORY. A gap in the stream, a move that will not
 *   play or a position that disagrees is a resync off GET watch, as online.
 *
 *   A GAME THAT CLOSES SAYS SO. A refusal the server words (the game stopped
 *   being watchable, it is gone, or the route is off) ends the poll and emits
 *   `watch-closed`; the door takes the player back to the tables.
 * ==========================================================================*/

import { createRules } from '../game/rules.js';
import { formatClock } from '../game/clock.js';
import { createRemoteClock, parseUci, endingFrom, TICK_MS } from './match.js';
import * as defaultApi from './api.js';

/** The delay the brief sets server-side; the real number comes on every answer as delay_ms. */
export const WATCH_DELAY_MS = 10000;

const BACKOFF_MS = [500, 1000, 2000, 4000, 8000, 15000];
const SIDES = ['w', 'b'];
const other = (side) => (side === 'w' ? 'b' : 'w');
const sideWord = (s) => (s === 'w' ? 'white' : 'black');

/** A seat's player off the wire -> { name, rating }. */
export function playerOf(p) {
  if (!p) return null;
  if (typeof p === 'string') return { name: p, rating: null };
  const r = Number(p.rating);
  return { name: String(p.display_name || p.name || 'someone'), rating: (p.rating === null || p.rating === undefined || !Number.isFinite(r)) ? null : Math.round(r) };
}

/**
 * The instant a watch answer describes: the server's now, minus the delay. Null
 * when the answer carries no server_now_ms.
 */
export function cutoffOf(data, fallbackDelay = WATCH_DELAY_MS) {
  if (!data || !Number.isFinite(Number(data.server_now_ms))) return null;
  const delay = Number.isFinite(Number(data.delay_ms)) ? Math.max(0, Number(data.delay_ms)) : fallbackDelay;
  return Number(data.server_now_ms) - delay;
}

/** The end of a watched game in plain words: no "you" in it, the stands won nothing. */
export function watchLine(end) {
  if (!end) return '';
  const who = end.winner ? sideWord(end.winner) : '';
  const loser = end.winner ? sideWord(other(end.winner)) : '';
  if (end.result === 'checkmate') return 'checkmate, ' + who + ' wins';
  if (end.result === 'flag') return loser + "'s flag fell, " + who + ' wins';
  if (end.result === 'resign') return loser + ' resigned, ' + who + ' wins';
  if (end.result === 'abandon') return loser + ' left, ' + who + ' wins';
  if (end.result === 'stalemate') return 'stalemate, a draw';
  return 'a draw by ' + String(end.reason || 'agreement').replace(/_/g, ' ');
}

/** The refusals that end a watch: no retry will change them. */
const CLOSED = new Set(['not_watchable', 'your_match', 'not_found', 'not_deployed', 'unauthorized']);

/**
 * @param {object} o
 * @param {object} o.bus, o.board  the page's bus and board, as net/match.js takes them
 * @param {object} [o.hud]         {w, b, status} elements
 * @param {string} o.matchId
 * @param {object} [o.initial]     a GET watch answer we already have
 * @param {object} [o.api]         net/api.js, swappable for tests
 * @param {() => number} [o.now]   monotonic clock, for the derived clocks
 * @param {(ms:number) => Promise} [o.wait]  the backoff sleep
 * @param {boolean} [o.autoStart=true] kick the poll from start()
 */
export function createWatchSession({
  bus, board, hud = null, matchId, initial = null,
  api = defaultApi, now = null, wait = null, autoStart = true,
}) {
  if (!matchId) throw new Error('createWatchSession: matchId is required');
  const rules = createRules();
  const pieces = board.pieces;
  const clock = createRemoteClock({ now });
  const sleep = wait || ((ms) => new Promise((r) => { const t = setTimeout(r, ms); if (t.unref) t.unref(); }));

  let seq = 0;
  let over = null;
  let closed = null;            // the reason the stands were shut, once they are
  let disposed = false;
  let pumping = false;
  let resyncing = false;
  let ticker = null;
  let delayMs = WATCH_DELAY_MS;
  let watchers = 0;
  let players = { w: null, b: null };
  let sided = false;            // the camera is set once; after that a flip is the watcher's own

  /* ---------------------------------------------------------------- paint */

  function clocks() { const s = clock.snapshot(); return { w: s.w, b: s.b, total: s.total }; }

  function paint() {
    if (!hud) return;
    const s = clock.snapshot();
    if (hud.w) { hud.w.textContent = formatClock(s.w); hud.w.classList?.toggle('on', s.active === 'w' && !over); }
    if (hud.b) { hud.b.textContent = formatClock(s.b); hud.b.classList?.toggle('on', s.active === 'b' && !over); }
    if (!hud.status || hud.status.dataset?.hudOwned) return;
    if (over) hud.status.textContent = watchLine(over);
    else hud.status.textContent = rules.inCheck() ? 'check' : sideWord(rules.turn()) + ' to move';
  }

  function tick() {
    if (over || disposed) return;
    bus.emit('clock', clock.snapshot());
    paint();
  }

  function stopTicker() { if (ticker) { clearInterval(ticker); ticker = null; } }

  function finish(end) {
    if (over || !end) return;
    over = end;
    clock.stop();
    stopTicker();
    paint();
    bus.emit('gameover', { result: end.result, winner: end.winner === undefined ? null : end.winner });
  }

  function close(reason) {
    if (closed || disposed) return;
    closed = reason || 'closed';
    stopTicker();
    bus.emit('watch-closed', { matchId, reason: closed });
  }

  /* ------------------------------------------------------------- the men */

  /** A ply onto the board, the way net/match.js plays the opponent's. Null when it is not legal here. */
  function playMove(from, to, promotion) {
    const played = rules.move(from, to, promotion || 'q');
    if (!played) return null;
    if (played.capturedSquare && played.capturedSquare !== to) pieces.remove(played.capturedSquare);
    pieces.move(from, to);
    const hop = rules.rookHop(played);
    if (hop) pieces.move(hop.from, hop.to);
    if (played.promotion) pieces.setPosition(rules.position());
    if (played.captured) {
      bus.emit('capture', { by: played.side, piece: played.captured, square: played.capturedSquare, victimSide: played.capturedSide });
    }
    if (played.check) {
      if (board.buzzCheck) board.buzzCheck(played.to);
      bus.emit('check', { side: played.turn });
    }
    const end = rules.result();
    if (end) finish(end);
    else bus.emit('turn', { side: played.turn, ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    paint();
    return played;
  }

  /* ---------------------------------------------------------------- state */

  function noteMeta(data) {
    if (Number.isFinite(Number(data.delay_ms))) delayMs = Math.max(0, Number(data.delay_ms));
    if (Number.isFinite(Number(data.watchers))) {
      const n = Math.max(0, Math.trunc(Number(data.watchers)));
      if (n !== watchers) { watchers = n; bus.emit('watchers', { count: n }); }
    }
  }

  /** A clocks block, read on the delayed timeline. */
  function syncClocks(block, cut) {
    if (!block || typeof block !== 'object') return;
    clock.sync(cut === null ? block : Object.assign({}, block, { server_now_ms: cut }));
  }

  /** Adopt a whole GET watch answer. Answers whether it carried a game. */
  function applyWatch(data) {
    if (disposed || !data || typeof data !== 'object' || !data.match || typeof data.match !== 'object') return false;
    noteMeta(data);
    const state = data.match;
    const cut = cutoffOf(data, delayMs);

    const next = { w: playerOf(state.white), b: playerOf(state.black) };
    if (JSON.stringify(next) !== JSON.stringify(players)) {
      players = next;
      bus.emit('players', { white: next.w, black: next.b, me: null, opponent: null, watching: true });
    }

    const moves = Array.isArray(state.moves) ? state.moves : [];
    let rebuilt = true;
    try {
      rules.chess.reset();
      for (const uci of moves) {
        const m = parseUci(uci);
        if (!m || !rules.move(m.from, m.to, m.promotion || 'q')) { rebuilt = false; break; }
      }
    } catch { rebuilt = false; }
    if (state.fen && (!rebuilt || rules.fen() !== state.fen)) {
      try { rules.chess.load(state.fen); } catch { /* keep what we have */ }
    }
    pieces.setPosition(rules.position());
    if (Number.isFinite(Number(state.seq))) seq = Number(state.seq);
    syncClocks(state.clocks, cut);
    if (!sided) { sided = true; board.setSide('w', true); }

    const end = endingFrom(state);
    if (end) { finish(end); return true; }
    bus.emit('resync', { seq, ply: rules.ply(), fen: rules.fen(), turn: rules.turn() });
    bus.emit('turn', { side: rules.turn(), ply: rules.ply(), clocks: clocks(), total: clock.snapshot().total });
    paint();
    return true;
  }

  /** One event off the delayed poll: 'ok', or 'resync' when the board must be rebuilt. */
  function applyEvent(ev) {
    if (!ev || typeof ev !== 'object') return 'ok';
    const evSeq = Number(ev.seq);
    if (!Number.isFinite(evSeq)) return 'resync';
    if (evSeq <= seq) return 'ok';
    if (evSeq > seq + 1) return 'resync';
    const endOf = (fallback) => endingFrom({ status: 'done', result: (ev.result && typeof ev.result === 'object') ? ev.result : fallback });
    const by = (ev.by === 'w' || ev.by === 'b') ? ev.by : (ev.side === 'w' || ev.side === 'b' ? ev.side : null);
    switch (String(ev.type || '')) {
      case 'move': {
        const m = parseUci(ev.uci);
        if (!m || !playMove(m.from, m.to, m.promotion)) return 'resync';
        if (typeof ev.fen === 'string' && ev.fen && rules.fen() !== ev.fen) return 'resync';
        break;
      }
      case 'resign': finish(endOf({ winner: by ? other(by) : null, reason: 'resign' })); break;
      case 'draw': finish(endOf({ winner: null, reason: ev.reason || 'agreement' })); break;
      case 'timeout': finish(endOf({ winner: by ? other(by) : (ev.loser === 'w' || ev.loser === 'b' ? other(ev.loser) : null), reason: 'timeout' })); break;
      case 'abandon': finish(endOf({ winner: by ? other(by) : null, reason: 'abandon' })); break;
      // draw offers and anything newer: the stands have nothing to say to them, but the seq counts
      default: break;
    }
    seq = evSeq;
    if (ev.clocks) clock.sync(ev.clocks);   // stamped at the move: read as at turn start, as online
    return 'ok';
  }

  /** A whole events answer. */
  function applyEnvelope(data) {
    if (!data || typeof data !== 'object') return 'resync';
    noteMeta(data);
    const cut = cutoffOf(data, delayMs);
    if (cut !== null) clock.noteServerNow(cut);
    const list = Array.isArray(data.events) ? data.events : [];
    const sorted = list.slice().sort((a, b) => Number(a && a.seq) - Number(b && b.seq));
    for (const ev of sorted) {
      if (applyEvent(ev) === 'resync') return 'resync';
      if (over) break;
    }
    if (data.clocks) syncClocks(data.clocks, cut);
    return 'ok';
  }

  /** Rebuild from GET watch. A worded refusal closes the stands. */
  async function resync() {
    if (disposed || resyncing || closed) return false;
    resyncing = true;
    try {
      const res = await api.watch(matchId);
      if (disposed) return false;
      if (!res.ok) { if (CLOSED.has(res.error)) close(res.error); return false; }
      return applyWatch(res.data);
    } finally { resyncing = false; }
  }

  async function pump() {
    if (pumping || disposed) return;
    pumping = true;
    let miss = 0;
    try {
      while (!disposed && !over && !closed) {
        const res = await api.watchEvents(matchId, seq);
        if (disposed || over || closed) break;
        if (res.ok) {
          miss = 0;
          if (applyEnvelope(res.data) === 'resync') await resync();
          continue;
        }
        if (CLOSED.has(res.error) && res.error !== 'not_found') { close(res.error); break; }
        if (res.error === api.ApiError.Stale || res.error === api.ApiError.NotFound) {
          if (await resync()) { miss = 0; continue; }
          if (closed) break;
        }
        const backoff = BACKOFF_MS[Math.min(miss, BACKOFF_MS.length - 1)];
        miss++;
        bus.emit('net', { state: 'retrying', error: res.error, attempt: miss, inMs: backoff });
        await sleep(backoff);
        if (disposed || over || closed) break;
        if (miss >= 2) await resync();
      }
    } finally { pumping = false; }
  }

  /* ------------------------------------------------------------ lifecycle */

  function record() {
    let moves = [];
    try { moves = rules.chess.history(); } catch { moves = []; }
    const s = clock.snapshot();
    return {
      moves, plies: moves.length,
      result: over ? { result: over.result, winner: over.winner === undefined ? null : over.winner, reason: over.reason || null } : null,
      clocks: { w: s.w, b: s.b, total: s.total }, me: null,
    };
  }

  function start() {
    if (initial) applyWatch(initial);
    else { pieces.setPosition(rules.position()); if (!sided) { sided = true; board.setSide('w', true); } }
    paint();
    if (!ticker && !disposed && !over) { ticker = setInterval(tick, TICK_MS); if (ticker.unref) ticker.unref(); }
    if (!autoStart) return;
    (async () => { await resync(); if (!disposed && !closed) pump(); })();
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    stopTicker();
    clock.stop();
  }

  return {
    rules,
    clock,
    start,
    update() {},
    tryMove: () => null,
    takeBack: () => null,
    canPick: () => false,
    legalTargets: () => [],
    resign() {},
    record,
    plies: () => rules.ply(),
    turn: () => rules.turn(),
    isOver: () => !!over,
    result: () => over,
    autoRemaining: () => 0,

    /** Nobody here owns a colour. */
    get seats() { return []; },
    /** Both sides are graded for the stands (game/iq.js reads `graded` off the local event). */
    get graded() { return SIDES.slice(); },
    isWatch: true,
    matchId,
    watchers: () => watchers,
    delayMs: () => delayMs,
    players: () => ({ w: players.w, b: players.b }),
    closed: () => closed,
    seq: () => seq,
    status: () => (over ? 'over' : closed ? 'closed' : 'live'),
    dispose,

    _applyWatch: applyWatch,
    _applyEnvelope: applyEnvelope,
    _resync: resync,
    _pump: pump,
    _tick: tick,
  };
}
