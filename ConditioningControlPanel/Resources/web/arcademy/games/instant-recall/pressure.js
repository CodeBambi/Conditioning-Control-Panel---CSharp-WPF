/* ============================================================================
 * games/instant-recall/pressure.js - DECK IV of the House Rules for the vigil:
 * THE SURGE. The casino lights the hall, the trickster lies about the slip;
 * this file is what the ROOM does to you as the vigil climbs.
 *
 * THE POOL IS CORE'S, AND THIS DECK MAY NOT TOUCH IT (retuned 2026-08-23, the
 * mosaic rework). The class now asks "which CCP effect just fired?" off a pool
 * of ten - Flash image, Subliminal, Whisper, Corner GIF, Fullscreen GIF,
 * Cascade, Bubbles, Spiral, Pink Filter, Brain Drain - and every one of them is
 * a ledger entry. A pink wash breathed by THIS file would be a second honest
 * answer to a question whose key came from the ledger, so the old BREATH /
 * BURST / SURGE rungs (wash:pink, gif_burst, flash_burst) are GONE. What is
 * left is weather the quiz never names: the scanline, the motes, the HUD
 * glitch, and this deck's own CSS tremor.
 *
 *   THE LADDER   the surge = 0.6 * progress + 0.4 * min(streak,4)/4 - rising
 *                with the class and with the player's own run. Rung by rung
 *                (cumulative; a lower rung is stepped DOWN behind a hysteresis,
 *                never snapped):
 *     rung 0  clean hall
 *     rung 1  CRT         a scanline whisper over the hall             [engine crt]
 *     rung 2  MOTES       ambient dust in the beam                     [engine ambient_field]
 *     rung 3  TREMOR      the chrome (HUD chips, proctor line) carries a
 *                         CSS tremor whose amplitude rides heat        [CSS only]
 *     rung 4  GLITCH      the HUD glitches (vhsroll) on stops and layout
 *                         beats; crt turns chroma            [engine glitch_swap, crt]
 *     rung 5  GALE        the tremor peaks (TREMOR_PX[5]) and the chroma
 *                         is repainted - no new primitive, no new node [CSS only]
 *   THE PUNCH    every verdict punches the chrome (extend-not-stack: a CSS
 *                class with a bigger amplitude for 300ms); reduced motion /
 *                motionLevel 0 turn every punch into a bloom (a transition).
 *
 * ENGINE vs GAME-LOCAL (audit): everything the engine can do goes through
 * opts.engine (CORE's weld: clickSafe forced, AUDIO_CEIL, capsOk). Game-local
 * is ONLY the tremor / punch / bloom: classes + two custom props on the chrome
 * nodes CORE hands us - no nodes of our own, no per-frame JS (the tremor is a
 * CSS steps() keyframe; the PERF LAW's 8Hz ceiling is respected by having no
 * JS paint at all).
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads nothing, writes nothing about the ledger, the
 *       stops, the streak or the grade; the chips' TEXT is never touched; and
 *       it NEVER fires a POOL primitive (flash_burst / sub_flash /
 *       bubble_field / wash / audio_trigger stings / gif_burst / gif_rain),
 *       because the quiz's answer key comes from CORE's ledger alone.
 *   II  input honest  - the tremor is applied to chrome only (never the slip,
 *       never an option, never a tile); every engine fire is clickSafe.
 *   III never still   - the breath and the tremor ride the rung.
 *   V   seeded        - per-tag mulberry32 off seed+'|ir-pressure|' (append-
 *       only tags); the engine's own rng is separate and fine.
 *   VI  exits sacred  - capsOk() false disarms everything (no fires, no
 *       classes); reduced motion / motionLevel 0 = tremor 0, punches bloom;
 *       every timer lives in the game's pause-aware registry AND a local set.
 *   VII strings      - this file renders no text at all.
 *   PERF LAW         - zero per-frame JS; zero touches on .g-ir-tile or the
 *       montage container; CSS animations on chrome only.
 *
 * NEVER: engine.stop('wash') - it blacks out EVERY wash kind at once, and CORE
 * pulses three of them as POOL effects. This deck no longer touches wash at all
 * (the guard in stopKind stays, because a future rung must not be able to).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const IR_PRESSURE = Object.freeze({
  /** surge (0..1) thresholds for rungs 1..5 (rung 0 below the first). */
  RUNG_AT: Object.freeze([0.18, 0.36, 0.54, 0.72, 0.88]),
  RUNG_MAX: 5,
  SURGE_FROM_PROGRESS: 0.6,
  SURGE_FROM_STREAK: 0.4,
  STREAK_CAP: 4,
  /** The ladder: what each rung ADDS (feature keys, cumulative), the cue it
   *  announces itself with (one per rung CHANGE, never per frame). */
  LADDER: Object.freeze([
    Object.freeze({ rung: 0, adds: Object.freeze([]), cue: null }),
    Object.freeze({ rung: 1, adds: Object.freeze(['crt']), cue: 'whisper' }),
    Object.freeze({ rung: 2, adds: Object.freeze(['motes']), cue: 'wash' }),
    Object.freeze({ rung: 3, adds: Object.freeze(['tremor']), cue: 'burst' }),
    Object.freeze({ rung: 4, adds: Object.freeze(['glitch', 'chroma']), cue: 'glitch' }),
    /* rung 5 adds no PRIMITIVE at all: the gale is TREMOR_PX[5] plus a chroma
     * repaint. Everything louder than that belongs to the pool, and the pool
     * belongs to CORE. */
    Object.freeze({ rung: 5, adds: Object.freeze(['gale']), cue: 'near_miss' }),
  ]),
  /** Stepping DOWN waits this long (a dip in the surge never snaps the storm). */
  HYST_MS: 1600,
  /** Forever-ish things are repainted when heat moved at least this much. */
  HEAT_REPAINT_STEP: 0.08,
  /* ---- the rungs' knobs ------------------------------------------------ */
  MOTES_DENSITY: Object.freeze([0.25, 0.7]),
  GLITCH_S: 0.55,
  GLITCH_CHANCE: Object.freeze([0.5, 1]),
  /* ---- the tremor (CSS on chrome) ------------------------------------- */
  TREMOR_PX: Object.freeze([0, 0, 0, 0.6, 1.0, 1.6]),      // by rung, at heat 1
  TREMOR_FLOOR: 0.5,                                        // heat 0 still shivers at half
  TREMOR_CAP_PX: 2,
  TREMOR_T_MS: Object.freeze([140, 90]),                    // steps() period by heat
  PUNCH_PX: Object.freeze({ correct: 2.2, wrong: 3.2, timeout: 1.8, stop: 2.6, layout: 1.2, peak: 1.4 }),
  PUNCH_MS: 300,
  BLOOM_MS: 320,
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  /** cue level = min(AUDIO_CEIL[gradeTier], base + rung * step). */
  CUE_LEVEL_BASE: 0.22,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.32, 2: 0.38, 3: 0.44, 4: 0.5 }),
});

const STYLE_ID = 'g-ir-pressure-style';
const STYLE_TEXT = `
/* THE TREMOR: chrome only. A steps() keyframe on translate, amplitude from
   --ir-p-ta (px), period from --ir-p-tt. The punch is the same keyframe at a
   bigger amplitude for a beat. Never on the slip, an option or a tile. */
.g-ir-p-tremor{animation:g-ir-p-tremor var(--ir-p-tt,.12s) steps(2,jump-none) infinite}
@keyframes g-ir-p-tremor{0%{translate:calc(var(--ir-p-ta,0px) * -1) calc(var(--ir-p-ta,0px) * .6)}
  33%{translate:var(--ir-p-ta,0px) calc(var(--ir-p-ta,0px) * -.4)}
  66%{translate:calc(var(--ir-p-ta,0px) * .5) var(--ir-p-ta,0px)}
  100%{translate:calc(var(--ir-p-ta,0px) * -.7) calc(var(--ir-p-ta,0px) * -.8)}}
.g-ir-p-punch{animation:g-ir-p-punch .3s steps(3,jump-none) 1}
@keyframes g-ir-p-punch{0%{translate:calc(var(--ir-p-pa,2px) * -1) calc(var(--ir-p-pa,2px) * .5)}
  40%{translate:var(--ir-p-pa,2px) calc(var(--ir-p-pa,2px) * -.6)}
  75%{translate:calc(var(--ir-p-pa,2px) * -.4) var(--ir-p-pa,2px)}100%{translate:0 0}}
/* THE BLOOM: reduced motion's whole punch - a box-shadow transition. */
.g-ir-p-bloom{box-shadow:0 0 0 2px rgba(255,105,180,.55), 0 0 22px rgba(255,105,180,.6) !important;transition:box-shadow .3s ease}
@media (prefers-reduced-motion: reduce){
  .g-ir-p-tremor,.g-ir-p-punch{animation:none !important;translate:0 0}
}
html.arc-reduced .g-ir-p-tremor,html.arc-reduced .g-ir-p-punch{animation:none !important;translate:0 0}
`;

function ensureStyle() {
  try {
    if (typeof document === 'undefined' || !document.head) return;
    if (document.getElementById(STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = STYLE_TEXT;
    document.head.appendChild(s);
  } catch (e) { /* cosmetic */ }
}

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }
/** progress + streak -> surge 0..1. Pure. */
export function surgeFor(progress01, streak) {
  const P = IR_PRESSURE;
  const s = Math.max(0, Math.min(P.STREAK_CAP, Number(streak) || 0)) / P.STREAK_CAP;
  return clamp01(P.SURGE_FROM_PROGRESS * clamp01(progress01) + P.SURGE_FROM_STREAK * s);
}
/** surge -> rung 0..5. Pure, monotone. */
export function rungFor(surge01) {
  const s = clamp01(surge01);
  let r = 0;
  for (const at of IR_PRESSURE.RUNG_AT) { if (s >= at) r += 1; else break; }
  return Math.min(IR_PRESSURE.RUNG_MAX, r);
}
/** The idle tremor amplitude in px for a rung at a heat. 0 under reduced motion. Pure. */
export function tremorAmpPx(rung, heat01, reducedMotion) {
  if (reducedMotion) return 0;
  const base = IR_PRESSURE.TREMOR_PX[Math.max(0, Math.min(IR_PRESSURE.RUNG_MAX, rung | 0))] || 0;
  if (base <= 0) return 0;
  const f = IR_PRESSURE.TREMOR_FLOOR;
  return Math.min(IR_PRESSURE.TREMOR_CAP_PX, +(base * (f + (1 - f) * clamp01(heat01))).toFixed(3));
}

function setCls(n, cls, on) { try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } }
function setVar(n, k, v) { try { if (n && n.style) n.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function delVar(n, k) { try { if (n && n.style && n.style.removeProperty) n.style.removeProperty(k); } catch (e) { /* noop */ } }
function restart(n, cls) {
  if (!n || !n.classList) return;
  try { n.classList.remove(cls); if (typeof n.offsetWidth === 'number') void n.offsetWidth; n.classList.add(cls); } catch (e) { /* noop */ }
}

/**
 * @param {Object} o
 *   seed, gradeTier, reduced, motionLevel, stage, montage (untouched), chrome:[els], hud,
 *   engine {fire, sustain, stop, channels}, assets (unused), timers {after,every,clear},
 *   capsOk (fn | bool), log
 */
export function createIrPressure(o) {
  const opts = o || {};
  const P = IR_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const eng = opts.engine || {};
  const armedBase = !!opts.stage && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk === true;
  }
  let destroyed = false;
  /* THE STAGE WATCH. CORE sends beat('hit'|'miss') and the montage's own effect
   * kinds, but not 'stop' or 'layout' - those are READ off the stage's
   * data-phase / data-layout attributes (one MutationObserver on one node, no
   * polling). An explicit beat of the same kind within DEDUPE_MS is folded. */
  let stageWatch = null;
  let lastPhase = null;
  let lastLayout = null;
  const lastBeatAt = { stop: -1e9, layout: -1e9 };
  const DEDUPE_MS = 320;
  function nowMs() {
    try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
    return Date.now();
  }
  function watchStage() {
    if (stageWatch || typeof MutationObserver === 'undefined' || !opts.stage) return;
    try {
      lastPhase = opts.stage.getAttribute ? opts.stage.getAttribute('data-phase') : null;
      lastLayout = opts.stage.getAttribute ? opts.stage.getAttribute('data-layout') : null;
      stageWatch = new MutationObserver(() => {
        if (destroyed || stopped) return;
        let ph = null; let ly = null;
        try { ph = opts.stage.getAttribute('data-phase'); ly = opts.stage.getAttribute('data-layout'); } catch (e) { return; }
        if (ph !== lastPhase) { lastPhase = ph; if (ph === 'stop') api.beat('stop'); }
        if (ly !== lastLayout) { const was = lastLayout; lastLayout = ly; if (was != null && ly) api.beat('layout'); }
      });
      stageWatch.observe(opts.stage, { attributes: true, attributeFilter: ['data-phase', 'data-layout'] });
    } catch (e) { stageWatch = null; }
  }
  function unwatchStage() {
    if (!stageWatch) return;
    try { stageWatch.disconnect(); } catch (e) { /* noop */ }
    stageWatch = null;
  }
  const armed = () => armedBase && !destroyed && capsOk();
  /* the chrome: what we may tremble. Never the montage, never the slip. */
  const chrome = [];
  try {
    for (const n of (Array.isArray(opts.chrome) ? opts.chrome : [])) if (n && n.classList) chrome.push(n);
    const hud = opts.hud || {};
    for (const k of ['clock', 'stops', 'density']) if (hud[k] && hud[k].classList && chrome.indexOf(hud[k]) < 0) chrome.push(hud[k]);
  } catch (e) { /* none */ }

  /* ---- timers: the game's pause-aware registry + a local set ------------- */
  const live = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  function after(ms, fn) {
    if (!armedBase || destroyed) return 0;
    let id = 0;
    id = opts.timers.after(ms, () => { live.delete(id); if (!destroyed) { try { fn(); } catch (e) { /* ignore */ } } });
    if (id) live.add(id);
    return id;
  }
  function cancel(id) {
    if (!id) return;
    live.delete(id);
    try { if (typeof cancelFn === 'function') cancelFn.call(opts.timers, id); } catch (e) { /* ignore */ }
  }
  /* ---- seeded streams (Law V; append-only tags) --------------------------- */
  const seedBase = String(opts.seed || 'ir') + '|ir-pressure|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };
  /* ---- the engine, counted ------------------------------------------------ */
  const fires = {};
  function count(key) { fires[key] = (fires[key] || 0) + 1; }
  function fire(kind, o2) {
    if (!armed()) return null;
    const v = o2 && o2.variant ? kind + ':' + o2.variant : kind;
    count(kind); if (v !== kind) count(v);
    if (typeof eng.fire !== 'function') return null;
    try { return eng.fire(kind, o2 || {}) || null; } catch (e) { return null; }
  }
  function sustain(kind, o2) {
    if (!armed()) return null;
    const v = o2 && o2.variant ? kind + ':' + o2.variant : kind;
    count(kind); if (v !== kind) count(v);
    if (typeof eng.sustain !== 'function') return null;
    try { return eng.sustain(kind, o2 || {}) || null; } catch (e) { return null; }
  }
  function stopKind(kind) {
    if (!armedBase || destroyed || kind === 'wash') return;     // NEVER stop('wash')
    count('stop:' + kind);
    if (typeof eng.stop !== 'function') return;
    try { eng.stop(kind); } catch (e) { /* ignore */ }
  }
  /* THE DECOUPLE (W2 sec 3, owner ruling). The rung ladder IS the class's own
   * streak, so its cue is not decoration: bgIntensity 0 takes the EFFECTS away
   * (fire()/sustain() stay behind armed()) and never the school's voice. The
   * count is kept identical to fire()'s so diagnostics do not move. */
  const sounds = () => armedBase && !destroyed && !stopped;
  function cue(name, rung) {
    if (!name || !sounds()) return;
    count('audio_trigger');
    if (typeof eng.fire !== 'function') return;
    try {
      eng.fire('audio_trigger', { name, level: Math.min(audioCeil, P.CUE_LEVEL_BASE + P.CUE_LEVEL_STEP * rung) });
    } catch (e) { /* a refused cue is not an error */ }
  }

  /* ---- state ------------------------------------------------------------ */
  let started = false;
  let stopped = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let progress = 0;
  let streak = 0;
  let rung = 0;
  let hystTimer = 0;
  let hystTarget = -1;
  let outOn = false;
  const present = new Set();
  let punchTimer = 0;
  let bloomTimer = 0;
  let tremorAmp = 0;
  let beats = 0;

  /* ------------------------------------------------------------- chrome */
  function paintTremor() {
    tremorAmp = (stopped || outOn || !armed() || !present.has('tremor')) ? 0
      : tremorAmpPx(rung, heat, still) * P.MOTION_MUL[motion];
    const on = tremorAmp > 0.05;
    const tt = (lerp(P.TREMOR_T_MS, heat) | 0) + 'ms';
    for (const n of chrome) {
      if (on) { setVar(n, '--ir-p-ta', tremorAmp.toFixed(2) + 'px'); setVar(n, '--ir-p-tt', tt); setCls(n, 'g-ir-p-tremor', true); }
      else { setCls(n, 'g-ir-p-tremor', false); delVar(n, '--ir-p-ta'); }
    }
  }
  let punches = 0;
  function punch(kind) {
    if (!armed() || stopped || !chrome.length) return;
    punches += 1;
    if (still) {
      for (const n of chrome) restart(n, 'g-ir-p-bloom');
      cancel(bloomTimer);
      bloomTimer = after(P.BLOOM_MS, () => { bloomTimer = 0; for (const n of chrome) setCls(n, 'g-ir-p-bloom', false); });
      return;
    }
    const px = Math.min(4, (P.PUNCH_PX[kind] || 1.5) * P.MOTION_MUL[motion] * (0.6 + 0.4 * heat));
    for (const n of chrome) { setVar(n, '--ir-p-pa', px.toFixed(2) + 'px'); restart(n, 'g-ir-p-punch'); }
    cancel(punchTimer);
    punchTimer = after(P.PUNCH_MS + 20, () => { punchTimer = 0; for (const n of chrome) setCls(n, 'g-ir-p-punch', false); });
  }
  function clearChrome() {
    for (const n of chrome) {
      setCls(n, 'g-ir-p-tremor', false); setCls(n, 'g-ir-p-punch', false); setCls(n, 'g-ir-p-bloom', false);
      delVar(n, '--ir-p-ta'); delVar(n, '--ir-p-tt'); delVar(n, '--ir-p-pa');
    }
  }

  /* ------------------------------------------------------------ the rungs */
  const has = (k) => present.has(k);
  function motes() {
    sustain('ambient_field', { kind: 'motes', density: lerp(P.MOTES_DENSITY, heat) });
  }
  function crtOn(variant, restartIt) {
    const o2 = { variant };
    if (restartIt) o2.restart = true;
    return sustain('crt', o2);
  }
  function hudGlitch() {
    if (still || !opts.hud) return null;
    const targets = [];
    try {
      const hud = opts.hud;
      if (hud && hud.classList) targets.push(hud);
      else for (const k of ['clock', 'stops', 'density']) if (hud[k]) targets.push(hud[k]);
    } catch (e) { /* none */ }
    if (!targets.length) return null;
    return fire('glitch_swap', { targets, variant: 'vhsroll', seconds: P.GLITCH_S, onSwap() {}, sfx: false });
  }
  const FEATURES = {
    motes: { on() { motes(); }, off() { stopKind('ambient_field'); } },
    crt: { on() { crtOn('scanline', false); }, off() { stopKind('crt'); } },
    tremor: { on() { paintTremor(); }, off() { paintTremor(); } },
    glitch: { on() { hudGlitch(); }, off() {} },
    chroma: { on() { crtOn('chroma', true); }, off(dest) { if (dest >= 2) crtOn('scanline', true); } },
    /* THE GALE: no primitive at all. paintTremor() reads the rung, so entering
     * and leaving rung 5 is the whole feature. */
    gale: { on() { paintTremor(); }, off() { paintTremor(); } },
  };
  function enterRung(k) {
    const row = P.LADDER[k];
    if (!row) return;
    for (const key of row.adds) {
      if (present.has(key)) continue;
      present.add(key);
      try { FEATURES[key].on(k); } catch (e) { say('pressure: ' + key + '.on threw: ' + ((e && e.message) || e)); }
    }
  }
  function leaveRung(k, dest) {
    const row = P.LADDER[k];
    if (!row) return;
    for (let i = row.adds.length - 1; i >= 0; i--) {
      const key = row.adds[i];
      if (!present.has(key)) continue;
      present.delete(key);
      try { FEATURES[key].off(dest); } catch (e) { say('pressure: ' + key + '.off threw: ' + ((e && e.message) || e)); }
    }
  }
  function climb(r) {
    if (r <= rung) return;
    const from = rung;
    for (let k = from + 1; k <= r; k++) enterRung(k);
    rung = r;
    cue(P.LADDER[r] && P.LADDER[r].cue, r);
    lastPaintHeat = heat;
    paintTremor();
    say('pressure: rung ' + from + ' -> ' + r + ' on: ' + Array.from(present).join(','));
  }
  function descend(r) {
    if (r >= rung) return;
    const from = rung;
    for (let k = from; k > r; k--) leaveRung(k, r);
    rung = r;
    paintTremor();
    say('pressure: rung ' + from + ' -> ' + r + ' (fade)');
  }
  function cancelHyst() { if (hystTimer) { cancel(hystTimer); hystTimer = 0; } hystTarget = -1; }
  function stepTo(r, immediate) {
    if (!started || stopped) return;
    if (r > rung) { cancelHyst(); climb(r); return; }
    if (r < rung) {
      if (immediate) { cancelHyst(); descend(r); return; }
      if (hystTimer && hystTarget === r) return;
      cancelHyst();
      hystTarget = r;
      hystTimer = after(P.HYST_MS, () => { hystTimer = 0; const tgt = hystTarget; hystTarget = -1; if (tgt >= 0) descend(tgt); });
      return;
    }
    cancelHyst();
  }
  function retune() {
    paintTremor();
    if (Math.abs(heat - lastPaintHeat) >= P.HEAT_REPAINT_STEP) {
      if (has('motes')) motes();
      lastPaintHeat = heat;
    }
  }
  function reconsider() {
    if (!started || stopped) return;
    stepTo(rungFor(surgeFor(progress, streak)), false);
  }
  function everythingOff() {
    cancelHyst();
    descend(0);
    if (punchTimer) { cancel(punchTimer); punchTimer = 0; }
    if (bloomTimer) { cancel(bloomTimer); bloomTimer = 0; }
    clearChrome();
  }

  /* ---------------------------------------------------------------- api */
  const api = {
    start() {
      if (!armed()) { say('pressure: disarmed'); return; }
      if (started) return;
      started = true;
      ensureStyle();
      paintTremor();
      watchStage();
      say('pressure: armed (' + chrome.length + ' chrome nodes)');
    },
    /** Magnitude rides heat. */
    setHeat(h) {
      heat = clamp01(h);
      if (!started) return;
      retune();
    },
    /** Presence rides the surge: progress through the class... */
    setProgress(p) {
      progress = clamp01(p);
      reconsider();
    },
    /** ...and the player's own run. */
    setStreak(n) {
      streak = Math.max(0, Number(n) || 0);
      reconsider();
    },
    /** A beat: 'stop' | 'answer' | 'correct' | 'wrong' | 'timeout' | 'layout' | 'peak' | 'plant'. */
    beat(kind) {
      if (!started || stopped || !armed()) return;
      beats += 1;
      let k = String(kind || '');
      if (k === 'hit') k = 'correct';                     // CORE's beat names
      else if (k === 'miss') k = 'wrong';
      if (k === 'stop' || k === 'layout') {
        const now = nowMs();
        if (now - lastBeatAt[k] < DEDUPE_MS) return;      // the stage watch + an explicit beat = one beat
        lastBeatAt[k] = now;
      }
      if (k === 'correct') {
        punch('correct');
      } else if (k === 'wrong' || k === 'timeout') {
        punch(k);
      } else if (k === 'stop') {
        punch('stop');
        if (has('glitch') && roll('glitch') < lerp(P.GLITCH_CHANCE, heat)) hudGlitch();
      } else if (k === 'layout') {
        punch('layout');
        if (has('glitch')) hudGlitch();
      } else if (k === 'peak') {
        punch('peak');
      }
    },
    /** The last stretch: nothing new climbs past where it is. */
    bell(on) { void on; },
    /** The class ended: everything sighs out. */
    dimOut() {
      outOn = true;
      if (!started) return;
      everythingOff();
    },
    pause() { /* CSS tremor freezes with the stage's .suspended rule; timers are the game's */ },
    resume() { /* nothing to rebuild */ },
    /** The class is over: fade everything, never snap; nothing may punch again. */
    stop() {
      if (stopped) return;
      stopped = true;
      unwatchStage();
      if (started) everythingOff();
    },
    destroy() {
      if (destroyed) return;
      api.stop();
      destroyed = true;
      unwatchStage();
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      clearChrome();
      present.clear();
    },
    diagnostics() {
      return {
        armed: armed(), started, stopped, out: outOn, rung, hystPending: !!hystTimer, hystTarget,
        heat, progress, streak, surge: +surgeFor(progress, streak).toFixed(3),
        features: Array.from(present), tremorPx: +tremorAmp.toFixed(3), chrome: chrome.length,
        beats, punches, fires: Object.assign({}, fires), timers: live.size, stageWatch: !!stageWatch,
      };
    },
  };
  return api;
}

export default createIrPressure;
