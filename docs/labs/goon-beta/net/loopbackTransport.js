// An in-process transport pair — port of Services/GoonGame/GoonLoopbackTransport.cs.
//
// This is the rig the match/rounds work develops and play-tests against: no server, no NAT, no
// second machine, but the same message types and the same clock behaviour as the real thing.
//
// It models the two properties of the real channel that actually change how calling code has to be
// written:
//  - LATENCY, so code that (wrongly) acts on arrival instead of on a shared timestamp visibly
//    desyncs here rather than in front of a player. Try the relay preset — if a feature survives
//    900 ms it will survive the relay fallback.
//  - CLOCK SKEW, so a raw local timestamp that should have been a match timestamp is off by
//    seconds and impossible to miss.
//
// Delivery is ORDERED even under jitter: each frame's delivery instant is clamped to be no earlier
// than the previous frame's. A naive per-message random delay would reorder, which the real
// ordered/reliable SCTP channel never does — a mock harsher than reality only teaches callers to
// defend against something that cannot happen.
//
// It also models the second, BULK channel — but only when asked (`loopbackOptions({bulk:true})`).
// That opt-in is the point: Practice mode runs on this same pair and must keep reporting
// `supportsBulk === false`, so the "no media over a non-P2P link" behaviour is exercised on a path
// a player actually walks, while test/selftest-transfer.js gets a real two-ended channel to drive
// net/mediaChannel.js over.

import { GoonTransportState } from '../core/contracts.js';
import { GoonRng } from '../core/rng.js';
import { BULK_LOW_WATER } from './mediaChannel.js';
import { GoonTransportBase, emit } from './transportBase.js';

/** Knobs for a loopback pair. */
export function loopbackOptions(o = {}) {
  return {
    /** One-way delay applied to every frame, in ms. 25 ms is a good LAN/near-peer. */
    latencyMs: o.latencyMs ?? 25,
    /** Random extra delay in [0, jitterMs). Never reorders — see the module remarks. */
    jitterMs: o.jitterMs ?? 10,
    /**
     * Fake skew applied to the GUEST's local clock. Non-zero on purpose: with both sides on the
     * same monotonic clock the offset would be 0 and the sync would "pass" without proving
     * anything. A weird prime-ish number makes an unconverted timestamp obvious in a log.
     */
    guestClockSkewMs: o.guestClockSkewMs ?? 3517,
    /** Seed for the jitter generator, so a loopback run is reproducible. */
    jitterSeed: o.jitterSeed ?? 0xC0FFEEn,
    /**
     * OPT-IN in-process bulk pair, for driving net/mediaChannel.js under node.
     *
     * DEFAULT FALSE ON PURPOSE. Practice mode runs on a loopback pair too, and it must keep
     * answering `supportsBulk === false` — that is what proves the "silent degradation is the
     * ABSENCE of a special case" claim on a path a player actually takes.
     */
    bulk: o.bulk ?? false,
    logger: o.logger ?? null,
  };
}

/** The three presets the C# harness ships. */
export const loopbackPresets = Object.freeze({
  /** A realistic peer-to-peer link. */
  p2p: () => loopbackOptions(),
  /** The relay path's feel — slow and lumpy, to shake out fire-at-timestamp bugs. */
  relay: () => loopbackOptions({ latencyMs: 900, jitterMs: 600 }),
  /** Instant delivery. Only for tests asserting logic, not timing. */
  instant: () => loopbackOptions({ latencyMs: 0, jitterMs: 0, guestClockSkewMs: 0 }),
});

export class GoonLoopbackTransport extends GoonTransportBase {
  constructor(isHost, options) {
    super({
      isHost,
      tag: isHost ? 'GoonLoop:host' : 'GoonLoop:guest',
      logger: options.logger,
      clockTestSkewMs: isHost ? 0 : options.guestClockSkewMs,
      // The pair factory drives both syncs explicitly (as GoonLoopbackPair.ConnectAsync does), so
      // auto-sync would race a second ping burst against it.
      autoClockSync: false,
    });

    this._options = options;
    // Different sub-streams per direction so host and guest jitter independently.
    this._jitter = GoonRng.derive(options.jitterSeed, isHost ? 'loop-host' : 'loop-guest');
    this._peer = null;
    this._lastDeliverAtLocalMs = 0;
    this._outageUntilLocalMs = 0;
    this._inFlight = 0;

    // Bulk side. Its own delivery ordering (`_lastBulkDeliverAt`) rather than the game channel's,
    // because SCTP ordering is PER-STREAM: a 24 MB transfer must not be able to delay a tick here
    // either, or the mock would teach callers to defend against something that cannot happen.
    this._bulk = !!options.bulk;
    this._bulkMessageListeners = new Set();
    this._bulkStateListeners = new Set();
    this._bulkOpen = false;
    this._bulkBuffered = 0;
    this._lastBulkDeliverAtLocalMs = 0;
  }

  // ------------------------------------------------------------------ bulk (opt-in)

  get supportsBulk() {
    return this._bulk && this._bulkOpen && !this.isDisposed
      && this.state === GoonTransportState.ConnectedP2P
      && !!this._peer && this._peer._bulk === true && !this._peer.isDisposed;
  }

  /** @returns {boolean} — false is a real answer here, exactly as on the wire. */
  sendBulk(data) {
    if (!this.supportsBulk) return false;

    // Copy: the real channel serializes, and a test that hands the same ArrayBuffer to both sides
    // would hide a sender that mutates its own buffer.
    let frame = data;
    if (data instanceof ArrayBuffer) frame = data.slice(0);
    else if (data && typeof data === 'object' && data.buffer instanceof ArrayBuffer) {
      frame = data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
    } else if (typeof data !== 'string') return false;

    const size = typeof frame === 'string' ? frame.length : frame.byteLength;
    this._bulkBuffered += size;

    const now = Date.now();
    const jitter = this._options.jitterMs > 0 ? this._jitter.nextInt(0, this._options.jitterMs) : 0;
    let earliest = now + this._options.latencyMs + jitter;
    if (earliest < this._lastBulkDeliverAtLocalMs) earliest = this._lastBulkDeliverAtLocalMs;
    if (earliest < this._outageUntilLocalMs) earliest = this._outageUntilLocalMs;
    this._lastBulkDeliverAtLocalMs = earliest;

    const deliver = () => {
      this._bulkBuffered = Math.max(0, this._bulkBuffered - size);
      const p = this._peer;
      if (!p || p.isDisposed || this.isDisposed) return;
      emit(p._bulkMessageListeners, frame, p._log, p._tag, 'bulkMessage');
    };

    const wait = Math.max(0, earliest - Date.now());
    if (wait === 0) {
      // Still a turn later: an in-process pair that delivered synchronously would let a pump
      // recurse into its own completion and would never exercise the async paths.
      Promise.resolve().then(deliver);
    } else {
      const t = setTimeout(deliver, wait);
      if (typeof t.unref === 'function') t.unref();
    }
    return true;
  }

  get bulkBufferedAmount() { return this._bulkBuffered; }
  get bulkLowThreshold() { return this._bulk ? BULK_LOW_WATER : 0; }

  /** @returns {() => void} unsubscribe */
  onBulkMessage(fn) {
    if (typeof fn !== 'function') return () => {};
    this._bulkMessageListeners.add(fn);
    return () => this._bulkMessageListeners.delete(fn);
  }

  /** @returns {() => void} unsubscribe */
  onBulkStateChanged(fn) {
    if (typeof fn !== 'function') return () => {};
    this._bulkStateListeners.add(fn);
    return () => this._bulkStateListeners.delete(fn);
  }

  _setBulkState(state) {
    const next = state === 'open';
    if (next === this._bulkOpen) return;
    this._bulkOpen = next;
    emit(this._bulkStateListeners, state, this._log, this._tag, 'bulkStateChanged');
  }

  // ------------------------------------------------------------------ transport surface

  /** No server here: report the state change and hand back a fixed code. */
  async createInvite() {
    this.markConnected();
    return 'LOOPBK';
  }

  async join(_inviteCode) {
    this.markConnected();
    return true;
  }

  async close() {
    if (this.state === GoonTransportState.Closed) return;
    this._setBulkState('closed');
    this._setState(GoonTransportState.Closed, 'loopback closed');
    try { this._clock.stop(); } catch (_e) { /* already gone */ }
  }

  /** TEST AFFORDANCE: drop the bulk channel without touching the game channel (resume coverage). */
  dropBulkChannel() { this._setBulkState('closed'); }
  /** TEST AFFORDANCE: bring it back, as a reconnect inside the grace window does. */
  restoreBulkChannel() { if (this._bulk) this._setBulkState('open'); }

  async _sendRaw(json) {
    if (this.isDisposed) return;
    const peer = this._peer;
    if (!peer || peer.isDisposed) return;

    const now = Date.now();
    const jitter = this._options.jitterMs > 0 ? this._jitter.nextInt(0, this._options.jitterMs) : 0;
    let earliest = now + this._options.latencyMs + jitter;

    // Ordered channel: never deliver before the frame that went out ahead of this one.
    if (earliest < this._lastDeliverAtLocalMs) earliest = this._lastDeliverAtLocalMs;
    if (earliest < this._outageUntilLocalMs) earliest = this._outageUntilLocalMs;
    this._lastDeliverAtLocalMs = earliest;

    this._inFlight++;
    const wait = Math.max(0, earliest - Date.now());
    const deliver = () => {
      this._inFlight--;
      const p = this._peer;
      if (!p || p.isDisposed || this.isDisposed) return;
      p._handleIncomingRaw(json);
    };
    if (wait === 0) { deliver(); return; }
    const t = setTimeout(deliver, wait);
    if (typeof t.unref === 'function') t.unref();
  }

  // ------------------------------------------------------------------ harness helpers

  /** Flips this side to connected without any signaling. Used by the pair factory. */
  markConnected() {
    if (this.state === GoonTransportState.ConnectedP2P) return;
    // ConnectedP2P rather than a mode of its own: from the caller's point of view a loopback link
    // behaves exactly like a healthy direct channel.
    this._setState(GoonTransportState.ConnectedP2P, 'loopback link up');
    // The negotiated media channel opens with the connection, the same way the real one does.
    if (this._bulk) this._setBulkState('open');
  }

  /** Runs this side's clock sync. The pair factory awaits both. */
  syncClock() { return this._clock.sync(); }

  /**
   * Holds every frame from this side for `ms`, then delivers the backlog in order. Models a Wi-Fi
   * drop: nothing is lost, everything is late.
   */
  simulateOutage(ms) {
    this._outageUntilLocalMs = Date.now() + Math.max(0, ms);
    this._info(`Simulating a ${ms}ms outage`);
  }

  /** Frames written but not yet delivered. Handy in a test assertion. */
  get inFlightCount() { return this._inFlight; }

  _disposeCore() {
    this._peer = null;
    this._bulkMessageListeners.clear();
    this._bulkStateListeners.clear();
  }
}

/** Two connected transports and the clock sync that binds them. */
export class GoonLoopbackPair {
  constructor(host, guest) {
    this.host = host;
    this.guest = guest;
  }

  /**
   * Brings both sides up and runs both clock syncs concurrently. Await this before scheduling
   * anything: the scheduler refuses to fire on an unsynced clock, which is the single most common
   * cause of a match test doing nothing at all.
   */
  async connect() {
    this.host.markConnected();
    this.guest.markConnected();
    const results = await Promise.all([this.host.syncClock(), this.guest.syncClock()]);
    return results[0] && results[1];
  }

  /** Cuts delivery in both directions for a while — for exercising the wobbly/abandon UI. */
  simulateOutage(ms) {
    this.host.simulateOutage(ms);
    this.guest.simulateOutage(ms);
  }

  dispose() {
    try { this.host.dispose(); } catch (_e) { /* ignore */ }
    try { this.guest.dispose(); } catch (_e) { /* ignore */ }
  }
}

/** Builds two transports wired to each other. Call `connect()` next. */
export function createLoopbackPair(options = null) {
  const opts = options && options.latencyMs !== undefined ? options : loopbackOptions(options || {});
  const host = new GoonLoopbackTransport(true, opts);
  const guest = new GoonLoopbackTransport(false, opts);
  host._peer = guest;
  guest._peer = host;
  return new GoonLoopbackPair(host, guest);
}
