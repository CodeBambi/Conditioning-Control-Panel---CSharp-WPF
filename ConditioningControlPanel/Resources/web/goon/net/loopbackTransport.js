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

import { GoonTransportState } from '../core/contracts.js';
import { GoonRng } from '../core/rng.js';
import { GoonTransportBase } from './transportBase.js';

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
    this._setState(GoonTransportState.Closed, 'loopback closed');
    try { this._clock.stop(); } catch (_e) { /* already gone */ }
  }

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

  _disposeCore() { this._peer = null; }
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
