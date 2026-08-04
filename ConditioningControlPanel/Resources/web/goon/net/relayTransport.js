// The fallback transport — port of Services/GoonGame/GoonRelayTransport.cs.
//
// Every message goes through the proxy's /v2/goon/relay mailbox instead of a peer-to-peer channel.
// Used when ICE never completes: symmetric NATs, corporate networks, anything the no-TURN rule
// leaves unpunchable.
//
// Degraded but NOT broken, and that is a design property: everything synchronized fires at a
// shared-clock instant at least MinScheduleBufferMs in the future, and every reaction measurement
// is a LOCAL stopwatch delta that never touches the network. A second of relay latency shifts
// nothing that decides the match.
//
// Request shape: one post-and-drain per cycle (outbound frames and the inbound cursor ride the same
// call). We hint RELAY_WAIT_MS as a long-poll budget and re-post as soon as the answer lands, but
// never faster than RELAY_MIN_GAP_MS. So against a server that holds the request we get roughly one
// call per 8 s with near-live delivery, and against one that answers instantly we settle at the
// plan's 2 s cadence.

import { GoonConsts, GoonTransportState } from '../core/contracts.js';
import { GoonSignalingClient, GoonSignalError, normalizeCode } from './signaling.js';
import { GoonTransportBase } from './transportBase.js';

/** Long-poll budget we ask the server for. Comfortably under a serverless timeout. */
export const RELAY_WAIT_MS = 8000;
/** Floor between two relay calls, so a non-holding server can't be hammered. */
export const RELAY_MIN_GAP_MS = 2000;
const BACKOFF_MAX_SECONDS = 60;
/** Consecutive failures before the UI is told the connection is wobbly. */
const FAILURES_BEFORE_RECONNECTING = 3;
/** Outbound frames held while the mailbox is unreachable. */
const OUTBOX_MAX = 256;

export class GoonRelayTransport extends GoonTransportBase {
  /**
   * @param {object} o
   * @param {boolean} o.isHost
   * @param {GoonSignalingClient} [o.signaling] shared client — pass the session's so the room is reused
   * @param {object} [o.logger]
   * @param {string} [o.displayName]
   * @param {number} [o.waitMs] long-poll hint (test affordance; defaults to RELAY_WAIT_MS)
   * @param {number} [o.minGapMs] cadence floor (test affordance; defaults to RELAY_MIN_GAP_MS)
   */
  constructor({ isHost = false, signaling = null, logger = null, displayName = '',
    waitMs = RELAY_WAIT_MS, minGapMs = RELAY_MIN_GAP_MS } = {}) {
    super({ isHost, tag: isHost ? 'GoonRelay:host' : 'GoonRelay:guest', logger });

    this._ownsSignaling = !signaling;
    this._signaling = signaling || new GoonSignalingClient({ logger });
    this._role = isHost ? 'host' : 'guest';
    this._displayName = displayName;
    this._waitMs = waitMs;
    this._minGapMs = minGapMs;

    this._outbox = [];
    this._wakeResolve = null;
    this._cancelled = false;
    this._looping = false;
    this._cursor = 0;
    this._backoffSeconds = 0;
    this._firstFailureLocalMs = 0;
    this._connectedOnce = false;

    this._code = null;
    this._token = null;
  }

  get code() { return this._code; }
  get token() { return this._token; }

  /**
   * Take over a room the WebRTC transport already opened, after ICE gave up. No second /invite or
   * /join call — the server room, the invite code the user already read out, and the weekly pass
   * that was already burned all stay as they are.
   */
  adoptRoom(code, token) {
    this._code = normalizeCode(code);
    this._token = token;
    this._cancelled = false;
    this._setState(GoonTransportState.Signaling, 'adopting room from failed P2P');
    this._startLoop();
  }

  // ------------------------------------------------------------------ setup

  async createInvite() {
    if (!this.isHost) throw new Error('createInvite is the host path.');

    this._setState(GoonTransportState.Signaling, 'minting invite (relay)');
    const invite = await this._signaling.createInvite(this._displayName, true);
    if (!invite) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return null;
    }

    this._code = invite.code;
    this._token = invite.token;
    this._startLoop();
    return invite.code;
  }

  async join(inviteCode) {
    if (this.isHost) throw new Error('join is the guest path.');

    this._setState(GoonTransportState.Signaling, 'redeeming code (relay)');
    const joined = await this._signaling.join(inviteCode, this._displayName);
    if (!joined) {
      this._setLastError(this._signaling.lastError);
      this._setState(GoonTransportState.Disconnected, this.lastError);
      return false;
    }

    this._code = normalizeCode(inviteCode);
    this._token = joined.token;
    this._startLoop();
    return true;
  }

  /** Resolves true once the mailbox has answered at least once. */
  async waitForChannel() {
    while (!this._cancelled && !this.isDisposed) {
      if (this.state === GoonTransportState.ConnectedRelay) return true;
      if (this.state === GoonTransportState.Disconnected && this.lastError !== null) return false;
      if (this.state === GoonTransportState.Closed) return false;
      await this._sleep(100);
    }
    return this.state === GoonTransportState.ConnectedRelay;
  }

  // ------------------------------------------------------------------ loop

  _startLoop() {
    if (this._looping) return;
    this._looping = true;
    this._loop().catch((e) => {
      this._error(`Relay loop died: ${(e && e.message) || e}`);
      this._setLastError('relay_error');
      this._setState(GoonTransportState.Disconnected, 'relay_error');
    });
  }

  async _loop() {
    let failures = 0;

    while (!this._cancelled && !this.isDisposed) {
      const started = Date.now();

      const outgoing = this._outbox;
      this._outbox = [];

      const result = await this._signaling.relay(
        this._code || '', this._token || '', this._role, this._cursor, outgoing, this._waitMs);

      if (this._cancelled || this.isDisposed) return;

      if (!result) {
        // Order within this batch is preserved and the batch as a whole retries.
        this._outbox = outgoing.concat(this._outbox);

        failures++;
        // Terminal server verdicts stop the loop. (C# keeps polling a dead room forever; in a
        // page that is a request leak, and the state is already Disconnected either way.)
        if (this._handleFailure(failures)) return;

        await this._sleep(this._computeBackoffMs());
        continue;
      }

      failures = 0;
      this._firstFailureLocalMs = 0;
      this._backoffSeconds = 0;
      this._cursor = result.cursor;

      if (!this._connectedOnce) {
        this._connectedOnce = true;
        // _setState kicks off the clock sync (autoClockSync) — GoonTransportBase.BeginClockSync.
        this._setState(GoonTransportState.ConnectedRelay, 'relay mailbox live');
      } else if (this.state === GoonTransportState.Reconnecting) {
        this._setState(GoonTransportState.ConnectedRelay, 'relay recovered');
      }

      for (const frame of result.frames) this._handleIncomingRaw(frame);

      // Pace: never faster than the floor, but if the server held the request we have already
      // spent that time and go straight back around.
      const gap = Math.max(0, this._minGapMs - (Date.now() - started));
      if (gap > 0) await this._waitForWake(gap);
    }
  }

  /** @returns {boolean} true when the failure is terminal and the loop must stop. */
  _handleFailure(consecutive) {
    const now = Date.now();
    if (this._firstFailureLocalMs === 0) this._firstFailureLocalMs = now;

    const err = this._signaling.lastError;

    if (err === GoonSignalError.RateLimited) {
      this._backoffSeconds = this._backoffSeconds <= 0
        ? (this._minGapMs / 1000) * 2
        : Math.min(this._backoffSeconds * 2, BACKOFF_MAX_SECONDS);
      this._warn(`Relay rate limited (429) — backing off to ${this._backoffSeconds}s`);
    }

    if (err === GoonSignalError.Expired || err === GoonSignalError.NotDeployed || err === GoonSignalError.Unauthorized) {
      this._setLastError(err);
      this._setState(GoonTransportState.Disconnected, err);
      return true;
    }

    if (consecutive >= FAILURES_BEFORE_RECONNECTING && this.state === GoonTransportState.ConnectedRelay) {
      this._setState(GoonTransportState.Reconnecting, err || 'relay unreachable');
    }

    // Past the tick-dead threshold this is an abandon, not a blip. The match engine decides what
    // that means; it is never auto-scored as a mercy.
    if (this._connectedOnce && now - this._firstFailureLocalMs > GoonConsts.TickDeadMs) {
      this._setLastError('relay_unreachable');
      this._setState(GoonTransportState.Disconnected, 'relay unreachable past abandon threshold');
    }
    return false;
  }

  _computeBackoffMs() {
    if (this._backoffSeconds > 0) return Math.trunc(this._backoffSeconds * 1000);
    const s = this._signaling.retryAfterSeconds;
    if (Number.isFinite(s) && s > 0) return Math.min(s, 30) * 1000;
    return this._minGapMs;
  }

  // ------------------------------------------------------------------ send

  async _sendRaw(json) {
    if (this.isDisposed) return;

    if (this._outbox.length >= OUTBOX_MAX) {
      this._warn(`Outbox full (${OUTBOX_MAX}) — dropping a frame`);
      return;
    }

    this._outbox.push(json);
    // Cut the cadence short so a payload or a mercy leaves now rather than in up to a full gap.
    this._wake();
  }

  // ------------------------------------------------------------------ teardown

  async close() {
    if (this.state === GoonTransportState.Closed) return;
    this._setState(GoonTransportState.Closed, 'closed by us');
    this._tearDown();
  }

  _tearDown() {
    this._cancelled = true;
    this._wake();
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

  /** Sleeps `ms`, but returns early when a send (or a teardown) wakes us. */
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
