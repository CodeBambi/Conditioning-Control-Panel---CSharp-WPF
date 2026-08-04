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
// throws for a server outcome — the lobby has to tell "hosting is a supporter perk" (a product
// message) apart from "the endpoint 404s" (server not deployed) apart from "you're offline".
// `lastErrorInfo` is the {kind, detail, retryAfterSeconds} triple the UI renders from.
//
// ENTITLEMENT (2026-08-04). HOSTING is tier 2 — /invite answers 403 no_host_access below it — and
// JOINING is free for everyone, including people with no account at all: see `anonymous` on the
// constructor for the server-minted `g_` seat identity that carries them. The weekly free pass
// (402 no_pass) is gone; it is still PARSED, so an old server keeps producing a sentence.

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
  /** Retired with the weekly free pass. Kept so an OLD server still gets a sentence, not a code. */
  NoPass: 'no_pass',
  /** /invite refused: minting a room is a tier-2 perk. Joining stays free for everyone. */
  NoHostAccess: 'no_host_access',
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

/**
 * A server-minted anonymous seat identity: `g_` + 8 random bytes hex. Real unified ids are
 * `u_[a-z0-9]{8,24}`, so the two shapes can never be confused for one another — which is the
 * point, because this one is presented in the same `unified_id` field.
 */
const GUEST_ID_RE = /^g_[a-f0-9]{16}$/;
export const isGuestId = (v) => typeof v === 'string' && GUEST_ID_RE.test(v);

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
   * @param {boolean} [o.anonymous] this page has NO account (see `this.anonymous`)
   * @param {string} [o.guestId] a `g_` seat id kept from an earlier launch, for the rejoin
   * @param {string} [o.guestRoom] the room code that `guestId` holds a seat in
   * @param {(id:string, code:string) => void} [o.onGuest] sink that persists a minted seat id
   */
  constructor({ post = null, unifiedId = '', appVersion = '', displayName = '', logger = null,
    anonymous = false, guestId = '', guestRoom = '', onGuest = null } = {}) {
    this._post = typeof post === 'function' ? post : postNet;
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this.unifiedId = String(unifiedId || '');
    this.appVersion = String(appVersion || '');
    this.displayName = String(displayName || '');

    /**
     * ANONYMOUS JOIN (2026-08-04). True when there is no account behind this page at all — an
     * invite-link click from somebody who has never seen the app. `/join` then omits `unified_id`
     * ENTIRELY (the field's ABSENCE is the request; an empty string is a 400) and the server mints
     * a `g_` seat identity, hands it back as `guest_id`, and expects it as `unified_id` on every
     * later room-scoped call. No X-Auth-Token rides along either — a guest has no account token,
     * and the room token is the whole of its authority.
     *
     * Hosting is never anonymous: /invite is tier-2 gated and a guest has no account to be tier 2
     * with. This flag only ever changes what the JOIN path sends.
     */
    this.anonymous = !!anonymous;
    /** The `g_` seat identity, once /join has minted one (or one kept from an earlier launch). */
    this.guestId = isGuestId(guestId) ? guestId : '';
    /** The room `guestId` holds a seat in. A g_ id is only ever re-presented for ITS OWN room. */
    this.guestRoom = normalizeCode(guestRoom);
    this._onGuest = typeof onGuest === 'function' ? onGuest : null;

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

    /**
     * THE SERVER'S SEND VERDICT. /invite and /join answer with `media_send`
     * (premium = tier>=1, same bar as the in-app gate) and this records it:
     * true/false once a response carried the field, null against a server that
     * predates it. boot.js folds it into session.caps.mediaTransfer for the
     * STANDALONE page only — hosted, the C# init frame is authoritative and the
     * two verdicts are computed from the same tier anyway. null deliberately
     * changes nothing: an old server must not strip a hosted premium sender.
     * @type {boolean|null}
     */
    this.mediaSend = null;

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

  /**
   * Adopts the `guest_id` off a /join response: THE seat identity from here on.
   *
   * Writing it into `unifiedId` is what makes /signal, /relay and /leave work with no second
   * branch — every one of them already sends that field. The sink persists it so a reload can
   * re-present it and reclaim the seat instead of arriving as a stranger and being told the room
   * is full. Ignores anything that is not the documented `g_` shape.
   */
  _adoptGuestId(json, code) {
    const id = json && typeof json.guest_id === 'string' ? json.guest_id : '';
    if (!isGuestId(id)) return null;
    this.guestId = id;
    this.guestRoom = code;
    this.unifiedId = id;
    if (this._onGuest) {
      // A persistence sink must never be able to break a join that already succeeded.
      try { this._onGuest(id, code); } catch (e) { this._warn(`guest-id sink threw: ${(e && e.message) || e}`); }
    }
    return id;
  }

  /** Records `media_send` off a parsed /invite or /join response. Absent = old server = no-op. */
  _noteMediaSend(json) {
    if (json && typeof json.media_send === 'boolean') this.mediaSend = json.media_send;
    return this.mediaSend;
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
      /** The server's premium send verdict (see `this.mediaSend`); null on an old server. */
      mediaSend: this._noteMediaSend(json),
    };
  }

  /** @returns {Promise<{token, expiresInSec, peerDisplayName, peerAppVersion, pass, relayAllowed, rejoin, selfDuel, guestId}|null>} */
  async join(code, displayName = null) {
    const room = normalizeCode(code);
    const body = {
      code: room,
      display_name: sanitize(displayName ?? this.displayName, 32),
      app_version: this.appVersion,
      protocol_version: GoonConsts.ProtocolVersion,
    };

    /* THE UID GOES ON LAST, and for an anonymous joiner it may not go on at all — the ABSENCE of
     * the field is what asks the server for a seat identity, so it is set rather than blanked.
     *
     * A reload re-presents the `g_` id it was given for THIS room and reclaims the seat
     * (pass:"rejoin", fresh token). Any other room omits it and takes a fresh identity: a guest id
     * lives only as long as the room it was minted for, and carrying one across rooms would make
     * it a durable handle on a person who never signed up for one. */
    if (!this.anonymous) {
      body.unified_id = this.unifiedId;
    } else if (this.guestId && this.guestRoom === room) {
      body.unified_id = this.guestId;
      this.unifiedId = this.guestId;
    }

    const json = await this._call(GOON_PATHS.join, body);
    if (!json) return null;

    const token = typeof json.token === 'string' ? json.token : '';
    if (!token) {
      this.lastError = GoonSignalError.Malformed;
      return null;
    }

    return {
      token,
      /** The `g_` seat identity this client now presents as unified_id; '' for an account join. */
      guestId: this._adoptGuestId(json, room) || '',
      expiresInSec: intOr(json.expires_in_sec, 300),
      peerDisplayName: typeof json.peer_display_name === 'string' ? json.peer_display_name : '',
      peerAppVersion: typeof json.peer_app_version === 'string' ? json.peer_app_version : '',
      pass: typeof json.pass === 'string' ? json.pass : '',
      relayAllowed: json.relay_allowed === undefined ? true : !!json.relay_allowed,
      /** §3: the guest learns the host's card version on the join response. */
      peerCardVer: this._notePeerCard(json),
      /** True when the server handed back a seat this uid already held. */
      rejoin: json.rejoin === true,
      /**
       * True when this account is sitting in BOTH seats — the whitelist-only
       * self-duel affordance (one account, two devices, the owner's play-test).
       * Advisory only: the server put the guest seat under a shadow identity, so
       * nothing about the match, the transports or the recap differs. Surfaced
       * because a client that cannot SAY "you are duelling yourself" would leave
       * the tester guessing which device is which.
       */
      selfDuel: json.self_duel === true,
      /** The server's premium send verdict (see `this.mediaSend`); null on an old server. */
      mediaSend: this._noteMediaSend(json),
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
      // A guest has no account token to send, and sending one would be a lie about who is
      // calling. The room token in the body is the whole of its authority (see `anonymous`).
      res = await this._post(path, body, this.anonymous ? { noAuth: true } : undefined);
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
        this.lastError = serverError || GoonSignalError.Unauthorized;
        break;
      case 403:
        // THE HOST GATE. /invite answers `no_host_access` here and it is a product message, not a
        // fault — telling a tier-1 patron to reconnect a perfectly connected account (which is
        // what the bare `unauthorized` fallback says) would send them chasing the wrong problem.
        this.lastError = serverError || GoonSignalError.Unauthorized;
        break;
      case 402:
        // Retired with the weekly free pass; parsed only so an old server still gets a sentence.
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
