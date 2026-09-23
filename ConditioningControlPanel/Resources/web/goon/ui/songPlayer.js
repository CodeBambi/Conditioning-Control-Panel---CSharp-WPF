/* ============================================================================
 * ui/songPlayer.js - Game Night: the match's song, played in step with the match.
 *
 *   createSongPlayer({ match, audio, logger, makeAudio }) -> { dispose, get playing }
 *
 * MATCH-SCOPED, the voiceService precedent: boot.js builds one in attachMatch and
 * disposes it in detachMatch, so a relay rebuild never leaves a player listening
 * to a dead match.
 *
 * THE MATCH CLOCK LEADS. The element is told where the match says the song is
 * (core/song.js songSyncAction) and re-seeked past SONG_DRIFT_MS. It never ends
 * anything: the engine's own duration timer ends Live, and leaving Live (the end,
 * Mercy, a recap, sudden death) is what stops the audio here.
 *
 * FAILURE IS SILENCE. A url that will not load, a refused play() (autoplay), a
 * host with no <audio> at all: one log line and the match runs on defaults.
 * Nothing here throws into the engine.
 *
 * THE BRIGHT LINE. The only url this file loads is `match.song.url`, which the
 * engine already held to core/song.js (the local pick) or wireSongUrl (the
 * peer's). `crossOrigin = 'anonymous'`: no cookie, no credential, ever.
 *
 * VOLUME is the page's own music x master (ui/audio.js `volumes`), re-read every
 * tick so a slider drag lands within a quarter second.
 * ==========================================================================*/

import { GoonMatchPhase } from '../core/contracts.js';
import { songSyncAction } from '../core/song.js';

export const SONG_TICK_MS = 250;

function defaultMakeAudio() {
  if (typeof Audio !== 'function') return null;
  const a = new Audio();
  a.crossOrigin = 'anonymous';
  a.preload = 'auto';
  return a;
}

export function createSongPlayer({ match, audio = null, logger = null, makeAudio = defaultMakeAudio } = {}) {
  const log = (m) => { try { logger?.info?.('[song] ' + m); } catch (_e) { /* ignore */ } };
  let el = null;
  let elUrl = '';
  let timer = 0;
  let playing = false;
  let failed = false;
  let disposed = false;
  const unsubs = [];

  function volume() {
    try {
      const v = audio && audio.volumes;
      if (!v) return 0.5;
      const n = (Number(v.music) || 0) * (Number(v.master) || 0);
      return n < 0 ? 0 : (n > 1 ? 1 : n);
    } catch (_e) { return 0.5; }
  }

  function drop() {
    stopTick();
    playing = false;
    if (!el) return;
    try { el.pause(); } catch (_e) { /* gone */ }
    try { el.removeAttribute?.('src'); el.load?.(); } catch (_e) { /* gone */ }
    el = null;
    elUrl = '';
  }

  /** Make sure an element exists for the current song (preloads in the lobby). */
  function ensure() {
    const song = match && match.song;
    const url = song && song.url ? song.url : '';
    if (!url) { drop(); failed = false; return null; }
    if (el && elUrl === url) return el;
    drop();
    failed = false;
    try { el = makeAudio(); } catch (e) { el = null; log('no audio element: ' + (e && e.message)); }
    if (!el) return null;
    elUrl = url;
    try {
      el.addEventListener?.('error', () => { failed = true; log('track would not load, playing on without it'); });
      el.src = url;
      el.load?.();
    } catch (e) { failed = true; log('load threw: ' + (e && e.message)); }
    return el;
  }

  function stopTick() { if (timer) { clearInterval(timer); timer = 0; } }

  function tick() {
    if (disposed || !el || failed) return;
    if (match.phase !== GoonMatchPhase.Live) { stop(); return; }
    let liveMs = 0;
    try { liveMs = (match.consentSheet.live_duration_sec | 0) * 1000; } catch (_e) { liveMs = 0; }
    const act = songSyncAction({
      elapsedMs: match.liveElapsedMs,
      audioSec: el.readyState > 0 ? el.currentTime : NaN,
      durationSec: Number.isFinite(el.duration) ? el.duration : 0,
      liveMs,
    });
    try { el.volume = volume(); } catch (_e) { /* read-only on some hosts */ }
    if (act && act.stop) { stop(); return; }
    if (act && typeof act.seek === 'number' && el.readyState > 0) {
      try { el.currentTime = act.seek; } catch (_e) { /* not seekable yet */ }
    }
    if (el.paused) {
      try {
        const p = el.play();
        if (p && typeof p.then === 'function') p.then(undefined, (e) => log('play refused: ' + (e && e.name)));
      } catch (e) { log('play threw: ' + (e && e.message)); }
    }
    playing = !el.paused;
  }

  function start() {
    if (!ensure() || failed) return;
    if (!timer) timer = setInterval(tick, SONG_TICK_MS);
    tick();
  }

  function stop() {
    stopTick();
    playing = false;
    if (el) { try { el.pause(); } catch (_e) { /* gone */ } }
  }

  function onPhase(phase) {
    if (disposed) return;
    if (phase === GoonMatchPhase.Live) start();
    else if (phase === GoonMatchPhase.SuddenDeath || phase === GoonMatchPhase.Recap || phase === GoonMatchPhase.Idle) drop();
    else ensure();   // pre-live: preload so the first beat lands with the first second
  }

  try { unsubs.push(match.onPhaseChanged(onPhase)); } catch (_e) { /* old match object */ }
  try { if (typeof match.onSongChanged === 'function') unsubs.push(match.onSongChanged(() => onPhase(match.phase))); }
  catch (_e) { /* old match object */ }
  onPhase(match.phase);

  return {
    get playing() { return playing; },
    get failed() { return failed; },
    /** Test seam: run one sync step now. */
    tick,
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const off of unsubs.splice(0)) { try { off && off(); } catch (_e) { /* ignore */ } }
      drop();
    },
  };
}

export default createSongPlayer;
