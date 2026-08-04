// The shared match clock — port of Services/GoonGame/MatchClock.cs.
//
// NTP-style offset between the two players' monotonic clocks. The rule everything rests on:
// nothing synchronized ever fires ON MESSAGE ARRIVAL. The sender picks an instant at least
// MinScheduleBufferMs in the future, both clients convert it through their own offset, and
// latency stops mattering.
//
// Roles: the HOST's clock IS the match clock (offset pinned at 0); the GUEST measures its offset.
// Both run ping rounds — the host needs the RTT for scheduler lead and needs proof the peer
// answers before anything may be scheduled.
//
// Time base: performance.now(), monotonic. Wall clock is never consulted; an NTP correction or a
// DST flip mid-match must not move a scheduled payload.

import { GoonConsts, makeClockPing, makeClockPong } from './contracts.js';

const PING_SPACING_MS = 120;
const PING_TIMEOUT_MS = 3000;
const MIN_USABLE_SAMPLES = 3;
// A resync whose RTT is wildly worse than the best we've ever seen is a congestion blip, not a
// clock correction. Sampling it would drag the offset around for no reason.
const RESYNC_RTT_REJECT_FACTOR = 4.0;

const monotonic = (typeof performance !== 'undefined' && typeof performance.now === 'function')
  ? () => performance.now()
  : (() => { const t0 = Date.now(); return () => Date.now() - t0; })();

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** C# Math.Round is banker's rounding; offsets are halves of integer sums, so .5 really occurs. */
function roundHalfToEven(x) {
  const f = Math.floor(x);
  const diff = x - f;
  if (diff > 0.5) return f + 1;
  if (diff < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

export class MatchClock {
  /**
   * @param {object} o
   * @param {boolean} o.isClockMaster true for the host — a master pins its offset at 0
   * @param {string} [o.tag] log prefix
   * @param {(msg:object)=>any} [o.sendPing] outbound path; may also be supplied later via attach()
   * @param {object} [o.logger] console-shaped
   * @param {number} [o.testSkewMs] TEST ONLY: fake local skew so a loopback pair must actually converge
   * @param {number} [o.pingTimeoutMs] how long after the burst a pong may still land and count.
   *   The default fits a data channel. A RELAY transport must pass its own: one mailbox
   *   round-trip is up to two long-poll holds, so a 3 s window scores every pong as
   *   unanswered and the sync never converges (2026-08-04 play-test, "0 usable samples"
   *   every round) — which matters, because the host refuses to propose a start on an
   *   unsynced clock (core/match.js _tryProposeStart).
   * @param {number} [o.pingSpacingMs] gap between pings in a burst — same story, relay passes longer.
   */
  constructor({ isClockMaster = false, tag = 'GoonClock', sendPing = null, logger = null, testSkewMs = 0,
    pingTimeoutMs = PING_TIMEOUT_MS, pingSpacingMs = PING_SPACING_MS } = {}) {
    this._isClockMaster = !!isClockMaster;
    this._tag = tag;
    this._send = sendPing;
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this._testSkewMs = testSkewMs | 0;
    this._pingTimeoutMs = Math.max(100, Number(pingTimeoutMs) || PING_TIMEOUT_MS);
    this._pingSpacingMs = Math.max(10, Number(pingSpacingMs) || PING_SPACING_MS);

    this._pending = new Map();      // seq -> local send ms
    this._samples = [];             // {rtt, offset}
    this._seq = 0;
    this._offsetMs = 0;
    this._rttMs = 0;
    this._bestRttMs = Infinity;
    this._synced = false;
    this._stopped = false;
    this._disposed = false;
    this._resyncTimer = null;
    this._syncedListeners = new Set();
  }

  // ------------------------------------------------------------------ time

  /** Monotonic local ms. Never comparable to another machine's value. */
  nowLocalMs() {
    return Math.floor(monotonic()) + this._testSkewMs;
  }

  /** The shared axis every *_match_ms field lives on. */
  nowMatchMs() {
    return this.nowLocalMs() + this._offsetMs;
  }

  matchMsToLocal(matchMs) {
    return matchMs - this._offsetMs;
  }

  get isSynced() { return this._synced; }
  get rttMs() { return this._rttMs; }
  get offsetMs() { return this._offsetMs; }
  get isClockMaster() { return this._isClockMaster; }
  get tag() { return this._tag; }

  /** @returns {() => void} unsubscribe */
  onSynced(fn) {
    if (typeof fn !== 'function') return () => {};
    this._syncedListeners.add(fn);
    return () => this._syncedListeners.delete(fn);
  }

  // ------------------------------------------------------------------ wiring

  /** Hands the clock its outbound path. The transport calls this before sync(). */
  attach(sendPing) {
    if (typeof sendPing !== 'function') throw new Error('attach: sendPing must be a function');
    this._send = sendPing;
  }

  /**
   * Offer an inbound message to the clock. Returns true if the clock consumed it, in which case
   * the transport MUST NOT surface it — clock traffic is private to this class.
   */
  tryHandleMessage(msg) {
    if (!msg) return false;
    if (msg.t === 'clock_ping') {
      // Answer immediately: a queued pong is a lie about when we saw the ping and lands as a
      // bogus offset on the far side.
      const pong = this.handlePing(msg);
      try {
        const send = this._send;
        if (send) Promise.resolve(send(pong)).catch((e) => this._warn(`Failed to answer clock ping ${msg.seq}: ${e && e.message}`));
      } catch (e) {
        this._warn(`Failed to answer clock ping ${msg.seq}: ${e && e.message}`);
      }
      return true;
    }
    if (msg.t === 'clock_pong') {
      this.handlePong(msg);
      return true;
    }
    return false;
  }

  /** Builds the pong for an inbound ping. Pure — the caller decides how it goes out. */
  handlePing(ping) {
    return makeClockPong({
      seq: ping.seq,
      echo_sent_local_ms: ping.sent_local_ms,
      pong_local_ms: this.nowLocalMs(),
    });
  }

  handlePong(pong) {
    if (!pong || !this._pending.has(pong.seq)) return;   // late or duplicate
    const t0 = this._pending.get(pong.seq);
    this._pending.delete(pong.seq);

    const t3 = this.nowLocalMs();
    const rtt = t3 - t0;
    if (rtt < 0) return;

    // Classic NTP: assume symmetric legs, so the peer's clock read t1 at our local midpoint.
    const offset = pong.pong_local_ms - (t0 + t3) / 2.0;
    this._samples.push({ rtt, offset });
  }

  // ------------------------------------------------------------------ sync

  /**
   * Runs ClockPingRounds ping/pong rounds, adopts the result, then arms the periodic resync.
   * Safe to call once per channel open; a second call just re-syncs.
   */
  async sync() {
    if (this._disposed) return false;
    if (!this._send) {
      this._warn('sync() with no transport attached');
      return false;
    }
    this._stopped = false;
    const ok = await this._runRounds(GoonConsts.ClockPingRounds);
    if (ok) this._armResync();
    return ok;
  }

  async _runRounds(rounds) {
    this._samples.length = 0;
    this._pending.clear();

    for (let i = 0; i < rounds && !this._stopped && !this._disposed; i++) {
      const seq = ++this._seq;
      const ping = makeClockPing({ seq, sent_local_ms: this.nowLocalMs() });
      this._pending.set(seq, ping.sent_local_ms);

      try {
        const send = this._send;
        if (!send) return false;
        await send(ping);
      } catch (e) {
        this._warn(`Clock ping ${seq} failed to send: ${e && e.message}`);
        this._pending.delete(seq);
        continue;
      }

      await delay(this._pingSpacingMs);
    }

    // Give the last few pongs a chance to land before scoring the burst.
    const deadline = this.nowLocalMs() + this._pingTimeoutMs;
    while (this.nowLocalMs() < deadline && !this._stopped && !this._disposed) {
      if (this._pending.size === 0) break;
      await delay(50);
    }

    return this._adopt();
  }

  /** Turns the collected samples into an offset + RTT. False if too few landed. */
  _adopt() {
    const stale = this._pending.size;
    this._pending.clear();

    if (this._samples.length < MIN_USABLE_SAMPLES) {
      this._warn(`Clock sync got only ${this._samples.length} usable samples (${stale} unanswered) — not adopting`);
      return false;
    }

    const byRtt = this._samples.slice().sort((a, b) => a.rtt - b.rtt);
    const medianRtt = byRtt[Math.floor(byRtt.length / 2)].rtt;

    // Score only the better half: a sample whose RTT was inflated by a queue on one leg has an
    // asymmetric — i.e. wrong — offset. Median of what's left discards the rest.
    const keep = Math.max(MIN_USABLE_SAMPLES, Math.floor(byRtt.length / 2));
    const best = byRtt.slice(0, keep).map((s) => s.offset).sort((a, b) => a - b);
    const medianOffset = best[Math.floor(best.length / 2)];

    if (this._synced && this._bestRttMs < Infinity && byRtt[0].rtt > this._bestRttMs * RESYNC_RTT_REJECT_FACTOR) {
      this._info(`Rejecting resync — best RTT ${byRtt[0].rtt.toFixed(0)}ms vs baseline ${this._bestRttMs.toFixed(0)}ms`);
      this._samples.length = 0;
      return false;
    }

    this._bestRttMs = Math.min(this._bestRttMs, byRtt[0].rtt);
    this._rttMs = medianRtt;

    // The host IS the match clock; adopting a non-zero offset would make it drift from itself.
    const newOffset = this._isClockMaster ? 0 : roundHalfToEven(medianOffset);
    const delta = newOffset - this._offsetMs;
    this._offsetMs = newOffset;

    const firstSync = !this._synced;
    this._synced = true;
    this._samples.length = 0;

    if (!firstSync && Math.abs(delta) > 50) {
      // Not an error: pending fires are stored in match-ms and re-read the clock, so the
      // correction lands on them too. Logged because a big jump matters in a desync report.
      this._info(`Clock corrected by ${delta}ms (offset ${newOffset}ms, rtt ${medianRtt.toFixed(0)}ms)`);
    }

    if (firstSync) {
      this._info(`Clock synced: offset=${newOffset}ms rtt=${medianRtt.toFixed(0)}ms master=${this._isClockMaster}`);
      for (const fn of Array.from(this._syncedListeners)) {
        try { fn(this); } catch (e) { this._warn(`Synced listener threw: ${e && e.message}`); }
      }
    }
    return true;
  }

  _armResync() {
    if (this._disposed || this._resyncTimer) return;
    this._resyncTimer = setInterval(() => {
      if (this._disposed || this._stopped) return;
      this._runRounds(GoonConsts.ClockPingRounds).catch((e) => this._warn(`Clock resync failed: ${e && e.message}`));
    }, GoonConsts.ClockResyncIntervalMs);
    if (typeof this._resyncTimer.unref === 'function') this._resyncTimer.unref();
  }

  // ------------------------------------------------------------------ scheduling helpers

  /**
   * A shared-clock instant that is safely schedulable right now: the minimum buffer, plus a full
   * RTT (so the peer still has a buffer's worth of lead after the message flies), plus extra.
   * Every fire_at_match_ms should be built through this, never hand-rolled "now + 1000".
   */
  safeFireAt(extraMs = 0) {
    const rtt = Math.trunc(Math.min(this._rttMs, 5000));
    return this.nowMatchMs() + GoonConsts.MinScheduleBufferMs + rtt + Math.max(0, extraMs);
  }

  /** True if the instant still has the minimum buffer left. */
  isFireable(fireAtMatchMs) {
    return fireAtMatchMs - this.nowMatchMs() >= GoonConsts.MinScheduleBufferMs;
  }

  // ------------------------------------------------------------------ lifetime

  /** Stops pings and resyncs. The clock keeps reading time — the recap still wants it. */
  stop() {
    this._stopped = true;
    if (this._resyncTimer) { clearInterval(this._resyncTimer); this._resyncTimer = null; }
    this._pending.clear();
  }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;
    this.stop();
    this._send = null;
    this._syncedListeners.clear();
  }

  _info(m) { if (this._log && this._log.info) this._log.info(`[${this._tag}] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[${this._tag}] ${m}`); }
}

export { monotonic as localMonotonicMs };
