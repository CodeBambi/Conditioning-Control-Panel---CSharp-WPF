/**
 * SHELL MUSIC — the looping track behind the menu screens (main menu, options,
 * pause). One licensed song: assets/music/menu-theme.mp3.
 *
 * NOT part of the run. The descent owns the ear from the briefing onward — the
 * binaural bed, the VO and the sfx are doing careful work down there and a pop
 * loop over the top would fight all three. This plays in the storefront and
 * stops when the storefront does.
 *
 * WHY A PLAIN <audio> ELEMENT and not render/audio.js's WebAudio graph:
 *   1. That graph is built lazily on a user gesture, suspended by the pause
 *      system, and torn down by dispose() at the end of a run. The shell music
 *      has to survive all three, and outlive none of them cleanly if it shares.
 *   2. It is a long streamed file. The graph's other voices are short decoded
 *      buffers held in memory; decoding a 3MB song into an AudioBuffer to loop
 *      it is pure waste when the element streams and loops natively.
 *   3. Separation is the point — this track has its own kill switch precisely
 *      so it can be silenced without touching the run's mix.
 *
 * Autoplay: WebView2 can refuse playback until the page has seen a real user
 * gesture. We try immediately, and if the promise rejects we arm a one-shot
 * gesture listener and try again. A player who clicks anything gets the music;
 * one who never touches the page never hears it, which is the correct failure.
 */

import { getPrefs, setPref, subscribe, musicScale } from './prefs.js';
import { audioUrl, altSrcFor } from '../core/audioSrc.js';

const SRC = '../assets/music/menu-theme.mp3';
const FADE_IN_MS  = 900;
const FADE_OUT_MS = 500;

/**
 * Fixed trim on the track itself, applied UNDER the user's slider.
 *
 * The mp3 is mastered hot relative to the sfx library, so at pref 1.0 it sat
 * well above the click and hover cues and buried the welcome VO. This is a
 * property of the file, not a preference, so it belongs here rather than in a
 * lowered default: baking it into PREF_DEFAULTS would silently do nothing for
 * anyone who had already touched the slider (their value is persisted), and
 * would misreport the ceiling — "100%" should mean the loudest we intend the
 * song to be, not the loudest the file happens to be.
 */
const TRACK_TRIM = 0.3;

/** How far the song drops under a spoken menu line (see duckMusic). */
const DUCK = 0.28;

/**
 * NATIVE TIMING, captured at module evaluation.
 *
 * ui/pause.js replaces window.requestAnimationFrame / performance.now with a
 * shim that DEFERS callbacks and FREEZES the clock for as long as the run is
 * paused — that is how it stops beat countdowns dead. This module must not be
 * caught by it: the pause menu is exactly when the music fades back IN, and a
 * fade driven by the frozen clock would never advance, leaving the song silent
 * for the whole pause and then snapping to full volume on resume.
 *
 * Capturing here is safe in every load order: ES module dependencies evaluate
 * before the body of the module that imports them, and pause.js imports this
 * file — so these bindings are always taken before its shim can install.
 */
const NATIVE = (() => {
  const w = (typeof window !== 'undefined') ? window : null;
  const perf = w && w.performance;
  return {
    raf: (w && typeof w.requestAnimationFrame === 'function') ? w.requestAnimationFrame.bind(w) : null,
    caf: (w && typeof w.cancelAnimationFrame === 'function') ? w.cancelAnimationFrame.bind(w) : null,
    now: (perf && typeof perf.now === 'function') ? perf.now.bind(perf) : () => Date.now(),
  };
})();

let audio = null;      // the singleton HTMLAudioElement
let unsub = null;      // prefs subscription
let fadeRaf = 0;       // in-flight fade handle
let armed = false;     // a gesture retry is pending
let wanted = false;    // the shell wants music playing (independent of the pref)

/* --- the corruption graph (built lazily; see setMusicCorruption) ----------- */
let ac = null;         // our OWN AudioContext — never render/audio.js's
let srcNode = null;    // MediaElementAudioSourceNode: the one-way door
let levelGain = null;  // the level, once routed (element pinned to volume 1)
let shaper = null;     // WaveShaperNode: the actual distortion
let lp = null;         // BiquadFilterNode: the lowpass that eats the sparkle
let outGain = null;    // makeup attenuation (drive adds loudness)
let graphDead = false; // a build failed for good; never try again
let corrupt = 0;       // last applied 0..1 level

/** Resolve the mp3 against THIS module, so the page's own path never matters —
 *  then let core/audioSrc.js say WHICH host it is on (the track ships as a
 *  downloaded content pack now; ensureEl retries the other host once). */
function srcUrl() {
  try { return audioUrl(new URL(SRC, import.meta.url).href); }
  catch (_e) { return './assets/music/menu-theme.mp3'; }
}

function cancelFade() {
  if (fadeRaf && NATIVE.caf) { NATIVE.caf(fadeRaf); }
  fadeRaf = 0;
}

/**
 * THE LEVEL, read/written through ONE pair of accessors.
 *
 * Normally this is just `audio.volume`. Once the corruption graph is live (see
 * setMusicCorruption below) the element is pinned to volume 1 and the level
 * moves to the graph's `levelGain` instead — pinned rather than multiplied, so
 * the level is applied EXACTLY ONCE either way and no fade, duck or mute
 * silently squares itself the moment the graph appears. Every existing caller
 * (fadeTo, the prefs subscription, duckMusic, stopMenuMusic) goes through here,
 * so mute/duck/fade behave identically routed or not.
 */
function getLevel() {
  if (levelGain) { try { return levelGain.gain.value; } catch (_e) { /* fall through */ } }
  return audio ? audio.volume : 0;
}
function setLevel(v) {
  const to = Math.min(1, Math.max(0, v));
  if (levelGain) { try { levelGain.gain.value = to; return; } catch (_e) { /* fall through */ } }
  try { if (audio) audio.volume = to; } catch (_e) { /* detached */ }
}

/**
 * Glide the level to a target. Linear is fine here — this is a music bed,
 * not a gain node feeding a limiter, and the ear reads a 900ms linear music
 * fade as smooth. Calls back when it lands (used to pause after a fade-out).
 */
function fadeTo(target, ms, done) {
  if (!audio) return;
  cancelFade();
  const from = getLevel();
  const to = Math.min(1, Math.max(0, target));
  // No native rAF (headless/import-safety) -> land immediately rather than
  // leaving the track stuck at its starting volume forever.
  if (ms <= 0 || from === to || !NATIVE.raf) {
    setLevel(to);
    if (done) done();
    return;
  }
  const t0 = NATIVE.now();
  const step = () => {
    // Read the clock ourselves instead of trusting the rAF timestamp argument:
    // pause.js hands its deferred callbacks a doctored `now`, and while we hold
    // the native rAF we should not depend on the shape of what it passes.
    const k = Math.min(1, (NATIVE.now() - t0) / ms);
    setLevel(from + (to - from) * k);
    if (k < 1) { fadeRaf = NATIVE.raf(step); }
    else { fadeRaf = 0; if (done) done(); }
  };
  fadeRaf = NATIVE.raf(step);
}

/** The pref gain with the file's fixed trim folded in — the ONE place that
 *  decides how loud the song actually is. Mute (0) stays exactly 0. */
function musicGain() { return musicScale() * TRACK_TRIM; }

function ensureEl() {
  if (audio) return audio;
  audio = new Audio(srcUrl());
  audio.loop = true;
  audio.preload = 'auto';
  audio.volume = 0;          // always fade up from silence
  // Long file: let it stream rather than blocking on a full buffer.
  // crossOrigin also matters once the track comes from the content host: the
  // corruption graph below taps the element through WebAudio, and a tapped
  // cross-origin element without CORS goes silent.
  try { audio.crossOrigin = 'anonymous'; } catch (_e) { /* same-origin anyway */ }

  // Not on the host we guessed? Swap to the other one ONCE and pick up where we
  // were (altSrcFor returns null the second time, so a track that is on neither
  // host simply stays silent — the shell has always tolerated that).
  audio.addEventListener('error', () => {
    const el = audio;
    if (!el) return;
    const alt = altSrcFor(el);
    if (!alt) return;
    try {
      el.src = alt;
      el.load();
      if (wanted && musicGain() > 0) play();
    } catch (_e) { /* silent shell is an acceptable end state */ }
  });

  // Live pref tracking: volume slider retunes the playing track, and the mute
  // switch fades out/in rather than cutting, so toggling it is never a pop.
  unsub = subscribe((_p, key) => {
    if (key && key !== 'musicOn' && key !== 'volMusic') return;
    if (!audio) return;
    const g = musicGain();
    if (g <= 0) { fadeTo(0, FADE_OUT_MS, () => { try { audio && audio.pause(); } catch (_e) {} }); }
    else if (wanted) { play(); fadeTo(g, 220); }
  });
  return audio;
}

/** Attempt playback, arming a gesture retry if the browser refuses. */
function play() {
  if (!audio) return;
  let p = null;
  try { p = audio.play(); } catch (_e) { /* older shapes throw synchronously */ }
  if (!p || typeof p.catch !== 'function') return;
  p.catch(() => {
    if (armed) return;
    armed = true;
    const retry = () => {
      armed = false;
      remove();
      if (wanted && musicGain() > 0) { try { audio && audio.play(); } catch (_e) {} }
    };
    const remove = () => {
      for (const ev of ['pointerdown', 'keydown', 'touchstart']) {
        try { window.removeEventListener(ev, retry, true); } catch (_e) {}
      }
    };
    for (const ev of ['pointerdown', 'keydown', 'touchstart']) {
      try { window.addEventListener(ev, retry, { capture: true, once: true }); } catch (_e) {}
    }
  });
}

/* ----------------------------------------------------------------------------
 * PUBLIC API
 * -------------------------------------------------------------------------- */

/**
 * Start (or resume) the shell music, fading up to the pref level. Idempotent —
 * calling it from the menu and again from the pause menu is safe and will not
 * restart the track, so the song keeps its place across screens.
 */
export function startMenuMusic() {
  wanted = true;
  ensureEl();
  if (musicGain() <= 0) return;    // muted: stay wanted, stay silent
  play();
  fadeTo(musicGain(), FADE_IN_MS);
}

/**
 * Fade out and pause. Keeps the element and its position, so returning to a
 * menu resumes mid-song instead of restarting the intro every time.
 * @param {{immediate?:boolean}} [opts]
 */
export function stopMenuMusic(opts) {
  wanted = false;
  if (!audio) return;
  const ms = (opts && opts.immediate) ? 0 : FADE_OUT_MS;
  fadeTo(0, ms, () => { try { audio && audio.pause(); } catch (_e) {} });
}

/** True when the music pref is on (regardless of whether it is audible now). */
export function isMusicOn() { return !!getPrefs().musicOn; }

/**
 * Flip the music pref. The prefs subscription above does the actual fade, so
 * every mute affordance (menu button, options row) stays in sync for free.
 * @returns {boolean} the new state
 */
export function toggleMusic() {
  const next = !getPrefs().musicOn;
  setPref('musicOn', next);
  return next;
}

/**
 * Duck the song under a spoken line and lift it again afterwards.
 *
 * Mirrors what render/audio.js does to the binaural bed under VO: the greeting
 * is the point of the screen, and at parity the mastered track sits right on
 * top of it. Reads the CURRENT gain each time rather than caching a "pre-duck"
 * level, so a slider move (or a mute) mid-sentence still wins.
 *
 * @param {boolean} on true to duck, false to restore
 */
export function duckMusic(on) {
  if (!audio) return;
  // THE SHELL NO LONGER WANTS MUSIC -> never touch the volume.
  // Without this, leaving the menu is a race the un-duck wins: finish() calls
  // stopMenuMusic() (fade to 0, then pause), then teardown runs the cleanups,
  // one of which stops the welcome VO and lifts its duck. That lift cancels the
  // in-flight fade-out and restores full gain, so the pause() callback never
  // fires and the song plays on into the run at full volume.
  if (!wanted) return;
  const g = musicGain();
  if (g <= 0) return;                       // muted: nothing to duck
  fadeTo(on ? g * DUCK : g, on ? 260 : 620);
}

/* ----------------------------------------------------------------------------
 * CORRUPTION — the song slows, drops pitch and distorts as the run goes deeper.
 * Driven by ui/corruption.js off the SAME 0..1 level as the palette rot, so the
 * ear and the eye can never report different depths.
 *
 * WHY A WEBAUDIO GRAPH (and what it costs).
 *   An <audio> element gives `playbackRate` for free, and with `preservesPitch`
 *   off a slow-down also drops the pitch — that alone is most of the "wrong".
 *   But an element cannot DISTORT, and distortion is half of what was asked
 *   for, so the element is routed through a WaveShaper (soft-clip drive) and a
 *   lowpass. The cost is a genuine ONE-WAY DOOR: after
 *   createMediaElementSource() the element's audio ONLY ever leaves through the
 *   graph. Three consequences, all handled:
 *     · We use our OWN AudioContext, never render/audio.js's — that one is
 *       suspended by pause (which is exactly when this music plays) and
 *       disposed at end of run.
 *     · We refuse to walk through the door until the context is actually
 *       RUNNING. A context blocked by autoplay policy would otherwise swallow
 *       the song forever; instead we bail, retry on the next call, and the
 *       element keeps playing normally in the meantime.
 *     · The level moves into the graph on routing (see getLevel/setLevel), so
 *       fades, `duckMusic` and the mute pref keep working unchanged rather than
 *       depending on element volume surviving the tap.
 *   The graph is only ever built when corruption is non-zero, i.e. from band 3
 *   of a run — never on the main menu, and never at all in a run the player
 *   quits early. disposeMenuMusic() tears it down.
 * -------------------------------------------------------------------------- */

/** How far the song slows at full corruption (1.0 -> 0.66x, pitch riding down
 *  with it). Deep enough to read as WRONG, shallow enough to stay a song. */
const RATE_DROP = 0.34;

/** Soft-clip curve. k=0 is a straight line (transparent); k climbs with the
 *  corruption level into a fat, ugly saturation. */
function driveCurve(k) {
  const n = 1024;
  const curve = new Float32Array(n);
  for (let i = 0; i < n; i++) {
    const x = (i * 2) / (n - 1) - 1;
    curve[i] = (1 + k) * x / (1 + k * Math.abs(x));
  }
  return curve;
}

/** Build the graph if we safely can. Returns true when audio is routed. */
function ensureGraph() {
  if (srcNode) return true;
  if (graphDead || !audio) return false;
  const AC = (typeof window !== 'undefined') && (window.AudioContext || window.webkitAudioContext);
  if (!AC) { graphDead = true; return false; }
  try {
    if (!ac) ac = new AC();
  } catch (_e) { graphDead = true; return false; }

  if (ac.state !== 'running') {
    // Not through the door yet. Ask for a resume and try again on the next
    // call — never route into a context that might never start.
    try {
      const p = ac.resume();
      if (p && typeof p.then === 'function') p.then(() => { applyCorruptionToAudio(); }, () => {});
    } catch (_e) { /* some hosts throw before a gesture */ }
    return false;
  }

  try {
    const lvl = audio.volume;              // carry the current level across
    srcNode  = ac.createMediaElementSource(audio);
    levelGain = ac.createGain();
    shaper   = ac.createWaveShaper();
    lp       = ac.createBiquadFilter();
    outGain  = ac.createGain();
    lp.type = 'lowpass';
    lp.frequency.value = 20000;
    lp.Q.value = 0.7;
    shaper.curve = driveCurve(0);
    shaper.oversample = '2x';
    levelGain.gain.value = lvl;
    outGain.gain.value = 1;
    srcNode.connect(levelGain);
    levelGain.connect(lp);
    lp.connect(shaper);
    shaper.connect(outGain);
    outGain.connect(ac.destination);
    audio.volume = 1;                      // the level lives in levelGain now
    return true;
  } catch (_e) {
    // Half-built graph: fail closed and stay on the plain element forever.
    graphDead = true;
    srcNode = null; levelGain = null; shaper = null; lp = null; outGain = null;
    return false;
  }
}

/** Push the current `corrupt` value onto whatever path we actually have. */
function applyCorruptionToAudio() {
  if (!audio) return;
  const c = corrupt;

  // 1. RATE + PITCH — free, always available, works with or without the graph.
  try {
    audio.playbackRate = 1 - RATE_DROP * c;
    // Let the pitch fall with the speed: that sag IS the effect. Restored to
    // true at c=0 so an uncorrupted shell is bit-for-bit what it was.
    const keep = (c <= 0);
    audio.preservesPitch = keep;
    audio.mozPreservesPitch = keep;
    audio.webkitPreservesPitch = keep;
  } catch (_e) { /* rate unsupported: the graph still carries the effect */ }

  // 2. DISTORTION — needs the graph. Never build one just to be transparent.
  if (c <= 0 && !srcNode) return;
  if (!ensureGraph()) return;
  try {
    shaper.curve = driveCurve(c <= 0 ? 0 : 4 + 70 * c * c);
    // 20kHz -> ~1.1kHz, exponentially: the top end rots away rather than
    // stepping out.
    lp.frequency.value = 20000 * Math.pow(1100 / 20000, c);
    // The drive makes it louder; take that back so corruption never becomes a
    // volume increase behind the player's slider.
    outGain.gain.value = 1 / (1 + 1.15 * c);
  } catch (_e) { /* a dead node is not worth a broken menu */ }
}

/**
 * Set the shell music's corruption, 0..1. Idempotent and cheap; safe to call
 * every beat. 0 restores the clean track exactly (linear curve, open filter,
 * rate 1, pitch preserved).
 * @param {number} level
 */
export function setMusicCorruption(level) {
  const c = Math.min(1, Math.max(0, Number(level) || 0));
  if (c === corrupt) return;
  corrupt = c;
  applyCorruptionToAudio();
}

/** Full teardown — used when the shell is done for good (run start / exit). */
export function disposeMenuMusic() {
  wanted = false;
  corrupt = 0;
  cancelFade();
  if (unsub) { try { unsub(); } catch (_e) {} unsub = null; }
  // The graph first: its source node holds the element, and closing the context
  // is what actually releases it.
  for (const n of [srcNode, levelGain, lp, shaper, outGain]) {
    if (n) { try { n.disconnect(); } catch (_e) {} }
  }
  srcNode = null; levelGain = null; lp = null; shaper = null; outGain = null;
  if (ac) { try { ac.close(); } catch (_e) {} ac = null; }
  graphDead = false;        // a fresh element may route fine next time
  if (audio) {
    try { audio.pause(); } catch (_e) {}
    try { audio.src = ''; } catch (_e) {}
    audio = null;
  }
}
