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

import { isMuted } from '../shared/audioMute.js';

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
  function load(src) {
    if (!AC || buffers.has(src)) return;
    buffers.set(src, 'pending');
    fetch(src)
      .then((r) => r.arrayBuffer())
      .then((raw) => {
        const c = getAudioCtx();
        if (!c) throw new Error('no audio ctx');
        return new Promise((res, rej) => c.decodeAudioData(raw, res, rej));
      })
      .then((decoded) => buffers.set(src, decoded))
      .catch(() => buffers.set(src, 'failed'));
  }
  function elementPlay(src, vol) {
    try {
      if (!cache[src]) { const a = new Audio(src); a.preload = 'auto'; cache[src] = a; }
      const a = cache[src].cloneNode(); a.volume = vol; a.play().catch(() => {});
    } catch (e) { /* ignore */ }
  }
  return {
    preload(srcs) { for (const s of srcs) load(s); },
    play(src, vol = 0.3) {
      if (isMuted() || vol <= 0.001) return; // a zeroed slider must mean SILENT on every path
      const buf = buffers.get(src);
      if (buf && buf !== 'pending' && buf !== 'failed') {
        const c = getAudioCtx();
        if (c) {
          try {
            const gain = c.createGain();
            gain.gain.value = vol;
            const node = c.createBufferSource();
            node.buffer = buf;
            node.connect(gain); gain.connect(c.destination);
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
