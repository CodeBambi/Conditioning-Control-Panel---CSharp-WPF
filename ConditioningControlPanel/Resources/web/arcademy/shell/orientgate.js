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
 *      CODA (2026-08-31, support): a CLASS card also offers a way in and a way
 *      out, but only after a grace period long enough that a phone which CAN
 *      turn has already turned. Some phones can never satisfy the requirement -
 *      an iOS system portrait lock has no in-page override, and the Discord
 *      Activity iframe hands the page whatever shape it likes - and a card that
 *      cannot be satisfied and cannot be dismissed is a locked door. So the
 *      class card grows two buttons: play it upright anyway (the requirement is
 *      waived and the caller is told the block lifted, exactly as if the phone
 *      had turned), or leave the class (the caller's own door, via `onLeave`).
 *      The campus card is untouched - the campus is not a room you can be stuck
 *      inside, and its way out is the phone.
 *      SECOND CODA (2026-09-04, the iOS store build): some hosts KNOW the
 *      window can never turn, and say so in `init.platform.orientationLocked`.
 *      Waiting out a grace period to offer a way in is then just seven seconds
 *      of asking a player to do something impossible, so a locked viewport gets
 *      the class card AT ONCE, worded as a notice rather than as an instruction
 *      ("this one plays wide, here is the way in") with both buttons already on
 *      screen. The campus card is not built at all on a locked viewport: it has
 *      no buttons by Law 2, so a card that could never lift would be the locked
 *      door this whole coda exists to prevent.
 *   3. ONE CARD, EVER. `requireOrientation` is idempotent and the node is
 *      REMOVED rather than hidden (trap 27), so a screen that repaints while the
 *      card is up cannot stack two of them.
 *   4. IT IS DOM AND CSS AND NOTHING ELSE. No canvas, no timer, no rAF loop.
 *      That holds for the grace period on Law 2's coda as well: the actions are
 *      in the DOM from the first paint and a delayed CSS keyframe reveals them,
 *      so there is no setTimeout to leak when the card is removed under it.
 *      Both keyframes are frozen by the reduced-motion rules at the bottom of
 *      styles.css - which is why the actions carry their OWN reduced-motion
 *      rule showing them at once, since a frozen reveal that left them at
 *      opacity 0 would hide the only way out from the people most likely to
 *      need it.
 *
 * THE CLOCK IS THE CALLER'S PROBLEM. This module blocks the screen and says so;
 * it does not know what a class is. shell.js pauses the class around the card,
 * which is why the class copy can honestly say nothing is running.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import {
  isMobile, orientation, orientationOk, onDeviceChange, viewportCanRotate,
} from '../core/device.js';

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
function copyFor(want, reason, locked) {
  /* A LOCKED VIEWPORT IS TOLD, NOT ASKED. Same card, same buttons, different
   * sentence: nothing here says "turn your phone", because on this host that is
   * an instruction the player cannot carry out and every second they spend
   * trying is a second we wasted for them. */
  if (locked) {
    return {
      title: t('rotate_locked_title', 'This one plays wide'),
      body: t('rotate_locked_body',
        'This room was drawn for a wide screen and this app stays upright, so the'
        + ' board comes in a little tighter than it was built for. Everything works.'
        + ' Nothing is running while you decide.'),
    };
  }
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
let onLeave = null;       // caller hook: () => void, the caller's own door

/**
 * Stand the requirement down from INSIDE the module (Law 2's coda). Same shape
 * as `requireOrientation(null)` - the want is dropped and the listener with it -
 * except that it deliberately leaves `onChange` and `onLeave` alone: the caller
 * has not gone anywhere, and `onChange(false)` is the signal it is waiting on to
 * start the class again. Re-entering the room later re-arms the gate, which is
 * correct: the waiver was for this sitting.
 */
function standDown() {
  wanted = null;
  wantedReason = '';
  if (unsub) { try { unsub(); } catch (e) { /* noop */ } unsub = null; }
  return reconcileAndNotify();
}

/** Build the card. Called only when one is actually going up. */
function build(want, reason, locked) {
  const words = copyFor(want, reason, locked);

  const root = el('div', 'arc-orientgate arc-orientgate-' + want
    + (locked ? ' arc-orientgate-locked' : ''));
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

  /* THE WAY IN AND THE WAY OUT (Law 2's coda). A class only, and landscape
   * only: portrait is the shape a phone already holds, and the campus is not a
   * room you can be trapped inside. Mounted from the first paint and revealed
   * by a delayed CSS keyframe (Law 4), so a phone that simply had not turned
   * yet never sees them - and `visibility` rides the same keyframe so they are
   * out of the tab order until they are on screen. */
  if (want === 'landscape' && reason === 'class') {
    /* `arc-orientgate-now` skips the grace-period keyframe (second coda): the
     * wait exists to let a phone that CAN turn turn first, and this one cannot. */
    const acts = el('div', 'arc-orientgate-actions'
      + (locked ? ' arc-orientgate-now' : ''));
    acts.appendChild(el('p', 'arc-note arc-orientgate-stuck',
      locked
        ? t('rotate_locked_note', 'Pick a way in below.')
        : t('rotate_stuck_note',
          'Phone not turning? Some are told to hold still. Pick a way in below.')));

    const row = el('div', 'arc-orientgate-actionrow');

    const waive = el('button', 'btn primary arc-orientgate-waive',
      t('rotate_play_anyway', 'Play it upright anyway'));
    waive.type = 'button';
    waive.addEventListener('click', () => { standDown(); });
    row.appendChild(waive);

    const leave = el('button', 'btn ghost arc-orientgate-leave',
      t('rotate_leave_class', 'Leave the class'));
    leave.type = 'button';
    leave.addEventListener('click', () => {
      /* The caller's door first, the card second. The shell's leave routine
       * freezes the class around its own question, and `orientFreeze` refuses
       * to resume a class that is already frozen for a reason of its own - so
       * standing the gate down after it cannot start a game up underneath the
       * card that asks whether to bin it. */
      try { if (onLeave) onLeave(); } catch (e) { /* noop */ }
      standDown();
    });
    row.appendChild(leave);

    acts.appendChild(row);
    card.appendChild(acts);
  }

  root.appendChild(card);
  return root;
}

/** Put the card up, take it down, or leave it exactly as it is. */
function reconcile() {
  /* A HOST THAT SAYS THE WINDOW CANNOT TURN (second coda). The campus asks for
   * width with a card that has no buttons, so on a locked viewport it is not
   * built at all and the campus simply runs upright - which it has done since
   * the floor plan stopped requiring landscape. A class still gets its card,
   * because the card is now the notice and the two buttons are the point. */
  const locked = !viewportCanRotate();
  const blocking = !!wanted && isMobile() && !orientationOk(wanted)
    && (!locked || wantedReason === 'class');

  if (blocking && !node) {
    try {
      node = build(wanted, wantedReason, locked);
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
        node = build(wanted, wantedReason, locked);
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
 * @param {Function=} o.onLeave  () => void, the caller's OWN door. Optional, and
 *   only a `reason: 'class'` card ever offers it: after the grace period the
 *   card grows a "leave" button that calls this and then stands the gate down.
 *   The module still knows nothing about what a class is - it just knocks.
 * @returns {boolean} whether a card is up right now
 */
export function requireOrientation(want, o) {
  const opts = o || {};
  wanted = (want === 'landscape' || want === 'portrait') ? want : null;
  wantedReason = String(opts.reason || '');
  if (typeof opts.onChange === 'function') onChange = opts.onChange;
  else if (opts.onChange === null) onChange = null;
  if (typeof opts.onLeave === 'function') onLeave = opts.onLeave;
  else if (opts.onLeave === null) onLeave = null;

  if (wanted && !unsub) unsub = onDeviceChange(() => reconcileAndNotify());
  if (!wanted && unsub) { try { unsub(); } catch (e) { /* noop */ } unsub = null; }

  return reconcileAndNotify();
}

/** Is a card up right now? Cheap; the shell asks before it resumes a class. */
export function isBlocking() { return !!node; }

/** Drop everything: no requirement, no card, no listener, no hooks. */
export function clearOrientation() {
  onChange = null;
  onLeave = null;
  return requireOrientation(null);
}

/** Test seam - never read by the shell. */
export function diagnostics() {
  return {
    wanted, reason: wantedReason, up: !!node, mobile: isMobile(),
    orientation: orientation(), canRotate: viewportCanRotate(),
  };
}

export default { requireOrientation, clearOrientation, isBlocking, diagnostics };
