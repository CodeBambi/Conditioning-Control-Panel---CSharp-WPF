/* ============================================================================
 * shell/orientgate.js - THE TURN-YOUR-PHONE CARD, built once.
 *
 * Same reasoning as exits.js: there are two places that need to ask a player to
 * rotate (the campus, which is a 16:9 floor plan, and a class whose board wants
 * a shape the phone is not currently holding), and two places is exactly how you
 * end up with two different cards saying two different things. So there is one
 * card, one mount, one listener, and callers only ever say what they WANT:
 *
 *     requireOrientation('landscape', { reason: 'campus' })
 *     requireOrientation(null)                       // stand down
 *
 * The card is a piece of campus signage, not a browser error: it wears the
 * school's own faces and colours and it never blames the player for holding a
 * phone the way phones are held.
 *
 * FOUR LAWS
 *   1. PHONES ONLY. `core/device.js` is the single decision, and it refuses a
 *      desktop window outright, so nothing in here can ever appear over the
 *      WebView2 build no matter what a caller asks for.
 *   2. IT LIFTS ITSELF. There is no dismiss button and there is no reload: the
 *      module subscribes to `onDeviceChange` and the card comes off the frame
 *      the phone comes round, in both directions, for as long as the requirement
 *      stands.
 *   3. ONE CARD, EVER. `requireOrientation` is idempotent and the node is
 *      REMOVED rather than hidden (trap 27), so a screen that repaints while the
 *      card is up cannot stack two of them.
 *   4. IT IS DOM AND CSS AND NOTHING ELSE. No canvas, no timer, no rAF loop.
 *      The one animation is a CSS keyframe that the reduced-motion rules at the
 *      bottom of styles.css already freeze along with everything else.
 *
 * THE CLOCK IS THE CALLER'S PROBLEM. This module blocks the screen and says so;
 * it does not know what a class is. shell.js pauses the class around the card,
 * which is why the class copy can honestly say nothing is running.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { isMobile, orientation, orientationOk, onDeviceChange } from '../core/device.js';

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

/* ----------------------------------------------------------------------------
 * THE WORDS
 * Front-desk voice: warm, plain, and specific about the room rather than about
 * the device. Lexicon rows with English fallbacks, same as every other string
 * the shell renders, so a mod can re-voice all six.
 * -------------------------------------------------------------------------- */
function copyFor(want, reason) {
  if (want === 'portrait') {
    return {
      title: t('rotate_portrait_title', 'Stand it back up'),
      body: t('rotate_portrait_body',
        'This one plays tall, so turn your phone upright and the board gets its'
        + ' full height back. Nothing is running while you sort it out.'),
    };
  }
  if (reason === 'campus') {
    return {
      title: t('rotate_campus_title', 'Turn it sideways'),
      body: t('rotate_campus_body',
        'The floor plan runs wide, the way the cabinets actually sit along the'
        + ' walls, so give your phone a quarter turn and you get the whole place'
        + ' back on the glass with your spot still held.'),
    };
  }
  return {
    title: t('rotate_landscape_title', 'Turn it sideways'),
    body: t('rotate_landscape_body',
      'This room was built wide, so give your phone a quarter turn and the board'
      + ' gets the width it was drawn for. Nothing is running while you sort it out.'),
  };
}

/* ----------------------------------------------------------------------------
 * THE STATE
 * Module-level on purpose: there is one viewport and one card, and handing every
 * caller its own instance would be handing them a way to stack two.
 * -------------------------------------------------------------------------- */

let wanted = null;        // 'landscape' | 'portrait' | null
let wantedReason = '';    // 'campus' | 'class' | ''
let node = null;          // the mounted card, or null
let unsub = null;         // onDeviceChange unsubscriber
let onChange = null;      // caller hook: (blocking:boolean) => void

/** Build the card. Called only when one is actually going up. */
function build(want, reason) {
  const words = copyFor(want, reason);

  const root = el('div', 'arc-orientgate arc-orientgate-' + want);
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-modal', 'true');
  root.setAttribute('aria-live', 'assertive');

  const card = el('div', 'arc-orientgate-card');

  /* The glyph: a phone that tips a quarter turn and holds, drawn with two divs
   * rather than an SVG so it inherits the sheet's colours for free. */
  const art = el('div', 'arc-orientgate-art');
  art.setAttribute('aria-hidden', 'true');
  const phone = el('i', 'arc-orientgate-phone');
  phone.appendChild(el('i', 'arc-orientgate-screen'));
  art.appendChild(phone);
  const arc = el('i', 'arc-orientgate-arc');
  arc.setAttribute('aria-hidden', 'true');
  art.appendChild(arc);
  card.appendChild(art);

  card.appendChild(el('p', 'arc-kicker', t('arcademy', 'The Arcademy')));
  card.appendChild(el('h2', 'arc-h2 arc-orientgate-title', words.title));
  card.appendChild(el('p', 'arc-note arc-orientgate-body', words.body));

  root.appendChild(card);
  return root;
}

/** Put the card up, take it down, or leave it exactly as it is. */
function reconcile() {
  const blocking = !!wanted && isMobile() && !orientationOk(wanted);

  if (blocking && !node) {
    try {
      node = build(wanted, wantedReason);
      document.body.appendChild(node);
    } catch (e) { node = null; }
  } else if (!blocking && node) {
    try { node.remove(); } catch (e) { /* noop */ }
    node = null;
  } else if (blocking && node) {
    // Still blocking, but the wanted side may have changed under us (a class
    // opened over the campus). Rebuild rather than patch: it is one card.
    const cls = 'arc-orientgate-' + wanted;
    try {
      if (!node.classList.contains(cls)) {
        node.remove();
        node = build(wanted, wantedReason);
        document.body.appendChild(node);
      }
    } catch (e) { /* noop */ }
  }

  try {
    const html = document.documentElement;
    if (html && html.classList) html.classList.toggle('arc-orient-blocked', !!node);
  } catch (e) { /* noop */ }

  return !!node;
}

let lastBlocking = false;
function reconcileAndNotify() {
  const blocking = reconcile();
  if (blocking === lastBlocking) return blocking;
  lastBlocking = blocking;
  try { if (onChange) onChange(blocking); } catch (e) { /* noop */ }
  return blocking;
}

/**
 * Say what the screen currently needs. Pass null (or 'any') to stand down.
 * @param {?string} want  'landscape' | 'portrait' | 'any' | null
 * @param {Object=} o
 * @param {string=} o.reason  'campus' | 'class' - picks which copy the card wears
 * @param {Function=} o.onChange  (blocking:boolean) => void, fired only on a flip
 * @returns {boolean} whether a card is up right now
 */
export function requireOrientation(want, o) {
  const opts = o || {};
  wanted = (want === 'landscape' || want === 'portrait') ? want : null;
  wantedReason = String(opts.reason || '');
  if (typeof opts.onChange === 'function') onChange = opts.onChange;
  else if (opts.onChange === null) onChange = null;

  if (wanted && !unsub) unsub = onDeviceChange(() => reconcileAndNotify());
  if (!wanted && unsub) { try { unsub(); } catch (e) { /* noop */ } unsub = null; }

  return reconcileAndNotify();
}

/** Is a card up right now? Cheap; the shell asks before it resumes a class. */
export function isBlocking() { return !!node; }

/** Drop everything: no requirement, no card, no listener, no hook. */
export function clearOrientation() {
  onChange = null;
  return requireOrientation(null);
}

/** Test seam - never read by the shell. */
export function diagnostics() {
  return { wanted, reason: wantedReason, up: !!node, mobile: isMobile(), orientation: orientation() };
}

export default { requireOrientation, clearOrientation, isBlocking, diagnostics };
