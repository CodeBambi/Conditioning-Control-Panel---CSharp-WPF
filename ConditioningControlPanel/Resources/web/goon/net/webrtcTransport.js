// The primary transport — port of Services/GoonGame/GoonWebRtcTransport.cs onto the browser's
// native RTCPeerConnection (the C# side rides SIPSorcery; the signaling blobs are byte-identical,
// so a C# peer and a page peer interoperate with no translation layer).
//
// Shape:
//   host  createInvite() -> /v2/goon/invite mints a code -> short-poll /v2/goon/signal until the
//         guest joins -> offer/answer/ICE through the mailbox -> DTLS -> channel.
//   guest join(code)     -> /v2/goon/join -> same mailbox from the other side.
// Once the channel opens the mailbox is dropped and nothing but the two clients is involved.
//
// DATA CHANNEL ONLY. No tracks are ever added, so no codec is negotiated and there is no path —
// deliberate or accidental — for mic or camera to reach the peer.
//
// Channel config: ONE channel, ordered + reliable (no maxRetransmits, no maxPacketLifeTime).
//
// ICE: public STUN only, no TURN by design. A NAT pair that can't be punched gives up after
// GoonConsts.IceTimeoutMs, sets `iceFailed`, and the caller opens a relay transport on the SAME
// room — `code`/`token` deliberately survive the failure so session.js can adopt it.
//
// NODE-IMPORT-SAFE: nothing at module scope touches RTCPeerConnection. It is constructed only
// inside the negotiation path, guarded — a page without WebRTC fails with lastError
// 'webrtc_unavailable' instead of throwing at import.

import { GoonConsts, GoonTransportState } from '../core/contracts.js';
import { GoonSignalingClient, normalizeCode } from './signaling.js';
import { GoonTransportBase } from './transportBase.js';

/** Public STUN. Two providers because one of them will be blocked on someone's network. */
export const STUN_URLS = Object.freeze([
  'stun:stun.l.google.com:19302',
  'stun:stun1.l.google.com:19302',
  'stun:stun.cloudflare.com:3478',
]);

const CHANNEL_LABEL = 'goon';
const SIGNAL_POLL_MS = 2000;          // setup only; stops when the channel opens
const SIGNAL_FAILURE_LIMIT = 5;
// Bounded so a peer that never comes back can't grow this without limit.
const RESUME_BUFFER_MAX = 128;

function hasWebRtc() {
  return typeof RTCPeerConnection !== 'undefined';
}

export class GoonWebRtcTransport extends GoonTransportBase {
  /**
   * @param {object} o
   * @param {boolean} o.isHost host mints the invite and owns the match clock
   * @param {GoonSignalingClient} [o.signaling] shared client — pass one in so a fallback relay reuses the room
   * @param {object} [o.logger]
   * @param {string} [o.displayName]
   */
  constructor({ isHost = false, signaling = null, logger = null, displayName = '' } = {}) {
    super({ isHost, tag: isHost ? 'GoonP2P:host' : 'GoonP2P:guest', logger });

    this._ownsSignaling = !signaling;
    this._signaling = signaling || new GoonSignalingClient({ logger });
    this._role = isHost ? 'host' : 'guest';
    this._displayName = displayName;

    this._outSignals = [];
    this._wakeResolve = null;
    this._pendingRemoteCandidates = [];
    this._resumeBuffer = [];

    this._cancelled = false;
    this._pumping = false;
    this._signalCursor = 0;
    this._remoteDescriptionSet = false;
    this._negotiationStarted = false;
    this._everConnected = false;
    this._opened = false;             // idempotence guard for channel-open
    this._reconnectGraceTimer = null;

    this._pc = null;
    this._dc = null;

    this._code = null;
    this._token = null;
    this._iceFailed = false;
    this._peerDisplayName = null;
  }

  /** Invite code for this room. Survives an ICE failure so the relay can adopt it. */
  get code() { return this._code; }
  /** Room token for this side. Same lifetime note as `code`. */
  get token() { return this._token; }
  /** Set when ICE gave up. The caller's cue to open a relay transport on this room. */
  get iceFailed() { return this._iceFailed; }
  /** Display name the server reported for the opponent (guest side, from /join). */
  get peerDisplayName() { return this._peerDisplayName; }

  // ------------------------------------------------------------------ setup

  async createInvite() {
    if (!this.isHost) throw new Error('createInvite is the host path.');

    this._setState(GoonTransportState.Signaling, 'minting invite');
    const invite = await this._signaling.createInvite(this._displayName, false);
    if (!invite) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return null;
    }

    this._code = invite.code;
    this._token = invite.token;
    this._startSignalPump();
    return invite.code;
  }

  async join(inviteCode) {
    if (this.isHost) throw new Error('join is the guest path.');

    this._setState(GoonTransportState.Signaling, 'redeeming code');
    const joined = await this._signaling.join(inviteCode, this._displayName);
    if (!joined) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return false;
    }

    this._code = normalizeCode(inviteCode);
    this._token = joined.token;
    this._peerDisplayName = joined.peerDisplayName;
    this._startSignalPump();
    return true;
  }

  /**
   * Resolves when the data channel is open, or when ICE gives up (GoonConsts.IceTimeoutMs after
   * negotiation STARTS). False means "fall back to relay" — check `iceFailed` to tell that apart
   * from a signaling failure.
   */
  async waitForChannel() {
    let deadline = Date.now() + GoonConsts.IceTimeoutMs;
    let negotiationSeen = false;

    while (!this._cancelled && !this.isDisposed) {
      if (this.state === GoonTransportState.ConnectedP2P) return true;
      if (this.state === GoonTransportState.Disconnected && this.lastError !== null) return false;
      if (this.state === GoonTransportState.Closed) return false;

      // The ICE budget starts when there is actually an ICE process to time out — waiting for a
      // human to type the code must not eat into it.
      if (!negotiationSeen && this._negotiationStarted) {
        negotiationSeen = true;
        deadline = Date.now() + GoonConsts.IceTimeoutMs;
      }

      if (negotiationSeen && Date.now() > deadline) {
        this._iceFailed = true;
        this._setLastError('ice_timeout');
        this._warn(`ICE did not complete in ${GoonConsts.IceTimeoutMs}ms — relay fallback`);
        this._setState(GoonTransportState.Disconnected, 'ice_timeout');
        return false;
      }

      await this._sleep(100);
    }

    return this.state === GoonTransportState.ConnectedP2P;
  }

  // ------------------------------------------------------------------ signaling pump

  _startSignalPump() {
    if (this._pumping) return;
    this._pumping = true;
    this._signalPump().catch((e) => {
      this._error(`Signaling pump died: ${(e && e.message) || e}`);
      this._setLastError('signaling_error');
      this._setState(GoonTransportState.Disconnected, 'signaling_error');
    });
  }

  /**
   * Post-and-drain against /v2/goon/signal every SIGNAL_POLL_MS, waking early whenever we have
   * something to send. Exits as soon as the channel opens — this mailbox is a setup-only facility
   * and must not keep costing requests during a 12-minute match.
   */
  async _signalPump() {
    let failures = 0;

    while (!this._cancelled && !this.isDisposed) {
      if (this.state === GoonTransportState.ConnectedP2P) break;

      const outgoing = this._outSignals;
      this._outSignals = [];

      const result = await this._signaling.signal(
        this._code || '', this._token || '', this._role, this._signalCursor, outgoing);

      if (this._cancelled || this.isDisposed) return;

      if (!result) {
        // Re-queue what we tried to send; a transient 5xx must not eat the offer.
        this._outSignals = outgoing.concat(this._outSignals);

        failures++;
        if (failures >= SIGNAL_FAILURE_LIMIT) {
          this._setLastError(this._signaling.lastError || 'signaling_failed');
          this._setState(GoonTransportState.Disconnected, this.lastError);
          return;
        }

        const retry = this._signaling.retryAfterSeconds;
        const wait = Number.isFinite(retry) && retry > 0
          ? Math.min(retry, 30) * 1000
          : SIGNAL_POLL_MS * failures;
        await this._sleep(wait);
        continue;
      }

      failures = 0;
      this._signalCursor = result.cursor;

      if (result.peerGone) {
        this._setLastError('peer_left');
        this._setState(GoonTransportState.Disconnected, 'peer left the room');
        return;
      }

      for (const msg of result.messages) await this._handleSignal(msg);

      // Host waits for the guest to appear before spending anything on ICE.
      if (this.isHost && result.peerJoined && !this._negotiationStarted) await this._startPeer(null);

      if (this.state === GoonTransportState.ConnectedP2P) break;

      await this._waitForWake(SIGNAL_POLL_MS);
    }
  }

  _enqueueSignal(kind, data) {
    this._outSignals.push({ kind, data });
    this._wake();
  }

  async _handleSignal(item) {
    switch (item.kind) {
      case 'offer': {
        if (this.isHost) return;                    // hosts make offers, they don't take them
        const offer = parseJson(item.data);
        if (!offer) return;
        await this._startPeer(offer);
        break;
      }

      case 'answer': {
        if (!this.isHost) return;
        const answer = parseJson(item.data);
        if (!answer || !this._pc) return;
        try {
          await this._pc.setRemoteDescription(answer);
        } catch (e) {
          this._warn(`setRemoteDescription(answer) rejected: ${(e && e.message) || e}`);
          this._setLastError('sdp_rejected');
          return;
        }
        this._remoteDescriptionSet = true;
        await this._drainPendingCandidates();
        break;
      }

      case 'ice': {
        const cand = parseJson(item.data);
        if (!cand) return;
        // Candidates routinely land before the description they belong to.
        if (!this._pc || !this._remoteDescriptionSet) {
          this._pendingRemoteCandidates.push(cand);
          return;
        }
        await this._tryAddCandidate(cand);
        break;
      }

      case 'bye':
        this._setLastError('peer_bye');
        this._setState(GoonTransportState.Disconnected, 'peer said bye');
        break;

      default:
        break;
    }
  }

  async _drainPendingCandidates() {
    if (this._pendingRemoteCandidates.length === 0) return;
    const pending = this._pendingRemoteCandidates;
    this._pendingRemoteCandidates = [];
    for (const c of pending) await this._tryAddCandidate(c);
  }

  async _tryAddCandidate(cand) {
    try {
      if (this._pc) await this._pc.addIceCandidate(cand);
    } catch (e) {
      // One unusable candidate is normal (IPv6-only, a stale srflx); the pair still works.
      this._info(`Rejected a remote ICE candidate: ${(e && e.message) || e}`);
    }
  }

  // ------------------------------------------------------------------ peer connection

  /** @param {object|null} remoteOffer null on the host (we make the offer), the inbound offer otherwise. */
  async _startPeer(remoteOffer) {
    if (this._negotiationStarted) return;
    this._negotiationStarted = true;

    if (!hasWebRtc()) {
      // Not an ICE problem, but the same remedy: the room is live and the pass is burned, so flag
      // it so session.js adopts it over relay instead of folding the lobby.
      this._iceFailed = true;
      this._setLastError('webrtc_unavailable');
      this._setState(GoonTransportState.Disconnected, 'webrtc_unavailable');
      return;
    }

    this._setState(GoonTransportState.ConnectingP2P, this.isHost ? 'offering' : 'answering');

    try {
      const pc = new RTCPeerConnection({ iceServers: STUN_URLS.map((urls) => ({ urls })) });
      this._pc = pc;

      pc.onicecandidate = (e) => {
        // Trickle: every candidate goes out the moment it is gathered.
        if (!e || !e.candidate || this.isDisposed) return;
        try { this._enqueueSignal('ice', JSON.stringify(e.candidate.toJSON())); }
        catch (err) { this._info(`Failed to queue local candidate: ${(err && err.message) || err}`); }
      };

      pc.onconnectionstatechange = () => this._onPeerStateChanged(pc.connectionState);
      pc.oniceconnectionstatechange = () => this._info(`ICE state ${pc.iceConnectionState}`);

      if (this.isHost) {
        // ordered, no maxRetransmits/maxPacketLifeTime => fully reliable.
        this._attachChannel(pc.createDataChannel(CHANNEL_LABEL, { ordered: true }));

        const offer = await pc.createOffer();
        await pc.setLocalDescription(offer);
        this._enqueueSignal('offer', JSON.stringify({ type: 'offer', sdp: pc.localDescription.sdp }));
      } else {
        pc.ondatachannel = (e) => this._attachChannel(e.channel);

        try {
          await pc.setRemoteDescription(remoteOffer);
        } catch (e) {
          this._warn(`setRemoteDescription(offer) rejected: ${(e && e.message) || e}`);
          this._setLastError('sdp_rejected');
          this._setState(GoonTransportState.Disconnected, 'sdp_rejected');
          return;
        }
        this._remoteDescriptionSet = true;
        await this._drainPendingCandidates();

        const answer = await pc.createAnswer();
        await pc.setLocalDescription(answer);
        this._enqueueSignal('answer', JSON.stringify({ type: 'answer', sdp: pc.localDescription.sdp }));
      }
    } catch (e) {
      this._error(`Peer connection setup failed: ${(e && e.message) || e}`);
      this._iceFailed = true;
      this._setLastError('peer_setup_failed');
      this._setState(GoonTransportState.Disconnected, 'peer_setup_failed');
    }
  }

  _onPeerStateChanged(s) {
    this._info(`Peer connection ${s}`);
    if (this.isDisposed) return;

    switch (s) {
      case 'connected':
        this._cancelReconnectGrace();
        break;

      case 'disconnected':
      case 'failed':
        if (this._everConnected) this._beginReconnectGrace(String(s));
        else {
          this._iceFailed = true;
          this._setLastError('ice_failed');
          this._setState(GoonTransportState.Disconnected, 'ice_failed');
        }
        break;

      case 'closed':
        if (this.state !== GoonTransportState.Closed) {
          this._setState(GoonTransportState.Disconnected, 'peer connection closed');
        }
        break;

      default:
        break;
    }
  }

  /**
   * Wire one data channel. IDEMPOTENT about the open event on purpose: the ANSWERING side's
   * ondatachannel routinely fires with readyState already 'open' (the SCTP stream is negotiated
   * before the event reaches us), so subscribing to onopen alone would strand the guest until the
   * ICE watchdog needlessly dropped a perfectly good match to relay. Never await onopen.
   */
  _attachChannel(dc) {
    if (!dc) return;
    this._dc = dc;
    try { dc.binaryType = 'arraybuffer'; } catch (_e) { /* not all impls expose it */ }

    dc.onopen = () => this._handleChannelOpen();

    dc.onmessage = (e) => {
      if (this.isDisposed || !e) return;
      try {
        const data = e.data;
        if (typeof data === 'string') this._handleIncomingRaw(data);
        else if (data && typeof data.byteLength === 'number') this._handleIncomingRaw(decodeUtf8(data));
      } catch (err) {
        this._warn(`Failed to read inbound frame: ${(err && err.message) || err}`);
      }
    };

    dc.onclose = () => {
      if (this.isDisposed) return;
      this._info('Data channel closed');
      if (this._everConnected && this.state !== GoonTransportState.Closed) {
        this._beginReconnectGrace('data channel closed');
      }
    };

    dc.onerror = (e) => this._warn(`Data channel error: ${(e && (e.message || (e.error && e.error.message))) || 'unknown'}`);

    if (dc.readyState === 'open') this._handleChannelOpen();
  }

  /** The channel is usable. Idempotent — see the note in _attachChannel. */
  _handleChannelOpen() {
    if (this.isDisposed || this._opened) return;
    this._opened = true;

    this._everConnected = true;
    this._cancelReconnectGrace();
    // _setState kicks off the clock sync (autoClockSync) — GoonTransportBase.BeginClockSync.
    this._setState(GoonTransportState.ConnectedP2P, 'data channel open');
    this._flushResumeBuffer();
  }

  // ------------------------------------------------------------------ reconnect / resume

  /**
   * Hold the match together across a blip. For GoonConsts.ReconnectGraceMs the transport reports
   * Reconnecting and buffers outbound frames instead of dropping them; if SCTP comes back the
   * buffer flushes in order and the match never noticed. Past the grace we surface Disconnected and
   * it becomes the match engine's abandon flow — never recorded as a mercy.
   */
  _beginReconnectGrace(reason) {
    if (this._reconnectGraceTimer) return;

    this._setState(GoonTransportState.Reconnecting, reason);
    // Arm the open handler again so a channel that comes back flushes the resume buffer.
    this._opened = false;

    this._reconnectGraceTimer = setTimeout(() => {
      this._reconnectGraceTimer = null;
      if (this.isDisposed) return;
      if (this.state !== GoonTransportState.Reconnecting) return;
      this._setLastError('reconnect_timeout');
      this._warn(`Peer did not resume within ${GoonConsts.ReconnectGraceMs}ms`);
      this._setState(GoonTransportState.Disconnected, 'reconnect_timeout');
    }, GoonConsts.ReconnectGraceMs);
    if (typeof this._reconnectGraceTimer.unref === 'function') this._reconnectGraceTimer.unref();
  }

  _cancelReconnectGrace() {
    if (!this._reconnectGraceTimer) return;
    clearTimeout(this._reconnectGraceTimer);
    this._reconnectGraceTimer = null;
  }

  _flushResumeBuffer() {
    const dc = this._dc;
    if (!dc || dc.readyState !== 'open' || this._resumeBuffer.length === 0) return;

    let sent = 0;
    while (this._resumeBuffer.length > 0) {
      const json = this._resumeBuffer.shift();
      try { dc.send(json); sent++; }
      catch (e) {
        this._resumeBuffer.unshift(json);
        this._warn(`Resume flush failed after ${sent} frames: ${(e && e.message) || e}`);
        break;
      }
    }
    if (sent > 0) this._info(`Resumed: flushed ${sent} buffered frames`);
  }

  // ------------------------------------------------------------------ send

  async _sendRaw(json) {
    if (this.isDisposed) return;

    const dc = this._dc;
    if (dc && dc.readyState === 'open') {
      try { dc.send(json); return; }
      catch (e) { this._warn(`send failed — buffering for resume: ${(e && e.message) || e}`); }
    }

    if (this.state === GoonTransportState.Reconnecting || this.state === GoonTransportState.ConnectingP2P) {
      if (this._resumeBuffer.length < RESUME_BUFFER_MAX) this._resumeBuffer.push(json);
      else this._warn('Resume buffer full — dropping a frame');
    } else {
      this._warn(`Dropping a frame — channel not open (state ${this.state})`);
    }
  }

  // ------------------------------------------------------------------ teardown

  async close() {
    if (this.state === GoonTransportState.Closed) return;

    // Best-effort courtesy so the peer stops polling immediately instead of waiting out the room
    // TTL. Never blocks the close.
    try {
      if (this._code && this._token && this.state === GoonTransportState.Signaling) {
        await this._signaling.signal(this._code, this._token, this._role, this._signalCursor,
          [{ kind: 'bye', data: '' }]);
      }
    } catch (e) { this._info(`bye failed (harmless): ${(e && e.message) || e}`); }

    this._setState(GoonTransportState.Closed, 'closed by us');
    this._tearDown();
  }

  _tearDown() {
    this._cancelReconnectGrace();
    this._cancelled = true;
    this._wake();

    const dc = this._dc;
    const pc = this._pc;
    this._dc = null;
    this._pc = null;

    try { if (dc) dc.close(); } catch (_e) { /* already gone */ }
    try { if (pc) pc.close(); } catch (_e) { /* already gone */ }
    try { this._clock.stop(); } catch (_e) { /* already gone */ }
  }

  _disposeCore() {
    this._tearDown();
    if (this._ownsSignaling) this._signaling.dispose();
  }

  // ------------------------------------------------------------------ wake / sleep

  _wake() {
    const r = this._wakeResolve;
    this._wakeResolve = null;
    if (r) r();
  }

  _waitForWake(ms) {
    return new Promise((resolve) => {
      let done = false;
      const finish = () => { if (done) return; done = true; clearTimeout(timer); resolve(); };
      const timer = setTimeout(finish, ms);
      if (typeof timer.unref === 'function') timer.unref();
      this._wakeResolve = finish;
    });
  }

  _sleep(ms) {
    return new Promise((resolve) => {
      const t = setTimeout(resolve, ms);
      if (typeof t.unref === 'function') t.unref();
    });
  }
}

function parseJson(s) {
  try {
    const v = JSON.parse(String(s || ''));
    return (v && typeof v === 'object') ? v : null;
  } catch (_e) { return null; }
}

function decodeUtf8(buf) {
  if (typeof TextDecoder === 'function') return new TextDecoder().decode(buf);
  let out = '';
  const view = new Uint8Array(buf);
  for (let i = 0; i < view.length; i++) out += String.fromCharCode(view[i]);
  return out;
}

export { hasWebRtc };
