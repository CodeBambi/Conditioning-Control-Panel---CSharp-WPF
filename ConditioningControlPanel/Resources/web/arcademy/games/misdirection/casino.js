/* ============================================================================
 * games/misdirection/casino.js - DECK II of the House Rules for the shell
 * game: THE FLOOR. The con artist's table, lit. index.js shuffles the shells
 * and keeps the ledger; the trickster (trickster.js) lies about what you read;
 * the pressure (pressure.js) is what the room does to you as your pick streak
 * climbs. This file is the lighting rig and the chime rack around the table.
 *
 *   TABLE IDENTITY   seeded per CLASS seed (Deck I): a felt hue pair on the
 *                    violet -> rose arc (~6% of tables are the green baize
 *                    night - a bonus round for loading in), a rail metal
 *                    (brass / silver / rose-gold), a lamp sway period, a
 *                    ken-burns period, a bulb count, and a PATTERN JOURNEY of
 *                    2-3 stops through one weave morph space (diamond,
 *                    herringbone, damask, pinstripe, lace) walked by the
 *                    class's own heat. Same seed, same table; a retake sits
 *                    down at the identical felt.
 *   THE MARQUEE      a chase of bulbs on an ARCH over the shells (the table is
 *                    an arc, so the frame is an arch, not a box). Crawls lazily
 *                    at low heat, chases hungrily at high heat, flashes on a
 *                    payout, goes GOLD for the bell and the royal, sighs out
 *                    on a dim-out - never cuts.
 *   THE LAMP         a hanging cone of light over the table: it swings slowly
 *                    (Law III), slides to the revealed shell on a reveal, opens
 *                    wide for the shuffle, narrows for the pick.
 *   THE CHIME LADDER every correct pick chimes and the chime CLIMBS: +1
 *                    semitone per link of the pick streak, capped +7 (the
 *                    intake precedent). A wrong pick is a muted thud, never
 *                    silence. Every cue is engine audio_trigger (pitch = the
 *                    streak) through the weld index.js hands us; shell/audio.js
 *                    stays the only audio owner (trap 18).
 *   PAYOUT LIGHT     a correct pick floods the picked shell's slot and punches
 *                    the pot chip (transform only; the TEXT is index.js's). A
 *                    ride slams the pot badge and deepens the shell rims
 *                    (--md-n-ride on the stage, style.js glows it). A BANK is
 *                    the payout: gold flood, a coin shower into the pot chip,
 *                    a marquee flash, the streak arpeggio + the stamp thunk.
 *   THE SWAP TRAIL   each swap draws a felt streak between the two slots that
 *                    traded (one reused node); a glitch swap is a static pop on
 *                    the overlay instead. The trail never touches a shell.
 *   THE ALMOST       near-miss staging: on a wrong pick the TRUE target ghosts
 *                    through the picked shell and drifts home to the true slot
 *                    while the word ALMOST rises - the truth index.js already
 *                    revealed, staged once more. Cosmetic; the ledger decided.
 *   JACKPOT LADDER   minor (a seeded roll on a correct pick, chance rising with
 *                    heat) / major (streak milestones, or a bank three rides
 *                    deep) / ROYAL (a bank at the ride cap: the table floods
 *                    gold, the arch goes gold, a chime stack, the word ROYAL).
 *                    A bust is the table swallowing the pot: the felt dims, the
 *                    pot chip sinks, a low thud - dim, never silent.
 *   DIM-OUT          the bell took the table: the rig sighs out and pays a dim
 *                    payout light - silence is where people stand up.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - reads the event objects index.js hands it AFTER its own
 *       accounting; writes nothing about pot, streak, round or grade; the chips
 *       move by transform only, their text is never touched.
 *   II  input honest  - every node here is pointer-events:none and lives in
 *       the table's overlay layers under and over the arc; --slot / --x,
 *       data-slot, the shells' own nodes and transforms are NEVER written.
 *   III never still   - the marquee crawls at heat 0, the lamp swings, the
 *       weave drifts, the bulbs breathe.
 *   IV  images > text - three words (ONE OFF / SHARP / ROYAL) through the
 *       lexicon, each alive for < 700ms.
 *   V   seeded        - per-tag mulberry32 off seed+'|md-casino|<tag>' (append-
 *       only tags; a new tag never shifts an old stream). No Math.random.
 *   VI  exits sacred  - capsOk false disarms every light; reduced motion keeps
 *       a static dim arch + lamp (no chase, no coins, no ghost drift, no punch -
 *       a bloom instead); the stage's .suspended rule freezes the chase; every
 *       timer rides the game's pause-aware registry AND a local set so
 *       destroy() cannot leak one.
 *   VII lexicon       - md_almost / md_sharp / md_royal only,
 *       through opts.t; nothing else is rendered as text.
 *
 * ENGINE vs GAME-LOCAL: cues and the jackpot / near_miss ceremonies go through
 * the engine weld (opts.engine: fire/sustain/stop/channels, ceremony when the
 * weld carries it). The arch, the lamp, the flood, the trail, the ghost, the
 * coins and the words are game-local: they sit on the table's own geometry,
 * which no engine primitive knows. Node budget: arch (1 + bulbs), overlay
 * (lamp, flood, trail, static, word) + top overlay (ghost, <= 40 coins).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const MD_CASINO = Object.freeze({
  /* ---- identity ------------------------------------------------------ */
  OFF_ARC: 0.06,
  HUE_ARC: Object.freeze([262, 342]),         // violet -> rose
  HUE_OFF: Object.freeze([148, 168]),         // the green baize night
  METALS: Object.freeze(['brass', 'silver', 'rosegold']),
  WEAVES: Object.freeze(['diamond', 'herringbone', 'damask', 'pinstripe', 'lace']),
  STOPS_MIN: 2,
  STOPS_SPAN: 2,                               // 2..3 stops
  STOP_HYST: 0.05,
  BULBS: Object.freeze([18, 22, 26]),
  /* ---- the marquee --------------------------------------------------- */
  MQ_T_SLOW: 3.4,
  MQ_T_FAST: 0.9,
  MQ_A_LO: 0.62,
  MQ_A_HI: 0.98,
  MQ_T_BELL: 0.5,
  MQ_A_BELL: 0.95,
  MQ_SHUFFLE_MUL: 0.82,                        // the chase quickens while the shells move
  MQ_FLASH_MS: 560,
  /* ---- the lamp ------------------------------------------------------ */
  LAMP_SWAY_S: Object.freeze([9, 15]),         // seeded sway period
  LAMP_W_PICK: 0.34,                           // cone width as a fraction of the table
  LAMP_W_REVEAL: 0.26,
  LAMP_W_SHUFFLE: 0.9,
  /* ---- the payout ---------------------------------------------------- */
  FLOOD_MS: 620,
  FLOOD_A: Object.freeze([0.16, 0.5]),
  PUNCH_SCALE: Object.freeze([0.05, 0.28]),
  PUNCH_MS: 240,
  BANK_FLOOD_MS: 900,
  COINS_MAX: 40,
  COINS_BANK: Object.freeze([6, 18]),
  COINS_ROYAL: 30,
  COIN_MS: Object.freeze([700, 1200]),
  /* ---- the ladder ---------------------------------------------------- */
  MILESTONES: Object.freeze([5, 10, 15]),
  MINOR_CHANCE: Object.freeze([0.16, 0.48]),   // correct pick, by heat
  MINOR_I: 0.35,
  MAJOR_I: Object.freeze([0.55, 0.75, 0.95]),  // by milestone index
  MAJOR_RIDE: 3,
  MAJOR_RIDE_I: 0.7,
  ROYAL_RIDE: 5,
  ROYAL_I: 1,
  ROYAL_MS: 3200,
  BUST_MS: 1100,
  END_DIM_MS: 1400,
  /* ---- near-miss staging --------------------------------------------- */
  ALMOST_MS: 1200,
  SHARP_MS: 800,                               // a pick faster than this is SHARP
  WORD_MS: 600,
  NEAR_MISS_I: Object.freeze({ almost: 0.7, sharp: 0.4 }),
  /* ---- the sound ladder ---------------------------------------------- */
  SEMITONE_CAP: 7,
  CHIME_LEVEL: Object.freeze([0.3, 0.5]),      // by min(streak,10)/10
  THUD_LEVEL: 0.22,
  SWAP_LEVEL: Object.freeze([0.08, 0.2]),      // by heat
  SWAP_GAP_MS: 170,                            // at most one swap cue this often
  GLITCH_LEVEL: 0.18,
  REVEAL_LEVEL: 0.22,
  SHUFFLE_LEVEL: 0.16,
  RIDE_LEVEL: 0.3,
  BANK_LEVEL: 0.45,
  JACKPOT_LEVEL: 0.55,
  NEAR_LEVEL: 0.3,
  BUST_LEVEL: 0.28,
  DIM_LEVEL: 0.22,
  /* ---- ken-burns ----------------------------------------------------- */
  KB_T: Object.freeze([30, 16]),
  KB_SEED_SPAN: 8,
  HEAT_REPAINT_STEP: 0.03,
});

const STYLE_ID = 'g-md-casino-style';
/* The stylesheet for the nodes this deck owns. Everything pointer-events:none.
   Geometry vars: --md-cs-lx (lamp centre, % of table), --md-cs-lw (cone width,
   fraction), --md-cs-fx (flood centre), --md-mq-t / --md-mq-a / --md-mq-hue
   (the arch), --md-cs-pay (flood presence). */
const STYLE_TEXT = `
.g-md-mq{position:absolute;inset:0;pointer-events:none;z-index:3;
  --md-mq-t:3.4s;--md-mq-a:.62;--md-mq-hue:300;opacity:var(--md-mq-a);transition:opacity .9s ease}
.g-md-mq-bulb{position:absolute;width:var(--md-mq-d,8px);height:var(--md-mq-d,8px);margin:calc(var(--md-mq-d,8px) * -.5);
  border-radius:50%;
  background:radial-gradient(circle at 40% 35%, #fff8e8 0 28%, hsl(var(--md-mq-hue) 95% 78%) 62%, hsl(var(--md-mq-hue) 80% 52%) 100%);
  box-shadow:0 0 0 1px rgba(0,0,0,.35),0 0 7px hsl(var(--md-mq-hue) 95% 76% / .95),0 0 18px hsl(var(--md-mq-hue) 90% 64% / .6),0 0 34px hsl(var(--md-mq-hue) 90% 60% / .25);
  animation:g-md-cs-chase var(--md-mq-t) linear infinite;animation-delay:calc(var(--i) / var(--n) * var(--md-mq-t) * -1)}
@keyframes g-md-cs-chase{0%{opacity:.42;transform:scale(.86)}12%{opacity:1;transform:scale(1.22)}40%{opacity:.6;transform:scale(.95)}100%{opacity:.42;transform:scale(.86)}}
.g-md-mq.gold{--md-mq-hue:46 !important}
.g-md-mq.flash .g-md-mq-bulb{animation-duration:.22s;filter:brightness(1.6)}
.g-md-mq.out{opacity:0 !important;transition:opacity 1.6s ease}
/* the under-overlay: lamp, flood, trail, static - below the arc */
.g-md-cs{position:absolute;inset:0;pointer-events:none;z-index:0;overflow:hidden;
  --md-cs-lx:50;--md-cs-lw:.34;--md-cs-la:.5;--md-cs-fx:50;--md-cs-pay:.3}
.g-md-cs *{pointer-events:none}
.g-md-cs-lamp{position:absolute;left:calc(var(--md-cs-lx) * 1%);top:-40%;width:calc(var(--md-cs-lw) * 100%);height:180%;
  transform:translateX(-50%);opacity:var(--md-cs-la);
  background:radial-gradient(50% 45% at 50% 45%, hsl(var(--md-n-hue-a,300) 70% 82% / .34) 0%, hsl(var(--md-n-hue-a,300) 60% 70% / .12) 45%, transparent 72%);
  transition:left .55s cubic-bezier(.2,.9,.3,1), width .6s ease, opacity .5s ease;
  animation:g-md-cs-sway var(--md-n-sway,12s) ease-in-out infinite alternate}
@keyframes g-md-cs-sway{from{margin-left:-1.2%}to{margin-left:1.2%}}
.g-md-cs-flood{position:absolute;inset:0;opacity:0;
  background:radial-gradient(circle at calc(var(--md-cs-fx) * 1%) 60%, hsl(var(--md-n-hue-b,330) 90% 72% / .9) 0%, hsl(var(--md-n-hue-b,330) 90% 62% / .35) 18%, transparent 50%);
  transition:opacity .14s ease-out}
.g-md-cs-flood.on{opacity:var(--md-cs-pay);transition:opacity .06s ease-in}
.g-md-cs-flood.gold{background:radial-gradient(circle at calc(var(--md-cs-fx) * 1%) 60%, hsl(46 100% 72% / .95) 0%, hsl(46 100% 60% / .45) 24%, hsl(46 100% 50% / .1) 60%, transparent 100%)}
.g-md-cs-flood.royal{opacity:.9;transition:opacity .35s ease-out;
  background:radial-gradient(circle at 50% 55%, hsl(46 100% 74% / .95) 0%, hsl(46 100% 60% / .5) 30%, hsl(46 100% 50% / .15) 70%, transparent 100%)}
.g-md-cs-flood.dim{opacity:var(--md-cs-pay);transition:opacity .9s ease}
.g-md-cs-trail{position:absolute;left:0;top:0;height:3px;width:0;opacity:0;transform-origin:0 50%;border-radius:2px;
  background:linear-gradient(90deg, transparent, hsl(var(--md-n-hue-b,330) 90% 80% / .95), transparent);
  box-shadow:0 0 10px hsl(var(--md-n-hue-b,330) 90% 70% / .7)}
.g-md-cs-trail.on{animation:g-md-cs-trail .42s ease-out 1}
@keyframes g-md-cs-trail{0%{opacity:0;transform:rotate(var(--md-cs-ta,0deg)) scaleX(.2)}25%{opacity:.95;transform:rotate(var(--md-cs-ta,0deg)) scaleX(1)}100%{opacity:0;transform:rotate(var(--md-cs-ta,0deg)) scaleX(1.1)}}
.g-md-cs-static{position:absolute;inset:0;opacity:0;mix-blend-mode:screen;
  background:repeating-linear-gradient(0deg, rgba(255,255,255,.14) 0 1px, transparent 1px 3px)}
.g-md-cs-static.on{animation:g-md-cs-static .16s steps(3) 1}
@keyframes g-md-cs-static{0%{opacity:.7;transform:translateY(0)}50%{opacity:.4;transform:translateY(-2px)}100%{opacity:0;transform:translateY(1px)}}
.g-md-cs-word{position:absolute;left:50%;top:12%;transform:translate(-50%,0);pointer-events:none;
  font:800 clamp(14px,2.4vmin,24px)/1 var(--disp,system-ui,sans-serif);letter-spacing:.24em;text-transform:uppercase;
  color:hsl(var(--md-n-hue-b,330) 90% 84%);text-shadow:0 0 12px hsl(var(--md-n-hue-b,330) 90% 70% / .8);opacity:0}
.g-md-cs-word.on{animation:g-md-cs-word .62s ease-out forwards}
.g-md-cs-word.lav{color:#d8ccff;text-shadow:0 0 12px rgba(184,166,232,.8)}
.g-md-cs-word.gold{color:#ffe08a;text-shadow:0 0 16px rgba(240,194,75,.95)}
@keyframes g-md-cs-word{0%{opacity:0;transform:translate(-50%,10px) scale(.7)}18%{opacity:1;transform:translate(-50%,0) scale(1.08)}70%{opacity:1;transform:translate(-50%,-4px) scale(1)}100%{opacity:0;transform:translate(-50%,-16px) scale(1)}}
/* the over-overlay: the ghost and the coins - above the arc, still no pointer */
.g-md-cs-top{position:absolute;inset:0;pointer-events:none;z-index:5;overflow:visible}
.g-md-cs-top *{pointer-events:none}
.g-md-cs-ghost{position:absolute;left:0;top:0;width:var(--w,60px);height:var(--h,60px);border-radius:18%;opacity:0;
  transform:translate(var(--x0,0px),var(--y0,0px));
  background:hsl(var(--md-n-hue-b,330) 80% 72% / .35) center/cover no-repeat;
  box-shadow:0 0 18px hsl(var(--md-n-hue-b,330) 90% 70% / .7), inset 0 0 0 2px hsl(var(--md-n-hue-b,330) 90% 84% / .8);
  filter:saturate(1.2)}
.g-md-cs-ghost.on{animation:g-md-cs-ghost 1.2s cubic-bezier(.3,.8,.3,1) 1}
@keyframes g-md-cs-ghost{0%{opacity:0;transform:translate(var(--x0,0px),var(--y0,0px)) scale(.8)}
  20%{opacity:.85;transform:translate(var(--x0,0px),calc(var(--y0,0px) - 10px)) scale(1.05)}
  75%{opacity:.7;transform:translate(var(--x1,0px),calc(var(--y1,0px) - 10px)) scale(1)}
  100%{opacity:0;transform:translate(var(--x1,0px),var(--y1,0px)) scale(.9)}}
.g-md-cs-coin{position:absolute;left:var(--x,50%);top:var(--y,50%);width:var(--s,10px);height:var(--s,10px);border-radius:50%;opacity:0;
  background:radial-gradient(circle at 35% 30%, #fff3c4, #f0c24b 45%, #a8771a 100%);
  box-shadow:0 0 8px rgba(240,194,75,.8);
  animation:g-md-cs-coin var(--d,.9s) cubic-bezier(.2,.7,.4,1) var(--dl,0s) 1 forwards}
@keyframes g-md-cs-coin{0%{opacity:0;transform:translate(-50%,-50%) scale(.4)}
  15%{opacity:1;transform:translate(calc(-50% + var(--dx,0px) * .3),calc(-50% - 30px)) scale(1)}
  100%{opacity:0;transform:translate(calc(-50% + var(--dx,0px)),calc(-50% + var(--dy,-80px))) scale(.6) rotate(var(--r,180deg))}}
.g-md-cs-punch{transition:transform .24s cubic-bezier(.2,1.6,.4,1) !important;transform:scale(var(--md-cs-ps,1)) translateY(var(--md-cs-py,0px))}
.g-md-cs-bloom{box-shadow:0 0 0 2px hsl(var(--md-n-hue-b,330) 90% 70% / .55),0 0 22px hsl(var(--md-n-hue-b,330) 90% 65% / .6) !important;transition:box-shadow .3s ease}
.g-md-cs-bloom.gold{box-shadow:0 0 0 2px rgba(240,194,75,.7),0 0 24px rgba(240,194,75,.7) !important}
.g-md-cs-sink{transition:transform .5s ease-in, opacity .5s ease-in !important;transform:translateY(10px) scale(.94);opacity:.45}
.g-md-cs-kb{animation:g-md-cs-kb var(--md-kb-t,30s) ease-in-out infinite alternate;transform-origin:var(--md-kb-o,50% 50%)}
@keyframes g-md-cs-kb{from{transform:scale(1) translate(0,0)}to{transform:scale(1.06) translate(var(--md-kb-x,1.5%),var(--md-kb-y,-1%))}}
@media (prefers-reduced-motion: reduce){
  .g-md-mq-bulb{animation:none !important;opacity:.55}
  .g-md-cs-lamp{animation:none !important}
  .g-md-cs-kb{animation:none !important}
  .g-md-cs-word.on{animation:none;opacity:1}
  .g-md-cs-ghost.on{animation:none;opacity:.7}
  .g-md-cs-coin{animation:none;opacity:0}
}
html.arc-reduced .g-md-mq-bulb{animation:none !important;opacity:.55}
html.arc-reduced .g-md-cs-lamp{animation:none !important}
html.arc-reduced .g-md-cs-kb{animation:none !important}
html.arc-reduced .g-md-cs-ghost.on{animation:none;opacity:.7}
.g-md-stage.suspended .g-md-mq-bulb,.g-md-stage.suspended .g-md-cs-lamp,.g-md-stage.suspended .g-md-cs-word,
.g-md-stage.suspended .g-md-cs-ghost,.g-md-stage.suspended .g-md-cs-coin,.g-md-stage.suspended .g-md-cs-kb{animation-play-state:paused !important}
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

/** The chime's pitch for a pick streak: +1 semitone per link, capped. Pure. */
export function pitchForStreak(streak) {
  const s = Math.max(0, Math.min(MD_CASINO.SEMITONE_CAP, (Number(streak) || 0) - 1));
  return +Math.pow(2, s / 12).toFixed(4);
}
/** A pick under the SHARP line (ms)? Pure. */
export function isSharp(latencyMs) {
  const l = Number(latencyMs);
  return Number.isFinite(l) && l > 0 && l <= MD_CASINO.SHARP_MS;
}
/** The jackpot rung a BANK earns from its ride depth: 'royal' | 'major' | null. Pure. */
export function bankRung(rideDepth) {
  const d = Math.max(0, Math.floor(Number(rideDepth) || 0));
  if (d >= MD_CASINO.ROYAL_RIDE) return 'royal';
  if (d >= MD_CASINO.MAJOR_RIDE) return 'major';
  return null;
}
/** A pot's light, 0..1, log-ish (1 -> 0, 256+ -> 1). Pure. */
export function potLight(pot) {
  const p = Math.max(1, Number(pot) || 1);
  return clamp01(Math.log2(p) / 8);
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
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
function restart(node, cls) {
  if (!node || !node.classList) return;
  try {
    node.classList.remove(cls);
    if (typeof node.offsetWidth === 'number') void node.offsetWidth;
    node.classList.add(cls);
  } catch (e) { /* noop */ }
}

/**
 * @param {Object} o
 *   seed, tier, stage (.g-md-stage), table (.g-md-table - the mount + geometry host; the
 *   contract's `board` is accepted as the fallback), hud (the .g-md-hud element) + chips
 *   {round,clock,pot,streak} (the contract's `hud` object shape is accepted too),
 *   backdrop (.g-md-backdrop), timers {after,every,clear}, reduced, capsOk (bool|fn),
 *   t (k, fallback) => string, engine {fire,sustain,stop,channels,ceremony?}, log
 */
export function createMdCasino(o) {
  const opts = o || {};
  const C = MD_CASINO;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const reduced = !!opts.reduced;
  /* the chips: index.js hands `chips` (and `hud` = the strip element); the
     contract shape hands `hud` = {pot,...}. Accept both. */
  const hud = opts.chips || (opts.hud && !opts.hud.nodeType ? opts.hud : {}) || {};
  /* the host: the TABLE (the arch, the lamp and the trails are sized from it);
     `board` is the contract's name for the same seam. */
  const host = opts.table || opts.board || null;
  const eng = opts.engine || {};
  const armedBase = !!opts.stage && !!host && !!opts.timers && typeof opts.timers.after === 'function'
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
  const seedBase = String(opts.seed || 'md') + '|md-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- the engine, counted ------------------------------------------------ */
  const counts = { cues: 0, jackpots: 0, nearMisses: 0, floods: 0, words: 0, trails: 0, coins: 0 };
  let lastSwapCueAt = -1e9;
  function cue(name, level, extra) {
    if (!armed()) return;
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
  function nowMs() {
    try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
    return Date.now();
  }

  /* ---- identity (per class seed; FIXED draw order, append-only) ----------- */
  const ID = (() => {
    const offArc = roll('arc') < C.OFF_ARC;
    let hueA;
    let hueB;
    if (offArc) {
      hueA = lerp(C.HUE_OFF, roll('hue'));
      hueB = Math.max(140, Math.min(180, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (8 + roll('hue2') * 12)));
    } else {
      hueA = lerp(C.HUE_ARC, roll('hue'));
      hueB = Math.max(250, Math.min(350, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (16 + roll('hue2') * 22)));
    }
    const metal = C.METALS[Math.floor(roll('metal') * C.METALS.length)];
    const sway = lerp(C.LAMP_SWAY_S, roll('sway'));
    const bulbs = C.BULBS[Math.floor(roll('bulbs') * C.BULBS.length)];
    const bulbD = 8 + Math.round(roll('bulbs') * 3);
    const kbExtra = roll('kb') * C.KB_SEED_SPAN;
    const kbX = (roll('kb') < 0.5 ? -1 : 1) * (1 + roll('kb') * 1.5);
    const kbY = (roll('kb') < 0.5 ? -1 : 1) * (0.5 + roll('kb'));
    const kbO = (40 + Math.round(roll('kb') * 20)) + '% ' + (40 + Math.round(roll('kb') * 20)) + '%';
    const breath = 7 + roll('breath') * 5;                                 // 7..12s
    const drift = 18 + roll('drift') * 16;                                 // 18..34s
    /* the journey: 2-3 distinct weave stops, genes jittered once */
    const stops = C.STOPS_MIN + Math.floor(roll('stops') * C.STOPS_SPAN);
    const pool = C.WEAVES.slice();
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(roll('shuffle') * (i + 1)));
      const sw = pool[i]; pool[i] = pool[j]; pool[j] = sw;
    }
    const journey = pool.slice(0, stops).map((fam, i) => ({
      fam,
      alpha: 0.35 + roll('alpha') * 0.35,
      next: pool[(i + 1) % stops],
      nextAlpha: 0.1 + roll('next') * 0.15,
      tilt: (roll('tilt') < 0.5 ? -1 : 1) * (4 + roll('tilt') * 26),
      scale: 0.8 + roll('scale') * 0.6,
    }));
    return {
      offArc, hueA: Math.round(hueA), hueB: Math.round(hueB), metal, sway: +sway.toFixed(1),
      bulbs, bulbD, kbExtra, kbX, kbY, kbO, breath: +breath.toFixed(1), drift: +drift.toFixed(1), journey,
    };
  })();

  /* ---- nodes ------------------------------------------------------------- */
  let mq = null; let cs = null; let top = null;
  let lamp = null; let flood = null; let trail = null; let stat = null; let word = null; let ghost = null;
  const layers = {};
  const props = new Set();
  const stageClasses = new Set();
  let started = false;
  let heat = 0;
  let lastPaintHeat = -1;
  let stopIx = -1;
  let bellOn = false;
  let royalOn = false;
  let outOn = false;
  let shuffling = false;
  let rideDepth = 0;
  let bestStreak = 0;
  let coins = 0;
  let flashTimer = 0; let floodTimer = 0; let wordTimer = 0; let punchTimer = 0; let royalTimer = 0;
  let ghostTimer = 0; let bustTimer = 0; let bloomTimer = 0;
  const jackLog = [];

  function setProp(k, v) { setVar(opts.stage, k, v); props.add(k); }
  function stageClass(name, on) {
    setCls(opts.stage, name, on);
    if (on) stageClasses.add(name); else stageClasses.delete(name);
  }

  /* ---- the felt ----------------------------------------------------------- */
  function dressTable() {
    const id = ID;
    const sat = id.offArc ? 46 : 58;
    setProp('--md-n-hue-a', String(id.hueA));
    setProp('--md-n-hue-b', String(id.hueB));
    setProp('--md-n-felt', 'hsl(' + id.hueA + ',' + sat + '%,22%)');
    setProp('--md-n-felt-deep', 'hsl(' + id.hueA + ',' + (sat + 4) + '%,12%)');
    setProp('--md-n-felt-lit', 'hsl(' + id.hueB + ',' + (sat + 10) + '%,34%)');
    setProp('--md-n-la', 'hsla(' + id.hueA + ',' + (sat + 10) + '%,76%,.28)');
    setProp('--md-n-lb', 'hsla(' + id.hueB + ',' + (sat + 16) + '%,70%,.22)');
    setProp('--md-n-glow', 'hsl(' + id.hueB + ',88%,72%)');
    setProp('--md-n-metal', id.metal === 'brass' ? '#d4a64a' : id.metal === 'silver' ? '#c9c6d6' : '#e2a38c');
    setProp('--md-n-metal-dk', id.metal === 'brass' ? '#7a5a1c' : id.metal === 'silver' ? '#6a6880' : '#8a5a4a');
    setProp('--md-n-sway', id.sway + 's');
    setProp('--md-n-breath', id.breath + 's');
    setProp('--md-n-drift', id.drift + 's');
    setProp('--md-n-ride', '0');
    paintStop(0);
    say('casino: table dressed (hue ' + id.hueA + '/' + id.hueB + (id.offArc ? ' GREEN BAIZE' : '') + ', ' + id.metal
      + ', journey ' + id.journey.map((s) => s.fam).join('>') + ')');
  }
  function paintStop(ix) {
    const stop = ID.journey[Math.max(0, Math.min(ID.journey.length - 1, ix))];
    if (!stop) return;
    stopIx = ix;
    for (const fam of C.WEAVES) {
      const a = fam === stop.fam ? stop.alpha : fam === stop.next ? stop.nextAlpha : 0;
      setProp('--md-n-a-' + fam, a.toFixed(2));
    }
    setProp('--md-n-tilt', stop.tilt.toFixed(1));
    setProp('--md-n-scale', stop.scale.toFixed(2));
  }
  function walkJourney(h) {
    const n = ID.journey.length;
    if (n < 2) return;
    const raw = Math.min(n - 1, Math.floor(h * n));
    if (stopIx < 0) { paintStop(raw); return; }
    if (raw > stopIx && h >= (stopIx + 1) / n + C.STOP_HYST) paintStop(stopIx + 1);
    else if (raw < stopIx && h <= stopIx / n - C.STOP_HYST) paintStop(stopIx - 1);
  }

  /* ---- mounting ----------------------------------------------------------- */
  function mountBackdrop() {
    const host = opts.backdrop;
    if (!host || !host.appendChild) return;
    const order = ['felt', 'diamond', 'herringbone', 'damask', 'pinstripe', 'lace', 'smoke', 'dark', 'vig'];
    for (const name of order) {
      const n = el('div', 'g-md-bd g-md-bd-' + name);
      if (!n) continue;
      layers[name] = n;
      host.appendChild(n);
    }
    if (!reduced) {
      for (const name of ['felt', 'smoke']) {
        const n = layers[name];
        if (!n) continue;
        setCls(n, 'g-md-cs-kb', true);
        setVar(n, '--md-kb-x', ID.kbX + '%');
        setVar(n, '--md-kb-y', ID.kbY + '%');
        setVar(n, '--md-kb-o', ID.kbO);
      }
    }
  }
  function mountMarquee() {
    if (mq || !host.appendChild) return;
    mq = el('div', 'g-md-mq');
    if (!mq) return;
    setVar(mq, '--md-mq-hue', ID.hueB);
    setVar(mq, '--md-mq-d', ID.bulbD + 'px');
    setVar(mq, '--n', ID.bulbs);
    /* the arch: bulbs on the upper 200 degrees of an ellipse hugging the arc */
    /* the rail's ellipse (style.js radius 46%/70%): centre (50%,70%), radii (49%,69%) so
       the bulbs sit ON the brass, the chase running up one side, over, and down */
    const span = Math.PI * 1.16;
    for (let i = 0; i < ID.bulbs; i++) {
      const b = el('span', 'g-md-mq-bulb');
      if (!b) break;
      const a = Math.PI + (Math.PI - span) / 2 + (i / (ID.bulbs - 1)) * span;   // left ... over the top ... right
      b.style.left = (50 + 49.2 * Math.cos(a)).toFixed(2) + '%';
      b.style.top = (70 + 69 * Math.sin(a)).toFixed(2) + '%';
      setVar(b, '--i', i);
      mq.appendChild(b);
    }
    try { host.appendChild(mq); } catch (e) { mq = null; }
  }
  function mountOverlay() {
    if (cs || !host.appendChild) return;
    cs = el('div', 'g-md-cs');
    if (!cs) return;
    lamp = el('div', 'g-md-cs-lamp');
    flood = el('div', 'g-md-cs-flood');
    trail = el('i', 'g-md-cs-trail');
    stat = el('div', 'g-md-cs-static');
    word = el('div', 'g-md-cs-word');
    for (const n of [lamp, flood, trail, stat, word]) { if (n) cs.appendChild(n); }
    try {
      if (typeof host.insertBefore === 'function' && host.firstChild) host.insertBefore(cs, host.firstChild);
      else host.appendChild(cs);
    } catch (e) { cs = null; }
    top = el('div', 'g-md-cs-top');
    if (top) {
      ghost = el('i', 'g-md-cs-ghost');
      if (ghost) top.appendChild(ghost);
      try { host.appendChild(top); } catch (e) { top = null; }
    }
  }

  /* ---- geometry ----------------------------------------------------------- */
  function shellOf(slot) {
    try {
      if (typeof opts.shells === 'function') {
        for (const s of (opts.shells() || [])) { if (s && s.getAttribute && String(s.getAttribute('data-slot')) === String(slot)) return s; }
      }
      const q = host && host.querySelector ? host : opts.stage;
      return q && q.querySelector ? q.querySelector('.g-md-shell[data-slot="' + String(slot) + '"]') : null;
    } catch (e) { return null; }
  }
  /** Table-relative centre (px) and width of a slot's shell; null off-DOM. */
  function slotPoint(slot) {
    const b = rectOf(host);
    const s = rectOf(shellOf(slot));
    if (!b || !s || !b.width || !s.width) return null;
    return { x: s.left - b.left + s.width / 2, y: s.top - b.top + s.height / 2, w: s.width, h: s.height, bw: b.width, bh: b.height };
  }
  function slotPct(slot, fallback) {
    const p = slotPoint(slot);
    if (!p) return fallback == null ? 50 : fallback;
    return Math.max(4, Math.min(96, (p.x / p.bw) * 100));
  }

  /* ---- the light ---------------------------------------------------------- */
  function paintHeat(force) {
    if (!mq && !layers.felt) return;
    if (!force && Math.abs(heat - lastPaintHeat) < C.HEAT_REPAINT_STEP) return;
    lastPaintHeat = heat;
    if (mq) {
      const gold = bellOn || royalOn;
      let period = gold ? C.MQ_T_BELL : (C.MQ_T_SLOW + (C.MQ_T_FAST - C.MQ_T_SLOW) * heat);
      if (shuffling && !gold) period *= C.MQ_SHUFFLE_MUL;
      const alpha = outOn ? 0 : (gold ? C.MQ_A_BELL : lerp([C.MQ_A_LO, C.MQ_A_HI], heat));
      setVar(mq, '--md-mq-t', period.toFixed(2) + 's');
      setVar(mq, '--md-mq-a', alpha.toFixed(2));
    }
    if (!reduced) {
      const kb = (C.KB_T[0] + (C.KB_T[1] - C.KB_T[0]) * heat + ID.kbExtra).toFixed(1) + 's';
      for (const name of ['felt', 'smoke']) setVar(layers[name], '--md-kb-t', kb);
    }
  }
  function flashArch(ms) {
    if (!mq || !armed() || reduced) return;
    setCls(mq, 'flash', true);
    cancel(flashTimer);
    flashTimer = after(ms || C.MQ_FLASH_MS, () => setCls(mq, 'flash', false));
  }
  function floodAt(pct, q, ms, tone) {
    if (!flood || !armed()) return;
    counts.floods++;
    flood.className = 'g-md-cs-flood' + (tone ? ' ' + tone : '');
    if (typeof flood.offsetWidth === 'number') void flood.offsetWidth;
    setVar(cs, '--md-cs-fx', pct.toFixed(1));
    setVar(cs, '--md-cs-pay', lerp(C.FLOOD_A, q).toFixed(2));
    setCls(flood, 'on', true);
    cancel(floodTimer);
    floodTimer = after(ms || C.FLOOD_MS, () => { setCls(flood, 'on', false); });
  }
  function lampTo(pct, width, alpha) {
    if (!cs) return;
    setVar(cs, '--md-cs-lx', pct.toFixed(1));
    setVar(cs, '--md-cs-lw', width.toFixed(2));
    setVar(cs, '--md-cs-la', alpha.toFixed(2));
  }
  function punchChip(chip, q, gold) {
    if (!chip || !armed()) return;
    if (reduced) {
      setCls(chip, 'g-md-cs-bloom', true);
      setCls(chip, 'gold', !!gold);
      cancel(bloomTimer);
      bloomTimer = after(340, () => { setCls(chip, 'g-md-cs-bloom', false); setCls(chip, 'gold', false); });
      return;
    }
    setCls(chip, 'g-md-cs-sink', false);
    setCls(chip, 'g-md-cs-punch', true);
    setVar(chip, '--md-cs-ps', (1 + lerp(C.PUNCH_SCALE, q)).toFixed(3));
    setVar(chip, '--md-cs-py', '0px');
    cancel(punchTimer);
    punchTimer = after(C.PUNCH_MS, () => { setVar(chip, '--md-cs-ps', '1'); });
  }
  function showWord(key, fallback, tone) {
    if (!word || !armed()) return;
    counts.words++;
    try { word.textContent = t(key, fallback); } catch (e) { return; }
    word.className = 'g-md-cs-word' + (tone ? ' ' + tone : '');
    if (typeof word.offsetWidth === 'number') void word.offsetWidth;
    setCls(word, 'on', true);
    cancel(wordTimer);
    wordTimer = after(C.WORD_MS + 60, () => setCls(word, 'on', false));
  }
  /** Coins shower from a table point toward the pot chip (or just up and out). */
  function coinsFrom(pct, count) {
    if (!top || !top.appendChild || reduced || !armed()) return 0;
    const b = rectOf(host);
    const p = rectOf(hud.pot);
    let dx = 0; let dy = -90;
    if (b && p && b.width) {
      dx = (p.left + p.width / 2) - (b.left + b.width * pct / 100);
      dy = (p.top + p.height / 2) - (b.top + b.height * 0.6);
    }
    let made = 0;
    for (let i = 0; i < count; i++) {
      if (coins >= C.COINS_MAX) break;
      const c = el('i', 'g-md-cs-coin');
      if (!c || !c.style) break;
      const d = lerp(C.COIN_MS, roll('coin-d')) / 1000;
      setVar(c, '--x', (pct + (roll('coin-x') - 0.5) * 10).toFixed(1) + '%');
      setVar(c, '--y', '60%');
      setVar(c, '--s', (7 + roll('coin-s') * 7).toFixed(0) + 'px');
      setVar(c, '--d', d.toFixed(2) + 's');
      setVar(c, '--dl', (roll('coin-dl') * 0.28).toFixed(2) + 's');
      setVar(c, '--dx', (dx + (roll('coin-sx') - 0.5) * 60).toFixed(0) + 'px');
      setVar(c, '--dy', (dy + (roll('coin-sy') - 0.5) * 30).toFixed(0) + 'px');
      setVar(c, '--r', ((roll('coin-r') - 0.5) * 720).toFixed(0) + 'deg');
      top.appendChild(c);
      coins += 1; made += 1; counts.coins += 1;
      after(d * 1000 + 400, () => { coins = Math.max(0, coins - 1); try { c.remove(); } catch (e) { /* ignore */ } });
    }
    return made;
  }

  /* ---- the ladder --------------------------------------------------------- */
  function jackpot(intensity, why) {
    if (!armed()) return;
    counts.jackpots++;
    jackLog.push(why);
    ceremony('jackpot', { intensity });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: +(0.9 + 0.3 * intensity).toFixed(3) });
    flashArch(C.MQ_FLASH_MS + 300 * intensity);
  }
  function nearMiss(kind, intensity) {
    if (!armed()) return;
    counts.nearMisses++;
    ceremony('near_miss', { intensity });
    cue('near_miss', C.NEAR_LEVEL, { pitch: kind === 'almost' ? 0.8 : 1.1 });
  }
  function royal(pct) {
    royalOn = true;
    counts.royal = (counts.royal || 0) + 1;
    stageClass('g-md-royal', true);
    setCls(mq, 'gold', true);
    if (flood) { flood.className = 'g-md-cs-flood royal'; setVar(cs, '--md-cs-fx', pct.toFixed(1)); }
    showWord('md_royal', 'ROYAL', 'gold');
    ceremony('jackpot', { intensity: C.ROYAL_I });
    cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.2 });
    after(260, () => cue('jackpot', C.JACKPOT_LEVEL, { pitch: 1.5 }));
    after(520, () => cue('stamp', 0.5, { pitch: 1 }));
    flashArch(C.ROYAL_MS);
    coinsFrom(pct, C.COINS_ROYAL);
    cancel(royalTimer);
    royalTimer = after(C.ROYAL_MS, () => {
      royalOn = false;
      if (flood) flood.className = 'g-md-cs-flood';
      if (!bellOn) setCls(mq, 'gold', false);
      stageClass('g-md-royal', false);
      paintHeat(true);
    });
    jackLog.push('royal');
    counts.jackpots++;
  }

  /* ============================================================ API ==== */
  const api = {
    start() {
      if (started) return;
      started = true;
      if (!armed()) { say('casino: disarmed'); return; }
      ensureStyle();
      mountBackdrop();
      mountMarquee();
      mountOverlay();
      dressTable();
      lampTo(50, C.LAMP_W_SHUFFLE, 0.45);
      paintHeat(true);
      say('casino: floor lit (' + ID.bulbs + ' bulbs, ' + Object.keys(layers).length + ' layers)');
    },
    setHeat(h) {
      heat = clamp01(h);
      if (!started) return;
      paintHeat(false);
      walkJourney(heat);
    },

    /** One shell lifts and shows the target: the lamp slides over it. */
    reveal(slot) {
      if (!armed() || !started) return;
      const pct = slotPct(slot, 50);
      lampTo(pct, C.LAMP_W_REVEAL, 0.7);
      cue('sting', C.REVEAL_LEVEL, { pitch: 0.9 });
    },

    /** The shuffle begins: the lamp opens wide, the chase quickens. */
    shuffleStart(n) {
      if (!armed() || !started) return;
      shuffling = true;
      lampTo(50, C.LAMP_W_SHUFFLE, 0.42);
      stageClass('g-md-shuffling', true);
      paintHeat(true);
      cue('slide', C.SHUFFLE_LEVEL, { pitch: 0.8 + 0.05 * Math.min(6, Number(n) || 0) });
    },

    /** Two slots traded: a felt streak between them; a glitch is a static pop. */
    swap(a, b, glitch) {
      if (!armed() || !started) return;
      const pa = slotPoint(a);
      const pb = slotPoint(b);
      if (trail && pa && pb && !reduced && !glitch) {
        const dx = pb.x - pa.x; const dy = pb.y - pa.y;
        const len = Math.sqrt(dx * dx + dy * dy);
        if (len > 2) {
          try {
            trail.style.left = pa.x.toFixed(0) + 'px';
            trail.style.top = pa.y.toFixed(0) + 'px';
            trail.style.width = len.toFixed(0) + 'px';
            setVar(trail, '--md-cs-ta', (Math.atan2(dy, dx) * 180 / Math.PI).toFixed(1) + 'deg');
          } catch (e) { /* ignore */ }
          restart(trail, 'on');
          counts.trails++;
        }
      }
      if (glitch && stat && !reduced) restart(stat, 'on');
      const now = nowMs();
      if (now - lastSwapCueAt >= C.SWAP_GAP_MS) {
        lastSwapCueAt = now;
        if (glitch) cue('glitch', C.GLITCH_LEVEL, { pitch: 0.9 + roll('glitch-p') * 0.3 });
        else cue('slide', lerp(C.SWAP_LEVEL, heat), { pitch: +(0.85 + 0.3 * heat + (roll('swap-p') - 0.5) * 0.1).toFixed(3) });
      }
    },

    /** The verdict, after the ledger. */
    pick(ev) {
      if (!armed() || !started) return;
      const e = ev || {};
      shuffling = false;
      stageClass('g-md-shuffling', false);
      const streak = Math.max(0, Number(e.streak) || 0);
      bestStreak = Math.max(bestStreak, streak);
      const pct = slotPct(e.slot, 50);
      lampTo(pct, C.LAMP_W_PICK, 0.75);
      if (e.correct) {
        /* THE CHIME LADDER */
        cue('sting', lerp(C.CHIME_LEVEL, Math.min(10, streak) / 10), { pitch: pitchForStreak(streak) });
        /* PAYOUT LIGHT */
        floodAt(pct, clamp01(0.35 + streak / 12), C.FLOOD_MS);
        punchChip(hud.pot, clamp01(0.3 + streak / 12), false);
        if (streak >= 3) flashArch();
        /* NEAR-MISS STAGING (b): the sharp read */
        if (isSharp(e.latencyMs)) { showWord('md_sharp', 'SHARP', 'lav'); nearMiss('sharp', C.NEAR_MISS_I.sharp); }
        /* THE JACKPOT LADDER */
        const mi = C.MILESTONES.indexOf(streak);
        if (mi >= 0) {
          cue('streak', 0.45, { pitch: pitchForStreak(streak) });
          jackpot(C.MAJOR_I[mi], 'major@' + streak);
        } else if (roll('jack-minor') < lerp(C.MINOR_CHANCE, heat)) {
          jackpot(C.MINOR_I, 'minor@' + streak);
        }
      } else {
        cue('bump', C.THUD_LEVEL, { pitch: 0.7 });       // a muted thud, never silence
        floodAt(pct, 0.1, 320);
        if (flood) setCls(flood, 'dim', true);
      }
      paintHeat(true);
    },

    /** The stake was taken: a ride slams the pot and deepens the rims. */
    stake(ev) {
      if (!armed() || !started) return;
      const e = ev || {};
      if (e.ride) {
        rideDepth += 1;
        setProp('--md-n-ride', String(Math.min(6, rideDepth)));
        punchChip(hud.pot, clamp01(0.5 + rideDepth / 6), false);
        flashArch();
        // the coin-tick roll: three quick pops stepping up
        cue('pop', C.RIDE_LEVEL, { pitch: 1 + rideDepth * 0.06 });
        after(90, () => cue('pop', C.RIDE_LEVEL, { pitch: 1.12 + rideDepth * 0.06 }));
        after(180, () => cue('pop', C.RIDE_LEVEL, { pitch: 1.26 + rideDepth * 0.06 }));
      }
    },

    /** The pot is banked: the payout. Gold light, coins, the arpeggio, the thunk. */
    bank(pot) {
      if (!armed() || !started) return;
      const q = potLight(pot);
      const pct = 50;
      const rung = bankRung(rideDepth);
      if (rung === 'royal') {
        royal(pct);
      } else {
        floodAt(pct, 0.5 + 0.5 * q, C.BANK_FLOOD_MS, 'gold');
        flashArch(C.MQ_FLASH_MS + 200);
        coinsFrom(pct, Math.round(lerp(C.COINS_BANK, Math.max(q, rideDepth / 5))));
        cue('streak', C.BANK_LEVEL, { pitch: 1 + 0.04 * rideDepth });
        after(140, () => cue('stamp', 0.4, { pitch: 1 }));
        if (rung === 'major') jackpot(C.MAJOR_RIDE_I, 'major@ride' + rideDepth);
      }
      punchChip(hud.pot, clamp01(0.5 + q / 2), true);
      if (reduced) setCls(hud.pot, 'gold', true);
      rideDepth = 0;
      setProp('--md-n-ride', '0');
    },

    /** The pot is lost: the table swallows it. Dim, never silent. */
    bust() {
      if (!armed() || !started) return;
      rideDepth = 0;
      setProp('--md-n-ride', '0');
      stageClass('g-md-bust', true);
      if (hud.pot && !reduced) { setCls(hud.pot, 'g-md-cs-punch', false); setCls(hud.pot, 'g-md-cs-sink', true); }
      lampTo(50, C.LAMP_W_PICK, 0.25);
      cue('stamp_bad', C.BUST_LEVEL, { pitch: 0.85 });
      cancel(bustTimer);
      bustTimer = after(C.BUST_MS, () => {
        stageClass('g-md-bust', false);
        setCls(hud.pot, 'g-md-cs-sink', false);
        lampTo(50, C.LAMP_W_SHUFFLE, 0.45);
      });
    },

    /** Near-miss staging: the true target ghosts through the picked shell. */
    almost(slotPicked, slotTrue) {
      if (!armed() || !started) return;
      const a = slotPoint(slotPicked);
      const b = slotPoint(slotTrue);
      if (ghost && a && b) {
        let url = null;
        try {
          const media = shellOf(slotTrue) && shellOf(slotTrue).querySelector ? shellOf(slotTrue).querySelector('.g-md-media') : null;
          if (media && media.tagName && String(media.tagName).toLowerCase() === 'img' && media.src) url = media.src;
          else if (media && media.poster) url = media.poster;
        } catch (e) { url = null; }
        const w = Math.max(24, a.w * 0.7); const h = Math.max(24, a.h * 0.7);
        setVar(ghost, '--w', w.toFixed(0) + 'px'); setVar(ghost, '--h', h.toFixed(0) + 'px');
        setVar(ghost, '--x0', (a.x - w / 2).toFixed(0) + 'px'); setVar(ghost, '--y0', (a.y - h / 2).toFixed(0) + 'px');
        setVar(ghost, '--x1', (b.x - w / 2).toFixed(0) + 'px'); setVar(ghost, '--y1', (b.y - h / 2).toFixed(0) + 'px');
        try { ghost.style.backgroundImage = url ? 'url("' + url + '")' : ''; } catch (e) { /* ignore */ }
        restart(ghost, 'on');
        cancel(ghostTimer);
        ghostTimer = after(C.ALMOST_MS + 80, () => setCls(ghost, 'on', false));
      }
      showWord('md_almost', 'ONE OFF', 'lav');
      nearMiss('almost', C.NEAR_MISS_I.almost);
    },

    /** The last stretch: gold arch, frantic chase. */
    bell(on) {
      bellOn = !!on;
      setCls(mq, 'gold', bellOn || royalOn);
      paintHeat(true);
    },

    /** The bell took the table: the rig sighs out and pays a dim light. */
    dimOut() {
      outOn = true;
      bellOn = false;
      setCls(mq, 'flash', false);
      setCls(mq, 'out', true);
      stageClass('g-md-out', true);
      stageClass('g-md-shuffling', false);
      if (armed() && started) {
        /* losses disguised: a dim payout, never silence */
        floodAt(50, 0.12 + 0.2 * clamp01(bestStreak / 10), C.END_DIM_MS, 'dim');
        cue('wash', C.DIM_LEVEL, { pitch: 0.9 });
        lampTo(50, C.LAMP_W_SHUFFLE, 0.2);
      }
      paintHeat(true);
    },

    pause() { /* transient light simply ends; the chase is CSS and .suspended freezes it */ },
    resume() { /* nothing to re-arm: every light is event-driven */ },

    stop() {
      cancelAll();
      for (const n of [mq]) setCls(n, 'flash', false);
      setCls(flood, 'on', false);
      setCls(word, 'on', false);
      setCls(ghost, 'on', false);
      stageClass('g-md-bust', false);
      stageClass('g-md-shuffling', false);
      if (hud.pot) { setCls(hud.pot, 'g-md-cs-sink', false); setVar(hud.pot, '--md-cs-ps', '1'); }
    },
    destroy() {
      destroyed = true;
      cancelAll();
      for (const n of [mq, cs, top]) { try { if (n && n.parentNode) n.parentNode.removeChild(n); } catch (e) { /* noop */ } }
      for (const k of Object.keys(layers)) { try { layers[k].remove(); } catch (e) { /* ignore */ } delete layers[k]; }
      mq = cs = top = lamp = flood = trail = stat = word = ghost = null;
      for (const name of Array.from(stageClasses)) setCls(opts.stage, name, false);
      stageClasses.clear();
      if (opts.stage && opts.stage.style) { for (const k of props) { try { opts.stage.style.removeProperty(k); } catch (e) { /* ignore */ } } }
      props.clear();
      for (const chip of [hud.pot, hud.streak]) {
        setCls(chip, 'g-md-cs-punch', false); setCls(chip, 'g-md-cs-bloom', false); setCls(chip, 'g-md-cs-sink', false); setCls(chip, 'gold', false);
      }
      coins = 0;
    },
    diagnostics() {
      return {
        armed: armed(), started, heat: +heat.toFixed(3), identity: ID, stop: stopIx,
        bell: bellOn, royal: royalOn, out: outOn, shuffling, rideDepth, bestStreak,
        counts: Object.assign({}, counts), jackpots: jackLog.slice(),
        liveTimers: live.size, nodes: [mq, cs, top].filter(Boolean).length, layers: Object.keys(layers).length, coins,
      };
    },
  };
  return api;
}

export default createMdCasino;
