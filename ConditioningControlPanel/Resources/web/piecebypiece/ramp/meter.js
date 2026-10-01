/* ============================================================================
 * ramp/meter.js - the intensity model for Piece by Piece.
 *
 * One number per side, 0..1, built from three inputs: how little clock is left,
 * how many pieces that side has TAKEN, and how many it has LOST. Taking ramps
 * you harder than losing (captureWeight 1.5 vs lossWeight 1.0) - the player who
 * is winning on the board gets the worse screen. Both sides end up melted.
 * That number is the FLOOR.
 *
 * THINKING MAKES IT WORSE (owner, 2026-10-01): on your own move the screen
 * climbs from that floor toward full the longer you sit on it (full at 20 s),
 * and the moment the move is made it drops straight back to the floor. To play
 * well you have to think, and thinking pulls you under.
 *
 * Pure and deterministic: every function takes the clock/counters it needs and
 * `now` is passed in, so smoke tests replay the same numbers without a timer.
 * ==========================================================================*/

export const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export const clamp01 = (v) => clamp(Number.isFinite(v) ? v : 0, 0, 1);
export const lerp = (a, b, t) => a + (b - a) * clamp01(t);

/** EVERY tunable in the ramp lives here. Layers read it, nothing hardcodes. */
export const RAMP_TUNING = Object.freeze({
  // --- the meter itself -----------------------------------------------------
  clockWeight: 0.35,     // full weight of a fully drained clock
  perPieceStep: 0.10,    // one piece of movement on the meter
  captureWeight: 1.5,    // taking ramps you MORE than losing (owner rule)
  lossWeight: 1.0,

  // --- the capture kick: a decaying bump on top of the meter, both sides -----
  burst: Object.freeze({ taker: 0.30, victim: 0.15, decayMs: 2600 }),

  // --- sustained unlock thresholds (on the meter, not the kick) -------------
  unlock: Object.freeze({ melt: 0.25, blur: 0.40, spiral: 0.55, overlay: 0.70 }),

  // --- one-shot cadence bands (ms between spawns, slow at 0 heat) -----------
  // centreAlpha / centreInner / centreOuter (Mort, 2026-09-28): a flash may land
  // over the board, but the nearer the centre of the screen it lands the more
  // see-through it is, so the move underneath stays readable. Distance is 0 at
  // the centre and 1 at the middle of a screen edge.
  flash: Object.freeze({ slowMs: 4200, fastMs: 520, maxLive: 6, centreAlpha: 0.22, centreInner: 0.16, centreOuter: 0.6 }),
  gifRain: Object.freeze({ slowMs: 3000, fastMs: 620, maxLive: 12, fallMsMin: 2400, fallMsMax: 3900 }),

  // --- sustained layer envelopes -------------------------------------------
  // Mort (2026-09-28): the early stages were right, full tilt was unreadable.
  // The top of every full-screen layer came down; the bottom barely moved.
  melt: Object.freeze({ minAlpha: 0.10, maxAlpha: 0.30 }),
  blurMaxPx: 3,
  // THE VEIL BUDGET. The two full-screen gif veils (spiral, overlay) may cover
  // this much between them and no more, and they step back to `cardVeilDamp` of
  // that while a video card is over the board. Without it the top of the ramp
  // is three walls at once and the board stops existing, which is a different
  // game to the one being played.
  veilBudget: 0.40,
  cardVeilDamp: 0.45,          // HARD CAP: a move must always stay physically possible
  spiral: Object.freeze({ minAlpha: 0.14, maxAlpha: 0.32, minHoldMs: 1400, maxHoldMs: 5200, gapMs: 9000 }),
  overlay: Object.freeze({ minAlpha: 0.08, maxAlpha: 0.22 }),

  // --- the video card + the drag glitch ------------------------------------
  videoCard: Object.freeze({ minHoldSec: 4, maxHoldSec: 13, riseMs: 620, startJitter: 0.7 }),
  // The sticker is measured against the BOARD, not the window: squareShare of
  // one square's on-screen width, clamped. A fixed pixel size was tuned in a
  // board-free harness and came out taller than a real piece, which turned the
  // sticker into a tile floating over the board.
  glitchGrab: Object.freeze({ sizePx: 72, alpha: 0.85, squareShare: 0.62, minPx: 44, maxPx: 110 }),

  // --- what we hand back to the board (A's side, always guarded) -----------
  wobbleScale: 1.0,
  swayScale: 1.0,

  // --- the effect-free share, ported from the Arcademy plainShare ramp ------
  plain: Object.freeze({ early: 0.80, floor: 0.30 }),

  // --- the think ramp (owner, 2026-10-01: "20 sec to the max") ---------------
  // curve > 1 eases in: a quick move stays clean, a long think piles on at the
  // end (5 s 0.13, 10 s 0.35, 15 s 0.65, 20 s 1). The wash rises once a think
  // passes cardAtMs and rides until the move. snapFrom: a move only snaps the
  // screen down when the think had lifted it at least this much. maxStepMs: one
  // beat never adds more than this, so a hidden tab cannot bank a full ramp.
  think: Object.freeze({ fullMs: 20000, curve: 1.5, cardAtMs: 12000, cardGapMs: 900, snapFrom: 0.08, snapMs: 600, maxStepMs: 250 }),

  // --- the fall: a local loss brings everything up at once, then drains -------
  surge: Object.freeze({ holdMs: 1200, drainMs: 1400 }),
});

/** How far a think of `ms` lifts the screen from its floor toward full, 0..1. */
export function thinkLift(ms, tuning = RAMP_TUNING) {
  const { fullMs, curve } = tuning.think;
  return Math.pow(clamp01(ms / fullMs), curve);
}

/** The floor, lifted `lift` of the way to a full meter. */
export const liftMeter = (floor, lift) => clamp01(clamp01(floor) + (1 - clamp01(floor)) * clamp01(lift));

/** The loss surge at `ms` after game over: full for holdMs, then a straight drain to nothing. */
export function surgeLevel(ms, tuning = RAMP_TUNING) {
  const { holdMs, drainMs } = tuning.surge;
  if (!(ms >= 0)) return 0;
  if (ms <= holdMs) return 1;
  return clamp01(1 - (ms - holdMs) / drainMs);
}

/**
 * createThinkClock({ tuning }) - how long the mover has been sitting on this move.
 *
 * The ramp advances it once a beat, and only counts a beat when the mover is
 * looking at a live board (their move, no pause card, no full-screen replay).
 * reset() is the move: back to zero, and true when that drop should snap.
 */
export function createThinkClock({ tuning = RAMP_TUNING } = {}) {
  let ms = 0;
  return {
    get ms() { return ms; },
    lift: () => thinkLift(ms, tuning),
    advance(dtMs, counting) {
      if (counting && dtMs > 0) ms += Math.min(dtMs, tuning.think.maxStepMs);
      return ms;
    },
    reset() {
      const snap = thinkLift(ms, tuning) >= tuning.think.snapFrom;
      ms = 0;
      return snap;
    },
  };
}

/** Share of beats that must stay effect-free, so effects read as a spill and
 *  not as a metronome. Ported from arcademy/engine/curves.js plainShare(). */
export function plainShare(heat, tuning = RAMP_TUNING) {
  const { early, floor } = tuning.plain;
  return clamp01(early + (clamp01(floor) - early) * clamp01(heat));
}

const SIDES = ['w', 'b'];
const otherSide = (s) => (s === 'w' ? 'b' : 'w');

/**
 * createMeter({ tuning }) - the live model.
 *
 * `meterFor(side)` is the pure formula (clock + counters). `heatFor(side, now)`
 * is what the layers actually ride: the meter plus whatever is left of the last
 * capture kick. The two are kept apart so the unlock thresholds never flicker
 * on and off with a burst.
 */
export function createMeter({ tuning = RAMP_TUNING } = {}) {
  const t = tuning;
  const state = {
    clocks: { w: 0, b: 0 },
    total: 0,
    active: 'w',
    captures: { w: 0, b: 0 },   // pieces this side has TAKEN
    losses: { w: 0, b: 0 },     // pieces this side has LOST
    kick: { w: 0, b: 0 },       // capture burst amplitude at kickAt
    kickAt: { w: 0, b: 0 },
    ply: 0,
    over: false,
  };

  /** Fraction of this side's clock still on the board (1 when we have no clock). */
  function clockFrac(side) {
    if (!(state.total > 0)) return 1;
    return clamp01(state.clocks[side] / state.total);
  }

  /** The formula. clock 0.35 + 0.10*1.5 per capture + 0.10 per loss, clamped. */
  function meterFor(side) {
    const s = side === 'b' ? 'b' : 'w';
    return clamp01(
      t.clockWeight * (1 - clockFrac(s))
      + t.perPieceStep * t.captureWeight * state.captures[s]
      + t.perPieceStep * t.lossWeight * state.losses[s],
    );
  }

  /** What is left of the capture kick for `side` at `now` (linear decay). */
  function kickFor(side, now) {
    const s = side === 'b' ? 'b' : 'w';
    const amp = state.kick[s];
    if (!(amp > 0)) return 0;
    const age = now - state.kickAt[s];
    if (age >= t.burst.decayMs || age < 0) return 0;
    return amp * (1 - age / t.burst.decayMs);
  }

  /** meter + kick: the number every layer rides. */
  const heatFor = (side, now) => clamp01(meterFor(side) + kickFor(side, now));

  function setClock(payload) {
    if (!payload) return;
    if (Number.isFinite(payload.w)) state.clocks.w = payload.w;
    if (Number.isFinite(payload.b)) state.clocks.b = payload.b;
    if (Number.isFinite(payload.total) && payload.total > 0) state.total = payload.total;
    if (payload.active === 'w' || payload.active === 'b') state.active = payload.active;
  }

  function setTurn(payload) {
    if (!payload) return;
    if (payload.side === 'w' || payload.side === 'b') state.active = payload.side;
    if (Number.isFinite(payload.ply)) state.ply = payload.ply;
    if (payload.clocks) setClock({ ...payload.clocks, total: payload.total });
    else if (Number.isFinite(payload.total)) state.total = payload.total;
  }

  /** A capture: the taker gains a capture, the victim a loss, both get a kick. */
  function noteCapture(payload, now) {
    if (!payload) return;
    const taker = payload.by === 'b' ? 'b' : (payload.by === 'w' ? 'w' : null);
    const victim = payload.victimSide === 'b' ? 'b'
      : (payload.victimSide === 'w' ? 'w' : (taker ? otherSide(taker) : null));
    if (taker) { state.captures[taker] += 1; state.kick[taker] = t.burst.taker; state.kickAt[taker] = now; }
    if (victim) { state.losses[victim] += 1; state.kick[victim] = t.burst.victim; state.kickAt[victim] = now; }
  }

  function reset() {
    state.clocks = { w: 0, b: 0 }; state.total = 0; state.active = 'w';
    state.captures = { w: 0, b: 0 }; state.losses = { w: 0, b: 0 };
    state.kick = { w: 0, b: 0 }; state.kickAt = { w: 0, b: 0 };
    state.ply = 0; state.over = false;
  }

  /** Everything a debug panel or a smoke test wants, in one readable object. */
  function snapshot(now = 0) {
    const out = { active: state.active, ply: state.ply, total: state.total, over: state.over, sides: {} };
    for (const s of SIDES) {
      out.sides[s] = {
        clockMs: state.clocks[s], clockFrac: clockFrac(s),
        captures: state.captures[s], losses: state.losses[s],
        meter: meterFor(s), kick: kickFor(s, now), heat: heatFor(s, now),
      };
    }
    return out;
  }

  return {
    setClock, setTurn, noteCapture, reset, snapshot,
    meterFor, heatFor, kickFor, clockFrac,
    get active() { return state.active; },
    get over() { return state.over; },
    set over(v) { state.over = !!v; },
    get ply() { return state.ply; },
  };
}

export default createMeter;
