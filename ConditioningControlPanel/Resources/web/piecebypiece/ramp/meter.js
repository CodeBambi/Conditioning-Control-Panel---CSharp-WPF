/* ============================================================================
 * ramp/meter.js - the intensity model for Piece by Piece.
 *
 * One number per side, 0..1, built from three inputs: how little clock is left,
 * how many pieces that side has TAKEN, and how many it has LOST. Taking ramps
 * you harder than losing (captureWeight 1.5 vs lossWeight 1.0) - the player who
 * is winning on the board gets the worse screen. That number is the PRESSURE.
 *
 * THINKING MAKES IT WORSE (owner, 2026-10-01): on your own move the screen
 * climbs toward full the longer you sit on it. To play well you have to think,
 * and thinking pulls you under.
 *
 * NO FLOOR, AND A BREATH (owner, 2026-10-02, supersedes the floor): the moment
 * you move, everything for you stops (pictures, wash, heartbeat, whispers) and
 * stays stopped through the other side's turn, until your own turn card. Then
 * the climb starts again from nothing. The pressure no longer holds the screen
 * up between moves; it makes the climb FASTER (thinkRate). The climb is slower
 * than before and the player picks its pace (thinkFullMs).
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
  // Owner (2026-10-02): "keep the pieces and the grid always visible or we
  // can't play". The pink wash and the full-screen pictures came down by about
  // half again (melt .30 -> .14, spiral .32 -> .16, overlay .22 -> .11, veil
  // budget .40 -> .22), and the blur on the board is capped under a pixel
  // (3 -> 0.8): the board softens, a piece never smears. The board mask
  // (layers/boardmask.js) thins what is left over the board's footprint.
  melt: Object.freeze({ minAlpha: 0.06, maxAlpha: 0.14 }),
  blurMaxPx: 0.8,
  // THE VEIL BUDGET. The two full-screen gif veils (spiral, overlay) may cover
  // this much between them and no more, and they step back to `cardVeilDamp` of
  // that while a video card is over the board. Without it the top of the ramp
  // is three walls at once and the board stops existing, which is a different
  // game to the one being played.
  veilBudget: 0.22,
  cardVeilDamp: 0.45,          // HARD CAP: a move must always stay physically possible
  spiral: Object.freeze({ minAlpha: 0.08, maxAlpha: 0.16, minHoldMs: 1400, maxHoldMs: 5200, gapMs: 9000 }),
  overlay: Object.freeze({ minAlpha: 0.05, maxAlpha: 0.11 }),

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

  // --- the think ramp -------------------------------------------------------
  // curve > 1 eases in: a quick move stays clean, a long think piles on at the
  // end. fullMs is the Normal pace; the player's Ramp pick swaps it (SPEEDS).
  // Owner 2026-10-02: 20 s was too fast, Normal is 40 s now and Fast is about
  // the old pace. The wash rises once a think passes cardAt of the full climb.
  // pressureRate: a full pressure meter (clock + captures) climbs this much
  // faster. wakeAfterMs: no turn card came (the clock is under 10 s, or the
  // card never shows) so the breath ends after this much of the player's own
  // live turn. maxStepMs: one beat never adds more than this, so a hidden tab
  // cannot bank a full ramp.
  think: Object.freeze({ fullMs: 40000, curve: 1.5, cardAt: 0.6, cardGapMs: 900, snapMs: 600, maxStepMs: 250,
    pressureRate: 0.6, wakeAfterMs: 3000 }),

  // --- the fall: a local loss brings everything up at once, then drains -------
  surge: Object.freeze({ holdMs: 1200, drainMs: 1400 }),
});

/** The player's Ramp pick -> how long the climb takes to full, in ms. */
export const SPEEDS = Object.freeze({ slow: 70000, normal: 40000, fast: 22000 });
export const thinkFullMs = (speed, tuning = RAMP_TUNING) => SPEEDS[speed] || tuning.think.fullMs;

/** The player's Amount pick -> how many pictures pop, as a rate on the one-shots. */
export const AMOUNTS = Object.freeze({ less: 0.5, normal: 1, more: 1.6 });
export const amountRate = (amount) => AMOUNTS[amount] || 1;

/** How far a think of `ms` lifts the screen toward full, 0..1. */
export function thinkLift(ms, tuning = RAMP_TUNING, fullMs = tuning.think.fullMs) {
  return Math.pow(clamp01(ms / fullMs), tuning.think.curve);
}

/** How much faster the climb runs under this much match pressure. */
export const thinkRate = (pressure, tuning = RAMP_TUNING) => 1 + tuning.think.pressureRate * clamp01(pressure);

/** The loss surge at `ms` after game over: full for holdMs, then a straight drain to nothing. */
export function surgeLevel(ms, tuning = RAMP_TUNING) {
  const { holdMs, drainMs } = tuning.surge;
  if (!(ms >= 0)) return 0;
  if (ms <= holdMs) return 1;
  return clamp01(1 - (ms - holdMs) / drainMs);
}

/**
 * createThinkClock({ tuning, fullMs }) - how far into this move's climb we are.
 *
 * The ramp advances it once a beat, and only counts a beat when the mover is
 * looking at a live board (their move, no pause card, no full-screen replay)
 * and is not taking the breath after their last move. reset() is the move.
 */
export function createThinkClock({ tuning = RAMP_TUNING, fullMs = () => tuning.think.fullMs } = {}) {
  let ms = 0;
  return {
    get ms() { return ms; },
    get fullMs() { return fullMs(); },
    lift: () => thinkLift(ms, tuning, fullMs()),
    /** `rate` > 1 is match pressure: the same seconds climb further. */
    advance(dtMs, counting, rate = 1) {
      if (counting && dtMs > 0) ms += Math.min(dtMs, tuning.think.maxStepMs) * Math.max(1, rate);
      return ms;
    },
    reset() { ms = 0; },
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
