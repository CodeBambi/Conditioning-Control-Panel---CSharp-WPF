/* ============================================================================
 * director.js - the game brain of the Sissy Fall.
 *
 * Owns run state and turns bubble play into tunnel motion:
 *   - intensity: a temple-run ramp (0 -> 1 over ~6 min of run time) that raises
 *     both the bubble population/rise speed and the base fall speed
 *   - boost: pops and (especially) screen effects add speed that bleeds off in
 *     a few seconds - chained effects visibly accelerate the tube
 *   - misses: ignored in ASMR mode (default); in challenge mode 10 misses end
 *     the run
 *
 * fallNav asks getTargetSpeed() every frame; the scene forwards the bubble
 * callbacks into notePop/noteEffect/noteMiss and watches isOver().
 * ==========================================================================*/

const RAMP_SECONDS = 360;      // run time to reach full intensity
const BASE_SPEED_CALM = 3.5;   // u/s at intensity 0 (gentler start; ramps to HOT as the run deepens)
const BASE_SPEED_HOT = 16;     // u/s at intensity 1
const BOOST_POP = 0.6;         // + per popped bubble
const BOOST_LUCKY = 4;         // + per golden jackpot
const BOOST_EFFECT = 2.2;      // + per fired screen effect (chains compound)
const BOOST_CAP = 14;
const BOOST_HALFLIFE = 3.5;    // seconds for a boost to bleed to ~37%
const EARLY_SLOW_SECONDS = 30; // the fall stays gentle for this long: scroll/pop boost is throttled at the top of the run, then opens fully
const EARLY_BOOST_FLOOR = 0.2; // how much of the boost lands at t=0 (ramps to 1.0 by EARLY_SLOW_SECONDS) - keeps the plunge slow even under heavy scrolling
const HOLD_FACTOR = 0.6;       // fall eases to this while a card is grabbed
const MAX_MISSES = 10;

const clamp01 = (v) => Math.min(1, Math.max(0, v));
const smooth = (t) => t * t * (3 - 2 * t);

// DtRH demotion (M3): when `intensitySource` is provided the director stops
// being the game brain - chaosRun.js owns intensity (elapsed/duration) and the
// director is a speed/boost PRESENTATION adapter. `timeFactor` lets the game
// visibly dilate tunnel time (freeze ~0.06, recap crawl 0.3); killBoost() is
// the detonation stumble.
export function createDirector({ challenge, intensitySource = null }) {
  let runTime = 0;
  let boost = 0;
  let misses = 0;
  let bestCombo = 0;
  let over = false;
  let holding = false; // true while the player is grabbing a card
  let timeFactor = 1;  // game-driven time dilation on the target speed

  const intensity = () => intensitySource
    ? smooth(clamp01(intensitySource()))
    : smooth(clamp01(runTime / RAMP_SECONDS));

  return {
    update(dt) {
      if (over) return;
      runTime += dt;
      boost *= Math.exp(-dt / BOOST_HALFLIFE);
    },

    getIntensity: intensity,
    // Active run time in seconds - paused frames don't advance it and reset()
    // zeroes it. Drives the bubble-kind unlock schedule in bubbles.js.
    getRunTime: () => runTime,

    // The speed fallNav chases (before the player's comfort trim). When the run
    // is over, the tunnel eases to a crawl under the results screen.
    getTargetSpeed() {
      if (over) return 0.15;
      const base = BASE_SPEED_CALM + (BASE_SPEED_HOT - BASE_SPEED_CALM) * intensity();
      // Early-run governor: only a fraction of the scroll/pop boost lands during
      // the first ~30s, so the top of the fall stays slow no matter how hard you
      // scroll; the throttle eases to full by EARLY_SLOW_SECONDS.
      const boostGate = EARLY_BOOST_FLOOR + (1 - EARLY_BOOST_FLOOR) * clamp01(runTime / EARLY_SLOW_SECONDS);
      return (base + boost * boostGate) * (holding ? HOLD_FACTOR : 1) * timeFactor;
    },

    // Game-driven time dilation (freeze / recap idle) + the detonation stumble.
    setTimeFactor(f) { timeFactor = Math.max(0.02, f); },
    killBoost() { boost = 0; },

    // Grabbing a card eases the fall (restored the instant it is released). The
    // scene sets this every frame from spawner.isGrabbing(), so auto-releases
    // (a video taking the stage) restore speed without extra plumbing.
    setHold(v) { holding = !!v; },

    notePop(kind, gain, combo) {
      if (over) return;
      boost = Math.min(BOOST_CAP, boost + (kind === 'lucky' ? BOOST_LUCKY : BOOST_POP));
      if (combo > bestCombo) bestCombo = combo;
    },
    noteEffect() {
      if (over) return;
      boost = Math.min(BOOST_CAP, boost + BOOST_EFFECT);
    },
    // Scroll throttle: feeds the same capped, decaying boost a bubble hit does,
    // so scrolling accelerates the tube up to the cap (delta<0 eases off).
    noteScroll(delta) {
      if (over) return;
      boost = Math.max(0, Math.min(BOOST_CAP, boost + delta));
    },
    // Returns true when THIS miss ends the run (challenge mode only).
    noteMiss() {
      if (over || !challenge) return false;
      misses += 1;
      if (misses >= MAX_MISSES) { over = true; return true; }
      return false;
    },

    isChallenge: () => !!challenge,
    getMisses: () => misses,
    getBestCombo: () => bestCombo,
    isOver: () => over,

    reset() { runTime = 0; boost = 0; misses = 0; bestCombo = 0; over = false; holding = false; timeFactor = 1; },
  };
}
