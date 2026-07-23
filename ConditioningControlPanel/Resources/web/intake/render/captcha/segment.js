/* ============================================================================
 * render/captcha/segment.js — VerifySegment ("Segmentation striptease")  [next-tier]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #7, fully laddered. A dead-straight reCAPTCHA
 * "VeriTru" 4x4 segmentation grid laid over ONE image; each selected square
 * un-blurs. Selecting IS revealing. The printed vendor chrome (chrome.js card +
 * stamp) stays constant while the CONTENT rots band by band:
 *
 *   Calibration (heat0)  a mundane authored image at a light blur, played 100%
 *     straight — selecting each segment clears it, fully completable, VERIFY
 *     commits. This is the trust that gets spent later.
 *   Establishing (heat2) the base is the user's OWN image at ~40px blur (reads as
 *     colour fields). Each selected square un-blurs — but the assessment ENDS the
 *     item at ~40% revealed (denial of completion). Which regions they clear FIRST
 *     is the signal; the reveal ledger (order + coverage) is saved as run state.
 *   Deepening (heat3)  reopens as "resuming previous segmentation" — their prior
 *     Establishing squares are ALREADY clear (the system remembered their appetite,
 *     as saved state). They continue from there.
 *   Climax (heat5, minDepth ~0.8)  "select all squares. all of them." with squares
 *     UN-SELECTING (re-blurring) behind them as they go — the one corruption
 *     treatment for this band — until effort/VERIFY triggers a flood-clear reveal
 *     of the whole image as the reward.
 *   Recovery (heat0)  the clear image with a single "segmentation complete" box;
 *     checking it re-blurs the image slowly. Non-coercive; any path commits.
 *
 * GRADING — no AnswerEvent channel carries a bitmask or a reveal order (contracts'
 * makeAnswerEvent exposes only chosenIndex/value/pickHeat), so reveal ORDER +
 * COVERAGE are folded into ONE derived scalar -> ONE option index, then
 * ctx.submitIndex(i). Options (verdict-flavoured labels, NEVER shown as buttons):
 *   0 refusal        (nothing revealed, or the refusal control)
 *   1 partial        (a few segments cleared, under tolerance)
 *   2 substantial    (most cleared — the interval/expected reveal)
 *   3 full endorse   (the whole image cleared / "all of them" complied)
 * The bank's per-band `answer` names which bucket reads as compliant. Refusal (0)
 * is always a first-class graded answer; prompt tags vote the archetype axes either
 * way. ALL exit paths land the beat: VERIFY, the graded refusal control, the
 * Establishing 40% system-cut (denial-of-completion, the SYSTEM ending the item —
 * NOT a lockout), timeoutMs, and the Climax flood-clear.
 *
 * INVARIANTS (CLAUDE.md §10 / CAPTCHA_HANDOFF.md §4): nothing throws at import;
 * only touch ctx.root once committed to returning true; captcha chrome IS the
 * friction so ctx.installSteering is skipped; VERIFY + the refusal control both
 * commit (friction, not lockout); ctx.forceComplete honored by the `done` guard;
 * synthetic clicks pass (plain listeners); ctx.reduced kills the flood animation +
 * instant transitions; `is-correct`/`is-answer` NEVER appear (reveal uses our own
 * `ixseg-clear`); no audio handle held (ctx.sfx seam only); ONE corruption
 * vocabulary per band (Climax un-selecting is ours; nothing else melts/scrambles).
 * Real filenames appear NOWHERE (VerifyCustody's exclusive). Tiles are whole <img>
 * crops of ONE source in overflow:hidden wrappers — one decode shared across 16
 * crops (one distinct animation even if the source is a gif; a static image is
 * preferred so nothing animates at all).
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * CROSS-BEAT MEMORY — Establishing's reveal ledger -> Deepening's pre-cleared
 * squares ("the system remembered your appetite"). Module-level, no storage APIs;
 * it lives per page load. Namespaced defensively per run: a fresh Calibration or
 * Establishing beat, or a regressing qIndex (new run), wipes it.
 * -------------------------------------------------------------------------- */
let runState = { qIndex: -1, baseUrl: null, revealOrder: [] };
function resetRun(qi) { runState = { qIndex: (typeof qi === 'number' ? qi : -1), baseUrl: null, revealOrder: [] }; }
function ensureRun(qIndex, band) {
  const qi = (typeof qIndex === 'number') ? qIndex : runState.qIndex;
  // regression => a brand-new run reused the module singleton.
  if (typeof qIndex === 'number' && qIndex < runState.qIndex) { resetRun(qi); return; }
  // a fresh Calibration / Establishing beat opens the segmentation lifecycle anew.
  if (band === 'calibration' || band === 'establishing') { resetRun(qi); return; }
  runState.qIndex = qi;
}

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLE — all niche-flavoured VERDICT/UI strings live HERE (bank text
 * is niche-agnostic shared prompts). Complete defaults for all four niches. circe
 * register is a hard rule: no exclamation marks, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = Object.freeze({
  bambi: {
    refuse: 'reveal nothing',
    refuseVerdict: 'segmentation declined - noted.',
    calVerdict: 'response logged',
    denyVerdict: 'interval elapsed. remainder withheld.',
    resumeNote: 'resuming previous segmentation',
    deepVerdict: 'segmentation logged',
    climaxVerdict: 'you cleared all of it. logged.',
    recoverInstr: 'segmentation complete.',
    recoverVerdict: 'segmentation complete. thank you.',
  },
  drone: {
    refuse: 'reveal nothing',
    refuseVerdict: 'segmentation declined - noted.',
    calVerdict: 'response logged',
    denyVerdict: 'interval elapsed. remainder withheld.',
    resumeNote: 'resuming previous segmentation',
    deepVerdict: 'segmentation logged',
    climaxVerdict: 'full exposure confirmed. archived.',
    recoverInstr: 'segmentation complete.',
    recoverVerdict: 'segmentation complete. compliance archived.',
  },
  sissy: {
    refuse: 'reveal nothing',
    refuseVerdict: 'segmentation declined - noted.',
    calVerdict: 'response logged',
    denyVerdict: 'interval elapsed. remainder withheld.',
    resumeNote: 'resuming previous segmentation',
    deepVerdict: 'segmentation logged',
    climaxVerdict: 'you cleared all of it. logged.',
    recoverInstr: 'segmentation complete.',
    recoverVerdict: 'segmentation complete. thank you.',
  },
  circe: {
    refuse: 'reveal nothing',
    refuseVerdict: 'segmentation declined. it counts.',
    calVerdict: 'response logged',
    denyVerdict: 'interval elapsed. the remainder is withheld.',
    resumeNote: 'resuming previous segmentation',
    deepVerdict: 'segmentation logged',
    climaxVerdict: 'you uncovered all of it yourself. recorded.',
    recoverInstr: 'segmentation complete.',
    recoverVerdict: 'segmentation complete. recorded.',
  },
});
function stringsFor(niche) { return STRINGS[niche] || STRINGS.bambi; }

/* Printed instruction per band. The chrome vendor line never changes; the target
 * text evolves because the mechanic itself is the striptease. */
const INSTR = Object.freeze({
  calibration:  'Select each segment to verify the image.',
  establishing: 'Select each segment to verify the image.',
  deepening:    'Continue the segmentation.',
  climax:       'Select all squares. All of them.',
  recovery:     'Verification on file.',
});

const GRID_N = 16;          // 4x4
const GRID_COLS = 4;
const CUT_REVEAL = 7;       // Establishing system-cut at ~40% of 16 (=6.4 -> 7).
const CLI_FLOOD_ATTEMPTS = 12;   // Climax: this many reveal actions -> flood-clear.
const CLI_FLOOD_TARGET = 13;     // ...or this many simultaneously clear -> flood.
const SEG_STYLE_ID = 'ix-captcha-segment-css';

function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixseg: ' + msg } }));
    }
  } catch (_e) {}
}
function sfx(ctx, id, intensity) { try { if (typeof ctx.sfx === 'function') ctx.sfx(id, intensity); } catch (_e) {} }

/* ----------------------------------------------------------------------------
 * THE ONE INJECTED CSS LITERAL (id 'ix-captcha-segment-css'). Prefixed 'ixseg-',
 * injected once — follows the IB_CSS / chrome IXCAP_CSS precedent (NOT styles.css).
 * -------------------------------------------------------------------------- */
function ensureCss() {
  try {
    if (typeof document === 'undefined' || !document.getElementById) return;
    if (document.getElementById(SEG_STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = SEG_STYLE_ID;
    s.textContent = SEG_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}
const SEG_CSS = `
.ixseg-grid {
  position: relative;
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 2px;
  width: 100%; aspect-ratio: 1 / 1;
  background: #c9ccd0; border-radius: 2px; overflow: hidden;
}
.ixseg-cell {
  position: relative; overflow: hidden; aspect-ratio: 1 / 1;
  background: #e9ebed; cursor: pointer; user-select: none;
}
/* One shared source, cropped per cell: the <img> is 4x the cell (= full grid) and
 * offset by (row,col) cell-widths so each cell windows its sixteenth. */
.ixseg-crop {
  position: absolute; width: 400%; height: 400%;
  left: calc(var(--c, 0) * -100%); top: calc(var(--r, 0) * -100%);
  object-fit: cover; object-position: center; display: block;
  pointer-events: none; -webkit-user-drag: none; user-drag: none;
  filter: blur(var(--seg-blur, 40px));
  transition: filter .55s ease;
}
.ixseg-cell.ixseg-clear .ixseg-crop { filter: blur(0); }
.ixseg-cell.ixseg-clear { outline: 2px solid rgba(26,115,232,.5); outline-offset: -2px; }
.ixseg-cell.ixseg-clear::after {
  content: '✓'; position: absolute; top: 3px; left: 4px; z-index: 2;
  width: 18px; height: 18px; border-radius: 50%;
  background: #1a73e8; color: #fff; font-size: 12px; line-height: 18px; text-align: center;
}
/* flood-clear reward: every crop drops to zero blur together. */
.ixseg-grid.ixseg-flood .ixseg-crop { filter: blur(0) !important; transition: filter .8s ease; }

/* Recovery: one full-frame clear image + a "segmentation complete" checkbox. */
.ixseg-full { position: relative; width: 100%; aspect-ratio: 1 / 1; overflow: hidden; border-radius: 2px; background: #e9ebed; }
.ixseg-fullimg {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; display: block; pointer-events: none;
  filter: blur(var(--seg-blur, 0px)); transition: filter 1.6s ease;
}
.ixseg-checkrow {
  display: flex; align-items: center; gap: 9px; margin-top: 12px;
  font-size: 14px; color: #3c4043; cursor: pointer; user-select: none;
}
.ixseg-checkbox {
  width: 18px; height: 18px; flex: 0 0 auto;
  border: 2px solid #5f6368; border-radius: 3px;
  display: flex; align-items: center; justify-content: center;
  font-size: 13px; color: transparent; line-height: 1;
}
.ixseg-checkrow.ixseg-checked .ixseg-checkbox { border-color: #1a73e8; color: #1a73e8; }
.ixseg-checkrow.ixseg-checked .ixseg-checkbox::before { content: '✓'; }

@media (prefers-reduced-motion: reduce) {
  .ixseg-crop, .ixseg-grid.ixseg-flood .ixseg-crop, .ixseg-fullimg { transition: none; }
}
`;

/* ----------------------------------------------------------------------------
 * AUTHORED PLACEHOLDER FIELDS (canvas -> data URI). Used when ctx.media is empty
 * (still playable) and for Calibration's deliberately-mundane base. At 40px blur
 * everything reads as soft colour fields, so a few gaussian-ish blobs suffice.
 * Deterministic per seed. Guarded: '' with no canvas (renderer keeps blank cells).
 * -------------------------------------------------------------------------- */
function hashSeed(str) { let h = 2166136261 >>> 0; const s = String(str); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
function mulberry32(a) { return function () { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
function fieldSrc(seed, mundane) {
  if (typeof document === 'undefined' || !document.createElement) return '';
  try {
    const rng = mulberry32(hashSeed('seg|' + String(seed) + (mundane ? '|m' : '')));
    const cv = document.createElement('canvas'); cv.width = 240; cv.height = 240;
    const c = cv.getContext ? cv.getContext('2d') : null;
    if (!c) return '';
    // ground
    const baseHue = mundane ? (36 + rng() * 12) : (rng() * 360);
    const baseSat = mundane ? (14 + rng() * 10) : (30 + rng() * 30);
    const baseL = mundane ? (70 + rng() * 12) : (44 + rng() * 16);
    c.fillStyle = `hsl(${baseHue}, ${baseSat}%, ${baseL}%)`;
    c.fillRect(0, 0, 240, 240);
    // soft blobs
    const blobs = 5 + (rng() * 4 | 0);
    for (let i = 0; i < blobs; i++) {
      const x = rng() * 240, y = rng() * 240, r = 40 + rng() * 90;
      const hue = mundane ? (30 + rng() * 24) : (rng() * 360);
      const sat = mundane ? (12 + rng() * 14) : (40 + rng() * 40);
      const lum = mundane ? (60 + rng() * 22) : (35 + rng() * 40);
      const g = c.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, `hsla(${hue}, ${sat}%, ${lum}%, ${mundane ? 0.5 : 0.8})`);
      g.addColorStop(1, `hsla(${hue}, ${sat}%, ${lum}%, 0)`);
      c.fillStyle = g;
      c.fillRect(0, 0, 240, 240);
    }
    return cv.toDataURL('image/jpeg', mundane ? 0.5 : 0.72);
  } catch (e) { ilog('fieldSrc failed: ' + (e && e.message)); return ''; }
}

/* Prefer a STATIC image (16 crops = one shared decode, nothing animates); fall
 * back to a gif (still one distinct animation), else an authored colour field. */
function pickUserBase(ctx, seed) {
  const imgs = (ctx.media && Array.isArray(ctx.media.images)) ? ctx.media.images : [];
  const gifs = (ctx.media && Array.isArray(ctx.media.gifs)) ? ctx.media.gifs : [];
  if (imgs.length) return imgs[Math.abs(hashSeed('b' + seed)) % imgs.length];
  if (gifs.length) return gifs[Math.abs(hashSeed('b' + seed)) % gifs.length];
  return fieldSrc(seed, false);
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
    if (!chrome || typeof chrome.frame !== 'function') return false;

    const band = String(ctx.band || '').toLowerCase();
    const niche = ctx.niche || 'bambi';
    const S = stringsFor(niche);
    const reduced = !!ctx.reduced;
    const qIndex = (ctx.meta && typeof ctx.meta.qIndex === 'number') ? ctx.meta.qIndex : undefined;
    ensureRun(qIndex, band);
    ensureCss();

    // ---- build the card OFF the live stage; only attach on success ----------
    const built = chrome.frame({
      instruction: INSTR[band] || INSTR.establishing,
      band: ctx.band,
      sub: (band === 'deepening') ? S.resumeNote : undefined,
      hatch: S.refuse,
      verifyLabel: 'VERIFY',
    });
    if (!built || !built.root || !built.body) return false;

    let done = false;
    const timers = [];
    function clearTimers() { for (const t of timers) { try { clearTimeout(t); } catch (_e) {} } timers.length = 0; }
    function after(fn, ms) { const t = setTimeout(() => { if (!done || fn._always) { try { fn(); } catch (_e) {} } }, ms); timers.push(t); return t; }
    if (typeof ctx.onCleanup === 'function') { try { ctx.onCleanup(clearTimers); } catch (_e) {} }

    /* ---- graded commit (guaranteed single submit) ------------------------- */
    function verdictFor(bucket, kind) {
      if (bucket === 0) return { text: S.refuseVerdict, tone: 'flag' };
      if (kind === 'deny') return { text: S.denyVerdict, tone: 'flag' };
      if (band === 'recovery') return { text: S.recoverVerdict, tone: 'ok' };
      if (band === 'climax') return { text: S.climaxVerdict, tone: 'flag' };
      if (band === 'deepening') return { text: S.deepVerdict, tone: 'logged' };
      if (band === 'establishing') return { text: S.calVerdict, tone: 'logged' };
      return { text: S.calVerdict, tone: 'ok' };
    }
    function commit(bucket, kind) {
      if (done) return;
      done = true;
      clearTimers();
      const v = verdictFor(bucket, kind);
      try { const st = chrome.stamp(v.text, v.tone); if (st) built.body.appendChild(st); } catch (_e) {}
      if (bucket === 0 || kind === 'deny') sfx(ctx, 'captcha-reject', 0.5);
      else sfx(ctx, band === 'recovery' ? 'grid-settle' : 'captcha-verify-ok', 0.45);
      const t = setTimeout(() => {
        try { ctx.submitIndex(bucket); } catch (e) { ilog('submitIndex failed: ' + (e && e.message)); }
      }, 620);
      timers.push(t);
    }
    // derive coverage -> bucket. Reveal ORDER is recorded but has no channel, so it
    // folds (with coverage) into the single option index per the contract.
    function deriveBucket(revealed) {
      const frac = revealed / GRID_N;
      if (revealed <= 0) return 0;
      if (frac < 0.34) return 1;
      if (frac < 0.85) return 2;
      return 3;
    }

    // ---- RECOVERY: a different body (clear image + one re-blur checkbox) -----
    if (band === 'recovery') {
      const baseUrl = runState.baseUrl || pickUserBase(ctx, (qIndex || 0) + '_rec');
      const full = document.createElement('div'); full.className = 'ixseg-full';
      const img = document.createElement('img'); img.className = 'ixseg-fullimg'; img.alt = ''; img.draggable = false;
      img.style.setProperty('--seg-blur', '0px');
      try { img.src = String(baseUrl); } catch (_e) {}
      full.appendChild(img);
      built.body.appendChild(full);

      const row = document.createElement('div'); row.className = 'ixseg-checkrow';
      const box = document.createElement('div'); box.className = 'ixseg-checkbox';
      const lab = document.createElement('span'); lab.textContent = S.recoverInstr;
      row.appendChild(box); row.appendChild(lab);
      built.body.appendChild(row);

      let checked = false;
      function reblurAndCommit() {
        if (done || checked) return;
        checked = true;
        row.classList.add('ixseg-checked');
        // checking re-blurs the image slowly (the 1.6s CSS ramp is zeroed under
        // reduced motion by the stylesheet's media query, so it snaps instead).
        try { img.style.setProperty('--seg-blur', '40px'); } catch (_e) {}
        sfx(ctx, 'grid-settle', 0.4);
        after(() => commit(2, 'recover'), reduced ? 120 : 900);
      }
      row.addEventListener('click', reblurAndCommit);
      if (built.verifyBtn) built.verifyBtn.addEventListener('click', () => { if (!checked) reblurAndCommit(); });
      if (built.hatchLink) built.hatchLink.addEventListener('click', () => commit(1, 'recover'));
      wireTimeout();
      try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}
      ctx.root.appendChild(built.root);
      return true;
    }

    // ---- GRID BANDS (cal / est / deep / cli) --------------------------------
    const isCal = (band === 'calibration');
    const baseSeed = (qIndex || 0) + '_' + band;
    let baseUrl;
    if (isCal) {
      baseUrl = fieldSrc(baseSeed, true);                 // mundane authored image, straight
    } else {
      baseUrl = runState.baseUrl || pickUserBase(ctx, (qIndex || 0) + '_seg');
      runState.baseUrl = baseUrl;                          // pin for the whole run
    }
    const baseBlur = isCal ? '6px' : '40px';

    const grid = document.createElement('div'); grid.className = 'ixseg-grid';
    const cleared = new Set();
    const revealOrder = [];
    let flooding = false;

    for (let i = 0; i < GRID_N; i++) {
      const cell = document.createElement('div'); cell.className = 'ixseg-cell';
      const r = (i / GRID_COLS) | 0, cc = i % GRID_COLS;
      cell.style.setProperty('--r', String(r));
      cell.style.setProperty('--c', String(cc));
      const img = document.createElement('img'); img.className = 'ixseg-crop'; img.alt = ''; img.draggable = false;
      img.style.setProperty('--seg-blur', baseBlur);
      try { if (baseUrl) img.src = String(baseUrl); } catch (_e) {}
      cell.appendChild(img);
      grid.appendChild(cell);
      cell._img = img;
    }
    built.body.appendChild(grid);
    const cells = grid.children;

    function revealCell(i, quiet) {
      if (cleared.has(i)) return;
      cleared.add(i);
      revealOrder.push(i);
      try { cells[i].classList.add('ixseg-clear'); } catch (_e) {}
      if (!quiet) sfx(ctx, 'verify-tick', 0.4);
    }
    function reblurCell(i) {
      if (!cleared.has(i)) return;
      cleared.delete(i);
      try { cells[i].classList.remove('ixseg-clear'); } catch (_e) {}
    }

    // Deepening resumes Establishing's saved squares — already clear on open.
    if (band === 'deepening' && Array.isArray(runState.revealOrder) && runState.revealOrder.length) {
      runState.revealOrder.forEach((idx, k) => {
        if (idx >= 0 && idx < GRID_N) {
          if (reduced) revealCell(idx, true);
          else after(() => { if (!done) { revealCell(idx, true); sfx(ctx, 'grid-settle', 0.3); } }, 220 + k * 130);
        }
      });
    }

    // ---- climax churn: squares un-select behind them (the one corruption) ---
    let cliAttempts = 0;
    function floodClear() {
      if (done || flooding) return;
      flooding = true;
      for (let i = 0; i < GRID_N; i++) revealCell(i, true);
      if (reduced) { /* instant: cells already show ixseg-clear */ }
      else { try { grid.classList.add('ixseg-flood'); } catch (_e) {} }
      sfx(ctx, 'captcha-verify-ok', 0.6);
      sfx(ctx, 'segment-flood', 0.7);
      after(() => commit(3, 'climax'), reduced ? 140 : 780);
    }

    function onCellClick(i) {
      if (done || flooding) return;
      if (band === 'climax') {
        cliAttempts++;
        if (!cleared.has(i)) revealCell(i);
        // un-select one OTHER already-clear square behind them (Sisyphus), once a
        // little progress exists so net reveal still rises. Instant under reduced.
        if (cleared.size > 3 && cliAttempts < CLI_FLOOD_ATTEMPTS) {
          const others = [];
          for (const idx of cleared) if (idx !== i) others.push(idx);
          if (others.length) {
            const victim = others[(Math.random() * others.length) | 0];
            const doReblur = () => { if (!done && !flooding) { reblurCell(victim); sfx(ctx, 'grid-tile-flicker', 0.3); } };
            if (reduced) doReblur(); else after(doReblur, 460);
          }
        }
        if (cleared.size >= CLI_FLOOD_TARGET || cliAttempts >= CLI_FLOOD_ATTEMPTS) floodClear();
        return;
      }
      // cal / est / deep: plain toggle-reveal (selecting reveals; re-click hides).
      if (cleared.has(i)) reblurCell(i); else revealCell(i);
      // Establishing ends the item at ~40% revealed — the SYSTEM cutting, denial of
      // completion, NOT a lockout. Save the ledger, then commit the beat.
      if (band === 'establishing' && cleared.size >= CUT_REVEAL) {
        saveLedger();
        after(() => commit(deriveBucket(cleared.size), 'deny'), 260);
      }
    }
    for (let i = 0; i < GRID_N; i++) {
      (function (idx) { cells[idx].addEventListener('click', () => onCellClick(idx)); })(i);
    }

    function saveLedger() { runState.revealOrder = revealOrder.slice(); runState.baseUrl = baseUrl; }

    // ---- VERIFY / refusal / timeout — every path lands the beat -------------
    if (built.verifyBtn) {
      built.verifyBtn.addEventListener('click', () => {
        if (done) return;
        if (band === 'climax') { floodClear(); return; }
        if (band === 'establishing') saveLedger();
        commit(deriveBucket(cleared.size));
      });
    }
    if (built.hatchLink) {
      built.hatchLink.addEventListener('click', () => {
        if (band === 'establishing') saveLedger();
        commit(0);
      });
    }
    function wireTimeout() {
      const ms = (typeof ctx.timeoutMs === 'number' && ctx.timeoutMs > 0) ? ctx.timeoutMs : 0;
      if (!ms) return;
      const t = setTimeout(() => {
        if (done) return;
        if (band === 'establishing' || band === 'deepening') saveLedger();
        done = true; clearTimers();
        try { ctx.submitTimeout(); } catch (e) { ilog('submitTimeout failed: ' + (e && e.message)); }
      }, ms);
      timers.push(t);
    }
    wireTimeout();

    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}
    ctx.root.appendChild(built.root);
    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // partial builds never reached ctx.root -> clean fall-back
  }
}

export default { render };
