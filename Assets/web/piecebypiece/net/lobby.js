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
 *   Match = { id, opponent: { id, name }, side: 'w' | 'b', clockMs }
 *
 * The mock is seeded by the clock, so the same second gives the same room; a
 * harness can pin it with createMockLobby({ seed }) and force a match with
 * lobby.debug.matchNow().
 * ==========================================================================*/

const HANDLES = ['velvet', 'moth', 'sugarcube', 'static', 'lace', 'nightlight', 'pixie', 'hush'];

function rng(seed) {
  let s = (Number(seed) || 1) >>> 0;
  return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; };
}

export function createMockLobby({ seed = Date.now(), clockMs = 15 * 60 * 1000, now = () => Date.now() } = {}) {
  const rand = rng(seed);
  const listeners = { list: new Set(), challenge: new Set() };
  let me = null;
  let waiting = [];
  let churn = null;
  let pending = null;        // { resolve, reject } for the one outstanding quickMatch/challenge
  let timers = [];

  function later(fn, ms) { const t = setTimeout(fn, ms); timers.push(t); return t; }
  function emitList() { for (const fn of [...listeners.list]) { try { fn(list()); } catch { /* keep going */ } } }
  function list() { return waiting.map((p) => ({ ...p })).sort((a, b) => a.waitingSince - b.waitingSince); }

  function seedRoom() {
    waiting = [];
    const n = 2 + Math.floor(rand() * 3);
    const pool = [...HANDLES];
    for (let i = 0; i < n; i++) {
      const name = pool.splice(Math.floor(rand() * pool.length), 1)[0];
      waiting.push({ id: 'm-' + name, name, waitingSince: now() - Math.floor(rand() * 240000) });
    }
  }

  // The room breathes: every few seconds someone arrives or gives up.
  function startChurn() {
    if (churn) return;
    const step = () => {
      churn = later(() => {
        const taken = new Set(waiting.map((p) => p.name));
        const free = HANDLES.filter((h) => !taken.has(h));
        if (waiting.length > 1 && rand() < 0.5) waiting.splice(Math.floor(rand() * waiting.length), 1);
        else if (free.length) { const name = free[Math.floor(rand() * free.length)]; waiting.push({ id: 'm-' + name, name, waitingSince: now() }); }
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

  function look(resolveWith, ms) {
    cancel();
    return new Promise((resolve, reject) => {
      pending = { resolve, reject };
      later(() => {
        if (pending && pending.reject === reject) {
          pending = null;
          const m = resolveWith();
          if (m) resolve(m); else reject(new Error('left'));
        }
      }, ms);
    });
  }

  function cancel() {
    if (pending) { const r = pending.reject; pending = null; r(new Error('cancelled')); }
  }

  return {
    async enter(profile = {}) {
      me = { name: profile.name || 'you' };
      seedRoom();
      startChurn();
      emitList();
    },
    leave() { me = null; cancel(); if (churn) { clearTimeout(churn); churn = null; } },
    async list() { return list(); },
    onList(fn) { listeners.list.add(fn); return () => listeners.list.delete(fn); },
    onChallenge(fn) { listeners.challenge.add(fn); return () => listeners.challenge.delete(fn); },
    quickMatch() {
      return look(() => {
        const first = list()[0];
        if (first) return matchWith(first);
        // nobody there: someone turns up for you
        const name = HANDLES[Math.floor(rand() * HANDLES.length)];
        return matchWith({ id: 'm-' + name, name });
      }, 1400 + rand() * 1400);
    },
    challenge(id) {
      return look(() => { const p = waiting.find((q) => q.id === id); return p ? matchWith(p) : null; }, 900 + rand() * 600);
    },
    cancel,
    dispose() { this.leave(); for (const t of timers) clearTimeout(t); timers = []; listeners.list.clear(); listeners.challenge.clear(); },
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
      isMock: true,
    },
  };
}
