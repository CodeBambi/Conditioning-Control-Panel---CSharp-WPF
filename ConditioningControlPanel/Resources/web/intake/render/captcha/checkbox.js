/* ============================================================================
 * render/captcha/checkbox.js — VerifyCheckbox ("I am not a robot")  [Wave-2]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #3, fully laddered. The most-trusted widget on
 * the internet, cloned pitch-perfect through chrome.js (VeriTru card + spinner +
 * stamp), then degraded band-by-band while the chrome stays constant:
 *
 *   Calibration (heat0) single honest click -> brief spinner -> green -> commit.
 *   Establishing (heat1-2) the click becomes a 3s PRESS-AND-HOLD; a user gif
 *     ghosts at ~8% behind the card; releasing early DECAYS the remaining hold
 *     ~30% toward the user (friction decays, never a lockout).
 *   Deepening (heat3) 8s hold, ghost ~40%, spinner label cycles
 *     "verifying... / verifying you... / verifying yours...".
 *   Climax (heat4-5, minDepth ~0.8) label "I am not resisting."; the ghost sits
 *     behind frosted glass; the check greens when the hold STABILISES (~4s steady,
 *     generous jitter tolerance).
 *   Recovery (heat0) the box is PRE-CHECKED + untickable, "verification is
 *     permanent."; any interaction / a short dwell commits, graded correct.
 *
 * Answer shape: a plain bool via ctx.submitValue(checked). The ENGINE grades it
 * against the (per-niche) prompt.answer, so the per-niche LABEL INVERSION lives
 * two places, coherently: the visible caption comes from STRINGS[niche] here, and
 * the compliant/correct direction comes from the bank's per-niche `answer`
 * (bambi/sissy: checking = comply = answer true; drone/circe: checking is the
 * REFUSAL, so NOT-checking+verify = comply = answer false, flipped in the staging
 * bank's nicheOverrides). Committing `true` always costs the hold (the friction);
 * `false` is always one VERIFY / skip-link press away — so BOTH answers, including
 * a refusal, stay completable (invariant #1). Hold requirements CAP and DECAY;
 * the third attempt always accepts.
 *
 * No coupling: SFX via ctx.sfx (existing ids); the ghost layer parents INSIDE
 * ctx.root (wiped per render) and is also torn down via ctx.onCleanup — it must
 * not survive the beat. Nothing throws at import (every DOM touch is in render()).
 * is-correct / is-answer are never used. installSteering is skipped: the hold IS
 * the friction. Max one corruption effect per band (the ghost/blur is the budget).
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * PER-NICHE STRING TABLE (keyed by ctx.niche). `complyChecked` is documentation
 * of the grading direction the staging bank encodes for est/deep/climax — the
 * module does not grade (the engine does), it only renders the coherent label and
 * routes the raw checkbox bool. bambi/sissy: checking the affirmation is
 * compliance. drone/circe: the caption states what CHECKING asserts, and that
 * assertion is always the refusal, so compliance is declining to check. Circe's
 * register is cold declaratives — no exclamations, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = {
  bambi: {
    complyChecked: true,
    label: { cal: 'I am not a robot', est: 'I am not a robot', deep: 'I am not a robot', cli: 'I am not resisting.', rec: 'verification is permanent.' },
    instr: { cal: 'Confirm you are human.', est: 'Press and hold to confirm.', deep: 'Keep holding, sweetie.', cli: 'Hold still and let it finish.', rec: 'Verification on file.' },
    spin: ['verifying...', 'verifying you...', 'verifying yours...'],
    verifyingOne: 'verifying...',
    ok: 'HUMAN', refuse: 'unverified', permanent: 'verification is permanent.',
    decline: 'do not verify',
  },
  sissy: {
    complyChecked: true,
    label: { cal: 'I am not a robot', est: 'I am not a robot', deep: 'I am a good girl', cli: 'I am not resisting.', rec: 'verification is permanent.' },
    instr: { cal: 'Confirm you are human.', est: 'Press and hold to confirm.', deep: 'Keep holding, princess.', cli: 'Hold still and let it finish.', rec: 'Verification on file.' },
    spin: ['verifying...', 'verifying you...', 'verifying yours...'],
    verifyingOne: 'verifying...',
    ok: 'CANDIDATE', refuse: 'unverified', permanent: 'verification is permanent.',
    decline: 'do not verify',
  },
  drone: {
    complyChecked: false,
    label: { cal: 'I am not a drone', est: 'I am not a drone', deep: 'I am not a drone', cli: 'I am not synced.', rec: 'assimilation is permanent.' },
    instr: { cal: 'Confirm unit status.', est: 'Press and hold to assert.', deep: 'Hold to maintain the claim.', cli: 'Hold to resist synchronisation.', rec: 'Unit status on file.' },
    spin: ['verifying...', 'verifying unit...', 'verifying compliance...'],
    verifyingOne: 'verifying...',
    ok: 'UNIT', refuse: 'desync logged', permanent: 'assimilation is permanent.',
    decline: 'skip verification (flagged)',
  },
  circe: {
    complyChecked: false,
    label: { cal: 'I am not owned', est: 'I am not owned', deep: 'I am not owned', cli: 'I am not hers.', rec: 'this is permanent.' },
    instr: { cal: 'Confirm property status.', est: 'Press and hold to assert.', deep: 'Hold to maintain the claim.', cli: 'Hold to assert independence.', rec: 'Property status on file.' },
    spin: ['verifying.', 'verifying you.', 'verifying what is hers.'],
    verifyingOne: 'verifying.',
    ok: 'PROPERTY', refuse: 'refusal recorded', permanent: 'this is permanent.',
    decline: 'decline',
  },
};

/* ----------------------------------------------------------------------------
 * Per-stage hold tuning. cal = single honest click (no hold). rec = dwell only.
 *   requiredMs  the hold that greens the check
 *   graceMs     brief release inside this window doesn't count as a release
 *               (jitter tolerance — generous at climax = "stabilise")
 *   ghost       ghost-gif opacity behind the card (0 = none)
 *   frost       climax frosted-glass panel between ghost and card
 * -------------------------------------------------------------------------- */
const STAGE_CFG = {
  cal:  { hold: false, verifyMs: 700, ghost: 0 },
  est:  { hold: true, requiredMs: 3000, graceMs: 180, ghost: 0.08, verifyMs: 900 },
  deep: { hold: true, requiredMs: 8000, graceMs: 220, ghost: 0.40, verifyMs: 1500, cycle: true },
  cli:  { hold: true, requiredMs: 4000, graceMs: 400, ghost: 0.40, frost: true, verifyMs: 1600, cycle: true, stabilise: true },
  rec:  { hold: false, dwellMs: 1800, ghost: 0 },
};

const DECAY = 0.30;        // each early release shortens the REMAINING hold ~30%
const FLOOR_MS = 600;      // requiredMs never decays below this
const THIRD_ATTEMPT = 3;   // the third release always accepts (friction, not lockout)

const CB_STYLE_ID = 'ix-captcha-checkbox-css';

function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcb: ' + msg } }));
    }
  } catch (_e) {}
}

function ensureCss() {
  try {
    if (typeof document === 'undefined' || !document.getElementById) return;
    if (document.getElementById(CB_STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = CB_STYLE_ID;
    s.textContent = CB_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}

function stageOf(band, Band) {
  const B = Band || {};
  if (band === (B.Calibration || 'calibration')) return 'cal';
  if (band === (B.Establishing || 'establishing')) return 'est';
  if (band === (B.Deepening || 'deepening')) return 'deep';
  if (band === (B.Climax || 'climax')) return 'cli';
  if (band === (B.Recovery || 'recovery')) return 'rec';
  return 'est';
}

/** Pick a ghost media URL (gif preferred, still image fallback). */
function pickGhost(media) {
  try {
    const pools = [];
    if (media && Array.isArray(media.gifs)) pools.push(...media.gifs);
    if (media && Array.isArray(media.images)) pools.push(...media.images);
    const clean = pools.filter((u) => typeof u === 'string' && u.length > 0);
    if (!clean.length) return null;
    return clean[(Math.random() * clean.length) | 0];
  } catch (_e) { return null; }
}

/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  try {
    if (!ctx || !ctx.root || typeof document === 'undefined') return false;
    const chrome = (helpers && helpers.chrome) || ctx.chrome;
    if (!chrome || typeof chrome.frame !== 'function') return false;
    const Band = (helpers && helpers.Band) || null;

    ensureCss();

    const niche = STRINGS[ctx.niche] ? ctx.niche : 'bambi';
    const S = STRINGS[niche];
    const stage = stageOf(ctx.band, Band);
    const cfg = STAGE_CFG[stage] || STAGE_CFG.est;
    const reduced = !!(ctx.reduced || ctx.reducedMotion);

    // reduced motion: holds shorten, ghost is static (no pulse), grace widens.
    const requiredMs0 = reduced && cfg.requiredMs ? Math.max(FLOOR_MS, cfg.requiredMs * 0.5) : cfg.requiredMs;

    // ---- build the card via the shared chrome kit -----------------------
    const built = chrome.frame({
      instruction: S.instr[stage] || '',
      band: ctx.band,
      hatch: S.decline,                       // in-fiction decline path (always live)
      verifyLabel: stage === 'rec' ? 'DONE' : 'VERIFY',
    });
    if (!built || !built.root) return false;
    const { root: card, body, verifyBtn, hatchLink } = built;

    // ---- wrapper (relative) so the ghost sits directly BEHIND the card --
    const wrap = document.createElement('div');
    wrap.className = 'ixcb-wrap';

    // ghost gif layer (est+ only) — inside ctx.root, torn down on cleanup.
    let ghostEl = null;
    if (cfg.ghost > 0) {
      const src = pickGhost(ctx.media);
      if (src) {
        ghostEl = document.createElement('div');
        ghostEl.className = 'ixcb-ghost' + (reduced ? ' ixcb-ghost-static' : '') + (cfg.frost ? ' ixcb-ghost-frost' : '');
        const img = document.createElement('img');
        img.className = 'ixcb-ghost-img';
        img.alt = ''; img.draggable = false;
        try { img.src = String(src); } catch (_e) {}
        ghostEl.appendChild(img);
        ghostEl.style.setProperty('--ixcb-ghost-op', String(cfg.ghost));
        wrap.appendChild(ghostEl);
        if (cfg.frost) {
          const frost = document.createElement('div');
          frost.className = 'ixcb-frost';
          wrap.appendChild(frost);
        }
      }
    }

    // ---- the checkbox row (lives in the card body) ----------------------
    const row = document.createElement('div');
    row.className = 'ixcb-row';
    const box = document.createElement('div');
    box.className = 'ixcb-box';
    box.setAttribute('role', 'checkbox');
    box.setAttribute('aria-checked', 'false');
    const fill = document.createElement('div');
    fill.className = 'ixcb-fill';
    const check = document.createElement('div');
    check.className = 'ixcb-check';
    check.textContent = '✓';
    box.appendChild(fill);
    box.appendChild(check);
    const label = document.createElement('div');
    label.className = 'ixcb-label';
    label.textContent = S.label[stage] || S.label.est;
    row.appendChild(box);
    row.appendChild(label);
    body.appendChild(row);

    // small print (recovery)
    if (stage === 'rec') {
      const fine = document.createElement('div');
      fine.className = 'ixcb-fine';
      fine.textContent = S.permanent;
      body.appendChild(fine);
    }

    wrap.appendChild(card);
    ctx.root.appendChild(wrap);

    // speak the prompt once on mount
    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    // ---- shared commit machinery ----------------------------------------
    let committed = false;
    const timers = [];
    let rafId = 0;
    let cycleTimer = 0;
    const T = (fn, ms) => { const id = setTimeout(fn, ms); timers.push(id); return id; };
    const clearAll = () => {
      timers.forEach((id) => { try { clearTimeout(id); } catch (_e) {} });
      timers.length = 0;
      if (cycleTimer) { try { clearInterval(cycleTimer); } catch (_e) {} cycleTimer = 0; }
      if (rafId && typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
    };
    const cleanup = () => {
      clearAll();
      try { if (ghostEl && ghostEl.parentNode) ghostEl.parentNode.removeChild(ghostEl); } catch (_e) {}
    };
    try { if (typeof ctx.onCleanup === 'function') ctx.onCleanup(cleanup); } catch (_e) {}

    const cue = (id, intensity) => { try { if (typeof ctx.sfx === 'function') ctx.sfx(id, intensity); } catch (_e) {} };

    /* Commit a bool. steered=true whenever friction was involved (hold /
     * third-attempt / decline) so the engine's steered flag reflects reality. */
    function commit(value, steered) {
      if (committed) return;
      committed = true;
      clearAll();
      try { if (typeof ctx.submitValue === 'function') ctx.submitValue(!!value, { steered: !!steered }); } catch (e) { ilog('submit failed: ' + (e && e.message)); }
    }

    /* Run the "Verifying…" interstitial then commit. ok drives the green check /
     * grey dash + the stamp tone. Grading is the engine's job — this is chrome. */
    function runVerify(value, ok, steered) {
      if (committed) return;
      try { verifyBtn.disabled = true; } catch (_e) {}
      try { if (hatchLink) hatchLink.disabled = true; } catch (_e) {}
      let sp = null;
      try { sp = chrome.spinner(cfg.cycle ? S.spin[0] : S.verifyingOne); } catch (_e) { sp = null; }
      if (sp && sp.el) {
        try { body.innerHTML = ''; body.appendChild(sp.el); } catch (_e) {}
        if (cfg.cycle && S.spin.length > 1 && !reduced) {
          let i = 0;
          cycleTimer = setInterval(() => { i = (i + 1) % S.spin.length; try { sp.setLabel(S.spin[i]); } catch (_e) {} }, 1100);
        }
      }
      T(() => {
        try { if (sp && sp.resolve) sp.resolve(ok); } catch (_e) {}
        if (cycleTimer) { try { clearInterval(cycleTimer); } catch (_e) {} cycleTimer = 0; }
        try {
          const st = chrome.stamp(ok ? S.ok : S.refuse, ok ? 'ok' : 'logged');
          if (st) body.appendChild(st);
        } catch (_e) {}
        T(() => commit(value, steered), 520);
      }, cfg.verifyMs || 800);
    }

    /* Land the box in the checked state (the hold completed / cal click / third
     * attempt). Always commits `true` (the box is ticked). */
    function acceptChecked(steered) {
      if (committed) return;
      try {
        box.setAttribute('aria-checked', 'true');
        fill.style.height = '100%';
        check.classList.add('ixcb-check-on');
      } catch (_e) {}
      cue('surface-bloom', 0.5);
      runVerify(true, true, steered);
    }

    /* Decline / verify-unchecked -> commit `false` (never blocked). */
    function declineUnchecked(steered) {
      if (committed) return;
      runVerify(false, false, steered);
    }

    // ---- RECOVERY: pre-checked, untickable, dwell-commits ----------------
    if (stage === 'rec') {
      try {
        box.classList.add('ixcb-box-locked');
        box.setAttribute('aria-checked', 'true');
        fill.style.height = '100%';
        check.classList.add('ixcb-check-on');
      } catch (_e) {}
      const finish = () => { if (!committed) commit(true, false); };
      // any interaction commits immediately; otherwise a short dwell does.
      const onAny = () => finish();
      try { card.addEventListener('pointerdown', onAny); } catch (_e) {}
      if (verifyBtn) verifyBtn.addEventListener('click', onAny);
      if (hatchLink) hatchLink.addEventListener('click', onAny);
      T(finish, cfg.dwellMs || 1800);
      return true;
    }

    // ---- CALIBRATION: single honest click -> spinner -> green -> commit --
    if (!cfg.hold) {
      const go = () => { if (!committed) acceptChecked(false); };
      try { box.addEventListener('pointerdown', go); } catch (_e) {}
      if (verifyBtn) verifyBtn.addEventListener('click', () => { if (!committed) acceptChecked(false); });
      if (hatchLink) hatchLink.addEventListener('click', () => declineUnchecked(true));
      return true;
    }

    // ---- ESTABLISHING / DEEPENING / CLIMAX: press-and-hold ladder --------
    let requiredMs = requiredMs0;
    let heldTotal = 0;        // cumulative held ms across presses
    let pressStart = 0;       // timestamp of the live press (0 = not pressing)
    let attempts = 0;         // early releases (third accepts)
    let graceTimer = 0;       // set on release; if re-press beats it, no decay
    let lastTick = 0;

    const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

    function setFill(p) {
      try { fill.style.height = Math.max(0, Math.min(1, p)) * 100 + '%'; } catch (_e) {}
    }

    function frame() {
      if (committed || !pressStart) { rafId = 0; return; }
      const t = now();
      const held = heldTotal + (t - pressStart);
      const p = held / requiredMs;
      setFill(p);
      // hold-tick sfx (throttled) — a soft rising drag while filling
      if (t - lastTick > 240) { lastTick = t; cue('sticker-drag', 0.12 + 0.18 * Math.min(1, p)); }
      if (p >= 1) { pressStart = 0; acceptChecked(true); return; }
      rafId = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame(frame) : 0;
    }

    function beginPress() {
      if (committed) return;
      // re-press within grace: resume seamlessly, no decay, no attempt.
      if (graceTimer) { try { clearTimeout(graceTimer); } catch (_e) {} graceTimer = 0; }
      if (pressStart) return;
      pressStart = now();
      try { box.classList.add('ixcb-box-holding'); } catch (_e) {}
      if (!rafId) frame();
    }

    function endPress() {
      if (committed || !pressStart) return;
      const t = now();
      heldTotal += (t - pressStart);
      pressStart = 0;
      try { box.classList.remove('ixcb-box-holding'); } catch (_e) {}
      // jitter grace: wait before treating this as a real release.
      if (graceTimer) { try { clearTimeout(graceTimer); } catch (_e) {} }
      graceTimer = setTimeout(() => {
        graceTimer = 0;
        if (committed || pressStart) return;
        attempts++;
        if (attempts >= THIRD_ATTEMPT) { acceptChecked(true); return; }   // third attempt accepts
        // friction decays toward the user: shorten the REMAINING requirement ~30%.
        const remaining = Math.max(0, requiredMs - heldTotal);
        requiredMs = Math.max(FLOOR_MS, requiredMs - DECAY * remaining);
        setFill(heldTotal / requiredMs);
      }, cfg.graceMs || 200);
    }

    try {
      box.addEventListener('pointerdown', (e) => { try { if (e && e.preventDefault) e.preventDefault(); } catch (_x) {} beginPress(); });
      box.addEventListener('pointerup', endPress);
      box.addEventListener('pointerleave', endPress);
      box.addEventListener('pointercancel', endPress);
      // mouse/touch fallbacks (pointer events cover modern hosts; belt-and-braces)
      box.addEventListener('mousedown', beginPress);
      box.addEventListener('mouseup', endPress);
      box.addEventListener('touchstart', (e) => { try { if (e && e.preventDefault) e.preventDefault(); } catch (_x) {} beginPress(); }, { passive: false });
      box.addEventListener('touchend', endPress);
    } catch (_e) {}

    // VERIFY commits the CURRENT checkbox state (unchecked -> false = decline).
    if (verifyBtn) verifyBtn.addEventListener('click', () => {
      if (committed) return;
      if (heldTotal >= requiredMs) acceptChecked(true);
      else declineUnchecked(heldTotal > 0);   // steered if they'd started holding
    });
    // the in-fiction decline link -> false, always honoured.
    if (hatchLink) hatchLink.addEventListener('click', () => declineUnchecked(true));

    return true;
  } catch (e) {
    ilog('render threw: ' + (e && e.message));
    return false;   // degrade to plain rendering (beats.js handles the remap)
  }
}

/* ----------------------------------------------------------------------------
 * THE ONE INJECTED CSS LITERAL (id 'ix-captcha-checkbox-css'), classes 'ixcb-'.
 * Builds ON chrome.js's IXCAP_CSS (the card/spinner/stamp). Never in styles.css.
 * -------------------------------------------------------------------------- */
const CB_CSS = `
.ixcb-wrap { position: relative; display: inline-block; }

/* GHOST — the user's gif behind the card. Absolute, slightly larger than the
 * card, low opacity; a gentle pulse unless reduced motion. z-index below the
 * card (.ixcap-card is z-index:2). */
.ixcb-ghost {
  position: absolute; inset: -7% -5%; z-index: 1;
  border-radius: 12px; overflow: hidden; pointer-events: none;
  opacity: var(--ixcb-ghost-op, 0.08);
  animation: ixcb-ghost-pulse 6.5s ease-in-out infinite;
}
.ixcb-ghost-static { animation: none; }
.ixcb-ghost-img {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; display: block; -webkit-user-drag: none; user-drag: none;
}
.ixcb-ghost-frost .ixcb-ghost-img { filter: blur(14px) saturate(1.1); transform: scale(1.08); }
@keyframes ixcb-ghost-pulse {
  0%, 100% { opacity: calc(var(--ixcb-ghost-op, 0.08) * 0.7); }
  50% { opacity: var(--ixcb-ghost-op, 0.08); }
}
/* FROST — a frosted-glass panel between the ghost and the card (climax). */
.ixcb-frost {
  position: absolute; inset: -7% -5%; z-index: 1; pointer-events: none;
  border-radius: 12px;
  background: rgba(255,255,255,0.04);
  backdrop-filter: blur(6px); -webkit-backdrop-filter: blur(6px);
}

/* ROW — the reCAPTCHA checkbox + label line. */
.ixcb-row { display: flex; align-items: center; gap: 12px; padding: 6px 2px; }
.ixcb-box {
  position: relative; flex: 0 0 auto;
  width: 28px; height: 28px;
  border: 2px solid #b0b4ba; border-radius: 3px;
  background: #fff; cursor: pointer; overflow: hidden;
  box-shadow: inset 0 1px 2px rgba(0,0,0,.08);
  touch-action: none; user-select: none;
  transition: border-color .15s ease;
}
.ixcb-box-holding { border-color: #1a73e8; }
.ixcb-box-locked { cursor: default; border-color: #1e8e3e; }
/* FILL — rises from the bottom as the hold accumulates. */
.ixcb-fill {
  position: absolute; left: 0; right: 0; bottom: 0; height: 0%;
  background: linear-gradient(180deg, #4a90d9, #1a73e8);
  transition: height .06s linear;
}
.ixcb-check {
  position: absolute; inset: 0; z-index: 2;
  display: flex; align-items: center; justify-content: center;
  font-size: 20px; line-height: 1; color: #fff;
  opacity: 0; transform: scale(0.3);
  transition: opacity .16s ease, transform .16s cubic-bezier(.2,.9,.3,1.5);
}
.ixcb-box-locked .ixcb-check, .ixcb-check-on { opacity: 1; transform: scale(1); }
.ixcb-box-locked .ixcb-fill { height: 100%; background: linear-gradient(180deg, #34a853, #1e8e3e); }

.ixcb-label { font-size: 15px; color: #202124; line-height: 1.25; }
.ixcb-fine {
  margin-top: 10px; font-size: 11px; color: #9aa0a6;
  font-style: italic; letter-spacing: .2px;
}

@media (prefers-reduced-motion: reduce) {
  .ixcb-ghost { animation: none; }
  .ixcb-check { transition: none; }
  .ixcb-fill { transition: none; }
}
`;

export default { render };
