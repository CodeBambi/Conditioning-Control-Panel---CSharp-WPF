/* ============================================================================
 * shell/corkboard.js - THE NOTICEBOARD (PHANTOM POST, agent M2).
 *
 * A cork panel in the hall with paper pinned to it. Five or six notices exist;
 * only a handful are up on any given night, and which ones is a function of the
 * DAY, not of a roll - so the board changes on a slow clock and two players on
 * the same night are looking at the same wall.
 *
 * It is an OVERLAY, not a screen: it never touches the shell's router, it mints
 * its own fixed stage, and it comes off by REMOVAL rather than by the hidden
 * attribute (trap 27). The pattern is shell/annexreveal.js's - module-local
 * stage, one dismiss funnel, every timer owned - with the reading furniture of
 * shell/records.js (kicker / h1 / lede / exit bar).
 *
 * EVERY STRING IN THE TABLE BELOW IS A PLACEHOLDER. The real notices arrive in
 * a separate writing pass; nothing here invents a voice, a name or a joke. What
 * IS finished is the shape: six rows, three kinds, two of them permanently
 * pinned, four rotating through the remaining slots.
 *
 * ---------------------------------------------------------------------------
 * STATE-NEEDS  (the driver's Wave 4 - this file persists NOTHING itself)
 * ---------------------------------------------------------------------------
 * Everything below is read from and written to an INJECTED plain object handed
 * in as `state`, with an injected `save(state)` callback. This module never
 * imports core/store.js, never posts a meta-command and never touches
 * localStorage. Hand it `{}` and it degrades to a board that forgets.
 *
 *   state.notices        { <noticeId>: { seenAt: string|null } }
 *                        One row per notice the player has actually had up on
 *                        the wall. `seenAt` is a LOCAL 'yyyy-mm-dd' (trap 8:
 *                        every date the player is shown on this page is local).
 *                        Written when the board is opened, for every notice
 *                        that is pinned up in that visit.
 *
 *   state.lastPinDay     string|null. The UTC day seed of the last set the
 *                        player actually SAW pinned up. It is not what selects
 *                        the set (the seed does that, purely) - it is how the
 *                        prop knows to wear its "something new" dot without
 *                        re-reading every row.
 *
 *   state.openedAt       string|null. LOCAL 'yyyy-mm-dd' of the last visit.
 *                        The prop's calm/new treatment hangs off it.
 *
 *   state.visits         number. Plain counter, incremented once per open.
 *
 * The counters the driver was asked to own (`boardSeen` and its family) are
 * NOT written here: this module reports what it saw through `onOpen` and
 * through the state object above, and the driver decides what a counter means.
 *
 * ---------------------------------------------------------------------------
 * WIRING THE DRIVER STILL OWNS
 * ---------------------------------------------------------------------------
 *   - Esc. House law (shell/exits.js header): nothing outside shell.js handles
 *     Esc by itself, and a modal the player opened one press ago gets ONE rung
 *     at the TOP of escapeStep (trap 48). So this overlay ships `close()` and
 *     binds NOTHING by default. `openCorkboard({ bindEscape: true })` exists
 *     for the standalone/demo case and is off in the shell.
 *   - Where the prop sits on the campus, and what it is appended to.
 *   - The lexicon rows. Every chrome string goes through `t(key, fallback)`,
 *     so an unmirrored key renders its English fallback (trap 15) until the
 *     host's NeutralLexicon grows the `board_*` family.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { makeRng, makeTaggedRoll, shuffled } from '../core/rng.js';
import { exitBar, sign as signExit } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE SHEET
 * A real file (shell/corkboard.css), linked once and lazily, resolved against
 * THIS MODULE rather than the document - shell modules and the document can sit
 * at different roots (the campus logo bug, campus.js:320). Injecting the link
 * from here rather than growing a line in index.html is what keeps this wave to
 * new files only.
 * -------------------------------------------------------------------------- */

export const STYLE_ID = 'arc-corkboard-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./corkboard.css', import.meta.url).href; }
  catch (e) { return 'shell/corkboard.css'; }
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
 * THE TABLE
 * `kind` drives the paper treatment and the pin colour, nothing mechanical.
 * `pinned` is "this one is always up" - a permanent fixture of the wall, which
 * is what stops the rotation from ever handing the player a blank board.
 * -------------------------------------------------------------------------- */

export const NOTICE_KINDS = Object.freeze(['notice', 'flyer', 'minutes']);

export const NOTICES = Object.freeze([
  Object.freeze({
    id: 'n_house_rules',
    kind: 'notice',
    pinned: true,
    title: 'PLACEHOLDER: the standing notice',
    body: 'PLACEHOLDER: the one sheet that is always up, so the wall is never'
      + ' empty. Two or three lines of plain hall copy go here, pinned flat and'
      + ' read a hundred times without ever being read once.',
  }),
  Object.freeze({
    id: 'n_lost_property',
    kind: 'notice',
    pinned: true,
    title: 'PLACEHOLDER: the second standing notice',
    body: 'PLACEHOLDER: the other permanent sheet. Short, dull on purpose, and'
      + ' curling at one corner from having been up the longest.',
  }),
  Object.freeze({
    id: 'f_club_night',
    kind: 'flyer',
    pinned: false,
    title: 'PLACEHOLDER: a flyer with tear-off tabs',
    body: 'PLACEHOLDER: bright paper, too many exclamation marks in the real'
      + ' copy, and a row of little tabs along the bottom that somebody has'
      + ' already taken two of.',
  }),
  Object.freeze({
    id: 'f_ride_share',
    kind: 'flyer',
    pinned: false,
    title: 'PLACEHOLDER: a second flyer, hand lettered',
    body: 'PLACEHOLDER: pinned crooked over the corner of the one underneath it,'
      + ' which is how a wall like this ends up three sheets deep.',
  }),
  Object.freeze({
    id: 'm_committee_a',
    kind: 'minutes',
    pinned: false,
    title: 'PLACEHOLDER: minutes of a meeting',
    body: 'PLACEHOLDER: typed, stapled, numbered in the margin. Item one was'
      + ' carried. Item two was held over. Item three is where the real copy'
      + ' will do its quiet work.',
  }),
  Object.freeze({
    id: 'm_committee_b',
    kind: 'minutes',
    pinned: false,
    title: 'PLACEHOLDER: minutes of a later meeting',
    body: 'PLACEHOLDER: the same shape as the sheet above it and a fortnight'
      + ' further on, so a player who reads both notices what moved.',
  }),
]);

/** How many sheets the wall holds at once. Pinned rows always make the cut. */
export const BOARD_SLOTS = 4;

/* ----------------------------------------------------------------------------
 * THE ROTATION
 * Deterministic by DAY, never by Math.random - the same law core/timetable.js
 * runs the night's four classes under. The seed is a UTC day string, because
 * UTC seeds CONTENT and local dates roll attendance (trap 8), and the set of
 * notices on a wall is content.
 * -------------------------------------------------------------------------- */

/** 'yyyy-mm-dd' in UTC. The default seed when the shell has not handed one in. */
export function utcDaySeed(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getUTCFullYear();
  const m = String(d.getUTCMonth() + 1).padStart(2, '0');
  const day = String(d.getUTCDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

/** 'yyyy-mm-dd' in LOCAL time. What a date STAMP on this page always is. */
export function localDay(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

/**
 * The night's wall. PURE: same seed in, same sheets out, for ever.
 *
 * Two properties worth keeping when this is edited: the pinned rows are always
 * present (so the wall is never blank), and the answer is returned in CATALOG
 * order rather than in draw order - the membership rotates, the layout does
 * not, so a returning player reads the wall the same way twice.
 *
 * @param {string} daySeed
 * @param {number=} slots
 * @returns {Array} the subset of NOTICES that is up
 */
export function pickNotices(daySeed, slots) {
  const seed = String(daySeed == null ? '' : daySeed);
  const want = Math.max(1, Math.min(NOTICES.length,
    Math.round(Number(slots) || BOARD_SLOTS)));
  const pinned = NOTICES.filter((n) => n.pinned);
  const loose = NOTICES.filter((n) => !n.pinned);
  const room = Math.max(0, want - pinned.length);
  const drawn = shuffled(loose, makeRng(seed + '|board-rota')).slice(0, room);
  const keep = Object.create(null);
  for (let i = 0; i < drawn.length; i += 1) keep[drawn[i].id] = true;
  return NOTICES.filter((n) => n.pinned || keep[n.id]);
}

/**
 * How a sheet HANGS. Also seeded, also by day, so the wall is not re-arranged
 * under a player who reopens it, and a screenshot taken twice matches itself.
 * @returns {{rot:number, pinX:number, lift:number, torn:boolean}}
 */
export function pinGeometry(daySeed, noticeId) {
  const roll = makeTaggedRoll(String(daySeed == null ? '' : daySeed) + '|board-pin');
  const id = String(noticeId == null ? '' : noticeId);
  const rot = (roll(id + ':rot') * 2 - 1) * 3.1;      // -3.1deg .. +3.1deg
  const pinX = 30 + roll(id + ':pin') * 40;            // 30% .. 70% across
  const lift = roll(id + ':lift') * 6;                 // 0 .. 6px of hang
  const torn = roll(id + ':torn') > 0.78;              // a corner gone, sometimes
  return { rot: rot, pinX: pinX, lift: lift, torn: torn };
}

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

function styleVar(node, name, value) {
  try { if (node && node.style && typeof node.style.setProperty === 'function') node.style.setProperty(name, value); }
  catch (e) { /* noop */ }
}

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/**
 * One cue through the one door (trap 18): shell/audio.js holds the only audio
 * node on the page, so this is a REQUEST on `document` and never a sound.
 * Copied shape from shell/records.js sfx(). A dropped cue is not an error.
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

function kindLabel(kind) {
  if (kind === 'flyer') return t('board_kind_flyer', 'Flyer');
  if (kind === 'minutes') return t('board_kind_minutes', 'Minutes');
  return t('board_kind_notice', 'Notice');
}

/* ----------------------------------------------------------------------------
 * THE INJECTED HALF
 * `state` + `save` are the whole persistence story (see STATE-NEEDS). They can
 * be handed to initCorkboard once, or per-open; per-open wins.
 * -------------------------------------------------------------------------- */

const deps = {
  state: null,
  save: null,
  daySeed: null,
  mount: null,
  log: null,
};

/**
 * Hand the module its injected persistence and defaults. Every field optional.
 * @param {{state?:Object, save?:Function, daySeed?:string, mount?:Object, log?:Function}} opts
 */
export function initCorkboard(opts) {
  const o = opts || {};
  if (o.state && typeof o.state === 'object') deps.state = o.state;
  if (typeof o.save === 'function') deps.save = o.save;
  if (o.daySeed != null) deps.daySeed = String(o.daySeed);
  if (o.mount) deps.mount = o.mount;
  if (typeof o.log === 'function') deps.log = o.log;
  return deps.state;
}

/** The injected object, or a throwaway. Test seam, and the empty-state answer. */
function stateOf(override) {
  const s = (override && typeof override === 'object') ? override
    : (deps.state && typeof deps.state === 'object') ? deps.state : {};
  if (!s.notices || typeof s.notices !== 'object') s.notices = {};
  return s;
}

function persist(s, save) {
  const fn = (typeof save === 'function') ? save : deps.save;
  if (typeof fn !== 'function') return;
  try { fn(s); }
  catch (e) { if (deps.log) { try { deps.log('corkboard save: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
}

/** Is anything on tonight's wall unread? Drives the prop's quiet marker. */
export function hasUnread(daySeed, override) {
  const s = stateOf(override);
  const up = pickNotices(daySeed || deps.daySeed || utcDaySeed());
  for (let i = 0; i < up.length; i += 1) {
    const row = s.notices[up[i].id];
    if (!row || !row.seenAt) return true;
  }
  return false;
}

/* ----------------------------------------------------------------------------
 * THE OVERLAY
 * -------------------------------------------------------------------------- */

let live = null;   // the one open board, or null. Two walls is a bug, not a feature.

/**
 * Open the noticeboard.
 *
 * @param {Object=} opts
 * @param {Object=} opts.mount          where to append (default document.body)
 * @param {string=} opts.daySeed        UTC day string; defaults to today
 * @param {Object=} opts.state          injected persistence (see STATE-NEEDS)
 * @param {Function=} opts.save         save(state) callback
 * @param {number=} opts.slots          how many sheets are up (default 4)
 * @param {boolean=} opts.bindEscape    self-bind Esc (default FALSE - the shell
 *                                      owns the ladder; this is for demos)
 * @param {Function=} opts.onClose      called once, after the stage is gone
 * @param {Function=} opts.onRead       onRead(noticeId) per sheet marked read
 * @returns {?Object} {root, close(), destroy(), notices} - null with no DOM
 */
export function openCorkboard(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = o.mount || deps.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  // ONE WALL. A second open is the first one raised, not a second stage.
  if (live && !live.closed) { focusSoon(live.firstButton); return live.handle; }

  ensureStyles(doc);

  const seed = String(o.daySeed != null ? o.daySeed : (deps.daySeed || utcDaySeed()));
  const s = stateOf(o.state);
  const up = pickNotices(seed, o.slots);
  const today = localDay();

  const root = el('div', 'arc-corkstage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', t('board_title', 'Noticeboard'));

  const board = el('div', 'arc-corkboard');
  const frame = el('div', 'arc-cork-frame');
  board.appendChild(frame);

  /* ------------------------------ the head ----------------------------- */
  const head = el('div', 'arc-cork-head');
  head.appendChild(el('p', 'arc-kicker', t('board_kicker', 'Pinned up')));
  head.appendChild(el('h1', 'arc-h1 arc-cork-title', t('board_title', 'Noticeboard')));
  head.appendChild(el('p', 'arc-lede arc-cork-lede', t('board_lede',
    'What is up on the wall tonight. Some of it stays. Most of it does not.')));
  frame.appendChild(head);

  /* ------------------------------ the wall ----------------------------- */
  const wall = el('div', 'arc-cork-wall');
  attr(wall, 'role', 'list');
  frame.appendChild(wall);

  if (!up.length) {
    // A wall with nothing on it is a real state (a table trimmed to nothing),
    // and saying so is cheaper than an empty frame reading as a broken screen.
    wall.appendChild(el('p', 'arc-note arc-cork-empty', t('board_empty',
      'Nothing pinned up tonight.')));
  }

  let firstButton = null;

  for (let i = 0; i < up.length; i += 1) {
    const notice = up[i];
    const geom = pinGeometry(seed, notice.id);
    const seenBefore = !!(s.notices[notice.id] && s.notices[notice.id].seenAt);

    /* The kind rides the SLOT as well as the sheet: the pin is the slot's
     * child now (see below), so the pin hue has to be reachable from here. */
    const slot = el('div', 'arc-cork-slot kind-' + notice.kind);
    attr(slot, 'role', 'listitem');
    styleVar(slot, '--rot', geom.rot.toFixed(2) + 'deg');
    styleVar(slot, '--lift', geom.lift.toFixed(1) + 'px');
    styleVar(slot, '--pin-x', geom.pinX.toFixed(1) + '%');

    const sheet = el('article', 'arc-corknote kind-' + notice.kind
      + (geom.torn ? ' is-torn' : '')
      + (seenBefore ? ' is-read' : ' is-fresh'));

    const tag = el('span', 'arc-cork-kind', kindLabel(notice.kind).toUpperCase());
    sheet.appendChild(tag);

    sheet.appendChild(el('h2', 'arc-cork-notetitle', String(notice.title || '')));
    sheet.appendChild(el('p', 'arc-cork-notebody', String(notice.body || '')));

    // A flyer is a flyer because somebody can take a tab off the bottom of it.
    if (notice.kind === 'flyer') {
      const tabs = el('div', 'arc-cork-tabs');
      attr(tabs, 'aria-hidden', 'true');
      for (let k = 0; k < 7; k += 1) tabs.appendChild(el('i', 'arc-cork-tab' + (k < 2 ? ' gone' : '')));
      sheet.appendChild(tabs);
    }

    slot.appendChild(sheet);
    /* THE PIN GOES THROUGH THE SLOT, NOT THROUGH THE SHEET. A torn sheet is a
     * clip-path, and clip-path clips its children too - a pin parented to the
     * paper came out as a half circle on every torn notice (caught in the
     * Chromium pass, not by the node run). */
    const pin = el('i', 'arc-cork-pin');
    attr(pin, 'aria-hidden', 'true');
    slot.appendChild(pin);
    wall.appendChild(slot);
    if (!firstButton) firstButton = sheet;

    /* READING THE WALL IS OPENING IT. There is no per-sheet open verb - a
     * corkboard is read at a glance, and a click-to-expand on a paragraph of
     * copy would be a door with nothing behind it. So the visit marks every
     * sheet it actually pinned up, once. */
    if (!seenBefore) {
      s.notices[notice.id] = { seenAt: today };
      try { if (typeof o.onRead === 'function') o.onRead(notice.id); }
      catch (e) { if (deps.log) { try { deps.log('corkboard onRead: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    }
  }

  /* THE SLOW CLOCK, SAID OUT LOUD. A board that changes silently reads as a
   * board that forgot what was on it. */
  frame.appendChild(el('p', 'arc-note arc-cork-foot', t('board_rotates',
    'The wall gets sorted through most days. What is pinned flat stays put.')));

  /* ------------------------------ the way out --------------------------- */
  let closed = false;
  let escBound = false;

  function onKey(e) {
    if (!e) return;
    if (e.key !== 'Escape' && e.key !== 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
    close();
  }

  function close() {
    if (closed) return;
    closed = true;
    if (escBound) {
      try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ }
      escBound = false;
    }
    try { root.remove(); } catch (e) { /* noop */ }
    if (live && live.handle === handle) live = null;
    sfx('paper', 0.18, { pitch: 0.92 });
    try { if (typeof o.onClose === 'function') o.onClose(); }
    catch (e) { if (deps.log) { try { deps.log('corkboard onClose: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  const back = el('button', 'btn primary arc-cork-back', t('back', 'Back'));
  back.type = 'button';
  back.addEventListener('click', close);
  signExit(back, { dir: 'back' });
  frame.appendChild(exitBar([back]));

  /* THE STAGE IS A DOOR TOO. Clicking the dusk outside the board closes it -
   * the board is read-only, so a stray press costs the player nothing. The
   * test is on the target being the stage ITSELF, so a press that lands on a
   * sheet and drags off it never counts. */
  root.addEventListener('click', (e) => {
    if (e && e.target === root) close();
  });

  root.appendChild(board);
  mount.appendChild(root);

  if (o.bindEscape && typeof doc.addEventListener === 'function') {
    doc.addEventListener('keydown', onKey, true);
    escBound = true;
  }

  /* THE VISIT, BANKED. One write per open, at the end, never per sheet. */
  s.lastPinDay = seed;
  s.openedAt = today;
  s.visits = Math.max(0, Math.round(Number(s.visits) || 0)) + 1;
  persist(s, o.save);

  sfx('paper', 0.25);
  focusSoon(back);

  const handle = {
    root: root,
    notices: up,
    daySeed: seed,
    close: close,
    destroy: close,
    get closed() { return closed; },
  };
  live = { handle: handle, firstButton: back, get closed() { return closed; } };
  return handle;
}

/** The open board, or null. Test seam and the driver's re-entry guard. */
export function currentCorkboard() {
  return (live && !live.closed) ? live.handle : null;
}

/* ----------------------------------------------------------------------------
 * THE PROP
 * A small board hung on a wall somewhere on the campus. It is a BUTTON, it says
 * what it is, and it wears one quiet marker when tonight's wall holds a sheet
 * this player has never had up. No pulse, no badge count, no bell: the campus
 * is already a busy picture and a noticeboard that shouts is not a noticeboard.
 *
 * POSITIONING IS THE DRIVER'S. The prop paints at `--bp-x` / `--bp-y` (percent
 * of the parent, absolute), which the driver overrides on the returned element
 * or from campus CSS. It defaults to somewhere sane rather than to 0,0 so a
 * mount with no placement is still visible on a screenshot.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} parentEl
 * @param {Object=} opts
 * @param {Function=} opts.onOpen   called on click (the driver calls openCorkboard)
 * @param {string=} opts.daySeed
 * @param {Object=} opts.state
 * @param {string=} opts.label
 * @returns {?Object} {el, root, refresh(), destroy()}
 */
export function mountBoardProp(parentEl, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  if (!parentEl || typeof parentEl.appendChild !== 'function') return null;

  ensureStyles(doc);

  const btn = el('button', 'arc-boardprop');
  btn.type = 'button';
  const label = o.label || t('board_prop_label', 'Noticeboard');
  attr(btn, 'aria-label', label);
  attr(btn, 'title', label);

  const cork = el('span', 'arc-boardprop-cork');
  attr(cork, 'aria-hidden', 'true');
  // Three slips of paper, fixed angles: the prop is scenery, and scenery that
  // re-arranges itself every repaint reads as a glitch.
  const slips = [[-4.5, 8, 12], [2.8, 46, 20], [-1.6, 70, 9]];
  for (let i = 0; i < slips.length; i += 1) {
    const slip = el('i', 'arc-boardprop-slip');
    styleVar(slip, '--r', slips[i][0] + 'deg');
    styleVar(slip, '--l', slips[i][1] + '%');
    styleVar(slip, '--tp', slips[i][2] + '%');
    cork.appendChild(slip);
  }
  btn.appendChild(cork);

  const cap = el('span', 'arc-boardprop-label', label.toUpperCase());
  btn.appendChild(cap);

  const dot = el('i', 'arc-boardprop-new');
  attr(dot, 'aria-hidden', 'true');
  btn.appendChild(dot);

  function refresh(daySeed) {
    const seed = daySeed != null ? String(daySeed)
      : (o.daySeed != null ? String(o.daySeed) : (deps.daySeed || utcDaySeed()));
    let fresh = false;
    try { fresh = hasUnread(seed, o.state); } catch (e) { fresh = false; }
    try { btn.classList.toggle('has-new', !!fresh); } catch (e) { /* noop */ }
    return fresh;
  }

  btn.addEventListener('click', () => {
    sfx('flap', 0.2);
    try { if (typeof o.onOpen === 'function') o.onOpen(); }
    catch (e) { if (deps.log) { try { deps.log('board prop onOpen: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    refresh();
  });

  refresh();
  parentEl.appendChild(btn);

  return {
    el: btn,
    root: btn,
    refresh: refresh,
    destroy() { try { btn.remove(); } catch (e) { /* noop */ } },
  };
}

export default openCorkboard;
