/* ============================================================================
 * net/lobby.js - who is at the board, and how two players find each other.
 *
 * The front door talks to ONE interface and never to a server. This file holds
 * that interface and a mock behind it, so every screen of the door is playable
 * and photographable with no host and no network. The real thing plugs in as
 * net/lobbyServer.js, exporting createServerLobby(opts) with the same shape;
 * boot.js prefers it when it exists and falls back to the mock when it does
 * not (or when ?lobby=mock asks for the mock on purpose).
 *
 *   lobby.enter({ name })            -> Promise<void>   I am at the board, visible
 *   lobby.leave()                    -> void            I have stepped away
 *   lobby.list()                     -> Promise<[{ id, name, waitingSince }]>
 *   lobby.onList(fn)                 -> off             the list changed
 *   lobby.quickMatch()               -> Promise<Match>  pair me with whoever waited longest
 *   lobby.challenge(id)              -> Promise<Match>  ask one player; rejects if they left
 *   lobby.onChallenge(fn)            -> off             someone asked me; fn({ id, name, accept(), decline() })
 *   lobby.cancel()                   -> void            stop looking (quickMatch/challenge reject 'cancelled')
 *   lobby.dispose()                  -> void
 *
 *   lobby.tables()                   -> Promise<{ open, playing }>  browse; never lists me
 *   lobby.onTables(fn)               -> off             the tables changed
 *   lobby.join(id)                   -> Promise<Match>  sit at one table; rejects 'gone' if it closed
 *   lobby.host({ timeControl })      -> Promise<Match>  list me and wait for someone to sit down
 *
 *   Match   = { id, opponent: { id, name }, side: 'w' | 'b', clockMs }
 *   open    = [{ id, name, waitingSince, rating, timeControl }]   oldest first
 *   playing = [{ white, black, timeControl, startedMs, moves }]   names only, newest first
 *
 * The mock is seeded by the clock, so the same second gives the same room; a
 * harness can pin it with createMockLobby({ seed }) and force a match with
 * lobby.debug.matchNow().
 * ==========================================================================*/

const HANDLES = ['velvet', 'moth', 'sugarcube', 'static', 'lace', 'nightlight', 'pixie', 'hush',
  'glimmer', 'marmalade', 'ribbon', 'quartz', 'fawn', 'cinder', 'dewdrop', 'tulle'];

/** The time controls a table can carry, the same pairs the server's menu offers. */
const MOCK_TCS = [[300000, 3000], [600000, 0], [900000, 10000], [180000, 2000], [600000, 5000]];
const tcOf = (i) => ({ initial_ms: MOCK_TCS[i % MOCK_TCS.length][0], increment_ms: MOCK_TCS[i % MOCK_TCS.length][1] });

function rng(seed) {
  let s = (Number(seed) || 1) >>> 0;
  return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
}

export function createMockLobby({ seed = Date.now(), clockMs = 15 * 60 * 1000, now = () => Date.now() } = {}) {
  const rand = rng(seed);
  const listeners = { list: new Set(), challenge: new Set(), tables: new Set() };
  let me = null;
  let waiting = [];
  let playing = [];
  let seeded = false;
  let churn = null;
  let pending = null;        // { resolve, reject } for the one outstanding quickMatch/challenge
  let timers = [];

  function later(fn, ms) { const t = setTimeout(fn, ms); timers.push(t); return t; }
  function emitList() {
    for (const fn of [...listeners.list]) { try { fn(list()); } catch { /* keep going */ } }
    for (const fn of [...listeners.tables]) { try { fn(tables()); } catch { /* keep going */ } }
  }
  function list() { return waiting.map((p) => ({ ...p })).sort((a, b) => a.waitingSince - b.waitingSince); }
  function tables() { return { open: list(), playing: playing.map((g) => ({ ...g })).sort((a, b) => b.startedMs - a.startedMs) }; }

  function table(name) {
    const i = Math.floor(rand() * 100);
    return { id: 'm-' + name, name, waitingSince: now() - Math.floor(rand() * 420000),
             rating: rand() < 0.7 ? 1050 + Math.floor(rand() * 500) : null, timeControl: tcOf(i) };
  }

  // Enough tables that the list has to scroll on a phone, and a few games on.
  function seedRoom() {
    seeded = true;
    waiting = [];
    playing = [];
    const pool = [...HANDLES];
    const n = 5 + Math.floor(rand() * 4);
    for (let i = 0; i < n; i++) waiting.push(table(pool.splice(Math.floor(rand() * pool.length), 1)[0]));
    const g = 2 + Math.floor(rand() * 2);
    for (let i = 0; i < g && pool.length > 1; i++) {
      const white = pool.splice(Math.floor(rand() * pool.length), 1)[0];
      const black = pool.splice(Math.floor(rand() * pool.length), 1)[0];
      playing.push({ white, black, timeControl: tcOf(i + 1), startedMs: now() - Math.floor(rand() * 600000), moves: 4 + Math.floor(rand() * 50) });
    }
  }
  function ensureRoom() { if (!seeded) seedRoom(); }

  // The room breathes: every few seconds someone arrives or gives up.
  function startChurn() {
    if (churn) return;
    const step = () => {
      churn = later(() => {
        const taken = new Set([...waiting.map((p) => p.name), ...playing.flatMap((g) => [g.white, g.black])]);
        const free = HANDLES.filter((h) => !taken.has(h));
        if (waiting.length > 1 && rand() < 0.5) waiting.splice(Math.floor(rand() * waiting.length), 1);
        else if (free.length) { const name = free[Math.floor(rand() * free.length)]; waiting.push({ ...table(name), waitingSince: now() }); }
        for (const g of playing) g.moves += 1 + Math.floor(rand() * 3);
        emitList();
        // one in five rooms, someone asks you
        if (me && rand() < 0.2 && waiting.length) askMe(waiting[Math.floor(rand() * waiting.length)]);
        step();
      }, 4000 + rand() * 5000);
    };
    step();
  }

  function askMe(p) {
    let done = false;
    const offer = {
      id: p.id, name: p.name,
      accept: () => { if (done) return null; done = true; return matchWith(p, true); },
      decline: () => { done = true; },
    };
    for (const fn of [...listeners.challenge]) { try { fn(offer); } catch { /* keep going */ } }
  }

  function matchWith(p, theyAsked = false) {
    waiting = waiting.filter((q) => q.id !== p.id);
    emitList();
    return { id: 'g-' + Math.floor(rand() * 1e9).toString(36), opponent: { id: p.id, name: p.name },
             side: theyAsked ? (rand() < 0.5 ? 'w' : 'b') : (rand() < 0.5 ? 'w' : 'b'), clockMs };
  }

  function look(resolveWith, ms, refusal = 'left', kind = 'look') {
    cancel();
    return new Promise((resolve, reject) => {
      pending = { resolve, reject, kind };
      later(() => {
        if (pending && pending.reject === reject) {
          pending = null;
          const m = resolveWith();
          if (m) resolve(m); else reject(new Error(refusal));
        }
      }, ms);
    });
  }

  function cancel() {
    if (pending) {
      const p = pending; pending = null;
      if (p.kind === 'host') me = null;
      p.reject(new Error('cancelled'));
    }
  }

  return {
    async enter(profile = {}) {
      me = { name: profile.name || 'you' };
      ensureRoom();
      startChurn();
      emitList();
    },
    leave() {
      me = null; cancel();
      if (churn && !listeners.tables.size) { clearTimeout(churn); churn = null; }
    },
    async list() { ensureRoom(); return list(); },
    onList(fn) { listeners.list.add(fn); return () => listeners.list.delete(fn); },
    async tables() { ensureRoom(); return tables(); },
    onTables(fn) {
      ensureRoom();
      listeners.tables.add(fn);
      startChurn();
      return () => listeners.tables.delete(fn);
    },
    join(id) {
      return look(() => { const p = waiting.find((q) => q.id === id); return p ? matchWith(p) : null; }, 700 + rand() * 500, 'gone', 'join');
    },
    host(opts = {}) {
      me = { name: 'you' };
      ensureRoom();
      startChurn();
      // someone sits down after a while
      return look(() => {
        const taken = new Set(waiting.map((p) => p.name));
        const name = HANDLES.find((h) => !taken.has(h)) || 'velvet';
        return matchWith({ id: 'm-' + name, name });
      }, 4000 + rand() * 4000, 'left', 'host');
    },
    onChallenge(fn) { listeners.challenge.add(fn); return () => listeners.challenge.delete(fn); },
    quickMatch() {
      return look(() => {
        const first = list()[0];
        if (first) return matchWith(first);
        // nobody there: someone turns up for you
        const name = HANDLES[Math.floor(rand() * HANDLES.length)];
        return matchWith({ id: 'm-' + name, name });
      }, 1400 + rand() * 1400, 'left', 'quick');
    },
    challenge(id) {
      return look(() => { const p = waiting.find((q) => q.id === id); return p ? matchWith(p) : null; }, 900 + rand() * 600);
    },
    cancel,
    dispose() { listeners.tables.clear(); this.leave(); for (const t of timers) clearTimeout(t); timers = []; listeners.list.clear(); listeners.challenge.clear(); },
    debug: {
      /** Resolve whatever is being looked for right now, this instant. */
      matchNow() {
        if (!pending) return null;
        const p = pending; pending = null;
        const m = matchWith(list()[0] || { id: 'm-velvet', name: 'velvet' });
        p.resolve(m);
        return m;
      },
      askMe(name = 'moth') { askMe({ id: 'm-' + name, name }); },
      /** Pin the room: `open` and `playing` as the tables() shapes, for a photograph. */
      setTables(open, games) {
        seeded = true;
        if (Array.isArray(open)) waiting = open.map((p) => ({ ...p }));
        if (Array.isArray(games)) playing = games.map((g) => ({ ...g }));
        emitList();
      },
      /** What is being looked for: 'quick' | 'join' | 'host' | 'look' | null. */
      looking: () => (pending ? pending.kind : null),
      isMock: true,
    },
  };
}
