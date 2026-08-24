/* ============================================================================
 * games/composure/casino.js - DECK II of the House Rules for the studio: THE
 * FLOOR. The trickster (trickster.js) lies about the picture; the pressure
 * (pressure.js) is what the room does to you as it comes together; this file
 * LIGHTS the room and makes every lock pay out in light and sound.
 *
 *   STUDIO IDENTITY seeded per CLASS seed (Deck I): a hue pair on the
 *                   violet->rose arc (~6% of studios leave the arc for a teal
 *                   night), a wall archetype (plaster / linen / brick /
 *                   velvet), a wood tone for the easel, where the lamp hangs,
 *                   a breathing period, a drift period, a ken-burns period,
 *                   a marquee phase. Painted as --cp-n-* props on the stage +
 *                   a wall class; style.js consumes them with plain-studio
 *                   fallbacks, so a disarmed floor changes nothing. Same seed,
 *                   same studio; a retake sits in the identical room.
 *   ASSET CHROME    (Deck VI) when a pool still is handed in (opts.assets),
 *                   it becomes the wall's whisper (--cp-n-asset at ~.06) and
 *                   the end card's paper - the player's own library is the
 *                   wallpaper.
 *   THE MARQUEE     a bulb-chase ring in the easel's WOOD band (z2 over the
 *                   frame's ::before; the light mat swallowed it), round the canvas.
 *                   TIMED: crawls lazily at heat 0, chases hungrily at heat 1,
 *                   goes gold and frantic for the bell, flashes on a payout,
 *                   sighs out on a dim-out. ZEN: no chase at all - the bulbs
 *                   breathe together on the room's own breath (.g-cp-mq-zen).
 *   SLIDE TICK      every legal slide pays a short whoosh (engine 'slide',
 *                   level by heat, pitch by progress) and the easel leans a
 *                   hair toward the move (--cp-lean-x/y on the frame, style.js
 *                   composes + springs it back). Zen: the tick at half level,
 *                   no lean.
 *   PAYOUT LIGHT    a lock pays light: the marquee flashes, the lamp pulses,
 *                   glints rise off the tile. THE STREAK: consecutive locks
 *                   with no thrash between them climb a pitch ladder (+1
 *                   semitone per link, cap +7 - the intake precedent) on the
 *                   lock chime; a thrash is a muted thud and resets the
 *                   ladder, never silence.
 *   THE ALMOST      near-miss staging: a tile that skirts its own home - sat
 *                   one cell off, slid, and is STILL one cell off - hums the
 *                   canvas, shows the near-miss word (lexicon) and rises the
 *                   near_miss cue. Cooldown 6s. Staging only; the ledger
 *                   already decided, and no tile is ever moved by this file.
 *   JACKPOT LADDER  MINOR when the lock count crosses a whole row of the
 *                   board, MAJOR at half the pieces, ROYAL on the solve (the
 *                   studio floods gold, the frame glows, a column of glints).
 *                   Zen: the solve is a warm royal - the lamp comes up, no
 *                   gold flood, no arpeggio stack.
 *   DIM-OUT         the bell took the board: the rig sighs out instead of
 *                   cutting, and a small muted wash cue plays - losses
 *                   disguised, silence is where people stand up.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects index.js hands it AFTER its
 *       own accounting; reads --r/--c/data-id/data-home off the tiles (never
 *       writes them); writes nothing about moves, locks, time or grade.
 *   II  input honest  - every node is pointer-events:none and lives on the
 *       frame or the backdrop, never inside a tile; the lean is the frame's
 *       (a 1.6deg perspective tilt, the Deep End precedent) and is cleared on
 *       a deadline.
 *   III never still   - the lamp breathes, the motes drift, the marquee crawls
 *       (timed) or breathes (zen) even at heat 0.
 *   IV  images > text - the only words this deck can show (the near-miss word,
 *       the jackpot word, the solve word) come from the lexicon rows CORE
 *       ships and live for < 700ms.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|cp-casino|<tag>'
 *       (append-only tags; a new tag never shifts an old stream). No
 *       Math.random anywhere.
 *   VI  exits sacred  - capsOk false disarms the whole floor (no nodes, no
 *       cues); reduced motion keeps a static dim lamp + marquee (style.js
 *       kills the animations, this file skips the glints, the lean and the
 *       flashes); the stage's .suspended rule freezes the chase; every timer
 *       lives in the game's pause-aware registry AND a local set, so destroy()
 *       cannot leak one.
 *   VII strings      - cp_near_miss / cp_jackpot / cp_stamp_solved through
 *       the t() this deck is handed; nothing else is rendered as text.
 *
 * ENGINE vs GAME-LOCAL: cues (audio_trigger) and the jackpot / near_miss
 * ceremonies go through opts.engine (CORE's deckEngine weld - clickSafe
 * forced, audio ceiling by tier, pitch passthrough; ceremony() is optional on
 * that weld and skipped when absent). The light is game-local: it sits on the
 * game's own geometry (the easel, the canvas), which no engine primitive
 * knows. Node budget: backdrop layers (7) + marquee (1 + 4 bars) + overlay (1
 * + the word) + live glints (<= MAX_GLINTS), all reused or budgeted.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const CP_CASINO = Object.freeze({
  /* ---- the marquee --------------------------------------------------- */
  MQ_T_SLOW: 3.0,               // chase period (s) at heat 0
  MQ_T_FAST: 0.9,               // at heat 1
  MQ_A_LO: 0.22,                // presence band
  MQ_A_HI: 0.8,
  MQ_T_BELL: 0.45,
  MQ_A_BELL: 0.95,
  MQ_A_ZEN: 0.34,               // zen: one calm presence, no chase
  FLASH_MS: 700,
  DEEP_MS: 1300,
  /* ---- identity ------------------------------------------------------ */
  OFF_ARC: 0.06,
  HUE_ARC: Object.freeze([262, 345]),       // violet -> rose
  HUE_OFF: Object.freeze([168, 200]),       // the teal night
  WALLS: Object.freeze(['plaster', 'linen', 'brick', 'velvet']),
  WOODS: Object.freeze(['#4a3224', '#5a3d2a', '#3a2a22', '#6b4a30', '#2f2420']),
  BREATH_S: Object.freeze([5.5, 9.5]),
  DRIFT_S: Object.freeze([18, 34]),
  KB_S: Object.freeze([24, 38]),
  LAMP_X: Object.freeze([28, 72]),          // % across the stage
  LAMP_Y: Object.freeze([-14, -2]),
  ASSET_ALPHA: 0.06,
  /* ---- the payout ---------------------------------------------------- */
  MAX_GLINTS: 36,
  GLINT_LOCK: 5,
  GLINT_JACK: 10,
  GLINT_ROYAL: 26,
  LEAN_MS: 240,
  LEAN_MAG: 0.55,
  /* ---- the sound ladder (engine audio_trigger, pitch = the streak) ---- */
  SEMITONE_CAP: 7,
  SLIDE_LEVEL: Object.freeze([0.14, 0.3]),  // by heat
  LOCK_LEVEL: Object.freeze([0.3, 0.5]),    // by min(streak,8)/8
  THRASH_LEVEL: 0.22,
  ASSIST_LEVEL: 0.3,
  JACK_LEVEL: 0.5,
  NEAR_LEVEL: 0.3,
  DIM_LEVEL: 0.25,
  ZEN_MUL: 0.55,                 // zen: every cue at about half
  /* ---- the almost ---------------------------------------------------- */
  ALMOST_GAP_MS: 6000,
  ALMOST_MS: 1300,
  WORD_MS: 660,
  /* ---- the jackpot ladder -------------------------------------------- */
  MINOR_I: 0.35,
  MAJOR_I: 0.65,
  ROYAL_I: 1,
  ROYAL_MS: 3400,
  END_DIM_MS: 1400,
  HEAT_REPAINT_STEP: 0.03,
});

const STYLE_ID = 'g-cp-casino-style';
const STYLE_TEXT = `
/* ---- CASINO (House Rules, Deck II) --------------------------------------- */
/* THE MARQUEE: a bulb-chase frame hugging the canvas, inside the easel (on the
   mat). Dots are gradients, the chase a background-position crawl. Pace
   (--g-cp-mqt) and presence (--g-cp-mqa) ride the class heat from casino.js;
   the bell and the royal turn it gold. ZEN (.g-cp-mq-zen): no chase - the bulbs
   breathe together, slowly. pointer-events:none is LAW. */
/* the bulb ring runs IN THE WOOD band (z2: over the easel's ::before, never over
   the board, which is z1 and ends at the mat) - pink bulbs on dark wood read;
   on the light mat they vanished (rig shot 04, 2026-08-23). */
.g-cp-mq{position:absolute;left:50%;top:50%;z-index:2;pointer-events:none;
  width:calc(var(--cp-board) + 2 * var(--cp-mat) + var(--cp-wood));height:calc(var(--cp-board) + 2 * var(--cp-mat) + var(--cp-wood));
  transform:translate(-50%,-50%);border-radius:4px;
  opacity:var(--g-cp-mqa,.26);transition:opacity .6s ease}
.g-cp-mq i{position:absolute;display:block;
  background-image:radial-gradient(circle, var(--cp-n-mq, hsl(var(--cp-hue-a),80%,74%)) 1.6px, transparent 2.6px)}
.g-cp-mq .mq-t,.g-cp-mq .mq-b{left:0;right:0;height:6px;background-size:16px 6px;background-repeat:repeat-x}
.g-cp-mq .mq-l,.g-cp-mq .mq-r{top:0;bottom:0;width:6px;background-size:6px 16px;background-repeat:repeat-y}
.g-cp-mq .mq-t{top:0;animation:g-cp-mqx var(--g-cp-mqt,2s) linear infinite var(--g-cp-mqp,0s)}
.g-cp-mq .mq-r{right:0;animation:g-cp-mqy var(--g-cp-mqt,2s) linear infinite var(--g-cp-mqp,0s)}
.g-cp-mq .mq-b{bottom:0;animation:g-cp-mqxr var(--g-cp-mqt,2s) linear infinite var(--g-cp-mqp,0s)}
.g-cp-mq .mq-l{left:0;animation:g-cp-mqyr var(--g-cp-mqt,2s) linear infinite var(--g-cp-mqp,0s)}
@keyframes g-cp-mqx{to{background-position-x:16px}}
@keyframes g-cp-mqxr{to{background-position-x:-16px}}
@keyframes g-cp-mqy{to{background-position-y:16px}}
@keyframes g-cp-mqyr{to{background-position-y:-16px}}
.g-cp-mq.g-cp-mq-zen i{animation:g-cp-mqbreathe calc(var(--cp-breath) * 1.2) ease-in-out infinite alternate}
@keyframes g-cp-mqbreathe{from{opacity:.4;filter:none}to{opacity:1;filter:drop-shadow(0 0 4px var(--cp-n-mq, hsl(var(--cp-hue-a),80%,74%)))}}
.g-cp-mq.g-cp-mq-bell{--cp-n-mq:var(--gold);filter:drop-shadow(0 0 6px rgba(240,194,75,.75))}
.g-cp-mq.g-cp-mq-flash{animation:g-cp-mqflash .6s ease-out 1}
@keyframes g-cp-mqflash{
  0%{opacity:1;filter:brightness(var(--g-cp-mqf,1.4)) drop-shadow(0 0 10px rgba(184,166,232,.9))}
  100%{opacity:var(--g-cp-mqa,.26);filter:none}}
.g-cp-mq.g-cp-mq-bell.g-cp-mq-flash{animation:g-cp-mqflashgold .6s ease-out 1}
@keyframes g-cp-mqflashgold{
  0%{opacity:1;filter:brightness(var(--g-cp-mqf,1.4)) drop-shadow(0 0 14px rgba(240,194,75,.95))}
  100%{opacity:var(--g-cp-mqa,.26);filter:drop-shadow(0 0 6px rgba(240,194,75,.75))}}
.g-cp-mq.g-cp-mq-out{opacity:0;transition:opacity 1.6s ease}
/* THE OVERLAY: casino decoration above the board (glints, the word, the hum).
   No pointer, ever. */
.g-cp-cs{position:absolute;inset:0;z-index:5;pointer-events:none;overflow:visible}
.g-cp-cs *{pointer-events:none}
/* a glint: a spark born at a locked tile, rising, gone */
.g-cp-glint{position:absolute;left:var(--x,50%);top:var(--y,50%);width:var(--s,6px);height:var(--s,6px);
  margin:calc(var(--s,6px) * -.5) 0 0 calc(var(--s,6px) * -.5);border-radius:50%;
  background:radial-gradient(circle, #fff, var(--cp-n-mq, hsl(var(--cp-hue-b),80%,78%)) 50%, transparent 72%);
  box-shadow:0 0 8px var(--cp-n-mq, hsl(var(--cp-hue-b),80%,78%));
  animation:g-cp-glint var(--d,1.1s) ease-out 1 forwards}
@keyframes g-cp-glint{
  0%{opacity:0;transform:translate(0,0) scale(.4)}
  15%{opacity:1}
  100%{opacity:0;transform:translate(var(--wx,0px), calc(var(--h,90px) * -1)) scale(1.1)}}
/* the word: ALMOST / a jackpot line, under the canvas, gone in 600ms */
.g-cp-cs-word{position:absolute;left:50%;top:100%;transform:translate(-50%,-40%);opacity:0;
  font-family:var(--disp);font-weight:800;font-size:clamp(13px,2vmin,20px);letter-spacing:.22em;text-transform:uppercase;
  color:hsl(var(--cp-hue-b),90%,84%);text-shadow:0 0 12px hsla(var(--cp-hue-b),90%,70%,.8);white-space:nowrap}
.g-cp-cs-word.on{animation:g-cp-word .62s ease-out 1 forwards}
.g-cp-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-cp-cs-word.gold{color:#ffe08a;text-shadow:0 0 14px rgba(240,194,75,.9)}
@keyframes g-cp-word{0%{opacity:0;transform:translate(-50%,-30%) scale(.8)}18%{opacity:1;transform:translate(-50%,-40%) scale(1.06)}
  70%{opacity:1;transform:translate(-50%,-44%) scale(1)}100%{opacity:0;transform:translate(-50%,-60%) scale(1)}}
/* the hum: the canvas glows from inside for a beat (the almost / a jackpot) */
.g-cp-cs::after{content:"";position:absolute;inset:-4px;border-radius:4px;opacity:0;
  box-shadow:inset 0 0 60px hsla(var(--cp-hue-b),80%,70%,.55), 0 0 36px hsla(var(--cp-hue-b),80%,70%,.4)}
.g-cp-cs.g-cp-hum::after{animation:g-cp-hum 1.3s ease-in-out 1}
@keyframes g-cp-hum{0%{opacity:0}30%{opacity:.9}60%{opacity:.45}80%{opacity:.75}100%{opacity:0}}
.g-cp-cs.g-cp-royal::after{opacity:1;
  box-shadow:inset 0 0 80px color-mix(in srgb, var(--gold), transparent 40%), 0 0 60px color-mix(in srgb, var(--gold), transparent 45%);
  animation:g-cp-royalhum 1.1s ease-in-out infinite alternate}
@keyframes g-cp-royalhum{from{opacity:.6}to{opacity:1}}
.g-cp-stage[data-mode="zen"] .g-cp-cs.g-cp-royal::after{
  box-shadow:inset 0 0 60px hsla(36,80%,70%,.45), 0 0 40px hsla(36,80%,70%,.35)}
/* reduced motion (both gates) */
html.arc-reduced .g-cp-mq{opacity:.16}
html.arc-reduced .g-cp-glint{opacity:0}
html.arc-reduced .g-cp-cs-word.on{opacity:1;transition:opacity .5s ease}
@media (prefers-reduced-motion: reduce){
  .g-cp-mq{opacity:.16}
  .g-cp-glint{opacity:0}
  .g-cp-cs-word.on{opacity:1;transition:opacity .5s ease}
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

/** The lock chime's pitch for a streak: +1 semitone per link, capped. Pure. */
export function pitchForStreak(streak) {
  const s = Math.max(0, Math.min(CP_CASINO.SEMITONE_CAP, (Number(streak) || 0) - 1));
  return +Math.pow(2, s / 12).toFixed(4);
}
/** Which rung of the jackpot ladder a lock count reaches. Pure.
 *  'minor' when the count crosses a whole row, 'major' at half the pieces,
 *  null otherwise. The royal is the solve, not a count. */
export function jackpotFor(prevCount, count, n) {
  const size = Math.max(3, Math.min(5, Math.round(Number(n) || 3)));
  const pieces = size * size - 1;
  const a = Math.max(0, Number(prevCount) || 0);
  const b = Math.max(0, Number(count) || 0);
  if (b <= a) return null;
  const half = Math.ceil(pieces / 2);
  if (a < half && b >= half) return 'major';
  if (Math.floor(b / size) > Math.floor(a / size) && b < pieces) return 'minor';
  return null;
}
/** Did a tile skirt its home? Pure: one off before, still one off after, moved. */
export function isAlmost(before, after, home) {
  if (!before || !after || !home) return false;
  if (before.r === after.r && before.c === after.c) return false;
  const d0 = Math.abs(before.r - home.r) + Math.abs(before.c - home.c);
  const d1 = Math.abs(after.r - home.r) + Math.abs(after.c - home.c);
  return d0 === 1 && d1 === 1;
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
/** A tile's honest row/col, read (never written) off index's inline vars. */
function gridOf(tile) {
  if (!tile || !tile.style) return null;
  try {
    const r = parseFloat(tile.style.getPropertyValue('--r'));
    const c = parseFloat(tile.style.getPropertyValue('--c'));
    if (Number.isFinite(r) && Number.isFinite(c)) return { r, c };
  } catch (e) { /* fall through */ }
  return null;
}
/** A tile's home as row/col: data-home is the integer index (board.js). */
function homeOf(tile, n) {
  let raw = null;
  try { raw = tile && tile.getAttribute ? tile.getAttribute('data-home') : null; } catch (e) { raw = null; }
  if (raw == null) return null;
  const s = String(raw);
  if (s.indexOf(',') >= 0) {
    const p = s.split(',');
    const r = parseInt(p[0], 10); const c = parseInt(p[1], 10);
    return Number.isFinite(r) && Number.isFinite(c) ? { r, c } : null;
  }
  const idx = parseInt(s, 10);
  if (!Number.isFinite(idx) || !(n > 0)) return null;
  return { r: Math.floor(idx / n), c: idx % n };
}
function attrNum(node, name, fallback) {
  try { const v = Number(node.getAttribute(name)); return Number.isFinite(v) && v > 0 ? v : fallback; } catch (e) { return fallback; }
}

/**
 * @param {Object} o
 * @param {string}   o.seed      the class seed (retakes replay the studio)
 * @param {number}   o.tier      1..4
 * @param {Object}   o.stage     .g-cp-stage (identity props + classes land here)
 * @param {Object}   o.board     .g-cp-board (geometry reference; tiles are read here)
 * @param {Object=}  o.frame     .g-cp-frame (marquee + overlay host; board.parentNode if absent)
 * @param {Object=}  o.hud       { moves, clock, locked, calm } chip elements (optional)
 * @param {Object}   o.backdrop  .g-cp-backdrop (the lighting layers)
 * @param {Object}   o.timers    {after(ms,fn)->id, every?, clear|cancel(id)} pause-aware
 * @param {boolean}  o.reduced   reduced motion
 * @param {boolean}  o.capsOk    false when bgIntensity is capped to 0
 * @param {Function=} o.t        ctx.lexicon (optional; English fallbacks here)
 * @param {Object=}  o.engine    CORE's deckEngine {fire, sustain, stop, channels, ceremony?}
 * @param {Object=}  o.assets    {next(kind)} live reader (Deck VI asset chrome)
 * @param {string=}  o.mode      'timed' | 'zen' (else read off stage data-mode)
 * @param {Function=} o.log
 */
export function createCpCasino(o) {
  const opts = o || {};
  const C = CP_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  const reduced = !!opts.reduced;
  /* THE bgIntensity DECOUPLE (W2, owner ruling). This deck used to weld capsOk
   * into its CONSTRUCTION arming, so bgIntensity 0 did not dim the floor - it
   * DELETED it, cues and all: the slide whoosh, the lock chime, the thrash
   * thud and the near-miss were every one of them gone. They are the class's
   * own beats, not decoration. So the gate splits: armed is the VISUAL gate
   * and keeps capsOk (Law VI - no light, no node, no timer-driven dressing);
   * sounds() is the audio gate and does not. */
  const armedBase = !!opts.stage && !!opts.board && !!opts.backdrop
    && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  const armed = armedBase && !!opts.capsOk;
  const sounds = () => armedBase && !destroyed;
  const eng = opts.engine || {};
  const hud = opts.hud || {};
  const frame = opts.frame || (opts.board && opts.board.parentNode) || opts.stage;
  let mode = String(opts.mode || '').toLowerCase();
  if (mode !== 'zen' && mode !== 'timed') {
    try { mode = String(opts.stage.getAttribute('data-mode') || 'timed').toLowerCase() === 'zen' ? 'zen' : 'timed'; } catch (e) { mode = 'timed'; }
  }
  const zen = mode === 'zen';

  /* timers: the game's registry (pause-aware) + a local set so destroy() can
     drop every one of ours without knowing the registry's shape. */
  const live = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  function after(ms, fn) {
    if (!armedBase || destroyed) return 0;   // the cue ladders ride these too (W2)
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

  const seedBase = String(opts.seed || 'cp') + '|cp-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, flashes: 0, words: 0, glints: 0, slides: 0, locks: 0, thrashes: 0, assists: 0 };
  const jackLog = [];
  function cue(name, level, extra) {
    if (!sounds()) return;
    counts.cues += 1;
    const lv = clamp01(level) * (zen ? C.ZEN_MUL : 1);
    try {
      if (typeof eng.fire === 'function') eng.fire('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    } catch (e) { /* a refused cue is not an error */ }
  }
  function ceremony(kind, o2) {
    if (!armed || destroyed) return null;
    try { return typeof eng.ceremony === 'function' ? eng.ceremony(kind, o2 || {}) : null; } catch (e) { return null; }
  }

  let destroyed = false;
  let started = false;
  let mq = null;
  let cs = null;
  let word = null;
  const layers = {};
  const props = new Set();
  const stageClasses = new Set();
  let identity = null;
  let heat = 0;
  let lastPaintHeat = -1;
  let bellOn = false;
  let royalOn = false;
  let outOn = false;
  let streak = 0;
  let bestStreak = 0;
  let lastCount = 0;
  let glints = 0;
  let flashTimer = 0; let deepTimer = 0; let humTimer = 0; let wordTimer = 0; let leanTimer = 0; let royalTimer = 0;
  let lastAlmostAt = -1e9;
  let almosts = 0;
  let assetUrl = null;
  const pos = new Map();           // data-id -> {r,c} after the last slide (read-only snapshot)

  function nowMs() {
    try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
    return Date.now();
  }

  /* ---------------------------------------------------- the studio identity */
  function setProp(k, v) {
    if (!opts.stage || !opts.stage.style) return;
    try { opts.stage.style.setProperty(k, v); props.add(k); } catch (e) { /* ignore */ }
  }
  function stageClass(name, on) {
    if (!opts.stage || !opts.stage.classList) return;
    try {
      if (on) { opts.stage.classList.add(name); stageClasses.add(name); }
      else { opts.stage.classList.remove(name); stageClasses.delete(name); }
    } catch (e) { /* ignore */ }
  }

  /** Draw the identity in a FIXED order (append-only, or every studio reskins). */
  function drawIdentity() {
    const offArc = roll('arc') < C.OFF_ARC;
    let hueA; let hueB;
    if (offArc) {
      hueA = lerp(C.HUE_OFF, roll('hue'));
      hueB = Math.max(160, Math.min(205, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (8 + roll('hue2') * 12)));
    } else {
      hueA = lerp(C.HUE_ARC, roll('hue'));
      hueB = Math.max(250, Math.min(350, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (16 + roll('hue2') * 24)));
    }
    const wall = C.WALLS[Math.min(C.WALLS.length - 1, Math.floor(roll('wall') * C.WALLS.length))];
    const wood = C.WOODS[Math.min(C.WOODS.length - 1, Math.floor(roll('wood') * C.WOODS.length))];
    const breath = lerp(C.BREATH_S, roll('breath'));
    const drift = lerp(C.DRIFT_S, roll('drift'));
    const kb = lerp(C.KB_S, roll('kb'));
    const lampX = lerp(C.LAMP_X, roll('lamp'));
    const lampY = lerp(C.LAMP_Y, roll('lamp'));
    const mqPhase = roll('mq-phase') * -3;
    const motes = 0.3 + roll('motes') * 0.35;
    identity = {
      offArc, hueA: Math.round(hueA), hueB: Math.round(hueB), wall, wood,
      breath: +breath.toFixed(1), drift: +drift.toFixed(1), kb: +kb.toFixed(1),
      lampX: Math.round(lampX), lampY: Math.round(lampY), mqPhase: +mqPhase.toFixed(2), motes: +motes.toFixed(2),
    };
  }

  function nextAsset() {
    const a = opts.assets;
    if (!a || typeof a.next !== 'function') return null;
    try {
      const got = a.next('still') || a.next('loop');
      return got && got.url ? String(got.url) : null;
    } catch (e) { return null; }
  }

  function dressStudio() {
    if (!identity) drawIdentity();
    const id = identity;
    setProp('--cp-n-hue-a', String(id.hueA));
    setProp('--cp-n-hue-b', String(id.hueB));
    setProp('--cp-n-mq', 'hsl(' + id.hueA + ',80%,74%)');
    setProp('--cp-n-wood', id.wood);
    setProp('--cp-n-breath', id.breath + 's');
    setProp('--cp-n-drift', id.drift + 's');
    setProp('--cp-n-kb', id.kb + 's');
    setProp('--cp-n-lamp-x', id.lampX + '%');
    setProp('--cp-n-lamp-y', id.lampY + '%');
    setProp('--cp-n-a-motes', String(id.motes));
    setProp('--cp-n-lamp', zen ? '0.82' : '0.7');
    setProp('--cp-n-dark', '0');
    stageClass('g-cp-wall-' + id.wall, true);
    // Deck VI asset chrome: the player's own still as the wall's whisper
    if (!assetUrl) {
      assetUrl = nextAsset();
      if (assetUrl) {
        const safe = assetUrl.replace(/["\\]/g, '');
        setProp('--cp-n-asset', 'url("' + safe + '")');
        setProp('--cp-n-a-asset', String(C.ASSET_ALPHA));
      }
    }
    say('casino: studio dressed (hue ' + id.hueA + '/' + id.hueB + (id.offArc ? ' OFF-ARC' : '')
      + ', ' + id.wall + ' wall, lamp ' + id.lampX + '%' + (zen ? ', ZEN' : '') + (assetUrl ? ', asset chrome' : '') + ')');
  }

  /* ------------------------------------------------------------- the DOM */
  function mountBackdrop() {
    const host = opts.backdrop;
    if (!host || !host.appendChild) return;
    for (const name of ['wall', 'lamp', 'motes', 'dark', 'flash', 'royal', 'vig']) {
      const n = el('div', 'g-cp-bd g-cp-bd-' + name);
      if (!n) continue;
      layers[name] = n;
      host.appendChild(n);
    }
  }
  function mountMarquee() {
    if (mq || !frame || !frame.appendChild) return;
    mq = el('div', 'g-cp-mq' + (zen ? ' g-cp-mq-zen' : ''));
    if (!mq) return;
    for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) {
      const bar = el('i', cls);
      if (bar) mq.appendChild(bar);
    }
    if (mq.style && identity) mq.style.setProperty('--g-cp-mqp', identity.mqPhase + 's');
    // the marquee sits UNDER the board (z0 vs the board's z1): insert first
    try { if (typeof frame.insertBefore === 'function' && frame.firstChild) frame.insertBefore(mq, frame.firstChild); else frame.appendChild(mq); }
    catch (e) { try { frame.appendChild(mq); } catch (e2) { mq = null; } }
  }
  function mountOverlay() {
    if (cs || !frame || !frame.appendChild) return;
    cs = el('div', 'g-cp-cs');
    if (!cs) return;
    frame.appendChild(cs);
    word = el('span', 'g-cp-cs-word');
    if (word && cs.appendChild) cs.appendChild(word);
  }

  /* ------------------------------------------------------------ the light */
  function paintMarquee(force) {
    if (!mq || !mq.style) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    let tS; let a;
    if (zen) { tS = C.MQ_T_SLOW; a = C.MQ_A_ZEN; }
    else {
      tS = bellOn ? C.MQ_T_BELL : (C.MQ_T_SLOW - (C.MQ_T_SLOW - C.MQ_T_FAST) * heat);
      a = bellOn ? C.MQ_A_BELL : C.MQ_A_LO + (C.MQ_A_HI - C.MQ_A_LO) * heat;
    }
    if (outOn) a = 0;
    mq.style.setProperty('--g-cp-mqt', tS.toFixed(2) + 's');
    mq.style.setProperty('--g-cp-mqa', a.toFixed(2));
  }
  function flashMarquee(strength) {
    if (!mq || !mq.classList || reduced) return;
    counts.flashes += 1;
    mq.classList.remove('g-cp-mq-flash');
    if (typeof mq.offsetWidth === 'number') void mq.offsetWidth;
    mq.style.setProperty('--g-cp-mqf', String(Math.max(1, Math.min(2.2, strength))));
    mq.classList.add('g-cp-mq-flash');
    if (flashTimer) cancel(flashTimer);
    flashTimer = after(C.FLASH_MS, () => { flashTimer = 0; if (mq && mq.classList) mq.classList.remove('g-cp-mq-flash'); });
  }
  function pulseLamp(strength, deep) {
    const f = layers.flash;
    if (!f || !f.classList || reduced) return;
    f.classList.remove('g-cp-on', 'g-cp-deep');
    if (typeof f.offsetWidth === 'number') void f.offsetWidth;
    setProp('--cp-n-pay', clamp01(strength).toFixed(2));
    f.classList.add(deep ? 'g-cp-deep' : 'g-cp-on');
    if (deepTimer) cancel(deepTimer);
    deepTimer = after(deep ? C.DEEP_MS : C.FLASH_MS, () => { deepTimer = 0; if (f.classList) f.classList.remove('g-cp-on', 'g-cp-deep'); });
  }
  function hum() {
    if (!cs || !cs.classList) return;
    cs.classList.remove('g-cp-hum');
    if (typeof cs.offsetWidth === 'number') void cs.offsetWidth;
    cs.classList.add('g-cp-hum');
    if (humTimer) cancel(humTimer);
    humTimer = after(C.ALMOST_MS, () => { humTimer = 0; if (cs && cs.classList) cs.classList.remove('g-cp-hum'); });
  }
  function showWord(key, fallback, tone) {
    if (!word) return;
    counts.words += 1;
    try { word.textContent = t(key, fallback); } catch (e) { return; }
    word.className = 'g-cp-cs-word' + (tone ? ' ' + tone : '');
    if (typeof word.offsetWidth === 'number') void word.offsetWidth;
    if (word.classList) word.classList.add('on');
    if (wordTimer) cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 40, () => { wordTimer = 0; if (word && word.classList) word.classList.remove('on'); });
  }
  function leanFrame(x, y, ms) {
    if (!frame || !frame.style || reduced || zen) return;
    try {
      frame.style.setProperty('--cp-lean-x', x.toFixed(2));
      frame.style.setProperty('--cp-lean-y', y.toFixed(2));
    } catch (e) { return; }
    if (leanTimer) cancel(leanTimer);
    leanTimer = after(ms, () => { leanTimer = 0; clearLean(); });
  }
  function clearLean() {
    if (!frame || !frame.style) return;
    try { frame.style.removeProperty('--cp-lean-x'); frame.style.removeProperty('--cp-lean-y'); } catch (e) { /* ignore */ }
  }

  /* ------------------------------------------------------------- glints */
  /** Frame-relative centre of a tile (or of the board when none). */
  function anchorOf(tileEl) {
    const fr = rectOf(frame);
    if (!fr) return null;
    const tr = rectOf(tileEl) || rectOf(opts.board);
    if (!tr || !tr.width) return { x: fr.width / 2, y: fr.height / 2, w: 0, h: 0 };
    return { x: tr.left - fr.left + tr.width / 2, y: tr.top - fr.top + tr.height / 2, w: tr.width, h: tr.height };
  }
  function glintsAt(x, y, count, spread, gold) {
    if (!cs || !cs.appendChild || reduced) return 0;
    let made = 0;
    for (let i = 0; i < count; i++) {
      if (glints >= C.MAX_GLINTS) break;
      const g = el('i', 'g-cp-glint');
      if (!g || !g.style) break;
      const s = 3 + roll('gl-s') * 5;
      const d = 0.8 + roll('gl-d') * 0.9;
      const gx = x + (roll('gl-x') - 0.5) * (spread || 40);
      const gy = y + (roll('gl-y') - 0.5) * ((spread || 40) * 0.4);
      g.style.setProperty('--x', gx.toFixed(0) + 'px');
      g.style.setProperty('--y', gy.toFixed(0) + 'px');
      g.style.setProperty('--s', s.toFixed(1) + 'px');
      g.style.setProperty('--d', d.toFixed(2) + 's');
      g.style.setProperty('--h', (40 + roll('gl-h') * 70).toFixed(0) + 'px');
      g.style.setProperty('--wx', ((roll('gl-w') - 0.5) * 30).toFixed(0) + 'px');
      if (gold) g.style.setProperty('--cp-n-mq', 'var(--gold)');
      cs.appendChild(g);
      glints += 1; made += 1; counts.glints += 1;
      after(d * 1000 + 120, () => { glints = Math.max(0, glints - 1); try { g.remove(); } catch (e) { /* ignore */ } });
    }
    return made;
  }

  /* ----------------------------------------------------------- the tiles */
  function tileById(id) {
    if (!opts.board || typeof opts.board.querySelector !== 'function') return null;
    try { return opts.board.querySelector('.g-cp-tile[data-id="' + String(id).replace(/["\\]/g, '') + '"]'); } catch (e) { return null; }
  }
  function allTiles() {
    if (!opts.board || typeof opts.board.querySelectorAll !== 'function') return [];
    try { return Array.from(opts.board.querySelectorAll('.g-cp-tile')); } catch (e) { return []; }
  }
  function gridN() {
    let n = 0;
    if (opts.stage) n = attrNum(opts.stage, 'data-n', 0);
    if (!n && opts.board && opts.board.style) { try { n = parseInt(opts.board.style.getPropertyValue('--cp-n'), 10) || 0; } catch (e) { n = 0; } }
    return n || 3;
  }
  /** grid size from either shape: 3..5 = the grid, 8/15/24 (anything > 5) = the tile count. */
  function gridSizeArg(n) {
    const v = Math.round(Number(n) || 0);
    if (v > 5) { const root = Math.round(Math.sqrt(v + 1)); return Math.max(3, Math.min(5, root)); }
    if (v >= 3) return Math.min(5, v);
    return Math.max(3, Math.min(5, gridN()));
  }
  function snapshot() {
    const next = new Map();
    for (const tile of allTiles()) {
      let id = null;
      try { id = tile.getAttribute('data-id'); } catch (e) { id = null; }
      const g = gridOf(tile);
      if (id != null && g) next.set(String(id), g);
    }
    pos.clear();
    for (const [k, v] of next) pos.set(k, v);
  }

  /* ------------------------------------------------------------ the ladder */
  function jackpot(kind, intensity, tileEl) {
    counts.jackpots += 1;
    jackLog.push(kind);
    const a = anchorOf(tileEl);
    if (zen) {
      // zen: a warm pulse and a soft chime; no flood, no word
      pulseLamp(0.35, false);
      cue('streak', C.JACK_LEVEL * 0.8, { pitch: 1 });
      if (a) glintsAt(a.x, a.y, C.GLINT_JACK, (a.w || 60) * 0.8, false);
      return;
    }
    ceremony('jackpot', { intensity });
    cue('jackpot', C.JACK_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashMarquee(1.4 + 0.6 * intensity);
    pulseLamp(0.45 + 0.35 * intensity, kind === 'major');
    hum();
    showWord('cp_jackpot', 'JACKPOT', kind === 'major' ? 'gold' : '');
    if (a) glintsAt(a.x, a.y, C.GLINT_JACK + (kind === 'major' ? 4 : 0), (a.w || 60) * 0.9, kind === 'major');
  }
  function nearMiss(tileEl) {
    counts.nearMisses += 1;
    almosts += 1;
    ceremony('near_miss', { intensity: 0.55 });
    cue('near_miss', C.NEAR_LEVEL, { pitch: 0.9 });
    hum();
    showWord('cp_near_miss', 'SO CLOSE', 'lav');
    const a = anchorOf(tileEl);
    if (a && !zen) glintsAt(a.x, a.y, 3, (a.w || 60) * 0.6, false);
  }

  /* ---------------------------------------------------------------- api */
  const api = {
    /** Dress the studio + light the frame. Call when play arms. */
    start() {
      if (started) return;
      started = true;
      /* DISARMED = DARK, NOT MUTE (W2): no style, no nodes, no dressing, but
       * started stays true so every beat below still finds its cue road. */
      if (!armed || destroyed) { say('casino: dark (bgIntensity 0) - cue road only'); return; }
      ensureStyle();
      drawIdentity();
      mountBackdrop();
      mountMarquee();
      mountOverlay();
      dressStudio();
      paintMarquee(true);
      snapshot();
      say('casino: floor lit, ' + Object.keys(layers).length + ' layers' + (zen ? ' (zen: quiet floor)' : ''));
    },

    /** Ride the class's own heat curve. index.js calls from its heat(). */
    setHeat(h) {
      heat = clamp01(h);
      if (started) paintMarquee(false);
    },

    /**
     * Every legal slide, AFTER index.js has written --r/--c. The tick, the
     * lean (from the tile's own travel, read off the snapshot) and the almost.
     */
    slide(ev) {
      if (!started || !sounds()) return;
      const e = ev || {};
      counts.slides += 1;
      const tile = e.id == null ? null : tileById(e.id);
      const before = e.id == null ? null : pos.get(String(e.id)) || null;
      const now = tile ? gridOf(tile) : null;
      // the tick: a short whoosh, level by heat, pitch creeping up with progress
      const n = gridN();
      const locked = Math.max(0, Number(lastCount) || 0);
      const frac = clamp01(locked / Math.max(1, n * n - 1));
      cue('slide', lerp(C.SLIDE_LEVEL, heat), { pitch: +(0.9 + 0.25 * frac).toFixed(3) });
      if (before && now) {
        const dx = Math.sign(now.c - before.c);
        const dy = Math.sign(now.r - before.r);
        if ((dx || dy) && armed) leanFrame(dx * C.LEAN_MAG, dy * C.LEAN_MAG, C.LEAN_MS);
        // THE ALMOST: skirted its home and is still one off (and not locked)
        if (!e.locked && tile) {
          const home = homeOf(tile, n);
          const tNow = nowMs();
          if (home && isAlmost(before, now, home) && tNow - lastAlmostAt >= C.ALMOST_GAP_MS) {
            lastAlmostAt = tNow;
            nearMiss(tile);
          }
        }
      }
      snapshot();
    },

    /** A tile (or more) sits home: the payout, the streak ladder, the jackpot rungs. */
    lock(count, n) {
      if (!started || !sounds()) return;   // every light below self-gates on a mounted node
      const cnt = Math.max(0, Number(count) || 0);
      // index.js hands the TILE COUNT (n*n-1 = 8/15/24) as the second argument; an older
      // caller may hand the grid size. Anything above 5 is a tile count - take its root.
      const size = gridSizeArg(n);
      const prev = lastCount;
      lastCount = cnt;
      if (cnt <= prev) return;           // a lock count that fell is not a payout
      counts.locks += 1;
      streak += 1;
      bestStreak = Math.max(bestStreak, streak);
      cue('pop', lerp(C.LOCK_LEVEL, Math.min(8, streak) / 8), { pitch: pitchForStreak(streak) });
      flashMarquee(1 + 0.1 * Math.min(6, streak));
      pulseLamp(0.25 + 0.06 * Math.min(6, streak), false);
      // glints rise off the freshest locked tile (the one not yet snapshotted as home)
      let tileEl = null;
      try {
        const lockedTiles = allTiles().filter((x) => x.classList && x.classList.contains('is-locked'));
        tileEl = lockedTiles.length ? lockedTiles[lockedTiles.length - 1] : null;
      } catch (e) { tileEl = null; }
      const a = anchorOf(tileEl);
      if (a) glintsAt(a.x, a.y, C.GLINT_LOCK + Math.min(4, streak), (a.w || 60) * 0.7, false);
      if (streak >= 3 && !zen) cue('streak', 0.3, { pitch: pitchForStreak(streak) });
      const jk = jackpotFor(prev, cnt, size);
      if (jk === 'minor') jackpot('minor', C.MINOR_I, tileEl);
      else if (jk === 'major') jackpot('major', C.MAJOR_I, tileEl);
    },

    /** A panic move (a backtrack or a thrash under a wash): the ladder resets. */
    thrash() {
      if (!started || !sounds()) return;
      counts.thrashes += 1;
      streak = 0;
      cue('bump', C.THRASH_LEVEL, { pitch: 0.7 });     // a muted thud, never silence
      if (armed && !reduced && frame && frame.style && !zen) leanFrame((roll('thrash') < 0.5 ? -1 : 1) * 0.4, 0, 160);
    },

    /** The rescue lit a piece: a soft whisper, the lamp dips and returns. */
    assist() {
      if (!started || !sounds()) return;
      counts.assists += 1;
      streak = 0;
      cue('whisper', C.ASSIST_LEVEL, { pitch: 0.85 });
      if (!armed) return;                              // the lamp is a LIGHT
      setProp('--cp-n-lamp', zen ? '0.62' : '0.5');
      after(900, () => setProp('--cp-n-lamp', zen ? '0.82' : '0.7'));
    },

    /** The picture is whole: THE ROYAL. Holds until dimOut/stop. */
    solved() {
      if (!started || !sounds()) return;
      royalOn = true;
      counts.jackpots += 1;
      jackLog.push('royal');
      if (armed) stageClass('g-cp-royal', true);
      if (cs && cs.classList) cs.classList.add('g-cp-royal');
      const b = rectOf(opts.board); const fr = rectOf(frame);
      const cx = b && fr ? b.left - fr.left + b.width / 2 : null;
      const cy = b && fr ? b.top - fr.top + b.height * 0.9 : null;
      if (zen) {
        // the warm royal: the lamp comes up, one soft chime, a few glints
        if (armed) setProp('--cp-n-lamp', '1');
        cue('streak', C.JACK_LEVEL, { pitch: 1.1 });
        after(320, () => cue('stamp', 0.35, { pitch: 1 }));
        pulseLamp(0.5, true);
        if (cx != null) glintsAt(cx, cy, 10, b.width * 0.8, false);
        say('casino: zen royal (warm)');
        return;
      }
      bellOn = true;
      if (mq && mq.classList) mq.classList.add('g-cp-mq-bell');
      paintMarquee(true);
      flashMarquee(2.2);
      pulseLamp(1, true);
      hum();
      ceremony('jackpot', { intensity: C.ROYAL_I });
      cue('jackpot', C.JACK_LEVEL, { pitch: 1.2 });
      after(260, () => cue('jackpot', C.JACK_LEVEL, { pitch: 1.5 }));
      after(520, () => cue('stamp', 0.5, { pitch: 1 }));
      showWord('cp_stamp_solved', 'COMPOSED', 'gold');
      if (cx != null) glintsAt(cx, cy, C.GLINT_ROYAL, b.width, true);
      if (royalTimer) cancel(royalTimer);
      royalTimer = after(C.ROYAL_MS, () => { royalTimer = 0; if (cs && cs.classList) cs.classList.remove('g-cp-royal'); });
      say('casino: ROYAL');
    },

    /**
     * THE BANK (multi-board, 2026-08-24). The royal was a beat, not the end of
     * the night: the class deals a fresh scramble and runs on. Take the royal
     * dressing back down and restore the marquee to whatever the REAL bell
     * warning is doing - `solved()` borrows `bellOn` for its own flash, so
     * without this the rig would sit in bell mode from the first bank onward.
     * `info.bell` is index.js's own bell-warning flag; it is the truth.
     */
    deal(info) {
      if (!started) return;
      const stillBell = !!(info && info.bell);
      royalOn = false;
      bellOn = stillBell;
      if (royalTimer) { cancel(royalTimer); royalTimer = 0; }
      if (mq && mq.classList) {
        mq.classList.remove('g-cp-mq-flash');
        if (stillBell) mq.classList.add('g-cp-mq-bell'); else mq.classList.remove('g-cp-mq-bell');
      }
      if (cs && cs.classList) cs.classList.remove('g-cp-royal');
      stageClass('g-cp-royal', false);
      stageClass('g-cp-bell', stillBell);
      setProp('--cp-n-dark', stillBell ? '0.35' : '0');
      paintMarquee(true);
      say('casino: deal (royal cleared)');
    },

    /** The last 20s: gold frame, frantic chase, the room darkens a stop. */
    bell(on) {
      bellOn = !!on;
      if (mq && mq.classList) { if (bellOn) mq.classList.add('g-cp-mq-bell'); else mq.classList.remove('g-cp-mq-bell'); }
      stageClass('g-cp-bell', bellOn);
      setProp('--cp-n-dark', bellOn ? '0.35' : '0');
      paintMarquee(true);
    },

    /** The bell took the board: the rig sighs out instead of cutting. */
    dimOut() {
      outOn = true;
      bellOn = false;
      royalOn = false;
      clearLean();
      if (mq && mq.classList) { mq.classList.remove('g-cp-mq-bell', 'g-cp-mq-flash'); mq.classList.add('g-cp-mq-out'); }
      if (cs && cs.classList) cs.classList.remove('g-cp-royal', 'g-cp-hum');
      stageClass('g-cp-royal', false);
      stageClass('g-cp-bell', false);
      stageClass('g-cp-out', true);
      setProp('--cp-n-dark', '0.6');
      // losses disguised: a dim payout, never silence
      pulseLamp(0.2, true);
      cue('wash', C.DIM_LEVEL, { pitch: 0.9 });
      paintMarquee(true);
    },

    /** The class is over; nothing may pulse again. */
    stop() {
      for (const id of [flashTimer, deepTimer, humTimer, wordTimer, leanTimer, royalTimer]) if (id) cancel(id);
      flashTimer = 0; deepTimer = 0; humTimer = 0; wordTimer = 0; leanTimer = 0; royalTimer = 0;
      clearLean();
      if (mq && mq.classList) mq.classList.remove('g-cp-mq-flash');
      if (cs && cs.classList) cs.classList.remove('g-cp-hum');
    },

    destroy() {
      destroyed = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      clearLean();
      if (mq) { try { mq.remove(); } catch (e) { /* ignore */ } }
      if (cs) { try { cs.remove(); } catch (e) { /* ignore */ } }
      for (const k of Object.keys(layers)) { try { layers[k].remove(); } catch (e) { /* ignore */ } delete layers[k]; }
      mq = null; cs = null; word = null; glints = 0;
      for (const name of Array.from(stageClasses)) stageClass(name, false);
      if (opts.stage && opts.stage.style) {
        for (const k of props) { try { opts.stage.style.removeProperty(k); } catch (e) { /* ignore */ } }
      }
      props.clear();
      pos.clear();
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, sounds: sounds(), started, zen, mode, marquee: !!mq, overlay: !!cs, layers: Object.keys(layers).length,
        bell: bellOn, royal: royalOn, out: outOn, heat, identity, assetUrl,
        streak, bestStreak, lastCount, glints, almosts,
        counts: Object.assign({}, counts), jackpots: jackLog.slice(), timers: live.size,
      };
    },
  };
  return api;
}

export default createCpCasino;
