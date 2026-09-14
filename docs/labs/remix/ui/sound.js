/* ============================================================================
 * sound.js - opt-in, silent by default, synthesised.
 *
 * The page pastes into Discord and gets opened on phones in public, so nothing
 * makes a noise until someone asks for it. There are no audio files: a tick is
 * a 70 ms triangle, a thud is a 90 Hz sine. The AudioContext is built on the
 * opt-in itself, which is the gesture autoplay policy wants anyway, and the
 * preference is remembered per browser.
 *
 * Every call is a no-op when sound is off or when the browser has no
 * AudioContext, so callers never have to check.
 * ==========================================================================*/

const KEY = 'remix.sound';
const CAP = 7; // the ladder tops out after seven rolls

let on = false, ac = null;
try { on = localStorage.getItem(KEY) === '1'; } catch { /* private mode: off, and that is fine */ }

/** The note for step n of the ladder: a semitone per roll, capped. Pure. */
export const pitchFor = step => 440 * Math.pow(2, Math.min(Math.max(Number(step) || 0, 0), CAP) / 12);

export const enabled = () => on;

const Ctor = () => (typeof window === 'undefined' ? null : (window.AudioContext || window.webkitAudioContext || null));

function audio() {
  if (!on) return null;
  const C = Ctor(); if (!C) return null;
  if (!ac) { try { ac = new C(); } catch { return null; } }
  try { if (ac.state === 'suspended') ac.resume(); } catch { /* a locked context just stays quiet */ }
  return ac;
}

function tone(type, freq, ms, gain, decayMs) {
  const a = audio(); if (!a) return;
  try {
    const o = a.createOscillator(), g = a.createGain(), t = a.currentTime;
    o.type = type; o.frequency.setValueAtTime(freq, t);
    g.gain.setValueAtTime(gain, t);
    g.gain.exponentialRampToValueAtTime(0.0001, t + (decayMs || ms) / 1000);
    o.connect(g); g.connect(a.destination);
    o.start(t); o.stop(t + ms / 1000 + 0.02);
    o.onended = () => { try { o.disconnect(); g.disconnect(); } catch {} };
  } catch { /* one failed tone is never worth a broken page */ }
}

/** One rung of the ladder. */
export const tick = step => tone('triangle', pitchFor(step), 70, 0.08, 40);
/** The save. */
export const thud = () => tone('sine', 90, 110, 0.12, 110);

/** On, off, and remembered. Turning it on is the gesture that unlocks audio. */
export function setEnabled(v) {
  on = !!v;
  try { localStorage.setItem(KEY, on ? '1' : '0'); } catch { /* nothing to remember it with */ }
  if (on) audio();
}

/**
 * The ladder listens for the Roll pill's held rolls. If nothing ever emits
 * 'roll', nothing ever ticks and nothing breaks.
 */
export function listen(ctx) {
  try { ctx.on('roll', e => { if (e && e.held) tick(e.n); }); } catch {}
}
