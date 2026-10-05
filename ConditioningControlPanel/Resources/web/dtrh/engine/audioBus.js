/* ============================================================================
 * audioBus.js (Sissy Fall) - ONE shared AudioContext for the whole fall.
 *
 * The fall used to spin up three separate contexts: spawner (video-card gain
 * nodes), bubbles (pop/chime/drop buffers) and scene (the drone bed's gain).
 * Each running context owns its own audio-thread render quantum and its own
 * suspend/resume dance with the OS audio session - on iOS that overhead is per
 * context, and three of them fighting over one hardware session is exactly the
 * kind of background load that shows up as skipped frames when the mix gets
 * busy. Everything now routes through this single context.
 *
 * getAudioCtx() lazily creates it, arms the usual resume-on-gesture listeners,
 * and nudges a suspended context on every call (media elements bound via
 * createMediaElementSource stay bound to it for their lifetime, so the scene
 * only closes it at full teardown via closeAudioBus()).
 * ==========================================================================*/

import { isMuted, isDucked } from '../shared/audioMute.js';
import { audioUrl, altAudioUrl } from '../shared/audioSrc.js';
import { depthCutoffHz, depthDroneMul, FAIL_HZ, FAIL_CLOSE_S, FAIL_BACK_S } from '../game/feelAudio.js';

const DEPTH_GLIDE_TC = 1.6;   // seconds; the depth tone is felt, never heard moving

const GESTURES = ['pointerdown', 'touchstart', 'keydown', 'wheel'];

let ctx = null;
let dead = false;     // constructor unavailable/failed: stop trying
let resumeHook = null;

export function getAudioCtx() {
  if (dead) return null;
  if (!ctx) {
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) { dead = true; return null; }
    try { ctx = new AC(); } catch (e) { dead = true; return null; }
    resumeHook = () => { if (ctx && ctx.state === 'suspended') ctx.resume().catch(() => {}); };
    GESTURES.forEach((ev) => window.addEventListener(ev, resumeHook, { passive: true }));
  }
  if (ctx.state === 'suspended') ctx.resume().catch(() => {});
  return ctx;
}

// ---- THE BIOMES' audio color (S2): one master filter the whole mix wears.
// Parked way above hearing it is transparent; a biome pulls it down and the
// drone, the video cards, the SFX and the drift whisper all go under together
// (the Searchlight's held-breath muffle, the Undertow's water). One node, one
// automation - never per-voice.
let master = null;   // BiquadFilterNode -> destination
const AUDIO_COLORS = { muffled: 780, underwater: 400 };
const COLOR_OPEN_HZ = 20000;

/** The node every voice should connect to instead of ctx.destination. Falls
 * back to the raw destination if the filter can't be built. */
export function getMasterOut() {
  const c = getAudioCtx();
  if (!c) return null;
  if (!master) {
    try {
      master = c.createBiquadFilter();
      master.type = 'lowpass';
      master.frequency.value = COLOR_OPEN_HZ;
      master.Q.value = 0.5;
      master.connect(c.destination);
    } catch (e) { master = null; return c.destination; }
  }
  return master;
}

// The master filter has three owners and ONE rule: it rests at the LOWER of the
// biome color and the depth tone, and a failure dip borrows it for a moment.
//   color - the chamber's own muffle (setAudioColor, below)
//   depth - the feel pass: deeper is warmer (setDepthTone)
//   dip   - a detonation closes the mix and it opens back (failureDip)
let colorHz = COLOR_OPEN_HZ;
let depthHz = COLOR_OPEN_HZ;
let depthDrone = 1;      // drone bed multiplier the scene reads each frame
let dipTimer = 0;        // non-zero while a failure dip owns the filter
const restHz = () => Math.min(colorHz, depthHz);

function glideMaster(timeConstant) {
  const target = restHz();
  if (target >= COLOR_OPEN_HZ && !master) return;   // nothing was colored: nothing to restore
  getMasterOut();
  if (!master || !ctx || dipTimer) return;          // a dip in flight lands on the rest value itself
  try {
    master.frequency.cancelScheduledValues(ctx.currentTime);
    master.frequency.setTargetAtTime(target, ctx.currentTime, timeConstant);
  } catch (e) {
    try { master.frequency.value = target; } catch (e2) { /* ignore */ }
  }
}

/** Ease the whole mix toward a biome color ('muffled' | 'underwater' | null). */
export function setAudioColor(mode) {
  colorHz = (mode && AUDIO_COLORS[mode]) || COLOR_OPEN_HZ;
  glideMaster(0.35);
}

/** Depth drives the mix: 0 = open, 1 = warmest. One slow glide, never a step. */
export function setDepthTone(depth) {
  depthHz = Math.min(COLOR_OPEN_HZ, depthCutoffHz(depth));
  depthDrone = depthDroneMul(depth);
  glideMaster(DEPTH_GLIDE_TC);
}

/** The drone bed's depth multiplier (1 at the surface). The scene smooths it. */
export function depthDroneLevel() { return depthDrone; }

/** Failure subtracts: close the mix fast, open it back slowly. Nothing is added. */
export function failureDip(hz = FAIL_HZ, backSec = FAIL_BACK_S) {
  getMasterOut();
  if (!master || !ctx) return;
  const rest = restHz();
  if (hz >= rest) return;   // the chamber is already darker than the dip
  try {
    const f = master.frequency, t = ctx.currentTime;
    const from = Math.max(40, Math.min(COLOR_OPEN_HZ, f.value));
    f.cancelScheduledValues(t);
    f.setValueAtTime(from, t);
    f.exponentialRampToValueAtTime(hz, t + FAIL_CLOSE_S);
    f.exponentialRampToValueAtTime(rest, t + FAIL_CLOSE_S + backSec);
  } catch (e) { return; }
  if (dipTimer) clearTimeout(dipTimer);
  dipTimer = setTimeout(() => { dipTimer = 0; glideMaster(0.35); }, (FAIL_CLOSE_S + backSec) * 1000 + 30);
}

// Panel diagnostics: report without creating (getAudioCtx would spin one up).
export function peekAudioState() {
  if (dead) return 'unavailable';
  return ctx ? ctx.state : 'not created';
}

// Full teardown (scene.dispose). A later start() may recreate from scratch.
export function closeAudioBus() {
  if (resumeHook) {
    GESTURES.forEach((ev) => window.removeEventListener(ev, resumeHook));
    resumeHook = null;
  }
  if (ctx) { try { ctx.close(); } catch (e) { /* ignore */ } ctx = null; }
  master = null;   // died with its context
  if (dipTimer) { clearTimeout(dipTimer); dipTimer = 0; }
  colorHz = depthHz = COLOR_OPEN_HZ; depthDrone = 1;
  dead = false;
}

// Overlapping-SFX player over the shared context (pops, chimes, drops...).
// WebAudio-ONLY wherever the browser has it: the old element-clone fallback
// played at FULL volume on iOS (media elements ignore .volume there), so any
// sound that fired before its buffer finished decoding bypassed the mix
// sliders entirely - the "dials are down but it still plays" bug. Now a
// not-yet-decoded sound is skipped once (and decoded for next time) instead
// of firing at the wrong loudness; element clones survive only for browsers
// with no WebAudio at all (where .volume actually works).
export function makeSfxPlayer() {
  const AC = window.AudioContext || window.webkitAudioContext;
  const buffers = new Map(); // src -> AudioBuffer | 'pending' | 'failed'
  const cache = {};          // element bases (no-WebAudio browsers only)
  // Callers keep passing plain '/dtrh/assets/...' paths and keying off them; the
  // host choice (installed tree vs downloaded content pack, shared/audioSrc.js)
  // is resolved HERE, with one retry on the other host before a src is written
  // off as 'failed'. Missing on BOTH hosts ends exactly where it did before.
  function fetchDecode(url) {
    return fetch(url)
      .then((r) => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.arrayBuffer(); })
      .then((raw) => {
        const c = getAudioCtx();
        if (!c) throw new Error('no audio ctx');
        return new Promise((res, rej) => c.decodeAudioData(raw, res, rej));
      });
  }
  function load(src) {
    if (!AC || buffers.has(src)) return;
    buffers.set(src, 'pending');
    const primary = audioUrl(src);
    fetchDecode(primary)
      .catch(() => {
        const alt = altAudioUrl(primary);
        if (!alt) throw new Error('no alternate host');
        return fetchDecode(alt);
      })
      .then((decoded) => buffers.set(src, decoded))
      .catch(() => buffers.set(src, 'failed'));
  }
  function elementPlay(src, vol) {
    try {
      if (!cache[src]) { const a = new Audio(audioUrl(src)); a.preload = 'auto'; cache[src] = a; }
      const a = cache[src].cloneNode(); a.volume = vol; a.play().catch(() => {});
    } catch (e) { /* ignore */ }
  }
  return {
    preload(srcs) { for (const s of srcs) load(s); },
    play(src, vol = 0.3) {
      if (isMuted() || isDucked() || vol <= 0.001) return; // muted, VN-ducked, or a zeroed slider = SILENT
      const buf = buffers.get(src);
      if (buf && buf !== 'pending' && buf !== 'failed') {
        const c = getAudioCtx();
        if (c) {
          try {
            const gain = c.createGain();
            gain.gain.value = vol;
            const node = c.createBufferSource();
            node.buffer = buf;
            node.connect(gain); gain.connect(getMasterOut() || c.destination);
            node.start();
            return;
          } catch (e) { /* fall through */ }
        }
      }
      if (buf === 'pending') return;                   // decoding: skip this one, the next fires right
      if (!buffers.has(src)) { load(src); if (AC) return; } // decode for next time
      elementPlay(src, vol);                           // failed decode / no WebAudio at all
    },
  };
}
