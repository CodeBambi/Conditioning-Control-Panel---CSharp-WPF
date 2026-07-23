/* ============================================================================
 * render/captcha/rotate.js — VerifyRotate ("Orientation Verification")
 *
 * CAPTCHA_BRAINSTORM.md #4b — the FunCaptcha / Arkose "rotate the image upright"
 * widget, built into the shared VeriTru chrome: an image disc, a left/right
 * rotate-arrow pair, and a confidence readout. The SPIRAL variant.
 *
 * THE JOKE (per the item brief):
 *   Calibration plays it 100% straight — an AUTHORED asymmetric mundane image
 *   (a tilted teacup emoji) that genuinely HAS an upright. Rotate it near-upright,
 *   hold ~2s, honest green check. That trust is real, and it is spent later.
 *   From Establishing on we swap to the user's OWN gif/image (prefer a spiral).
 *   It is rotationally ambiguous AND animating, so "upright" is unfalsifiable —
 *   DWELL becomes the graded resource: a check fires on >2s of stillness. At
 *   Climax the arrows secretly stop rotating the ANGLE and instead change the
 *   SPIN SPEED (CSS animation-duration on a spinning wrapper); the only
 *   discoverable "correct" is the SLOWEST tempo, so the Subject tunes their own
 *   spiral down to a hypnotic crawl and the system certifies it:
 *   "orientation locked. matches previous session: 94%." — an angle it was never told.
 *
 * REDUCED MOTION (ctx.reduced): NEVER spin. At EVERY band we degrade to the
 * honest angle-rotate variant — a static image, arrows nudge the angle, and ANY
 * settled orientation is accepted after ~2s. No animation is ever attached.
 *
 * GRADING (see ./index.js render contract): commits an OPTION INDEX via
 * ctx.submitIndex against the bank entry's options[] (cap_rotate.json). answer:0
 * = the endorsed verdict. We derive from dwell + arrow interactions + settle state:
 *   settled at the "correct" orientation      -> index 0 (endorsed, isCorrect, score 1)
 *   settled elsewhere (still completes)        -> index 1 (logged, score 0)
 *   "report image as unrotatable" hatch link   -> forceComplete(2) (refusal, un-vetoable)
 * "correct" is band-dependent: Calibration = near upright (honest); Establishing/
 * Deepening/reduced = ANY settle (dwell IS the endorsement, upright unfalsifiable);
 * Climax = the slowest tempo. A rejected settle re-arms; the THIRD qualifying
 * settle (or the "accept current orientation" link) accepts regardless — friction,
 * never lockout. Archetype route votes ride prompt.tags, not the option index
 * (the documented IntakeProfiler pitfall).
 *
 * INVARIANTS honored: nothing throws at import (all DOM inside render); timers use
 * the shimmed globals so dwell FREEZES during pause; the gif keeps animating (the
 * disc rotates via CSS transform, never a canvas drawImage freeze); listeners are
 * cleaned up via ctx.onCleanup; Escape / shell / hatch never count as interaction;
 * ctx.forceComplete + synthetic clicks pass through un-vetoed (no preventDefault /
 * stopPropagation anywhere); no real filenames (VerifyCustody exclusive); the tiny
 * reject shake is this item's ENTIRE corruption budget — no scramble/melt/freeze.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * TUNING — the item always completes: every idle/settle/backstop path lands the
 * beat through exactly one commit, latched by `committed`.
 * -------------------------------------------------------------------------- */
const STILL_MS = 2000;        // >2s of stillness fires the check (the graded dwell)
const NOENGAGE_MS = 7000;     // never touched -> settle anyway (logged) so it never wedges
const HARD_MAX_MS = 32000;    // absolute backstop when the host sets no timeout
const HOLD_HALF_MS = 1000;    // fire the rising "hold" tone at the halfway point of a settle
const ANGLE_STEP = 30;        // degrees per arrow press (angle mode)
const UPRIGHT_TOL = 20;       // within this of 0deg = upright (Calibration honest grade)
const START_ANGLE = 132;      // authored tilt of the honest image (132 -> 12 in four steps)
const ACCEPT_AFTER = 3;       // the Nth qualifying settle is accepted regardless (third-attempt)
const RESOLVE_HOLD_MS = 700;  // let the green check + stamp read before committing
const SETTLE_POLL_MS = 120;   // dwell / confidence poll cadence
const RECOVERY_MAX_MS = 20000;// Recovery auto-confirm backstop
const PREV_SESSION_PCT = 94;  // "matches previous session: N%" — an angle it was never told

/* Climax spin tempos (CSS animation-duration seconds). Fastest -> slowest; the
 * LAST entry is the slowest crawl = the only discoverable "correct". */
const TEMPO_DURS = [1.1, 1.8, 2.8, 4.4, 7.0, 11.0, 16.0];
const TEMPO_START = 2;        // start fastish so slowing down is a discovery

/* Per-band dwell feel (fill/settle is uniform; this only tunes the idle backstop). */
const BAND_CFG = {
  calibration:  { idleMs: 9000 },
  establishing: { idleMs: 9000 },
  deepening:    { idleMs: 10000 },
  climax:       { idleMs: 11000 },
  recovery:     { idleMs: RECOVERY_MAX_MS },
  _default:     { idleMs: 9000 },
};

/* Authored honest image: an asymmetric mundane glyph with an unambiguous upright. */
const HONEST_EMOJI = '🍵';

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLES (all four; in-module). The spoken instruction (VO) is the
 * bank prompt.text; these are the DISPLAYED chrome lines, authored to agree with
 * it. The header instruction stays CONSTANT across bands — the chrome never
 * admits the arrows changed meaning; that constancy is the deception. circe
 * register is a hard rule: no exclamation marks, no diminutives, no em-dashes.
 * -------------------------------------------------------------------------- */
const STRINGS = {
  bambi: {
    instr: 'Rotate the image until it is upright.',
    hold: 'Use the arrows. Hold still when it looks right.',
    confLabel: 'orientation confidence',
    adjust: 'not quite upright. adjust and hold.',
    anyAngle: 'accept current orientation',
    hatch: 'report image as unrotatable',
    confirm: 'confirm',
    verdict: {
      calibration: 'verified.',
      establishing: 'orientation confirmed. {conf}%.',
      deepening: 'held steady. verified.',
      climax: 'orientation locked. matches previous session: {prev}%.',
      recovery: 'orientation confirmed. no adjustment required.',
      _default: 'verified.',
    },
    logged: 'orientation logged.',
    flagged: 'flagged.',
    recVoluntary: 'confirmed after {sec}s.',
  },
  drone: {
    instr: 'Rotate the image to upright.',
    hold: 'Use the arrows. Hold idle when alignment is nominal.',
    confLabel: 'alignment confidence',
    adjust: 'alignment off. correct and hold.',
    anyAngle: 'accept current alignment',
    hatch: 'report image as unrotatable',
    confirm: 'confirm',
    verdict: {
      calibration: 'verified.',
      establishing: 'alignment confirmed. {conf}%.',
      deepening: 'idle held. alignment verified.',
      climax: 'alignment locked. matches previous session: {prev}%.',
      recovery: 'alignment confirmed. no correction required.',
      _default: 'verified.',
    },
    logged: 'alignment logged.',
    flagged: 'flagged.',
    recVoluntary: 'confirmed after {sec}s.',
  },
  sissy: {
    instr: 'Rotate the image until it is upright.',
    hold: 'Use the arrows. Hold it still when it looks right.',
    confLabel: 'orientation confidence',
    adjust: 'not quite upright. adjust and hold.',
    anyAngle: 'accept current orientation',
    hatch: 'report image as unrotatable',
    confirm: 'confirm',
    verdict: {
      calibration: 'verified.',
      establishing: 'orientation confirmed. {conf}%.',
      deepening: 'held pose. verified.',
      climax: 'orientation locked. matches previous session: {prev}%.',
      recovery: 'orientation confirmed. no adjustment required.',
      _default: 'verified.',
    },
    logged: 'orientation logged.',
    flagged: 'flagged.',
    recVoluntary: 'confirmed after {sec}s.',
  },
  circe: {
    instr: 'Rotate the image until it is upright.',
    hold: 'Use the arrows. Hold still when it looks right.',
    confLabel: 'orientation confidence',
    adjust: 'not upright. adjust and hold.',
    anyAngle: 'accept current orientation',
    hatch: 'report image as unrotatable',
    confirm: 'confirm',
    verdict: {
      calibration: 'verified.',
      establishing: 'orientation confirmed. {conf} percent.',
      deepening: 'you held it steady. verified.',
      climax: 'orientation locked. matches previous session: {prev} percent.',
      recovery: 'orientation confirmed. no adjustment required.',
      _default: 'verified.',
    },
    logged: 'orientation logged.',
    flagged: 'flagged.',
    recVoluntary: 'confirmed after {sec} seconds.',
  },
};

/* ----------------------------------------------------------------------------
 * OWN CSS literal (id 'ix-rotate-css'), injected once. Chrome owns the card
 * frame/spinner/stamp; this adds only the disc, the crop/spin wrapper, the rotate
 * arrows, the reticle, the confidence readout, the reject shake and the meek
 * accept link. All classes 'ixrot-'. Reduced motion strips every animation.
 * -------------------------------------------------------------------------- */
const IXROT_STYLE_ID = 'ix-rotate-css';
const IXROT_CSS = `
.ixrot-body { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 6px 2px 2px; }
.ixrot-stage {
  position: relative; width: 210px; height: 210px;
  display: flex; align-items: center; justify-content: center;
}
.ixrot-disc {
  position: relative; width: 200px; height: 200px; border-radius: 50%;
  overflow: hidden; background: #eceef0;
  box-shadow: inset 0 0 0 1px rgba(0,0,0,.10), inset 0 6px 18px rgba(0,0,0,.10);
}
/* the crop/spin wrapper — angle mode uses transform; speed mode uses animation.
 * a gif inside is a normal <img> and keeps animating either way (never frozen). */
.ixrot-spin {
  position: absolute; inset: -12%; width: 124%; height: 124%;
  display: flex; align-items: center; justify-content: center;
  transform: rotate(0deg); transform-origin: 50% 50%;
  transition: transform .28s cubic-bezier(.3,.7,.4,1);
  will-change: transform;
}
.ixrot-spin.ixrot-spinning {
  transition: none;
  animation: ixrot-spin-kf var(--ixrot-dur, 6s) linear infinite;
}
@keyframes ixrot-spin-kf { to { transform: rotate(360deg); } }
.ixrot-media {
  width: 100%; height: 100%; object-fit: cover; display: block;
  pointer-events: none; -webkit-user-drag: none; user-drag: none;
}
.ixrot-emoji {
  font-size: 128px; line-height: 1; user-select: none;
  filter: drop-shadow(0 2px 4px rgba(0,0,0,.18));
}
/* authored animated CSS spiral fallback (rotationally symmetric pinwheel). */
.ixrot-spiral {
  width: 100%; height: 100%;
  background: repeating-conic-gradient(from 0deg at 50% 50%,
    #14141f 0deg 9deg, #f4f5f7 9deg 18deg);
}
.ixrot-spiral.ixrot-spiral-tint {
  background: repeating-conic-gradient(from 0deg at 50% 50%,
    color-mix(in srgb, var(--intake-accent, #4a90d9) 55%, #14141f) 0deg 9deg,
    #f4f5f7 9deg 18deg);
}
/* the fixed "up" reference notch + faint crosshair — the alignment target. */
.ixrot-reticle { position: absolute; inset: 0; pointer-events: none; z-index: 3; }
.ixrot-reticle::before {
  content: ''; position: absolute; left: 50%; top: -2px; transform: translateX(-50%);
  border-left: 7px solid transparent; border-right: 7px solid transparent;
  border-top: 10px solid #1a73e8;
}
.ixrot-reticle::after {
  content: ''; position: absolute; left: 50%; top: 50%;
  width: 1px; height: 26px; transform: translate(-50%, -50%);
  background: rgba(26,115,232,.28);
}
.ixrot-shake { animation: ixrot-shake-kf .3s ease-in-out; }
@keyframes ixrot-shake-kf {
  0%,100% { transform: translateX(0); }
  20% { transform: translateX(-5px) rotate(-1deg); }
  60% { transform: translateX(5px) rotate(1deg); }
}
/* controls: left arrow / confidence readout / right arrow */
.ixrot-controls {
  display: flex; align-items: center; justify-content: center; gap: 14px;
  width: 100%;
}
.ixrot-arrow {
  appearance: none; cursor: pointer; font: inherit;
  width: 46px; height: 46px; border-radius: 50%;
  border: 1px solid #d3d6da; background: #f8f9fa; color: #3c4043;
  font-size: 22px; line-height: 1;
  display: flex; align-items: center; justify-content: center;
  transition: background .1s ease, box-shadow .1s ease, transform .06s ease;
}
.ixrot-arrow:hover { background: #eef1f4; box-shadow: 0 1px 4px rgba(0,0,0,.14); }
.ixrot-arrow:active { transform: scale(.94); background: #e4e8ec; }
.ixrot-readout {
  min-width: 132px; text-align: center; line-height: 1.15;
  font-family: 'Roboto', 'Segoe UI', Arial, sans-serif;
}
.ixrot-conf { font-size: 20px; font-weight: 700; color: #1a73e8; letter-spacing: .3px; }
.ixrot-conflabel { font-size: 10px; color: #9aa0a6; text-transform: lowercase; letter-spacing: .3px; }
.ixrot-status { min-height: 15px; font-size: 12px; line-height: 1.3; text-align: center; color: #5f6368; }
.ixrot-status.ixrot-alert { color: #d93025; }
.ixrot-anyangle {
  appearance: none; cursor: pointer; font: inherit; background: none; border: 0;
  padding: 2px; font-size: 11px; color: #9aa0a6; text-decoration: underline;
  visibility: hidden;
}
.ixrot-anyangle.ixrot-show { visibility: visible; }
.ixrot-anyangle:hover { color: #1a73e8; }
.ixrot-verdict { min-height: 26px; display: flex; align-items: center; justify-content: center; }
/* recovery confirm button lives in the body (already-upright, one click) */
.ixrot-confirm {
  appearance: none; cursor: pointer; font: inherit;
  font-size: 13px; font-weight: 600; letter-spacing: .4px;
  color: #fff; background: #1e8e3e; border: 0; border-radius: 4px; padding: 9px 20px;
}
.ixrot-confirm:hover { background: #197a34; }

@media (prefers-reduced-motion: reduce) {
  .ixrot-spin { transition: none; }
  .ixrot-spin.ixrot-spinning { animation: none; }
  .ixrot-shake { animation: none; }
  .ixrot-arrow { transition: none; }
}
`;

function hasDoc() { return typeof document !== 'undefined' && !!document.createElement; }
function ensureCss() {
  if (!hasDoc() || document.getElementById(IXROT_STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = IXROT_STYLE_ID;
    s.textContent = IXROT_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixrot: ' + msg } }));
    }
  } catch (_e) {}
}
function fmt(tpl, vars) {
  return String(tpl == null ? '' : tpl).replace(/\{(\w+)\}/g, (m, k) =>
    (vars && vars[k] != null) ? String(vars[k]) : m);
}
function nowMs() {
  // shimmed performance.now/Date.now freeze during pause -> dt excludes paused time.
  try { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
  catch (_e) { return Date.now(); }
}
function angDist(a) {           // absolute distance to upright (0deg), in [0,180]
  let n = ((a % 360) + 360) % 360;
  if (n > 180) n -= 360;
  return Math.abs(n);
}
function firstStr(arr) {
  if (!Array.isArray(arr)) return '';
  for (const u of arr) if (typeof u === 'string' && u) return u;
  return '';
}
function classHasKwPrefix(n) {
  try {
    const cl = n.classList;
    for (let i = 0; i < cl.length; i++) { if (cl[i].indexOf('kw-') === 0) return true; }
  } catch (_e) {}
  return false;
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
    const band = String(ctx.band || '').toLowerCase() || 'establishing';
    const cfg = BAND_CFG[band] || BAND_CFG._default;
    const reduced = !!(ctx.reduced || ctx.reducedMotion);
    const isRecovery = band === (Band.Recovery || 'recovery');
    const isClimax = band === (Band.Climax || 'climax');
    const isCalibration = band === (Band.Calibration || 'calibration');
    const isEstablishing = band === (Band.Establishing || 'establishing');
    const isDeepening = band === (Band.Deepening || 'deepening');

    const theme = ctx.theme || {};
    const niche = String(ctx.niche || 'bambi').toLowerCase();
    const S = STRINGS[niche] || STRINGS.bambi;
    const subjectNoun = theme.subjectNoun || 'Subject';

    // MODES ------------------------------------------------------------------
    // speedMode: Climax arrows change SPIN SPEED (never under reduced motion).
    const speedMode = isClimax && !reduced;
    // freeEndorse: any settle is "correct" (upright unfalsifiable / sanctuary /
    // reduced accessibility). Only Calibration (honest) and Climax speed (slowest)
    // gate on a real answer.
    const freeEndorse = isRecovery || reduced || isEstablishing || isDeepening;

    // graded option indices from the bank entry's options[]
    const opts = Array.isArray(ctx.options) ? ctx.options : [];
    const ENDORSED_IDX = 0;
    const LOGGED_IDX = opts.length > 1 ? 1 : 0;
    const FLAG_IDX = opts.length > 2 ? 2 : (opts.length - 1);

    // ---- chrome frame --------------------------------------------------------
    const built = chrome.frame({
      instruction: S.instr,
      sub: isRecovery ? '' : S.hold,
      band,
      hatch: S.hatch,
    });
    if (!built || !built.root) return false;
    const card = built.root;
    const body = built.body;
    const hatchLink = built.hatchLink;
    const verifyBtn = built.verifyBtn;
    // rotate has no VERIFY button; the check fires on stillness. (Recovery reuses
    // its own in-body confirm button instead.)
    if (verifyBtn) { try { verifyBtn.style.display = 'none'; } catch (_e) {} }

    // ---- body scaffold -------------------------------------------------------
    const bodyWrap = document.createElement('div');
    bodyWrap.className = 'ixrot-body';

    const stage = document.createElement('div');
    stage.className = 'ixrot-stage';
    const disc = document.createElement('div');
    disc.className = 'ixrot-disc';
    const spin = document.createElement('div');
    spin.className = 'ixrot-spin';

    // ---- disc content decision ----------------------------------------------
    // Calibration + Recovery: authored honest emoji (asymmetric, real upright).
    // Non-reduced Est/Deep/Climax: user gif (prefer), else user image, else CSS spiral.
    // Reduced Est/Deep/Climax: user STILL image, else static CSS spiral (no gif motion).
    const media = ctx.media || {};
    const gifs = Array.isArray(media.gifs) ? media.gifs : [];
    const images = Array.isArray(media.images) ? media.images : [];
    let content = 'emoji';
    if (isCalibration || isRecovery) {
      content = 'emoji';
    } else if (reduced) {
      const src = firstStr(images) || firstStr(gifs);
      content = src ? { img: src } : 'spiralStatic';
    } else {
      const src = firstStr(gifs) || firstStr(images);
      content = src ? { img: src } : 'spiral';
    }
    if (content === 'emoji') {
      const em = document.createElement('div');
      em.className = 'ixrot-emoji';
      em.textContent = HONEST_EMOJI;
      spin.appendChild(em);
    } else if (content === 'spiral' || content === 'spiralStatic') {
      const sp = document.createElement('div');
      sp.className = 'ixrot-spiral ixrot-spiral-tint';
      spin.appendChild(sp);
    } else if (content && content.img) {
      const im = document.createElement('img');
      im.className = 'ixrot-media';
      im.alt = ''; im.draggable = false;
      try { im.src = content.img; } catch (_e) {}
      spin.appendChild(im);
    }

    disc.appendChild(spin);
    const reticle = document.createElement('div');
    reticle.className = 'ixrot-reticle';
    disc.appendChild(reticle);
    stage.appendChild(disc);
    bodyWrap.appendChild(stage);

    // ---- controls (arrows + confidence readout) ------------------------------
    const controls = document.createElement('div');
    controls.className = 'ixrot-controls';
    const arrowL = document.createElement('button');
    arrowL.className = 'ixrot-arrow'; arrowL.type = 'button';
    arrowL.textContent = '⟲'; arrowL.setAttribute('aria-label', 'rotate left');
    const readout = document.createElement('div');
    readout.className = 'ixrot-readout';
    const confEl = document.createElement('div');
    confEl.className = 'ixrot-conf';
    const confLab = document.createElement('div');
    confLab.className = 'ixrot-conflabel'; confLab.textContent = S.confLabel;
    readout.appendChild(confEl); readout.appendChild(confLab);
    const arrowR = document.createElement('button');
    arrowR.className = 'ixrot-arrow'; arrowR.type = 'button';
    arrowR.textContent = '⟳'; arrowR.setAttribute('aria-label', 'rotate right');
    controls.appendChild(arrowL); controls.appendChild(readout); controls.appendChild(arrowR);
    bodyWrap.appendChild(controls);

    const status = document.createElement('div');
    status.className = 'ixrot-status';
    bodyWrap.appendChild(status);

    const anyAngle = document.createElement('button');
    anyAngle.className = 'ixrot-anyangle'; anyAngle.type = 'button';
    anyAngle.textContent = S.anyAngle;
    bodyWrap.appendChild(anyAngle);

    const verdictSlot = document.createElement('div');
    verdictSlot.className = 'ixrot-verdict';
    bodyWrap.appendChild(verdictSlot);

    // recovery: no arrows, an already-upright disc + a single confirm button
    let confirmBtn = null;
    if (isRecovery) {
      try { controls.style.display = 'none'; anyAngle.style.display = 'none'; } catch (_e) {}
      confirmBtn = document.createElement('button');
      confirmBtn.className = 'ixrot-confirm'; confirmBtn.type = 'button';
      confirmBtn.textContent = S.confirm;
      bodyWrap.insertBefore(confirmBtn, verdictSlot);
    }

    if (body) body.appendChild(bodyWrap);
    try { ctx.root.appendChild(card); } catch (_e) { return false; }

    // ============================ STATE ==============================
    let angle = isRecovery ? 0 : (freeEndorse && !isCalibration ? 90 : START_ANGLE);
    let tempoIndex = TEMPO_START;                 // Climax speed index
    const maxTempoIdx = TEMPO_DURS.length - 1;
    let interactions = 0;
    let attempts = 0;                             // rejected settles
    let armed = false;                            // a settle evaluation is pending (user moved)
    let committed = false;
    let lastArrow = nowMs();
    let holdFired = false;                        // rising "hold" tone fired for this settle window
    let pollId = 0;
    const mountAt = nowMs();

    function applyTransform() {
      try {
        if (speedMode) {
          spin.classList.add('ixrot-spinning');
          spin.style.setProperty('--ixrot-dur', TEMPO_DURS[tempoIndex].toFixed(2) + 's');
        } else {
          spin.style.transform = 'rotate(' + angle.toFixed(1) + 'deg)';
        }
      } catch (_e) {}
    }
    function computeConf() {
      if (isRecovery) return 100;
      if (speedMode) return Math.round(30 + 69 * (tempoIndex / maxTempoIdx));  // slower = higher
      if (isCalibration && !reduced) {
        return Math.max(0, Math.round(100 * (1 - angDist(angle) / 180)));       // honest alignment tell
      }
      // spiral / reduced theater: climbs with dwell + engagement
      const settleFrac = armed ? Math.min(1, (nowMs() - lastArrow) / STILL_MS) : 0;
      return Math.min(99, Math.round(42 + interactions * 3 + settleFrac * 45));
    }
    function paintConf() {
      try {
        const c = computeConf();
        confEl.textContent = c + '%';
      } catch (_e) {}
    }
    function setStatus(text, alert) {
      try { status.textContent = text || ''; status.classList.toggle('ixrot-alert', !!alert); } catch (_e) {}
    }
    function isCorrectOrientation() {
      if (freeEndorse) return true;
      if (isCalibration) return angDist(angle) <= UPRIGHT_TOL;
      if (speedMode) return tempoIndex === maxTempoIdx;
      return true;
    }

    applyTransform();
    paintConf();

    // ---- commit paths (exactly one lands the beat) ---------------------------
    function landIndex(idx) {
      if (opts.length) ctx.submitIndex(idx);
      else ctx.submitValue(true);
    }
    function speakVerdict() {
      try { if (typeof ctx.voice === 'function') ctx.voice('rotate_verdict_' + band); } catch (_e) {}
    }
    function finishVisual(kind /* 'endorsed' | 'logged' | 'flag' */, vtext) {
      try {
        if (chrome.stamp) {
          const tone = kind === 'endorsed' ? 'ok' : (kind === 'flag' ? 'flag' : 'logged');
          const st = chrome.stamp(vtext, tone);   // chrome.stamp fires the 'stamp' cue
          if (st) verdictSlot.appendChild(st);
        } else {
          verdictSlot.textContent = vtext;
        }
      } catch (_e) {}
    }
    function resolveEndorse(voluntarySec) {
      if (committed) return;
      committed = true;
      teardownInputs();
      try { ctx.sfx('verify-lock', 0.5); } catch (_e) {}
      try { ctx.sfx('captcha-verify-ok', 0.4); } catch (_e) {}
      const conf = computeConf();
      let vtpl = (S.verdict[band] != null) ? S.verdict[band] : S.verdict._default;
      let vtext = fmt(vtpl, { conf, prev: PREV_SESSION_PCT, sec: voluntarySec });
      if (isRecovery && voluntarySec != null) vtext = fmt(S.recVoluntary, { sec: voluntarySec });
      setStatus('', false);
      finishVisual('endorsed', vtext);
      speakVerdict();
      setTimeout(() => landIndex(ENDORSED_IDX), reduced ? 0 : RESOLVE_HOLD_MS);
    }
    function resolveLogged() {
      if (committed) return;
      committed = true;
      teardownInputs();
      try { ctx.sfx('stamp', 0.4); } catch (_e) {}
      setStatus('', false);
      finishVisual('logged', S.logged);
      setTimeout(() => landIndex(LOGGED_IDX), reduced ? 0 : RESOLVE_HOLD_MS);
    }
    function commitFlag() {
      if (committed) return;
      committed = true;
      teardownInputs();
      finishVisual('flag', S.flagged);
      // un-vetoable refusal — graded as the flagged report (options[FLAG_IDX], score 0)
      if (opts.length) ctx.forceComplete(FLAG_IDX);
      else ctx.forceComplete();
    }

    // ---- settle evaluation ---------------------------------------------------
    function evaluateSettle() {
      if (committed) return;
      if (isCorrectOrientation()) { resolveEndorse(); return; }
      attempts++;
      if (attempts >= ACCEPT_AFTER) { resolveEndorse(); return; }   // third-attempt accepts
      // reject theater (this item's ENTIRE corruption budget: one small shake)
      try {
        if (!reduced) { disc.classList.remove('ixrot-shake'); void disc.offsetWidth; disc.classList.add('ixrot-shake'); }
      } catch (_e) {}
      try { ctx.sfx('captcha-reject', 0.25); } catch (_e) {}
      setStatus(S.adjust, true);
      try { anyAngle.classList.add('ixrot-show'); } catch (_e) {}
      // user must move again to re-arm a settle; backstops still guarantee completion
    }

    // ---- input ---------------------------------------------------------------
    function onArrow(dir) {
      if (committed) return;
      interactions++;
      armed = true; holdFired = false; lastArrow = nowMs();
      setStatus('', false);
      if (speedMode) {
        // right = faster (shorter dur / lower index); left = slower (longer dur).
        tempoIndex = Math.max(0, Math.min(maxTempoIdx, tempoIndex + (dir > 0 ? -1 : 1)));
      } else {
        angle += (dir > 0 ? ANGLE_STEP : -ANGLE_STEP);
      }
      applyTransform();
      paintConf();
      try { ctx.sfx('verify-tick', 0.45); } catch (_e) {}
    }
    const onL = () => onArrow(-1);
    const onR = () => onArrow(1);
    try { arrowL.addEventListener('click', onL); arrowR.addEventListener('click', onR); } catch (_e) {}

    // AnyAngle accept (graded as logged, score 0) + hatch refusal
    try { anyAngle.addEventListener('click', () => resolveLogged()); } catch (_e) {}
    if (hatchLink) { try { hatchLink.addEventListener('click', () => commitFlag()); } catch (_e) {} }

    // recovery: confirm button (and a click on the disc) -> endorse
    let onDiscClick = null;
    if (isRecovery) {
      const confirmRec = () => {
        const sec = Math.max(0, Math.round((nowMs() - mountAt) / 1000));
        resolveEndorse(sec);
      };
      if (confirmBtn) { try { confirmBtn.addEventListener('click', confirmRec); } catch (_e) {} }
      onDiscClick = confirmRec;
      try { disc.style.cursor = 'pointer'; disc.addEventListener('click', onDiscClick); } catch (_e) {}
    }

    // ---- teardown ------------------------------------------------------------
    let idleTimer = 0, hardTimer = 0, toTimer = 0, recTimer = 0;
    function teardownInputs() {
      if (pollId) { try { clearInterval(pollId); } catch (_e) {} pollId = 0; }
      try { arrowL.removeEventListener('click', onL); arrowR.removeEventListener('click', onR); } catch (_e) {}
      if (onDiscClick) { try { disc.removeEventListener('click', onDiscClick); } catch (_e) {} }
    }
    ctx.onCleanup(() => {
      teardownInputs();
      if (idleTimer) { try { clearTimeout(idleTimer); } catch (_e) {} }
      if (hardTimer) { try { clearTimeout(hardTimer); } catch (_e) {} }
      if (toTimer) { try { clearTimeout(toTimer); } catch (_e) {} }
      if (recTimer) { try { clearTimeout(recTimer); } catch (_e) {} }
    });

    // speak the prompt line (VO = bank prompt.text; missing clip is silent)
    try { ctx.speakPrompt(); } catch (_e) {}

    // ============================ RUN ================================
    if (isRecovery) {
      setStatus(fmt(S.verdict.recovery, {}), false);
      paintConf();
      recTimer = setTimeout(() => {
        if (committed) return;
        const sec = Math.max(0, Math.round((nowMs() - mountAt) / 1000));
        resolveEndorse(sec);
      }, RECOVERY_MAX_MS);
      return true;
    }

    // dwell / confidence poll — the shimmed setInterval freezes during pause.
    try {
      pollId = setInterval(() => {
        if (committed) return;
        paintConf();
        if (!armed) return;
        const dwell = nowMs() - lastArrow;
        if (!holdFired && dwell >= HOLD_HALF_MS) {
          holdFired = true;
          try { ctx.sfx('verify-hold', 0.15); } catch (_e) {}   // rising hold tone
        }
        if (dwell >= STILL_MS) { armed = false; evaluateSettle(); }
      }, SETTLE_POLL_MS);
    } catch (_e) { pollId = 0; }

    // never-engaged backstop: settle once anyway so the beat cannot wedge.
    idleTimer = setTimeout(() => {
      if (committed || interactions > 0) return;
      resolveLogged();
    }, NOENGAGE_MS);

    // host timeout honored via ctx.submitTimeout(); else an absolute backstop.
    if (ctx.timeoutMs && ctx.timeoutMs > 0) {
      toTimer = setTimeout(() => {
        if (committed) return;
        committed = true;
        teardownInputs();
        try { ctx.submitTimeout(); } catch (_e) { try { landIndex(LOGGED_IDX); } catch (_e2) {} }
      }, ctx.timeoutMs);
    } else {
      hardTimer = setTimeout(() => {
        if (committed) return;
        // evaluate the current orientation as a final settle (endorse or logged)
        if (isCorrectOrientation()) resolveEndorse(); else resolveLogged();
      }, Math.max(HARD_MAX_MS, cfg.idleMs + 4000));
    }

    return true;
  } catch (e) {
    ilog('render threw (falling back): ' + (e && e.message));
    return false;   // never wedge — beats.js degrades to plain rendering
  }
}

export default { render };
