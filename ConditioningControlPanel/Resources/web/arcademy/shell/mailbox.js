/* ============================================================================
 * shell/mailbox.js - THE PHANTOM POST, paper half: the envelope and the box.
 *
 * shell/mail.js decides what may arrive and when. This file is the only thing
 * that draws any of it: a small envelope that hangs in the campus chrome, and
 * the box it opens - a wall of envelopes on the left, the letter you picked
 * lying open on the right, on paper dressed by its own letterhead.
 *
 * SIX LAWS
 *
 *  1. IT IS AN OVERLAY, NOT A SCREEN. It never touches the shell's router. It
 *     mints its own fixed layer, mounts to `document.body` by default, and is
 *     REMOVED on close rather than hidden (trap 27 / orientgate law 3), so a
 *     campus repaint underneath it can never leave two boxes stacked.
 *
 *  2. IT OWNS ONE ESC RUNG, AT THE TOP, AND ONLY WHILE IT IS UP. The player
 *     opened it one press ago, so Esc closing it first is the only answer that
 *     is not a surprise (trap 48's reasoning). The listener is capture-phase
 *     and stops there, so the shell's own ladder does not ALSO fire and walk
 *     the player off the campus on a single tap. A driver that would rather own
 *     the rung in `escapeStep()` passes `ownEscape:false` and calls
 *     `closeMailbox()` from its own ladder - both paths are supported and
 *     neither needs a change in here.
 *
 *  3. IT HOLDS NO STATE OF ITS OWN. Everything on screen is read from the
 *     engine handle it was given (`all()`, `unreadCount()`), and opening a
 *     letter calls `markRead(id)`. There is no second copy of the box in this
 *     file to drift out of step with the one that gets banked.
 *
 *  4. THE SHEET IS TABLE-DRIVEN. A letterhead's treatment is four tokens on the
 *     paper node (`data-letterhead` / `data-accent` / `data-rule` / `data-mark`
 *     / `data-paper`) and `shell/mail.css` does the rest, so a new sender is a
 *     row in mail.js's LETTERHEADS plus one selector - never a new layout, and
 *     never a colour outside the shell's palette tokens.
 *
 *  5. AUDIO GOES THROUGH THE ONE DOOR. `shell/audio.js` is the only holder of
 *     an audio node on this page (trap 18), so every cue here is an
 *     `arcademy-sfx` request on `document`, in the exact defensive shape
 *     shell/ceremonies.js set. A dropped cue is not an error.
 *
 *  6. PLAIN ELEMENTS ONLY. No innerHTML, no querySelector, no canvas, no
 *     measurement: the headless DOM double the suites drive can build every
 *     node in this file, and every optional DOM verb is guarded (trap 60).
 *
 * LEXICON. The chrome rows this file renders, all well under the 96-char
 * mod-skin cap (trap 26). The host's `NeutralLexicon` must mirror each one or a
 * mod skin cannot re-voice them (the driver's job, Wave 4):
 *
 *   mail_kicker      mail_title       mail_chip_label   mail_unread
 *   mail_all_read    mail_empty       mail_pick         mail_delivered
 *   mail_new         mail_close
 *
 * Letter BODIES are content, not chrome: they live in mail.js's catalog as
 * plain strings and deliberately do not pass through `t()`.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';
import { LETTERHEADS, DEFAULT_LETTERHEAD, dayKeyOf } from './mail.js';

/* ----------------------------------------------------------------------------
 * THE SHEET
 * `styles.css` is shell chrome and the mail box is not part of it, so this
 * ships its own file the way emi/ ships two of its own. index.html may carry
 * the <link> instead (id `arc-mail-css` - the driver's call); this injection is
 * idempotent and stands down the moment that id exists, so the two can never
 * both land. Resolved against THIS MODULE, never the document: shell modules
 * and the document can sit at different roots (campus.js's broken-logo bug).
 * -------------------------------------------------------------------------- */
const SHEET_ID = 'arc-mail-css';
const SHEET_URL = (function resolveSheet() {
  try { return new URL('./mail.css', import.meta.url).href; }
  catch (e) { return 'shell/mail.css'; }
}());

function ensureSheet() {
  try {
    if (typeof document === 'undefined' || typeof document.createElement !== 'function') return;
    if (typeof document.getElementById === 'function' && document.getElementById(SHEET_ID)) return;
    const head = document.head || document.body;
    if (!head || typeof head.appendChild !== 'function') return;
    const link = document.createElement('link');
    link.id = SHEET_ID;
    link.rel = 'stylesheet';
    link.href = SHEET_URL;
    head.appendChild(link);
  } catch (e) { /* a missing sheet costs the look, never the box */ }
}

/* ----------------------------------------------------------------------------
 * HOUSE HELPERS (the shape shell/records.js and shell/exits.js already set)
 * -------------------------------------------------------------------------- */

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

function drop(node) {
  try { if (node && typeof node.remove === 'function') node.remove(); }
  catch (e) { /* noop */ }
}

function callNum(v, fallback) {
  const raw = (typeof v === 'function') ? v() : v;
  const n = Number(raw);
  return Number.isFinite(n) ? Math.max(0, Math.round(n)) : fallback;
}

/** The letterhead row for a key, never null. */
function headOf(key) {
  return LETTERHEADS[String(key || '')] || DEFAULT_LETTERHEAD;
}

/** Dress a node with a letterhead's tokens. mail.css reads all five. */
function dressLetterhead(node, head) {
  attr(node, 'data-letterhead', head.id);
  attr(node, 'data-accent', head.accent);
  attr(node, 'data-rule', head.rule);
  attr(node, 'data-mark', head.mark);
  attr(node, 'data-paper', head.paper);
}

/* ----------------------------------------------------------------------------
 * THE ENVELOPE CHIP
 *
 * A small closed envelope, drawn in CSS off the shell's own tokens - no emoji,
 * no image, no font that has to be present. It BLINKS only while something in
 * the box is unread (the bulb cadence the exit signs and the campus marquee
 * already keep, 1.04s steps(1, end)) and sits steady otherwise, because a chip
 * that blinks at a player with nothing to read is a chip they learn to ignore.
 *
 * It is HIDDEN while the box is empty, through the `hidden` attribute and the
 * `[hidden] { display:none !important }` reset at the top of styles.css - never
 * a bare `display:` of its own (trap 27).
 *
 * @param {Element} parentEl                where the driver hangs it (the
 *                                          bell/crest cluster on the campus)
 * @param {Object} o
 * @param {Function} o.onOpen               pressed
 * @param {number|Function} o.unreadCount   unread letters (a number or a getter)
 * @param {number|Function=} o.total        letters IN the box. Pass it (usually
 *                                          `mail.all().length`) or a fully-read
 *                                          box reads as an empty one: with no
 *                                          `total` the chip falls back to the
 *                                          unread count, which hides it the
 *                                          moment the last letter is opened.
 * @param {string=} o.label                 aria label override
 * @returns {?Object} {el, update(next), destroy()} - null with nowhere to mount
 * -------------------------------------------------------------------------- */
export function mountMailChip(parentEl, o) {
  const s = o || {};
  if (!parentEl || typeof parentEl.appendChild !== 'function') return null;
  if (typeof document === 'undefined' || typeof document.createElement !== 'function') return null;
  ensureSheet();

  const root = el('button', 'arc-mailchip');
  root.type = 'button';
  const env = el('span', 'arc-mailchip-env');
  attr(env, 'aria-hidden', 'true');
  env.appendChild(el('i', 'arc-mailchip-flap'));
  root.appendChild(env);
  const pip = el('span', 'arc-mailchip-pip');
  attr(pip, 'aria-hidden', 'true');
  root.appendChild(pip);

  root.addEventListener('click', () => {
    sfx('flap', 0.22);
    try { if (typeof s.onOpen === 'function') s.onOpen(); }
    catch (e) { /* a bad handler must not brick the chrome */ }
  });

  /**
   * Repaint the counts.
   * @param {Object|number=} next  {unread, total}, or just an unread number.
   *   Anything omitted keeps the getter (or the value) it was mounted with.
   */
  function update(next) {
    const n = (next && typeof next === 'object') ? next : null;
    const unread = n && n.unread != null ? callNum(n.unread, 0) : callNum(
      (typeof next === 'number') ? next : s.unreadCount, 0
    );
    const total = n && n.total != null ? callNum(n.total, unread)
      : (s.total != null ? callNum(s.total, unread) : unread);

    attr(root, 'data-state', unread > 0 ? 'new' : 'idle');
    pip.textContent = unread > 0 ? String(unread) : '';
    attr(pip, 'data-on', unread > 0 ? 'yes' : 'no');
    const base = s.label || t('mail_chip_label', 'Mail');
    attr(root, 'aria-label', unread > 0
      ? base + ' - ' + unread + ' ' + t('mail_unread', 'unread')
      : base);
    attr(root, 'title', base);
    // An empty box has no chip at all: nothing has ever arrived, so there is
    // nothing to advertise (and no lie about a room that is not open yet).
    root.hidden = total <= 0;
  }

  update();
  parentEl.appendChild(root);
  return {
    el: root,
    update,
    destroy() { drop(root); },
  };
}

/* ----------------------------------------------------------------------------
 * THE BOX
 * -------------------------------------------------------------------------- */

/** The one live overlay. A second open is a no-op on the one already up. */
let live = null;

/** LOCAL date line under a letter (trap 8: the dates on this page are local). */
function whenLine(ms) {
  return ms ? dayKeyOf(ms) : '';
}

/** The paper. A fresh node every time, so the unfold keyframe fires on its own. */
function letterPaper(letter) {
  const head = headOf(letter.letterhead);
  const paper = el('article', 'arc-letter');
  dressLetterhead(paper, head);

  const top = el('header', 'arc-letter-head');
  const mark = el('span', 'arc-letter-mark');
  attr(mark, 'aria-hidden', 'true');
  top.appendChild(mark);
  top.appendChild(el('p', 'arc-letter-from', letter.from || ''));
  top.appendChild(el('h3', 'arc-letter-line', letter.heading || ''));
  const rule = el('i', 'arc-letter-rule');
  attr(rule, 'aria-hidden', 'true');
  top.appendChild(rule);
  paper.appendChild(top);

  const body = el('div', 'arc-letter-body');
  const paras = Array.isArray(letter.body) ? letter.body : [];
  for (let i = 0; i < paras.length; i += 1) body.appendChild(el('p', null, String(paras[i] || '')));
  paper.appendChild(body);

  const foot = el('footer', 'arc-letter-foot');
  foot.appendChild(el('span', 'arc-letter-when',
    t('mail_delivered', 'Delivered') + ' ' + whenLine(letter.deliveredAt)));
  paper.appendChild(foot);
  return paper;
}

/**
 * Open the box.
 *
 * @param {Object} o
 * @param {Object} o.mail             the shell/mail.js handle (all / markRead /
 *                                    unreadCount). Required.
 * @param {Element=} o.mount          default `document.body`
 * @param {string=} o.openId          a letter to open on arrival (default: the
 *                                    newest unread, else the newest)
 * @param {boolean=} o.ownEscape      default true - see law 2
 * @param {Function=} o.onClose       called once, whatever closed it
 * @param {Function=} o.onRead        (id) - fired when a letter is opened for
 *                                    the FIRST time, so the driver can count it
 * @param {Function=} o.log
 * @returns {?Object} {root, close(), isOpen()} - null with nothing to draw
 */
export function openMailbox(o) {
  const s = o || {};
  const say = typeof s.log === 'function' ? s.log : () => {};
  const mail = s.mail;
  if (!mail || typeof mail.all !== 'function') return null;
  if (typeof document === 'undefined' || typeof document.createElement !== 'function') return null;
  const mount = s.mount || document.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;
  if (live) return live;                     // one box, ever
  ensureSheet();

  const doc = document;
  const opener = (doc.activeElement && typeof doc.activeElement.focus === 'function')
    ? doc.activeElement : null;

  const root = el('div', 'arc-mailstage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', t('mail_title', 'The Mail Box'));

  const box = el('div', 'arc-mailbox');
  root.appendChild(box);

  /* ------------------------------- the head ---------------------------- */
  const top = el('div', 'arc-mailbox-head');
  top.appendChild(el('p', 'arc-kicker', t('mail_kicker', 'Mail')));
  top.appendChild(el('h2', 'arc-h2', t('mail_title', 'The Mail Box')));
  const tally = el('span', 'chip arc-mailbox-tally');
  top.appendChild(tally);
  box.appendChild(top);

  /* ------------------------------- the body ---------------------------- */
  const bodyRow = el('div', 'arc-mailbox-body');
  const list = el('div', 'arc-mail-list');
  attr(list, 'role', 'list');
  const stage = el('div', 'arc-mail-paper');
  bodyRow.appendChild(list);
  bodyRow.appendChild(stage);
  box.appendChild(bodyRow);

  let closed = false;
  let selected = null;

  /* THE ONE ESC RUNG (law 2). Capture phase, stopped here so the shell's own
   * ladder does not walk the player off the campus on the same press, and gone
   * from the document the instant the box is. */
  function onKey(e) {
    if (!e || closed) return;
    const k = e.key;
    if (k !== 'Escape' && k !== 'Esc') return;
    try { e.preventDefault(); e.stopPropagation(); } catch (err) { /* noop */ }
    close();
  }
  const canListen = s.ownEscape !== false && typeof doc.addEventListener === 'function';
  if (canListen) doc.addEventListener('keydown', onKey, true);

  function close() {
    if (closed) return;
    closed = true;
    if (live && live.root === root) live = null;
    if (canListen) { try { doc.removeEventListener('keydown', onKey, true); } catch (e) { /* noop */ } }
    drop(root);
    focusSoon(opener);
    try { if (typeof s.onClose === 'function') s.onClose(); }
    catch (e) { say('mailbox onClose: ' + ((e && e.message) || e)); }
  }

  /* The scrim is a way out too: a press on the ground outside the box closes
   * it, the same verb the Close sign runs. */
  root.addEventListener('click', (e) => {
    if (e && e.target === root) close();
  });

  /* ------------------------------ painting ----------------------------- */

  function paintTally(letters) {
    let unread = 0;
    try { unread = Number(mail.unreadCount && mail.unreadCount()) || 0; }
    catch (e) { unread = 0; }
    tally.textContent = unread > 0
      ? unread + ' ' + t('mail_unread', 'unread')
      : letters.length + ' ' + t('mail_all_read', 'read');
    attr(tally, 'data-state', unread > 0 ? 'new' : 'idle');
  }

  function paintPaper(letter) {
    stage.textContent = '';
    if (!letter) {
      stage.appendChild(el('p', 'arc-note arc-mail-hint',
        t('mail_pick', 'Pick an envelope to read it.')));
      return;
    }
    stage.appendChild(letterPaper(letter));
  }

  /**
   * One envelope on the wall. It carries its letterhead's tokens too, so the
   * pile reads as a pile of different papers rather than a list of rows.
   */
  function envelope(letter) {
    const item = el('button', 'arc-mail-item');
    item.type = 'button';
    attr(item, 'role', 'listitem');
    dressLetterhead(item, headOf(letter.letterhead));
    attr(item, 'data-unread', letter.unread ? 'yes' : 'no');
    if (selected === letter.id) attr(item, 'aria-current', 'true');

    const pip = el('span', 'arc-mail-pip');
    attr(pip, 'aria-hidden', 'true');
    item.appendChild(pip);

    const lines = el('span', 'arc-mail-item-lines');
    lines.appendChild(el('span', 'arc-mail-item-from', letter.from || ''));
    lines.appendChild(el('span', 'arc-mail-item-line', letter.heading || ''));
    item.appendChild(lines);

    const meta = el('span', 'arc-mail-item-meta');
    if (letter.unread) meta.appendChild(el('span', 'arc-mail-item-new', t('mail_new', 'New')));
    meta.appendChild(el('span', 'arc-mail-item-when', whenLine(letter.deliveredAt)));
    item.appendChild(meta);

    item.addEventListener('click', () => open(letter.id));
    return item;
  }

  /**
   * Open one letter. THIS is the read: the box marks it before it paints it.
   * `quiet` is the arrival path - the box already made its own sound landing on
   * the desk, and a second paper cue on the same frame reads as two letters.
   */
  function open(id, quiet) {
    let wasUnread = false;
    const before = readAll();
    for (let i = 0; i < before.length; i += 1) {
      if (before[i].id === id) { wasUnread = !!before[i].unread; break; }
    }
    selected = id;
    let minted = false;
    try { minted = !!(mail.markRead && mail.markRead(id)); }
    catch (e) { say('mailbox markRead: ' + ((e && e.message) || e)); }
    // The seal breaking is the reward beat; a letter you have already read just
    // turns over, so the two do not sound the same.
    if (!quiet) sfx(wasUnread ? 'paper' : 'flap', wasUnread ? 0.32 : 0.2);
    if (minted) {
      try { if (typeof s.onRead === 'function') s.onRead(id); }
      catch (e) { say('mailbox onRead: ' + ((e && e.message) || e)); }
    }
    render();
  }

  function readAll() {
    try {
      const a = mail.all();
      return Array.isArray(a) ? a : [];
    } catch (e) {
      say('mailbox all(): ' + ((e && e.message) || e));
      return [];
    }
  }

  function render() {
    const letters = readAll();
    list.textContent = '';
    paintTally(letters);

    if (!letters.length) {
      list.appendChild(el('p', 'arc-note arc-mail-empty',
        t('mail_empty', 'Nothing in the box yet.')));
      paintPaper(null);
      return;
    }

    // Default selection: the newest unread, else the newest thing in the box.
    if (!selected || !letters.some((l) => l.id === selected)) {
      let pick = null;
      for (let i = 0; i < letters.length; i += 1) {
        if (letters[i].unread) { pick = letters[i].id; break; }
      }
      selected = pick || letters[0].id;
    }

    let showing = null;
    for (let i = 0; i < letters.length; i += 1) {
      list.appendChild(envelope(letters[i]));
      if (letters[i].id === selected) showing = letters[i];
    }
    paintPaper(showing);
  }

  /* ------------------------------- the exit ---------------------------- */
  const closeBtn = el('button', 'btn primary', t('mail_close', 'Close'));
  closeBtn.type = 'button';
  closeBtn.addEventListener('click', close);
  signExit(closeBtn, { dir: 'back' });
  // `arc-exitbar-card` is the house treatment for a bar that lives inside a
  // card's own box rather than at the bottom of a screen (styles.css).
  const bar = exitBar([closeBtn]);
  bar.className = (String(bar.className || '') + ' arc-exitbar-card').trim();
  box.appendChild(bar);

  /* The box lands on the desk. One cue, on the open - the letters make their
   * own sounds from here. */
  if (typeof s.openId === 'string' && s.openId) selected = s.openId;
  render();
  mount.appendChild(root);
  sfx('paper', 0.25);

  /* THE FIRST THING UNDER THE HAND is the pile, not the way out: a player who
   * opened the box came to read something. An empty box focuses Close, which is
   * then the only thing in it worth pressing. */
  const first = (list.children && list.children.length && list.children[0]) || null;
  focusSoon((first && typeof first.focus === 'function') ? first : closeBtn);

  // The arrival selection is ON THE PAPER, so it has been read: mark it, once,
  // without a second cue over the one the box just made.
  if (selected) {
    const shown = readAll();
    for (let i = 0; i < shown.length; i += 1) {
      if (shown[i].id === selected && shown[i].unread) { open(selected, true); break; }
    }
  }

  live = { root, close, isOpen() { return !closed; } };
  return live;
}

/** Close whatever box is up. Safe to call when there is none. */
export function closeMailbox() {
  if (live && typeof live.close === 'function') live.close();
}

/** Is a box up right now? The driver's Esc-ladder test, if it owns the rung. */
export function isMailboxOpen() {
  return !!(live && live.isOpen && live.isOpen());
}

export default openMailbox;
