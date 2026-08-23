/* ============================================================================
 * games/anomaly/pressure.js - DECK IV of the House Rules for the darkroom: THE
 * SURGE. The casino lights the room, the trickster lies about it; this file is
 * what the room DOES to you as your STREAK climbs. The rung is your current
 * run of first-tap finds; a wrong tap drops it and the storm steps DOWN behind
 * a hysteresis (fades, never a snap). Magnitude rides the class heat (CORE
 * already folds streak + tier + progress into it). Same seed, same surge.
 *
 * THE RUNG TABLE (streak -> rung -> what switches ON; cumulative). Scaled to a
 * 90s class of ~10 rounds: the whole ladder is reachable by a clean run, the
 * top of it only by a perfect one.
 *   0-1  rung 0  clean darkroom (the safelight and CORE's own class dials)
 *   2    rung 1  BREATH     a pink wash breathes on a cadence (a FLARE over   [engine wash:pink, holdMs]
 *                           whatever CORE holds - never sustainForever here,
 *                           so it never ends CORE's own hold: trap 33)
 *   3    rung 2  BURST      gif bursts ride finds, clickSafe                  [engine gif_burst]
 *   4    rung 3  WHEEL      the spiral wash wakes at a low alpha,             [engine wash:spiral, forever]
 *                + TREMOR   the chrome starts to shiver (HUD chips, the
 *                           backdrop - NEVER the grid: the delta is the truth)
 *   5    rung 4  VEIL+SUBS  the chrome glitch-shudders on finds,              [engine glitch_swap on chrome,
 *                           the sub flash stream starts                        sub_flash alias]
 *   6    rung 5  DECOYS     a bubble field of decoys drifts over the room     [engine bubble_field clickSafe,
 *                + FLASHES  and flash bursts ride finds                         flash_burst]
 *   8    rung 6  STORM      the pink flood, the swarm, flashes doubled,       [engine wash:pink hi, bubble_field
 *                           the tremor at its cap, a whisper of row_drift       swarm, row_drift on the strips]
 *                           on the backdrop's hanging strips
 *
 * THE TREMOR: written on the CSS individual `translate` of the CHROME elements
 * CORE hands in (opts.chrome: HUD chips, the message line, the backdrop) -
 * and NEVER on the grid, the tiles or the faces. The grid is the truth delta
 * and it must stay readable; this file cannot even reach it (it is not handed
 * the grid, on purpose - a deck that is not given a node cannot shake it). A
 * rAF loop ONLY while amplitude > 0, halted by pause()/stop()/destroy().
 * Reduced motion / motionLevel 0 -> no tremor; punches become a bloom class.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads streak/heat/beat kinds, writes nothing about
 *       rounds, streak, time or grade, renders no text.
 *   II  input honest  - no node of ours exists; the grid is never transformed,
 *       never covered by anything of ours; every engine fire is clickSafe
 *       (CORE's weld forces it too).
 *   III never still   - from rung 1 the room breathes; from rung 3 it shivers.
 *   V   seeded        - per-tag mulberry32 off seed+'|an-pressure|<tag>',
 *       append-only; no Math.random.
 *   VI  exits sacred  - capsOk false (bgIntensity 0) = zero fires, zero tremor;
 *       pause() halts the rAF; destroy() restores every translate and whispers
 *       the washes out.
 *   VII lexicon       - this file renders no text.
 *
 * THE WASH TRAP (trap 33): engine.stop('wash') is NEVER called here - not even
 * in destroy(). A wash is stepped DOWN by re-triggering its variant at a
 * whisper alpha with a short hold (the spiral, which only this deck holds),
 * or simply not re-armed (the pink breath, which is a hold-bounded flare over
 * CORE's own pink, if any).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const AN_PRESSURE = Object.freeze({
  /** streak thresholds per rung (index = rung). Monotone. */
  RUNG_STREAK: Object.freeze([0, 2, 3, 4, 5, 6, 8]),
  RUNG_ADDS: Object.freeze([
    Object.freeze([]),
    Object.freeze(['breath']),
    Object.freeze(['burst']),
    Object.freeze(['wheel', 'tremor']),
    Object.freeze(['veil', 'subs']),
    Object.freeze(['decoys', 'flashes']),
    Object.freeze(['storm']),
  ]),
  RUNG_CUE: Object.freeze([null, 'wash', 'burst', 'wash', 'glitch', 'burst', 'near_miss']),
  /** Stepping DOWN waits this long and fades; stepping UP is immediate. */
  HYST_MS: 1600,
  HEAT_REPAINT_STEP: 0.06,

  /* ---- THE TREMOR (chrome only) -------------------------------------- */
  TREMOR_PX: Object.freeze([0, 0, 0, 0.5, 0.8, 1.2, 1.8]),
  TREMOR_CAP_PX: 2.2,
  TREMOR_HEAT_FLOOR: 0.5,
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.4, 2.6]),
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  PUNCH_PX: Object.freeze([0.8, 3]),
  PUNCH_MS: Object.freeze([160, 340]),
  PUNCH_MILESTONE_PX: 4,
  PUNCH_MILESTONE_MS: 400,
  PUNCH_MISS_PX: 3.2,
  PUNCH_MISS_MS: 300,
  PUNCH_CAP_PX: 5,
  PUNCH_CAP_MS: 450,
  MILESTONES: Object.freeze([3, 5, 8]),
  BLOOM_MS: 320,

  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([7500, 4500]),
  BREATH_HOLD_MS: 2200,
  BREATH_ALPHA: Object.freeze([0.1, 0.34]),
  STORM_BREATH_ALPHA: Object.freeze([0.3, 0.55]),
  BURST_CHANCE: Object.freeze([0.45, 0.9]),
  BURST_COUNT: Object.freeze([1, 3]),
  BURST_HOLD_MS: Object.freeze([700, 1200]),
  BURST_VMIN: Object.freeze([0.22, 0.4]),
  WHEEL_ALPHA: Object.freeze([0.16, 0.42]),
  VEIL_CHANCE: Object.freeze([0.35, 0.8]),
  VEIL_S: Object.freeze([0.3, 0.8]),
  VEIL_VARIANTS: Object.freeze(['rgbsplit', 'vhsroll', 'datamosh']),
  DECOY_MAX: Object.freeze([5, 10]),
  DECOY_ALPHA: Object.freeze([0.25, 0.5]),
  STORM_DECOY_MAX: 16,
  FLASH_COUNT: 2,
  STORM_FLASH_COUNT: 4,
  WHISPER_ALPHA: 0.01,
  WHISPER_HOLD_MS: 900,
  /** cue level = min(AUDIO_CEIL[tier], base + rung * step). */
  CUE_LEVEL_BASE: 0.22,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
});

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }

/** streak -> rung (0..6). Pure, monotone, clamps garbage to 0. */
export function rungFor(streak) {
  const s = Math.max(0, Math.floor(Number(streak) || 0));
  const T = AN_PRESSURE.RUNG_STREAK;
  let r = 0;
  for (let i = 0; i < T.length; i++) { if (s >= T[i]) r = i; else break; }
  return r;
}
/** idle tremor amplitude in px; 0 under reduced motion / motionLevel 0. Pure. */
export function tremorAmpPx(streak, heat01, reducedMotion, motionLevel) {
  if (reducedMotion) return 0;
  const m = AN_PRESSURE.MOTION_MUL[Math.max(0, Math.min(2, motionLevel == null ? 2 : Math.round(Number(motionLevel) || 0)))];
  if (!m) return 0;
  const base = AN_PRESSURE.TREMOR_PX[rungFor(streak)] || 0;
  if (base <= 0) return 0;
  const f = AN_PRESSURE.TREMOR_HEAT_FLOOR;
  return Math.min(AN_PRESSURE.TREMOR_CAP_PX, +(base * m * (f + (1 - f) * clamp01(heat01))).toFixed(3));
}
/** a find punch {px, ms} from the streak's q and whether it was a milestone / a miss. Pure. */
export function punchFor(o) {
  const d = o || {};
  const q = clamp01((Number(d.streak) || 0) / 8);
  const P = AN_PRESSURE;
  let px = lerp(P.PUNCH_PX, q);
  let ms = lerp(P.PUNCH_MS, q);
  if (d.milestone) { px = Math.max(px, P.PUNCH_MILESTONE_PX); ms = Math.max(ms, P.PUNCH_MILESTONE_MS); }
  if (d.miss) { px = Math.max(px, P.PUNCH_MISS_PX); ms = Math.max(ms, P.PUNCH_MISS_MS); }
  return { px: +Math.min(P.PUNCH_CAP_PX, px).toFixed(2), ms: Math.min(P.PUNCH_CAP_MS, Math.round(ms)) };
}

function nowMs() {
  try { if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
  return Date.now();
}
function rafFn() {
  try { if (typeof requestAnimationFrame === 'function') return requestAnimationFrame; } catch (e) { /* fall */ }
  try { if (typeof globalThis !== 'undefined' && typeof globalThis.requestAnimationFrame === 'function') return globalThis.requestAnimationFrame; } catch (e) { /* none */ }
  return null;
}
function cafFn() {
  try { if (typeof cancelAnimationFrame === 'function') return cancelAnimationFrame; } catch (e) { /* fall */ }
  try { if (typeof globalThis !== 'undefined' && typeof globalThis.cancelAnimationFrame === 'function') return globalThis.cancelAnimationFrame; } catch (e) { /* none */ }
  return null;
}
function vmin() {
  try {
    if (typeof window !== 'undefined' && window) {
      const w = Number(window.innerWidth) || 0, h = Number(window.innerHeight) || 0;
      if (w > 0 && h > 0) return Math.min(w, h);
    }
  } catch (e) { /* headless */ }
  return 720;
}
function setTranslate(node, x, y) {
  try { if (node && node.style) node.style.translate = (x || y) ? (x.toFixed(2) + 'px ' + y.toFixed(2) + 'px') : ''; } catch (e) { /* noop */ }
}
function setCls(n, cls, on) { try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } }
/** The grid and its children are off-limits, whatever CORE hands in. */
function isGridish(node) {
  try {
    if (!node || !node.classList) return false;
    if (node.classList.contains('g-an-grid') || node.classList.contains('g-an-tile') || node.classList.contains('g-an-face')) return true;
    if (typeof node.closest === 'function' && node.closest('.g-an-grid')) return true;
    if (node.querySelector && node.querySelector('.g-an-grid')) return true;
  } catch (e) { /* fall */ }
  return false;
}

/**
 * @param {Object} o
 *   engine {fire,sustain,stop,channels,audio?}, stage, chrome:[els] (HUD chips,
 *   msg, backdrop - the things allowed to tremble), timers {after,every,clear},
 *   reduced, capsOk (bool or fn), motionLevel, seed?, gradeTier?, backdrop?,
 *   strips? (row_drift targets), log
 */
export function createAnPressure(o) {
  const opts = o || {};
  const P = AN_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const eng = opts.engine || {};
  /* the chrome: whatever CORE allows to tremble, MINUS anything grid-shaped */
  const chrome = (Array.isArray(opts.chrome) ? opts.chrome : []).filter((n) => n && n.style && !isGridish(n));
  const refused = (Array.isArray(opts.chrome) ? opts.chrome : []).length - chrome.length;
  const armedBase = !!opts.stage && !!opts.timers && typeof opts.timers.after === 'function';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false && opts.capsOk != null;
  }
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();

  /* ---- timers ------------------------------------------------------------- */
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
  function cancelAll() { for (const id of Array.from(live)) cancel(id); }

  /* ---- seeded streams ----------------------------------------------------- */
  const seedBase = String(opts.seed || 'an') + '|an-pressure|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };
  const tremorPhase = { fx: roll('tremor') * 6.28, fy: roll('tremor') * 6.28, hzF: lerp(P.TREMOR_HZ_FAST, roll('tremor')), hzS: lerp(P.TREMOR_HZ_SLOW, roll('tremor')) };

  /* ---- the engine, counted ------------------------------------------------ */
  const fires = {};
  function count(key) { fires[key] = (fires[key] || 0) + 1; }
  function fire(kind, o2) {
    if (!armed()) return null;
    count(kind); if (o2 && o2.variant) count(kind + ':' + o2.variant);
    try { return typeof eng.fire === 'function' ? eng.fire(kind, o2 || {}) : null; } catch (e) { return null; }
  }
  function sustain(kind, o2) {
    if (!armed()) return null;
    count(kind); if (o2 && o2.variant) count(kind + ':' + o2.variant);
    try { return typeof eng.sustain === 'function' ? eng.sustain(kind, o2 || {}) : null; } catch (e) { return null; }
  }
  function stopKind(kind) {
    if (!armedBase || kind === 'wash') return;          // NEVER stop('wash') - trap 33
    count('stop:' + kind);
    try { if (typeof eng.stop === 'function') eng.stop(kind); } catch (e) { /* noop */ }
  }
  function cue(name, rung) {
    if (!armed() || !name) return;
    const level = Math.min(audioCeil, P.CUE_LEVEL_BASE + rung * P.CUE_LEVEL_STEP);
    count('cue');
    try {
      if (typeof eng.audio === 'function') eng.audio(name, level, { pitch: +(0.9 + rung * 0.04).toFixed(3) });
      else if (typeof eng.fire === 'function') eng.fire('audio_trigger', { name, level, pitch: +(0.9 + rung * 0.04).toFixed(3) });
    } catch (e) { /* noop */ }
  }

  /* ---- state -------------------------------------------------------------- */
  let started = false;
  let stopped = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let streak = 0;
  let rung = 0;              // the SHOWN rung
  let wantRung = 0;          // what the streak says
  let downTimer = 0;
  let breathTimer = 0;
  const on = new Set();
  let spiralUrl = null;
  const bloomTimers = new Map();

  /* ---- the tremor --------------------------------------------------------- */
  let rafId = 0;
  let loopOn = false;
  let amp = 0;
  let punch = { px: 0, until: 0, ms: 1 };
  let paused = false;
  let translateWrites = 0;

  function writeAll(x, y) {
    for (let i = 0; i < chrome.length; i++) {
      /* the HUD shivers full, everything else (backdrop, msg) at 60% */
      const k = i === 0 ? 1 : 0.6;
      setTranslate(chrome[i], x * k, y * k);
    }
    translateWrites++;
  }
  function loop() {
    rafId = 0;
    if (!loopOn || paused || destroyed) return;
    const tNow = nowMs();
    let extra = 0;
    if (punch.px > 0) {
      const left = punch.until - tNow;
      if (left <= 0) punch = { px: 0, until: 0, ms: 1 };
      else { const k = left / punch.ms; extra = punch.px * k * k; }   // quadratic ring-down
    }
    const a = armed() ? amp + extra : 0;
    if (a <= 0.005) { writeAll(0, 0); loopOn = false; return; }
    const ts = tNow / 1000;
    const x = a * (0.7 * Math.sin(ts * tremorPhase.hzF * 6.28 + tremorPhase.fx) + 0.3 * Math.sin(ts * tremorPhase.hzS * 6.28));
    const y = a * (0.7 * Math.cos(ts * tremorPhase.hzF * 5.9 + tremorPhase.fy) + 0.3 * Math.sin(ts * tremorPhase.hzS * 5.1 + 1.7));
    writeAll(x, y);
    const raf = rafFn();
    if (raf) rafId = raf(loop);
  }
  function armLoop() {
    if (loopOn || paused || destroyed || still || !chrome.length) return;
    if (amp <= 0 && punch.px <= 0) return;
    loopOn = true;
    const raf = rafFn();
    if (raf) rafId = raf(loop); else loop();
  }
  function haltLoop() {
    loopOn = false;
    const caf = cafFn();
    if (rafId && caf) { try { caf(rafId); } catch (e) { /* noop */ } }
    rafId = 0;
  }
  function retune() {
    amp = 0;
    if (!still && armed()) {
      const base = P.TREMOR_PX[rung] || 0;
      amp = base <= 0 ? 0 : Math.min(P.TREMOR_CAP_PX, base * P.MOTION_MUL[motion] * (P.TREMOR_HEAT_FLOOR + (1 - P.TREMOR_HEAT_FLOOR) * heat));
    }
    armLoop();
  }
  function bloom() {
    /* reduced motion's whole punch: a box-shadow class on the first chrome node */
    const n = chrome[0];
    if (!n) return;
    setCls(n, 'g-an-p-bloom', false);
    try { if (typeof n.offsetWidth === 'number') void n.offsetWidth; } catch (e) { /* noop */ }
    setCls(n, 'g-an-p-bloom', true);
    const old = bloomTimers.get(n);
    if (old) cancel(old);
    bloomTimers.set(n, after(P.BLOOM_MS, () => { bloomTimers.delete(n); setCls(n, 'g-an-p-bloom', false); }));
  }
  function doPunch(spec) {
    if (!armed() || stopped || !spec || spec.px <= 0) return;
    if (still) { bloom(); return; }
    const tNow = nowMs();
    punch = { px: Math.max(punch.px, spec.px), until: Math.max(punch.until, tNow + spec.ms), ms: Math.max(spec.ms, 1) };
    count('punch');
    armLoop();
  }

  /* ---- the rungs ---------------------------------------------------------- */
  function breathe(storm) {
    /* a hold-bounded trigger: a FLARE over whatever pink CORE may hold, never
       a forever hold of our own (so stepping off never ends CORE's) */
    sustain('wash', { variant: 'pink', alpha: lerp(storm ? P.STORM_BREATH_ALPHA : P.BREATH_ALPHA, heat), holdMs: P.BREATH_HOLD_MS });
  }
  function armBreath() {
    if (breathTimer) cancel(breathTimer);
    const ms = lerp(P.BREATH_MS, heat) * (0.85 + roll('breath') * 0.3);
    breathTimer = after(Math.round(ms), () => { breathTimer = 0; if (on.has('breath') && !stopped) { breathe(on.has('storm')); armBreath(); } });
  }
  function wheelOn() {
    const o2 = { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true };
    if (spiralUrl) o2.url = spiralUrl;
    sustain('wash', o2);
  }
  function whisperOut(variant) {
    const o2 = { variant, alpha: P.WHISPER_ALPHA, holdMs: P.WHISPER_HOLD_MS };
    if (variant === 'spiral' && spiralUrl) o2.url = spiralUrl;
    sustain('wash', o2);
  }
  function decoysOn(storm) {
    sustain('bubble_field', {
      max: storm ? P.STORM_DECOY_MAX : Math.round(lerp(P.DECOY_MAX, heat)),
      alpha: lerp(P.DECOY_ALPHA, heat), clickSafe: true, media: true,
      variant: storm ? 'swarm' : 'drift',
    });
  }
  /** row_drift targets for the storm: CORE's opts.strips, else the casino's
   *  hanging strips found under the stage - never anything grid-shaped. */
  function stripTargets() {
    let list = Array.isArray(opts.strips) ? opts.strips.slice() : [];
    if (!list.length) {
      try {
        const host = opts.backdrop || opts.stage;
        if (host && host.querySelectorAll) list = Array.from(host.querySelectorAll('.g-an-bd-strips'));
      } catch (e) { list = []; }
    }
    return list.filter((n) => n && n.style && !isGridish(n));
  }
  function veil(seconds, variant) {
    if (still || !chrome.length) return null;
    const targets = chrome.filter((n) => n && !isGridish(n));
    if (!targets.length) return null;
    return fire('glitch_swap', { targets, variant, seconds, sfx: false, onSwap() { /* presentation only */ } });
  }
  function applyAdd(add, goingUp) {
    if (goingUp) {
      on.add(add);
      if (add === 'breath') { breathe(false); armBreath(); }
      else if (add === 'wheel') wheelOn();
      else if (add === 'subs') sustain('sub_flash', {});
      else if (add === 'decoys') decoysOn(false);
      else if (add === 'storm') {
        decoysOn(true);
        breathe(true);
        const strips = stripTargets();
        if (strips.length && !still) sustain('row_drift', { targets: strips, axis: 'y', amplitudeMult: 0.5, speedMult: 0.6, variant: 'sway' });
      }
      /* burst / tremor / veil / flashes are event-driven: nothing to switch on */
    } else {
      on.delete(add);
      if (add === 'breath') { if (breathTimer) { cancel(breathTimer); breathTimer = 0; } }   // the flare fades on its own hold
      else if (add === 'wheel') whisperOut('spiral');
      else if (add === 'subs') stopKind('sub_flash');
      else if (add === 'decoys') stopKind('bubble_field');
      else if (add === 'storm') { stopKind('row_drift'); if (on.has('decoys')) decoysOn(false); }
    }
  }
  function setRung(r) {
    if (r === rung) return;
    const up = r > rung;
    if (up) {
      for (let k = rung + 1; k <= r; k++) for (const add of P.RUNG_ADDS[k]) applyAdd(add, true);
      cue(P.RUNG_CUE[r], r);
    } else {
      for (let k = rung; k > r; k--) for (const add of P.RUNG_ADDS[k]) applyAdd(add, false);
    }
    rung = r;
    retune();
    say('pressure: rung ' + rung + ' (' + (up ? 'up' : 'down') + ', streak ' + streak + ') on: ' + Array.from(on).join(','));
  }
  function setStreak(s) {
    streak = Math.max(0, Math.floor(Number(s) || 0));
    wantRung = rungFor(streak);
    if (!started || stopped || !armed()) return;
    if (wantRung > rung) { cancel(downTimer); downTimer = 0; setRung(wantRung); }
    else if (wantRung < rung && !downTimer) {
      downTimer = after(P.HYST_MS, () => { downTimer = 0; if (wantRung < rung) setRung(wantRung); });
    } else if (wantRung === rung) { cancel(downTimer); downTimer = 0; }
  }
  function repaintHeat() {
    if (Math.abs(heat - lastPaintHeat) < P.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    if (on.has('wheel')) wheelOn();
    if (on.has('decoys') && !on.has('storm')) decoysOn(false);
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      stopped = false;
      lastPaintHeat = -1;
      if (!armed()) { say('pressure: disarmed'); return; }
      try { if (typeof opts.spiralUrl === 'function') spiralUrl = opts.spiralUrl() || null; else if (typeof opts.spiralUrl === 'string') spiralUrl = opts.spiralUrl; } catch (e) { spiralUrl = null; }
      say('pressure: armed, chrome ' + chrome.length + (refused ? ' (' + refused + ' grid-shaped node(s) REFUSED)' : ''));
      if (wantRung > 0) setRung(wantRung);
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started && !stopped) { repaintHeat(); retune(); }
    },
    /** The class's own ladder. Up is immediate, down waits HYST_MS. */
    setStreak(n) { setStreak(n); },
    /** CORE's beats: 'find' (a first-tap find), 'miss' (a wrong tap),
     *  'round' (a new sheet), 'relocate' (the anomaly moved), 'bell'. */
    beat(kind, info) {
      if (!started || stopped || !armed()) return;
      const e = info || {};
      const k = String(kind || '');
      if (k === 'find') {
        const s = e.streak != null ? Number(e.streak) : streak;
        const milestone = P.MILESTONES.indexOf(s) >= 0;
        doPunch(punchFor({ streak: s, milestone }));
        if (on.has('burst') && roll('burst') < lerp(P.BURST_CHANCE, heat)) {
          fire('gif_burst', {
            count: Math.round(lerp(P.BURST_COUNT, heat)), clickSafe: true, clickable: false,
            sizePx: Math.round(vmin() * lerp(P.BURST_VMIN, heat)),
            holdMs: Math.round(lerp(P.BURST_HOLD_MS, heat)),
            variant: on.has('storm') ? 'scatter' : undefined,
          });
        }
        if (on.has('veil') && roll('veil') < lerp(P.VEIL_CHANCE, heat)) {
          veil(lerp(P.VEIL_S, heat), P.VEIL_VARIANTS[Math.floor(roll('veil') * P.VEIL_VARIANTS.length)]);
        }
        if (on.has('flashes')) fire('flash_burst', { count: on.has('storm') ? P.STORM_FLASH_COUNT : P.FLASH_COUNT, clickSafe: true, clickable: false });
      } else if (k === 'miss') {
        doPunch(punchFor({ streak: 0, miss: true }));
        if (on.has('veil')) veil(0.45, 'datamosh');
      } else if (k === 'relocate') {
        if (on.has('veil')) veil(0.5, 'rgbsplit');
        if (on.has('burst') && roll('burst') < 0.5) fire('gif_burst', { count: 1, clickSafe: true, clickable: false, holdMs: 600 });
      } else if (k === 'round') {
        if (on.has('flashes') && roll('round') < 0.4) fire('flash_burst', { count: 1, clickSafe: true, clickable: false });
      } else if (k === 'bell') {
        if (on.has('decoys')) decoysOn(true);
      }
    },
    stop() {
      if (stopped) return;
      stopped = true;
      cancel(downTimer); downTimer = 0;
      if (breathTimer) { cancel(breathTimer); breathTimer = 0; }
      if (on.has('wheel')) whisperOut('spiral');
      if (on.has('subs')) stopKind('sub_flash');
      if (on.has('decoys') || on.has('storm')) stopKind('bubble_field');
      if (on.has('storm')) stopKind('row_drift');
      on.clear();
      rung = 0;
      amp = 0; punch = { px: 0, until: 0, ms: 1 };
      haltLoop();
      writeAll(0, 0);
    },
    pause() {
      paused = true;
      haltLoop();
      writeAll(0, 0);
    },
    resume() {
      paused = false;
      if (!stopped) { retune(); setStreak(streak); if (on.has('breath') && !breathTimer) armBreath(); }
    },
    destroy() {
      api.stop();
      destroyed = true;
      cancelAll();
      haltLoop();
      for (const n of chrome) { setTranslate(n, 0, 0); setCls(n, 'g-an-p-bloom', false); }
      bloomTimers.clear();
    },
    diagnostics() {
      return {
        armed: armed(), started, stopped, paused, heat: +heat.toFixed(3), streak, rung, wantRung,
        on: Array.from(on), tremorPx: +amp.toFixed(3), punchPx: +punch.px.toFixed(2), loopOn,
        chrome: chrome.length, chromeRefused: refused, translateWrites,
        fires: Object.assign({}, fires), liveTimers: live.size, nodes: 0, spiralUrl,
      };
    },
  };
  return api;
}

export default createAnPressure;
