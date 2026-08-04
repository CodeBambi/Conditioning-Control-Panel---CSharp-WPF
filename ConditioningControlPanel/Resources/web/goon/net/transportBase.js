// What every Goon Game transport shares — port of Services/GoonGame/GoonTransportBase.cs
// (+ the IGoonTransport contract in GoonContracts.cs).
//
// Inbound path, in order: raw string -> wire.parse (size-capped, never throws) -> the MatchClock
// gets first refusal (ping/pong are private to it) -> everything else surfaces on the
// messageReceived listeners. Outbound: wire.serializeForSend drops an oversize frame with an error
// rather than wedging the channel, exactly as the C# base does.
//
// Subclasses only implement "get this string to the peer" (_sendRaw) and tell the base when the
// channel is up (_setState -> a connected state).
//
// NO DISPATCHER. GoonDispatch exists in C# because SIPSorcery/HttpClient callbacks land on pool
// threads and WPF objects may only be touched on one; the browser has a single UI thread and no
// such hazard. What survives from it is the discipline: a listener that throws is logged and never
// takes the others (or the transport) down with it.
//
// NAMING: the surface below is the mechanical camelCase of IGoonTransport / GoonTransportBase,
// with the C# `Async` suffix dropped (core/clock.js set that precedent: SyncAsync -> sync()).
// `*Async` aliases are also defined for every one of those methods so a caller that mapped the
// suffix literally still binds.

import { MatchClock } from '../core/clock.js';
import { GoonTransportState, enumName } from '../core/contracts.js';
import { parse, serializeForSend } from '../core/wire.js';

const CONNECTED_STATES = [GoonTransportState.ConnectedP2P, GoonTransportState.ConnectedRelay];

/**
 * Fires `fn` for each listener, isolating throws.
 * Exported so the subclasses that actually implement a bulk surface (webrtc, loopback) get the
 * same one-listener-cannot-take-the-others-down discipline without copying it.
 */
export function emit(listeners, arg, logger, tag, what) {
  for (const fn of Array.from(listeners)) {
    try { fn(arg); } catch (e) {
      if (logger && logger.error) logger.error(`[${tag}] ${what} listener threw: ${(e && e.message) || e}`);
    }
  }
}

export class GoonTransportBase {
  /**
   * @param {object} o
   * @param {boolean} o.isHost host mints the invite AND owns the match clock
   * @param {string} o.tag log prefix for this instance
   * @param {object} [o.logger] console-shaped
   * @param {number} [o.clockTestSkewMs] TEST ONLY — fake local skew handed to the MatchClock
   * @param {boolean} [o.autoClockSync] run clock sync automatically on the first connected state
   */
  constructor({ isHost = false, tag = 'GoonTransport', logger = null, clockTestSkewMs = 0, autoClockSync = true } = {}) {
    this._isHost = !!isHost;
    this._tag = tag;
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this._state = GoonTransportState.Disconnected;
    this._lastError = null;
    this._disposed = false;
    this._autoClockSync = autoClockSync !== false;
    this._clockSyncRunning = false;

    this._messageListeners = new Set();
    this._stateListeners = new Set();

    // Host's monotonic clock IS the match clock; the guest measures its offset to it.
    this._clock = new MatchClock({
      isClockMaster: this._isHost,
      tag,
      logger: this._log,
      testSkewMs: clockTestSkewMs,
    });
    // The clock's pings ride the same guarded send path as everything else.
    this._clock.attach((msg) => this.send(msg));
  }

  // ------------------------------------------------------------------ surface

  get isHost() { return this._isHost; }
  get tag() { return this._tag; }
  /** The shared MatchClock (C# IGoonTransport.Clock). */
  get clock() { return this._clock; }
  get state() { return this._state; }
  get lastError() { return this._lastError; }
  get isDisposed() { return this._disposed; }
  get isConnected() { return CONNECTED_STATES.indexOf(this._state) >= 0; }

  /** @returns {() => void} unsubscribe */
  onMessageReceived(fn) {
    if (typeof fn !== 'function') return () => {};
    this._messageListeners.add(fn);
    return () => this._messageListeners.delete(fn);
  }

  /** @returns {() => void} unsubscribe */
  onStateChanged(fn) {
    if (typeof fn !== 'function') return () => {};
    this._stateListeners.add(fn);
    return () => this._stateListeners.delete(fn);
  }

  // ------------------------------------------------------------ bulk (out-of-band binary)
  //
  // The media-transfer channel (net/mediaChannel.js) rides a SECOND, out-of-band channel that
  // bypasses wire.js entirely — 16 KiB binary chunks cannot fit MAX_WIRE_BYTES and must never be
  // routable over the relay. These six members are the whole seam, and the defaults below are the
  // honest answer for every transport that cannot carry bulk: relay inherits them unchanged
  // (a 16 KB/frame HTTP long-poll ring is physically unsuitable, and media must never touch the
  // server), loopback opts in only for tests, and only webrtcTransport overrides them for real.
  //
  // `supportsBulk` exists BECAUSE `isConnected` cannot be used: CONNECTED_STATES above treats
  // ConnectedP2P and ConnectedRelay identically, so "connected" is true on exactly the transport
  // that must never see a byte of media.

  /** Can this transport carry bulk binary out-of-band? P2P only, by design. */
  get supportsBulk() { return false; }

  /**
   * Hand one bulk frame to the channel. NEVER goes through `send`/`_sendRaw`, so bulk data can
   * never enter the 128-frame resume buffer (that buffer is sized for ticks).
   *
   * The boolean return is load-bearing: `_sendRaw` silently DROPS frames in non-open states, and a
   * pump that assumes delivery wedges where one that reads `false` retries. Every caller branches.
   *
   * @param {ArrayBuffer|string} _data
   * @returns {boolean} true when the bytes were handed to the channel.
   */
  sendBulk(_data) { return false; }

  /** Bytes queued on the bulk channel — the backpressure reading. */
  get bulkBufferedAmount() { return 0; }
  /** The low-water mark the channel will report `bufferedamountlow` at. */
  get bulkLowThreshold() { return 0; }

  /** @returns {() => void} unsubscribe. Delivers string | ArrayBuffer, RAW — no parsing, ever. */
  onBulkMessage(_fn) { return () => {}; }
  /** @returns {() => void} unsubscribe. Delivers 'open' | 'closed'. */
  onBulkStateChanged(_fn) { return () => {}; }

  // ------------------------------------------------------------------ setup

  /** Host path: mint an invite, resolve with the code to show the user (null = failure). */
  async createInvite() { throw new Error(`[${this._tag}] createInvite not implemented`); }

  /** Guest path: redeem a code the user typed. */
  async join(_inviteCode) { throw new Error(`[${this._tag}] join not implemented`); }

  async close() { throw new Error(`[${this._tag}] close not implemented`); }

  /**
   * Serialize + size-guard + hand to the subclass. Mirrors GoonTransportBase.SendAsync: our own
   * oversize frame is OUR bug, and dropping it beats wedging the channel.
   */
  async send(message) {
    if (!message || this._disposed) return;
    const json = serializeForSend(message, { logger: this._log, tag: this._tag });
    if (json === null) return;                      // already logged as an error
    await this._sendRaw(json);
  }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;
    try { this._disposeCore(); }
    catch (e) { this._warn(`Dispose failed: ${(e && e.message) || e}`); }
    try { this._clock.dispose(); } catch (_e) { /* already gone */ }
    this._messageListeners.clear();
    this._stateListeners.clear();
  }

  // `*Async` aliases — see the NAMING note at the top of the file.
  createInviteAsync() { return this.createInvite(); }
  joinAsync(inviteCode) { return this.join(inviteCode); }
  sendAsync(message) { return this.send(message); }
  closeAsync() { return this.close(); }

  // ------------------------------------------------------------------ subclass hooks

  /** Push one already-serialized frame to the peer. Implementations may buffer. */
  async _sendRaw(_json) { throw new Error(`[${this._tag}] _sendRaw not implemented`); }

  /** Subclasses funnel every inbound frame through here. */
  _handleIncomingRaw(json) {
    if (this._disposed) return;

    const message = parse(json, { logger: this._log, tag: this._tag });
    if (!message) {
      // Belt and braces for the ONE wiring mistake this design can make silently. `parse()` drops
      // an unknown `t` at INFO, so an `xfer_*` control frame accidentally routed to the GAME
      // channel (mediaChannel handed the wrong send function, say) would simply vanish and the
      // transfer would hang with no evidence anywhere. Say so, loudly.
      if (typeof json === 'string' && json.startsWith('{"t":"xfer_')) {
        this._warn('An xfer_ control frame arrived on the GAME channel — the media channel is '
          + 'mis-wired (bulk frames belong on sendBulk/onBulkMessage, never on send()).');
      }
      return;                                       // already logged
    }

    // The clock's ping/pong is its own private conversation — the match engine must never see it.
    if (this._clock.tryHandleMessage(message)) return;

    emit(this._messageListeners, message, this._log, this._tag, 'messageReceived');
  }

  _setState(next, reason = null) {
    if (this._state === next) return;
    this._state = next;
    this._info(reason ? `State -> ${enumName(GoonTransportState, next)} (${reason})`
      : `State -> ${enumName(GoonTransportState, next)}`);

    if (this._autoClockSync && CONNECTED_STATES.indexOf(next) >= 0) this._beginClockSync();

    emit(this._stateListeners, next, this._log, this._tag, 'stateChanged');
  }

  _setLastError(err) { this._lastError = err || null; }

  /**
   * Runs the clock's ping rounds once the channel is usable. Fire-and-forget safe; one retry,
   * same as GoonTransportBase.BeginClockSync.
   */
  _beginClockSync() {
    if (this._disposed || this._clockSyncRunning) return;
    this._clockSyncRunning = true;
    (async () => {
      try {
        const ok = await this._clock.sync();
        if (ok) return;
        this._warn('Initial clock sync did not converge — retrying once');
        await new Promise((r) => setTimeout(r, 1000));
        if (this._disposed) return;
        await this._clock.sync();
      } catch (e) {
        this._error(`Clock sync threw: ${(e && e.message) || e}`);
      } finally {
        this._clockSyncRunning = false;
      }
    })();
  }

  _disposeCore() { /* subclasses override */ }

  _info(m) { if (this._log && this._log.info) this._log.info(`[${this._tag}] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[${this._tag}] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[${this._tag}] ${m}`); }
}

export { GoonTransportState };
