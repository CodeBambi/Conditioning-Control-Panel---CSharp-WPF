/* ============================================================================
 * shell/punchcard.js - THE CARD ITSELF (PUNCHCARD.md §2 / §6 / §7).
 *
 * One class, one card, ten stamps. Enrollment lands two of them on the first
 * night (the tutorial punch and the one on the house); after that a graded
 * finish punches one per local day, and the tenth hole unlocks the room for
 * good. The MATH is C#'s (`ArcademyPunchCards`), the STATE is the host's
 * (`punchCards`, refused to the page - core/store.js), and this file is only
 * ever the FACE of it: given a normalized card it draws one, and it can punch a
 * stamp with a thud on request.
 *
 * TWO CALLERS, ONE FACE. `shell/enrollment.js` blows the card up to full screen
 * for the stamp ceremony; `shell/records.js` pins ten of them to the wall of the
 * Records Office. Neither owns a pixel of the drawing, so a card can never look
 * like two different objects in the same school.
 *
 * ---------------------------------------------------------------------------
 * THE FACE IS THE ART, AND THE SHELL OVERLAYS EXACTLY THREE THINGS.
 * (owner-locked, 2026-08-23, off a hand-arranged layout.)
 *
 * A finished card face is ONE full-bleed themed image per class, with the
 * class's own logo and the Arcademy logo BAKED INTO IT. The shell draws only
 * what MOVES:
 *
 *   1. THE STAMPS   ten slots, two rows of five, low and left. The grid squares
 *                   themselves are painted into the art, so the shell puts ink
 *                   in them and never draws a box around them. With no art it
 *                   draws the boxes too (the gradient floor below).
 *   2. THE CREST    the class's seal, in the crest bay on the right, tilted.
 *                   IT IS THE REWARD: hidden until the card is complete, then
 *                   revealed on the unlock beat beside the ribbon's own seal
 *                   animation (`markComplete()`).
 *   3. THE TEXT     the live strip under the crest bay. In progress it carries
 *                   the count and one rotating line of campus flavour; complete
 *                   it carries MASTERED and the date the card closed.
 *
 * WHERE THOSE THREE SIT IS DATA, NOT CSS: `art/punchcard/faces.json` maps a
 * gameKey to fractions of its own face image -
 *
 *   { "<gameKey>": { "slots": [{x,y,w,h} x10 row-major],
 *                    "crest": {x, y, scale, rotation},
 *                    "text":  {x, y, w, h} } }
 *
 * x/y/w/h are fractions of the face image (slots and text are BOXES, top-left
 * plus size; the crest's x/y is its CENTRE, `scale` its width as a fraction of
 * the face, `rotation` degrees). The file is loaded ONCE, optionally, exactly
 * the way `shell.js` loads the engine and the provider: no file, a broken file,
 * or a class missing from it all fall back to DEFAULT_GEOM below, and the whole
 * card still draws. **faces.json IS the art manifest**: a class present in it is
 * a class whose face image has shipped, which is the one signal that flips the
 * card off its gradient floor (`data-art`), so ship the json with the pngs.
 *
 * ART IS SWAPPABLE AND STILL NOT ALL HERE (§7). Every graphic is a CSS custom
 * property that resolves to `none` until the batch lands, and the CSS floor
 * draws the whole thing - aged panel, ruled grid, embossed crest - with
 * gradients. When the batch lands, the ONLY edit is the token block at the top
 * of the PUNCH CARDS section in styles.css:
 *
 *   --pc-face-src     the cardstock face, ONE PER art/punchcard/face-<key>.png
 *                     CLASS, overridden on
 *                     `.arc-pc[data-game="<key>"]`
 *   --pc-stamp-src    the ink stamp mark          art/punchcard/stamp.png
 *   --pc-ribbon-src   the UNLOCKED seal           art/punchcard/ribbon.png
 *   --pc-crest-src    per class, keyed by         art/punchcard/crest-<key>.png
 *                     `.arc-pc[data-game="<key>"]`
 *
 * NO TEXT IS EVER BAKED INTO THE STAMP, THE CREST OR THE RIBBON (lexicon law):
 * the count, the flavour line and every label are rendered live over the top.
 * The one deliberate exception is the class LOGO on the face image, which is
 * the owner's locked design - and it is why the drawn name band steps aside on
 * the art path (`[data-art="on"]`) instead of printing the name twice.
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

/** Where the face geometry lives, relative to THIS module's folder. */
const FACES_URL = '../art/punchcard/faces.json';

/* ----------------------------------------------------------------------------
 * THE GEOMETRY
 * -------------------------------------------------------------------------- */

/** Face aspect (w/h) when the art does not declare one. §7's template is ~1024x640. */
const DEFAULT_ASPECT = 1024 / 640;

function defaultGrid() {
  /* Two rows of five, low and left - the shape every face image is drawn
   * around, so a class with no entry in faces.json still lands its ink where a
   * reader expects a punch card to carry it. */
  const out = [];
  const w = 0.082, h = 0.132, x0 = 0.06, dx = 0.098;
  const rows = [0.52, 0.71];
  for (let r = 0; r < 2; r++) {
    for (let i = 0; i < 5; i++) out.push(Object.freeze({ x: x0 + i * dx, y: rows[r], w, h }));
  }
  return out;
}

/**
 * The floor every card falls back to: 2x5 grid lower-left, crest bay right of
 * centre with the owner's tilt, live text strip in the bottom-right corner.
 */
export const DEFAULT_GEOM = Object.freeze({
  slots: Object.freeze(defaultGrid()),
  crest: Object.freeze({ x: 0.775, y: 0.42, scale: 0.26, rotation: -8.79 }),
  text: Object.freeze({ x: 0.60, y: 0.70, w: 0.36, h: 0.24 }),
  aspect: DEFAULT_ASPECT,
  art: false,
});

/** Per-gameKey geometry, once faces.json has landed. Empty until then. */
let faces = Object.create(null);
let facesLoaded = false;
let facesPromise = null;

function num(v, lo, hi, dflt) {
  const n = Number(v);
  if (!isFinite(n)) return dflt;
  return Math.max(lo, Math.min(hi, n));
}

function box(src, dflt) {
  if (!src || typeof src !== 'object') return dflt;
  return {
    x: num(src.x, -0.5, 1.5, dflt.x),
    y: num(src.y, -0.5, 1.5, dflt.y),
    w: num(src.w, 0.005, 1.5, dflt.w),
    h: num(src.h, 0.005, 1.5, dflt.h),
  };
}

/** One class's entry, sanitized. Anything unusable keeps the default. */
function normalizeEntry(raw) {
  const src = (raw && typeof raw === 'object') ? raw : {};
  let slots = DEFAULT_GEOM.slots;
  if (Array.isArray(src.slots) && src.slots.length === HOLES) {
    const cleaned = [];
    for (let i = 0; i < HOLES; i++) cleaned.push(box(src.slots[i], DEFAULT_GEOM.slots[i]));
    slots = cleaned;
  }
  const c = (src.crest && typeof src.crest === 'object') ? src.crest : {};
  return {
    slots,
    crest: {
      x: num(c.x, -0.5, 1.5, DEFAULT_GEOM.crest.x),
      y: num(c.y, -0.5, 1.5, DEFAULT_GEOM.crest.y),
      scale: num(c.scale, 0.02, 1.5, DEFAULT_GEOM.crest.scale),
      rotation: num(c.rotation, -180, 180, DEFAULT_GEOM.crest.rotation),
    },
    text: box(src.text, DEFAULT_GEOM.text),
    aspect: num(src.aspect, 0.2, 6, DEFAULT_ASPECT),
    // The class is IN the manifest, so its face image shipped with it.
    art: true,
  };
}

/**
 * Install a face-geometry table by hand. The loader's own sink, and the seam
 * the suites drive - nothing else should call it.
 * @param {Object} table {gameKey: {slots, crest, text}}
 */
export function setFaceGeometry(table) {
  const out = Object.create(null);
  if (table && typeof table === 'object') {
    for (const k of Object.keys(table)) {
      const v = table[k];
      if (v && typeof v === 'object') out[k] = normalizeEntry(v);
    }
  }
  faces = out;
  facesLoaded = true;
  return faces;
}

/** The geometry a class draws with. Always a complete object, never null. */
export function geomFor(gameKey) {
  const g = faces[String(gameKey || '')];
  return g || DEFAULT_GEOM;
}

/** True once a load has been attempted (the file may still have been absent). */
export function faceGeometryLoaded() { return facesLoaded; }

/**
 * Load `art/punchcard/faces.json` ONCE, the way shell.js loads the engine and
 * the provider: optional, guarded, and never fatal. No file, no `fetch`, a 404,
 * a parse error or a nonsense body all leave every class on DEFAULT_GEOM - which
 * is a finished card, not a hole in the page.
 *
 * @param {Function=} log
 * @returns {Promise<Object>} the (possibly empty) table
 */
export function loadFaceGeometry(log) {
  if (facesPromise) return facesPromise;
  const say = typeof log === 'function' ? log : () => {};
  facesPromise = (async () => {
    try {
      if (typeof fetch !== 'function') throw new Error('no fetch');
      // Resolved against THIS module so the page's own origin is used, and the
      // suites' file:// copy simply fails fast into the fallback.
      const url = new URL(FACES_URL, import.meta.url).href;
      const res = await fetch(url, { cache: 'no-cache' });
      if (!res || !res.ok) throw new Error('http ' + ((res && res.status) || '?'));
      const body = await res.json();
      const table = setFaceGeometry(body);
      const n = Object.keys(table).length;
      say('punch cards: face geometry for ' + n + ' class' + (n === 1 ? '' : 'es'));
      return table;
    } catch (e) {
      facesLoaded = true;
      say('punch cards: no face geometry (' + ((e && e.message) || e) + ') - drawing the floor');
      return faces;
    }
  })();
  return facesPromise;
}

/* ----------------------------------------------------------------------------
 * THE ROTATING LINE
 * Eight rows, ONE PER LINE, so a mod can re-voice each of them on its own - a
 * single row holding eight lines could only ever be replaced wholesale.
 * -------------------------------------------------------------------------- */

/** How many `punchcard_phrase_<n>` rows the shell ships. */
export const PHRASES = 8;

/** English for the pool, exported as DATA the way ENROLL_LEX is. */
export const PHRASE_LEX = Object.freeze({
  punchcard_phrase_1: 'Attendance is a habit. Habits are the only thing this school grades.',
  punchcard_phrase_2: 'The card does not ask how well you did. Only that you came.',
  punchcard_phrase_3: 'Ten stamps and the room is yours. Nine of them are patience.',
  punchcard_phrase_4: 'Nobody has ever filled a card in one night. You will not be the first.',
  punchcard_phrase_5: 'The register is already open. Your name has been on it a while.',
  punchcard_phrase_6: 'One stamp a night. The school is in no hurry whatsoever.',
  punchcard_phrase_7: 'Progress in here is measured in ink, never in effort.',
  punchcard_phrase_8: 'You will stop noticing that you collect these. That is the intention.',
});

/** FNV-1a with a murmur3 finaliser - a trailing counter has to avalanche (trap 40). */
function hashKey(s) {
  let h = 2166136261 >>> 0;
  const str = String(s);
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 16777619) >>> 0;
  }
  h ^= h >>> 16; h = Math.imul(h, 2246822507) >>> 0;
  h ^= h >>> 13; h = Math.imul(h, 3266489909) >>> 0;
  h ^= h >>> 16;
  return h >>> 0;
}

/**
 * The line this card shows right now. DETERMINISTIC: the same class at the same
 * count says the same thing every time the card is drawn, so re-opening the
 * Records Office does not reshuffle the wall - and the line CHANGES as the card
 * fills, which is the whole reason it is on the card.
 */
export function phraseFor(gameKey, punches) {
  const n = Math.max(0, Math.min(HOLES, Math.round(Number(punches) || 0)));
  const i = (hashKey(String(gameKey || '') + '#' + n) % PHRASES) + 1;
  const key = 'punchcard_phrase_' + i;
  return t(key, PHRASE_LEX[key]);
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

function css(node, name, value) {
  try {
    if (node && node.style && typeof node.style.setProperty === 'function') {
      node.style.setProperty(name, value);
    }
  } catch (e) { /* noop */ }
}

function pct(v) { return (Math.round(Number(v) * 100000) / 1000) + '%'; }

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
 * @param {string} o.gameKey            drives `data-game`, which is how a face
 *                                      and a crest are swapped in per class
 * @param {Object} o.card               store.punchCard(gameKey) - already
 *                                      normalized, so nothing here re-derives
 * @param {string} o.name               the class's display name (lexicon)
 * @param {boolean=} o.small            the Records wall size
 * @param {number=} o.showPunches       draw THIS many holes instead of
 *                                      `card.punches` (the ceremony starts the
 *                                      card at its PRE-mint total and punches up
 *                                      to the real one - never the reverse)
 * @returns {Object} {root, punch(n, opts), punchTo(n, opts), markComplete(date)}
 */
export function cardFace({ gameKey, card, name, small, showPunches } = {}) {
  const c = card || {};
  const key = String(gameKey || '');
  const total = Math.max(0, Math.min(HOLES, Math.round(Number(c.punches) || 0)));
  const shown = showPunches == null ? total : Math.max(0, Math.min(HOLES, Math.round(showPunches)));
  /* `enrolled` is DERIVED (§2.2) and store.punchCard already carries it, but the
   * host's raw `punchcard-result` card does not - derive it here too so both
   * shapes draw the same face and no caller has to remember which one it holds. */
  const enrolled = c.enrolled === true
    || (typeof c.enrolledAt === 'string' && !!c.enrolledAt);
  const state = c.complete ? 'complete' : (enrolled ? 'open' : 'empty');
  const geom = geomFor(key);
  const label = String(name || key || '');

  const root = el('div', 'arc-pc' + (small ? ' arc-pc-small' : ''));
  attr(root, 'data-game', key);
  attr(root, 'data-state', state);
  /* THE ONE SIGNAL. A class in faces.json has a face image; a class that is not
   * keeps the drawn grid, the drawn name band and the gradient cardstock. */
  attr(root, 'data-art', geom.art ? 'on' : 'off');
  attr(root, 'role', 'img');

  const face = el('div', 'arc-pc-face');
  css(face, '--pc-aspect', String(geom.aspect || DEFAULT_ASPECT));
  root.appendChild(face);

  /* --- the name band. FALLBACK ONLY: the art bakes the class logo. -------- */
  const band = el('div', 'arc-pc-band');
  band.appendChild(el('span', 'arc-pc-name', label));
  band.appendChild(el('span', 'arc-pc-kind', t('punchcard', 'Stamp Card')));
  face.appendChild(band);

  /* --- 1. THE STAMPS ----------------------------------------------------- */
  const holes = el('div', 'arc-pc-holes');
  attr(holes, 'aria-hidden', 'true');
  const nodes = [];
  for (let i = 0; i < HOLES; i++) {
    const g = geom.slots[i] || DEFAULT_GEOM.slots[i];
    const h = el('i', 'arc-pc-hole' + (i < shown ? ' is-punched' : ''));
    css(h, '--i', String(i));
    css(h, 'left', pct(g.x));
    css(h, 'top', pct(g.y));
    css(h, 'width', pct(g.w));
    css(h, 'height', pct(g.h));
    attr(h, 'data-i', String(i + 1));
    holes.appendChild(h);
    nodes.push(h);
  }
  face.appendChild(holes);

  /* --- 2. THE CREST: the reward, not the decoration ----------------------
   * The BAY is the fallback's empty well, drawn in the crest's exact box so a
   * card with no art does not read as lopsided while it waits for its seal. It
   * never exists on the art path: a real face image has the bay printed on it. */
  const bay = el('div', 'arc-pc-bay');
  attr(bay, 'aria-hidden', 'true');
  const crest = el('div', 'arc-pc-crest');
  attr(crest, 'aria-hidden', 'true');
  for (const n of [bay, crest]) {
    css(n, 'left', pct(geom.crest.x));
    css(n, 'top', pct(geom.crest.y));
    css(n, 'width', pct(geom.crest.scale));
    css(n, '--pc-crest-rot', String(geom.crest.rotation) + 'deg');
  }
  bay.hidden = !!c.complete;
  crest.hidden = !c.complete;
  face.appendChild(bay);
  face.appendChild(crest);

  /* --- 3. THE LIVE TEXT ZONE --------------------------------------------- */
  const zone = el('div', 'arc-pc-text');
  css(zone, 'left', pct(geom.text.x));
  css(zone, 'top', pct(geom.text.y));
  css(zone, 'width', pct(geom.text.w));
  css(zone, 'height', pct(geom.text.h));
  const count = el('div', 'arc-pc-count', '');
  const phrase = el('div', 'arc-pc-phrase', '');
  const mastered = el('div', 'arc-pc-mastered', t('punchcard_mastered', 'Mastered'));
  const dateEl = el('div', 'arc-pc-date', '');
  zone.appendChild(count);
  zone.appendChild(phrase);
  zone.appendChild(mastered);
  zone.appendChild(dateEl);
  face.appendChild(zone);

  /* The seal hangs OFF the corner, so it rides the root and not the face - the
   * face clips (it is a picture) and a clipped seal reads as a rendering bug. */
  const ribbon = el('div', 'arc-pc-ribbon', t('punchcard_unlocked_chip', 'Unlocked'));
  ribbon.hidden = !c.complete;
  root.appendChild(ribbon);

  let drawn = shown;
  let done = !!c.complete;
  let closedOn = typeof c.unlockedAt === 'string' ? c.unlockedAt : '';

  function countLine(n) {
    return String(t('punchcard_count', '{have}/{need}'))
      .replace('{have}', String(n))
      .replace('{need}', String(HOLES));
  }

  /** Repaint the strip + the a11y label. The ONE place either is written. */
  function paintText() {
    count.hidden = done;
    phrase.hidden = done;
    mastered.hidden = !done;
    dateEl.hidden = !done;
    if (done) {
      /* No date-display convention exists anywhere on this page (the report card
       * prints its dateSeed raw), so the host's LOCAL `yyyy-MM-dd` is shown as
       * it is stored rather than invented into a second format. */
      dateEl.textContent = closedOn;
      attr(root, 'aria-label', label + ' - ' + t('punchcard_mastered', 'Mastered')
        + (closedOn ? ' ' + closedOn : ''));
    } else {
      count.textContent = countLine(drawn);
      phrase.textContent = phraseFor(key, drawn);
      attr(root, 'aria-label', label + ' - ' + drawn + ' / ' + HOLES);
    }
  }
  paintText();

  return {
    root,
    /** How many holes are currently drawn (the ceremony's cursor). */
    get drawn() { return drawn; },
    /** Whether the face is dressed as a finished card (the test seam). */
    get complete() { return done; },
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
      paintText();
      return true;
    },
    /** Punch every hole up to `n` at once (the reconcile path). */
    punchTo(n, opts) {
      const target = Math.max(0, Math.min(HOLES, Math.round(Number(n) || 0)));
      let any = false;
      for (let i = drawn + 1; i <= target; i++) any = this.punch(i, opts) || any;
      return any;
    },
    /**
     * The tenth hole landed: dress the card as an unlock. The CREST is the
     * reveal - `is-unlocking` runs it on the same beat as the ribbon's seal -
     * and the strip turns over to MASTERED plus the day the card closed.
     * @param {string=} unlockedAt yyyy-MM-dd; defaults to the card's own
     */
    markComplete(unlockedAt) {
      if (typeof unlockedAt === 'string' && unlockedAt) closedOn = unlockedAt;
      done = true;
      attr(root, 'data-state', 'complete');
      bay.hidden = true;
      crest.hidden = false;
      ribbon.hidden = false;
      if (root.classList) root.classList.add('is-unlocking');
      paintText();
    },
  };
}

/* ----------------------------------------------------------------------------
 * COPY HELPERS
 * Every user-facing string on a card is a lexicon row, so a mod can re-voice the
 * whole mechanic. Nothing here builds a key by concatenation the C# table cannot
 * enumerate: the per-class rows are `enroll_<gameKey>_1..3` and the rotating
 * lines are `punchcard_phrase_1..8`, both fixed and both enumerable.
 * -------------------------------------------------------------------------- */

/** '3 of 10' for a progress line. */
export function holesLine(punches) {
  const n = Math.max(0, Math.min(HOLES, Math.round(Number(punches) || 0)));
  return String(t('punchcard_holes', '{have} of {need}'))
    .replace('{have}', String(n))
    .replace('{need}', String(HOLES));
}

export default {
  HOLES, cardFace, thud, holesLine, THUD_PITCH,
  loadFaceGeometry, setFaceGeometry, geomFor, faceGeometryLoaded,
  DEFAULT_GEOM, phraseFor, PHRASES, PHRASE_LEX,
};
