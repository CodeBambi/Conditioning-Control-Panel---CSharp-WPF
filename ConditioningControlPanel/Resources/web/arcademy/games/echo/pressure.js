/* ============================================================================
 * games/echo/pressure.js - the music room's effects ladder: THE SURGE. The
 * casino (casino.js) lights the room, the trickster (trickster.js) lies about
 * it; this file is what the room DOES to you as the echo gets LONGER. The
 * ladder is LENGTH-DRIVEN: the rung is the sequence length the class is
 * currently dealing (the game's own ladder - a fail sets the next deal back
 * two and the storm steps DOWN behind a hysteresis, with fades, never a snap);
 * the MAGNITUDE rides the class heat (which index.js folds length, tier cap
 * and the clean-press streak into). A 105-second class reaches length 8-12,
 * so the whole ladder fits inside a real class. Same seed, same surge.
 *
 * THE RUNG TABLE (sequence length -> rung -> what switches ON; cumulative):
 *   3-4   rung 0  clean room (the casino's halo and the core's own ambience)
 *   5     rung 1  BREATH      a pink wash breathes on a cadence          [engine wash:pink]
 *   6     rung 2  BURST       gif bursts ride cleared sequences (28-42   [engine gif_burst sizePx]
 *                             vmin) + THE TREMOR wakes on the chrome
 *   7     rung 3  WHEEL       the full-screen spiral wash wakes (the     [engine wash:spiral]
 *                             shell's real image through the provider)
 *   8     rung 4  VEIL+GIANT  the backdrop glitch-shudders on clears and  [engine glitch_swap on the
 *                             fails; a clear may drop ONE near-fullscreen  backdrop, gif_burst ~0.9vmin]
 *                             gif
 *   9     rung 5  SUBS        the sub-flash stream runs between turns    [engine sub_flash alias]
 *   10    rung 6  FLASHES     flash bursts ride clears                   [engine flash_burst]
 *   12    rung 7  STORM       embers, the pink flood, the tremor at its  [engine ambient_field, wash:pink]
 *                             cap
 *
 * THE TREMOR: the CHROME vibrates with the length, Balatro style - a
 * continuous low tremor whose amplitude grows with the rung, a PUNCH on every
 * cleared sequence sized by the length, a JOLT on a fail. Written on the CSS
 * individual `translate` of the HUD (the top-level chrome nodes index.js hands
 * in; a chip inside a trembling HUD is not moved twice), and NEVER on the
 * ring or a pad: the pads are the tap targets (Law II), and a memory test
 * cannot have moving targets. The chips themselves scale-punch (len on a
 * clear, streak on a long link) - transform only, the TEXT is index.js's. A
 * rAF loop ONLY while amplitude > 0; stopped by pause()/stop()/destroy(),
 * re-armed by resume(). Reduced motion / motionLevel 0 -> no tremor at all,
 * punches become a brief bloom on the chip.
 *
 * ENGINE vs GAME-LOCAL (audit): EVERYTHING visual goes through opts.engine
 * (index.js's null-safe weld: clickSafe forced, audio ceiling, capsOk,
 * effectsConsumed). Game-local: the tremor (translate writes on the chrome)
 * and the chip punches (transform writes on the chips). This file creates NO
 * nodes. Node budget: 0.
 *
 * SHARED KINDS (the core's own ambience rides the same engine): the core holds
 * ambient_field (specks / motes) for the class and pulses wash:pink itself at
 * tier 3; this deck therefore NEVER stop()s ambient_field (the storm's off
 * hand re-sustains the core's kind instead) and steps every wash DOWN by
 * re-triggering its variant at a whisper alpha (trap 33), never stop('wash').
 * bubble_field is the core's input-turn pressure and is not touched here.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event arguments (len / links / heat / beat
 *       kind), writes nothing about the sequence, the streak or the grade,
 *       renders no text; chip TEXT is never touched.
 *   II  input honest  - no node of ours exists; the ring and the pads are never
 *       transformed or covered; every engine fire is clickSafe by the weld.
 *   III never still   - from rung 1 the room breathes; from rung 2 it shakes.
 *   V   seeded        - per-tag mulberry32 off seed+'|ec-pressure|<tag>',
 *       append-only; no Math.random.
 *   VI  exits sacred  - capsOk() false = zero fires, zero tremor; reduced
 *       motion = no tremor, no veil; pause() halts the rAF and drops transient
 *       timers; destroy() restores every translate and whispers every wash out.
 *   VII lexicon       - this file renders no text.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const EC_PRESSURE = Object.freeze({
  /** sequence length thresholds per rung (index = rung). Monotone. */
  RUNG_LEN: Object.freeze([0, 5, 6, 7, 8, 9, 10, 12]),
  RUNG_ADDS: Object.freeze([
    Object.freeze([]),
    Object.freeze(['breath']),
    Object.freeze(['burst', 'tremor']),
    Object.freeze(['wheel']),
    Object.freeze(['veil', 'giant']),
    Object.freeze(['subs']),
    Object.freeze(['flashes']),
    Object.freeze(['storm']),
  ]),
  RUNG_CUE: Object.freeze([null, 'wash', 'burst', 'wash', 'glitch', 'whisper', 'burst', 'near_miss']),
  /** Stepping DOWN waits this long and fades; stepping UP is immediate. */
  HYST_MS: 1800,
  HEAT_REPAINT_STEP: 0.06,

  /* ---- THE TREMOR ------------------------------------------------------ */
  TREMOR_PX: Object.freeze([0, 0, 0.35, 0.5, 0.7, 0.95, 1.3, 1.8]),
  TREMOR_CAP_PX: 2.2,
  TREMOR_HEAT_FLOOR: 0.5,
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.4, 2.6]),
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  /** a clear punch: px / ms from q = len/14; a fail jolt; hard caps. */
  PUNCH_PX: Object.freeze([0.9, 3.4]),
  PUNCH_MS: Object.freeze([170, 380]),
  JOLT_PX: 4.2,
  JOLT_MS: 360,
  PUNCH_CAP_PX: 6,
  PUNCH_CAP_MS: 450,
  /** chip punches (scale) */
  LEN_PUNCH: Object.freeze([1.08, 1.26]),
  STREAK_PUNCH_PER_LINK: 0.025,
  STREAK_PUNCH_CAP: 1.22,
  CHIP_MS: 240,
  BLOOM_MS: 320,

  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([8200, 5000]),
  BREATH_HOLD_MS: 2400,
  BREATH_ALPHA: Object.freeze([0.1, 0.36]),
  STORM_BREATH_ALPHA: Object.freeze([0.28, 0.52]),
  BURST_CHANCE: Object.freeze([0.45, 0.9]),
  BURST_COUNT: Object.freeze([1, 3]),
  BURST_VMIN: Object.freeze([0.28, 0.42]),
  BURST_HOLD_MS: Object.freeze([750, 1250]),
  GIANT_CHANCE: Object.freeze([0.28, 0.6]),
  GIANT_VMIN: 0.9,
  GIANT_HOLD_MS: Object.freeze([1000, 1600]),
  VEIL_CHANCE: Object.freeze([0.35, 0.8]),
  VEIL_S: Object.freeze([0.35, 0.85]),
  VEIL_VARIANTS: Object.freeze(['rgbsplit', 'vhsroll', 'datamosh']),
  WHEEL_ALPHA: Object.freeze([0.18, 0.44]),
  SUB_VARIANT: 'scatter',
  FLASH_COUNT: 2,
  STORM_EMBERS: Object.freeze([0.3, 0.7]),
  WHISPER_ALPHA: 0.01,
  WHISPER_HOLD_MS: 900,
  /** cue level = min(AUDIO_CEIL[tier], base + rung * step). */
  CUE_LEVEL_BASE: 0.22,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
  /** the core's ambient kind by tier (re-sustained when the storm leaves) */
  AMBIENT_OF_TIER: Object.freeze({ 1: 'specks', 2: 'specks', 3: 'motes', 4: 'motes' }),
});

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }

/** sequence length -> rung (0..7). Pure, monotone, clamps garbage to 0. */
export function rungFor(len) {
  const l = Math.max(0, Math.floor(Number(len) || 0));
  const T = EC_PRESSURE.RUNG_LEN;
  let r = 0;
  for (let i = 0; i < T.length; i++) { if (l >= T[i]) r = i; else break; }
  return r;
}
/** idle tremor amplitude in px; 0 under reduced motion / motionLevel 0. Pure. */
export function tremorAmpPx(len, heat01, reducedMotion, motionLevel) {
  if (reducedMotion) return 0;
  const m = EC_PRESSURE.MOTION_MUL[Math.max(0, Math.min(2, motionLevel == null ? 2 : Math.round(Number(motionLevel) || 0)))];
  if (!m) return 0;
  const base = EC_PRESSURE.TREMOR_PX[rungFor(len)] || 0;
  if (base <= 0) return 0;
  const f = EC_PRESSURE.TREMOR_HEAT_FLOOR;
  return Math.min(EC_PRESSURE.TREMOR_CAP_PX, +(base * m * (f + (1 - f) * clamp01(heat01))).toFixed(3));
}
/** a punch {px, ms} from a cleared length, or a fail jolt. Pure. */
export function punchFor(o) {
  const d = o || {};
  const P = EC_PRESSURE;
  const q = clamp01((Number(d.len) || 0) / 14);
  let px = lerp(P.PUNCH_PX, q);
  let ms = lerp(P.PUNCH_MS, q);
  if (d.fail) { px = Math.max(px, P.JOLT_PX); ms = Math.max(ms, P.JOLT_MS); }
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
function setCls(n, cls, on) { try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } }
function restart(n, cls) {
  if (!n || !n.classList) return;
  try { n.classList.remove(cls); if (typeof n.offsetWidth === 'number') void n.offsetWidth; n.classList.add(cls); } catch (e) { /* noop */ }
}
function vmin() {
  try {
    if (typeof window !== 'undefined') {
      const w = Number(window.innerWidth) || 0; const h = Number(window.innerHeight) || 0;
      if (w && h) return Math.min(w, h);
    }
  } catch (e) { /* fall */ }
  return 800;
}
/** Of a list of nodes, those not contained by another in the list. */
function topLevel(nodes) {
  const list = (nodes || []).filter(Boolean);
  return list.filter((n) => !list.some((m) => m !== n && contains(m, n)));
}
function contains(a, b) {
  try { if (typeof a.contains === 'function') return a.contains(b); } catch (e) { /* fall */ }
  let p = null;
  try { p = b.parentNode; } catch (e) { p = null; }
  while (p) { if (p === a) return true; try { p = p.parentNode; } catch (e) { p = null; } }
  return false;
}

/**
 * @param {Object} o
 *   seed, gradeTier|tier, reduced, motionLevel, coarse (touch pointer - the
 *   perf diet; every seeded roll is still consumed), stage, ring, backdrop,
 *   chrome:[els] (the tremor rides the top-level ones), hud:{len,clock,streak,best},
 *   engine {fire, sustain, stop, channels}, assets {next}, timers {after,every,clear|cancel},
 *   capsOk (fn or bool), log
 */
export function createEcPressure(o) {
  const opts = o || {};
  const P = EC_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const coarse = !!opts.coarse;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const tier = Math.max(1, Math.min(4, Math.round(Number(opts.gradeTier != null ? opts.gradeTier : opts.tier) || 1)));
  const audioCeil = P.AUDIO_CEIL[tier] || P.AUDIO_CEIL[1];
  const eng = opts.engine || {};
  const hud = opts.hud || {};
  const armedBase = !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk === true;
  }
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();

  /* ---- timers ------------------------------------------------------------ */
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

  /* ---- seeded streams ---------------------------------------------------- */
  const seedBase = String(opts.seed || 'ec') + '|ec-pressure|';
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
    if (!armedBase || destroyed) return;
    count('stop:' + kind);
    if (typeof eng.stop !== 'function') return;
    try { eng.stop(kind); } catch (e) { /* ignore */ }
  }
  /* THE DECOUPLE (W2 sec 3, owner ruling). The rung ladder IS the class's own
   * streak, so its cue is not decoration: bgIntensity 0 takes the EFFECTS away
   * (fire()/sustain() stay behind armed()) and never the school's voice. The
   * count is kept identical to fire()'s so diagnostics do not move. */
  const sounds = () => armedBase && !destroyed && !stopped;
  function cue(name, r) {
    if (!name || !sounds()) return;
    count('audio_trigger');
    if (typeof eng.fire !== 'function') return;
    try {
      eng.fire('audio_trigger', { name, level: Math.min(audioCeil, P.CUE_LEVEL_BASE + P.CUE_LEVEL_STEP * r) });
    } catch (e) { /* a refused cue is not an error */ }
  }

  /* ---- state ------------------------------------------------------------ */
  let started = false; let stopped = false; let paused = false;
  let heat = 0; let lastPaintHeat = -1;
  let len = 0; let links = 0;
  let rung = 0; let wantRung = 0; let downTimer = 0;
  let bellOn = false;
  const on = new Set();
  let breathTimer = 0;

  /* the tremor + the juice */
  const chrome = topLevel(Array.isArray(opts.chrome) ? opts.chrome : []);
  const fq = {
    fx: lerp(P.TREMOR_HZ_FAST, roll('tremor')), fy: lerp(P.TREMOR_HZ_FAST, roll('tremor')),
    sx: lerp(P.TREMOR_HZ_SLOW, roll('tremor')), sy: lerp(P.TREMOR_HZ_SLOW, roll('tremor')),
    px: roll('tremor') * 6.283, py: roll('tremor') * 6.283, qx: roll('tremor') * 6.283, qy: roll('tremor') * 6.283,
  };
  let amp = 0;
  let punch = { px: 0, until: 0, ms: 1 };
  let rafId = 0; let loopOn = false; let pausedAt = 0;
  let translateWrites = 0; let translateOn = false;
  const chips = {
    len: { el: hud.len || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
    streak: { el: hud.streak || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
  };
  const chipBloomTimers = new Map();

  function writeTranslate(x, y) {
    for (const n of chrome) {
      if (!n || !n.style) continue;
      try { n.style.translate = x.toFixed(2) + 'px ' + y.toFixed(2) + 'px'; } catch (e) { /* ignore */ }
    }
    translateWrites += 1; translateOn = true;
  }
  function clearTranslate() {
    for (const n of chrome) {
      if (!n || !n.style) continue;
      try { n.style.translate = ''; if (typeof n.style.removeProperty === 'function') n.style.removeProperty('translate'); } catch (e) { /* ignore */ }
    }
    translateOn = false;
  }
  function writeChip(c, value) {
    if (!c.el || !c.el.style || c.last === value) return;
    try { c.el.style.transform = value; c.last = value; c.writes += 1; } catch (e) { /* ignore */ }
  }
  function needsLoop() {
    if (amp > 0.005) return true;
    if (punch.px > 0 && punch.until > nowMs()) return true;
    const t = nowMs();
    for (const k of Object.keys(chips)) { const c = chips[k]; if (c.a > 0 && t - c.at < c.ms) return true; }
    return false;
  }
  function armLoop() {
    if (loopOn || paused || destroyed || stopped || still) return;
    if (!needsLoop()) return;
    const r = rafFn();
    if (!r) return;
    loopOn = true;
    rafId = r(frame) || 1;
  }
  function haltLoop() {
    if (rafId) { const c = cafFn(); if (c) { try { c(rafId); } catch (e) { /* ignore */ } } }
    rafId = 0; loopOn = false;
  }
  function rest() {
    if (translateOn) clearTranslate();
    for (const k of Object.keys(chips)) writeChip(chips[k], '');
  }
  function frame() {
    rafId = 0;
    if (!loopOn || paused || destroyed) { loopOn = false; return; }
    step(nowMs());
    if (needsLoop()) { const r = rafFn(); if (r) { rafId = r(frame) || 1; return; } }
    loopOn = false;
    rest();
  }
  function step(t) {
    const s = t / 1000;
    let x = 0; let y = 0; let moving = false;
    if (amp > 0.005) {
      x = amp * (0.62 * Math.sin(6.283 * fq.fx * s + fq.px) + 0.38 * Math.sin(6.283 * fq.sx * s + fq.qx));
      y = amp * (0.62 * Math.sin(6.283 * fq.fy * s + fq.py) + 0.38 * Math.sin(6.283 * fq.sy * s + fq.qy));
      moving = true;
    }
    if (punch.px > 0) {
      const left = punch.until - t;
      if (left <= 0) { punch = { px: 0, until: 0, ms: 1 }; }
      else {
        const decay = clamp01(left / punch.ms);
        const a = punch.px * decay * decay;                  // quadratic ring-down
        x += (roll('jolt') * 2 - 1) * a;
        y += (roll('jolt') * 2 - 1) * a;
        moving = true;
      }
    }
    if (moving) writeTranslate(x, y);
    else if (translateOn) clearTranslate();
    for (const k of Object.keys(chips)) {
      const c = chips[k];
      if (!c.el) continue;
      let sc = 1;
      if (c.a > 0) {
        const p = (t - c.at) / c.ms;
        if (p >= 1) c.a = 0;
        else sc = 1 + c.a * (1 - p) * (1 - p) * Math.cos(p * Math.PI * 2.2);   // overshoot, dip, settle
      }
      writeChip(c, sc === 1 ? '' : 'scale(' + sc.toFixed(3) + ')');
    }
  }
  function doPunch(spec) {
    if (!armed() || stopped || !spec || spec.px <= 0) return;
    if (still) { bloomChip('len'); return; }
    const tNow = nowMs();
    const a = spec.px * P.MOTION_MUL[motion];
    punch = { px: Math.max(punch.px, a), until: Math.max(punch.until, tNow + spec.ms), ms: Math.max(spec.ms, 1) };
    count('punch');
    armLoop();
  }
  function punchChip(which, scale, ms) {
    const c = chips[which];
    if (!c || !c.el || !armed() || stopped) return;
    if (still) { bloomChip(which); return; }
    const a = Math.max(1, Number(scale) || 1) - 1;
    if (a <= 0.001) return;
    const t = nowMs();
    const left = c.a > 0 ? Math.max(0, 1 - (t - c.at) / c.ms) : 0;
    c.a = Math.max(a, c.a * left);
    c.at = t;
    c.ms = Math.max(120, Math.min(P.PUNCH_CAP_MS, Number(ms) || P.CHIP_MS));
    armLoop();
  }
  function bloomChip(which) {
    const c = chips[which];
    if (!c || !c.el) return;
    restart(c.el, 'g-ec-p-bloom');
    const old = chipBloomTimers.get(which);
    if (old) cancel(old);
    chipBloomTimers.set(which, after(P.BLOOM_MS, () => { chipBloomTimers.delete(which); setCls(c.el, 'g-ec-p-bloom', false); }));
  }

  /* ---- the rungs -------------------------------------------------------- */
  function retune() {
    amp = (stopped || !armed() || still) ? 0
      : Math.min(P.TREMOR_CAP_PX, (P.TREMOR_PX[rung] || 0) * P.MOTION_MUL[motion] * (P.TREMOR_HEAT_FLOOR + (1 - P.TREMOR_HEAT_FLOOR) * heat));
    if (amp > 0) armLoop();
  }
  function breathe(storm) {
    sustain('wash', { variant: 'pink', alpha: lerp(storm ? P.STORM_BREATH_ALPHA : P.BREATH_ALPHA, heat), holdMs: P.BREATH_HOLD_MS });
  }
  function armBreath() {
    if (breathTimer) cancel(breathTimer);
    const ms = lerp(P.BREATH_MS, heat) * (0.85 + roll('breath') * 0.3);
    breathTimer = after(Math.round(ms), () => { breathTimer = 0; if (on.has('breath') && !stopped) { breathe(on.has('storm')); armBreath(); } });
  }
  function whisperOut(variant) {
    /* step a wash DOWN: a whisper alpha + a short hold - it fades on its own
       deadline (never stop('wash'); the core may hold one too) */
    sustain('wash', { variant, alpha: P.WHISPER_ALPHA, holdMs: P.WHISPER_HOLD_MS });
  }
  /* On touch the held spiral runs at half strength: a sustained full-screen
   * wash is the single heaviest thing this ladder holds on a phone. No roll
   * is consumed here, so the scale is rng-neutral (Law V). */
  function wheel() { sustain('wash', { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat) * (coarse ? 0.5 : 1), sustainForever: true }); }
  function ambientBack() { sustain('ambient_field', { kind: P.AMBIENT_OF_TIER[tier] || 'specks' }); }
  function veil(variant, seconds) {
    if (still || !opts.backdrop) return null;
    return fire('glitch_swap', { targets: opts.backdrop, variant, seconds: seconds || 0.5, sfx: false, onSwap() { /* presentation only */ } });
  }
  function applyAdd(add, goingUp) {
    if (goingUp) {
      on.add(add);
      if (add === 'breath') { breathe(false); armBreath(); }
      else if (add === 'wheel') wheel();
      else if (add === 'subs') sustain('sub_flash', { variant: P.SUB_VARIANT });
      else if (add === 'storm') {
        sustain('ambient_field', { kind: 'embers', density: lerp(P.STORM_EMBERS, heat) });
        breathe(true);
        fire('flash_burst', { count: P.FLASH_COUNT, clickSafe: true });
      }
      /* burst / tremor / veil / giant / flashes are event-driven: nothing to switch on */
    } else {
      on.delete(add);
      if (add === 'breath') { if (breathTimer) { cancel(breathTimer); breathTimer = 0; } whisperOut('pink'); }
      else if (add === 'wheel') whisperOut('spiral');
      else if (add === 'subs') stopKind('sub_flash');
      else if (add === 'storm') { ambientBack(); if (on.has('breath')) breathe(false); }
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
    say('pressure: rung ' + rung + ' (' + (up ? 'up' : 'down') + ', len ' + len + ')');
  }
  function setLen(l) {
    len = Math.max(0, Math.floor(Number(l) || 0));
    wantRung = rungFor(len);
    if (!started || stopped) return;
    if (wantRung > rung) { cancel(downTimer); downTimer = 0; setRung(wantRung); }
    else if (wantRung < rung && !downTimer) {
      downTimer = after(P.HYST_MS, () => { downTimer = 0; if (wantRung < rung) setRung(wantRung); });
    } else if (wantRung === rung) { cancel(downTimer); downTimer = 0; }
  }
  function repaintHeat() {
    if (Math.abs(heat - lastPaintHeat) < P.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    /* On touch the held spiral is NOT re-mounted on every 0.06 heat move - it
     * keeps its arm-time alpha. The rung ladder still steps it down through
     * whisperOut (applyAdd / stop), which never rides this repaint. */
    if (on.has('wheel') && !coarse) wheel();
    if (on.has('storm')) sustain('ambient_field', { kind: 'embers', density: lerp(P.STORM_EMBERS, heat) });
  }

  /* ---- the riders (on a clear) ------------------------------------------ */
  function ridersOnClear(l) {
    if (!armed() || stopped) return;
    /* THE TOUCH DIET, under Law V: every roll below is consumed exactly as it
     * always was - compute-then-discard - and only the FIRE is gated or capped,
     * so a phone and a desktop walk identical rng streams from the same seed. */
    if (on.has('burst') && roll('burst') < lerp(P.BURST_CHANCE, heat)) {
      const sizePx = Math.round(vmin() * lerp(P.BURST_VMIN, heat));
      fire('gif_burst', {
        count: Math.round(lerp(P.BURST_COUNT, heat)), clickSafe: true,
        sizePx: coarse ? Math.min(sizePx, Math.round(vmin() * 0.28)) : sizePx,
        holdMs: Math.round(lerp(P.BURST_HOLD_MS, heat)),
      });
    }
    if (on.has('giant') && roll('giant') < lerp(P.GIANT_CHANCE, heat)) {
      /* a 0.9vmin decode is a whole-screen gif on a phone - the roll spends
       * its turn and the giant simply does not land there */
      if (!coarse) fire('gif_burst', { count: 1, clickSafe: true, x: 50, y: 50, sizePx: Math.round(vmin() * P.GIANT_VMIN), holdMs: Math.round(lerp(P.GIANT_HOLD_MS, heat)), variant: 'single' });
    }
    if (on.has('veil') && roll('veil') < lerp(P.VEIL_CHANCE, heat)) {
      const vIdx = Math.floor(roll('veil') * P.VEIL_VARIANTS.length);   // ALWAYS drawn
      if (!coarse) veil(P.VEIL_VARIANTS[vIdx], lerp(P.VEIL_S, heat));
    }
    if (on.has('flashes')) fire('flash_burst', { count: P.FLASH_COUNT, clickSafe: true });
    void l;
  }

  /* ---------------------------------------------------------------- api */
  const api = {
    start() {
      if (started) return;
      started = true;
      if (!armed()) { say('pressure: disarmed'); return; }
      setLen(len);
      retune();
      say('pressure: armed (' + chrome.length + ' chrome, tier ' + tier + ')');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (!started || stopped) return;
      retune();
      repaintHeat();
    },
    /** The rung: the sequence length the class is dealing. The second argument
     *  is index.js's additive context ({links, cleared, heat}). */
    setStreak(l, ctx2) {
      const c = ctx2 || {};
      if (c.links != null) links = Math.max(0, Math.floor(Number(c.links) || 0));
      if (c.heat != null) { heat = clamp01(c.heat); }
      setLen(l);
      if (started && !stopped) { retune(); repaintHeat(); }
    },
    /** index.js's beats: 'seam' (playback ended, the turn opens), 'clear',
     *  'fail', 'bell'; anything else is a small punch. */
    beat(kind) {
      if (!started || stopped) return;
      const k = String(kind || '');
      if (k === 'clear') {
        doPunch(punchFor({ len }));
        punchChip('len', lerp(P.LEN_PUNCH, clamp01(len / 14)), P.CHIP_MS + 60);
        if (links >= 3) punchChip('streak', Math.min(P.STREAK_PUNCH_CAP, 1 + P.STREAK_PUNCH_PER_LINK * Math.min(8, links)), 220);
        ridersOnClear(len);
      } else if (k === 'fail') {
        doPunch(punchFor({ len, fail: true }));
        if (on.has('veil')) veil('datamosh', 0.5);
      } else if (k === 'seam') {
        if (on.has('breath')) breathe(on.has('storm'));
      } else if (k === 'bell') {
        bellOn = true;
      } else {
        doPunch({ px: 0.8, ms: 160 });
      }
    },
    pause() {
      paused = true;
      pausedAt = nowMs();
      cancelAll(); downTimer = 0; breathTimer = 0;
      for (const [, id] of chipBloomTimers) cancel(id);
      chipBloomTimers.clear();
      haltLoop();
      rest();
    },
    resume() {
      if (!paused) return;
      paused = false;
      const gap = Math.max(0, nowMs() - pausedAt);
      if (punch.px > 0) punch.until += gap;
      for (const k of Object.keys(chips)) { if (chips[k].a > 0) chips[k].at += gap; }
      if (!stopped && started) { retune(); if (on.has('breath')) armBreath(); setLen(len); }
    },
    /** The class is over: everything sighs out (fades are the engine's), nothing punches again. */
    stop() {
      if (stopped) return;
      stopped = true;
      cancel(downTimer); downTimer = 0;
      if (breathTimer) { cancel(breathTimer); breathTimer = 0; }
      if (on.has('breath') || on.has('storm')) whisperOut('pink');
      if (on.has('wheel')) whisperOut('spiral');
      if (on.has('subs')) stopKind('sub_flash');
      if (on.has('storm')) ambientBack();
      on.clear();
      rung = 0;
      amp = 0; punch = { px: 0, until: 0, ms: 1 };
      for (const k of Object.keys(chips)) chips[k].a = 0;
      haltLoop();
      rest();
    },
    destroy() {
      if (destroyed) return;
      api.stop();
      destroyed = true;
      cancelAll();
      for (const [which, id] of chipBloomTimers) { cancel(id); const c = chips[which]; if (c) setCls(c.el, 'g-ec-p-bloom', false); }
      chipBloomTimers.clear();
      haltLoop();
      clearTranslate();
      for (const k of Object.keys(chips)) { const c = chips[k]; if (c.el) { setCls(c.el, 'g-ec-p-bloom', false); try { c.el.style.transform = ''; } catch (e) { /* ignore */ } } }
    },
    diagnostics() {
      return {
        armed: armed(), started, stopped, paused, bell: bellOn,
        rung, wantRung, hystPending: !!downTimer, len, links, heat: +heat.toFixed(3),
        features: Array.from(on),
        tremorPx: +amp.toFixed(3), punchPx: +punch.px.toFixed(2), punchLive: punch.px > 0 && punch.until > nowMs(),
        loop: loopOn, translateWrites, translateOn, chrome: chrome.length,
        chips: { len: { writes: chips.len.writes, last: chips.len.last }, streak: { writes: chips.streak.writes, last: chips.streak.last } },
        fires: Object.assign({}, fires),
        timers: live.size,
      };
    },
  };
  return api;
}

export default createEcPressure;
