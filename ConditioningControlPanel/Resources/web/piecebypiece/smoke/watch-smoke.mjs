/* ============================================================================
 * smoke/watch-smoke.mjs - the stands (net/watch.js), in node, no browser:
 *
 *   node smoke/watch-smoke.mjs
 *
 * A fake delayed server stands in for GET /v2/pbp/watch/:id and its events
 * poll (the brief's wire): it plays a real game on its own timeline and hands
 * the stands only what is older than the delay. Checked here:
 *
 *   NOBODY IN THE STANDS MOVES. canPick, tryMove, legalTargets and the verbs
 *   all refuse; no seat, both sides graded.
 *   EVENTS LAND IN ORDER, A GAP IS A RESYNC, a disagreeing position is a resync.
 *   THE CLOCKS ARE DERIVED on the delayed timeline, never counted.
 *   A WORDED REFUSAL CLOSES THE STANDS (watch-closed), a 404 not_deployed too.
 *   THE WIRE: routes, the worded 403s and the 404 not_deployed, and
 *   watchable:false only when the player said no.
 *   THE LOBBY'S PLAYING ROWS carry match_id / watchable / watchers through.
 *   THE SWITCH: startWatchMatch puts the stands in the chair with no seat and
 *   both sides graded, and switchBack disposes them.
 * ==========================================================================*/

import { Chess } from '../vendor/chess.js';
import { createBus } from '../game/events.js';
import * as api from '../net/api.js';
import { createWatchSession, cutoffOf, watchLine, playerOf } from '../net/watch.js';
import { createDriverSwitch, startWatchMatch } from '../net/online.js';
import { createServerLobby } from '../net/lobbyServer.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const settle = async (n = 12) => { for (let i = 0; i < n; i++) await Promise.resolve(); };

function fakeBoard() {
  const men = new Map();
  const log = [];
  return {
    men, log,
    setSide(side, instant) { log.push(`side:${side}:${!!instant}`); },
    buzzCheck(sq) { log.push(`buzz:${sq}`); },
    pieces: {
      setPosition(map) { men.clear(); for (const sq of Object.keys(map)) men.set(sq, map[sq].side + map[sq].type); log.push('setPosition'); },
      move(from, to) { const m = men.get(from); men.delete(from); if (m) men.set(to, m); log.push(`move:${from}${to}`); },
      remove(sq) { men.delete(sq); log.push(`remove:${sq}`); },
    },
  };
}

const env = (status, data, error = null) => ({ ok: status >= 200 && status < 300, status, data, error });

/**
 * A live game on the server's own timeline, seen DELAY_MS late. Each move is
 * stamped when it was played; the stands see it once it is older than the delay.
 */
const SERVER_EPOCH = 1757400000000;
const DELAY_MS = 10000;
const INITIAL_MS = 300000;
function fakeServer() {
  const chess = new Chess();
  const log = [];   // { seq, at, uci, fen, clocks }
  const st = { now: SERVER_EPOCH, left: { w: INITIAL_MS, b: INITIAL_MS }, turnStarted: SERVER_EPOCH, watchers: 2, result: null, refuse: null };
  const calls = [];
  function play(uci, after = 2000) {
    st.now += after;
    const side = chess.turn();
    st.left[side] -= after;
    chess.move({ from: uci.slice(0, 2), to: uci.slice(2, 4), promotion: uci[4] || 'q' });
    st.turnStarted = st.now;
    log.push({ seq: log.length + 1, at: st.now, type: 'move', uci, fen: chess.fen(), clocks: { w_ms: st.left.w, b_ms: st.left.b, turn: chess.turn(), turn_started_ms: st.turnStarted } });
  }
  function resign(by, after = 1000) {
    st.now += after;
    log.push({ seq: log.length + 1, at: st.now, type: 'resign', by, result: { winner: by === 'w' ? 'b' : 'w', reason: 'resign' } });
  }
  /** Everything the stands may see right now. */
  function view() {
    const cut = st.now - DELAY_MS;
    const seen = log.filter((e) => e.at <= cut);
    const c = new Chess();
    for (const e of seen) if (e.type === 'move') c.move({ from: e.uci.slice(0, 2), to: e.uci.slice(2, 4), promotion: e.uci[4] || 'q' });
    const lastMove = [...seen].reverse().find((e) => e.type === 'move');
    const base = lastMove ? lastMove.clocks : { w_ms: INITIAL_MS, b_ms: INITIAL_MS, turn: 'w', turn_started_ms: SERVER_EPOCH };
    // the balances as they stood at the cutoff (discounted to it, like the seat route's 'now' reading)
    const mover = base.turn;
    const clocks = { ...base, [mover + '_ms']: Math.max(0, base[mover + '_ms'] - (cut - base.turn_started_ms)) };
    const ended = seen.find((e) => e.type === 'resign');
    return { seen, cut, match: {
      id: 'm_watch', status: ended ? 'done' : 'live', result: ended ? ended.result : null,
      white: { display_name: 'velvet', rating: 1301, self: false }, black: { display_name: 'moth', rating: null, self: false },
      moves: seen.filter((e) => e.type === 'move').map((e) => e.uci), fen: c.fen(), seq: seen.length, clocks,
    } };
  }
  const api2 = {
    ApiError: api.ApiError,
    calls,
    watch: async (id) => {
      calls.push('watch:' + id);
      if (st.refuse) return env(st.refuse.status, { error: st.refuse.error }, st.refuse.error);
      const v = view();
      return env(200, { ok: true, server_now_ms: st.now, delay_ms: DELAY_MS, watchers: st.watchers, match: v.match });
    },
    watchEvents: async (id, since) => {
      calls.push('events:' + since);
      if (st.refuse) return env(st.refuse.status, { error: st.refuse.error }, st.refuse.error);
      const v = view();
      const events = v.seen.filter((e) => e.seq > since).map(({ at, ...e }) => e);
      return env(200, { ok: true, server_now_ms: st.now, delay_ms: DELAY_MS, watchers: st.watchers, events });
    },
  };
  return { st, play, resign, view, api: api2, chess, log };
}

/* ------------------------------------------------------------------ pure bits */
{
  eq('the cutoff is now minus the delay', cutoffOf({ server_now_ms: 50000, delay_ms: 10000 }), 40000);
  eq('no server time, no cutoff', cutoffOf({}), null);
  eq('the delay defaults to ten seconds', cutoffOf({ server_now_ms: 50000 }), 40000);
  eq('a player off the wire', playerOf({ display_name: 'velvet', rating: 1301.4 }), { name: 'velvet', rating: 1301 });
  eq('a player with no rating', playerOf({ display_name: 'moth', rating: null }), { name: 'moth', rating: null });
  eq('the stands say who won, with no "you"', watchLine({ result: 'resign', winner: 'b' }), 'white resigned, black wins');
  eq('a flag', watchLine({ result: 'flag', winner: 'w' }), "black's flag fell, white wins");
  eq('a draw by repetition reads plainly', watchLine({ result: 'draw', reason: 'threefold' }), 'a draw by threefold');
}

/* --------------------------------------------------------------- the session */
{
  const srv = fakeServer();
  srv.play('e2e4'); srv.play('e7e5'); srv.play('g1f3');
  srv.st.now += DELAY_MS + 1;                 // all three are now older than the delay
  srv.play('b8c6', 500);                      // and this one is not
  const bus = createBus();
  const board = fakeBoard();
  let t = 0;
  const heard = { turn: 0, watchers: [], gameover: null, closed: null };
  bus.on('turn', () => heard.turn++);
  bus.on('watchers', (p) => heard.watchers.push(p.count));
  bus.on('gameover', (p) => { heard.gameover = p; });
  bus.on('watch-closed', (p) => { heard.closed = p; });
  const first = await srv.api.watch('m_watch');
  const w = createWatchSession({ bus, board, matchId: 'm_watch', initial: first.data, api: srv.api, now: () => t, autoStart: false });
  w.start();
  eq('the stands see only what is older than the delay', w.plies(), 3);
  eq('the seq is the delayed one', w.seq(), 3);
  eq('the board shows the delayed position', board.men.get('f3'), 'wn');
  eq('black to move, as of ten seconds ago', w.turn(), 'b');
  eq('nobody here owns a colour', w.seats, []);
  eq('both sides are graded', w.graded, ['w', 'b']);
  eq('no man can be picked up', ['e7', 'b8', 'e4', 'f3'].map((sq) => w.canPick(sq)), [false, false, false, false]);
  eq('a move is refused', w.tryMove('b8', 'c6'), null);
  eq('no targets', w.legalTargets('b8'), []);
  eq('and the move did not land', w.plies(), 3);
  eq('the watcher count arrived', heard.watchers, [2]);
  eq('the players arrive named and rated', w.players(), { w: { name: 'velvet', rating: 1301 }, b: { name: 'moth', rating: null } });
  ok('the camera was set once, from white', board.log.filter((l) => l.startsWith('side:')).length === 1);

  // THE CLOCKS. Black has been thinking since the cutoff's view of g1f3; the derived clock
  // reads the balance at the cutoff and runs on from there, on the delayed timeline.
  const v = srv.view();
  const wantB = v.match.clocks.b_ms;
  ok(`black's clock reads the delayed balance (got ${w.clock.remaining('b')}, wanted ${wantB})`, Math.abs(w.clock.remaining('b') - wantB) <= 1);
  t += 3000;
  ok(`and runs on from there (got ${w.clock.remaining('b')}, wanted ${wantB - 3000})`, Math.abs(w.clock.remaining('b') - (wantB - 3000)) <= 1);
  eq('white\'s clock stands still while black thinks', w.clock.remaining('w'), v.match.clocks.w_ms);

  // EVENTS IN ORDER. The next poll brings b8c6 once it is old enough.
  srv.st.now += DELAY_MS;
  const evs = await srv.api.watchEvents('m_watch', w.seq());
  eq('the poll only carries what is new and old enough', evs.data.events.map((e) => e.uci), ['b8c6']);
  eq('it applies', w._applyEnvelope(evs.data), 'ok');
  eq('b8c6 is on the board', board.men.get('c6'), 'bn');
  eq('seq moved on', w.seq(), 4);
  eq('a repeat of an old event is ignored', w._applyEnvelope({ events: [{ seq: 4, type: 'move', uci: 'b8c6' }] }), 'ok');
  eq('a gap is a resync', w._applyEnvelope({ events: [{ seq: 6, type: 'move', uci: 'f1c4' }] }), 'resync');
  eq('a move that will not play is a resync', w._applyEnvelope({ events: [{ seq: 5, type: 'move', uci: 'e1e8' }] }), 'resync');
  eq('a position that disagrees is a resync', w._applyEnvelope({ events: [{ seq: 5, type: 'move', uci: 'f1c4', fen: 'nonsense' }] }), 'resync');

  // RESYNC rebuilds from GET watch.
  srv.play('f1c4', 1000); srv.play('g8f6', 1000);
  srv.st.now += DELAY_MS;
  ok('a resync answers true', await w._resync());
  eq('and the board is the server\'s again', [w.plies(), w.seq(), board.men.get('f6')], [6, 6, 'bn']);

  // THE END. A resign, once it is older than the delay.
  srv.resign('w');
  srv.st.now += DELAY_MS;
  const end = await srv.api.watchEvents('m_watch', w.seq());
  w._applyEnvelope(end.data);
  ok('the game is over in the stands', w.isOver());
  eq('gameover carries the winner', heard.gameover, { result: 'resign', winner: 'b' });
  eq('the record has no seat', w.record().me, null);
  w.dispose();
}

/* ------------------------------------------------------------ the poll loop */
{
  const srv = fakeServer();
  srv.play('d2d4'); srv.st.now += DELAY_MS;
  const bus = createBus();
  const board = fakeBoard();
  let closed = null;
  bus.on('watch-closed', (p) => { closed = p; });
  const w = createWatchSession({ bus, board, matchId: 'm_watch', api: srv.api, now: () => 0, wait: () => Promise.resolve() });
  // the poll answers once, then the game stops being watchable
  let polls = 0;
  const base = srv.api.watchEvents;
  srv.api.watchEvents = async (id, since) => {
    polls++;
    if (polls === 1) { srv.play('d7d5'); srv.st.now += DELAY_MS; return base(id, since); }
    srv.st.refuse = { status: 403, error: 'not_watchable' };
    return base(id, since);
  };
  w.start();
  await settle(40);
  eq('start resyncs off GET watch first', srv.api.calls[0], 'watch:m_watch');
  eq('the poll brought the next move', board.men.get('d5'), 'bp');
  eq('a worded refusal closes the stands', closed, { matchId: 'm_watch', reason: 'not_watchable' });
  eq('and the poll stops', w.status(), 'closed');
  const n = polls;
  await settle(20);
  eq('no poll after the close', polls, n);
  w.dispose();
}
{
  // the route is off server-side: the stands close at once, no poll at all
  const srv = fakeServer();
  srv.st.refuse = { status: 404, error: 'not_deployed' };
  const bus = createBus();
  let closed = null;
  bus.on('watch-closed', (p) => { closed = p; });
  const w = createWatchSession({ bus, board: fakeBoard(), matchId: 'm_off', api: srv.api, now: () => 0, wait: () => Promise.resolve() });
  w.start();
  await settle(20);
  eq('not deployed closes the stands', closed && closed.reason, 'not_deployed');
  eq('and never polls', srv.api.calls.filter((c) => c.startsWith('events')).length, 0);
  w.dispose();
}

/* ------------------------------------------------------------------ the wire */
{
  const sent = [];
  let answer = { status: 200, body: '{}' };
  api.setTransport(async (method, path, body) => { sent.push({ method, path, body }); return answer; });
  await api.watch('m 1');
  eq('GET watch', [sent[0].method, sent[0].path], ['GET', '/v2/pbp/watch/m%201']);
  await api.watchEvents('m1', 7, 3000);
  eq('GET watch events, since and wait', sent[1].path, '/v2/pbp/watch/m1/events?since=7&wait_ms=3000');
  answer = { status: 404, body: JSON.stringify({ error: 'not_deployed' }) };
  eq('a worded 404 not_deployed', (await api.watch('m1')).error, 'not_deployed');
  answer = { status: 404, body: JSON.stringify({ error: 'not_found' }) };
  eq('a 404 not_found is still not found', (await api.watch('m1')).error, 'not_found');
  answer = { status: 404, body: '' };
  eq('a bare 404 is not deployed', (await api.watch('m1')).error, 'not_deployed');
  answer = { status: 403, body: JSON.stringify({ error: 'not_watchable' }) };
  eq('403 not_watchable', (await api.watch('m1')).error, 'not_watchable');
  answer = { status: 403, body: JSON.stringify({ error: 'your_match' }) };
  eq('403 your_match', (await api.watch('m1')).error, 'your_match');
  answer = { status: 403, body: JSON.stringify({ error: 'forbidden' }) };
  eq('any other 403 is still unauthorized', (await api.watch('m1')).error, 'unauthorized');

  // "Let people watch my games": only a no travels
  answer = { status: 200, body: '{}' };
  sent.length = 0;
  await api.lobbyEnter(api.timeControl(300000, 0)); await api.quick(api.timeControl(300000, 0)); await api.join('p_x');
  eq('on (the default): no watchable field', sent.map((s) => 'watchable' in s.body), [false, false, false]);
  let allow = false;
  api.setWatchPolicy(() => allow);
  sent.length = 0;
  await api.lobbyEnter(api.timeControl(300000, 0)); await api.quick(api.timeControl(300000, 0)); await api.join('p_x');
  eq('off: watchable:false on enter, quick and join', sent.map((s) => s.body.watchable), [false, false, false]);
  sent.length = 0;
  await api.challenge('p_x');
  ok('a challenge never carries it (a friend\'s game is never watchable anyway)', !('watchable' in sent[0].body));
  api.setWatchPolicy(() => { throw new Error('boom'); });
  sent.length = 0;
  await api.quick(api.timeControl(300000, 0));
  ok('a policy that throws reads as yes', !('watchable' in sent[0].body));
  api.setWatchPolicy(null);
  api.setTransport(null);
}

/* ------------------------------------------------------- the lobby's playing rows */
{
  const lobbyApi = {
    lobbyList: async () => env(200, { ok: true, players: [], playing: [
      { white: 'lace', black: 'hush', time_control: { initial_ms: 600000, increment_ms: 0 }, moves: 4, match_id: 'm_a', watchable: true, watchers: 3 },
      { white: 'velvet', black: 'moth', moves: 9, watchable: false },
      { white: 'rose', black: 'thorn', moves: 1, watchable: true },
    ] }),
    challenges: async () => env(200, { ok: true, incoming: [], outgoing: [] }),
  };
  const lobby = createServerLobby({ api: lobbyApi, force: true, pollMs: 5 });
  const t = await lobby.tables();
  eq('a watchable row carries its match id and count', [t.playing[0].matchId, t.playing[0].watchable, t.playing[0].watchers], ['m_a', true, 3]);
  eq('an unwatchable row carries none', [t.playing[1].matchId, t.playing[1].watchable], [null, false]);
  eq('watchable with no id is not watchable', t.playing[2].watchable, false);
  lobby.dispose?.();
}

/* --------------------------------------------------------------------- the switch */
{
  const srv = fakeServer();
  srv.play('e2e4'); srv.st.now += DELAY_MS;
  const bus = createBus();
  const board = fakeBoard();
  const hot = { rules: { chess: new Chess() }, start() {}, canPick: () => true, plies: () => 0, turn: () => 'w', isOver: () => false, result: () => null, record: () => ({}) };
  const game = createDriverSwitch(hot);
  let local = null;
  bus.on('local', (p) => { local = p; });
  const first = await srv.api.watch('m_watch');
  const d = startWatchMatch({ bus, board, game, match: { id: 'm_watch', state: first.data }, api: srv.api, now: () => 0 });
  eq('the local event names no seat and grades both', [local.mode, local.sides, local.graded], ['watch', [], ['w', 'b']]);
  ok('the switch says the stands are in the chair', game.isWatch === true && game.isOnline === true);
  eq('through the switch: no seat, both graded', [game.seats, game.graded], [[], ['w', 'b']]);
  eq('through the switch: nothing to pick', game.canPick('e2'), false);
  eq('through the switch: the watcher count', game.watchers(), 2);
  game.switchBack();
  ok('switchBack puts the hotseat back', game.current === hot && game.isWatch === false);
  eq('and the stands were disposed (a resync no longer runs)', await d._resync(), false);
  eq('a hotseat still grades both through the switch', game.graded, ['w', 'b']);
}

console.log(`watch-smoke: ${passed} checks passed`);
for (const f of failures) console.log('FAILED ' + f);
process.exit(failures.length ? 1 : 0);
