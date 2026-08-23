/* ============================================================================
 * games/echo/casino.js - DECK II of the House Rules for the music room: THE
 * FLOOR. The pads' tones and the combo ratchet are index.js's (one engine
 * recipe, six pitches, +1 semitone a link); this file is the LIGHT that rides
 * the tune, and the ladder of payouts that makes a long echo feel like a win.
 *
 *   ROOM IDENTITY   seeded per CLASS seed (Deck I): the six pad hues rotate as
 *                   a set around the wheel (they never collapse onto each
 *                   other - the rotation is one number on the stage), ~6% of
 *                   rooms leave the house arc for a cold night (the rotation
 *                   lands the pink on teal), the halo's bulb count and size,
 *                   its chase phase, the spotlight's breath, the media's
 *                   ken-burns period, and the direction the remix runs. Same
 *                   seed, same room; a retake walks into the identical show.
 *   THE HALO        a ring of bulbs hugging the chalk circle (the board is
 *                   round, so the marquee is a halo, not a box). Crawls lazily
 *                   at heat 0, chases hungrily as the sequence lengthens,
 *                   flashes on a payout, turns amber and slow for an ENCORE,
 *                   gold and frantic for the bell and the ROYAL, sighs out -
 *                   never cuts - on a dim-out.
 *   THE FLOOR POOLS six pools of light on the boards, one under each pad,
 *                   that take the pad's light when it plays (playback) and
 *                   PAY it back brighter when it is pressed right (payout
 *                   light, scaled by the link of the streak). The pools decay
 *                   at the lamp's own rate - they never remember the sequence
 *                   for the player.
 *   PAYOUT LIGHT    a correct press: the pool under the pad blooms, the halo
 *                   flashes by the link, the streak chip punches (transform
 *                   only; the TEXT is index.js's). A cleared sequence: a sweep
 *                   of all six pools around the ring, the room tint pulses,
 *                   an arpeggio pitched by the length.
 *   THE ALMOST      near-miss staging: a wrong press on the pad NEXT to the
 *                   right one (ring-adjacent) is staged as a near miss - the
 *                   pool under the pad you needed hums, the word comes up,
 *                   the near_miss ceremony rises. A wrong press anywhere else
 *                   is a muted thud and a cold flash, never silence.
 *   THE JACKPOT LADDER   minor / major / ROYAL on sequence milestones. Minor: a
 *                   cleared sequence may roll one (seeded, chance rises with
 *                   heat) - a flash and a chime. Major: every even length from
 *                   six, deterministic - THE REMIX: the player's own sequence
 *                   (recorded from the playback this deck was shown) replays
 *                   as a light show at double tempo around the ring, forward,
 *                   as a palindrome or rotated (the seed picks), each step a
 *                   pool bloom + a bulb sector + a soft blip at the pad's own
 *                   pitch. ROYAL: once a class, at the tier's royal length -
 *                   the hall floods gold, the halo goes gold, the remix runs
 *                   twice, the word comes up. A failed class still gets a dim
 *                   payout on dim-out: silence is where people stand up.
 *   ENCORE          the same sequence at half tempo: the room goes warm and
 *                   slow - amber halo at the calm pace, warm pools, a longer
 *                   breath - and comes back when the encore ends.
 *   RESISTED        a decoy let pass: a glint runs the chalk circle once and a
 *                   tiny tick sounds. The trap you dodged is acknowledged.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects index.js hands it AFTER its
 *       own accounting; writes nothing about the sequence, the streak, the
 *       length, accuracy or the grade; the streak chip is moved by transform
 *       only, its text is never touched. The sequence this deck records from
 *       padLit is used ONLY for the remix light show after the player has
 *       already cleared it.
 *   II  input honest  - every node here is pointer-events:none, lives inside
 *       the ring UNDER the pads (z 0 vs the pads' z 1) or in the backdrop;
 *       data-pad / data-state / the pads' own nodes are never written; no
 *       pad is ever moved, covered, resized or delayed.
 *   III never still   - the halo crawls at heat 0, the pools ember, the
 *       spotlight breathes (style.js), the media drifts.
 *   IV  images > text - three words this deck can show (ec_near_miss,
 *       ec_jackpot, ec_royal), through the lexicon, for < 700ms.
 *   V   seeded        - per-tag mulberry32 off seed+'|ec-casino|<tag>'
 *       (append-only tags; a new tag never shifts an old stream); no
 *       Math.random anywhere.
 *   VI  exits sacred  - capsOk false (bgIntensity 0) disarms every light;
 *       reduced motion keeps a static dim halo and the pools' glow (no chase,
 *       no sweep, no remix travel, no punch; blooms become fades); the stage's
 *       .suspended rule freezes the chase; every timer rides the game's
 *       pause-aware registry AND a local set so destroy() cannot leak one.
 *   VII lexicon       - ec_near_miss / ec_jackpot / ec_royal only, via opts.t.
 *
 * ENGINE vs GAME-LOCAL: cues, the jackpot / near_miss ceremonies and nothing
 * else go through opts.engine (index.js's null-safe weld: audio ceiling by
 * tier, clickSafe forced, pitch passthrough). The halo, the pools, the word,
 * the room pulse and the chip punch are game-local: they sit on the game's
 * own geometry (the ring), which no engine primitive knows.
 * NODE BUDGET: halo (1 + bulbs), overlay (1 + 6 pools + glint + word), room
 * pulse + royal flood in the backdrop. All reused; nothing per event.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { pitchFor } from './sequence.js';

export const EC_CASINO = Object.freeze({
  /* ---- the halo ------------------------------------------------------- */
  BULBS: Object.freeze([24, 30, 36]),          // seeded pick
  MQ_T_SLOW: 3.4,                              // chase period (s) at heat 0
  MQ_T_FAST: 0.9,                              // at heat 1
  MQ_A_LO: 0.22,                               // presence (opacity) band
  MQ_A_HI: 0.84,
  MQ_T_GOLD: 0.48,
  MQ_A_GOLD: 0.95,
  MQ_ENCORE_MUL: 1.7,                          // the encore's calm pace
  MQ_FLASH_MS: 540,
  MQ_DIP_MS: 900,
  /* ---- the pools -------------------------------------------------------- */
  POOL_LIT: 0.62,                              // playback level
  POOL_PAY: Object.freeze([0.8, 1.0]),         // payout level by link/8
  POOL_DECAY_MS: 620,                          // the lamp's own rate (style.js)
  POOL_HUM_MS: 1400,                           // the almost: the needed pool hums
  SWEEP_STEP_MS: 58,
  /* ---- payout ----------------------------------------------------------- */
  FLASH_LINK_STEP: 0.1,                        // halo flash strength per link
  ROOM_PULSE_MS: 680,
  ROOM_PULSE_A: Object.freeze([0.12, 0.34]),   // by len/12
  PUNCH_SCALE: Object.freeze([0.04, 0.22]),    // streak chip by link/8
  PUNCH_MS: 220,
  /* ---- the near miss ---------------------------------------------------- */
  WORD_MS: 640,
  NEAR_MISS_I: 0.55,
  /* ---- the jackpot ladder ----------------------------------------------- */
  MINOR_FROM_LEN: 4,
  MINOR_CHANCE: Object.freeze([0.18, 0.5]),    // by heat
  MINOR_I: 0.35,
  MAJOR_FROM_LEN: 6,                           // every even length from here
  MAJOR_I: Object.freeze([0.5, 0.85]),         // by len/14
  ROYAL_LEN: Object.freeze({ 1: 9, 2: 10, 3: 11, 4: 12 }),
  ROYAL_I: 1,
  ROYAL_MS: 3600,
  END_DIM_MS: 1400,
  /* ---- the remix -------------------------------------------------------- */
  REMIX_STEP_MS: 170,
  REMIX_ROYAL_STEP_MS: 150,
  REMIX_MAX_STEPS: 28,
  REMIX_LEVEL: 0.28,
  /* ---- sound (light's companions; the pad tones are index.js's) -------- */
  MILESTONE_LEVEL: 0.42,
  JACKPOT_LEVEL: 0.5,
  NEAR_LEVEL: 0.3,
  THUD_LEVEL: 0.24,
  RESIST_LEVEL: 0.22,
  AUDIO_CEIL: Object.freeze({ 1: 0.45, 2: 0.6, 3: 0.75, 4: 0.9 }),
  /* ---- identity ----------------------------------------------------------- */
  OFF_ARC: 0.06,
  ROT_SPAN: 40,                                // the set rotates 0..40deg
  ROT_OFF: 150,                                // the cold night: pink lands near teal
  BREATH: Object.freeze([5.8, 8.6]),           // s
  KB: Object.freeze([20, 32]),                 // s
  SPOT: Object.freeze([0.6, 0.95]),           // spotlight presence by heat
  HEAT_REPAINT_STEP: 0.03,
});

const STYLE_ID = 'g-ec-casino-style';
const STYLE_TEXT = `
/* THE HALO: bulbs on the chalk circle, inside the ring, under the pads */
.g-ec-mq{position:absolute;inset:-7%;border-radius:50%;pointer-events:none;z-index:0;
  --ec-mq-t:3.4s;--ec-mq-a:.22;--ec-mq-hue:330;--ec-mq-f:1;
  opacity:var(--ec-mq-a);transition:opacity .9s ease}
.g-ec-mq *{pointer-events:none}
.g-ec-mq-bulb{position:absolute;width:var(--ec-mq-d,7px);height:var(--ec-mq-d,7px);margin:calc(var(--ec-mq-d,7px) * -.5);
  border-radius:50%;background:hsl(var(--ec-mq-hue) 95% 74%);
  box-shadow:0 0 6px hsl(var(--ec-mq-hue) 95% 70% / .9),0 0 14px hsl(var(--ec-mq-hue) 90% 60% / .5);
  animation:g-ec-mq-chase var(--ec-mq-t) linear infinite;
  animation-delay:calc(var(--i) / var(--n) * var(--ec-mq-t) * -1 + var(--ec-mq-p,0s))}
@keyframes g-ec-mq-chase{0%{opacity:.18;transform:scale(.8)}12%{opacity:1;transform:scale(1.15)}40%{opacity:.35;transform:scale(.92)}100%{opacity:.18;transform:scale(.8)}}
.g-ec-mq.gold{--ec-mq-hue:46 !important}
.g-ec-mq.amber{--ec-mq-hue:36 !important}
.g-ec-mq.flash .g-ec-mq-bulb{animation-duration:.22s;filter:brightness(calc(1.2 + .5 * var(--ec-mq-f,1)))}
.g-ec-mq.dip{opacity:calc(var(--ec-mq-a) * .35);transition:opacity .12s ease}
.g-ec-mq.out{opacity:0 !important;transition:opacity 1.6s ease}
/* a bulb sector lit for the remix: the bulbs nearest a pad flare */
.g-ec-mq-bulb.sec{animation:none;opacity:1;transform:scale(1.35);filter:brightness(1.7)}

/* THE OVERLAY: the pools, the glint and the word, under the pads */
.g-ec-cs{position:absolute;inset:0;border-radius:50%;pointer-events:none;z-index:0;overflow:visible}
.g-ec-cs *{pointer-events:none}
.g-ec-cs-pool{position:absolute;left:calc(50% + var(--px) * 1%);top:calc(50% + var(--py) * 1%);
  width:44%;height:44%;border-radius:50%;transform:translate(-50%,-50%) scale(calc(.8 + .35 * var(--ec-cs-l,0)));
  --ec-h:calc(var(--ec-h-base,330) + var(--ec-n-rot,0));
  background:radial-gradient(circle, hsl(var(--ec-h) 92% 70% / .55) 0%, hsl(var(--ec-h) 90% 62% / .22) 32%, transparent 66%);
  opacity:calc(.05 + .95 * var(--ec-cs-l,0));
  transition:opacity .62s ease-out, transform .62s ease-out;
  animation:g-ec-pool-ember 7s ease-in-out infinite alternate;animation-delay:calc(var(--i) * -1.15s)}
@keyframes g-ec-pool-ember{from{filter:brightness(.9)}to{filter:brightness(1.25)}}
.g-ec-cs-pool.up{transition:opacity .07s ease-out, transform .09s cubic-bezier(.2,1.4,.4,1)}
.g-ec-cs-pool.cold{--ec-h:196 !important;filter:saturate(.4)}
.g-ec-cs-pool.hum{animation:g-ec-pool-hum .7s ease-in-out infinite alternate}
@keyframes g-ec-pool-hum{from{opacity:.35;transform:translate(-50%,-50%) scale(.95)}to{opacity:.95;transform:translate(-50%,-50%) scale(1.18)}}
.g-ec-cs-pool.warm{filter:sepia(.45) saturate(1.1)}
/* THE GLINT: one lap of the chalk circle for a decoy let pass */
.g-ec-cs-glint{position:absolute;inset:0;border-radius:50%;opacity:0;
  background:conic-gradient(from 0deg, transparent 0 300deg, hsl(46 95% 75% / .9) 345deg, transparent 360deg);
  -webkit-mask:radial-gradient(circle, transparent 0 calc(50% - 6px), #000 calc(50% - 5px) calc(50% - 1px), transparent 50%);
  mask:radial-gradient(circle, transparent 0 calc(50% - 6px), #000 calc(50% - 5px) calc(50% - 1px), transparent 50%)}
.g-ec-cs-glint.on{animation:g-ec-glint .9s linear 1}
@keyframes g-ec-glint{0%{opacity:0;transform:rotate(0deg)}15%{opacity:1}85%{opacity:1}100%{opacity:0;transform:rotate(360deg)}}
/* THE WORD: the centre of the ring, for under a second */
.g-ec-cs-word{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);opacity:0;
  font:800 clamp(13px,2.1vmin,20px)/1 var(--disp,system-ui,sans-serif);letter-spacing:.18em;text-transform:uppercase;
  white-space:nowrap;color:hsl(var(--ec-cs-hue,330) 90% 84%);text-shadow:0 0 12px hsl(var(--ec-cs-hue,330) 90% 70% / .85)}
.g-ec-cs-word.on{animation:g-ec-cs-word .64s ease-out forwards}
.g-ec-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-ec-cs-word.gold{color:#ffe08a;text-shadow:0 0 14px rgba(240,194,75,.9)}
@keyframes g-ec-cs-word{0%{opacity:0;transform:translate(-50%,-50%) scale(.7)}18%{opacity:1;transform:translate(-50%,-50%) scale(1.08)}
  70%{opacity:1;transform:translate(-50%,-50%) scale(1)}100%{opacity:0;transform:translate(-50%,-46%) scale(1)}}
/* the streak chip's punch: transform only */
.g-ec-cs-punch{transition:transform .22s cubic-bezier(.2,1.6,.4,1) !important;transform:scale(var(--ec-cs-ps,1))}
.g-ec-cs-bloom{box-shadow:0 0 0 2px hsl(var(--ec-cs-hue,330) 90% 70% / .55),0 0 22px hsl(var(--ec-cs-hue,330) 90% 65% / .6) !important;transition:box-shadow .3s ease}

/* THE ROOM: the tint pulse and the royal flood live in the backdrop */
.g-ec-bd-pulse{position:absolute;inset:0;opacity:0;
  background:radial-gradient(circle at 50% 56%, hsl(var(--ec-cs-hue,330) 90% 70% / .7) 0%, hsl(var(--ec-cs-hue,330) 90% 60% / .25) 30%, transparent 68%);
  transition:opacity .5s ease-out}
.g-ec-bd-pulse.on{opacity:var(--ec-cs-pay,.2);transition:opacity .08s ease-in}
.g-ec-bd-royal{position:absolute;inset:0;opacity:0;
  background:radial-gradient(circle at 50% 56%, hsl(46 100% 72% / .9) 0%, hsl(46 100% 60% / .4) 34%, hsl(46 100% 50% / .12) 70%, transparent 100%);
  transition:opacity .9s ease}
.g-ec-bd-royal.on{opacity:.85;transition:opacity .35s ease-out}
.g-ec-stage.g-ec-encore .g-ec-bd-pulse{background:radial-gradient(circle at 50% 56%, hsl(36 90% 70% / .6) 0%, transparent 66%)}
.g-ec-stage.g-ec-out .g-ec-cs-pool{opacity:.03;transition:opacity 1.6s ease}

@media (prefers-reduced-motion: reduce){
  .g-ec-mq-bulb{animation:none !important;opacity:.55}
  .g-ec-cs-pool{animation:none !important;transition:opacity .4s ease}
  .g-ec-cs-pool.hum{animation:none !important;opacity:.8}
  .g-ec-cs-glint.on{animation:none;opacity:.7}
  .g-ec-cs-word.on{animation:none;opacity:1}
}
html.arc-reduced .g-ec-mq-bulb{animation:none !important;opacity:.55}
html.arc-reduced .g-ec-cs-pool{animation:none !important;transition:opacity .4s ease;transform:translate(-50%,-50%)}
html.arc-reduced .g-ec-cs-pool.hum{animation:none !important;opacity:.8}
html.arc-reduced .g-ec-cs-glint.on{animation:none;opacity:.7}
html.arc-reduced .g-ec-cs-word.on{animation:none;opacity:1}
.g-ec-stage.suspended .g-ec-mq-bulb,.g-ec-stage.suspended .g-ec-cs-pool,.g-ec-stage.suspended .g-ec-cs-word,
.g-ec-stage.suspended .g-ec-cs-glint{animation-play-state:paused !important}
`;

function ensureStyle() {
  try {
    if (typeof document === 'undefined' || !document.createElement) return;
    if (document.getElementById && document.getElementById(STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = STYLE_TEXT;
    const host = document.head || document.documentElement || document.body;
    if (host && host.appendChild) host.appendChild(s);
    if (document._register) document._register(STYLE_ID, s);
  } catch (e) { /* cosmetic */ }
}

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }
const PAD_HUES = Object.freeze([330, 46, 178, 264, 20, 205]);

/** Ring-adjacent pads (the near miss): |a-b| == 1 on a ring of n. Pure. */
export function isAdjacent(a, b, n) {
  const m = Math.max(2, Math.floor(Number(n) || 6));
  const x = Math.floor(Number(a));
  const y = Math.floor(Number(b));
  if (!Number.isFinite(x) || !Number.isFinite(y) || x === y) return false;
  const d = Math.abs(x - y);
  return d === 1 || d === m - 1;
}
/** The royal length for a tier. Pure. */
export function royalLenFor(tier) {
  const t = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  return EC_CASINO.ROYAL_LEN[t] || 12;
}
/** Is a cleared length a major milestone? Every even length from six. Pure. */
export function isMajorLen(len) {
  const l = Math.floor(Number(len) || 0);
  return l >= EC_CASINO.MAJOR_FROM_LEN && l % 2 === 0;
}
/** The remix plan for a sequence: a list of pad indices in show order. Pure.
 *  variant 0 = forward, 1 = palindrome (forward then back, the turn shared),
 *  2 = rotated (starts a third in, wraps). Capped at REMIX_MAX_STEPS. */
export function remixPlan(seq, variant) {
  const s = Array.isArray(seq) ? seq.filter((x) => Number.isFinite(Number(x))).map((x) => Math.floor(Number(x))) : [];
  if (!s.length) return [];
  const v = Math.floor(Number(variant) || 0) % 3;
  let out;
  if (v === 1) out = s.concat(s.slice(0, -1).reverse());
  else if (v === 2 && s.length > 2) { const k = Math.floor(s.length / 3); out = s.slice(k).concat(s.slice(0, k)); }
  else out = s.slice();
  return out.slice(0, EC_CASINO.REMIX_MAX_STEPS);
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
function qsa(root, sel) {
  try { if (root && typeof root.querySelectorAll === 'function') return Array.from(root.querySelectorAll(sel)); } catch (e) { /* fall */ }
  return [];
}
function qs(root, sel) {
  try { if (root && typeof root.querySelector === 'function') return root.querySelector(sel) || null; } catch (e) { /* fall */ }
  return null;
}

/**
 * @param {Object} o
 *   seed, tier (1..4), stage (.g-ec-stage), board (.g-ec-ring), hud (the
 *   .g-ec-hud element OR {len, clock, streak, best}), backdrop (.g-ec-backdrop),
 *   timers {after, every?, clear|cancel}, reduced, capsOk (bool or () => bool),
 *   t (key, fallback) => string, engine {fire, ceremony?, channels?}, log,
 *   optional: pads () => Element[] (else read off the board), padCount
 */
export function createEcCasino(o) {
  const opts = o || {};
  const C = EC_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const reduced = !!opts.reduced;
  const tier = Math.max(1, Math.min(4, Math.round(Number(opts.tier) || 1)));
  const audioCeil = C.AUDIO_CEIL[tier] || C.AUDIO_CEIL[1];
  const eng = opts.engine || {};
  const armedBase = !!opts.stage && !!opts.board && !!opts.timers && typeof opts.timers.after === 'function'
    && typeof document !== 'undefined';
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
  const seedBase = String(opts.seed || 'ec') + '|ec-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, pulses: 0, words: 0, remixSteps: 0, resisted: 0 };
  /* THE DECOUPLE (W2 sec 3, owner ruling: a visual dial must not mute the room).
   * Every LIGHT below stays behind armed(); the cue road gates on everything
   * EXCEPT capsOk, because bgIntensity 0 is the player's visual exit (Law VI),
   * not a mute. */
  const sounds = () => armedBase && !destroyed;
  function cue(name, level, extra) {
    if (!sounds()) return;
    counts.cues++;
    const lv = Math.min(audioCeil, Math.max(0, Number(level) || 0));
    try {
      if (typeof eng.fire === 'function') eng.fire('audio_trigger', Object.assign({ name, level: lv }, extra || {}));
    } catch (e) { /* a refused cue is not an error */ }
  }
  function ceremony(kind, o2) {
    if (!armed()) return null;
    try { return typeof eng.ceremony === 'function' ? eng.ceremony(kind, o2 || {}) : null; } catch (e) { return null; }
  }

  /* ---- identity (per class seed; FIXED draw order) ------------------------ */
  const ID = (() => {
    const offArc = roll('arc') < C.OFF_ARC;
    const rot = offArc ? C.ROT_OFF + Math.round(roll('rot') * 20) : Math.round(roll('rot') * C.ROT_SPAN);
    const bulbs = C.BULBS[Math.floor(roll('bulbs') * C.BULBS.length)];
    const bulbD = 6 + Math.round(roll('bulbs') * 3);            // 6..9 px
    const phase = -(roll('mq-phase') * 3.4);                     // s
    const breath = lerp(C.BREATH, roll('breath'));
    const kb = lerp(C.KB, roll('kb'));
    const remixDir = roll('remix-dir') < 0.5 ? -1 : 1;
    return { offArc, rot, bulbs, bulbD, phase, breath: +breath.toFixed(1), kb: +kb.toFixed(1), remixDir };
  })();
  const hueOf = (i) => ((PAD_HUES[Math.max(0, Math.min(5, i | 0))] + ID.rot) % 360);
  const mqHue = hueOf(0);

  /* ---- nodes ------------------------------------------------------------- */
  let halo = null; let bulbs = [];
  let cs = null; const pools = []; let glint = null; let word = null;
  let pulse = null; let royal = null;
  const props = new Set();
  const stageClasses = new Set();
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let bellOn = false; let encoreOn = false; let royalOn = false; let outOn = false;
  let padCount = 6;
  const poolTimers = new Array(6).fill(0);
  let flashTimer = 0; let dipTimer = 0; let wordTimer = 0; let pulseTimer = 0; let punchTimer = 0;
  let royalTimer = 0; let glintTimer = 0; let humTimer = 0; let humPool = -1;
  const sweepTimers = new Set();
  const remixTimers = new Set();
  let remixing = false;
  let seq = [];                 // the LAST playback this deck was shown (for the remix)
  let bestLen = 0;
  const jackLog = [];
  let almostSeen = 0;

  function streakChip() {
    const h = opts.hud;
    if (!h) return null;
    if (h.streak) return h.streak;
    return qs(h, '.g-ec-streak');
  }
  function livePads() {
    if (typeof opts.pads === 'function') { try { const p = opts.pads(); if (p && p.length) return Array.from(p); } catch (e) { /* fall */ } }
    return qsa(opts.board, '.g-ec-pad');
  }
  function countPads() {
    const n = Number(opts.padCount) || livePads().length || 6;
    padCount = Math.max(2, Math.min(6, n));
    return padCount;
  }
  function angleOf(i) {
    const n = padCount;
    const step = n === 4 ? 90 : 60;
    return (-90 + step * i) * Math.PI / 180;
  }

  function setProp(k, v) { setVar(opts.stage, k, v); props.add(k); }
  function stageClass(name, on) {
    setCls(opts.stage, name, on);
    if (on) stageClasses.add(name); else stageClasses.delete(name);
  }

  function mount() {
    if (halo || !armedBase || !capsOk()) return;      // bgIntensity 0: the floor stays dark
    ensureStyle();
    countPads();
    /* the room's identity on the stage: style.js reads these */
    setProp('--ec-n-rot', String(ID.rot));
    setProp('--ec-n-breath', ID.breath + 's');
    setProp('--ec-n-kb', ID.kb + 's');
    setProp('--ec-cs-hue', String(mqHue));
    if (ID.offArc) {
      setProp('--ec-n-la', 'hsl(190 60% 70% / .18)');
      setProp('--ec-n-lb', 'hsl(210 60% 60% / .14)');
    }
    /* THE HALO, first child of the ring so it paints under the pads */
    halo = el('div', 'g-ec-mq');
    if (halo) {
      setVar(halo, '--ec-mq-hue', mqHue);
      setVar(halo, '--ec-mq-d', ID.bulbD + 'px');
      setVar(halo, '--n', ID.bulbs);
      setVar(halo, '--ec-mq-p', ID.phase.toFixed(2) + 's');
      for (let i = 0; i < ID.bulbs; i++) {
        const b = el('span', 'g-ec-mq-bulb');
        if (!b) break;
        const a = (i / ID.bulbs) * Math.PI * 2 - Math.PI / 2;
        try {
          b.style.left = (50 + 50 * Math.cos(a)).toFixed(2) + '%';
          b.style.top = (50 + 50 * Math.sin(a)).toFixed(2) + '%';
        } catch (e) { /* shim */ }
        setVar(b, '--i', i);
        bulbs.push(b);
        halo.appendChild(b);
      }
      try {
        if (typeof opts.board.insertBefore === 'function' && opts.board.firstChild) opts.board.insertBefore(halo, opts.board.firstChild);
        else opts.board.appendChild(halo);
      } catch (e) { halo = null; bulbs = []; }
    }
    /* THE OVERLAY: pools, glint, word */
    cs = el('div', 'g-ec-cs');
    if (cs) {
      for (let i = 0; i < 6; i++) {
        const p = el('i', 'g-ec-cs-pool');
        if (!p) break;
        const a = angleOf(i);
        setVar(p, '--i', i);
        setVar(p, '--px', (35.5 * Math.cos(a)).toFixed(2));
        setVar(p, '--py', (35.5 * Math.sin(a)).toFixed(2));
        setVar(p, '--ec-h-base', PAD_HUES[i]);
        setVar(p, '--ec-cs-l', '0');
        if (i >= padCount) { try { p.style.opacity = '0'; } catch (e) { /* shim */ } }
        pools.push(p);
        cs.appendChild(p);
      }
      glint = el('i', 'g-ec-cs-glint');
      if (glint) cs.appendChild(glint);
      word = el('div', 'g-ec-cs-word');
      if (word) cs.appendChild(word);
      try {
        if (halo && typeof opts.board.insertBefore === 'function' && halo.nextSibling) opts.board.insertBefore(cs, halo.nextSibling);
        else if (typeof opts.board.insertBefore === 'function' && opts.board.firstChild && !halo) opts.board.insertBefore(cs, opts.board.firstChild);
        else opts.board.appendChild(cs);
      } catch (e) { cs = null; }
    }
    /* THE ROOM: pulse + royal flood in the backdrop */
    if (opts.backdrop && opts.backdrop.appendChild) {
      pulse = el('div', 'g-ec-bd-pulse');
      if (pulse) opts.backdrop.appendChild(pulse);
      royal = el('div', 'g-ec-bd-royal');
      if (royal) opts.backdrop.appendChild(royal);
    }
    paintHeat(true);
  }

  function paintHeat(force) {
    if (!halo && !opts.stage) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    if (halo) {
      const gold = bellOn || royalOn;
      let period = gold ? C.MQ_T_GOLD : (C.MQ_T_SLOW + (C.MQ_T_FAST - C.MQ_T_SLOW) * heat);
      if (encoreOn && !gold) period *= C.MQ_ENCORE_MUL;
      const alpha = outOn ? 0 : (gold ? C.MQ_A_GOLD : lerp([C.MQ_A_LO, C.MQ_A_HI], heat));
      setVar(halo, '--ec-mq-t', period.toFixed(2) + 's');
      setVar(halo, '--ec-mq-a', alpha.toFixed(2));
    }
    setProp('--ec-n-spot', lerp(C.SPOT, heat).toFixed(2));
  }

  /* ---- the light ---------------------------------------------------------- */
  function flashHalo(strength, ms) {
    if (!halo || !armed() || reduced) return;
    setVar(halo, '--ec-mq-f', Math.max(1, Math.min(2.2, Number(strength) || 1)).toFixed(2));
    restart(halo, 'flash');
    cancel(flashTimer);
    flashTimer = after(ms || C.MQ_FLASH_MS, () => { flashTimer = 0; setCls(halo, 'flash', false); });
  }
  function dipHalo() {
    if (!halo || !armed()) return;
    setCls(halo, 'flash', false);
    setCls(halo, 'dip', true);
    cancel(dipTimer);
    dipTimer = after(C.MQ_DIP_MS, () => { dipTimer = 0; setCls(halo, 'dip', false); });
  }
  /** A pool takes a level and decays back at the lamp's rate. */
  function poolUp(i, level, holdMs, extraCls) {
    const p = pools[i];
    if (!p || !armed()) return;
    setCls(p, 'up', true);
    setCls(p, 'cold', extraCls === 'cold');
    setVar(p, '--ec-cs-l', clamp01(level).toFixed(2));
    if (poolTimers[i]) cancel(poolTimers[i]);
    poolTimers[i] = after(holdMs || C.POOL_DECAY_MS, () => {
      poolTimers[i] = 0;
      setCls(p, 'up', false);
      setCls(p, 'cold', false);
      setVar(p, '--ec-cs-l', '0');
    });
  }
  function poolsDown() {
    for (let i = 0; i < pools.length; i++) {
      if (poolTimers[i]) { cancel(poolTimers[i]); poolTimers[i] = 0; }
      setCls(pools[i], 'up', false);
      setCls(pools[i], 'cold', false);
      setVar(pools[i], '--ec-cs-l', '0');
    }
  }
  function clearHum() {
    if (humTimer) { cancel(humTimer); humTimer = 0; }
    if (humPool >= 0 && pools[humPool]) setCls(pools[humPool], 'hum', false);
    humPool = -1;
  }
  function hum(i) {
    clearHum();
    const p = pools[i];
    if (!p || !armed()) return;
    humPool = i;
    setCls(p, 'hum', true);
    humTimer = after(C.POOL_HUM_MS, () => { humTimer = 0; setCls(p, 'hum', false); humPool = -1; });
  }
  /** The sweep: every pool around the ring, one after another, the remix's direction. */
  function sweep(level, stepMs) {
    if (!armed()) return;
    if (reduced) { for (let i = 0; i < padCount; i++) poolUp(i, level, 900); return; }
    for (let k = 0; k < padCount; k++) {
      const i = ((ID.remixDir > 0 ? k : padCount - k) % padCount + padCount) % padCount;
      let id = 0;
      id = after(k * (stepMs || C.SWEEP_STEP_MS), () => { sweepTimers.delete(id); poolUp(i, level, C.POOL_DECAY_MS); });
      if (id) sweepTimers.add(id);
    }
  }
  function pulseRoom(q, ms) {
    if (!pulse || !armed()) return;
    counts.pulses++;
    setVar(pulse, '--ec-cs-pay', lerp(C.ROOM_PULSE_A, q).toFixed(2));
    setCls(pulse, 'on', true);
    cancel(pulseTimer);
    pulseTimer = after(ms || C.ROOM_PULSE_MS, () => { pulseTimer = 0; setCls(pulse, 'on', false); });
  }
  function punchStreak(q) {
    const chip = streakChip();
    if (!chip || !armed()) return;
    if (reduced) {
      setCls(chip, 'g-ec-cs-bloom', true);
      cancel(punchTimer);
      punchTimer = after(320, () => { punchTimer = 0; setCls(chip, 'g-ec-cs-bloom', false); });
      return;
    }
    setCls(chip, 'g-ec-cs-punch', true);
    setVar(chip, '--ec-cs-ps', (1 + lerp(C.PUNCH_SCALE, q)).toFixed(3));
    cancel(punchTimer);
    punchTimer = after(C.PUNCH_MS, () => { punchTimer = 0; setVar(chip, '--ec-cs-ps', '1'); });
  }
  function showWord(key, fallback, tone) {
    if (!word || !armed()) return;
    counts.words++;
    try { word.textContent = t(key, fallback); } catch (e) { return; }
    word.className = 'g-ec-cs-word' + (tone ? ' ' + tone : '');
    restart(word, 'on');
    cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 60, () => { wordTimer = 0; setCls(word, 'on', false); });
  }
  /** The bulbs nearest a pad flare for a beat (the remix's sector). */
  function sector(i, ms) {
    if (!bulbs.length || reduced) return;
    const n = bulbs.length;
    const centre = Math.round(((angleOf(i) + Math.PI / 2) / (Math.PI * 2)) * n);
    const span = Math.max(1, Math.round(n / padCount / 2) - 1);
    const lit = [];
    for (let d = -span; d <= span; d++) {
      const b = bulbs[((centre + d) % n + n) % n];
      if (b) { setCls(b, 'sec', true); lit.push(b); }
    }
    let id = 0;
    id = after(ms, () => { remixTimers.delete(id); for (const b of lit) setCls(b, 'sec', false); });
    if (id) remixTimers.add(id);
  }

  /* ---- the ladder --------------------------------------------------------- */
  function killRemix() {
    for (const id of Array.from(remixTimers)) cancel(id);
    remixTimers.clear();
    for (const b of bulbs) setCls(b, 'sec', false);
    remixing = false;
  }
  /** THE REMIX: the player's own sequence as a light show at double tempo. */
  function remix(stepMs, level, twice) {
    if (!armed() || !seq.length) return 0;
    killRemix();
    const plan = remixPlan(seq, Math.floor(roll('remix') * 3));
    const show = twice ? plan.concat(plan) : plan;
    remixing = true;
    const ratchet = Math.min(7, Math.max(0, seq.length - 3));
    for (let k = 0; k < show.length; k++) {
      const pad = show[k];
      let id = 0;
      id = after(k * stepMs, () => {
        remixTimers.delete(id);
        counts.remixSteps++;
        poolUp(pad, 1, C.POOL_DECAY_MS);
        sector(pad, stepMs);
        cue('blip', level, { pitch: pitchFor(pad, ratchet) });
        if (k === show.length - 1) remixing = false;
      });
      if (id) remixTimers.add(id);
    }
    return show.length;
  }
  function jackpot(intensity, why) {
    if (!armed()) return;
    counts.jackpots++;
    jackLog.push(why);
    /* the engine's jackpot forces a full-screen drain|spiral garnish (trap 33)
       - in a room where the NEXT thing is watching six pads, that wash lands
       on the playback and competes with the sequence; the remix IS the minor
       and major show, so only the ROYAL keeps the forced garnish */
    ceremony('jackpot', { intensity, garnish: /^royal/.test(String(why || '')) });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashHalo(1.3 + intensity * 0.8, C.MQ_FLASH_MS + 300 * intensity);
    pulseRoom(0.4 + 0.6 * intensity, C.ROOM_PULSE_MS + 320);
  }
  function nearMiss(intensity) {
    if (!armed()) return;
    counts.nearMisses++;
    ceremony('near_miss', { intensity });
    cue('near_miss', C.NEAR_LEVEL, { pitch: 1.1 });
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      if (!armedBase || !capsOk()) { say('casino: disarmed'); return; }
      mount();
      say('casino: floor lit (rot ' + ID.rot + (ID.offArc ? ' off-arc' : '') + ', ' + ID.bulbs + ' bulbs, '
        + padCount + ' pads, breath ' + ID.breath + 's)');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started) paintHeat(false);
    },

    /** Playback: pad i lights as item idx of len. The pool under it takes the light. */
    padLit(i, idx, len) {
      const pad = Math.floor(Number(i));
      if (!Number.isFinite(pad)) return;
      const k = Math.floor(Number(idx));
      if (Number.isFinite(k) && k >= 0) {
        if (k === 0) seq = [];
        seq[k] = pad;
        if (Number(len) > 0 && seq.length > Number(len)) seq.length = Math.floor(Number(len));
      }
      if (!armed() || !started) return;
      poolUp(pad, C.POOL_LIT, C.POOL_DECAY_MS);
    },

    /** Input: a press. Correct pays light by its link; wrong is a cold flash + a thud. */
    padPressed(i, correct, link) {
      if (!armed() || !started) return;
      const pad = Math.floor(Number(i));
      const l = Math.max(1, Math.min(12, Number(link) || 1));
      if (correct) {
        poolUp(pad, lerp(C.POOL_PAY, Math.min(8, l) / 8), C.POOL_DECAY_MS);
        if (l >= 2) flashHalo(1 + C.FLASH_LINK_STEP * Math.min(8, l), C.MQ_FLASH_MS * 0.6);
        punchStreak(Math.min(8, l) / 8);
        if (l >= 3) pulseRoom(0.1 + 0.04 * Math.min(8, l), 420);
      } else {
        poolUp(pad, 0.7, 380, 'cold');
        dipHalo();
      }
    },

    /** A cleared sequence: the sweep, the arpeggio, and the jackpot ladder. */
    sequenceDone(len) {
      if (!armed() || !started) return null;
      const l = Math.max(1, Math.floor(Number(len) || 0));
      bestLen = Math.max(bestLen, l);
      sweep(0.9, C.SWEEP_STEP_MS);
      pulseRoom(Math.min(1, l / 12), C.ROOM_PULSE_MS);
      flashHalo(1.2 + 0.05 * l, C.MQ_FLASH_MS);
      cue('streak', C.MILESTONE_LEVEL, { pitch: +(Math.pow(2, Math.min(7, Math.max(0, l - 3)) / 12)).toFixed(4) });
      /* THE JACKPOT LADDER */
      let kind = null;
      if (!royalOn && l >= royalLenFor(tier)) {
        kind = 'royal';
        royalOn = true;
        counts.royal = 1;
        stageClass('g-ec-royal', true);
        setCls(halo, 'gold', true);
        setCls(royal, 'on', true);
        showWord('ec_royal', 'The whole melody', 'gold');
        jackpot(C.ROYAL_I, 'royal@' + l);
        after(260, () => cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.5 }));
        after(520, () => cue('stamp', 0.45, { pitch: 1 }));
        remix(C.REMIX_ROYAL_STEP_MS, C.REMIX_LEVEL, true);
        cancel(royalTimer);
        royalTimer = after(C.ROYAL_MS, () => {
          royalTimer = 0;
          setCls(royal, 'on', false);
          if (!bellOn) setCls(halo, 'gold', false);
          paintHeat(true);
        });
        paintHeat(true);
      } else if (isMajorLen(l)) {
        kind = 'major';
        const q = clamp01(l / 14);
        jackpot(lerp(C.MAJOR_I, q), 'major@' + l);
        showWord('ec_jackpot', 'Perfect pitch', '');
        remix(C.REMIX_STEP_MS, C.REMIX_LEVEL, false);
      } else if (l >= C.MINOR_FROM_LEN && roll('jack-minor') < lerp(C.MINOR_CHANCE, heat)) {
        kind = 'minor';
        jackpot(C.MINOR_I, 'minor@' + l);
      }
      return { kind, len: l };
    },

    /** A fail at pad i where `expected` was due: a thud, a cold dip; next door = THE ALMOST. */
    fail(i, expected) {
      if (!armed() || !started) return null;
      const pad = Math.floor(Number(i));
      const exp = Math.floor(Number(expected));
      killRemix();
      dipHalo();
      if (Number.isFinite(pad) && pad >= 0) poolUp(pad, 0.7, 420, 'cold');
      cue('bump', C.THUD_LEVEL, { pitch: 0.7 });               // a muted thud, never silence
      let almost = false;
      if (Number.isFinite(pad) && Number.isFinite(exp) && isAdjacent(pad, exp, padCount)) {
        almost = true;
        almostSeen++;
        hum(exp);
        showWord('ec_near_miss', 'So nearly', 'lav');
        nearMiss(C.NEAR_MISS_I);
      }
      return { almost };
    },

    /** A decoy let pass: the glint laps the circle, a tiny tick. */
    decoyResisted() {
      if (!armed() || !started) return;
      counts.resisted++;
      if (glint) {
        restart(glint, 'on');
        cancel(glintTimer);
        glintTimer = after(950, () => { glintTimer = 0; setCls(glint, 'on', false); });
      }
      flashHalo(1.1, 260);
      cue('streak', C.RESIST_LEVEL, { pitch: 1.4 });
    },

    /** Encore: the room goes warm and slow; back when it ends. */
    encore(on) {
      encoreOn = !!on;
      stageClass('g-ec-encore', encoreOn);
      setCls(halo, 'amber', encoreOn && !bellOn && !royalOn);
      for (const p of pools) setCls(p, 'warm', encoreOn);
      paintHeat(true);
    },

    /** The bell (the last stretch): gold and fast. */
    bell(on) {
      bellOn = !!on;
      setCls(halo, 'gold', bellOn || royalOn);
      if (bellOn) setCls(halo, 'amber', false);
      paintHeat(true);
    },

    /** The bell took the room: the rig sighs out, a dim payout on a plain class. */
    dimOut() {
      outOn = true;
      const wasRoyal = royalOn;
      bellOn = false;
      encoreOn = false;
      killRemix();
      clearHum();
      for (const id of Array.from(sweepTimers)) cancel(id);
      sweepTimers.clear();
      setCls(halo, 'flash', false);
      setCls(halo, 'dip', false);
      setCls(halo, 'amber', false);
      setCls(halo, 'out', true);
      stageClass('g-ec-encore', false);
      stageClass('g-ec-out', true);
      if (armed()) {
        /* losses disguised: a dim payout, never silence. After a ROYAL the
           hall is already gold, so only the cue (a shade brighter) plays. */
        if (!wasRoyal) pulseRoom(0.1 + 0.25 * clamp01(bestLen / 12), C.END_DIM_MS);
        cue('wash', 0.25, { pitch: wasRoyal ? 1.15 : 0.9 });
      }
      paintHeat(true);
    },

    pause() { /* transient light simply ends on its own timers (the registry is pause-aware) */ },
    resume() { /* the chase is CSS; the stage's .suspended rule released it */ },

    /** The class is over; nothing may pulse again. */
    stop() {
      cancelAll();
      flashTimer = dipTimer = wordTimer = pulseTimer = punchTimer = royalTimer = glintTimer = humTimer = 0;
      sweepTimers.clear();
      killRemix();
      clearHum();
      poolsDown();
      setCls(halo, 'flash', false);
      setCls(halo, 'dip', false);
      setCls(pulse, 'on', false);
      setCls(word, 'on', false);
      setCls(glint, 'on', false);
      const chip = streakChip();
      if (chip) { setCls(chip, 'g-ec-cs-punch', false); setCls(chip, 'g-ec-cs-bloom', false); setVar(chip, '--ec-cs-ps', '1'); }
    },

    destroy() {
      if (destroyed) return;
      api.stop();
      destroyed = true;
      cancelAll();
      for (const n of [halo, cs, pulse, royal]) {
        try { if (n && typeof n.remove === 'function') n.remove(); else if (n && n.parentNode) n.parentNode.removeChild(n); } catch (e) { /* noop */ }
      }
      halo = cs = pulse = royal = glint = word = null;
      bulbs = []; pools.length = 0;
      for (const name of Array.from(stageClasses)) stageClass(name, false);
      if (opts.stage && opts.stage.style) {
        for (const k of props) { try { opts.stage.style.removeProperty(k); } catch (e) { /* ignore */ } }
      }
      props.clear();
    },

    diagnostics() {
      return {
        armed: armed(), started, heat: +heat.toFixed(3), rot: ID.rot, offArc: ID.offArc, bulbs: ID.bulbs, pads: padCount,
        bell: bellOn, encore: encoreOn, royal: royalOn, out: outOn, bestLen, seq: seq.slice(), almost: almostSeen,
        remixing, counts: Object.assign({}, counts), jackpots: jackLog.slice(),
        liveTimers: live.size, nodes: [halo, cs, pulse, royal].filter(Boolean).length,
      };
    },
  };
  return api;
}

export default createEcCasino;
