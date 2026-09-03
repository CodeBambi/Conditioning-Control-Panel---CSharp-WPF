/* =========================================================================
 * THE NIGHT THE WALL MOVED - the Records Annex reveal cinematic.
 *
 * Fires exactly once in a save's whole life: the moment the tenth hole of the
 * LAST punch card lands (or, for a player who sealed the school before this
 * wave shipped, on their next arrival at the campus). Beat by beat:
 *
 *   cut to black -> a heavy THUD from somewhere below -> EMI, startled ->
 *   one second of the records office at night, a wall panel ajar, cold green
 *   light in the seam -> and back to the evening as if nothing happened.
 *
 * That is the WHOLE payload. Nothing unlocks visually afterwards this wave -
 * the door itself is a later phase. The cinematic never announces, never
 * badges, never explains: the player is meant to walk to the records office
 * on their own suspicion (the same pre-attentive doctrine as EMI's
 * off-channels palette break).
 *
 * LAWS THIS FILE LIVES UNDER
 *  - it is an overlay, NOT a screen: it never touches shell's router, mirrors
 *    the punch ceremony's lifecycle (module-local stage + dismiss + one Esc
 *    rung at the top, traps 48/50), and mounts at z 48 - above the ceremony
 *    (45), UNDER the EMI layer (50) so her bubble paints over the black for
 *    free (trap 59: her layer is pointer-events:none, it blocks nothing).
 *  - audio goes through the ONE door: an `arcademy-sfx` CustomEvent on
 *    `document` (trap 18). The `thud` recipe is real (shell/audio.js SOUNDS);
 *    loud = level toward 1 and pitch dropped, plus a low echo hit.
 *  - EMI speaks via `getEmi().say(...)` - protect+force, so the line cannot
 *    lose the runner to a bark. The line is a hardcoded literal like every
 *    other EMI line (emi/barks.js:9's law); it is DIEGETIC and change-hostile.
 *  - the still preloads at module load (widget.js:395's pattern) - a beat
 *    this short cannot afford a first-decode flash - and the art URL resolves
 *    module-relative (campus.js:320's nine-broken-logos bug is why).
 *  - node DOM double guards throughout (trap 60): no `Image`, maybe no
 *    `CustomEvent`, no layout - this module must import clean and no-op.
 *  - reduced motion collapses every fade to a hard cut and shortens the
 *    whole beat; the THUD and the line survive (they are the information).
 * ========================================================================= */

import { getEmi } from '../emi/index.js';

/* The still: the records office at night, the wall ajar. Resolved against THIS
 * MODULE, never the document - shell modules and the document can sit at
 * different roots (the campus logo bug). */
const ART_URL = (function resolveArt() {
  try { return new URL('../art/annex/door-reveal.png', import.meta.url).href; }
  catch (e) { return 'art/annex/door-reveal.png'; }
}());

/* PRELOAD, ONCE. Kept alive in module scope so the decode cache cannot drop
 * it between boot and the (possibly much later) beat. No `Image` in the node
 * DOM double - no preload, no problem. */
const preloaded = [];
(function preloadStill() {
  if (typeof Image !== 'function') return;
  try {
    const im = new Image();
    im.src = ART_URL;
    preloaded.push(im);
  } catch (e) { /* a missing decode costs a flash, never the beat */ }
}());

/* EMI's one line. Startled, under-explained, pointing - never narrating.
 * '0_0' is her canon wide-eyed face (story.js uses it for shocks). */
const LINE = '...that came from the records office.';
const FACE = '0_0';

/* The timeline (ms from mount). Full-motion vs reduced. EMI's bubble carries
 * ~1560ms of typing lead before the first character (widget.js SAY_LEAD_MS),
 * so the say is fired EARLY: cue at 950 -> text lands ~2500, right as the
 * still fades up - she reacts while you see what she heard. */
const T = Object.freeze({
  thud: 80,    /* the cut lands, then the impact - near-simultaneous       */
  echo: 300,   /* a lower second hit: the building settling                */
  line: 950,
  still: 2600, /* the office fades up out of the black                     */
  out: 5700,   /* "a second" of the door, then the world comes back        */
  gone: 6350,
  rThud: 80, rLine: 400, rStill: 700, rOut: 3600, rGone: 3850,
  skipOut: 240,
});

/** One cue through the one door (trap 18). Copied defensive shape from
 *  shell/punchcard.js `thud()` - a cue must never be the thing that throws. */
function cue(name, level, pitch) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: { name: name, bus: 'fx', level: level, pitch: pitch },
    }));
  } catch (e) { /* silence is an acceptable thud; a throw is not */ }
}

/**
 * Build and mount the reveal. Mirrors createPunchCeremony's contract: returns
 * `{ root, skip, destroy }`; `destroy()` clears every timer, unmounts, and
 * calls `onDone` exactly once - and is safe to call from any path (Esc rung,
 * screen change, shell destroy) in any order.
 *
 * @param {{ mount?:Element, reducedMotion?:boolean, onDone?:function }} opts
 */
export function createAnnexReveal(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || !doc.body || typeof doc.createElement !== 'function') return null;
  const reduced = !!o.reducedMotion;

  const root = doc.createElement('div');
  root.className = 'arc-annexstage' + (reduced ? ' arc-annex-reduced' : '');
  const still = doc.createElement('div');
  still.className = 'arc-annex-still';
  root.appendChild(still);

  let dead = false;
  let out = false;
  const timers = [];
  const later = function (fn, ms) { timers.push(setTimeout(fn, ms)); };

  /* While the black is up, the board underneath must not hear hotkeys (Enter
   * could start a class under the cinematic). Esc is the ONE key that passes:
   * the shell's ladder owns it (trap 29's spirit - never swallow the way out).
   * Capture phase, removed on destroy. */
  function onKey(e) {
    if (!e || e.key === 'Escape' || e.key === 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
  }
  const canListen = typeof doc.addEventListener === 'function';
  if (canListen) doc.addEventListener('keydown', onKey, true);

  function sayLine() {
    try {
      const emi = getEmi();
      if (emi && typeof emi.say === 'function') emi.say(LINE, { face: FACE });
    } catch (e) { /* a mascot may never break a cinematic */ }
  }

  function destroy() {
    if (dead) return;
    dead = true;
    for (let i = 0; i < timers.length; i += 1) clearTimeout(timers[i]);
    timers.length = 0;
    if (canListen) { try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ } }
    try { if (root.parentNode) root.parentNode.removeChild(root); } catch (e) { /* noop */ }
    try { if (typeof o.onDone === 'function') o.onDone(); } catch (e) { /* noop */ }
  }

  /** Fade the world back in. `fast` is the Esc path. */
  function leave(fast) {
    if (dead || out) return;
    out = true;
    try {
      root.classList.add('is-out');
      if (fast) root.classList.add('is-fast');
    } catch (e) { /* a double with no classList: just go */ }
    later(destroy, fast ? T.skipOut : (reduced ? 260 : 640));
  }

  function skip() { if (out) destroy(); else leave(true); }

  /* THE TIMELINE. Mount is the cut: the stage is opaque from its first paint,
   * no fade-in - a cut is the grammar of "something happened", a fade is the
   * grammar of "we are going somewhere", and we are not going anywhere. */
  if (reduced) {
    later(function () { cue('thud', 1, 0.66); }, T.rThud);
    later(sayLine, T.rLine);
    later(function () { root.classList.add('is-still'); }, T.rStill);
    later(function () { leave(false); }, T.rOut);
  } else {
    later(function () { cue('thud', 1, 0.66); }, T.thud);
    later(function () { cue('thud', 0.45, 0.5); }, T.echo);
    later(sayLine, T.line);
    later(function () { root.classList.add('is-still'); }, T.still);
    /* W3 P1-22: THE THREE SECONDS OF BLACK. From `still` to `out` the office
     * fades up out of nothing and the whole stretch played in silence, which
     * left the two thuds at the top sounding like the end of the beat rather
     * than the start of it. Three strikes of the seep's own hum, 800ms apart,
     * pitched down a step: the mixer cannot sustain a recipe, so a synth
     * "sustain" is re-struck on the caller's clock (trap 108) and is therefore
     * self-limiting - and these are `later` timers, which destroy() sweeps, so
     * a skipped reveal takes them with it. Reduced motion runs a much shorter
     * timeline and is deliberately left alone. */
    later(function () { cue('seep_hum', 0.16, 0.9); }, 2600);
    later(function () { cue('seep_hum', 0.16, 0.9); }, 3400);
    later(function () { cue('seep_hum', 0.16, 0.9); }, 4200);
    later(function () { leave(false); }, T.out);
  }

  (o.mount || doc.body).appendChild(root);
  return { root: root, skip: skip, destroy: destroy };
}

export default createAnnexReveal;
