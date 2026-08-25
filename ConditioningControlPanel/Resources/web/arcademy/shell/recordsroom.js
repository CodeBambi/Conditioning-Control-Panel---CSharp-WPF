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
 * THE ROOM IS ALIVE (W2). scene.js grew a declarative FX layer; this file
 * grew the office's TABLE for it, below `RECTS`. Six rows on the wide shot -
 * the sign, the lamp, the dust in its cone, the window, the clock and (only
 * when the storeroom is open) the seam - plus two pixels of parallax on the
 * painting. None of it is a verb: every rect down there is decoration, and the
 * four things you can press are exactly the four things you could press
 * before. The seventh piece, the corkboard's paper flutter, is pure CSS in
 * corkboard.css, because a hover state does not need a runtime.
 *
 * THE CLOCK IS THE ONE PIECE THAT IS NOT DECORATION. It reads the player's own
 * wall clock, and it is the only diegetic real-time element in the school
 * (owner ruling 2026-08-25). The plate painted a face stopped at eleven; a
 * cream disc buries the painted hands and two DOM hands tell the truth over it.
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
import { mountNotices, closeNoticeReader, readerUp } from './corkboard.js';

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

/* ----------------------------------------------------------------------------
 * THE ALIVE LAYER'S TABLE
 *
 * scene.js's `fx` contract, in the same stage pixels as everything else, and
 * every rect below was crop-verified against `art/vn/vn-09-records-office.png`
 * rather than trusted from the spec sheet. Where a number here differs from
 * `records-room-spec.json` the difference is written down beside it.
 *
 * NOTHING IN THIS TABLE IS A VERB. The layer takes no pointer events and no
 * row of it appears in the Esc fold, the tab order or the audit; a row that
 * fails to build costs a breath and never a rect. The apron-line warning does
 * not apply either - it audits HOTSPOTS, and the floor pool under the storeroom
 * door is light on a floor, which is exactly where light on a floor goes.
 * -------------------------------------------------------------------------- */

/** The RECORDS sign over the counter. */
export const FX_SIGN = Object.freeze([577, 169, 346, 91]);
/** The banker's lamp's pool on the counter (the rect is centred on the base). */
export const FX_LAMP = Object.freeze([383, 433, 282, 42]);
/** The cone above it - the lamp head down to the counter, where the dust is. */
export const FX_MOTES = Object.freeze([420, 300, 200, 180]);
/** The window, and the route lamp burning outside it. */
export const FX_WINDOW = Object.freeze([52, 120, 64, 291]);
/** The wall clock. `r` is the BEZEL; `faceR` is the disc that buries the
 *  painted hands, measured off the plate (hands reach r16, numerals start at
 *  r21) so the numerals stay painted and the hands do not. */
export const FX_CLOCK = Object.freeze({ cx: 1009, cy: 187, r: 44 });
export const FX_CLOCK_FACE_R = 19;
/** The open door's leading edge - the bright side of the gap in the ajar
 *  plate, measured off the composite at x=1180 (the door rect's own left edge,
 *  1175, is the JAMB, and a breath on the jamb is a breath on the wall). */
export const FX_SEAM_EDGE = Object.freeze([1180, 132, 6, 444]);
/** ...and what it throws on the floor. Below the apron line on purpose: the
 *  patch is cut at y=575 and the light has to land somewhere. */
export const FX_SEAM_POOL = Object.freeze([1107, 552, 180, 148]);

/**
 * THE TABLE ITSELF. Seeds are literal so a "random" stutter is the same
 * stutter every night - a rare event nobody can reproduce is a bug report.
 * `now` is left off the clock row, so it reads the real Date; a suite hands
 * its own in through `caps.now`.
 */
export function recordsFx(now) {
  return [
    { kind: 'neon', view: 'wide', rect: FX_SIGN, seed: 0x5EC0DE },
    { kind: 'lamp', view: 'wide', rect: FX_LAMP },
    { kind: 'motes', view: 'wide', rect: FX_MOTES, seed: 0x11FADE },
    { kind: 'window', view: 'wide', rect: FX_WINDOW, seed: 0x0FF1CE },
    {
      kind: 'clock', view: 'wide', circle: FX_CLOCK,
      faceR: FX_CLOCK_FACE_R,
      hourLen: 12, minLen: 17,
      now: typeof now === 'function' ? now : undefined,
    },
    /* THE SEAM IS THE DOOR'S OWN GATE, read the way the rect reads it: no
     * reveal, no flag, no nodes. */
    {
      kind: 'seam', view: 'wide', when: 'ajar',
      rect: RECTS.door, edge: FX_SEAM_EDGE, pool: FX_SEAM_POOL,
    },
    { kind: 'tilt', view: 'wide', amp: 2 },
  ];
}

/** The bare cork inside the board close-up - where the paper goes. */
export const CORK_INNER = Object.freeze([43, 37, 1285, 692]);

/* ----------------------------------------------------------------------------
 * THE PAINTED CORK ON THE WIDE PLATE, and the lamp that stands in front of it.
 *
 * `RECTS.corkboard` is the HOTSPOT - the whole framed object, timber and all,
 * with a few pixels of slack under it so the press is comfortable. It is not a
 * place to hang paper, and hanging paper on it is exactly what the first
 * miniature did: the sheets started 9px above the cork (on the frame), ran 17px
 * below its bottom rail onto the desk, and the bottom-right one landed on the
 * banker's lamp (owner screenshot, 2026-08-25).
 *
 * These two numbers are measured off `art/vn/vn-09-records-office.png` itself,
 * not read off a spec sheet:
 *   - the frame's INNER edges are the dark outline at x=236 and x=539, y=163
 *     and y=362, so the cork face is x 237..538, y 165..361 (302 x 197);
 *   - the lamp's shade breaks that plane at y=322 and reaches back to x=480,
 *     which is inside the cork's own right third.
 * So the paper stops five pixels above the shade: [237, 165, 302, 152]. The
 * bottom band of the board is bare on purpose - it is the part of the board
 * the lamp is standing in front of.
 * -------------------------------------------------------------------------- */

/** The painted cork on the WIDE plate, trimmed to clear the lamp shade. */
export const CORK_WIDE = Object.freeze([237, 165, 302, 152]);
/** The top edge of the banker's lamp shade on that plate (stage px). */
export const LAMP_SHADE_TOP = 322;

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
 *  now          - optional () -> Date for the wall clock (a suite's seam; the
 *                 shipping room leaves it off and reads the real one).
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
  let mini = null;             // the WIDE shot's miniature wall, or null
  let miniOff = null;          // its unmount
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
    /* THE ROOM BREATHES. Decoration only - see the table's own header. */
    fx: recordsFx(typeof c.now === 'function' ? c.now : undefined),
    apron: { back: () => { try { if (c.onBack) c.onBack(); } catch (e) { log('records room back threw'); } } },
    onAction: onAction,
  });
  if (!scene) return null;

  try { scene.root.classList.add('rr-room'); } catch (e) { /* noop */ }
  scene.setFlag('ajar', ajar);
  mountMiniCork();

  /* THE ROOM TONE IS NOT WIRED, AND THAT IS A DELIBERATE ABSENCE.
   *
   * The design asked for an ambient bed under this room - one
   * `arcademy-sfx {name:'records_bed', level:.12, bus:'fx', loop:true}` on
   * entry and a `{name:'records_bed', stop:true}` on the way out. THE SFX
   * CONTRACT CANNOT CARRY IT. `shell/audio.js`'s `onSfx` reads exactly
   * `{name, level, bus, duck, pitch, url, key, maxMs}` and one control message
   * (`name:'stop_clips'`); `detail.loop` is read nowhere, and there is no
   * per-name stop. Every cue it plays is a ONE-SHOT that schedules its own
   * `stop()` on the audio clock.
   *
   * So the bed is not here. Wiring it would mean either a looping node in this
   * file - which is trap 18, the law that says a room owns no audio node - or
   * a private timer re-firing a one-shot forever, which is worse. What is
   * MISSING is a contract: `detail.loop` plus a keyed stop on the
   * `arcademy-sfx` bus, owned by audio.js. When that lands, the two calls go
   * here and in destroy(), and nothing else in this file has to move.
   * TODO(audio.js): `arcademy-sfx` has no loop/stop contract - see above. */

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

  /* ------------------------------------------------------- THE MINIATURE */

  /**
   * THE BOARD HAS PAPER ON IT FROM ACROSS THE ROOM (owner ruling 2026-08-25).
   * The plate paints bare cork, so until you walked up to it the office's
   * noticeboard was an empty board in a room that is supposed to be lived in.
   * This hangs the SAME night's set - one table, one state, one wall - into the
   * cork rect on the wide shot, stood back from.
   *
   * IT IS A PREVIEW, AND THAT WORD IS LOAD-BEARING: `mountNotices({preview})`
   * marks nothing read and banks no visit, so looking at the board is not
   * reading the board and the fresh dot survives the glance.
   *
   * THE SCALE IS DERIVED, NEVER TYPED. The inner wall is laid out at the
   * CLOSE-UP's own width, so a sheet in the miniature is the same shape as the
   * sheet you walk up to, and the whole thing is then scaled by the ratio the
   * two rects give us. Its height is the rect divided back out by that scale,
   * so the miniature is the TOP of the close-up's own board - the same grid,
   * the same rows, the same type - ending where the painted cork ends.
   *
   * IT HANGS ON `CORK_WIDE`, NOT ON THE HOTSPOT. See that constant: the rect
   * you press is the framed object and the rect paper hangs on is the cork
   * inside it, minus the band the lamp stands in front of.
   *
   * AND IT IS DEALT THE CLOSE-UP'S OWN FIT. Same `boxH`, same `scale`, so the
   * two walls shrink the same sheet by the same amount and the thumbnail is a
   * picture of the board rather than a second, differently-typeset board.
   */
  function mountMiniCork() {
    if (dead || mini) return;
    const rect = CORK_WIDE;
    const scale = rect[2] / CORK_INNER[2];
    if (!(scale > 0)) return;
    const wrap = el('div', 'rr-corkmini');
    attr(wrap, 'aria-hidden', 'true');
    /* `rr-cork` so it is laid out and inked exactly like the wall it is a
     * picture of; `rr-cork-mini` so nothing - a sheet, a suite - can mistake
     * the picture for the wall. */
    const inner = el('div', 'rr-cork rr-cork-mini arc-cork-wall');
    try {
      inner.style.width = CORK_INNER[2] + 'px';
      inner.style.height = Math.round(rect[3] / scale) + 'px';
      inner.style.transform = 'scale(' + scale.toFixed(4) + ')';
    } catch (e) { /* the node double has no style box - the wall still mounts */ }
    wrap.appendChild(inner);
    miniOff = scene.mountInView('wide', wrap, rect);
    mini = mountNotices(inner, {
      daySeed: c.daySeed,
      preview: true,
      fit: corkFit(),
      /* The board has a bottom rail painted into it: a sheet sliced flat along
       * that rail is a rendering fault, a shorter wall is a wall. */
      wholeRows: true,
      log: log,
    });
    if (!mini) log('records room: the miniature wall could not be pinned');
  }

  /* ------------------------------------------------------------ THE BOARD */

  /**
   * THE FIT BOTH WALLS SHARE. `boxH` is the close-up cork's height in stage px
   * and `scale` is the STAGE's, read live off the chassis (a window resize
   * moves it, and corkboard.js re-runs the fit on its own resize listener).
   * The miniature must never measure its own host for this: it carries a
   * second scale of its own, which would answer a smaller number and print the
   * thumbnail in bigger type than the board it is a picture of.
   */
  function corkFit() {
    return {
      boxH: CORK_INNER[3],
      scale: function () {
        try { return scene.scale ? scene.scale() : 1; } catch (e) { return 1; }
      },
    };
  }

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
      /* EVERY SHEET COMES OFF THE WALL. The close-up is a painting scaled to
       * the window, so on a phone its body copy lands near seven pixels and
       * the bottom row hangs out of the frame; one press lifts the paper to
       * full size over the window (corkboard.js's READER). The wall is still
       * read at a glance - this is what a glance you cannot resolve does next. */
      readable: true,
      /* The board's own box, and the stage's own scale - the miniature above
       * is handed this same pair on purpose. */
      fit: corkFit(),
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
    /* A SHEET IN THE HAND IS THE THING ONE PRESS AGO, so it folds before the
     * spotlight and before the chassis - inward-out, all the way down. */
    if (dismissReader()) return true;
    if (dismissSpotlight()) return true;
    try { return !!scene.escapeStep(); } catch (e) { return false; }
  }

  /** The reader's own rung, kept separate so the shell can name it. */
  function dismissReader() {
    try { return !!closeNoticeReader(); } catch (e) { return false; }
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
    /* The miniature is scenery and goes with the room; its unmount is the
     * chassis's, its sheets are corkboard.js's, and a reader left in the
     * player's hand is a <body>-level node that would outlive both. */
    if (mini) { try { mini.destroy(); } catch (e) { /* noop */ } mini = null; }
    if (miniOff) { try { miniOff(); } catch (e) { /* noop */ } miniOff = null; }
    try { closeNoticeReader(); } catch (e) { /* noop */ }
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
    fit: function () {
      let s = null;
      try { s = scene.fit(); } catch (e) { s = null; }
      /* The stage moved, so the type floor moved with it. */
      if (paper && paper.refit) { try { paper.refit(); } catch (e) { /* noop */ } }
      if (mini && mini.refit) { try { mini.refit(); } catch (e) { /* noop */ } }
      return s;
    },
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
    /** The wide shot's miniature wall, or null. */
    miniNotices: function () { return mini ? mini.notices : null; },
    /** Is a sheet in the player's hand right now? */
    readerUp: function () { try { return !!readerUp(); } catch (e) { return false; } },
    dismissReader: dismissReader,
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
