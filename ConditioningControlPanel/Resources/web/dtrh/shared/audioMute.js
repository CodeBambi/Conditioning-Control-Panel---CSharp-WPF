/* ============================================================================
 * audioMute.js — one master "mute all audio" switch, shared across the dive.
 *
 * A single HUD button (in bubbles.js) flips this; the bubble SFX + voices
 * (bubbles.js), the card-open barks (barks.js) and the proximity video cards
 * (sectorBuilder.js) all read it, so one control silences everything at once.
 *
 * Default: audible. The pref persists in localStorage.
 * ==========================================================================*/

const KEY = 'rh-audio-muted';
let muted = false;
try { muted = localStorage.getItem(KEY) === '1'; } catch (e) { /* ignore */ }

const subs = new Set();

export function isMuted() { return muted; }

export function setMuted(v) {
  muted = !!v;
  try { localStorage.setItem(KEY, muted ? '1' : '0'); } catch (e) { /* ignore */ }
  subs.forEach((fn) => { try { fn(muted); } catch (e) { /* ignore */ } });
}

export function toggleMuted() { setMuted(!muted); return muted; }

// Subscribe to changes; returns an unsubscribe fn.
export function onMuteChange(fn) { subs.add(fn); return () => subs.delete(fn); }
