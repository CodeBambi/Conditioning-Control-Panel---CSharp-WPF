// The points model (owner, 2026-09-24): SENDING pays about 70% of a typical match, YOUR OWN
// about 30%. Receiving is what people already want; sending is what needs the incentive.
//
// Pure state. No timers, no DOM, no wire. core/scoring.js owns one of these per player and
// core/match.js feeds it the three facts that exist on the wire today:
//
//   1. a receipt for OUR throw came back `completed` / `survived` (+ an optional `held` share)
//        -> SENT points: weight(kind) x held share. A rejected receipt scores nothing.
//   2. a throw from THEM finished on our screen (+ how much of it we held)
//        -> OWN points: weight(kind) x held share x OWN_HELD_RATE x the attention/risk chip.
//   3. we popped a bubble or a flash
//        -> a small OWN tick, lifted a little by the combo, and only inside the pop limiter.
//
// Duels are points only: winner takes DUEL_POT, loser (and each side of a tie) half of it.
//
// BALANCE IS ONE EDIT: every number is in POINTS below. test/selftest-scoring.js plays a scripted
// 10-minute match through this module and pins the 70/30 split and the duel pot at about 20%.

import { GoonPayloadKind } from './contracts.js';

/** The whole balance table. */
export const POINTS = Object.freeze({
  /** Sent points per fully held throw, by kind. Flash small; video, spiral, lock card bigger. */
  SEND_WEIGHT: Object.freeze({
    [GoonPayloadKind.FlashBurst]: 10,
    [GoonPayloadKind.SubliminalStorm]: 8,
    [GoonPayloadKind.BubbleSwarm]: 10,
    [GoonPayloadKind.ToyPattern]: 14,
    [GoonPayloadKind.Video]: 24,
    [GoonPayloadKind.Spiral]: 24,
    [GoonPayloadKind.LockCard]: 24,
    [GoonPayloadKind.BrainDrain]: 40,
  }),
  /** A kind this table has never heard of (a newer peer) still pays like a flash. */
  SEND_WEIGHT_DEFAULT: 10,
  /** Own points for holding a throw = its weight x held share x this. */
  OWN_HELD_RATE: 0.2,
  /** A `completed` receipt with no `held` share (older peer): an early close, partial. */
  LEGACY_COMPLETED_HELD: 0.6,
  /** Own points per pop, before the combo lift. */
  POP_TICK: 0.3,
  /** Each combo step adds this much to a pop, capped at COMBO_LIFT_MAX steps. */
  COMBO_STEP_LIFT: 0.08,
  COMBO_LIFT_MAX: 12,
  /** A pop more than this after the last one starts a new chain. */
  COMBO_GAP_MS: 1200,
  /** Pop limiter: a burst of POP_BURST, then one scoring pop per POP_GAP_MS. Past it pops are free. */
  POP_BURST: 8,
  POP_GAP_MS: 1000,
  /** The old 1 pt/s survival trickle, kept only as a whisper (x the chip). */
  TRICKLE_PER_S: 0.02,
  /** Duel pot. Winner takes it, the loser and each side of a tie get half. */
  DUEL_POT: 120,
  DUEL_LOSER_SHARE: 0.5,
  /** heat: lead that reads as full heat, and the combo that does. */
  HEAT_LEAD_FULL: 150,
  HEAT_COMBO_FULL: 10,
  HEAT_LEAD_SHARE: 0.6,
  /** Seconds for heat to move about 63% toward its target. */
  HEAT_TAU_S: 1.2,
});

function clamp01(v) { const n = Number(v); return !(n > 0) ? 0 : n > 1 ? 1 : n; }

export function sendWeight(kind) {
  const w = POINTS.SEND_WEIGHT[kind];
  return typeof w === 'number' ? w : POINTS.SEND_WEIGHT_DEFAULT;
}

/**
 * The held share a receipt stands for. An explicit `held` (0..1) wins; otherwise `survived` is
 * all of it and `completed` is the legacy partial.
 */
export function heldShareOf(status, held) {
  if (held !== null && held !== undefined && Number.isFinite(Number(held))) return clamp01(held);
  if (status === 'survived') return 1;
  if (status === 'completed') return POINTS.LEGACY_COMPLETED_HELD;
  return 0;
}

/** Duel points for one side, off that side's outcome. */
export function duelPoints(outcome) {
  if (outcome === 'win') return POINTS.DUEL_POT;
  return Math.round(POINTS.DUEL_POT * POINTS.DUEL_LOSER_SHARE);
}

/** Target heat 0..1 off the lead (my score minus theirs) and my live combo. */
export function heatTarget(lead, combo) {
  const l = clamp01((Number(lead) || 0) / POINTS.HEAT_LEAD_FULL);
  const c = clamp01((Number(combo) || 0) / POINTS.HEAT_COMBO_FULL);
  return clamp01(POINTS.HEAT_LEAD_SHARE * l + (1 - POINTS.HEAT_LEAD_SHARE) * c);
}

/** One smoothing step toward the target (exponential, frame-rate independent). */
export function smoothHeat(prev, target, dtS) {
  const k = 1 - Math.exp(-Math.max(0, Number(dtS) || 0) / POINTS.HEAT_TAU_S);
  return clamp01(prev + (clamp01(target) - prev) * k);
}

/**
 * One player's ledger. Every award returns `{points, type, ...}` (or null when it scored nothing),
 * so the HUD can float a +N at its source.
 */
export class PointsLedger {
  constructor() { this.reset(); }

  reset() {
    this.sentPts = 0;
    this.ownPts = 0;
    this.duelPts = 0;
    this.sent = { landed: 0, held: 0, byKind: {} };
    this.received = { held: 0, byKind: {} };
    this.pops = 0;
    this.combo = 0;
    this.bestCombo = 0;
    this.duels = { won: 0, lost: 0, tied: 0, points: 0 };
    this._lastPopMs = -Infinity;
    this._popTokens = POINTS.POP_BURST;
    this._popRefillMs = null;
  }

  get total() { return this.sentPts + this.ownPts + this.duelPts; }

  /** Our throw landed on them. */
  landed(kind, held) {
    const share = clamp01(held);
    const points = sendWeight(kind) * share;
    this.sent.landed++;
    this.sent.held += share;
    const k = String(kind);
    this.sent.byKind[k] = (this.sent.byKind[k] || 0) + 1;
    if (!(points > 0)) return null;
    this.sentPts += points;
    return { type: 'hit', points, kind, held: share };
  }

  /** Their throw finished on us. `mult` is the attention x risk chip. */
  held(kind, held, mult = 1) {
    const share = clamp01(held);
    const points = sendWeight(kind) * share * POINTS.OWN_HELD_RATE * (Number(mult) > 0 ? Number(mult) : 1);
    this.received.held += share;
    const k = String(kind);
    this.received.byKind[k] = (this.received.byKind[k] || 0) + 1;
    if (!(points > 0)) return null;
    this.ownPts += points;
    return { type: 'held', points, kind, held: share };
  }

  lockBounty(points) {
    const prize = Math.max(0, Math.min(120, Math.round(Number(points) || 0)));
    if (!prize) return null;
    this.ownPts += prize;
    return { type: 'held', points: prize, kind: GoonPayloadKind.LockCard };
  }

  /** A pop at monotonic `nowMs`. Combo counts every pop; only pops inside the limiter score. */
  pop(nowMs, mult = 1) {
    const now = Number(nowMs) || 0;
    this.pops++;
    this.combo = (now - this._lastPopMs <= POINTS.COMBO_GAP_MS) ? this.combo + 1 : 1;
    this._lastPopMs = now;
    if (this.combo > this.bestCombo) this.bestCombo = this.combo;
    if (!this._admitPop(now)) return { type: 'pop', points: 0, combo: this.combo };
    const lift = 1 + POINTS.COMBO_STEP_LIFT * Math.min(this.combo - 1, POINTS.COMBO_LIFT_MAX);
    const points = POINTS.POP_TICK * lift * (Number(mult) > 0 ? Number(mult) : 1);
    this.ownPts += points;
    return { type: 'pop', points, combo: this.combo };
  }

  /** The live combo, 0 once the gap has passed. */
  comboAt(nowMs) {
    return (Number(nowMs) || 0) - this._lastPopMs <= POINTS.COMBO_GAP_MS ? this.combo : 0;
  }

  /** How much of the combo window is left, 1 right after a pop, 0 once the chain has lapsed. */
  comboLeft(nowMs) {
    const t = 1 - ((Number(nowMs) || 0) - this._lastPopMs) / POINTS.COMBO_GAP_MS;
    return t > 1 ? 1 : t > 0 ? t : 0;
  }

  trickle(seconds, mult = 1) {
    if (!(seconds > 0)) return 0;
    const p = seconds * POINTS.TRICKLE_PER_S * (Number(mult) > 0 ? Number(mult) : 1);
    this.ownPts += p;
    return p;
  }

  duel(outcome) {
    const points = duelPoints(outcome);
    if (outcome === 'win') this.duels.won++;
    else if (outcome === 'lose') this.duels.lost++;
    else this.duels.tied++;
    this.duels.points += points;
    this.duelPts += points;
    return { type: 'duel', points, outcome };
  }

  /** The recap shape. `score` is filled by the owner (GoonScoring floors the exact total). */
  stats(score) {
    return {
      score: Math.floor(Number.isFinite(score) ? score : this.total),
      sent: { landed: this.sent.landed, held: round2(this.sent.held), byKind: Object.assign({}, this.sent.byKind) },
      received: { held: round2(this.received.held), byKind: Object.assign({}, this.received.byKind) },
      pops: this.pops,
      bestCombo: this.bestCombo,
      duels: { won: this.duels.won, lost: this.duels.lost, points: this.duels.points },
      split: { sent: Math.round(this.sentPts), own: Math.round(this.ownPts), duel: Math.round(this.duelPts) },
    };
  }

  /** Compact form for the optional `sc` field on the state tick. */
  wire(nowMs) {
    return {
      s: Math.round(this.sentPts), o: Math.round(this.ownPts), d: Math.round(this.duelPts),
      p: this.pops, b: this.bestCombo, c: this.comboAt(nowMs),
    };
  }

  _admitPop(now) {
    if (this._popRefillMs === null) this._popRefillMs = now;
    const dt = now - this._popRefillMs;
    if (dt > 0) {
      this._popRefillMs = now;
      this._popTokens = Math.min(POINTS.POP_BURST, this._popTokens + dt / POINTS.POP_GAP_MS);
    }
    if (this._popTokens < 1) return false;
    this._popTokens -= 1;
    return true;
  }
}

function round2(v) { return Math.round(v * 100) / 100; }

/** A peer's `sc` tick field, untrusted, made safe to draw. null when absent or unusable. */
export function readWireStats(sc) {
  if (!sc || typeof sc !== 'object' || Array.isArray(sc)) return null;
  const n = (v, hi) => { const x = Math.trunc(Number(v)); return Number.isFinite(x) && x > 0 ? Math.min(x, hi) : 0; };
  return { s: n(sc.s, 1e7), o: n(sc.o, 1e7), d: n(sc.d, 1e7), p: n(sc.p, 1e6), b: n(sc.b, 1e5), c: n(sc.c, 1e5) };
}
