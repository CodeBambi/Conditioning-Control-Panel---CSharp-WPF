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
const BASE_SPEED_CALM = 5;     // u/s at intensity 0
const BASE_SPEED_HOT = 16;     // u/s at intensity 1
const BOOST_POP = 0.6;         // + per popped bubble
const BOOST_LUCKY = 4;         // + per golden jackpot
const BOOST_EFFECT = 2.2;      // + per fired screen effect (chains compound)
const BOOST_CAP = 14;
const BOOST_HALFLIFE = 3.5;    // seconds for a boost to bleed to ~37%
const HOLD_FACTOR = 0.6;       // fall eases to this while a card is grabbed
const MAX_MISSES = 10;

const clamp01 = (v) => Math.min(1, Math.max(0, v));
const smooth = (t) => t * t * (3 - 2 * t);

export function createDirector({ challenge }) {
  let runTime = 0;
  let boost = 0;
  let misses = 0;
  let bestCombo = 0;
  let over = false;
  let holding = false; // true while the player is grabbing a card

  const intensity = () => smooth(clamp01(runTime / RAMP_SECONDS));

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
      return (base + boost) * (holding ? HOLD_FACTOR : 1);
    },

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

    reset() { runTime = 0; boost = 0; misses = 0; bestCombo = 0; over = false; holding = false; },
  };
}
