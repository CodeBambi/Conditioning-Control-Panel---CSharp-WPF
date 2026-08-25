/* ============================================================================
 * games/impulse-control/pressure.js - the Drop Tube's effects ladder: THE SURGE.
 * The casino (casino.js) lights the room, the trickster (trickster.js) lies
 * about it; this file is what the room DOES to you as your CHAIN climbs. The
 * owner's ruling for this class: the ladder is STREAK-DRIVEN. The rung is your
 * current run of clean pops; an X hit or a drift drops it to zero and the storm
 * steps DOWN behind a hysteresis (fades, never a snap); a survived X HOLDS the
 * rung (restraint is not a break). Magnitude rides the class heat (which
 * index.js already folds streak + progress into). Same seed, same surge.
 *
 * THE RUNG TABLE (streak -> rung -> what switches ON; cumulative). Re-cut
 * 2026-08-22 after the owner's play: the spiral and the big gifs now arrive at
 * streaks a real class reaches (4-8), not 14+; the whisper-class dressing
 * (scan, subs, chroma) moved up the ladder where it is seasoning, not the meal.
 *   0-1   rung 0  clean room (the tube's own seeded show is all there is)
 *   2     rung 1  BREATH      a pink wash breathes on a cadence        [engine wash:pink]
 *   4     rung 2  BURST       big gif bursts ride pops (30-46vmin)     [engine gif_burst sizePx]
 *   6     rung 3  WHEEL       the full-screen spiral wash wakes (the   [engine wash:spiral]
 *                             shell's real image through the provider) +
 *                             THE TREMOR wakes (tube + HUD, never the basin)
 *   8     rung 4  VEIL+GIANT  the backdrop glitch-shudders on pops;    [engine glitch_swap on bgA/bgB,
 *                             a pop may drop ONE near-fullscreen gif     gif_burst sizePx ~0.92vmin]
 *   11    rung 5  RAIN+SCAN   gif rain on pops, scanline whisper        [engine gif_rain, crt:scanline]
 *   14    rung 6  SUBS+CHROMA sub flash stream + the crt turns chroma  [engine sub_flash alias, crt:chroma]
 *   18    rung 7  FLASHES     flash bursts ride pops                   [engine flash_burst]
 *   22    rung 8  STORM       embers, downpour, the pink flood,        [engine ambient_field, gif_rain,
 *                             the tremor at its cap                      wash:pink]
 *
 * THE DUSK (render.js nodes.dusk): the ONE game-local surface of this file -
 * an empty dimmer laid over the tube and under the basin/HUD whose opacity
 * climbs with the rung (duskFor). The chute out-shone the engine's washes
 * and gifs (they are screen-blended and heat-gated), which read as "the
 * effects play behind the tube"; dimming the tube under them is what puts
 * them in front without touching the engine's ceilings. Opacity only.
 *
 * THE TREMOR: the whole machine room vibrates with the chain - a continuous
 * low tremor whose amplitude grows with the rung, a PUNCH on every pop sized
 * by its points (extend-not-stack, quadratic ring-down, screenShake posture),
 * a heavier punch on a milestone. Written on the CSS individual `translate`
 * of render's `tubewrap` (the chute canvas) and `hud` (at 60%), which nothing
 * else transforms - and NEVER on the basin or the bubble: that is the one tap
 * target in the game (Law II), and a reflex test cannot have a moving target.
 * A rAF loop ONLY while amplitude > 0; stopped by pause()/stop()/destroy(),
 * re-armed by resume(). Reduced motion / motionLevel 0 -> no tremor at all.
 *
 * ENGINE vs GAME-LOCAL (audit): EVERYTHING visual goes through opts.engine
 * (index.js's null-safe wrapper: clickSafe welded, ceiling rule, capsOk,
 * effectsConsumed). Game-local: the tremor (two transform writes on render's
 * own layers) and the dusk's opacity (one style write on render's own node).
 * This file creates NO nodes. Node budget: 0.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects (streak / pts / progress),
 *       writes nothing about score, streak, tally or grade, renders no text.
 *   II  input honest  - no node of ours exists; the basin and the bubble are
 *       never transformed, never covered; every engine fire is clickSafe.
 *   III never still   - from rung 1 the room breathes; from rung 3 it shakes.
 *   V   seeded        - per-tag mulberry32 off seed+'|ic-pressure|<tag>',
 *       append-only; no Math.random.
 *   VI  exits sacred  - capsOk false (bgIntensity 0) = zero fires, zero
 *       tremor; the rung CUE is decoupled from it on purpose (W2) - a visual
 *       dial must not mute the school, and the cue is clamped either way;
 *       reduced motion = no tremor (the engine gates its own kinds);
 *       pause() halts the rAF and drops transient timers; destroy() restores
 *       the translate and stops every sustain with a fade.
 *   VII lexicon       - this file renders no text.
 *
 * THE WASH TRAP (Deep End, pass 4): engine.stop('wash') kills EVERY wash
 * variant. Two live here (pink + spiral), so a rung is stepped DOWN by
 * re-triggering its variant at a whisper alpha with a short hold (it fades on
 * its own deadline) - stop('wash') is called ONLY from stop()/destroy().
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const IC_PRESSURE = Object.freeze({
  /** streak thresholds per rung (index = rung). Monotone. */
  RUNG_STREAK: Object.freeze([0, 2, 4, 6, 8, 11, 14, 18, 22]),
  RUNG_ADDS: Object.freeze([
    Object.freeze([]),
    Object.freeze(['breath']),
    Object.freeze(['burst']),
    Object.freeze(['wheel', 'tremor']),
    Object.freeze(['veil', 'giant']),
    Object.freeze(['rain', 'scan']),
    Object.freeze(['subs', 'chroma']),
    Object.freeze(['flashes']),
    Object.freeze(['storm']),
  ]),
  RUNG_CUE: Object.freeze([null, 'wash', 'burst', 'wash', 'glitch', 'burst', 'whisper', 'burst', 'near_miss']),
  /** the dusk's opacity per rung (x (DUSK_HEAT_FLOOR + (1-floor)*heat)); 0 at rest. */
  DUSK: Object.freeze([0, 0.14, 0.26, 0.38, 0.46, 0.52, 0.56, 0.6, 0.64]),
  DUSK_HEAT_FLOOR: 0.75,
  DUSK_END: 0.3,
  /** THE FLARE: under a gif burst the dusk snaps UP for the burst's hold so a
      translucent gif (the engine caps bursts at ~0.75 alpha) reads OPAQUE and
      IN FRONT - neon lines bleeding through a gif read as "behind the tube"
      (owner 2026-08-22, twice). Snaps up in FLARE_IN_MS, eases back on the
      dusk's own transition. */
  FLARE_BURST: 0.84,
  FLARE_GIANT: 0.92,
  FLARE_IN_MS: 160,
  BURST_HOLD_MS: Object.freeze([750, 1250]),
  /** Stepping DOWN waits this long and fades; stepping UP is immediate. */
  HYST_MS: 1600,
  HEAT_REPAINT_STEP: 0.06,

  /* ---- THE TREMOR ------------------------------------------------------ */
  TREMOR_PX: Object.freeze([0, 0, 0, 0.4, 0.6, 0.9, 1.3, 1.8, 2.2]),
  TREMOR_CAP_PX: 2.5,
  TREMOR_HEAT_FLOOR: 0.5,
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.4, 2.6]),
  HUD_SHARE: 0.6,
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  /** a pop punch: px / ms from q = pts/100; milestones heavier; hard cap. */
  PUNCH_PX: Object.freeze([0.8, 3.2]),
  PUNCH_MS: Object.freeze([160, 360]),
  PUNCH_MILESTONE_PX: 4.5,
  PUNCH_MILESTONE_MS: 420,
  PUNCH_HIT_PX: 5,                 // the X popped: the room jolts (render shakes the stage; we jolt the tube)
  PUNCH_HIT_MS: 380,
  PUNCH_CAP_PX: 6,
  PUNCH_CAP_MS: 450,
  MILESTONES: Object.freeze([5, 10, 15, 20]),

  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([8000, 4800]),
  BREATH_HOLD_MS: 2400,
  BREATH_ALPHA: Object.freeze([0.1, 0.38]),
  STORM_BREATH_ALPHA: Object.freeze([0.3, 0.55]),
  SCAN_LEVEL: Object.freeze([0.18, 0.5]),
  CHROMA_LEVEL: Object.freeze([0.35, 0.7]),
  BURST_CHANCE: Object.freeze([0.45, 0.9]),
  BURST_COUNT: Object.freeze([1, 3]),
  /** burst box edge as a fraction of vmin (the engine's default 120-270px is a
      postage stamp against the chute); the engine still jitters it 0.8-1.2x. */
  BURST_VMIN: Object.freeze([0.3, 0.46]),
  /** THE GIANT: one near-fullscreen gif dropped on the landing, by chance. */
  GIANT_CHANCE: Object.freeze([0.3, 0.65]),
  GIANT_VMIN: 0.92,
  GIANT_HOLD_MS: Object.freeze([1100, 1700]),
  VEIL_CHANCE: Object.freeze([0.3, 0.75]),
  VEIL_S: Object.freeze([0.35, 0.9]),
  VEIL_VARIANTS: Object.freeze(['rgbsplit', 'vhsroll', 'datamosh']),
  RAIN_CHANCE: Object.freeze([0.4, 0.9]),
  RAIN_MS: Object.freeze([2200, 4600]),
  WHEEL_ALPHA: Object.freeze([0.3, 0.52]),
  STORM_RAIN_MS: 6000,
  STORM_EMBERS: Object.freeze([0.3, 0.7]),
  STORM_FLASH_COUNT: 2,
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

/** streak -> rung (0..8). Pure, monotone, clamps garbage to 0. */
export function rungFor(streak) {
  const s = Math.max(0, Math.floor(Number(streak) || 0));
  const T = IC_PRESSURE.RUNG_STREAK;
  let r = 0;
  for (let i = 0; i < T.length; i++) { if (s >= T[i]) r = i; else break; }
  return r;
}
/** the dusk's opacity for a shown rung and heat (0..1). Pure. */
export function duskFor(rung, heat01) {
  const P = IC_PRESSURE;
  const r = Math.max(0, Math.min(P.DUSK.length - 1, Math.floor(Number(rung) || 0)));
  const base = P.DUSK[r] || 0;
  if (base <= 0) return 0;
  return +(base * (P.DUSK_HEAT_FLOOR + (1 - P.DUSK_HEAT_FLOOR) * clamp01(heat01))).toFixed(3);
}
/** idle tremor amplitude in px; 0 under reduced motion / motionLevel 0. Pure. */
export function tremorAmpPx(streak, heat01, reducedMotion, motionLevel) {
  if (reducedMotion) return 0;
  const m = IC_PRESSURE.MOTION_MUL[Math.max(0, Math.min(2, motionLevel == null ? 2 : Math.round(Number(motionLevel) || 0)))];
  if (!m) return 0;
  const base = IC_PRESSURE.TREMOR_PX[rungFor(streak)] || 0;
  if (base <= 0) return 0;
  const f = IC_PRESSURE.TREMOR_HEAT_FLOOR;
  return Math.min(IC_PRESSURE.TREMOR_CAP_PX, +(base * m * (f + (1 - f) * clamp01(heat01))).toFixed(3));
}
/** a pop punch {px, ms} from pts (0..100) and whether it was a milestone. Pure. */
export function punchFor(o) {
  const d = o || {};
  const q = clamp01((Number(d.pts) || 0) / 100);
  const P = IC_PRESSURE;
  let px = lerp(P.PUNCH_PX, q);
  let ms = lerp(P.PUNCH_MS, q);
  if (d.milestone) { px = Math.max(px, P.PUNCH_MILESTONE_PX); ms = Math.max(ms, P.PUNCH_MILESTONE_MS); }
  if (d.hit) { px = Math.max(px, P.PUNCH_HIT_PX); ms = Math.max(ms, P.PUNCH_HIT_MS); }
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
function setOpacity(node, v) {
  try { if (node && node.style) node.style.opacity = (v == null || v === '') ? '' : String(v); } catch (e) { /* noop */ }
}
function setTranslate(node, x, y) {
  try { if (node && node.style) node.style.translate = (x || y) ? (x.toFixed(2) + 'px ' + y.toFixed(2) + 'px') : ''; } catch (e) { /* noop */ }
}

/**
 * @param {Object} o  see IC-HOUSE-RULES §4:
 *   seed, gradeTier, reduced, motionLevel, nodes (render.nodes), engine
 *   {fire,sustain,stop,channels,audio}, assets, timers {after,every,clear},
 *   capsOk () => bool, log
 */
export function createIcPressure(o) {
  const opts = o || {};
  const P = IC_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  /* EMI COMMENTARY SEAM: the game hands its guarded note() down in the base
     kit. A deck built by anything older simply has none. */
  const note = typeof opts.note === 'function' ? opts.note : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const nodes = opts.nodes || {};
  const eng = opts.engine || {};
  const armedBase = !!nodes.stage && !!opts.timers && typeof opts.timers.after === 'function';
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
  const seedBase = String(opts.seed || 'ic') + '|ic-pressure|';
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
  function stop(kind) {
    if (!armedBase) return;
    count('stop:' + kind);
    try { if (typeof eng.stop === 'function') eng.stop(kind); } catch (e) { /* noop */ }
  }
  /**
   * W2 - THE DECOUPLE (spec 3). This cue used to gate on `armed()`, which
   * folds capsOk in - so a player who turned bgIntensity to 0 lost the SURGE's
   * only announcement along with its light. bgIntensity is the VISUAL exit
   * (Law VI), never a request for a silent school. Sound now rides the rest of
   * the gate (armedBase / destroyed / stopped); every visual fire above is
   * untouched and still capsOk-gated, and the level is still clamped to this
   * tier's audio ceiling with the same per-rung pitch ratchet.
   */
  function cue(name, rung) {
    if (!armedBase || destroyed || stopped || !name) return;
    const level = Math.min(audioCeil, P.CUE_LEVEL_BASE + rung * P.CUE_LEVEL_STEP);
    count('cue');
    try {
      if (typeof eng.audio === 'function') eng.audio(name, level, { pitch: +(0.9 + rung * 0.04).toFixed(3) });
      else if (typeof eng.fire === 'function') eng.fire('audio_trigger', { name, level, pitch: +(0.9 + rung * 0.04).toFixed(3) });
    } catch (e) { /* noop */ }
  }

  /* ---- state -------------------------------------------------------------- */
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let streak = 0;
  let rung = 0;              // the SHOWN rung
  let wantRung = 0;          // what the streak says
  let downTimer = 0;
  const on = new Set();      // adds currently live
  let stopped = false;

  /* ---- the tremor --------------------------------------------------------- */
  let rafId = 0;
  let loopOn = false;
  let amp = 0;               // idle amplitude (px)
  let punch = { px: 0, until: 0, ms: 1 };
  let paused = false;

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
    const a = amp + extra;
    if (a <= 0.005) {
      setTranslate(nodes.tubewrap, 0, 0);
      setTranslate(nodes.hud, 0, 0);
      loopOn = false;
      return;
    }
    const ts = tNow / 1000;
    const x = a * (0.7 * Math.sin(ts * tremorPhase.hzF * 6.28 + tremorPhase.fx) + 0.3 * Math.sin(ts * tremorPhase.hzS * 6.28));
    const y = a * (0.7 * Math.cos(ts * tremorPhase.hzF * 5.9 + tremorPhase.fy) + 0.3 * Math.sin(ts * tremorPhase.hzS * 5.1 + 1.7));
    setTranslate(nodes.tubewrap, x, y);
    setTranslate(nodes.hud, x * P.HUD_SHARE, y * P.HUD_SHARE);
    const raf = rafFn();
    if (raf) rafId = raf(loop);
  }
  function armLoop() {
    if (loopOn || paused || destroyed || still) return;
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
  let flareUntil = 0;
  let flareTimer = 0;
  function paintDusk() {
    if (!armedBase || destroyed) return;
    if (flareUntil > nowMs()) return;           // a flare is holding the dusk up
    setOpacity(nodes.dusk, stopped ? Math.min(P.DUSK_END, duskFor(rung, heat)) : (capsOk() ? duskFor(rung, heat) : 0));
  }
  /* snap the dusk up under a burst, then let it ease back to the rung's level */
  function flare(level, holdMs) {
    if (!armed() || stopped || !nodes.dusk) return;
    const until = nowMs() + Math.max(120, holdMs | 0);
    const lv = Math.max(level, duskFor(rung, heat));
    try {
      nodes.dusk.style.transition = 'opacity ' + P.FLARE_IN_MS + 'ms ease-out';
      setOpacity(nodes.dusk, lv);
    } catch (e) { /* cosmetic */ }
    if (until > flareUntil) {
      flareUntil = until;
      cancel(flareTimer);
      flareTimer = after(until - nowMs(), () => {
        flareTimer = 0; flareUntil = 0;
        try { nodes.dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
        paintDusk();
      });
    }
    count('flare');
  }
  function retune() {
    paintDusk();
    amp = still ? 0 : tremorAmpPx(streak, heat, reduced, motion);
    /* the SHOWN rung drives the idle tremor (a hysteresis step-down keeps it
       humming for the grace period too) */
    if (!still) {
      const base = P.TREMOR_PX[rung] || 0;
      amp = base <= 0 ? 0 : Math.min(P.TREMOR_CAP_PX, base * P.MOTION_MUL[motion] * (P.TREMOR_HEAT_FLOOR + (1 - P.TREMOR_HEAT_FLOOR) * heat));
    }
    armLoop();
  }
  function doPunch(spec) {
    if (still || !armed() || !spec || spec.px <= 0) return;
    const tNow = nowMs();
    /* extend-not-stack: keep the louder amplitude, push the deadline */
    punch = { px: Math.max(punch.px, spec.px), until: Math.max(punch.until, tNow + spec.ms), ms: Math.max(spec.ms, 1) };
    count('punch');
    armLoop();
  }

  /* ---- the rungs ---------------------------------------------------------- */
  function breathOn(storm) {
    sustain('wash', {
      variant: 'pink',
      alpha: lerp(storm ? P.STORM_BREATH_ALPHA : P.BREATH_ALPHA, heat),
      holdMs: P.BREATH_HOLD_MS,
      sustainForever: true,
    });
  }
  function whisperOut(variant) {
    /* step a wash DOWN: re-trigger at a whisper with a short hold - it fades on
       its own deadline (never stop('wash'), see the header) */
    sustain('wash', { variant, alpha: P.WHISPER_ALPHA, holdMs: P.WHISPER_HOLD_MS });
  }
  function applyAdd(add, goingUp) {
    if (goingUp) {
      on.add(add);
      if (add === 'breath') breathOn(false);
      else if (add === 'scan') sustain('crt', { level: lerp(P.SCAN_LEVEL, heat), variant: 'scanline' });
      else if (add === 'wheel') sustain('wash', { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true });
      else if (add === 'subs') sustain('sub_flash', {});
      else if (add === 'chroma') sustain('crt', { level: lerp(P.CHROMA_LEVEL, heat), variant: 'chroma' });
      else if (add === 'storm') {
        sustain('ambient_field', { kind: 'embers', density: lerp(P.STORM_EMBERS, heat) });
        sustain('gif_rain', { durationMs: P.STORM_RAIN_MS, variant: 'downpour', strength: heat, clickSafe: true });
        breathOn(true);
      }
      /* burst / giant / veil / rain / flashes / tremor are event-driven: nothing to switch on */
    } else {
      on.delete(add);
      if (add === 'breath') whisperOut('pink');
      else if (add === 'scan') { if (!on.has('chroma')) stop('crt'); }
      else if (add === 'wheel') whisperOut('spiral');
      else if (add === 'subs') stop('sub_flash');
      else if (add === 'chroma') { stop('crt'); if (on.has('scan')) sustain('crt', { level: lerp(P.SCAN_LEVEL, heat), variant: 'scanline' }); }
      else if (add === 'storm') { stop('ambient_field'); stop('gif_rain'); if (on.has('breath')) breathOn(false); }
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
      /* the room going quiet is the AFTERMATH of the mistake, a hysteresis
         later - the lights going out one at a time, nothing pending */
      note('ic.rungFell', { kind: 'commiserate', n: r, streak: streak });
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
    if (on.has('wheel')) sustain('wash', { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true });
    if (on.has('chroma')) sustain('crt', { level: lerp(P.CHROMA_LEVEL, heat), variant: 'chroma' });
    else if (on.has('scan')) sustain('crt', { level: lerp(P.SCAN_LEVEL, heat), variant: 'scanline' });
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      stopped = false;
      lastPaintHeat = -1;
      /* a dev streak may already be set: show its rung now */
      if (wantRung > 0) setRung(wantRung);
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started && !stopped) { repaintHeat(); retune(); }
    },
    load(ev) {
      const e = ev || {};
      if (e.streak != null) setStreak(e.streak);
    },
    slide() { /* the tube owns the slide */ },
    reveal(ev) { const e = ev || {}; if (e.streak != null) setStreak(e.streak); },
    pop(ev) {
      const e = ev || {};
      const milestone = P.MILESTONES.indexOf(Number(e.streak) || 0) >= 0;
      setStreak(e.streak);
      doPunch(punchFor({ pts: e.pts, milestone }));
      if (!armed() || stopped) return;
      if (on.has('burst') || rung >= 2) {
        if (roll('burst') < lerp(P.BURST_CHANCE, heat)) {
          const hold = Math.round(lerp(P.BURST_HOLD_MS, heat));
          const went = fire('gif_burst', {
            count: Math.round(lerp(P.BURST_COUNT, heat)), clickSafe: true,
            sizePx: Math.round(vmin() * lerp(P.BURST_VMIN, heat)),
            holdMs: hold,
            variant: rung >= 8 ? 'scatter' : undefined,
          });
          if (went) flare(P.FLARE_BURST, hold + 300);
        }
      }
      if ((on.has('giant') || rung >= 4) && roll('giant') < lerp(P.GIANT_CHANCE, heat)) {
        /* ONE near-fullscreen gif on the landing; the engine's heat alpha and
           the clickSafe weld still rule it (it is decoration over a dimmed tube) */
        const hold = Math.round(lerp(P.GIANT_HOLD_MS, heat));
        const went = fire('gif_burst', {
          count: 1, clickSafe: true, x: 50, y: 50,
          sizePx: Math.round(vmin() * P.GIANT_VMIN),
          holdMs: hold,
          variant: 'single',
        });
        if (went) flare(P.FLARE_GIANT, hold + 400);
      }
      if (rung >= 4 && roll('veil') < lerp(P.VEIL_CHANCE, heat)) {
        const targets = [nodes.bgA, nodes.bgB].filter(Boolean);
        if (targets.length) {
          fire('glitch_swap', {
            targets,
            variant: P.VEIL_VARIANTS[Math.floor(roll('veil') * P.VEIL_VARIANTS.length)],
            seconds: lerp(P.VEIL_S, heat),
            sfx: false,
            onSwap() { /* presentation only: the backdrop keeps its own image */ },
          });
        }
      }
      if (rung >= 5 && roll('rain') < lerp(P.RAIN_CHANCE, heat)) {
        sustain('gif_rain', { durationMs: Math.round(lerp(P.RAIN_MS, heat)), clickSafe: true, strength: heat, variant: rung >= 8 ? 'downpour' : 'steady' });
      }
      if (on.has('flashes') || rung >= 7) fire('flash_burst', { count: P.STORM_FLASH_COUNT, clickSafe: true });
    },
    drift() { setStreak(0); },
    denyPass(ev) { const e = ev || {}; if (e.streak != null) setStreak(e.streak); /* restraint HOLDS the rung */ },
    denyHit() {
      doPunch(punchFor({ pts: 0, hit: true }));
      if (rung >= 4 && armed() && !stopped) {
        const targets = [nodes.bgA, nodes.bgB].filter(Boolean);
        if (targets.length) fire('glitch_swap', { targets, variant: 'datamosh', seconds: 0.5, sfx: false, onSwap() {} });
      }
      setStreak(0);
    },
    hover() { /* nothing to do with a hover here */ },
    /** index.js's per-pop effect beat: a 'flash' beat gets the flare too. */
    beat(ev) {
      const e = ev || {};
      if (e.flavor === 'flash' && rung >= 1) flare(P.FLARE_BURST, (Number(e.holdMs) || 900) + 300);
    },
    end() { api.stop(); },
    stop() {
      if (stopped) return;
      stopped = true;
      cancel(downTimer); downTimer = 0;
      /* everything sighs out: whisper the washes, stop the rest (fades are the engine's) */
      if (on.has('breath') || on.has('storm')) whisperOut('pink');
      if (on.has('wheel')) whisperOut('spiral');
      if (on.has('scan') || on.has('chroma')) stop('crt');
      if (on.has('subs')) stop('sub_flash');
      if (on.has('storm')) { stop('ambient_field'); stop('gif_rain'); }
      /* the dusk eases to its debrief level (never a snap back to a blazing tube) */
      cancel(flareTimer); flareTimer = 0; flareUntil = 0;
      try { if (nodes.dusk && nodes.dusk.style) nodes.dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
      setOpacity(nodes.dusk, rung > 0 ? Math.min(P.DUSK_END, duskFor(rung, heat)) : 0);
      on.clear();
      rung = 0;
      amp = 0; punch = { px: 0, until: 0, ms: 1 };
      haltLoop();
      setTranslate(nodes.tubewrap, 0, 0);
      setTranslate(nodes.hud, 0, 0);
    },
    pause() {
      paused = true;
      cancelAll(); downTimer = 0; flareTimer = 0; flareUntil = 0;
      try { if (nodes.dusk && nodes.dusk.style) nodes.dusk.style.transition = ''; } catch (e) { /* cosmetic */ }
      haltLoop();
      setTranslate(nodes.tubewrap, 0, 0);
      setTranslate(nodes.hud, 0, 0);
    },
    resume() {
      paused = false;
      if (!stopped) { retune(); setStreak(streak); }
    },
    destroy() {
      api.stop();
      destroyed = true;
      cancelAll();
      haltLoop();
      setOpacity(nodes.dusk, '');
      try { stop('wash'); } catch (e) { /* noop */ }
    },
    diagnostics() {
      return {
        armed: armed(), started, stopped, paused, heat: +heat.toFixed(3), streak, rung, wantRung,
        on: Array.from(on), tremorPx: +amp.toFixed(3), punchPx: +punch.px.toFixed(2), loopOn,
        dusk: stopped ? 0 : duskFor(rung, heat), flaring: flareUntil > nowMs(),
        fires: Object.assign({}, fires), liveTimers: live.size, nodes: 0,
      };
    },
  };
  return api;
}

export default createIcPressure;
