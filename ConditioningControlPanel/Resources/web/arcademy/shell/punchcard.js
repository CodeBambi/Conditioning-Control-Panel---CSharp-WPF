/* ============================================================================
 * shell/punchcard.js - THE CARD ITSELF (PUNCHCARD.md §2 / §6 / §7).
 *
 * One class, one card, ten holes. Enrollment punches two of them on the first
 * night (the tutorial punch and the one on the house); after that a graded
 * finish punches one per local day, and the tenth hole unlocks the room for
 * good. The MATH is C#'s (`ArcademyPunchCards`), the STATE is the host's
 * (`punchCards`, refused to the page - core/store.js), and this file is only
 * ever the FACE of it: given a normalized card it draws one, and it can punch a
 * hole with a thud on request.
 *
 * TWO CALLERS, ONE FACE. `shell/enrollment.js` blows the card up to full screen
 * for the stamp ceremony; `shell/records.js` pins ten of them to the wall of the
 * Records Office. Neither owns a pixel of the drawing, so a card can never look
 * like two different objects in the same school.
 *
 * ART IS SWAPPABLE AND NOT HERE YET (§7). Every graphic on the card is a CSS
 * custom property that currently resolves to `none`, and the CSS floor draws the
 * whole thing - aged panel, ring of holes, embossed crest - with gradients. When
 * the nano-banana batch lands, the ONLY edit is the token block at the top of
 * the PUNCH CARDS section in styles.css:
 *
 *   --pc-face-src     the cardstock face          art/punchcard/face.png
 *   --pc-hole-src     one punched hole, torn      art/punchcard/hole.png
 *   --pc-stamp-src    the ink stamp / impact      art/punchcard/stamp.png
 *   --pc-ribbon-src   the UNLOCKED seal           art/punchcard/ribbon.png
 *   --pc-crest-src    per class, keyed by         art/punchcard/crest-<key>.png
 *                     `.arc-pc[data-game="<key>"]`
 *
 * NO TEXT IS EVER BAKED INTO THOSE IMAGES (lexicon law): the class name, the
 * count and every line on the card are rendered live from the lexicon over the
 * top of them.
 *
 * The DOM double the suites drive builds all of this: plain elements, no
 * innerHTML, no querySelector, and every attribute write guarded.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';

/** Holes on a card. Mirrors ArcademyPunchCards.Holes and store.PUNCH_HOLES. */
export const HOLES = 10;

/** The sfx the punch rides. `shell/audio.js` synthesises it (tone + body hit). */
const THUD_CUE = 'stamp';
/** thud-1 is the tutorial punch, thud-2 the one on the house: lower, heavier. */
export const THUD_PITCH = Object.freeze({ first: 1, house: 0.78, daily: 0.92, unlock: 1.18 });

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

/**
 * Ask shell/audio.js for a thud. It is the ONE holder of an audio node in the
 * page (trap 18), so this is a request on `document`, never a sound.
 * @param {number} pitch 0.5-2; anything unusable clamps host-side to 1.
 */
export function thud(pitch) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: { name: THUD_CUE, bus: 'fx', pitch: Number(pitch) || 1 },
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

/* ----------------------------------------------------------------------------
 * THE FACE
 * -------------------------------------------------------------------------- */

/**
 * Draw one punch card.
 *
 * @param {Object} o
 * @param {string} o.gameKey            drives `data-game`, which is how a crest
 *                                      is swapped in per class
 * @param {Object} o.card               store.punchCard(gameKey) - already
 *                                      normalized, so nothing here re-derives
 * @param {string} o.name               the class's display name (lexicon)
 * @param {boolean=} o.small            the Records wall size
 * @param {number=} o.showPunches       draw THIS many holes instead of
 *                                      `card.punches` (the ceremony starts the
 *                                      card at its PRE-mint total and punches up
 *                                      to the real one - never the reverse)
 * @returns {Object} {root, punch(i, opts), setCount(n), markComplete()}
 */
export function cardFace({ gameKey, card, name, small, showPunches } = {}) {
  const c = card || {};
  const total = Math.max(0, Math.min(HOLES, Math.round(Number(c.punches) || 0)));
  const shown = showPunches == null ? total : Math.max(0, Math.min(HOLES, Math.round(showPunches)));
  /* `enrolled` is DERIVED (§2.2) and store.punchCard already carries it, but the
   * host's raw `punchcard-result` card does not - derive it here too so both
   * shapes draw the same face and no caller has to remember which one it holds. */
  const enrolled = c.enrolled === true
    || (typeof c.enrolledAt === 'string' && !!c.enrolledAt);
  const state = c.complete ? 'complete' : (enrolled ? 'open' : 'empty');

  const root = el('div', 'arc-pc' + (small ? ' arc-pc-small' : ''));
  attr(root, 'data-game', String(gameKey || ''));
  attr(root, 'data-state', state);
  attr(root, 'role', 'img');

  const face = el('div', 'arc-pc-face');
  root.appendChild(face);

  /* the crest well - an image slot today, a gradient emboss until it fills */
  const crest = el('div', 'arc-pc-crest');
  attr(crest, 'aria-hidden', 'true');
  face.appendChild(crest);

  /* the name band. Text is LIVE (never baked into the art). */
  const band = el('div', 'arc-pc-band');
  band.appendChild(el('span', 'arc-pc-name', String(name || gameKey || '')));
  band.appendChild(el('span', 'arc-pc-kind', t('punchcard', 'Punch Card')));
  face.appendChild(band);

  /* the ring of holes */
  const holes = el('div', 'arc-pc-holes');
  const nodes = [];
  for (let i = 0; i < HOLES; i++) {
    const h = el('i', 'arc-pc-hole' + (i < shown ? ' is-punched' : ''));
    try { h.style.setProperty('--i', String(i)); } catch (e) { /* noop */ }
    attr(h, 'data-i', String(i + 1));
    holes.appendChild(h);
    nodes.push(h);
  }
  face.appendChild(holes);

  const count = el('div', 'arc-pc-count', shown + ' / ' + HOLES);
  face.appendChild(count);

  const ribbon = el('div', 'arc-pc-ribbon', t('punchcard_unlocked_chip', 'Unlocked'));
  ribbon.hidden = !c.complete;
  face.appendChild(ribbon);

  attr(root, 'aria-label', String(name || gameKey || '') + ' - ' + shown + ' / ' + HOLES);

  let drawn = shown;

  return {
    root,
    /** How many holes are currently drawn (the ceremony's cursor). */
    get drawn() { return drawn; },
    /**
     * Punch hole number `n` (1-based). Idempotent per hole, and it NEVER
     * un-punches: a card only ever gains holes, in either direction of a race
     * between the ceremony's schedule and the host's authoritative card.
     * @param {Object=} opts {quiet:boolean} no pop class (reduced motion)
     */
    punch(n, opts) {
      const i = Math.round(Number(n) || 0) - 1;
      if (i < 0 || i >= HOLES) return false;
      const node = nodes[i];
      if (!node || (node.classList && node.classList.contains('is-punched'))) return false;
      if (node.classList) {
        node.classList.add('is-punched');
        if (!(opts && opts.quiet)) node.classList.add('is-fresh');
      }
      drawn = Math.max(drawn, i + 1);
      count.textContent = drawn + ' / ' + HOLES;
      attr(root, 'aria-label', String(name || gameKey || '') + ' - ' + drawn + ' / ' + HOLES);
      return true;
    },
    /** Punch every hole up to `n` at once (the reconcile path). */
    punchTo(n, opts) {
      const target = Math.max(0, Math.min(HOLES, Math.round(Number(n) || 0)));
      let any = false;
      for (let i = drawn + 1; i <= target; i++) any = this.punch(i, opts) || any;
      return any;
    },
    /** The tenth hole landed: dress the card as an unlock. */
    markComplete() {
      attr(root, 'data-state', 'complete');
      ribbon.hidden = false;
      if (root.classList) root.classList.add('is-unlocking');
    },
  };
}

/* ----------------------------------------------------------------------------
 * COPY HELPERS
 * Every user-facing string on a card is a lexicon row, so a mod can re-voice the
 * whole mechanic. Nothing here builds a key by concatenation the C# table cannot
 * enumerate: the per-class rows are `enroll_<gameKey>_1..3` and nothing else.
 * -------------------------------------------------------------------------- */

/** '3 of 10' for a progress line. */
export function holesLine(punches) {
  const n = Math.max(0, Math.min(HOLES, Math.round(Number(punches) || 0)));
  return String(t('punchcard_holes', '{have} of {need}'))
    .replace('{have}', String(n))
    .replace('{need}', String(HOLES));
}

export default { HOLES, cardFace, thud, holesLine, THUD_PITCH };
