/* sound.js - the slot's cues (lane F1). No new files: the chime family and the thud are the race's own
 * clips (dtrh/assets/bubbles/sfx, played the way dtrh/race/audio.js plays them: one buffer, pitched by
 * playbackRate) and the host/content-pack lookup is dtrh/shared/audioSrc.js. A clip that has not decoded
 * yet falls back to the race's synth thump, so a beat is never silent (Brake 6, Law X).
 *
 * One AudioContext per open(), created on the first press (a gesture), closed in dispose(). */

import { audioUrl, altAudioUrl } from '../../../dtrh/shared/audioSrc.js';

const FILES = { chime1: 'chime1.mp3', chime2: 'chime2.mp3', chime3: 'chime3.mp3', thud: 'thud.mp3' };
const LEVEL = { thud: 0.5, muted: 0.22, chime: 0.34, token: 0.12 };
const rate = s => 2 ** (s / 12);

export function createSound() {
  let ctx = null, out = null, noise = null, suspended = false, disposed = false, rising = null;
  let climbing = [];   // ladder notes scheduled ahead across a rollup, so a skip can silence them (Law VI)
  const buffers = new Map();
  const trace = [];   // the last cues with their page time, for dev.html and CDP checks

  function graph() {
    if (ctx || disposed) return ctx;
    const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AC) return null;
    try {
      ctx = new AC(); out = ctx.createGain(); out.gain.value = 0.9; out.connect(ctx.destination);
      const n = ctx.createBuffer(1, ctx.sampleRate / 4, ctx.sampleRate), d = n.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
      noise = n;
    } catch { ctx = null; return null; }
    for (const [key, file] of Object.entries(FILES)) {
      const first = audioUrl(new URL(`../../../dtrh/assets/bubbles/sfx/${file}`, import.meta.url).href);
      const grab = url => fetch(url).then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.arrayBuffer(); })
        .then(raw => new Promise((res, rej) => ctx.decodeAudioData(raw, res, rej)));
      grab(first).catch(() => { const alt = altAudioUrl(first); if (!alt) throw new Error('no alt'); return grab(alt); })
        .then(b => buffers.set(key, b)).catch(() => console.warn(`[slot] cue ${file} unavailable, synth stands in`));
    }
    return ctx;
  }

  const live = () => !disposed && !suspended && graph() && ctx.state !== 'closed';
  function note(name, semis, level, inMs = 0) { trace.push({ name, semis, level, at: Math.round(performance.now()), in: Math.round(inMs) }); if (trace.length > 60) trace.shift(); }

  function play(key, { semis = 0, level = 0.3, at = 0, lowpass = 0 } = {}) {
    const buf = buffers.get(key);
    if (!buf) return false;
    try {
      const t = ctx.currentTime + at, src = ctx.createBufferSource(), g = ctx.createGain();
      src.buffer = buf; src.playbackRate.value = rate(semis); g.gain.value = level;
      let head = src;
      if (lowpass) { const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = lowpass; head.connect(f); head = f; }
      head.connect(g); g.connect(out); src.start(t);
      return src;   // truthy as before; the source itself so a note scheduled ahead can be stopped (climb/hush)
    } catch { return false; }
  }
  /** The race's bank thump, for a clip that is not there. */
  function thump(level, semis = 0, at = 0) {
    try {
      const t = ctx.currentTime + at, osc = ctx.createOscillator(), g = ctx.createGain();
      osc.frequency.setValueAtTime(120 * rate(semis), t); osc.frequency.exponentialRampToValueAtTime(38, t + 0.3);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(level, t + 0.015); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.32);
      osc.connect(g); g.connect(out); osc.start(t); osc.stop(t + 0.34);
      const n = ctx.createBufferSource(), ng = ctx.createGain(); n.buffer = noise; ng.gain.value = level * 0.2;
      n.connect(ng); ng.connect(out); n.start(t); n.stop(t + 0.05);
    } catch { /* a beat never breaks the spin */ }
  }

  /** A1's tone, stopped early when a spin is skipped, disposed or suspended. */
  function stopRise() {
    if (!rising) return;
    try { rising.g.gain.cancelScheduledValues(ctx.currentTime); rising.osc.stop(); } catch { /* already done */ }
    rising = null;
  }

  /** Take back every ladder note that has not sounded yet (a skip, Back, suspend). */
  function hush() {
    for (const src of climbing) { try { src.stop(); } catch { /* already played or closed */ } }
    climbing = [];
  }

  const api = {
    /** Wake the context inside a gesture (a press or a pull). */
    arm() { if (live() && ctx.state === 'suspended') ctx.resume().catch(() => {}); },
    /** THE THUD's cue for reel `i` (0..2), rising left to right; `muted` for the last stop of a no-pay spin. */
    thud(i, muted = false) {
      const semis = muted ? -5 : (i - 1) * 2, level = muted ? LEVEL.muted : LEVEL.thud;
      note(muted ? 'thud-muted' : 'thud', semis, level);
      if (!live()) return;
      if (!play('thud', { semis, level, lowpass: muted ? 700 : 0 })) thump(level, semis);
    },
    /** THE CHIME LADDER: one family (chime1..3), `semis` from feel.ladderSemis. `sound` from feel.recipe. */
    win(sound, semis) {
      note(sound, semis, LEVEL.chime);
      if (!live()) return;
      const c = (key, s, lv, at) => play(key, { semis: semis + s, level: LEVEL.chime * lv, at });
      if (sound === 'chime') c('chime1', 0, 1, 0);
      else if (sound === 'two') { c('chime1', 0, 1, 0); c('chime2', 7, 0.8, 0.09); }
      else if (sound === 'thud') { if (!play('thud', { semis: semis - 3, level: LEVEL.thud })) thump(LEVEL.thud); c('chime2', 0, 0.9, 0.02); }
      else if (sound === 'reveal') { ['chime1', 'chime2', 'chime3'].forEach((k, i) => c(k, 0, 0.9, i * 0.08)); c('chime3', 12, 0.7, 0.3); }
    },
    /** THE CHIME LADDER climbing across THE BANK's rollup (playbook A3). `plan` is feel.ladderPlan: step 0 is
     *  the landing note win() has already played, so only the steps after it sound, each a semitone up on
     *  `base`. They are scheduled ahead, and hush() takes back whatever has not sounded yet. */
    climb(plan, base = 0) {
      hush();
      const steps = (Array.isArray(plan) ? plan : []).slice(1);
      if (!steps.length) return;
      for (const s of steps) note('climb', base + s.semis, LEVEL.chime * 0.8, s.at);
      if (!live()) return;
      for (const s of steps) {
        const src = play('chime2', { semis: base + s.semis, level: LEVEL.chime * 0.8, at: s.at / 1000 });
        if (src) climbing.push(src);
      }
    },
    /** Law VI: a skip silences the rest of the climb; it never plays a faster version of it. */
    hush,
    /** A token landing (THE BANK); the last one gets the mini-thud. */
    token(last) {
      note(last ? 'bank-thud' : 'token', last ? 5 : 12, last ? LEVEL.thud * 0.6 : LEVEL.token);
      if (!live()) return;
      if (last) { if (!play('thud', { semis: 5, level: LEVEL.thud * 0.6 })) thump(LEVEL.thud * 0.6, 5); }
      else play('chime3', { semis: 12, level: LEVEL.token });
    },
    /** A1 THE ANTICIPATION REEL: a tone climbing across reel 3's hold (a synth sweep, no new files).
     *  Quiet under Calm, and it still plays under reduced motion: the hold is pace, not animation. */
    rise(ms, calm = false) {
      const dur = Math.max(0.12, (Number(ms) || 0) / 1000), peak = (calm ? LEVEL.token : LEVEL.muted) * 0.9;
      note('rise', 0, peak);
      if (!live()) return;
      try {
        stopRise();
        const t = ctx.currentTime, osc = ctx.createOscillator(), g = ctx.createGain();
        osc.type = 'triangle';
        osc.frequency.setValueAtTime(196, t); osc.frequency.exponentialRampToValueAtTime(660, t + dur);
        g.gain.setValueAtTime(0.0001, t);
        g.gain.exponentialRampToValueAtTime(peak, t + Math.min(0.2, dur * 0.45));
        g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
        osc.connect(g); g.connect(out); osc.start(t); osc.stop(t + dur + 0.02);
        rising = { osc, g };
        osc.onended = () => { if (rising && rising.osc === osc) rising = null; };
      } catch { /* a beat never breaks the spin */ }
    },
    /** A2 THE ALMOST: a ghost note UNDER the no-pay muted thud, on the same frame (Law X, one beat). */
    almost(calm = false) {
      const level = LEVEL.muted * (calm ? 0.4 : 0.7);
      note('almost', -7, level);
      if (!live()) return;
      if (!play('chime3', { semis: -7, level, lowpass: 900 })) thump(level, -7);
    },
    suspend(on) {
      suspended = !!on;
      if (suspended) { stopRise(); hush(); }
      if (ctx && ctx.state !== 'closed') (suspended ? ctx.suspend() : ctx.resume()).catch(() => {});
    },
    dispose() { disposed = true; stopRise(); hush(); if (ctx) ctx.close().catch(() => {}); ctx = null; buffers.clear(); },
    trace,
  };
  return api;
}
