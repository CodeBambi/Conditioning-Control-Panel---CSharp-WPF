/* ============================================================================
 * feelAudio.js - the soft, pitched half of the descent's feel pass.
 *
 * DtRH is a trance game: these voices make the fall warmer, never louder.
 *   - the pop ladder: each pop in a streak sounds one rung higher on the C
 *     pentatonic (no semitone clashes, so no order of pops can sound wrong),
 *     capped at LADDER_RUNGS, back to the root when the streak breaks;
 *   - the arrival tick: a reward landing in its HUD slot;
 *   - the resolve: one low root note when a landing (boon draft) opens.
 * Plus the pure maths the bus uses so depth can drive the mix.
 *
 * This file imports NOTHING: the audio context, the master out, mute, the
 * effects slider and "is the voice speaking" are handed in, so the pure
 * helpers run under node:test and the voices go through the one shared bus
 * (mute, slider and the biome/depth low-pass all apply).
 * Every voice is a sine with one quiet octave partial, a short attack (the IN)
 * and an exponential decay (the OUT). Nothing here speaks.
 * ==========================================================================*/

// ---- tunables (by ear) -------------------------------------------------------
export const LADDER_ROOT_HZ = 261.63;   // C4, rung 0
export const LADDER_RUNGS = 8;          // C4 D4 E4 G4 A4 C5 D5 E5
export const LADDER_TILT = 0.04;        // each rung is this much quieter than the one below
export const POP_GAIN = 0.06;           // ladder note, at the default effects slider
export const POP_DUR_S = 0.26;
export const POP_GAP_MS = 45;           // a sweep popping many at once sounds one note, not a cluster
export const GOLD_GAIN = 0.07;          // a gold/special pop: the current rung, one octave up, no climb
export const ARRIVE_GAIN = 0.035;       // reward landing in its slot
export const ARRIVE_DUR_S = 0.11;
export const ARRIVE_RUNG = 9;           // A5: above the ladder so it never masks a pop
export const ARRIVE_GAP_MS = 40;
export const RESOLVE_GAIN = 0.08;       // the landing's root
export const RESOLVE_DUR_S = 0.3;
export const RESOLVE_HZ = LADDER_ROOT_HZ / 2;   // C3
export const PARTIAL_GAIN = 0.22;       // octave partial, relative to the fundamental
export const ATTACK_S = 0.008;
export const VOICE_DUCK = 0.4;          // every voice here sits under a speaking voice line
export const FX_SLIDER_DEFAULT = 0.48;  // audioLevels.js 'fx' default; gains above are quoted at this
export const DEPTH_OPEN_HZ = 20000;     // master low-pass at depth 0 (transparent)
export const DEPTH_DEEP_HZ = 3200;      // master low-pass at depth 1
export const DEPTH_DRONE_LIFT = 0.3;    // drone bed is this much fuller at depth 1
export const FAIL_HZ = 350;             // a detonation closes the mix to here...
export const FAIL_CLOSE_S = 0.09;       // ...this fast...
export const FAIL_BACK_S = 2.6;         // ...and it opens back over this long

const PENTATONIC = [0, 2, 4, 7, 9];
const clamp01 = (v) => (v > 0 ? (v < 1 ? v : 1) : 0);

// ---- pure helpers (unit tested) ----------------------------------------------
/** Streak 1 is the root; each pop climbs one rung; capped; anything else = root. */
export function rungForStreak(streak) {
  const n = Math.floor(Number(streak));
  if (!(n >= 1)) return 0;
  return Math.min(LADDER_RUNGS - 1, n - 1);
}

/** Frequency of a pentatonic rung above the root (rung 5 is the octave). */
export function freqForRung(rung, rootHz = LADDER_ROOT_HZ) {
  const r = Math.max(0, Math.floor(Number(rung)) || 0);
  const semis = 12 * Math.floor(r / PENTATONIC.length) + PENTATONIC[r % PENTATONIC.length];
  return rootHz * Math.pow(2, semis / 12);
}

/** Level tilt: the ladder gets a touch quieter as it climbs, never brighter-and-louder. */
export function gainForRung(rung, base = POP_GAIN) {
  return base * Math.max(0.5, 1 - LADDER_TILT * Math.max(0, rung));
}

/** Depth 0..1 to the master low-pass cutoff. Exponential: equal steps sound equal. */
export function depthCutoffHz(depth) {
  return DEPTH_OPEN_HZ * Math.pow(DEPTH_DEEP_HZ / DEPTH_OPEN_HZ, clamp01(Number(depth) || 0));
}

/** Depth 0..1 to the drone bed multiplier. Linear: it is a gain. */
export function depthDroneMul(depth) {
  return 1 + DEPTH_DRONE_LIFT * clamp01(Number(depth) || 0);
}

// ---- the voices ----------------------------------------------------------------
/**
 * @param {object} io
 * @param {() => AudioContext|null} io.getCtx      the shared context
 * @param {() => AudioNode|null}    io.getOut      the master out (falls back to destination)
 * @param {() => boolean}           io.silent      muted or hard-ducked: play nothing
 * @param {() => number}            io.level       the effects slider, 0..1
 * @param {() => boolean}           [io.voiceActive] a voice line is speaking: sit under it
 * @param {() => number}            [io.now]       ms clock
 */
export function createFeelAudio(io) {
  const now = io.now || (() => performance.now());
  let lastPop = -1e9, lastArrive = -1e9, dead = false;

  function bell(hz, gain, dur) {
    if (dead || io.silent()) return false;
    const slider = Math.max(0, Number(io.level()) || 0) / FX_SLIDER_DEFAULT;
    let g = gain * Math.min(2, slider);
    if (io.voiceActive && io.voiceActive()) g *= VOICE_DUCK;
    if (g <= 0.0005) return false;
    const c = io.getCtx();
    if (!c || c.state !== 'running') return false;   // never queue notes into a sleeping context
    try {
      const t = c.currentTime;
      const env = c.createGain();
      env.gain.setValueAtTime(0.0001, t);
      env.gain.linearRampToValueAtTime(g, t + ATTACK_S);
      env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
      env.connect(io.getOut() || c.destination);
      const part = c.createGain();
      part.gain.value = PARTIAL_GAIN;
      part.connect(env);
      const a = c.createOscillator(); a.type = 'sine'; a.frequency.value = hz;
      const b = c.createOscillator(); b.type = 'sine'; b.frequency.value = hz * 2;
      a.connect(env); b.connect(part);
      a.onended = () => {
        try { a.disconnect(); b.disconnect(); part.disconnect(); env.disconnect(); } catch (e) { /* ignore */ }
      };
      a.start(t); b.start(t);
      a.stop(t + dur + 0.02); b.stop(t + dur + 0.02);
      return true;
    } catch (e) { return false; }
  }

  return {
    /** One pop. `streak` is the streak AFTER this pop; `gold` = a special that does not climb. */
    pop(streak, opts) {
      const t = now();
      if (t - lastPop < POP_GAP_MS) return false;
      lastPop = t;
      const rung = rungForStreak(streak);
      if (opts && opts.gold) return bell(freqForRung(rung) * 2, gainForRung(rung, GOLD_GAIN), POP_DUR_S);
      return bell(freqForRung(rung), gainForRung(rung), POP_DUR_S);
    },
    /** A reward landed in its HUD slot. */
    arrive() {
      const t = now();
      if (t - lastArrive < ARRIVE_GAP_MS) return false;
      lastArrive = t;
      return bell(freqForRung(ARRIVE_RUNG), ARRIVE_GAIN, ARRIVE_DUR_S);
    },
    /** A landing opened: the ladder comes home to one low root. */
    resolve() { return bell(RESOLVE_HZ, RESOLVE_GAIN, RESOLVE_DUR_S); },
    dispose() { dead = true; },
  };
}
