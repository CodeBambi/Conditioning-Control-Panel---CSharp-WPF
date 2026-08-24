/* ============================================================================
 * shell/bugle.js - THE PAPER (PHANTOM POST, agent M2).
 *
 * A small campus newspaper, read as an overlay: a masthead, a flag line, two
 * columns of set copy, a comics box, and pages you turn. It is the corkboard's
 * long-form twin - the wall says six things badly, the paper says one thing at
 * length - and it is built on the same bones: an OVERLAY, not a screen, mounted
 * on its own fixed stage, removed rather than hidden (trap 27), with the way
 * out on the sticky exit bar (trap 46).
 *
 * EVERY STRING IN THE TABLE BELOW IS A PLACEHOLDER, masthead included. The real
 * paper is written in a separate pass; what is finished here is the FURNITURE -
 * one issue, four pages, a kicker and a headline on each, and a comics slot
 * with nothing in it yet.
 *
 * THE MASTHEAD FACE is 'Arcademy Display' (styles.css --disp), which is the
 * bundled Graduate woff2 under art/fonts/ - a collegiate slab serif that
 * happens to be exactly what a small-town paper sets its own name in. No new
 * font ships with this wave and none may (trap 2: the webview is offline, a
 * font host silently falls back and burns a boot on a DNS timeout).
 *
 * ---------------------------------------------------------------------------
 * STATE-NEEDS  (the driver's Wave 4 - this file persists NOTHING itself)
 * ---------------------------------------------------------------------------
 * Read from and written to an INJECTED plain object handed in as `state`, with
 * an injected `save(state)` callback. No core/store.js import, no meta-command,
 * no localStorage. Hand it `{}` and the paper simply forgets it was read.
 *
 *   state.issues        { <issueId>: { readAt: string|null, lastPage: number } }
 *                       `readAt` is a LOCAL 'yyyy-mm-dd' (trap 8: every date
 *                       shown to the player on this page is local). `lastPage`
 *                       is a zero-based page index, clamped on read, so a
 *                       reopened issue lands where the player left it.
 *
 *   state.latestSeen    string|null. The id of the newest issue that has ever
 *                       been opened. It is what the prop's marker hangs off:
 *                       a paper with a fresh issue on it is worth crossing a
 *                       campus for, and a paper without one is scenery.
 *
 *   state.opens         number. Plain counter, incremented once per open.
 *
 * The `bugleRead` counter family stays the DRIVER'S: this module reports what
 * happened through `onRead(issueId)` / `onPage(index)` and through the state
 * object, and the driver decides what a counter means.
 *
 * ---------------------------------------------------------------------------
 * WIRING THE DRIVER STILL OWNS
 * ---------------------------------------------------------------------------
 *   - Esc. House law (shell/exits.js header): nothing outside shell.js handles
 *     Esc by itself, and a modal gets ONE rung at the TOP of escapeStep (trap
 *     48). This overlay ships `close()` and binds nothing by default;
 *     `openBugle(id, { bindEscape: true })` is the standalone/demo path.
 *   - Where the folded prop sits on the campus.
 *   - The lexicon rows. Chrome goes through `t(key, fallback)`, so an
 *     unmirrored key renders English (trap 15) until the host's NeutralLexicon
 *     grows the `bugle_*` family. The COPY in ISSUES is content and stays a
 *     plain string in the table, per the ground rules.
 * ==========================================================================*/

import { t } from '../core/lexicon.js';
import { exitBar, sign as signExit } from './exits.js';

/* ----------------------------------------------------------------------------
 * THE SHEET
 * A real file (shell/bugle.css), linked once and lazily, resolved against THIS
 * MODULE rather than the document - shell modules and the document can sit at
 * different roots (the campus logo bug, campus.js:320).
 * -------------------------------------------------------------------------- */

export const STYLE_ID = 'arc-bugle-style';

export const STYLE_HREF = (function resolveSheet() {
  try { return new URL('./bugle.css', import.meta.url).href; }
  catch (e) { return 'shell/bugle.css'; }
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
 * THE PAPER'S OWN NAME
 * Content, not chrome, so it lives in the table rather than behind t() - and it
 * is a placeholder like everything else until the writing pass lands. The
 * subtitle is the strap under the rule; the flag line is the small print
 * beside it (volume, issue, price, the usual furniture of a masthead).
 * -------------------------------------------------------------------------- */
export const MASTHEAD = Object.freeze({
  title: 'PLACEHOLDER: the paper',
  strap: 'PLACEHOLDER: the line under the name',
  flagLeft: 'PLACEHOLDER: vol. i',
  flagRight: 'PLACEHOLDER: free to take',
});

/* ----------------------------------------------------------------------------
 * THE TABLE
 * One issue. `pages[].body` accepts a string OR a list of paragraphs; a paper
 * wants paragraphs and one string is the degenerate case of a list, so the
 * renderer takes both and the shipped placeholder uses lists.
 * `comics: true` puts the comics box on that page (additive flag - a page
 * without it renders columns only).
 * -------------------------------------------------------------------------- */

export const ISSUES = Object.freeze([
  Object.freeze({
    id: 'issue_001',
    number: 1,
    headline: 'PLACEHOLDER: the front page headline',
    pages: Object.freeze([
      Object.freeze({
        kicker: 'PLACEHOLDER: front page',
        title: 'PLACEHOLDER: the front page headline',
        body: Object.freeze([
          'PLACEHOLDER: the opening paragraph, set wide enough to carry a drop'
          + ' cap and long enough that the first column has something to do'
          + ' before it hands over to the second one.',
          'PLACEHOLDER: the second paragraph, where a small paper repeats what'
          + ' the first one already said and attributes it to somebody with a'
          + ' title.',
          'PLACEHOLDER: the third paragraph, shorter, because the story has run'
          + ' out well before the column has.',
          'PLACEHOLDER: and a last line that trails off into the fold the way'
          + ' the front page of a four page paper always does.',
        ]),
      }),
      Object.freeze({
        kicker: 'PLACEHOLDER: page two',
        title: 'PLACEHOLDER: the second page piece',
        body: Object.freeze([
          'PLACEHOLDER: a quieter piece, set in the same two columns, doing the'
          + ' work of filling a page that nobody was going to read closely.',
          'PLACEHOLDER: a paragraph with a number in it, which is how a paper'
          + ' this size signals that somebody went and checked something.',
          'PLACEHOLDER: a closing line that promises more next week.',
        ]),
      }),
      Object.freeze({
        kicker: 'PLACEHOLDER: page three',
        title: 'PLACEHOLDER: the noticeboard column',
        comics: true,
        body: Object.freeze([
          'PLACEHOLDER: the short column that runs beside the funnies, made of'
          + ' three or four items that were never big enough to be a story.',
          'PLACEHOLDER: another item, one sentence long, in the same list.',
        ]),
      }),
      Object.freeze({
        kicker: 'PLACEHOLDER: the back page',
        title: 'PLACEHOLDER: the back page',
        body: Object.freeze([
          'PLACEHOLDER: the back page, which in a paper like this is mostly'
          + ' advertisements for things inside the same building.',
          'PLACEHOLDER: the last paragraph of the issue, and then the rule, and'
          + ' then the fold.',
        ]),
      }),
    ]),
  }),
]);

/** Newest issue first is the reading order; the table is authored oldest first. */
export function latestIssue() {
  return ISSUES.length ? ISSUES[ISSUES.length - 1] : null;
}

/** Look one up by id. Falls back to the latest, then to null. */
export function findIssue(issueId) {
  const id = issueId == null ? '' : String(issueId);
  for (let i = 0; i < ISSUES.length; i += 1) if (ISSUES[i].id === id) return ISSUES[i];
  return latestIssue();
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

function focusSoon(node) {
  try { if (node && typeof node.focus === 'function') node.focus(); }
  catch (e) { /* noop */ }
}

/** One cue through the one door (trap 18). Same defensive shape as records.js. */
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

/** 'yyyy-mm-dd' in LOCAL time. What a date STAMP on this page always is. */
export function localDay(when) {
  const d = (when instanceof Date) ? when : new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return y + '-' + m + '-' + day;
}

function paragraphsOf(body) {
  if (Array.isArray(body)) return body.map((p) => String(p == null ? '' : p)).filter((p) => p.length);
  const one = String(body == null ? '' : body);
  return one.length ? [one] : [];
}

/* ----------------------------------------------------------------------------
 * THE INJECTED HALF
 * -------------------------------------------------------------------------- */

const deps = {
  state: null,
  save: null,
  mount: null,
  log: null,
};

/**
 * Hand the module its injected persistence and defaults. Every field optional.
 * @param {{state?:Object, save?:Function, mount?:Object, log?:Function}} opts
 */
export function initBugle(opts) {
  const o = opts || {};
  if (o.state && typeof o.state === 'object') deps.state = o.state;
  if (typeof o.save === 'function') deps.save = o.save;
  if (o.mount) deps.mount = o.mount;
  if (typeof o.log === 'function') deps.log = o.log;
  return deps.state;
}

function stateOf(override) {
  const s = (override && typeof override === 'object') ? override
    : (deps.state && typeof deps.state === 'object') ? deps.state : {};
  if (!s.issues || typeof s.issues !== 'object') s.issues = {};
  return s;
}

function persist(s, save) {
  const fn = (typeof save === 'function') ? save : deps.save;
  if (typeof fn !== 'function') return;
  try { fn(s); }
  catch (e) { if (deps.log) { try { deps.log('bugle save: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
}

/** Is the newest issue still unopened? Drives the prop's quiet marker. */
export function hasUnreadIssue(override) {
  const s = stateOf(override);
  const latest = latestIssue();
  if (!latest) return false;
  const row = s.issues[latest.id];
  return !(row && row.readAt);
}

/* ----------------------------------------------------------------------------
 * THE OVERLAY
 * -------------------------------------------------------------------------- */

let live = null;   // the one open paper, or null

/**
 * Open the paper.
 *
 * @param {string=} issueId             defaults to the newest issue
 * @param {Object=} opts
 * @param {Object=} opts.mount          where to append (default document.body)
 * @param {Object=} opts.state          injected persistence (see STATE-NEEDS)
 * @param {Function=} opts.save         save(state) callback
 * @param {number=} opts.page           zero-based page to open on; defaults to
 *                                      the injected lastPage, then to 0
 * @param {boolean=} opts.bindEscape    self-bind Esc (default FALSE - the shell
 *                                      owns the ladder; this is for demos)
 * @param {Function=} opts.onClose      called once, after the stage is gone
 * @param {Function=} opts.onRead       onRead(issueId) on the first ever open
 * @param {Function=} opts.onPage       onPage(index, issueId) per page turn
 * @returns {?Object} {root, close(), destroy(), issue, page, goto(i)} or null
 */
export function openBugle(issueId, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  const mount = o.mount || deps.mount || doc.body;
  if (!mount || typeof mount.appendChild !== 'function') return null;

  const issue = findIssue(issueId);
  if (!issue) return null;

  // ONE PAPER. A second open is the first one raised, not a second stage.
  if (live && !live.closed) { focusSoon(live.firstButton); return live.handle; }

  ensureStyles(doc);

  const s = stateOf(o.state);
  const pages = Array.isArray(issue.pages) ? issue.pages : [];
  const lastRow = s.issues[issue.id];
  const wasRead = !!(lastRow && lastRow.readAt);
  const clampPage = (i) => Math.max(0, Math.min(Math.max(0, pages.length - 1), Math.round(Number(i) || 0)));
  let page = clampPage(o.page != null ? o.page : (lastRow ? lastRow.lastPage : 0));

  const root = el('div', 'arc-buglestage');
  attr(root, 'role', 'dialog');
  attr(root, 'aria-modal', 'true');
  attr(root, 'aria-label', String(MASTHEAD.title || ''));

  const paper = el('div', 'arc-bugle');

  /* ----------------------------- the masthead --------------------------- */
  const head = el('header', 'arc-bugle-masthead');
  const flag = el('div', 'arc-bugle-flag');
  flag.appendChild(el('span', 'arc-bugle-flagbit', String(MASTHEAD.flagLeft || '')));
  flag.appendChild(el('span', 'arc-bugle-flagbit',
    t('bugle_issue', 'Issue') + ' ' + String(issue.number == null ? '' : issue.number)));
  flag.appendChild(el('span', 'arc-bugle-flagbit', String(MASTHEAD.flagRight || '')));

  head.appendChild(el('h1', 'arc-bugle-name', String(MASTHEAD.title || '')));
  head.appendChild(el('p', 'arc-bugle-strap', String(MASTHEAD.strap || '')));
  head.appendChild(flag);
  paper.appendChild(head);

  /* ------------------------------- the tabs ----------------------------- */
  const tabs = el('nav', 'arc-bugle-tabs');
  attr(tabs, 'role', 'tablist');
  attr(tabs, 'aria-label', t('bugle_pages', 'Pages'));
  const tabButtons = [];
  for (let i = 0; i < pages.length; i += 1) {
    const tb = el('button', 'arc-bugle-tab', t('bugle_page', 'Page') + ' ' + (i + 1));
    tb.type = 'button';
    attr(tb, 'role', 'tab');
    (function bind(index) {
      tb.addEventListener('click', () => { goto(index); });
    }(i));
    tabs.appendChild(tb);
    tabButtons.push(tb);
  }
  if (pages.length > 1) paper.appendChild(tabs);

  /* ------------------------------- the page ----------------------------- */
  const sheet = el('div', 'arc-bugle-sheet');
  attr(sheet, 'role', 'tabpanel');
  paper.appendChild(sheet);

  function paintPage() {
    sheet.textContent = '';
    const p = pages[page];
    if (!p) {
      sheet.appendChild(el('p', 'arc-note arc-bugle-empty',
        t('bugle_empty', 'Nothing set for this page.')));
      return;
    }

    const lede = el('div', 'arc-bugle-lede');
    lede.appendChild(el('p', 'arc-bugle-kicker', String(p.kicker || '').toUpperCase()));
    lede.appendChild(el('h2', 'arc-bugle-headline', String(p.title || '')));
    lede.appendChild(el('div', 'arc-bugle-rule'));
    sheet.appendChild(lede);

    const cols = el('div', 'arc-bugle-cols');
    const paras = paragraphsOf(p.body);
    for (let i = 0; i < paras.length; i += 1) {
      cols.appendChild(el('p', 'arc-bugle-para' + (i === 0 ? ' is-open' : ''), paras[i]));
    }
    if (!paras.length) {
      cols.appendChild(el('p', 'arc-bugle-para', t('bugle_empty', 'Nothing set for this page.')));
    }
    sheet.appendChild(cols);

    /* THE COMICS BOX. A slot, not a picture: three empty frames and a caption
     * rule, waiting on the art pass. It is drawn rather than left blank so the
     * page composes correctly at every width before anything ships into it. */
    if (p.comics) {
      const box = el('figure', 'arc-bugle-comics');
      box.appendChild(el('figcaption', 'arc-bugle-comics-cap',
        t('bugle_comics', 'Comics').toUpperCase()));
      const strip = el('div', 'arc-bugle-strip');
      attr(strip, 'aria-hidden', 'true');
      for (let i = 0; i < 3; i += 1) {
        const panel = el('div', 'arc-bugle-panel');
        panel.appendChild(el('span', 'arc-bugle-panelnum', String(i + 1)));
        strip.appendChild(panel);
      }
      box.appendChild(strip);
      box.appendChild(el('p', 'arc-note arc-bugle-comics-note',
        'PLACEHOLDER: the strip goes in here.'));
      sheet.appendChild(box);
    }

    for (let i = 0; i < tabButtons.length; i += 1) {
      const on = (i === page);
      try { tabButtons[i].classList.toggle('is-on', on); } catch (e) { /* noop */ }
      attr(tabButtons[i], 'aria-selected', on ? 'true' : 'false');
    }
    if (prev) prev.disabled = (page <= 0);
    if (next) next.disabled = (page >= pages.length - 1);
    if (folio) folio.textContent = t('bugle_page', 'Page') + ' ' + (page + 1) + ' / ' + Math.max(1, pages.length);

    /* THE TURN IS A TRANSFORM, NEVER A BACKGROUND (trap 36). Remove, reflow,
     * re-add is the same re-trigger dance the split-flap board does (trap 4):
     * without the forced reflow the browser coalesces it and nothing moves. */
    try {
      sheet.classList.remove('is-turning');
      void sheet.offsetWidth;
      sheet.classList.add('is-turning');
    } catch (e) { /* a DOM double has no layout to force - nothing to animate */ }
  }

  /** Turn to a page. Clamped, idempotent, and it banks where the player is. */
  function goto(index) {
    const want = clampPage(index);
    if (want === page) return page;
    page = want;
    sfx('flap', 0.22);
    paintPage();
    s.issues[issue.id] = Object.assign({}, s.issues[issue.id] || {}, { lastPage: page });
    persist(s, o.save);
    try { if (typeof o.onPage === 'function') o.onPage(page, issue.id); }
    catch (e) { if (deps.log) { try { deps.log('bugle onPage: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
    return page;
  }

  /* ------------------------------- the foot ----------------------------- */
  const foot = el('div', 'arc-bugle-foot');
  const prev = el('button', 'btn ghost arc-bugle-turn', t('bugle_prev', 'Previous page'));
  prev.type = 'button';
  prev.addEventListener('click', () => { goto(page - 1); });
  const folio = el('span', 'arc-bugle-folio');
  const next = el('button', 'btn ghost arc-bugle-turn', t('bugle_next', 'Next page'));
  next.type = 'button';
  next.addEventListener('click', () => { goto(page + 1); });
  foot.appendChild(prev);
  foot.appendChild(folio);
  foot.appendChild(next);
  if (pages.length > 1) paper.appendChild(foot);

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
    catch (e) { if (deps.log) { try { deps.log('bugle onClose: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  const back = el('button', 'btn primary arc-bugle-back', t('back', 'Back'));
  back.type = 'button';
  back.addEventListener('click', close);
  signExit(back, { dir: 'back' });
  paper.appendChild(exitBar([back]));

  /* THE STAGE IS A DOOR TOO - a press on the dusk outside the paper folds it.
   * The paper is read-only, so a stray press costs nothing. */
  root.addEventListener('click', (e) => {
    if (e && e.target === root) close();
  });

  root.appendChild(paper);
  mount.appendChild(root);
  paintPage();

  if (o.bindEscape && typeof doc.addEventListener === 'function') {
    doc.addEventListener('keydown', onKey, true);
    escBound = true;
  }

  /* THE VISIT, BANKED. One write per open. `readAt` is set once and never
   * moved: it is "this issue has been picked up", not "when last touched". */
  const today = localDay();
  const row = Object.assign({ readAt: null, lastPage: 0 }, s.issues[issue.id] || {});
  if (!row.readAt) row.readAt = today;
  row.lastPage = page;
  s.issues[issue.id] = row;
  const latest = latestIssue();
  if (latest && latest.id === issue.id) s.latestSeen = issue.id;
  s.opens = Math.max(0, Math.round(Number(s.opens) || 0)) + 1;
  persist(s, o.save);

  if (!wasRead) {
    try { if (typeof o.onRead === 'function') o.onRead(issue.id); }
    catch (e) { if (deps.log) { try { deps.log('bugle onRead: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
  }

  sfx('paper', 0.3);
  focusSoon(back);

  const handle = {
    root: root,
    issue: issue,
    close: close,
    destroy: close,
    goto: goto,
    get page() { return page; },
    get closed() { return closed; },
  };
  live = { handle: handle, firstButton: back, get closed() { return closed; } };
  return handle;
}

/** The open paper, or null. Test seam and the driver's re-entry guard. */
export function currentBugle() {
  return (live && !live.closed) ? live.handle : null;
}

/* ----------------------------------------------------------------------------
 * THE PROP
 * A folded paper the driver scatters on the campus - on a bench, under a door,
 * in a rack by the hall. It is a BUTTON that reads as an object: the masthead
 * shows above the fold, the fold itself is a crease across the middle, and the
 * whole thing sits at an angle because nobody puts a newspaper down straight.
 *
 * POSITIONING IS THE DRIVER'S: --np-x / --np-y (percent of the parent,
 * absolute), overridden on the returned element or from campus CSS.
 * -------------------------------------------------------------------------- */

/**
 * @param {Object} parentEl
 * @param {Object=} opts
 * @param {Function=} opts.onOpen   called on click (the driver calls openBugle)
 * @param {Object=} opts.state
 * @param {string=} opts.label
 * @returns {?Object} {el, root, refresh(), destroy()}
 */
export function mountBugleProp(parentEl, opts) {
  const o = opts || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return null;
  if (!parentEl || typeof parentEl.appendChild !== 'function') return null;

  ensureStyles(doc);

  const btn = el('button', 'arc-bugleprop');
  btn.type = 'button';
  const label = o.label || t('bugle_prop_label', 'The paper');
  attr(btn, 'aria-label', label);
  attr(btn, 'title', label);

  const fold = el('span', 'arc-bugleprop-fold');
  attr(fold, 'aria-hidden', 'true');
  // The masthead sliver above the fold, then three ruled lines of column, then
  // the crease. Fixed geometry: scenery that re-arranges itself reads as a bug.
  fold.appendChild(el('i', 'arc-bugleprop-name'));
  const lines = el('i', 'arc-bugleprop-lines');
  fold.appendChild(lines);
  fold.appendChild(el('i', 'arc-bugleprop-crease'));
  btn.appendChild(fold);

  btn.appendChild(el('span', 'arc-bugleprop-label', label.toUpperCase()));

  const dot = el('i', 'arc-bugleprop-new');
  attr(dot, 'aria-hidden', 'true');
  btn.appendChild(dot);

  function refresh() {
    let fresh = false;
    try { fresh = hasUnreadIssue(o.state); } catch (e) { fresh = false; }
    try { btn.classList.toggle('has-new', !!fresh); } catch (e) { /* noop */ }
    return fresh;
  }

  btn.addEventListener('click', () => {
    sfx('paper', 0.2);
    try { if (typeof o.onOpen === 'function') o.onOpen(); }
    catch (e) { if (deps.log) { try { deps.log('bugle prop onOpen: ' + ((e && e.message) || e)); } catch (e2) { /* noop */ } } }
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

export default openBugle;
