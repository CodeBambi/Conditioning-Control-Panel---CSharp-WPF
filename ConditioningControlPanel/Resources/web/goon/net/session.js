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
// This facade does NOT gate on premium/pass state — the server enforces passes at /v2/goon/invite
// (402 no_pass). UI may pre-gate for niceness, never here.
//
// LEAVE vs TEARDOWN: leave() is user-initiated and routes through match.cancelMatch (in Live that
// is a Mercy). Every internal path — fallback, failure, dispose — uses dispose() only, which sends
// nothing. Getting that backwards would turn a transport hiccup into a forfeit.

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
   * @param {object} [o.identity] {unifiedId, displayName, appVersion} for the signaling body
   * @param {object} [o.logger]
   * @param {() => GoonSignalingClient} [o.createSignaling] test seam
   * @param {(isHost:boolean, signaling:GoonSignalingClient) => object} [o.createWebrtc] test seam
   * @param {(isHost:boolean, signaling:GoonSignalingClient) => object} [o.createRelay] test seam
   */
  constructor({ createMatch, identity = null, logger = null, createSignaling = null,
    createWebrtc = null, createRelay = null } = {}) {
    if (typeof createMatch !== 'function') throw new Error('GoonSession: createMatch factory is required');

    this._createMatch = createMatch;
    this._identity = identity || {};
    this._log = logger || (typeof console !== 'undefined' ? console : null);

    this._createSignaling = createSignaling || (() => new GoonSignalingClient({
      unifiedId: this._identity.unifiedId || '',
      appVersion: this._identity.appVersion || '',
      displayName: this._identity.displayName || '',
      logger: this._log,
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
    this._info(`P2P failed (${webrtc.lastError || '?'}), adopting room ${code} over relay`);

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

  // ------------------------------------------------------------------ events

  _raiseMatchChanged() { emit(this._matchChangedListeners, this.currentMatch, this._log, 'currentMatchChanged'); }
  _raiseConnectFailed(reason) { emit(this._connectFailedListeners, reason, this._log, 'connectFailed'); }

  _info(m) { if (this._log && this._log.info) this._log.info(`[GG] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[GG] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[GG] ${m}`); }
}
