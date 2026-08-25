/* ============================================================================
 * shell/recordsroom.js - THE RECORDS OFFICE, AS A ROOM.
 *
 * The office used to be a page: a wall of cards, a docket, a link to the report
 * card, all of it scrolling in a 1000px column. It is a PLACE now - the painted
 * set (art/vn/vn-09-records-office.png), four things in it you can touch, and
 * the old page folded down into one of them.
 *
 *   THE TRAY      the cards, and it is THE verb of the room: it wears the
 *                 breath, it wears the fresh tab, and on a first-ever visit it
 *                 is the only lit thing for two seconds - lit LOUDER, with the
 *                 `.rr-solo` ring, because "the other two went quiet" is not a
 *                 sentence a player can see. Pressing it opens
 *                 shell/records.js INSIDE a scene panel - the same wall, the
 *                 same docket, the same spotlight - and the way out of that
 *                 panel is the room, never the campus (a door you came through
 *                 is the door you leave by).
 *   THE BOARD     a close-up of the cork, with the night's Phantom Post sheets
 *                 pinned to it. Not a copy of the hall's board: the SAME
 *                 `mountNotices` over the SAME state, so both walls show one
 *                 night's set and reading either marks it read (owner call).
 *   THE BOOK      a close-up of the open volume on the desk, mounted by
 *                 shell/deskbook.js over the two page rects the art was
 *                 measured at. Placeholder chapters until the owner's prose
 *                 lands. Never called by the other word for it - that register
 *                 is barred from every user-facing string in this school.
 *   THE STOREROOM the annex door, and it does not exist until the night the
 *                 wall moved. Closed = no patch, no rect, nothing: an office
 *                 with a locked door you can press is an office with a puzzle
 *                 in it, and there is no puzzle here.
 *
 * WHAT THIS FILE OWNS AND WHAT IT BORROWS. It owns the room's table (the rects,
 * the views, the patch) and its two nudges; it borrows the whole chassis from
 * shell/scene.js and every screen it shows from a module that already existed.
 * It touches no store, no bridge and no EMI - the shell hands it narrow caps,
 * the annex's law (`showAnnex`'s), and it calls back.
 *
 * THE APRON OWNS THE FLOOR OF THE WIDE SHOT, and only of the wide shot. All
 * four measured rects up there clear y=640 on their own. The two CLOSE-UP hosts
 * (the cork, the book's pages) are measured from the PLATE and run under that
 * line - and they are pinned at their FULL measured size, unclamped, because
 * scene.js's band fades out on the way into a close-up (the zoom-is-a-zoom
 * ruling, 2026-08-25). The old clamp cost the cork 89px and each page 16px of
 * paper to hide behind a slab that is no longer there; a sheet of paper is
 * measured from the painting or it is not measured at all.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';
import { createScene } from './scene.js';
import { createRecords } from './records.js';
import { createDeskBook } from './deskbook.js';
import { mountNotices } from './corkboard.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 * Every number here is stage pixels on the 1376x768 plate, straight out of the
 * art prep (`records-room-spec.json`, owner-locked 2026-08-25). Nothing in this
 * file may round one for looks - a rect is a measurement of a painting.
 * -------------------------------------------------------------------------- */

export const RECTS = Object.freeze({
  tray: Object.freeze([184, 410, 178, 62]),
  corkboard: Object.freeze([226, 153, 323, 226]),
  ledger: Object.freeze([593, 428, 190, 47]),
  door: Object.freeze([1175, 128, 147, 455]),
});

/** The ajar panel, laid over the wide plate while `ajar` is on. */
export const DOOR_PATCH = Object.freeze([1050, 100, 326, 476]);

/** The bare cork inside the board close-up - where the paper goes. */
export const CORK_INNER = Object.freeze([43, 37, 1285, 692]);

/** The two blank pages inside the book close-up. */
export const LEDGER_PAGES = Object.freeze({
  left: Object.freeze([224, 164, 436, 492]),
  right: Object.freeze([712, 166, 438, 490]),
});

/** How long the other three rects hold back on a first-ever visit. */
export const LATE_MS = 2000;

/* The fresh tab hangs off the tray's top-right corner, outside the rect so it
 * never sits on the card art the tray is painted with. Decoration: it takes no
 * pointer events, so the tray under it is one unbroken press. */
const FRESH_W = 34;
const FRESH_H = 20;

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

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/** recordsroom.css, linked once and lazily - room.js's / corkboard.js's pattern. */
function ensureSheet(doc, log) {
  try {
    if (!doc || typeof doc.createElement !== 'function') return null;
    const had = typeof doc.getElementById === 'function' ? doc.getElementById('rr-styles') : null;
    if (had) return had;
    const link = doc.createElement('link');
    link.id = 'rr-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./recordsroom.css', 'shell/recordsroom.css');
    const host = doc.head || doc.documentElement || doc.body;
    if (host) host.appendChild(link);
    return link;
  } catch (e) { if (log) log('records room sheet failed'); return null; }
}

/** The shell's `<html class="arc-reduced">`, read defensively. */
function htmlReduced() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    if (de && de.classList && typeof de.classList.contains === 'function'
      && de.classList.contains('arc-reduced')) return true;
  } catch (e) { /* noop */ }
  try {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(prefers-reduced-motion: reduce)');
      if (m && m.matches) return true;
    }
  } catch (e) { /* noop */ }
  return false;
}

/** Walk a subtree for the first node wearing `cls`, BY INDEX and never with a
 *  selector - `querySelector` does not exist in the node double, and a room
 *  that only finds its own tray in a browser is a room with no suite. */
function findCls(node, cls) {
  if (!node) return null;
  try {
    if (node.classList && typeof node.classList.contains === 'function'
      && node.classList.contains(cls)) return node;
  } catch (e) { /* noop */ }
  const kids = node.children;
  if (!kids) return null;
  for (let i = 0; i < kids.length; i += 1) {
    const hit = findCls(kids[i], cls);
    if (hit) return hit;
  }
  return null;
}

/* ----------------------------------------------------------------------------
 * THE ROOM
 * -------------------------------------------------------------------------- */

/**
 * createRecordsRoom(caps) -> the handle, or null with no DOM.
 *
 * Narrow caps, the annex's law: nothing below this line imports the store, the
 * bridge or EMI, and every fact about the player arrives as a function.
 *
 * @param {Object} caps
 *  mount        - where the room's root goes (the shell's screen).
 *  t / log / lite / reduced
 *  gameKeys     - every registered class, registry order (records.js's wall).
 *  gameName     - (key) -> display name.
 *  punchCard    - (key) -> the normalized card.
 *  stampTotal   - () -> total stamps across every card RIGHT NOW.
 *  seenStamps   - () -> the total banked at the last panel open.
 *  markSeen     - (n) -> bank it (page-owned key `recordsRoomSeenStamps`).
 *  visits       - () -> how many times this room has been entered.
 *  markVisit    - (n) -> bank it (page-owned key `recordsRoomVisits`).
 *  bookPage     - () -> the remembered spread (`recordsBookPage`).
 *  saveBookPage - (n) -> bank it.
 *  daySeed      - the UTC day string the notices are dealt from.
 *  onCorkRead   - optional (noticeId) per sheet the visit marked read.
 *  ajar         - is the storeroom open at all (the reveal has fired)?
 *  onBack / onAnnex / onReport - the three ways out.
 *  reportLabel  - the report card's own label, the shell's word for it.
 */
export function createRecordsRoom(caps) {
  const c = caps || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof c.t === 'function' ? c.t : lexT;
  const log = typeof c.log === 'function' ? c.log : function () {};
  const gameKeys = Array.isArray(c.gameKeys) ? c.gameKeys.slice() : [];
  const num = (fn) => {
    try { const v = Number(fn && fn()); return isFinite(v) ? v : 0; }
    catch (e) { return 0; }
  };
  const reduced = () => !!c.reduced || htmlReduced();

  ensureSheet(doc, log);

  let dead = false;
  let recordsPage = null;      // shell/records.js, built on the first tray press
  let closeCards = null;       // the live panel's own close, or null
  let book = null;             // shell/deskbook.js, built on the first book view
  let paper = null;            // the corkboard's mount handle, or null
  let freshEl = null;          // the pink tab, or null
  let freshOff = null;         // its unmount
  let ajar = !!c.ajar;
  const timers = [];

  function later(fn, ms) {
    const id = setTimeout(function () {
      if (dead) return;
      try { fn(); } catch (e) { log('records room timer threw: ' + ((e && e.message) || e)); }
    }, ms);
    timers.push(id);
    return id;
  }

  /* ------------------------------------------------------------- the scene */

  const scene = createScene({
    mount: c.mount,
    lite: !!c.lite,
    reduced: !!c.reduced,
    log: log,
    t: t,
    label: t('records_kicker', 'Records Office'),
    views: {
      wide: {
        art: 'vn-09-records-office.png',
        hotspots: [
          /* THE VERB OF THE ROOM. Everything the office was built to show you
           * is behind this one rect, so it is the only thing that breathes. */
          [RECTS.tray[0], RECTS.tray[1], RECTS.tray[2], RECTS.tray[3],
            'cards', 'records_tray', 'The card tray', { main: true }],
          [RECTS.corkboard[0], RECTS.corkboard[1], RECTS.corkboard[2], RECTS.corkboard[3],
            'board', 'records_board', 'The noticeboard'],
          /* NEVER the other word for this in a label - `records_book` is the
           * key and 'The book' is the floor under it. */
          [RECTS.ledger[0], RECTS.ledger[1], RECTS.ledger[2], RECTS.ledger[3],
            'book', 'records_book', 'The book'],
          /* THE STOREROOM, and `when` is the whole gate: no flag, no rect. */
          [RECTS.door[0], RECTS.door[1], RECTS.door[2], RECTS.door[3],
            'annex', 'records_storeroom', 'The storeroom', { quiet: true, when: 'ajar' }],
        ],
      },
      board: { art: 'vn-10-records-corkboard.png', hotspots: [] },
      book: { art: 'vn-11-records-ledger.png', hotspots: [] },
    },
    patches: [
      { view: 'wide', art: 'vn-09-records-door-ajar.png', rect: DOOR_PATCH, when: 'ajar' },
    ],
    apron: { back: () => { try { if (c.onBack) c.onBack(); } catch (e) { log('records room back threw'); } } },
    onAction: onAction,
  });
  if (!scene) return null;

  try { scene.root.classList.add('rr-room'); } catch (e) { /* noop */ }
  scene.setFlag('ajar', ajar);

  /* ------------------------------------------------------- THE FRESH TAB */
  /* A pink tab on the tray whenever the school has stamped a card since the
   * last time the player actually opened the drawer. It is a COMPARISON, not a
   * counter: `recordsRoomSeenStamps` is written at the panel open and the tab
   * is the difference. Equal or behind (a card that healed down) = no tab. */

  function stampsFresh() {
    return num(c.stampTotal) > num(c.seenStamps);
  }

  function paintFresh() {
    if (dead) return;
    const want = stampsFresh() && !reduced();
    if (want && !freshEl) {
      freshEl = el('i', 'rr-fresh');
      attr(freshEl, 'aria-hidden', 'true');
      /* The word is optional and deliberately tiny - the tab IS the signal and
       * the label is the courtesy. It never announces itself to a reader; the
       * tray's own aria-label is the accessible half. */
      freshEl.appendChild(el('span', 'rr-fresh-word', t('records_fresh', 'New')));
      freshOff = scene.mountInView('wide', freshEl, [
        RECTS.tray[0] + RECTS.tray[2] - Math.round(FRESH_W * 0.5),
        RECTS.tray[1] - Math.round(FRESH_H * 0.6),
        FRESH_W, FRESH_H,
      ]);
    } else if (!want && freshEl) {
      try { if (freshOff) freshOff(); } catch (e) { /* noop */ }
      freshEl = null;
      freshOff = null;
    }
  }

  /* ------------------------------------------------------ THE FIRST VISIT */
  /* Nobody has ever stood in this room before, so the room introduces itself:
   * the tray alone for two seconds, then the other three fade up. It is a
   * CLASS ON THE ROOT and a rule in the sheet - one timer, no per-node state,
   * and `.arc-reduced` never gets it at all (everything is simply there). */

  /* THE SOLO RING. Holding the other three back is only half a first line: the
   * headline shot showed a room where the two things that went away were a 2px
   * rim at .30 nobody could see going. So the TRAY gets `.rr-solo` for exactly
   * the same window - a second, wider ring and a stronger glow over its normal
   * breath - and settles back into `.arm-main` when the window closes. The
   * class rides the TRAY, not the root, because it is the tray's own state, and
   * the tray is scene.js's one `main` hotspot whichever rect that is. */
  function soloRing(on) {
    try {
      const tray = findCls(scene.root, 'arm-main');
      if (!tray || !tray.classList) return;
      if (on) { if (tray.classList.add) tray.classList.add('rr-solo'); }
      else if (tray.classList.remove) tray.classList.remove('rr-solo');
    } catch (e) { /* a decoration must never be the thing that throws */ }
  }

  const firstEver = num(c.visits) <= 0;
  try { if (typeof c.markVisit === 'function') c.markVisit(num(c.visits) + 1); }
  catch (e) { log('records room visit bank failed: ' + ((e && e.message) || e)); }

  if (firstEver && !reduced()) {
    try { scene.root.classList.add('rr-late'); } catch (e) { /* noop */ }
    soloRing(true);
    later(function () {
      try { scene.root.classList.remove('rr-late'); } catch (e) { /* noop */ }
      soloRing(false);
    }, LATE_MS);
  }

  paintFresh();

  /* --------------------------------------------------------- THE ACTIONS */

  function onAction(action) {
    if (dead) return;
    if (action === 'cards') { openCards(); return; }
    if (action === 'board') { scene.showView('board'); mountBoard(); return; }
    if (action === 'book') { scene.showView('book'); mountBook(); return; }
    if (action === 'annex') {
      try { if (c.onAnnex) c.onAnnex(); } catch (e) { log('records room annex threw'); }
    }
  }

  /* ------------------------------------------------------------ THE CARDS */

  /**
   * The old Records Office screen, folded into a panel. records.js is mounted
   * `embedded`, which is the whole of the difference: no campus pill and a Back
   * that puts the cards away instead of walking out of the building. The room
   * is the way out of the room.
   */
  function openCards() {
    if (dead) return;
    /* THE DRAWER IS OPEN, so the tab is spent - banked BEFORE the render, so a
     * throw in the panel still costs the tab rather than repeating it. */
    try { if (typeof c.markSeen === 'function') c.markSeen(num(c.stampTotal)); }
    catch (e) { log('records room stamp bank failed: ' + ((e && e.message) || e)); }
    paintFresh();

    closeCards = scene.openOverlay('cards', function (panel) {
      try { panel.classList.add('rr-cards'); } catch (e) { /* noop */ }
      if (!recordsPage) {
        recordsPage = createRecords({
          gameName: c.gameName,
          punchCard: c.punchCard,
          log: log,
        });
      }
      recordsPage.render({
        gameKeys: gameKeys,
        embedded: true,
        onBack: function () { if (closeCards) closeCards(); },
        onReport: function () { try { if (c.onReport) c.onReport(); } catch (e) { log('records room report threw'); } },
        reportLabel: c.reportLabel,
        /* The office's own storeroom door already carries this errand, so the
         * panel's ajar seam is the same gate read the same way - one flag,
         * two handles, and neither invents the reveal. */
        onAnnex: ajar ? function () { try { if (c.onAnnex) c.onAnnex(); } catch (e) { /* noop */ } } : null,
      });
      panel.appendChild(recordsPage.root);
    });
  }

  /* ------------------------------------------------------------ THE BOARD */

  /** Pin the night's sheets over the bare cork, once per visit to the room. */
  function mountBoard() {
    if (dead || paper) return;
    const host = el('div', 'rr-cork arc-cork-wall');
    attr(host, 'role', 'list');
    /* THE FULL MEASURED CORK. Unclamped: the band is away in a close-up. */
    scene.mountInView('board', host, CORK_INNER);
    paper = mountNotices(host, {
      daySeed: c.daySeed,
      onRead: typeof c.onCorkRead === 'function' ? c.onCorkRead : undefined,
      log: log,
    });
    if (!paper) log('records room: the wall could not be pinned');
  }

  /* ------------------------------------------------------------- THE BOOK */

  function mountBook() {
    if (dead || book) return;
    book = createDeskBook({
      t: t,
      log: log,
      reduced: !!c.reduced,
      page: num(c.bookPage),
      onPage: typeof c.saveBookPage === 'function' ? c.saveBookPage : undefined,
      /* The keys are only the book's while the book is the room. */
      isActive: function () { return !dead && scene.view() === 'book'; },
    });
    if (!book) { log('records room: the book would not open'); return; }
    /* Both pages at their full measured height, for the cork's reason. */
    scene.mountInView('book', book.left, LEDGER_PAGES.left);
    scene.mountInView('book', book.right, LEDGER_PAGES.right);
  }

  /* ------------------------------------------------------------- the fold */

  /**
   * THE ESC RUNG, and the order is the whole of it: the spotlight the player
   * lifted one press ago, then the chassis's own inward-out fold (a panel, then
   * a close-up), then FALSE - at which point the shell's rung walks out of the
   * building. This module binds no key; the shell asks.
   */
  function escapeStep() {
    if (dead) return false;
    if (dismissSpotlight()) return true;
    try { return !!scene.escapeStep(); } catch (e) { return false; }
  }

  /** The spotlight's own rung, kept separate so the shell can name it. */
  function dismissSpotlight() {
    if (!recordsPage || typeof recordsPage.dismissSpotlight !== 'function') return false;
    try { return !!recordsPage.dismissSpotlight(); } catch (e) { return false; }
  }

  /* --------------------------------------------------------------- ajar */

  /** The night the wall moved, arriving live. Idempotent, and it is a LEVEL. */
  function setAjar(on) {
    if (dead) return;
    ajar = !!on;
    try { scene.setFlag('ajar', ajar); } catch (e) { /* noop */ }
  }

  function destroy() {
    if (dead) return;
    dead = true;
    for (let i = 0; i < timers.length; i += 1) { try { clearTimeout(timers[i]); } catch (e) { /* noop */ } }
    timers.length = 0;
    if (book) { try { book.destroy(); } catch (e) { /* noop */ } book = null; }
    if (paper) { try { paper.destroy(); } catch (e) { /* noop */ } paper = null; }
    if (recordsPage) { try { recordsPage.destroy(); } catch (e) { /* noop */ } recordsPage = null; }
    closeCards = null;
    freshEl = null;
    freshOff = null;
    /* The apron lives on <body>: scene.destroy() is the ONLY thing that takes
     * it off, which is why the shell's clearScreen has to reach this line. */
    try { scene.destroy(); } catch (e) { /* noop */ }
  }

  return {
    root: scene.root,
    escapeStep: escapeStep,
    dismissSpotlight: dismissSpotlight,
    setAjar: setAjar,
    fit: function () { try { return scene.fit(); } catch (e) { return null; } },
    destroy: destroy,
    /* ------------------------------------------------------- test seams */
    scene: scene,
    view: function () { return scene.view(); },
    /** Is the pink tab up? */
    freshUp: function () { return !!freshEl; },
    /** The records.js instance behind the panel, once it has been opened. */
    cards: function () { return recordsPage; },
    /** The book, once the close-up has been visited. */
    book: function () { return book; },
    /** The night's pinned sheets, once the cork has been visited. */
    notices: function () { return paper ? paper.notices : null; },
    /** Was this the room's first-ever visit? */
    firstVisit: function () { return firstEver; },
    /** Is the tray wearing the first-visit solo ring right now? */
    soloUp: function () {
      const tray = findCls(scene.root, 'arm-main');
      try { return !!(tray && tray.classList && tray.classList.contains('rr-solo')); }
      catch (e) { return false; }
    },
  };
}

export default createRecordsRoom;
