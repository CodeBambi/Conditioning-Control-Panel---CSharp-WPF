/* ============================================================================
 * shell/exits.js - THE WAY OUT, built once.
 *
 * Law VI of the house rules says exits are sacred. This module is where that
 * stops being a slogan: every back / leave / done affordance in the Arcademy is
 * minted here, so there is exactly one place to make them louder, one place to
 * make them stick, and no chance of ten classes each inventing their own.
 *
 * THREE THINGS LIVE HERE
 *   the campus pill   the crest, shrunk. Pinned top-left of a live class, calm
 *                     on purpose, and it asks before it takes you anywhere.
 *   the sign          the casino arrow-board treatment (stationary bulbs, the
 *                     light travelling through them toward the arrow). It is
 *                     for TERMINAL screens - a report, an end card, a settings
 *                     page - never for something that fights live play.
 *   the exit bar      a sticky footer that holds the way out at the bottom of
 *                     the viewport no matter how far a screen scrolls. The
 *                     whole reason this file exists: a Back button at the top
 *                     of a long page is a Back button you cannot reach.
 *
 * WIRING LAW (trap 29 and its corollary)
 *   Nothing here handles Esc by itself. shell.js owns the ladder and adds ONE
 *   rung for the confirm dialog, at the top, where a modal belongs. The dialog
 *   is REMOVED, never toggled with the hidden attribute (trap 27), and every
 *   node it mints is a plain element - no innerHTML, no querySelector - so the
 *   headless DOM double the suites drive can build all of it.
 *
 * The copy is lexicon rows with English fallbacks, same as everywhere else; the
 * host's NeutralLexicon mirrors each key or a mod cannot re-voice it.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';

/* ----------------------------------------------------------------------------
 * THE WAY OUT MAKES A SOUND.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
/* THE PILL, AND THE ONE DIALOG THAT COSTS SOMETHING (W3 P0-37). The pill is the
 * leave-campus verb and has always sounded. Ordinary signed buttons still do
 * not: they land on a screen change and shell.js's clearScreen() already cues
 * the swap, so a second thump there is the double this wave exists to avoid.
 * The jeopardy confirm is the exception the owner's ruling carves out - it is
 * the only place in the school where a press can cost a class, and a dialog
 * that asks that question in silence is a dialog nobody reads. Three beats,
 * strictly graded: it opens on air, Stay is a tick, and Go is a full door,
 * because Go is a committed choice.
 */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

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

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/* ----------------------------------------------------------------------------
 * THE SIGN
 * The bulbs are STATIONARY and the light travels through them: `.arc-sign-lamps`
 * carries the bulb mask, its child `.arc-sign-chase` carries an oversized
 * repeating band that translates by exactly one period. That is the whole trick,
 * and it is why the chase is a real node instead of a pseudo-element - a
 * pseudo cannot own a child, and transforming the masked layer itself would drag
 * the bulbs along with the light (trap 36's law: patterns drift by TRANSFORM,
 * never by background-position).
 * -------------------------------------------------------------------------- */

/** Arrow glyphs by direction. `back` points the way you are going. */
const ARROWS = Object.freeze({ back: '◀', go: '▶', up: '▲' });

/**
 * Dress an existing button as a lit arrow sign. Idempotent: a button that is
 * already signed is left alone, so a screen that repaints cannot stack lamps.
 * @param {Object} btn                  the button element
 * @param {Object=} opts
 * @param {string=} opts.dir            'back' (default) | 'go' | 'up'
 * @param {boolean=} opts.quiet         no bulbs, glow only (a secondary exit)
 * @returns {Object} the same button, for chaining
 */
export function sign(btn, opts) {
  if (!btn || btn.__arcSigned) return btn;
  const o = opts || {};
  const dir = ARROWS[o.dir] ? o.dir : 'back';
  const label = btn.textContent == null ? '' : String(btn.textContent);

  btn.textContent = '';
  btn.className = ((btn.className ? btn.className + ' ' : '')
    + 'arc-exitsign arc-sign-' + dir + (o.quiet ? ' arc-sign-quiet' : '')).trim();

  if (!o.quiet) {
    const lamps = el('i', 'arc-sign-lamps');
    attr(lamps, 'aria-hidden', 'true');
    lamps.appendChild(el('i', 'arc-sign-chase'));
    btn.appendChild(lamps);
  }
  const arrow = el('span', 'arc-sign-arrow', ARROWS[dir]);
  attr(arrow, 'aria-hidden', 'true');
  // 'go' reads left-to-right, so its arrow follows the words instead of leading.
  if (dir !== 'go') btn.appendChild(arrow);
  btn.appendChild(el('span', 'arc-sign-label', label));
  if (dir === 'go') btn.appendChild(arrow);

  btn.__arcSigned = true;
  return btn;
}

/**
 * A brand new lit exit button.
 * @param {string} label
 * @param {Function} onClick
 * @param {Object=} opts  see sign()
 */
export function signButton(label, onClick, opts) {
  const btn = el('button', 'btn primary', label);
  btn.type = 'button';
  if (typeof onClick === 'function') btn.addEventListener('click', onClick);
  return sign(btn, opts);
}

/* ----------------------------------------------------------------------------
 * THE EXIT BAR
 * `position:sticky; bottom:0` inside whatever scrolls - the document for the
 * settings page, the report stage for the report, an overlay's own box for a
 * card. One class, one behaviour, every screen.
 * -------------------------------------------------------------------------- */

/**
 * @param {Array=} children  nodes to seat in the bar, left to right
 * @param {Object=} opts     {fixed:boolean} pin to the viewport instead of the
 *                           scroll container (for a screen with no scroller of
 *                           its own)
 * @returns {Object} the bar element
 */
export function exitBar(children, opts) {
  const bar = el('div', 'arc-exitbar' + (opts && opts.fixed ? ' arc-exitbar-fixed' : ''));
  for (const c of (Array.isArray(children) ? children : [])) if (c) bar.appendChild(c);
  return bar;
}

/* ----------------------------------------------------------------------------
 * THE CONFIRM
 * One dialog, one shape, used by the campus pill today and by anything else
 * that needs a "are you sure you want to lose this" beat tomorrow.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} o
 * @param {Object} o.mount            element to append the overlay to
 * @param {string} o.title
 * @param {string} o.body
 * @param {string} o.confirmLabel
 * @param {string} o.cancelLabel
 * @param {string=} o.note            an extra dim line under the buttons
 * @param {Function} o.onConfirm
 * @param {Function} o.onCancel
 * @returns {?Object} {root, close()} - null when there was nowhere to mount it
 */
export function createConfirm(o) {
  const s = o || {};
  if (!s.mount || typeof s.mount.appendChild !== 'function') return null;

  const root = el('div', 'arc-confirm');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');

  const card = el('div', 'arc-confirm-card');
  card.appendChild(el('h2', 'arc-h2', s.title || ''));
  if (s.body) card.appendChild(el('p', 'arc-note arc-confirm-body', s.body));

  let closed = false;
  function close() {
    if (closed) return;
    closed = true;
    try { root.remove(); } catch (e) { /* noop */ }
  }

  const actions = el('div', 'arc-confirm-actions');
  // THE SAFE ANSWER IS THE FOCUSED ANSWER. Staying costs nothing and leaving
  // costs the class, so a stray Enter must never be the one that dumps it.
  const stay = el('button', 'btn primary arc-confirm-stay', s.cancelLabel || '');
  stay.type = 'button';
  stay.addEventListener('click', () => {
    sfx('blip', 0.16);              // W3 P0-37: nothing happened, and that is the point
    close();
    try { if (s.onCancel) s.onCancel(); } catch (e) { /* noop */ }
  });
  const go = el('button', 'btn', s.confirmLabel || '');
  go.type = 'button';
  go.addEventListener('click', () => {
    sfx('door', 0.3);               // W3 P0-37: the committed choice, full door
    close();
    try { if (s.onConfirm) s.onConfirm(); } catch (e) { /* noop */ }
  });
  sign(go, { dir: 'back' });

  actions.appendChild(stay);
  actions.appendChild(go);
  card.appendChild(actions);
  if (s.note) card.appendChild(el('p', 'arc-note arc-confirm-note', s.note));

  root.appendChild(card);
  s.mount.appendChild(root);
  // W3 P0-37: the room holds its breath. Quiet, and under both answers above.
  sfx('pad', 0.18);
  focusSoon(stay);

  return { root, close, get closed() { return closed; } };
}

/* ----------------------------------------------------------------------------
 * THE CAMPUS PILL
 * The crest from the campus hub, shrunk to a chip and pinned to the top-left of
 * every live class. CALM by construction - no bulbs, no pulse, nothing that
 * competes with the board you are supposed to be watching - but always there,
 * always in the same place, and never further than one press from the way home.
 * It lives in the proctor strip, which already reserves the top ~56px of the
 * stage, so it collides with nothing a game draws.
 *
 * IT IS NOT A CLASS-ONLY CHIP ANY MORE (owner ruling 2026-08-24: "the Arcademy
 * logo button to go back to the campus must ALWAYS be visible and available").
 * The same node now seats in three places, and all three mint it from here:
 * the proctor strip (a class), the top bar's wordmark slot (the settings page),
 * and campusPillRow() below (the Records Office and the report card, both of
 * which scroll). The campus itself is the one screen that never wears it - a
 * door back to the room you are standing in is noise, and the hub already
 * carries the crest in the scene.
 *
 * WHERE IT IS, AND THE FIVE PLACES IT IS NOT
 *   class            the proctor strip, from the moment the stage mounts - and
 *                    now it STAYS there: the pause card and the host-suspend
 *                    card were z 35 inside a .arc-classroot that is not a
 *                    stacking context, so they painted over the strip's 30 and
 *                    took the pill with them. They are z 29 now (styles.css).
 *   settings         the top bar's wordmark slot.
 *   Records Office   a sticky campusPillRow at the top of the desk.
 *   report card      a sticky campusPillRow at the top of the paper.
 *   campus           NO - it is the destination.
 *   boot splash      NO - nothing has been dealt yet and there is nowhere to go.
 *   First Bell (vn/) NO - a once-ever cinematic that ends AT the campus.
 *   ceremony cards   NO - the punch-card stage, the annex reveal and the end
 *                    card are terminal beats that carry their own way out, and
 *                    the rotate gate lifts itself the moment the phone turns.
 *   leave-confirm    NO - it is the pill's own dialog, and it covers the pill
 *                    for the length of one question with the answer on it.
 *   host suspend     visible, DISABLED. The host owns that screen and its card
 *                    carries the only door the page is allowed to offer.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} o
 * @param {Function} o.onActivate  called on click/Enter (shell.js opens the confirm)
 * @param {string=} o.label        defaults to the lexicon's school name
 * @returns {Object} the pill button
 */
export function campusPill(o) {
  const s = o || {};
  const name = s.label || t('arcademy', 'The Arcademy');
  const btn = el('button', 'arc-campuspill');
  btn.type = 'button';
  btn.title = t('back_to_campus', 'Back to campus');
  attr(btn, 'aria-label', t('back_to_campus', 'Back to campus'));

  const chev = el('span', 'arc-campuspill-chev', '‹');
  attr(chev, 'aria-hidden', 'true');
  btn.appendChild(chev);

  const wrap = el('span', 'arc-campuspill-wrap');
  wrap.appendChild(el('span', 'arc-campuspill-name', name));
  wrap.appendChild(el('span', 'arc-campuspill-rule'));
  btn.appendChild(wrap);

  if (typeof s.onActivate === 'function') {
    btn.addEventListener('click', () => {
      // Reaching for the door. The confirm that opens next is the shell's; this
      // is the hand on the handle, and it sounds whether or not you go through.
      sfx('door', 0.3);
      try { s.onActivate(); } catch (e) { /* noop */ }
    });
  }
  return btn;
}

/**
 * The pill, seated in a row that sticks to the top of whatever scrolls. The
 * exit bar's twin, and it exists for the same reason: a wall of ten cards or a
 * four-class report is taller than a short window, and a way home that scrolls
 * off the top of its own page is a way home you cannot reach.
 * @param {Object} o  as campusPill
 * @returns {Object} the row element (the pill is its only child)
 */
export function campusPillRow(o) {
  const row = el('div', 'arc-pillrow');
  row.appendChild(campusPill(o));
  return row;
}

export default { sign, signButton, exitBar, createConfirm, campusPill, campusPillRow };
