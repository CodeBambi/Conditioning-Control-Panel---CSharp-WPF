/* ============================================================================
 * shell/ost.js - THE SOUNDTRACK. One track per place, held while you are there.
 *
 * The Arcademy got music on 2026-08-27: five Suno tracks the owner picked, one
 * for the campus and one each for the Records Office and three classrooms,
 * with more on the way. This module is the TABLE that says which track belongs
 * to which place, and the two verbs that start and stop them. It owns no Audio
 * node, no element and no timer - every track leaves through the one audio
 * door as an `arcademy-sfx` request on `document` (trap 18), exactly the way
 * the room-tone beds already do.
 *
 * ---------------------------------------------------------------------------
 * THE LAWS
 * ---------------------------------------------------------------------------
 *  1. A TRACK IS A HOLD. audio.js's HOLD contract (W3) loops a SAMPLED name in
 *     a keyed slot, fades it in over CLIP_FADE_MS and ignores the clip cap. A
 *     track is a bed with a tune in it and rides the same door, same bus
 *     (`music`), same mute, master and duck laws. The PA and every voice cue
 *     already pull `music` down (audio.js DUCK), so a line lands over the tune
 *     without a word of code here.
 *  2. EVERY HOLD HAS AN OWNER (trap 114), and this module is the owner of
 *     exactly one slot, `OST_KEY`. `enter(place)` swaps what is in the slot;
 *     `leave()` empties it. The shell calls `leave()` from clearScreen, the
 *     one funnel every screen change goes through, so a track can never
 *     outlive the place it was for - a class's own `stop_clips` cuts it too,
 *     and that is fine, because the campus enters again on its way back.
 *  3. A PLACE WITH NO TRACK IS SILENT, not "the last track". `enter()` on an
 *     unknown place is `leave()`. When the owner drops the next mp3 in
 *     `assets/sfx` the ONLY edit is a row in TRACKS below (and the same name in
 *     audio.js SAMPLES, which is the page's list of files it may ask for).
 *  4. SAMPLE-ONLY. A track with no file behind it is dropped by the mixer -
 *     there is no oscillator impression of a soundtrack, and a build without
 *     the mp3s is simply a quieter school, never a blip.
 *  5. ENTERING THE PLACE YOU ARE IN IS A NO-OP. The campus is rebuilt on every
 *     visit and a rebuild must not restart the tune from the top when nothing
 *     else changed; `enter()` compares the resolved track name, not the place.
 *     And because the shell's clearScreen() calls leave() BEFORE the next
 *     screen can enter(), leave() is deferred by one macrotask: a screen change
 *     that lands on the same track (the booth window into the counter, both on
 *     ost_prizes) keeps the tune running instead of restarting it from the top.
 *     A leave() nobody follows up still stops the track, one tick later.
 *  6. LITE DOES NOT SILENCE THE SCHOOL (was: never under lite, 2026-08-27).
 *     A track is NEVER_BUFFERED in audio.js - an element streams it, nothing
 *     is decoded into memory - so the performance rung has no cost to refuse
 *     here. And the desktop's rung is AUTOMATIC (PerformanceProfile: eight
 *     flash windows on screen is "Balanced"), so gating on it made the music
 *     vanish whenever the rest of the app was busy, with nothing in the log.
 *     The `lite` option is still accepted and ignored, so no caller breaks.
 *  7. A HOLD ASKED FOR BEFORE THE FIRST TOUCH IS KEPT. On a browser host the
 *     campus is built under the splash, before the knock, and audio.js used to
 *     drop that cue (no context yet) - the web campus was silent until the
 *     next screen. audio.js now parks a pre-gesture hold by slot and starts
 *     it from the first pointer/key (pendingHolds); this module needs no retry.
 *
 * ---------------------------------------------------------------------------
 * THE PUBLIC SURFACE
 * ---------------------------------------------------------------------------
 *   createOst({ sfx, log, lite }) -> ost
 *     sfx   (name, level, extra)  optional cue helper; omitted, the module
 *                                 dispatches the `arcademy-sfx` event itself.
 *     log   (msg)=>void           the shell's `say`.
 *     lite  bool | ()=>bool       accepted, ignored (law 6).
 *
 *   ost.enter(place)   start the track for a place ('campus', 'records', or a
 *                      class gameKey). Unknown place = leave().
 *   ost.leave()        fade the current track out. Idempotent.
 *   ost.current()      the held track name, or null.
 *   ost.trackFor(place)  table lookup, for tests and the debug panel.
 * ========================================================================== */

/** The one slot this module owns in the mixer. */
export const OST_KEY = 'ost';

/** Under the cues, over the beds. audio.js squares the level (sqrt curve) and
 *  halves a clip (CLIP_GAIN), so 0.2 lands a -16 LUFS file about 6 dB under a
 *  class cue fired at 0.4 - present, never in the way. Per-track `level`
 *  overrides this for a mix that needs it. */
export const OST_LEVEL = 0.2;

/**
 * place -> track. The name is the file: `assets/sfx/<name>.mp3`, flat beside
 * the bells (the host scans TopDirectoryOnly). Places are the shell's own
 * words: 'campus', 'records', and a class's `gameKey` as the registry spells it.
 *
 *   ost_campus          Star Byte Loop      the hub, heard most, sits back
 *   ost_deep_end        Pixel Rush          The Deep End
 *   ost_sort            Pixel Rush 2        the Sorting Room
 *   ost_records         Midnight Static     the Records Office
 *   ost_lost_found      Neon Skyline        Lost & Found
 *   -- batch two (owner: "softer tunes: the two Midnight Statics, active: the rest")
 *   ost_instant_recall  Midnight Static 2   the vigil, soft
 *   ost_anomaly         Midnight Static 3   the long search, soft
 *   ost_daily_trigger   Neon Pixel Rain     homeroom, active
 *   ost_impulse_control Neon Pixel Rain 2   the red button, active
 *   ost_prizes          Neon Jackpot 3      the Prize Counter, active
 *   ost_misdirection    Neon Jackpot        the parlour, active
 *   ost_deja_vu         Neon Jackpot 2      the card racks, active
 *   ost_annex           Corroded Pulse      the lab, slow and wrong
 * Still silent: echo, composure. The annex cams app holds its own `cam_bed`
 * room tone on the music bus in its own slot; the two layer, by design.
 */
export const TRACKS = Object.freeze({
  campus:          Object.freeze({ name: 'ost_campus' }),
  records:         Object.freeze({ name: 'ost_records' }),
  prizes:          Object.freeze({ name: 'ost_prizes' }),
  the_deep_end:    Object.freeze({ name: 'ost_deep_end' }),
  sort:            Object.freeze({ name: 'ost_sort' }),
  lost_and_found:  Object.freeze({ name: 'ost_lost_found' }),
  instant_recall:  Object.freeze({ name: 'ost_instant_recall' }),
  anomaly:         Object.freeze({ name: 'ost_anomaly' }),
  daily_trigger:   Object.freeze({ name: 'ost_daily_trigger' }),
  impulse_control: Object.freeze({ name: 'ost_impulse_control' }),
  misdirection:    Object.freeze({ name: 'ost_misdirection' }),
  deja_vu:         Object.freeze({ name: 'ost_deja_vu' }),
  annex:           Object.freeze({ name: 'ost_annex' }),
});

/** Every track name the table knows, for audio.js to register in one loop. */
export const OST_NAMES = Object.freeze(
  Object.keys(TRACKS).map((k) => TRACKS[k].name)
);

function dispatchCue(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'music' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

export function createOst(opts) {
  const o = opts || {};
  const say = (typeof o.log === 'function') ? o.log : () => {};
  const cue = (typeof o.sfx === 'function') ? o.sfx : dispatchCue;
  let held = null;   // the track NAME in the slot, or null
  let pendingStop = null;   // the deferred leave (law 5), or null

  function trackFor(place) {
    const row = TRACKS[String(place == null ? '' : place)];
    return row ? row : null;
  }

  function cancelPending() {
    if (pendingStop === null) return;
    try { clearTimeout(pendingStop); } catch (e) { /* noop */ }
    pendingStop = null;
  }

  function stopNow() {
    cancelPending();
    if (!held) return;
    const was = held;
    held = null;
    try { cue(was, OST_LEVEL, { bus: 'music', key: OST_KEY, stop: true }); }
    catch (e) { /* the mixer's problem */ }
    say('ost: ' + was + ' out');
  }

  function leave() {
    if (!held || pendingStop !== null) return;
    if (typeof setTimeout !== 'function') { stopNow(); return; }
    pendingStop = setTimeout(() => { pendingStop = null; stopNow(); }, 0);
  }

  function enter(place) {
    const row = trackFor(place);
    if (!row) { leave(); return; }
    if (held === row.name) { cancelPending(); return; }     // law 5
    /* The slot is keyed, so a new hold on `OST_KEY` REPLACES the old one in the
     * mixer - but the old element would be dropped without its fade. Stop
     * first: two 180ms ramps, out then in, is the gesture every room already
     * makes. */
    stopNow();
    held = row.name;
    const level = Number.isFinite(row.level) ? row.level : OST_LEVEL;
    try { cue(row.name, level, { bus: 'music', key: OST_KEY, hold: true }); }
    catch (e) { /* the mixer's problem */ }
    say('ost: ' + row.name + ' in (' + String(place) + ')');
  }

  return Object.freeze({
    enter,
    leave,
    current: () => held,
    trackFor: (place) => { const r = trackFor(place); return r ? r.name : null; },
  });
}
