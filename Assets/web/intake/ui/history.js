/* ============================================================================
 * ui/history.js — THE RECORDS OFFICE.
 *
 * The intake keeps a file on you. This is the drawer it keeps them in: every
 * completed assessment, newest at the front, each one openable back into the
 * card it closed on — the grade, the judgment, the numbers, the colours you
 * named, the spirals they wove, the payloads you were issued, and what was
 * drafted out of you afterwards.
 *
 * Mounted from the main menu (ui/menu.js) and ONLY once at least one completed
 * run exists, so a first-ever subject is never shown an empty drawer.
 *
 *   hasArchive(stats)                -> Promise<boolean>   // is there anything on file
 *   openHistory({ parent, stats, audio }) -> Promise<void>       // resolves on close
 *
 * WHERE THE DATA COMES FROM: core/stats.js, which has recorded every run since
 * the day it landed (IndexedDB, degrading to localStorage, degrading to memory).
 * Runs recorded before the recap block existed still list — they simply show
 * the paperwork they have and say so. Runs that were ABANDONED (quit from the
 * pause menu, or the "are you sure?" abort) were never recorded at all and have
 * no file here, which is exactly right: an assessment that was not completed
 * was not an assessment.
 *
 * Structurally this is ui/options.js's sibling: one modal, one promise, every
 * listener in a ledger, no global state, never throws past its own boundary.
 * Offline-safe: no fonts, no remote assets, no network. The only import beyond
 * the intake is the Loom field renderer, dynamically and optionally, to repaint
 * a stored spiral as a still.
 * ========================================================================== */

/* ----------------------------------------------------------------------------
 * COPY — clinical register, matching boot.js's briefing/outro exactly. The
 * storefront voice stops at the panel border; inside the drawer it is a filing
 * system that has been taking notes.
 * -------------------------------------------------------------------------- */
const EYEBROW = 'Graded Intake · Form CRA-7/A';
const TITLE = 'Records Office';
const SUBTITLE = 'Prior assessments, retained on this terminal. Nothing here has left this machine.';
const EMPTY_LINE = 'No completed assessments on file.';
const THINNED_NOTE = 'Photographic material discharged under the retention schedule. The findings remain.';
const LEGACY_NOTE = 'Filed before the archive kept full records. Only the summary survives.';

/** How many files the drawer lists. Older records still exist in stats.js (the
 *  lifetime aggregates need them) — the drawer simply does not page past this. */
const LIST_LIMIT = 60;
/** Spiral still size, in device px. Matches the outro gallery's tile budget. */
const TILE_PX = 112;

/* ----------------------------------------------------------------------------
 * SCOPED STYLESHEET — injected once, on first open. Namespaced `.ixh-` so it
 * cannot collide with the run's `intake-`/`ix-` classes or the shell's `kw-`.
 * It borrows the RUN's palette tokens (styles.css :root) rather than the kawaii
 * storefront's, because a filing cabinet is not a storefront; every var carries
 * a literal fallback so the panel still reads correctly in a bare harness.
 * -------------------------------------------------------------------------- */
const IXH_CSS = `
.ixh-root {
  position: fixed; inset: 0; z-index: 45;
  display: flex; align-items: center; justify-content: center;
  color: var(--intake-text, #f3e9f6);
  font: 15px/1.5 'Segoe UI', system-ui, sans-serif;
}
.ixh-scrim { position: absolute; inset: 0; background: rgba(12, 8, 20, .72);
  -webkit-backdrop-filter: blur(7px); backdrop-filter: blur(7px); }
.ixh-panel {
  position: relative;
  width: min(760px, calc(100vw - 36px));
  max-height: calc(100dvh - 48px);
  display: flex; flex-direction: column;
  background: rgba(37, 37, 66, .93);
  border: 1px solid rgba(176, 108, 255, .38);
  outline: 1px solid rgba(255, 105, 180, .16); outline-offset: 5px;
  border-radius: 14px;
  box-shadow: 0 22px 70px rgba(0, 0, 0, .58);
  animation: ixh-rise .34s ease both;
}
@keyframes ixh-rise { from { opacity: 0; transform: translateY(14px) scale(.985); } to { opacity: 1; transform: none; } }
.ixh-head { padding: 20px 26px 12px; border-bottom: 1px dashed rgba(169, 156, 192, .22); }
.ixh-eyebrow, .ixh-label {
  font-size: 12px; font-weight: 700; letter-spacing: .26em; text-transform: uppercase;
  color: var(--intake-dim, #a99cc0);
}
.ixh-title { margin: 6px 0 4px; font-size: 25px; font-weight: 700; color: var(--intake-accent, #ff69b4); }
.ixh-sub { margin: 0; font-size: 14px; color: var(--intake-dim, #a99cc0); }
.ixh-body { overflow-y: auto; overflow-x: hidden; padding: 16px 26px 20px;
  scrollbar-width: thin; scrollbar-color: rgba(176, 108, 255, .5) transparent; }
.ixh-body::-webkit-scrollbar { width: 10px; }
.ixh-body::-webkit-scrollbar-thumb { background: rgba(176, 108, 255, .38); border-radius: 999px; }
.ixh-foot {
  display: flex; justify-content: space-between; align-items: center; gap: 12px;
  padding: 12px 26px 16px; border-top: 1px dashed rgba(169, 156, 192, .22);
}
.ixh-foot-note { font-size: 13px; color: var(--intake-dim, #a99cc0); }

/* --- buttons -------------------------------------------------------------- */
.ixh-btn {
  font: inherit; font-weight: 700; letter-spacing: .06em;
  color: var(--intake-text, #f3e9f6);
  background: rgba(176, 108, 255, .18);
  border: 1px solid rgba(176, 108, 255, .5);
  border-radius: 8px; padding: 8px 18px; cursor: pointer;
  transition: background .15s ease, border-color .15s ease;
}
.ixh-btn:hover { background: rgba(176, 108, 255, .3); border-color: var(--intake-accent, #ff69b4); }
.ixh-btn:focus-visible { outline: 2px dashed var(--intake-accent, #ff69b4); outline-offset: 3px; }

/* --- the drawer ----------------------------------------------------------- */
.ixh-list { display: flex; flex-direction: column; gap: 8px; }
.ixh-file {
  display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: 14px;
  width: 100%; text-align: left; font: inherit; color: inherit; cursor: pointer;
  background: rgba(26, 26, 46, .58);
  border: 1px solid rgba(169, 156, 192, .18);
  border-left: 3px solid rgba(176, 108, 255, .55);
  border-radius: 10px; padding: 11px 16px;
  transition: border-color .15s ease, background .15s ease, transform .15s ease;
}
.ixh-file:hover { background: rgba(37, 37, 66, .8); border-color: rgba(255, 105, 180, .45); transform: translateX(2px); }
.ixh-file:focus-visible { outline: 2px dashed var(--intake-accent, #ff69b4); outline-offset: 2px; }
.ixh-file-grade {
  font-size: 30px; font-weight: 800; line-height: 1;
  color: var(--intake-accent, #ff69b4); min-width: 1.2em; text-align: center;
  text-shadow: 0 0 18px rgba(255, 105, 180, .45);
}
.ixh-file-main { min-width: 0; }
.ixh-file-name { font-size: 17px; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ixh-file-meta { font-size: 12.5px; color: var(--intake-dim, #a99cc0); letter-spacing: .04em; }
.ixh-file-no { font-size: 12px; font-weight: 700; letter-spacing: .18em; color: var(--intake-dim, #a99cc0); }

/* --- the detail card ------------------------------------------------------ */
.ixh-block { margin: 0 0 20px; }
.ixh-judgment { display: flex; align-items: center; gap: 26px; flex-wrap: wrap; }
.ixh-grade { font-size: 76px; font-weight: 800; line-height: 1; color: var(--intake-accent, #ff69b4);
  text-shadow: 0 0 30px rgba(255, 105, 180, .5); }
.ixh-verdict { flex: 1 1 240px; min-width: 200px; }
.ixh-route-name { font-size: 24px; font-weight: 700; color: var(--intake-text, #f3e9f6); }
.ixh-route-share { font-size: 14px; color: var(--intake-accent-2, #b06cff); letter-spacing: .1em; }
.ixh-route-blurb { margin: 6px 0 0; font-size: 14.5px; color: var(--intake-dim, #a99cc0); }
.ixh-route-second { margin-top: 6px; font-size: 13.5px; color: var(--intake-dim, #a99cc0); }

.ixh-stats { display: flex; flex-wrap: wrap; gap: 10px; }
.ixh-stat {
  flex: 1 1 128px; min-width: 118px;
  background: rgba(26, 26, 46, .5); border: 1px solid rgba(169, 156, 192, .16);
  border-radius: 10px; padding: 10px 12px;
}
.ixh-stat-v { font-size: 22px; font-weight: 700; color: var(--intake-accent-2, #b06cff); }
.ixh-stat-k { font-size: 11px; font-weight: 700; letter-spacing: .16em; text-transform: uppercase;
  color: var(--intake-dim, #a99cc0); margin-top: 2px; }

.ixh-rows { display: flex; flex-direction: column; }
.ixh-row { display: flex; justify-content: space-between; gap: 14px; padding: 5px 0;
  border-bottom: 1px dashed rgba(169, 156, 192, .18); font-size: 15px; }
.ixh-row-k { color: var(--intake-dim, #a99cc0); letter-spacing: .07em; text-transform: uppercase;
  font-size: 12.5px; font-weight: 700; align-self: center; }
.ixh-row-v { text-align: right; }

.ixh-quotes { list-style: none; margin: 0; padding: 0; }
.ixh-quote { font-size: 15.5px; font-style: italic; color: var(--intake-text, #f3e9f6);
  padding: 3px 0 3px 12px; border-left: 2px solid rgba(255, 105, 180, .35); margin-bottom: 5px; }

.ixh-chips { display: flex; flex-wrap: wrap; gap: 8px; }
.ixh-chip { display: inline-flex; align-items: center; gap: 6px; font-size: 13px;
  color: var(--intake-dim, #a99cc0); background: rgba(26, 26, 46, .5);
  border: 1px solid rgba(169, 156, 192, .18); border-radius: 999px; padding: 4px 11px; }
.ixh-dot { width: 12px; height: 12px; border-radius: 50%; background: var(--ixh-chip, #fff);
  box-shadow: 0 0 8px var(--ixh-chip, #fff); }

.ixh-grid { display: flex; flex-wrap: wrap; gap: 10px; }
.ixh-tile { margin: 0; width: ${TILE_PX}px; }
.ixh-tile canvas, .ixh-tile img {
  width: ${TILE_PX}px; height: ${TILE_PX}px; display: block; object-fit: cover;
  border-radius: 8px; border: 1px solid rgba(176, 108, 255, .35); background: #0d0a16;
}
.ixh-tile figcaption { font-size: 11px; letter-spacing: .1em; text-transform: uppercase;
  color: var(--intake-dim, #a99cc0); margin-top: 4px; text-align: center;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ixh-tile.is-kept canvas { border-color: var(--intake-accent, #ff69b4); box-shadow: 0 0 14px rgba(255, 105, 180, .4); }
.ixh-tile.is-kept figcaption { color: var(--intake-accent, #ff69b4); }

.ixh-note { font-size: 13px; color: var(--intake-dim, #a99cc0); margin-top: 8px; font-style: italic; }

@media (prefers-reduced-motion: reduce) {
  .ixh-root *, .ixh-panel { animation: none !important; transition: none !important; }
  .ixh-file:hover { transform: none; }
}
`;

/* ----------------------------------------------------------------------------
 * SMALL HELPERS
 * -------------------------------------------------------------------------- */
function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}
const num = (n, d = 0) => (typeof n === 'number' && isFinite(n) ? n : d);
const arr = (a) => (Array.isArray(a) ? a : []);
const obj = (o) => (o && typeof o === 'object' ? o : {});
const str = (s) => (typeof s === 'string' ? s : '');
const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : num(n));
const pct = (n) => Math.round(clamp01(n) * 100) + '%';
const titleCase = (s) => str(s).replace(/(^|[\s-])([a-z])/g, (m, a, b) => a + b.toUpperCase());

/** "21 Jul, 14:22" — local, short, never a raw epoch. */
function stamp(at) {
  const t = num(at);
  if (!t) return 'date not recorded';
  try {
    const d = new Date(t);
    return d.toLocaleDateString(undefined, { day: '2-digit', month: 'short' })
      + ', ' + d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  } catch (_e) { return 'date not recorded'; }
}

/** ms -> "1.4 s" / "820 ms" (latency), and ms -> "12.5 min" (dwell). */
function secs(ms) {
  const v = num(ms);
  return v >= 1000 ? (v / 1000).toFixed(1) + ' s' : Math.round(v) + ' ms';
}
function mins(ms) { return (num(ms) / 60000).toFixed(1); }

/** The bank's colour word for a hex, when it happens to be one of the swatches. */
let SWATCH_NAMES = null;
async function swatchName(hex) {
  const h = str(hex).toLowerCase();
  if (SWATCH_NAMES === null) {
    SWATCH_NAMES = {};
    try {
      const p = await import('../core/palette.js');
      const sw = obj(p.COLOR_SWATCHES);
      for (const k of Object.keys(sw)) SWATCH_NAMES[String(sw[k]).toLowerCase()] = k;
    } catch (_e) { /* no names, just hexes */ }
  }
  return SWATCH_NAMES[h] || h;
}

/* Reduced motion is handled entirely in IXH_CSS (the media query at its foot):
 * this panel's only motion is the panel rise and the row nudge, both CSS. */

function ensureCss() {
  try {
    if (typeof document === 'undefined' || document.getElementById('ixh-css')) return;
    const s = document.createElement('style');
    s.id = 'ixh-css';
    s.textContent = IXH_CSS;
    document.head.appendChild(s);
  } catch (_e) { /* a styleless drawer still lists the files */ }
}

/* ----------------------------------------------------------------------------
 * IS THERE ANYTHING ON FILE?  (the menu's gate — first run shows no button)
 * -------------------------------------------------------------------------- */
/**
 * @param {object=} stats a core/stats.js handle (boot passes its live one)
 * @returns {Promise<number>} how many completed runs are on file (0 = hide the button)
 */
export async function hasArchive(stats) {
  try {
    const s = await resolveStats(stats);
    if (!s || typeof s.recentRuns !== 'function') return 0;
    const rows = await s.recentRuns(LIST_LIMIT);
    return arr(rows).length;
  } catch (_e) { return 0; }   // an unreadable archive is an absent archive
}

/** Use the handle boot supplied; else build one against the same backend. */
async function resolveStats(stats) {
  if (stats && typeof stats.recentRuns === 'function') return stats;
  try {
    const mod = await import('../core/stats.js');
    const make = mod && (mod.createStats || mod.default);
    return typeof make === 'function' ? make() : null;
  } catch (_e) { return null; }
}

/* ----------------------------------------------------------------------------
 * THE DRAWER
 * -------------------------------------------------------------------------- */
/**
 * @param {{parent?:Element, stats?:object, audio?:object}} [opts]
 * @returns {Promise<void>} resolves once the panel is fully torn down.
 */
export function openHistory(opts) {
  opts = opts || {};
  const parent = (opts.parent && opts.parent.appendChild) ? opts.parent
    : (typeof document !== 'undefined' ? document.body : null);
  if (!parent) return Promise.resolve();

  ensureCss();

  return new Promise((resolve) => {
    const audio = opts.audio || null;
    const sfx = (id, i) => { try { if (audio && typeof audio.sfx === 'function') audio.sfx(id, i); } catch (_e) {} };

    const listeners = [];
    const on = (t, type, fn, o) => { try { t.addEventListener(type, fn, o); listeners.push([t, type, fn, o]); } catch (_e) {} };
    /** Everything a detail view spun up (WebGL contexts) — dropped on every swap. */
    let viewCleanups = [];
    const dropView = () => {
      while (viewCleanups.length) { const fn = viewCleanups.pop(); try { fn(); } catch (_e) {} }
    };

    /* --- shell ------------------------------------------------------------- */
    const root = el('div', 'ixh-root');
    root.setAttribute('role', 'dialog');
    root.setAttribute('aria-modal', 'true');
    root.setAttribute('aria-label', TITLE);

    const scrim = el('div', 'ixh-scrim');
    root.appendChild(scrim);

    const panel = el('div', 'ixh-panel');
    root.appendChild(panel);

    const head = el('div', 'ixh-head');
    head.appendChild(el('div', 'ixh-eyebrow', EYEBROW));
    const titleEl = el('h2', 'ixh-title', TITLE);
    head.appendChild(titleEl);
    const subEl = el('p', 'ixh-sub', SUBTITLE);
    head.appendChild(subEl);
    panel.appendChild(head);

    const body = el('div', 'ixh-body');
    panel.appendChild(body);

    const foot = el('div', 'ixh-foot');
    const footNote = el('div', 'ixh-foot-note', '');
    const backBtn = el('button', 'ixh-btn', 'Back to the drawer');
    backBtn.type = 'button';
    backBtn.hidden = true;
    const closeBtn = el('button', 'ixh-btn', 'Close the drawer');
    closeBtn.type = 'button';
    const footBtns = el('div');
    footBtns.style.display = 'flex';
    footBtns.style.gap = '10px';
    footBtns.append(backBtn, closeBtn);
    foot.append(footNote, footBtns);
    panel.appendChild(foot);

    /* --- data -------------------------------------------------------------- */
    let rows = [];      // newest first
    let total = 0;

    async function load() {
      body.appendChild(el('p', 'ixh-sub', 'Retrieving…'));
      const s = await resolveStats(opts.stats);
      try { rows = arr(s && await s.recentRuns(LIST_LIMIT)); }
      catch (_e) { rows = []; }
      total = rows.length;
      showList();
    }

    /* --- view: the drawer -------------------------------------------------- */
    function showList() {
      dropView();
      body.innerHTML = '';
      titleEl.textContent = TITLE;
      subEl.textContent = SUBTITLE;
      backBtn.hidden = true;
      footNote.textContent = total === 1
        ? '1 assessment on file.'
        : `${total} assessments on file.`;

      if (!total) {
        body.appendChild(el('p', 'ixh-sub', EMPTY_LINE));
        return;
      }

      const list = el('div', 'ixh-list');
      rows.forEach((r, i) => {
        const rec = obj(r);
        const recap = obj(rec.recap);
        const cls = obj(recap.classification);
        const btn = el('button', 'ixh-file');
        btn.type = 'button';

        btn.appendChild(el('div', 'ixh-file-grade', str(recap.grade) || '·'));

        const main = el('div', 'ixh-file-main');
        const name = str(cls.primaryName) || titleCase(str(rec.niche)) || 'Unclassified';
        main.appendChild(el('div', 'ixh-file-name', name));
        const bits = [
          stamp(rec.at),
          titleCase(str(rec.deepestBand) || 'calibration'),
          'susceptibility ' + pct(rec.susceptibility != null ? rec.susceptibility : rec.peakDepth),
        ];
        if (rec.endless) bits.push('endless');
        main.appendChild(el('div', 'ixh-file-meta', bits.join(' · ')));
        btn.appendChild(main);

        // File numbers count UP with age of the archive, not down the list, so a
        // given run keeps its number forever: file 1 is the first ever taken.
        btn.appendChild(el('div', 'ixh-file-no', 'FILE ' + String(total - i).padStart(3, '0')));

        on(btn, 'click', () => { sfx('briefing-open', 0.5); showDetail(rec, total - i); });
        list.appendChild(btn);
      });
      body.appendChild(list);
    }

    /* --- view: one file ---------------------------------------------------- */
    function showDetail(rec, fileNo) {
      dropView();
      body.innerHTML = '';
      body.scrollTop = 0;
      backBtn.hidden = false;

      const recap = obj(rec.recap);
      const cls = obj(recap.classification);
      const ans = obj(rec.answers);
      const legacy = !rec.recap;

      titleEl.textContent = 'File ' + String(fileNo).padStart(3, '0');
      subEl.textContent = [
        str(recap.subject) || 'Subject unrecorded',
        stamp(rec.at),
        titleCase(str(rec.niche)),
      ].filter(Boolean).join(' · ');
      footNote.textContent = str(recap.grade) ? 'Composite grade ' + recap.grade + '.' : '';

      /* a. the judgment — the closing card's two headline blocks ------------- */
      const jBlock = el('div', 'ixh-block');
      jBlock.appendChild(el('div', 'ixh-label', 'Judgment'));
      const j = el('div', 'ixh-judgment');
      if (str(recap.grade)) j.appendChild(el('div', 'ixh-grade', recap.grade));
      const verdict = el('div', 'ixh-verdict');
      verdict.appendChild(el('div', 'ixh-route-name',
        str(cls.primaryName) || titleCase(str(rec.niche)) || 'Unclassified'));
      if (num(cls.primaryShare) > 0) {
        verdict.appendChild(el('div', 'ixh-route-share', pct(cls.primaryShare) + ' expression'));
      }
      if (str(cls.primaryBlurb)) verdict.appendChild(el('p', 'ixh-route-blurb', cls.primaryBlurb));
      if (str(cls.secondaryName)) {
        verdict.appendChild(el('div', 'ixh-route-second',
          'Secondary: ' + cls.secondaryName
          + (num(cls.secondaryShare) > 0 ? ' · ' + pct(cls.secondaryShare) : '')));
      }
      j.appendChild(verdict);
      jBlock.appendChild(j);
      if (legacy) jBlock.appendChild(el('div', 'ixh-note', LEGACY_NOTE));
      body.appendChild(jBlock);

      /* b. findings — the outro's three tiles, plus what it never showed you -- */
      const fBlock = el('div', 'ixh-block');
      fBlock.appendChild(el('div', 'ixh-label', 'Findings'));
      const tiles = el('div', 'ixh-stats');
      const tile = (label, value) => {
        const c = el('div', 'ixh-stat');
        c.appendChild(el('div', 'ixh-stat-v', value));
        c.appendChild(el('div', 'ixh-stat-k', label));
        tiles.appendChild(c);
      };
      tile('Susceptibility index', pct(rec.susceptibility != null ? rec.susceptibility : rec.peakDepth));
      tile('Deepest section', str(recap.sectionTitle) || titleCase(str(rec.deepestBand)) || '—');
      tile('Questions answered', String(num(ans.answered, num(rec.beatCount))));
      if (num(rec.maxScore) > 0) tile('Composite score', pct(rec.scoreRate));
      if (num(ans.medianLatencyMs) > 0) tile('Median response', secs(ans.medianLatencyMs));
      if (num(rec.tranceMs) > 0) tile('Time at depth', mins(rec.tranceMs) + ' min');
      fBlock.appendChild(tiles);
      body.appendChild(fBlock);

      /* c. section by section — where compliance actually gave way ----------- */
      const byBand = obj(ans.byBand);
      const bandKeys = Object.keys(byBand);
      if (bandKeys.length) {
        const bBlock = el('div', 'ixh-block');
        bBlock.appendChild(el('div', 'ixh-label', 'Response by section'));
        const rowsWrap = el('div', 'ixh-rows');
        const ORDER = ['calibration', 'establishing', 'deepening', 'climax', 'recovery'];
        bandKeys
          .sort((a, b) => ORDER.indexOf(a) - ORDER.indexOf(b))
          .forEach((band) => {
            const b = obj(byBand[band]);
            if (!num(b.n)) return;
            const row = el('div', 'ixh-row');
            row.appendChild(el('span', 'ixh-row-k', titleCase(band)));
            row.appendChild(el('span', 'ixh-row-v',
              `${num(b.correct)} of ${num(b.n)} · ${pct(num(b.correct) / num(b.n, 1))}`));
            rowsWrap.appendChild(row);
          });
        bBlock.appendChild(rowsWrap);
        body.appendChild(bBlock);
      }

      /* d. conduct — the parts of the run that were done TO you -------------- */
      const cBlock = el('div', 'ixh-block');
      cBlock.appendChild(el('div', 'ixh-label', 'Conduct of the assessment'));
      const cRows = el('div', 'ixh-rows');
      const cRow = (k, v) => {
        const row = el('div', 'ixh-row');
        row.appendChild(el('span', 'ixh-row-k', k));
        row.appendChild(el('span', 'ixh-row-v', v));
        cRows.appendChild(row);
      };
      if (num(ans.answered) > 0) {
        cRow('Assisted commits', `${num(ans.steered)} of ${num(ans.answered)} · ${pct(num(ans.steered) / num(ans.answered, 1))}`);
      }
      const mech = obj(ans.mechanics);
      const mechList = Object.keys(mech)
        .map((k) => ({ k, n: num(mech[k]) }))
        .sort((a, b) => b.n - a.n)
        .slice(0, 5)
        .map((e) => `${titleCase(e.k)} ×${e.n}`)
        .join(', ');
      if (mechList) cRow('Instrument mix', mechList);
      if (num(rec.rewardFired) > 0) {
        cRow('Payouts issued', num(rec.rewardDecoupled) > 0
          ? `${num(rec.rewardFired)}, of which ${num(rec.rewardDecoupled)} unearned`
          : String(num(rec.rewardFired)));
      }
      if (rec.chasedReward) {
        cRow('Reward pursuit', 'Observed · ' + pct(rec.chaseMagnitude) + ' drift');
      }
      if (num(ans.tricks) > 0) cRow('Unanswerable items', String(num(ans.tricks)));
      if (cRows.childNodes.length) { cBlock.appendChild(cRows); body.appendChild(cBlock); }

      /* e. recorded statements ---------------------------------------------- */
      const mantras = arr(rec.affirmedMantras).filter(Boolean);
      if (mantras.length) {
        const mBlock = el('div', 'ixh-block');
        mBlock.appendChild(el('div', 'ixh-label', 'Recorded statements'));
        const ul = el('ul', 'ixh-quotes');
        for (const m of mantras.slice(0, 10)) ul.appendChild(el('li', 'ixh-quote', '“' + str(m) + '”'));
        mBlock.appendChild(ul);
        body.appendChild(mBlock);
      }

      /* f. colours named ------------------------------------------------------ */
      const colors = arr(recap.colors).filter(Boolean);
      if (colors.length) {
        const kBlock = el('div', 'ixh-block');
        kBlock.appendChild(el('div', 'ixh-label', 'Colours you named'));
        const chips = el('div', 'ixh-chips');
        for (const hex of colors) {
          const chip = el('span', 'ixh-chip');
          const dot = el('span', 'ixh-dot');
          try { dot.style.setProperty('--ixh-chip', hex); } catch (_e) {}
          const word = el('span', null, hex);
          swatchName(hex).then((n) => { try { word.textContent = n; } catch (_e) {} });
          chip.append(dot, word);
          chips.appendChild(chip);
        }
        kBlock.appendChild(chips);
        body.appendChild(kBlock);
      }

      /* g. the weave — the spirals your colours wove, and which you kept ----- */
      mountWeave(body, recap, viewCleanups);

      /* h. payloads issued — what the run actually flashed at you ------------ */
      const media = arr(recap.media);
      if (media.length) {
        const pBlock = el('div', 'ixh-block');
        pBlock.appendChild(el('div', 'ixh-label', 'Payloads issued'));
        const grid = el('div', 'ixh-grid');
        for (const m of media.slice(0, 12)) {
          const o = obj(m);
          const fig = el('figure', 'ixh-tile');
          const img = document.createElement('img');
          img.alt = '';
          img.loading = 'lazy';
          img.draggable = false;
          // A file the user has since deleted simply drops out of the strip.
          img.addEventListener('error', () => { try { fig.remove(); } catch (_e) {} }, { once: true });
          img.src = str(o.url);
          fig.appendChild(img);
          fig.appendChild(el('figcaption', null,
            fileNameOf(o.url) + (num(o.count) > 1 ? ' ×' + num(o.count) : '')));
          grid.appendChild(fig);
        }
        pBlock.appendChild(grid);
        const shown = num(recap.mediaShown);
        pBlock.appendChild(el('div', 'ixh-note', shown > media.length
          ? `${media.length} distinct, shown ${shown} times in all.`
          : `${media.length} shown.`));
        body.appendChild(pBlock);
      } else if (recap.thinned) {
        const tBlock = el('div', 'ixh-block');
        tBlock.appendChild(el('div', 'ixh-label', 'Payloads issued'));
        tBlock.appendChild(el('div', 'ixh-note', THINNED_NOTE));
        body.appendChild(tBlock);
      }

      /* i. disposition — what was drafted out of you ------------------------- */
      const dBlock = el('div', 'ixh-block');
      dBlock.appendChild(el('div', 'ixh-label', 'Disposition'));
      const dRows = el('div', 'ixh-rows');
      const ses = obj(recap.session);
      const drow = (k, v) => {
        const row = el('div', 'ixh-row');
        row.appendChild(el('span', 'ixh-row-k', k));
        row.appendChild(el('span', 'ixh-row-v', v));
        dRows.appendChild(row);
      };
      if (ses.delivered === 'host') {
        drow('Session drafted', str(ses.name) || 'yes — see your Sessions');
        dRows.appendChild(noteRow('Filed into your Sessions when this assessment closed.'));
      } else if (recap.session) {
        drow('Session drafted', 'None — this terminal kept the result only');
      }
      const keeps = arr(recap.keepsakes);
      const loom = keeps.filter((k) => obj(k).kind === 'loom');
      const pngs = keeps.filter((k) => obj(k).kind === 'png');
      if (loom.length) {
        drow('Kept in the Loom', loom.map((k) => str(obj(k).label) || ('No. ' + num(obj(k).index))).join(', '));
      }
      if (pngs.length) drow('Printed', pngs.length === 1 ? '1 spiral' : pngs.length + ' spirals');
      if (num(rec.runMs) > 0) drow('Elapsed', mins(rec.runMs) + ' min');
      if (rec.endless) drow('Mode', 'Endless — no scheduled end');
      if (dRows.childNodes.length) { dBlock.appendChild(dRows); body.appendChild(dBlock); }
    }

    function noteRow(text) { return el('div', 'ixh-note', text); }

    /* --- dismissal --------------------------------------------------------- */
    let closed = false;
    const prevFocus = (typeof document !== 'undefined') ? document.activeElement : null;

    function close() {
      if (closed) return;
      closed = true;
      dropView();
      for (const [t, type, fn, o] of listeners) {
        try { t.removeEventListener(type, fn, o); } catch (_e) {}
      }
      listeners.length = 0;
      try { if (root.parentNode) root.parentNode.removeChild(root); } catch (_e) {}
      try { if (prevFocus && prevFocus.focus) prevFocus.focus(); } catch (_e) {}
      resolve();
    }

    on(closeBtn, 'click', () => { sfx('sticker-drag', 0.4); close(); });
    on(backBtn, 'click', () => { sfx('sticker-drag', 0.4); showList(); try { closeBtn.focus(); } catch (_e) {} });
    on(scrim, 'click', () => close());
    // Capture + stopPropagation: Escape closes THIS drawer and must not also
    // reach the menu underneath and pop two layers at once.
    on(document, 'keydown', (e) => {
      if (e.key === 'Escape' || e.key === 'Esc') {
        e.preventDefault(); e.stopPropagation();
        if (!backBtn.hidden) { showList(); return; }   // detail -> drawer -> closed
        close();
        return;
      }
      if (e.key !== 'Tab') return;
      const focusables = Array.prototype.filter.call(
        panel.querySelectorAll('button, [tabindex]:not([tabindex="-1"])'),
        (n) => !n.disabled && !n.hidden && n.offsetParent !== null,
      );
      if (!focusables.length) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }, true);

    parent.appendChild(root);
    try { closeBtn.focus(); } catch (_e) {}
    load();
  });
}

/** "https://ccp.assets/images/foo bar.gif" -> "foo bar.gif" (display only). */
function fileNameOf(url) {
  const u = str(url);
  const cut = u.split(/[?#]/)[0].split('/');
  let name = cut[cut.length - 1] || u;
  try { name = decodeURIComponent(name); } catch (_e) { /* keep it raw */ }
  return name.length > 22 ? name.slice(0, 20) + '…' : name;
}

/* ----------------------------------------------------------------------------
 * THE WEAVE, RE-HUNG.
 *
 * The outro's gallery, but STILL: one shared offscreen field renderer paints one
 * frame per stored spiral and is released immediately. No rAF, no per-tile WebGL
 * context, nothing left running behind the panel — the archive is a place you
 * read, not a place that performs. Tiles the run KEPT (loom) wear the accent.
 * Every failure path (no loomField module, no WebGL, a malformed param blob)
 * degrades to fewer tiles, never to a broken block.
 * -------------------------------------------------------------------------- */
function mountWeave(body, recap, cleanups) {
  const spirals = arr(recap.spirals);
  const rolled = num(recap.spiralsRolled);
  const kept = arr(recap.keepsakes).filter((k) => obj(k).kind === 'loom').map((k) => num(obj(k).index));
  if (!spirals.length) {
    if (recap.thinned && rolled > 0) {
      const block = el('div', 'ixh-block');
      block.appendChild(el('div', 'ixh-label', 'Woven from your colours'));
      block.appendChild(el('div', 'ixh-note',
        `${rolled} spirals were woven. The plates have since been discharged.`));
      body.appendChild(block);
    }
    return;
  }

  const block = el('div', 'ixh-block');
  block.appendChild(el('div', 'ixh-label', 'Woven from your colours'));
  const grid = el('div', 'ixh-grid');
  block.appendChild(grid);
  if (rolled > spirals.length) {
    block.appendChild(el('div', 'ixh-note', `${spirals.length} of ${rolled} kept from that descent.`));
  }
  body.appendChild(block);

  // Async, optional, and fully after-the-fact: the block is already in the
  // document, so a failed import costs the captions, not the section.
  (async () => {
    let m = null;
    try { m = await import('../../dtrh/shared/loomField.js'); }
    catch (_e) { try { block.remove(); } catch (_e2) {} return; }
    if (!grid.isConnected) return;

    let field = null;
    try {
      const off = document.createElement('canvas');
      off.width = TILE_PX; off.height = TILE_PX;
      field = m.createFieldRenderer(off);
    } catch (_e) { field = null; }
    // Release the one context as soon as the stills are painted (and on close).
    const release = () => {
      try {
        if (field && field.gl) {
          const lose = field.gl.getExtension('WEBGL_lose_context');
          if (lose) lose.loseContext();
        }
      } catch (_e) {}
      field = null;
    };
    cleanups.push(release);

    spirals.forEach((params, i) => {
      let q;
      try { q = m.normalizeParams2(params); } catch (_e) { return; }
      const fig = el('figure', 'ixh-tile');
      if (kept.indexOf(i + 1) >= 0) fig.classList.add('is-kept');
      const canvas = document.createElement('canvas');
      canvas.width = TILE_PX; canvas.height = TILE_PX;
      const c2d = canvas.getContext('2d');
      if (!c2d) return;
      try {
        if (field) m.composeFrame(c2d, field, q, 0, TILE_PX, TILE_PX);
        else m.drawFallbackFrame(c2d, q, 0, TILE_PX, TILE_PX);
      } catch (_e) { /* one bad plate must not stop the gallery */ }
      fig.appendChild(canvas);
      fig.appendChild(el('figcaption', null,
        kept.indexOf(i + 1) >= 0 ? 'No. ' + (i + 1) + ' · kept' : 'No. ' + (i + 1)));
      grid.appendChild(fig);
    });

    release();
    if (!grid.childNodes.length) { try { block.remove(); } catch (_e) {} }
  })();
}

export default openHistory;
