/* ============================================================================
 * games/the-deep-end/pressure.js - DECK IV of the House Rules for the pool:
 * THE PRESSURE. The casino lights the water, the trickster lies about it; this
 * file is what the water DOES to you as you sink. Two things, one deck:
 *
 *   THE LADDER   the CCP effects, rung by rung, keyed to the DEEPEST TILE on the
 *                board. Presence rides the rung (the word you reached), magnitude
 *                rides the class heat (which already folds in depth, the grade
 *                cap and the exhale). Each rung ADDS; nothing goes away until a
 *                resurface or a dim-out lowers the rung, and a lower rung is
 *                stepped DOWN with fades behind a hysteresis, never a snap.
 *   THE TREMOR   the whole square vibrates, Balatro score style: a continuous
 *                low tremor whose amplitude grows with the rung, a PUNCH on every
 *                merge sized by the link / the points, heavier on a new deepest,
 *                heaviest on the ceiling. Extend-not-stack with a quadratic
 *                ring-down (dtrh screenShake posture), written on the board's
 *                individual `translate` so it never fights the bump keyframes,
 *                the bench lean or a tile's own slide. THE JUICE: the score chip
 *                scale-punches on every delta (4 points = a nudge, a 2048-class
 *                merge = a slam, overshoot then settle), the depth chip punches
 *                on a new word, the chain chip rides the link, and at deep rungs
 *                the score chip keeps a nervous jitter of its own.
 *
 * THE RUNG TABLE (deepest tier -> rung -> what switches ON; cumulative):
 *   tier 1-2  rung 0  clean pool (the ambience ladder in index.js is all there is)
 *   tier 3    rung 1  BREATH      pink wash breathes on a cadence           [engine wash:pink]
 *   tier 4    rung 2  PIN + CRT   the pinned wheel wakes behind the board   [local .g-de-p-pin]
 *                                 + scanline whisper                        [engine crt:scanline]
 *   tier 5    rung 3  BURST       gif bursts ride merges                    [engine gif_burst]
 *   tier 6    rung 4  GLITCH      full-stage glitch wash wearing a pool     [local .g-de-p-glitch]
 *                                 image + the bench shudders (vhsroll)      [engine glitch_swap]
 *   tier 7    rung 5  RAIN        gif rain on a new deepest / a 3-link      [engine gif_rain]
 *   tier 8    rung 6  WHEEL+DRAIN the full-screen wheel wash (real image),  [engine wash:spiral url]
 *                                 the drain deepens, the pin spins faster   [engine wash:drain]
 *   tier 9    rung 7  SLIDEGLITCH every slide glitches (local flash +       [engine glitch_swap,
 *                                 seeded bench roll), herald flash burst     flash_burst]
 *   tier 10   rung 8  SUBS+CHROMA sub flash stream + crt turns chroma       [engine sub_flash, crt]
 *   tier 11   rung 9  ROYAL       the blackout storm: downpour, pink flood, [engine gif_rain,
 *                                 bench roll, flash burst, gold pin         wash:pink, glitch_swap,
 *                                                                           flash_burst]
 *
 * ENGINE vs GAME-LOCAL (audit): everything the engine can do goes through
 * opts.engine (index.js's fireSafe / sustainSafe / stopSafe - clickSafe welded,
 * ceiling rule, capsOk, effectsConsumed). Game-local, and why:
 *   (a) THE TREMOR + THE JUICE   transform-level writes on the game's own DOM
 *       (board `translate`, chip `transform`); no engine primitive moves a
 *       game element.
 *   (b) THE GLITCH WASH          the engine's drain wash CAN wear a url, but it
 *       is ONE shared element that index.js holds forever at its own alpha,
 *       it has no luminosity blend, no dark base and no shudder - the DTRH
 *       showGlitch look needs all four. One node, inside the stage, at most
 *       one live image, blur only while lit.
 *   (c) THE PINNED WHEEL         DTRH #647 posture: inside the bench, BELOW the
 *       tiles, masked edges, slow spin. The engine's wash mounts at z40 over
 *       the whole class, which is the rung-6 full-screen look, not this one.
 *   (d) THE RING                 a box-shadow bloom around the board - the
 *       reduced-motion body of every punch, and the new-deepest / royal hit.
 *   Node budget: NODE_BUDGET (3: pin, ring, glitch). No per-event nodes. Every
 *   layer is pointer-events:none, lives inside the stage (so .suspended
 *   freezes it), is reused, and is removed in destroy().
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads nothing, writes nothing about board, score, chain,
 *       depth or grade; the chips' TEXT is never touched (scale only).
 *   II  input honest  - every node is pointer-events:none; --r/--c, data-tier
 *       and the tiles' own transforms are never written; the board's `translate`
 *       is the one thing we move and it is not a hit-test change the player can
 *       feel (sub-3px, the gesture surface is the whole board).
 *   III never still   - the tremor and the pin breathe with the rung.
 *   V   seeded        - per-tag mulberry32 off seed+'|de-pressure|' (append-only
 *       tags); the engine's own rng is separate and fine.
 *   VI  exits sacred  - capsOk() false disarms every VISUAL (no nodes, no
 *       fires); the rung CUE is decoupled from it on purpose (W2) - a visual
 *       dial must not mute the school, and the cue is still clamped;
 *       reduced motion / motionLevel 0 = tremor 0, punches become a brief ring
 *       bloom, the pin does not spin, the glitch does not shudder; every timer
 *       lives in the game's pause-aware registry AND a local set; the rAF loop
 *       runs ONLY while something moves and pause()/stop()/destroy() halt it.
 *   VII strings      - this file renders no text at all.
 *
 * NEVER: engine.stop('wash') - it blacks out EVERY wash kind, including the
 * drain index.js holds forever. A wash is stepped down by re-triggering the
 * same variant with a tiny alpha and a 120ms hold (it fades on its own .45s
 * transition). The one spiral gif is chosen once per class (sp6/sp7 weighted,
 * sp5 never) and shared by the pin and the engine wash.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DE_PRESSURE = Object.freeze({
  /** deepest tier (index 0..11) -> rung 0..9. tier 1-2 clean, then +1 per tier. */
  RUNG_OF_TIER: Object.freeze([0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9]),
  RUNG_MAX: 9,
  /** The ladder: what each rung ADDS (feature keys, cumulative), the cue it
   *  announces itself with (one per rung CHANGE, never per frame). */
  LADDER: Object.freeze([
    Object.freeze({ rung: 0, tier: 1, adds: Object.freeze([]), cue: null }),
    Object.freeze({ rung: 1, tier: 3, adds: Object.freeze(['breath']), cue: 'wash' }),
    Object.freeze({ rung: 2, tier: 4, adds: Object.freeze(['pin', 'crt']), cue: 'whisper' }),
    Object.freeze({ rung: 3, tier: 5, adds: Object.freeze(['burst']), cue: 'burst' }),
    Object.freeze({ rung: 4, tier: 6, adds: Object.freeze(['glitch']), cue: 'glitch' }),
    Object.freeze({ rung: 5, tier: 7, adds: Object.freeze(['rain']), cue: 'burst' }),
    Object.freeze({ rung: 6, tier: 8, adds: Object.freeze(['wheel', 'drain']), cue: 'wash' }),
    Object.freeze({ rung: 7, tier: 9, adds: Object.freeze(['slideglitch']), cue: 'glitch' }),
    Object.freeze({ rung: 8, tier: 10, adds: Object.freeze(['subs', 'chroma']), cue: 'whisper' }),
    Object.freeze({ rung: 9, tier: 11, adds: Object.freeze(['royal']), cue: 'near_miss' }),
  ]),
  /** Stepping DOWN waits this long (a jitter in the deepest tile never snaps the storm). */
  HYST_MS: 1600,
  /** Forever washes are repainted when heat moved at least this much. */
  HEAT_REPAINT_STEP: 0.08,

  /* ---- THE TREMOR ------------------------------------------------------ */
  /** idle amplitude (px) by rung at heat 1; sub-pixel at 3, ~1 at 6, 2.5 at 9. */
  TREMOR_PX: Object.freeze([0, 0, 0, 0.4, 0.6, 0.8, 1.0, 1.5, 2.0, 2.5]),
  TREMOR_CAP_PX: 3,
  /** amp = base * (floor + (1 - floor) * heat): tier-1 players still feel it. */
  TREMOR_HEAT_FLOOR: 0.5,
  /** two layered sines per axis: a fast shiver and a slow wander (Hz bands, seeded). */
  TREMOR_HZ_FAST: Object.freeze([7, 11]),
  TREMOR_HZ_SLOW: Object.freeze([1.6, 2.8]),
  /** motionLevel 0 / 1 / 2 -> amplitude multiplier. */
  MOTION_MUL: Object.freeze([0, 0.6, 1]),
  EXHALE_TREMOR_MUL: 0.5,
  /** a merge punch: px / ms / chip scale from q = log-ish deltaScore (0..1) + link. */
  PUNCH_PX_BASE: 1.0,
  PUNCH_PX_SPAN: 5.0,
  PUNCH_PX_LINK: 0.3,
  PUNCH_PX_CAP: 8,
  PUNCH_MS_BASE: 180,
  PUNCH_MS_SPAN: 220,
  PUNCH_MS_CAP: 450,
  PUNCH_SCALE_BASE: 0.04,
  PUNCH_SCALE_SPAN: 0.22,
  PUNCH_SCALE_CAP: 1.3,
  /** a new deepest: heavier; the ceiling: heaviest (the 8px/450ms hard cap). */
  PUNCH_DEEP_PX: 3,
  PUNCH_DEEP_PER_TIER: 0.4,
  PUNCH_DEEP_MS: 420,
  PUNCH_ROYAL_PX: 8,
  PUNCH_ROYAL_MS: 450,
  /* ---- THE JUICE ------------------------------------------------------- */
  CHAIN_SCALE_PER_LINK: 0.03,
  DEPTH_PUNCH_SCALE: 1.22,
  /** the score chip's nervous jitter: from this rung, this px band by heat. */
  JITTER_RUNG: 6,
  JITTER_PX: Object.freeze([0.35, 0.8]),
  /** the ring / chip bloom hold (a class + a transition; reduced motion's whole punch). */
  BLOOM_MS: 320,

  /* ---- the rungs' knobs ------------------------------------------------ */
  BREATH_MS: Object.freeze([9000, 5500]),
  BREATH_HOLD_MS: 2600,
  BREATH_ALPHA: Object.freeze([0.12, 0.42]),
  EXHALE_BREATH_MUL: 1.5,
  PIN_ALPHA: Object.freeze([0.18, 0.5]),
  PIN_SPIN_S: Object.freeze([40, 16]),
  PIN_FAST_MUL: 0.55,
  EXHALE_SPIN_MUL: 1.6,
  GLITCH_HOLD_MS: Object.freeze([900, 2200]),
  GLITCH_ALPHA: Object.freeze([0.3, 0.6]),
  GLITCH_SHUDDER_MS: 700,
  GLITCH_MERGE_TIER: 5,
  GLITCH_MERGE_CHANCE: Object.freeze([0.18, 0.5]),
  BURST_CHANCE: Object.freeze([0.35, 0.85]),
  BURST_COUNT: Object.freeze([1, 3]),
  BURST_HOLD_MS: 900,
  RAIN_MS: Object.freeze([2600, 5200]),
  RAIN_LINK: 3,
  WHEEL_ALPHA: Object.freeze([0.22, 0.55]),
  DRAIN_ALPHA_DEEP: Object.freeze([0.45, 0.62]),
  SLIDE_GLITCH_GAP_MS: 420,
  SLIDE_GLITCH_S: 0.35,
  SLIDE_GLITCH_CHANCE: Object.freeze([0.5, 1]),
  SLIDE_FLASH_MS: 260,
  SUB_VARIANT: 'scatter',
  ROYAL_RAIN_MS: 7000,
  /** cue level = min(AUDIO_CEIL[gradeTier], base + rung * step). */
  CUE_LEVEL_BASE: 0.25,
  CUE_LEVEL_STEP: 0.05,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),

  /* ---- the one spiral image per class ---------------------------------- */
  SPIRAL_DIR: '../../../dtrh/assets/bubbles/effects/spirals/',
  /** sp6 (123K) / sp7 (721K) carry most of the weight; sp5 (5.3M) is never drawn. */
  SPIRALS: Object.freeze([
    Object.freeze({ file: 'sp6.gif', w: 4 }),
    Object.freeze({ file: 'sp7.gif', w: 4 }),
    Object.freeze({ file: 'sp1.gif', w: 1 }),
    Object.freeze({ file: 'sp2.webp', w: 1 }),
    Object.freeze({ file: 'sp3.gif', w: 1 }),
    Object.freeze({ file: 'sp4.webp', w: 1 }),
  ]),
  /** game-local nodes, total: pin + ring + glitch. */
  NODE_BUDGET: 3,
});

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }
function tierOf(v) { return Math.max(0, Math.min(11, Math.round(Number(v) || 0))); }

/** deepest tier (1..11) -> rung (0..9). Pure, monotone, clamps outside input. */
export function rungFor(deepestTier) {
  return DE_PRESSURE.RUNG_OF_TIER[tierOf(deepestTier)] || 0;
}

/** The idle tremor amplitude in px. 0 under reduced motion; heat scales it
 *  between the floor and the full table value; hard-capped. Pure. */
export function tremorAmpPx(deepestTier, heat01, reducedMotion) {
  if (reducedMotion) return 0;
  const base = DE_PRESSURE.TREMOR_PX[rungFor(deepestTier)] || 0;
  if (base <= 0) return 0;
  const f = DE_PRESSURE.TREMOR_HEAT_FLOOR;
  const amp = base * (f + (1 - f) * clamp01(heat01));
  return Math.min(DE_PRESSURE.TREMOR_CAP_PX, +amp.toFixed(3));
}

/** A merge punch: { scale (chip), px (board), ms } - log-ish in deltaScore
 *  (4 points = a nudge, a 2048-class merge = a slam), a little per link and
 *  per tier, every term capped. Pure. */
export function punchFor(o) {
  const d = o || {};
  const link = Math.max(1, Math.min(12, Number(d.link) || 1));
  const tier = Math.max(1, Math.min(11, Number(d.tier) || 1));
  const ds = Math.max(0, Number(d.deltaScore) || 0);
  const q = clamp01((Math.log2(Math.max(2, ds)) - 1) / 11);     // 4 -> .09, 2048 -> .91, 4096 -> 1
  const P = DE_PRESSURE;
  const px = Math.min(P.PUNCH_PX_CAP, P.PUNCH_PX_BASE + P.PUNCH_PX_SPAN * q + P.PUNCH_PX_LINK * (link - 1) + 0.15 * (tier / 11));
  const ms = Math.min(P.PUNCH_MS_CAP, Math.round(P.PUNCH_MS_BASE + P.PUNCH_MS_SPAN * q + 8 * (link - 1)));
  const scale = Math.min(P.PUNCH_SCALE_CAP, 1 + P.PUNCH_SCALE_BASE + P.PUNCH_SCALE_SPAN * q + 0.01 * (link - 1));
  return { scale: +scale.toFixed(3), px: +px.toFixed(2), ms };
}

/** The class's spiral image, chosen once from the bundled dtrh pool (weighted). */
function pickSpiral(roll) {
  const list = DE_PRESSURE.SPIRALS;
  let total = 0;
  for (const s of list) total += s.w;
  let r = roll * total;
  let file = list[0].file;
  for (const s of list) { r -= s.w; if (r <= 0) { file = s.file; break; } }
  let href = DE_PRESSURE.SPIRAL_DIR + file;
  try { href = new URL(DE_PRESSURE.SPIRAL_DIR + file, import.meta.url).href; } catch (e) { /* relative is fine */ }
  return { file, href };
}

function el(tag, cls) {
  try {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
function nowMs() {
  try { if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall through */ }
  return Date.now();
}
function rafFn() {
  try { if (typeof requestAnimationFrame === 'function') return requestAnimationFrame; } catch (e) { /* fall through */ }
  try { if (typeof globalThis !== 'undefined' && typeof globalThis.requestAnimationFrame === 'function') return globalThis.requestAnimationFrame; } catch (e) { /* none */ }
  return null;
}
function cafFn() {
  try { if (typeof cancelAnimationFrame === 'function') return cancelAnimationFrame; } catch (e) { /* fall through */ }
  try { if (typeof globalThis !== 'undefined' && typeof globalThis.cancelAnimationFrame === 'function') return globalThis.cancelAnimationFrame; } catch (e) { /* none */ }
  return null;
}

/**
 * @param {Object} o   see THE INTERFACE in the pass-4 sheet:
 *   seed, gradeTier, reduced, motionLevel, stage, bench, board,
 *   hud:{score,depth,chain,clock}, engine:{fire,sustain,stop,channels},
 *   assets:{next(kind)}, timers:{after,every,clear}, capsOk:()=>bool, log
 */
export function createDePressure(o) {
  const opts = o || {};
  const P = DE_PRESSURE;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;           // no tremor, no chip transforms, no spin, no shudder
  /* pass 6 - THE TOUCH RUNG: on a phone the merge punch takes the reduced-
     motion body (the ring bloom) instead of the rAF board-translate loop -
     `board.style.translate` per frame restyles the whole board subtree, and it
     fires on the very first 2-tile merge. */
  const touchDev = !!opts.touch;
  const gradeTier = Math.max(1, Math.min(4, Number(opts.gradeTier) || 1));
  const audioCeil = P.AUDIO_CEIL[gradeTier] || P.AUDIO_CEIL[1];
  const hud = opts.hud || {};
  const eng = opts.engine || {};
  const armedBase = !!opts.stage && !!opts.bench && !!opts.board
    && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  /** capsOk is a LIVE read: a function (the interface), a boolean (casino parity), else disarmed. */
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk === true;
  }
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();

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
  const seedBase = String(opts.seed || 'de') + '|de-pressure|';
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
  /**
   * W2 - THE DECOUPLE (spec 3). The rung cue used to ride `fire()`, which
   * gates on `armed()` - and `armed()` folds in capsOk. bgIntensity 0 is the
   * player's VISUAL exit (Law VI), never a request for a silent school, so a
   * capped-background class went both dark AND mute and the ladder lost its
   * only announcement. Sound now rides the rest of the gate - armedBase,
   * destroyed, stopped - and the level is still clamped to this tier's audio
   * ceiling. Every VISUAL fire above is untouched and still capsOk-gated.
   * The counter stays on the same key so diagnostics read as they always did.
   */
  function cue(name, rung) {
    if (!name || !armedBase || destroyed || stopped) return;
    count('audio_trigger');
    if (typeof eng.fire !== 'function') return;
    const level = Math.min(audioCeil, P.CUE_LEVEL_BASE + P.CUE_LEVEL_STEP * rung);
    try { eng.fire('audio_trigger', { name, level }); } catch (e) { /* a cue never throws upward */ }
  }

  /* ---- state ------------------------------------------------------------ */
  let started = false;
  let stopped = false;
  let paused = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let tierNow = 1;
  let rung = 0;
  let hystTimer = 0;
  let hystTarget = -1;
  let exhaleOn = false;
  let bellOn = false;
  let royalOn = false;
  let outOn = false;
  const present = new Set();          // feature keys ON right now
  const spiral = pickSpiral(roll('spiral'));
  let preload = null;
  let assetUrl = null;                // the glitch wash's pool image (one per kind)

  /* the local layers */
  let pin = null; let ring = null; let glitch = null;
  let breathTimer = 0; let glitchTimer = 0; let shudderTimer = 0; let ringTimer = 0;
  let lastSlideGlitchAt = -1e9;
  const chipBloomTimers = new Map();

  /* the tremor */
  const fq = {
    fx: lerp(P.TREMOR_HZ_FAST, roll('tremor-f')), fy: lerp(P.TREMOR_HZ_FAST, roll('tremor-f')),
    sx: lerp(P.TREMOR_HZ_SLOW, roll('tremor-f')), sy: lerp(P.TREMOR_HZ_SLOW, roll('tremor-f')),
    px: roll('tremor-f') * 6.283, py: roll('tremor-f') * 6.283, qx: roll('tremor-f') * 6.283, qy: roll('tremor-f') * 6.283,
  };
  let tremorAmp = 0;
  let punchPeak = 0; let punchEnd = 0; let punchSpan = 1;
  let translateWrites = 0;
  let translateOn = false;
  let rafId = 0; let loopOn = false; let pausedAt = 0;

  /* the juice */
  const jit = { fx: lerp(P.TREMOR_HZ_FAST, roll('jit')), sx: lerp(P.TREMOR_HZ_SLOW, roll('jit')), px: roll('jit') * 6.283, py: roll('jit') * 6.283 };
  const chips = {
    score: { el: hud.score || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
    depth: { el: hud.depth || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
    chain: { el: hud.chain || null, a: 0, at: 0, ms: 1, last: '', writes: 0 },
  };

  /* ------------------------------------------------------------- helpers */
  const has = (k) => present.has(k);
  function setVar(node, k, v) { if (!node || !node.style) return; try { node.style.setProperty(k, v); } catch (e) { /* ignore */ } }
  function cls(node, name, on) {
    if (!node || !node.classList) return;
    try { if (on) node.classList.add(name); else node.classList.remove(name); } catch (e) { /* ignore */ }
  }
  function restart(node, name) {
    if (!node || !node.classList) return;
    try {
      node.classList.remove(name);
      if (typeof node.offsetWidth === 'number') void node.offsetWidth;
      node.classList.add(name);
    } catch (e) { /* ignore */ }
  }
  /** A tile's centre as a % of the viewport (the engine layer's own units). */
  function pctOf(tileEl) {
    const r = rectOf(tileEl);
    let w = 0; let h = 0;
    try { w = typeof window !== 'undefined' ? Number(window.innerWidth) || 0 : 0; h = typeof window !== 'undefined' ? Number(window.innerHeight) || 0 : 0; } catch (e) { /* none */ }
    if (!r || !r.width || !w || !h) return null;
    return { x: Math.round((r.left + r.width / 2) / w * 100), y: Math.round((r.top + r.height / 2) / h * 100), size: Math.round(r.width) };
  }
  function nextAsset() {
    const a = opts.assets;
    if (!a || typeof a.next !== 'function') return null;
    try {
      const got = a.next('still') || a.next('loop');
      return got && got.url ? String(got.url) : null;
    } catch (e) { return null; }
  }

  /* ------------------------------------------------------------- mounting */
  function mount() {
    if (pin || !opts.bench || !opts.bench.appendChild) return;
    pin = el('div', 'g-de-p-pin');
    if (pin) {
      if (pin.style) {
        try { pin.style.backgroundImage = 'url("' + spiral.href + '")'; } catch (e) { /* ignore */ }
        setVar(pin, '--de-p-spindir', roll('pin-dir') < 0.5 ? '-1' : '1');
      }
      opts.bench.appendChild(pin);
    }
    ring = el('i', 'g-de-p-ring');
    if (ring) opts.bench.appendChild(ring);
    if (opts.stage && opts.stage.appendChild) {
      glitch = el('div', 'g-de-p-glitch');
      if (glitch) opts.stage.appendChild(glitch);
    }
    // preload the ONE spiral this class will ever show (the pin and the wash share it)
    try {
      if (typeof Image === 'function') { preload = new Image(); preload.src = spiral.href; }
    } catch (e) { preload = null; }
  }

  /* ------------------------------------------------------------ the loop */
  function needsLoop() {
    if (tremorAmp > 0.005) return true;
    if (punchPeak > 0) return true;
    const t = nowMs();
    for (const k of Object.keys(chips)) { const c = chips[k]; if (c.a > 0 && t - c.at < c.ms) return true; }
    if (jitterAmp() > 0) return true;
    return false;
  }
  function jitterAmp() {
    // W2: the ladder may now climb with capsOk false (see start()), so this
    // VISUAL - the score chip's nervous jitter - has to say so itself.
    if (still || stopped || outOn || !armed() || rung < P.JITTER_RUNG || !chips.score.el) return 0;
    return lerp(P.JITTER_PX, heat);
  }
  function armLoop() {
    if (loopOn || paused || destroyed || stopped) return;
    if (!needsLoop()) return;
    const r = rafFn();
    if (!r) return;
    loopOn = true;
    rafId = r(frame) || 1;
  }
  function haltLoop() {
    if (rafId) { const c = cafFn(); if (c) { try { c(rafId); } catch (e) { /* ignore */ } } }
    rafId = 0;
    loopOn = false;
  }
  function frame() {
    rafId = 0;
    if (!loopOn || paused || destroyed) { loopOn = false; return; }
    step(nowMs());
    if (needsLoop()) {
      const r = rafFn();
      if (r) { rafId = r(frame) || 1; return; }
    }
    loopOn = false;
    rest();
  }
  function writeTranslate(x, y) {
    const b = opts.board;
    if (!b || !b.style) return;
    try { b.style.translate = x.toFixed(2) + 'px ' + y.toFixed(2) + 'px'; translateWrites += 1; translateOn = true; } catch (e) { /* ignore */ }
  }
  function clearTranslate() {
    const b = opts.board;
    if (!b || !b.style) return;
    try { b.style.translate = ''; if (typeof b.style.removeProperty === 'function') b.style.removeProperty('translate'); } catch (e) { /* ignore */ }
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
    let x = 0; let y = 0;
    let moving = false;
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
        const a = punchPeak * decay * decay;          // quadratic ring-down: hits hard, settles fast
        x += (roll('jolt') * 2 - 1) * a;
        y += (roll('jolt') * 2 - 1) * a;
        moving = true;
      }
    }
    if (moving) writeTranslate(x, y);
    else if (translateOn) clearTranslate();

    const ja = jitterAmp();
    for (const k of Object.keys(chips)) {
      const c = chips[k];
      if (!c.el) continue;
      let sc = 1;
      if (c.a > 0) {
        const p = (t - c.at) / c.ms;
        if (p >= 1) c.a = 0;
        else sc = 1 + c.a * (1 - p) * (1 - p) * Math.cos(p * Math.PI * 2.2);   // overshoot, dip under, settle
      }
      let jx = 0; let jy = 0;
      if (k === 'score' && ja > 0) {
        jx = ja * (0.6 * Math.sin(6.283 * jit.fx * s + jit.px) + 0.4 * Math.sin(6.283 * jit.sx * s + jit.py));
        jy = ja * (0.6 * Math.sin(6.283 * jit.fx * s * 0.93 + jit.py) + 0.4 * Math.sin(6.283 * jit.sx * s + jit.px));
      }
      if (sc === 1 && jx === 0 && jy === 0) writeChip(c, '');
      else writeChip(c, 'translate(' + jx.toFixed(2) + 'px,' + jy.toFixed(2) + 'px) scale(' + sc.toFixed(3) + ')');
    }
  }

  /** Extend-not-stack: the louder amplitude wins, the deadline moves out. */
  function punchBoard(px, ms) {
    if (!armed() || stopped) return;
    if (still || touchDev) { bloomRing(false); return; }   // touch: bloom, never the translate loop
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
    const a = Math.min(P.PUNCH_SCALE_CAP, Math.max(1, Number(scale) || 1)) - 1;
    if (a <= 0.001) return;
    const t = nowMs();
    // extend-not-stack for the chips too: keep the bigger punch
    const left = c.a > 0 ? Math.max(0, 1 - (t - c.at) / c.ms) : 0;
    c.a = Math.max(a, c.a * left);
    c.at = t;
    c.ms = Math.max(120, Math.min(P.PUNCH_MS_CAP, Number(ms) || 240));
    armLoop();
  }
  function bloomChip(which) {
    const c = chips[which];
    if (!c || !c.el) return;
    restart(c.el, 'g-de-p-bloom');
    const old = chipBloomTimers.get(which);
    if (old) cancel(old);
    chipBloomTimers.set(which, after(P.BLOOM_MS, () => { chipBloomTimers.delete(which); cls(c.el, 'g-de-p-bloom', false); }));
  }
  function bloomRing(deep) {
    if (!ring) return;
    cls(ring, 'is-deep', !!deep);
    restart(ring, 'is-hit');
    if (ringTimer) cancel(ringTimer);
    ringTimer = after(deep ? P.BLOOM_MS * 2 : P.BLOOM_MS, () => { ringTimer = 0; cls(ring, 'is-hit', false); cls(ring, 'is-deep', false); });
  }

  /* ------------------------------------------------------------ the rungs */
  function retune() {
    const base = tremorAmpPx(tierNow, heat, reduced) * P.MOTION_MUL[motion] * (exhaleOn ? P.EXHALE_TREMOR_MUL : 1);
    tremorAmp = (stopped || outOn || !armed()) ? 0 : Math.min(P.TREMOR_CAP_PX, base);
    if (pin) {
      setVar(pin, '--de-p-pa', lerp(P.PIN_ALPHA, heat).toFixed(2));
      const spin = lerp(P.PIN_SPIN_S, heat) * (has('wheel') ? P.PIN_FAST_MUL : 1) * (exhaleOn ? P.EXHALE_SPIN_MUL : 1);
      setVar(pin, '--de-p-spin', spin.toFixed(1) + 's');
    }
    if (glitch) setVar(glitch, '--de-p-ga', lerp(P.GLITCH_ALPHA, heat).toFixed(2));
    if (Math.abs(heat - lastPaintHeat) >= P.HEAT_REPAINT_STEP) {
      if (has('wheel')) wheelWash();
      if (has('drain')) drainDeep(true);
      lastPaintHeat = heat;
    }
    if (tremorAmp > 0 || jitterAmp() > 0) armLoop();
  }

  /* the features: on(rungEntered) / off(destinationRung) */
  function breathe() {
    sustain('wash', { variant: 'pink', alpha: lerp(P.BREATH_ALPHA, heat), holdMs: P.BREATH_HOLD_MS });
  }
  function armBreath() {
    if (breathTimer) cancel(breathTimer);
    const ms = lerp(P.BREATH_MS, heat) * (exhaleOn ? P.EXHALE_BREATH_MUL : 1) * (0.85 + roll('breath') * 0.3);
    breathTimer = after(Math.round(ms), () => { breathTimer = 0; if (has('breath') && !stopped) { breathe(); armBreath(); } });
  }
  function wheelWash() {
    sustain('wash', { variant: 'spiral', url: spiral.href, alpha: lerp(P.WHEEL_ALPHA, heat), sustainForever: true });
  }
  function fadeWash(variant, extra) {
    // NEVER stop('wash'): it blacks out the drain index.js holds. A tiny alpha
    // + a 120ms hold lets the engine's own .45s transition carry it out.
    sustain('wash', Object.assign({ variant, alpha: 0.01, holdMs: 120 }, extra || {}));
  }
  function drainDeep(on) {
    if (on) sustain('wash', { variant: 'drain', alpha: lerp(P.DRAIN_ALPHA_DEEP, heat), sustainForever: true });
    else sustain('wash', { variant: 'drain', sustainForever: true });     // back to index.js's baseline alpha
  }
  function glitchWash(holdMs, shudder) {
    if (!glitch) return;
    if (!assetUrl) { assetUrl = nextAsset(); if (assetUrl && glitch.style) { try { glitch.style.backgroundImage = 'url("' + assetUrl + '")'; } catch (e) { /* ignore */ } } }
    cls(glitch, 'is-on', true);
    if (shudder && !still) {
      restart(glitch, 'is-shudder');
      if (shudderTimer) cancel(shudderTimer);
      shudderTimer = after(P.GLITCH_SHUDDER_MS, () => { shudderTimer = 0; cls(glitch, 'is-shudder', false); });
    }
    if (glitchTimer) cancel(glitchTimer);
    glitchTimer = after(Math.max(120, holdMs | 0), () => { glitchTimer = 0; cls(glitch, 'is-on', false); });
  }
  function glitchOff() {
    if (glitchTimer) { cancel(glitchTimer); glitchTimer = 0; }
    if (shudderTimer) { cancel(shudderTimer); shudderTimer = 0; }
    cls(glitch, 'is-on', false);
    cls(glitch, 'is-shudder', false);
  }
  function benchRoll(seconds) {
    if (still || !opts.bench) return null;
    return fire('glitch_swap', { targets: opts.bench, variant: 'vhsroll', seconds: seconds || 0.6, onSwap() {}, sfx: false });
  }
  function rain(variant, ms) {
    return sustain('gif_rain', { variant, durationMs: Math.round(ms), clickSafe: true });
  }
  function burstAt(tileEl, n) {
    const at = pctOf(tileEl);
    const o2 = { count: Math.max(1, n | 0), holdMs: P.BURST_HOLD_MS, clickSafe: true, clickable: false };
    if (at) { o2.x = at.x; o2.y = at.y; o2.sizePx = Math.max(90, Math.round(at.size * 0.9)); }
    return fire('gif_burst', o2);
  }
  function crtOn(variant, restartIt) {
    const o2 = { variant };
    if (restartIt) o2.restart = true;
    return sustain('crt', o2);
  }

  const FEATURES = {
    breath: {
      on() { breathe(); armBreath(); },
      off() { if (breathTimer) { cancel(breathTimer); breathTimer = 0; } },
    },
    pin: { on() { cls(pin, 'is-on', true); }, off() { cls(pin, 'is-on', false); } },
    crt: { on() { crtOn('scanline', false); }, off() { stopKind('crt'); } },
    burst: { on() { burstAt(null, 1); }, off() {} },
    glitch: {
      on() { glitchWash(lerp(P.GLITCH_HOLD_MS, heat), true); benchRoll(0.6); },
      off() { glitchOff(); },
    },
    rain: { on() { rain('light', lerp(P.RAIN_MS, heat)); }, off() { stopKind('gif_rain'); } },
    wheel: { on() { wheelWash(); }, off() { fadeWash('spiral', { url: spiral.href }); } },
    drain: { on() { drainDeep(true); }, off() { drainDeep(false); } },
    slideglitch: { on() { fire('flash_burst', { count: 2, holdMs: 700, clickSafe: true, clickable: false }); }, off() {} },
    subs: { on() { sustain('sub_flash', { variant: P.SUB_VARIANT }); }, off() { stopKind('sub_flash'); } },
    chroma: {
      on() { crtOn('chroma', true); },
      off(dest) { if (dest >= 2) crtOn('scanline', true); },   // crt itself is rung 2's; leaving past it stops it there
    },
    royal: {
      on() {
        rain('downpour', P.ROYAL_RAIN_MS);
        sustain('wash', { variant: 'pink', alpha: 0.7, holdMs: 3200 });
        benchRoll(0.9);
        fire('flash_burst', { count: 4, holdMs: 900, clickSafe: true, clickable: false });
        cls(pin, 'is-gold', true);
      },
      off() { if (!bellOn) cls(pin, 'is-gold', false); },
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
    cue(P.LADDER[r] && P.LADDER[r].cue, r);          // ONE cue per rung change, the top rung's
    lastPaintHeat = heat;
    retune();
    say('pressure: rung ' + from + ' -> ' + r + ' (tier ' + tierNow + ') on: ' + Array.from(present).join(','));
  }
  function descend(r) {
    if (r >= rung) return;
    const from = rung;
    for (let k = from; k > r; k--) leaveRung(k, r);
    rung = r;
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
    descend(0);
    glitchOff();
    if (ringTimer) { cancel(ringTimer); ringTimer = 0; }
    cls(ring, 'is-hit', false); cls(ring, 'is-deep', false);
    for (const [which, id] of chipBloomTimers) { cancel(id); const c = chips[which]; if (c) cls(c.el, 'g-de-p-bloom', false); }
    chipBloomTimers.clear();
    punchPeak = 0; punchEnd = 0;
    for (const k of Object.keys(chips)) { chips[k].a = 0; }
  }

  /* ---------------------------------------------------------------- api */
  const api = {
    /**
     * Mount the layers. Called right after casino.start().
     * W2 - THE DECOUPLE, HALF TWO. This used to bail on `armed()`, which folds
     * capsOk in - so bgIntensity 0 stopped the LADDER ITSELF and the rung cue
     * had nothing left to announce. The ladder now runs either way (it is a
     * state machine over the deepest tile, not a picture); with the dial at 0
     * NOTHING is mounted and every fire/sustain below still no-ops on
     * `armed()`, so the room stays dark and only the rung cue is heard. IC's
     * pressure deck has always started this way - the two now match.
     */
    start() {
      if (!armedBase || destroyed) { say('pressure: disarmed'); return; }
      if (started) return;
      started = true;
      if (!armed()) { say('pressure: caps 0 - the ladder climbs for SOUND only'); return; }
      mount();
      retune();
      say('pressure: mounted ' + [pin, ring, glitch].filter(Boolean).length + ' layers, wheel ' + spiral.file);
    },

    /** Magnitude rides heat. index.js calls after casino.setHeat. */
    setHeat(h) {
      heat = clamp01(h);
      if (!started) return;
      retune();
    },

    /** Presence rides the rung. Called on every heat recompute + newDive/resurface. */
    setDepth(deepestTier) {
      tierNow = Math.max(1, tierOf(deepestTier));
      if (!started || stopped) return;
      stepTo(rungFor(tierNow), false);
      retune();
    },

    /** Every legal slide. At rung 7+ the room glitches with the move (throttled, seeded). */
    slide(dir) {
      if (!started || stopped || !armed() || !has('slideglitch') || !dir) return;
      const t = nowMs();
      if (t - lastSlideGlitchAt < P.SLIDE_GLITCH_GAP_MS) return;
      lastSlideGlitchAt = t;
      glitchWash(P.SLIDE_FLASH_MS, false);
      if (roll('slide-g') < lerp(P.SLIDE_GLITCH_CHANCE, heat)) benchRoll(P.SLIDE_GLITCH_S);
    },

    /** Every merge, after the ledger: the punch, the juice, the rung's riders. */
    merge(m) {
      if (!started || stopped || !armed()) return;
      const info = m || {};
      const link = Math.max(1, Math.min(12, Number(info.link) || 1));
      const tier = Math.max(1, Math.min(11, Number(info.tier) || 1));
      const p = punchFor({ link, tier, deltaScore: info.deltaScore });
      punchBoard(p.px, p.ms);
      punchChip('score', p.scale, p.ms + 60);
      if (link > 1) punchChip('chain', 1 + P.CHAIN_SCALE_PER_LINK * Math.min(8, link), 220);
      if (has('burst') && roll('burst') < lerp(P.BURST_CHANCE, heat) + 0.05 * Math.min(4, link - 1)) {
        burstAt(info.tileEl, Math.round(lerp(P.BURST_COUNT, heat)) + (link >= 3 ? 1 : 0));
      }
      if (has('glitch') && tier >= P.GLITCH_MERGE_TIER && roll('glitch-chance') < lerp(P.GLITCH_MERGE_CHANCE, heat)) {
        glitchWash(lerp(P.GLITCH_HOLD_MS, heat) * 0.6, true);
        benchRoll(0.5);
      }
      if (has('rain') && link >= P.RAIN_LINK) rain('steady', lerp(P.RAIN_MS, heat));
    },

    /** A new deepest tier this dive: the rung climbs NOW, then the heavy hit. */
    newDeepest(tier, tileEl) {
      if (!started || stopped || !armed()) return;
      tierNow = Math.max(1, tierOf(tier));
      stepTo(rungFor(tierNow), false);
      punchBoard(Math.min(P.PUNCH_PX_CAP, P.PUNCH_DEEP_PX + P.PUNCH_DEEP_PER_TIER * tierNow), P.PUNCH_DEEP_MS);
      punchChip('depth', P.DEPTH_PUNCH_SCALE, 380);
      bloomRing(true);
      if (has('rain')) rain(heat > 0.66 ? 'downpour' : heat > 0.33 ? 'steady' : 'light', lerp(P.RAIN_MS, heat));
      if (has('glitch')) { glitchWash(lerp(P.GLITCH_HOLD_MS, heat), true); benchRoll(0.7); }
      if (has('burst')) burstAt(tileEl, Math.round(lerp(P.BURST_COUNT, heat)));
    },

    /** The mercy breath: slower tremor, slower spin, slower breath. */
    exhale(on) {
      exhaleOn = !!on;
      if (started) { retune(); if (has('breath')) armBreath(); }
    },

    /** The board drains: the storm steps down to the shallows, with fades. */
    resurface() {
      if (!started || stopped) return;
      tierNow = 1;
      stepTo(0, true);
    },

    /** The royal: tier 11 ends the class. The storm, the heaviest punch; stop() follows. */
    ceiling() {
      if (!started || stopped || !armed()) return;
      royalOn = true;
      tierNow = 11;
      stepTo(P.RUNG_MAX, false);
      punchBoard(P.PUNCH_ROYAL_PX, P.PUNCH_ROYAL_MS);
      punchChip('depth', P.PUNCH_SCALE_CAP, 420);
      punchChip('score', P.PUNCH_SCALE_CAP, 420);
      cls(ring, 'is-gold', true);
      bloomRing(true);
    },

    /** The last 20s: gold tint, no new rungs. */
    bell(on) {
      bellOn = !!on;
      cls(pin, 'is-gold', bellOn || royalOn);
      cls(ring, 'is-gold', bellOn || royalOn);
    },

    /** The bell took the board: everything sighs out. */
    dimOut() {
      outOn = true;
      bellOn = false;
      royalOn = false;
      if (!started) return;
      everythingOff();
      cls(pin, 'is-gold', false);
      cls(ring, 'is-gold', false);
      retune();
    },

    /** Freeze the loop (pause / suspend). Timers are the game's and already freeze. */
    pause() {
      if (paused) return;
      paused = true;
      pausedAt = nowMs();
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
      for (const k of Object.keys(chips)) { const c = chips[k]; if (c.el) { cls(c.el, 'g-de-p-bloom', false); try { c.el.style.transform = ''; } catch (e) { /* ignore */ } } }
      for (const node of [pin, ring, glitch]) { if (node) { try { node.remove(); } catch (e) { /* ignore */ } } }
      pin = null; ring = null; glitch = null; preload = null;
      present.clear();
    },

    /** For the harness + index.js diagnostics().pressure. */
    diagnostics() {
      return {
        armed: armed(), started, stopped, paused, out: outOn, bell: bellOn, royal: royalOn, exhale: exhaleOn,
        rung, hystPending: !!hystTimer, hystTarget, tier: tierNow, heat,
        features: Array.from(present),
        tremorPx: +tremorAmp.toFixed(3), punchPx: +punchPeak.toFixed(2), punchLive: punchPeak > 0,
        loop: loopOn, translateWrites, translateOn,
        chips: {
          score: { writes: chips.score.writes, last: chips.score.last },
          depth: { writes: chips.depth.writes, last: chips.depth.last },
          chain: { writes: chips.chain.writes, last: chips.chain.last },
        },
        liveNodes: [pin, ring, glitch].filter(Boolean).length,
        pinOn: !!(pin && pin.classList && pin.classList.contains('is-on')),
        glitchOn: !!(glitch && glitch.classList && glitch.classList.contains('is-on')),
        spiral: spiral.file, spiralUrl: spiral.href, assetUrl,
        fires: Object.assign({}, fires),
        timers: live.size,
      };
    },
  };
  return api;
}

export default createDePressure;
