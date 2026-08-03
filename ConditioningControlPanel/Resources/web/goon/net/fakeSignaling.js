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
//   * room TTL (the C# fake has no clock; this one does, so an "expired" path is testable).
//
// Long-poll: the fake answers instantly and the caller's cadence timer paces it, same as C#.

import { GoonSignalError } from './signaling.js';

const DEFAULT_TTL_MS = 300000;   // ~5 min, the documented room TTL

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
   */
  constructor({ ttlMs = DEFAULT_TTL_MS, now = () => Date.now(), codePrefix = 'LOOP' } = {}) {
    this._rooms = new Map();
    this._ttlMs = ttlMs;
    this._now = now;
    this._codePrefix = codePrefix;
    this._codeCounter = 0;
    /** Every request the server saw — handy in an assertion. */
    this.requests = [];
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
        if (room.joined) return fail(409, GoonSignalError.AlreadyJoined);
        room.joined = true;
        return ok({
          ok: true,
          token: room.guestToken,
          role: 'guest',
          expires_in_sec: Math.max(0, Math.round((room.expiresAt - this._now()) / 1000)),
          peer_display_name: 'host',
          peer_app_version: 'fake',
          pass: 'premium',
          relay_allowed: true,
        });
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

        return ok({ ok: true, cursor, msgs: mine, peer_joined: room.joined, peer_gone: false });
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
