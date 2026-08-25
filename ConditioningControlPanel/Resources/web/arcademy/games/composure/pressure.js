/* ============================================================================
 * games/composure/pressure.js - DECK IV of the House Rules for the studio: THE
 * SURGE. The casino lights the room, the trickster lies about the picture;
 * this file is what the room DOES to you as the picture comes together. The
 * ladder is PROGRESS-driven: the rung is the fraction of pieces sitting home
 * (index.js calls setProgress after its own accounting); a piece pulled back
 * out steps the storm DOWN behind a hysteresis (fades, never a snap).
 * Magnitude rides the class heat. Same seed, same surge.
 *
 * THE RUNG TABLE (locked fraction -> rung -> what switches ON; cumulative):
 *   < .15   rung 0  clean studio (the casino's lamp is all there is)
 *   .15     rung 1  BREATH      a pink wash breathes on a cadence         [engine wash:pink]
 *   .30     rung 2  BUBBLES     a slow bubble field drifts over the room  [engine bubble_field]
 *   .45     rung 3  DRIFT       the HUD chips sway + THE TREMOR wakes     [engine row_drift:sway]
 *                               + the haze comes up under the washes     [local .g-cp-p-haze]
 *   .60     rung 4  BURST       gif bursts ride locks                    [engine gif_burst]
 *   .75     rung 5  WHEEL       the spiral wash wakes (the class spiral) [engine wash:spiral]
 *                               + the easel shudders on locks            [engine glitch_swap]
 *   .90     rung 6  SUBS        sub flash stream + flash bursts on locks [engine sub_flash, flash_burst]
 *   solve   rung 7  ROYAL       the pink flood, three flash bursts, the  [engine wash:pink, flash_burst,
 *                               easel rolls, the ring goes gold           glitch_swap]
 * ZEN plays ONLY the lowest two rungs (breath + bubbles): no tremor, no
 * drift, no bursts, no wheel, no subs; the solve is a warm pink breath.
 *
 * THE TREMOR: the easel (.g-cp-frame) and the HUD vibrate from rung 3 - a
 * continuous low tremor whose amplitude grows with the rung, a PUNCH on every
 * lock sized by the streak, heavier on the solve. Extend-not-stack with a
 * quadratic ring-down (dtrh screenShake posture), written on the frame's and
 * the hud's individual CSS `translate` (nothing else transforms them) - NEVER
 * on a tile: the tiles are the tap targets and --r/--c is index.js's. A rAF
 * loop ONLY while something moves; pause()/stop()/destroy() halt it.
 *
 * THE HAZE (trap 35): the clip under the engine's screen-blended washes is
 * bright; a dimmer over the canvas (opacity by rung x heat, a FLARE under
 * every burst) is what puts the effects in FRONT without touching the
 * engine's ceilings. Opacity only. THE RING: a box-shadow bloom around the
 * canvas - the reduced-motion body of every punch, gold for the bell.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the numbers index.js hands it, writes nothing
 *       about moves, locks, time or grade; chip TEXT is never touched.
 *   II  input honest  - the frame's translate is sub-3px (the gesture surface
 *       is the whole tile); every node is pointer-events:none; no tile is
 *       transformed, covered or written.
 *   III never still   - from rung 1 the room breathes; from rung 3 it shakes.
 *   V   seeded        - per-tag mulberry32 off seed+'|cp-pressure|' (append-
 *       only tags); the engine's own rng is separate and fine.
 *   VI  exits sacred  - capsOk false (bgIntensity 0) = zero fires, zero
 *       tremor, no nodes; reduced motion / motionLevel 0 = no tremor (punches
 *       become the ring bloom), no shudder; every timer lives in the game's
 *       pause-aware registry AND a local set; pause() halts the loop.
 *   VII strings      - this file renders no text at all.
 *
 * NEVER: engine.stop('wash'). It blacks out EVERY wash kind at once (trap 33).
 * A wash is stepped down by re-triggering its variant at a whisper alpha with
 * a short hold (the engine's own transition carries it out) - including on
 * stop() and destroy(). THE WHEEL WEARS THE CLASS SPIRAL (2026-08-25, the Loom
 * directive): the wash is triggered with NO url, so the engine's own
 * spiralUrl() provider answers - the shell's woven Loom params (a live shader
 * canvas), a saved user-loom gif, or the bundled-gif floor. The private
 * SPIRALS pool that used to live here is gone; the echo/misdirection posture
 * is the pattern now.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const CP_PRESSURE = Object.freeze({
  /** locked fraction thresholds per rung (index = rung). Monotone. */
  RUNG_FRAC: Object.freeze([0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.01]),
  RUNG_MAX: 7,
  ZEN_RUNG_MAX: 2,
  LADDER: Object.freeze([
    Object.freeze({ rung: 0, adds: Object.freeze([]), cue: null }),
    Object.freeze({ rung: 1, adds: Object.freeze(['breath']), cue: 'wash' }),
    Object.freeze({ rung: 2, adds: Object.freeze(['bubbles']), cue: 'whisper' }),
    Object.freeze({ rung: 3, adds: Object.freeze(['drift', 'haze']), cue: 'whisper' }),
    Object.freeze({ rung: 4, adds: Object.freeze(['burst']), cue: 'burst' }),
    Object.freeze({ rung: 5, adds: Object.freeze(['wheel']), cue: 'wash' }),
    Object.freeze({ rung: 6, adds: Object.freeze(['subs']), cue: 'glitch' }),
    Object.freeze({ rung: 7, adds: Object.freeze(['royal']), cue: 'near_miss' }),
  ]),
  HYST_MS: 1800,
  HEAT_REPAINT_STEP: 0.08,
  /* ---- THE TREMOR ------------------------------------------------------ */
  TREMOR_RUNG: 3,
  TREMOR_PX: Object.freeze([0, 0, 0, 0.45, 0.7, 1.0, 1.5, 2.2]),
  TREMOR_CAP_PX: 3,
  TREMOR_HEAT_FLOOR: 0.5,
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.6, 2.8]),
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  HUD_MUL: 0.6,
  PUNCH_PX_BASE: 1.2,
  PUNCH_PX_STREAK: 0.5,
  PUNCH_PX_CAP: 7,
  PUNCH_MS_BASE: 200,
  PUNCH_MS_STREAK: 25,
  PUNCH_MS_CAP: 450,
  PUNCH_SLIDE_PX: 0.6,
  PUNCH_SLIDE_MS: 120,
  PUNCH_THRASH_PX: 2.2,
  PUNCH_THRASH_MS: 260,
  PUNCH_ROYAL_PX: 7,
  PUNCH_ROYAL_MS: 450,
  CHIP_PUNCH_SCALE: Object.freeze([1.06, 1.26]),
  BLOOM_MS: 320,
  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([8500, 5200]),
  BREATH_HOLD_MS: 2400,
  BREATH_ALPHA: Object.freeze([0.12, 0.4]),
  BUBBLE_MAX: Object.freeze([6, 14]),
  BUBBLE_ALPHA: Object.freeze([0.25, 0.5]),
  HAZE_A: Object.freeze([0.12, 0.3]),
  HAZE_FLARE: 0.6,
  BURST_CHANCE: Object.freeze([0.4, 0.9]),
  BURST_COUNT: Object.freeze([1, 3]),
  BURST_HOLD_MS: 900,
  WHEEL_ALPHA: Object.freeze([0.18, 0.42]),   // a shade under DE (.22-.55): the picture must stay legible under the wheel
  SHUDDER_CHANCE: Object.freeze([0.35, 0.8]),
  SHUDDER_S: 0.45,
  SUB_VARIANT: 'scatter',
  ROYAL_FLOOD_MS: 3200,
  ROYAL_FLOOD_A: 0.65,
  ZEN_SOLVE_A: 0.35,
  /** cue level = min(AUDIO_CEIL[gradeTier], base + rung * step). */
  CUE_LEVEL_BASE: 0.25,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
  NODE_BUDGET: 2,
});

const STYLE_ID = 'g-cp-pressure-style';
const STYLE_TEXT = `
/* ---- PRESSURE (House Rules, Deck IV) --------------------------------------- */
/* THE TREMOR is not here: pressure.js writes the frame's and the HUD's
   individual translate from one rAF loop, never a tile. What IS here: the haze
   (a dimmer over the canvas whose opacity climbs with the rung, so the engine's
   screen-blended washes read over a bright clip), the ring (the reduced-motion
   body of a punch) and the chip bloom. Every layer pointer-events:none. */
.g-cp-p-haze{position:absolute;inset:0;z-index:3;pointer-events:none;opacity:0;
  background:radial-gradient(80% 80% at 50% 50%, rgba(10,4,16,.2), rgba(10,4,16,.7) 100%);
  transition:opacity .8s ease}
.g-cp-p-haze.is-on{opacity:var(--cp-p-ha,.25)}
.g-cp-p-haze.is-flare{transition-duration:.08s}
.g-cp-p-ring{position:absolute;inset:-8px;z-index:4;pointer-events:none;display:block;border-radius:6px;opacity:0;
  transition:opacity .5s ease;--cp-p-rc:var(--pink);
  box-shadow:0 0 0 2px color-mix(in srgb, var(--cp-p-rc), transparent 30%), 0 0 34px color-mix(in srgb, var(--cp-p-rc), transparent 45%),
    inset 0 0 40px color-mix(in srgb, var(--cp-p-rc), transparent 60%)}
.g-cp-p-ring.is-hit{opacity:.75;transition-duration:.04s}
.g-cp-p-ring.is-deep{opacity:1;
  box-shadow:0 0 0 3px color-mix(in srgb, var(--cp-p-rc), transparent 15%), 0 0 60px color-mix(in srgb, var(--cp-p-rc), transparent 30%),
    inset 0 0 70px color-mix(in srgb, var(--cp-p-rc), transparent 45%)}
.g-cp-p-ring.is-gold{--cp-p-rc:var(--gold)}
.g-cp-chip.g-cp-p-bloom{transition-duration:.3s,.3s,.05s;
  box-shadow:0 0 0 1px color-mix(in srgb, var(--pink), transparent 20%), 0 0 22px color-mix(in srgb, var(--pink), transparent 35%)}
/* reduced motion (both gates) */
html.arc-reduced .g-cp-p-haze.is-on{opacity:calc(var(--cp-p-ha,.25) * .7)}
@media (prefers-reduced-motion: reduce){
  .g-cp-p-haze.is-on{opacity:calc(var(--cp-p-ha,.25) * .7)}
}
`;
function ensureStyle() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return false;
    if (document.getElementById && document.getElementById(STYLE_ID)) return true;
    const tag = document.createElement('style');
    tag.id = STYLE_ID;
    tag.textContent = STYLE_TEXT;
    const host = document.head || document.documentElement || document.body;
    if (!host || !host.appendChild) return false;
    host.appendChild(tag);
    if (document._register) document._register(STYLE_ID, tag);
    return true;
  } catch (e) { return false; }
}

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }

/** locked fraction (0..1) -> rung (0..6; 7 is the solve, entered by beat). Pure. */
export function rungFor(frac, zen) {
  const f = clamp01(frac);
  let r = 0;
  const T = CP_PRESSURE.RUNG_FRAC;
  for (let i = 1; i < T.length; i++) { if (f >= T[i]) r = i; }
  r = Math.min(r, CP_PRESSURE.RUNG_MAX - 1);
  if (zen) r = Math.min(r, CP_PRESSURE.ZEN_RUNG_MAX);
  return r;
}
/** The idle tremor amplitude in px. 0 under reduced motion; heat scales it. Pure. */
export function tremorAmpPx(rung, heat01, reducedMotion) {
  if (reducedMotion) return 0;
  const base = CP_PRESSURE.TREMOR_PX[Math.max(0, Math.min(CP_PRESSURE.RUNG_MAX, Math.round(Number(rung) || 0)))] || 0;
  if (base <= 0) return 0;
  const f = CP_PRESSURE.TREMOR_HEAT_FLOOR;
  const amp = base * (f + (1 - f) * clamp01(heat01));
  return Math.min(CP_PRESSURE.TREMOR_CAP_PX, +amp.toFixed(3));
}
/** A lock punch: { px, ms, scale } from the streak. Pure, capped. */
export function punchFor(streak) {
  const s = Math.max(0, Math.min(8, Number(streak) || 0));
  const P = CP_PRESSURE;
  const px = Math.min(P.PUNCH_PX_CAP, P.PUNCH_PX_BASE + P.PUNCH_PX_STREAK * s);
  const ms = Math.min(P.PUNCH_MS_CAP, Math.round(P.PUNCH_MS_BASE + P.PUNCH_MS_STREAK * s));
  const scale = +lerp(P.CHIP_PUNCH_SCALE, s / 8).toFixed(3);
  return { px: +px.toFixed(2), ms, scale };
}

function el(tag, cls) {
  try { const n = document.createElement(tag); if (cls) n.className = cls; return n; } catch (e) { return null; }
}
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
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

/**
 * @param {Object} o
 *   seed, gradeTier, reduced, motionLevel, stage, frame, board,
 *   hud:{moves,clock,locked,calm} (elements), engine:{fire,sustain,stop,channels},
 *   assets:{next(kind)}, timers:{after,every,clear}, capsOk:()=>bool|boolean,
 *   mode?: 'timed'|'zen' (else read off stage data-mode), log
 */
export function createCpPressure(o) {
  const opts = o || {};
  const P = CP_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const hud = opts.hud || {};
  const eng = opts.engine || {};
  const frame = opts.frame || (opts.board && opts.board.parentNode) || null;
  const armedBase = !!opts.stage && !!frame && !!opts.timers && typeof opts.timers.after === 'function'
    && typeof document !== 'undefined';
  let mode = String(opts.mode || '').toLowerCase();
  if (mode !== 'zen' && mode !== 'timed') {
    try { mode = String(opts.stage.getAttribute('data-mode') || 'timed').toLowerCase() === 'zen' ? 'zen' : 'timed'; } catch (e) { mode = 'timed'; }
  }
  const zen = mode === 'zen';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk === true;
  }
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();
  /* THE bgIntensity DECOUPLE (W2). capsOk false is the player's VISUAL exit
   * (Law VI): zero fires, zero sustains, zero tremor, exactly as before - and
   * that stays true, because every visual in this file already gates on
   * armed() or on a node mount() never made. What must NOT die with the
   * lights is the SURGE's own voice, so the rung cue gates on sounds(). */
  const sounds = () => armedBase && !destroyed;

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

  /* ---- seeded streams ---------------------------------------------------- */
  const seedBase = String(opts.seed || 'cp') + '|cp-pressure|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ---------------------------------------------- */
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
  /** stop() for kinds that are NOT wash (trap 33: wash is never stopped). */
  function stopKind(kind) {
    if (!armedBase || destroyed || kind === 'wash') return;
    count('stop:' + kind);
    if (typeof eng.stop !== 'function') return;
    try { eng.stop(kind); } catch (e) { /* ignore */ }
  }
  function cueAt(name, level, extra) {
    if (!name || !sounds()) return;
    /* deliberately NOT through fire(): that road gates on armed(), and the
     * whole point of the decouple is that a cue does not (W2) */
    count('audio_trigger');
    if (typeof eng.fire !== 'function') return;
    const lv = Math.min(audioCeil, level) * (zen ? 0.6 : 1);
    try { eng.fire('audio_trigger', Object.assign({ name, level: lv }, extra || {})); }
    catch (e) { /* a refused cue is not an error */ }
  }
  function cue(name, rung) { cueAt(name, P.CUE_LEVEL_BASE + P.CUE_LEVEL_STEP * rung); }

  /* ---- state -------------------------------------------------------------- */
  let started = false; let stopped = false; let paused = false;
  let heat = 0; let lastPaintHeat = -1;
  let frac = 0; let rung = 0;
  let hystTimer = 0; let hystTarget = -1;
  let bellOn = false; let royalOn = false; let outOn = false;
  let streak = 0;
  const present = new Set();
  /* NO PRIVATE SPIRAL (2026-08-25): the wheel wash carries no url, so the
   * engine's spiralUrl() provider answers with the class spiral - the shell's
   * woven Loom, a saved user gif, or the bundled floor. One picker, one truth.
   * (roll('spiral') is gone with it; tags are independent streams, so no other
   * roll moved.) */
  let haze = null; let ring = null;
  let breathTimer = 0; let ringTimer = 0; let flareTimer = 0;
  const chipBloomTimers = new Map();

  /* the tremor */
  const fq = {
    fx: lerp(P.TREMOR_HZ_FAST, roll('tremor-f')), fy: lerp(P.TREMOR_HZ_FAST, roll('tremor-f')),
    sx: lerp(P.TREMOR_HZ_SLOW, roll('tremor-f')), sy: lerp(P.TREMOR_HZ_SLOW, roll('tremor-f')),
    px: roll('tremor-f') * 6.283, py: roll('tremor-f') * 6.283, qx: roll('tremor-f') * 6.283, qy: roll('tremor-f') * 6.283,
  };
  let tremorAmp = 0;
  let punchPeak = 0; let punchEnd = 0; let punchSpan = 1;
  let translateWrites = 0; let translateOn = false;
  let rafId = 0; let loopOn = false; let pausedAt = 0;
  const chips = {
    moves: { el: hud.moves || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
    locked: { el: hud.locked || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
    calm: { el: hud.calm || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
  };
  const hudEl = (hud.moves && hud.moves.parentNode) || null;

  /* ---- helpers ----------------------------------------------------------- */
  const has = (k) => present.has(k);
  function setVar(node, k, v) { if (!node || !node.style) return; try { node.style.setProperty(k, v); } catch (e) { /* ignore */ } }
  function cls(node, name, on) {
    if (!node || !node.classList) return;
    try { if (on) node.classList.add(name); else node.classList.remove(name); } catch (e) { /* ignore */ }
  }
  function restart(node, name) {
    if (!node || !node.classList) return;
    try { node.classList.remove(name); if (typeof node.offsetWidth === 'number') void node.offsetWidth; node.classList.add(name); } catch (e) { /* ignore */ }
  }
  function pctOf(node) {
    const r = rectOf(node);
    let w = 0; let h = 0;
    try { w = typeof window !== 'undefined' ? Number(window.innerWidth) || 0 : 0; h = typeof window !== 'undefined' ? Number(window.innerHeight) || 0 : 0; } catch (e) { /* none */ }
    if (!r || !r.width || !w || !h) return null;
    return { x: Math.round((r.left + r.width / 2) / w * 100), y: Math.round((r.top + r.height / 2) / h * 100), size: Math.round(r.width) };
  }
  function hudChips() {
    const list = [];
    for (const k of Object.keys(chips)) if (chips[k].el) list.push(chips[k].el);
    if (hud.clock) list.push(hud.clock);
    return list;
  }

  /* ---- mounting ---------------------------------------------------------- */
  function mount() {
    if (haze || !frame || !frame.appendChild) return;
    haze = el('div', 'g-cp-p-haze');
    if (haze) frame.appendChild(haze);
    ring = el('i', 'g-cp-p-ring');
    if (ring) frame.appendChild(ring);
    // no spiral preload: the engine owns the class spiral (and the Loom path
    // renders live - there is nothing to fetch ahead of the first wheel)
  }

  /* ---- the loop ------------------------------------------------------------ */
  function needsLoop() {
    if (tremorAmp > 0.005) return true;
    if (punchPeak > 0) return true;
    const t = nowMs();
    for (const k of Object.keys(chips)) { const c = chips[k]; if (c.a > 0 && t - c.at < c.ms) return true; }
    return false;
  }
  function armLoop() {
    if (loopOn || paused || destroyed || stopped) return;
    if (!needsLoop()) return;
    const r = rafFn();
    if (!r) return;
    loopOn = true;
    rafId = r(frameFn) || 1;
  }
  function haltLoop() {
    if (rafId) { const c = cafFn(); if (c) { try { c(rafId); } catch (e) { /* ignore */ } } }
    rafId = 0; loopOn = false;
  }
  function frameFn() {
    rafId = 0;
    if (!loopOn || paused || destroyed) { loopOn = false; return; }
    step(nowMs());
    if (needsLoop()) { const r = rafFn(); if (r) { rafId = r(frameFn) || 1; return; } }
    loopOn = false;
    rest();
  }
  function writeTranslate(x, y) {
    try { if (frame && frame.style) { frame.style.translate = x.toFixed(2) + 'px ' + y.toFixed(2) + 'px'; translateWrites += 1; translateOn = true; } } catch (e) { /* ignore */ }
    try { if (hudEl && hudEl.style) hudEl.style.translate = (x * P.HUD_MUL).toFixed(2) + 'px ' + (y * P.HUD_MUL).toFixed(2) + 'px'; } catch (e) { /* ignore */ }
  }
  function clearTranslate() {
    for (const n of [frame, hudEl]) {
      if (!n || !n.style) continue;
      try { n.style.translate = ''; if (typeof n.style.removeProperty === 'function') n.style.removeProperty('translate'); } catch (e) { /* ignore */ }
    }
    translateOn = false;
  }
  function writeChip(c, value) {
    if (!c.el || !c.el.style || c.last === value) return;
    try { c.el.style.transform = value; c.last = value; c.writes += 1; } catch (e) { /* ignore */ }
  }
  function rest() {
    if (translateOn) clearTranslate();
    for (const k of Object.keys(chips)) writeChip(chips[k], '');
  }
  function step(t) {
    const s = t / 1000;
    let x = 0; let y = 0; let moving = false;
    if (tremorAmp > 0.005) {
      x = tremorAmp * (0.62 * Math.sin(6.283 * fq.fx * s + fq.px) + 0.38 * Math.sin(6.283 * fq.sx * s + fq.qx));
      y = tremorAmp * (0.62 * Math.sin(6.283 * fq.fy * s + fq.py) + 0.38 * Math.sin(6.283 * fq.sy * s + fq.qy));
      moving = true;
    }
    if (punchPeak > 0) {
      const left = punchEnd - t;
      if (left <= 0) { punchPeak = 0; punchEnd = 0; }
      else {
        const decay = clamp01(left / punchSpan);
        const a = punchPeak * decay * decay;
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
        else sc = 1 + c.a * (1 - p) * (1 - p) * Math.cos(p * Math.PI * 2.2);
      }
      if (sc === 1) writeChip(c, '');
      else writeChip(c, 'scale(' + sc.toFixed(3) + ')');
    }
  }
  function punchFrame(px, ms) {
    if (!armed() || stopped) return;
    if (still || zen) { bloomRing(false); return; }
    const a = Math.min(P.PUNCH_PX_CAP, Math.max(0, Number(px) || 0)) * P.MOTION_MUL[motion];
    if (a < 0.1) return;
    const t = nowMs();
    const dur = Math.max(60, Math.min(P.PUNCH_MS_CAP, Number(ms) || 260));
    const left = Math.max(0, punchEnd - t);
    const decay = punchSpan > 0 ? clamp01(left / punchSpan) : 0;
    punchPeak = Math.max(a, punchPeak * decay);
    punchEnd = Math.max(punchEnd, t + dur);
    punchSpan = Math.max(dur, punchEnd - t);
    armLoop();
  }
  function punchChip(which, scale, ms) {
    const c = chips[which];
    if (!c || !c.el || !armed() || stopped) return;
    if (still) { bloomChip(which); return; }
    const a = Math.min(1.3, Math.max(1, Number(scale) || 1)) - 1;
    if (a <= 0.001) return;
    const t = nowMs();
    const left = c.a > 0 ? Math.max(0, 1 - (t - c.at) / c.ms) : 0;
    c.a = Math.max(a, c.a * left);
    c.at = t;
    c.ms = Math.max(120, Math.min(P.PUNCH_MS_CAP, Number(ms) || 240));
    armLoop();
  }
  function bloomChip(which) {
    const c = chips[which];
    if (!c || !c.el) return;
    restart(c.el, 'g-cp-p-bloom');
    const old = chipBloomTimers.get(which);
    if (old) cancel(old);
    chipBloomTimers.set(which, after(P.BLOOM_MS, () => { chipBloomTimers.delete(which); cls(c.el, 'g-cp-p-bloom', false); }));
  }
  function bloomRing(deep) {
    if (!ring) return;
    cls(ring, 'is-deep', !!deep);
    restart(ring, 'is-hit');
    if (ringTimer) cancel(ringTimer);
    ringTimer = after(deep ? P.BLOOM_MS * 2 : P.BLOOM_MS, () => { ringTimer = 0; cls(ring, 'is-hit', false); cls(ring, 'is-deep', false); });
  }
  /** THE FLARE: the haze snaps up under a burst for its hold, eases back. */
  function flare(ms) {
    if (!haze || !has('haze')) return;
    cls(haze, 'is-flare', true);
    setVar(haze, '--cp-p-ha', P.HAZE_FLARE.toFixed(2));
    if (flareTimer) cancel(flareTimer);
    flareTimer = after(Math.max(200, ms | 0), () => { flareTimer = 0; cls(haze, 'is-flare', false); setVar(haze, '--cp-p-ha', lerp(P.HAZE_A, heat).toFixed(2)); });
  }

  /* ---- the rungs ----------------------------------------------------------- */
  function retune() {
    const base = tremorAmpPx(rung, heat, reduced) * P.MOTION_MUL[motion];
    tremorAmp = (stopped || outOn || zen || !armed()) ? 0 : Math.min(P.TREMOR_CAP_PX, base);
    if (haze && !flareTimer) setVar(haze, '--cp-p-ha', lerp(P.HAZE_A, heat).toFixed(2));
    if (Math.abs(heat - lastPaintHeat) >= P.HEAT_REPAINT_STEP) {
      if (has('wheel')) wheelWash();
      lastPaintHeat = heat;
    }
    if (tremorAmp > 0) armLoop();
  }
  function breathe() { sustain('wash', { variant: 'pink', alpha: lerp(P.BREATH_ALPHA, heat) * (zen ? 0.7 : 1), holdMs: P.BREATH_HOLD_MS }); }
  function armBreath() {
    if (breathTimer) cancel(breathTimer);
    const ms = lerp(P.BREATH_MS, heat) * (zen ? 1.5 : 1) * (0.85 + roll('breath') * 0.3);
    breathTimer = after(Math.round(ms), () => { breathTimer = 0; if (has('breath') && !stopped) { breathe(); armBreath(); } });
  }
  /** No url: the engine's spiralUrl() provider supplies the class spiral
   *  (echo/misdirection posture; since 2026-08-25 that is the woven Loom). */
  function wheelWash() { sustain('wash', { variant: 'spiral', alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true }); }
  /** NEVER stop('wash'): a whisper alpha + a short hold lets the engine's own transition carry it out. */
  function fadeWash(variant, extra) { sustain('wash', Object.assign({ variant, alpha: 0.01, holdMs: 120 }, extra || {})); }
  function frameRoll(seconds) {
    if (still || !frame) return null;
    return fire('glitch_swap', { targets: frame, variant: 'vhsroll', seconds: seconds || 0.5, onSwap() {}, sfx: false });
  }
  function burstAt(node, n) {
    const at = pctOf(node);
    const o2 = { count: Math.max(1, n | 0), holdMs: P.BURST_HOLD_MS, clickSafe: true, clickable: false };
    if (at) { o2.x = at.x; o2.y = at.y; o2.sizePx = Math.max(90, Math.round(at.size * 0.9)); }
    flare(P.BURST_HOLD_MS);
    return fire('gif_burst', o2);
  }
  function flashes(n, holdMs) {
    flare(holdMs || 700);
    return fire('flash_burst', { count: Math.max(1, n | 0), holdMs: holdMs || 700, clickSafe: true, clickable: false });
  }

  const FEATURES = {
    breath: { on() { breathe(); armBreath(); }, off() { if (breathTimer) { cancel(breathTimer); breathTimer = 0; } } },
    bubbles: {
      on() { sustain('bubble_field', { max: Math.round(lerp(P.BUBBLE_MAX, heat)), alpha: lerp(P.BUBBLE_ALPHA, heat), clickSafe: true, variant: 'drift' }); },
      off() { stopKind('bubble_field'); },
    },
    drift: {
      on() { const targets = hudChips(); if (targets.length) sustain('row_drift', { targets, axis: 'y', variant: 'sway', amplitudeMult: 0.6 }); },
      off() { stopKind('row_drift'); },
    },
    haze: { on() { cls(haze, 'is-on', true); }, off() { cls(haze, 'is-on', false); } },
    burst: { on() { burstAt(null, 1); }, off() {} },
    wheel: { on() { wheelWash(); }, off() { fadeWash('spiral'); } },
    subs: { on() { sustain('sub_flash', { variant: P.SUB_VARIANT }); flashes(1, 600); }, off() { stopKind('sub_flash'); } },
    royal: {
      on() {
        sustain('wash', { variant: 'pink', alpha: P.ROYAL_FLOOD_A, holdMs: P.ROYAL_FLOOD_MS });
        frameRoll(0.9);
        flashes(3, 900);
        cls(ring, 'is-gold', true);
      },
      off() { if (!bellOn) cls(ring, 'is-gold', false); },
    },
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
    retune();
    say('pressure: rung ' + from + ' -> ' + r + ' (frac ' + frac.toFixed(2) + ') on: ' + Array.from(present).join(','));
  }
  function descend(r, quiet) {
    if (r >= rung) return;
    const from = rung;
    for (let k = from; k > r; k--) leaveRung(k, r);
    rung = r;
    /* W3 P1-7: the ladder only ever announced its climb. Coming down is
     * relief and sounds like it - ONE cue per descend call however many rungs
     * were shed, half the climb's level and pitched under it. The teardown
     * walk to zero passes `quiet`: the bell owns that beat. */
    if (!quiet) cueAt('slide', (P.CUE_LEVEL_BASE + P.CUE_LEVEL_STEP * r) / 2, { pitch: 0.8 });
    retune();
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
  function everythingOff() {
    cancelHyst();
    descend(0, true);
    if (ringTimer) { cancel(ringTimer); ringTimer = 0; }
    if (flareTimer) { cancel(flareTimer); flareTimer = 0; }
    cls(ring, 'is-hit', false); cls(ring, 'is-deep', false);
    cls(haze, 'is-on', false); cls(haze, 'is-flare', false);
    for (const [which, id] of chipBloomTimers) { cancel(id); const c = chips[which]; if (c) cls(c.el, 'g-cp-p-bloom', false); }
    chipBloomTimers.clear();
    punchPeak = 0; punchEnd = 0;
    for (const k of Object.keys(chips)) chips[k].a = 0;
  }

  /* ---- api ----------------------------------------------------------------- */
  const api = {
    start() {
      if (started) return;
      started = true;
      /* DISARMED = DARK, NOT MUTE (W2): no sheet, no haze, no ring - but
       * started stays true, so the ladder still walks and still speaks. */
      if (!armed()) { say('pressure: dark (bgIntensity 0) - cue road only'); return; }
      ensureStyle();
      mount();
      retune();
      say('pressure: mounted ' + [haze, ring].filter(Boolean).length + ' layers, wheel = class spiral' + (zen ? ' (zen: rungs 0-2 only)' : ''));
    },
    setHeat(h) {
      heat = clamp01(h);
      if (!started) return;
      retune();
    },
    /** Presence rides the locked fraction. index.js calls after every lock/unlock. */
    setProgress(lockedFrac) {
      frac = clamp01(lockedFrac);
      if (!started || stopped || royalOn) return;
      stepTo(rungFor(frac, zen), false);
      retune();
    },
    /**
     * THE BANK (multi-board, 2026-08-24). A solve no longer ends the class: it
     * banks the picture and a fresh scramble deals, so the ROYAL is a beat and
     * has to come back down. This is NOT optional polish - `setProgress` above
     * early-returns while `royalOn`, so without this the whole CCP-effects
     * ladder would freeze at RUNG_MAX from the first bank to the bell.
     */
    deal() {
      if (!started || stopped) return;
      royalOn = false;
      streak = 0;
      cls(ring, 'is-gold', bellOn);
      stepTo(rungFor(frac, zen), false);
      retune();
    },
    /**
     * The class's beats: 'slide' | 'lock' | 'thrash' | 'assist' | 'solved' |
     * 'bell' | 'wash' | 'unwash' (+ {streak?, tileEl?} as a second arg). Unknown
     * kinds are ignored.
     */
    beat(kind, info) {
      /* no armed() here by design: punchFrame/punchChip gate on armed(), every
       * fire/sustain gates on armed(), and haze/ring are null when the deck is
       * dark - so what survives a capped bgIntensity is exactly the sound (W2) */
      if (!started || stopped) return;
      const k = String(kind || '');
      const d = info || {};
      if (k === 'slide') {
        streak = Math.max(0, Number(d.streak) || streak);
        if (rung >= P.TREMOR_RUNG) punchFrame(P.PUNCH_SLIDE_PX, P.PUNCH_SLIDE_MS);
        if (has('wheel') && roll('slide-g') < lerp(P.SHUDDER_CHANCE, heat) * 0.4) frameRoll(0.35);
      } else if (k === 'lock') {
        streak = Number.isFinite(Number(d.streak)) ? Math.max(0, Number(d.streak)) : streak + 1;
        const p = punchFor(streak);
        punchFrame(p.px, p.ms);
        punchChip('locked', p.scale, p.ms + 60);
        if (has('burst') && roll('burst') < lerp(P.BURST_CHANCE, heat)) burstAt(d.tileEl || opts.board, Math.round(lerp(P.BURST_COUNT, heat)));
        if (has('wheel') && roll('shudder') < lerp(P.SHUDDER_CHANCE, heat)) frameRoll(P.SHUDDER_S);
        if (has('subs')) flashes(1 + (streak >= 3 ? 1 : 0), 700);
        if (still || zen) bloomRing(false);
      } else if (k === 'thrash') {
        streak = 0;
        punchFrame(P.PUNCH_THRASH_PX, P.PUNCH_THRASH_MS);
        punchChip('calm', 1.12, 260);
        if (has('wheel')) frameRoll(0.4);
      } else if (k === 'assist') {
        streak = 0;
        punchChip('calm', 1.08, 240);
      } else if (k === 'solved') {
        royalOn = true;
        if (zen) {
          sustain('wash', { variant: 'pink', alpha: P.ZEN_SOLVE_A, holdMs: P.ROYAL_FLOOD_MS });
          bloomRing(true);
          cue('wash', 2);
        } else {
          cancelHyst();
          climb(P.RUNG_MAX);
          punchFrame(P.PUNCH_ROYAL_PX, P.PUNCH_ROYAL_MS);
          punchChip('locked', 1.3, 420);
          punchChip('calm', 1.3, 420);
          bloomRing(true);
        }
      } else if (k === 'bell') {
        bellOn = d.on !== false;
        cls(ring, 'is-gold', bellOn || royalOn);
      } else if (k === 'wash') {
        flare(Number(d.ms) || 1200);
      } else if (k === 'unwash') {
        if (flareTimer) { cancel(flareTimer); flareTimer = 0; cls(haze, 'is-flare', false); setVar(haze, '--cp-p-ha', lerp(P.HAZE_A, heat).toFixed(2)); }
      }
    },
    /** The bell took the board: everything sighs out. */
    dimOut() {
      outOn = true; bellOn = false; royalOn = false;
      if (!started) return;
      everythingOff();
      cls(ring, 'is-gold', false);
      retune();
    },
    pause() {
      if (paused) return;
      paused = true; pausedAt = nowMs();
      haltLoop();
    },
    resume() {
      if (!paused) return;
      paused = false;
      const gap = Math.max(0, nowMs() - pausedAt);
      if (punchPeak > 0) punchEnd += gap;
      for (const k of Object.keys(chips)) { if (chips[k].a > 0) chips[k].at += gap; }
      armLoop();
    },
    /** The class is over: fade everything, never snap; nothing may punch again. */
    stop() {
      if (stopped) return;
      stopped = true;
      if (started) everythingOff();
      tremorAmp = 0;
      haltLoop();
      rest();
    },
    destroy() {
      if (destroyed) return;
      api.stop();
      destroyed = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      haltLoop();
      clearTranslate();
      for (const k of Object.keys(chips)) { const c = chips[k]; if (c.el) { cls(c.el, 'g-cp-p-bloom', false); try { c.el.style.transform = ''; } catch (e) { /* ignore */ } } }
      for (const node of [haze, ring]) { if (node) { try { node.remove(); } catch (e) { /* ignore */ } } }
      haze = null; ring = null;
      present.clear();
    },
    diagnostics() {
      return {
        armed: armed(), sounds: sounds(), started, stopped, paused, zen, mode, out: outOn, bell: bellOn, royal: royalOn,
        rung, hystPending: !!hystTimer, hystTarget, frac, heat, streak,
        features: Array.from(present),
        tremorPx: +tremorAmp.toFixed(3), punchPx: +punchPeak.toFixed(2), punchLive: punchPeak > 0,
        loop: loopOn, translateWrites, translateOn,
        chips: { moves: { writes: chips.moves.writes }, locked: { writes: chips.locked.writes }, calm: { writes: chips.calm.writes } },
        liveNodes: [haze, ring].filter(Boolean).length,
        hazeOn: !!(haze && haze.classList && haze.classList.contains('is-on')),
        /* keys kept for rig continuity; the value is the engine's since the
         * Loom directive - this deck no longer holds a spiral of its own */
        spiral: 'class', spiralUrl: null,
        fires: Object.assign({}, fires), timers: live.size,
      };
    },
  };
  return api;
}

export default createCpPressure;
