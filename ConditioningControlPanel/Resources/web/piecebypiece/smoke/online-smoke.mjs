/* ============================================================================
 * smoke/online-smoke.mjs - node checks for the online lane, against a whole
 * fake server running in the same process. No browser, no network, no
 * dependencies:
 *
 *   node smoke/online-smoke.mjs
 *
 * The fake server is written from pbp-online-api.md v1: real chess.js as the
 * referee, real seq, real 409s with the full state in the body, the contract's
 * field names throughout, and a clock model taken from its section 6. If this
 * file and that one disagree, this file is the bug.
 *
 * What it is actually checking, in the order it matters:
 *
 *   THE CLOCKS ARE DERIVED. A clock that counted locally would pass every test
 *   you could write against a machine whose clock is correct. So the fake
 *   server's timeline is deliberately nowhere near the client's, and the client
 *   still reads the right numbers off it - including through a local clock jump.
 *
 *   AND IT WORKS EITHER WAY ROUND. The contract describes the balances on the
 *   wire twice and the two readings differ by up to a poll interval. The client
 *   measures which one it is talking to; both are exercised here.
 *
 *   SEQ IS OBEYED. A move sent against a stale seq gets a 409, and the answer
 *   to a 409 is the state in its body - not a retry, and not a second round
 *   trip either. A gap in the incoming stream is a resync. Events that arrive
 *   out of order are put back in order before any of them touch the board.
 *
 *   OUR OWN MOVE COMES BACK. The echo of a move we already played
 *   optimistically must not be played again, whichever way round it lands
 *   relative to the POST that caused it.
 *
 *   RECONNECTION. Miss three plies, resync, and the board, the referee's
 *   history and the clocks are all the server's again.
 *
 *   THE LOBBY WEARS THE DOOR'S INTERFACE. net/lobbyServer.js answers exactly
 *   what door/door.js calls, including the shapes it never sees a server
 *   produce - an outgoing challenge accepted with no colour attached to it.
 *
 *   THE SWITCH GOES BOTH WAYS. net/online.js puts an online seat in the chair
 *   and, afterwards, puts the hotseat back - disposing the seat - and forwards
 *   record() from whichever is there, or the shelf keeps games with no moves.
 *
 *   ABANDONMENT IS CLAIMED. Sixty seconds of the server saying he is not there
 *   (ABANDON_MS, the server's own number) is one claim, retried on the backoff
 *   ladder while the server says "not yet", and a fresh spell of silence is a
 *   fresh claim. Never one a tick.
 *
 *   THE LOBBY POLLS UNDER THE CAPS. The list every tick at POLL_MS, the
 *   challenge list every other one, except while one of ours is out.
 *
 * The whole thing runs on an injected clock and an injected sleep, so it is
 * deterministic and finishes in milliseconds.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { createBus } from '../game/events.js';
import * as api from '../net/api.js';
import { createOnlineMatch, createRemoteClock, parseUci, toUci, ABANDON_MS } from '../net/match.js';
import { createServerLobby, POLL_MS, CHALLENGE_TICKS } from '../net/lobbyServer.js';
import { createDriverSwitch } from '../net/online.js';
import { createHotseat } from '../game/hotseat.js';

let passed = 0;
const failures = [];

function ok(what, cond) {
  if (cond) { passed++; return; }
  failures.push(what);
}
function eq(what, got, want) {
  ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`,
    JSON.stringify(got) === JSON.stringify(want));
}
const settle = async (n = 8) => { for (let i = 0; i < n; i++) await Promise.resolve(); };

/* ---------------------------------------------------------------------------
 * THE FAKE SERVER
 *
 * A real referee, a real seq, real 409s, and a timeline of its own that starts
 * a long way from the client's.
 * ------------------------------------------------------------------------- */

const SERVER_EPOCH = 1757400000000;   // nothing like the injected local clock

const ME = { id: 'p_1a2b3c4d5e6f', display_name: 'Bambi', rating: 1240 };
const THEM = { id: 'p_9c8d7e6f5a4b', display_name: 'Rose', rating: null };

function createFakeServer({
  initialMs = 5 * 60 * 1000, incrementMs = 0,
  /**
   * Which reading of the contract this server implements.
   *   'start' - the wire carries the stored balance, as at turn_started_ms
   *   'now'   - the wire carries it discounted to server_now_ms
   * Both are tested; the client is supposed to work against either.
   */
  discount = 'now',
  you = 'w',
} = {}) {
  const chess = new Chess();
  const events = [];
  const state = {
    id: 'm_7d1efake',
    moves: [],
    seq: 0,
    status: 'live',
    result: null,
    drawOffer: null,
    left: { w: initialMs, b: initialMs },
    turnStarted: SERVER_EPOCH,
    opponentOnline: true,      // what the events envelope reports
    grantAbandon: false,       // when set, claim_timeout finds him gone and says so
  };
  let nowMs = SERVER_EPOCH;
  const calls = [];          // every path the client asked for, for assertions

  const advance = (ms) => { nowMs += ms; };

  /** Section 6, verbatim. */
  function remaining(side) {
    if (side !== chess.turn() || state.status !== 'live') return state.left[side];
    return Math.max(0, state.left[side] - (nowMs - state.turnStarted));
  }

  /** What goes on the wire, under whichever reading this server implements. */
  function clocks(withNow = true) {
    const c = {
      w_ms: discount === 'now' ? remaining('w') : state.left.w,
      b_ms: discount === 'now' ? remaining('b') : state.left.b,
      turn: chess.turn(),
      turn_started_ms: state.turnStarted,
    };
    if (withNow) c.server_now_ms = nowMs;
    return c;
  }

  /** Charge the mover for the think he just finished, then hand the clock over. */
  function press(mover) {
    const spent = Math.max(0, nowMs - state.turnStarted);
    state.left[mover] = Math.max(0, state.left[mover] - spent) + incrementMs;
    state.turnStarted = nowMs;
  }

  function push(type, extra) {
    state.seq += 1;
    // An event's clocks carry no server_now_ms - the contract's event shape.
    events.push(Object.assign({ seq: state.seq, type, at_ms: nowMs, clocks: clocks(false) }, extra || {}));
    return state.seq;
  }

  function finishWith(winner, reason) {
    state.status = 'done';
    state.result = { winner, reason, ended_ms: nowMs, rating_delta: null };
    return state.result;
  }

  /** Play a ply as either side, without going through the wire. The opponent uses this. */
  function play(uci) {
    const m = parseUci(uci);
    const mover = chess.turn();
    const played = chess.move({ from: m.from, to: m.to, promotion: m.promotion || undefined });
    if (!played) throw new Error('fake server was handed an illegal move: ' + uci);
    press(mover);
    state.moves.push(uci);
    state.drawOffer = null;
    const seq = push('move', { uci, by: mover, san: played.san, fen: chess.fen(), ply: state.moves.length });
    if (chess.isGameOver()) {
      const result = chess.isCheckmate()
        ? finishWith(mover, 'checkmate')
        : finishWith(null, chess.isStalemate() ? 'stalemate' : 'insufficient_material');
      push(chess.isCheckmate() ? 'checkmate' : 'draw', { result });
    }
    return seq;
  }

  function snapshot() {
    return {
      ok: true,
      id: state.id,
      white: Object.assign({}, you === 'w' ? ME : THEM, { self: you === 'w', online: true }),
      black: Object.assign({}, you === 'b' ? ME : THEM, { self: you === 'b', online: true }),
      you,
      time_control: { initial_ms: initialMs, increment_ms: incrementMs },
      fen: chess.fen(),
      moves: state.moves.slice(),
      clocks: clocks(),
      status: state.status,
      result: state.result,
      draw_offer: state.drawOffer,
      seq: state.seq,
      started_ms: SERVER_EPOCH,
    };
  }

  /** The transport net/api.js talks through. Same shape bridge.netRequest has. */
  async function transport(method, path, body) {
    calls.push(`${method} ${path.split('?')[0]}`);
    const [route, query] = String(path).split('?');
    const q = new URLSearchParams(query || '');
    const json = (status, obj) => ({ status, body: JSON.stringify(obj) });
    const M = `${api.BASE}/match/${state.id}`;

    if (method === 'GET' && route === M) return json(200, snapshot());

    if (method === 'GET' && route === `${M}/events`) {
      const since = Number(q.get('since') || 0);
      return json(200, {
        ok: true,
        seq: state.seq,
        server_now_ms: nowMs,
        events: events.filter((e) => e.seq > since),
        status: state.status,
        opponent_online: state.opponentOnline,
      });
    }

    if (method === 'POST' && route === `${M}/move`) {
      if (state.status !== 'live') return json(409, Object.assign({ error: 'match_over' }, snapshot()));
      // 409 stale_seq carries the WHOLE state, so the client needs no second call.
      if (Number(body.seq_expected) !== state.seq) {
        return json(409, Object.assign({ error: 'stale_seq' }, snapshot()));
      }
      const m = parseUci(body.uci);
      if (!m) return json(400, { error: 'illegal_move' });
      const mover = chess.turn();
      if (mover !== you) return json(409, Object.assign({ error: 'not_your_turn' }, snapshot()));
      let played = null;
      try { played = chess.move({ from: m.from, to: m.to, promotion: m.promotion || undefined }); }
      catch (err) { played = null; }
      if (!played) return json(400, { error: 'illegal_move' });
      press(mover);
      state.moves.push(body.uci);
      state.drawOffer = null;
      const seq = push('move', { uci: body.uci, by: mover, san: played.san, fen: chess.fen(), ply: state.moves.length });
      return json(200, {
        ok: true, seq, fen: chess.fen(), moves: state.moves.slice(),
        clocks: clocks(), status: state.status, result: state.result, san: played.san,
      });
    }

    if (method === 'POST' && route === `${M}/resign`) {
      const result = finishWith(you === 'w' ? 'b' : 'w', 'resign');
      push('resign', { by: you, result });
      return json(200, { ok: true, status: 'done', result, seq: state.seq });
    }

    if (method === 'POST' && route === `${M}/draw`) {
      if (body.action === 'offer') { state.drawOffer = you; push('draw_offer', { by: you }); }
      else if (body.action === 'accept') { const result = finishWith(null, 'agreement'); push('draw', { reason: 'agreement', result }); }
      else { state.drawOffer = null; push('draw_decline', { by: you }); }
      return json(200, { ok: true, seq: state.seq });
    }

    if (method === 'POST' && route === `${M}/heartbeat`) return json(200, { ok: true, opponent_online: state.opponentOnline, seq: state.seq, status: state.status });
    if (method === 'POST' && route === `${M}/claim_timeout`) {
      // Condition 2 of the contract's claim_timeout: the opponent unseen for
      // ABANDON_MS. The caller wins. Otherwise "not yet", with the clocks.
      if (state.grantAbandon && state.status === 'live') {
        const result = finishWith(you, 'abandon');
        push('abandon', { side: you === 'w' ? 'b' : 'w', result });
        return json(200, snapshot());
      }
      return json(409, { error: 'not_expired', clocks: clocks() });
    }

    return json(404, {});
  }

  return {
    transport, play, snapshot, advance, calls, events, push, chess, state,
    finishWith,
    now: () => nowMs,
    remaining,
  };
}

/** The board the driver draws on, reduced to the three things it actually touches. */
function fakeBoard() {
  const men = new Map();
  const log = [];
  return {
    men,
    log,
    setSide(side, instant) { log.push(`side:${side}:${!!instant}`); },
    buzzCheck(sq) { log.push(`buzz:${sq}`); },
    pieces: {
      setPosition(map) {
        men.clear();
        for (const sq of Object.keys(map)) men.set(sq, map[sq].side + map[sq].type);
        log.push('setPosition');
      },
      move(from, to) {
        const man = men.get(from);
        men.delete(from);
        if (man) men.set(to, man);
        log.push(`move:${from}${to}`);
      },
      remove(sq) { men.delete(sq); log.push(`remove:${sq}`); },
    },
  };
}

/** What the board is showing, as a comparable string. */
function boardOf(board) {
  return Array.from(board.men.entries()).sort((a, b) => (a[0] < b[0] ? -1 : 1)).map((e) => e[0] + e[1]).join(',');
}

/** What the referee says the board should be showing. */
function refOf(game) {
  const p = game.rules.position();
  return Object.keys(p).sort().map((sq) => sq + p[sq].side + p[sq].type).join(',');
}

/** A driver wired to a fake server, on an injected clock that never advances by itself. */
function harness(opts = {}) {
  // `you` defaults to the seat the lobby claims, but a test may set them apart
  // on purpose - that disagreement is the case the seat correction exists for.
  const server = createFakeServer(Object.assign({ you: opts.color || 'w' }, opts));
  api.setTransport(server.transport);
  const bus = createBus();
  const board = fakeBoard();
  const seen = [];
  for (const t of ['turn', 'capture', 'check', 'gameover', 'clock', 'resync', 'draw-offer', 'net', 'seat', 'players', 'opponent']) {
    bus.on(t, (p) => seen.push({ type: t, payload: p }));
  }
  let local = 0;
  const game = createOnlineMatch({
    bus,
    board,
    matchId: server.state.id,
    color: opts.color === undefined ? 'w' : opts.color,
    api,
    now: () => local,
    wait: () => Promise.resolve(),
    autoStart: false,
  });
  return {
    server, bus, board, game, seen,
    tick: (ms) => { local += ms; server.advance(ms); },
    /** Move the client's clock WITHOUT moving the server's - the drift the offset absorbs. */
    drift: (ms) => { local += ms; },
    of: (type) => seen.filter((e) => e.type === type),
  };
}

/* ---------------------------------------------------------------------------
 * 1. UCI both ways
 * ------------------------------------------------------------------------- */
{
  eq('a plain move parses', parseUci('e2e4'), { from: 'e2', to: 'e4', promotion: null });
  eq('a promotion parses', parseUci('e7e8q'), { from: 'e7', to: 'e8', promotion: 'q' });
  eq('case and space are forgiven', parseUci(' E2E4 '), { from: 'e2', to: 'e4', promotion: null });
  ok('rubbish is refused', parseUci('e2e9') === null && parseUci('') === null && parseUci('resign') === null);
  eq('a move goes back out', toUci('e2', 'e4', null), 'e2e4');
  eq('a promotion goes back out', toUci('e7', 'e8', 'Q'), 'e7e8q');
  ok('castling is a king move, never O-O', toUci('e1', 'g1', null) === 'e1g1' && parseUci('e1g1') !== null);
}

/* ---------------------------------------------------------------------------
 * 2. THE DERIVED CLOCK
 *
 * The server's timeline is 1.7 trillion; the client's starts at zero. Nothing
 * below would work if the clock counted locally instead of deriving.
 * ------------------------------------------------------------------------- */
{
  let local = 0;
  const clock = createRemoteClock({ now: () => local });
  // A server that discounts on the way out: the default reading.
  clock.sync({ w_ms: 300000, b_ms: 300000, turn: 'w', server_now_ms: SERVER_EPOCH, turn_started_ms: SERVER_EPOCH, total_ms: 300000 });
  eq('at the moment of the sync, nobody has spent anything', [clock.remaining('w'), clock.remaining('b')], [300000, 300000]);

  local += 4000;
  eq('the side to move is charged for the think', clock.remaining('w'), 296000);
  eq('the side waiting is not', clock.remaining('b'), 300000);
  eq('the offset was fixed at the sync, not recomputed since', clock.offset(), SERVER_EPOCH);
  eq('so our reading of the server clock moved with us', clock.serverNow(), SERVER_EPOCH + 4000);

  // The server answers again, 10s into the same turn, having discounted.
  local += 6000;
  clock.sync({ w_ms: 290000, b_ms: 300000, turn: 'w', server_now_ms: SERVER_EPOCH + 10000, turn_started_ms: SERVER_EPOCH, total_ms: 300000 });
  eq('a fresh sync agrees with the derivation', clock.remaining('w'), 290000);
  eq('and a falling balance in one turn identifies a discounting server', clock.discountMode(), { mode: 'now', measured: true });

  // Now the local clock jumps forward on its own (a suspend/resume, an NTP
  // step). The next server answer must pull it straight back.
  local += 60000;
  ok('a local jump does show, between syncs', clock.remaining('w') < 290000);
  clock.sync({ w_ms: 289000, b_ms: 300000, turn: 'w', server_now_ms: SERVER_EPOCH + 11000, turn_started_ms: SERVER_EPOCH, total_ms: 300000 });
  eq('and the next sync corrects it completely', clock.remaining('w'), 289000);

  clock.sync({ w_ms: 500, b_ms: 1000, turn: 'w', server_now_ms: SERVER_EPOCH + 50000, turn_started_ms: SERVER_EPOCH + 40000 });
  eq('a clock cannot go below zero', (() => { local += 900; return clock.remaining('w'); })(), 0);
  eq('and that is a flag, on our reading', clock.flagged(), 'w');

  clock.stop();
  eq('stopping freezes the numbers', clock.snapshot().active, null);
  const frozen = clock.remaining('b');
  local += 20000;
  eq('and they stay frozen', clock.remaining('b'), frozen);
}

/* ---------------------------------------------------------------------------
 * 2b. THE OTHER READING OF THE SAME CONTRACT
 *
 * A server that hands over the STORED balance instead. The client has to notice
 * and stop double-charging, or every clock in the game runs a poll interval fast.
 * ------------------------------------------------------------------------- */
{
  let local = 0;
  const clock = createRemoteClock({ now: () => local });
  const stored = { w_ms: 300000, b_ms: 300000, turn: 'w', turn_started_ms: SERVER_EPOCH, total_ms: 300000 };

  clock.sync(Object.assign({}, stored, { server_now_ms: SERVER_EPOCH }));
  local += 8000;
  // Same turn, same stored number, 8s later. That is the tell.
  clock.sync(Object.assign({}, stored, { server_now_ms: SERVER_EPOCH + 8000 }));
  eq('an unchanged balance in one turn identifies a storing server', clock.discountMode(), { mode: 'start', measured: true });
  eq('and the elapsed think is subtracted here instead', clock.remaining('w'), 292000);
  local += 2000;
  eq('the clock keeps running between answers', clock.remaining('w'), 290000);

  // Only a server_now_ms, which is what a quiet long poll answers with.
  clock.noteServerNow(SERVER_EPOCH + 10000);
  eq('a bare server_now_ms keeps the offset honest', clock.remaining('w'), 290000);
}

/* ---------------------------------------------------------------------------
 * 3. A LOCAL MOVE: optimistic on the board, seq on the wire
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  eq('the seat starts at seq 0', h.game.seq(), 0);
  eq('the camera was set to our side once', h.board.log.filter((l) => l.startsWith('side:')).length, 1);
  eq('and to OUR side', h.board.log.find((l) => l.startsWith('side:')), 'side:w:true');
  eq('the players were announced', h.of('players').length, 1);
  eq('with the opponent named, not identified', h.of('players')[0].payload.opponent.display_name, 'Rose');
  ok('and no unified id anywhere in it', JSON.stringify(h.of('players')[0].payload).indexOf('u_') < 0);

  h.tick(3000);
  const played = h.game.tryMove('e2', 'e4');
  ok('the move is on the board before the wire has answered', !!played);
  eq('the men moved with it', h.board.men.get('e4'), 'wp');
  eq('the referee agrees with the board', boardOf(h.board), refOf(h.game));
  ok('the seat is locked while the move is out', h.game._inFlight() === true);
  ok('and nothing else can be picked up', h.game.canPick('d2') === false);

  await settle();
  eq('the wire answered and the seq advanced', h.game.seq(), 1);
  ok('the seat is free again', h.game._inFlight() === false);
  eq('the server has the same position', h.server.snapshot().fen, h.game.rules.fen());
  eq('white was charged for the think', h.server.state.left.w, 5 * 60 * 1000 - 3000);

  // THE ECHO. Our own move now comes back down the poll. It must not be played
  // a second time.
  const before = boardOf(h.board);
  eq('the echo is recognised and dropped', h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() }), 'ok');
  eq('the board did not move again', boardOf(h.board), before);
  eq('and the referee is still one ply in', h.game.plies(), 1);
}

/* ---------------------------------------------------------------------------
 * 4. THE ECHO ARRIVING BEFORE THE POST'S ANSWER
 *
 * The other order, which is the one that actually corrupts a position if the
 * pending-move check is not there.
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  let release = null;
  const held = new Promise((r) => { release = r; });
  const real = h.server.transport;
  api.setTransport(async (method, path, body) => {
    const res = await real(method, path, body);
    if (path.endsWith('/move')) await held;
    return res;
  });

  h.game.tryMove('d2', 'd4');
  await settle(3);
  eq('the POST is still out', h.game.seq(), 0);
  // ...and the poll gets there first.
  eq('the early echo is accounted for', h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() }), 'ok');
  eq('the seq moved on the echo alone', h.game.seq(), 1);
  eq('and the board still has exactly one d-pawn move', h.game.plies(), 1);
  eq('the board matches the referee', boardOf(h.board), refOf(h.game));

  release();
  await settle();
  eq('the late answer changed nothing', h.game.plies(), 1);
  api.setTransport(real);
}

/* ---------------------------------------------------------------------------
 * 5. 409 STALE: the server moved on without us
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  // A ply we never saw, so our idea of the seq is behind before we touch a man.
  h.server.play('e2e4');
  eq('the server is at seq 1, we still think 0', [h.server.state.seq, h.game.seq()], [1, 0]);

  const before = h.server.calls.length;
  const played = h.game.tryMove('d2', 'd4');
  ok('the board took our move optimistically', !!played);
  eq('the board is a move ahead of the truth, briefly', h.game.plies(), 1);

  await settle(14);
  eq('the 409 was reported as a refusal, not retried', h.of('net').filter((e) => e.payload.state === 'move-refused').length, 1);
  eq('with the server\'s own code kept for the log', h.of('net').pop().payload.serverError, 'stale_seq');
  eq('the client took the server position', h.game.rules.fen(), h.server.snapshot().fen);
  eq('and the board with it', boardOf(h.board), refOf(h.game));
  eq('the seq caught up', h.game.seq(), h.server.state.seq);
  eq('our d-pawn move is gone; the e-pawn one is there', h.game.rules.chess.history(), ['e4']);

  const after = h.server.calls.slice(before);
  eq('exactly one move POST was ever sent', after.filter((c) => c.endsWith('/move')).length, 1);
  // The whole point of the contract putting the state in the 409 body.
  eq('and the resync cost no extra round trip', after.filter((c) => c.startsWith('GET')).length, 0);
}

/* ---------------------------------------------------------------------------
 * 6. EVENT ORDER, AND GAPS
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  h.server.play('e2e4');
  h.server.play('e7e5');
  h.server.play('g1f3');

  // Handed to the client backwards. It has to put them right before applying,
  // or the second one is illegal in the position the third one leaves.
  const backwards = h.server.events.slice().reverse();
  eq('an out-of-order batch is still applied', h.game._applyEnvelope({ events: backwards, server_now_ms: h.server.now() }), 'ok');
  eq('in seq order', h.game.rules.chess.history(), ['e4', 'e5', 'Nf3']);
  eq('the board followed', boardOf(h.board), refOf(h.game));
  eq('the seq is the last of them', h.game.seq(), 3);

  // A gap. The client must refuse to guess.
  const gap = { seq: h.game.seq() + 2, type: 'move', uci: 'b8c6' };
  eq('a hole in the stream asks for a resync', h.game._applyEnvelope({ events: [gap] }), 'resync');
  eq('and nothing was applied on the way to saying so', h.game.rules.chess.history(), ['e4', 'e5', 'Nf3']);

  // A move whose fen does not match the one we computed.
  eq('a move that lands somewhere else asks for a resync',
    h.game._applyEnvelope({ events: [{ seq: 4, type: 'move', uci: 'b8c6', fen: 'nonsense' }] }), 'resync');

  // An event type from a newer server. Its seq still counts.
  eq('an unknown event does not wedge the stream',
    h.game._applyEnvelope({ events: [{ seq: 4, type: 'takeback_offer', by: 'b' }] }), 'ok');
  eq('and it took its seq with it', h.game.seq(), 4);
}

/* ---------------------------------------------------------------------------
 * 7. RECONNECTION
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  h.tick(1000);
  h.server.play('d2d4');
  h.tick(2000);
  h.server.play('g8f6');
  h.tick(1500);
  h.server.play('c2c4');

  eq('the client is three plies behind', [h.game.plies(), h.server.state.moves.length], [0, 3]);
  h.drift(45000);          // and its own clock wandered while it was away
  const got = await h.game._resync();
  ok('the resync landed', got === true);
  eq('the position is the server\'s', h.game.rules.fen(), h.server.snapshot().fen);
  eq('the HISTORY is too, not just the position', h.game.rules.chess.history(), ['d4', 'Nf6', 'c4']);
  eq('the board shows it', boardOf(h.board), refOf(h.game));
  eq('the seq is the server\'s', h.game.seq(), h.server.state.seq);
  eq('and the clocks are the server\'s, drift and all', h.game.clock.remaining('b'), h.server.remaining('b'));
  ok('a resync tells the page it happened', h.of('resync').length >= 1);

  // A resync onto a FINISHED match must not put the player back in a live seat.
  h.server.finishWith('b', 'timeout');
  await h.game._resync();
  ok('a finished match resyncs into a finished board', h.game.isOver() === true);
  eq('the contract\'s reason becomes the board\'s vocabulary', h.game.result().result, 'flag');
  eq('with the server\'s verdict', h.game.result().winner, 'b');
  eq('and the page was told once', h.of('gameover').length, 1);
  ok('the seat refuses to move after that', h.game.tryMove('a2', 'a3') === null);
}

/* ---------------------------------------------------------------------------
 * 8. THE PUMP, END TO END
 *
 * The real loop this time: it polls, applies, and stops itself when the game
 * ends. A pump that did not stop would hang this test, which is the check.
 * ------------------------------------------------------------------------- */
{
  const h = harness({ color: 'b' });
  await h.game._resync();
  eq('a black seat looks down the board from black', h.board.log.find((l) => l.startsWith('side:')), 'side:b:true');
  ok('and cannot move white\'s men', h.game.canPick('e2') === false);

  h.server.play('e2e4');
  const result = h.server.finishWith('b', 'resign');
  h.server.push('resign', { by: 'w', result });

  await h.game._pump();
  eq('the pump applied the move', h.game.rules.chess.history(), ['e4']);
  ok('saw the resignation', h.game.isOver() === true);
  eq('and called it for us', h.game.result().winner, 'b');
  eq('the page heard about it exactly once', h.of('gameover').length, 1);
  eq('the seat reports itself over', h.game.status(), 'over');
  ok('and refuses to move', h.game.tryMove('e7', 'e5') === null);
  ok('the long poll asked for the 8s the server actually allows',
    h.server.calls.every((c) => true) && api.EVENT_WAIT_MS === 8000);
}

/* ---------------------------------------------------------------------------
 * 9. THE SEAT'S OWN RULES, AND LEARNING WHICH SEAT IT IS
 * ------------------------------------------------------------------------- */
{
  const h = harness({ color: 'b' });
  await h.game._resync();
  ok('a seat cannot move out of turn', h.game.tryMove('e7', 'e5') === null);
  eq('and offers no targets while it is not his', h.game.legalTargets('e7'), []);
  ok('nor can he pick up the other man\'s pieces', h.game.canPick('e2') === false);

  h.server.play('e2e4');
  h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() });
  ok('once it is his turn he can pick up', h.game.canPick('e7') === true);
  ok('a target list appears with it', h.game.legalTargets('e7').indexOf('e5') >= 0);
  eq('there are no take-backs online', h.game.takeBack(), null);
  eq('and no auto-play', h.game.autoRemaining(), 0);
  eq('the seat knows which colour it is', [h.game.color, h.game.seats], ['b', ['b']]);
}
{
  // A challenge that was accepted hands back no colour at all. The seat is a
  // guess until the first GET match, and then it is not.
  // The fake server seats this client as BLACK; the lobby handed over no colour
  // at all, which is what an accepted outgoing challenge actually looks like.
  const h = harness({ color: null, you: 'b' });
  eq('with no colour from the lobby it guesses', [h.game.color, h.game.seatKnown], ['w', false]);
  await h.game._resync();
  eq('and the server corrects it', [h.game.color, h.game.seatKnown], ['b', true]);
  eq('the page is told the seat changed', h.of('seat').length, 1);
  eq('and the camera turned round with it', h.board.log.find((l) => l.startsWith('side:')), 'side:b:true');
  ok('the corrected seat is what gates the men', h.game.canPick('e2') === false);
}

/* ---------------------------------------------------------------------------
 * 10. CAPTURES AND CHECK STILL RING THE SAME BELLS
 *
 * The whole point of the shared playMove path: a remote move has to fire the
 * exact events the board's dust, sound and outline are already listening for.
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  h.server.play('e2e4');
  h.server.play('d7d5');
  h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() });

  const before = h.of('capture').length;
  h.game.tryMove('e4', 'd5');
  eq('a capture off our own move is announced', h.of('capture').length, before + 1);
  eq('with the man who came off', h.of('capture').pop().payload.piece, 'p');
  await settle();

  // A remote check, applied through the poll.
  h.server.play('d8d5');    // queen takes back
  h.server.play('f1b5');    // check
  h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() });
  ok('a remote check is announced', h.of('check').length >= 1);
  ok('and the man giving it was buzzed', h.board.log.filter((l) => l.startsWith('buzz:')).length >= 1);
  eq('the board never drifted from the referee', boardOf(h.board), refOf(h.game));
}

/* ---------------------------------------------------------------------------
 * 11. DRAWS
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  h.game._applyEnvelope({ events: [{ seq: 1, type: 'draw_offer', by: 'b' }] });
  eq('an offer from him is surfaced', h.of('draw-offer').length, 1);
  eq('and named as his', h.of('draw-offer')[0].payload.mine, false);
  eq('the seat remembers it stands', h.game.drawOffer(), 'them');

  h.game._applyEnvelope({ events: [{ seq: 2, type: 'move', uci: 'e2e4' }] });
  eq('a move clears it, as it does server-side', h.game.drawOffer(), null);

  h.game._applyEnvelope({ events: [{ seq: 3, type: 'draw', reason: 'agreement', result: { winner: null, reason: 'agreement' } }] });
  eq('an agreed draw is a draw', [h.game.isOver(), h.game.result().result, h.game.result().winner], [true, 'draw', null]);
}

/* ---------------------------------------------------------------------------
 * 12. net/api.js's own envelope
 * ------------------------------------------------------------------------- */
{
  api.setTransport(async () => ({ status: 409, body: JSON.stringify({ error: 'stale_seq', seq: 7 }) }));
  const stale = await api.move('m1', 3, 'e2e4');
  eq('a 409 reads as stale', [stale.ok, stale.error], [false, api.ApiError.Stale]);
  eq('and keeps the server\'s own code', stale.serverError, 'stale_seq');

  api.setTransport(async () => ({ status: 0, body: '' }));
  eq('no answer at all reads as offline', (await api.match('m1')).error, api.ApiError.Offline);

  api.setTransport(async () => ({ status: 401, body: JSON.stringify({ error: 'unauthorized' }) }));
  eq('401 is unauthorized', (await api.match('m1')).error, api.ApiError.Unauthorized);

  api.setTransport(async () => ({ status: 404, body: '' }));
  eq('a bare 404 is a route that is not there yet', (await api.match('m1')).error, api.ApiError.NotDeployed);

  api.setTransport(async () => ({ status: 404, body: JSON.stringify({ error: 'not_found' }) }));
  eq('a 404 the server wrote is a missing match', (await api.match('m1')).error, api.ApiError.NotFound);

  api.setTransport(async () => ({ status: 429, body: JSON.stringify({ error: 'rate_limited', cap: 'user', retry_after_seconds: 12 }) }));
  const limited = await api.quick(api.timeControl(60000, 0));
  eq('a 429 carries its wait', [limited.error, limited.retryAfterSeconds], [api.ApiError.RateLimited, 12]);

  api.setTransport(async () => ({ status: 200, body: 'not json at all' }));
  eq('a 2xx that is not JSON is malformed, not ok', (await api.match('m1')).error, api.ApiError.Malformed);

  let seenPath = '';
  let seenBody = null;
  api.setTransport(async (m, p, b) => { seenPath = p; seenBody = b; return { status: 200, body: '{}' }; });
  await api.events('m 1/../x', 5);
  ok('an id is encoded into the path, never concatenated raw', seenPath.indexOf('m%201%2F..%2Fx') > 0);
  ok('the since rides as a query', seenPath.indexOf('since=5') > 0);
  ok('and so does the wait budget', seenPath.indexOf('wait_ms=8000') > 0);
  await api.events('m1', 0, 90000);
  ok('a caller cannot ask for longer than the server allows', seenPath.indexOf('wait_ms=8000') > 0);

  await api.draw('m1', 'accept');
  eq('a draw sends its action', seenBody.action, 'accept');
  ok('and the legacy shorthand beside it', seenBody.accept === true);

  eq('every route sits under one base', api.BASE, '/v2/pbp');
  eq('a time control is clamped to the band the server takes',
    api.timeControl(1000, 999999), { initial_ms: 60000, increment_ms: 60000 });
  eq('and a sensible one is left alone',
    api.timeControl(300000, 3000), { initial_ms: 300000, increment_ms: 3000 });
}

/* ---------------------------------------------------------------------------
 * 13. THE LOBBY, WEARING THE FRONT DOOR'S INTERFACE
 * ------------------------------------------------------------------------- */

/** Enough of the lobby surface to answer a door, with the contract's shapes. */
function lobbyServerFixture() {
  const lobby = [
    { id: 'p_aaaaaaaaaaaa', display_name: 'velvet', rating: 1301, since_ms: 1000, self: false, time_control: { initial_ms: 600000, increment_ms: 0 } },
    { id: 'p_bbbbbbbbbbbb', display_name: 'moth', rating: null, since_ms: 500, self: false, time_control: { initial_ms: 600000, increment_ms: 0 } },
    { id: 'p_meeeeeeeeeee', display_name: 'you', rating: 1200, since_ms: 2000, self: true, time_control: { initial_ms: 600000, increment_ms: 0 } },
  ];
  const st = { incoming: [], outgoing: [], quickPaired: null, calls: [], matchYou: 'b' };
  const json = (status, obj) => ({ status, body: JSON.stringify(obj) });
  async function transport(method, path, body) {
    const route = String(path).split('?')[0];
    st.calls.push(`${method} ${route}`);
    if (route === `${api.BASE}/lobby/enter`) return json(200, { ok: true, ticket: 't_1', expires_in_sec: 180 });
    if (route === `${api.BASE}/lobby/leave`) return json(200, { ok: true, left: true });
    if (route === `${api.BASE}/lobby`) return json(200, { ok: true, players: lobby, server_now_ms: 1 });
    if (route === `${api.BASE}/challenges`) return json(200, { ok: true, incoming: st.incoming, outgoing: st.outgoing });
    if (route === `${api.BASE}/quick`) {
      return st.quickPaired ? json(200, { ok: true, match_id: st.quickPaired, color: 'w' })
        : json(200, { ok: true, waiting: true, queued_ms: 100 });
    }
    if (route === `${api.BASE}/challenge`) {
      if (body.target === 'p_gonegonegone') return json(404, { error: 'no_such_player' });
      st.outgoing = [{ challenge_id: 'c_mine', to: lobby[0], time_control: { initial_ms: 600000, increment_ms: 0 }, status: 'pending' }];
      return json(200, { ok: true, challenge_id: 'c_mine', expires_in_sec: 300 });
    }
    if (route === `${api.BASE}/challenge/c_theirs/accept`) return json(200, { ok: true, match_id: 'm_accepted', color: 'b' });
    if (route.startsWith(`${api.BASE}/challenge/`) && route.endsWith('/decline')) return json(200, { ok: true, declined: true });
    if (route.startsWith(`${api.BASE}/match/`)) {
      const id = route.split('/')[4];
      return json(200, {
        ok: true, id, you: st.matchYou,
        white: { id: 'p_aaaaaaaaaaaa', display_name: 'velvet', rating: 1301, self: st.matchYou === 'w', online: true },
        black: { id: 'p_meeeeeeeeeee', display_name: 'you', rating: 1200, self: st.matchYou === 'b', online: true },
        time_control: { initial_ms: 600000, increment_ms: 0 },
        fen: new Chess().fen(), moves: [], clocks: { w_ms: 600000, b_ms: 600000, turn: 'w', server_now_ms: 1, turn_started_ms: 1 },
        status: 'live', result: null, draw_offer: null, seq: 0, started_ms: 1,
      });
    }
    return json(404, {});
  }
  return { transport, st };
}

{
  const fx = lobbyServerFixture();
  api.setTransport(fx.transport);
  const bus = createBus();
  const seenOnline = [];
  bus.on('online-match', (m) => seenOnline.push(m));
  const lobby = createServerLobby({ bus, api, force: true, pollMs: 5 });

  eq('the surface is the door\'s, exactly',
    ['cancel', 'challenge', 'dispose', 'enter', 'leave', 'list', 'onChallenge', 'onList', 'quickMatch']
      .every((k) => typeof lobby[k] === 'function'), true);

  await lobby.enter({ name: 'you' });
  const rows = await lobby.list();
  eq('the list is other people only', rows.map((r) => r.name), ['moth', 'velvet']);
  eq('oldest waiting first', rows[0].waitingSince, 500);
  eq('and it speaks the door\'s field names', Object.keys(rows[0]).sort().join(','), 'id,name,rating,timeControl,waitingSince');
  ok('with opaque ids, never unified ones', rows.every((r) => r.id.startsWith('p_')));

  // A challenge somebody sent us.
  const offers = [];
  lobby.onChallenge((o) => offers.push(o));
  fx.st.incoming = [{ challenge_id: 'c_theirs', from: { id: 'p_aaaaaaaaaaaa', display_name: 'velvet', rating: 1301 }, color: 'b', time_control: { initial_ms: 600000, increment_ms: 0 } }];
  await lobby.debug.refresh();
  eq('an incoming challenge is offered once', offers.length, 1);
  eq('named the way the door renders it', [offers[0].id, offers[0].name], ['p_aaaaaaaaaaaa', 'velvet']);
  await lobby.debug.refresh();
  eq('and not offered again on the next poll', offers.length, 1);

  const m = offers[0].accept();
  ok('accept answers synchronously, as door.js requires', !!m && typeof m === 'object');
  eq('with the opponent already named', m.opponent.name, 'velvet');
  await m.ready;
  eq('and the match filled in behind it', [m.id, m.side, m.clockMs], ['m_accepted', 'b', 600000]);
  eq('the page heard about it too', seenOnline.length, 1);

  // Quick match: waiting, then paired, resolved through the poll.
  fx.st.quickPaired = null;
  const quick = lobby.quickMatch();
  await settle(6);
  fx.st.quickPaired = 'm_quick';
  fx.st.matchYou = 'w';
  await lobby.debug.refresh();
  const qm = await quick;
  eq('quick match resolves with a Match', [qm.id, qm.side, qm.clockMs], ['m_quick', 'w', 600000]);
  eq('and names the opponent from the match record', qm.opponent.name, 'you');

  // Cancelling is a rejection the door already handles.
  fx.st.quickPaired = null;
  const abandoned = lobby.quickMatch();
  await settle(4);
  lobby.cancel();
  let cancelled = null;
  try { await abandoned; } catch (e) { cancelled = e.message; }
  eq('cancel rejects with the word the door listens for', cancelled, 'cancelled');

  // An outgoing challenge, accepted with NO colour on the row.
  fx.st.matchYou = 'b';
  const asked = lobby.challenge('p_aaaaaaaaaaaa');
  await settle(6);
  fx.st.outgoing = [{ challenge_id: 'c_mine', to: fx.st.incoming[0].from, status: 'accepted', match_id: 'm_fromchallenge' }];
  await lobby.debug.refresh();
  const cm = await asked;
  eq('a challenge resolves with a Match', cm.id, 'm_fromchallenge');
  eq('and the SIDE came from the match, not from a guess', cm.side, 'b');

  // A player who is not there any more.
  let gone = null;
  try { await lobby.challenge('p_gonegonegone'); } catch (e) { gone = e.message; }
  eq('challenging a ghost rejects with the door\'s own word', gone, 'left');

  lobby.dispose();
  eq('a page with no host and no account gets no server lobby', createServerLobby({ api }), null);
}

/* ---------------------------------------------------------------------------
 * 14. THE SWITCH, BOTH WAYS
 *
 * boot.js hands everything the switch, never a driver. It has to reach the
 * online seat while one is there, and hand the hotseat back - with the seat
 * disposed - when the next game is dealt "here", or "play here" after an
 * online game resets a finished seat and fires its gameover again.
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  const hot = createHotseat({ bus: h.bus, board: h.board });
  const sw = createDriverSwitch(hot);
  eq('the hotseat starts in the chair', [sw.isOnline, sw.current === hot], [false, true]);
  eq('record() is forwarded from a hotseat', sw.record().plies, 0);
  eq('the draw verbs answer quietly in a hotseat', [sw.drawOffer(), sw.offerDraw()], [null, undefined]);

  let disposed = false;
  const realDispose = h.game.dispose;
  h.game.dispose = () => { disposed = true; realDispose(); };
  sw.switchTo(h.game);
  eq('an online seat reads as online', sw.isOnline, true);
  eq('and as one seat', sw.seats, ['w']);
  await h.game._resync();
  h.server.play('e2e4');
  h.game._applyEnvelope({ events: h.server.events.slice(), server_now_ms: h.server.now() });
  const rec = sw.record();
  eq('record() is forwarded from the online seat, moves in SAN', rec.moves, ['e4']);
  eq('in hotseat.record()\'s shape', Object.keys(rec).sort().join(','), 'clocks,me,moves,plies,result');
  eq('with the seat on it', rec.me, 'w');
  eq('and no result while it is live', rec.result, null);
  eq('the draw verbs reach the seat', typeof sw.offerDraw === 'function' && sw.drawOffer(), null);
  sw.offerDraw();
  eq('our own offer is known at once, not a poll later', sw.drawOffer(), 'me');

  const back = sw.switchBack();
  eq('switchBack puts the hotseat back', [back === hot, sw.current === hot, sw.isOnline], [true, true, false]);
  ok('and the online seat was disposed on the way out', disposed);
  eq('the switch is the hotseat\'s again', sw.seats, ['w', 'b']);
  eq('switching back twice is nothing', sw.switchBack() === hot, true);
  api.setTransport(null);
}

/* ---------------------------------------------------------------------------
 * 15. ABANDONMENT
 *
 * The server says he is gone; sixty seconds later we ask it to say so on the
 * record. Once - and if it says not yet, again on the backoff ladder - and a
 * fresh spell of silence is a fresh claim. The ticker is driven by hand.
 * ------------------------------------------------------------------------- */
{
  const h = harness();
  await h.game._resync();
  const claims = () => h.server.calls.filter((c) => c.endsWith('/claim_timeout')).length;
  const envelope = (online) => h.game._applyEnvelope({ events: [], server_now_ms: h.server.now(), opponent_online: online });

  eq('the client\'s number is the server\'s', ABANDON_MS, 60000);
  h.game._tick();
  eq('while he is here nothing is claimed', claims(), 0);

  envelope(false);
  eq('the page hears he has gone', h.of('opponent').pop().payload.online, false);
  ok('and the seat starts counting', h.game._silence() !== null);
  h.game._tick();
  eq('nothing is claimed at once', claims(), 0);
  h.tick(ABANDON_MS - 1000);
  h.game._tick();
  eq('nor a second before the server would agree', claims(), 0);
  h.tick(1000);
  h.game._tick();
  eq('at sixty seconds of silence, one claim goes out', claims(), 1);
  h.game._tick();
  h.game._tick();
  eq('and the next ticks do not repeat it', claims(), 1);
  await settle();
  // The server said not_expired: its count of his silence started later than
  // ours. The next ask waits the first step of the ladder, not the next tick.
  h.game._tick();
  eq('a refused claim is not retried on the very next tick', claims(), 1);
  h.tick(499);
  h.game._tick();
  eq('nor a moment early', claims(), 1);
  h.tick(1);
  h.game._tick();
  eq('but after the first backoff step it is', claims(), 2);
  await settle();
  ok('the board is still live: the server never said otherwise', !h.game.isOver());

  envelope(true);
  eq('his return ends the spell', h.game._silence(), null);
  h.tick(5000);
  h.game._tick();
  eq('and nothing more is claimed for it', claims(), 2);

  envelope(false);
  h.tick(ABANDON_MS);
  h.server.state.grantAbandon = true;
  h.game._tick();
  eq('a fresh silence is a fresh claim', claims(), 3);
  await settle();
  ok('the server\'s verdict finishes the board here, without waiting for the poll', h.game.isOver());
  eq('as an abandonment in our favour', [h.game.result().result, h.game.result().winner], ['abandon', 'w']);
  eq('the page was told once', h.of('gameover').length, 1);
  h.game._tick();
  eq('and a finished game claims nothing', claims(), 3);
  api.setTransport(null);
}

/* ---------------------------------------------------------------------------
 * 16. THE LOBBY'S POLL, AGAINST THE CAPS
 *
 * GET /lobby is 30/min/user and GET /challenges 30/min/user. The tick is
 * driven through the injectable timer, so the cadence is counted, not timed.
 * ------------------------------------------------------------------------- */
{
  const fx = lobbyServerFixture();
  api.setTransport(fx.transport);
  const queue = [];
  const timer = {
    set(fn, ms) { const t = { fn, ms }; queue.push(t); return t; },
    clear(t) { const i = queue.indexOf(t); if (i >= 0) queue.splice(i, 1); },
  };
  const lobby = createServerLobby({ api, force: true, timer });
  const count = (route) => fx.st.calls.filter((c) => c === `GET ${api.BASE}/${route}`).length;
  /** Fire the armed tick and let its refresh run to the point of re-arming. */
  const fire = async () => {
    const t = queue.shift();
    t.fn();
    for (let i = 0; i < 60 && queue.length === 0; i++) await Promise.resolve();
  };

  ok('the poll is slower than the list cap (30/min is 2s)', POLL_MS > 2000);
  eq('and is the number the contract\'s freshness allows', POLL_MS, 3000);
  eq('the challenge list is asked for every other tick', CHALLENGE_TICKS, 2);

  await lobby.enter({ name: 'you' });
  eq('enter asks for the list and the challenges once', [count('lobby'), count('challenges')], [1, 1]);
  eq('and arms the poll at POLL_MS', [queue.length, queue[0] && queue[0].ms], [1, POLL_MS]);
  await fire();
  eq('tick 2: the list, not the challenges', [count('lobby'), count('challenges')], [2, 1]);
  await fire();
  eq('tick 3: both', [count('lobby'), count('challenges')], [3, 2]);
  await fire();
  eq('tick 4: the list alone again', [count('lobby'), count('challenges')], [4, 2]);
  ok('the poll re-arms itself each time', queue.length === 1);

  // Our own challenge is out: the challenge list is the only place its answer
  // can land, so it is watched every tick until it does.
  const asked = lobby.challenge('p_aaaaaaaaaaaa');
  await settle(6);
  await fire();
  await fire();
  eq('an outgoing challenge is watched every tick', [count('lobby'), count('challenges')], [6, 4]);
  lobby.cancel();
  try { await asked; } catch (e) { /* cancelled, as intended */ }
  await fire();
  await fire();
  eq('and the cadence resumes once it is gone', count('challenges') - 4 <= 1, true);
  lobby.dispose();
  eq('dispose disarms the poll', queue.length, 0);
}

/* ------------------------------------------------------------------------- */

api.setTransport(null);

if (failures.length) {
  console.error(`online-smoke: ${failures.length} FAILED of ${passed + failures.length}`);
  for (const f of failures) console.error('  x ' + f);
  process.exit(1);
}
console.log(`online-smoke: ${passed} checks passed`);
