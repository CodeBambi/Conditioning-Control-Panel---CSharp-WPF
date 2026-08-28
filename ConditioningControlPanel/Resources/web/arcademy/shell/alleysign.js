/* ============================================================================
 * shell/alleysign.js - THE TWO SIGNS IN THE ALLEY.
 *
 * The Prize Counter and the Locker are neighbours: one alley, two service
 * windows, the counter first and RM 004 one door further down (shell.js's own
 * words on the campus plan - "the counter is where a thing is bought and the
 * locker is where it is kept, so walking past the shop to reach your own door
 * is the right way round"). On the PLAN they are one tap apart. Standing INSIDE
 * either one there was no way to the other at all: the booth's only doors were
 * the window and the apron, and the Locker's only doors were its own bank and
 * the apron, so a player who bought a jacket and wanted to wear it walked out
 * to the quad, across it, and back down the same alley.
 *
 * This is the corridor sign that fixes that, and there are two of them because
 * a one-way sign is a trap: the booth's points RIGHT to the Locker, the
 * Locker's points LEFT back to the counter, and each one is the other's return
 * leg.
 *
 * IT IS THE HOUSE'S SIGN, RESKINNED - NOT A NEW ONE. `shell/exits.js` already
 * owns the casino arrow board (stationary bulbs, the light travelling through
 * them toward the arrow - trap 36's law, patterns drift by TRANSFORM), and this
 * module DRESSES a button with that exact `sign()` so the bulb mask, the chase
 * period, the arrow and every reduced-motion rule in styles.css are one
 * implementation and not two. What alleysign.css adds on top is the PLATE: the
 * campus neon vocabulary (dark navy ground, a pink tube, a pink bloom that
 * breathes) so the thing reads as hardware screwed to a painted wall rather
 * than a UI button dropped on one. Compare `.campus-neon` in styles.css - same
 * three parts, same order.
 *
 * IT IS A PROP, NOT A HOTSPOT. scene.js's `mountInView` hangs it at an authored
 * stage rect inside ONE view, which is what lets it ride the fit, the tilt and
 * the zoom exactly like the painting it is screwed to. A hotspot would have
 * been an invisible rect over a sign nobody drew; this is the sign.
 *
 * NARROW CAPS, the annex's law, same as both rooms that mount it: no store, no
 * bridge, no EMI, no wallet. It takes a label, a direction and a callback, and
 * the only thing it knows how to do besides look like a sign is ask `document`
 * for a door cue (trap 18: audio.js owns the one audio node, everybody else
 * REQUESTS).
 *
 * THE RECTS ARE MEASUREMENTS OF PAINTINGS, both of them, and neither may be
 * rounded for looks:
 *   the booth   vn-18's right-hand wall is flat ochre from the shelf jamb
 *               (x 1030) to the plate edge, and unbroken from the ceiling down
 *               to the skirting at y ~575. The sign hangs at the WINDOW's own
 *               eye line (the lit mouth is y 172..398, centre 285) so the two
 *               read as one row of things at head height, and it clears the
 *               painted PRIZE COUNTER marquee above it (y 34..134) by a hundred
 *               pixels.
 *   the Locker  vn-21's left wall is spoken for twice over - the clock at
 *               y 110..195 and the corkboard at y 222..425 - so the sign takes
 *               the empty band underneath, above the skirting. Lower than the
 *               booth's on purpose: it is where the wall is, and a sign floated
 *               into the clock to match a sign in another room would be a sign
 *               nailed to a painting instead of into it.
 * Both clear the apron line (y 640) on their own, which is the floor scene.js
 * warns about and this file never crosses.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { sign as dressSign } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 * -------------------------------------------------------------------------- */

/** vn-18's right-hand wall, at the service window's eye line. */
export const BOOTH_SIGN_RECT = Object.freeze([1072, 232, 272, 112]);

/** vn-21's left wall, in the band between the corkboard and the skirting. */
export const LOCKER_SIGN_RECT = Object.freeze([26, 430, 206, 100]);

/* ----------------------------------------------------------------------------
 * PLUMBING - prizebooth.js's four helpers, for prizebooth.js's four reasons.
 * -------------------------------------------------------------------------- */

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function attr(node, name, value) {
  try { if (node && typeof node.setAttribute === 'function') node.setAttribute(name, value); }
  catch (e) { /* the DOM double may not carry attributes - never fatal */ }
}

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** alleysign.css, linked once and lazily - recordsroom.js's pattern, and the
 *  reason it is here rather than in either room's sheet: two rooms mount this
 *  and a skin that lived in one of them would be a skin the other is missing. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('ally-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'ally-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./alleysign.css', 'shell/alleysign.css');
    const host = doc.head || doc.documentElement || doc.body;
    if (host && typeof host.appendChild === 'function') host.appendChild(link);
    return link;
  } catch (e) { if (log) log('alley sign sheet failed to link'); return null; }
}

/** THE WAY THROUGH MAKES A SOUND, and it is the school's own door - exits.js's
 *  shape exactly (trap 18: a REQUEST on `document`, never an audio node, and a
 *  dropped cue is not an error). Nothing new is minted here: `door` is what the
 *  campus pill, the Locker's own arrival and every committed leave already
 *  play, because walking from the counter to RM 004 is walking through a door. */
function sfx(name, level) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/* ----------------------------------------------------------------------------
 * THE SIGN
 * -------------------------------------------------------------------------- */

/**
 * alleySign(o) -> the button, or null with no DOM.
 *
 * @param {Object} o
 *  variant  - 'booth' (points RIGHT at the Locker) | 'locker' (points LEFT
 *             back at the counter). It picks the direction, the lexicon rows
 *             and the plate's measure; nothing else in here branches on it.
 *  t        - the room's own `t`, so a mod re-voices the sign with the room.
 *  onGo     - the press. The ROOM decides what the other end of the alley is;
 *             this file has never known there is a shell.
 *  log      - optional
 * @returns {?Object} the button element
 */
export function alleySign(o) {
  const s = o || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof s.t === 'function' ? s.t : lexT;
  const log = typeof s.log === 'function' ? s.log : function () {};
  const toLocker = s.variant !== 'locker';

  ensureSheet(doc, log);

  /* TRAP 6 OF THE WAVE CONTRACT: `t(key, fallback)` answers the FALLBACK before
   * it ever reaches DEFAULT_LEXICON, so a fallback passed here would mask the
   * row this bundle actually ships. Both rows are in core/lexicon.js and both
   * are mirrored in the host's NeutralLexicon; `t` is called bare. */
  const label = toLocker ? t('alley_sign_locker') : t('alley_sign_counter');
  const aria = toLocker ? t('alley_sign_locker_aria') : t('alley_sign_counter_aria');

  const btn = el('button', 'ally-sign ally-sign-' + (toLocker ? 'booth' : 'locker'), label);
  btn.type = 'button';
  attr(btn, 'aria-label', aria);
  attr(btn, 'title', aria);

  /* THE HOUSE'S ARROW BOARD, borrowed whole. `sign()` empties the button, hangs
   * the bulb mask and its chase child, and seats the arrow AFTER the words for
   * 'go' and BEFORE them for 'back' - which is the whole of why the two signs
   * are one call with one flag rather than two builders. */
  try { dressSign(btn, { dir: toLocker ? 'go' : 'back' }); }
  catch (e) { log('alley sign would not dress: ' + ((e && e.message) || e)); }

  btn.addEventListener('click', function () {
    /* The cue first and the move second, in that order and never the other way:
     * the room this press opens tears this node down, and a cue asked for by a
     * detached button is a cue nobody hears. */
    sfx('door', 0.26);
    try { if (s.onGo) s.onGo(); } catch (e) { log('alley sign press threw: ' + ((e && e.message) || e)); }
  });

  return btn;
}

export default alleySign;
