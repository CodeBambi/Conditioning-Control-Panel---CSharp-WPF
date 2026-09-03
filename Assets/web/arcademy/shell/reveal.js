/* ============================================================================
 * shell/reveal.js - THE PURCHASE REVEAL.
 *
 * The counter's own beat answers the question "did it go through". This one
 * answers the question the player actually asked, which is "what did I just
 * buy". Until this file, a token spent on the PA pack bought a gold row, a
 * sentence and silence: the thing itself did not appear until some later night
 * when the tannoy happened to speak, and a prize you cannot see is a prize you
 * eventually believe you imagined. So every sku gets ONE beat where the thing
 * is on the screen doing the thing it does.
 *
 * WHERE IT SITS IN THE PURCHASE
 *   press Trade  ->  prize-buy               (the counter proposes)
 *   wallet-result ->  settle() paints the row, the tray beat slides out
 *                 ->  the shell fires `arcademy-bought` on `document`
 *                 ->  THIS FILE, one scene overlay, one ceremony, two verbs
 *
 * It hangs off the ECHO like everything else in the purchase (prizecounter.js
 * trap 1): a page that celebrated a press would be celebrating a thing the host
 * has not agreed to hand over. Nothing below this line reads a wallet, spends
 * anything, or persists a pick - the OUTFIT SWEEP in particular is a preview
 * with no equip behind it, which is why "Later" is a real answer and not a
 * politeness (the Locker still has the jacket either way).
 *
 * THE THREE RUNGS (House Book, Deck IV). Every reveal, whatever the kind:
 *   1  the curtain: a whoosh, the scrim, the card lands with THE REVEAL move
 *      (scale 2.6 -> .9 -> 1, brightness 2.4 -> 1, one overshoot, 620ms)
 *   2  the thing does its thing: the strut, the swing, the crackle, the pin
 *      thunk. One cue out of the SOUNDS table, on the impact frame.
 *   3  the settle: the chime ladder (three `chime` cues, one semitone apart)
 *      and the verb starts breathing. The verb is the ONE breather here.
 *
 * FOUR LAWS
 *  1. NOTHING IS HELD HOSTAGE. Esc (the shell's ladder, top rung), the scrim,
 *     a tap anywhere off the card and both verbs all close it, from the first
 *     frame. The prize is already banked - closing early loses nothing at all.
 *  2. REDUCED MOTION GETS THE PICTURE, NOT LESS OF IT. Same card, same art,
 *     same verbs, no travel and no particles (the counter's tray beat rule).
 *     `lite` keeps the motion and drops the particles and the frame loops.
 *  3. ONE AT A TIME. A second buy while a card is up replaces it. Two
 *     ceremonies is two rooms.
 *  4. IT OWNS NO STRING. Every word is `t(key)` with the key in
 *     DEFAULT_LEXICON, or the host's own catalog row (`nameEn`/`blurbEn` are
 *     already authored and already localised - a second sentence about the
 *     late slip in this file is a sentence that disagrees with the shelf).
 *
 * WHY IT MOUNTS ON <body> AND NOT ON `.asc-root`. The booth is a scene, and
 * `.asc-root` is `position:fixed; z-index:10` - its own stacking context. A
 * child of it can never rise above EMI (#arc-emi z50) or the toast strip (z60)
 * however high its own z-index is, so a ceremony mounted inside the scene would
 * be a ceremony with the mascot sitting on top of it. The apron band solved the
 * same problem the same way one room over (rooms.css `.arm-bar`, a body-level
 * sibling at z55): body-level, z62, above the toast that announced the buy.
 * `.asc-panel` is worse again - it carries a transform mid-slide, so it would
 * be the containing block for anything fixed inside it.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
/* The counter's two registers and its verb table, single-sourced for the reason
 * prizecounter.js's own header gives: a second table of glyphs, sprites or
 * verbs here is a table that disagrees with the shelf the week somebody
 * restocks it. */
import { spriteUrl, GLYPHS, EQUIP_VERBS } from './prizecounter.js';
/* The wardrobe's art, from the mascot that renders it. Neither table is state:
 * `OUTFIT_FRAME_SRC` is which sheets exist and `TOYS` is which props do. */
import {
  OUTFITS, OUTFIT_FRAME_SRC, OVER_FRAME_SRC, TOYS, toyFrames, toyIndex,
} from '../emi/widget.js';
import { themeBySku, THEME_TOKENS } from './themes.js';
import { posterUrl, pickPoster } from './corkboard.js';

/* ----------------------------------------------------------------------------
 * THE SHEET (corkboard's pattern: lazily linked, from the MODULE's url).
 * -------------------------------------------------------------------------- */

export const STYLE_ID = 'arc-reveal-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./reveal.css', import.meta.url).href; }
  catch (e) { return 'shell/reveal.css'; }
}());

/** Link the sheet once. Idempotent, guarded, a no-op on the node DOM double. */
export function ensureStyles(doc) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  if (!d || typeof d.createElement !== 'function') return false;
  try {
    if (d.getElementById && d.getElementById(STYLE_ID)) return true;
    const link = d.createElement('link');
    link.id = STYLE_ID;
    link.rel = 'stylesheet';
    link.href = STYLE_HREF;
    const head = d.head || d.body || d.documentElement;
    if (!head || typeof head.appendChild !== 'function') return false;
    head.appendChild(link);
    return true;
  } catch (e) { return false; }
}

/* ----------------------------------------------------------------------------
 * THE EVENTS. Three document-level CustomEvents, named here ONCE so that four
 * files and five agents cannot each spell them slightly differently.
 * -------------------------------------------------------------------------- */

/** Fired by the shell the moment a `wallet-result` comes back ok.
 *  detail {sku, kind, name, cost, cur} - `kind` is `kindOf()` below, NOT the
 *  catalog's coarse `cosmetic|consumable|unlock|display`. */
export const BOUGHT_EVENT = 'arcademy-bought';
/** Fired by locker.js on a pick that actually stuck.
 *  detail {slot:'outfit'|'frame'|'theme'|'toy', id} - id null = no pick. */
export const EQUIPPED_EVENT = 'arcademy-equipped';
/** Asked of shell/pa.js: speak ONE line right now, outside the session cap.
 *  detail {reason:'purchase'|'preview'}. */
export const PA_REQUEST_EVENT = 'arcademy-pa-request';

/* ----------------------------------------------------------------------------
 * THE KINDS.
 *
 * The catalog's own `kind` is the HOST's four-way split (cosmetic / consumable
 * / unlock / display) and it is the right answer to a different question: it
 * prices a row and caps a stack. What a REVEAL needs to know is what the thing
 * IS, because a jacket and a campus look are both `cosmetic` and there is no
 * ceremony that can be both a strut and a palette.
 *
 * A SKU WITH NO ROW HERE IS NOT A BUG. It falls to `other`, which is the plate
 * beat: the sprite arrives with THE REVEAL move, the host's own blurb says what
 * it does, and one verb closes it. That is a real ceremony and it is the floor
 * every sku the catalog grows tomorrow lands on.
 * -------------------------------------------------------------------------- */
export const KIND_BY_SKU = Object.freeze({
  emi_varsity: 'outfit',
  emi_labcoat: 'outfit',
  emi_cheer: 'outfit',
  emi_swim: 'outfit',
  theme_drone: 'theme',
  theme_snowday: 'theme',
  id_frame_gold: 'frame',
  id_frame_navy: 'frame',
  brass_bell: 'bell',
  poster_drop_1: 'poster',
  emi_desk_toy: 'toy',
  pa_pack: 'pa',
  /* The three that dress the little walker crossing the quad. `away_colors` is
   * the kit rather than the trail, but it shows up in the same six seconds of
   * the same walk, and one plate saying so is more honest than three. */
  ghost_walk: 'walk',
  sparkler_steps: 'walk',
  away_colors: 'walk',
});

/**
 * What kind of ceremony does this sku want? The table first, then the sku's own
 * prefix (so a fifth outfit or a third frame is a reveal before anybody edits
 * this file), then the catalog's coarse kind for consumables, then `other`.
 *
 * @param {string} sku
 * @param {Object=} row the catalog row, when the caller has one
 * @returns {string} outfit|theme|frame|bell|poster|toy|pa|walk|consumable|other
 */
export function kindOf(sku, row) {
  const s = String(sku == null ? '' : sku);
  if (!s) return 'other';
  if (Object.prototype.hasOwnProperty.call(KIND_BY_SKU, s)) return KIND_BY_SKU[s];
  if (s.indexOf('emi_') === 0 && OUTFITS.indexOf(s.slice(4)) >= 0) return 'outfit';
  if (s.indexOf('theme_') === 0) return 'theme';
  if (s.indexOf('id_frame_') === 0) return 'frame';
  if (s.indexOf('poster') === 0) return 'poster';
  if (row && row.kind === 'consumable') return 'consumable';
  return 'other';
}

/* ----------------------------------------------------------------------------
 * THE CUES.
 *
 * ONE AUDIO DOOR (trap 18) and ONLY NAMES THE SOUNDS TABLE ALREADY CARRIES
 * (trap 115): an invented name degrades to a blip, which is a worse sound than
 * the one you meant. Every name below was read off shell/audio.js.
 *
 * THE CHIME LADDER is the House Book's, taken literally: one family, pitched up
 * a semitone per rung, three rungs. `pitch` is audio.js's own field (it
 * multiplies every frequency in the recipe), so this is one cue name and three
 * numbers rather than three sounds.
 * -------------------------------------------------------------------------- */

/** Equal temperament, three rungs: 1, 2^(1/12), 2^(2/12). */
export const CHIME_STEPS = Object.freeze([1, 1.0595, 1.1225]);
/** How far apart the rungs land. Slow enough to read as a climb. */
export const CHIME_GAP_MS = 130;

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

/** Fire one document CustomEvent, defensively. Answers whether it went. */
function shout(name, detail) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return false;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return false;
    document.dispatchEvent(new Ctor(String(name), { detail: detail || {} }));
    return true;
  } catch (e) { return false; }
}

/**
 * THE PURCHASE ANNOUNCEMENT. The shell calls this the moment a buy settles ok;
 * everything downstream (this file's ceremony, EMI's line) hangs off it, and it
 * is exported so the exact name lives in ONE place.
 * @param {Object} d {sku, kind?, name?, cost?, cur?}
 */
export function fireBought(d) {
  const o = d || {};
  const sku = String(o.sku == null ? '' : o.sku);
  if (!sku) return false;
  return shout(BOUGHT_EVENT, {
    sku,
    kind: o.kind || kindOf(sku, o.row || null),
    name: String(o.name == null ? '' : o.name),
    cost: Number.isFinite(Number(o.cost)) ? Number(o.cost) : 0,
    cur: o.cur === 'k' ? 'k' : 't',
  });
}

/**
 * THE EQUIP ANNOUNCEMENT. locker.js calls this from its four writers, which is
 * where every road that can change a pick already ends up (the room's tiles and
 * the receipt's verb both go through them).
 * The fifth slot is `bell`, and it is the odd one out: the bell has no switch,
 * so the Locker's "Ring it" poke fires it to announce a RING rather than a pick.
 * @param {string} slot 'outfit'|'frame'|'theme'|'toy'|'bell'
 * @param {?string} id   the pick, or null for "no pick"
 */
export function fireEquipped(slot, id) {
  return shout(EQUIPPED_EVENT, {
    slot: String(slot || ''),
    id: (typeof id === 'string' && id) ? id : null,
  });
}

/* ----------------------------------------------------------------------------
 * THE ART. Module-relative through `urlFor`, exactly like prizecounter.js's
 * `spriteUrl` and corkboard.js's `posterUrl` - a page-relative path breaks the
 * moment the page is mounted from anywhere but the bundle root (campus.js:320).
 * -------------------------------------------------------------------------- */

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** './art/emi/cheer/body-idle.png' (the widget's page-relative shape) as a url
 *  resolved off THIS module. Junk answers null, which is the plate's cue. */
function emiFrameUrl(pageRel) {
  const s = String(pageRel == null ? '' : pageRel);
  if (!s) return null;
  const rest = s.replace(/^\.\//, '');
  return urlFor('../' + rest, rest);
}

/**
 * THE POSE SWEEP. Six frames of the sheet that was just bought, in the order a
 * person turns around in: she walks in mid-sway, settles, sways the other way,
 * gives you the look, holds it up, and comes to rest. It is the same ten-frame
 * sheet the widget animates with, so a jacket that ships without `body-sway3`
 * simply skips that rung (a frame that will not load leaves the one before it
 * standing - see `swapFrame`).
 */
export const STRUT = Object.freeze(['sway1', 'idle', 'sway3', 'smug', 'pet', 'idle']);
/** How long one pose holds. 240ms reads as a turn; 120 reads as a flicker. */
export const STRUT_MS = 240;

/* ----------------------------------------------------------------------------
 * PLUMBING
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

function addCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.add === 'function') node.classList.add(cls); }
  catch (e) { /* noop */ }
}

function dropCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.remove === 'function') node.classList.remove(cls); }
  catch (e) { /* noop */ }
}

/** The document's own reduced-motion witness, the counter's reader exactly. */
function htmlReduced() {
  try {
    const h = document.documentElement;
    if (h && h.classList && h.classList.contains('arc-reduced')) return true;
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      return !!window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }
  } catch (e) { /* noop */ }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE INSTALL. The bell's shape and the Locker's (shell.js): four callers may
 * one day want to run a reveal and not one of them is holding a caps bag, so
 * the shell hands the factory over ONCE and every cap inside it is re-asked at
 * the moment the card opens.
 * -------------------------------------------------------------------------- */

let INSTALLED = null;
let wired = false;
/** The card on screen, or null. ONE at a time (law 3). */
let live = null;

function norm(caps) {
  const c = caps || {};
  const fn = (v, fb) => (typeof v === 'function' ? v : fb);
  return {
    t: fn(c.t, lexT),
    log: fn(c.log, () => {}),
    lite: fn(c.lite, () => false),
    reduced: fn(c.reduced, () => false),
    isMobile: fn(c.isMobile, () => false),
    daySeed: fn(c.daySeed, () => ''),
    catalog: fn(c.catalog, () => []),
    equip: fn(c.equip, null),
    previewTheme: fn(c.previewTheme, null),
  };
}

/**
 * installReveal({t, log, lite, reduced, isMobile, daySeed, catalog, equip,
 *                previewTheme})
 *
 *  equip        (sku) -> boolean. The Locker's `equipFromToast`. A false answer
 *               takes the verb off, exactly as it does at the counter.
 *  previewTheme (id, ms) -> a canceller | false. Lays a campus palette for a
 *               couple of seconds and puts the player's own back. The SHELL owns
 *               the writer (applyTheme is the one writer of theme tokens); this
 *               file never touches a custom property on <html>.
 */
export function installReveal(caps) {
  INSTALLED = caps && typeof caps === 'object' ? caps : null;
  if (INSTALLED && !wired) {
    try {
      if (typeof document !== 'undefined' && typeof document.addEventListener === 'function') {
        document.addEventListener(BOUGHT_EVENT, onBought);
        wired = true;
      }
    } catch (e) { /* no document, no ceremony - the school still works */ }
  }
  return INSTALLED;
}

function capsNow() {
  if (!INSTALLED) return null;
  try { return norm(INSTALLED); } catch (e) { return null; }
}

function onBought(e) {
  const d = (e && e.detail) || {};
  const sku = String(d.sku == null ? '' : d.sku);
  if (!sku) return;
  try { revealPurchase(sku, d); }
  catch (err) {
    const k = capsNow();
    if (k) k.log('reveal threw: ' + ((err && err.message) || err));
  }
}

/* ----------------------------------------------------------------------------
 * THE ROW. The host's catalog is the only place a name, a price or a blurb
 * comes from (law 4). A sku with no row still gets a ceremony - it simply gets
 * the sku's own glyph and a generic line.
 * -------------------------------------------------------------------------- */

function rowFor(k, sku) {
  let rows = [];
  try { rows = k.catalog() || []; } catch (e) { rows = []; }
  for (const r of rows) if (r && r.sku === sku) return r;
  return null;
}

function nameOf(k, sku, row, info) {
  if (row && (row.nameKey || row.nameEn)) return k.t(row.nameKey || '', row.nameEn || sku);
  if (info && info.name) return String(info.name);
  return sku;
}

function blurbOf(k, row) {
  if (!row || (!row.blurbKey && !row.blurbEn)) return '';
  const s = k.t(row.blurbKey || '', row.blurbEn || '');
  return typeof s === 'string' ? s : '';
}

/**
 * WHERE IT SHOWS UP. The catalog's blurb is the host's and says what the thing
 * IS; this line is the school's and says where the player will meet it again.
 * ONLY the kinds listed here have a key: `t()` would otherwise de-snake the key
 * into "Reveal Where Other" and print it, which is trap 12 written as a
 * sentence. A kind with nothing honest to add gets nothing rather than filler.
 */
export const WHERE_KINDS = Object.freeze(
  ['outfit', 'theme', 'frame', 'bell', 'poster', 'toy', 'pa', 'walk', 'consumable']
);

/* ----------------------------------------------------------------------------
 * THE CEREMONY
 * -------------------------------------------------------------------------- */

/** Is a reveal on screen right now? */
export function revealUp() { return !!live; }

/** Take it off, from any road, twice if you like. Answers whether one went. */
export function closeReveal() {
  if (!live) return false;
  const card = live;
  live = null;
  for (const id of card.timers) { try { clearTimeout(id); } catch (e) { /* noop */ } }
  card.timers.length = 0;
  /* Whatever the ceremony borrowed goes back BEFORE the node does - a campus
   * palette left on the root by a card that has gone is a page wearing a look
   * the player never bought. */
  if (typeof card.undo === 'function') { try { card.undo(); } catch (e) { /* noop */ } }
  card.undo = null;
  try {
    const h = document.documentElement;
    if (h && h.classList) h.classList.remove('arc-reveal-on');
  } catch (e) { /* noop */ }
  try { card.root.remove(); } catch (e) { /* noop */ }
  return true;
}

/**
 * ONE RUNG OF THE ESC LADDER, and it is the TOP one: nothing on the page can be
 * over a z62 ceremony the player opened a second ago (trap 48's shape). The
 * shell asks this before its own ladder.
 * @returns {boolean} true when the press was spent here
 */
export function revealEscape() {
  if (!live) return false;
  closeReveal();
  return true;
}

/**
 * Run the ceremony for one sku.
 * @param {string} sku
 * @param {Object=} info the `arcademy-bought` detail, when there is one
 * @returns {?Object} the handle (test seam), or null when there is no DOM,
 *          no install, or nothing to say
 */
export function revealPurchase(sku, info) {
  const want = String(sku == null ? '' : sku);
  if (!want) return null;
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const k = capsNow();
  if (!k) return null;

  closeReveal();
  ensureStyles(doc);

  const reduced = !!k.reduced() || htmlReduced();
  const lite = !!k.lite();
  const row = rowFor(k, want);
  const kind = (info && info.kind) ? String(info.kind) : kindOf(want, row);
  const name = nameOf(k, want, row, info);
  const blurb = blurbOf(k, row);

  const card = {
    sku: want, kind, timers: [], undo: null, root: null, verb: null, stage: null,
  };
  const after = (ms, fn) => {
    const id = setTimeout(() => {
      const i = card.timers.indexOf(id);
      if (i >= 0) card.timers.splice(i, 1);
      if (live !== card) return;          // the card went; the beat does not
      try { fn(); } catch (e) { k.log('reveal beat threw: ' + ((e && e.message) || e)); }
    }, ms);
    card.timers.push(id);
    return id;
  };

  /* --------------------------------------------------------------- the box */

  const root = el('div', 'arv-root'
    + (reduced ? ' is-still' : '')
    + (lite ? ' is-lite' : ''));
  attr(root, 'data-kind', kind);
  attr(root, 'data-sku', want);
  card.root = root;

  const scrim = el('div', 'arv-scrim');
  try { scrim.addEventListener('click', () => closeReveal()); } catch (e) { /* noop */ }
  root.appendChild(scrim);

  const box = el('div', 'arv-card');
  attr(box, 'role', 'dialog');
  attr(box, 'aria-live', 'polite');
  attr(box, 'aria-label', name);
  root.appendChild(box);

  /* THE KICKER. Micro-label, pixel face, and the one place the school says out
   * loud that the thing is yours now. */
  box.appendChild(el('p', 'arv-kick', k.t('reveal_kicker')));

  const stage = el('div', 'arv-stage is-' + kind);
  attr(stage, 'aria-hidden', 'true');
  card.stage = stage;
  box.appendChild(stage);

  box.appendChild(el('h2', 'arv-name', name));
  if (blurb) box.appendChild(el('p', 'arv-blurb', blurb));

  if (WHERE_KINDS.indexOf(kind) >= 0) {
    box.appendChild(el('p', 'arv-where', k.t('reveal_where_' + kind)));
  }

  const verbs = el('div', 'arv-verbs');
  box.appendChild(verbs);

  /* -------------------------------------------------------------- the show */

  try { (doc.body || doc.documentElement).appendChild(root); }
  catch (e) { k.log('reveal would not mount'); return null; }
  live = card;
  /* The mark the CELEBRATION rides (House Book Law III: the breath pauses while
   * one runs). reveal.css is the only reader and it holds EMI's idle still for
   * the length of the card - two things breathing is a nervous room. */
  try {
    const h = document.documentElement;
    if (h && h.classList) h.classList.add('arc-reveal-on');
  } catch (e) { /* noop */ }

  /* RUNG 1 - the curtain. */
  sfx('whoosh', 0.26);
  buildStage(k, card, stage, { sku: want, kind, row, reduced, lite, after });
  /* One frame to let the browser see the parked state, then the move. */
  after(20, () => addCls(root, 'is-up'));

  /* RUNG 2 lives inside buildStage (it is the thing doing its thing).
   * RUNG 3 - the settle. The ladder climbs while the card is still arriving,
   * which is what makes it read as one beat rather than three sounds. */
  const ladderAt = reduced ? 240 : 520;
  CHIME_STEPS.forEach((pitch, i) => {
    after(ladderAt + (i * CHIME_GAP_MS), () => sfx('chime', 0.34 + (i * 0.06), { pitch }));
  });

  /* --------------------------------------------------------------- the verbs */

  /* THE WEARABLE HALF. The counter's own table decides what may be worn from a
   * receipt and the Locker still gets the last word - a FALSE answer means
   * there was nothing to put on, and a button that says otherwise is a lie. */
  const verbRow = Object.prototype.hasOwnProperty.call(EQUIP_VERBS, want)
    ? EQUIP_VERBS[want] : null;
  if (verbRow && k.equip) {
    const put = el('button', 'btn primary arv-verb', k.t(verbRow[0], verbRow[1]));
    put.type = 'button';
    try {
      put.addEventListener('click', () => {
        let ok = false;
        try { ok = !!k.equip(want); }
        catch (e) { k.log('reveal equip threw: ' + ((e && e.message) || e)); ok = false; }
        /* Either answer spends the press: a verb you can hit twice is a verb
         * that looks broken the second time. */
        if (ok) sfx('commit', 0.34);
        else sfx('bump', 0.3);
        closeReveal();
      });
    } catch (e) { /* the DOM double carries no listeners - never fatal */ }
    verbs.appendChild(put);
    card.verb = put;

    const later = el('button', 'btn ghost arv-later', k.t('reveal_later'));
    later.type = 'button';
    try { later.addEventListener('click', () => closeReveal()); } catch (e) { /* noop */ }
    verbs.appendChild(later);
  } else {
    /* NOTHING TO PUT ON is not a lesser ceremony, it is a different one: the
     * thing is already on (the bell, the PA, the poster, the trail), so the one
     * verb is the way out and it says so plainly. */
    const done = el('button', 'btn primary arv-verb', k.t('reveal_good'));
    done.type = 'button';
    try { done.addEventListener('click', () => closeReveal()); } catch (e) { /* noop */ }
    verbs.appendChild(done);
    card.verb = done;
  }

  /* THE ONE BREATHER (Law III). It starts only once the arrival is over, so the
   * celebration and the breath never run together. */
  after(reduced ? 120 : 900, () => { if (card.verb) addCls(card.verb, 'is-breathing'); });
  /* Focus is the keyboard's way in, and it is the LAST thing that happens, so a
   * screen reader hears the name and the line before the button. */
  after(reduced ? 140 : 940, () => {
    try { if (card.verb && card.verb.focus) card.verb.focus({ preventScroll: true }); }
    catch (e) { /* noop */ }
  });

  return {
    root,
    get sku() { return card.sku; },
    get kind() { return card.kind; },
    get verb() { return card.verb; },
    get stage() { return card.stage; },
    close: closeReveal,
  };
}

/* ----------------------------------------------------------------------------
 * THE STAGES. One per kind, and every one of them is RUNG 2: the thing on the
 * screen doing the thing it does, with one cue on the impact frame.
 *
 * Every stage is allowed to draw nothing. A missing png takes ITSELF off and
 * leaves the glyph bed standing (the counter's rule 1), and a stage that cannot
 * build at all leaves a card that is still a name, a line and a verb.
 * -------------------------------------------------------------------------- */

function buildStage(k, card, stage, o) {
  switch (o.kind) {
    case 'outfit': return stageOutfit(k, card, stage, o);
    case 'pa': return stagePa(k, card, stage, o);
    case 'bell': return stageBell(k, card, stage, o);
    case 'theme': return stageTheme(k, card, stage, o);
    case 'poster': return stagePoster(k, card, stage, o);
    case 'frame': return stageFrame(k, card, stage, o);
    case 'toy': return stageToy(k, card, stage, o);
    default: return stagePlate(k, card, stage, o);
  }
}

/** The sku's own sprite on its glyph bed, with THE REVEAL move. Every stage
 *  that is not a picture of its own is this one. */
function plate(sku, cls) {
  const wrap = el('div', 'arv-plate' + (cls ? ' ' + cls : ''));
  wrap.appendChild(el('span', 'arv-glyph', GLYPHS[sku] || '▤'));
  const url = spriteUrl(sku);
  if (url) {
    const img = el('img', 'arv-sprite');
    try { img.src = url; img.alt = ''; } catch (e) { /* noop */ }
    attr(img, 'draggable', 'false');
    try {
      img.addEventListener('error', () => {
        try { img.remove(); } catch (e) { /* the bed is already under it */ }
      });
    } catch (e) { /* noop */ }
    wrap.appendChild(img);
  }
  return wrap;
}

/** 5 to 9 sparks, radially, dying before they land. Never the event itself -
 *  the garnish on one, and the first thing `lite` and reduced motion drop. */
function sparkle(host, n) {
  const count = Math.max(5, Math.min(9, Math.round(n) || 7));
  const burst = el('div', 'arv-burst');
  attr(burst, 'aria-hidden', 'true');
  for (let i = 0; i < count; i += 1) {
    const sp = el('span', 'arv-spark');
    try { sp.style.setProperty('--a', String(Math.round((360 / count) * i)) + 'deg'); }
    catch (e) { /* noop */ }
    try { sp.style.setProperty('--d', String(40 + ((i * 37) % 26)) + 'ms'); }
    catch (e) { /* noop */ }
    burst.appendChild(sp);
  }
  try { host.appendChild(burst); } catch (e) { /* noop */ }
  return burst;
}

function stagePlate(k, card, stage, o) {
  stage.appendChild(plate(o.sku));
  o.after(o.reduced ? 60 : 340, () => {
    sfx('commit', 0.3);
    if (!o.reduced && !o.lite) sparkle(stage, 7);
  });
  return stage;
}

/**
 * THE STRUT. She comes in wearing the thing you just bought, sweeps a handful
 * of poses out of that sheet, and stops under the light.
 *
 * IT PERSISTS NOTHING. This is art in a box, not an equip - `lockerOutfit` is
 * untouched, the widget in the corner is untouched, and "Later" is a real
 * answer. That is the whole reason the verb underneath is the counter's verb
 * and not a second writer.
 */
function stageOutfit(k, card, stage, o) {
  const name = o.sku.indexOf('emi_') === 0 ? o.sku.slice(4) : '';
  const frames = Object.prototype.hasOwnProperty.call(OUTFIT_FRAME_SRC, name)
    ? OUTFIT_FRAME_SRC[name] : null;
  if (!frames) return stagePlate(k, card, stage, o);

  const spot = el('div', 'arv-spot');
  attr(spot, 'aria-hidden', 'true');
  stage.appendChild(spot);

  /* THE WRAP carries the entrance so the two layers travel as one. It shrinks
   * to the body's width (a flex item is its content), which is what lets the
   * overlay be `inset: 0` and land on the same pixels. */
  const wrap = el('div', 'arv-emiwrap');
  const img = el('img', 'arv-emi');
  attr(img, 'draggable', 'false');
  try { img.alt = ''; img.src = emiFrameUrl(frames.idle) || ''; } catch (e) { /* noop */ }
  /* A sheet that will not load takes the whole picture off and leaves the
   * sku's own plate: a broken outfit reveal must still be a reveal. */
  try {
    img.addEventListener('error', () => {
      try { wrap.remove(); spot.remove(); } catch (e2) { /* noop */ }
      if (!stage.firstChild) stagePlate(k, card, stage, o);
    });
  } catch (e) { /* noop */ }
  wrap.appendChild(img);

  /* THE OVERLAY SHEET, and it is silent about being missing. Three of the four
   * wardrobes ship no `over-` frames at all and never will; the one that does
   * is swim, whose goggles have to be drawn IN FRONT of her glass or they are
   * not goggles (emi/widget.js's own block says it). One img, one probe, and a
   * 404 removes it without touching the body underneath. */
  const overFrames = Object.prototype.hasOwnProperty.call(OVER_FRAME_SRC, name)
    ? OVER_FRAME_SRC[name] : null;
  let over = null;
  if (overFrames) {
    over = el('img', 'arv-over');
    attr(over, 'draggable', 'false');
    attr(over, 'aria-hidden', 'true');
    try { over.alt = ''; over.src = emiFrameUrl(overFrames.idle) || ''; } catch (e) { /* noop */ }
    try {
      over.addEventListener('error', () => {
        try { over.remove(); } catch (e2) { /* noop */ }
        over = null;
      });
    } catch (e) { /* noop */ }
    wrap.appendChild(over);
  }
  stage.appendChild(wrap);

  /* THE IMPACT FRAME: she lands, the light comes up, one door-weight cue. A
   * still card gets the cue too - the sound was never the motion. */
  o.after(o.reduced ? 60 : 380, () => {
    sfx('commit', 0.34);
    addCls(spot, 'is-lit');
    if (!o.reduced && !o.lite) sparkle(stage, 7);
  });

  /* THE POSE SWEEP. Reduced motion holds `idle` (the honest still of a
   * wardrobe, locker.js's own ruling), and `lite` holds it too - a frame swap
   * is a decode per frame and that is exactly what the phone diet cut. */
  if (o.reduced || o.lite) return stage;
  let at = 0;
  const step = () => {
    if (at >= STRUT.length) return;
    const key = STRUT[at];
    at += 1;
    const url = emiFrameUrl(frames[key]);
    if (url) { try { img.src = url; } catch (e) { /* the frame before stands */ } }
    /* The goggles walk the same pose. Same key, same instant - two srcs set in
     * one turn of the loop can never drift a frame apart. */
    if (over && overFrames) {
      const ov = emiFrameUrl(overFrames[key]);
      if (ov) { try { over.src = ov; } catch (e) { /* noop */ } }
    }
    o.after(STRUT_MS, step);
  };
  o.after(440, step);
  return stage;
}

/**
 * THE TANNOY. The horn plate, three rings going out of it, and a real line: the
 * request goes to shell/pa.js, which owns the schedule, the cap and the lite
 * gate, and which is the only thing on this page allowed to speak.
 *
 * IT IS A REQUEST, NOT A PLAY. If the PA declines (lite, a class in the air,
 * no pack yet in the wallet the frame that answered the buy has not landed on),
 * nothing here breaks: the plate still crackles and the line still reads.
 */
function stagePa(k, card, stage, o) {
  stage.appendChild(plate(o.sku, 'is-horn'));
  const rings = el('div', 'arv-rings');
  attr(rings, 'aria-hidden', 'true');
  rings.appendChild(el('span', 'arv-ring'));
  rings.appendChild(el('span', 'arv-ring'));
  rings.appendChild(el('span', 'arv-ring'));
  stage.appendChild(rings);

  o.after(o.reduced ? 60 : 360, () => {
    /* The crackle before the voice. `paper` is the driest thing in the table
     * and a tannoy clearing its throat is exactly that. */
    sfx('paper', 0.26);
    addCls(rings, 'is-on');
  });
  o.after(o.reduced ? 260 : 760, () => {
    shout(PA_REQUEST_EVENT, { reason: 'purchase' });
  });
  return stage;
}

/**
 * THE BELL. It swings and it RINGS, and the ring is the plain `bell` cue: the
 * shell installed a cosmetic getter on audio.js at boot, the wallet frame that
 * opened this card has already landed, so `bell` resolves to `bell_brass` all
 * by itself. The `set_bell` bus message writes the same slot and would outlive
 * this card (shell.js's own warning) - it stays unused here.
 */
function stageBell(k, card, stage, o) {
  const p = plate(o.sku, 'is-bell');
  stage.appendChild(p);
  o.after(o.reduced ? 60 : 400, () => {
    addCls(p, 'is-swinging');
    sfx('bell', 0.42);
    if (!o.reduced && !o.lite) sparkle(stage, 5);
  });
  /* A second, quieter strike on the swing back: one ring is a sample, two is a
   * bell. Nothing else in the school rings twice. */
  o.after(o.reduced ? 700 : 1240, () => sfx('bell', 0.2, { pitch: 0.99 }));
  return stage;
}

/**
 * THE CAMPUS LOOK. Two seconds of the real thing, on the real page, and then
 * the player's own back.
 *
 * THE SHELL LAYS IT, NOT US. `applyTheme` is the ONE writer of theme tokens
 * (shell.js) and a second writer is the bug that ends with a page wearing a
 * palette nobody picked. We ask for a preview, we are handed a canceller, and
 * closeReveal spends it however the card goes away - the verb, the scrim, Esc,
 * or a second purchase landing on top.
 */
function stageTheme(k, card, stage, o) {
  const th = themeBySku(o.sku);
  stage.appendChild(plate(o.sku));

  /* The swatch strip is the part that survives a host with no preview road: six
   * of the thirteen tokens, in the order the eye reads a palette. */
  if (th && th.palette) {
    const strip = el('div', 'arv-swatch');
    attr(strip, 'aria-hidden', 'true');
    for (const key of ['ground', 'navy', 'panel', 'accent', 'accent2', 'gold']) {
      const hex = th.palette[key];
      if (typeof hex !== 'string' || !hex) continue;
      const chip = el('span', 'arv-chip');
      attr(chip, 'data-token', THEME_TOKENS[key] || key);
      try { chip.style.background = hex; } catch (e) { /* noop */ }
      strip.appendChild(chip);
    }
    stage.appendChild(strip);
  }

  o.after(o.reduced ? 60 : 340, () => {
    sfx('commit', 0.3);
    if (!o.reduced && !o.lite) sparkle(stage, 7);
    if (!th || !k.previewTheme) return;
    let undo = null;
    try { undo = k.previewTheme(th.id, 2000); }
    catch (e) { k.log('reveal theme preview threw: ' + ((e && e.message) || e)); undo = null; }
    if (typeof undo === 'function') card.undo = undo;
    addCls(stage, 'is-previewing');
    o.after(2000, () => { dropCls(stage, 'is-previewing'); card.undo = null; });
  });
  return stage;
}

/**
 * THE PIN THUNK. A patch of the corkboard by the door with tonight's print
 * landing on it crooked and a pin going in. The print is the same one
 * corkboard.js would deal for this day - a reveal that showed a poster the wall
 * is not about to hang is a reveal that lied.
 */
function stagePoster(k, card, stage, o) {
  let id = '';
  try { id = pickPoster(String(k.daySeed() || '')); } catch (e) { id = ''; }
  const cork = el('div', 'arv-cork');
  stage.appendChild(cork);
  if (!id) { cork.appendChild(plate(o.sku)); return stage; }

  const sheet = el('div', 'arv-print');
  const img = el('img', 'arv-print-img');
  attr(img, 'draggable', 'false');
  try { img.alt = ''; img.src = posterUrl(id); } catch (e) { /* noop */ }
  try {
    img.addEventListener('error', () => {
      try { sheet.remove(); } catch (e2) { /* noop */ }
      cork.appendChild(plate(o.sku));
    });
  } catch (e) { /* noop */ }
  sheet.appendChild(img);
  sheet.appendChild(el('span', 'arv-pin'));
  cork.appendChild(sheet);

  o.after(o.reduced ? 60 : 420, () => {
    addCls(sheet, 'is-pinned');
    /* The paper, then the pin. `thud` is the body knock the House Book asks a
     * confirmation to end on and it is the quieter of the two on purpose. */
    sfx('paper', 0.28);
  });
  o.after(o.reduced ? 120 : 620, () => sfx('thud', 0.3));
  return stage;
}

/**
 * THE CARD FLIPS. A little student ID, front then back, and the back is wearing
 * the frame that was just bought. It is drawn rather than borrowed: idcard.js
 * paints the real card with a photo, a chip and a share pipeline behind it, and
 * none of that belongs in a two second beat.
 */
function stageFrame(k, card, stage, o) {
  const id = o.sku.indexOf('id_frame_') === 0 ? o.sku.slice('id_frame_'.length) : '';
  /* THE LITTLE CARD, twice. A photo well and three ruled lines is the least
   * drawing that still reads as a student ID rather than an empty rectangle -
   * and it has to read as one instantly, because the whole point of the beat is
   * that the SECOND one is wearing a frame the first one was not. No text: a
   * name here would be a string with nothing to say and a lexicon key to keep. */
  const face = (cls, frame) => {
    const f = el('div', 'arv-face ' + cls);
    if (frame) attr(f, 'data-frame', frame);
    const mini = el('span', 'arv-mini');
    mini.appendChild(el('span', 'arv-well'));
    const lines = el('span', 'arv-lines');
    for (let i = 0; i < 3; i++) lines.appendChild(el('i', ''));
    mini.appendChild(lines);
    f.appendChild(mini);
    return f;
  };
  const flip = el('div', 'arv-flip');
  const front = face('is-front', '');
  const back = face('is-back', id || 'gold');
  flip.appendChild(front);
  flip.appendChild(back);
  stage.appendChild(flip);

  o.after(o.reduced ? 40 : 420, () => {
    addCls(flip, 'is-flipped');
    sfx('card_deal', 0.3);
  });
  o.after(o.reduced ? 80 : 760, () => {
    sfx('commit', 0.28);
    if (!o.reduced && !o.lite) sparkle(stage, 6);
  });
  return stage;
}

/**
 * SHE HOLDS IT UP. The standard sheet (the toy is the prize, not the outfit)
 * with tonight's prop rising into her hands and turning over.
 *
 * TONIGHT'S, not a favourite: `toyIndex` is the same seeded pick her desk is
 * about to make, so the thing in the reveal is the thing on the tube.
 */
function stageToy(k, card, stage, o) {
  const toy = TOYS.length ? TOYS[toyIndex(String(k.daySeed() || ''))] : null;
  const frames = toyFrames(toy).map(emiFrameUrl).filter(Boolean);
  const body = el('img', 'arv-emi is-holding');
  attr(body, 'draggable', 'false');
  try { body.alt = ''; body.src = urlFor('../art/emi/body-pet.png', 'art/emi/body-pet.png'); }
  catch (e) { /* noop */ }
  try {
    body.addEventListener('error', () => { try { body.remove(); } catch (e2) { /* noop */ } });
  } catch (e) { /* noop */ }
  stage.appendChild(body);

  if (!frames.length) { stage.appendChild(plate(o.sku)); return stage; }
  const prop = el('img', 'arv-toy');
  attr(prop, 'draggable', 'false');
  try { prop.alt = ''; prop.src = frames[0]; } catch (e) { /* noop */ }
  try {
    prop.addEventListener('error', () => { try { prop.remove(); } catch (e2) { /* noop */ } });
  } catch (e) { /* noop */ }
  stage.appendChild(prop);

  o.after(o.reduced ? 60 : 400, () => {
    addCls(prop, 'is-up');
    sfx('lift', 0.3);
    if (!o.reduced && !o.lite) sparkle(stage, 6);
  });

  /* The prop's own loop, at the widget's own frame length. Off under lite and
   * reduced motion for the strut's reason: a frame swap is a decode. */
  if (o.reduced || o.lite || frames.length < 2) return stage;
  let at = 0;
  const ms = (toy && Number(toy.ms)) || 140;
  const spin = () => {
    at = (at + 1) % frames.length;
    try { prop.src = frames[at]; } catch (e) { /* the frame before stands */ }
    o.after(ms, spin);
  };
  o.after(700, spin);
  return stage;
}

export default revealPurchase;
