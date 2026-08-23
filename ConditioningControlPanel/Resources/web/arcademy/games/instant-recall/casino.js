/* ============================================================================
 * games/instant-recall/casino.js - DECK II of the House Rules for the vigil:
 * THE FLOOR. The montage is CORE's (the screen, its tiles, its channels); this
 * file lights the HALL around the screen and makes every stop and every answer
 * pay out in light and sound. The trickster (trickster.js) lies about the
 * slip; the pressure (pressure.js) is what the room DOES to you as the surge
 * climbs. This file is the lighting rig and the chime rack.
 *
 *   HALL IDENTITY     seeded per CLASS seed (Deck I): a hue pair on the
 *                     violet->rose arc (~6% of halls leave the arc for a teal
 *                     night), the beam's breath, the dust density, the bulb
 *                     pitch of the marquee, and a JOURNEY of 2-3 stops the
 *                     hall walks as heat climbs (the beam swaps its hue pair,
 *                     the glow behind the screen deepens). Same seed, same
 *                     hall; a retake sits in the identical room.
 *   MARQUEE           a bezel of bulbs around the screen (drift-by-transform,
 *                     trap 36 - never background-position). Crawls at low heat,
 *                     chases hungrily at high heat, flashes on a payout, goes
 *                     GOLD for the bell and the royal, HOLDS for a stop (the
 *                     reel stops with the montage) and sighs out on a dim-out.
 *   THE STOP          is the slot-pull: a lever at the screen's edge drops on
 *                     every stop; a drawn bell swings and RINGS only when the
 *                     stop is announced (tier 1 every stop, tier 3+ none) - a
 *                     bell that is not always rung is the whole class.
 *   PAYOUT LIGHT      a correct answer floods the hall in the identity hue,
 *                     flashes the marquee and chimes the SOUND LADDER: +1
 *                     semitone per link of the streak, capped +7 (the intake
 *                     precedent). A miss is a muted thud, a timeout a soft
 *                     exhale - never silence.
 *   THE ALMOST        near-miss staging: on a wrong pick the TRUE option ghosts
 *                     through - a pulse of light on its rectangle (an overlay
 *                     node over the slip, pointer-events:none, never a write
 *                     on the option) while the word lands and the near_miss
 *                     ceremony rises. The ledger already decided.
 *   THE JACKPOT LADDER minor (a seeded roll on a 2-link, chance rising with
 *                     heat), major (3 and 5 links, deterministic), ROYAL (once
 *                     a class, a streak that reaches the royal line - the hall
 *                     floods gold and the marquee stays gold). A failed class
 *                     still gets a dim payout on dimOut(): silence is where
 *                     people stand up.
 *   THE REEL CHANGE   a layout change sweeps a band of projector light down
 *                     the screen; a density peak flares the beam.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects CORE hands it AFTER its own
 *       accounting; writes nothing about the ledger, the stops, the streak,
 *       the options' order or the grade.
 *   II  input honest  - every node here is pointer-events:none; nothing is
 *       mounted inside, over (with hit-testing) or around an option; the
 *       almost ghost is a decoration positioned from the option's rect.
 *   III never still   - the marquee crawls at heat 0, the dust drifts, the
 *       beam breathes (style.js), the lever glints.
 *   IV  images > text - three words (ALMOST / RESISTED / ROYAL) through the
 *       lexicon, under 700ms each.
 *   V   seeded        - per-tag mulberry32 off seed+'|ir-casino|<tag>' (append-
 *       only tags; a new tag never shifts an old stream). No Math.random.
 *   VI  exits sacred  - capsOk false disarms every light (no nodes, no cues);
 *       reduced motion keeps a static dim bezel (no chase, no sweep, no ghost
 *       pulse - a flood is a fade and survives); the stage's .suspended rule
 *       freezes the chase; timers ride the game's pause-aware registry AND a
 *       local set so destroy() cannot leak one.
 *   VII lexicon       - ir_almost / ir_resisted / ir_royal only, through opts.t.
 *   PERF LAW          - zero per-frame JS; zero writes on .g-ir-tile or its
 *       media; every animation is CSS on chrome nodes this deck owns.
 *
 * ENGINE vs GAME-LOCAL: cues and the jackpot / near_miss ceremonies go through
 * opts.engine (CORE's null-safe weld: audio ceiling, clickSafe, pitch pass-
 * through). The bezel, the flood, the lever, the bell, the words and the ghost
 * are game-local: they sit on the game's own geometry (the screen, the slip).
 * ==========================================================================*/

import { makeRng, hash01 } from '../../core/rng.js';

export const IR_CASINO = Object.freeze({
  /* ---- the marquee ------------------------------------------------------ */
  MQ_T_SLOW: 3.4,                              // chase period (s) at heat 0
  MQ_T_FAST: 0.9,                              // at heat 1
  MQ_A_LO: 0.22,                               // presence (opacity) band
  MQ_A_HI: 0.84,
  MQ_T_GOLD: 0.5,
  MQ_A_GOLD: 0.95,
  MQ_FLASH_MS: 560,
  /** tonight only: three bulb temperatures, the UTC date picks one. */
  TONIGHT_HUES: Object.freeze([38, 330, 268]),
  /* ---- the payout ------------------------------------------------------- */
  FLOOD_MS: 700,
  FLOOD_A: Object.freeze([0.16, 0.5]),         // by streak/6
  WORD_MS: 640,
  /* ---- the stop --------------------------------------------------------- */
  LEVER_MS: 900,
  BELL_MS: 1400,
  BELL_LEVEL: 0.42,
  BELL_PITCH: 1.5,
  LEVER_LEVEL: 0.18,
  /* ---- the ladders ------------------------------------------------------ */
  SEMITONE_CAP: 7,
  HIT_LEVEL: Object.freeze([0.3, 0.52]),        // by min(streak,6)/6
  MISS_LEVEL: 0.24,
  TIMEOUT_LEVEL: 0.22,
  MILESTONE_LEVEL: 0.45,
  JACKPOT_LEVEL: 0.55,
  NEAR_LEVEL: 0.3,
  MINOR_STREAK: 2,
  MINOR_CHANCE: Object.freeze([0.25, 0.6]),    // by heat
  MINOR_I: 0.35,
  MAJOR_STREAKS: Object.freeze([3, 5]),
  MAJOR_I: Object.freeze([0.6, 0.85]),
  ROYAL_MIN_STREAK: 4,                         // a perfect tier-3+ class (tier 1-2 cannot reach it)
  ROYAL_I: 1,
  ROYAL_MS: 3200,
  END_DIM_MS: 1500,
  ALMOST_MS: 1300,
  NEAR_MISS_I: 0.65,
  /* ---- reel / peak ------------------------------------------------------ */
  REEL_MS: 720,
  PEAK_MS: 1100,
  /* ---- identity --------------------------------------------------------- */
  OFF_ARC: 0.06,
  HUE_ARC: Object.freeze([262, 345]),          // violet -> rose
  HUE_OFF: 182,                                // the teal night
  JOURNEY_MIN: 2,
  JOURNEY_SPAN: 2,                             // 2..3 stops
  STOP_HYST: 0.06,
  HEAT_REPAINT_STEP: 0.03,
  DUST_MIN: 8,
  DUST_SPAN: 10,                               // 8..17 motes
});

const STYLE_ID = 'g-ir-casino-style';
const STYLE_TEXT = `
/* the bezel: four bars of bulbs INSIDE the screen's edge (the montage clips, so
   the bulbs live on the rim, above the tiles, below the veil). THE DRIFT LAW
   (trap 36): each bar is a CLIP and the dots live on its ::before, oversized by
   one dot pitch (20px) at both ends and translated by exactly that pitch - a
   compositor-only chase with an invisible wrap; never background-position, and
   no per-bulb filter (a glow is baked into the gradient). Pace and presence
   ride heat; a stop HOLDS the chase (animation-play-state), gold for the bell. */
.g-ir-mq{position:absolute;inset:0;z-index:6;pointer-events:none;border-radius:inherit;
  --ir-mqt:3.4s;--ir-mqa:.22;--ir-mqhue:330;opacity:var(--ir-mqa);transition:opacity .8s ease}
.g-ir-mq i{position:absolute;display:block;overflow:hidden}
.g-ir-mq i::before{content:"";position:absolute;
  background-image:radial-gradient(circle, hsl(var(--ir-mqhue) 95% 80%) 1.6px, hsl(var(--ir-mqhue) 92% 70% / .9) 2.4px, hsl(var(--ir-mqhue) 90% 65% / .35) 3.4px, transparent 4.6px)}
.g-ir-mq .mq-t,.g-ir-mq .mq-b{left:0;right:0;height:9px}
.g-ir-mq .mq-l,.g-ir-mq .mq-r{top:0;bottom:0;width:9px}
.g-ir-mq .mq-t::before,.g-ir-mq .mq-b::before{top:0;bottom:0;left:-20px;right:-20px;background-size:20px 9px;background-repeat:repeat-x}
.g-ir-mq .mq-l::before,.g-ir-mq .mq-r::before{left:0;right:0;top:-20px;bottom:-20px;background-size:9px 20px;background-repeat:repeat-y}
.g-ir-mq .mq-t{top:0}.g-ir-mq .mq-r{right:0}.g-ir-mq .mq-b{bottom:0}.g-ir-mq .mq-l{left:0}
.g-ir-mq .mq-t::before{animation:g-ir-mqx var(--ir-mqt) linear infinite var(--ir-mqp,0s)}
.g-ir-mq .mq-r::before{animation:g-ir-mqy var(--ir-mqt) linear infinite var(--ir-mqp,0s)}
.g-ir-mq .mq-b::before{animation:g-ir-mqxr var(--ir-mqt) linear infinite var(--ir-mqp,0s)}
.g-ir-mq .mq-l::before{animation:g-ir-mqyr var(--ir-mqt) linear infinite var(--ir-mqp,0s)}
@keyframes g-ir-mqx{from{transform:translate3d(0,0,0)}to{transform:translate3d(20px,0,0)}}
@keyframes g-ir-mqxr{from{transform:translate3d(0,0,0)}to{transform:translate3d(-20px,0,0)}}
@keyframes g-ir-mqy{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,20px,0)}}
@keyframes g-ir-mqyr{from{transform:translate3d(0,0,0)}to{transform:translate3d(0,-20px,0)}}
.g-ir-mq.is-held i::before{animation-play-state:paused}
.g-ir-mq.is-gold{--ir-mqhue:46 !important}
/* the flash is an opacity breathe on the bezel itself - never a filter on the bars */
.g-ir-mq.is-flash{animation:g-ir-mqflash .56s ease-out 1}
@keyframes g-ir-mqflash{0%{opacity:1}100%{opacity:var(--ir-mqa)}}
.g-ir-mq.is-out{opacity:.08;transition:opacity 1.6s ease}

/* the lever: a slot handle on the screen's right rim; it drops on every stop */
.g-ir-cs-lever{position:absolute;right:14px;top:50%;z-index:7;width:10px;height:84px;margin-top:-42px;pointer-events:none;
  transform-origin:50% 90%;opacity:.55;transition:opacity .6s ease}
.g-ir-cs-lever::before{content:"";position:absolute;left:3px;top:0;bottom:10px;width:4px;border-radius:2px;
  background:linear-gradient(180deg, rgba(255,255,255,.8), rgba(184,166,232,.45));box-shadow:0 0 8px rgba(184,166,232,.5)}
.g-ir-cs-lever::after{content:"";position:absolute;left:-6px;top:-8px;width:22px;height:22px;border-radius:50%;
  background:radial-gradient(circle at 35% 30%, #ffd1ea, var(--pink) 55%, #8d2c64);box-shadow:0 0 12px rgba(255,105,180,.7)}
.g-ir-cs-lever.is-pulled{animation:g-ir-cs-pull .9s cubic-bezier(.3,1.4,.4,1) 1}
@keyframes g-ir-cs-pull{0%{transform:rotate(0)}30%{transform:rotate(150deg)}55%{transform:rotate(150deg)}100%{transform:rotate(0)}}

/* the bell: a drawn bell at the screen's top rim - it swings only when rung */
.g-ir-cs-bell{position:absolute;left:50%;top:10px;z-index:7;width:26px;height:26px;margin-left:-13px;pointer-events:none;
  opacity:.35;transform-origin:50% 0;transition:opacity .5s ease}
.g-ir-cs-bell::before{content:"";position:absolute;left:3px;top:2px;width:20px;height:18px;border-radius:10px 10px 3px 3px;
  background:linear-gradient(180deg, #ffe9a8, var(--gold) 60%, #9a7a1e);box-shadow:0 0 10px rgba(240,194,75,.5)}
.g-ir-cs-bell::after{content:"";position:absolute;left:10px;top:18px;width:6px;height:6px;border-radius:50%;background:#ffe9a8}
.g-ir-cs-bell.is-rung{opacity:1;animation:g-ir-cs-ring 1.4s ease-in-out 1}
@keyframes g-ir-cs-ring{0%{transform:rotate(0)}15%{transform:rotate(28deg)}35%{transform:rotate(-24deg)}55%{transform:rotate(16deg)}
  75%{transform:rotate(-9deg)}100%{transform:rotate(0)}}

/* the reel change: a band of projector light sweeps down the screen */
.g-ir-cs-reel{position:absolute;left:0;right:0;top:-18%;height:18%;z-index:6;pointer-events:none;opacity:0;
  background:linear-gradient(180deg, transparent, rgba(255,255,255,.22) 45%, rgba(255,255,255,.32) 50%, rgba(255,255,255,.22) 55%, transparent)}
.g-ir-cs-reel.is-on{animation:g-ir-cs-sweep .72s ease-in-out 1}
@keyframes g-ir-cs-sweep{0%{opacity:0;transform:translateY(0)}15%{opacity:1}85%{opacity:1}100%{opacity:0;transform:translateY(660%)}}

/* the flood: payout light over the whole hall, in the identity hue */
.g-ir-cs-flood{position:absolute;inset:0;z-index:4;pointer-events:none;opacity:0;
  background:radial-gradient(70% 60% at 50% 50%, hsl(var(--ir-cs-hue,330) 90% 70% / .7) 0%, hsl(var(--ir-cs-hue,330) 90% 60% / .25) 40%, transparent 72%);
  transition:opacity .16s ease-out}
.g-ir-cs-flood.is-on{opacity:var(--ir-cs-pay,.3);transition:opacity .06s ease-in}
.g-ir-cs-flood.is-royal{background:radial-gradient(80% 70% at 50% 50%, hsl(46 100% 72% / .95) 0%, hsl(46 100% 60% / .45) 35%, hsl(46 100% 50% / .12) 70%, transparent 100%);
  opacity:.9;transition:opacity .35s ease-out}
/* the glow behind the screen (identity hue, deepens on the journey) */
.g-ir-cs-glow{position:absolute;left:50%;top:50%;z-index:0;pointer-events:none;width:min(110%,1240px);height:70%;
  transform:translate(-50%,-48%);border-radius:40%;
  background:radial-gradient(60% 60% at 50% 50%, hsl(var(--ir-cs-hue,330) 80% 62% / calc(.10 + var(--ir-cs-deep,0) * .16)), transparent 70%);
  animation:g-ir-cs-glow var(--ir-n-breath,9s) ease-in-out infinite alternate}
@keyframes g-ir-cs-glow{from{transform:translate(-50%,-48%) scale(1)}to{transform:translate(-50%,-50%) scale(1.06)}}
.g-ir-stage.g-ir-cs-peak .g-ir-cs-glow{animation-duration:1.2s}
/* the dust: motes in the beam, each on its own slow rise */
.g-ir-cs-dust{position:absolute;inset:0;z-index:0;pointer-events:none;overflow:hidden}
.g-ir-cs-mote{position:absolute;left:var(--x,50%);top:var(--y,50%);width:var(--s,3px);height:var(--s,3px);border-radius:50%;
  background:hsl(var(--ir-cs-hue,330) 60% 88% / .55);box-shadow:0 0 6px hsl(var(--ir-cs-hue,330) 70% 80% / .5);
  animation:g-ir-cs-mote var(--d,14s) linear infinite;animation-delay:var(--dl,0s);opacity:0}
@keyframes g-ir-cs-mote{0%{opacity:0;transform:translate(0,0)}10%{opacity:.8}90%{opacity:.6}100%{opacity:0;transform:translate(var(--dx,10px),var(--dy,-60px))}}

/* the words: ALMOST / RESISTED / ROYAL, on the stage under the slip's foot */
.g-ir-cs-word{position:absolute;left:50%;top:calc(50% + 150px);z-index:8;transform:translate(-50%,-50%);
  pointer-events:none;font:800 clamp(14px,2.2vmin,22px)/1 var(--disp);letter-spacing:.24em;
  color:hsl(var(--ir-cs-hue,330) 90% 82%);text-shadow:0 0 12px hsl(var(--ir-cs-hue,330) 90% 70% / .8);opacity:0}
.g-ir-cs-word.is-on{animation:g-ir-cs-word .64s ease-out forwards}
.g-ir-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-ir-cs-word.gold{color:#ffe08a;text-shadow:0 0 14px rgba(240,194,75,.9)}
@keyframes g-ir-cs-word{0%{opacity:0;transform:translate(-50%,-50%) scale(.7)}18%{opacity:1;transform:translate(-50%,-50%) scale(1.08)}
  70%{opacity:1;transform:translate(-50%,-54%) scale(1)}100%{opacity:0;transform:translate(-50%,-64%) scale(1)}}

/* the almost ghost: the TRUE option's rectangle pulses through a wrong pick.
   An overlay in the stage, placed from the option's rect; never a write on it. */
.g-ir-cs-ghost{position:absolute;z-index:8;pointer-events:none;border-radius:9px;opacity:0;
  left:var(--x,0);top:var(--y,0);width:var(--w,100px);height:var(--h,50px);
  box-shadow:0 0 0 2px rgba(240,194,75,.85), 0 0 22px rgba(240,194,75,.6), inset 0 0 18px rgba(240,194,75,.35)}
.g-ir-cs-ghost.is-on{animation:g-ir-cs-ghost 1.3s ease-out 1}
@keyframes g-ir-cs-ghost{0%{opacity:0}12%{opacity:1}30%{opacity:.25}48%{opacity:1}66%{opacity:.3}84%{opacity:.9}100%{opacity:0}}

@media (prefers-reduced-motion: reduce){
  .g-ir-mq i::before,.g-ir-mq.is-flash,.g-ir-cs-glow,.g-ir-cs-mote,.g-ir-cs-lever.is-pulled,.g-ir-cs-bell.is-rung,.g-ir-cs-reel.is-on{animation:none !important}
  .g-ir-cs-mote{opacity:.5}
  .g-ir-cs-bell.is-rung{opacity:1}
  .g-ir-cs-word.is-on{animation:none;opacity:1}
  .g-ir-cs-ghost.is-on{animation:none;opacity:.9}
}
html.arc-reduced .g-ir-mq i::before,html.arc-reduced .g-ir-mq.is-flash,html.arc-reduced .g-ir-cs-glow,html.arc-reduced .g-ir-cs-mote{animation:none !important}
html.arc-reduced .g-ir-cs-mote{opacity:.5}
html.arc-reduced .g-ir-cs-word.is-on{animation:none;opacity:1}
html.arc-reduced .g-ir-cs-ghost.is-on{animation:none;opacity:.9}
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
  const s = Math.max(0, Math.min(IR_CASINO.SEMITONE_CAP, (Number(streak) || 0) - 1));
  return +Math.pow(2, s / 12).toFixed(4);
}
/** Tonight's bulb hue from the UTC date string alone (shared by everyone). Pure. */
export function tonightHue(utcDate) {
  const h = hash01('ir-tonight|' + String(utcDate || ''));
  const list = IR_CASINO.TONIGHT_HUES;
  return list[Math.min(list.length - 1, Math.floor(h * list.length))];
}
/** The jackpot rung a streak pays, or null. Pure. minor is a ROLL (caller). */
export function jackpotFor(streak) {
  const s = Number(streak) || 0;
  if (s >= IR_CASINO.ROYAL_MIN_STREAK) return 'royal';
  const mi = IR_CASINO.MAJOR_STREAKS.indexOf(s);
  if (mi >= 0) return 'major';
  if (s === IR_CASINO.MINOR_STREAK) return 'minor';
  return null;
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

/**
 * @param {Object} o
 *   seed, tier, stage (.g-ir-stage), montage|board (.g-ir-montage), hud (the .g-ir-hud), stopEl (.g-ir-stop),
 *   backdrop (.g-ir-backdrop), timers {after,every,clear}, reduced, capsOk (bool | () => bool),
 *   t (k, fallback) => string, engine {fire, ceremony?}, log, utcDate?, motionLevel?
 */
export function createIrCasino(o) {
  const opts = o || {};
  const C = IR_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const eng = opts.engine || {};
  const stage = opts.stage || null;
  const board = opts.montage || opts.board || null;     // CORE passes `montage`; `board` = the contract's name
  const armedBase = !!stage && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false;
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
  function cancelAll() { for (const id of Array.from(live)) cancel(id); }

  /* ---- seeded streams (Law V; append-only tags) --------------------------- */
  const seedBase = String(opts.seed || 'ir') + '|ir-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, floods: 0, words: 0, bells: 0, levers: 0, reels: 0, peaks: 0 };
  function cue(name, level, extra) {
    if (!armed()) return;
    counts.cues++;
    try {
      if (typeof eng.fire === 'function') eng.fire('audio_trigger', Object.assign({ name, level }, extra || {}));
    } catch (e) { /* a refused cue is not an error */ }
  }
  function ceremony(kind, o2) {
    if (!armed()) return null;
    try { return typeof eng.ceremony === 'function' ? eng.ceremony(kind, o2 || {}) : null; } catch (e) { return null; }
  }

  /* ---- identity (per class seed) ------------------------------------------ */
  const ID = (() => {
    const offArc = roll('hue') < C.OFF_ARC;
    const hueA = offArc ? C.HUE_OFF : Math.round(lerp(C.HUE_ARC, roll('hue')));
    const hueB = offArc ? Math.max(160, Math.min(205, hueA + 14))
      : Math.max(250, Math.min(350, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (16 + Math.round(roll('hue2') * 24))));
    const breath = +(7 + roll('breath') * 5).toFixed(1);             // 7..12s
    const dust = C.DUST_MIN + Math.floor(roll('dust') * C.DUST_SPAN);
    const stops = C.JOURNEY_MIN + Math.floor(roll('journey') * C.JOURNEY_SPAN);
    const journey = [];
    for (let i = 0; i < stops; i++) {
      journey.push({
        hue: i === 0 ? hueA : (i % 2 ? hueB : Math.round((hueA + hueB) / 2)),
        deep: +(i / Math.max(1, stops - 1)).toFixed(2),
        beam: +(0.55 + 0.3 * (i / Math.max(1, stops - 1))).toFixed(2),
      });
    }
    const mqPhase = (roll('mq-phase') * -3.4).toFixed(2) + 's';
    return { offArc, hueA, hueB, breath, dust, journey, mqPhase };
  })();
  const mqHue = tonightHue(opts.utcDate);

  /* ---- nodes ------------------------------------------------------------- */
  let mq = null; let lever = null; let bellEl = null; let reel = null;
  let flood = null; let glow = null; let dust = null; let word = null; let ghost = null; let veil = null;
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let stopIx = -1;
  let goldOn = false;
  let royalOn = false;
  let outOn = false;
  let heldOn = false;
  let bestStreakSeen = 0;
  let stopsSeen = 0;
  let answersSeen = 0;
  let flashTimer = 0; let floodTimer = 0; let wordTimer = 0; let leverTimer = 0; let bellTimer = 0;
  let reelTimer = 0; let peakTimer = 0; let ghostTimer = 0; let royalTimer = 0;
  const jackLog = [];

  function mount() {
    if (mq || !armedBase || !capsOk()) return;     // bgIntensity 0: the hall stays dark
    ensureStyle();
    setVar(stage, '--ir-cs-hue', ID.hueA);
    setVar(stage, '--ir-n-hue-a', ID.hueA);
    setVar(stage, '--ir-n-hue-b', ID.hueB);
    setVar(stage, '--ir-n-breath', ID.breath + 's');
    /* the hall layers in the backdrop: glow + dust */
    const bd = opts.backdrop || stage;
    glow = el('div', 'g-ir-cs-glow');
    dust = el('div', 'g-ir-cs-dust');
    try {
      if (glow) bd.appendChild(glow);
      if (dust) {
        for (let i = 0; i < ID.dust; i++) {
          const m = el('i', 'g-ir-cs-mote');
          if (!m) break;
          setVar(m, '--x', (18 + roll('mote') * 64).toFixed(1) + '%');
          setVar(m, '--y', (10 + roll('mote') * 70).toFixed(1) + '%');
          setVar(m, '--s', (2 + roll('mote') * 2.5).toFixed(1) + 'px');
          setVar(m, '--d', (11 + roll('mote') * 12).toFixed(1) + 's');
          setVar(m, '--dl', (-roll('mote') * 20).toFixed(1) + 's');
          setVar(m, '--dx', ((roll('mote') - 0.5) * 40).toFixed(0) + 'px');
          setVar(m, '--dy', (-(30 + roll('mote') * 70)).toFixed(0) + 'px');
          dust.appendChild(m);
        }
        bd.appendChild(dust);
      }
    } catch (e) { /* partial mounts are fine */ }
    /* the veil (the held breath) - style.js styles it; mount one if CORE did not */
    try {
      const have = stage.querySelector ? stage.querySelector('.g-ir-veil') : null;
      if (!have) { veil = el('div', 'g-ir-veil'); if (veil) stage.appendChild(veil); }
    } catch (e) { veil = null; }
    /* the flood over the hall, the word and the ghost on the stage */
    flood = el('div', 'g-ir-cs-flood');
    word = el('div', 'g-ir-cs-word');
    ghost = el('div', 'g-ir-cs-ghost');
    try { if (flood) stage.appendChild(flood); } catch (e) { flood = null; }
    try { if (word) stage.appendChild(word); } catch (e) { word = null; }
    /* the ghost rides INSIDE the slip when CORE hands it over (so it follows the
       slip's own transform and a relayout), else on the stage */
    try { if (ghost) ((opts.stopEl && opts.stopEl.appendChild) ? opts.stopEl : stage).appendChild(ghost); } catch (e) { ghost = null; }
    /* the bezel, the lever, the bell and the reel band live INSIDE the screen */
    const host = board && board.appendChild ? board : stage;
    mq = el('div', 'g-ir-mq');
    if (mq) {
      setVar(mq, '--ir-mqhue', mqHue);
      setVar(mq, '--ir-mqp', ID.mqPhase);
      for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) { const bar = el('i', cls); if (bar) mq.appendChild(bar); }
      try { host.appendChild(mq); } catch (e) { mq = null; }
    }
    lever = el('i', 'g-ir-cs-lever');
    bellEl = el('i', 'g-ir-cs-bell');
    reel = el('i', 'g-ir-cs-reel');
    try { if (lever) host.appendChild(lever); } catch (e) { lever = null; }
    try { if (bellEl) host.appendChild(bellEl); } catch (e) { bellEl = null; }
    try { if (reel) host.appendChild(reel); } catch (e) { reel = null; }
    paintStop(0);
    paintHeat(true);
  }

  /** One journey stop: hue + depth + beam, painted as props (style.js tweens). */
  function paintStop(ix) {
    const stop = ID.journey[Math.max(0, Math.min(ID.journey.length - 1, ix))];
    if (!stop) return;
    stopIx = ix;
    setVar(stage, '--ir-cs-hue', stop.hue);
    setVar(stage, '--ir-cs-deep', stop.deep);
    setVar(stage, '--ir-n-beam', stop.beam);
  }
  function walkJourney(h) {
    const n = ID.journey.length;
    if (n < 2) return;
    const raw = Math.min(n - 1, Math.floor(h * n));
    if (stopIx < 0) { paintStop(raw); return; }
    if (raw > stopIx && h >= (stopIx + 1) / n + C.STOP_HYST) paintStop(stopIx + 1);
    else if (raw < stopIx && h <= stopIx / n - C.STOP_HYST) paintStop(stopIx - 1);
  }
  function paintHeat(force) {
    if (!mq) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    const gold = goldOn || royalOn;
    const period = gold ? C.MQ_T_GOLD : (C.MQ_T_SLOW + (C.MQ_T_FAST - C.MQ_T_SLOW) * heat);
    const alpha = outOn ? 0.08 : (gold ? C.MQ_A_GOLD : lerp([C.MQ_A_LO, C.MQ_A_HI], heat));
    setVar(mq, '--ir-mqt', period.toFixed(2) + 's');
    setVar(mq, '--ir-mqa', alpha.toFixed(2));
    walkJourney(heat);
  }

  /* ---- the light ---------------------------------------------------------- */
  function flashMarquee(ms) {
    if (!mq || !armed() || still) return;
    setCls(mq, 'is-flash', true);
    cancel(flashTimer);
    flashTimer = after(ms || C.MQ_FLASH_MS, () => setCls(mq, 'is-flash', false));
  }
  function floodHall(q, ms) {
    if (!flood || !armed()) return;
    counts.floods++;
    setVar(flood, '--ir-cs-pay', lerp(C.FLOOD_A, q).toFixed(2));
    setCls(flood, 'is-on', true);
    cancel(floodTimer);
    floodTimer = after(ms || C.FLOOD_MS, () => setCls(flood, 'is-on', false));
  }
  function showWord(key, fallback, tone) {
    if (!word || !armed()) return;
    counts.words++;
    try { word.textContent = t(key, fallback); } catch (e) { /* noop */ }
    word.className = 'g-ir-cs-word' + (tone ? ' ' + tone : '');
    try { if (typeof word.offsetWidth === 'number') void word.offsetWidth; } catch (e) { /* noop */ }
    setCls(word, 'is-on', true);
    cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 40, () => setCls(word, 'is-on', false));
  }
  function pullLever() {
    if (!lever || !armed()) return;
    counts.levers++;
    if (still) return;
    restart(lever, 'is-pulled');
    cancel(leverTimer);
    leverTimer = after(C.LEVER_MS + 40, () => setCls(lever, 'is-pulled', false));
  }
  function ringBell() {
    if (!armed()) return;
    counts.bells++;
    cue('sting', C.BELL_LEVEL, { pitch: C.BELL_PITCH });
    after(150, () => cue('sting', C.BELL_LEVEL * 0.8, { pitch: C.BELL_PITCH }));
    if (bellEl) {
      restart(bellEl, 'is-rung');
      cancel(bellTimer);
      bellTimer = after(C.BELL_MS + 40, () => setCls(bellEl, 'is-rung', false));
    }
  }
  /** The true option ghosts through: an overlay placed from its rect. */
  function ghostOption(optEl) {
    if (!ghost || !armed() || !optEl) return false;
    const r = rectOf(optEl);
    const s = rectOf(ghost.parentNode || stage);
    if (!r || !s || !r.width) return false;
    setVar(ghost, '--x', (r.left - s.left).toFixed(0) + 'px');
    setVar(ghost, '--y', (r.top - s.top).toFixed(0) + 'px');
    setVar(ghost, '--w', r.width.toFixed(0) + 'px');
    setVar(ghost, '--h', r.height.toFixed(0) + 'px');
    restart(ghost, 'is-on');
    cancel(ghostTimer);
    ghostTimer = after(C.ALMOST_MS + 40, () => setCls(ghost, 'is-on', false));
    return true;
  }
  /** Find the true option after a verdict: the event says, or CORE marked it. */
  function truthOption(e) {
    let idx = null;
    for (const k of ['truth', 'trueIndex', 'truthIndex', 'correctIndex']) {
      if (e && Number.isFinite(Number(e[k])) && e[k] != null) { idx = Number(e[k]); break; }
    }
    if (!stage || typeof stage.querySelectorAll !== 'function') return null;
    let opts2 = [];
    try { opts2 = Array.from(stage.querySelectorAll('.g-ir-opt')); } catch (err) { opts2 = []; }
    if (!opts2.length) return null;
    if (idx != null) {
      for (const n of opts2) { try { if (Number(n.getAttribute('data-i')) === idx) return n; } catch (err) { /* noop */ } }
    }
    for (const n of opts2) {
      try {
        if ((n.classList && (n.classList.contains('is-true') || n.classList.contains('is-correct') || n.classList.contains('is-truth')))
          || n.getAttribute('data-verdict') === 'correct' || n.getAttribute('data-truth') === '1') return n;
      } catch (err) { /* noop */ }
    }
    return null;
  }

  /* ---- the ladder --------------------------------------------------------- */
  function jackpot(intensity, why) {
    if (!armed()) return;
    counts.jackpots++;
    jackLog.push(why);
    /* garnish:false is LOAD-BEARING here (mosaic rework, 2026-08-23). The
     * engine's jackpot ceremony otherwise FORCES a drain|spiral wash, and both
     * of those are POOL effects in this class - a wash the ledger never saw
     * would give the next question a second honest answer. The hall's own
     * flood + marquee are the payout instead. */
    ceremony('jackpot', { intensity, garnish: false });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashMarquee(C.MQ_FLASH_MS + 300 * intensity);
    floodHall(intensity, C.FLOOD_MS + 300);
  }
  function nearMiss(intensity, pitch) {
    if (!armed()) return;
    counts.nearMisses++;
    ceremony('near_miss', { intensity });
    cue('near_miss', C.NEAR_LEVEL, { pitch: pitch || 0.8 });
  }
  function royal() {
    if (royalOn || !armed()) return;
    royalOn = true;
    counts.royal = 1;
    setCls(mq, 'is-gold', true);
    setCls(flood, 'is-royal', true);
    showWord('ir_royal', 'ROYAL', 'gold');
    ceremony('jackpot', { intensity: C.ROYAL_I, garnish: false });   // see jackpot()
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.2 });
    after(260, () => cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.5 }));
    after(520, () => cue('stamp', 0.5, { pitch: 1 }));
    flashMarquee(C.ROYAL_MS);
    cancel(royalTimer);
    royalTimer = after(C.ROYAL_MS, () => { setCls(flood, 'is-royal', false); paintHeat(true); });
    paintHeat(true);
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      mount();
      say('casino: hall lit (hue ' + ID.hueA + '/' + ID.hueB + (ID.offArc ? ' off-arc' : '') + ', journey '
        + ID.journey.length + ', dust ' + ID.dust + ', tonight ' + mqHue + ')');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started) paintHeat(false);
    },
    /** The reel change: a band of light sweeps the screen. CORE calls it with
     *  'reshuffle' now (the wall turning itself over on a resume); the older
     *  layout names still answer, so nothing here had to be renamed. */
    layoutChange(kind) {
      if (!armed() || !started) return;
      counts.reels++;
      cue('slide', 0.14, { pitch: kind === 'reshuffle' ? 1.15 : kind === 'swirl' ? 0.8 : kind === 'mosaic' ? 1.1 : 1 });
      if (still || !reel) return;
      restart(reel, 'is-on');
      cancel(reelTimer);
      reelTimer = after(C.REEL_MS + 40, () => setCls(reel, 'is-on', false));
    },
    /** A density peak: the beam flares, the bezel flashes. */
    densityPeak() {
      if (!armed() || !started) return;
      counts.peaks++;
      cue('whisper', 0.2, { pitch: 1.2 });
      flashMarquee(C.MQ_FLASH_MS);
      setCls(stage, 'g-ir-cs-peak', true);
      cancel(peakTimer);
      peakTimer = after(C.PEAK_MS, () => setCls(stage, 'g-ir-cs-peak', false));
    },
    /** THE STOP is the slot-pull: the chase holds, the lever drops, the bell
     *  rings only when the stop is announced. CORE calls stopBeat(n, announced);
     *  the contract's name, stop(n, announced), is honoured below when called
     *  WITH a numeric first argument. */
    stopBeat(n, announced) {
      if (!armed() || !started) return;
      stopsSeen++;
      heldOn = true;
      setCls(mq, 'is-held', true);
      pullLever();
      if (announced) ringBell();
      else cue('slide', C.LEVER_LEVEL, { pitch: 0.75 });
      void n;
    },
    /** Every answer, after the ledger: the chime ladder, the payout light,
     *  the almost, the jackpot ladder. The chase is released. */
    answer(ev) {
      if (!armed() || !started) return;
      const e = ev || {};
      answersSeen++;
      heldOn = false;
      setCls(mq, 'is-held', false);
      const streak = Math.max(0, Number(e.streak) || 0);
      bestStreakSeen = Math.max(bestStreakSeen, streak);
      if (e.correct) {
        /* THE SOUND LADDER + PAYOUT LIGHT */
        const q = clamp01(Math.min(6, streak) / 6);
        cue('sting', lerp(C.HIT_LEVEL, q), { pitch: pitchForStreak(streak) });
        floodHall(q);
        flashMarquee();
        /* THE JACKPOT LADDER */
        const rung = jackpotFor(streak);
        if (rung === 'royal') { royal(); }
        else if (rung === 'major') {
          const mi = C.MAJOR_STREAKS.indexOf(streak);
          cue('streak', C.MILESTONE_LEVEL, { pitch: pitchForStreak(streak) });
          jackpot(C.MAJOR_I[Math.max(0, mi)], 'major@' + streak);
        } else if (rung === 'minor' && roll('jack-minor') < lerp(C.MINOR_CHANCE, heat)) {
          jackpot(C.MINOR_I, 'minor@' + streak);
        }
        return;
      }
      if (e.timedOut) {
        cue('whisper', C.TIMEOUT_LEVEL, { pitch: 0.85 });      // the soft exhale
        floodHall(0.05, 400);
        return;
      }
      /* a miss: a muted thud, never silence - and THE ALMOST if the truth shows */
      cue('bump', C.MISS_LEVEL, { pitch: 0.7 });
      const truth = truthOption(e);
      if (truth && !still && ghostOption(truth)) {
        showWord('ir_almost', 'ALMOST', 'lav');
        nearMiss(C.NEAR_MISS_I, 0.8);
      }
    },
    /** A planted decoy went unanswered: a lavender glint, a soft word. */
    plantResisted() {
      if (!armed() || !started) return;
      cue('whisper', 0.25, { pitch: 1.3 });
      floodHall(0.12, 500);
      showWord('ir_resisted', 'RESISTED', 'lav');
    },
    /** The last stretch / the final stop: gold bezel, frantic chase. */
    bell(on) {
      goldOn = !!on;
      setCls(mq, 'is-gold', goldOn || royalOn);
      paintHeat(true);
    },
    /** The class ended short of the royal: a dim payout, never silence. */
    dimOut() {
      outOn = true;
      goldOn = false;
      heldOn = false;
      setCls(mq, 'is-held', false);
      setCls(mq, 'is-flash', false);
      if (!royalOn) setCls(mq, 'is-gold', false);
      setCls(mq, 'is-out', true);
      if (armed() && started) {
        floodHall(0.15, C.END_DIM_MS);
        cue('wash', 0.25, { pitch: 0.9 });
      }
      paintHeat(true);
    },
    pause() { /* CSS chases are frozen by the stage's .suspended rule; timers are the game's */ },
    resume() { /* nothing to rebuild: transient light simply ended */ },
    /** The class is over: no light may pulse again. (Called with a numeric
     *  first argument it is the contract's `stop(n, announced)` event instead.) */
    stop(n, announced) {
      if (typeof n === 'number') { api.stopBeat(n, announced); return; }
      cancelAll();
      setCls(mq, 'is-flash', false);
      setCls(flood, 'is-on', false);
      setCls(ghost, 'is-on', false);
      setCls(stage, 'g-ir-cs-peak', false);
    },
    destroy() {
      destroyed = true;
      cancelAll();
      for (const n of [mq, lever, bellEl, reel, flood, glow, dust, word, ghost, veil]) {
        try { if (n && typeof n.remove === 'function') n.remove(); else if (n && n.parentNode) n.parentNode.removeChild(n); } catch (e) { /* noop */ }
      }
      mq = lever = bellEl = reel = flood = glow = dust = word = ghost = veil = null;
      setCls(stage, 'g-ir-cs-peak', false);
      if (stage && stage.style) {
        for (const k of ['--ir-cs-hue', '--ir-cs-deep', '--ir-n-hue-a', '--ir-n-hue-b', '--ir-n-breath', '--ir-n-beam']) {
          try { stage.style.removeProperty(k); } catch (e) { /* noop */ }
        }
      }
    },
    diagnostics() {
      return {
        armed: armed(), started, heat: +heat.toFixed(3), hueA: ID.hueA, hueB: ID.hueB, offArc: ID.offArc,
        journey: ID.journey.length, stop: stopIx, dust: ID.dust, tonightHue: mqHue,
        gold: goldOn, royal: royalOn, out: outOn, held: heldOn,
        stops: stopsSeen, answers: answersSeen, bestStreak: bestStreakSeen,
        counts: Object.assign({}, counts), jackpots: jackLog.slice(),
        liveTimers: live.size, nodes: [mq, lever, bellEl, reel, flood, glow, dust, word, ghost].filter(Boolean).length,
      };
    },
  };
  return api;
}

export default createIrCasino;
