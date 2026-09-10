/* ============================================================================
 * net/lobbyServer.js - the real lobby, behind the front door's interface.
 *
 * THE INTERFACE IS NOT MINE. door/door.js was written against the mock in
 * net/lobby.js, and this file exists to be indistinguishable from it:
 *
 *   enter({ name })   -> Promise<void>
 *   leave()           -> void
 *   list()            -> Promise<[{ id, name, waitingSince }]>
 *   onList(fn)        -> off
 *   quickMatch()      -> Promise<Match>     rejects Error('cancelled')
 *   challenge(id)     -> Promise<Match>     rejects Error('left') / Error('cancelled')
 *   onChallenge(fn)   -> off   fn({ id, name, accept(), decline() })
 *   cancel()          -> void
 *   dispose()         -> void
 *
 *   Match = { id, opponent: { id, name }, side: 'w' | 'b', clockMs }
 *
 * boot.js imports this in preference to the mock and falls back when the import
 * fails OR when the factory answers null. Answering null is a first-class
 * outcome here, not an error: a page with no account behind it cannot play
 * anybody, and a lobby that 401s every call for the rest of the session is a
 * much worse experience than the mock the door already knows how to open.
 *
 * WHAT THE SERVER CALLS THINGS, AND WHAT THE DOOR CALLS THINGS.
 * The wire never hands a client another player's unified id - it hands opaque
 * `p_` ids and display names (contract section 0). Those map cleanly onto the
 * door's `{ id, name }`, so `id` here is a `p_` id all the way through: it is
 * what a lobby row carries, it is what `challenge(id)` sends as `target`, and
 * it is what the door hands back when somebody clicks a row. Nothing has to be
 * translated anywhere else.
 *
 * WHY EVERY MATCH GOES THROUGH GET /match/:id.
 * The door needs `side` and `clockMs` before it can deal a board. Quick match
 * and accept both answer with a colour, but an outgoing challenge that was
 * accepted does NOT - the challenger asked for `random`, and the only place the
 * answer exists is the match record. Rather than have two paths, one of which
 * is only exercised by the rarer half of the traffic, all four resolve through
 * one `matchFrom` that asks. It costs one round trip at the moment a game
 * starts, which is the moment nobody is counting.
 * ==========================================================================*/

import * as defaultApi from './api.js';
import { isHosted, identity, whenIdentity } from '../bridge.js';

/**
 * The contract asks for ~2s while a lobby screen is open, and caps /quick at
 * 40/min/user, which is 1.5s. This is the fastest this may honestly poll.
 */
export const POLL_MS = 2000;

/**
 * A lobby listing expires after 180s of no enter/quick refresh. While the
 * player is merely sitting in the lobby - not in the quick queue - nothing else
 * we send counts as a refresh, so the listing has to be renewed or the player
 * quietly vanishes from everybody else's list after three minutes.
 */
export const REENTER_MS = 60000;

/** 10+0. The door has no time-control picker yet; when it grows one, it passes it in. */
export const DEFAULT_TIME_CONTROL = Object.freeze({ initial_ms: 10 * 60 * 1000, increment_ms: 0 });

function emit(set, arg) {
  for (const fn of Array.from(set)) {
    try { fn(arg); } catch (err) { console.warn('[pbp] lobby listener threw', err); }
  }
}

/**
 * @param {object} [o]
 * @param {object} [o.bus]        the game bus, for the events the door does not read
 * @param {URLSearchParams} [o.params]
 * @param {object} [o.api]        net/api.js, swappable for tests
 * @param {object} [o.timeControl]
 * @param {number} [o.pollMs]
 * @param {boolean} [o.force]     build one even with no host (tests, ?net=live)
 * @returns {object|null} the adapter, or null when this page cannot play online
 */
export function createServerLobby({
  bus = null, params = null, api = defaultApi, timeControl = null,
  pollMs = POLL_MS, force = false, timer = null,
} = {}) {
  const forced = force || (params && params.get('net') === 'live');
  // No host means no auth token, and every route needs one. Say so now, once,
  // by answering null - boot.js then opens the door on the mock, which is the
  // right thing for a browser tab with no account behind it.
  if (!isHosted && !forced) return null;

  /**
   * Is there an account behind this page? `forced` stands in for one, which is
   * how the smoke run and a `?net=live` dev page get to exercise the real
   * adapter without a desktop host to hand them a token.
   */
  const signedIn = () => forced || !!identity().unifiedId;

  const setT = (timer && timer.set) || ((fn, ms) => { const t = setTimeout(fn, ms); if (t && t.unref) t.unref(); return t; });
  const clearT = (timer && timer.clear) || ((t) => clearTimeout(t));

  const listListeners = new Set();
  const challengeListeners = new Set();

  let tc = timeControl || DEFAULT_TIME_CONTROL;
  let entered = false;
  let disposed = false;
  let handle = null;
  let lastEnter = 0;
  let people = [];
  /** Incoming challenge ids we have already offered the door, so it is asked once. */
  const offered = new Set();
  /** Incoming challenge ids the player turned down, so a stale row is not re-offered. */
  const refused = new Set();

  /**
   * The one thing being looked for right now, or null. Exactly one at a time,
   * because the door only ever shows one "looking" state and the server only
   * ever gives you one live match.
   * @type {{kind:'quick'|'challenge', challengeId:string|null, resolve:Function, reject:Function, settled:boolean}|null}
   */
  let pending = null;

  function settle(fn, value) {
    if (!pending || pending.settled) return;
    pending.settled = true;
    const p = pending;
    pending = null;
    try { fn(p, value); } catch (err) { console.warn('[pbp] lobby settle threw', err); }
  }
  const resolveLook = (m) => settle((p, v) => p.resolve(v), m);
  const rejectLook = (why) => settle((p, v) => p.reject(new Error(v)), why);

  /* ---------------------------------------------------------------- shapes */

  /** A server lobby row -> the door's row. `self` is dropped: it lists other people. */
  function toRow(p) {
    return {
      id: String(p.id || ''),
      name: String(p.display_name || p.name || 'someone'),
      waitingSince: Number(p.since_ms) || 0,
      /** Extras the door ignores today and may want tomorrow. */
      rating: (p.rating === null || p.rating === undefined) ? null : Number(p.rating),
      timeControl: p.time_control || null,
    };
  }

  /**
   * A match id -> the Match the door deals a board from. Asks the server, which
   * is the only thing that knows which colour you got.
   *
   * `colorHint` and `opponentHint` are what the answer that produced this id
   * happened to carry; they are the fallback for the one case that matters -
   * the GET failing on a flaky connection at the exact moment a game starts.
   * Handing back a match with a guessed side is better than handing back
   * nothing: net/match.js corrects the side from its own first GET anyway.
   */
  async function matchFrom(matchId, colorHint, opponentHint) {
    const res = await api.match(matchId);
    if (res.ok && res.data) {
      const d = res.data;
      const side = (d.you === 'w' || d.you === 'b') ? d.you : (colorHint === 'b' ? 'b' : 'w');
      const them = (side === 'w' ? d.black : d.white) || opponentHint || {};
      return {
        id: String(d.id || matchId),
        opponent: { id: String(them.id || ''), name: String(them.display_name || them.name || 'someone') },
        side,
        clockMs: Number((d.time_control && d.time_control.initial_ms)) || tc.initial_ms,
        /** Extras for the online lane; the door reads none of them. */
        state: d,
        rating: (them.rating === null || them.rating === undefined) ? null : Number(them.rating),
      };
    }
    return {
      id: String(matchId),
      opponent: {
        id: String((opponentHint && opponentHint.id) || ''),
        name: String((opponentHint && (opponentHint.display_name || opponentHint.name)) || 'someone'),
      },
      side: colorHint === 'b' ? 'b' : 'w',
      clockMs: tc.initial_ms,
      state: null,
      rating: null,
    };
  }

  /** A match is starting. The door hears it as a resolved promise; the rest of the page as an event. */
  function announce(match) {
    if (bus) bus.emit('online-match', match);
    return match;
  }

  /* ----------------------------------------------------------------- offers */

  /**
   * Hand the door an incoming challenge.
   *
   * ONE WART, DELIBERATE AND DOCUMENTED. door.js does
   * `const m = ask.accept(); if (m) matched(m);` - it takes the return value
   * SYNCHRONOUSLY. Accepting on a server is a round trip, so there is no honest
   * synchronous answer to give. What comes back instead is the Match object the
   * door will eventually deal from, with `id` and `side` filled in a moment
   * later, in place, and a `ready` promise for anything that would rather wait
   * properly. The door's own "found" screen shows the opponent's name, which is
   * known immediately, so the gap is invisible at the speed a person clicks.
   *
   * The clean fix is one line in door.js -
   *     Promise.resolve(ask.accept()).then((m) => { if (m) matched(m); });
   * which the sync mock satisfies unchanged. It is written up in
   * scratchpad/pbp-online/lobby-adapter.md for whoever owns that file.
   */
  function offer(row) {
    const challengeId = String(row.challenge_id || row.challengeId || '');
    const from = row.from || {};
    let done = false;
    const placeholder = {
      id: '',
      opponent: { id: String(from.id || ''), name: String(from.display_name || from.name || 'someone') },
      side: (row.color === 'w' || row.color === 'b') ? row.color : 'w',
      clockMs: Number((row.time_control && row.time_control.initial_ms)) || tc.initial_ms,
      state: null,
      ready: null,
    };
    return {
      id: String(from.id || challengeId),
      name: placeholder.opponent.name,
      challengeId,
      timeControl: row.time_control || null,
      accept() {
        if (done || disposed) return null;
        done = true;
        placeholder.ready = (async () => {
          const res = await api.acceptChallenge(challengeId);
          if (!res.ok) return null;
          const id = res.data.match_id || res.data.matchId;
          if (!id) return null;
          const m = await matchFrom(id, res.data.color, from);
          // In place: the door is already holding this object.
          Object.assign(placeholder, m);
          announce(placeholder);
          return placeholder;
        })();
        placeholder.ready.catch(() => {});
        return placeholder;
      },
      decline() {
        if (done) return;
        done = true;
        refused.add(challengeId);
        if (challengeId) api.declineChallenge(challengeId).catch(() => {});
      },
    };
  }

  /* ------------------------------------------------------------------ poll */

  async function refresh() {
    if (disposed || !entered) return;

    // Keep the listing alive. /quick counts as a refresh, so this only fires
    // while the player is sitting in the lobby rather than queueing.
    const queueing = !!(pending && pending.kind === 'quick');
    if (!queueing && Date.now() - lastEnter >= REENTER_MS) {
      lastEnter = Date.now();
      api.lobbyEnter(tc).catch(() => {});
    }

    const [lob, chal] = await Promise.all([api.lobbyList(), api.challenges()]);
    if (disposed || !entered) return;

    if (lob.ok && Array.isArray(lob.data.players)) {
      people = lob.data.players.filter((p) => p && p.self !== true).map(toRow);
      emit(listListeners, list());
    }

    if (chal.ok) {
      const incoming = Array.isArray(chal.data.incoming) ? chal.data.incoming : [];
      const outgoing = Array.isArray(chal.data.outgoing) ? chal.data.outgoing : [];

      for (const row of incoming) {
        const id = String(row.challenge_id || row.challengeId || '');
        if (!id || offered.has(id) || refused.has(id)) continue;
        offered.add(id);
        emit(challengeListeners, offer(row));
      }

      // Our own challenge: accepted is a match, and gone is a no.
      if (pending && pending.kind === 'challenge' && pending.challengeId) {
        const mine = outgoing.find((r) => String(r.challenge_id || r.challengeId || '') === pending.challengeId);
        if (mine && (mine.match_id || mine.matchId)) {
          const hint = mine.to || null;
          const id = mine.match_id || mine.matchId;
          const m = await matchFrom(id, mine.color, hint);
          if (!disposed) resolveLook(announce(m));
          return;
        }
        // The row is gone, or explicitly turned down. Either way he is not
        // playing: 'left' is the word the door plays its refusal sound on.
        if (!mine || String(mine.status || '') === 'declined') { rejectLook('left'); return; }
      }
    }

    // Still queueing. /quick IS the poll: it keeps the listing alive and
    // answers waiting:true until somebody pairs with us.
    if (pending && pending.kind === 'quick') {
      const q = await api.quick(tc);
      if (disposed) return;
      lastEnter = Date.now();
      if (q.ok) {
        const id = q.data.match_id || q.data.matchId;
        if (id) {
          const m = await matchFrom(id, q.data.color, null);
          if (!disposed) resolveLook(announce(m));
        }
      }
    }
  }

  function tick() {
    handle = null;
    refresh()
      .catch(() => {})
      .then(() => { if (!disposed && entered) handle = setT(tick, pollMs); });
  }

  function stopPoll() { if (handle) { clearT(handle); handle = null; } }

  /* ------------------------------------------------------------- the doors */

  function list() { return people.map((p) => Object.assign({}, p)).sort((a, b) => a.waitingSince - b.waitingSince); }

  function cancel() {
    // A challenge we are cancelling has to be withdrawn on the server too, or
    // it sits in the other player's list for its whole 300s life waiting for
    // somebody who has walked away.
    if (pending && pending.kind === 'challenge' && pending.challengeId) {
      api.declineChallenge(pending.challengeId).catch(() => {});
    }
    rejectLook('cancelled');
  }

  return {
    async enter(profile = {}) {
      if (disposed) return;
      // The server names the player from the account; the door's `name` is not
      // sent anywhere. Waiting for the identity frame first, because the lane
      // that imported this may well have got here before the host's frame did.
      await whenIdentity();
      if (disposed) return;
      if (!signedIn()) throw new Error('no account');
      if (profile && profile.timeControl) tc = profile.timeControl;
      const res = await api.lobbyEnter(tc);
      if (!res.ok) throw new Error(res.error || 'enter failed');
      entered = true;
      lastEnter = Date.now();
      if (!handle) handle = setT(tick, pollMs);
      await refresh();
    },

    /**
     * Stand up. VOID, and unconditional locally: the door calls this on its way
     * out and does not wait. The goodbye is best-effort on the wire because the
     * server's own 180s expiry is the real backstop.
     */
    leave() {
      entered = false;
      stopPoll();
      cancel();
      people = [];
      offered.clear();
      refused.clear();
      if (signedIn()) api.lobbyLeave().catch(() => {});
    },

    async list() {
      if (disposed) return [];
      const res = await api.lobbyList();
      if (res.ok && Array.isArray(res.data.players)) {
        people = res.data.players.filter((p) => p && p.self !== true).map(toRow);
      }
      return list();
    },

    onList(fn) {
      if (typeof fn !== 'function') return () => {};
      listListeners.add(fn);
      return () => listListeners.delete(fn);
    },

    onChallenge(fn) {
      if (typeof fn !== 'function') return () => {};
      challengeListeners.add(fn);
      return () => challengeListeners.delete(fn);
    },

    /**
     * "Anyone." Resolves with a Match when somebody pairs with us, and rejects
     * 'cancelled' if the player gives up first. The waiting is done by the
     * poll, which is also what keeps the lobby listing alive.
     */
    quickMatch() {
      cancel();
      if (disposed) return Promise.reject(new Error('cancelled'));
      return new Promise((resolve, reject) => {
        pending = { kind: 'quick', challengeId: null, resolve, reject, settled: false };
        entered = true;
        if (!handle) handle = setT(tick, pollMs);
        (async () => {
          const res = await api.quick(tc);
          if (disposed || !pending || pending.settled || pending.kind !== 'quick') return;
          lastEnter = Date.now();
          if (!res.ok) { rejectLook(res.error === 'offline' ? 'left' : 'left'); return; }
          const id = res.data.match_id || res.data.matchId;
          // Already paired, or already in a live match the server handed back
          // rather than starting a second one.
          if (id) {
            const m = await matchFrom(id, res.data.color, null);
            if (!disposed) resolveLook(announce(m));
          }
          // Otherwise waiting:true, and the poll takes it from here.
        })().catch(() => rejectLook('left'));
      });
    },

    /** Ask one player, by the opaque id off their lobby row. */
    challenge(id) {
      cancel();
      if (disposed) return Promise.reject(new Error('cancelled'));
      if (!id) return Promise.reject(new Error('left'));
      return new Promise((resolve, reject) => {
        pending = { kind: 'challenge', challengeId: null, resolve, reject, settled: false };
        entered = true;
        if (!handle) handle = setT(tick, pollMs);
        (async () => {
          const res = await api.challenge(String(id), tc);
          if (disposed || !pending || pending.settled || pending.kind !== 'challenge') return;
          // He is not there any more, or the server refused the pairing. The
          // door plays its refusal on exactly this word.
          if (!res.ok) { rejectLook('left'); return; }
          const direct = res.data.match_id || res.data.matchId;
          if (direct) {
            const m = await matchFrom(direct, res.data.color, null);
            if (!disposed) resolveLook(announce(m));
            return;
          }
          const cid = res.data.challenge_id || res.data.challengeId || null;
          if (!cid) { rejectLook('left'); return; }
          pending.challengeId = String(cid);
          // ...and the poll watches for him to say yes.
        })().catch(() => rejectLook('left'));
      });
    },

    cancel,

    dispose() {
      disposed = true;
      stopPoll();
      cancel();
      entered = false;
      listListeners.clear();
      challengeListeners.clear();
      if (signedIn()) api.lobbyLeave().catch(() => {});
    },

    /** Matches the mock's `debug` block, so a harness can poke either one. */
    debug: {
      isMock: false,
      state: () => ({ entered, looking: pending ? pending.kind : null, people: list(), timeControl: tc }),
      refresh,
    },
  };
}
