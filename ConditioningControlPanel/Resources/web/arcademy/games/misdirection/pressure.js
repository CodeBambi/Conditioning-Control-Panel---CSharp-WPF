/* ============================================================================
 * games/misdirection/pressure.js - the shell game's effects ladder: THE SURGE.
 * The casino (casino.js) lights the table, the trickster (trickster.js) lies
 * about it; this file is what the room DOES to you as your PICK STREAK climbs.
 * The rung is your current run of correct picks; a miss drops it to zero and
 * the storm steps DOWN behind a hysteresis (fades, never a snap). Magnitude
 * rides the class heat (index.js folds streak + ride + progress into it, capped
 * by the grade tier). Same seed, same surge.
 *
 * THE RUNG TABLE (pick streak -> rung -> what switches ON; cumulative). A
 * 120s class deals ~10-14 rounds, so the whole ladder fits a perfect class:
 *   0-1   rung 0  clean table (the casino's seeded felt is all there is)
 *   2     rung 1  BREATH      a lavender wash breathes on a cadence     [engine wash:sublim]
 *   3     rung 2  MOTES       dust drifts over the felt                 [engine ambient_field:motes]
 *   4     rung 3  BURST+TREMOR gif bursts ride the verdicts; the chrome  [engine gif_burst]
 *                             (HUD, arch, backdrop - never the arc) hums
 *   5     rung 4  WHEEL       the spiral wash wakes behind the table    [engine wash:spiral]
 *   6     rung 5  VEIL        the backdrop glitch-shudders on verdicts  [engine glitch_swap on the backdrop]
 *   8     rung 6  SUBS        the sub flash stream                      [engine sub_flash alias]
 *   10    rung 7  FLASHES     flash bursts ride the verdicts            [engine flash_burst clickSafe]
 *   12    rung 8  STORM       embers, the lavender flood, the tremor cap [engine ambient_field:embers, wash:sublim]
 *
 * WHICH WASH: index.js owns the PINK wash (the dossier's occlusion washes and
 * the blackout beats ride it, and a re-trigger at a lower alpha would cut a
 * blackout short - trap 33's step-down branch). This deck therefore breathes
 * on the SUBLIM variant (its own element) and wakes the SPIRAL; it never
 * touches pink. bubble_field is index.js's too (the dossier's bubble decoys):
 * not used here.
 *
 * WHEN THE RIDERS FIRE: ONLY on verdict beats (pick / bank / bust / ride) -
 * never during the shuffle. The trackability invariant is index.js's law
 * (occlusion covers at most one link of a swap chain) and this deck must never
 * be the thing that hides the swap you needed to see: the sustains are the
 * engine's screen-blended washes and fields, and every one-shot waits for the
 * verdict. beat('shuffle') only retunes.
 *
 * THE DUSK: ONE game-local node (.g-md-p-dusk, appended to the backdrop, under
 * the table) whose opacity climbs with the rung, so the engine's washes and
 * gifs read IN FRONT of the felt instead of drowned by it (the IC lesson, trap
 * 35: it was alpha, never z-order). It dims the BACKDROP only - the shells,
 * the arc and the HUD are never under it. THE FLARE snaps it up under a burst.
 *
 * THE TREMOR: the chrome vibrates with the streak - a continuous low tremor
 * whose amplitude grows with the rung, a PUNCH on every verdict (extend-not-
 * stack, quadratic ring-down, screenShake posture), heavier on a bank, a jolt
 * on a bust. Written on the CSS individual `translate` of the elements index.js
 * hands us as `chrome` (HUD, backdrop, marquee host) - and NEVER on the arc or
 * a shell: those are the pick targets (Law II) and a tracking test cannot have
 * a trembling target. A rAF loop ONLY while amplitude > 0; stopped by pause()/
 * stop()/destroy(), re-armed by resume(). Reduced motion / motionLevel 0 -> no
 * tremor; the punch becomes a brief bloom class on the chrome.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads streak / beat kinds; writes nothing about pot,
 *       streak, round or grade; renders no text.
 *   II  input honest  - the arc and the shells are never transformed, never
 *       covered; every engine fire is clickSafe; the dusk is under the table.
 *   III never still   - from rung 1 the room breathes; from rung 3 it hums.
 *   V   seeded        - per-tag mulberry32 off seed+'|md-pressure|<tag>',
 *       append-only; no Math.random.
 *   VI  exits sacred  - capsOk false (bgIntensity 0) = zero fires, zero tremor;
 *       reduced motion = no tremor, no shudder; pause() halts the rAF and drops
 *       transient timers; destroy() restores every translate and stops every
 *       sustain with a fade.
 *   VII lexicon       - this file renders no text.
 *
 * THE WASH TRAP (trap 33): engine.stop('wash') kills EVERY wash variant,
 * including anything index.js holds. Two live here (sublim + spiral), so a rung
 * is stepped DOWN by re-triggering its variant at a whisper alpha with a short
 * hold (it fades on its own deadline). stop('wash') is NEVER called here, not
 * even from destroy().
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const MD_PRESSURE = Object.freeze({
  /** pick streak thresholds per rung (index = rung). Monotone. */
  RUNG_STREAK: Object.freeze([0, 2, 3, 4, 5, 6, 8, 10, 12]),
  RUNG_ADDS: Object.freeze([
    Object.freeze([]),
    Object.freeze(['breath']),
    Object.freeze(['motes']),
    Object.freeze(['burst', 'tremor']),
    Object.freeze(['wheel']),
    Object.freeze(['veil']),
    Object.freeze(['subs']),
    Object.freeze(['flashes']),
    Object.freeze(['storm']),
  ]),
  RUNG_CUE: Object.freeze([null, 'wash', 'whisper', 'burst', 'wash', 'glitch', 'whisper', 'burst', 'near_miss']),
  /** the dusk's opacity per rung (x (DUSK_HEAT_FLOOR + (1-floor)*heat)); 0 at rest. */
  DUSK: Object.freeze([0, 0.1, 0.18, 0.28, 0.36, 0.42, 0.48, 0.54, 0.6]),
  DUSK_HEAT_FLOOR: 0.75,
  DUSK_END: 0.26,
  FLARE_BURST: 0.78,
  FLARE_IN_MS: 160,
  /** Stepping DOWN waits this long and fades; stepping UP is immediate. */
  HYST_MS: 1600,
  HEAT_REPAINT_STEP: 0.06,

  /* ---- THE TREMOR ------------------------------------------------------ */
  TREMOR_PX: Object.freeze([0, 0, 0, 0.4, 0.6, 0.9, 1.2, 1.6, 2.2]),
  TREMOR_CAP_PX: 2.5,
  TREMOR_HEAT_FLOOR: 0.5,
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.4, 2.6]),
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  PUNCH_PICK_PX: Object.freeze([0.8, 3.0]),     // by min(streak,10)/10
  PUNCH_PICK_MS: Object.freeze([160, 340]),
  PUNCH_BANK_PX: 4.2,
  PUNCH_BANK_MS: 420,
  PUNCH_BUST_PX: 5,
  PUNCH_BUST_MS: 380,
  PUNCH_RIDE_PX: 2.4,
  PUNCH_RIDE_MS: 260,
  PUNCH_CAP_PX: 6,
  PUNCH_CAP_MS: 450,
  BLOOM_MS: 320,

  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([8000, 4800]),
  BREATH_HOLD_MS: 2400,
  BREATH_ALPHA: Object.freeze([0.1, 0.36]),
  STORM_BREATH_ALPHA: Object.freeze([0.3, 0.55]),
  MOTES_DENSITY: Object.freeze([0.18, 0.45]),
  STORM_EMBERS: Object.freeze([0.35, 0.75]),
  BURST_CHANCE: Object.freeze([0.45, 0.9]),
  BURST_COUNT: Object.freeze([1, 3]),
  BURST_HOLD_MS: Object.freeze([750, 1250]),
  BURST_VMIN: Object.freeze([0.22, 0.38]),
  WHEEL_ALPHA: Object.freeze([0.1, 0.22]),      // quieter than IC/DE: this class is eye-tracking, the wheel rides BEHIND the shells
  VEIL_CHANCE: Object.freeze([0.3, 0.75]),
  VEIL_S: Object.freeze([0.35, 0.8]),
  VEIL_VARIANTS: Object.freeze(['rgbsplit', 'vhsroll', 'datamosh']),
  FLASH_COUNT: 2,
  WHISPER_ALPHA: 0.01,
  WHISPER_HOLD_MS: 900,
  /** cue level = min(AUDIO_CEIL[tier], base + rung * step). */
  CUE_LEVEL_BASE: 0.22,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
  NODE_BUDGET: 1,
});

const STYLE_ID = 'g-md-pressure-style';
const STYLE_TEXT = `
.g-md-p-dusk{position:absolute;inset:-2%;pointer-events:none;opacity:0;z-index:3;
  background:radial-gradient(80% 70% at 50% 60%, rgba(6,4,16,.55), rgba(6,4,16,.92) 100%);
  transition:opacity 1.2s ease;will-change:opacity}
.g-md-p-bloom{box-shadow:0 0 0 2px rgba(255,105,180,.45), 0 0 18px rgba(255,105,180,.5) !important;transition:box-shadow .3s ease}
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

/** pick streak -> rung (0..8). Pure, monotone, clamps garbage to 0. */
export function rungFor(streak) {
  const s = Math.max(0, Math.floor(Number(streak) || 0));
  const T = MD_PRESSURE.RUNG_STREAK;
  let r = 0;
  for (let i = 0; i < T.length; i++) { if (s >= T[i]) r = i; else break; }
  return r;
}
/** the dusk's opacity for a shown rung and heat (0..1). Pure. */
export function duskFor(rung, heat01) {
  const P = MD_PRESSURE;
  const r = Math.max(0, Math.min(P.DUSK.length - 1, Math.floor(Number(rung) || 0)));
  const base = P.DUSK[r] || 0;
  if (base <= 0) return 0;
  return +(base * (P.DUSK_HEAT_FLOOR + (1 - P.DUSK_HEAT_FLOOR) * clamp01(heat01))).toFixed(3);
}
/** idle tremor amplitude in px; 0 under reduced motion / motionLevel 0. Pure. */
export function tremorAmpPx(streak, heat01, reducedMotion, motionLevel) {
  if (reducedMotion) return 0;
  const m = MD_PRESSURE.MOTION_MUL[Math.max(0, Math.min(2, motionLevel == null ? 2 : Math.round(Number(motionLevel) || 0)))];
  if (!m) return 0;
  const base = MD_PRESSURE.TREMOR_PX[rungFor(streak)] || 0;
  if (base <= 0) return 0;
  const f = MD_PRESSURE.TREMOR_HEAT_FLOOR;
  return Math.min(MD_PRESSURE.TREMOR_CAP_PX, +(base * m * (f + (1 - f) * clamp01(heat01))).toFixed(3));
}
/** a verdict punch {px, ms} by beat kind + streak. Pure. */
export function punchFor(kind, streak) {
  const P = MD_PRESSURE;
  const q = clamp01(Math.min(10, Number(streak) || 0) / 10);
  let px; let ms;
  if (kind === 'bank') { px = P.PUNCH_BANK_PX; ms = P.PUNCH_BANK_MS; }
  else if (kind === 'bust') { px = P.PUNCH_BUST_PX; ms = P.PUNCH_BUST_MS; }
  else if (kind === 'ride') { px = P.PUNCH_RIDE_PX; ms = P.PUNCH_RIDE_MS; }
  else if (kind === 'pick') { px = lerp(P.PUNCH_PICK_PX, q); ms = lerp(P.PUNCH_PICK_MS, q); }
  else if (kind === 'miss') { px = 1.6; ms = 220; }
  else return { px: 0, ms: 0 };
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
      const w = Number(window.innerWidth) || 0; const h = Number(window.innerHeight) || 0;
      if (w > 0 && h > 0) return Math.min(w, h);
    }
  } catch (e) { /* headless */ }
  return 720;
}
function setOpacity(node, v) {
  try { if (node && node.style) node.style.opacity = (v == null || v === '') ? '' : String(v); } catch (e) { /* noop */ }
}
function setTranslate(node, x, y) {
  try { if (node && node.style) node.style.translate = (x || y) ? (x.toFixed(2) + 'px ' + y.toFixed(2) + 'px') : ''; } catch (e) { /* noop */ }
}
function cls(node, name, on) { try { if (node && node.classList) node.classList[on ? 'add' : 'remove'](name); } catch (e) { /* noop */ } }
function el(tag, c) {
  try { const n = document.createElement(tag); if (c) n.className = c; return n; } catch (e) { return null; }
}

/**
 * @param {Object} o
 *   engine {fire,sustain,stop,channels}, stage (.g-md-stage), chrome [els] (HUD, backdrop,
 *   marquee host - never the arc), timers {after,every,clear}, reduced, capsOk (bool|fn),
 *   motionLevel 0..2, seed, gradeTier, backdrop (.g-md-backdrop, optional - the dusk's host
 *   and the veil's glitch target; else found under the stage), log
 */
export function createMdPressure(o) {
  const opts = o || {};
  const P = MD_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const eng = opts.engine || {};
  /* the chrome: never a node that holds a real button (the stake strip is
     input - Law II), never the arc. index.js hands [hud, msg, stake]; the
     stake is dropped here. */
  const chrome = (Array.isArray(opts.chrome) ? opts.chrome : []).filter((n) => {
    if (!n || !n.style) return false;
    try {
      if (n.classList && (n.classList.contains('g-md-stake') || n.classList.contains('g-md-arc'))) return false;
      if (typeof n.querySelector === 'function' && n.querySelector('button')) return false;
    } catch (e) { /* keep */ }
    return true;
  });
  const armedBase = !!opts.stage && !!opts.timers && typeof opts.timers.after === 'function';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false;
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
  const seedBase = String(opts.seed || 'md') + '|md-pressure|';
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
    if (!armedBase || kind === 'wash') return;      // NEVER stop('wash') - trap 33
    count('stop:' + kind);
    try { if (typeof eng.stop === 'function') eng.stop(kind); } catch (e) { /* noop */ }
  }
  function cue(name, rung) {
    if (!armed() || !name) return;
    const level = Math.min(audioCeil, P.CUE_LEVEL_BASE + rung * P.CUE_LEVEL_STEP);
    count('cue');
    fire('audio_trigger', { name, level, pitch: +(0.9 + rung * 0.04).toFixed(3) });
  }

  /* ---- state -------------------------------------------------------------- */
  let started = false;
  let stopped = false;
  let paused = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let streak = 0;
  let rung = 0;
  let wantRung = 0;
  let downTimer = 0;
  let bellOn = false;
  let outOn = false;
  const on = new Set();
  let dusk = null;
  let backdrop = opts.backdrop || null;
  let flareUntil = 0;
  let flareTimer = 0;
  let bloomTimer = 0;
  let breathTimer = 0;

  /* ---- the tremor --------------------------------------------------------- */
  let rafId = 0; let loopOn = false; let amp = 0;
  let punch = { px: 0, until: 0, ms: 1 };
  let translateWrites = 0;

  function restChrome() { for (const n of chrome) setTranslate(n, 0, 0); }
  function loop() {
    rafId = 0;
    if (!loopOn || paused || destroyed) return;
    const tNow = nowMs();
    let extra = 0;
    if (punch.px > 0) {
      const left = punch.until - tNow;
      if (left <= 0) punch = { px: 0, until: 0, ms: 1 };
      else { const k = left / punch.ms; extra = punch.px * k * k; }
    }
    const a = amp + extra;
    if (a <= 0.005 || !armed()) { restChrome(); loopOn = false; return; }
    const ts = tNow / 1000;
    const x = a * (0.7 * Math.sin(ts * tremorPhase.hzF * 6.28 + tremorPhase.fx) + 0.3 * Math.sin(ts * tremorPhase.hzS * 6.28));
    const y = a * (0.7 * Math.cos(ts * tremorPhase.hzF * 5.9 + tremorPhase.fy) + 0.3 * Math.sin(ts * tremorPhase.hzS * 5.1 + 1.7));
    for (let i = 0; i < chrome.length; i++) { setTranslate(chrome[i], x * (i === 0 ? 1 : 0.6), y * (i === 0 ? 1 : 0.6)); }
    translateWrites += 1;
    const raf = rafFn();
    if (raf) rafId = raf(loop);
  }
  function armLoop() {
    if (loopOn || paused || destroyed || still || !chrome.length || !armed()) return;
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
  function doPunch(spec) {
    if (!armed() || stopped || !spec || spec.px <= 0) return;
    if (still) { bloomChrome(); return; }
    const tNow = nowMs();
    punch = { px: Math.max(punch.px, spec.px), until: Math.max(punch.until, tNow + spec.ms), ms: Math.max(spec.ms, 1) };
    count('punch');
    armLoop();
  }
  function bloomChrome() {
    const n = chrome[0];
    if (!n) return;
    cls(n, 'g-md-p-bloom', false);
    if (typeof n.offsetWidth === 'number') void n.offsetWidth;
    cls(n, 'g-md-p-bloom', true);
    cancel(bloomTimer);
    bloomTimer = after(P.BLOOM_MS, () => cls(n, 'g-md-p-bloom', false));
  }

  /* ---- the dusk ----------------------------------------------------------- */
  function findBackdrop() {
    if (backdrop) return backdrop;
    try { backdrop = opts.stage && opts.stage.querySelector ? opts.stage.querySelector('.g-md-backdrop') : null; } catch (e) { backdrop = null; }
    return backdrop;
  }
  function mount() {
    if (dusk) return;
    const host = findBackdrop();
    if (!host || !host.appendChild) return;
    ensureStyle();
    dusk = el('div', 'g-md-p-dusk');
    if (dusk) host.appendChild(dusk);
  }
  function paintDusk() {
    if (!armedBase || destroyed || !dusk) return;
    if (flareUntil > nowMs()) return;
    setOpacity(dusk, stopped ? Math.min(P.DUSK_END, duskFor(rung, heat)) : (capsOk() ? duskFor(rung, heat) : 0));
  }
  function flare(level, holdMs) {
    if (!armed() || stopped || !dusk) return;
    const until = nowMs() + Math.max(120, holdMs | 0);
    const lv = Math.max(level, duskFor(rung, heat));
    try { dusk.style.transition = 'opacity ' + P.FLARE_IN_MS + 'ms ease-out'; setOpacity(dusk, lv); } catch (e) { /* cosmetic */ }
    if (until > flareUntil) {
      flareUntil = until;
      cancel(flareTimer);
      flareTimer = after(until - nowMs(), () => {
        flareTimer = 0; flareUntil = 0;
        try { dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
        paintDusk();
      });
    }
    count('flare');
  }
  function retune() {
    paintDusk();
    amp = 0;
    if (!still && !stopped && !outOn) {
      const base = P.TREMOR_PX[rung] || 0;
      amp = base <= 0 ? 0 : Math.min(P.TREMOR_CAP_PX, base * P.MOTION_MUL[motion] * (P.TREMOR_HEAT_FLOOR + (1 - P.TREMOR_HEAT_FLOOR) * heat));
    }
    armLoop();
  }

  /* ---- the rungs ---------------------------------------------------------- */
  function breathOn(storm) {
    sustain('wash', { variant: 'sublim', alpha: lerp(storm ? P.STORM_BREATH_ALPHA : P.BREATH_ALPHA, heat), holdMs: P.BREATH_HOLD_MS });
  }
  function armBreath() {
    if (breathTimer) cancel(breathTimer);
    const ms = lerp(P.BREATH_MS, heat) * (0.85 + roll('breath') * 0.3);
    breathTimer = after(Math.round(ms), () => { breathTimer = 0; if (on.has('breath') && !stopped) { breathOn(on.has('storm')); armBreath(); } });
  }
  function whisperOut(variant) {
    sustain('wash', { variant, alpha: P.WHISPER_ALPHA, holdMs: P.WHISPER_HOLD_MS });
  }
  function wheelOn() { sustain('wash', { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true }); }
  function applyAdd(add, goingUp) {
    if (goingUp) {
      on.add(add);
      if (add === 'breath') { breathOn(false); armBreath(); }
      else if (add === 'motes') sustain('ambient_field', { kind: 'motes', density: lerp(P.MOTES_DENSITY, heat) });
      else if (add === 'wheel') wheelOn();
      else if (add === 'subs') sustain('sub_flash', {});
      else if (add === 'storm') {
        sustain('ambient_field', { kind: 'embers', density: lerp(P.STORM_EMBERS, heat) });
        breathOn(true);
      }
      /* burst / tremor / veil / flashes ride the verdict beats: nothing to switch on */
    } else {
      on.delete(add);
      if (add === 'breath') { if (breathTimer) { cancel(breathTimer); breathTimer = 0; } whisperOut('sublim'); }
      else if (add === 'motes') { if (!on.has('storm')) stopKind('ambient_field'); }
      else if (add === 'wheel') whisperOut('spiral');
      else if (add === 'subs') stopKind('sub_flash');
      else if (add === 'storm') {
        stopKind('ambient_field');
        if (on.has('motes')) sustain('ambient_field', { kind: 'motes', density: lerp(P.MOTES_DENSITY, heat) });
        if (on.has('breath')) breathOn(false);
      }
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
    say('pressure: rung ' + rung + ' (' + (up ? 'up' : 'down') + ', streak ' + streak + ')');
  }
  function setStreak(s) {
    streak = Math.max(0, Math.floor(Number(s) || 0));
    wantRung = rungFor(streak);
    if (!started || stopped) return;
    if (wantRung > rung) { cancel(downTimer); downTimer = 0; setRung(wantRung); }
    else if (wantRung < rung && !downTimer) {
      downTimer = after(P.HYST_MS, () => { downTimer = 0; if (wantRung < rung) setRung(wantRung); });
    } else if (wantRung === rung) { cancel(downTimer); downTimer = 0; }
  }
  function repaintHeat() {
    if (Math.abs(heat - lastPaintHeat) < P.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    if (on.has('breath')) breathOn(on.has('storm'));
    if (on.has('wheel')) wheelOn();
    if (on.has('storm')) sustain('ambient_field', { kind: 'embers', density: lerp(P.STORM_EMBERS, heat) });
    else if (on.has('motes')) sustain('ambient_field', { kind: 'motes', density: lerp(P.MOTES_DENSITY, heat) });
  }

  /* the riders: only on verdict beats */
  function riders(kind) {
    if (!armed() || stopped) return;
    const verdict = kind === 'pick' || kind === 'bank' || kind === 'ride';
    if (!verdict) return;
    const heavy = kind === 'bank';
    if (rung >= 3 && !still && (heavy || roll('burst') < lerp(P.BURST_CHANCE, heat))) {
      const hold = Math.round(lerp(P.BURST_HOLD_MS, heat));
      const went = fire('gif_burst', {
        count: Math.round(lerp(P.BURST_COUNT, heat)) + (heavy ? 1 : 0), clickSafe: true, clickable: false,
        sizePx: Math.round(vmin() * lerp(P.BURST_VMIN, heat)), holdMs: hold,
        variant: rung >= 8 ? 'scatter' : undefined,
      });
      if (went) flare(P.FLARE_BURST, hold + 300);
    }
    if (rung >= 5 && !still && (heavy || roll('veil') < lerp(P.VEIL_CHANCE, heat))) {
      const target = findBackdrop();
      if (target) {
        fire('glitch_swap', {
          targets: target, variant: P.VEIL_VARIANTS[Math.floor(roll('veil-v') * P.VEIL_VARIANTS.length)],
          seconds: lerp(P.VEIL_S, heat), sfx: false,
          onSwap() { /* presentation only: the backdrop keeps its own felt */ },
        });
      }
    }
    if (rung >= 7 && !still) fire('flash_burst', { count: P.FLASH_COUNT + (heavy ? 1 : 0), clickSafe: true, clickable: false });
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      stopped = false;
      lastPaintHeat = -1;
      if (!armed()) { say('pressure: disarmed'); return; }
      mount();
      if (wantRung > 0) setRung(wantRung);
      say('pressure: mounted ' + (dusk ? 1 : 0) + ' node, chrome x' + chrome.length);
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started && !stopped) { repaintHeat(); retune(); }
    },
    /** The pick streak, after the ledger. 0 = a miss (the storm steps down behind HYST). */
    setStreak(n) { setStreak(n); },
    /** index.js's beats: 'reveal' | 'shuffle' | 'swap' | 'pick' | 'miss' | 'ride' | 'bank' | 'bust' | 'bell'. */
    beat(kind) {
      if (!started || stopped) return;
      const k = String(kind || '');
      if (k === 'pick' || k === 'bank' || k === 'ride' || k === 'bust' || k === 'miss') doPunch(punchFor(k, streak));
      if (k === 'bust' && rung >= 5 && armed() && !still) {
        const target = findBackdrop();
        if (target) fire('glitch_swap', { targets: target, variant: 'datamosh', seconds: 0.5, sfx: false, onSwap() {} });
      }
      riders(k);
    },
    bell(on2) { bellOn = !!on2; },
    /** The bell took the table: everything sighs out, the dusk eases to its debrief level. */
    dimOut() { outOn = true; api.stop(); },
    stop() {
      if (stopped) return;
      stopped = true;
      cancel(downTimer); downTimer = 0;
      if (breathTimer) { cancel(breathTimer); breathTimer = 0; }
      if (on.has('breath') || on.has('storm')) whisperOut('sublim');
      if (on.has('wheel')) whisperOut('spiral');
      if (on.has('subs')) stopKind('sub_flash');
      if (on.has('motes') || on.has('storm')) stopKind('ambient_field');
      cancel(flareTimer); flareTimer = 0; flareUntil = 0;
      try { if (dusk && dusk.style) dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
      setOpacity(dusk, rung > 0 ? Math.min(P.DUSK_END, duskFor(rung, heat)) : 0);
      on.clear();
      rung = 0;
      amp = 0; punch = { px: 0, until: 0, ms: 1 };
      haltLoop();
      restChrome();
    },
    pause() {
      paused = true;
      cancelAll(); downTimer = 0; flareTimer = 0; flareUntil = 0; breathTimer = 0; bloomTimer = 0;
      try { if (dusk && dusk.style) dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
      haltLoop();
      restChrome();
    },
    resume() {
      paused = false;
      if (!stopped) { retune(); setStreak(streak); if (on.has('breath')) armBreath(); }
    },
    destroy() {
      api.stop();
      destroyed = true;
      cancelAll();
      haltLoop();
      restChrome();
      for (const n of chrome) cls(n, 'g-md-p-bloom', false);
      if (dusk) { try { dusk.remove(); } catch (e) { /* noop */ } }
      dusk = null;
    },
    diagnostics() {
      return {
        armed: armed(), started, stopped, paused, heat: +heat.toFixed(3), streak, rung, wantRung, bell: bellOn, out: outOn,
        on: Array.from(on), tremorPx: +amp.toFixed(3), punchPx: +punch.px.toFixed(2), loopOn, translateWrites,
        dusk: stopped ? 0 : duskFor(rung, heat), flaring: flareUntil > nowMs(),
        fires: Object.assign({}, fires), liveTimers: live.size, nodes: dusk ? 1 : 0, chrome: chrome.length,
      };
    },
  };
  return api;
}

export default createMdPressure;
