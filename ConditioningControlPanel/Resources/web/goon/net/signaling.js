// Client for the /v2/goon/* surface — port of Services/GoonGame/GoonSignalingClient.cs.
// The contract (request/response JSON per endpoint) is documented in that file's header and in
// docs/GOON_GAME_PROTOCOL.md; this module is only its JS binding.
//
// TRANSPORT: bridge.postNet(path, body) -> Promise<{status, body}>. It NEVER rejects (status 0 =
// "never got an answer"), and it owns the viaHost-vs-direct decision and the auth headers, so the
// only thing layered here is the goon error map.
//
// FAILURE MODEL (unchanged from C#): every method resolves null on failure and leaves a
// machine-readable reason in `lastError` (+ `lastErrorDetail`, `retryAfterSeconds`). Nothing here
// throws for a server outcome — the lobby has to tell "your weekly pass is spent" (a product
// message) apart from "the endpoint 404s" (server not deployed) apart from "you're offline".
// `lastErrorInfo` is the {kind, detail, retryAfterSeconds} triple the UI renders from.

import { postNet } from '../bridge.js';
import { GoonConsts } from '../core/contracts.js';

export const GOON_PATHS = Object.freeze({
  invite: '/v2/goon/invite',
  join: '/v2/goon/join',
  leave: '/v2/goon/leave',
  signal: '/v2/goon/signal',
  relay: '/v2/goon/relay',
});

/** Machine-readable `lastError` values. Mirrors GoonSignalingClient's Error* constants. */
export const GoonSignalError = Object.freeze({
  /** The endpoint answered 404 with no room context, i.e. the server route isn't deployed. */
  NotDeployed: 'not_deployed',
  NoPass: 'no_pass',
  UnknownCode: 'unknown_code',
  AlreadyJoined: 'already_joined',
  /** The code you typed is your OWN room — one account cannot sit on both seats. */
  SelfJoin: 'self_join',
  Expired: 'expired',
  Unauthorized: 'unauthorized',
  RateLimited: 'rate_limited',
  Network: 'network',
  Malformed: 'malformed_response',
});

/**
 * Invite codes are crockford32 and case-insensitive; normalize before it hits the wire.
 * Trim + upper + strip dashes and spaces — exactly GoonSignalingClient.NormalizeCode. It does NOT
 * fold Crockford's I/L -> 1 or O -> 0: the server mints the alphabet, and a client that silently
 * rewrote a character would turn "you typed it wrong" into "the room doesn't exist". If that
 * folding is ever wanted it has to land on BOTH sides at once.
 */
export function normalizeCode(code) {
  return String(code ?? '').trim().toUpperCase().replace(/[-\s]/g, '');
}

function sanitize(s, max) {
  const v = String(s ?? '').trim();
  if (!v) return '';
  return v.length <= max ? v : v.slice(0, max);
}

function intOr(v, d) { return Number.isFinite(v) ? Math.trunc(v) : d; }

export class GoonSignalingClient {
  /**
   * @param {object} [o]
   * @param {(path:string, body:object)=>Promise<{status:number, body:string}>} [o.post]
   *   Swap the HTTP layer out — GoonFakeSignalingServer uses this for offline development and the
   *   two-instance play-test harness. Defaults to bridge.postNet.
   * @param {string} [o.unifiedId] identity for the request body (boot's session.identity)
   * @param {string} [o.appVersion]
   * @param {string} [o.displayName] default display name for createInvite/join
   * @param {object} [o.logger]
   */
  constructor({ post = null, unifiedId = '', appVersion = '', displayName = '', logger = null } = {}) {
    this._post = typeof post === 'function' ? post : postNet;
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this.unifiedId = String(unifiedId || '');
    this.appVersion = String(appVersion || '');
    this.displayName = String(displayName || '');

    /** @type {string|null} machine-readable reason for the last failure */
    this.lastError = null;
    /** @type {string|null} extra detail, e.g. the next weekly-pass reset instant */
    this.lastErrorDetail = null;
    /** @type {number|null} seconds the server asked us to wait, when lastError is rate_limited */
    this.retryAfterSeconds = null;

    /**
     * THE PEER'S CARD VERSION (GOON_DISCORD_CONTRACT §3). Both /join (guest) and
     * every /signal poll (host) may carry `peer_card_ver`; null means the peer
     * shares nothing. It is an OPAQUE string — never an id, never a URL — and
     * its only job is to say "what you fetched last time is stale".
     *
     * It lives here because this client is the one thing that parses both
     * responses. ui/discord.js subscribes and decides whether to ask the host
     * for the card; this file never fetches anything itself and knows nothing
     * about avatars.
     * @type {string|null}
     */
    this.peerCardVer = null;
    this._peerCardListeners = new Set();

    this._disposed = false;
  }

  /**
   * Fires with the peer's card version whenever a response carries a non-null
   * one. Repeats are NOT filtered here — the subscriber (ui/discord.js) owns
   * the "have I already fetched this version" decision, because it is the side
   * that knows whether the last fetch actually landed.
   * @param {(ver:string) => void} fn
   * @returns {() => void} unsubscribe
   */
  onPeerCard(fn) {
    if (typeof fn !== 'function') return () => {};
    this._peerCardListeners.add(fn);
    return () => this._peerCardListeners.delete(fn);
  }

  /** Records `peer_card_ver` off a parsed response and notifies subscribers. */
  _notePeerCard(json) {
    const raw = json && typeof json.peer_card_ver === 'string' ? json.peer_card_ver : null;
    if (!raw) return null;
    this.peerCardVer = raw;
    for (const fn of Array.from(this._peerCardListeners)) {
      // A UI listener must never be able to break a signaling poll.
      try { fn(raw); } catch (e) { this._warn(`peer-card listener threw: ${(e && e.message) || e}`); }
    }
    return raw;
  }

  /** The shape the lobby UI consumes. `kind` is one of GoonSignalError (or `http_<status>`). */
  get lastErrorInfo() {
    return this.lastError
      ? { kind: this.lastError, detail: this.lastErrorDetail, retryAfterSeconds: this.retryAfterSeconds }
      : null;
  }

  /** Called by boot once `init` lands. */
  setIdentity({ unifiedId, appVersion, displayName } = {}) {
    if (unifiedId !== undefined) this.unifiedId = String(unifiedId || '');
    if (appVersion !== undefined) this.appVersion = String(appVersion || '');
    if (displayName !== undefined) this.displayName = String(displayName || '');
    return this;
  }

  // ------------------------------------------------------------------ endpoints

  /** @returns {Promise<{code, token, expiresInSec, pass, relayAllowed}|null>} */
  async createInvite(displayName = null, preferRelay = false) {
    const json = await this._call(GOON_PATHS.invite, {
      unified_id: this.unifiedId,
      display_name: sanitize(displayName ?? this.displayName, 32),
      app_version: this.appVersion,
      protocol_version: GoonConsts.ProtocolVersion,
      prefer_relay: !!preferRelay,
    });
    if (!json) return null;

    const code = typeof json.code === 'string' ? json.code : '';
    const token = typeof json.token === 'string' ? json.token : '';
    if (!code || !token) {
      this.lastError = GoonSignalError.Malformed;
      this._warn('/invite returned no code/token');
      return null;
    }

    return {
      code,
      token,
      expiresInSec: intOr(json.expires_in_sec, 300),
      pass: typeof json.pass === 'string' ? json.pass : '',
      relayAllowed: json.relay_allowed === undefined ? true : !!json.relay_allowed,
    };
  }

  /** @returns {Promise<{token, expiresInSec, peerDisplayName, peerAppVersion, pass, relayAllowed}|null>} */
  async join(code, displayName = null) {
    const json = await this._call(GOON_PATHS.join, {
      unified_id: this.unifiedId,
      code: normalizeCode(code),
      display_name: sanitize(displayName ?? this.displayName, 32),
      app_version: this.appVersion,
      protocol_version: GoonConsts.ProtocolVersion,
    });
    if (!json) return null;

    const token = typeof json.token === 'string' ? json.token : '';
    if (!token) {
      this.lastError = GoonSignalError.Malformed;
      return null;
    }

    return {
      token,
      expiresInSec: intOr(json.expires_in_sec, 300),
      peerDisplayName: typeof json.peer_display_name === 'string' ? json.peer_display_name : '',
      peerAppVersion: typeof json.peer_app_version === 'string' ? json.peer_app_version : '',
      pass: typeof json.pass === 'string' ? json.pass : '',
      relayAllowed: json.relay_allowed === undefined ? true : !!json.relay_allowed,
      /** §3: the guest learns the host's card version on the join response. */
      peerCardVer: this._notePeerCard(json),
      /** True when the server handed back a seat this uid already held. */
      rejoin: json.rejoin === true,
    };
  }

  /**
   * Best-effort seat release. A guest hands its seat back so a retry is not met
   * by its own GHOST ("that room already has two players" for the rest of the
   * room TTL); a host folds the lobby it is walking away from.
   *
   * FIRE-AND-FORGET BY CONTRACT. It resolves true/false and never throws, the
   * caller must not await it on a teardown path, and the server treats it as
   * advisory — the room TTL was always the real backstop and still is. It is
   * also refused (409 match_started) once the match has reached the ledger,
   * because releasing the seat invalidates the very token the result row needs;
   * callers must only send it for a room that never connected.
   * @returns {Promise<boolean>} true if the server acknowledged the release
   */
  async leave(code, token, role) {
    // A goodbye must never overwrite the diagnosis the lobby is about to render:
    // this runs on teardown paths that are ALREADY holding the reason they folded
    // (`ice_timeout`, `already_joined`, …) in exactly these three fields.
    const prev = { e: this.lastError, d: this.lastErrorDetail, r: this.retryAfterSeconds };
    const json = await this._call(GOON_PATHS.leave, {
      unified_id: this.unifiedId,
      code: normalizeCode(code),
      token: token || '',
      role,
    });
    this.lastError = prev.e;
    this.lastErrorDetail = prev.d;
    this.retryAfterSeconds = prev.r;
    return !!json;
  }

  /**
   * Post-and-drain against the SDP/ICE mailbox. One call per short-poll tick.
   * @param {Array<{kind:string, data:string}>} [outgoing]
   * @returns {Promise<{cursor, messages, peerJoined, peerGone}|null>}
   */
  async signal(code, token, role, since, outgoing) {
    const msgs = (outgoing || []).map((m) => ({ kind: m.kind || '', data: m.data || '' }));
    const json = await this._call(GOON_PATHS.signal, {
      unified_id: this.unifiedId,
      code: normalizeCode(code),
      token: token || '',
      role,
      since,
      msgs,
    });
    if (!json) return null;

    const messages = [];
    if (Array.isArray(json.msgs)) {
      for (const m of json.msgs) {
        if (!m || typeof m !== 'object') continue;
        messages.push({
          seq: intOr(m.seq, 0),
          kind: typeof m.kind === 'string' ? m.kind : '',
          data: typeof m.data === 'string' ? m.data : '',
          from: typeof m.from === 'string' ? m.from : '',
        });
      }
    }

    return {
      cursor: intOr(json.cursor, since),
      messages,
      peerJoined: !!json.peer_joined,
      peerGone: !!json.peer_gone,
      /** §3: the host learns the guest's card version next to `peer_joined`. */
      peerCardVer: this._notePeerCard(json),
    };
  }

  /**
   * Post-and-drain against the relay mailbox. `waitMs` is the long-poll hint the server may hold
   * the request for.
   * @param {string[]} [outgoing] whole GoonMessage frames
   * @returns {Promise<{cursor, frames, peerOnline}|null>}
   */
  async relay(code, token, role, since, outgoing, waitMs) {
    const json = await this._call(GOON_PATHS.relay, {
      unified_id: this.unifiedId,
      code: normalizeCode(code),
      token: token || '',
      role,
      since,
      wait_ms: waitMs,
      msgs: outgoing || [],
    });
    if (!json) return null;

    const frames = [];
    if (Array.isArray(json.msgs)) {
      for (const m of json.msgs) {
        const data = m && typeof m === 'object' ? m.data : null;
        if (typeof data === 'string' && data !== '') frames.push(data);
      }
    }

    return {
      cursor: intOr(json.cursor, since),
      frames,
      peerOnline: !!json.peer_online,
    };
  }

  // ------------------------------------------------------------------ plumbing

  /**
   * POST + status triage. Resolves the parsed body on 2xx, null otherwise with `lastError` set.
   * Never rejects for a server outcome.
   */
  async _call(path, body) {
    this.lastError = null;
    this.lastErrorDetail = null;
    this.retryAfterSeconds = null;

    if (this._disposed) {
      this.lastError = GoonSignalError.Network;
      return null;
    }

    let res;
    try {
      res = await this._post(path, body);
    } catch (e) {
      // postNet does not reject, but an injected post might.
      this.lastError = GoonSignalError.Network;
      this._warn(`${path} transport failure: ${(e && e.message) || e}`);
      return null;
    }

    const status = res && Number.isFinite(res.status) ? res.status | 0 : 0;
    const raw = res && typeof res.body === 'string' ? res.body : '';

    if (status === 0) {
      // bridge.postNet's "never got an answer" code: host gone, offline, or timed out.
      this.lastError = GoonSignalError.Network;
      this._warn(`${path} -> no answer`);
      return null;
    }

    let json = null;
    if (raw.trim() !== '') {
      try { json = JSON.parse(raw); } catch (_e) { /* non-JSON body — the status still tells us enough */ }
      if (json !== null && (typeof json !== 'object' || Array.isArray(json))) json = null;
    }

    if (status >= 200 && status < 300) {
      if (json === null) {
        this.lastError = GoonSignalError.Malformed;
        return null;
      }
      return json;
    }

    const serverError = json && typeof json.error === 'string' ? json.error : null;

    switch (status) {
      case 401:
      case 403:
        this.lastError = serverError || GoonSignalError.Unauthorized;
        break;
      case 402:
        this.lastError = serverError || GoonSignalError.NoPass;
        this.lastErrorDetail = json && typeof json.next_pass_utc === 'string' ? json.next_pass_utc : null;
        break;
      case 404:
        // A 404 with no error body means the route itself isn't there — the server hasn't
        // deployed. The lobby says "Goon Game is warming up", not "bad code".
        this.lastError = serverError || GoonSignalError.NotDeployed;
        break;
      case 409:
        this.lastError = serverError || GoonSignalError.AlreadyJoined;
        break;
      case 429:
        this.lastError = GoonSignalError.RateLimited;
        this.retryAfterSeconds = json && Number.isFinite(json.retry_after_seconds)
          ? json.retry_after_seconds | 0 : null;
        break;
      default:
        this.lastError = serverError || `http_${status}`;
        break;
    }

    this._warn(`${path} -> ${status} (${this.lastError})`);
    return null;
  }

  dispose() {
    this._disposed = true;
    this._peerCardListeners.clear();
  }

  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[GoonSignal] ${m}`); }
}
