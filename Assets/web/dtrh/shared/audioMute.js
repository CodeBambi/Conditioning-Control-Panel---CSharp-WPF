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

// Transient "duck the SFX" switch (separate from the user mute): the VN tutorial
// flips this so pops/chimes/stingers fall silent while the persona speaks, while
// the ambient bed keeps playing. Not persisted.
let ducked = false;
export function isDucked() { return ducked; }
export function setDucked(v) { ducked = !!v; }

export function setMuted(v) {
  muted = !!v;
  try { localStorage.setItem(KEY, muted ? '1' : '0'); } catch (e) { /* ignore */ }
  if (muted) sweepPageMute();
  subs.forEach((fn) => { try { fn(muted); } catch (e) { /* ignore */ } });
}

export function toggleMuted() { setMuted(!muted); return muted; }

// ---- the mute is PARAMOUNT: a page-level enforcer ---------------------------
// Subscribers cover the systems that know about the switch, but any <video>/
// <audio> anywhere in the page (payload video cards, flung flash clips, future
// features that forget to subscribe) must fall silent too. While the master
// mute is ON, force-mute every media element the moment it plays or tries to
// raise its voice. One-directional by design: unmuting is each owner's job
// (they know whether their element was MEANT to be silent), so nothing that
// was born muted ever blares on unmute.
function sweepPageMute() {
  try {
    document.querySelectorAll('video, audio').forEach((el) => { el.muted = true; });
  } catch (e) { /* ignore */ }
}
function enforce(e) {
  const t = e.target;
  if (muted && t && (t.tagName === 'VIDEO' || t.tagName === 'AUDIO') && !t.muted) t.muted = true;
}
if (typeof document !== 'undefined') {
  // capture phase: media events don't bubble, but they do capture through document
  document.addEventListener('play', enforce, true);
  document.addEventListener('volumechange', enforce, true);
  if (muted) sweepPageMute();   // persisted mute applies to anything already in the DOM
}

// Subscribe to changes; returns an unsubscribe fn.
export function onMuteChange(fn) { subs.add(fn); return () => subs.delete(fn); }
