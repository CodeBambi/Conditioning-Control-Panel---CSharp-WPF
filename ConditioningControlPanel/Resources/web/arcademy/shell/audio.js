/* ============================================================================
 * shell/audio.js - THE ONE consumer of the engine's `arcademy-sfx` requests.
 *
 * GROUND-RULES §6: the engine holds no audio handle, it SHOUTS
 * `CustomEvent('arcademy-sfx', {detail:{name, level, bus, duck?}})` on `document`
 * and somebody else owns the sound. That somebody is this file.
 *
 *   createAudio({ init, bridge, log })  once, from boot.js
 *
 * WHY IT SYNTHESISES: there are no sfx files in the build yet, and a page that
 * 404s six wav files on the first beat is worse than one that beeps. Every sound
 * is a short oscillator/noise envelope, so the mixer is real today and swapping
 * in real samples later only touches `play()`.
 *
 * LEVELS: gain = sqrt(sfx level) x group level x masterVolume x (audioMute ? 0 : 1).
 * The group levels arrive already clamped in `init.audioLevels` and move ONLY on
 * the host's `setting` echo (`audioLevels.fx`, `audioMute`, `masterVolume`) -
 * same law as the settings page: the echo is the truth, never the control.
 *
 * DUCKING: `detail.duck = {target, mult, ms}` is already scaled by the player's
 * duckDepth cap (engine/curves.js DUCK .4/.25/.15). We ramp the affected buses to
 * `mult` and back over ~200ms. A duck is never a snap.
 *
 * PITCH: `detail.pitch` (optional, 0.5-2, default 1) multiplies every frequency in
 * the recipe - oscillator sweeps, arpeggio steps, the noise band and the stamp's
 * body thunk. That is the whole feature: three games wanted a pitch ratchet (a
 * rising chain of hits) and were reduced to ratcheting the LEVEL instead, which
 * reads as "louder", not as "climbing". Garbage clamps to 1, so an older engine
 * that never sends the field sounds exactly as it did.
 *
 * AUTOPLAY: no AudioContext is created until the first user gesture; requests
 * before that are dropped silently (a beep the browser refuses is not an error).
 * No AudioContext at all (headless, old webview) -> every call is a no-op.
 *
 * CLIPS (2026-08-23, Echo's trigger bubbles). A cue may carry `detail.url` - a
 * SAME-ORIGIN `ccp.*` media file (the app's own whisper clips). It is then
 * played from an HTMLAudioElement routed through the requested bus, so every
 * law above still holds: mute, master, bus level, ducking. Three rules make it
 * safe for a game that fires one per press:
 *   - `detail.key` is a VOICE SLOT. A second clip on the same key cuts the
 *     first (a fast sequence must not pile six whispers on top of each other);
 *     with no key the url is the slot.
 *   - `detail.maxMs` truncates playback (default CLIP_MAX_MS) with a short
 *     fade, so a 6-second phrase does not outlive the round.
 *   - a clip is never louder than a recipe at the same level: CLIP_GAIN is the
 *     same headroom the SOUNDS table's `gain` gives an oscillator.
 * A url the browser will not decode is silently dropped, exactly like a name
 * that is not in SOUNDS - a cue must never be the thing that throws.
 * ==========================================================================*/

const BUSES = ['fx', 'voice', 'tutorial', 'drops', 'music'];
const DEFAULT_LEVELS = { fx: 0.85, voice: 0.85, tutorial: 0.85, drops: 0.4, music: 1.0 };

/** Which buses a duck target pulls down (DTRH's sidechain hierarchy). */
const DUCK_TARGETS = {
  voice: ['fx', 'music', 'drops'],
  spotlight: ['fx', 'voice', 'tutorial', 'music'],
  voiceUnderSpotlight: ['voice', 'tutorial', 'fx', 'music'],
};

/** name -> recipe. `noise` = filtered white noise, else an oscillator sweep.
 *  Unknown names fall through to `blip` (a game may invent an sfx id freely). */
const SOUNDS = {
  blip:      { type: 'sine',     f0: 660, f1: 660, ms: 60,  gain: 0.6 },
  sting:     { type: 'triangle', f0: 440, f1: 520, ms: 140, gain: 0.7 },
  glitch:    { noise: true, hp: 900,  lp: 5200, ms: 90,  gain: 0.8, bits: 6 },
  whisper:   { noise: true, hp: 1400, lp: 4200, ms: 240, gain: 0.35 },
  wash:      { noise: true, hp: 120,  lp: 900,  ms: 900, gain: 0.5, attack: 0.35 },
  flash:     { noise: true, hp: 2000, lp: 9000, ms: 50,  gain: 0.7 },
  burst:     { noise: true, hp: 700,  lp: 6000, ms: 120, gain: 0.7 },
  pop:       { type: 'sine',     f0: 900, f1: 260, ms: 80,  gain: 0.8 },
  bubble_pop:{ type: 'sine',     f0: 1200, f1: 380, ms: 70, gain: 0.7 },
  stamp:     { type: 'sine',     f0: 150, f1: 58,  ms: 190, gain: 1.0, thunk: true },
  stamp_bad: { type: 'sawtooth', f0: 96,  f1: 44,  ms: 240, gain: 0.9, thunk: true },
  /* W0 (2026-08-24): Anomaly's wrong-tap / round-timeout beats requested `thud`
     since Semester II and were silently degrading to `blip` - a bright tick on
     the game's two LOSS moments, the exact opposite of the muted thud the House
     Book asks for. Softer and shorter than either stamp: a body knock, no bite. */
  thud:      { type: 'sine',     f0: 130, f1: 52,  ms: 150, gain: 0.85, thunk: true },
  streak:    { arp: [523.25, 659.25], ms: 90,  gain: 0.6 },
  jackpot:   { arp: [523.25, 659.25, 783.99, 1046.5], ms: 110, gain: 0.9 },
  near_miss: { type: 'sawtooth', f0: 180, f1: 760, ms: 520, gain: 0.5, riser: true },
  /* The Deep End, pass 2: the slide is a short filtered-noise whoosh (level =
     tiles moved, pitch = depth); the wall is a low sawtooth buzz with a body
     thunk - a muted loss, never silence. */
  slide:     { noise: true, hp: 260,  lp: 2600, ms: 150, gain: 0.55, attack: 0.18 },
  bump:      { type: 'sawtooth', f0: 92,  f1: 46,  ms: 150, gain: 0.85, thunk: true },
  /* Semesters II/III (2026-08-23). `pad` is Echo's instrument: an UNSWEPT triangle
     so `pitch` alone carries pad identity (six pads = six pitches off one recipe,
     +1 semitone per streak link); `decoy` is the telegraphed false pad. `tell` /
     `lift` are Misdirection's trackability tell and the decoy-lid sting; `near` the
     Anomaly near-miss ping (distinct from the long `near_miss` riser); `chime` a
     clean bell for the streak ladders; `shutter` the darkroom's camera click. */
  pad:       { type: 'triangle', f0: 392, f1: 392, ms: 200, gain: 0.55 },
  decoy:     { noise: true, hp: 600,  lp: 3800, ms: 120, gain: 0.6, bits: 5 },
  tell:      { type: 'sine',     f0: 880, f1: 880, ms: 70,  gain: 0.5 },
  lift:      { type: 'triangle', f0: 330, f1: 495, ms: 120, gain: 0.55 },
  near:      { type: 'sine',     f0: 740, f1: 620, ms: 110, gain: 0.55 },
  chime:     { type: 'sine',     f0: 1046.5, f1: 1046.5, ms: 160, gain: 0.5 },
  shutter:   { noise: true, hp: 1800, lp: 9000, ms: 40,  gain: 0.7 },
  /* EMI's VOICE, "Blipese" (2026-08-24, emi/vox.js). Two recipes and no third:
     the mascot babbles by firing `emi_blip` many times with a per-blip `pitch`,
     so the TIMBRE lives here, once, and the melody lives over there.
     TRIANGLE, not square. The owner's directive is "pleasant at low volume - she
     has to be someone you get attached to", and a square up here is a beeper.
     The chirp falls ~a whole tone across the blip so each one reads as a little
     syllable rather than a tone; `attack` 0.3 of the duration (~17ms of the 56)
     is what keeps it from clicking; and gain 0.35 is HALF a game pop. A mascot
     murmurs - she is never the loudest thing on the screen.
     TASTE ROUND (owner): the square variant is kept here rather than in a doc so
     the comparison is a two-line swap.
       emi_blip:  { type: 'square', f0: 760, f1: 680, ms: 52, gain: 0.26, attack: 0.34 },
   */
  emi_blip:  { type: 'triangle', f0: 760, f1: 680, ms: 56, gain: 0.35, attack: 0.3 },
  /* ...and the tick under the `.` `..` `...` typing frames: low, short, a fifth
     of the blip's gain. It is anticipation, not a sound in its own right - the
     moment a player NOTICES it, it is wrong (vox.js DOT_TICKS turns it off). */
  emi_tick:  { type: 'triangle', f0: 336, f1: 322, ms: 30, gain: 0.16, attack: 0.35 },
};

const clamp01 = (v) => (Number.isFinite(+v) ? Math.max(0, Math.min(1, +v)) : 0);

/** Clip playback ceilings. MAX_MS truncates, FADE_MS is the way out, VOICES is
 *  the hard cap on simultaneous slots (Echo needs six, one per pad). */
const CLIP_MAX_MS = 1200;
const CLIP_FADE_MS = 180;
const CLIP_VOICES = 6;
/** The headroom a recipe gets from its own `gain`, given to clips too, so a
 *  clip at level L is never louder than an oscillator at level L. */
const CLIP_GAIN = 0.5;

/** Playback-rate multiplier for a cue. Anything unusable is 1 (unpitched). */
const PITCH_MIN = 0.5;
const PITCH_MAX = 2;
const clampPitch = (v) => (
  Number.isFinite(+v) && +v > 0 ? Math.max(PITCH_MIN, Math.min(PITCH_MAX, +v)) : 1
);

/**
 * @param {Object} o
 * @param {Object} o.init    the init projection (audioLevels / audioMute / masterVolume)
 * @param {Object=} o.bridge the shell bridge (for the `setting` echo). Optional.
 * @param {Function=} o.log
 */
export function createAudio({ init, bridge, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const src = init || {};
  const doc = (typeof document !== 'undefined') ? document : null;

  const levels = Object.assign({}, DEFAULT_LEVELS);
  const initLevels = (src.audioLevels && typeof src.audioLevels === 'object') ? src.audioLevels : {};
  for (const b of BUSES) if (initLevels[b] != null) levels[b] = clamp01(initLevels[b]);
  let mute = !!src.audioMute;
  let master = src.masterVolume == null ? 1 : clamp01(src.masterVolume);

  const Ctor = (typeof AudioContext !== 'undefined') ? AudioContext
    : (typeof window !== 'undefined' && window.webkitAudioContext) ? window.webkitAudioContext : null;

  let ac = null;
  let out = null;                    // master gain
  const busGain = Object.create(null);   // bus -> {level: GainNode, duck: GainNode}
  let gestured = false;
  const stats = { handled: 0, played: 0, dropped: 0, ducks: 0, clips: 0, last: null };

  function ensureContext() {
    if (ac || !Ctor || !gestured) return ac;
    try {
      ac = new Ctor();
      out = ac.createGain();
      out.gain.value = 1;
      out.connect(ac.destination);
      for (const b of BUSES) {
        const level = ac.createGain();
        const duck = ac.createGain();
        level.gain.value = levels[b];
        duck.gain.value = 1;
        level.connect(duck); duck.connect(out);
        busGain[b] = { level, duck };
      }
      applyMaster();
      say('[audio] context up (' + (ac.sampleRate || '?') + 'Hz)');
    } catch (e) { ac = null; say('[audio] no context: ' + ((e && e.message) || e)); }
    return ac;
  }

  function applyMaster() { if (out) try { out.gain.value = mute ? 0 : master; } catch { /* ignore */ } }
  function applyLevel(b) {
    const g = busGain[b];
    if (g) try { g.level.gain.value = levels[b]; } catch { /* ignore */ }
  }

  /** One noise buffer, reused: allocating 900ms of noise per beat is a stutter. */
  let noiseBuf = null;
  function noiseBuffer() {
    if (noiseBuf || !ac) return noiseBuf;
    const len = Math.max(1, Math.floor((ac.sampleRate || 44100) * 1.2));
    noiseBuf = ac.createBuffer(1, len, ac.sampleRate || 44100);
    const d = noiseBuf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    return noiseBuf;
  }

  function envelope(node, peak, ms, attackFrac) {
    const t = ac.currentTime;
    const dur = Math.max(0.02, ms / 1000);
    const atk = Math.max(0.004, dur * (attackFrac == null ? 0.06 : attackFrac));
    const g = node.gain;
    g.setValueAtTime(0.0001, t);
    g.linearRampToValueAtTime(Math.max(0.0002, peak), t + atk);
    g.exponentialRampToValueAtTime(0.0001, t + dur);
    return { t, dur };
  }

  function voiceOut(bus, node) {
    const g = busGain[bus] || busGain.fx;
    if (g) node.connect(g.level);
  }

  /** Frequencies live in a 20Hz..20kHz sanity window whatever the pitch asks for. */
  const hz = (f, pitch) => Math.max(20, Math.min(20000, f * pitch));

  function playRecipe(rec, bus, amp, pitch) {
    const p = clampPitch(pitch);
    const env = ac.createGain();
    voiceOut(bus, env);
    // Duration is deliberately NOT scaled: a pitch ratchet should climb, not
    // speed up - the cadence belongs to whoever is firing the cues.
    const { t, dur } = envelope(env, amp * (rec.gain == null ? 0.7 : rec.gain), rec.ms, rec.attack);
    const stop = t + dur + 0.02;

    if (rec.noise) {
      const s = ac.createBufferSource();
      s.buffer = noiseBuffer();
      s.loop = true;
      const f = ac.createBiquadFilter();
      f.type = 'bandpass';
      f.frequency.value = hz(Math.sqrt(Math.max(40, rec.hp) * Math.max(80, rec.lp)), p);
      f.Q.value = rec.bits ? 1.6 : 0.7;
      s.connect(f); f.connect(env);
      s.start(t); s.stop(stop);
      return;
    }
    if (rec.arp) {
      rec.arp.forEach((f0, i) => {
        const o = ac.createOscillator();
        const g = ac.createGain();
        o.type = 'triangle';
        o.frequency.value = hz(f0, p);
        const at = t + i * (rec.ms / 1000) * 0.75;
        g.gain.setValueAtTime(0.0001, at);
        g.gain.linearRampToValueAtTime(amp * rec.gain * 0.6, at + 0.012);
        g.gain.exponentialRampToValueAtTime(0.0001, at + rec.ms / 1000);
        o.connect(g); g.connect((busGain[bus] || busGain.fx).level);
        o.start(at); o.stop(at + rec.ms / 1000 + 0.02);
      });
      return;
    }
    const o = ac.createOscillator();
    o.type = rec.type || 'sine';
    o.frequency.setValueAtTime(hz(rec.f0, p), t);
    if (rec.riser) o.frequency.linearRampToValueAtTime(hz(rec.f1, p), t + dur);
    else o.frequency.exponentialRampToValueAtTime(hz(rec.f1, p), t + dur);
    o.connect(env);
    o.start(t); o.stop(stop);
    if (rec.thunk) {                     // a stamp is a tone AND a body hit
      const n = ac.createBufferSource();
      n.buffer = noiseBuffer();
      const f = ac.createBiquadFilter();
      f.type = 'lowpass'; f.frequency.value = hz(420, p);
      const g = ac.createGain();
      envelope(g, amp * 0.5, Math.min(90, rec.ms));
      n.connect(f); f.connect(g); voiceOut(bus, g);
      n.start(t); n.stop(t + 0.12);
    }
  }

  /* ---- CLIPS: a url played through a bus ------------------------------- */
  /** key -> {el, gain, node, timer} . One live clip per slot, cut on re-fire. */
  const clips = new Map();

  function killClip(rec, fadeMs) {
    if (!rec) return;
    try { if (rec.timer) clearTimeout(rec.timer); } catch { /* ignore */ }
    rec.timer = 0;
    const fade = Math.max(0, Number(fadeMs) || 0);
    const stop = () => {
      try { rec.el.pause(); } catch { /* ignore */ }
      // Releasing the src lets the decoder go; a MediaElementSource cannot be
      // re-created for the same element, so the element is never reused.
      try { rec.el.src = ''; } catch { /* ignore */ }
      try { if (rec.node) rec.node.disconnect(); } catch { /* ignore */ }
      try { if (rec.gain) rec.gain.disconnect(); } catch { /* ignore */ }
    };
    if (fade > 0 && ac && rec.gain) {
      try {
        const t = ac.currentTime;
        rec.gain.gain.cancelScheduledValues(t);
        rec.gain.gain.setValueAtTime(Math.max(0.0001, rec.gain.gain.value), t);
        rec.gain.gain.exponentialRampToValueAtTime(0.0001, t + fade / 1000);
      } catch { /* ignore */ }
      setTimeout(stop, fade + 20);
      return;
    }
    stop();
  }

  /** @returns {boolean} true if the clip was taken (played or scheduled). */
  function playClip(d, bus, amp) {
    if (typeof Audio !== 'function') return false;
    const url = String(d.url == null ? '' : d.url);
    if (!url) return false;
    const key = String(d.key == null ? url : d.key);

    const prev = clips.get(key);
    if (prev) { clips.delete(key); killClip(prev, 60); }
    // A game that forgets to key its slots must still not run away with the
    // decoders: the oldest slot goes first.
    while (clips.size >= CLIP_VOICES) {
      const oldest = clips.keys().next().value;
      const rec = clips.get(oldest);
      clips.delete(oldest);
      killClip(rec, 60);
    }

    let el;
    try {
      el = new Audio();
      // The ccp.* origins are mapped CORS-clean, which is what lets the element
      // feed a WebAudio graph at all; a tainted stream would only play direct.
      el.crossOrigin = 'anonymous';
      el.preload = 'auto';
      el.src = url;
    } catch { return false; }

    const maxMs = Math.max(80, Math.min(CLIP_MAX_MS, Number(d.maxMs) || CLIP_MAX_MS));
    const fadeMs = Math.max(0, Math.min(maxMs / 2, Number(d.fadeMs) || CLIP_FADE_MS));
    const rec = { el, gain: null, node: null, timer: 0 };

    let routed = false;
    try {
      const g = ac.createGain();
      g.gain.value = Math.max(0.0001, amp * CLIP_GAIN);
      const node = ac.createMediaElementSource(el);
      node.connect(g);
      voiceOut(bus, g);
      rec.gain = g;
      rec.node = node;
      routed = true;
    } catch {
      // No MediaElementSource (an older webview, a tainted stream): fall back to
      // the element's own volume, folding in every level the graph would have.
      routed = false;
    }
    if (!routed) {
      try {
        el.volume = clamp01(amp * CLIP_GAIN * (levels[bus] == null ? 1 : levels[bus]) * master * (mute ? 0 : 1));
      } catch { /* ignore */ }
    }

    clips.set(key, rec);
    rec.timer = setTimeout(() => {
      rec.timer = 0;
      if (clips.get(key) === rec) clips.delete(key);
      killClip(rec, fadeMs);
    }, maxMs);
    try { el.addEventListener('ended', () => { if (clips.get(key) === rec) { clips.delete(key); killClip(rec, 0); } }); } catch { /* ignore */ }
    try { el.addEventListener('error', () => { if (clips.get(key) === rec) { clips.delete(key); killClip(rec, 0); } }); } catch { /* ignore */ }

    try {
      const p = el.play();
      if (p && typeof p.catch === 'function') p.catch(() => { /* a refused clip is not an error */ });
    } catch { /* ignore */ }
    return true;
  }

  function stopAllClips() {
    for (const [k, rec] of Array.from(clips.entries())) { clips.delete(k); killClip(rec, 0); }
  }

  function duck(spec) {
    if (!ac || !spec) return;
    const targets = DUCK_TARGETS[spec.target] || DUCK_TARGETS.voice;
    const mult = clamp01(spec.mult == null ? 0.4 : spec.mult);
    const ms = Math.max(60, Math.min(2000, Number(spec.ms) || 200));
    const t = ac.currentTime;
    for (const b of targets) {
      const g = busGain[b];
      if (!g) continue;
      try {
        g.duck.gain.cancelScheduledValues(t);
        g.duck.gain.setValueAtTime(g.duck.gain.value, t);
        g.duck.gain.linearRampToValueAtTime(mult, t + 0.05);
        g.duck.gain.setValueAtTime(mult, t + ms / 1000);
        g.duck.gain.linearRampToValueAtTime(1, t + ms / 1000 + 0.2);
      } catch { /* ignore */ }
    }
    stats.ducks += 1;
  }

  function onSfx(e) {
    const d = (e && e.detail) || {};
    stats.handled += 1;
    // The one CONTROL message on the sfx bus: the shell sends it when a class is
    // torn down so a trigger clip (<=1.2s) never leaks into the lobby. No audio
    // handle crosses into shell.js for this; the bus was already the seam.
    if (d.name === 'stop_clips') { stopAllClips(); return; }
    const pitch = clampPitch(d.pitch);
    stats.last = {
      name: d.name || null, level: d.level, bus: d.bus || 'fx', duck: d.duck || null, pitch,
      url: d.url || null,
    };
    if (mute || master <= 0) { stats.dropped += 1; return; }
    if (!ensureContext()) { stats.dropped += 1; return; }
    const bus = BUSES.indexOf(d.bus) >= 0 ? d.bus : 'fx';
    // PERCEPTUAL CURVE (2026-08-24): engine levels are fractions of fractions - a 0.25
    // cue under the bus and master gains landed near -29 dB and the whole campus read
    // as silent. sqrt lifts the quiet floor (0.25 -> 0.5) while 1.0 stays 1.0, so the
    // relative loudness ladder the games ratchet is preserved, just audible.
    const amp = Math.sqrt(clamp01(d.level == null ? 0.5 : d.level));
    if (amp <= 0 || levels[bus] <= 0) { stats.dropped += 1; return; }
    try {
      // A url is a CLIP, whatever the name says. If the host cannot play one we
      // fall through to the recipe rather than going silent.
      const took = d.url ? playClip(d, bus, amp) : false;
      if (took) stats.clips += 1;
      else playRecipe(SOUNDS[String(d.name || '')] || SOUNDS.blip, bus, amp, pitch);
      stats.played += 1;
    } catch (err) { stats.dropped += 1; say('[audio] ' + (d.name || '?') + ' failed: ' + ((err && err.message) || err)); }
    if (d.duck) duck(d.duck);
  }

  /** The host's echo is the ONLY thing that moves a level (settings.js trap 1). */
  function onSetting(m) {
    const key = m && m.key;
    if (typeof key !== 'string') return;
    if (key === 'audioMute') {
      mute = !!m.value;
      applyMaster();
      // A muted master silences the graph, but a clip that fell back to the
      // element's own volume is outside it - cut them rather than trust it.
      if (mute) stopAllClips();
      return;
    }
    if (key === 'masterVolume') { master = clamp01(m.value); applyMaster(); return; }
    if (key.indexOf('audioLevels.') === 0) {
      const b = key.slice('audioLevels.'.length);
      if (BUSES.indexOf(b) >= 0) { levels[b] = clamp01(m.value); applyLevel(b); }
    }
  }

  function onGesture() {
    if (gestured) return;
    gestured = true;
    ensureContext();
    if (ac && ac.state === 'suspended' && typeof ac.resume === 'function') { try { ac.resume(); } catch { /* ignore */ } }
  }

  if (doc && doc.addEventListener) {
    doc.addEventListener('arcademy-sfx', onSfx);
    doc.addEventListener('pointerdown', onGesture, true);
    doc.addEventListener('keydown', onGesture, true);
  }
  const offSetting = (bridge && typeof bridge.on === 'function') ? bridge.on('setting', onSetting) : () => {};
  if (!Ctor) say('[audio] no AudioContext in this host - sfx are inert');

  return {
    onSfx,                             // exported for the harness / a manual cue
    onSetting,
    stopClips: stopAllClips,           // the shell cuts every clip on teardown
    liveClips: () => clips.size,
    stats: () => Object.assign({ mute, master, levels: Object.assign({}, levels), gestured, live: !!ac }, stats),
    destroy() {
      if (doc && doc.removeEventListener) {
        doc.removeEventListener('arcademy-sfx', onSfx);
        doc.removeEventListener('pointerdown', onGesture, true);
        doc.removeEventListener('keydown', onGesture, true);
      }
      try { offSetting(); } catch { /* ignore */ }
      stopAllClips();
      if (ac && typeof ac.close === 'function') { try { ac.close(); } catch { /* ignore */ } }
      ac = null; out = null; noiseBuf = null;
    },
  };
}

export default createAudio;
