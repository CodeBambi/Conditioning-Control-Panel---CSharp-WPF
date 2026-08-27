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
 *  6. NEVER UNDER LITE. The lite rung is a performance cap and a 140s mp3
 *     decoding on a phone is exactly what it exists to refuse. Read at call
 *     time, like the PA.
 *
 * ---------------------------------------------------------------------------
 * THE PUBLIC SURFACE
 * ---------------------------------------------------------------------------
 *   createOst({ sfx, log, lite }) -> ost
 *     sfx   (name, level, extra)  optional cue helper; omitted, the module
 *                                 dispatches the `arcademy-sfx` event itself.
 *     log   (msg)=>void           the shell's `say`.
 *     lite  bool | ()=>bool       the performance cap, read at call time.
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
 *   ost_campus      Star Byte Loop     the hub, heard most, sits back
 *   ost_deep_end    Pixel Rush         The Deep End
 *   ost_sort        Pixel Rush 2       the Sorting Room
 *   ost_records     Midnight Static    the Records Office
 *   ost_lost_found  Neon Skyline       Lost & Found
 */
export const TRACKS = Object.freeze({
  campus:         Object.freeze({ name: 'ost_campus' }),
  records:        Object.freeze({ name: 'ost_records' }),
  the_deep_end:   Object.freeze({ name: 'ost_deep_end' }),
  sort:           Object.freeze({ name: 'ost_sort' }),
  lost_and_found: Object.freeze({ name: 'ost_lost_found' }),
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

/** A cap that may be a value or a getter. Read it EVERY time; never cache. */
function readFlag(v) {
  if (typeof v === 'function') { try { return !!v(); } catch (e) { return false; } }
  return !!v;
}

export function createOst(opts) {
  const o = opts || {};
  const say = (typeof o.log === 'function') ? o.log : () => {};
  const cue = (typeof o.sfx === 'function') ? o.sfx : dispatchCue;
  let held = null;   // the track NAME in the slot, or null

  function trackFor(place) {
    const row = TRACKS[String(place == null ? '' : place)];
    return row ? row : null;
  }

  function leave() {
    if (!held) return;
    const was = held;
    held = null;
    try { cue(was, OST_LEVEL, { bus: 'music', key: OST_KEY, stop: true }); }
    catch (e) { /* the mixer's problem */ }
    say('ost: ' + was + ' out');
  }

  function enter(place) {
    const row = trackFor(place);
    if (!row) { leave(); return; }
    if (readFlag(o.lite)) { leave(); return; }             // law 6
    if (held === row.name) return;                          // law 5
    /* The slot is keyed, so a new hold on `OST_KEY` REPLACES the old one in the
     * mixer - but the old element would be dropped without its fade. Leave
     * first: two 180ms ramps, out then in, is the gesture every room already
     * makes. */
    leave();
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
