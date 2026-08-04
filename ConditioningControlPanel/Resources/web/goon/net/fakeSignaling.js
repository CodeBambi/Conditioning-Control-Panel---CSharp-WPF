// In-memory /v2/goon/* server — port of GoonFakeSignalingServer (bottom of GoonSignalingClient.cs).
//
// Two purposes. It lets the real transports be exercised end-to-end with no server (both peers in
// one page, or two tabs sharing one of these), and — because it is executable — it pins the
// contract the real server has to match. If the two ever disagree, one of them is a bug.
//
// It deliberately does NOT model auth, the weekly pass, or rate limiting: those are server policy,
// and faking them here would only teach the client to trust a fake.
//
// Server rules that ARE modelled, because clients depend on them:
//   * append-only per-room list, `since` is an EXCLUSIVE cursor;
//   * a caller never receives its own messages back (no self-echo) — but the cursor still advances
//     PAST them, so the next poll doesn't re-walk the whole list;
//   * /join marks the room joined, which is what the host's next /signal sees as peer_joined;
//   * SEAT IDENTITY: the seat belongs to a uid. The same uid rejoining RECLAIMS it (fresh token,
//     stale SDP dropped), a different uid is refused `already_joined`, and the host's own uid is
//     refused `self_join`. This is not auth — it is the rule that decides whether a client can get
//     back into the room it just fell out of, and a fake that got it wrong would teach the client
//     that a ghost seat is normal;
//   * SELF-DUEL: a WHITELISTED uid is the one exception to `self_join` — it may hold both seats, and
//     its guest seat is stored under the shadow id `<uid>#self`. Modelled because it is seat
//     identity, and because a fake that refused it would make the owner's two-device play-test
//     (one account, PC hosts, phone joins) look like a client bug. `whitelist`/`setWhitelisted`
//     stand in for the server's user record — the fake models WHO may, never HOW it is proven;
//   * /leave hands the seat back (guest) or folds the room (host), so a fold is testable without
//     waiting out a TTL;
//   * room TTL (the C# fake has no clock; this one does, so an "expired" path is testable).
//
// Long-poll: the fake answers instantly and the caller's cadence timer paces it, same as C#.

import { GoonSignalError } from './signaling.js';

const DEFAULT_TTL_MS = 300000;   // ~5 min, the documented room TTL

/**
 * A self-duel's guest seat id. Unified ids are `u_[a-z0-9]{8,24}` server-side, so the '#' can never
 * collide with — or be mistaken for — a real account, which is the whole point: every uid-keyed
 * thing the real server writes (pair record, ledger, report attribution) then sees two identities.
 */
const SELF_SUFFIX = '#self';
export const shadowGuestUid = (uid) => `${uid}${SELF_SUFFIX}`;
export const isShadowUid = (uid) => typeof uid === 'string' && uid.endsWith(SELF_SUFFIX);

function ok(body) { return Promise.resolve({ status: 200, body: JSON.stringify(body) }); }
function fail(status, error) { return Promise.resolve({ status, body: JSON.stringify({ error }) }); }

/** Accepts a bare path or a full URL (the C# fake is handed a URL). */
function pathOf(p) {
  const s = String(p || '');
  const i = s.indexOf('://');
  if (i < 0) return s.split('?')[0];
  const slash = s.indexOf('/', i + 3);
  return slash < 0 ? '/' : s.slice(slash).split('?')[0];
}

let tokenCounter = 0;
function newToken() { return `tok${(++tokenCounter).toString(36)}${Math.random().toString(36).slice(2, 10)}`; }

export class GoonFakeSignalingServer {
  /**
   * @param {object} [o]
   * @param {number} [o.ttlMs] room lifetime
   * @param {() => number} [o.now] injectable clock (ms) — tests expire rooms without waiting
   * @param {string} [o.codePrefix]
   * @param {string[]} [o.whitelist] uids allowed to self-duel (see setWhitelisted)
   */
  constructor({ ttlMs = DEFAULT_TTL_MS, now = () => Date.now(), codePrefix = 'LOOP', whitelist = [] } = {}) {
    this._rooms = new Map();
    this._ttlMs = ttlMs;
    this._now = now;
    this._codePrefix = codePrefix;
    this._codeCounter = 0;
    /**
     * Uids that may hold BOTH seats of their own room. The real server reads this off the user
     * record (`patreon_is_whitelisted` / `substar_is_whitelisted` — whitelist, NOT tier: a paying
     * tier-2 patron is refused, because a self-duel is a ledger/self-report surface and only the
     * hand-maintained list is trusted with it). Here it is just a set a test can fill.
     * @type {Set<string>}
     */
    this._whitelist = new Set(Array.isArray(whitelist) ? whitelist.map(String) : []);
    /** Every request the server saw — handy in an assertion. */
    this.requests = [];
    /**
     * THE PEER CARD VERSION (GOON_DISCORD_CONTRACT §3). The real server derives
     * it from the sharer's avatar hash + dm flag and hands it to the OTHER side
     * on /join and /signal; null means "this peer shares nothing", which is the
     * default here because every sharing flag defaults false.
     *
     * It is modelled — unlike auth and rate limiting — because a client
     * behaviour depends on it: ui/discord.js must ask for a card exactly once
     * per version, and there is no way to prove "exactly once" without a server
     * that can change its mind. `setPeerCardVer` is how a test does that.
     */
    this.hostCardVer = null;    // what the GUEST is told about the host
    this.guestCardVer = null;   // what the HOST is told about the guest
  }

  /**
   * Test hook: publish a card version for one side. The value is opaque to
   * everything — it only has to CHANGE when the underlying share changes.
   * @param {'host'|'guest'} role whose card moved
   */
  setPeerCardVer(role, ver) {
    const v = (typeof ver === 'string' && ver) ? ver : null;
    if (role === 'guest') this.guestCardVer = v; else this.hostCardVer = v;
    return v;
  }

  /**
   * Test hook: mark a uid privileged (or not), i.e. allowed to self-duel.
   * @param {string} uid
   * @param {boolean} [on]
   */
  setWhitelisted(uid, on = true) {
    const u = String(uid || '');
    if (!u) return this;
    if (on) this._whitelist.add(u); else this._whitelist.delete(u);
    return this;
  }

  /** Hand this to GoonSignalingClient's `post` option. */
  get post() { return (path, body) => this._handle(path, body); }

  get roomCount() { return this._rooms.size; }

  /** Test hook: force a room past its TTL without moving the clock. */
  expire(code) {
    const room = this._rooms.get(String(code || '').toUpperCase());
    if (!room) return false;
    room.expiresAt = this._now() - 1;
    return true;
  }

  /** Read-only peek for assertions. */
  room(code) { return this._rooms.get(String(code || '').toUpperCase()) || null; }

  _live(code) {
    const room = this._rooms.get(String(code || '').toUpperCase());
    if (!room) return null;
    if (room.expiresAt <= this._now()) { this._rooms.delete(room.code); return null; }
    return room;
  }

  _handle(path, body) {
    const p = pathOf(path);
    const req = (body && typeof body === 'object') ? body : {};
    this.requests.push({ path: p, body: req });

    switch (p) {
      case '/v2/goon/invite': {
        this._codeCounter++;
        const room = {
          code: `${this._codePrefix}${String(this._codeCounter).padStart(2, '0')}`,
          hostToken: newToken(),
          guestToken: newToken(),
          hostUid: typeof req.unified_id === 'string' ? req.unified_id : '',
          guestUid: null,
          guestEpoch: 0,
          joined: false,
          signals: [],          // {seq, kind, data, from}
          relay: [],            // {seq, from, data}
          signalSeq: 0,
          relaySeq: 0,
          expiresAt: this._now() + this._ttlMs,
        };
        this._rooms.set(room.code, room);
        return ok({
          ok: true,
          code: room.code,
          token: room.hostToken,
          role: 'host',
          expires_in_sec: Math.round(this._ttlMs / 1000),
          pass: 'premium',
          relay_allowed: true,
        });
      }

      case '/v2/goon/join': {
        const room = this._live(req.code);
        // A bad code and an expired code are indistinguishable to a joiner, by design.
        if (!room) return fail(404, GoonSignalError.UnknownCode);
        const uid = typeof req.unified_id === 'string' ? req.unified_id : '';
        // Your own room is not a full room. One account cannot hold both seats:
        // the ledger, the pair record and the peer card are all "the other uid".
        // …unless the account is WHITELISTED, which is the owner's two-device
        // play-test. Then it takes the SHADOW seat, so those three stay pointed at
        // two distinct identities and the exception costs the guard nothing.
        const selfDuel = !!uid && uid === room.hostUid;
        if (selfDuel && !this._whitelist.has(uid)) return fail(409, GoonSignalError.SelfJoin);
        // Which seat this caller is entitled to reclaim. A self-duel guest must match
        // the SHADOW seat, or every rejoin would re-enter the fresh-claim path.
        const seatUid = selfDuel ? shadowGuestUid(uid) : uid;
        const rejoin = room.joined && !!uid && room.guestUid === seatUid;
        if (room.joined && !rejoin) return fail(409, GoonSignalError.AlreadyJoined);
        room.joined = true;
        room.guestUid = seatUid;
        room.guestEpoch++;
        room.guestToken = newToken();     // every claim gets a fresh token
        // A reclaimed seat starts on a clean mailbox: the dead attempt's SDP would
        // otherwise be handed to the fresh peer connection as if it were live. The
        // SEQ counter deliberately keeps counting — the host's cursor is past it.
        if (rejoin) room.signals = [];
        return ok({
          ok: true,
          rejoin,
          // Advisory: both seats are one account. Nothing in the match depends on it.
          self_duel: selfDuel,
          token: room.guestToken,
          role: 'guest',
          expires_in_sec: Math.max(0, Math.round((room.expiresAt - this._now()) / 1000)),
          peer_display_name: 'host',
          peer_app_version: 'fake',
          pass: 'premium',
          relay_allowed: true,
          // The joiner is the GUEST, so the peer whose card it learns is the host.
          peer_card_ver: this.hostCardVer,
        });
      }

      case '/v2/goon/leave': {
        const room = this._live(req.code);
        if (!room) return fail(404, GoonSignalError.Expired);
        const role = typeof req.role === 'string' ? req.role : 'guest';
        const token = typeof req.token === 'string' ? req.token : '';
        if (token !== (role === 'host' ? room.hostToken : room.guestToken)) {
          return fail(401, GoonSignalError.Unauthorized);
        }
        if (role === 'host') {
          // The code dies with the person holding it.
          this._rooms.delete(room.code);
          return ok({ ok: true, folded: true });
        }
        // The seat goes back on the market; the ROOM stays up, so the same player
        // can retry (or anyone else can join) without a second code and a second
        // burned pass. This is the anti-ghost half of the 2026-08-04 fix.
        room.joined = false;
        room.guestUid = null;
        room.signals = [];
        return ok({ ok: true, folded: false });
      }

      case '/v2/goon/signal': {
        const room = this._live(req.code);
        if (!room) return fail(404, GoonSignalError.Expired);

        const role = typeof req.role === 'string' ? req.role : 'host';
        const since = Number.isFinite(req.since) ? req.since : 0;

        if (Array.isArray(req.msgs)) {
          for (const m of req.msgs) {
            if (!m || typeof m !== 'object') continue;
            room.signals.push({
              seq: ++room.signalSeq,
              kind: typeof m.kind === 'string' ? m.kind : '',
              data: typeof m.data === 'string' ? m.data : '',
              from: role,
            });
          }
        }

        const mine = [];
        let cursor = since;
        for (const s of room.signals) {
          if (s.seq <= since || s.from === role) { cursor = Math.max(cursor, s.seq); continue; }
          mine.push({ seq: s.seq, kind: s.kind, data: s.data, from: s.from });
          cursor = Math.max(cursor, s.seq);
        }

        return ok({
          ok: true,
          cursor,
          msgs: mine,
          peer_joined: room.joined,
          peer_gone: false,
          // Whose card you are told about is whoever you are NOT. Repeated on
          // every poll, exactly like the real route: de-duplication is the
          // page's job (ui/discord.js), never the wire's.
          peer_card_ver: role === 'host' ? this.guestCardVer : this.hostCardVer,
        });
      }

      case '/v2/goon/relay': {
        const room = this._live(req.code);
        if (!room) return fail(404, GoonSignalError.Expired);

        const role = typeof req.role === 'string' ? req.role : 'host';
        const since = Number.isFinite(req.since) ? req.since : 0;

        if (Array.isArray(req.msgs)) {
          for (const data of req.msgs) {
            if (typeof data === 'string' && data !== '') {
              room.relay.push({ seq: ++room.relaySeq, from: role, data });
            }
          }
        }

        const mine = [];
        let cursor = since;
        for (const f of room.relay) {
          if (f.seq <= since || f.from === role) { cursor = Math.max(cursor, f.seq); continue; }
          mine.push({ seq: f.seq, data: f.data });
          cursor = Math.max(cursor, f.seq);
        }

        return ok({ ok: true, cursor, msgs: mine, peer_online: room.joined });
      }

      default:
        return fail(404, GoonSignalError.NotDeployed);
    }
  }
}
