/* ============================================================================
 * render/captcha/recall.js — VerifyRecall ("The Recognition Audit")   [next-tier]
 *
 * CAPTCHA_BRAINSTORM.md #11 (the Recovery closer). An old/new memory paradigm
 * dressed as a VeriTru re-verification: "select every item you were shown during
 * this assessment." The audit grid mixes REAL stimuli (the run's own payloads,
 * read back from core/mediaLog.js), never-shown LURES (host-sampled assets that
 * were never actually displayed, wrapped in a seeded "lure frame" style so they
 * feel almost familiar), and ONE plaintext mantra card (a praise line from the
 * theme, styled as a media card). Every possible response is scored as evidence
 * the intake worked; the file closes itself, kindly.
 *
 * This item's HOME is RECOVERY: it reaches the renderer via the engine's
 * recovery-substitution plan (buildRecoverySubPlan/buildRecoveryBeat), which pins
 * the family's heat-0 `_rec` prompt to a middle walk-down beat with Steer.None,
 * heat 0, timeoutMs 0. The other four bands are SUPPORTING MATERIAL that plant
 * and rehearse the trap during the descent:
 *
 *   Calibration  THE PLANTED SEED. A straight "style acclimation" item: a small
 *                row of sample frames displayed HONESTLY in the exact lure-frame
 *                style, "confirm legibility". No trap, no corruption — spent later.
 *   Establishing light single-item recognition: "was this item presented?" with
 *                yes / no / not sure. Honest and completable (the shown card is a
 *                genuinely-shown payload, so "yes" is the accurate answer).
 *   Deepening    the same single-item recognition check, one heat deeper.
 *   Climax       a harder single-item recognition check with "not sure" REMOVED
 *                (yes / no only).
 *   Recovery     THE FULL old/new audit grid described above. Verdicts stay
 *                gentle/deadpan (sanctuary, invariant #3) and the file closes with
 *                "audit complete. your file is consistent. thank you."
 *
 * GRADING — every band folds its selection into ONE derived bucket, mapped to a
 * single option index via ctx.submitIndex (the bank's per-band `answer` names the
 * compliant bucket; labels are NEVER displayed as buttons):
 *   0  denial            (nothing selected / decline)      "denial of exposure is
 *                                                            inconsistent with your record"
 *   1  recognition       (only true-shown items, or "yes") the accurate/compliant read
 *   2  source-monitoring (a lure endorsed, or "not sure")  the planted trap sprung
 *   3  endorsement       (the mantra card selected)        the words, kept
 * All four paths land; refusal (0) is a first-class graded answer, never a lockout.
 *
 * INVARIANTS (CLAUDE.md §10 / CAPTCHA_HANDOFF.md §4): nothing throws at import
 * (the mediaLog import is DOM-less + import-safe, and every document/window touch
 * is guarded); only touch ctx.root once committed to returning true; captcha
 * chrome IS the friction so ctx.installSteering is skipped; VERIFY, the choice
 * buttons and the "decline audit" hatch all commit (no lockout); synthetic clicks
 * pass (handlers never gate on isTrusted) so ctx.forceComplete is honored; a
 * nonzero ctx.timeoutMs is honored via ctx.submitTimeout (Recovery is untimed, 0);
 * ctx.reduced -> every card static; gif cards are whole <img>s in overflow:hidden
 * wrappers, distinct animated gifs capped at 3, the rest frozen (canvas first
 * frame) until hovered/selected; ZERO corruption treatments in Recovery; real
 * filenames appear NOWHERE (cards are thumbnails only); `is-correct`/`is-answer`
 * never appear (selection uses chrome's own ixcap-sel + our ixr-* classes); no
 * audio handle held (ctx.sfx seam only), and Recovery audio is kept soft.
 * ==========================================================================*/

import { recordedMedia } from '../../core/mediaLog.js';

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLE — all niche-flavored VERDICT/UI strings live HERE (the bank
 * text is niche-agnostic shared prompts; the bank's nicheOverrides only carry
 * per-niche PROMPT text variants for circe's register). circe register is a hard
 * rule and, because Recovery is a sanctuary, ALL niches stay gentle/deadpan here:
 * no exclamation marks, no diminutives, no em-dashes anywhere in this table.
 * -------------------------------------------------------------------------- */
const STRINGS = Object.freeze({
  bambi: {
    decline: 'decline audit',
    yes: 'yes', no: 'no', unsure: 'not sure',
    calVerdict: 'legibility confirmed. these frames are familiar now.',
    deny: 'denial of exposure is inconsistent with your response record.',
    affirm: 'recognition consistent with the record.',
    lure: 'this item was not presented. source-monitoring error logged. material absorbed below presentation threshold.',
    endorse: 'you kept the words. recorded.',
    recClose: 'audit complete. your file is consistent. thank you.',
  },
  drone: {
    decline: 'decline audit',
    yes: 'yes', no: 'no', unsure: 'not sure',
    calVerdict: 'legibility confirmed. frame set cached.',
    deny: 'denial of exposure is inconsistent with the response record.',
    affirm: 'recognition consistent with the record.',
    lure: 'this input was not presented. source-monitoring error logged. material absorbed below presentation threshold.',
    endorse: 'designation retained. recorded.',
    recClose: 'audit complete. this file is consistent. compliance archived.',
  },
  sissy: {
    decline: 'decline audit',
    yes: 'yes', no: 'no', unsure: 'not sure',
    calVerdict: 'legibility confirmed. these frames are familiar now.',
    deny: 'denial of exposure is inconsistent with your response record.',
    affirm: 'recognition consistent with the record.',
    lure: 'this item was not presented. source-monitoring error logged. material absorbed below presentation threshold.',
    endorse: 'you kept the words. recorded.',
    recClose: 'audit complete. your file is consistent. thank you.',
  },
  circe: {
    decline: 'decline audit',
    yes: 'yes', no: 'no', unsure: 'not sure',
    calVerdict: 'legibility confirmed. the frames are known to you now.',
    deny: 'denial of exposure is inconsistent with your record.',
    affirm: 'recognition consistent with the record.',
    lure: 'this item was not presented. a source-monitoring error is logged. the material was absorbed below the threshold you were shown.',
    endorse: 'you endorsed the words yourself. recorded.',
    recClose: 'audit complete. your file is consistent. thank you.',
  },
});
function stringsFor(niche) { return STRINGS[niche] || STRINGS.bambi; }

/* ----------------------------------------------------------------------------
 * SEAMS / small DOM-less-safe helpers.
 * -------------------------------------------------------------------------- */
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcap/recall: ' + msg } }));
    }
  } catch (_e) {}
}
function uniq(arr) {
  const out = [], seen = new Set();
  for (const u of (arr || [])) { if (u && !seen.has(u)) { seen.add(u); out.push(u); } }
  return out;
}
function shuffle(a) {
  for (let i = a.length - 1; i > 0; i--) { const j = (Math.random() * (i + 1)) | 0; const t = a[i]; a[i] = a[j]; a[j] = t; }
  return a;
}
/** The run's actual payloads, read back from the ledger. Tolerates empty state. */
function shownUrls() {
  try {
    const arr = recordedMedia();
    return Array.isArray(arr) ? arr.map((e) => e && e.url).filter(Boolean) : [];
  } catch (_e) { return []; }
}
/** A mantra/praise line for the plaintext card, pulled from the theme pack. */
function mantraLineFrom(theme, seed) {
  try {
    const pool = (theme && Array.isArray(theme.praise) && theme.praise.length) ? theme.praise : ['good'];
    const raw = String(pool[Math.abs(seed | 0) % pool.length] || 'good');
    return raw.charAt(0).toUpperCase() + raw.slice(1);
  } catch (_e) { return 'Good'; }
}

/* ----------------------------------------------------------------------------
 * THE ONE INJECTED CSS LITERAL (id 'ix-recall-css'). All classes prefixed 'ixr-'.
 * Follows the IXCAP_CSS precedent; never lands in styles.css.
 * -------------------------------------------------------------------------- */
const IXR_STYLE_ID = 'ix-recall-css';
const IXR_CSS = `
.ixr-body { display: flex; flex-direction: column; gap: 10px; }

/* ---- CALIBRATION SEED: a straight row of sample frames in the lure style ---- */
.ixr-seed-row { display: flex; gap: 8px; justify-content: center; padding: 6px 0 2px; }
.ixr-seed-card {
  position: relative; width: 88px; height: 88px;
  border-radius: 2px; overflow: hidden; background: #e9ebed; flex: 0 0 auto;
}
.ixr-seed-card > img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; display: block; -webkit-user-drag: none; user-drag: none; }

/* ---- THE LURE FRAME STYLE (planted at Calibration, reused verbatim in the
 *      Recovery audit so the lures feel almost familiar). Subtle, not alarming. */
.ixr-lure::after {
  content: ''; position: absolute; inset: 4px; z-index: 2; pointer-events: none;
  border: 2px dashed color-mix(in srgb, var(--intake-accent, #d93025) 55%, #ffffff);
  border-radius: 2px;
}
.ixr-lure::before {
  content: ''; position: absolute; top: 5px; right: 5px; z-index: 3; pointer-events: none;
  width: 7px; height: 7px; border-radius: 50%;
  background: var(--intake-accent, #d93025); opacity: .82;
}

/* ---- SINGLE-ITEM RECOGNITION (est / deep / cli) ---- */
.ixr-single { display: flex; flex-direction: column; align-items: center; gap: 14px; padding: 6px 0 2px; }
.ixr-single-card {
  position: relative; width: 152px; height: 152px;
  border-radius: 3px; overflow: hidden; background: #e9ebed;
}
.ixr-single-card > img, .ixr-single-card > canvas {
  position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; display: block;
  -webkit-user-drag: none; user-drag: none;
}
.ixr-choices { display: flex; gap: 10px; flex-wrap: wrap; justify-content: center; }
.ixr-choice {
  appearance: none; cursor: pointer; font: inherit; font-size: 13px; font-weight: 600;
  color: #202124; background: #f1f3f4; border: 1px solid #d3d6da;
  border-radius: 4px; padding: 9px 18px; transition: background .12s ease;
}
.ixr-choice:hover { background: #e6e9ec; }
.ixr-choice:active { background: #dadde0; }

/* ---- THE AUDIT GRID: plaintext mantra card + frozen tiles ---- */
.ixr-frozen { position: absolute; inset: 0; width: 100%; height: 100%; display: block; z-index: 1; }
.ixr-mantra { display: flex; align-items: center; justify-content: center; padding: 8px; background: #f3f1ea; }
.ixr-mantra-txt {
  font-size: 13px; font-weight: 600; font-style: italic; color: #3a3a44;
  text-align: center; line-height: 1.25; letter-spacing: .2px; z-index: 1;
}

/* ---- VERDICT + KIND CLOSE (soft, no rubber-stamp violence) ---- */
.ixr-verdict {
  margin-top: 8px; font-size: 13px; line-height: 1.4; color: #3c4043;
  padding: 8px 10px; background: #f6f7f8; border-left: 3px solid #9aa0a6;
  border-radius: 0 4px 4px 0; opacity: 0; animation: ixr-fade .3s ease forwards;
}
.ixr-verdict.ixr-v-flag { border-left-color: var(--intake-accent, #d93025); }
.ixr-verdict.ixr-v-ok { border-left-color: #1e8e3e; }
.ixr-close { margin-top: 6px; font-size: 13px; font-weight: 600; color: #1e8e3e; opacity: 0; animation: ixr-fade .4s ease .1s forwards; }
@keyframes ixr-fade { to { opacity: 1; } }
@media (prefers-reduced-motion: reduce) {
  .ixr-verdict, .ixr-close { animation: none; opacity: 1; }
}
`;
function ensureRecallCss() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return;
    if (!document.getElementById || document.getElementById(IXR_STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = IXR_STYLE_ID;
    s.textContent = IXR_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}
function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}

/**
 * @param {import('./index.js').CaptchaCtx} ctx
 * @param {import('./index.js').CaptchaHelpers} helpers
 * @returns {boolean}
 */
export function render(ctx, helpers) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return false;
    if (!ctx || !ctx.root) return false;
    const chrome = (helpers && helpers.chrome) || ctx.chrome;
    if (!chrome || typeof chrome.frame !== 'function' || typeof chrome.gridShell !== 'function') return false;

    ensureRecallCss();

    const band = String(ctx.band || '').toLowerCase();
    const niche = ctx.niche || 'bambi';
    const S = stringsFor(niche);
    const reduced = !!ctx.reduced;
    const prompt = ctx.prompt || {};
    const meta = ctx.meta || {};
    const qIndex = (typeof meta.qIndex === 'number') ? meta.qIndex : 0;

    // ---- media pools --------------------------------------------------------
    const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
    const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
    const allMedia = uniq([...gifs, ...imgs]);
    const gifSet = new Set(gifs);

    // "old" = the run's real payloads (from the ledger) that are in our sample;
    // "lures" = sampled assets that were NEVER shown. Degrade gracefully when the
    // ledger is empty/unavailable: split the sample deterministically so the
    // fiction still works (never crash, never block).
    const shownSet = new Set(shownUrls());
    let oldUrls = allMedia.filter((u) => shownSet.has(u));
    let lureUrls = allMedia.filter((u) => !shownSet.has(u));
    if (oldUrls.length === 0 && allMedia.length) {
      const half = Math.max(1, Math.ceil(allMedia.length / 2));
      oldUrls = allMedia.slice(0, half);
      lureUrls = allMedia.slice(half);
    }

    // ---- build the card OFF the live stage; attach only on success ----------
    const verifyLabel = (band === 'calibration') ? 'CONFIRM' : 'VERIFY';
    const built = chrome.frame({
      instruction: prompt.text != null ? prompt.text : 'Confirm what you recognize.',
      band: ctx.band,
      hatch: S.decline,
      verifyLabel,
    });
    if (!built || !built.root || !built.body) return false;
    built.body.classList.add('ixr-body');

    let done = false;
    const timers = [];
    function clearTimers() { for (const t of timers) { try { clearTimeout(t); } catch (_e) {} } timers.length = 0; }
    if (typeof ctx.onCleanup === 'function') ctx.onCleanup(clearTimers);

    // ---- shared visual helpers ---------------------------------------------
    function freezeInto(container, url, box) {
      const D = box || 152;
      try {
        if (typeof Image !== 'function') { const i = el('img'); i.src = url; container.appendChild(i); return; }
        const im = new Image();
        try { im.crossOrigin = 'anonymous'; } catch (_e) {}
        im.onload = () => {
          if (done) return;
          try {
            const cv = document.createElement('canvas');
            cv.width = D; cv.height = D;
            const c = cv.getContext ? cv.getContext('2d') : null;
            if (!c) { const i = el('img'); i.src = url; container.appendChild(i); return; }
            const iw = im.naturalWidth || im.width || D, ih = im.naturalHeight || im.height || D;
            const sc = Math.max(D / iw, D / ih), w = iw * sc, h = ih * sc;
            c.drawImage(im, (D - w) / 2, (D - h) / 2, w, h);
            cv.className = 'ixr-frozen';
            container.appendChild(cv);
          } catch (_e) { try { const i = el('img'); i.src = url; container.appendChild(i); } catch (_e2) {} }
        };
        im.onerror = () => { if (!done) { try { const i = el('img'); i.src = url; container.appendChild(i); } catch (_e) {} } };
        im.src = url;
      } catch (_e) {}
    }

    // ---- grading + guaranteed commit ---------------------------------------
    function verdictFor(bucket) {
      if (band === 'calibration') return { text: (bucket === 0 ? S.deny : S.calVerdict), tone: (bucket === 0 ? 'flag' : 'ok') };
      if (bucket === 0) return { text: S.deny, tone: 'flag' };
      if (bucket === 2) return { text: S.lure, tone: 'flag' };
      if (bucket === 3) return { text: S.endorse, tone: 'ok' };
      return { text: S.affirm, tone: 'ok' };
    }
    function commit(bucket) {
      if (done) return;
      done = true;
      clearTimers();
      const isRec = band === 'recovery';
      const v = verdictFor(bucket);
      try {
        built.body.appendChild(el('div', 'ixr-verdict ' + (v.tone === 'flag' ? 'ixr-v-flag' : 'ixr-v-ok'), v.text));
      } catch (_e) {}
      // Recovery audio stays soft; descent bands get the standard verdict thunk.
      try { ctx.sfx(isRec ? 'captcha-verify-ok' : 'stamp', isRec ? 0.3 : 0.4); } catch (_e) {}
      if (isRec) {
        // the file closes itself, kindly.
        const tc = setTimeout(() => {
          if (done !== true) return;
          try { built.body.appendChild(el('div', 'ixr-close', S.recClose)); } catch (_e) {}
          try { ctx.sfx('chime', 0.22); } catch (_e) {}
        }, 300);
        timers.push(tc);
      }
      const hold = isRec ? 980 : 660;
      const t = setTimeout(() => {
        try { ctx.submitIndex(bucket); } catch (e) { ilog('submitIndex failed: ' + (e && e.message)); }
      }, hold);
      timers.push(t);
    }

    // ---- BAND: calibration (the planted seed) -------------------------------
    function renderSeed() {
      const row = el('div', 'ixr-seed-row');
      const samplePool = uniq([...(imgs || []), ...(gifs || [])]).slice(0, 3);
      const kinds = ['bus', 'crosswalk', 'stapler'];
      for (let i = 0; i < 3; i++) {
        const cardEl = el('div', 'ixr-seed-card ixr-lure');
        const url = samplePool[i];
        const im = el('img'); im.alt = ''; im.draggable = false;
        try { im.src = url || (chrome.placeholderTile ? chrome.placeholderTile(kinds[i % 3], i) : ''); } catch (_e) {}
        cardEl.appendChild(im);
        row.appendChild(cardEl);
      }
      built.body.appendChild(row);
      try { ctx.sfx('grid-settle', 0.16); } catch (_e) {}
      if (built.verifyBtn) built.verifyBtn.addEventListener('click', () => commit(1));   // confirm legibility
    }

    // ---- BANDS: establishing / deepening / climax (single-item recognition) --
    function renderSingle(withUnsure) {
      const wrap = el('div', 'ixr-single');
      const cardEl = el('div', 'ixr-single-card');
      // Show a genuinely-shown payload when we have one so "yes" is the honest,
      // completable answer; otherwise fall back to any sample / a placeholder.
      const url = oldUrls[0] || allMedia[0] || null;
      const isGif = url ? gifSet.has(url) : false;
      if (url) {
        if (isGif && !reduced) { const im = el('img'); im.alt = ''; im.draggable = false; im.src = url; cardEl.appendChild(im); }
        else if (isGif) { freezeInto(cardEl, url, 152); }
        else { const im = el('img'); im.alt = ''; im.draggable = false; im.src = url; cardEl.appendChild(im); }
      } else {
        const im = el('img'); im.alt = ''; im.draggable = false;
        try { im.src = chrome.placeholderTile ? chrome.placeholderTile('stapler', 3) : ''; } catch (_e) {}
        cardEl.appendChild(im);
      }
      wrap.appendChild(cardEl);

      const choices = el('div', 'ixr-choices');
      const opts = withUnsure
        ? [[S.yes, 1], [S.no, 0], [S.unsure, 2]]
        : [[S.yes, 1], [S.no, 0]];
      opts.forEach(([label, bucket]) => {
        const b = el('button', 'ixr-choice', label); b.type = 'button';
        b.addEventListener('click', () => { try { ctx.sfx('verify-tick', 0.2); } catch (_e) {} commit(bucket); });
        choices.appendChild(b);
      });
      wrap.appendChild(choices);
      built.body.appendChild(wrap);
      if (built.verifyBtn) built.verifyBtn.style.display = 'none';   // the choice buttons are the action
    }

    // ---- BAND: recovery (the full old/new audit grid) -----------------------
    function renderAudit() {
      // Compose up to 9 cards: real-shown + lures + one mantra card.
      const oldPick = shuffle(oldUrls.slice()).slice(0, 5);
      const lurePick = shuffle(lureUrls.slice()).slice(0, 3);
      const cards = [];
      for (const u of oldPick) cards.push({ kind: 'old', url: u, isGif: gifSet.has(u) });
      for (const u of lurePick) cards.push({ kind: 'lure', url: u, isGif: gifSet.has(u) });
      if (cards.length === 0) {
        // no media at all: two placeholder "old" cards so the grid + mantra still read.
        const kinds = ['hydrant', 'bus'];
        for (let i = 0; i < 2; i++) {
          let src = ''; try { src = chrome.placeholderTile ? chrome.placeholderTile(kinds[i], i) : ''; } catch (_e) {}
          cards.push({ kind: 'old', url: src, isGif: false });
        }
      }
      cards.push({ kind: 'mantra', text: mantraLineFrom(ctx.theme, qIndex) });
      shuffle(cards);
      const count = Math.min(9, cards.length);
      cards.length = count;

      const cols = count <= 4 ? 2 : 3;
      const shell = chrome.gridShell(count, cols);
      if (!shell || !shell.grid || !shell.tiles || shell.tiles.length !== count) return false;
      built.body.appendChild(shell.grid);

      const recs = [];
      const LIVE_CAP = reduced ? 0 : 3;
      let liveCount = 0;

      function mountLive(rec) {
        if (rec.live || done) return;
        rec.live = true;
        try { rec.tile.setImage(rec.url); } catch (_e) {}
        if (rec.canvas) { try { rec.canvas.remove(); } catch (_e) {} rec.canvas = null; }
      }
      function mountFrozen(rec) {
        const url = rec.url;
        try {
          if (typeof Image !== 'function') { try { rec.tile.setImage(url); } catch (_e) {} return; }
          const im = new Image();
          try { im.crossOrigin = 'anonymous'; } catch (_e) {}
          im.onload = () => {
            if (done || rec.live) return;
            try {
              const cv = document.createElement('canvas'); cv.width = 120; cv.height = 120;
              const c = cv.getContext ? cv.getContext('2d') : null;
              if (!c) { try { rec.tile.setImage(url); } catch (_e) {} return; }
              const iw = im.naturalWidth || im.width || 120, ih = im.naturalHeight || im.height || 120;
              const sc = Math.max(120 / iw, 120 / ih), w = iw * sc, h = ih * sc;
              c.drawImage(im, (120 - w) / 2, (120 - h) / 2, w, h);
              cv.className = 'ixr-frozen';
              rec.canvas = cv;
              rec.tile.el.insertBefore(cv, rec.tile.el.firstChild);
            } catch (_e) { try { rec.tile.setImage(url); } catch (_e2) {} }
          };
          im.onerror = () => { if (!done) { try { rec.tile.setImage(url); } catch (_e) {} } };
          im.src = url;
        } catch (_e) { try { rec.tile.setImage(url); } catch (_e2) {} }
      }

      cards.forEach((card, i) => {
        const tile = shell.tiles[i];
        const rec = { tile, kind: card.kind, url: card.url, isGif: !!card.isGif, selected: false, live: false, canvas: null };
        if (card.kind === 'mantra') {
          tile.el.classList.add('ixr-mantra');
          tile.el.insertBefore(el('div', 'ixr-mantra-txt', card.text), tile.el.firstChild);
        } else {
          if (card.kind === 'lure') tile.el.classList.add('ixr-lure');
          if (rec.isGif && !reduced && liveCount < LIVE_CAP) { mountLive(rec); liveCount++; }
          else if (rec.isGif) {
            mountFrozen(rec);   // frozen until hovered/selected
            tile.el.addEventListener('pointerenter', () => { if (!reduced && !done) mountLive(rec); });
          } else { try { tile.setImage(card.url); } catch (_e) {} }   // static image / placeholder
        }
        tile.el.addEventListener('click', () => {
          if (done) return;
          const now = !tile.isSelected();
          tile.select(now);
          rec.selected = now;
          if (now && rec.isGif && !reduced) mountLive(rec);   // selected cards animate
          try { ctx.sfx('verify-tick', 0.12); } catch (_e) {}   // soft in Recovery
        });
        recs.push(rec);
      });
      try { ctx.sfx('grid-settle', 0.16); } catch (_e) {}

      function deriveBucket() {
        let sel = 0, lure = 0, mantra = 0;
        for (const r of recs) {
          if (!r.selected) continue;
          sel++;
          if (r.kind === 'lure') lure++;
          else if (r.kind === 'mantra') mantra++;
        }
        if (sel === 0) return 0;   // denial of exposure
        if (lure > 0) return 2;    // source-monitoring error
        if (mantra > 0) return 3;  // the words, kept
        return 1;                  // recognition consistent (only true-shown items)
      }
      if (built.verifyBtn) built.verifyBtn.addEventListener('click', () => commit(deriveBucket()));
      return true;
    }

    // ---- dispatch -----------------------------------------------------------
    if (band === 'calibration') renderSeed();
    else if (band === 'recovery') { if (renderAudit() === false) return false; }
    else if (band === 'climax') renderSingle(false);   // "not sure" removed
    else renderSingle(true);                            // establishing / deepening

    // The "decline audit" hatch is ALWAYS a graded refusal (bucket 0), never a
    // lockout. forceComplete stays honored by the `done` guard + synthetic clicks.
    if (built.hatchLink) built.hatchLink.addEventListener('click', () => commit(0));

    // Honor a nonzero timeout (descent bands) via ctx.submitTimeout; Recovery is
    // untimed (timeoutMs 0) so no clock is armed there.
    if (typeof ctx.timeoutMs === 'number' && ctx.timeoutMs > 0 && typeof ctx.submitTimeout === 'function') {
      const tt = setTimeout(() => { if (done) return; done = true; try { ctx.submitTimeout(); } catch (_e) {} }, ctx.timeoutMs);
      timers.push(tt);
    }

    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    ctx.root.appendChild(built.root);
    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // partial builds never reached ctx.root -> clean fall-back
  }
}

export default { render };
