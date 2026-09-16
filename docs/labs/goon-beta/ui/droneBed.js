/* ============================================================================
 * ui/droneBed.js — the low binaural drone the duel runs on.
 *
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │ oscL (carrier)      -> gainL -\                                       │
 *   │ oscR (carrier+beat) -> gainR --> merger(2) -> lowpass -> bedGain ->   │
 *   │                                                          [droneBus]   │
 *   │ ...and droneBus -> masterBus -> destination, in ui/audio.js.          │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * WHAT IT IS, AND WHERE IT CAME FROM. The Intake's bed (intake/render/audio.js)
 * is two sine carriers around 174-196 Hz, the right detuned above the left by a
 * few Hz, summed through a lowpass into one depth gain. It is SYNTHESIZED — there
 * is no drone loop file anywhere in the intake tree — so this is a port of the
 * CONCEPT, not of an asset: same topology, same band, one fixed point on the
 * intake's depth curve instead of a live depth scalar. Being synthesized is also
 * what makes it seamless: a loop by construction has no seam to hear.
 *
 * IT IS DELIBERATELY QUIET. Every cue in ui/audio.js's registry is under 0.5 on
 * purpose — this game is ambience-heavy and the motion budget has a volume twin —
 * and a bed that plays for twelve unbroken minutes has to be quieter still. Peak
 * amplitude is DRONE_PEAK, and the player's own droneVolume slider (default 0.5)
 * and master slider both sit downstream of it.
 *
 * THE GAIN STAGING, in one line, is: DRONE_PEAK (here, the fade envelope's
 * ceiling) x droneVolume (the droneBus in ui/audio.js) x masterVolume (masterBus).
 * Three nodes, multiplied, never conflated — the same rule cueGain() follows, and
 * the reason a slider drag can retune a bed that is ALREADY playing rather than
 * only the next thing to start.
 *
 * START / STOP IS BUILD / TEAR DOWN, not run/pause, and that is what makes the
 * relay rebuild safe. attachMatch() can run a second time on the same session
 * (the transport was replaced mid-Live) and it re-fires the phase, so start()
 * arrives again on a bed that is already up: it re-ramps the SAME oscillator pair
 * and creates nothing. stop() schedules the pair's own stop() and immediately
 * forgets the nodes, so a later start() builds a fresh pair and no oscillator can
 * ever outlive the bed that owns it.
 *
 * Import-safe under node: no AudioContext, no DOM, no fetch at import time. The
 * parameter math and the phase mapping below are pure and headless-tested
 * (test/selftest-hud.js).
 * ==========================================================================*/

import { GoonMatchPhase } from '../core/contracts.js';

/* ----------------------------------------------------------------------------
 * THE CURVE — the Intake's endpoints, verbatim, so the two beds are audibly the
 * same instrument. depth 0 -> beat 10.0 Hz / carrier 174 Hz; depth 1 -> beat
 * 3.5 Hz / carrier 196 Hz.
 * -------------------------------------------------------------------------- */
export const DRONE_BEAT_HZ = Object.freeze({ rest: 10.0, deep: 3.5 });
export const DRONE_CARRIER_HZ = Object.freeze({ rest: 174, deep: 196 });

/** Where on that curve the duel's bed sits. Fixed: the goon page has no depth
 *  scalar to drive it, and a bed that drifted under a match would read as a bug
 *  rather than as an effect. 0.6 lands at ~187 Hz with a ~6.1 Hz beat. */
export const DRONE_DEPTH = 0.6;

/** Peak amplitude of the bed BEFORE the droneVolume and master sliders. Under
 *  every cue trim in the registry, because it never stops. */
export const DRONE_PEAK = 0.16;

/** The lowpass over the summed sines — takes the edge off two bare oscillators. */
export const DRONE_LOWPASS_HZ = 900;
export const DRONE_LOWPASS_Q = 0.6;

/** Fades. In is slow enough that the bed arrives rather than switches on; out is
 *  quick enough to be gone before the recap's sting lands. */
export const DRONE_FADE_IN_SEC = 2.5;
export const DRONE_FADE_OUT_SEC = 1.2;
/** Slider glide (setTargetAtTime time constant source) — a drag must not zipper. */
export const DRONE_GLIDE_SEC = 0.25;

function clamp01(v) { const n = Number(v); return !isFinite(n) ? 0 : n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(a, b, t) { return a + (b - a) * t; }

/** depth (0..1) -> the two carrier frequencies and the beat between them. Pure. */
export function droneHz(depth) {
  const d = clamp01(depth == null ? DRONE_DEPTH : depth);
  const beatHz = lerp(DRONE_BEAT_HZ.rest, DRONE_BEAT_HZ.deep, d);
  const carrierHz = lerp(DRONE_CARRIER_HZ.rest, DRONE_CARRIER_HZ.deep, d);
  return { beatHz, carrierHz, leftHz: carrierHz, rightHz: carrierHz + beatHz };
}

/** peak x droneVolume x masterVolume -> what the room actually hears. Pure, and
 *  the mirror of cueGain(): three numbers multiplied, never conflated. */
export function droneGain(droneVol, master) {
  return DRONE_PEAK * clamp01(droneVol) * clamp01(master);
}

/* ----------------------------------------------------------------------------
 * PHASE -> STATE. The engine's phase is the single source of truth for what is
 * on screen (boot.js onPhase) and it is the single source of truth for this too.
 *
 * The bed runs from the COUNTDOWN, not from Live: the countdown is already the
 * match — the splash is up, the chrome is swept — and a bed that only arrived
 * when the clock started would announce itself instead of already being there.
 * It ends at the Recap, which every match reaches (boot.js treats the recap as
 * non-optional: RECAP_FALLBACK_MS, forceRecap), so there is no road out of a
 * live match that leaves the drone playing.
 * -------------------------------------------------------------------------- */
export const DRONE_PHASES = Object.freeze([
  GoonMatchPhase.Countdown,
  GoonMatchPhase.Live,
  GoonMatchPhase.SuddenDeath,
]);

/** Should the bed be playing at this phase? Pure; anything unrecognised is OFF. */
export function droneWantsPlay(phase) {
  const p = Number(phase);
  if (!isFinite(p)) return false;
  return DRONE_PHASES.indexOf(p) >= 0;
}

/* ----------------------------------------------------------------------------
 * THE LIVE BED. Owns nothing but its own oscillators; the bus it plays into is
 * ui/audio.js's droneBus, handed in as `out`.
 * -------------------------------------------------------------------------- */

/**
 * @param {object} o
 * @param {AudioContext} o.ctx
 * @param {AudioNode} o.out the droneBus (already under masterBus)
 * @param {number} [o.depth] point on the curve (default DRONE_DEPTH)
 * @param {object} [o.logger] console-shaped; omit for silence
 */
export function createDroneBed({ ctx, out, depth = DRONE_DEPTH, logger = null } = {}) {
  let built = false;
  let disposed = false;
  let nodes = null;          // { oscL, oscR, gL, gR, merger, lowpass, bedGain }
  const hz = droneHz(depth);

  function warn(what) {
    try { if (logger && logger.warn) logger.warn('[GG drone] ' + what); } catch (_e) { /* ignore */ }
  }

  function build() {
    if (!ctx || built || disposed) return built;
    try {
      const oscL = ctx.createOscillator(); oscL.type = 'sine'; oscL.frequency.value = hz.leftHz;
      const oscR = ctx.createOscillator(); oscR.type = 'sine'; oscR.frequency.value = hz.rightHz;
      const gL = ctx.createGain(); gL.gain.value = 0.5;
      const gR = ctx.createGain(); gR.gain.value = 0.5;
      const merger = ctx.createChannelMerger(2);
      oscL.connect(gL); gL.connect(merger, 0, 0);   // L
      oscR.connect(gR); gR.connect(merger, 0, 1);   // R
      const lowpass = ctx.createBiquadFilter();
      lowpass.type = 'lowpass';
      lowpass.frequency.value = DRONE_LOWPASS_HZ;
      lowpass.Q.value = DRONE_LOWPASS_Q;
      const bedGain = ctx.createGain();
      bedGain.gain.value = 0;                       // silent until the fade in
      merger.connect(lowpass); lowpass.connect(bedGain);
      bedGain.connect(out || ctx.destination);
      oscL.start(); oscR.start();
      nodes = { oscL, oscR, gL, gR, merger, lowpass, bedGain };
      built = true;
    } catch (e) {
      nodes = null;
      built = false;
      warn('could not build the bed: ' + ((e && e.message) || e));
    }
    return built;
  }

  return {
    get isPlaying() { return built; },
    get hz() { return Object.assign({}, hz); },
    get peak() { return DRONE_PEAK; },

    /**
     * Fade the bed in. IDEMPOTENT: called on a bed that is already up (the relay
     * rebuild re-fires the phase) it re-ramps the same oscillators and builds
     * nothing. Ramping from the CURRENT value is also why a late start after the
     * autoplay unlock cannot burst: the fade always begins where the gain is.
     */
    start(fadeSec) {
      if (disposed || !ctx) return false;
      if (!built && !build()) return false;
      const sec = (typeof fadeSec === 'number' && fadeSec >= 0) ? fadeSec : DRONE_FADE_IN_SEC;
      try {
        const t = ctx.currentTime;
        const g = nodes.bedGain.gain;
        g.cancelScheduledValues(t);
        g.setValueAtTime(g.value, t);
        g.linearRampToValueAtTime(DRONE_PEAK, t + Math.max(0.01, sec));
      } catch (_e) {
        try { nodes.bedGain.gain.value = DRONE_PEAK; } catch (_e2) { /* ignore */ }
      }
      return true;
    },

    /**
     * Fade the bed out and let it go. The oscillators are handed their own stop()
     * on the way down and the references are dropped immediately, so a start()
     * that lands during the fade builds a fresh pair rather than reviving a node
     * that is already scheduled to die.
     */
    stop(fadeSec) {
      if (!built || !nodes) return false;
      const sec = (typeof fadeSec === 'number' && fadeSec >= 0) ? fadeSec : DRONE_FADE_OUT_SEC;
      const dying = nodes;
      nodes = null;
      built = false;
      try {
        const t = ctx.currentTime;
        const g = dying.bedGain.gain;
        g.cancelScheduledValues(t);
        g.setValueAtTime(g.value, t);
        g.linearRampToValueAtTime(0.0001, t + Math.max(0.01, sec));
        dying.oscL.stop(t + sec + 0.2);
        dying.oscR.stop(t + sec + 0.2);
      } catch (_e) {
        try { dying.oscL.stop(); } catch (_e2) { /* gone */ }
        try { dying.oscR.stop(); } catch (_e3) { /* gone */ }
      }
      return true;
    },

    /** Hard stop — no fade, no scheduling. The page is going away. */
    dispose() {
      if (disposed) return;
      disposed = true;
      const dying = nodes;
      nodes = null;
      built = false;
      if (!dying) return;
      try { dying.oscL.stop(); } catch (_e) { /* gone */ }
      try { dying.oscR.stop(); } catch (_e) { /* gone */ }
      try { dying.bedGain.disconnect(); } catch (_e) { /* gone */ }
    },
  };
}

export default createDroneBed;
