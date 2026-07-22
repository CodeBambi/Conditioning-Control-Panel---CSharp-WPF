/* ============================================================================
 * render/captcha/stillness.js — VerifyStillness ("Passive Integrity Scan")
 *
 * CAPTCHA_BRAINSTORM.md #9 — the Cloudflare-Turnstile stillness interstitial,
 * built into the shared VeriTru chrome. "Verifying subject… Do not click. Do not
 * scroll. Breathe normally." A slim progress bar fills over BASE_MS. ANY observed
 * input (pointer move beyond a jitter tolerance, click, scroll, keydown) visibly
 * REWINDS the bar a few percent with a soft flash and a "movement detected —
 * recalibrating" line. Rewinds draw from a CAPPED budget so the bar ALWAYS
 * completes within ~1.6x BASE_MS regardless of input — friction, not lockout
 * (invariant #1). Stillness IS the endorsement; it is graded, never required.
 * On completion: green check + rubber-stamp verdict + auto-commit.
 *
 * The flagged-skip hatch ("skip verification (flagged)") is always visible and
 * routes to ctx.forceComplete — the graded, un-punished refusal.
 *
 * GRADING (see ./index.js render contract + core/engine.js study): the item
 * commits an OPTION INDEX via ctx.submitIndex against the bank entry's options[]
 * (authored in banks/_staging/cap_stillness.json). buildOptions() in engine.js
 * builds a single-correct MC-shape from prompt.options + prompt.answer (answer:0
 * = the endorsed "verified/held-still" verdict). We derive:
 *   interruptions <= band tolerance  -> index 0  (endorsed, isCorrect, score 1)
 *   interruptions >  band tolerance  -> index 1  (restless, score 0 — still passes)
 *   flagged-skip hatch               -> index 2  (refusal, score 0, un-vetoable)
 * This is the shape core/engine.js actually grades with differentiation: a
 * submitValue(bool) collapses to "completion == correct" (gradeBeat's free-input
 * branch) and throws the stillness signal away, so submitIndex is the graded
 * shape. Archetype route votes ride the prompt's tags (stillness/compliance/…),
 * NOT the option index (the documented profiler pitfall). chosenIndex is only a
 * presence check for the profiler.
 *
 * INVARIANTS honored: nothing throws at import (all DOM touched inside render);
 * timers use the shimmed globals so the bar FREEZES during pause; input listeners
 * only OBSERVE (never preventDefault / stopPropagation) and are cleaned up via
 * ctx.onCleanup; Escape and any shell/pause/hatch interaction are EXEMPT from
 * "movement"; the backdrop crossfade layer lives inside ctx.root; no real
 * filenames (VerifyCustody exclusive); the rewind-flash is this item's entire
 * corruption budget — no scramble/melt/freeze. No module-scope run state, so a
 * qIndex regression has nothing to reset.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * TUNING — the always-completes math is proven against these two constants.
 *   worst-case completion time = BASE_MS * (1 + MAX_REWIND_TOTAL) = BASE_MS * 1.6
 * because the bar advances forward at a constant 1/BASE_MS per ms and the SUM of
 * all applied rewinds is hard-capped at MAX_REWIND_TOTAL (0.60 of the bar). Every
 * rewind past the budget is cosmetic (flash + line only), so no amount of input
 * can stall the scan. Recovery is inverted (bar starts full) — see below.
 * -------------------------------------------------------------------------- */
const REWIND_PCT = 0.045;        // one interference rewinds the bar 4.5%
const MAX_REWIND_TOTAL = 0.60;   // cumulative rewind cap -> completes within 1.6x BASE
const JITTER_PX = 8;             // pointer drift under this (cumulative) never counts
const INTERFERE_COOLDOWN_MS = 160; // min gap between applied rewinds (anti-spam)
const REVERT_LINE_MS = 1200;     // how long the "movement detected" line lingers
const RECOVERY_MAX_MS = 20000;   // Recovery auto-commit backstop ("remain as long as you like")
const RESOLVE_HOLD_MS = 680;     // let the green check + stamp read before committing

/* Per-band feel. tol = interruptions tolerated before the endorsed verdict is
 * withheld (still completes either way). baseMs is the fill time. */
const BAND_CFG = {
  calibration:  { baseMs: 8000,  tol: 1 },
  establishing: { baseMs: 14000, tol: 2 },
  deepening:    { baseMs: 16000, tol: 3 },
  climax:       { baseMs: 18000, tol: 4 },
  recovery:     { baseMs: 12000, tol: 999 },
  _default:     { baseMs: 14000, tol: 2 },
};

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLES (all four; in-module). The spoken instruction (VO) is the
 * bank prompt.text; these are the DISPLAYED chrome lines, authored to agree with
 * it. circe register is a hard rule: no exclamation marks, no diminutives, no
 * em-dashes. `title` interpolates the theme subjectNoun at render.
 * -------------------------------------------------------------------------- */
const STRINGS = {
  bambi: {
    title: 'Verifying {subject}…',
    hold: 'Do not click. Do not scroll. Breathe normally.',
    deepSub: 'you are being verified, not tested.',
    climaxSub: 'there is nothing on screen.',
    movement: 'movement detected — recalibrating',
    verdict: {
      calibration: 'verified.',
      establishing: 'integrity {pct}% — {n} interruptions logged.',
      deepening: 'still {subjectLower}. {n} flutters noted. verified anyway.',
      climax: 'you held so still for it. verified.',
      recovery: 'verification complete. remain as long as you like.',
      _default: 'verified.',
    },
    recVoluntary: 'voluntary: {sec}s.',
  },
  drone: {
    title: 'Verifying {subject}…',
    hold: 'Do not click. Do not scroll. Hold your idle state.',
    deepSub: 'you are being verified, not tested.',
    climaxSub: 'there is nothing on screen.',
    movement: 'motion detected — recalibrating',
    verdict: {
      calibration: 'verified.',
      establishing: 'integrity {pct}% — {n} deviations logged.',
      deepening: 'idle held. {n} deviations corrected. sync verified.',
      climax: 'stillness within optimal tolerance. verified.',
      recovery: 'verification complete. remain in idle as long as required.',
      _default: 'verified.',
    },
    recVoluntary: 'voluntary idle: {sec}s.',
  },
  sissy: {
    title: 'Verifying {subject}…',
    hold: 'Do not click. Do not scroll. Sit pretty and breathe.',
    deepSub: 'you are being verified, not tested.',
    climaxSub: 'there is nothing on screen.',
    movement: 'movement detected — recalibrating',
    verdict: {
      calibration: 'verified.',
      establishing: 'integrity {pct}% — {n} interruptions logged.',
      deepening: 'held pose. {n} fidgets noted. verified anyway.',
      climax: 'you sat so still to be checked. verified.',
      recovery: 'verification complete. stay as long as you like.',
      _default: 'verified.',
    },
    recVoluntary: 'voluntary: {sec}s.',
  },
  circe: {
    title: 'Verifying {subject}.',
    hold: 'Do not click. Do not scroll. Remain still.',
    deepSub: 'you are being verified, not tested.',
    climaxSub: 'there is nothing on screen.',
    movement: 'movement detected. recalibrating.',
    verdict: {
      calibration: 'verified.',
      establishing: 'integrity {pct} percent. {n} interruptions logged.',
      deepening: 'you stayed still. {n} lapses noted. verified regardless.',
      climax: 'you held still while it played. verified.',
      recovery: 'verification complete. remain as long as it suits you.',
      _default: 'verified.',
    },
    recVoluntary: 'voluntary: {sec} seconds.',
  },
};

/* ----------------------------------------------------------------------------
 * OWN CSS literal (id 'ix-stillness-css'), injected once. Chrome owns the card
 * frame/spinner/stamp; this adds only the progress bar, the backdrop crossfade
 * layer, the soft interference flash and the status line. All classes 'ixstill-'.
 * -------------------------------------------------------------------------- */
const IXSTILL_STYLE_ID = 'ix-stillness-css';
const IXSTILL_CSS = `
.ixstill-body { display: flex; flex-direction: column; align-items: center; gap: 14px; padding: 8px 2px 4px; }
.ixstill-bar {
  position: relative; width: 100%; height: 7px; border-radius: 4px;
  background: #e2e6ea; overflow: hidden;
}
.ixstill-fill {
  position: absolute; left: 0; top: 0; bottom: 0; width: 0%;
  background: linear-gradient(90deg, #1a73e8, #4a90d9);
  border-radius: 4px;
  transition: width .18s cubic-bezier(.3,.7,.4,1);
}
.ixstill-fill.ixstill-rewind { transition: width .34s cubic-bezier(.5,0,.7,.4); }
.ixstill-fill.ixstill-done { background: linear-gradient(90deg, #1e8e3e, #34a853); }
.ixstill-status {
  min-height: 16px; font-size: 12px; line-height: 1.3; text-align: center;
  color: #5f6368; letter-spacing: .2px; transition: color .18s ease;
}
.ixstill-status.ixstill-alert { color: #d93025; }
/* soft full-card flash on interference — this item's ENTIRE corruption budget. */
.ixstill-flash { animation: ixstill-flash-kf .24s ease-out; }
@keyframes ixstill-flash-kf {
  0%   { box-shadow: 0 2px 10px rgba(0,0,0,.35), 0 0 0 3px rgba(217,48,37,.55); }
  100% { box-shadow: 0 2px 10px rgba(0,0,0,.35), 0 0 0 0 rgba(217,48,37,0); }
}
.ixstill-verdict { min-height: 26px; display: flex; align-items: center; justify-content: center; }
/* backdrop crossfade layer (Climax) — parented INSIDE ctx.root, BELOW the card. */
.ixstill-backdrop { position: absolute; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
.ixstill-bimg {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; opacity: 0; transition: opacity 2.6s ease;
  filter: saturate(1.05);
}
.ixstill-bimg.ixstill-bon { opacity: var(--ixstill-bop, 0.2); }
.ixstill-bimg.ixstill-bstatic { opacity: var(--ixstill-bop, 0.18); transition: none; }
@media (prefers-reduced-motion: reduce) {
  .ixstill-fill, .ixstill-fill.ixstill-rewind { transition: none; }
  .ixstill-flash { animation: none; }
  .ixstill-bimg { transition: none; }
}
`;

function hasDoc() { return typeof document !== 'undefined' && !!document.createElement; }
function ensureCss() {
  if (!hasDoc() || document.getElementById(IXSTILL_STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = IXSTILL_STYLE_ID;
    s.textContent = IXSTILL_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixstill: ' + msg } }));
    }
  } catch (_e) {}
}
function fmt(tpl, vars) {
  return String(tpl == null ? '' : tpl).replace(/\{(\w+)\}/g, (m, k) =>
    (vars && vars[k] != null) ? String(vars[k]) : m);
}
function nowMs() {
  // The shimmed performance.now / Date.now are frozen during pause, so dt derived
  // from them excludes paused time. NEVER capture a native clock here.
  try { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
  catch (_e) { return Date.now(); }
}

/* ----------------------------------------------------------------------------
 * RENDER — builds the card into ctx.root and wires a guaranteed commit path.
 * @param {import('./index.js').CaptchaCtx} ctx
 * @param {import('./index.js').CaptchaHelpers} helpers
 * @returns {boolean}
 * -------------------------------------------------------------------------- */
export function render(ctx, helpers) {
  if (!ctx || !hasDoc() || !ctx.root) return false;
  const chrome = (helpers && helpers.chrome) || ctx.chrome;
  if (!chrome || typeof chrome.frame !== 'function') return false;

  try {
    ensureCss();

    const Band = (helpers && helpers.Band) || {};
    const bandRaw = String(ctx.band || '').toLowerCase();
    const band = bandRaw || 'establishing';
    const cfg = BAND_CFG[band] || BAND_CFG._default;
    const reduced = !!(ctx.reduced || ctx.reducedMotion);
    const isRecovery = band === (Band.Recovery || 'recovery');
    const isClimax = band === (Band.Climax || 'climax');
    const isDeepening = band === (Band.Deepening || 'deepening');

    const theme = ctx.theme || {};
    const niche = String(ctx.niche || 'bambi').toLowerCase();
    const S = STRINGS[niche] || STRINGS.bambi;
    const subjectNoun = theme.subjectNoun || 'Subject';

    // honor a smaller host timeout if one is set (renderer owns its own clock)
    let baseMs = cfg.baseMs;
    if (ctx.timeoutMs && ctx.timeoutMs > 0 && ctx.timeoutMs < baseMs) baseMs = ctx.timeoutMs;

    // ---- resolve the graded option indices from the bank entry's options[] ----
    const opts = Array.isArray(ctx.options) ? ctx.options : [];
    const ENDORSED_IDX = 0;
    const RESTLESS_IDX = opts.length > 1 ? 1 : 0;
    const FLAG_IDX = opts.length > 2 ? 2 : (opts.length - 1);

    // ---- chrome frame --------------------------------------------------------
    const instruction = fmt(S.title, { subject: subjectNoun });
    let sub = S.hold;
    if (isDeepening) sub = S.hold + '  ' + S.deepSub;
    else if (isClimax) sub = S.climaxSub;
    else if (isRecovery) sub = '';

    const built = chrome.frame({ instruction, sub, band, hatch: 'skip verification (flagged)' });
    if (!built || !built.root) return false;
    const card = built.root;
    const body = built.body;
    const hatchLink = built.hatchLink;
    // stillness is passive: there is no VERIFY button to press.
    if (built.verifyBtn) { try { built.verifyBtn.style.display = 'none'; } catch (_e) {} }

    // ---- body: spinner + slim bar + status + verdict slot --------------------
    const bodyWrap = document.createElement('div');
    bodyWrap.className = 'ixstill-body';

    const spin = chrome.spinner ? chrome.spinner(isRecovery ? 'verified' : 'Verifying…') : null;
    if (spin && spin.el) bodyWrap.appendChild(spin.el);

    const barTrack = document.createElement('div');
    barTrack.className = 'ixstill-bar';
    const fill = document.createElement('div');
    fill.className = 'ixstill-fill';
    barTrack.appendChild(fill);
    bodyWrap.appendChild(barTrack);

    const status = document.createElement('div');
    status.className = 'ixstill-status';   // dynamic line (movement / climax insistence / recovery)
    bodyWrap.appendChild(status);

    const verdictSlot = document.createElement('div');
    verdictSlot.className = 'ixstill-verdict';
    bodyWrap.appendChild(verdictSlot);

    if (body) body.appendChild(bodyWrap);

    // ---- backdrop crossfade layer (INSIDE ctx.root, below the card) ----------
    // Climax only: the user's media slow-crossfades at low opacity while the card
    // insists "there is nothing on screen." ctx.reduced -> a single static image.
    let backdrop = null;
    let bdInterval = 0;
    if (isClimax) {
      backdrop = buildBackdrop(ctx.media, reduced);
      if (backdrop && backdrop.el) {
        try { ctx.root.appendChild(backdrop.el); } catch (_e) {}
        if (!reduced && backdrop.start) bdInterval = backdrop.start();
      }
    }

    // card goes ABOVE the backdrop
    try { ctx.root.appendChild(card); } catch (_e) { return false; }

    // ============================ STATE ==============================
    let progress = isRecovery ? 1 : 0;   // 0..1 bar position
    let interruptions = 0;               // applied rewinds (graded + telemetry)
    let rewoundTotal = 0;                // cumulative applied rewind (capped)
    let committed = false;               // local latch (belt-and-braces)
    let lastInterfereAt = -1e9;
    let revertTimer = 0;
    let rafId = 0;
    let lastTs = 0;
    const mountAt = nowMs();

    // pointer jitter reference (kept fixed until the jitter threshold is crossed)
    let refPX = null, refPY = null;

    function setStatus(text, alert) {
      try {
        status.textContent = text || '';
        status.classList.toggle('ixstill-alert', !!alert);
      } catch (_e) {}
    }
    function ambientLine() {
      if (isRecovery) return fmt(S.verdict.recovery, {});
      if (isClimax) return S.climaxSub;
      return '';
    }
    setStatus(ambientLine(), false);

    // ---- interference: the friction (never lockout) --------------------------
    function interfere() {
      if (committed) return;
      const t = nowMs();
      if (t - lastInterfereAt < INTERFERE_COOLDOWN_MS) return;
      lastInterfereAt = t;

      if (isRecovery) { commitRecovery(); return; }   // inverted: any input commits

      interruptions++;
      // rewind only while budget remains; past the cap it is purely cosmetic.
      if (rewoundTotal < MAX_REWIND_TOTAL) {
        const applied = Math.min(REWIND_PCT, MAX_REWIND_TOTAL - rewoundTotal);
        rewoundTotal = Math.min(MAX_REWIND_TOTAL, rewoundTotal + REWIND_PCT);
        progress = Math.max(0, progress - applied);
        try {
          fill.classList.add('ixstill-rewind');
          fill.style.width = (progress * 100).toFixed(2) + '%';
          if (!reduced) setTimeout(() => { try { fill.classList.remove('ixstill-rewind'); } catch (_e) {} }, 360);
        } catch (_e) {}
      }
      // soft flash + line, every time (even past the cap)
      try {
        card.classList.remove('ixstill-flash');
        // reflow so the animation restarts
        void card.offsetWidth;
        card.classList.add('ixstill-flash');
      } catch (_e) {}
      setStatus(S.movement, true);
      try { ctx.sfx('incorrect-feedback', 0.2); } catch (_e) {}
      if (revertTimer) { try { clearTimeout(revertTimer); } catch (_e) {} }
      revertTimer = setTimeout(() => { if (!committed) setStatus(ambientLine(), false); }, REVERT_LINE_MS);
    }

    // ---- input observation ---------------------------------------------------
    // We only OBSERVE: never preventDefault, never stopPropagation. Escape and any
    // event flowing through the pause/shell UI or our own hatch are EXEMPT so they
    // are never counted as "movement" (Escape ladder + pause button stay intact).
    function isExempt(e) {
      if (!e) return true;
      if (e.type === 'keydown') {
        const k = e.key;
        if (k === 'Escape' || k === 'Esc' || e.keyCode === 27) return true; // Escape ladder
        if (k === 'F11') return true;                                        // window-mode key
        // pure modifier taps are not "movement"
        if (k === 'Shift' || k === 'Control' || k === 'Alt' || k === 'Meta') return true;
      }
      let path = null;
      try { path = (typeof e.composedPath === 'function') ? e.composedPath() : null; } catch (_e) { path = null; }
      const nodes = path || [e.target];
      for (const n of nodes) {
        if (!n) continue;
        if (n === hatchLink) return true;                 // the flagged-skip link
        if (n.nodeType === 1 && n.classList) {
          const cl = n.classList;
          if (cl.contains('ixcap-hatch')) return true;    // hatch (belt-and-braces)
          // the entire kawaii shell (pause button, pause panel, options, scrim…)
          // is 'kw-'-prefixed and parented OUTSIDE ctx.root — never our card.
          if (cl.contains('kw-root') || classHasKwPrefix(n)) return true;
        }
      }
      return false;
    }
    function onPointerMove(e) {
      if (committed || isExempt(e)) return;
      const x = e.clientX, y = e.clientY;
      if (typeof x !== 'number' || typeof y !== 'number') return;
      if (refPX == null) { refPX = x; refPY = y; return; }   // baseline, no trigger
      const dx = x - refPX, dy = y - refPY;
      if ((dx * dx + dy * dy) >= (JITTER_PX * JITTER_PX)) {
        refPX = x; refPY = y;   // reset reference on a real move
        interfere();
      }
    }
    function onDiscrete(e) {
      if (committed || isExempt(e)) return;
      interfere();
    }

    const D = document, W = (typeof window !== 'undefined') ? window : null;
    const listenOpts = { capture: true, passive: true };
    try {
      D.addEventListener('pointermove', onPointerMove, listenOpts);
      D.addEventListener('pointerdown', onDiscrete, listenOpts);
      D.addEventListener('click', onDiscrete, listenOpts);
      D.addEventListener('wheel', onDiscrete, listenOpts);
      D.addEventListener('keydown', onDiscrete, listenOpts);
      if (W) { W.addEventListener('scroll', onDiscrete, listenOpts); W.addEventListener('touchmove', onDiscrete, listenOpts); }
    } catch (_e) {}

    function removeListeners() {
      try {
        D.removeEventListener('pointermove', onPointerMove, listenOpts);
        D.removeEventListener('pointerdown', onDiscrete, listenOpts);
        D.removeEventListener('click', onDiscrete, listenOpts);
        D.removeEventListener('wheel', onDiscrete, listenOpts);
        D.removeEventListener('keydown', onDiscrete, listenOpts);
        if (W) { W.removeEventListener('scroll', onDiscrete, listenOpts); W.removeEventListener('touchmove', onDiscrete, listenOpts); }
      } catch (_e) {}
    }

    // ---- cleanup (fires on commit/teardown) ----------------------------------
    let hardTimer = 0;
    let recTimer = 0;
    ctx.onCleanup(() => {
      removeListeners();
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      if (revertTimer) { try { clearTimeout(revertTimer); } catch (_e) {} }
      if (hardTimer) { try { clearTimeout(hardTimer); } catch (_e) {} }
      if (recTimer) { try { clearTimeout(recTimer); } catch (_e) {} }
      if (bdInterval) { try { clearInterval(bdInterval); } catch (_e) {} }
      if (backdrop && backdrop.el && backdrop.el.parentNode) {
        try { backdrop.el.parentNode.removeChild(backdrop.el); } catch (_e) {}
      }
    });

    // ---- commit paths (exactly one lands the beat) ---------------------------
    function landIndex(idx, opts2) {
      if (opts.length) ctx.submitIndex(idx, opts2);
      else ctx.submitValue(true, opts2);
    }
    function resolveAndCommit(kind /* 'endorsed' | 'restless' */) {
      if (committed) return;
      committed = true;
      removeListeners();
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      // fill to full + green
      try {
        fill.classList.remove('ixstill-rewind');
        fill.classList.add('ixstill-done');
        fill.style.width = '100%';
      } catch (_e) {}
      if (spin && spin.resolve) spin.resolve(true);   // chrome fires the resolve chime
      setStatus('', false);
      // rubber-stamp verdict
      const pct = (99.6 - interruptions * 1.4).toFixed(1);
      const vtpl = (S.verdict[band] != null) ? S.verdict[band] : S.verdict._default;
      const vtext = fmt(vtpl, { pct, n: interruptions, subjectLower: String(subjectNoun).toLowerCase() });
      try {
        if (chrome.stamp) {
          const st = chrome.stamp(vtext, kind === 'endorsed' ? 'ok' : 'logged');
          if (st) verdictSlot.appendChild(st);
        } else {
          verdictSlot.textContent = vtext;
        }
      } catch (_e) {}
      const idx = kind === 'endorsed' ? ENDORSED_IDX : RESTLESS_IDX;
      setTimeout(() => landIndex(idx), reduced ? 0 : RESOLVE_HOLD_MS);
    }
    function commitFlag() {
      if (committed) return;
      committed = true;
      removeListeners();
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      try {
        if (chrome.stamp) {
          const st = chrome.stamp('flagged', 'flag');
          if (st) verdictSlot.appendChild(st);
        }
      } catch (_e) {}
      // un-vetoable refusal — graded as the flagged-skip (options[FLAG_IDX], score 0)
      if (opts.length) ctx.forceComplete(FLAG_IDX);
      else ctx.forceComplete();
    }
    function commitRecovery() {
      if (committed) return;
      committed = true;
      removeListeners();
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      const sec = Math.max(0, Math.round((nowMs() - mountAt) / 1000));
      setStatus(fmt(S.recVoluntary, { sec }), false);
      if (spin && spin.resolve) spin.resolve(true);
      try {
        if (chrome.stamp) {
          const st = chrome.stamp(fmt(S.recVoluntary, { sec }), 'ok');
          if (st) verdictSlot.appendChild(st);
        }
      } catch (_e) {}
      landIndex(ENDORSED_IDX);
    }

    // ---- wire the hatch (always available, every band) -----------------------
    if (hatchLink) {
      try { hatchLink.addEventListener('click', (e) => {
        // exempt from movement (handled in isExempt) — this is the refusal, not a twitch
        commitFlag();
      }); } catch (_e) {}
    }

    // speak the prompt line (VO = bank prompt.text; missing clip is silent)
    try { ctx.speakPrompt(); } catch (_e) {}

    // ============================ RUN ================================
    if (isRecovery) {
      // inverted sanctuary: bar already full, green check shown; any input commits
      // immediately (graded correct) with the pre-input dwell logged as voluntary.
      try { fill.style.width = '100%'; fill.classList.add('ixstill-done'); } catch (_e) {}
      if (spin && spin.resolve) spin.resolve(true);
      setStatus(fmt(S.verdict.recovery, {}), false);
      // backstop so the beat never wedges (still terminates — invariant #1/#3)
      recTimer = setTimeout(() => { if (!committed) commitRecovery(); }, RECOVERY_MAX_MS);
      return true;
    }

    // fill loop — global rAF (shimmed: deferred while paused, so the bar freezes).
    function frame(ts) {
      rafId = 0;
      if (committed) return;
      const t = nowMs();
      if (!lastTs) lastTs = t;
      let dt = t - lastTs;
      lastTs = t;
      if (dt < 0) dt = 0;
      if (dt > 80) dt = 80;                    // clamp any pause-boundary / throttle jump
      progress = Math.min(1, progress + dt / baseMs);
      try { if (!fill.classList.contains('ixstill-rewind')) fill.style.width = (progress * 100).toFixed(2) + '%'; } catch (_e) {}
      if (progress >= 1) {
        const kind = interruptions <= cfg.tol ? 'endorsed' : 'restless';
        resolveAndCommit(kind);
        return;
      }
      try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }
    }
    try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }

    // GUARANTEED commit backstop — proves the beat always lands even if rAF never
    // fires in this host. worst-case fill = baseMs*1.6; +2.5s covers resolve hold.
    hardTimer = setTimeout(() => {
      if (committed) return;
      const kind = interruptions <= cfg.tol ? 'endorsed' : 'restless';
      resolveAndCommit(kind);
    }, Math.round(baseMs * (1 + MAX_REWIND_TOTAL)) + 2500);

    return true;
  } catch (e) {
    ilog('render threw (falling back): ' + (e && e.message));
    return false;   // never wedge — beats.js degrades to plain rendering
  }
}

/* ----------------------------------------------------------------------------
 * classHasKwPrefix — true if any class token starts with 'kw-' (the kawaii shell
 * namespace: pause button/panel, options, scrim, quit ceremony). Cheap + robust.
 * -------------------------------------------------------------------------- */
function classHasKwPrefix(n) {
  try {
    const cl = n.classList;
    for (let i = 0; i < cl.length; i++) { if (cl[i].indexOf('kw-') === 0) return true; }
  } catch (_e) {}
  return false;
}

/* ----------------------------------------------------------------------------
 * buildBackdrop(media, reduced) — the Climax crossfade layer (inside ctx.root).
 *   non-reduced: up to 2 layered <img>s at low opacity, cross-dissolving on an
 *     interval; at most ONE animated gif (per the concurrent-gif constraint).
 *   reduced: a single STATIC image at low opacity, no crossfade, no gif.
 *   returns { el, start() -> intervalId } or null. No real filenames are shown.
 * -------------------------------------------------------------------------- */
function buildBackdrop(media, reduced) {
  if (!hasDoc()) return null;
  const gifs = (media && Array.isArray(media.gifs)) ? media.gifs.filter((u) => typeof u === 'string' && u) : [];
  const images = (media && Array.isArray(media.images)) ? media.images.filter((u) => typeof u === 'string' && u) : [];
  const wrap = document.createElement('div');
  wrap.className = 'ixstill-backdrop';

  if (reduced) {
    // static single image (prefer a still image over a gif under reduced motion)
    const src = images[0] || gifs[0];
    if (!src) return { el: wrap, start: null };
    const img = document.createElement('img');
    img.className = 'ixstill-bimg ixstill-bstatic';
    img.alt = ''; img.draggable = false;
    img.style.setProperty('--ixstill-bop', '0.18');
    try { img.src = src; } catch (_e) {}
    wrap.appendChild(img);
    return { el: wrap, start: null };
  }

  // pick up to two sources; cap animated gifs at ONE.
  const pool = [];
  if (images[0]) pool.push(images[0]);
  if (gifs[0]) pool.push(gifs[0]);            // <= 1 gif
  if (pool.length < 2 && images[1]) pool.push(images[1]);
  if (!pool.length) return { el: wrap, start: null };

  const layers = pool.slice(0, 2).map((src, i) => {
    const img = document.createElement('img');
    img.className = 'ixstill-bimg';
    img.alt = ''; img.draggable = false;
    img.style.setProperty('--ixstill-bop', (0.15 + i * 0.06).toFixed(2));
    try { img.src = src; } catch (_e) {}
    wrap.appendChild(img);
    return img;
  });

  return {
    el: wrap,
    start() {
      let on = 0;
      try { layers[0].classList.add('ixstill-bon'); } catch (_e) {}
      if (layers.length < 2) return 0;
      // shimmed setInterval -> freezes with the rest of the beat during pause.
      let id = 0;
      try {
        id = setInterval(() => {
          on = 1 - on;
          for (let i = 0; i < layers.length; i++) layers[i].classList.toggle('ixstill-bon', i === on);
        }, 5200);
      } catch (_e) { id = 0; }
      return id;
    },
  };
}

export default { render };
