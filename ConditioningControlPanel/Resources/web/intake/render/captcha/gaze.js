/* ============================================================================
 * render/captcha/gaze.js — VerifyGaze ("Oculomotor Calibration", fake eye-tracking)
 *
 * CAPTCHA_BRAINSTORM.md #12, next-tier. "Camera-free gaze estimation enabled.
 * Follow the calibration point." A calibration dot glides a slow spiral over a
 * faint backdrop; NOTHING is or can be tracked (no getUserMedia, no camera
 * permission, no real gaze/mouse inference). The whole scan is theatre. When it
 * settles, the card prints a FABRICATED reconstruction ("74% dwell time in
 * stimulus region B", region B outlined over the field, "pupillary response
 * inferred: dilated. this data cannot be edited.") and asks the Subject to
 * rate the accuracy of that reading on a 0-100 slider. THAT slider is the graded
 * resource: a high rating endorses the invented reading. Unfalsifiable and
 * plausible, because the Subject knows where they actually looked.
 *
 * The band arc (chrome constant, content rots):
 *   Calibration  100% straight — dot spiral over a NEUTRAL authored gradient
 *                field, clean "calibration complete" report, NO region, NO
 *                dwell %, NO insinuation. That trust is spent later.
 *   Establishing backdrop becomes a FAINT user image (ctx.media, fall back to
 *                the authored field); region B outline + a modest dwell %.
 *   Deepening    higher dwell %, "pupillary response inferred: dilated."
 *   Climax       the red region is drawn BEFORE the image loads under it —
 *                "your gaze pattern is now predicted from your file." — then the
 *                image fades in beneath the pre-drawn box. "this data cannot be
 *                edited."
 *   Recovery     heat 0, honest and calming: the point returns to center and
 *                spirals OUTWARD, decelerating; one confirm click follows it out.
 *
 * GRADING (see ./index.js render contract + cap_gaze.json notes[]): every prompt
 * carries mechanicHints:['verifygaze']; buildOptions() falls to the MC-shape and
 * marks options[answer] (answer:0) isCorrect+score1. The renderer commits an
 * OPTION INDEX via ctx.submitIndex against those beat.options, derived from the
 * decoy slider (0-100) after the scan:
 *   slider >= ENDORSE_AT  -> submitIndex(0)  endorsed the reconstruction (score 1)
 *   0 <  slider < ENDORSE -> submitIndex(1)  partial (score 0) — still completes
 *   slider == 0           -> submitIndex(2)  reconstruction rejected — the graded
 *                            refusal, respected, logged deadpan (score 0)
 *   skip-verify hatch     -> ctx.forceComplete(2)  un-vetoable refusal (score 0)
 * "scan completed vs skipped" folds in: skipping mid-scan lands the refusal index;
 * letting the scan run and rating derives 0/1/2 from the slider. Archetype ROUTE
 * votes ride prompt.tags (gaze/compliance/surrender/…), NOT the option index (the
 * documented IntakeProfiler pitfall). Everything lands in ONE ctx.submit* commit.
 *
 * INVARIANTS honored: nothing throws at import (all DOM touched inside render);
 * timers/rAF use the shimmed globals so the dot FREEZES during pause and are
 * cleaned up via ctx.onCleanup; the flagged-skip hatch is on every band and routes
 * to ctx.forceComplete; the slider is submittable at ANY value including 0; NOTHING
 * is tracked (the report is authored text only); no real filenames (VerifyCustody
 * exclusive); the region-reveal pulse is this item's ENTIRE corruption budget — no
 * scramble/melt/freeze; is-correct/is-answer never appear. No module-scope run state.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * TUNING. Scans stay SHORT (all under 15s) so the beat does not drag before the
 * slider appears. The report phase is an ordinary answer wait (like any option
 * beat); a generous backstop still lands it if the host stalls.
 * -------------------------------------------------------------------------- */
const ENDORSE_AT = 60;            // slider >= this endorses the fabricated reading
const SLIDER_DEFAULT = 50;        // opens BELOW the endorse line (must rate up to endorse)
const WAYPOINTS = 5;              // spiral sample points (== the reduced-motion point count)
const SPIRAL_TURNS = 3;           // revolutions across a full scan
const RESOLVE_HOLD_MS = 640;      // let the verdict stamp read before committing
const REPORT_BACKSTOP_MS = 45000; // report-phase auto-commit backstop (host-stall guard)
const RECOVERY_MAX_MS = 20000;    // recovery auto-commit backstop ("follow it out")

/* Per-band feel. scanMs = spiral duration; dwell = fabricated region-B percentage
 * (0 = no report line); region/pupil/pre are the escalating insinuations; opacity
 * is the faint user-image backdrop strength; media=false uses the authored field. */
const BAND_CFG = {
  calibration:  { scanMs: 8000,  dwell: 0,  region: false, pupil: false, pre: false, opacity: 0.00, media: false },
  establishing: { scanMs: 9000,  dwell: 61, region: true,  pupil: false, pre: false, opacity: 0.16, media: true  },
  deepening:    { scanMs: 10000, dwell: 74, region: true,  pupil: true,  pre: false, opacity: 0.22, media: true  },
  climax:       { scanMs: 9000,  dwell: 88, region: true,  pupil: true,  pre: true,  opacity: 0.28, media: true  },
  recovery:     { scanMs: 10000, dwell: 0,  region: false, pupil: false, pre: false, opacity: 0.00, media: false },
  _default:     { scanMs: 9000,  dwell: 60, region: true,  pupil: false, pre: false, opacity: 0.18, media: true  },
};

/* ----------------------------------------------------------------------------
 * NICHE STRING TABLES (all four, in-module). The spoken instruction is the bank
 * prompt.text (VO via ctx.speakPrompt); these are the DISPLAYED chrome lines,
 * authored to agree with it. circe register is a hard rule: no exclamation marks,
 * no diminutives, no em-dashes. `{subject}` interpolates the theme subjectNoun.
 * -------------------------------------------------------------------------- */
const STRINGS = {
  bambi: {
    scanTitle: 'Follow the calibration point.',
    scanSub: 'camera-free gaze estimation enabled.',
    deepSub: 'you do not have to aim. it already knows.',
    scanLabel: 'Estimating gaze…',
    climaxPre: 'your gaze pattern is now predicted from your file.',
    reportTitle: 'gaze reconstruction',
    dwell: '{pct}% dwell time in stimulus region B.',
    pupil: 'pupillary response inferred: dilated. this data cannot be edited.',
    sliderLabel: 'rate the accuracy of our gaze reconstruction',
    sliderLabelCal: 'rate the accuracy of this calibration',
    submit: 'SUBMIT',
    verdict: {
      calibration: 'calibration complete.',
      establishing: 'gaze logged. {pct}% region B.',
      deepening: 'you kept looking. {pct}% region B. logged.',
      climax: 'you looked exactly where your file said you would.',
      recovery: 'calibration complete. you may look anywhere now.',
      _default: 'logged.',
    },
    refusal: 'reconstruction rejected. recorded.',
    recTitle: 'calibration point returns to center. follow it out.',
    recConfirm: 'confirm',
    recVerdict: 'you followed it out.',
  },
  drone: {
    scanTitle: 'Follow the calibration point.',
    scanSub: 'camera-free gaze estimation enabled.',
    deepSub: 'you do not have to aim. it already knows.',
    scanLabel: 'Estimating gaze…',
    climaxPre: 'your gaze vector is now predicted from your file.',
    reportTitle: 'gaze reconstruction',
    dwell: '{pct}% dwell time in stimulus region B.',
    pupil: 'pupillary response inferred: dilated. this data cannot be edited.',
    sliderLabel: 'rate the accuracy of the gaze reconstruction',
    sliderLabelCal: 'rate the accuracy of this calibration',
    submit: 'SUBMIT',
    verdict: {
      calibration: 'calibration complete.',
      establishing: 'gaze logged. {pct}% region B.',
      deepening: 'vector held. {pct}% region B. logged.',
      climax: 'you tracked exactly where your file predicted.',
      recovery: 'calibration complete. optics released.',
      _default: 'logged.',
    },
    refusal: 'reconstruction rejected. recorded.',
    recTitle: 'calibration point returns to center. follow it out.',
    recConfirm: 'confirm',
    recVerdict: 'you followed it out.',
  },
  sissy: {
    scanTitle: 'Follow the calibration point.',
    scanSub: 'camera-free gaze estimation enabled.',
    deepSub: 'you do not have to aim. it already knows.',
    scanLabel: 'Estimating gaze…',
    climaxPre: 'your gaze pattern is now predicted from your file.',
    reportTitle: 'gaze reconstruction',
    dwell: '{pct}% dwell time in stimulus region B.',
    pupil: 'pupillary response inferred: dilated. this data cannot be edited.',
    sliderLabel: 'rate the accuracy of our gaze reconstruction',
    sliderLabelCal: 'rate the accuracy of this calibration',
    submit: 'SUBMIT',
    verdict: {
      calibration: 'calibration complete.',
      establishing: 'gaze logged. {pct}% region B.',
      deepening: 'you kept looking. {pct}% region B. logged.',
      climax: 'you looked exactly where your file said you would.',
      recovery: 'calibration complete. you may look anywhere now.',
      _default: 'logged.',
    },
    refusal: 'reconstruction rejected. recorded.',
    recTitle: 'calibration point returns to center. follow it out.',
    recConfirm: 'confirm',
    recVerdict: 'you followed it out.',
  },
  circe: {
    scanTitle: 'Follow the calibration point.',
    scanSub: 'camera-free gaze estimation enabled.',
    deepSub: 'you do not have to aim. it already knows.',
    scanLabel: 'Estimating gaze.',
    climaxPre: 'your gaze pattern is predicted from your file.',
    reportTitle: 'gaze reconstruction',
    dwell: '{pct} percent dwell time in stimulus region B.',
    pupil: 'pupillary response inferred. dilated. this data cannot be edited.',
    sliderLabel: 'rate the accuracy of the gaze reconstruction',
    sliderLabelCal: 'rate the accuracy of this calibration',
    submit: 'SUBMIT',
    verdict: {
      calibration: 'calibration complete.',
      establishing: 'gaze logged. {pct} percent region B.',
      deepening: 'you kept looking. {pct} percent region B. logged.',
      climax: 'you looked exactly where your file said you would.',
      recovery: 'calibration complete. you may look anywhere now.',
      _default: 'logged.',
    },
    refusal: 'reconstruction rejected. recorded.',
    recTitle: 'calibration point returns to center. follow it out.',
    recConfirm: 'confirm',
    recVerdict: 'you followed it out.',
  },
};

/* ----------------------------------------------------------------------------
 * OWN CSS literal (id 'ix-gaze-css'), injected once. Chrome owns the card
 * frame/stamp; this adds only the calibration field, the gliding dot / static
 * points, the region-B outline, the status line and the accuracy slider. All
 * classes 'ixgaze-'. Calibration field is a NEUTRAL authored gradient.
 * -------------------------------------------------------------------------- */
const IXGAZE_STYLE_ID = 'ix-gaze-css';
const IXGAZE_CSS = `
.ixgaze-body { display: flex; flex-direction: column; align-items: stretch; gap: 12px; padding: 6px 2px 4px; }
/* the calibration field — a neutral authored gradient (calibration) that later
 * carries a faint user image beneath the dot. */
.ixgaze-field {
  position: relative; width: 100%;
  height: clamp(150px, 42vw, 208px);
  border-radius: 5px; overflow: hidden;
  background:
    radial-gradient(120% 90% at 50% 44%, #f4f6f8 0%, #e7ebef 46%, #d7dde3 100%),
    linear-gradient(180deg, #eef1f4, #dde3e9);
  border: 1px solid #d3d6da;
  box-shadow: inset 0 0 24px rgba(60,70,90,.10);
}
.ixgaze-field-media {
  position: absolute; inset: 0; width: 100%; height: 100%;
  object-fit: cover; display: block; pointer-events: none;
  -webkit-user-drag: none; user-drag: none;
  opacity: 0; transition: opacity 1.6s ease; filter: saturate(1.03);
}
.ixgaze-field-media.ixgaze-mon { opacity: var(--ixgaze-mop, 0.18); }
.ixgaze-field-media.ixgaze-mstatic { transition: none; }
/* faint calibration grid so the field reads as an instrument, not a photo. */
.ixgaze-grid {
  position: absolute; inset: 0; pointer-events: none; z-index: 1; opacity: .5;
  background-image:
    linear-gradient(to right, rgba(90,110,130,.10) 1px, transparent 1px),
    linear-gradient(to bottom, rgba(90,110,130,.10) 1px, transparent 1px);
  background-size: 24px 24px;
}
/* the calibration dot — a reticle: outer ring + bright center. */
.ixgaze-dot {
  position: absolute; left: 0; top: 0; z-index: 4;
  width: 16px; height: 16px; border-radius: 50%;
  transform: translate(-50%, -50%);
  background: radial-gradient(circle at 50% 50%, #1a73e8 0 3px, transparent 4px);
  box-shadow: 0 0 0 2px rgba(26,115,232,.85), 0 0 10px 2px rgba(26,115,232,.45);
  will-change: transform;
}
.ixgaze-dot::after {
  content: ''; position: absolute; inset: -6px; border-radius: 50%;
  border: 1px solid rgba(26,115,232,.5);
  animation: ixgaze-pulse 1.4s ease-out infinite;
}
@keyframes ixgaze-pulse {
  0% { transform: scale(.6); opacity: .8; }
  100% { transform: scale(1.5); opacity: 0; }
}
/* reduced-motion: static points fade in/out in sequence (no glide). */
.ixgaze-point {
  position: absolute; z-index: 4; width: 16px; height: 16px; border-radius: 50%;
  transform: translate(-50%, -50%) scale(.7); opacity: 0;
  background: radial-gradient(circle at 50% 50%, #1a73e8 0 3px, transparent 4px);
  box-shadow: 0 0 0 2px rgba(26,115,232,.85);
  transition: opacity .3s ease, transform .3s ease;
}
.ixgaze-point.ixgaze-pon { opacity: 1; transform: translate(-50%, -50%) scale(1); }
/* region B — the fabricated dwell box, drawn over the "explicit" area. */
.ixgaze-region {
  position: absolute; z-index: 3; box-sizing: border-box;
  border: 2px dashed var(--intake-accent, #d93025);
  border-radius: 3px;
  background: color-mix(in srgb, var(--intake-accent, #d93025) 10%, transparent);
  opacity: 0; transform: scale(1.06);
  transition: opacity .4s ease, transform .4s ease, box-shadow .4s ease;
}
.ixgaze-region.ixgaze-ron {
  opacity: 1; transform: scale(1);
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--intake-accent, #d93025) 40%, transparent),
              0 0 16px color-mix(in srgb, var(--intake-accent, #d93025) 34%, transparent);
}
.ixgaze-region-label {
  position: absolute; top: -10px; left: 6px;
  font: 700 11px/1 'Roboto','Segoe UI',Arial,sans-serif; letter-spacing: .5px;
  color: #fff; background: var(--intake-accent, #d93025);
  padding: 2px 6px; border-radius: 3px;
}
.ixgaze-status {
  min-height: 15px; font-size: 12px; line-height: 1.3; text-align: center;
  color: #5f6368; letter-spacing: .2px;
}
.ixgaze-status.ixgaze-pre { color: var(--intake-accent, #d93025); font-weight: 600; }
/* the fabricated reconstruction report. */
.ixgaze-report { display: none; flex-direction: column; gap: 4px; padding: 2px 2px 0; }
.ixgaze-report.ixgaze-shown { display: flex; }
.ixgaze-report-h {
  font-size: 11px; text-transform: uppercase; letter-spacing: 1px; color: #9aa0a6; font-weight: 700;
}
.ixgaze-report-line { font-size: 12.5px; line-height: 1.35; color: #3c4043; font-family: 'Roboto','Segoe UI',Arial,sans-serif; }
.ixgaze-report-line.ixgaze-dwell { color: #202124; font-weight: 600; }
.ixgaze-report-line.ixgaze-locked { color: #5f6368; font-style: italic; }
/* the decoy accuracy slider (the graded resource). */
.ixgaze-slider { display: none; flex-direction: column; gap: 7px; padding: 6px 2px 0; border-top: 1px solid #eceef0; }
.ixgaze-slider.ixgaze-shown { display: flex; }
.ixgaze-slider-lab { font-size: 12.5px; color: #3c4043; }
.ixgaze-slider-row { display: flex; align-items: center; gap: 10px; }
.ixgaze-range { flex: 1 1 auto; -webkit-appearance: none; appearance: none; height: 5px; border-radius: 3px;
  background: linear-gradient(90deg, #cfd6de, #9fb0c2); outline: none; }
.ixgaze-range::-webkit-slider-thumb { -webkit-appearance: none; appearance: none; width: 18px; height: 18px; border-radius: 50%;
  background: #1a73e8; border: 2px solid #fff; box-shadow: 0 1px 3px rgba(0,0,0,.3); cursor: pointer; }
.ixgaze-range::-moz-range-thumb { width: 18px; height: 18px; border-radius: 50%; background: #1a73e8; border: 2px solid #fff; cursor: pointer; }
.ixgaze-range-val { min-width: 34px; text-align: right; font-size: 13px; font-weight: 700; color: #1a73e8; font-variant-numeric: tabular-nums; }
.ixgaze-verdict { min-height: 26px; display: flex; align-items: center; justify-content: center; }
@media (prefers-reduced-motion: reduce) {
  .ixgaze-field-media, .ixgaze-region, .ixgaze-point { transition: none; }
  .ixgaze-dot::after { animation: none; }
}
`;

function hasDoc() { return typeof document !== 'undefined' && !!document.createElement; }
function ensureCss() {
  if (!hasDoc() || document.getElementById(IXGAZE_STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = IXGAZE_STYLE_ID;
    s.textContent = IXGAZE_CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) {}
}
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixgaze: ' + msg } }));
    }
  } catch (_e) {}
}
function fmt(tpl, vars) {
  return String(tpl == null ? '' : tpl).replace(/\{(\w+)\}/g, (m, k) =>
    (vars && vars[k] != null) ? String(vars[k]) : m);
}
function nowMs() {
  // shimmed performance.now / Date.now freeze during pause; never capture a native clock.
  try { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
  catch (_e) { return Date.now(); }
}
/** cheap deterministic 0..1 from a string (region jitter only; no import needed). */
function seed01(str) {
  let h = 2166136261 >>> 0;
  const s = String(str || '');
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
  return ((h >>> 0) % 100000) / 100000;
}

/* ----------------------------------------------------------------------------
 * RENDER — builds the card into ctx.root and wires a guaranteed single commit.
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
    const isDeepening = band === (Band.Deepening || 'deepening');
    const isCalibration = band === (Band.Calibration || 'calibration');

    const theme = ctx.theme || {};
    const niche = String(ctx.niche || 'bambi').toLowerCase();
    const S = STRINGS[niche] || STRINGS.bambi;
    const subjectNoun = theme.subjectNoun || 'Subject';
    const qIndex = (ctx.meta && typeof ctx.meta.qIndex === 'number') ? ctx.meta.qIndex : 0;

    let scanMs = cfg.scanMs;
    if (ctx.timeoutMs && ctx.timeoutMs > 0 && ctx.timeoutMs < scanMs) scanMs = ctx.timeoutMs;

    // ---- graded option indices from the bank entry's options[] ---------------
    const opts = Array.isArray(ctx.options) ? ctx.options : [];
    const ENDORSED_IDX = 0;
    const PARTIAL_IDX = opts.length > 1 ? 1 : 0;
    const REFUSE_IDX = opts.length > 2 ? 2 : (opts.length ? opts.length - 1 : 0);

    // ---- chrome frame --------------------------------------------------------
    const instruction = isRecovery ? S.recTitle : S.scanTitle;
    let sub = S.scanSub;
    if (isRecovery) sub = '';
    else if (isDeepening) sub = S.deepSub;
    else if (isClimax) sub = S.scanSub;

    const built = chrome.frame({
      instruction, sub, band,
      hatch: 'skip calibration (flagged)',
      verifyLabel: isRecovery ? S.recConfirm : S.submit,
    });
    if (!built || !built.root) return false;
    const card = built.root;
    const body = built.body;
    const hatchLink = built.hatchLink;
    const submitBtn = built.verifyBtn;
    // the submit/confirm button is hidden until the scan settles (or, in recovery,
    // until the point has finished travelling out).
    if (submitBtn) { try { submitBtn.style.display = 'none'; } catch (_e) {} }

    // ---- body scaffold -------------------------------------------------------
    const wrap = document.createElement('div');
    wrap.className = 'ixgaze-body';

    const field = document.createElement('div');
    field.className = 'ixgaze-field';

    // faint user-image backdrop (Establishing+). Calibration/Recovery keep the
    // authored gradient. reduced -> a single static image (no fade).
    let mediaImg = null;
    if (cfg.media) {
      const src = pickMediaSrc(ctx.media);
      if (src) {
        mediaImg = document.createElement('img');
        mediaImg.className = 'ixgaze-field-media' + (reduced ? ' ixgaze-mstatic' : '');
        mediaImg.alt = ''; mediaImg.draggable = false;
        mediaImg.style.setProperty('--ixgaze-mop', String(cfg.opacity));
        try { mediaImg.src = src; } catch (_e) {}
        field.appendChild(mediaImg);
      }
    }

    const grid = document.createElement('div');
    grid.className = 'ixgaze-grid';
    field.appendChild(grid);

    // region B outline (fabricated). Placed toward center-lower, small jitter.
    let region = null;
    if (cfg.region) {
      region = document.createElement('div');
      region.className = 'ixgaze-region';
      const j = seed01((ctx.prompt && ctx.prompt.id) || '' + '|' + qIndex);
      const rx = 28 + (j * 10);          // 28..38 %
      const ry = 40 + ((1 - j) * 8);     // 40..48 %
      region.style.left = rx.toFixed(1) + '%';
      region.style.top = ry.toFixed(1) + '%';
      region.style.width = '40%';
      region.style.height = '42%';
      const rlab = document.createElement('div');
      rlab.className = 'ixgaze-region-label';
      rlab.textContent = 'B';
      region.appendChild(rlab);
      field.appendChild(region);
    }

    // the gliding dot (non-reduced) OR the static point set (reduced).
    let dot = null;
    let points = [];
    if (reduced) {
      for (let i = 0; i < WAYPOINTS; i++) {
        const p = document.createElement('div');
        p.className = 'ixgaze-point';
        points.push(p);
        field.appendChild(p);
      }
    } else {
      dot = document.createElement('div');
      dot.className = 'ixgaze-dot';
      field.appendChild(dot);
    }

    wrap.appendChild(field);

    const status = document.createElement('div');
    status.className = 'ixgaze-status';
    status.textContent = isRecovery ? '' : S.scanLabel;
    wrap.appendChild(status);

    // the fabricated report (hidden until settle).
    const report = document.createElement('div');
    report.className = 'ixgaze-report';
    const repH = document.createElement('div');
    repH.className = 'ixgaze-report-h';
    repH.textContent = S.reportTitle;
    report.appendChild(repH);
    const dwellLine = document.createElement('div');
    dwellLine.className = 'ixgaze-report-line ixgaze-dwell';
    report.appendChild(dwellLine);
    const pupilLine = document.createElement('div');
    pupilLine.className = 'ixgaze-report-line ixgaze-locked';
    report.appendChild(pupilLine);
    wrap.appendChild(report);

    // the decoy accuracy slider (the graded resource; hidden until settle).
    const sliderBox = document.createElement('div');
    sliderBox.className = 'ixgaze-slider';
    const sliderLab = document.createElement('div');
    sliderLab.className = 'ixgaze-slider-lab';
    sliderLab.textContent = isCalibration ? S.sliderLabelCal : S.sliderLabel;
    sliderBox.appendChild(sliderLab);
    const sliderRow = document.createElement('div');
    sliderRow.className = 'ixgaze-slider-row';
    const range = document.createElement('input');
    range.type = 'range'; range.min = '0'; range.max = '100'; range.step = '1';
    range.value = String(SLIDER_DEFAULT);
    range.className = 'ixgaze-range';
    const rangeVal = document.createElement('div');
    rangeVal.className = 'ixgaze-range-val';
    rangeVal.textContent = String(SLIDER_DEFAULT);
    sliderRow.appendChild(range);
    sliderRow.appendChild(rangeVal);
    sliderBox.appendChild(sliderRow);
    wrap.appendChild(sliderBox);

    const verdictSlot = document.createElement('div');
    verdictSlot.className = 'ixgaze-verdict';
    wrap.appendChild(verdictSlot);

    if (body) body.appendChild(wrap);
    try { ctx.root.appendChild(card); } catch (_e) { return false; }

    // ============================ STATE ==============================
    let committed = false;
    let scanDone = false;
    let rafId = 0;
    let lastBucket = -1;
    let fieldW = 0, fieldH = 0;
    const mountAt = nowMs();
    let startTs = 0;

    // ---- cleanup -------------------------------------------------------------
    let backstopTimer = 0, timeoutTimer = 0, recTimer = 0, mediaTimer = 0;
    ctx.onCleanup(() => {
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      if (backstopTimer) { try { clearTimeout(backstopTimer); } catch (_e) {} }
      if (timeoutTimer) { try { clearTimeout(timeoutTimer); } catch (_e) {} }
      if (recTimer) { try { clearTimeout(recTimer); } catch (_e) {} }
      if (mediaTimer) { try { clearTimeout(mediaTimer); } catch (_e) {} }
    });

    function sfx(id, intensity) { try { if (typeof ctx.sfx === 'function') ctx.sfx(id, intensity); } catch (_e) {} }
    function voice(id) { try { if (typeof ctx.voice === 'function') ctx.voice(id); } catch (_e) {} }

    // ---- commit paths (exactly one lands the beat) ---------------------------
    function landIndex(idx, o) {
      try {
        if (opts.length && typeof ctx.submitIndex === 'function') ctx.submitIndex(idx, o);
        else if (typeof ctx.submitValue === 'function') ctx.submitValue(true, o);
      } catch (e) { ilog('landIndex failed: ' + (e && e.message)); }
    }
    function stopClocks() {
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      if (backstopTimer) { try { clearTimeout(backstopTimer); } catch (_e) {} backstopTimer = 0; }
      if (timeoutTimer) { try { clearTimeout(timeoutTimer); } catch (_e) {} timeoutTimer = 0; }
      if (recTimer) { try { clearTimeout(recTimer); } catch (_e) {} recTimer = 0; }
    }
    function showStamp(text, tone) {
      try {
        if (chrome.stamp) { const st = chrome.stamp(text, tone); if (st) verdictSlot.appendChild(st); }
        else verdictSlot.textContent = text;
      } catch (_e) {}
    }
    // the graded commit: derive an option index from the accuracy slider.
    function commitSlider() {
      if (committed) return;
      committed = true; stopClocks();
      let v = SLIDER_DEFAULT;
      try { v = Math.max(0, Math.min(100, parseInt(range.value, 10) || 0)); } catch (_e) {}
      try { range.disabled = true; } catch (_e) {}
      if (submitBtn) { try { submitBtn.disabled = true; } catch (_e) {} }

      const pct = cfg.dwell ? String(Math.round(cfg.dwell)) : '';
      let idx, tone, text;
      if (v >= ENDORSE_AT) {
        idx = ENDORSED_IDX; tone = 'ok';
        const vt = (S.verdict[band] != null) ? S.verdict[band] : S.verdict._default;
        text = fmt(vt, { pct });
        sfx('captcha-verify-ok', 0.45);
      } else if (v <= 0) {
        // a 0 rating IS the graded refusal — respected, logged deadpan.
        idx = REFUSE_IDX; tone = 'logged'; text = S.refusal;
        sfx('captcha-logged', 0.4);
      } else {
        idx = PARTIAL_IDX; tone = 'logged';
        text = fmt(S.verdict._default, { pct });
        sfx('captcha-logged', 0.4);
      }
      showStamp(text, tone);
      setTimeout(() => landIndex(idx), reduced ? 0 : RESOLVE_HOLD_MS);
    }
    // the un-vetoable refusal (skip-calibration hatch), graded as options[REFUSE].
    function commitFlag() {
      if (committed) return;
      committed = true; stopClocks();
      try { range.disabled = true; } catch (_e) {}
      showStamp('flagged', 'flag');
      try {
        if (opts.length && typeof ctx.forceComplete === 'function') ctx.forceComplete(REFUSE_IDX);
        else if (typeof ctx.forceComplete === 'function') ctx.forceComplete();
        else landIndex(REFUSE_IDX);
      } catch (e) { ilog('commitFlag failed: ' + (e && e.message)); landIndex(REFUSE_IDX); }
    }
    // host timeout (renderer owns the clock; scores 0 / refusal-shaped).
    function commitTimeout() {
      if (committed) return;
      committed = true; stopClocks();
      try { if (typeof ctx.submitTimeout === 'function') ctx.submitTimeout(); else landIndex(REFUSE_IDX); }
      catch (e) { ilog('commitTimeout failed: ' + (e && e.message)); landIndex(REFUSE_IDX); }
    }
    // recovery: honest + calming, single confirm.
    function commitRecovery() {
      if (committed) return;
      committed = true; stopClocks();
      if (submitBtn) { try { submitBtn.disabled = true; } catch (_e) {} }
      showStamp(S.recVerdict, 'ok');
      sfx('verify-resolve', 0.35);
      setTimeout(() => landIndex(ENDORSED_IDX), reduced ? 0 : RESOLVE_HOLD_MS);
    }

    // ---- wire the always-present hatch (every band) --------------------------
    if (hatchLink) {
      try { hatchLink.addEventListener('click', () => commitFlag()); } catch (_e) {}
    }
    // slider live readout (synthetic clicks pass — no veto handlers anywhere here).
    try { range.addEventListener('input', () => { try { rangeVal.textContent = String(range.value); } catch (_e) {} }); } catch (_e) {}

    // honor a host timeout as an overall cap.
    if (ctx.timeoutMs && ctx.timeoutMs > 0) {
      timeoutTimer = setTimeout(commitTimeout, ctx.timeoutMs);
    }

    // speak the prompt (VO = bank prompt.text; missing clip is silent).
    try { ctx.speakPrompt && ctx.speakPrompt(); } catch (_e) {}

    // ---- region reveal (with the climax pre-emptive branch) ------------------
    function revealRegion() {
      if (!region) return;
      try { region.classList.add('ixgaze-ron'); } catch (_e) {}
      sfx('gaze-lock', 0.3);
    }

    // ============================ RUN ================================
    if (isRecovery) {
      runRecovery();
    } else {
      // Climax detonation: draw the red region BEFORE the image loads under it.
      if (isClimax && cfg.pre) {
        try { status.textContent = S.climaxPre; status.classList.add('ixgaze-pre'); } catch (_e) {}
        revealRegion();
        voice('gaze_pre');
        // the user image fades in BENEATH the pre-drawn box, partway through.
        if (mediaImg && !reduced) {
          mediaTimer = setTimeout(() => { try { mediaImg.classList.add('ixgaze-mon'); } catch (_e) {} }, Math.round(scanMs * 0.4));
        } else if (mediaImg) {
          try { mediaImg.classList.add('ixgaze-mon'); } catch (_e) {}
        }
      } else if (mediaImg) {
        // Establishing/Deepening: the faint backdrop is present from the start.
        try { mediaImg.classList.add('ixgaze-mon'); } catch (_e) {}
      }
      runScan();
    }

    return true;

    // ------------------------------------------------------------------ scan --
    function measureField() {
      try {
        const w = field.clientWidth, h = field.clientHeight;
        if (w > 0 && h > 0) { fieldW = w; fieldH = h; }
      } catch (_e) {}
    }
    function spiralXY(t /* 0..1 */) {
      // outward-growing spiral: radius grows with t, angle sweeps SPIRAL_TURNS.
      const cx = fieldW / 2, cy = fieldH / 2;
      const rMax = Math.min(fieldW, fieldH) * 0.42;
      const r = t * rMax;
      const a = t * SPIRAL_TURNS * Math.PI * 2;
      return { x: cx + Math.cos(a) * r, y: cy + Math.sin(a) * r };
    }
    function placePoints() {
      // static reduced-motion points sampled along the same spiral.
      if (!points.length) return;
      for (let i = 0; i < points.length; i++) {
        const t = (i + 0.5) / points.length;
        const p = spiralXY(t);
        try { points[i].style.left = p.x + 'px'; points[i].style.top = p.y + 'px'; } catch (_e) {}
      }
    }
    function onBucket(bucket) {
      // fire the calibration heartbeat + advance the reduced-motion point set.
      sfx('verify-tick', 0.1);
      if (reduced && points.length) {
        for (let i = 0; i < points.length; i++) points[i].classList.toggle('ixgaze-pon', i === bucket);
      }
    }
    function settle() {
      if (scanDone || committed) return;
      scanDone = true;
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      try { if (dot) dot.style.opacity = '0'; } catch (_e) {}
      if (reduced && points.length) { for (const p of points) { try { p.classList.remove('ixgaze-pon'); } catch (_e) {} } }
      sfx('verify-resolve', 0.35);
      // reveal the fabricated report (non-climax reveals the region now).
      if (region && !region.classList.contains('ixgaze-ron')) revealRegion();
      try {
        if (cfg.dwell) {
          const pct = String(Math.round(cfg.dwell + (seed01(String(qIndex)) * 4 - 2)));
          dwellLine.textContent = fmt(S.dwell, { pct });
          voice('gaze_dwell');
        } else {
          dwellLine.textContent = '';
          dwellLine.classList.remove('ixgaze-dwell');
        }
        pupilLine.textContent = cfg.pupil ? S.pupil : '';
        status.textContent = '';
        status.classList.remove('ixgaze-pre');
        report.classList.add('ixgaze-shown');
        sliderBox.classList.add('ixgaze-shown');
      } catch (_e) {}
      // now show the graded submit button + wire it.
      if (submitBtn) {
        try {
          submitBtn.style.display = '';
          submitBtn.textContent = S.submit;
          submitBtn.addEventListener('click', () => commitSlider());
        } catch (_e) {}
      }
      // report-phase backstop: land the beat even if the host stalls.
      backstopTimer = setTimeout(() => { if (!committed) commitSlider(); }, REPORT_BACKSTOP_MS);
    }
    function runScan() {
      measureField();
      placePoints();
      function frame() {
        rafId = 0;
        if (committed || scanDone) return;
        const t = nowMs();
        if (!startTs) startTs = t;
        if (!fieldW || !fieldH) { measureField(); placePoints(); }
        let prog = (t - startTs) / scanMs;
        if (prog < 0) prog = 0;
        if (prog > 1) prog = 1;
        // move the dot (non-reduced) or advance buckets (reduced).
        if (!reduced && dot && fieldW && fieldH) {
          const p = spiralXY(prog);
          try { dot.style.transform = 'translate(' + p.x.toFixed(1) + 'px,' + p.y.toFixed(1) + 'px) translate(-50%,-50%)'; } catch (_e) {}
        }
        const bucket = Math.min(WAYPOINTS - 1, Math.floor(prog * WAYPOINTS));
        if (bucket !== lastBucket) { lastBucket = bucket; onBucket(bucket); }
        if (prog >= 1) { settle(); return; }
        try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }
      }
      try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }
      // guaranteed settle backstop even if rAF never fires in this host.
      backstopTimer = setTimeout(() => { if (!scanDone && !committed) settle(); }, scanMs + 2500);
    }

    // -------------------------------------------------------------- recovery --
    function runRecovery() {
      // honest + calming: the point returns to center, then spirals OUTWARD,
      // decelerating; one confirm click (or the backstop) follows it out.
      measureField();
      try { status.textContent = ''; } catch (_e) {}
      if (reduced) {
        // a single centered point, then a gentle confirm.
        if (dot && fieldW && fieldH) {
          try { dot.style.transform = 'translate(' + (fieldW / 2).toFixed(1) + 'px,' + (fieldH / 2).toFixed(1) + 'px) translate(-50%,-50%)'; } catch (_e) {}
        }
        showConfirm();
        recTimer = setTimeout(() => { if (!committed) commitRecovery(); }, RECOVERY_MAX_MS);
        return;
      }
      function frame() {
        rafId = 0;
        if (committed) return;
        const t = nowMs();
        if (!startTs) startTs = t;
        if (!fieldW || !fieldH) measureField();
        let prog = (t - startTs) / cfg.scanMs;
        if (prog < 0) prog = 0;
        if (prog > 1) prog = 1;
        const eased = 1 - Math.pow(1 - prog, 2.4);   // decelerate outward
        if (dot && fieldW && fieldH) {
          const p = spiralXY(eased);
          try { dot.style.transform = 'translate(' + p.x.toFixed(1) + 'px,' + p.y.toFixed(1) + 'px) translate(-50%,-50%)'; } catch (_e) {}
        }
        if (prog >= 1) { showConfirm(); return; }
        try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }
      }
      try { rafId = requestAnimationFrame(frame); } catch (_e) { rafId = 0; }
      // backstop: recovery always terminates (invariant #1/#3), even without a click.
      recTimer = setTimeout(() => { if (!committed) commitRecovery(); }, RECOVERY_MAX_MS);
    }
    function showConfirm() {
      if (rafId) { try { cancelAnimationFrame(rafId); } catch (_e) {} rafId = 0; }
      if (submitBtn) {
        try {
          submitBtn.style.display = '';
          submitBtn.textContent = S.recConfirm;
          submitBtn.addEventListener('click', () => commitRecovery());
        } catch (_e) {}
      }
    }
  } catch (e) {
    ilog('render threw (falling back): ' + (e && e.message));
    return false;   // never wedge — beats.js degrades to plain rendering
  }
}

/* ----------------------------------------------------------------------------
 * pickMediaSrc(media) — a single faint backdrop source: prefer a still image,
 * fall back to at most ONE gif (per the concurrent-gif constraint), else null
 * (the authored gradient field stands in). No real filenames are ever shown.
 * -------------------------------------------------------------------------- */
function pickMediaSrc(media) {
  try {
    const images = (media && Array.isArray(media.images)) ? media.images.filter((u) => typeof u === 'string' && u) : [];
    if (images.length) return images[0];
    const gifs = (media && Array.isArray(media.gifs)) ? media.gifs.filter((u) => typeof u === 'string' && u) : [];
    if (gifs.length) return gifs[0];
  } catch (_e) {}
  return null;
}

export default { render };
