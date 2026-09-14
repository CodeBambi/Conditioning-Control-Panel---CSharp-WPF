/* sound.js - the wheel's cues. No new files: the chime family and the thud are the race's own clips
 * (dtrh/assets/bubbles/sfx), looked up through dtrh/shared/audioSrc.js, the same stable paths the slot's cues
 * use. A clip that has not decoded yet falls back to a synth thump, so a beat is never silent (Brake 6).
 * One AudioContext per open(), created on the first gesture, closed in dispose(). */

import { audioUrl, altAudioUrl } from '../../../dtrh/shared/audioSrc.js';

const FILES = { chime1: 'chime1.mp3', chime2: 'chime2.mp3', chime3: 'chime3.mp3', thud: 'thud.mp3' };
const LEVEL = { thud: 0.5, muted: 0.22, chime: 0.34, tick: 0.1, token: 0.12 };
const rate = s => 2 ** (s / 12);

export function createSound() {
  let ctx = null, out = null, suspended = false, disposed = false;
  const buffers = new Map(), trace = [];

  function graph() {
    if (ctx || disposed) return ctx;
    const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AC) return null;
    try { ctx = new AC(); out = ctx.createGain(); out.gain.value = 0.9; out.connect(ctx.destination); } catch { ctx = null; return null; }
    for (const [key, file] of Object.entries(FILES)) {
      const first = audioUrl(new URL(`../../../dtrh/assets/bubbles/sfx/${file}`, import.meta.url).href);
      const grab = url => fetch(url).then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.arrayBuffer(); })
        .then(raw => new Promise((res, rej) => ctx.decodeAudioData(raw, res, rej)));
      grab(first).catch(() => { const alt = altAudioUrl(first); if (!alt) throw new Error('no alt'); return grab(alt); })
        .then(b => buffers.set(key, b)).catch(() => console.warn(`[wheel] cue ${file} unavailable, synth stands in`));
    }
    return ctx;
  }
  const live = () => !disposed && !suspended && graph() && ctx.state !== 'closed';
  const note = (name, semis, level) => { trace.push({ name, semis, level, at: Math.round(performance.now()) }); if (trace.length > 80) trace.shift(); };

  function play(key, { semis = 0, level = 0.3, at = 0, lowpass = 0 } = {}) {
    const buf = buffers.get(key);
    if (!buf) return false;
    try {
      const t = ctx.currentTime + at, src = ctx.createBufferSource(), g = ctx.createGain();
      src.buffer = buf; src.playbackRate.value = rate(semis); g.gain.value = level;
      let head = src;
      if (lowpass) { const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = lowpass; head.connect(f); head = f; }
      head.connect(g); g.connect(out); src.start(t);
      return true;
    } catch { return false; }
  }
  /** A pitched sine drop: the thump for a missing clip, and the yawn (slow, low, falling). */
  function sweep(level, fromHz, toHz, ms, at = 0) {
    try {
      const t = ctx.currentTime + at, osc = ctx.createOscillator(), g = ctx.createGain();
      osc.frequency.setValueAtTime(fromHz, t); osc.frequency.exponentialRampToValueAtTime(toHz, t + ms / 1000);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(level, t + Math.min(0.12, ms / 4000));
      g.gain.exponentialRampToValueAtTime(0.0001, t + ms / 1000);
      osc.connect(g); g.connect(out); osc.start(t); osc.stop(t + ms / 1000 + 0.02);
    } catch { /* a beat never breaks the wheel */ }
  }

  return {
    trace,
    /** Wake the context inside a gesture (a press or a drag). */
    arm() { if (live() && ctx.state === 'suspended') ctx.resume().catch(() => {}); },
    /** THE CHIME LADDER on a peg crossing: one quiet chime, `semis` from feel.tick. */
    tick(semis) { note('tick', semis, LEVEL.tick); if (live()) play('chime3', { semis: semis - 5, level: LEVEL.tick }); },
    /** THE THUD when the pointer settles; `muted` for Snooze (Brake 6: a muted thud, never silence). */
    thud(muted = false) {
      const level = muted ? LEVEL.muted : LEVEL.thud;
      note(muted ? 'thud-muted' : 'thud', muted ? -5 : 0, level);
      if (live() && !play('thud', { semis: muted ? -5 : 0, level, lowpass: muted ? 700 : 0 })) sweep(level, 120, 38, 320);
    },
    /** The landing's party cue from feel.recipe: chime | two | thud | reveal | snooze. */
    win(sound) {
      note(sound, 0, LEVEL.chime);
      if (!live()) return;
      const c = (key, s, lv, at) => play(key, { semis: s, level: LEVEL.chime * lv, at });
      if (sound === 'chime') c('chime1', 0, 1, 0.05);
      else if (sound === 'two') { c('chime1', 0, 1, 0.05); c('chime2', 7, 0.8, 0.14); }
      else if (sound === 'thud') { c('chime2', 0, 0.9, 0.06); c('chime3', 7, 0.8, 0.16); }
      else if (sound === 'reveal') { ['chime1', 'chime2', 'chime3'].forEach((k, i) => c(k, 0, 0.9, 0.05 + i * 0.08)); c('chime3', 12, 0.7, 0.35); }
      else if (sound === 'snooze') sweep(0.08, 330, 150, 900, 0.18);   // EMI's yawn
    },
    /** A token landing (THE BANK); the last one gets the mini-thud. */
    token(last) {
      note(last ? 'bank-thud' : 'token', last ? 5 : 12, last ? LEVEL.thud * 0.6 : LEVEL.token);
      if (!live()) return;
      if (last) { if (!play('thud', { semis: 5, level: LEVEL.thud * 0.6 })) sweep(LEVEL.thud * 0.6, 160, 50, 300); }
      else play('chime3', { semis: 12, level: LEVEL.token });
    },
    suspend(on) {
      suspended = !!on;
      if (ctx && ctx.state !== 'closed') (suspended ? ctx.suspend() : ctx.resume()).catch(() => {});
    },
    dispose() { disposed = true; if (ctx) ctx.close().catch(() => {}); ctx = null; buffers.clear(); },
  };
}
