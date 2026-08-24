/* ============================================================================
 * games/anomaly/casino.js - DECK II of the House Rules for the darkroom: THE
 * FLOOR. The grid is a contact sheet under a red safelight; the trickster
 * (trickster.js) lies about the room, the pressure (pressure.js) is what the
 * room does to you as your streak climbs. This file is the lighting rig and
 * the chime rack - the enlarger lamp, the safelight, the bulb string, the
 * payout light and the sound ladder.
 *
 *   DARKROOM IDENTITY  seeded per CLASS seed (Deck I): a safelight hue on the
 *                      red->amber arc (~6% of darkrooms leave the arc for a
 *                      green "infrared" night - a bonus round for loading in),
 *                      the enlarger lamp's position and tilt, a breathing
 *                      period, a drift period, the dust density, the ken-burns
 *                      roll, the frame number the contact sheet starts at, and
 *                      a JOURNEY of 2-4 stops through one morph space (the
 *                      lamp swings, the dust thickens, the safelight deepens)
 *                      walked by the class's own heat. Same seed, same room; a
 *                      retake develops the identical print.
 *   THE MARQUEE        a string of bulbs hugging the grid (a real node in the
 *                      overlay, sized from the grid's rect - never a child of
 *                      the grid, never a tile). Crawls at low heat, chases
 *                      hungrily at high heat, flashes on a payout, goes GOLD
 *                      for the bell and the royal, sighs out - never cuts - on
 *                      a dim-out.
 *   THE EXPOSURE       a correct first tap pays light in under 500ms: the
 *                      enlarger FLASHES (the backdrop's flash layer), a ring
 *                      blooms on the found frame (overlay, measured once), the
 *                      marquee flashes, the chime CLIMBS (+1 semitone per link
 *                      of the streak, capped +7 - the intake precedent). A
 *                      wrong tap is a muted thud and a cold pulse (the
 *                      safelight dips, the frame frosts) - never silence.
 *   NEAR-MISS STAGING  almost(): the word ALMOST near the last tapped frame,
 *                      the near_miss ceremony rises. A FAST find (<700ms) pings
 *                      the word FAST. Both are staging; the ledger already
 *                      decided. Staging only - CORE says when.
 *   THE JACKPOT LADDER minor: a fast find at streak >= 2 may roll one (seeded,
 *                      chance rises with heat); major: streak milestones
 *                      (3/5/8) pay one deterministically; ROYAL: once a class,
 *                      only when every tap was a first tap AND the streak
 *                      reached the royal line - then the darkroom floods gold.
 *                      A failed class still gets a DIM payout: silence is where
 *                      people stand up.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects index.js hands it AFTER its
 *       own accounting; writes nothing about rounds, streak, time or grade.
 *       It NEVER reads, infers or marks the odd tile: the only tile index it
 *       ever touches is the one the PLAYER tapped (public by then).
 *   II  input honest  - every node here is pointer-events:none and lives in
 *       the overlay / the backdrop, never inside the grid; .g-an-tile and
 *       .g-an-face are never written (no class, no style, no attribute).
 *   III never still   - the safelight breathes, the dust drifts, the lamp
 *       swings, the marquee crawls even at heat 0.
 *   IV  images > text - three words (ALMOST / FAST / ROYAL) through the
 *       lexicon, each alive for < 600ms.
 *   V   seeded        - per-tag mulberry32 off seed+'|an-casino|<tag>'
 *       (append-only tags; a new tag never shifts an old stream).
 *   VI  exits sacred  - capsOk false disarms every light; reduced motion keeps
 *       a static safelight + a dim frame (no chase, no punch, no ken-burns,
 *       blooms only); the stage's .suspended rule freezes the chase; timers
 *       ride the game's pause-aware registry AND a local set.
 *   VII lexicon       - an_almost / an_fast / an_royal only, through opts.t.
 *
 * ENGINE vs GAME-LOCAL: cues (audio_trigger) and the jackpot / near_miss
 * ceremonies go through opts.engine (CORE's weld: clickSafe forced, audio
 * ceiling by tier, pitch passthrough). The lights are game-local: they sit on
 * the game's own geometry (the grid, a tapped frame), which no engine
 * primitive knows. NODE BUDGET: backdrop layers (9) + marquee (1 + bulbs) +
 * overlay (1) + ring + frost + word + sparks (<= 12 live). No per-frame JS.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const AN_CASINO = Object.freeze({
  /* ---- the marquee --------------------------------------------------- */
  BULBS: Object.freeze([20, 24, 28]),          // seeded pick (per side count scales with n)
  MQ_T_SLOW: 3.0,                              // chase period (s) at heat 0
  MQ_T_FAST: 0.8,                              // at heat 1
  MQ_A_LO: 0.22,
  MQ_A_HI: 0.85,
  MQ_T_GOLD: 0.5,
  MQ_A_GOLD: 0.95,
  MQ_FLASH_MS: 520,
  MQ_INSET_PX: 14,                             // how far outside the grid the bulbs sit
  MQ_REMEASURE_MS: Object.freeze([16, 240]),   // after a roundStart: twice (layout settles)
  /* ---- the payout ---------------------------------------------------- */
  FLASH_MS: 420,                               // the enlarger exposure (outlives the CSS)
  FLASH_A: Object.freeze([0.22, 0.6]),         // by q (streak / 8)
  RING_MS: 480,
  FROST_MS: 700,
  COLD_MS: 520,
  PUNCH_MS: 220,
  PUNCH_SCALE: Object.freeze([0.05, 0.22]),
  SPARKS: Object.freeze([4, 9]),               // per find, by q
  MAX_SPARKS: 24,
  MILESTONES: Object.freeze([3, 5, 8]),
  /* ---- near misses --------------------------------------------------- */
  FAST_MS: 700,
  WORD_MS: 560,
  NEAR_MISS_I: Object.freeze({ almost: 0.65, fast: 0.4 }),
  /* ---- the jackpot ladder -------------------------------------------- */
  MINOR_FROM_STREAK: 2,
  MINOR_CHANCE: Object.freeze([0.12, 0.32]),   // fast find, by heat
  MINOR_I: 0.35,
  MAJOR_I: Object.freeze([0.5, 0.7, 0.95]),    // by milestone index
  ROYAL_MIN_STREAK: 6,
  ROYAL_I: 1,
  ROYAL_MS: 3200,
  END_DIM_MS: 1400,
  /* ---- the sound ladder ---------------------------------------------- */
  SEMITONE_CAP: 7,
  FIND_LEVEL: Object.freeze([0.3, 0.5]),       // by min(streak,8)/8
  MISS_LEVEL: 0.22,
  ADVANCE_LEVEL: 0.16,
  MOVED_LEVEL: 0.24,
  MILESTONE_LEVEL: 0.45,
  JACKPOT_LEVEL: 0.55,
  NEAR_LEVEL: 0.3,
  /* ---- identity ------------------------------------------------------ */
  OFF_ARC: 0.06,
  HUE_ARC: Object.freeze([352, 378]),          // red -> amber (wraps past 360)
  HUE_OFF: Object.freeze([150, 176]),          // the green night
  BREATH_S: Object.freeze([5.5, 9.5]),
  DRIFT_S: Object.freeze([18, 30]),
  KB_S: Object.freeze([22, 34]),
  DUST: Object.freeze([0.25, 0.7]),
  FRAME_START: 37,                             // contact sheets never start at 1
  STOPS_MIN: 2,
  STOPS_SPAN: 3,
  STOP_HYST: 0.05,
  HEAT_REPAINT_STEP: 0.03,
});

const STYLE_ID = 'g-an-casino-style';
/* Every rule below is the casino's OWN sheet (style.js owns the room, this
   owns the rig). No hidden-attribute rule lives here, by law. */
const STYLE_TEXT = `
.g-an-cs{position:absolute;inset:0;z-index:3;pointer-events:none;overflow:hidden}
.g-an-cs *{pointer-events:none}
/* THE MARQUEE: a rectangle of bulbs sized from the grid's rect (casino.js
   writes left/top/width/height once per round, never per frame). */
.g-an-mq{position:absolute;left:0;top:0;width:0;height:0;border-radius:12px;
  --an-mq-t:3s;--an-mq-a:.22;--an-mq-hue:354;--an-mq-n:24;
  opacity:var(--an-mq-a);transition:opacity .9s ease,left .25s ease,top .25s ease,width .25s ease,height .25s ease}
.g-an-mq-bulb{position:absolute;width:var(--an-mq-d,7px);height:var(--an-mq-d,7px);
  margin:calc(var(--an-mq-d,7px) * -.5);border-radius:50%;
  background:hsl(var(--an-mq-hue) 92% 70%);
  box-shadow:0 0 6px hsl(var(--an-mq-hue) 92% 68% / .9),0 0 14px hsl(var(--an-mq-hue) 88% 58% / .5);
  animation:g-an-mq-chase var(--an-mq-t) linear infinite;
  animation-delay:calc(var(--i) / var(--an-mq-n) * var(--an-mq-t) * -1)}
@keyframes g-an-mq-chase{0%{opacity:.18;transform:scale(.8)}12%{opacity:1;transform:scale(1.15)}40%{opacity:.35;transform:scale(.92)}100%{opacity:.18;transform:scale(.8)}}
.g-an-mq.gold{--an-mq-hue:46 !important}
.g-an-mq.flash .g-an-mq-bulb{animation-duration:.22s;filter:brightness(1.6)}
.g-an-mq.out{opacity:.08;transition:opacity 1.6s ease}
/* THE RING: blooms on the found frame (rect measured at tap time). */
.g-an-cs-ring{position:absolute;border-radius:8px;opacity:0;
  border:2px solid hsl(var(--an-cs-hue,354) 90% 78%);
  box-shadow:0 0 18px hsl(var(--an-cs-hue,354) 90% 70% / .8),inset 0 0 22px hsl(var(--an-cs-hue,354) 90% 70% / .35)}
.g-an-cs-ring.on{animation:g-an-cs-ring .48s ease-out forwards}
@keyframes g-an-cs-ring{0%{opacity:0;transform:scale(.92)}18%{opacity:1;transform:scale(1.02)}100%{opacity:0;transform:scale(1.14)}}
/* THE FROST: a wrong frame goes cold for a beat (the frame itself is CORE's;
   this is light laid over it). */
.g-an-cs-frost{position:absolute;border-radius:6px;opacity:0;
  background:radial-gradient(60% 60% at 50% 50%, rgba(190,214,255,.42), rgba(120,150,220,.12) 60%, transparent 80%);
  border:1px solid rgba(190,214,255,.45)}
.g-an-cs-frost.on{animation:g-an-cs-frost .7s ease-out forwards}
@keyframes g-an-cs-frost{0%{opacity:0;transform:scale(1.04)}20%{opacity:1;transform:scale(1)}100%{opacity:0;transform:scale(.98)}}
/* THE SPARKS: silver grains off a found frame (Deck IV - the verb pays). */
.g-an-cs-spark{position:absolute;left:var(--x);top:var(--y);width:var(--s,4px);height:var(--s,4px);
  border-radius:50%;opacity:0;background:hsl(var(--an-cs-hue,354) 95% 82%);
  box-shadow:0 0 8px hsl(var(--an-cs-hue,354) 95% 75% / .9);
  animation:g-an-cs-spark var(--d,.7s) ease-out forwards}
@keyframes g-an-cs-spark{0%{opacity:0;transform:translate(0,0) scale(.5)}15%{opacity:1}
  100%{opacity:0;transform:translate(var(--dx,0px),var(--dy,-40px)) scale(1.1)}}
/* THE WORD: ALMOST / FAST / ROYAL, under 600ms, placed near the tapped frame. */
.g-an-cs-word{position:absolute;left:var(--wx,50%);top:var(--wy,50%);transform:translate(-50%,-50%);
  font:800 clamp(14px,2.2vmin,22px)/1 var(--disp,system-ui,sans-serif);letter-spacing:.22em;
  color:hsl(var(--an-cs-hue,354) 90% 84%);text-shadow:0 0 12px hsl(var(--an-cs-hue,354) 90% 70% / .8);opacity:0;white-space:nowrap}
.g-an-cs-word.on{animation:g-an-cs-word .56s ease-out forwards}
.g-an-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-an-cs-word.gold{color:#ffe08a;text-shadow:0 0 14px rgba(240,194,75,.9)}
@keyframes g-an-cs-word{0%{opacity:0;transform:translate(-50%,-50%) scale(.7)}18%{opacity:1;transform:translate(-50%,-50%) scale(1.08)}
  70%{opacity:1;transform:translate(-50%,-54%) scale(1)}100%{opacity:0;transform:translate(-50%,-64%) scale(1)}}
/* the HUD punch (transform only; the text is CORE's) */
.g-an-cs-punch{transition:transform .22s cubic-bezier(.2,1.6,.4,1) !important;transform:scale(var(--an-cs-ps,1))}
.g-an-cs-bloom{box-shadow:0 0 0 2px hsl(var(--an-cs-hue,354) 90% 70% / .55),0 0 22px hsl(var(--an-cs-hue,354) 90% 65% / .6) !important;transition:box-shadow .3s ease}
/* reduced motion, both gates */
@media (prefers-reduced-motion: reduce){
  .g-an-mq-bulb{animation:none !important;opacity:.55}
  .g-an-cs-spark{display:none}
  .g-an-cs-word.on{animation:none;opacity:1}
  .g-an-cs-ring.on{animation:none;opacity:.8}
  .g-an-cs-frost.on{animation:none;opacity:.8}
}
html.arc-reduced .g-an-mq-bulb{animation:none !important;opacity:.55}
html.arc-reduced .g-an-cs-spark{display:none}
html.arc-reduced .g-an-cs-word.on{animation:none;opacity:1}
html.arc-reduced .g-an-cs-ring.on{animation:none;opacity:.8}
html.arc-reduced .g-an-cs-frost.on{animation:none;opacity:.8}
.g-an-stage.suspended .g-an-mq-bulb,.g-an-stage.suspended .g-an-cs-spark,.g-an-stage.suspended .g-an-cs-word,
.g-an-stage.suspended .g-an-cs-ring,.g-an-stage.suspended .g-an-cs-frost{animation-play-state:paused !important}
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

/** The chime's pitch for a streak: +1 semitone per link, capped. Pure. */
export function pitchForStreak(streak) {
  const s = Math.max(0, Math.min(AN_CASINO.SEMITONE_CAP, (Number(streak) || 0) - 1));
  return +Math.pow(2, s / 12).toFixed(4);
}
/** A find under the FAST line? Pure. */
export function isFast(latencyMs) {
  const l = Number(latencyMs);
  return Number.isFinite(l) && l > 0 && l < AN_CASINO.FAST_MS;
}
/** The royal verdict: every tap a first tap, streak at the royal line. Pure. */
export function isRoyal(end) {
  const e = end || {};
  if (e.royal === true) return true;
  if (e.fail === true) return false;
  return (Number(e.wrongTaps) || 0) === 0 && (Number(e.bestStreak) || 0) >= AN_CASINO.ROYAL_MIN_STREAK
    && (Number(e.finds) || 0) >= AN_CASINO.ROYAL_MIN_STREAK;
}

function el(tag, cls) {
  try {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function setCls(n, cls, on) { try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } }
function setVar(n, k, v) { try { if (n && n.style) n.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function restart(n, cls) {
  if (!n || !n.classList) return;
  try { n.classList.remove(cls); if (typeof n.offsetWidth === 'number') void n.offsetWidth; n.classList.add(cls); } catch (e) { /* noop */ }
}
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
function nowMs() {
  try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
  return Date.now();
}

/**
 * @param {Object} o
 * @param {string}   o.seed      the class seed (retakes replay the room)
 * @param {number}   o.tier      1..4
 * @param {Object}   o.stage     .g-an-stage (identity props land here)
 * @param {Object}   o.board     .g-an-grid (geometry reference - READ only)
 * @param {Object}   o.hud       .g-an-hud element OR {root, round, clock, streak}
 * @param {Object}   o.backdrop  .g-an-backdrop (the lighting layers)
 * @param {Object}   o.timers    {after(ms,fn)->id, every?, clear|cancel(id)}
 * @param {boolean}  o.reduced   reduced motion
 * @param {boolean|Function} o.capsOk  false when bgIntensity is capped to 0
 * @param {Function=} o.t        ctx.lexicon (English fallbacks here)
 * @param {Object=}  o.engine    CORE's deck weld {fire, sustain, stop, ceremony?, audio?}
 * @param {number=}  o.motionLevel
 * @param {Function=} o.log
 */
export function createAnCasino(o) {
  const opts = o || {};
  const C = AN_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => (f == null ? k : f));
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const eng = opts.engine || {};
  const hudRoot = opts.hud && opts.hud.nodeType ? opts.hud : (opts.hud && opts.hud.root) || null;
  const hudChips = opts.hud && !opts.hud.nodeType ? opts.hud : {};
  const armedBase = !!opts.stage && !!opts.timers && typeof opts.timers.after === 'function'
    && typeof document !== 'undefined';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false && opts.capsOk != null;
  }
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();
  /* THE bgIntensity DECOUPLE (W2, owner ruling). capsOk false is the player's
   * VISUAL exit (Law VI) and it stays exactly that: every light still gates on
   * armed(). It is NOT an audio exit - a room with the lights off still has to
   * SOUND like the room, or one visual dial mutes the whole school. Cue firing
   * therefore gates on sounds(), which is armed() minus capsOk. */
  const sounds = () => armedBase && !destroyed;

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
  function cancelAll() { for (const id of Array.from(live)) cancel(id); }

  /* ---- seeded streams (Law V; append-only tags) --------------------------- */
  const seedBase = String(opts.seed || 'an') + '|an-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, flashes: 0, words: 0, rings: 0, frosts: 0 };
  function cue(name, level, extra) {
    if (!sounds()) return;
    counts.cues++;
    try {
      if (typeof eng.audio === 'function') eng.audio(name, level, extra || {});
      else if (typeof eng.fire === 'function') eng.fire('audio_trigger', Object.assign({ name, level }, extra || {}));
    } catch (e) { /* a refused cue is not an error */ }
  }
  function ceremony(kind, o2) {
    if (!armed()) return null;
    try { return typeof eng.ceremony === 'function' ? eng.ceremony(kind, o2 || {}) : null; } catch (e) { return null; }
  }

  /* ---- identity (per class seed, FIXED draw order) ------------------------ */
  const ID = (() => {
    const offArc = roll('arc') < C.OFF_ARC;
    const hue = offArc ? Math.round(lerp(C.HUE_OFF, roll('hue'))) : Math.round(lerp(C.HUE_ARC, roll('hue'))) % 360;
    const lampX = 32 + Math.round(roll('lamp') * 36);                 // 32..68 %
    const lampTilt = (roll('lamp') < 0.5 ? -1 : 1) * (4 + roll('lamp') * 10);
    const breath = +lerp(C.BREATH_S, roll('breath')).toFixed(1);
    const drift = +lerp(C.DRIFT_S, roll('drift')).toFixed(1);
    const kb = +lerp(C.KB_S, roll('kb')).toFixed(1);
    const kbX = (roll('kb') < 0.5 ? -1 : 1) * (1 + roll('kb') * 1.5);
    const kbY = (roll('kb') < 0.5 ? -1 : 1) * (0.5 + roll('kb'));
    const dust = +lerp(C.DUST, roll('dust')).toFixed(2);
    const bulbs = C.BULBS[Math.floor(roll('bulbs') * C.BULBS.length)];
    const bulbD = 6 + Math.round(roll('bulbs') * 3);
    const frameStart = C.FRAME_START + Math.floor(roll('frame') * 60);
    const stops = C.STOPS_MIN + Math.floor(roll('stops') * C.STOPS_SPAN);
    const journey = [];
    for (let i = 0; i < stops; i++) {
      journey.push({
        lampX: Math.max(20, Math.min(80, lampX + (roll('journey') - 0.5) * 30)),
        lampA: +(0.35 + roll('journey') * 0.45).toFixed(2),
        dust: +Math.min(1, dust * (0.7 + roll('journey') * 0.8)).toFixed(2),
        deep: +(i / Math.max(1, stops - 1) * (0.5 + roll('journey') * 0.4)).toFixed(2),
        tilt: +((roll('journey') < 0.5 ? -1 : 1) * (3 + roll('journey') * 12)).toFixed(1),
      });
    }
    return { offArc, hue, lampX, lampTilt, breath, drift, kb, kbX, kbY, dust, bulbs, bulbD, frameStart, journey };
  })();

  /* ---- nodes ------------------------------------------------------------- */
  const layers = {};
  let cs = null, mq = null, ring = null, frost = null, word = null;
  const props = new Set();
  const stageClasses = new Set();
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let stopIx = -1;
  let goldOn = false;
  let royalOn = false;
  let outOn = false;
  let bellOn = false;
  let lastTapRect = null;
  let sparks = 0;
  let flashTimer = 0, ringTimer = 0, frostTimer = 0, wordTimer = 0, punchTimer = 0, coldTimer = 0, royalTimer = 0, mqFlashTimer = 0;
  let almostPending = false;
  let almostTimer = 0;
  const mqTimers = [];
  let resizeFn = null;
  let ro = null;
  /* the tally this deck keeps for the ROYAL verdict (its own view, never the ledger) */
  let finds = 0, wrongTaps = 0, bestStreak = 0, rounds = 0, relocations = 0;
  const jackLog = [];

  function setProp(k, v) { if (!opts.stage || !opts.stage.style) return; try { opts.stage.style.setProperty(k, String(v)); props.add(k); } catch (e) { /* noop */ } }
  function stageClass(name, on) {
    if (!opts.stage || !opts.stage.classList) return;
    try { if (on) { opts.stage.classList.add(name); stageClasses.add(name); } else { opts.stage.classList.remove(name); stageClasses.delete(name); } } catch (e) { /* noop */ }
  }

  function dressRoom() {
    const sat = ID.offArc ? 62 : 88;
    setProp('--an-n-hue', ID.hue);
    setProp('--an-n-sat', sat + '%');
    setProp('--an-n-safe', 'hsl(' + ID.hue + ' ' + sat + '% 52%)');
    setProp('--an-n-safe-a', 'hsl(' + ID.hue + ' ' + sat + '% 56% / .28)');
    setProp('--an-n-lamp-x', ID.lampX + '%');
    setProp('--an-n-lamp-tilt', ID.lampTilt + 'deg');
    setProp('--an-n-breath', ID.breath + 's');
    setProp('--an-n-drift', ID.drift + 's');
    setProp('--an-n-kb', ID.kb + 's');
    setProp('--an-n-kb-x', ID.kbX.toFixed(2) + '%');
    setProp('--an-n-kb-y', ID.kbY.toFixed(2) + '%');
    setProp('--an-n-dust', ID.dust);
    setProp('--an-n-frame-start', ID.frameStart);
    setProp('--an-n-deep', '0');
    setProp('--an-cs-hue', ID.hue);
    paintStop(0);
    say('casino: darkroom dressed (hue ' + ID.hue + (ID.offArc ? ' OFF-ARC' : '') + ', lamp ' + ID.lampX + '%, '
      + ID.journey.length + ' stops, frames from ' + ID.frameStart + ')');
  }
  function paintStop(ix) {
    const stop = ID.journey[Math.max(0, Math.min(ID.journey.length - 1, ix))];
    if (!stop) return;
    stopIx = ix;
    setProp('--an-n-lamp-x', stop.lampX.toFixed(0) + '%');
    setProp('--an-n-lamp-a', stop.lampA);
    setProp('--an-n-dust', stop.dust);
    setProp('--an-n-deep', stop.deep);
    setProp('--an-n-lamp-tilt', stop.tilt + 'deg');
  }
  function walkJourney(h) {
    const n = ID.journey.length;
    if (n < 2) return;
    const raw = Math.min(n - 1, Math.floor(h * n));
    if (stopIx < 0) { paintStop(raw); return; }
    if (raw > stopIx && h >= (stopIx + 1) / n + C.STOP_HYST) paintStop(stopIx + 1);
    else if (raw < stopIx && h <= stopIx / n - C.STOP_HYST) paintStop(stopIx - 1);
  }

  function mountBackdrop() {
    const host = opts.backdrop;
    if (!host || !host.appendChild) return;
    for (const name of ['safe', 'lamp', 'strips', 'dust', 'grain', 'dark', 'flash', 'cold', 'royal', 'vig']) {
      const n = el('div', 'g-an-bd g-an-bd-' + name);
      if (!n) continue;
      layers[name] = n;
      host.appendChild(n);
    }
  }
  function mountOverlay() {
    if (cs || !opts.stage || !opts.stage.appendChild) return;
    cs = el('div', 'g-an-cs');
    if (!cs) return;
    opts.stage.appendChild(cs);
    mq = el('div', 'g-an-mq');
    if (mq) {
      setVar(mq, '--an-mq-hue', ID.hue);
      setVar(mq, '--an-mq-d', ID.bulbD + 'px');
      cs.appendChild(mq);
    }
    ring = el('i', 'g-an-cs-ring'); if (ring) cs.appendChild(ring);
    frost = el('i', 'g-an-cs-frost'); if (frost) cs.appendChild(frost);
    word = el('div', 'g-an-cs-word'); if (word) cs.appendChild(word);
  }

  /* ---- the marquee: sized from the grid, bulbs laid around the rectangle --- */
  function layoutMarquee() {
    if (!mq || !opts.board || !cs) return false;
    const g = rectOf(opts.board);
    const base = rectOf(cs);
    if (!g || !base || !g.width || !g.height) return false;
    const inset = C.MQ_INSET_PX;
    const left = g.left - base.left - inset;
    const top = g.top - base.top - inset;
    const w = g.width + inset * 2;
    const h = g.height + inset * 2;
    mq.style.left = left.toFixed(0) + 'px';
    mq.style.top = top.toFixed(0) + 'px';
    mq.style.width = w.toFixed(0) + 'px';
    mq.style.height = h.toFixed(0) + 'px';
    /* bulb count follows the perimeter so a 5x5 grid is not a sparse 3x3 string */
    const per = Math.round((w + h) * 2 / 34);
    const n = Math.max(12, Math.min(72, Math.round(ID.bulbs * (per / 48))));
    if (mq.childNodes && mq.childNodes.length === n) return true;
    while (mq.firstChild) { try { mq.removeChild(mq.firstChild); } catch (e) { break; } }
    setVar(mq, '--an-mq-n', n);
    const perim = 2 * (w + h);
    for (let i = 0; i < n; i++) {
      const b = el('span', 'g-an-mq-bulb');
      if (!b) break;
      let d = (i / n) * perim;
      let x, y;
      if (d < w) { x = d; y = 0; }
      else if (d < w + h) { x = w; y = d - w; }
      else if (d < 2 * w + h) { x = w - (d - w - h); y = h; }
      else { x = 0; y = h - (d - 2 * w - h); }
      b.style.left = (x / w * 100).toFixed(2) + '%';
      b.style.top = (y / h * 100).toFixed(2) + '%';
      setVar(b, '--i', i);
      mq.appendChild(b);
    }
    return true;
  }
  function scheduleLayout() {
    for (const id of mqTimers) cancel(id);
    mqTimers.length = 0;
    for (const ms of C.MQ_REMEASURE_MS) mqTimers.push(after(ms, () => layoutMarquee()));
  }
  function watchGeometry() {
    try {
      if (typeof ResizeObserver === 'function' && opts.board && opts.board.nodeType) {
        ro = new ResizeObserver(() => { if (!destroyed) layoutMarquee(); });
        ro.observe(opts.board);
      }
    } catch (e) { ro = null; }
    try {
      if (typeof window !== 'undefined' && window.addEventListener) {
        resizeFn = () => { if (!destroyed) layoutMarquee(); };
        window.addEventListener('resize', resizeFn);
      }
    } catch (e) { resizeFn = null; }
  }
  function paintMarquee(force) {
    if (!mq) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    const gold = goldOn || royalOn || bellOn;
    const period = gold ? C.MQ_T_GOLD : (C.MQ_T_SLOW + (C.MQ_T_FAST - C.MQ_T_SLOW) * heat);
    const alpha = outOn ? 0.08 : (gold ? C.MQ_A_GOLD : lerp([C.MQ_A_LO, C.MQ_A_HI], heat));
    setVar(mq, '--an-mq-t', period.toFixed(2) + 's');
    setVar(mq, '--an-mq-a', alpha.toFixed(2));
    setCls(mq, 'gold', gold);
  }
  function flashMarquee(ms) {
    if (!mq || !armed() || still) return;
    setCls(mq, 'flash', true);
    cancel(mqFlashTimer);
    mqFlashTimer = after(ms || C.MQ_FLASH_MS, () => setCls(mq, 'flash', false));
  }

  /* ---- geometry helpers ---------------------------------------------------- */
  /** The rect of a tile INDEX, read off the live grid (children in DOM order),
   *  translated into overlay space. The index is the PLAYER's own tap. */
  function tileRect(i) {
    if (!opts.board || !cs || i == null) return null;
    let tile = null;
    try {
      const list = opts.board.querySelectorAll ? opts.board.querySelectorAll('.g-an-tile') : null;
      if (list && list.length) {
        for (let k = 0; k < list.length; k++) {
          const n = list[k];
          let di = null;
          try { di = n.getAttribute ? n.getAttribute('data-i') : null; } catch (e) { di = null; }
          if (di != null && String(di) === String(i)) { tile = n; break; }
        }
        if (!tile && Number(i) >= 0 && Number(i) < list.length) tile = list[Number(i)];
      }
    } catch (e) { tile = null; }
    const r = rectOf(tile);
    const base = rectOf(cs);
    if (!r || !base || !r.width) return null;
    return { x: r.left - base.left, y: r.top - base.top, w: r.width, h: r.height, cx: r.left - base.left + r.width / 2, cy: r.top - base.top + r.height / 2 };
  }
  function gridCentre() {
    const g = rectOf(opts.board);
    const base = rectOf(cs);
    if (!g || !base) return { cx: 0, cy: 0, w: 0, h: 0 };
    return { cx: g.left - base.left + g.width / 2, cy: g.top - base.top + g.height / 2, w: g.width, h: g.height };
  }

  /* ---- the light ---------------------------------------------------------- */
  function flashRoom(q, ms) {
    const f = layers.flash;
    if (!f || !armed()) return;
    counts.flashes++;
    setProp('--an-n-pay', lerp(C.FLASH_A, q).toFixed(2));
    if (still) { setCls(f, 'on', true); cancel(flashTimer); flashTimer = after(ms || C.FLASH_MS, () => setCls(f, 'on', false)); return; }
    restart(f, 'on');
    cancel(flashTimer);
    flashTimer = after(ms || C.FLASH_MS, () => setCls(f, 'on', false));
  }
  function coldRoom() {
    const c = layers.cold;
    if (!c || !armed()) return;
    restart(c, 'on');
    cancel(coldTimer);
    coldTimer = after(C.COLD_MS, () => setCls(c, 'on', false));
  }
  function placeBox(node, r, pad) {
    if (!node || !r) return false;
    node.style.left = (r.x - pad).toFixed(0) + 'px';
    node.style.top = (r.y - pad).toFixed(0) + 'px';
    node.style.width = (r.w + pad * 2).toFixed(0) + 'px';
    node.style.height = (r.h + pad * 2).toFixed(0) + 'px';
    return true;
  }
  function ringAt(r) {
    if (!ring || !armed() || !placeBox(ring, r, 3)) return;
    counts.rings++;
    restart(ring, 'on');
    cancel(ringTimer);
    ringTimer = after(C.RING_MS + 40, () => setCls(ring, 'on', false));
  }
  function frostAt(r) {
    if (!frost || !armed() || !placeBox(frost, r, 1)) return;
    counts.frosts++;
    restart(frost, 'on');
    cancel(frostTimer);
    frostTimer = after(C.FROST_MS + 40, () => setCls(frost, 'on', false));
  }
  function sparksAt(r, q) {
    if (!cs || !r || still || !armed()) return 0;
    const n = Math.round(lerp(C.SPARKS, q));
    let made = 0;
    for (let i = 0; i < n; i++) {
      if (sparks >= C.MAX_SPARKS) break;
      const s = el('i', 'g-an-cs-spark');
      if (!s || !s.style) break;
      const a = roll('spark') * Math.PI * 2;
      const dist = 18 + roll('spark') * Math.max(24, r.w * 0.6);
      s.style.setProperty('--x', (r.cx + (roll('spark') - 0.5) * r.w * 0.5).toFixed(0) + 'px');
      s.style.setProperty('--y', (r.cy + (roll('spark') - 0.5) * r.h * 0.5).toFixed(0) + 'px');
      s.style.setProperty('--dx', (Math.cos(a) * dist).toFixed(0) + 'px');
      s.style.setProperty('--dy', (Math.sin(a) * dist - 12).toFixed(0) + 'px');
      s.style.setProperty('--s', (3 + roll('spark') * 3).toFixed(1) + 'px');
      const d = 0.5 + roll('spark') * 0.4;
      s.style.setProperty('--d', d.toFixed(2) + 's');
      cs.appendChild(s);
      sparks++; made++;
      after(d * 1000 + 80, () => { sparks = Math.max(0, sparks - 1); try { s.remove(); } catch (e) { /* ignore */ } });
    }
    return made;
  }
  function punchChip(which, q) {
    const chip = hudChips[which] || null;
    if (!chip || !armed()) return;
    if (still) {
      setCls(chip, 'g-an-cs-bloom', true);
      cancel(punchTimer);
      punchTimer = after(320, () => setCls(chip, 'g-an-cs-bloom', false));
      return;
    }
    setCls(chip, 'g-an-cs-punch', true);
    setVar(chip, '--an-cs-ps', (1 + lerp(C.PUNCH_SCALE, q)).toFixed(3));
    cancel(punchTimer);
    punchTimer = after(C.PUNCH_MS, () => setVar(chip, '--an-cs-ps', '1'));
  }
  function showWord(key, fallback, tone, at) {
    if (!word || !armed()) return;
    counts.words++;
    const r = at || lastTapRect || gridCentre();
    word.textContent = t(key, fallback);
    word.className = 'g-an-cs-word' + (tone ? ' ' + tone : '');
    setVar(word, '--wx', (r.cx || 0).toFixed(0) + 'px');
    setVar(word, '--wy', ((r.cy || 0) - (r.h ? r.h * 0.62 : 30)).toFixed(0) + 'px');
    if (typeof word.offsetWidth === 'number') void word.offsetWidth;
    setCls(word, 'on', true);
    cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 40, () => setCls(word, 'on', false));
  }

  /* ---- the ladder --------------------------------------------------------- */
  function jackpot(intensity, why) {
    if (!sounds()) return;          // every light below self-gates on armed()
    counts.jackpots++;
    jackLog.push(why);
    ceremony('jackpot', { intensity });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashMarquee(C.MQ_FLASH_MS + 300 * intensity);
    flashRoom(intensity, C.FLASH_MS + 300);
  }
  function nearMiss(kind, intensity) {
    if (!sounds()) return;          // ceremony() self-gates on armed()
    counts.nearMisses++;
    ceremony('near_miss', { intensity });
    cue('near_miss', C.NEAR_LEVEL, { pitch: kind === 'almost' ? 0.8 : 1.1 });
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      if (!armed()) { say('casino: disarmed'); return; }
      ensureStyle();
      mountBackdrop();
      mountOverlay();
      dressRoom();
      if (!still) stageClass('g-an-kb', true);
      layoutMarquee();
      scheduleLayout();
      watchGeometry();
      paintMarquee(true);
      say('casino: safelight on, ' + Object.keys(layers).length + ' layers, marquee ' + (mq && mq.childNodes ? mq.childNodes.length : 0) + ' bulbs');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (!started) return;
      paintMarquee(false);
      walkJourney(heat);
    },
    /** A new round is dealt: the sheet advances (a soft carriage tick) and
     *  the marquee re-measures the grid (its size may have changed). */
    roundStart(n, kind) {
      if (!started || !sounds()) return;
      rounds++;
      lastTapRect = null;
      if (armed()) scheduleLayout();
      cue('slide', C.ADVANCE_LEVEL, { pitch: 1.2 });
      const strips = layers.strips;
      if (strips && !still) restart(strips, 'advance');
      void n; void kind;
    },
    /** Every tap, AFTER CORE's accounting. The only index read is the tap's. */
    tap(ev) {
      if (!started || !sounds()) return;   // the lights below self-gate on armed()
      const e = ev || {};
      const streak = Math.max(0, Number(e.streak) || 0);
      const r = tileRect(e.i);
      if (r) lastTapRect = r;
      if (e.correct) {
        finds++;
        bestStreak = Math.max(bestStreak, streak);
        const q = clamp01(Math.min(8, streak) / 8);
        /* THE SOUND LADDER */
        cue('pop', lerp(C.FIND_LEVEL, q), { pitch: pitchForStreak(streak) });
        /* THE EXPOSURE (< 500ms, all CSS) */
        flashRoom(q);
        if (r) { ringAt(r); sparksAt(r, q); }
        punchChip('streak', q);
        if (streak >= 2) flashMarquee();
        /* NEAR-MISS STAGING: the FAST ping */
        const fast = isFast(e.latencyMs);
        if (fast && streak >= 2) { showWord('an_fast', 'FAST', 'lav', r); nearMiss('fast', C.NEAR_MISS_I.fast); }
        /* THE JACKPOT LADDER */
        const mi = C.MILESTONES.indexOf(streak);
        if (mi >= 0) {
          cue('streak', C.MILESTONE_LEVEL, { pitch: pitchForStreak(streak) });
          jackpot(C.MAJOR_I[mi], 'major@' + streak);
        } else if (fast && streak >= C.MINOR_FROM_STREAK && roll('jack-minor') < lerp(C.MINOR_CHANCE, heat)) {
          jackpot(C.MINOR_I, 'minor@' + (e.i == null ? '?' : e.i));
        }
      } else {
        wrongTaps++;
        if (almostPending) {
          /* CORE calls almost() BEFORE tap() on an it-moved tap: the word lands
             on THIS tap's frame, not the last one's */
          almostPending = false;
          cancel(almostTimer); almostTimer = 0;
          showWord('an_almost', 'ALMOST', 'lav', r);
          nearMiss('almost', C.NEAR_MISS_I.almost);
          cue('whisper', C.MISS_LEVEL, { pitch: 1.1 });      // the sting is soft: it teaches
          if (r) ringAt(r);
          return;
        }
        cue('bump', C.MISS_LEVEL, { pitch: 0.7 });         // a muted thud, never silence
        coldRoom();
        if (r) frostAt(r);
      }
    },
    /** The anomaly relocated (CORE says so AFTER the glitch): a whisper. */
    relocated() {
      if (!started || !sounds()) return;
      relocations++;
      cue('whisper', C.MOVED_LEVEL, { pitch: 0.9 });
      const lamp = layers.lamp;
      if (lamp && !still) restart(lamp, 'sweep');
    },
    /** Near-miss staging: a tap where it WAS (or an adjacent wrong tap). CORE
     *  calls this right before the matching tap(); the word waits one beat for
     *  that tap's frame, and falls back to the last tapped frame / the sheet
     *  centre when no tap follows. */
    almost() {
      if (!started || !sounds()) return;
      almostPending = true;
      cancel(almostTimer);
      almostTimer = after(40, () => {
        almostTimer = 0;
        if (!almostPending) return;
        almostPending = false;
        showWord('an_almost', 'ALMOST', 'lav');
        nearMiss('almost', C.NEAR_MISS_I.almost);
      });
    },
    bell(on) {
      bellOn = !!on;
      goldOn = bellOn || goldOn;
      stageClass('g-an-bell', bellOn);
      paintMarquee(true);
    },
    /** The bell took the sheet. ROYAL when the class was perfect, else a dim payout. */
    dimOut(info) {
      const e = info || {};
      outOn = true;
      const royal = isRoyal(Object.assign({ wrongTaps, bestStreak, finds }, e));
      if (royal && sounds()) {
        royalOn = true;
        counts.royal = 1;
        if (armed()) stageClass('g-an-royal', true);
        showWord('an_royal', 'ROYAL', 'gold', gridCentre());
        ceremony('jackpot', { intensity: C.ROYAL_I });
        cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.2 });
        after(260, () => cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.5 }));
        after(520, () => cue('stamp', 0.5, { pitch: 1 }));
        flashMarquee(C.ROYAL_MS);
        cancel(royalTimer);
        royalTimer = after(C.ROYAL_MS, () => {
          /* THE SIGH (W2). The header has promised since Semester II that the
           * marquee "sighs out - never cuts - on a dim-out"; the DIM payout
           * below always had its wash, but the ROYAL's own way out was mute.
           * A slide dropped a whole tone is that sigh. */
          cue('slide', 0.3, { pitch: 0.8 });
          if (!armed()) return;
          stageClass('g-an-royal', false); stageClass('g-an-out', true); setCls(mq, 'out', true); paintMarquee(true);
        });
      } else if (sounds()) {
        /* losses disguised: a dim payout, never silence */
        const q = clamp01(finds / 10);
        flashRoom(0.1 + 0.3 * q, C.END_DIM_MS);
        cue('wash', 0.25, { pitch: 0.9 });
        if (armed()) { stageClass('g-an-out', true); setCls(mq, 'out', true); }
      }
      bellOn = false;
      paintMarquee(true);
      return { royal };
    },
    stop() {
      cancelAll();
      for (const n of [ring, frost, word]) { if (n) n.className = n.className.split(' ')[0]; }
      setCls(mq, 'flash', false);
      const c = layers.cold; if (c) setCls(c, 'on', false);
    },
    pause() { /* the CSS freezes under .suspended; transient light simply ends on its timers */ },
    resume() { /* nothing to re-arm: the chase is CSS */ },
    destroy() {
      destroyed = true;
      cancelAll();
      try { if (ro) ro.disconnect(); } catch (e) { /* noop */ }
      ro = null;
      try { if (resizeFn && typeof window !== 'undefined') window.removeEventListener('resize', resizeFn); } catch (e) { /* noop */ }
      resizeFn = null;
      for (const k of Object.keys(layers)) { try { layers[k].remove(); } catch (e) { /* noop */ } delete layers[k]; }
      if (cs) { try { cs.remove(); } catch (e) { /* noop */ } }
      cs = mq = ring = frost = word = null;
      for (const name of Array.from(stageClasses)) stageClass(name, false);
      if (opts.stage && opts.stage.style) for (const k of props) { try { opts.stage.style.removeProperty(k); } catch (e) { /* noop */ } }
      props.clear();
      for (const k of Object.keys(hudChips)) { const chip = hudChips[k]; if (chip && chip.classList) { setCls(chip, 'g-an-cs-punch', false); setCls(chip, 'g-an-cs-bloom', false); } }
      void hudRoot;
    },
    diagnostics() {
      return {
        armed: armed(), sounds: sounds(), started, heat: +heat.toFixed(3), hue: ID.hue, offArc: ID.offArc, bulbs: mq && mq.childNodes ? mq.childNodes.length : 0,
        frameStart: ID.frameStart, journey: ID.journey.length, stop: stopIx,
        gold: goldOn, bell: bellOn, royal: royalOn, out: outOn,
        finds, wrongTaps, bestStreak, rounds, relocations, sparks, almostPending,
        counts: Object.assign({}, counts), jackpots: jackLog.slice(),
        liveTimers: live.size, layers: Object.keys(layers).length, overlay: !!cs, marquee: !!mq,
        lexicon: ['an_almost', 'an_fast', 'an_royal'],
      };
    },
  };
  return api;
}

export default createAnCasino;
