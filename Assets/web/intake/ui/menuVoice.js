/**
 * MENU VOICE — the spoken welcome on the main menu.
 *
 * One clip: assets/vo/menu_welcome.mp3, the greeter reading the same words the
 * player is looking at, in the intake voice. Rendered by the shipped VO
 * pipeline and loudness-matched to the corpus (-22.4 LUFS), so it sits at the
 * same level as every question line.
 *
 * Plain <audio>, like ui/menuMusic.js and for the same reasons: this is shell,
 * not run. render/audio.js's graph is built lazily on a gesture and is the
 * descent's instrument — routing a menu greeting through it would mean the
 * greeting is gated on the run's AudioContext existing yet, which on a cold
 * title screen it does not.
 *
 * It obeys the SAME voiceover prefs as the run's VO (voEnabled + volMaster *
 * volVoice, via voiceScale). Someone who turned the voiceover off did so to
 * stop being spoken to, and the menu is not an exception to that.
 */

import { voiceScale } from './prefs.js';
import { duckMusic } from './menuMusic.js';
import { audioUrl, altSrcFor } from '../core/audioSrc.js';

const SRC = '../assets/vo/menu_welcome.mp3';

/** Beat before speaking, so the line doesn't collide with the panel's pop-in
 *  and the music's fade-up. Long enough to read the title first. */
const LEAD_IN_MS = 700;

let voice = null;
let leadTimer = 0;
/** An other-host retry is in flight — the original clip's play() rejection must
 *  not tear the element down underneath it (both fire for the same 404). */
let swapping = false;

/** Resolved against THIS module, then pointed at whichever host actually has the
 *  clip (core/audioSrc.js — the VO ships as a downloaded content pack now). */
function srcUrl() {
  try { return audioUrl(new URL(SRC, import.meta.url).href); }
  catch (_e) { return './assets/vo/menu_welcome.mp3'; }
}

/** The clip 404'd: try the other audio host ONCE, then let it go (a missing
 *  greeting has always been a silent no-op). altSrcFor spends the single retry,
 *  so this can never bounce between hosts. */
function onVoiceError() {
  if (swapping) return;   // 'error' + the play() rejection both fire: one retry, not two
  const el = voice;
  const alt = el ? altSrcFor(el) : null;
  if (!alt) { release(); return; }
  swapping = true;
  try {
    el.src = alt;
    const p = el.play();
    if (p && typeof p.then === 'function') {
      p.then(() => { swapping = false; duckMusic(true); },
             () => { swapping = false; release(); });
    } else { swapping = false; }
  } catch (_e) { swapping = false; release(); }
}

/**
 * Speak the welcome once. Safe to call when the file is missing, when audio is
 * blocked, or when the voiceover pref is off — all three are silent no-ops.
 * Returns a stop() function; the menu calls it on teardown so the greeting can
 * never outlive the screen it belongs to.
 */
export function playWelcome() {
  stopWelcome();
  if (voiceScale() <= 0) return stopWelcome;   // voiceover off: say nothing

  leadTimer = setTimeout(() => {
    leadTimer = 0;
    try {
      voice = new Audio(srcUrl());
      // The clip is already corpus-normalized, so the pref IS the whole gain —
      // no extra trim here (contrast menuMusic's TRACK_TRIM, which exists only
      // because that mp3 was mastered outside our pipeline).
      voice.volume = Math.min(1, Math.max(0, voiceScale()));
      voice.addEventListener('ended', release);
      voice.addEventListener('error', onVoiceError);
      const p = voice.play();
      if (p && typeof p.catch === 'function') {
        // Autoplay refused (no gesture yet). Drop it rather than arming a
        // retry: a greeting that fires minutes later, after the player has
        // already clicked into Options, is worse than no greeting. A 404 lands
        // here too, but the 'error' listener above owns the one host retry, so
        // don't tear the element down while that retry is in flight.
        p.catch(() => { if (!swapping) release(); });
      } else {
        duckMusic(true);
      }
      if (p && typeof p.then === 'function') p.then(() => duckMusic(true), () => {});
    } catch (_e) { release(); }
  }, LEAD_IN_MS);

  return stopWelcome;
}

function release() {
  swapping = false;   // whatever ends the greeting also abandons a pending retry
  duckMusic(false);
  if (voice) {
    try { voice.pause(); } catch (_e) {}
    try { voice.src = ''; } catch (_e) {}
    voice = null;
  }
}

/** Stop the greeting and lift the music duck. Idempotent. */
export function stopWelcome() {
  if (leadTimer) { clearTimeout(leadTimer); leadTimer = 0; }
  release();
}
