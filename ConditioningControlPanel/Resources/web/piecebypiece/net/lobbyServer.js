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
 *   tables()          -> Promise<{ open, playing }>   browse; never lists me
 *   onTables(fn)      -> off     polls while anyone watches
 *   join(id)          -> Promise<Match>   rejects 'gone' / 'self' / 'blocked' / 'left'
 *                       (a 409 in_match resumes that game instead)
 *   host(opts)        -> Promise<Match>   list me, wait for someone to sit down
 *
 *   Match   = { id, opponent: { id, name }, side: 'w' | 'b', clockMs }
 *   open    = [{ id, name, waitingSince, rating, timeControl }]   oldest first
 *   playing = [{ white, black, timeControl, startedMs, moves, matchId, watchable, watchers }]   names only
 *
 * BROWSING IS NOT SITTING. Watching the open tables reads GET /lobby and never
 * posts /lobby/enter, so a player looking at the list is not on it. Only
 * host() lists the player; while it waits, the same poll reads `matched`, the
 * server's word that somebody sat down at our table.
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
 * The contract asks for ~2s while a lobby screen is open, but every tick of
 * this poll spends a call on GET /lobby, which is capped at 30/min/user - a
 * 2s tick sits EXACTLY on that cap, and enter()'s own refresh and the 60s
 * re-enter push it over. 3s is 20/min, which leaves room for both, and the
 * list going stale by one extra second is not something a person in a lobby
 * can see. /quick (40/min) and /challenges (30/min, and asked for only every
 * CHALLENGE_TICKS-th tick) both sit comfortably under this.
 */
export const POLL_MS = 3000;

/**
 * The challenge list is asked for on every this-many-th tick. Incoming
 * challenges live 300s and an ignored one ignores itself after 8s, so seeing
 * one 6s late costs nothing; the outgoing challenge we are waiting on is the
 * exception and is watched every tick (see refresh), which is where the
 * contract's "~2s while a challenge screen is open" is actually needed.
 */
export const CHALLENGE_TICKS = 2;

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
  const tableListeners = new Set();

  let tc = timeControl || DEFAULT_TIME_CONTROL;
  let entered = false;
  /** We posted /lobby/enter (enter or host) and have not stood up: the listing needs renewing. */
  let listed = false;
  let disposed = false;
  let handle = null;
  let lastEnter = 0;
  let people = [];
  let playing = [];
  /**
   * Match ids a `matched` read has already handed over. The server keeps
   * answering `matched` for as long as the match is live, so a repeat of the
   * same id deals nothing twice.
   */
  const seenMatched = new Set();
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
   * A `playing` row: two names and a count. With spectating on server-side a
   * watchable game also carries its `match_id` (and only a watchable one does),
   * which is all the Watch button needs; no player id ever comes with it.
   */
  function toPlaying(p) {
    const name = (v) => String((v && typeof v === 'object' ? (v.display_name || v.name) : v) || 'someone');
    const matchId = (p.match_id || p.matchId) ? String(p.match_id || p.matchId) : null;
    return {
      white: name(p.white),
      black: name(p.black),
      timeControl: p.time_control || null,
      startedMs: Number(p.started_ms) || 0,
      moves: Math.max(0, Number(p.moves) || 0),
      matchId,
      watchable: p.watchable === true && !!matchId,
      watchers: Math.max(0, Math.trunc(Number(p.watchers) || 0)),
    };
  }

  function tablesSnapshot() {
    return { open: list(), playing: playing.map((p) => Object.assign({}, p)) };
  }

  /** Take one GET /lobby answer in. Answers whether it was one. */
  function absorb(res) {
    if (!(res && res.ok && res.data && Array.isArray(res.data.players))) return false;
    people = res.data.players.filter((p) => p && p.self !== true).map(toRow);
    playing = Array.isArray(res.data.playing) ? res.data.playing.filter(Boolean).slice(0, 30).map(toPlaying) : [];
    return true;
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
  async function matchFrom(matchId, colorHint, opponentHint, strict = false) {
    const res = await api.match(matchId);
    if (res.ok && res.data) {
      const d = res.data;
      if (strict && (!['w', 'b'].includes(d.you) || d.status === 'done' || d.result)) throw new Error('not ready');
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
    if (strict) throw new Error(res.error || 'not ready');
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

  /** The offer resolves only once the server has accepted and assigned a seat. */
  function offer(row) {
    const challengeId = String(row.challenge_id || row.challengeId || '');
    const from = row.from || {};
    let done = false;
    return {
      id: String(from.id || challengeId),
      name: String(from.display_name || from.name || 'someone'),
      challengeId,
      timeControl: row.time_control || null,
      async accept() {
        if (done || disposed) return null;
        done = true;
        const res = await api.acceptChallenge(challengeId);
        if (!res.ok) throw new Error(res.error || 'left');
        const id = res.data.match_id || res.data.matchId;
        if (!id) throw new Error('not ready');
        const match = await matchFrom(id, res.data.color, from, true);
        return disposed ? null : announce(match);
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

  /** Polls made so far, for the every-other-tick cadence. A forced refresh does not count. */
  let polls = 0;

  /**
   * One poll. `everything` asks for the challenge list whatever the cadence
   * says - what debug.refresh passes, so a harness that plants a challenge
   * and refreshes sees it on that refresh and not the one after.
   */
  async function refresh(everything = false) {
    if (disposed || !active()) return;

    // Keep the listing alive. /quick counts as a refresh, so this only fires
    // while the player is sitting in the lobby rather than queueing.
    const queueing = !!(pending && pending.kind === 'quick');
    if (listed && !queueing && Date.now() - lastEnter >= REENTER_MS) {
      lastEnter = Date.now();
      api.lobbyEnter(tc).catch(() => {});
    }

    // The list every tick; the challenges every CHALLENGE_TICKS-th, unless one
    // of ours is out, because the challenge list is the only place its answer
    // can arrive and a person waiting on a yes can feel every second of it.
    if (!everything) polls += 1;
    const askChallenges = everything
      || !!(pending && pending.kind === 'challenge')
      || ((polls - 1) % CHALLENGE_TICKS === 0);

    const [lob, chal] = await Promise.all([api.lobbyList(), askChallenges ? api.challenges() : Promise.resolve(null)]);
    if (disposed || !active()) return;

    if (absorb(lob)) {
      emit(listListeners, list());
      emit(tableListeners, tablesSnapshot());
      // Somebody sat down at our table. Only a host is waiting for this; the
      // quick queue hears the same pairing from /quick below.
      const mt = lob.data.matched;
      const mid = mt && (mt.match_id || mt.matchId);
      if (mid && !seenMatched.has(String(mid)) && pending && pending.kind === 'host') {
        const request = pending;
        seenMatched.add(String(mid));
        listed = false;
        try {
          const m = await matchFrom(String(mid), mt.color, null);
          if (!disposed && pending === request) resolveLook(announce(m));
        } catch (err) { if (pending === request) rejectLook('left'); }
        return;
      }
    }

    if (chal && chal.ok) {
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
        const request = pending;
        const mine = outgoing.find((r) => String(r.challenge_id || r.challengeId || '') === request.challengeId);
        if (mine && (mine.match_id || mine.matchId)) {
          const hint = mine.to || null;
          const id = mine.match_id || mine.matchId;
          try {
            if (id === request.previousMatchId) throw new Error('not ready');
            const m = await matchFrom(id, mine.color, hint, !!request.previousMatchId);
            if (!disposed && pending === request) resolveLook(announce(m));
          } catch (err) {
            if (pending === request) rejectLook(err.message || 'left');
          }
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
      .then(() => { if (!disposed && active()) handle = setT(tick, pollMs); });
  }

  function stopPoll() { if (handle) { clearT(handle); handle = null; } }

  /**
   * Is anybody asking for the poll? A look or a listing, or a screen watching
   * the tables or waiting on a challenge. The tick stops on its own once
   * nobody is, so an unsubscribe needs no bookkeeping.
   */
  function active() { return entered || listed || tableListeners.size > 0 || challengeListeners.size > 0; }

  /** Start the poll now if it is not running; the first tick lands on the next turn. */
  function kick() { if (!disposed && !handle && active()) handle = setT(tick, 0); }

  /* ------------------------------------------------------------- the doors */

  function list() { return people.map((p) => Object.assign({}, p)).sort((a, b) => a.waitingSince - b.waitingSince); }

  function cancel() {
    // A challenge we are cancelling has to be withdrawn on the server too, or
    // it sits in the other player's list for its whole 300s life waiting for
    // somebody who has walked away.
    if (pending && pending.kind === 'challenge' && pending.challengeId) {
      api.declineChallenge(pending.challengeId).catch(() => {});
    }
    // Getting up from our own table takes the table away with us, and giving
    // up on quick match takes us out of the queue /quick put us in: a player
    // who is only browsing again must not be pairable.
    if (pending && ((pending.kind === 'host' && listed) || pending.kind === 'quick')) {
      listed = false;
      entered = false;
      api.lobbyLeave().catch(() => {});
      rejectLook('cancelled');
      return true;
    }
    rejectLook('cancelled');
    return false;
  }

  /** The challenge flow behind challenge() and join()'s old-server fallback. */
  function challengeImpl(id, options, resolve, reject) {
    const request = pending = { kind: 'challenge', challengeId: null, resolve, reject, settled: false, previousMatchId: options.previousMatchId || null };
    entered = true;
    kick();
    (async () => {
      // Same wait as quickMatch, for the same reason.
      await whenIdentity();
      if (disposed || pending !== request || request.settled) return;
      if (!signedIn()) { rejectLook('left'); return; }
      const res = await api.challenge(String(id), options.timeControl || tc, options.color);
      if (disposed || pending !== request || request.settled) return;
      // He is not there any more, or the server refused the pairing. The
      // door plays its refusal on exactly this word.
      if (!res.ok) { rejectLook('left'); return; }
      const direct = res.data.match_id || res.data.matchId;
      if (direct) {
        if (direct === request.previousMatchId) throw new Error('not ready');
        const m = await matchFrom(direct, res.data.color, null, !!request.previousMatchId);
        if (!disposed && pending === request) resolveLook(announce(m));
        return;
      }
      const cid = res.data.challenge_id || res.data.challengeId || null;
      if (!cid) { rejectLook('left'); return; }
      request.challengeId = String(cid);
      // The friends drawer sends this id to the friend as an invite.
      if (typeof options.onChallengeId === 'function') { try { options.onChallengeId(request.challengeId); } catch { /* the wait goes on */ } }
      // ...and the poll watches for him to say yes.
    })().catch((err) => { if (pending === request) rejectLook(err.message || 'left'); });
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
      listed = true;
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
      // Anything but browsing may have put us on the server's list (a table,
      // the quick queue, a challenge's poll), so only browsing skips the goodbye.
      const wasListed = listed || entered;
      listed = false;
      entered = false;
      const said = cancel();
      if (!active()) stopPoll();
      people = [];
      playing = [];
      offered.clear();
      refused.clear();
      // A player who only browsed never sat down, so there is nothing to stand up from.
      if (wasListed && !said && signedIn()) api.lobbyLeave().catch(() => {});
    },

    async list() {
      if (disposed) return [];
      absorb(await api.lobbyList());
      return list();
    },

    /** The open tables and the games on right now. Reads only; never lists the player. */
    async tables() {
      if (disposed) return { open: [], playing: [] };
      await whenIdentity();
      if (disposed || !signedIn()) return { open: [], playing: [] };
      if (absorb(await api.lobbyList())) emit(listListeners, list());
      return tablesSnapshot();
    },

    onTables(fn) {
      if (typeof fn !== 'function') return () => {};
      tableListeners.add(fn);
      kick();
      return () => tableListeners.delete(fn);
    },

    /**
     * Sit at one open table, by the `p_` id off its row. The table's time
     * control is the game's. An older server with no /join answers a bare 404,
     * and then this is a challenge to the same row, which is what it was before.
     */
    join(id) {
      cancel();
      if (disposed) return Promise.reject(new Error('cancelled'));
      if (!id) return Promise.reject(new Error('gone'));
      const row = people.find((p) => p.id === String(id)) || null;
      return new Promise((resolve, reject) => {
        const request = pending = { kind: 'join', challengeId: null, resolve, reject, settled: false };
        (async () => {
          await whenIdentity();
          if (disposed || pending !== request || request.settled) return;
          if (!signedIn()) { rejectLook('left'); return; }
          const res = await api.join(String(id));
          if (disposed || pending !== request || request.settled) return;
          // Already in a live game (409 in_match): that game is the answer, not a refusal.
          const live = !res.ok && res.serverError === 'in_match' && res.data && (res.data.match_id || res.data.matchId);
          if (live) {
            const m = await matchFrom(String(live), res.data.color, null);
            if (!disposed && pending === request) resolveLook(announce(m));
            return;
          }
          if (!res.ok) {
            if (res.error === 'not_deployed') {
              pending = null;
              challengeImpl(String(id), {}, resolve, reject);
              return;
            }
            const word = res.serverError === 'self' ? 'self'
              : res.serverError === 'blocked' ? 'blocked'
                : (res.serverError === 'table_gone' || res.serverError === 'invalid_target' || res.status === 409) ? 'gone' : 'left';
            rejectLook(word);
            return;
          }
          const mid = res.data.match_id || res.data.matchId;
          if (!mid) { rejectLook('gone'); return; }
          const m = await matchFrom(String(mid), res.data.color, row ? { id: row.id, name: row.name } : null);
          if (!disposed && pending === request) resolveLook(announce(m));
        })().catch(() => { if (pending === request) rejectLook('left'); });
      });
    },

    /**
     * Host a table: list the player with a time control and wait for somebody
     * to sit down. Resolves with the Match; rejects 'cancelled' when the player
     * gets up (cancel() takes the listing down with it).
     */
    host(opts = {}) {
      cancel();
      if (disposed) return Promise.reject(new Error('cancelled'));
      if (opts && opts.timeControl) tc = opts.timeControl;
      return new Promise((resolve, reject) => {
        const request = pending = { kind: 'host', challengeId: null, resolve, reject, settled: false };
        (async () => {
          await whenIdentity();
          if (disposed || pending !== request || request.settled) return;
          if (!signedIn()) { rejectLook('left'); return; }
          const res = await api.lobbyEnter(tc);
          if (disposed || pending !== request || request.settled) return;
          if (!res.ok) { rejectLook('left'); return; }
          listed = true;
          entered = true;
          lastEnter = Date.now();
          kick();
        })().catch(() => { if (pending === request) rejectLook('left'); });
      });
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
          // The door may press QUICK MATCH before the host's identity frame
          // has landed - only enter() waited for it, and the menu's own quick
          // button never went through enter(). A /quick with no unified_id on
          // it is a 400, read here as "he left", which is a lie.
          await whenIdentity();
          if (disposed || !pending || pending.settled || pending.kind !== 'quick') return;
          if (!signedIn()) { rejectLook('left'); return; }
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
    challenge(id, options = {}) {
      cancel();
      if (disposed) return Promise.reject(new Error('cancelled'));
      if (!id) return Promise.reject(new Error('left'));
      return new Promise((resolve, reject) => challengeImpl(id, options, resolve, reject));
    },

    /**
     * Take up one challenge by its id, the way a friends-drawer invite hands it over. Same
     * answer as accepting an offer: a Match once the server has seated us.
     */
    acceptChallenge(challengeId) {
      if (disposed) return Promise.reject(new Error('cancelled'));
      if (!/^c_[0-9a-f]{16}$/.test(String(challengeId || ''))) return Promise.reject(new Error('left'));
      return (async () => {
        await whenIdentity();
        if (disposed) throw new Error('cancelled');
        if (!signedIn()) throw new Error('left');
        offered.add(String(challengeId));   // the poll must not offer it a second time
        const m = await offer({ challenge_id: challengeId, from: {} }).accept();
        if (!m) throw new Error('cancelled');
        return m;
      })();
    },

    cancel,

    dispose() {
      disposed = true;
      stopPoll();
      const wasListed = listed || entered;
      listed = false;
      const said = cancel();
      entered = false;
      listed = false;
      listListeners.clear();
      challengeListeners.clear();
      tableListeners.clear();
      if (wasListed && !said && signedIn()) api.lobbyLeave().catch(() => {});
    },

    /** Matches the mock's `debug` block, so a harness can poke either one. */
    debug: {
      isMock: false,
      state: () => ({ entered, listed, looking: pending ? pending.kind : null, people: list(), playing: playing.length, timeControl: tc, polls }),
      /** A full poll, challenges included, off the cadence - see refresh. */
      refresh: () => refresh(true),
    },
  };
}
