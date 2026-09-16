// GOON GAME — match session facade. Port of Services/GoonGame/GoonGameService.cs.
//
// Owns the per-match plumbing the individual pieces deliberately do not:
//   * ONE GoonSignalingClient per session, shared by both transports, so a relay fallback re-uses
//     the room (and the weekly pass that was already burned) instead of minting a second one;
//   * the WebRTC-first / relay-fallback dance (webrtc.iceFailed -> relay.adoptRoom ->
//     match.adoptLobby);
//   * match construction, via an INJECTED FACTORY.
//
// THE createMatch FACTORY (what boot must wire):
//     new GoonSession({ createMatch: (transport, isHost) => new GoonMatch(transport, isHost, ...) })
//   This module never imports core/match.js. That is on purpose: the fallback REBUILDS the match
//   (a match binds its transport for life), and keeping the construction in one injected closure is
//   what lets boot own everything match-shaped — the RNG factory, the sudden-death runner, the
//   presenter — without this file knowing any of it. The returned object must expose:
//     createInvite() -> Promise<string|null>     (alias createInviteAsync accepted)
//     join(code)     -> Promise<boolean>         (alias joinAsync accepted)
//     adoptLobby()   -> boolean                  (relay-fallback entry; no second /invite or /join)
//     cancelMatch(reason)                        (user-visible cancel; may send on the wire)
//     dispose()                                  (SILENT teardown; must send nothing)
//
// The rebuild is why UI must track onCurrentMatchChanged and RE-SUBSCRIBE rather than caching the
// first currentMatch instance. The rebuild window is safe: ICE can only fail in Lobby, before the
// hello/consent exchange has happened.
//
// THE FALLBACK IS ONE-WAY, AND THAT IS A DELIBERATE NON-FEATURE (investigated 2026-08-05).
// It looks tempting to keep the peer connection alive through the downgrade and "upgrade back"
// when a TURN pair finally completes — the match already survives one rebuild, why not two. It
// does not work, for two reasons that are not about effort:
//   1. A TRANSPORT SWITCH IS BILATERAL. The downgrade is safe only because BOTH peers independently
//      reach the same verdict about the same room and meet again on the mailbox. An upgrade would
//      be decided by whichever side's channel opened first, and the moment it switched it would be
//      talking into a pipe the other side is not listening to — a duel with zero frames crossing,
//      strictly worse than a relayed one. Making it safe needs a handshake ON the relay ("I have
//      P2P, switch at N"), i.e. a new protocol frame, a new phase, and its own play-test.
//   2. THE REBUILD WINDOW HAS CLOSED BY THEN. adoptLobby() only accepts a match in Idle, and it is
//      only safe at all because nothing has been said yet. A late upgrade lands after hello and
//      quite possibly after consent, and a fresh match object knows none of it.
// So the fix for "cellular duels lose their media lane" lives one layer down, in how patient the
// ICE budget is BEFORE _fallBackToRelay is ever reached — see GoonConsts.IceRelayGraceMs and
// webrtcTransport.iceBudgetMs. Anyone revisiting the upgrade idea starts at point 1, not here.
//
// This facade does NOT gate on entitlement — the server enforces the tier-2 HOST bar at
// /v2/goon/invite (403 no_host_access) and nothing at all at /join, which is free for everyone,
// account or not. UI may pre-gate for niceness (title.js dims Host on caps.canHost), never here.
//
// LEAVE vs TEARDOWN: leave() is user-initiated and routes through match.cancelMatch (in Live that
// is a Mercy). Every internal path — fallback, failure, dispose — uses dispose() only, which sends
// nothing. Getting that backwards would turn a transport hiccup into a forfeit.
//
// THE SEAT IS HANDED BACK (2026-08-04). Every teardown of a room that never connected fires a
// best-effort /v2/goon/leave (_releaseRoom). Without it, a guest whose handshake died left its uid
// sitting in the room's guest slot for the whole 30-minute TTL, and the retry it obviously makes
// next was answered with "that room already has two players" — by its own ghost. The server also
// reclaims a same-uid seat on /join now, so the two fixes cover each other: the goodbye can be
// lost (offline, closed tab, old server) and the rejoin still works.
//
// ONE /join PER USER ACTION: the entry points refuse to start while `currentMatch` is set
// (_beginSession returns null), the relay fallback re-uses the room via adoptRoom instead of
// re-joining, and the join screen holds a `busy` latch across the await. There is no path that
// issues two concurrent redemptions for one tap, and that is load-bearing — the second one would
// be answered by the first one's seat.

import { GoonSignalingClient } from './signaling.js';
import { GoonWebRtcTransport } from './webrtcTransport.js';
import { GoonRelayTransport } from './relayTransport.js';

/** Calls the first method that exists, so a mechanical `Async` suffix on the match binds too. */
function invoke(target, names, ...args) {
  for (const n of names) {
    if (target && typeof target[n] === 'function') return target[n](...args);
  }
  throw new Error(`match object exposes none of: ${names.join(', ')}`);
}

/**
 * Why did P2P give up? Two answers only, because only two matter when reading a play-test log:
 *   deadline — a stopwatch of OURS fired. The browser may well still have been making progress,
 *              so a recurring `deadline` is a signal the budget is wrong (webrtcTransport
 *              iceBudgetMs / GoonConsts.IceRelayGraceMs), not that the network is hopeless.
 *   failed   — the browser, the peer or the SDP said no. Nothing to tune; the link is unusable.
 * Anything unrecognised reads `other` rather than being guessed at.
 */
function classifyFallback(lastError) {
  switch (lastError) {
    case 'ice_timeout':
    case 'no_offer':
      return 'deadline';
    case 'ice_failed':
    case 'sdp_rejected':
    case 'peer_setup_failed':
    case 'webrtc_unavailable':
      return 'failed';
    default:
      return 'other';
  }
}

function emit(listeners, arg, logger, what) {
  for (const fn of Array.from(listeners)) {
    try { fn(arg); } catch (e) {
      if (logger && logger.error) logger.error(`[GG] ${what} handler threw: ${(e && e.message) || e}`);
    }
  }
}

export class GoonSession {
  /**
   * @param {object} o
   * @param {(transport:object, isHost:boolean) => object} o.createMatch REQUIRED — see the header
   * @param {object} [o.identity] {unifiedId, displayName, appVersion, anonymous} for the body
   * @param {object} [o.logger]
   * @param {{id?:string, code?:string, save?:(id:string, code:string)=>void}} [o.guest]
   *   the anonymous seat identity kept across reloads — see `this._guest`
   * @param {() => GoonSignalingClient} [o.createSignaling] test seam
   * @param {(isHost:boolean, signaling:GoonSignalingClient) => object} [o.createWebrtc] test seam
   * @param {(isHost:boolean, signaling:GoonSignalingClient) => object} [o.createRelay] test seam
   */
  constructor({ createMatch, identity = null, logger = null, guest = null, createSignaling = null,
    createWebrtc = null, createRelay = null } = {}) {
    if (typeof createMatch !== 'function') throw new Error('GoonSession: createMatch factory is required');

    this._createMatch = createMatch;
    this._identity = identity || {};
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    /**
     * THE ANONYMOUS SEAT, across reloads. Read at signaling-CONSTRUCTION time rather than copied
     * here, so a `save` that also updates `id`/`code` in place is what lets the next session on
     * this page reclaim the seat instead of arriving as a stranger.
     */
    this._guest = (guest && typeof guest === 'object') ? guest : null;

    this._createSignaling = createSignaling || (() => new GoonSignalingClient({
      unifiedId: this._identity.unifiedId || '',
      appVersion: this._identity.appVersion || '',
      displayName: this._identity.displayName || '',
      logger: this._log,
      /* NO ACCOUNT BEHIND THIS PAGE: /join asks the server for a seat identity rather than
       * presenting one, and re-presents the one it holds when this is the same room again. */
      anonymous: this._identity.anonymous === true,
      guestId: (this._guest && this._guest.id) || '',
      guestRoom: (this._guest && this._guest.code) || '',
      onGuest: this._guest && typeof this._guest.save === 'function'
        ? (id, code) => this._guest.save(id, code)
        : null,
    }));
    this._createWebrtc = createWebrtc || ((isHost, signaling) => new GoonWebRtcTransport({
      isHost, signaling, logger: this._log, displayName: this._identity.displayName || '',
    }));
    this._createRelay = createRelay || ((isHost, signaling) => new GoonRelayTransport({
      isHost, signaling, logger: this._log, displayName: this._identity.displayName || '',
    }));

    this._signaling = null;
    this._transport = null;
    this._cancelled = false;
    this._disposed = false;
    /**
     * Did any transport in this session ever hand us an open channel? It gates the
     * goodbye in _teardown: a room that never connected can safely be handed back,
     * a room that DID is a live match whose room token the ledger still needs.
     */
    this._everConnected = false;

    /** The live match, or null when idle. REPLACED on relay fallback. */
    this.currentMatch = null;

    this._matchChangedListeners = new Set();
    this._connectFailedListeners = new Set();
  }

  get isBusy() { return this.currentMatch !== null; }
  /** The session's signaling client (null when idle). Both transports share it. */
  get signaling() { return this._signaling; }
  /** The live transport (webrtc, then relay after a fallback). */
  get transport() { return this._transport; }

  /**
   * currentMatch was created, replaced (relay fallback), or torn down. UI re-subscribes its match
   * event handlers here — never cache the first instance.
   * @returns {() => void} unsubscribe
   */
  onCurrentMatchChanged(fn) {
    if (typeof fn !== 'function') return () => {};
    this._matchChangedListeners.add(fn);
    return () => this._matchChangedListeners.delete(fn);
  }

  /**
   * Neither P2P nor relay could connect. Arg = machine reason ("ice_timeout", server error codes).
   * @returns {() => void} unsubscribe
   */
  onConnectFailed(fn) {
    if (typeof fn !== 'function') return () => {};
    this._connectFailedListeners.add(fn);
    return () => this._connectFailedListeners.delete(fn);
  }

  // ------------------------------------------------------------------ entry

  /** Host path: mints an invite and resolves the code to show the user (null = failure). */
  async host() {
    if (this._disposed) return null;
    const started = this._beginSession(true);
    if (!started) return null;
    const { webrtc, match } = started;

    const code = await invoke(match, ['createInvite', 'createInviteAsync']);
    if (!code) {
      const reason = webrtc.lastError || 'invite_failed';
      await this._teardown();
      this._raiseConnectFailed(reason);
      return null;
    }

    this._watchChannel(webrtc, true);
    return code;
  }

  /** Guest path: redeems a code the user typed. */
  async join(inviteCode) {
    if (this._disposed) return false;
    const started = this._beginSession(false);
    if (!started) return false;
    const { webrtc, match } = started;

    const ok = await invoke(match, ['join', 'joinAsync'], inviteCode);
    if (!ok) {
      const reason = webrtc.lastError || 'join_failed';
      await this._teardown();
      this._raiseConnectFailed(reason);
      return false;
    }

    this._watchChannel(webrtc, false);
    return true;
  }

  /**
   * User-initiated exit from wherever we are. In Live/SuddenDeath this is a Mercy (cancelMatch
   * routes it); earlier phases just fold the lobby. Always tears the plumbing down to idle.
   */
  async leave() {
    const match = this.currentMatch;
    if (match) {
      try { invoke(match, ['cancelMatch'], 'left'); }
      catch (e) { this._warn(`cancel on leave threw: ${(e && e.message) || e}`); }
    }
    await this._teardown();
  }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;
    return this._teardown();
  }

  // ------------------------------------------------------------------ innards

  _beginSession(isHost) {
    if (this.currentMatch) return null;

    this._cancelled = false;
    this._everConnected = false;   // per ATTEMPT, not per session object
    this._signaling = this._createSignaling();
    const webrtc = this._createWebrtc(isHost, this._signaling);
    this._transport = webrtc;
    const match = this._buildMatch(webrtc, isHost);

    this._raiseMatchChanged();
    return { webrtc, match };
  }

  _buildMatch(transport, isHost) {
    const match = this._createMatch(transport, isHost);
    if (!match) throw new Error('GoonSession: createMatch returned nothing');
    this.currentMatch = match;
    return match;
  }

  /** Fire-and-forget watchdog: resolves the P2P attempt, then either folds or adopts relay. */
  _watchChannel(webrtc, isHost) {
    (async () => {
      try {
        const ok = await webrtc.waitForChannel();
        if (ok) this._noteConnected(webrtc);
        if (ok || this._cancelled || this._disposed) return;

        if (!webrtc.iceFailed || !webrtc.code || !webrtc.token) {
          // Signaling-level failure (server down, room expired, peer never came) — a relay would
          // fail the same way. Surface and fold.
          const reason = webrtc.lastError || 'signaling_failed';
          await this._teardown();
          this._raiseConnectFailed(reason);
          return;
        }

        await this._fallBackToRelay(webrtc, isHost);
      } catch (e) {
        this._error(`channel watch failed: ${(e && e.message) || e}`);
        this._raiseConnectFailed('connect_error');
      }
    })();
  }

  async _fallBackToRelay(webrtc, isHost) {
    if (this._disposed || this._cancelled || this._transport !== webrtc) return;

    const code = webrtc.code;
    const token = webrtc.token;
    /**
     * THE FALLBACK BREADCRUMB (2026-08-05). A warn, not an info, because info dies before the C#
     * log and the phone's ?debug=1 overlay — and this is the single most consequential line in a
     * duel's life: past it `supportsBulk` is false FOREVER (the ws mailbox is the one transport
     * media may never ride), so every artifact the player queues for the rest of the match fires
     * untagged. When a cellular play-test comes back saying "no transfers again", this line is
     * what says whether we ran out of patience or the link genuinely died.
     *
     * `deadline` = a stopwatch fired while the browser may still have been working
     * (ice_timeout / no_offer). `failed` = the browser or the SDP said no. See
     * GoonConsts.IceRelayGraceMs for what the deadline is worth now.
     */
    this._warn(`relay fallback (${classifyFallback(webrtc.lastError)}: ${webrtc.lastError || '?'})`
      + ` — adopting room ${code} over the server mailbox; bulk media lane is OFF for this match`);

    // Old match FIRST (it unsubscribes the dying transport; dispose sends nothing), then the
    // transport. The shared signaling client stays — the relay needs it.
    if (this.currentMatch) {
      try { this.currentMatch.dispose(); }
      catch (e) { this._warn(`match dispose threw during fallback: ${(e && e.message) || e}`); }
    }
    this.currentMatch = null;

    try { await webrtc.close(); }
    catch (e) { this._warn(`webrtc close threw during fallback: ${(e && e.message) || e}`); }
    webrtc.dispose();

    const relay = this._createRelay(isHost, this._signaling);
    this._transport = relay;
    const match = this._buildMatch(relay, isHost);
    invoke(match, ['adoptLobby']);
    relay.adoptRoom(code, token);
    this._raiseMatchChanged();

    const ok = await relay.waitForChannel();
    if (ok) this._noteConnected(relay);
    if (!ok && !this._cancelled && !this._disposed) {
      const reason = relay.lastError || 'relay_failed';
      await this._teardown();
      this._raiseConnectFailed(reason);
    }
  }

  async _teardown() {
    const match = this.currentMatch;
    const transport = this._transport;
    const signaling = this._signaling;

    this.currentMatch = null;
    this._transport = null;
    this._signaling = null;
    this._cancelled = true;

    // THE GOODBYE. Hand the seat back before anything is disposed, so the room we
    // are abandoning does not sit there as a GHOST for the rest of its TTL —
    // which is what turns "join again" into "that room already has two players"
    // (owner play-test, 2026-08-04). Strictly best-effort: not awaited, never
    // throws, and skipped entirely once a channel came up, because past that
    // point the room token belongs to a live match and the ledger still needs it.
    this._releaseRoom(transport, signaling);

    if (match) {
      try { match.dispose(); }
      catch (e) { this._warn(`match dispose threw: ${(e && e.message) || e}`); }
    }
    if (transport) {
      try { await transport.close(); }
      catch (e) { this._warn(`transport close threw: ${(e && e.message) || e}`); }
      try { transport.dispose(); } catch (_e) { /* already gone */ }
    }
    if (signaling) { try { signaling.dispose(); } catch (_e) { /* already gone */ } }

    this._raiseMatchChanged();
  }

  /**
   * A transport reported an open channel. Ignored unless it is still THE transport
   * of THIS attempt: a watchdog for a torn-down leg resolving late must not mark
   * the next attempt's room as connected — that would silently stop the goodbye.
   */
  _noteConnected(transport) {
    if (this._cancelled || this._disposed) return;
    if (this._transport !== transport) return;
    this._everConnected = true;
  }

  /**
   * Fire-and-forget /v2/goon/leave for a room that never connected. Deliberately
   * NOT awaited: a teardown must complete at the same speed whether the server
   * answers, 404s (route not deployed) or never replies at all.
   */
  _releaseRoom(transport, signaling) {
    if (this._everConnected) return;
    if (!signaling || typeof signaling.leave !== 'function') return;
    const code = transport && transport.code;
    const token = transport && transport.token;
    if (!code || !token) return;    // never got a room; there is nothing to hand back
    const role = transport.isHost ? 'host' : 'guest';
    try {
      const p = signaling.leave(code, token, role);
      if (p && typeof p.catch === 'function') p.catch(() => {});
    } catch (e) {
      this._warn(`leave threw: ${(e && e.message) || e}`);
    }
  }

  // ------------------------------------------------------------------ events

  _raiseMatchChanged() { emit(this._matchChangedListeners, this.currentMatch, this._log, 'currentMatchChanged'); }
  _raiseConnectFailed(reason) { emit(this._connectFailedListeners, reason, this._log, 'connectFailed'); }

  _info(m) { if (this._log && this._log.info) this._log.info(`[GG] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[GG] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[GG] ${m}`); }
}
