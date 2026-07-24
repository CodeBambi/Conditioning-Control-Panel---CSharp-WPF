/* ============================================================================
 * render/captcha/checkbox.js — VerifyCheckbox ("I am not a robot")  [Wave-2]
 *
 * CAPTCHA_BRAINSTORM.md SYNTHESIS #3, fully laddered. The most-trusted widget on
 * the internet, cloned pitch-perfect through chrome.js (VeriTru card + spinner +
 * stamp), then degraded band-by-band while the chrome stays constant:
 *
 *   Calibration (heat0) single honest click -> brief spinner -> green -> commit.
 *     Stays 100% clinical/straight — no hold, no zoom, no fullscreen effect.
 *   Establishing / Deepening / Climax (the HOLD ladder) a press-and-hold whose
 *     duration and jitter tolerance deepen by band. While held:
 *       · a slow continuous ZOOM pulls the whole card in (deeper bands zoom
 *         further); releasing early eases the zoom back out (never snaps).
 *       · ONE randomly-rolled FULLSCREEN EFFECT (spiral / pink filter /
 *         braindrain gif) fades in from ~1% toward ~40% across the seconds
 *         needed to complete the hold; releasing early decays it back down;
 *         a successful verify fades it out fast. It parents to <body>,
 *         pointer-events:none, sits just BELOW the pause button, and is ALWAYS
 *         torn down (ctx.onCleanup + on commit).
 *       · the caption escalates through 3-4 lewder rungs as the hold deepens,
 *         AND by band (suggestive undertow -> openly intimate -> lewd surrender).
 *   Recovery (heat0) the box is PRE-CHECKED + untickable, "verification is
 *     permanent."; any interaction / a short dwell commits, graded correct.
 *
 * The zoom + one fullscreen effect REPLACE the old behind-card gif "ghost"/frost
 * (CAPTCHA_HANDOFF.md §4.3: max ONE corruption system per item per band — the
 * effect IS the whole budget now, the ghost is retired).
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
 * the third attempt always accepts. The escalating hold copy belongs to the act
 * of holding in each niche's register (for drone/circe the verdicts narrate the
 * capture the hold enacts, not the refusal the box asserts).
 *
 * No coupling: SFX via ctx.sfx (existing ids); the effect layer parents to <body>
 * and is torn down via ctx.onCleanup + on commit — it must not survive the beat.
 * Nothing throws at import (every DOM/window touch is in render()). is-correct /
 * is-answer are never used. installSteering is skipped: the hold IS the friction.
 * Timers/rAF use the shimmed globals so everything freezes with the pause shim.
 * ctx.reduced: no zoom, and the effect degrades to a static <=15% pink tint with
 * no animation (no rotating spiral, no gif).
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * PER-NICHE STRING TABLE (keyed by ctx.niche). `complyChecked` documents the
 * grading direction the staging bank encodes for est/deep/climax — the module
 * does not grade (the engine does), it only renders the coherent copy and routes
 * the raw checkbox bool. bambi/sissy: checking the affirmation is compliance.
 * drone/circe: the caption states what CHECKING asserts (always the refusal), so
 * compliance is declining to check.
 *
 *   label[cal|rec]   the resting caption for the two non-hold stages.
 *   hold[est|deep|cli]  the in-hold escalation ladder. rung[0] is the caption
 *                       shown at rest; it swaps rung-by-rung as the hold deepens.
 *                       Escalation direction: est = suggestive undertow beneath
 *                       clinical phrasing; deep = openly intimate; cli = explicitly
 *                       lewd surrender anchored on "I am not resisting." (bambi/
 *                       sissy) / the band's refusal label (drone/circe).
 *   instr[stage]     the header instruction (escalates by band).
 *   spin[stage]      the "Verifying…" interstitial lines (cal straight; deep/cli
 *                    cycle). Text-only lewd; always in the verification-vendor
 *                    fiction (verdicts, "subject" framing, "verified/logged").
 *
 * Circe's register is a hard rule: NO exclamation marks, NO diminutives, NO
 * em-dashes — cold keyholder verdicts only.
 * -------------------------------------------------------------------------- */
const STRINGS = {
  bambi: {
    complyChecked: true,
    label: { cal: 'I am not a robot', rec: 'verification is permanent.' },
    hold: {
      est:  ['I am not a robot', 'keep holding, good girl', 'verified soft and pink'],
      deep: ['verifying you...', "verifying you're mine...", 'held open, held pink, held mine'],
      cli:  ['I am not resisting.', 'I am soft and open for you.', 'keep me held until I drip green', 'verified empty. verified yours.'],
    },
    instr: {
      cal: 'Confirm you are human.',
      est: "Press and hold to confirm you're a good girl.",
      deep: 'Keep holding. Let it verify you all the way down.',
      cli: 'Hold still and let it finish emptying you.',
      rec: 'Verification on file.',
    },
    spin: {
      cal:  ['verifying...'],
      est:  ['verifying...', 'verifying you...'],
      deep: ['verifying you...', "verifying you're mine...", 'verifying yours...'],
      cli:  ['verifying every soft inch...', "verifying you're empty...", "verifying you're kept..."],
    },
    verifyingOne: 'verifying...',
    ok: 'HUMAN', refuse: 'unverified', permanent: 'verification is permanent.',
    decline: 'do not verify',
  },
  sissy: {
    complyChecked: true,
    label: { cal: 'I am not a robot', rec: 'verification is permanent.' },
    hold: {
      est:  ['I am not a robot', 'hold it like a good girl', 'verified pretty and obedient'],
      deep: ['verifying you...', 'verifying the good girl...', 'held pretty, held open, held owned'],
      cli:  ['I am not resisting.', 'I am your perfect little thing.', 'hold me until my head is empty and pink', 'verified obedient. verified yours.'],
    },
    instr: {
      cal: 'Confirm you are human.',
      est: "Press and hold to prove you're a good girl.",
      deep: 'Keep holding, princess. Let it verify all of you.',
      cli: 'Hold still and let it finish making you perfect and empty.',
      rec: 'Verification on file.',
    },
    spin: {
      cal:  ['verifying...'],
      est:  ['verifying...', 'verifying you...'],
      deep: ['verifying you...', 'verifying the good girl...', 'verifying yours...'],
      cli:  ['verifying obedience...', "verifying you're empty...", "verifying you're kept..."],
    },
    verifyingOne: 'verifying...',
    ok: 'CANDIDATE', refuse: 'unverified', permanent: 'verification is permanent.',
    decline: 'do not verify',
  },
  drone: {
    complyChecked: false,
    label: { cal: 'I am not a drone', rec: 'assimilation is permanent.' },
    hold: {
      est:  ['I am not a drone', 'resistance noted, sync rising', 'output climbing toward optimal'],
      deep: ['verifying unit...', 'verifying you belong to the hive...', 'sync running deep and slick'],
      cli:  ['I am not synced.', 'the denial fails, the unit runs hot', 'hold until assimilation finishes inside you', 'synced. optimal. drained well.'],
    },
    instr: {
      cal: 'Confirm unit status.',
      est: 'Press and hold to assert unit status.',
      deep: 'Maintain the hold while sync completes.',
      cli: 'Hold while assimilation finishes routing through you.',
      rec: 'Unit status on file.',
    },
    spin: {
      cal:  ['verifying...'],
      est:  ['verifying...', 'verifying unit...'],
      deep: ['verifying unit...', 'verifying sync...', 'verifying compliance...'],
      cli:  ['verifying assimilation...', 'verifying output...', 'verifying the unit is kept...'],
    },
    verifyingOne: 'verifying...',
    ok: 'UNIT', refuse: 'desync logged', permanent: 'assimilation is permanent.',
    decline: 'skip verification (flagged)',
  },
  circe: {
    complyChecked: false,
    label: { cal: 'I am not owned', rec: 'this is permanent.' },
    hold: {
      est:  ['I am not owned', 'the objection is noted and overruled', 'you hold the way property holds'],
      deep: ['verifying you.', 'verifying what is already hers.', 'you are being logged as taken'],
      cli:  ['I am not hers.', 'the denial is recorded and dismissed', 'hold until the last of you is signed over', 'owned. filed. kept.'],
    },
    instr: {
      cal: 'Confirm property status.',
      est: 'Press and hold to enter your objection.',
      deep: 'Hold while the claim is finalised.',
      cli: 'Hold while ownership is signed and sealed.',
      rec: 'Property status on file.',
    },
    spin: {
      cal:  ['verifying.'],
      est:  ['verifying.', 'verifying you.'],
      deep: ['verifying you.', 'verifying what is hers.', 'verifying the claim.'],
      cli:  ['verifying ownership.', 'verifying you are emptied.', 'verifying you are kept.'],
    },
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
 *   zoom        extra card scale at a completed hold (1 + zoom); deeper = more
 * The behind-card ghost/frost is retired: the fullscreen effect (built below) is
 * the whole per-band corruption budget now.
 * -------------------------------------------------------------------------- */
const STAGE_CFG = {
  cal:  { hold: false, verifyMs: 700 },
  est:  { hold: true, requiredMs: 3000, graceMs: 180, zoom: 0.12, verifyMs: 900 },
  deep: { hold: true, requiredMs: 8000, graceMs: 220, zoom: 0.18, verifyMs: 1500, cycle: true },
  cli:  { hold: true, requiredMs: 4000, graceMs: 400, zoom: 0.24, verifyMs: 1600, cycle: true, stabilise: true },
  rec:  { hold: false, dwellMs: 1800 },
};

const DECAY = 0.30;        // each early release shortens the REMAINING hold ~30%
const FLOOR_MS = 600;      // requiredMs never decays below this
const THIRD_ATTEMPT = 3;   // the third release always accepts (friction, not lockout)

const EFFECT_MAX_OP = 0.40;     // fullscreen effect opacity at a completed hold
const EFFECT_MIN_OP = 0.01;     // effect opacity at rest / hold start
const EFFECT_REDUCED_OP = 0.12; // static tint under reduced motion (<= 0.15, no anim)
const EFFECT_DECAY_MS = 900;    // on release, full effect/zoom decays to 0 in ~0.9s

/* The effect layer sits just BELOW the pause button (kawaii.css .kw-pause-btn is
 * z-index 2147480900), above the run's own effect stack, always pointer-events:none. */
const EFFECT_Z = 2147480800;

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

/** Pick a media URL for the braindrain effect (gif preferred, still fallback). */
function pickMedia(media) {
  try {
    const pools = [];
    if (media && Array.isArray(media.gifs)) pools.push(...media.gifs);
    if (media && Array.isArray(media.images)) pools.push(...media.images);
    const clean = pools.filter((u) => typeof u === 'string' && u.length > 0);
    if (!clean.length) return null;
    return clean[(Math.random() * clean.length) | 0];
  } catch (_e) { return null; }
}

const clampU = (v) => (v < 0 ? 0 : (v > 1 ? 1 : v));

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

    // reduced motion: holds shorten, grace widens.
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

    // ---- wrapper (relative + the hold-zoom transform target) ------------
    const wrap = document.createElement('div');
    wrap.className = 'ixcb-wrap';

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
    // hold stages open on the ladder's first rung; cal/rec use the static label.
    const ladder = cfg.hold ? ((S.hold && S.hold[stage]) || null) : null;
    label.textContent = (ladder && ladder[0]) || (S.label && S.label[stage]) || (S.label && S.label.cal) || '';
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

    /* ---- FULLSCREEN EFFECT + HOLD-ZOOM (est/deep/cli only) ---------------
     * ONE random effect per beat (spiral / pink / braindrain). Parents to <body>
     * (stage.innerHTML is wiped per render), pointer-events:none, below the pause
     * button, torn down via ctx.onCleanup + on commit. All hold visuals ride the
     * same hold-progress the module already tracks. */
    let fxEl = null;
    let fxKind = null;
    let visRaf = 0;
    let visFrozen = false;   // set on verify/commit: stop driving hold visuals
    let vis = 0;             // 0..1 smoothed hold intensity (drives zoom + effect)
    let lastRung = 0;
    const zoomMax = cfg.zoom || 0;

    function buildEffect() {
      try {
        if (!document.body) return null;
        const accent = (ctx.theme && typeof ctx.theme.accent === 'string' && ctx.theme.accent) || '#ff69b4';
        const accent2 = (ctx.theme && typeof ctx.theme.accent2 === 'string' && ctx.theme.accent2) || accent;
        // available kinds — reduced motion collapses to a static pink tint only.
        let kinds;
        let drainSrc = null;
        if (reduced) {
          kinds = ['pink'];
        } else {
          kinds = ['spiral', 'pink'];
          drainSrc = pickMedia(ctx.media);
          if (drainSrc) kinds.push('drain');
        }
        const kind = kinds[(Math.random() * kinds.length) | 0];
        const layer = document.createElement('div');
        layer.className = 'ixcb-fx ixcb-fx-' + kind;
        layer.style.setProperty('--ixcb-accent', accent);
        layer.style.setProperty('--ixcb-accent2', accent2);
        layer.style.zIndex = String(EFFECT_Z);
        layer.style.opacity = reduced ? String(EFFECT_REDUCED_OP) : String(EFFECT_MIN_OP);
        if (kind === 'spiral') {
          const sp = document.createElement('div');
          sp.className = 'ixcb-fx-spiral';
          sp.style.animationDuration = (stage === 'cli' ? 9 : stage === 'deep' ? 12 : 16) + 's';
          layer.appendChild(sp);
        } else if (kind === 'drain' && drainSrc) {
          const d = document.createElement('div');
          d.className = 'ixcb-fx-drainimg';
          const img = document.createElement('img');
          img.alt = ''; img.draggable = false;
          try { img.src = String(drainSrc); } catch (_e) {}
          d.appendChild(img);
          layer.appendChild(d);
        } else { // pink (and the reduced-motion default)
          const p = document.createElement('div');
          p.className = 'ixcb-fx-pinkwash';
          layer.appendChild(p);
        }
        document.body.appendChild(layer);
        fxKind = kind;
        ilog('effect ' + kind + ' @ ' + stage + (reduced ? ' (reduced)' : ''));
        return layer;
      } catch (_e) { return null; }
    }

    function applyZoom(v) {
      if (reduced || !zoomMax) return;
      try { wrap.style.transform = 'scale(' + (1 + zoomMax * clampU(v)).toFixed(4) + ')'; } catch (_e) {}
    }
    function applyEffect(v) {
      if (reduced || !fxEl) return;   // reduced = static tint, never per-frame driven
      const op = EFFECT_MIN_OP + (EFFECT_MAX_OP - EFFECT_MIN_OP) * clampU(v);
      try { fxEl.style.opacity = op.toFixed(3); } catch (_e) {}
    }
    function updateHoldLabel(v) {
      if (!ladder || !ladder.length) return;
      let idx = Math.floor(clampU(v) * ladder.length);
      if (idx >= ladder.length) idx = ladder.length - 1;
      if (idx !== lastRung) { lastRung = idx; try { label.textContent = ladder[idx]; } catch (_e) {} }
    }
    // ease the hold visuals out (successful verify / decline) — never a snap.
    function releaseVisuals() {
      visFrozen = true;
      if (visRaf && typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(visRaf); } catch (_e) {} visRaf = 0; }
      try { if (fxEl) { fxEl.style.transition = 'opacity .45s ease'; fxEl.style.opacity = '0'; } } catch (_e) {}
      try { if (!reduced && zoomMax) { wrap.style.transition = 'transform .5s ease'; wrap.style.transform = 'scale(1)'; } } catch (_e) {}
    }

    if (cfg.hold) fxEl = buildEffect();

    // ---- shared commit machinery ----------------------------------------
    let committed = false;
    const timers = [];
    let cycleTimer = 0;
    const T = (fn, ms) => { const id = setTimeout(fn, ms); timers.push(id); return id; };
    const clearAll = () => {
      timers.forEach((id) => { try { clearTimeout(id); } catch (_e) {} });
      timers.length = 0;
      if (cycleTimer) { try { clearInterval(cycleTimer); } catch (_e) {} cycleTimer = 0; }
      if (visRaf && typeof cancelAnimationFrame === 'function') { try { cancelAnimationFrame(visRaf); } catch (_e) {} visRaf = 0; }
    };
    const cleanup = () => {
      clearAll();
      try { if (fxEl && fxEl.parentNode) fxEl.parentNode.removeChild(fxEl); } catch (_e) {}
      fxEl = null;
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
      releaseVisuals();                       // ease the zoom + fade the effect out fast
      try { verifyBtn.disabled = true; } catch (_e) {}
      try { if (hatchLink) hatchLink.disabled = true; } catch (_e) {}
      const spins = (S.spin && S.spin[stage]) || [S.verifyingOne || 'verifying...'];
      let sp = null;
      try { sp = chrome.spinner(spins[0]); } catch (_e) { sp = null; }
      if (sp && sp.el) {
        try { body.innerHTML = ''; body.appendChild(sp.el); } catch (_e) {}
        if (cfg.cycle && spins.length > 1 && !reduced) {
          let i = 0;
          cycleTimer = setInterval(() => { i = (i + 1) % spins.length; try { sp.setLabel(spins[i]); } catch (_e) {} }, 1100);
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
    let lastVisT = 0;

    const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());

    function setFill(p) {
      try { fill.style.height = Math.max(0, Math.min(1, p)) * 100 + '%'; } catch (_e) {}
    }

    /* ONE persistent visual loop: while pressing it advances the fill + zoom +
     * effect + caption to live hold-progress and completes at p>=1; while released
     * it eases the zoom/effect back down (friction decays, never lockout), then
     * idles until the next press. Uses the shimmed rAF so it freezes on pause. */
    function visLoop() {
      if (committed || visFrozen) { visRaf = 0; return; }
      const t = now();
      const dt = lastVisT ? (t - lastVisT) : 16;
      lastVisT = t;
      if (pressStart) {
        const held = heldTotal + (t - pressStart);
        const frac = held / requiredMs;
        setFill(frac);
        vis = Math.min(1, frac);
        if (t - lastTick > 240) { lastTick = t; cue('sticker-drag', 0.12 + 0.18 * vis); }
        updateHoldLabel(vis);
        applyZoom(vis); applyEffect(vis);
        if (frac >= 1) { pressStart = 0; acceptChecked(true); return; }
      } else if (vis > 0) {
        vis = Math.max(0, vis - dt / EFFECT_DECAY_MS);
        updateHoldLabel(vis);
        applyZoom(vis); applyEffect(vis);
        if (vis <= 0) { lastVisT = 0; visRaf = 0; return; }   // idle until re-press
      } else {
        lastVisT = 0; visRaf = 0; return;                     // idle
      }
      visRaf = (typeof requestAnimationFrame === 'function') ? requestAnimationFrame(visLoop) : 0;
    }

    function kickLoop() {
      if (committed || visFrozen) return;
      if (!visRaf) { lastVisT = 0; visLoop(); }
    }

    function beginPress() {
      if (committed || visFrozen) return;
      // re-press within grace: resume seamlessly, no decay, no attempt.
      if (graceTimer) { try { clearTimeout(graceTimer); } catch (_e) {} graceTimer = 0; }
      if (pressStart) return;
      pressStart = now();
      try { box.classList.add('ixcb-box-holding'); } catch (_e) {}
      kickLoop();
    }

    function endPress() {
      if (committed || !pressStart) return;
      const t = now();
      heldTotal += (t - pressStart);
      pressStart = 0;
      try { box.classList.remove('ixcb-box-holding'); } catch (_e) {}
      kickLoop();   // keep the loop alive to EASE the zoom/effect back down
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
.ixcb-wrap {
  position: relative; display: inline-block;
  transform-origin: center center; will-change: transform;
}

/* ---- FULLSCREEN EFFECT LAYER ------------------------------------------------
 * Parents to <body>, pointer-events:none, z-index set inline (just below the
 * pause button). Opacity is JS-driven while holding (est/deep/cli); it fades out
 * on verify. Under reduced motion it is a single static <=15% tint (no anim). */
.ixcb-fx {
  position: fixed; inset: 0; pointer-events: none; overflow: hidden;
  opacity: 0.01; will-change: opacity;
}
/* (a) SPIRAL — a slow rotating hypnotic conic wedge set, themed to the accent. */
.ixcb-fx-spiral {
  position: absolute; inset: -30%; will-change: transform;
  background: conic-gradient(from 0deg at 50% 50%,
    transparent 0deg, var(--ixcb-accent, #ff69b4) 28deg, transparent 70deg,
    rgba(255,255,255,.55) 150deg, transparent 195deg,
    var(--ixcb-accent2, var(--ixcb-accent, #ff69b4)) 262deg, transparent 312deg);
  animation: ixcb-fx-spin 14s linear infinite;
}
@keyframes ixcb-fx-spin { to { transform: rotate(360deg); } }
/* (b) PINK FILTER — a fullscreen accent tint wash (also the reduced default). */
.ixcb-fx-pinkwash {
  position: absolute; inset: 0;
  background: radial-gradient(120% 120% at 50% 42%,
    var(--ixcb-accent, #ff69b4) 0%,
    color-mix(in srgb, var(--ixcb-accent, #ff69b4) 55%, #1a0a14) 100%);
}
/* (c) BRAINDRAIN — one of the user's own gifs stretched near-fullscreen (cover). */
.ixcb-fx-drainimg { position: absolute; inset: -4%; }
.ixcb-fx-drainimg img {
  width: 100%; height: 100%; object-fit: cover; display: block;
  filter: blur(2px) saturate(1.15) brightness(.95);
  -webkit-user-drag: none; user-drag: none;
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
  .ixcb-fx-spiral { animation: none; }
  .ixcb-check { transition: none; }
  .ixcb-fill { transition: none; }
}
`;

export default { render };
