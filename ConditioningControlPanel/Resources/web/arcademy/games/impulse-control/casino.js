/* ============================================================================
 * games/impulse-control/casino.js - DECK II of the House Rules for the Drop
 * Tube: THE FLOOR. The tube (tube3d.js) is the seeded-identity flagship and
 * stays untouched; this file lights the ROOM around it and makes every verdict
 * pay out in sound and light. The trickster (trickster.js) lies about the
 * room; the pressure (pressure.js) is what the room DOES to you as your chain
 * climbs. This file is the lighting rig and the chime rack.
 *
 *   THE SOUND LADDER  every pop chimes, and the chime CLIMBS: +1 semitone per
 *                     link of the streak, capped +7 (the intake precedent), so
 *                     an eyes-busy player hears the chain rise. A drift is a
 *                     muted thud, never silence; a survived X is a soft exhale;
 *                     the X popped adds NOTHING here (render.js owns the real
 *                     denied.mp3 sting). Milestones (5/10/15/20) stack an
 *                     arpeggio on top. Every cue is engine audio_trigger, so
 *                     shell/audio.js stays the only audio owner (trap 18).
 *   THE MARQUEE       a ring of bulbs hugging the basin (the reveal surface is
 *                     round, so the frame is a HALO, not a box). Crawls lazily
 *                     at low heat, chases hungrily at high heat, flashes on a
 *                     payout, goes GOLD for the last tenth of the class and the
 *                     royal, sighs out at the end. Bulb colour temperature is
 *                     TONIGHT ONLY: seeded from the UTC date alone, so every
 *                     player on earth shares the hall tonight and it expires
 *                     at midnight (Deck V).
 *   PAYOUT LIGHT      a pop pays light scaled by its points: the basin floods,
 *                     the marquee flashes, the score odometer scale-punches
 *                     (transform only; the TEXT is index.js's and never moves).
 *   THE GATE          on a good reveal a thin arc closes around the basin over
 *                     the reveal window - the closing gate the player is racing.
 *                     It never touches the bubble node and tells nothing the
 *                     reveal did not already say (the kind is on the bubble).
 *   NEAR-MISS STAGING (a) JUST - a pop that lands in the last 12% of its
 *                     window: the gate flashes white at the point it nearly
 *                     shut, the near_miss ceremony rises; (b) the RECORD PING -
 *                     an RT within 8ms of the personal record without beating
 *                     it: the record line pings; (c) ALMOST - the pointer sat on
 *                     the X while it was live and the player STILL held: the
 *                     word, a lavender exhale, a rising tone. All three are
 *                     staging; the ledger already decided.
 *   THE JACKPOT LADDER the reward canon, scaled by an intensity dial, no named
 *                     tiers (jackpotSpec takes care of bursts/particles): a
 *                     PERFECT stamp may roll a minor jackpot (seeded, chance
 *                     rises with heat); streak milestones pay a major one
 *                     deterministically; the ROYAL is once-a-class and only
 *                     when the class was perfect, the gate held and the chain
 *                     reached 10 - then the hall floods gold. A failed class
 *                     still gets a dim payout: silence is where people stand up.
 *   KEN-BURNS         the two backdrop <img> drift (scale 1.00 -> 1.06, slow
 *                     pan), period seeded per class and quickening with heat.
 *   ROOM IDENTITY     per CLASS seed (Deck I): bulb count/size, payout hue on
 *                     the violet -> rose arc (~6% off-arc teal), ken-burns
 *                     period and direction. Same seed, same room; a retake
 *                     replays the identical show.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects index.js hands it AFTER its own
 *       accounting; writes nothing about score, streak, tally or grade; the
 *       score chip is moved by transform only, its text is never touched.
 *   II  input honest  - the bubble is the ONE tap target; every node here is
 *       pointer-events:none, lives INSIDE render's stage, and nothing is ever
 *       mounted over, inside or around the bubble's own node. The hover signal
 *       is read, never acted on.
 *   III never still   - the marquee crawls at heat 0, the bulbs breathe, the
 *       backdrop drifts.
 *   IV  images > text - the three words this deck can show (JUST / ALMOST /
 *       ROYAL, plus "record") come through the lexicon and live for <600ms.
 *   V   seeded        - per-tag mulberry32 off seed+'|ic-casino|<tag>' (append-
 *       only tags; a new tag never shifts an old stream). No Math.random.
 *       Tonight-only cosmetics come off the UTC DATE alone, by design.
 *   VI  exits sacred  - capsOk false disarms every light; reduced motion keeps
 *       a static dim halo (no chase, no punch, no ken-burns; bloom only);
 *       the stage's .suspended rule freezes the chase; timers ride the game's
 *       pause-aware registry AND a local set so destroy() cannot leak one.
 *   VII lexicon       - ic_just / ic_almost / ic_royal / ic_record_ping only,
 *       through opts.t; nothing else is rendered as text.
 *
 * ENGINE vs GAME-LOCAL: cues, the jackpot / near_miss ceremonies and nothing
 * else go through the engine (opts.engine = index.js's null-safe wrapper with
 * the audio ceiling and clickSafe welded on). The halo, the flood, the gate,
 * the words and the odometer punch are game-local: they are positioned on the
 * game's own geometry (the basin), which no engine primitive knows.
 * NODE BUDGET: halo (1 + bulbs), flood, gate, word, that is all; all reused.
 * ==========================================================================*/

import { makeRng, hash01 } from '../../core/rng.js';

export const IC_CASINO = Object.freeze({
  /* ---- the marquee --------------------------------------------------- */
  BULBS: Object.freeze([24, 28, 32]),          // seeded pick
  MQ_T_SLOW: 3.2,                              // chase period (s) at heat 0
  MQ_T_FAST: 0.8,                              // at heat 1
  MQ_A_LO: 0.22,                               // presence (opacity) band
  MQ_A_HI: 0.85,
  MQ_T_GOLD: 0.5,
  MQ_A_GOLD: 0.95,
  MQ_FLASH_MS: 520,
  /** tonight only: three bulb temperatures, UTC date picks one. */
  TONIGHT_HUES: Object.freeze([38, 330, 268]), // amber / pink / lavender
  /* ---- the payout ---------------------------------------------------- */
  FLOOD_MS: 620,
  FLOOD_A: Object.freeze([0.18, 0.55]),        // by pts/100
  PUNCH_SCALE: Object.freeze([0.04, 0.26]),    // odometer scale by pts/100
  PUNCH_MS: 220,
  MILESTONES: Object.freeze([5, 10, 15, 20]),
  /* ---- the gate + the near misses ------------------------------------ */
  JUST_SHARE: 0.88,                            // pop at >= 88% of the window = JUST
  RECORD_PING_MS: 8,                           // within 8ms of the record, not under it
  WORD_MS: 560,
  ALMOST_HOVER_MS: 140,                        // the pointer must SIT on the X this long
  NEAR_MISS_I: Object.freeze({ just: 0.55, record: 0.4, almost: 0.7 }),
  /* ---- the jackpot ladder -------------------------------------------- */
  MINOR_CHANCE: Object.freeze([0.22, 0.55]),   // perfect stamp, by heat
  MINOR_I: 0.35,
  MAJOR_I: Object.freeze([0.5, 0.65, 0.8, 0.95]),  // by milestone index
  ROYAL_MIN_STREAK: 10,
  ROYAL_I: 1,
  ROYAL_MS: 3200,
  END_DIM_MS: 1400,
  /* ---- the sound ladder ---------------------------------------------- */
  SEMITONE_CAP: 7,
  POP_LEVEL: Object.freeze([0.3, 0.5]),        // by min(streak,10)/10
  DRIFT_LEVEL: 0.11,   /* owner 2026-08-24: error cues -50% */
  PASS_LEVEL: 0.3,
  MILESTONE_LEVEL: 0.45,
  JACKPOT_LEVEL: 0.55,
  NEAR_LEVEL: 0.3,
  /* ---- ken-burns ----------------------------------------------------- */
  KB_T: Object.freeze([30, 16]),               // seconds: heat 0 -> heat 1
  KB_SEED_SPAN: 8,                             // +0..8s seeded
  KB_SCALE: 1.06,
  /* ---- identity ------------------------------------------------------ */
  OFF_ARC: 0.06,
  HUE_ARC: Object.freeze([268, 342]),          // violet -> rose
  HUE_OFF: 184,                                // the teal night
  HEAT_REPAINT_STEP: 0.03,
});

const STYLE_ID = 'g-ic-casino-style';
const STYLE_TEXT = `
.g-ic-cs-halo{position:absolute;inset:0;pointer-events:none;--ic-mq-t:3.2s;--ic-mq-a:.22;--ic-mq-hue:330;
  opacity:var(--ic-mq-a);transition:opacity .9s ease}
.g-ic-cs-bulb{position:absolute;width:var(--ic-mq-d,7px);height:var(--ic-mq-d,7px);margin:calc(var(--ic-mq-d,7px) * -.5);
  border-radius:50%;background:hsl(var(--ic-mq-hue) 95% 72%);
  box-shadow:0 0 6px hsl(var(--ic-mq-hue) 95% 70% / .9),0 0 14px hsl(var(--ic-mq-hue) 90% 60% / .5);
  animation:g-ic-cs-chase var(--ic-mq-t) linear infinite;animation-delay:calc(var(--i) / var(--n) * var(--ic-mq-t) * -1)}
@keyframes g-ic-cs-chase{0%{opacity:.18;transform:scale(.8)}12%{opacity:1;transform:scale(1.15)}40%{opacity:.35;transform:scale(.92)}100%{opacity:.18;transform:scale(.8)}}
.g-ic-cs-halo.gold{--ic-mq-hue:46 !important}
.g-ic-cs-halo.flash .g-ic-cs-bulb{animation-duration:.22s;filter:brightness(1.6)}
.g-ic-cs-flood{position:absolute;inset:0;pointer-events:none;opacity:0;
  background:radial-gradient(circle at var(--ic-basin-x,50%) var(--ic-basin-y,50%), hsl(var(--ic-cs-hue,330) 90% 70% / .9) 0%, hsl(var(--ic-cs-hue,330) 90% 60% / .35) 22%, transparent 58%);
  transition:opacity .12s ease-out}
.g-ic-cs-flood.on{opacity:var(--ic-cs-pay,.3);transition:opacity .06s ease-in}
.g-ic-cs-flood.royal{background:radial-gradient(circle at var(--ic-basin-x,50%) var(--ic-basin-y,50%), hsl(46 100% 72% / .95) 0%, hsl(46 100% 60% / .45) 30%, hsl(46 100% 50% / .12) 70%, transparent 100%);
  opacity:.9;transition:opacity .35s ease-out}
.g-ic-cs-gate{position:absolute;inset:0;pointer-events:none;opacity:0;
  background:conic-gradient(from -90deg, hsl(var(--ic-cs-hue,330) 90% 78% / .85) 0deg, hsl(var(--ic-cs-hue,330) 90% 78% / .85) calc(var(--ic-gate,1) * 360deg), transparent calc(var(--ic-gate,1) * 360deg));
  -webkit-mask:radial-gradient(circle, transparent calc(var(--ic-gate-r,46%) - 3px), #000 calc(var(--ic-gate-r,46%) - 2px), #000 var(--ic-gate-r,46%), transparent calc(var(--ic-gate-r,46%) + 1px));
  mask:radial-gradient(circle, transparent calc(var(--ic-gate-r,46%) - 3px), #000 calc(var(--ic-gate-r,46%) - 2px), #000 var(--ic-gate-r,46%), transparent calc(var(--ic-gate-r,46%) + 1px));
  transition:opacity .18s ease}
.g-ic-cs-gate.on{opacity:.7}
.g-ic-cs-gate.just{opacity:1;filter:brightness(2) saturate(.2);transition:none}
.g-ic-cs-word{position:absolute;left:var(--ic-basin-x,50%);top:var(--ic-basin-y,50%);transform:translate(-50%,-50%) translateY(calc(var(--ic-basin-d,40vmin) * .62));
  pointer-events:none;font:800 clamp(14px,2.2vmin,22px)/1 var(--font-head,system-ui,sans-serif);letter-spacing:.22em;
  color:hsl(var(--ic-cs-hue,330) 90% 82%);text-shadow:0 0 12px hsl(var(--ic-cs-hue,330) 90% 70% / .8);opacity:0}
.g-ic-cs-word.on{animation:g-ic-cs-word .56s ease-out forwards}
.g-ic-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-ic-cs-word.gold{color:#ffe08a;text-shadow:0 0 14px rgba(240,194,75,.9)}
@keyframes g-ic-cs-word{0%{opacity:0;transform:translate(-50%,-50%) translateY(calc(var(--ic-basin-d,40vmin) * .62)) scale(.7)}
  18%{opacity:1;transform:translate(-50%,-50%) translateY(calc(var(--ic-basin-d,40vmin) * .62)) scale(1.08)}
  70%{opacity:1;transform:translate(-50%,-50%) translateY(calc(var(--ic-basin-d,40vmin) * .6)) scale(1)}
  100%{opacity:0;transform:translate(-50%,-50%) translateY(calc(var(--ic-basin-d,40vmin) * .5)) scale(1)}}
.g-ic-cs-punch{transition:transform .22s cubic-bezier(.2,1.6,.4,1) !important;transform:scale(var(--ic-cs-ps,1))}
.g-ic-cs-bloom{box-shadow:0 0 0 2px hsl(var(--ic-cs-hue,330) 90% 70% / .55),0 0 22px hsl(var(--ic-cs-hue,330) 90% 65% / .6) !important;transition:box-shadow .3s ease}
.g-ic-cs-kb{animation:g-ic-cs-kb var(--ic-kb-t,30s) ease-in-out infinite alternate;transform-origin:var(--ic-kb-o,50% 50%)}
@keyframes g-ic-cs-kb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.06) translate(var(--ic-kb-x,1.5%),var(--ic-kb-y,-1%))}}
@media (prefers-reduced-motion: reduce){
  .g-ic-cs-bulb{animation:none !important;opacity:.55}
  .g-ic-cs-kb{animation:none !important}
  .g-ic-cs-word.on{animation:none;opacity:1}
}
.g-ic.reduced .g-ic-cs-bulb{animation:none !important;opacity:.55}
.g-ic.reduced .g-ic-cs-kb{animation:none !important}
.g-ic.suspended .g-ic-cs-bulb,.g-ic.suspended .g-ic-cs-kb,.g-ic.suspended .g-ic-cs-word{animation-play-state:paused !important}
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
  const s = Math.max(0, Math.min(IC_CASINO.SEMITONE_CAP, (Number(streak) || 0) - 1));
  return +Math.pow(2, s / 12).toFixed(4);
}
/** Tonight's bulb hue from the UTC date string alone (shared by everyone). Pure. */
export function tonightHue(utcDate) {
  const h = hash01('ic-tonight|' + String(utcDate || ''));
  const list = IC_CASINO.TONIGHT_HUES;
  return list[Math.min(list.length - 1, Math.floor(h * list.length))];
}
/** Did a pop land in the JUST band of its window? Pure. */
export function isJust(rt, windowMs) {
  const w = Number(windowMs) || 0;
  if (w <= 0) return false;
  return (Number(rt) || 0) >= w * IC_CASINO.JUST_SHARE;
}
/** Within the record-ping band (slower than the record, by at most N ms)? Pure. */
export function isRecordPing(rt, recordRt) {
  const r = Number(recordRt) || 0;
  if (r <= 0) return false;
  const d = (Number(rt) || 0) - r;
  return d > 0 && d <= IC_CASINO.RECORD_PING_MS;
}
/** The royal verdict. Pure. */
export function isRoyal(end) {
  const e = end || {};
  return !!e.perfect && !!e.sGateOk && (Number(e.bestStreak) || 0) >= IC_CASINO.ROYAL_MIN_STREAK;
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

/**
 * @param {Object} o  see IC-HOUSE-RULES §3:
 *   seed, gradeTier, reduced, motionLevel, utcDate, nodes (render.nodes),
 *   engine {fire,sustain,stop,ceremony,channels,rewardRoll,audio}, timers {after,every,clear},
 *   capsOk () => bool, t (k, fallback) => string, log
 */
export function createIcCasino(o) {
  const opts = o || {};
  const C = IC_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const reduced = !!opts.reduced;
  const motion = Math.max(0, Math.min(2, Math.round(opts.motionLevel == null ? 2 : Number(opts.motionLevel) || 0)));
  const still = reduced || motion <= 0;
  const nodes = opts.nodes || {};
  const eng = opts.engine || {};
  const armedBase = !!nodes.stage && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
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
  const seedBase = String(opts.seed || 'ic') + '|ic-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, floods: 0, words: 0 };
  /* THE DECOUPLE (W2 sec 3, owner ruling: a visual dial must not mute the room).
   * Every LIGHT below stays behind armed(); the cue road gates on everything
   * EXCEPT capsOk, because bgIntensity 0 is the player's visual exit (Law VI),
   * not a mute. */
  const sounds = () => armedBase && !destroyed;
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

  /* ---- identity (per class seed) ------------------------------------------ */
  const ID = (() => {
    const offArc = roll('hue') < C.OFF_ARC;
    const hue = offArc ? C.HUE_OFF : Math.round(lerp(C.HUE_ARC, roll('hue')));
    const bulbs = C.BULBS[Math.floor(roll('bulbs') * C.BULBS.length)];
    const bulbD = 6 + Math.round(roll('bulbs') * 3);            // 6..9 px
    const kbExtra = roll('kb') * C.KB_SEED_SPAN;
    const kbX = (roll('kb') < 0.5 ? -1 : 1) * (1 + roll('kb') * 1.5);  // 1..2.5%
    const kbY = (roll('kb') < 0.5 ? -1 : 1) * (0.5 + roll('kb'));      // .5..1.5%
    const kbO = (40 + Math.round(roll('kb') * 20)) + '% ' + (40 + Math.round(roll('kb') * 20)) + '%';
    return { offArc, hue, bulbs, bulbD, kbExtra, kbX, kbY, kbO };
  })();
  const mqHue = tonightHue(opts.utcDate);

  /* ---- nodes ------------------------------------------------------------- */
  let halo = null, flood = null, gate = null, word = null;
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let goldOn = false;
  let royalOn = false;
  let endedOn = false;
  let lastReveal = null;        // { kind, windowMs, idx, at }
  let hoverOn = false;
  let hoverSince = 0;
  let tempted = false;           // the pointer sat on a live X
  let gateTimer = 0;
  let wordTimer = 0;
  let floodTimer = 0;
  let flashTimer = 0;
  let punchTimer = 0;
  let royalTimer = 0;
  let bestStreakSeen = 0;
  /** W3 P0-4: the record topper is once a class, however many times the number
   *  moves. A deck instance is built per class, so this latch is the class. */
  let recordCued = false;
  const jackLog = [];

  function nowMs() {
    try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
    return Date.now();
  }

  function mount() {
    if (halo || !armedBase || !capsOk()) return;   // bgIntensity 0: the floor stays dark
    ensureStyle();
    const stage = nodes.stage;
    setVar(stage, '--ic-cs-hue', ID.hue);
    /* the halo: in STAGE's marquee slot when it exists, else on the stage
       (centred on the basin by the slot's own CSS) */
    const host = nodes.marqueeSlot || stage;
    halo = el('div', 'g-ic-cs-halo');
    if (halo) {
      setVar(halo, '--ic-mq-hue', mqHue);
      setVar(halo, '--ic-mq-d', ID.bulbD + 'px');
      setVar(halo, '--n', ID.bulbs);
      for (let i = 0; i < ID.bulbs; i++) {
        const b = el('span', 'g-ic-cs-bulb');
        if (!b) break;
        const a = (i / ID.bulbs) * Math.PI * 2 - Math.PI / 2;
        b.style.left = (50 + 50 * Math.cos(a)).toFixed(2) + '%';
        b.style.top = (50 + 50 * Math.sin(a)).toFixed(2) + '%';
        setVar(b, '--i', i);
        halo.appendChild(b);
      }
      try { host.appendChild(halo); } catch (e) { halo = null; }
    }
    flood = el('div', 'g-ic-cs-flood');
    gate = el('div', 'g-ic-cs-gate');
    word = el('div', 'g-ic-cs-word');
    try {
      /* the flood under everything of ours, the gate in the marquee slot (its
         geometry IS the basin's), the word on the stage */
      if (flood) {
        if (typeof stage.insertBefore === 'function' && stage.firstChild) stage.insertBefore(flood, stage.firstChild);
        else stage.appendChild(flood);
      }
    } catch (e) { /* partial mounts are fine */ }
    try { if (gate) (nodes.marqueeSlot || stage).appendChild(gate); } catch (e) { gate = null; }
    try { if (word) stage.appendChild(word); } catch (e) { word = null; }
    /* ken-burns on the two backdrop images */
    if (!still) {
      for (const img of [nodes.bgA, nodes.bgB]) {
        if (!img) continue;
        setCls(img, 'g-ic-cs-kb', true);
        setVar(img, '--ic-kb-x', ID.kbX + '%');
        setVar(img, '--ic-kb-y', ID.kbY + '%');
        setVar(img, '--ic-kb-o', ID.kbO);
      }
    }
    paintHeat(true);
  }

  function paintHeat(force) {
    if (!halo && !nodes.bgA) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    if (halo) {
      const gold = goldOn || royalOn;
      const period = gold ? C.MQ_T_GOLD : (C.MQ_T_SLOW + (C.MQ_T_FAST - C.MQ_T_SLOW) * heat);
      const alpha = endedOn ? 0 : (gold ? C.MQ_A_GOLD : lerp([C.MQ_A_LO, C.MQ_A_HI], heat));
      setVar(halo, '--ic-mq-t', period.toFixed(2) + 's');
      setVar(halo, '--ic-mq-a', alpha.toFixed(2));
    }
    if (!still) {
      const kb = (C.KB_T[0] + (C.KB_T[1] - C.KB_T[0]) * heat + ID.kbExtra).toFixed(1) + 's';
      setVar(nodes.bgA, '--ic-kb-t', kb);
      setVar(nodes.bgB, '--ic-kb-t', kb);
    }
  }

  /* ---- the light ---------------------------------------------------------- */
  function flashHalo(ms) {
    if (!halo || !armed() || still) return;
    setCls(halo, 'flash', true);
    cancel(flashTimer);
    flashTimer = after(ms || C.MQ_FLASH_MS, () => setCls(halo, 'flash', false));
  }
  function floodBasin(q, ms) {
    if (!flood || !armed()) { return; }
    counts.floods++;
    setVar(flood, '--ic-cs-pay', lerp(C.FLOOD_A, q).toFixed(2));
    setCls(flood, 'on', true);
    cancel(floodTimer);
    floodTimer = after(ms || C.FLOOD_MS, () => setCls(flood, 'on', false));
  }
  function punchScore(q) {
    const chip = nodes.score;
    if (!chip || !armed()) return;
    if (still) {
      /* reduced motion: a bloom, never a move */
      setCls(chip, 'g-ic-cs-bloom', true);
      cancel(punchTimer);
      punchTimer = after(320, () => setCls(chip, 'g-ic-cs-bloom', false));
      return;
    }
    setCls(chip, 'g-ic-cs-punch', true);
    setVar(chip, '--ic-cs-ps', (1 + lerp(C.PUNCH_SCALE, q)).toFixed(3));
    cancel(punchTimer);
    punchTimer = after(C.PUNCH_MS, () => { setVar(chip, '--ic-cs-ps', '1'); });
  }
  function showWord(key, fallback, tone) {
    if (!word || !armed()) return;
    counts.words++;
    word.textContent = t(key, fallback);
    word.className = 'g-ic-cs-word' + (tone ? ' ' + tone : '');
    void (word.offsetWidth);
    setCls(word, 'on', true);
    cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 40, () => { setCls(word, 'on', false); });
  }
  function openGate(windowMs) {
    /* THE DECOUPLE, the gate's half: the ARC is a light and keeps its armed()
       gate, but the LADDER underneath it is the reaction window's countdown -
       a game-called beat, which trap 69 says still sounds with the lights off.
       So one loop drives both and the var write is simply skipped when there is
       no gate to write to. */
    if (!sounds()) return;
    const lit = !!gate && armed();
    if (lit) {
      gate.className = 'g-ic-cs-gate';
      setVar(gate, '--ic-gate', '1');
      void (gate.offsetWidth);
      /* the arc closes over the window: a single CSS transition on the var is
         not animatable without @property, so drive it in 5 steps from the
         game's timers (cheap, pause-aware, and exact enough for an eye) */
      setCls(gate, 'on', true);
    }
    if (!lit && still) return;          // nothing to draw and nothing to sound
    const steps = still ? 1 : 6;
    const stepMs = windowMs / steps;
    let k = 0;
    cancel(gateTimer);
    const tick = () => {
      k++;
      if (lit && gate) setVar(gate, '--ic-gate', (1 - k / steps).toFixed(3));
      /* W3 P0-9: THE GATE LADDER. The reaction window is this class's only
         countdown, and it drained in silence. The cue rides the gate's OWN
         steps - it is not a ticker and there is nothing here per frame - and
         closeGate() cancels gateTimer, which is what kills it. Skipped under
         reduced motion, where the arc is a single step and a ladder would be a
         sound with no picture. The last rung lands ON windowEnd (k === steps
         fires at exactly windowMs). */
      if (!still) cue('clock_tick', 0.1, { pitch: 1 + 0.08 * k });
      if (k < steps) gateTimer = after(stepMs, tick);
    };
    gateTimer = after(stepMs, tick);
  }
  function closeGate(just) {
    /* the ladder is cancelled FIRST and unconditionally: with the lights off
       there is no gate node, and a countdown nobody can see still has to stop. */
    cancel(gateTimer);
    if (!gate) return;
    if (just && !still) {
      setCls(gate, 'just', true);
      after(180, () => { if (gate) { gate.className = 'g-ic-cs-gate'; } });
    } else {
      gate.className = 'g-ic-cs-gate';
    }
  }

  /* ---- the ladder --------------------------------------------------------- */
  function jackpot(intensity, why) {
    if (!armed()) return;
    counts.jackpots++;
    jackLog.push(why);
    ceremony('jackpot', { intensity });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashHalo(C.MQ_FLASH_MS + 300 * intensity);
    floodBasin(intensity, C.FLOOD_MS + 300);
  }
  function nearMiss(kind, intensity) {
    if (!armed()) return;
    counts.nearMisses++;
    ceremony('near_miss', { intensity });
    cue('near_miss', C.NEAR_LEVEL, { pitch: kind === 'almost' ? 0.8 : 1.1 });
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      mount();
      say('casino: floor lit (hue ' + ID.hue + (ID.offArc ? ' off-arc' : '') + ', ' + ID.bulbs + ' bulbs, tonight ' + mqHue + ')');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (started) paintHeat(false);
    },
    load(ev) {
      /* the kind is in ev (the trickster's Tell needs it). This deck MUST NOT
         render a tell: nothing here reads ev.kind before reveal(). */
      const e = ev || {};
      if (!goldOn && !royalOn && Number(e.progress) >= 0.9) {
        goldOn = true;
        setCls(halo, 'gold', true);
        paintHeat(true);
      }
    },
    slide() { /* the tube owns the slide; nothing to light */ },
    reveal(ev) {
      const e = ev || {};
      lastReveal = { kind: e.kind, windowMs: Number(e.windowMs) || 0, idx: e.idx, at: nowMs() };
      tempted = false;
      if (e.kind !== 'denied') openGate(lastReveal.windowMs);
      else closeGate(false);
    },
    hover(on) {
      hoverOn = !!on;
      if (hoverOn) hoverSince = nowMs();
      else if (lastReveal && lastReveal.kind === 'denied' && nowMs() - hoverSince >= C.ALMOST_HOVER_MS) tempted = true;
    },
    pop(ev) {
      const e = ev || {};
      const streak = Math.max(0, Number(e.streak) || 0);
      const q = clamp01((Number(e.pts) || 0) / 100);
      bestStreakSeen = Math.max(bestStreakSeen, streak);
      const just = isJust(e.rt, e.windowMs);
      closeGate(just);
      /* THE SOUND LADDER */
      cue('bubble_pop', lerp(C.POP_LEVEL, Math.min(10, streak) / 10), { pitch: pitchForStreak(streak) });
      /* PAYOUT LIGHT */
      floodBasin(q);
      punchScore(q);
      if (e.stampKind === 'perfect' || e.newRecord) flashHalo();
      /* NEAR-MISS STAGING */
      if (just) { showWord('ic_just', 'JUST', ''); nearMiss('just', C.NEAR_MISS_I.just); }
      else if (!e.newRecord && isRecordPing(e.rt, e.recordRt)) { showWord('ic_record_ping', 'record', 'lav'); nearMiss('record', C.NEAR_MISS_I.record); }
      /* THE JACKPOT LADDER */
      const mi = C.MILESTONES.indexOf(streak);
      /* W3 P0-4: A LIFETIME RECORD. It used to sound exactly like an ordinary
         pop, which is the one thing the rarest event in the class must not do.
         It lands 150ms BEHIND the pop so the ear gets the win and then the
         topper, it is latched to once a class, and the MILESTONE is checked
         first: a record that falls on a jackpot rung leaves that rung the
         frame and never stacks two payoffs on one beat. */
      if (e.newRecord && mi < 0 && !recordCued) {
        recordCued = true;
        after(150, () => cue('record', 0.45, { pitch: 1 }));
      }
      if (mi >= 0) {
        cue('streak', C.MILESTONE_LEVEL, { pitch: pitchForStreak(streak) });
        jackpot(C.MAJOR_I[mi], 'major@' + streak);
      } else if (e.stampKind === 'perfect' && roll('jack-minor') < lerp(C.MINOR_CHANCE, heat)) {
        jackpot(C.MINOR_I, 'minor@' + (e.idx == null ? '?' : e.idx));
      }
    },
    drift() {
      closeGate(false);
      cue('bump', C.DRIFT_LEVEL, { pitch: 0.7 });     // a muted thud, never silence
    },
    denyPass(ev) {
      const e = ev || {};
      cue('whisper', C.PASS_LEVEL, { pitch: 0.85 });   // the soft exhale
      floodBasin(0.15);
      if (tempted || (hoverOn && lastReveal && nowMs() - hoverSince >= C.ALMOST_HOVER_MS)) {
        showWord('ic_almost', 'ALMOST', 'lav');
        nearMiss('almost', C.NEAR_MISS_I.almost);
      }
      tempted = false;
      void e;
    },
    denyHit() {
      /* render.js owns the sting; here the floor just dims for a beat */
      closeGate(false);
      tempted = false;
      if (halo && !still) { setCls(halo, 'flash', false); }
    },
    end(ev) {
      const e = ev || {};
      endedOn = true;
      cancel(gateTimer);
      if (gate) gate.className = 'g-ic-cs-gate';
      const royal = isRoyal(Object.assign({ bestStreak: bestStreakSeen }, e));
      if (royal && armed()) {
        royalOn = true;
        counts.royal = 1;
        setCls(halo, 'gold', true);
        setCls(flood, 'royal', true);
        showWord('ic_royal', 'ROYAL', 'gold');
        ceremony('jackpot', { intensity: C.ROYAL_I });
        cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.2 });
        after(260, () => cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.5 }));
        after(520, () => cue('stamp', 0.5, { pitch: 1 }));
        flashHalo(C.ROYAL_MS);
        cancel(royalTimer);
        royalTimer = after(C.ROYAL_MS, () => { setCls(flood, 'royal', false); paintHeat(true); });
      } else {
        /* losses disguised: a dim payout, never silence */
        const q = clamp01((Number(e.score) || 0) / 1500);
        floodBasin(0.1 + 0.3 * q, C.END_DIM_MS);
        cue('wash', 0.25, { pitch: 0.9 });
        after(C.END_DIM_MS, () => paintHeat(true));
      }
      paintHeat(true);
      return { royal };
    },
    pause() {
      cancelAll();
      if (gate) gate.className = 'g-ic-cs-gate';
    },
    resume() { /* transient light simply ended; the chase is CSS and the stage's .suspended rule released it */ },
    destroy() {
      destroyed = true;
      cancelAll();
      for (const n of [halo, flood, gate, word]) { try { if (n && n.parentNode) n.parentNode.removeChild(n); } catch (e) { /* noop */ } }
      halo = flood = gate = word = null;
      for (const img of [nodes.bgA, nodes.bgB]) setCls(img, 'g-ic-cs-kb', false);
      if (nodes.score) { setCls(nodes.score, 'g-ic-cs-punch', false); setCls(nodes.score, 'g-ic-cs-bloom', false); }
    },
    diagnostics() {
      return {
        armed: armed(), started, heat: +heat.toFixed(3), hue: ID.hue, offArc: ID.offArc, bulbs: ID.bulbs,
        tonightHue: mqHue, gold: goldOn, royal: royalOn, ended: endedOn, bestStreak: bestStreakSeen,
        counts: Object.assign({}, counts), jackpots: jackLog.slice(),
        liveTimers: live.size, nodes: [halo, flood, gate, word].filter(Boolean).length,
      };
    },
  };
  return api;
}

export default createIcCasino;
