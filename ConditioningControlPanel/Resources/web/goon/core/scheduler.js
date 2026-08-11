// Fire-at-timestamp scheduler. Replaces MatchClock.ScheduledFire (C# slept per fire; a browser
// throttles timers in a background tab, so we poll instead of sleep).
//
// Two invariants, both load-bearing:
//  1. NEVER fire now. An instant with less than MinScheduleBufferMs of lead is REFUSED and logged
//     — firing it late would start one player's round after the other's, which is exactly the
//     unfairness the fire-at-timestamp rule exists to prevent.
//  2. Due-ness is always recomputed from the live clock, never accumulated from tick counts. A
//     3-second throttle stall self-corrects on the next tick, and a mid-flight clock resync moves
//     every pending fire with it (they're stored in match-ms).

import { GoonConsts } from './contracts.js';
import { localMonotonicMs } from './clock.js';

const hasRaf = () => typeof requestAnimationFrame === 'function' && typeof cancelAnimationFrame === 'function';

export class GoonScheduler {
  /**
   * @param {object} clock a MatchClock (needs nowMatchMs(); isSynced honored when present)
   * @param {object} [o]
   * @param {object} [o.logger] console-shaped
   * @param {number} [o.intervalMs] backstop poll cadence
   */
  constructor(clock, { logger = null, intervalMs = 200, tag = 'GoonSched' } = {}) {
    if (!clock || typeof clock.nowMatchMs !== 'function') throw new Error('GoonScheduler: clock required');
    this._clock = clock;
    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this._tag = tag;
    this._intervalMs = intervalMs;
    this._items = new Set();
    this._interval = null;
    this._raf = 0;
    this._disposed = false;
  }

  get pendingCount() { return this._items.size; }

  /**
   * @returns {object|null} handle, or null when refused (too little lead / clock unsynced).
   * Callers treat null as "drop this event and log it", never as "fire it now".
   */
  scheduleAt(fireAtMatchMs, fn, label = '') {
    if (this._disposed || typeof fn !== 'function') return null;

    if (this._clock.isSynced === false) {
      this._warn(`Refusing to schedule '${label}' — clock not synced`);
      return null;
    }

    const lead = fireAtMatchMs - this._clock.nowMatchMs();
    if (lead < GoonConsts.MinScheduleBufferMs) {
      this._warn(`Refusing to schedule '${label}' — only ${lead}ms lead (need ${GoonConsts.MinScheduleBufferMs}ms)`);
      return null;
    }

    const item = { fireAtMatchMs, fn, label, cancelled: false };
    this._items.add(item);
    this._ensureRunning();
    return item;
  }

  cancel(handle) {
    if (!handle) return false;
    handle.cancelled = true;
    const had = this._items.delete(handle);
    if (this._items.size === 0) this._stopDrivers();
    return had;
  }

  dispose() {
    this._disposed = true;
    this._items.clear();
    this._stopDrivers();
  }

  _ensureRunning() {
    if (this._disposed) return;
    if (!this._interval) {
      this._interval = setInterval(() => this._tick(), this._intervalMs);
      if (typeof this._interval.unref === 'function') this._interval.unref();
    }
    if (!this._raf && hasRaf()) {
      const step = () => {
        this._raf = 0;
        this._tick();
        if (!this._disposed && this._items.size > 0 && hasRaf()) this._raf = requestAnimationFrame(step);
      };
      this._raf = requestAnimationFrame(step);
    }
  }

  _stopDrivers() {
    if (this._interval) { clearInterval(this._interval); this._interval = null; }
    if (this._raf && hasRaf()) { cancelAnimationFrame(this._raf); }
    this._raf = 0;
  }

  _tick() {
    if (this._disposed || this._items.size === 0) return;
    const now = this._clock.nowMatchMs();

    let due = null;
    for (const item of this._items) {
      if (item.cancelled) continue;
      if (item.fireAtMatchMs - now <= 0) (due ||= []).push(item);
    }
    if (!due) return;

    // Ties fire in schedule order; the Set preserves insertion order.
    for (const item of due) {
      this._items.delete(item);
      if (item.cancelled) continue;
      const slip = now - item.fireAtMatchMs;
      if (slip > 250) this._info(`Scheduled '${item.label}' fired ${slip}ms late`);
      try {
        item.fn();
      } catch (e) {
        this._error(`Scheduled fire '${item.label}' failed: ${(e && e.stack) || e}`);
      }
    }

    if (this._items.size === 0) this._stopDrivers();
  }

  _info(m) { if (this._log && this._log.info) this._log.info(`[${this._tag}] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[${this._tag}] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[${this._tag}] ${m}`); }
}

/**
 * Delta-based repeating timer on the LOCAL monotonic clock (score ticks are local; only
 * synchronized events go through GoonScheduler). fn receives the ACTUAL elapsed ms since the last
 * fire, so a stalled tab produces one catch-up call with a large delta rather than a burst of
 * backdated ones.
 *
 * @returns {{stop:()=>void, reset:()=>void}}
 */
export function ticker(intervalMs, fn, { now = localMonotonicMs, logger = null, tag = 'GoonTicker' } = {}) {
  if (typeof fn !== 'function') throw new Error('ticker: fn required');
  const log = logger || (typeof console !== 'undefined' ? console : null);
  let last = now();
  let stopped = false;
  let raf = 0;

  const fire = () => {
    if (stopped) return;
    const t = now();
    const elapsed = t - last;
    if (elapsed < intervalMs) return;
    last = t;
    try {
      fn(elapsed);
    } catch (e) {
      if (log && log.error) log.error(`[${tag}] tick failed: ${(e && e.stack) || e}`);
    }
  };

  const interval = setInterval(fire, Math.max(16, Math.min(intervalMs, 200)));
  if (typeof interval.unref === 'function') interval.unref();

  if (hasRaf()) {
    const step = () => {
      raf = 0;
      fire();
      if (!stopped && hasRaf()) raf = requestAnimationFrame(step);
    };
    raf = requestAnimationFrame(step);
  }

  return {
    stop() {
      stopped = true;
      clearInterval(interval);
      if (raf && hasRaf()) cancelAnimationFrame(raf);
      raf = 0;
    },
    reset() { last = now(); },
  };
}
