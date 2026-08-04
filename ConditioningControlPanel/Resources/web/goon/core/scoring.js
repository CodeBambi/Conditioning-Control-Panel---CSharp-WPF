// Score ticks, the charge economy, and the receiver-side payload rate limiter —
// port of Services/GoonGame/GoonScoring.cs (GoonScoring + GoonPayloadRateLimiter).
//
// Formula: 1 pt/s x (1 + DraftRiskStep x riskTier) x attention multiplier.
//   Cam   : rolling attention % -> AttentionMultMin .. AttentionMultMax
//   NoCam : 1.0 flat; a failed interaction check drops it to NoCamFailedCheckMult for 60 s.
//
// Attention % ARRIVES as an input (reportAttention). Nothing here reads a camera or a timer:
// match.js owns the 1 s tick and feeds real elapsed seconds in.

import { GoonAttentionMode, GoonConsts, costOf } from './contracts.js';
import { MAX_MATCH_RISK_TIER, riskMultiplier } from './draft.js';

/** The documented payload_receipt.status values. Keep receipts inside this set. */
export const GoonReceiptStatus = Object.freeze({
  Accepted: 'accepted',
  RejectedRate: 'rejected_rate',
  RejectedFiltered: 'rejected_filtered',
  Completed: 'completed',
  Survived: 'survived',
});

/** Rolling window over which the cam attention % is averaged. */
const ATTENTION_WINDOW_MS = 30000;

/** Cam mode: below this rolling attention the charge trickle pauses (no reset). */
const CLEAN_ATTENTION_FLOOR_PCT = 35.0;

function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }

/** C# Math.Round(double) is banker's rounding; attention halves really occur. */
function roundHalfToEven(x) {
  const f = Math.floor(x);
  const diff = x - f;
  if (diff > 0.5) return f + 1;
  if (diff < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

/**
 * One player's own score + charge meter. Pure state — no timers, no services.
 * C# `long.MinValue` sentinel for "never penalised" is -Infinity here.
 */
export class GoonScoring {
  /**
   * @param {number} mode GoonAttentionMode
   * @param {number} matchRiskTier summed draft risk
   * @param {object} [o]
   * @param {object} [o.logger] console-shaped
   */
  constructor(mode, matchRiskTier, { logger = null, tag = 'GoonScore' } = {}) {
    this._mode = mode;
    this._riskTier = clamp(matchRiskTier | 0, 0, MAX_MATCH_RISK_TIER);
    this._log = logger || null;
    this._tag = tag;

    this._attentionSamples = [];   // {atMs, pct}
    this._scoreExact = 0;
    this._elapsedMs = 0;
    this._cleanMs = 0;
    this._failedCheckUntilMs = -Infinity;

    this._charges = 0;
    this._chargesEarned = 0;
    this._chargesSpent = 0;
    this._failedChecks = 0;
    this._attentionPct = 100.0;

    this._chargeListeners = new Set();
  }

  get mode() { return this._mode; }
  get riskTier() { return this._riskTier; }
  get scoreExact() { return this._scoreExact; }
  get score() { return Math.floor(this._scoreExact); }
  get charges() { return this._charges; }
  get chargesEarned() { return this._chargesEarned; }
  get chargesSpent() { return this._chargesSpent; }
  get failedChecks() { return this._failedChecks; }

  /** Rolling attention 0..100. NoCam reports "check health" instead (100 clean, 60 penalised). */
  get attentionPct() { return this._attentionPct; }

  get riskMultiplier() { return riskMultiplier(this._riskTier); }

  /** Current attention/interaction multiplier applied to the per-second score. */
  get attentionMultiplier() {
    if (this._mode === GoonAttentionMode.NoCam) {
      return this._elapsedMs < this._failedCheckUntilMs ? GoonConsts.NoCamFailedCheckMult : 1.0;
    }
    const t = clamp(this._attentionPct / 100.0, 0.0, 1.0);
    return GoonConsts.AttentionMultMin + (GoonConsts.AttentionMultMax - GoonConsts.AttentionMultMin) * t;
  }

  /** @returns {() => void} unsubscribe */
  onChargesChanged(fn) {
    if (typeof fn !== 'function') return () => {};
    this._chargeListeners.add(fn);
    return () => this._chargeListeners.delete(fn);
  }

  /** Late binding for the consent/draft outcome (the draft can change until it locks). */
  configure(mode, matchRiskTier) {
    this._mode = mode;
    this._riskTier = clamp(matchRiskTier | 0, 0, MAX_MATCH_RISK_TIER);
  }

  /** Gaze-derived attention percentage (0..100). Ignored in NoCam mode. */
  reportAttention(pct) {
    if (this._mode !== GoonAttentionMode.Cam) return;
    const p = clamp(Number(pct) || 0, 0.0, 100.0);
    this._attentionSamples.push({ atMs: Math.trunc(this._elapsedMs), pct: p });
    this._trimAttentionWindow();
    this._attentionPct = this._rollingAttention();
  }

  /** NoCam mode: result of a periodic interaction check. A failure penalises for 60 s. */
  reportInteractionCheck(passed) {
    if (this._mode !== GoonAttentionMode.NoCam) return;
    if (passed) return;

    this._failedChecks++;
    this._failedCheckUntilMs = Math.trunc(this._elapsedMs) + GoonConsts.NoCamFailedCheckPenaltyMs;
    this._cleanMs = 0;   // "clean" streak broken -> charge trickle restarts
  }

  /**
   * One score tick. match.js passes the REAL elapsed seconds since the previous tick, so a
   * throttled tab catches up in one call instead of under-scoring.
   */
  tick(seconds) {
    if (!(seconds > 0)) return;
    this._elapsedMs += seconds * 1000.0;

    this._scoreExact += seconds * this.riskMultiplier * this.attentionMultiplier;

    if (this._mode === GoonAttentionMode.NoCam) {
      this._attentionPct = this._elapsedMs < this._failedCheckUntilMs ? 60.0 : 100.0;
    } else {
      this._trimAttentionWindow();
    }

    // Charge trickle: +1 per clean ChargeTrickleMs.
    const clean = this._mode === GoonAttentionMode.NoCam
      ? this._elapsedMs >= this._failedCheckUntilMs
      : this._attentionPct >= CLEAN_ATTENTION_FLOOR_PCT;

    if (clean) {
      this._cleanMs += seconds * 1000.0;
      if (this._cleanMs >= GoonConsts.ChargeTrickleMs) {
        this._cleanMs -= GoonConsts.ChargeTrickleMs;
        this._addCharge('trickle');
      }
    }
  }

  /** +1 charge for taking an incoming payload all the way to its end. */
  awardPayloadEndured() { this._addCharge('payload endured'); }

  /** +1 charge for winning a sudden-death round or a mini event. */
  awardEventWon() { this._addCharge('event won'); }

  /**
   * Spends the cost of a payload kind. C# `TrySpend(kind, out cost)` -> {ok, cost}; on failure
   * `cost` is what the kind REQUIRES, which is what the caller puts in its error string.
   */
  trySpend(kind) {
    const cost = costOf(kind);
    if (cost <= 0 || !Number.isFinite(cost)) return { ok: false, cost };
    if (this._charges < cost) return { ok: false, cost };

    this._charges -= cost;
    this._chargesSpent += cost;
    this._emitCharges();
    return { ok: true, cost };
  }

  /** Refund after a receiver-side rejection (rate/filter). Never exceeds the cap. */
  refund(cost) {
    if (!(cost > 0)) return;
    this._chargesSpent = Math.max(0, this._chargesSpent - cost);
    this._charges = Math.min(GoonConsts.ChargeCap, this._charges + cost);
    this._emitCharges();
  }

  snapshot() {
    return {
      score: this.score,
      scoreExact: this._scoreExact,
      charges: this._charges,
      attentionPct: roundHalfToEven(this._attentionPct),
      attentionMultiplier: this.attentionMultiplier,
      riskMultiplier: this.riskMultiplier,
      riskTier: this._riskTier,
      mode: this._mode,
      failedChecks: this._failedChecks,
      chargesEarned: this._chargesEarned,
      chargesSpent: this._chargesSpent,
    };
  }

  reset() {
    this._attentionSamples.length = 0;
    this._scoreExact = 0;
    this._elapsedMs = 0;
    this._cleanMs = 0;
    this._failedCheckUntilMs = -Infinity;
    this._charges = 0;
    this._chargesEarned = 0;
    this._chargesSpent = 0;
    this._failedChecks = 0;
    this._attentionPct = 100.0;
    this._emitCharges();
  }

  _addCharge(reason) {
    if (this._charges >= GoonConsts.ChargeCap) return;
    this._charges++;
    this._chargesEarned++;
    if (this._log && this._log.debug) this._log.debug(`[${this._tag}] charge +1 (${reason}) -> ${this._charges}`);
    this._emitCharges();
  }

  _emitCharges() {
    for (const fn of Array.from(this._chargeListeners)) {
      try { fn(this._charges); } catch { /* a HUD listener must never break scoring */ }
    }
  }

  _trimAttentionWindow() {
    const cutoff = Math.trunc(this._elapsedMs) - ATTENTION_WINDOW_MS;
    let drop = 0;
    while (drop < this._attentionSamples.length && this._attentionSamples[drop].atMs < cutoff) drop++;
    if (drop > 0) this._attentionSamples.splice(0, drop);
  }

  _rollingAttention() {
    if (this._attentionSamples.length === 0) return this._attentionPct;
    let sum = 0;
    for (const s of this._attentionSamples) sum += s.pct;
    return sum / this._attentionSamples.length;
  }
}

/**
 * Receiver-side payload admission: PayloadMinGapMs steady state with a burst of PayloadBurst.
 * Token bucket on a MONOTONIC local clock — enforced regardless of what the wire claims. The
 * sender runs one too, so a well-behaved client never gets rejected.
 */
export class GoonPayloadRateLimiter {
  constructor(minGapMs = GoonConsts.PayloadMinGapMs, burst = GoonConsts.PayloadBurst) {
    this._minGapMs = Math.max(1000, minGapMs);
    this._burst = Math.max(1, burst);
    this._tokens = this._burst;
    this._lastRefillMs = null;    // C# long.MinValue sentinel: "no baseline yet"
  }

  get tokens() { return this._tokens; }

  reset() {
    this._tokens = this._burst;
    this._lastRefillMs = null;
  }

  /** Consumes one token if available. nowLocalMs MUST be monotonic. */
  tryAdmit(nowLocalMs) {
    this._refill(nowLocalMs);
    if (this._tokens < 1.0) return false;
    this._tokens -= 1.0;
    return true;
  }

  /** Milliseconds until the next token, for UI ("next payload in ..."). */
  msUntilNextToken(nowLocalMs) {
    this._refill(nowLocalMs);
    if (this._tokens >= 1.0) return 0;
    return Math.ceil((1.0 - this._tokens) * this._minGapMs);
  }

  _refill(nowLocalMs) {
    if (this._lastRefillMs === null) {
      this._lastRefillMs = nowLocalMs;
      return;
    }
    const delta = nowLocalMs - this._lastRefillMs;
    if (delta <= 0) return;
    this._lastRefillMs = nowLocalMs;
    this._tokens = Math.min(this._burst, this._tokens + delta / this._minGapMs);
  }
}
