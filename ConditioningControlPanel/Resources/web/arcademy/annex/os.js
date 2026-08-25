/* ============================================================================
 * THE ANNEX OS. The dated windowed desktop on the lab laptop: login, FILES,
 * REGISTRY, SUBJECT SEARCH, TERMINAL, RECYCLE. Carries punches 1, 2 and 3 of
 * the reveal; punch 4 (the mascot dossier) is paper on the desk and lab.js
 * owns it.
 *
 * CHROME ONLY, same law as cams.js: no bridge, no store, no fetch. Everything
 * live arrives through the caps the lab injects (liveFile, fetchStats,
 * subject), everything written comes from ./docs.js, and every consented or
 * derived value was resolved host-side before it got here. The OS never
 * computes a truth, it only displays the ones it was handed.
 *
 * Laws inherited from the build docs (ANNEX-OS.md):
 * - The REGISTRY's LIVE panel renders real counts or LINK DOWN. It never
 *   draws a number from fiction; the written archive notes sit below,
 *   visibly paper, never mixed into the table. (C6: the room never lies on
 *   a live screen.) The archive FIGURE under those notes is drawn from a
 *   frozen row and says "archive figure" on its face for the same reason.
 * - Counts under REDACT_UNDER render as black bars, the count withheld.
 * - The TERMINAL's final line renders only when all four punches are open,
 *   and it is the only surface in the Arcademy allowed the glyph it ends on.
 * - The login password is written on the note. The puzzle is noticing, not
 *   guessing; wrong entries get a dry line and never a lockout.
 * - Esc never closes the OS from in here: escapeStep() only folds an expanded
 *   attachment and then a window, and the lab owns the rung that puts the
 *   laptop down (shell ladder law: the modal you opened one press ago closes
 *   first).
 *
 * THE READING ROOM (wave 1). FILES is a three-pane explorer: the doc opens
 * BESIDE the list, never instead of it, with its figures in a sidecar column.
 * Bodies carry **keyword** and __pen__ runs which are PARSED into nodes; the
 * skim toggle restyles them without re-parsing a character.
 *
 * `parseRuns` is EXPORTED and is the annex's ONE run parser (wave 2). The
 * paper layer in lab.js imports it and paints the same rows as highlighter
 * and pen ink; two drifting parsers is the bug that export refuses.
 * ==========================================================================*/

import { TREE, DOCS, REGISTRY_NOTES, LOG_LINES, FINAL_LINE, SEARCH_DENIED } from './docs.js';
/* The three tables the reading room adds land with docs.js's own migration.
 * A NAMED import of a row that has not arrived yet is a LINK-TIME error that
 * takes the whole laptop down with it, so these come through the namespace
 * and default to empty: a table that is not there yet is a missing feature,
 * never a dead terminal. */
import * as PAPERS from './docs.js';
import { renderChart } from './charts.js';

/** The note on the bezel. Case folds, the hyphen is optional, mercy is law. */
const OS_PASSWORD = 'CYBER-PUNK';

/** Registry cells below this render as a bar. Theater threshold, page-side. */
const REDACT_UNDER = 5;

/** App order: desktop icons, taskbar, and the openers all share it. The bin
 *  is APPENDED - the original four keep their places on the desk. */
const APPS = Object.freeze(['files', 'registry', 'search', 'term', 'bin']);

/** The archive's own size, and it is a LITERAL. Hidden rows and the bin are
 *  outside it on purpose: a denominator that grew would tell the player there
 *  is more down here, which is not this counter's job to say. */
const READ_TOTAL = 26;

const RECYCLE = Array.isArray(PAPERS.RECYCLE) ? PAPERS.RECYCLE : [];
const ARCHIVED_FILES = PAPERS.ARCHIVED_FILES && typeof PAPERS.ARCHIVED_FILES === 'object'
  ? PAPERS.ARCHIVED_FILES : {};
const REGISTRY_CHART = PAPERS.REGISTRY_CHART || null;

/** Codes fold to letters and digits; the paper writes them with hyphens. */
function normCode(s) { return String(s || '').trim().toUpperCase().replace(/[^A-Z0-9]/g, ''); }

/* ---------------------------------------------------------------- the runs */

const RUNS = /\*\*([\s\S]+?)\*\*|__([\s\S]+?)__/g;

/**
 * THE ONE RUN PARSER IN THE ANNEX. `**keyword**` and `__pen__` marks, split
 * into paragraphs on a blank line and into runs inside each.
 *
 *   parseRuns(text) -> [ [ {kind:'text'|'kw'|'pen', text} ] ]
 *
 * PURE: no DOM, no document, no classes. The two materials paint it two ways
 * and neither may own a copy of it - the OS draws a kw run bold in phosphor,
 * the paper layer (lab.js) draws the same run as highlighter and the pen run
 * as blue underline. Two drifting parsers is the bug this export refuses.
 */
export function parseRuns(text) {
  const out = [];
  const paras = String(text == null ? '' : text).split(/\n\s*\n/);
  for (let i = 0; i < paras.length; i++) {
    const para = paras[i];
    if (!para.trim()) continue;
    const runs = [];
    RUNS.lastIndex = 0;
    let at = 0;
    let m = RUNS.exec(para);
    while (m) {
      if (m.index > at) runs.push({ kind: 'text', text: para.slice(at, m.index) });
      if (m[1] != null) runs.push({ kind: 'kw', text: m[1] });
      else runs.push({ kind: 'pen', text: m[2] });
      at = m.index + m[0].length;
      m = RUNS.exec(para);
    }
    if (at < para.length) runs.push({ kind: 'text', text: para.slice(at) });
    out.push(runs);
  }
  return out;
}

export function createAnnexOs(opts) {
  const o = opts || {};
  const t = typeof o.t === 'function' ? o.t : (k, f) => f;
  const subject = o.subject || {};
  const liveFile = typeof o.liveFile === 'function' ? o.liveFile : () => [];
  const fetchStats = typeof o.fetchStats === 'function' ? o.fetchStats : () => Promise.resolve(null);
  const gameName = typeof o.gameName === 'function' ? o.gameName : (k) => k;
  const gamesList = Array.isArray(o.gamesList) ? o.gamesList : [];
  const getPunches = typeof o.getPunches === 'function' ? o.getPunches : () => ({});
  const onPunch = typeof o.onPunch === 'function' ? o.onPunch : () => {};
  /* the reading room's own caps: the lab keeps them in the annex blob */
  const getRead = typeof o.getRead === 'function' ? o.getRead : () => ({});
  const onRead = typeof o.onRead === 'function' ? o.onRead : () => {};
  const getSkim = typeof o.getSkim === 'function' ? o.getSkim : () => true;
  const setSkim = typeof o.setSkim === 'function' ? o.setSkim : () => {};
  const getWithheld = typeof o.getWithheld === 'function' ? o.getWithheld : () => 0;
  const setWithheld = typeof o.setWithheld === 'function' ? o.setWithheld : () => {};
  const lite = !!o.lite;

  const doc = document;
  let dead = false;
  let statsCache;           /* last fetchStats body, kept for re-opens        */
  const wins = {};          /* app -> { el, body, barBtn }                    */
  let frontApp = null;
  let clockTimer = null;

  /* docId -> 1. Seeded from the blob, written through onRead once per id. */
  const readDocs = Object.assign({}, safeObj(getRead()));
  let skimOn = getSkim() !== false;
  /* the withheld counter is a HIGH WATER mark: bars this sitting actually
   * painted, never fewer than the number the blob already remembers */
  let withheldHigh = Math.max(0, Number(getWithheld()) || 0);
  const barsSeen = {};      /* bar id -> 1, so a repaint cannot double count  */

  let readCountEl = null;
  let withheldCountEl = null;
  let filesUi = null;       /* the live explorer's repaint hooks, or null     */
  let foldAtt = null;       /* folds an expanded attachment, Esc's top rung   */

  function safeObj(v) { return v && typeof v === 'object' ? v : {}; }

  /* Records.js's sfx template, copied not imported (trap 18). */
  function sfx(name, level, extra) {
    try {
      const detail = Object.assign({ name, level: level == null ? 0.5 : level, bus: 'fx' }, extra || {});
      doc.dispatchEvent(new CustomEvent('arcademy-sfx', { detail }));
    } catch (e) { /* sound is decoration */ }
  }

  function el(tag, cls, text) {
    const n = doc.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  /** {token} substitution for the two chrome strings that carry numbers. */
  function fmt(s, map) {
    let out = String(s == null ? '' : s);
    Object.keys(map || {}).forEach((k) => { out = out.split('{' + k + '}').join(String(map[k])); });
    return out;
  }

  const root = el('div', 'aos-root' + (lite ? ' aos-lite' : ''));
  root.setAttribute('role', 'region');
  root.setAttribute('aria-label', t('annex_os_label', 'Annex terminal'));

  /* ------------------------------------------------------------------ boot */

  function showBoot() {
    const b = el('div', 'aos-boot');
    const lines = [
      t('annex_os_boot_1', 'RECORDS ANNEX / UNIT TERMINAL'),
      t('annex_os_boot_2', 'memory check: fine, thanks for asking'),
      t('annex_os_boot_3', 'feed wall link: up'),
      t('annex_os_boot_4', 'archive index: 26 files, 5 drawers'),
      '',
    ];
    lines.forEach((s, i) => {
      const ln = el('div', 'aos-boot-line', s);
      ln.style.animationDelay = lite ? '0ms' : (i * 260) + 'ms';
      b.appendChild(ln);
    });
    root.appendChild(b);
    const hold = lite ? 60 : lines.length * 260 + 420;
    later(() => { b.remove(); showLogin(); }, hold);
  }

  /* ----------------------------------------------------------------- login */

  function normPw(s) { return String(s || '').trim().toUpperCase().replace(/[\s-]+/g, ''); }

  function showLogin() {
    const scr = el('div', 'aos-login');
    const box = el('div', 'aos-login-box');
    box.appendChild(el('div', 'aos-login-title', t('annex_os_boot_1', 'RECORDS ANNEX / UNIT TERMINAL')));
    box.appendChild(el('div', 'aos-login-sub', t('annex_os_login_sub', 'authorised staff. there is no other kind of staff.')));

    const field = el('div', 'aos-field');
    const lab = el('label', null, t('annex_os_pass', 'password'));
    lab.setAttribute('for', 'aos-pw');
    const input = el('input', 'aos-input');
    input.id = 'aos-pw';
    input.type = 'password';
    input.autocomplete = 'off';
    field.appendChild(lab);
    field.appendChild(input);
    box.appendChild(field);

    const go = el('button', 'aos-btn', t('annex_os_enter', 'log in'));
    const err = el('div', 'aos-login-err', '');
    box.appendChild(go);
    box.appendChild(err);
    scr.appendChild(box);

    /* the whole puzzle */
    const note = el('div', 'aos-note', t('annex_os_note', 'PW: CYBER-PUNK') + '\n-M');
    note.style.whiteSpace = 'pre-line';
    scr.appendChild(note);

    function attempt() {
      if (normPw(input.value) === normPw(OS_PASSWORD)) {
        sfx('commit', 0.4);
        scr.remove();
        try { o.onUnlock && o.onUnlock(); } catch (e) { /* noop */ }
        showDesktop();
      } else {
        sfx('bump', 0.2);
        err.textContent = t('annex_os_wrong', 'no. the note is right there.');
        input.value = '';
        input.focus();
      }
    }
    go.addEventListener('click', attempt);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter') attempt(); });

    root.appendChild(scr);
    later(() => { try { input.focus(); } catch (e) { /* noop */ } }, 40);
  }

  /* --------------------------------------------------------------- desktop */

  let desk = null;
  let bar = null;

  function showDesktop() {
    desk = el('div', 'aos-desk');
    const icons = el('div', 'aos-icons');
    APPS.forEach((app) => {
      const b = el('button', 'aos-icon');
      b.dataset.app = app;
      const g = el('span', 'aos-icon-glyph');
      g.setAttribute('aria-hidden', 'true');
      b.appendChild(g);
      /* the bin wears what is in it; an empty bin wears nothing */
      if (app === 'bin' && RECYCLE.length) {
        b.appendChild(el('span', 'aos-icon-badge', String(RECYCLE.length)));
      }
      b.appendChild(el('span', null, appTitle(app)));
      b.addEventListener('click', () => { sfx('blip', 0.2, { pitch: 1.05 }); openApp(app); });
      icons.appendChild(b);
    });
    desk.appendChild(icons);
    root.appendChild(desk);

    bar = el('div', 'aos-bar');
    const right = el('div', 'aos-bar-right');
    readCountEl = el('span', 'aos-count');
    withheldCountEl = el('span', 'aos-count');
    right.appendChild(readCountEl);
    right.appendChild(withheldCountEl);
    const clock = el('span', 'aos-clock');
    function tickClock() {
      const d = new Date();
      clock.textContent = pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    }
    tickClock();
    clockTimer = setInterval(tickClock, 30000);
    right.appendChild(clock);
    bar.appendChild(right);
    root.appendChild(bar);
    paintCounters();

    fit();
  }

  function appTitle(app) {
    if (app === 'files') return t('annex_os_files', 'FILES');
    if (app === 'registry') return t('annex_os_registry', 'REGISTRY');
    if (app === 'search') return t('annex_os_search', 'SUBJECT SEARCH');
    if (app === 'bin') return t('annex_os_bin', 'RECYCLE');
    return t('annex_os_term', 'TERMINAL');
  }

  /**
   * THE OS MEASURES ITSELF, IN REAL PIXELS (wave 3). Until the layer came off
   * the lab's scaled stage this test could never fire: `clientWidth` inside
   * the stage was always ~1180 unscaled layout px, whatever the phone said,
   * so `aos-cramped` was dead code and 13px type rendered near 4px. Off the
   * stage the number is the real one and the rung is honest.
   *
   *   >= 700  the windowed desktop, exactly as waves 1-2 built it
   *   <  700  full-frame windows, no drag, and FILES stacks one pane at a time
   */
  const CRAMP_UNDER = 700;

  function cramped() { return root.classList.contains('aos-cramped'); }

  function fit() {
    try {
      const w = root.clientWidth || 0;
      root.classList.toggle('aos-cramped', w > 0 && w < CRAMP_UNDER);
      if (filesUi && filesUi.sync) filesUi.sync();
    } catch (e) { /* noop */ }
  }

  /* ------------------------------------------------------------- the count */

  /** The 26 rows a first login can see. A hidden row is not in it, and the
   *  bin never was: both are outside the archive's advertised size. */
  const VISIBLE_IDS = (function buildVisible() {
    const m = {};
    TREE.forEach((f) => {
      (f.docs || []).forEach((id) => {
        const d = DOCS[id];
        if (d && !d.hidden) m[id] = 1;
      });
    });
    return m;
  }());

  function readCount() {
    let n = 0;
    Object.keys(VISIBLE_IDS).forEach((id) => { if (readDocs[id]) n++; });
    return n;
  }

  function paintCounters() {
    const n = readCount();
    if (readCountEl) {
      readCountEl.textContent = fmt(t('annex_os_read_n', 'read {n}/{total}'),
        { n, total: READ_TOTAL });
      readCountEl.classList.toggle('is-full', n >= READ_TOTAL);
    }
    if (withheldCountEl) {
      withheldCountEl.textContent = fmt(t('annex_os_withheld_n', 'withheld {n}'), { n: withheldHigh });
    }
    if (filesUi && filesUi.counts) { try { filesUi.counts(); } catch (e) { /* noop */ } }
  }

  /** One document read, once. The punch is the lab's; the tick is ours. */
  function markRead(id) {
    if (readDocs[id]) return;
    readDocs[id] = 1;
    try { onRead(id); } catch (e) { /* the blob is not worth a dead click */ }
    paintCounters();
  }

  /** A black bar the player has actually had rendered in front of them. */
  function markBar(id) {
    if (barsSeen[id]) return;
    barsSeen[id] = 1;
    const n = Object.keys(barsSeen).length;
    if (n > withheldHigh) {
      withheldHigh = n;
      try { setWithheld(n); } catch (e) { /* noop */ }
    }
    paintCounters();
  }

  /* --------------------------------------------------------------- windows */

  const SPAWN = Object.freeze({
    files: [90, 20, 730, 452],
    registry: [190, 46, 480, 424],   /* +the figure, so the notes still fit */
    search: [220, 44, 440, 350],
    term: [170, 90, 470, 290],
    bin: [220, 120, 430, 300],
  });

  function openApp(app) {
    if (dead || !desk) return;
    let w = wins[app];
    if (!w) {
      w = buildWindow(app);
      wins[app] = w;
      desk.appendChild(w.el);
      addBarBtn(app, w);
      renderApp(app, w);
    }
    w.el.hidden = false;
    bringToFront(app);
    if (app === 'files') {
      onPunch('p1obs'); /* observed, not read; the read stamps p1 */
      /* a punch opened elsewhere can have unlocked a row while this window
       * sat behind another one: re-read the gates on every raise */
      if (filesUi && filesUi.refresh) { try { filesUi.refresh(); } catch (e) { /* noop */ } }
    }
    if (app === 'registry') onPunch('p2');
    if (app === 'term') renderTerm(w); /* the log re-reads the punches each open */
  }

  function buildWindow(app) {
    const [x, y, wpx, hpx] = SPAWN[app];
    const win = el('div', 'aos-win');
    win.style.left = x + 'px';
    win.style.top = y + 'px';
    win.style.width = wpx + 'px';
    win.style.height = hpx + 'px';

    const title = el('div', 'aos-title', appTitle(app));
    const x2 = el('button', 'aos-title-x', '×');
    x2.setAttribute('aria-label', t('annex_os_close', 'close'));
    x2.addEventListener('click', (e) => { e.stopPropagation(); closeApp(app); });
    title.appendChild(x2);
    win.appendChild(title);

    const body = el('div', 'aos-body');
    win.appendChild(body);

    win.addEventListener('pointerdown', () => bringToFront(app));
    wireDrag(win, title);
    return { el: win, body, barBtn: null };
  }

  function addBarBtn(app, w) {
    const b = el('button', 'aos-bar-btn is-open', appTitle(app));
    b.addEventListener('click', () => {
      if (w.el.hidden) { w.el.hidden = false; bringToFront(app); }
      else if (frontApp === app) { w.el.hidden = true; pickNewFront(); }
      else bringToFront(app);
    });
    bar.insertBefore(b, bar.lastChild); /* the counters and clock stay right */
    w.barBtn = b;
  }

  function closeApp(app) {
    const w = wins[app];
    if (!w) return;
    sfx('blip', 0.14, { pitch: 0.92 });
    w.el.remove();
    if (w.barBtn) w.barBtn.remove();
    delete wins[app];
    if (app === 'files') { filesUi = null; foldAtt = null; }
    if (frontApp === app) pickNewFront();
  }

  function bringToFront(app) {
    frontApp = app;
    Object.keys(wins).forEach((k) => {
      wins[k].el.classList.toggle('is-front', k === app);
      if (wins[k].barBtn) wins[k].barBtn.classList.toggle('is-open', k === app && !wins[k].el.hidden);
    });
  }

  function pickNewFront() {
    const open = Object.keys(wins).filter((k) => !wins[k].el.hidden);
    frontApp = open.length ? open[open.length - 1] : null;
    if (frontApp) bringToFront(frontApp);
  }

  function wireDrag(win, handle) {
    let sx = 0, sy = 0, ox = 0, oy = 0, drag = false;
    handle.addEventListener('pointerdown', (e) => {
      if (e.target && e.target.classList && e.target.classList.contains('aos-title-x')) return;
      if (root.classList.contains('aos-cramped')) return;
      drag = true;
      sx = e.clientX; sy = e.clientY;
      ox = win.offsetLeft; oy = win.offsetTop;
      try { handle.setPointerCapture(e.pointerId); } catch (err) { /* noop */ }
    });
    handle.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const nx = Math.max(-40, Math.min((desk ? desk.clientWidth : 800) - 60, ox + e.clientX - sx));
      const ny = Math.max(0, Math.min((desk ? desk.clientHeight : 500) - 30, oy + e.clientY - sy));
      win.style.left = nx + 'px';
      win.style.top = ny + 'px';
    });
    const drop = () => { drag = false; };
    handle.addEventListener('pointerup', drop);
    handle.addEventListener('pointercancel', drop);
  }

  /* ------------------------------------------------------------------ apps */

  function renderApp(app, w) {
    if (app === 'files') renderFiles(w);
    else if (app === 'registry') renderRegistry(w);
    else if (app === 'search') renderSearch(w);
    else if (app === 'bin') renderBin(w);
    else renderTerm(w);
  }

  /* ------------------------------------------------------- reading pieces */

  /** A drawn glyph, the way the desktop icons are drawn: no image assets and
   *  nothing remote (trap 2). Kinds: txt chart img aud fold lock. */
  function ico(kind) {
    const s = el('span', 'aos-ico is-' + kind);
    s.setAttribute('aria-hidden', 'true');
    return s;
  }

  /** The type glyph a row wears. A document with no words and one figure is
   *  a figure; everything else is what docs.js says it is. */
  function iconFor(d) {
    if (d.kind === 'scan') return 'img';
    const a = Array.isArray(d.attachments) ? d.attachments : [];
    if (!d.body && a.length === 1) {
      if (a[0].kind === 'chart') return 'chart';
      if (a[0].kind === 'audio') return 'aud';
      return 'img';
    }
    return 'txt';
  }

  function metaFor(d) {
    const parts = [];
    if (d.kind === 'scan') parts.push(t('annex_os_scan', 'scan'));
    const a = Array.isArray(d.attachments) ? d.attachments : [];
    if (a.length) parts.push('+' + a.length);
    return parts.join('  ');
  }

  /** ms as a stopwatch, for an audio card's corner. */
  function clockMs(ms) {
    const s = Math.max(0, Math.round((Number(ms) || 0) / 1000));
    return Math.floor(s / 60) + ':' + pad2(s % 60);
  }

  /** Keyword folding, for matching a TL;DR term against a bold run. */
  function foldTerm(s) {
    return String(s || '').toLowerCase().replace(/[^a-z0-9 ]+/g, ' ').replace(/\s+/g, ' ').trim();
  }

  /**
   * The body, PAINTED off the shared parse (parseRuns, module scope above).
   * `**keyword**` is a bold phosphor run, `__word__` an underlined pen run,
   * a blank line a paragraph. Text nodes and elements, never innerHTML - and
   * never a second parse, because the skim toggle only ever changes a class
   * on the pane above it.
   */
  function paintBody(host, text) {
    host.textContent = '';
    parseRuns(text).forEach((runs) => {
      const p = el('p', 'aos-p');
      runs.forEach((r) => {
        if (r.kind === 'kw') {
          const b = el('b', 'aos-kw', r.text);
          b.dataset.kw = foldTerm(r.text);
          p.appendChild(b);
        } else if (r.kind === 'pen') {
          p.appendChild(el('u', 'aos-pen', r.text));
        } else {
          p.appendChild(doc.createTextNode(r.text));
        }
      });
      host.appendChild(p);
    });
  }

  /* FILES: the explorer. Tree, list, preview, and nothing ever replaces
   * anything - the old viewer wiped the list to paint a document, which is
   * what made every file a dead end you had to back out of. */
  function renderFiles(w) {
    w.body.textContent = '';
    w.body.classList.add('aos-flush');

    let sel = TREE[0] || { label: '', docs: [] };
    let selId = null;
    let openAtt = -1;          /* which sidecar card is expanded, or -1       */

    /**
     * THE STACK IS STATE, NOT WINDOWS (wave 3, Plate 4). Below 700px the three
     * panes are the same three panes - one of them is simply the one on
     * screen. 'tree' is the folder list, 'list' the files in it, 'doc' the
     * document; the crumb's back button and the Esc ladder both walk the same
     * ladder down it. Above 700px the value is written and CSS ignores it, so
     * the desktop explorer is untouched by every line of this.
     */
    let pane = 'tree';
    function setPane(p) {
      pane = p;
      try { w.body.dataset.pane = p; } catch (e) { /* noop */ }
      paintCrumb();
    }

    /* -------------------------------------------------------- breadcrumb */
    const crumb = el('div', 'aos-crumb');
    const crumbBack = el('button', 'aos-crumb-back');
    crumbBack.addEventListener('click', () => {
      if (pane === 'doc') { sfx('blip', 0.14, { pitch: 0.92 }); setPane('list'); return; }
      if (pane === 'list') { sfx('blip', 0.14, { pitch: 0.92 }); setPane('tree'); }
    });
    const crumbT = el('span', 'aos-crumb-t');
    const sp = el('span', 'aos-crumb-sp');
    const skimBtn = el('button', 'aos-chip aos-skim');
    const readChip = el('span', 'aos-chip');
    sp.appendChild(skimBtn);
    sp.appendChild(readChip);
    crumb.appendChild(crumbBack);
    crumb.appendChild(crumbT);
    crumb.appendChild(sp);
    w.body.appendChild(crumb);

    const wrap = el('div', 'aos-explorer');
    const tree = el('div', 'aos-tree');
    const list = el('div', 'aos-flist');
    const pv = el('div', 'aos-pv');
    wrap.appendChild(tree);
    wrap.appendChild(list);
    wrap.appendChild(pv);
    w.body.appendChild(wrap);

    /** The rows of a folder the player may actually see. A hidden row waits
     *  on its gate; gate.punches means all four, read off the lab's cap. */
    function visibleDocs(folder) {
      const p = getPunches() || {};
      const allFour = !!(p.p1 && p.p2 && p.p3 && p.p4);
      return (folder.docs || []).filter((id) => {
        const d = DOCS[id];
        if (!d) return false;
        if (!d.hidden) return true;
        const g = d.gate || {};
        return g.punches ? allFour : false;
      });
    }

    /** '02 RETENTION' -> '02'. The crumb's back button wears the drawer number
     *  the way Plate 4 draws it; a phone has no room for the whole name. */
    function shortLabel(s) {
      const first = String(s || '').trim().split(/\s+/)[0];
      return first || String(s || '');
    }

    function paintCrumb() {
      crumbT.textContent = '';
      const d = selId ? DOCS[selId] : null;
      const arch = t('annex_os_archive', 'ARCHIVE');
      if (!cramped()) {
        crumbT.appendChild(doc.createTextNode(arch + ' › '));
        crumbT.appendChild(el('b', null, sel.label || ''));
        if (d) crumbT.appendChild(doc.createTextNode(' › ' + d.name));
      } else if (pane === 'doc' && d) {
        /* stacked, the rung ABOVE is already spelled out on the back button -
         * a crumb that repeats it is half a phone's width of the same word */
        crumbT.appendChild(el('b', null, d.name));
      } else if (pane === 'list') {
        crumbT.appendChild(el('b', null, sel.label || ''));
      } else {
        crumbT.appendChild(doc.createTextNode(arch));
      }
      crumbBack.textContent = '‹ ' + (pane === 'doc' ? shortLabel(sel.label) : arch);
    }

    function paintChips() {
      const n = readCount();
      readChip.textContent = fmt(t('annex_os_read_n', 'read {n}/{total}'),
        { n, total: READ_TOTAL });
      readChip.classList.toggle('is-full', n >= READ_TOTAL);
    }

    function applySkim() {
      skimBtn.textContent = skimOn
        ? t('annex_os_skim_on', 'skim: on')
        : t('annex_os_skim_off', 'skim: off');
      pv.classList.toggle('is-plain', !skimOn);
    }
    skimBtn.addEventListener('click', () => {
      skimOn = !skimOn;
      try { setSkim(skimOn); } catch (e) { /* noop */ }
      sfx('blip', 0.14, { pitch: skimOn ? 1.1 : 0.9 });
      applySkim();
    });

    /* -------------------------------------------------------------- tree */

    /** 05. A slot, not a folder: it never opens and it never fills. The name
     *  is a bar, which is the one thing the archive says about it. */
    function lockedRow() {
      const r = el('div', 'aos-frow is-locked');
      r.appendChild(ico('lock'));
      const nm = el('span', 'aos-frow-t', '05 ');
      const bb = el('span', 'aos-bar-black');
      bb.setAttribute('role', 'img');
      bb.setAttribute('aria-label', t('annex_os_redacted', 'withheld'));
      nm.appendChild(bb);
      r.appendChild(nm);
      r.appendChild(el('span', 'aos-fcount', '?'));
      markBar('folder05');
      return r;
    }

    function paintTree() {
      tree.textContent = '';
      TREE.forEach((folder) => {
        const ids = visibleDocs(folder);
        const unread = ids.filter((id) => !readDocs[id]).length;
        const b = el('button', 'aos-frow' + (folder === sel ? ' is-sel' : ''));
        b.title = folder.label;      /* the pane is narrow; the name is not */
        b.appendChild(ico('fold'));
        b.appendChild(el('span', 'aos-frow-t', folder.label));
        b.appendChild(el('span', 'aos-fcount' + (unread > 0 ? ' is-new' : ''),
          unread > 0 ? unread + ' ' + t('annex_os_new', 'new') : String(ids.length)));
        b.addEventListener('click', () => {
          /* stacked, the folder you are already in is still a folder you are
           * asking to go INTO: the tap has to move, or the pane is a dead row */
          if (folder === sel) { if (cramped()) { sfx('blip', 0.14, { pitch: 1.05 }); setPane('list'); } return; }
          sel = folder;
          selId = null;
          openAtt = -1;
          foldAtt = null;
          sfx('blip', 0.14, { pitch: 1.05 });
          if (cramped()) setPane('list');
          paintCrumb(); paintTree(); paintList(); paintPreview();
        });
        tree.appendChild(b);
      });
      tree.appendChild(lockedRow());
    }

    /* -------------------------------------------------------------- list */

    function paintList() {
      list.textContent = '';
      visibleDocs(sel).forEach((id) => {
        const d = DOCS[id];
        const b = el('button', 'aos-fitem'
          + (readDocs[id] ? ' is-read' : ' is-unread')
          + (id === selId ? ' is-sel' : ''));
        b.appendChild(ico(iconFor(d)));
        const nm = el('span', 'aos-fitem-t', d.name);
        if (!readDocs[id]) nm.appendChild(el('i', 'aos-newtag', t('annex_os_newtag', 'NEW')));
        b.appendChild(nm);
        b.appendChild(el('span', 'aos-fmeta', metaFor(d)));
        b.addEventListener('click', () => openDoc(id));
        list.appendChild(b);
      });
    }

    function openDoc(id) {
      const d = DOCS[id];
      if (!d) return;
      sfx('paper', 0.25);
      selId = id;
      openAtt = -1;
      foldAtt = null;
      markRead(id);
      onPunch('p1');
      if (cramped()) setPane('doc');
      paintCrumb(); paintTree(); paintList(); paintPreview();
      /* the pager walks the folder, so a new document starts at ITS top */
      if (cramped()) { try { pv.scrollTop = 0; } catch (e) { /* noop */ } }
    }

    function step(dir) {
      const ids = visibleDocs(sel);
      const i = ids.indexOf(selId);
      const next = i < 0 ? 0 : i + dir;
      if (next < 0 || next >= ids.length) return;
      openDoc(ids[next]);
    }

    /* ----------------------------------------------------------- preview */

    function flash(node) {
      try { node.scrollIntoView({ block: 'nearest' }); } catch (e) { /* noop */ }
      node.classList.add('is-flash');
      later(() => { try { node.classList.remove('is-flash'); } catch (e) { /* noop */ } }, 900);
    }

    /** One attachment, drawn small. Only the two kinds the OS owns render;
     *  a 'note', a 'live' strip or an 'image' is PAPER material and this
     *  sidecar simply does not know what they are. */
    function attCard(att, idx, host) {
      if (!att || (att.kind !== 'chart' && att.kind !== 'audio')) return;
      const box = el('div', 'aos-att' + (idx === openAtt ? ' is-open' : ''));
      const head = el('div', 'aos-att-t');
      head.appendChild(ico(att.kind === 'audio' ? 'aud' : 'chart'));
      head.appendChild(el('span', null, att.kind === 'audio'
        ? t('annex_os_audio', 'audio log') : t('annex_os_chart', 'chart')));
      head.appendChild(el('span', 'aos-att-tag', att.kind === 'audio'
        ? clockMs(att.ms) : t('annex_os_archivefig', 'archive figure')));
      box.appendChild(head);

      if (att.kind === 'chart') {
        try {
          box.appendChild(renderChart(att.chart, { palette: 'phosphor', caption: att.caption }));
        } catch (e) { /* a bad row costs the figure, never the paragraph */ }
      } else {
        const row = el('div', 'aos-aud');
        const play = el('button', 'aos-play');
        play.setAttribute('aria-label', t('annex_os_play', 'play'));
        play.addEventListener('click', (ev) => {
          ev.stopPropagation();
          /* a REQUEST on document; this file holds no audio node (trap 18) */
          sfx(att.sfx, 0.35);
        });
        row.appendChild(play);
        row.appendChild(el('span', 'aos-aud-bar'));
        box.appendChild(row);
      }
      if (att.caption) box.appendChild(el('div', 'aos-att-cap', att.caption));

      box.addEventListener('click', () => {
        const was = openAtt === idx;
        openAtt = was ? -1 : idx;
        foldAtt = was ? null : () => { openAtt = -1; paintPreview(); };
        sfx('blip', 0.14, { pitch: was ? 0.92 : 1.08 });
        paintPreview();
        /* stacked, the expanded figure is pinned to the top of the pane, so
         * a pane scrolled halfway down would open it out of sight */
        if (!was && cramped()) { try { pv.scrollTop = 0; } catch (e) { /* noop */ } }
      });
      host.appendChild(box);
    }

    function paintPreview() {
      pv.textContent = '';
      pv.classList.toggle('is-plain', !skimOn);
      const d = selId ? DOCS[selId] : null;
      if (!d) {
        pv.appendChild(el('div', 'aos-pv-empty', t('annex_os_pick', 'pick a file.')));
        return;
      }

      /* THE TWO WALKERS ARE THEIR OWN ROW, and a direct child of the pane.
       * On the desktop they sit exactly where they always did, above the
       * typed header; stacked, CSS orders the same node to the bottom as the
       * footer bar Plate 4 draws, which is the only place a thumb can reach
       * it. A node nested inside the header could be neither. */
      const nav = el('div', 'aos-pv-nav');
      const ids = visibleDocs(sel);
      const i = ids.indexOf(selId);
      const prev = el('button', 'aos-navbtn', '‹ ' + t('annex_os_prev', 'prev'));
      const next = el('button', 'aos-navbtn', t('annex_os_next', 'next') + ' ›');
      prev.disabled = i <= 0;
      next.disabled = i < 0 || i >= ids.length - 1;
      prev.addEventListener('click', () => step(-1));
      next.addEventListener('click', () => step(1));
      nav.appendChild(prev);
      nav.appendChild(el('span', 'aos-pv-pos',
        fmt(t('annex_os_pos', '{i} of {n}'), { i: i + 1, n: ids.length })));
      nav.appendChild(next);
      pv.appendChild(nav);
      const head = el('div', 'aos-pv-head');
      head.appendChild(el('div', 'aos-doc-head', d.head || ''));
      pv.appendChild(head);

      /* the skeleton of the document, in body order */
      const terms = Array.isArray(d.tldr) ? d.tldr : [];
      const bodyBox = el('div', 'aos-pv-body');
      if (terms.length) {
        const strip = el('div', 'aos-tldr');
        strip.appendChild(el('span', 'aos-tldr-lab', t('annex_os_tldr', 'TL;DR')));
        terms.forEach((term) => {
          const chip = el('button', 'aos-tldr-chip', String(term));
          chip.addEventListener('click', () => {
            const want = foldTerm(term);
            const runs = bodyBox.querySelectorAll('.aos-kw');
            for (let k = 0; k < runs.length; k++) {
              const kw = runs[k].dataset ? runs[k].dataset.kw : foldTerm(runs[k].textContent);
              if (kw && (kw === want || kw.indexOf(want) >= 0 || want.indexOf(kw) >= 0)) {
                flash(runs[k]);
                return;
              }
            }
          });
          strip.appendChild(chip);
        });
        pv.appendChild(strip);
      }

      /* the body, or the notice it was photographed off the board as */
      if (d.kind === 'scan') {
        const plate = el('div', 'aos-scan');
        plate.appendChild(el('span', 'aos-pin'));
        plate.appendChild(el('span', 'aos-pin is-right'));
        const inner = el('div', 'aos-scan-txt');
        paintBody(inner, d.body);
        plate.appendChild(inner);
        bodyBox.appendChild(plate);
        if (d.cap) bodyBox.appendChild(el('div', 'aos-scan-cap', d.cap));
      } else {
        paintBody(bodyBox, d.body);
      }
      pv.appendChild(bodyBox);

      /* a document with no figures gets the sidecar's width back: the pane
       * is 730 wide and a 152px empty column is 152px of prose */
      const side = el('div', 'aos-side');
      (Array.isArray(d.attachments) ? d.attachments : []).forEach((att, idx) => attCard(att, idx, side));
      pv.classList.toggle('is-solo', !side.childNodes.length);
      if (side.childNodes.length) pv.appendChild(side);
    }

    filesUi = {
      counts: paintChips,
      refresh: () => { paintTree(); paintList(); },
      /* fit() calls this on every rung change: a window that was showing a
       * document keeps showing it, and one that was not lands on the folders */
      sync: () => setPane(selId ? 'doc' : 'tree'),
      /** THE STACKED RUNGS, and only when they exist. Esc folds the expanded
       *  attachment (escapeStep's own first rung), then the doc pane, then the
       *  file pane, then answers false and the window closes as it always
       *  has. On the desktop this is a no-op by the first line. */
      escape: () => {
        if (!cramped()) return false;
        if (pane === 'doc') { setPane('list'); return true; }
        if (pane === 'list') { setPane('tree'); return true; }
        return false;
      },
    };

    setPane(pane);
    applySkim();
    paintCrumb();
    paintChips();
    paintTree();
    paintList();
    paintPreview();
  }

  /* REGISTRY: the LIVE table (real or LINK DOWN, never fiction), then paper,
   * then the archive figure that paper talks about. */
  function renderRegistry(w) {
    w.body.textContent = '';
    const live = el('div');
    live.appendChild(el('div', 'aos-reg-h', t('annex_os_live', 'LIVE')));
    const slot = el('div');
    live.appendChild(slot);
    w.body.appendChild(live);

    const arch = el('div');
    arch.appendChild(el('div', 'aos-reg-h', t('annex_os_archive', 'ARCHIVE')));
    REGISTRY_NOTES.forEach((n) => arch.appendChild(el('p', 'aos-reg-note', n)));
    if (REGISTRY_CHART && REGISTRY_CHART.chart) {
      const fig = el('div', 'aos-att aos-reg-fig');
      const t2 = el('div', 'aos-att-t');
      t2.appendChild(ico('chart'));
      t2.appendChild(el('span', null, t('annex_os_chart', 'chart')));
      t2.appendChild(el('span', 'aos-att-tag', t('annex_os_archivefig', 'archive figure')));
      fig.appendChild(t2);
      try {
        fig.appendChild(renderChart(REGISTRY_CHART.chart,
          { palette: 'phosphor', caption: REGISTRY_CHART.caption }));
      } catch (e) { /* the notes still stand without it */ }
      if (REGISTRY_CHART.caption) fig.appendChild(el('div', 'aos-att-cap', REGISTRY_CHART.caption));
      arch.appendChild(fig);
    }
    w.body.appendChild(arch);

    function cell(n, id) {
      if (typeof n !== 'number' || !isFinite(n)) n = 0;
      if (n < REDACT_UNDER) {
        const s = el('span', 'aos-redact');
        s.setAttribute('role', 'img');
        s.setAttribute('aria-label', t('annex_os_redacted', 'withheld'));
        markBar('reg:' + id);
        return s;
      }
      return doc.createTextNode(String(n));
    }

    function paint(body) {
      slot.textContent = '';
      if (!body || !body.games) {
        const down = el('div', 'aos-reg-down', t('annex_os_linkdown', 'LINK DOWN'));
        const retry = el('button', 'aos-btn', t('annex_os_retry', 'retry'));
        retry.style.marginLeft = '12px';
        retry.addEventListener('click', () => { statsCache = undefined; load(); });
        down.appendChild(retry);
        slot.appendChild(down);
        return;
      }
      const tbl = el('table', 'aos-reg-table');
      const hr = el('tr');
      [t('annex_os_room', 'room'), t('annex_os_enrolled', 'enrolled'), t('annex_os_completed', 'completed')]
        .forEach((h) => hr.appendChild(el('th', null, h)));
      tbl.appendChild(hr);
      const keys = gamesList.length ? gamesList : Object.keys(body.games);
      keys.forEach((k) => {
        const g = body.games[k];
        if (!g) return;
        const tr = el('tr');
        tr.appendChild(el('td', null, gameName(k)));
        const td1 = el('td'); td1.appendChild(cell(g.enrolled, k + ':e')); tr.appendChild(td1);
        const td2 = el('td'); td2.appendChild(cell(g.complete, k + ':c')); tr.appendChild(td2);
        tbl.appendChild(tr);
      });
      if (body.totals) {
        const tr = el('tr', 'aos-reg-total');
        tr.appendChild(el('td', null, t('annex_os_all', 'all subjects')));
        const td1 = el('td'); td1.appendChild(cell(body.totals.enrolled, 'all:e')); tr.appendChild(td1);
        const td2 = el('td'); td2.appendChild(cell(body.totals.complete, 'all:c')); tr.appendChild(td2);
        tbl.appendChild(tr);
      }
      slot.appendChild(tbl);
    }

    function load() {
      if (statsCache !== undefined) { paint(statsCache); return; }
      slot.textContent = '';
      slot.appendChild(el('div', 'aos-reg-wait', t('annex_os_linkwait', 'link…')));
      Promise.resolve()
        .then(() => fetchStats())
        .then((body) => { if (dead) return; statsCache = body || null; paint(statsCache); })
        .catch(() => { if (dead) return; statsCache = null; paint(null); });
    }
    load();
  }

  /* RECYCLE: what somebody deleted and the machine kept. One doc at a time,
   * the old FILES pattern, and deliberately outside every counter - a draft
   * in the bin was never one of the archive's 26 and stamps no punch. */
  function renderBin(w) {
    w.body.textContent = '';
    if (!RECYCLE.length) {
      w.body.appendChild(el('div', 'aos-reg-wait', t('annex_os_bin_empty', 'nothing in here.')));
      return;
    }
    paintRows();

    function paintRows() {
      w.body.textContent = '';
      RECYCLE.forEach((d, i) => {
        if (!d) return;
        const b = el('button', 'aos-fitem is-bin');
        b.appendChild(ico(d.kind === 'scan' ? 'img' : 'txt'));
        b.appendChild(el('span', 'aos-fitem-t', d.name || ''));
        b.appendChild(el('span', 'aos-fmeta', metaFor(d)));
        b.addEventListener('click', () => openRow(i));
        w.body.appendChild(b);
      });
    }

    function openRow(i) {
      const d = RECYCLE[i];
      if (!d) return;
      sfx('paper', 0.22);
      w.body.textContent = '';
      const back = el('button', 'aos-navbtn', '‹ ' + t('annex_os_back', 'back'));
      back.addEventListener('click', () => { sfx('paper', 0.16); paintRows(); });
      w.body.appendChild(back);
      w.body.appendChild(el('div', 'aos-doc-head', d.head || ''));
      const body = el('div', 'aos-pv-body');
      paintBody(body, d.body);
      w.body.appendChild(body);
    }
  }

  /* SUBJECT SEARCH: the code and password from the paper open the live file.
   * An ARCHIVED pair opens a closed one instead, and that path is a dead end
   * on purpose: it stamps nothing and asks the lab for nothing. */
  function renderSearch(w) {
    w.body.textContent = '';
    const form = el('div', 'aos-search-form');
    const f1 = el('div', 'aos-field');
    f1.appendChild(el('label', null, t('annex_os_code', 'subject code')));
    const code = el('input', 'aos-input');
    code.autocomplete = 'off';
    f1.appendChild(code);
    const f2 = el('div', 'aos-field');
    f2.appendChild(el('label', null, t('annex_os_pass', 'password')));
    const pw = el('input', 'aos-input');
    pw.type = 'password';
    pw.autocomplete = 'off';
    f2.appendChild(pw);
    const go = el('button', 'aos-btn', t('annex_os_open_file', 'open file'));
    const err = el('div', 'aos-login-err', '');
    form.appendChild(f1); form.appendChild(f2); form.appendChild(go); form.appendChild(err);
    w.body.appendChild(form);

    function attempt() {
      const closed = matchArchived(code.value, pw.value);
      if (closed) { sfx('commit', 0.34); renderClosedFile(w, closed); return; }
      const okCode = subject.code && normCode(code.value) === normCode(subject.code);
      const okPw = subject.password && normPw(pw.value) === normPw(subject.password);
      if (okCode && okPw) { sfx('commit', 0.4); onPunch('p3'); renderFile(w); }
      else {
        sfx('bump', 0.2);
        err.textContent = t('annex_os_notfound', SEARCH_DENIED);
      }
    }
    go.addEventListener('click', attempt);
    pw.addEventListener('keydown', (e) => { if (e.key === 'Enter') attempt(); });
  }

  /** A second, unadvertised pair. `normPw` folds case and hyphens, so a file
   *  whose password is written NONE also answers to the word none. */
  function matchArchived(codeVal, pwVal) {
    const keys = Object.keys(ARCHIVED_FILES);
    for (let i = 0; i < keys.length; i++) {
      const e = ARCHIVED_FILES[keys[i]];
      if (!e || !e.code) continue;
      if (normCode(codeVal) !== normCode(e.code)) continue;
      if (normPw(pwVal) !== normPw(e.password)) continue;
      return e;
    }
    return null;
  }

  /* The live file. Sections come from the lab's cap, freshly computed, so the
   * numbers are current at open. The paper upstairs was old. This is not. */
  function renderFile(w) {
    w.body.textContent = '';
    w.body.appendChild(el('div', 'aos-doc-head',
      t('annex_os_file_title', 'SUBJECT FILE') + '  ' + (subject.code || '')));
    let sections = [];
    try { sections = liveFile() || []; } catch (e) { sections = []; }
    sections.forEach((sec) => {
      const box = el('div', 'aos-file-sec');
      box.appendChild(el('h4', null, sec.title));
      const dl = el('dl');
      (sec.rows || []).forEach((r) => {
        const row = el('div', 'aos-file-row');
        row.appendChild(el('dt', null, r[0]));
        row.appendChild(el('dd', null, String(r[1])));
        dl.appendChild(row);
      });
      box.appendChild(dl);
      w.body.appendChild(box);
    });
    w.body.appendChild(el('span', 'aos-file-stamp', t('annex_os_ongoing', 'ONGOING')));
  }

  /** A closed file. Static rows, a colder stamp, and a line at the bottom
   *  somebody typed years ago. Nothing here is live and nothing is punched. */
  function renderClosedFile(w, entry) {
    w.body.textContent = '';
    w.body.appendChild(el('div', 'aos-doc-head',
      (entry.title || t('annex_os_file_title', 'SUBJECT FILE')) + '  ' + (entry.code || '')));
    const dl = el('dl');
    (Array.isArray(entry.rows) ? entry.rows : []).forEach((r) => {
      const row = el('div', 'aos-file-row');
      const k = Array.isArray(r) ? r[0] : (r && r.k);
      const v = Array.isArray(r) ? r[1] : (r && r.v);
      row.appendChild(el('dt', null, k == null ? '' : String(k)));
      row.appendChild(el('dd', null, v == null ? '' : String(v)));
      dl.appendChild(row);
    });
    w.body.appendChild(dl);
    w.body.appendChild(el('span', 'aos-file-stamp is-closed',
      entry.stamp || t('annex_os_closed', 'CLOSED')));
    if (entry.footer) w.body.appendChild(el('div', 'aos-file-foot', entry.footer));
  }

  /* TERMINAL: the log. Re-rendered on every open so the final line can land. */
  function renderTerm(w) {
    w.body.textContent = '';
    const now = new Date();
    const t1 = new Date(now.getTime() - 63 * 60000);
    const t2 = new Date(now.getTime() - 7 * 60000);
    const stamp = (d) => pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    const box = el('div', 'aos-term');
    LOG_LINES.forEach((ln) => {
      box.appendChild(el('div', null,
        ln.replace('{t1}', stamp(t1)).replace('{t2}', stamp(t2))));
    });
    const p = getPunches() || {};
    if (p.p1 && p.p2 && p.p3 && p.p4) {
      box.appendChild(el('div', 'aos-term-final', FINAL_LINE));
    }
    w.body.appendChild(box);
  }

  /* ----------------------------------------------------------------- misc */

  const timers = [];
  function later(fn, ms) {
    const id = setTimeout(() => { if (!dead) fn(); }, ms);
    timers.push(id);
    return id;
  }
  function pad2(n) { return (n < 10 ? '0' : '') + n; }

  /** Rungs, inward-out: an expanded figure folds first, then - on a stacked
   *  FILES only - the doc pane and the file pane, then the window itself.
   *  The lab still decides when the laptop goes down, and the desktop ladder
   *  is byte for byte the one waves 1-2 shipped (filesUi.escape answers false
   *  on its first line when the window is not cramped). */
  function escapeStep() {
    if (foldAtt) {
      const f = foldAtt;
      foldAtt = null;
      try { f(); } catch (e) { /* noop */ }
      sfx('blip', 0.14, { pitch: 0.92 });
      return true;
    }
    if (frontApp === 'files' && wins.files && !wins.files.el.hidden && filesUi && filesUi.escape) {
      let folded = false;
      try { folded = !!filesUi.escape(); } catch (e) { folded = false; }
      if (folded) { sfx('blip', 0.14, { pitch: 0.92 }); return true; }
    }
    if (frontApp && wins[frontApp] && !wins[frontApp].el.hidden) {
      closeApp(frontApp);
      return true;
    }
    return false;
  }

  function destroy() {
    if (dead) return;
    dead = true;
    filesUi = null;
    foldAtt = null;
    timers.forEach((id) => clearTimeout(id));
    if (clockTimer) clearInterval(clockTimer);
    try { root.remove(); } catch (e) { /* noop */ }
  }

  /* boot: revisits skip the theater, the room already earned it once */
  if (o.osUnlocked) showDesktop();
  else showBoot();

  return { root, escapeStep, destroy, fit };
}

export default createAnnexOs;
