/* ============================================================================
 * shell/deskbook.js - THE BOOK ON THE DESK.
 *
 * The big open volume in the Records Office, read as a two-page spread over the
 * painted plate: the left page and the right page are two elements the room
 * pins to the rects the art was measured at (shell/recordsroom.js's LEDGER),
 * and everything between them - the chapters, the tabs down the right edge, the
 * page arrows, the turn - lives here.
 *
 * THREE THINGS IT IS NOT
 *  - it is not a screen. It mints no stage, binds no Esc, and knows nothing
 *    about the router. The room hands it two rects and takes it away again.
 *  - it is not LEXICON COPY. The prose in a book is prose: it does not go
 *    through `t()`, because trap 26 caps a mod-skinnable row at 96 characters
 *    and a paragraph is not a label. Only the chrome is keyed - the three
 *    chapter tabs and the two page arrows - and those are short by nature.
 *  - it is not WRITTEN YET. `BOOK` below is a PLACEHOLDER table on purpose: the
 *    shape the owner's prose lands in, with rows that say out loud that they
 *    are rows. Nobody but the owner writes the school's story; a filled-in
 *    placeholder is worse than an obvious one, because it ships.
 *
 * THE TURN. A page turn is one half-page rotating on the spine (CSS, ~500ms),
 * and the spread underneath repaints at the MIDPOINT so the new pages are
 * revealed by the leaf rather than popping in behind it. `.arc-reduced` cuts
 * the leaf entirely and the spread simply changes - the decoration law, and the
 * page you asked for is never the thing that is lost.
 *
 * THE KEYS. ArrowLeft / ArrowRight, on `document`, and they are a PASSIVE READ
 * with one guard: the injected `isActive()` (the room answers "the book view is
 * the live slide"). It never touches Escape - boot.js owns that key and the
 * shell owns its ladder (traps 29 / 48 / 80) - and it never calls
 * `stopPropagation`. Modifier chords are ignored, so a browser shortcut is
 * never eaten by a book nobody is looking at.
 * ==========================================================================*/

import { t as lexT } from '../core/lexicon.js';

/* ----------------------------------------------------------------------------
 * THE TABLE
 *
 * `{chapters:[{key, titleKey, title, pages:[{head, body}]}]}`. The owner's prose
 * replaces every `body` below and nothing else has to move: the spread maths,
 * the tabs and the arrows are all derived from this shape.
 *
 * TWO PROPERTIES WORTH KEEPING when the real chapters land. A chapter with an
 * EVEN page count starts the next chapter on a left-hand page, which is what
 * lets a tab jump to a spread rather than to the middle of one; and a `title`
 * is a TAB LABEL (three or four words), not a heading - the long form belongs
 * on the page.
 * -------------------------------------------------------------------------- */

export const BOOK = Object.freeze({
  chapters: Object.freeze([
    Object.freeze({
      key: 'school',
      titleKey: 'records_book_ch_school',
      title: 'The Arcademy',
      pages: Object.freeze([
        Object.freeze({ head: 'section one', body: 'page 1 of the story goes here' }),
        Object.freeze({ head: '', body: 'page 2 of the story goes here' }),
        Object.freeze({ head: 'section two', body: 'page 3 of the story goes here' }),
        Object.freeze({ head: '', body: 'page 4 of the story goes here' }),
      ]),
    }),
    Object.freeze({
      key: 'rules',
      titleKey: 'records_book_ch_rules',
      title: 'House rules',
      pages: Object.freeze([
        Object.freeze({ head: 'section one', body: 'page 5 of the story goes here' }),
        Object.freeze({ head: '', body: 'page 6 of the story goes here' }),
        Object.freeze({ head: 'section two', body: 'page 7 of the story goes here' }),
        Object.freeze({ head: '', body: 'page 8 of the story goes here' }),
      ]),
    }),
    Object.freeze({
      key: 'tips',
      titleKey: 'records_book_ch_tips',
      title: 'Tips',
      pages: Object.freeze([
        Object.freeze({ head: 'section one', body: 'page 9 of the story goes here' }),
        Object.freeze({ head: '', body: 'page 10 of the story goes here' }),
      ]),
    }),
  ]),
});

/** The leaf's whole travel. Mirrored in recordsroom.css - move one, move both. */
export const FLIP_MS = 500;
/** When the spread underneath repaints: the leaf is edge-on to the reader. */
const FLIP_MID_MS = Math.round(FLIP_MS * 0.5);

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

/** One cue through the one door (trap 18): a REQUEST on `document`, never a node. */
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

/**
 * Flatten the table into the reading order, remembering where each chapter
 * begins. PURE - handed the table so a suite can drive a fixture of its own.
 * @returns {{pages:Array, chapters:Array, spreads:number}}
 */
export function paginate(book) {
  const table = (book && Array.isArray(book.chapters)) ? book.chapters : [];
  const pages = [];
  const chapters = [];
  for (let c = 0; c < table.length; c += 1) {
    const ch = table[c] || {};
    const rows = Array.isArray(ch.pages) ? ch.pages : [];
    const at = pages.length;
    for (let p = 0; p < rows.length; p += 1) {
      pages.push({
        head: String((rows[p] && rows[p].head) || ''),
        body: String((rows[p] && rows[p].body) || ''),
        chapter: c,
        n: pages.length + 1,
      });
    }
    chapters.push({
      key: String(ch.key || ('ch' + c)),
      titleKey: ch.titleKey || null,
      title: String(ch.title || ''),
      at: at,
      /* A chapter opens on the SPREAD its first page falls on - a tab that
       * landed the reader on a right-hand page would hide half the chapter
       * behind a backwards turn. */
      spread: Math.floor(at / 2),
      pages: rows.length,
    });
  }
  return { pages: pages, chapters: chapters, spreads: Math.max(1, Math.ceil(pages.length / 2)) };
}

/* ----------------------------------------------------------------------------
 * THE BOOK
 * -------------------------------------------------------------------------- */

/**
 * createDeskBook(opts) -> {left, right, ...} or null with no DOM.
 *
 * @param {Object=} opts
 * @param {Function=} opts.t          lexicon lookup override
 * @param {Object=} opts.book         the table (default BOOK)
 * @param {number=} opts.page         the remembered SPREAD index
 * @param {Function=} opts.onPage     onPage(spread) - the room persists it
 * @param {Function=} opts.isActive   () -> is the book the live view?
 * @param {boolean=} opts.reduced     force the cut (the html class is honoured)
 * @param {Function=} opts.log
 */
export function createDeskBook(opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;

  const t = typeof o.t === 'function' ? o.t : lexT;
  const log = typeof o.log === 'function' ? o.log : function () {};
  const isActive = typeof o.isActive === 'function' ? o.isActive : function () { return true; };
  const model = paginate(o.book || BOOK);
  const reducedNow = () => !!o.reduced || htmlReduced();

  const timers = [];
  let dead = false;
  let busy = false;
  let spread = 0;

  /* THE REMEMBERED PAGE. A junk value, a value from a shorter book, a value
   * from a book that has since grown - all of them clamp into range rather
   * than opening on a blank spread. */
  spread = clampSpread(o.page);

  function clampSpread(v) {
    const n = Math.round(Number(v));
    if (!isFinite(n)) return 0;
    return Math.max(0, Math.min(model.spreads - 1, n));
  }

  function later(fn, ms) {
    const id = setTimeout(function () {
      if (dead) return;
      try { fn(); } catch (e) { log('deskbook timer threw: ' + ((e && e.message) || e)); }
    }, ms);
    timers.push(id);
    return id;
  }

  /* ------------------------------------------------------------- the leaves */

  const left = el('div', 'rdb-page rdb-left');
  attr(left, 'role', 'group');
  const right = el('div', 'rdb-page rdb-right');
  attr(right, 'role', 'group');

  const leftBody = el('div', 'rdb-leaf');
  const rightBody = el('div', 'rdb-leaf');
  left.appendChild(leftBody);
  right.appendChild(rightBody);

  /* THE TURNING LEAF. One node, parked and invisible until a turn: it sweeps
   * across the spine while the spread underneath repaints at the midpoint. It
   * is decoration end to end - `aria-hidden`, no text, no pointer events - so
   * a reduced-motion reader loses a sweep and nothing else. */
  const flip = el('i', 'rdb-flip');
  attr(flip, 'aria-hidden', 'true');
  right.appendChild(flip);

  /* --------------------------------------------------------------- the tabs */
  /* Down the RIGHT page's outer edge, the way a real book's index tabs sit -
   * one per chapter, the live one pushed proud. A tab is a jump, never a page
   * turn: it lands on the chapter's own opening spread. */
  const tabs = el('div', 'rdb-tabs');
  attr(tabs, 'role', 'tablist');
  const tabEls = [];
  for (let i = 0; i < model.chapters.length; i += 1) {
    const ch = model.chapters[i];
    const label = ch.titleKey ? String(t(ch.titleKey, ch.title)) : ch.title;
    const b = el('button', 'rdb-tab', label);
    b.type = 'button';
    attr(b, 'role', 'tab');
    attr(b, 'aria-label', label);
    attr(b, 'data-chapter', ch.key);
    /* eslint-disable-next-line no-loop-func */
    b.addEventListener('click', function () { goto(ch.spread, 'tab'); });
    tabs.appendChild(b);
    tabEls.push(b);
  }
  right.appendChild(tabs);

  /* ------------------------------------------------------------- the arrows */

  const prevLabel = String(t('records_book_prev', 'Back a page'));
  const nextLabel = String(t('records_book_next', 'Next page'));
  const prevBtn = el('button', 'rdb-arrow rdb-prev', '‹ ' + prevLabel);
  prevBtn.type = 'button';
  attr(prevBtn, 'aria-label', prevLabel);
  prevBtn.addEventListener('click', function () { prev(); });
  left.appendChild(prevBtn);

  const nextBtn = el('button', 'rdb-arrow rdb-next', nextLabel + ' ›');
  nextBtn.type = 'button';
  attr(nextBtn, 'aria-label', nextLabel);
  nextBtn.addEventListener('click', function () { next(); });
  right.appendChild(nextBtn);

  /* ---------------------------------------------------------------- paint */

  function paintSide(host, page) {
    host.textContent = '';
    if (!page) {
      /* THE BLANK VERSO. An odd page count leaves the last right-hand side
       * empty, and an empty side of a real book is empty - not a note about
       * being empty. It keeps its folio so the spread still reads as a book. */
      host.appendChild(el('p', 'rdb-blank', ''));
      return;
    }
    if (page.head) host.appendChild(el('h3', 'rdb-head', page.head));
    host.appendChild(el('p', 'rdb-body', page.body));
    host.appendChild(el('span', 'rdb-folio', String(page.n)));
  }

  function paint() {
    paintSide(leftBody, model.pages[spread * 2] || null);
    paintSide(rightBody, model.pages[spread * 2 + 1] || null);
    const chapter = (model.pages[spread * 2] || model.pages[spread * 2 + 1] || {}).chapter;
    for (let i = 0; i < tabEls.length; i += 1) {
      const on = (i === chapter);
      try { tabEls[i].classList.toggle('is-live', !!on); } catch (e) { /* noop */ }
      attr(tabEls[i], 'aria-selected', on ? 'true' : 'false');
    }
    try {
      prevBtn.disabled = spread <= 0;
      nextBtn.disabled = spread >= model.spreads - 1;
    } catch (e) { /* noop */ }
  }

  /* ---------------------------------------------------------------- turns */

  /**
   * goto(target, why) - the ONE mover. Every verb (an arrow, a key, a tab)
   * lands here so the flip, the persist and the busy latch have exactly one
   * home. Answers whether the book actually moved.
   */
  function goto(target, why) {
    if (dead || busy) return false;
    const want = clampSpread(target);
    if (want === spread) return false;
    const dir = want > spread ? 'fwd' : 'back';

    if (reducedNow()) {
      spread = want;
      paint();
      bank();
      return true;
    }

    busy = true;
    try {
      flip.className = 'rdb-flip is-' + dir;
      /* the reflow the split-flap board reads for the same reason (trap 4):
       * without it the browser coalesces the start pose and nothing turns */
      void right.offsetWidth;
      flip.classList.add('is-turning');
    } catch (e) { /* noop */ }
    sfx('paper', 0.2, { pitch: dir === 'fwd' ? 1 : 0.94 });

    /* THE REVEAL IS BEHIND THE LEAF, not in front of it. */
    later(function () { spread = want; paint(); bank(); }, FLIP_MID_MS);
    later(function () {
      busy = false;
      try { flip.className = 'rdb-flip'; } catch (e) { /* noop */ }
    }, FLIP_MS + 20);
    return true;
  }

  function next() { return goto(spread + 1, 'next'); }
  function prev() { return goto(spread - 1, 'prev'); }

  function bank() {
    if (typeof o.onPage !== 'function') return;
    try { o.onPage(spread); }
    catch (e) { log('deskbook page save: ' + ((e && e.message) || e)); }
  }

  /* ----------------------------------------------------------------- keys */

  function onKey(ev) {
    if (dead || !ev) return;
    if (ev.altKey || ev.ctrlKey || ev.metaKey) return;
    let live = false;
    try { live = !!isActive(); } catch (e) { live = false; }
    if (!live) return;
    if (ev.key === 'ArrowRight') { next(); return; }
    if (ev.key === 'ArrowLeft') { prev(); }
  }
  if (typeof doc.addEventListener === 'function') doc.addEventListener('keydown', onKey);

  paint();

  return {
    left: left,
    right: right,
    /** The reading model (test seam): pages, chapters, spread count. */
    model: model,
    next: next,
    prev: prev,
    goto: goto,
    /** Which spread is open. */
    spread: function () { return spread; },
    /** True while a leaf is in the air (the busy latch). */
    turning: function () { return busy; },
    destroy() {
      if (dead) return;
      dead = true;
      for (let i = 0; i < timers.length; i += 1) { try { clearTimeout(timers[i]); } catch (e) { /* noop */ } }
      timers.length = 0;
      if (typeof doc.removeEventListener === 'function') {
        try { doc.removeEventListener('keydown', onKey); } catch (e) { /* noop */ }
      }
      try { left.remove(); } catch (e) { /* noop */ }
      try { right.remove(); } catch (e) { /* noop */ }
    },
  };
}

export default createDeskBook;
