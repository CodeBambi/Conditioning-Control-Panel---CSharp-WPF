/* ============================================================================
 * smoke/tables-smoke.mjs - the open tables, in node, no browser:
 *
 *   node smoke/tables-smoke.mjs
 *
 * Both lobbies wear the same new surface: tables() / onTables() / join() /
 * host(). The server adapter runs against a fake api written from the wire
 * contract (GET /lobby with `playing` and `matched`, POST /join and its
 * refusals), and what it must never do is as checked as what it must:
 *
 *   BROWSING IS NOT SITTING. tables() and onTables() read the list and never
 *   post /lobby/enter; leaving a browse posts no goodbye either.
 *   A JOIN IS ONE CALL. 200 is a match at once; table_gone / invalid_target
 *   read 'gone', self and blocked keep their words, 409 in_match resumes the
 *   live game, and a server with no /join falls back to a challenge.
 *   HOSTING LISTS, AND HEARS. host() posts enter once; the poll's `matched`
 *   starts the game ONCE, even though the server keeps answering it.
 *   The playing rows carry names and counts, never an id.
 * ==========================================================================*/

import { createMockLobby } from '../net/lobby.js';
import { createServerLobby } from '../net/lobbyServer.js';
import { tcWord } from '../door/door.js';

let passed = 0;
const failures = [];
const ok = (what, cond) => { if (cond) passed++; else failures.push(what); };
const eq = (what, got, want) => ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const settle = async (n = 10) => { for (let i = 0; i < n; i++) await Promise.resolve(); };

/* ------------------------------------------------------------------ fake api */

const env = (status, data) => {
  const ok2 = status >= 200 && status < 300;
  const out = { ok: ok2, status, data, error: ok2 ? null : (status === 409 ? 'stale' : status === 404 && !data ? 'not_deployed' : status === 403 ? 'unauthorized' : 'server_error') };
  if (!ok2 && data && typeof data.error === 'string') out.serverError = data.error;
  return out;
};

function fakeApi() {
  const calls = [];
  const st = {
    players: [
      { id: 'p_aaaaaaaaaaaa', display_name: 'velvet', since_ms: 1000, rating: 1301, time_control: { initial_ms: 600000, increment_ms: 0 } },
      { id: 'p_bbbbbbbbbbbb', display_name: 'moth', since_ms: 2000, rating: null, time_control: { initial_ms: 300000, increment_ms: 3000 } },
    ],
    playing: [{ white: 'lace', black: 'hush', time_control: { initial_ms: 900000, increment_ms: 10000 }, started_ms: 5, moves: 14 }],
    matched: null,
    join: () => env(200, { ok: true, match_id: 'm_join', color: 'b' }),
  };
  const log = (name) => { calls.push(name); };
  const api = {
    calls, st,
    count: (name) => calls.filter((c) => c === name).length,
    lobbyEnter: async () => { log('enter'); return env(200, { ok: true }); },
    lobbyLeave: async () => { log('leave'); return env(200, { ok: true }); },
    lobbyList: async () => { log('lobby'); return env(200, { ok: true, server_now_ms: 1, players: st.players.slice(), playing: st.playing.slice(), matched: st.matched }); },
    challenges: async () => { log('challenges'); return env(200, { ok: true, incoming: [], outgoing: [] }); },
    quick: async () => { log('quick'); return env(200, { ok: true, waiting: true }); },
    challenge: async () => { log('challenge'); return env(200, { ok: true, match_id: 'm_chal', color: 'w' }); },
    acceptChallenge: async () => env(200, { ok: true, match_id: 'm_acc', color: 'w' }),
    declineChallenge: async () => env(200, { ok: true }),
    join: async (target) => { log('join:' + target); return st.join(target); },
    match: async (id) => {
      log('match');
      const you = id === 'm_join' ? 'b' : 'w';
      return env(200, { id, you, status: 'live', result: null,
        white: { id: 'p_w', display_name: you === 'w' ? 'you' : 'velvet' }, black: { id: 'p_b', display_name: you === 'b' ? 'you' : 'glimmer' },
        time_control: { initial_ms: 300000, increment_ms: 3000 } });
    },
  };
  return api;
}

/* --------------------------------------------------------------- the adapter */

async function adapter() {
  {
    const api = fakeApi();
    const lobby = createServerLobby({ api, force: true, pollMs: 5 });
    eq('the adapter wears the new surface', ['tables', 'onTables', 'join', 'host'].every((k) => typeof lobby[k] === 'function'), true);

    const t = await lobby.tables();
    eq('tables(): open rows in the door shape', t.open.map((r) => [r.id, r.name, r.waitingSince, r.rating]), [['p_aaaaaaaaaaaa', 'velvet', 1000, 1301], ['p_bbbbbbbbbbbb', 'moth', 2000, null]]);
    eq('tables(): the table carries its clock', t.open[1].timeControl, { initial_ms: 300000, increment_ms: 3000 });
    eq('tables(): playing rows are names and counts only', t.playing, [{ white: 'lace', black: 'hush', timeControl: { initial_ms: 900000, increment_ms: 10000 }, startedMs: 5, moves: 14, matchId: null, watchable: false, watchers: 0 }]);
    ok('a playing row carries no id', !JSON.stringify(t.playing).includes('"id"'));

    const seen = [];
    const off = lobby.onTables((x) => seen.push(x));
    await sleep(40);
    ok('onTables polls while watched', seen.length >= 2);
    eq('browsing never lists the player', api.count('enter'), 0);
    off();
    await sleep(30);
    const n = api.count('lobby');
    await sleep(30);
    eq('the poll stops once nobody watches', api.count('lobby'), n);
    lobby.leave();
    eq('leaving a browse posts no goodbye', api.count('leave'), 0);

    // the self row is not a table to sit at
    api.st.players.push({ id: 'p_meeeeeeeeeee', display_name: 'you', since_ms: 3, self: true });
    eq('my own row is not in the list', (await lobby.tables()).open.length, 2);
    lobby.dispose();
  }

  {
    const api = fakeApi();
    const lobby = createServerLobby({ api, force: true, pollMs: 5 });
    await lobby.tables();
    const m = await lobby.join('p_aaaaaaaaaaaa');
    eq('join 200: one call, a match, the seat from the record', [api.count('join:p_aaaaaaaaaaaa'), m.id, m.side], [1, 'm_join', 'b']);
    eq('join 200: the opponent named', m.opponent.name, 'velvet');
    eq('a join posts no enter', api.count('enter'), 0);

    const refusal = async (status, body) => {
      api.st.join = () => env(status, body);
      try { await lobby.join('p_bbbbbbbbbbbb'); return 'resolved'; } catch (e) { return e.message; }
    };
    eq('409 table_gone reads gone', await refusal(409, { error: 'table_gone' }), 'gone');
    eq('400 invalid_target reads gone', await refusal(400, { error: 'invalid_target' }), 'gone');
    eq('400 self keeps its word', await refusal(400, { error: 'self' }), 'self');
    eq('403 blocked keeps its word', await refusal(403, { error: 'blocked' }), 'blocked');

    api.st.join = () => env(409, { error: 'in_match', match_id: 'm_live', color: 'w' });
    const live = await lobby.join('p_bbbbbbbbbbbb');
    eq('409 in_match resumes the live game', [live.id, live.side], ['m_live', 'w']);

    api.st.join = () => env(404, null);
    const fell = await lobby.join('p_bbbbbbbbbbbb');
    eq('no /join on the server: a challenge to the same row', [fell.id, api.count('challenge')], ['m_chal', 1]);
    lobby.dispose();
  }

  {
    const api = fakeApi();
    const lobby = createServerLobby({ api, force: true, pollMs: 5 });
    const hosted = lobby.host({ timeControl: { initial_ms: 300000, increment_ms: 3000 } });
    await sleep(25);
    eq('host() lists the player once', api.count('enter'), 1);
    eq('the adapter says it is listed', lobby.debug.state().listed, true);
    api.st.matched = { match_id: 'm_host', color: 'w' };
    // the adapter's poll timers are unref'd, so the test keeps node awake while it ticks
    await sleep(40);
    const m = await hosted;
    eq('matched starts the game', [m.id, m.side], ['m_host', 'w']);

    // the server keeps answering `matched` while the match is live: a second
    // table must not be handed the same game
    let second = null;
    lobby.host().then((x) => { second = x; }, () => {});
    await sleep(40);
    eq('a repeated matched is ignored', second, null);
    lobby.cancel();
    await settle();
    eq('leaving the table posts the goodbye', api.count('leave'), 1);
    eq('and the player is no longer listed', lobby.debug.state().listed, false);
    lobby.dispose();
  }

  {
    const api = fakeApi();
    const lobby = createServerLobby({ api, force: true, pollMs: 5 });
    const q = lobby.quickMatch().catch((e) => e.message);
    await sleep(15);
    lobby.cancel();
    eq('stop looking takes the player out of the quick queue', [await q, api.count('leave')], ['cancelled', 1]);
    lobby.dispose();
  }
}

/* ------------------------------------------------------------------ the mock */

async function mock() {
  const lobby = createMockLobby({ seed: 7 });
  const t = await lobby.tables();
  ok('the mock has enough tables to scroll', t.open.length >= 5);
  ok('the mock has games on', t.playing.length >= 2);
  ok('every open row has a clock', t.open.every((r) => r.timeControl && r.timeControl.initial_ms > 0));
  ok('every playing row is two names and a count', t.playing.every((g) => typeof g.white === 'string' && typeof g.black === 'string' && Number.isFinite(g.moves)));
  eq('the mock is still a mock', lobby.debug.isMock, true);

  const m = await lobby.join(t.open[0].id);
  eq('joining a mock table seats you against its host', m.opponent.name, t.open[0].name);
  let gone = null;
  try { await lobby.join('m-nobody'); } catch (e) { gone = e.message; }
  eq('a mock table that closed reads gone', gone, 'gone');

  const h = lobby.host();
  eq('the mock knows it is hosting', lobby.debug.looking(), 'host');
  lobby.debug.matchNow();
  ok('matchNow seats a hosted table', !!(await h).id);
  lobby.dispose();
}

/* ------------------------------------------------------------------ the words */

function words() {
  eq('10+0', tcWord({ initial_ms: 600000, increment_ms: 0 }), '10+0');
  eq('5+3', tcWord({ initial_ms: 300000, increment_ms: 3000 }), '5+3');
  eq('a clock that is not whole minutes', tcWord({ initial_ms: 90000, increment_ms: 1000 }), '1:30+1');
  eq('no clock, no word', tcWord(null), '');
}

words();
await mock();
await adapter();

if (failures.length) {
  console.log(`tables-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.log('  FAIL ' + f);
  process.exit(1);
}
console.log(`tables-smoke: ${passed} checks passed`);
process.exit(0);
