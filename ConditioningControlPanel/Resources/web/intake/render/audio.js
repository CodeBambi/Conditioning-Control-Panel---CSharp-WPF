/* ============================================================================
 * audio.js — WebAudio binaural + reward chimes for "Graded Intake" (Agent E).
 *
 * ONE depth scalar drives a binaural beat: two sine oscillators (L / R), the
 * right detuned above the left by the beat frequency, summed through a lowpass
 * -> depth gain -> limiter -> destination. As depth 0->1 the beat tightens from
 * 10 Hz to 3.5 Hz and the carrier rises 174 Hz -> 196 Hz (memory note + brief).
 *
 * CONTRACT (contracts.js §"EFFECTS + AUDIO (Agent E)"):
 *   createAudio({ caps }) -> {
 *     setDepth(depth),        // binauralDepth (clamped) -> beat/carrier Hz + gain, glided
 *     chime(rewardEvent),     // short reward chime scaled by clampIntensity
 *     emerge(),               // Recovery: reverse the ramp (beat -> 10 Hz, carrier down, gain out)
 *   }
 *
 * INVARIANTS:
 *   #2 — the binaural LEVEL flows from clampToCaps(depthToChannels(depth)).binauralDepth
 *        and the chime from clampIntensity(intensity, caps). No hardcoded loudness
 *        beyond the fixed 0..1 -> amplitude/Hz translation constants.
 *   #3 — emerge() glides the whole graph back to the depth-0 base and silences it.
 *
 * SIDE-EFFECT FREE AT IMPORT: the AudioContext is created LAZILY (first setDepth /
 * chime, armed to resume on the first user gesture). No WebAudio/DOM at load, so
 * importing never throws. Pure Hz/gain mappings are exported for headless tests.
 * ==========================================================================*/

import { depthToChannels, clampToCaps, clampIntensity, clamp01, lerp } from '../core/contracts.js';

/* ----------------------------------------------------------------------------
 * PURE MAPPINGS — binauralDepth (0..1) -> Hz + amplitude. Exported + tested.
 * Endpoints are LOCKED by the brief/memory note:
 *   depth 0 -> beat 10.0 Hz, carrier 174 Hz
 *   depth 1 -> beat  3.5 Hz, carrier 196 Hz
 * -------------------------------------------------------------------------- */
export const BEAT_HZ    = { rest: 10.0, deep: 3.5 };
export const CARRIER_HZ = { rest: 174,  deep: 196 };
/** Peak binaural amplitude at full clamped depth (before limiter). Translation constant. */
export const BINAURAL_GAIN_MAX = 0.22;

/** binauralDepth (already clamped to caps) -> { beatHz, carrierHz }. Pure. */
export function binauralToHz(binauralDepth) {
  const b = clamp01(binauralDepth);
  return {
    beatHz:    lerp(BEAT_HZ.rest, BEAT_HZ.deep, b),       // 10 -> 3.5
    carrierHz: lerp(CARRIER_HZ.rest, CARRIER_HZ.deep, b), // 174 -> 196
  };
}
/** binauralDepth -> master binaural amplitude (0 at rest). Pure. */
export function binauralGainFor(binauralDepth) {
  return clamp01(binauralDepth) * BINAURAL_GAIN_MAX;
}
/** reward intensity (already clampIntensity'd) -> chime envelope numbers. Pure. */
export function chimeSpec(intensity) {
  const i = clamp01(intensity);
  return {
    gain:       0.03 + i * 0.17,   // 0.03 .. 0.20
    durSec:     0.26 + i * 0.22,   // 0.26 .. 0.48 s
    baseHz:     660 + i * 220,     // brighter when it pays more
    partials:   [1, 2, 3],         // simple bell-ish stack
  };
}

/* Full depth->binauralDepth channel read (curve x caps in one place). */
function binauralChannel(depth, caps) {
  return clampToCaps(depthToChannels(clamp01(depth)), caps).binauralDepth;
}

/* ----------------------------------------------------------------------------
 * SELF-CONTAINED MUTE — a tiny master switch (persisted). Kept local so audio.js
 * never depends on the dtrh shared/audioMute.js module.
 * -------------------------------------------------------------------------- */
const MUTE_KEY = 'intake-audio-muted';
let _muted = false;
try { _muted = (typeof localStorage !== 'undefined') && localStorage.getItem(MUTE_KEY) === '1'; } catch (_e) {}
export function isMuted() { return _muted; }
export function setMuted(v) {
  _muted = !!v;
  try { if (typeof localStorage !== 'undefined') localStorage.setItem(MUTE_KEY, _muted ? '1' : '0'); } catch (_e) {}
  return _muted;
}

/* ----------------------------------------------------------------------------
 * FACTORY
 * -------------------------------------------------------------------------- */
const GESTURES = ['pointerdown', 'touchstart', 'keydown', 'wheel'];
const GLIDE_SEC   = 0.6;   // depth-change glide (no zipper noise)
const EMERGE_SEC  = 4.0;   // recovery walk-out

export function createAudio({ caps } = {}) {
  let ctx = null;
  let dead = false;             // constructor unavailable / failed: stop trying
  let resumeHook = null;

  // graph nodes
  let oscL = null, oscR = null;
  let gainL = null, gainR = null;
  let merger = null, lowpass = null, binGain = null, limiter = null;
  let started = false;

  let depthNow = 0;             // last requested depth (for lazy graph seed)

  function capsOf() { return caps || undefined; }

  function ensureCtx() {
    if (dead) return null;
    if (!ctx) {
      const AC = (typeof window !== 'undefined') && (window.AudioContext || window.webkitAudioContext);
      if (!AC) { dead = true; return null; }
      try { ctx = new AC(); } catch (_e) { dead = true; return null; }
      resumeHook = () => { if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {}); };
      if (typeof window !== 'undefined') {
        GESTURES.forEach((ev) => window.addEventListener(ev, resumeHook, { passive: true }));
      }
    }
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
    return ctx;
  }

  /** Build the binaural graph once. Starts silent (gain 0) at the current depth. */
  function ensureGraph() {
    const c = ensureCtx();
    if (!c || started) return c;
    try {
      const { beatHz, carrierHz } = binauralToHz(binauralChannel(depthNow, capsOf()));

      oscL = c.createOscillator(); oscL.type = 'sine'; oscL.frequency.value = carrierHz;
      oscR = c.createOscillator(); oscR.type = 'sine'; oscR.frequency.value = carrierHz + beatHz;

      gainL = c.createGain(); gainL.gain.value = 0.5;
      gainR = c.createGain(); gainR.gain.value = 0.5;

      merger = c.createChannelMerger(2);
      oscL.connect(gainL); gainL.connect(merger, 0, 0);   // L
      oscR.connect(gainR); gainR.connect(merger, 0, 1);   // R

      lowpass = c.createBiquadFilter();
      lowpass.type = 'lowpass'; lowpass.frequency.value = 900; lowpass.Q.value = 0.6;

      binGain = c.createGain(); binGain.gain.value = 0;    // fade in via setDepth

      limiter = c.createDynamicsCompressor();
      // gentle brick-wall so summed sines + chimes never clip.
      try {
        limiter.threshold.value = -6; limiter.knee.value = 6;
        limiter.ratio.value = 12; limiter.attack.value = 0.003; limiter.release.value = 0.25;
      } catch (_e) {}

      merger.connect(lowpass); lowpass.connect(binGain);
      binGain.connect(limiter); limiter.connect(c.destination);

      oscL.start(); oscR.start();
      started = true;
    } catch (_e) {
      started = false;
    }
    return c;
  }

  function glide(param, value, sec) {
    if (!param || !ctx) return;
    try {
      const t = ctx.currentTime;
      param.cancelScheduledValues(t);
      // setTargetAtTime gives a smooth exponential-ish glide with no zipper.
      param.setValueAtTime(param.value, t);
      param.setTargetAtTime(value, t, Math.max(0.02, sec / 3));
    } catch (_e) {
      try { param.value = value; } catch (_e2) {}
    }
  }

  /* ----- public API --------------------------------------------------------- */

  /** Drive the binaural from one depth scalar (invariant #2: via caps). */
  function setDepth(depth) {
    depthNow = clamp01(depth);
    const c = ensureGraph();
    if (!c || !started) return;
    const b = binauralChannel(depthNow, capsOf());
    const { beatHz, carrierHz } = binauralToHz(b);
    const gain = _muted ? 0 : binauralGainFor(b);
    glide(oscL.frequency, carrierHz, GLIDE_SEC);
    glide(oscR.frequency, carrierHz + beatHz, GLIDE_SEC);
    glide(binGain.gain, gain, GLIDE_SEC);
  }

  /** Short reward chime scaled by clamped intensity. Fire-and-forget one-shot. */
  function chime(rewardEvent) {
    if (_muted || !rewardEvent || !rewardEvent.fire) return;
    const intensity = clampIntensity(rewardEvent.intensity, capsOf());
    if (intensity <= 0.0005) return;
    const c = ensureCtx();
    if (!c) return;
    try {
      const spec = chimeSpec(intensity);
      const t0 = c.currentTime;
      const out = c.createGain();
      out.gain.setValueAtTime(0, t0);
      out.gain.linearRampToValueAtTime(spec.gain, t0 + 0.012);
      out.gain.exponentialRampToValueAtTime(0.0001, t0 + spec.durSec);
      // route the chime through the limiter if the graph exists, else straight out.
      out.connect((started && limiter) ? limiter : c.destination);
      spec.partials.forEach((mult, idx) => {
        const o = c.createOscillator();
        o.type = idx === 0 ? 'triangle' : 'sine';
        o.frequency.value = spec.baseHz * mult;
        const pg = c.createGain();
        pg.gain.value = 1 / (mult * 1.6); // higher partials quieter
        o.connect(pg); pg.connect(out);
        o.start(t0);
        o.stop(t0 + spec.durSec + 0.05);
      });
    } catch (_e) {}
  }

  /** Invariant #3: emerge — glide beat back to 10 Hz, carrier down, gain out. */
  function emerge() {
    depthNow = 0;
    if (!ctx || !started) return;
    const { beatHz, carrierHz } = binauralToHz(0); // 10 Hz / 174 Hz
    glide(oscL.frequency, carrierHz, EMERGE_SEC);
    glide(oscR.frequency, carrierHz + beatHz, EMERGE_SEC);
    glide(binGain.gain, 0, EMERGE_SEC);
  }

  /** Full teardown (optional; boot doesn't call it, but hosts may on unload). */
  function dispose() {
    try { if (oscL) oscL.stop(); } catch (_e) {}
    try { if (oscR) oscR.stop(); } catch (_e) {}
    if (resumeHook && typeof window !== 'undefined') {
      GESTURES.forEach((ev) => window.removeEventListener(ev, resumeHook));
      resumeHook = null;
    }
    if (ctx) { try { ctx.close(); } catch (_e) {} }
    ctx = null; started = false; dead = false;
    oscL = oscR = gainL = gainR = merger = lowpass = binGain = limiter = null;
  }

  return { setDepth, chime, emerge, dispose, setMuted, isMuted };
}
