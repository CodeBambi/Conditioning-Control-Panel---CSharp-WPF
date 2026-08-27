/* ============================================================================
 * games/anomaly/trickster.js - DECK III of the House Rules, dealt into the
 * darkroom. A spot-the-difference room is where your own eyes stop being
 * reliable witnesses, so every card here attacks the READING - the chrome, the
 * clock, the hand - and NEVER the grid. This deck does not know which frame is
 * the odd one (CORE keeps it in closure), never reads it, never infers it.
 *
 *   GHOST OUTLINE   "it moved": CORE tells this deck (afterTap({moved:true,i})
 *                   or ghostOutline(i)) that the player tapped where the
 *                   anomaly WAS before a relocation. A dashed outline blooms on
 *                   THAT frame (the player's own tap - public by then), a "+1s"
 *                   pip rises off it (the refund is CORE's; the pip is the
 *                   staging; the proctor line is CORE's). Near-miss that
 *                   teaches.
 *   GHOST CURSOR    a faint cursor echo trails the real pointer by ~430ms;
 *                   stall >= 2.6s and it stops trailing and drifts toward a
 *                   frame of the house's choosing - a SEEDED frame, which is the
 *                   point: this deck cannot know the odd one, so the lure can
 *                   never be trusted. Pure suggestion: pointer-events none, it
 *                   cannot click, the real cursor is never hidden, it dies on
 *                   the next tap. Tier 2+, mouse pointers only.
 *   STAT FLICKER    the streak or the round chip briefly reads slightly off,
 *                   then corrects itself with a static pop to the exact text
 *                   CORE's chipText() answers. The ledger never moves.
 *   CROOKED CLOCK   the clock FACE bends: races through the middle (shows less
 *                   time than you have), crawls as the bell nears, meets the
 *                   truth at the last 15s and stays honest. A MutationObserver
 *                   re-bends the face the instant CORE repaints the truth (a
 *                   microtask, before the frame paints). Tier 2+.
 *   GLITCH-TO-ASSET a piece of HUD chrome flickers into one of the player's
 *                   own pool stills for ~130ms, then back. Tier 3+.
 *   THE MELT        a NON-odd frame runs like wax under the safelight: a wax
 *                   sheet slides down over it and drips, then it reforms. The
 *                   frame is chosen by this deck's OWN seeded draw from the
 *                   list CORE's meltCandidates() answers (ELEMENTS, never an
 *                   index - the odd one is simply not in the list) or, failing
 *                   that, vetoed by a canMelt(i) predicate. Neither hook -> the
 *                   card folds, by law. The melt is an OVERLAY
 *                   at the frame's rect: .g-an-tile / .g-an-face are never
 *                   written, no class, no style, no attribute. Nothing is lost.
 *
 * DEALING RULE: flicker / clock / chrome / melt are SCHEDULE-DEALT at start()
 * from the seeded plan - budget 2/4/6/8 by tier, at most 4 in any rolling 60s
 * with a 9s floor (a 90s class fits ~5 at most; the budget is a ceiling), never
 * in the first 10s, never in the last 12s. A slot whose moment arrives halted
 * re-queues once, then folds. The ghost cursor is stall-reactive (the stall is
 * the player's own); the ghost outline is event-reactive (CORE says when).
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the flicker and the clock write chip TEXT and restore
 *       it from chipText (the truth source); nothing here reads or writes the
 *       round, streak, time budget, grade or the odd index.
 *   II  input honest  - every node lives in the deck's own overlay layer in the
 *       stage, pointer-events:none; the grid, the tiles and the faces are never
 *       touched, covered in a way that steals a click, moved or resized.
 *   IV  images over text - four short lines (it moved / +1s / did you see that
 *       / runs like wax), every one through opts.t, under 2s on screen.
 *   V   seeded        - per-tag mulberry32 off seed+'|an-trickster|<tag>',
 *       append-only; a retake replays the identical deal.
 *   VI  exits sacred  - capsOk false disarms the deck (no nodes, no listener,
 *       no timers); reduced motion drops the ghost cursor, the flicker pop, the
 *       chrome flicker and the melt's drip (the clock still bends - a number is
 *       not motion; the melt becomes a plain veil); every timer rides the game's
 *       registry AND a local set.
 *   VII strings       - an_refund / an_trick_seen / an_trick_melt only (the
 *       "it moved" proctor line is CORE's an_moved).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const AN_TRICKSTER = Object.freeze({
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  FIRST_DEAL_MS: 10000,
  TAIL_MS: 12000,
  MIN_GAP_MS: 9000,
  RATE_WINDOW_MS: 60000,
  RATE_MAX: 4,
  RETRY_MS: 1800,
  RETRY_MAX: 2,

  /** Card weights by tier (the clock is ONE slot from CLOCK_FROM_TIER). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ flicker: 0.6, melt: 0.4, chrome: 0 }),
    2: Object.freeze({ flicker: 0.45, melt: 0.55, chrome: 0 }),
    3: Object.freeze({ flicker: 0.32, melt: 0.38, chrome: 0.3 }),
    4: Object.freeze({ flicker: 0.28, melt: 0.36, chrome: 0.36 }),
  }),
  CLOCK_FROM_TIER: 2,
  CHROME_FROM_TIER: 3,
  MELT_FROM_TIER: 1,
  GHOST_FROM_TIER: 2,
  MELT_TRIES: 8,

  /** Stat flicker: how long the wrong number stands. */
  FLICKER_MS: 450,
  /** Crooked clock: bend at mid-budget (fraction of budget), honest tail. */
  CLOCK_BEND: 0.12,
  CLOCK_HONEST_SEC: 15,
  CLOCK_RAMP: 0.35,
  CLOCK_POLL_MS: 250,
  /** Glitch-to-asset: one beat of someone else's memory. */
  CHROME_MS: 130,
  SEEN_TAUNT_CHANCE: 0.3,
  /** The melt: sag, drip, reform. */
  MELT_MS: 1400,
  MELT_FOLLOW_MS: Object.freeze([90, 220, 400, 640, 950, 1300]),
  MELT_REFORM_MS: 500,
  /** The ghost cursor. */
  GHOST_TICK_MS: 90,
  GHOST_TRAIL_MS: 430,
  GHOST_STALL_MS: 2600,
  GHOST_LERP: 0.14,
  GHOST_KEEP_MS: 2200,
  /** The ghost outline. */
  OUTLINE_MS: 1000,
  REFUND_MS: 900,
});

/** Every lexicon key this deck can render (CORE's lex.js mirrors them). */
export const AN_TRICKSTER_LEX = Object.freeze(['an_refund', 'an_trick_seen', 'an_trick_melt']);

const STYLE_ID = 'g-an-trickster-style';
export const STYLE_TEXT = `
/* THE TRICKSTER LAYER: one layer in the stage, every node pointer-events:none. */
.g-an-tk{position:absolute;inset:0;z-index:4;pointer-events:none;overflow:hidden}
.g-an-tk *{pointer-events:none}
/* GHOST CURSOR */
.g-an-tk-ghost{position:absolute;left:0;top:0;width:15px;height:19px;opacity:0;will-change:transform;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, hsl(var(--an-n-hue,354) 90% 78% / .9), rgba(184,166,232,.55));
  filter:drop-shadow(0 0 6px hsl(var(--an-n-hue,354) 90% 70% / .6));
  transition:transform .34s ease-out, opacity .38s ease}
.g-an-tk-ghost.on{opacity:.3}
.g-an-tk-ghost.lure{opacity:.52;animation:g-an-tk-ghostpulse 1.5s ease-in-out infinite}
@keyframes g-an-tk-ghostpulse{50%{filter:drop-shadow(0 0 12px hsl(var(--an-n-hue,354) 90% 70% / .95))}}
/* GHOST OUTLINE: it moved */
.g-an-tk-outline{position:absolute;border-radius:6px;opacity:0;border:2px dashed rgba(184,166,232,.9);
  box-shadow:0 0 16px rgba(184,166,232,.55),inset 0 0 16px rgba(184,166,232,.25)}
.g-an-tk-outline.on{animation:g-an-tk-outline 1s ease-out forwards}
@keyframes g-an-tk-outline{0%{opacity:0;transform:scale(1.06)}15%{opacity:1;transform:scale(1)}70%{opacity:.9}100%{opacity:0;transform:scale(.98)}}
.g-an-tk-refund{position:absolute;left:var(--x,50%);top:var(--y,50%);transform:translate(-50%,-50%);opacity:0;
  font:800 clamp(12px,1.9vmin,18px)/1 var(--mono,monospace);letter-spacing:.12em;color:#d8ccff;
  text-shadow:0 0 10px rgba(184,166,232,.8);white-space:nowrap}
.g-an-tk-refund.on{animation:g-an-tk-refund .9s ease-out forwards}
@keyframes g-an-tk-refund{0%{opacity:0;transform:translate(-50%,-40%)}20%{opacity:1;transform:translate(-50%,-60%)}100%{opacity:0;transform:translate(-50%,-140%)}}
/* STAT FLICKER: the static pop */
.g-an-tk-flick{text-shadow:0 0 22px rgba(184,166,232,.75), 0 0 3px rgba(255,255,255,.5);
  animation:g-an-tk-static .16s steps(3) 2}
@keyframes g-an-tk-static{0%{filter:none}40%{filter:brightness(1.5) contrast(1.4)}100%{filter:none}}
/* GLITCH-TO-ASSET: a pool still wearing a chrome box for a beat */
.g-an-tk-chrome{position:absolute;overflow:hidden;border-radius:999px;opacity:.92;
  box-shadow:0 0 0 1px rgba(184,166,232,.4);animation:g-an-tk-chrome .13s steps(2) 1}
.g-an-tk-chrome img{width:100%;height:100%;object-fit:cover;display:block;filter:saturate(1.2) contrast(1.1)}
@keyframes g-an-tk-chrome{0%{transform:translateX(-2px)}100%{transform:translateX(2px)}}
/* THE MELT: a wax sheet over a NON-odd frame, at its rect, dripping */
.g-an-tk-wax{position:absolute;border-radius:6px;opacity:0;overflow:visible;
  background:linear-gradient(180deg, hsl(var(--an-n-hue,354) 60% 22% / .0) 0%, hsl(var(--an-n-hue,354) 70% 30% / .55) 30%, hsl(var(--an-n-hue,354) 80% 38% / .8) 100%);
  transform-origin:50% 0}
.g-an-tk-wax.on{animation:g-an-tk-wax 1.4s cubic-bezier(.5,.05,.7,1) forwards}
.g-an-tk-wax.reform{animation:g-an-tk-reform .5s ease-out forwards}
@keyframes g-an-tk-wax{0%{opacity:0;transform:scaleY(.2)}25%{opacity:.85;transform:scaleY(.6)}100%{opacity:.95;transform:scaleY(1.08)}}
@keyframes g-an-tk-reform{from{opacity:.95;transform:scaleY(1.08)}to{opacity:0;transform:scaleY(1)}}
.g-an-tk-wax::before,.g-an-tk-wax::after{content:"";position:absolute;top:100%;width:14%;height:0;border-radius:0 0 50% 50%;
  background:hsl(var(--an-n-hue,354) 80% 40% / .85);box-shadow:0 2px 8px hsl(var(--an-n-hue,354) 80% 30% / .6)}
.g-an-tk-wax::before{left:22%}
.g-an-tk-wax::after{left:61%;width:10%}
.g-an-tk-wax.on::before{animation:g-an-tk-drip 1.4s .3s ease-in forwards}
.g-an-tk-wax.on::after{animation:g-an-tk-drip 1.4s .55s ease-in forwards}
@keyframes g-an-tk-drip{from{height:0}to{height:46%}}
@media (prefers-reduced-motion: reduce){
  .g-an-tk-ghost{display:none}
  .g-an-tk-flick{animation:none}
  .g-an-tk-chrome{display:none}
  .g-an-tk-wax.on{animation:none;opacity:.7;transform:none}
  .g-an-tk-wax::before,.g-an-tk-wax::after{display:none}
  .g-an-tk-outline.on{animation:none;opacity:.9}
  .g-an-tk-refund.on{animation:none;opacity:1}
}
html.arc-reduced .g-an-tk-ghost{display:none}
html.arc-reduced .g-an-tk-flick{animation:none}
html.arc-reduced .g-an-tk-chrome{display:none}
html.arc-reduced .g-an-tk-wax.on{animation:none;opacity:.7;transform:none}
html.arc-reduced .g-an-tk-wax::before,html.arc-reduced .g-an-tk-wax::after{display:none}
html.arc-reduced .g-an-tk-outline.on{animation:none;opacity:.9}
html.arc-reduced .g-an-tk-refund.on{animation:none;opacity:1}
.g-an-stage.suspended .g-an-tk *{animation-play-state:paused !important}
/* ---- THE PHONE CEILING (html.ae-touch) ------------------------------------
   Coarse pointer. The lure ghost pulsed a drop-shadow forever - a filter
   re-rastered every frame on a node a transition is also moving - and the stat
   flicker keyframed brightness+contrast. Both keep their beat on opacity
   instead; the ghost keeps its shape and its colour, it just stops breathing
   its glow. The chrome still's two static passes (saturate/contrast over a
   decoded photo) come off too. Nothing here is hidden: it is all read.
   -------------------------------------------------------------------------- */
html.ae-touch .g-an-tk-ghost{filter:none}
html.ae-touch .g-an-tk-ghost.lure{animation:g-an-tk-ghostpulse-t 1.5s ease-in-out infinite}
@keyframes g-an-tk-ghostpulse-t{50%{opacity:.78}}
html.ae-touch .g-an-tk-flick{animation:g-an-tk-static-t .16s steps(3) 2}
@keyframes g-an-tk-static-t{0%{opacity:1}40%{opacity:.55}100%{opacity:1}}
/* the twin is a new name, so the reduced gate has to say its kill again */
html.arc-reduced.ae-touch .g-an-tk-flick{animation:none}
@media (prefers-reduced-motion: reduce){html.ae-touch .g-an-tk-flick{animation:none}}
html.ae-touch .g-an-tk-chrome img{filter:none}
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
function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }

/**
 * The crooked face, pure: displayed seconds-left for a real seconds-left.
 * f(B)=B, f(honest)=honest, monotonic (bend*pi < 1), honest at/after the bell.
 */
export function bendClock(secLeft, budgetSec, bend, honestSec) {
  const x = Math.max(0, Number(secLeft) || 0);
  const B = Math.max(30, Number(budgetSec) || 90);
  const H = Number.isFinite(honestSec) ? honestSec : AN_TRICKSTER.CLOCK_HONEST_SEC;
  if (x <= H || x >= B) return Math.round(x);
  const A0 = Number.isFinite(bend) ? bend : AN_TRICKSTER.CLOCK_BEND;
  const u = x / B;
  const uh = H / B;
  const ramp = Math.max(0, Math.min(1, (u - uh) / AN_TRICKSTER.CLOCK_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, Math.round(B * (u - A * Math.sin(Math.PI * u))));
}

/**
 * The deal plan, pure: n slots over the budget, first/tail guards, the 9s
 * floor and the 4-in-60s cap, cards by tier weight, the clock one slot.
 * @param {Function} roll  tag -> 0..1 (the deck's seeded stream)
 */
export function buildDealPlan(tier, budgetSec, rollFn) {
  const T = AN_TRICKSTER;
  const roll = typeof rollFn === 'function' ? rollFn : () => 0.5;
  const tr = clampTier(tier);
  const n = T.DEALS[tr] || 2;
  const spanMs = Math.max(45, Number(budgetSec) || 90) * 1000;
  const usable = Math.max(15000, spanMs - T.FIRST_DEAL_MS - T.TAIL_MS);
  let times = [];
  for (let i = 0; i < n; i++) times.push(T.FIRST_DEAL_MS + roll('when') * usable);
  times.sort((a, b) => a - b);
  for (let i = 1; i < times.length; i++) if (times[i] - times[i - 1] < T.MIN_GAP_MS) times[i] = times[i - 1] + T.MIN_GAP_MS;
  /* the rolling-window cap: drop slots that would be a 5th within 60s */
  const kept = [];
  for (const at of times) {
    const inWindow = kept.filter((k) => at - k < T.RATE_WINDOW_MS).length;
    if (inWindow >= T.RATE_MAX) continue;
    if (at > spanMs - T.TAIL_MS) continue;
    kept.push(at);
  }
  times = kept;
  const clockSlot = tr >= T.CLOCK_FROM_TIER && times.length > 1 ? Math.floor(roll('clock-slot') * Math.ceil(times.length / 2)) : -1;
  const w = T.WEIGHTS[tr] || T.WEIGHTS[1];
  return times.map((at, i) => {
    let card;
    if (i === clockSlot) card = 'clock';
    else {
      const r = roll('card');
      card = r < w.flicker ? 'flicker' : r < w.flicker + w.melt ? 'melt' : 'chrome';
      if (card === 'chrome' && tr < T.CHROME_FROM_TIER) card = 'melt';
      if (card === 'melt' && tr < T.MELT_FROM_TIER) card = 'flicker';
    }
    return { at: Math.round(at), card };
  });
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

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (retakes replay)
 * @param {number}   o.tier        1..4
 * @param {Object}   o.timers      {after(ms,fn)->id, every?(ms,fn)->id, clear|cancel(id)}
 * @param {boolean}  o.reduced     reduced motion
 * @param {boolean|Function} o.capsOk  false when bgIntensity is capped to 0
 * @param {Function} o.isHalted    () => bool (dead/paused/ended/between rounds)
 * @param {Function=} o.t          ctx.lexicon (English fallbacks here)
 * @param {Function} o.stats       () => {round, secLeft, streak, rounds?} - the TRUTH
 * @param {Function} o.chipEl      (which: 'round'|'clock'|'streak') => element|null
 * @param {Function} o.chipText    (which) => the honest text (repaint source)
 * @param {Object}   o.stage       .g-an-stage (the overlay's host)
 * @param {Object=}  o.grid        .g-an-grid (READ: tile rects by data-i)
 * @param {Function=} o.tiles      () => live .g-an-tile elements (else queried off grid)
 * @param {Function=} o.meltCandidates () => HTMLElement[]  CORE's melt list (never the odd / eliminated)
 * @param {Function=} o.canMelt    (i) => bool   the predicate form of the same veto (either hook arms the melt)
 * @param {Function=} o.getStill   () => url|null (a pool still for the chrome flicker)
 * @param {Object=}   o.assets     {next(kind)} CORE's live pool reader (getStill fallback)
 * @param {Function=} o.chromeEls  () => HTMLElement[] (HUD chrome the flicker may wear; else the chips)
 * @param {Function=} o.announce   (text, ms) => void (the proctor line)
 * @param {Function=} o.cue        CORE's own clamped cue(name, level, extra) - THE DECK'S
 *                                 ONLY VOICE. No deck ever holds an audio node (House Book).
 * @param {number=}  o.budgetSec   class budget (default 90)
 * @param {boolean=} o.coarse      coarse pointer (no ghost)
 * @param {Function=} o.log
 */
export function createAnTrickster(o) {
  const opts = o || {};
  const T = AN_TRICKSTER;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const coarse = !!opts.coarse;
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const canMelt = typeof opts.canMelt === 'function' ? opts.canMelt : null;
  const meltCandidates = typeof opts.meltCandidates === 'function' ? opts.meltCandidates : null;
  const meltArmed = !!(meltCandidates || canMelt);
  const getStill = typeof opts.getStill === 'function' ? opts.getStill : () => {
    try {
      const a = opts.assets;
      if (!a || typeof a.next !== 'function') return null;
      const got = a.next('still') || a.next('loop');
      return got && got.url ? String(got.url) : null;
    } catch (e) { return null; }
  };
  const chromeEls = typeof opts.chromeEls === 'function' ? opts.chromeEls
    : () => ['round', 'clock', 'streak'].map((w) => chipEl(w)).filter(Boolean);
  const announce = typeof opts.announce === 'function' ? opts.announce : null;
  const budgetSec = Math.max(45, Number(opts.budgetSec) || 90);
  function capsOk() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false && opts.capsOk != null;
  }
  const armedBase = !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  let destroyed = false;
  const armed = () => armedBase && !destroyed && capsOk();
  /* THE DECK'S VOICE (W2). Three of this deck's cards were described in the
   * header as making a SOUND ("corrects itself with a static pop") and every
   * one of them was mute since Semester II. The road is CORE's own clamped
   * helper, handed down at the construction site - this file never touches an
   * audio node and never fires the engine itself (House Book: shell/audio.js
   * is the one audio owner). The gate deliberately excludes capsOk: a capped
   * bgIntensity is the player's VISUAL exit (Law VI), not an audio one. */
  const cueFn = typeof opts.cue === 'function' ? opts.cue : () => {};
  const sounds = () => armedBase && !destroyed;
  let cues = 0;
  function cue(name, level, extra) {
    if (!name || !sounds()) return;
    cues++;
    try { cueFn(name, level, extra || {}); } catch (e) { /* a refused cue is not an error */ }
  }

  /* timers: the game's registry + a local set */
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
  const chains = new Set();
  function every(ms, fn) {
    if (!armedBase || destroyed) return 0;
    if (typeof opts.timers.every === 'function') {
      const id = opts.timers.every(ms, () => { if (!destroyed) { try { fn(); } catch (e) { /* ignore */ } } });
      if (id) live.add(id);
      return id;
    }
    const handle = { id: 0, on: true };
    const tick = () => { if (!handle.on || destroyed) return; try { fn(); } catch (e) { /* ignore */ } handle.id = after(ms, tick); };
    handle.id = after(ms, tick);
    chains.add(handle);
    return handle;
  }
  function stopEvery(h) {
    if (!h) return;
    if (typeof h === 'object') { h.on = false; cancel(h.id); chains.delete(h); } else cancel(h);
  }

  const seedBase = String(opts.seed || 'an') + '|an-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let stopped = false;
  let paused = false;
  let started = false;
  const fired = { flicker: 0, clock: 0, chrome: 0, melt: 0, outline: 0, ghost: 0 };
  const folded = { melt: 0, chrome: 0, flicker: 0 };
  let deals = [];
  const fireTimes = [];
  const lexiconUsed = new Set();
  function tt(key, fallback) { lexiconUsed.add(key); return t(key, fallback); }

  /* ---- the overlay ------------------------------------------------------- */
  let layer = null;
  let stageEl = opts.stage || null;
  /** The stage: CORE's opts.stage, else the closest .g-an-stage above a chip
   *  or a tile (CORE hands this deck chips and tiles, not the stage). */
  function hostStage() {
    if (stageEl) return stageEl;
    const probes = [chipEl('clock'), chipEl('round'), chipEl('streak')];
    try { const l = tileList(); if (l.length) probes.push(l[0]); } catch (e) { /* none */ }
    for (const p of probes) {
      try { if (p && typeof p.closest === 'function') { const s = p.closest('.g-an-stage'); if (s) { stageEl = s; return s; } } } catch (e) { /* next */ }
    }
    return null;
  }
  function ensureLayer() {
    if (layer) return layer;
    const host = hostStage();
    if (!host || !host.appendChild) return null;
    ensureStyle();
    layer = el('div', 'g-an-tk');
    if (layer) host.appendChild(layer);
    return layer;
  }
  function tileList() {
    try {
      if (typeof opts.tiles === 'function') { const l = opts.tiles(); if (l && l.length) return Array.from(l); }
      if (opts.grid && opts.grid.querySelectorAll) return Array.from(opts.grid.querySelectorAll('.g-an-tile'));
      if (stageEl && stageEl.querySelectorAll) return Array.from(stageEl.querySelectorAll('.g-an-tile'));
    } catch (e) { /* fall */ }
    return [];
  }
  function tileByIndex(i) {
    const list = tileList();
    for (const n of list) {
      let di = null;
      try { di = n.getAttribute ? n.getAttribute('data-i') : null; } catch (e) { di = null; }
      if (di != null && String(di) === String(i)) return n;
    }
    const k = Number(i);
    return Number.isFinite(k) && k >= 0 && k < list.length ? list[k] : null;
  }
  /** A tile's rect in overlay space. */
  function tileRect(tileEl) {
    const r = rectOf(tileEl);
    const base = rectOf(layer);
    if (!r || !base || !r.width) return null;
    return { x: r.left - base.left, y: r.top - base.top, w: r.width, h: r.height, cx: r.left - base.left + r.width / 2, cy: r.top - base.top + r.height / 2 };
  }
  function placeBox(node, r, pad) {
    if (!node || !r) return false;
    node.style.left = (r.x - pad).toFixed(0) + 'px';
    node.style.top = (r.y - pad).toFixed(0) + 'px';
    node.style.width = (r.w + pad * 2).toFixed(0) + 'px';
    node.style.height = (r.h + pad * 2).toFixed(0) + 'px';
    return true;
  }
  function rateOk(now) {
    while (fireTimes.length && now - fireTimes[0] > T.RATE_WINDOW_MS) fireTimes.shift();
    if (fireTimes.length >= T.RATE_MAX) return false;
    if (fireTimes.length && now - fireTimes[fireTimes.length - 1] < T.MIN_GAP_MS) return false;
    return true;
  }
  function nowMs() { return Date.now(); }

  /* ---- the deal ----------------------------------------------------------- */
  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    if (isHalted() || paused || !stats()) {
      if (tries < T.RETRY_MAX) after(T.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (!rateOk(nowMs())) { if (tries < T.RETRY_MAX) after(T.RETRY_MS, () => attempt(deal, tries + 1)); return; }
    let went = false;
    if (deal.card === 'flicker') went = dealFlicker();
    else if (deal.card === 'clock') went = armClock();
    else if (deal.card === 'chrome') went = dealChrome();
    else if (deal.card === 'melt') went = dealMelt();
    if (went) fireTimes.push(nowMs());
  }

  /* -------------------------------------------------------- stat flicker */
  function dealFlicker() {
    const s = stats();
    if (!s) return false;
    const which = roll('flick-which') < 0.5 ? 'streak' : 'round';
    const chip = chipEl(which);
    if (!chip || chip.textContent == null) { folded.flicker++; return false; }
    const truth = chipText(which);
    const real = Number(which === 'streak' ? s.streak : s.round) || 0;
    const fake = Math.max(0, real + (roll('flick-sign') < 0.5 ? -1 : 1) * (1 + Math.floor(roll('flick-drop') * 2)));
    const lie = (typeof truth === 'string' && /\d/.test(truth)) ? truth.replace(/\d+/, String(fake)) : String(fake);
    if (!lie || lie === chip.textContent) { folded.flicker++; return false; }
    try { chip.textContent = lie; } catch (e) { return false; }
    if (!reduced) setCls(chip, 'g-an-tk-flick', true);
    fired.flicker++;
    after(T.FLICKER_MS, () => {
      /* THE STATIC POP: the self-correction is the beat, not the lie - the
       * number snapping back to the truth is what the header promised. */
      cue('decoy', 0.35);
      setCls(chip, 'g-an-tk-flick', false);
      const now = chipText(which);
      if (now != null) { try { chip.textContent = now; } catch (e) { /* ignore */ } }
    });
    say('trickster: stat flicker (' + which + ')');
    return true;
  }

  /* ------------------------------------------------------- crooked clock */
  let clockArmed = false;
  let clockObs = null;
  let clockPoll = 0;
  let lastLie = null;
  let budgetSeen = budgetSec;
  let clockLies = 0;
  function formatLike(sec, truth) {
    const s = Math.max(0, Math.round(sec));
    if (typeof truth === 'string' && /^\d+s$/.test(truth.trim())) return s + 's';
    if (typeof truth === 'string' && /^\d+$/.test(truth.trim())) return String(s);
    const m = Math.floor(s / 60);
    const r = s % 60;
    const padM = typeof truth === 'string' && /^\d{2}:\d{2}$/.test(truth.trim());
    return (padM ? String(m).padStart(2, '0') : String(m)) + ':' + String(r).padStart(2, '0');
  }
  function bendFace() {
    if (!clockArmed || destroyed || stopped) return;
    const chip = chipEl('clock');
    const s = stats();
    if (!chip || !s || !Number.isFinite(Number(s.secLeft))) return;
    const secLeft = Number(s.secLeft);
    if (secLeft > budgetSeen) budgetSeen = secLeft;
    const shown = bendClock(secLeft, budgetSeen, T.CLOCK_BEND, T.CLOCK_HONEST_SEC);
    if (shown === Math.round(secLeft)) { lastLie = null; return; }
    const truth = chipText('clock');
    const text = formatLike(shown, truth);
    if (chip.textContent === text) return;
    lastLie = text;
    try { chip.textContent = text; clockLies++; } catch (e) { /* ignore */ }
  }
  function armClock() {
    if (clockArmed || tier < T.CLOCK_FROM_TIER) return false;
    const chip = chipEl('clock');
    if (!chip) return false;
    clockArmed = true;
    fired.clock++;
    let hooked = false;
    try {
      if (typeof MutationObserver === 'function' && typeof chip.nodeType === 'number') {
        clockObs = new MutationObserver(() => { if (chip.textContent === lastLie) return; bendFace(); });
        clockObs.observe(chip, { childList: true, characterData: true, subtree: true });
        hooked = true;
      }
    } catch (e) { clockObs = null; }
    if (!hooked) clockPoll = every(T.CLOCK_POLL_MS, bendFace);
    bendFace();
    /* W3 P1-6: the room starts lying about time, and it says so ONCE - a
     * single tick a hair flat of the honest countdown's. armClock returns
     * early once armed, so this cannot repeat inside a class. */
    cue('clock_tick', 0.2, { pitch: 0.94 });
    say('trickster: the clock goes crooked (' + (hooked ? 'observer' : 'poll') + ')');
    return true;
  }
  function disarmClock(restore) {
    if (clockObs) { try { clockObs.disconnect(); } catch (e) { /* ignore */ } clockObs = null; }
    if (clockPoll) { stopEvery(clockPoll); clockPoll = 0; }
    if (clockArmed && restore) {
      const chip = chipEl('clock');
      const truth = chipText('clock');
      if (chip && truth != null) { try { chip.textContent = truth; } catch (e) { /* ignore */ } }
    }
    clockArmed = false;
    lastLie = null;
  }

  /* ------------------------------------------------------ glitch-to-asset */
  function dealChrome() {
    if (reduced || tier < T.CHROME_FROM_TIER) { folded.chrome++; return false; }
    let chrome = [];
    try { chrome = Array.from(chromeEls() || []).filter((n) => n && typeof n.getBoundingClientRect === 'function'); } catch (e) { chrome = []; }
    const host = ensureLayer();
    if (!chrome.length || !host) { folded.chrome++; return false; }
    const url = getStill();
    if (!url) { folded.chrome++; return false; }
    const target = chrome[Math.floor(roll('chrome-pick') * chrome.length)];
    const r = tileRect(target);
    if (!r) { folded.chrome++; return false; }
    const node = el('div', 'g-an-tk-chrome');
    if (!node || !placeBox(node, r, 0)) return false;
    const img = el('img');
    if (img) { img.alt = ''; try { img.setAttribute('draggable', 'false'); } catch (e) { /* noop */ } img.src = url; node.appendChild(img); }
    host.appendChild(node);
    fired.chrome++;
    /* one beat of someone else's memory over the chrome: a glitch, and the
     * same recipe CORE fires when the anomaly itself relocates */
    cue('glitch', 0.3);
    after(T.CHROME_MS, () => { try { node.remove(); } catch (e) { /* ignore */ } });
    if (announce && roll('seen') < T.SEEN_TAUNT_CHANCE) { try { announce(tt('an_trick_seen', 'Did you see that?'), 1600); } catch (e) { /* noop */ } }
    say('trickster: chrome flicker');
    return true;
  }

  /* ------------------------------------------------------------- the melt */
  let wax = null;
  let waxTimer = 0;
  let meltAnnounced = false;
  function dealMelt() {
    if (tier < T.MELT_FROM_TIER || wax) return false;
    if (!meltArmed) { folded.melt++; say('trickster: melt folded (no meltCandidates / canMelt from CORE)'); return false; }
    const host = ensureLayer();
    if (!host) { folded.melt++; return false; }
    let tile = null;
    let idx = -1;
    if (meltCandidates) {
      /* CORE's list: elements that are NOT the odd one and not eliminated */
      let list = [];
      try { list = Array.from(meltCandidates() || []).filter((n) => n && n.getBoundingClientRect); } catch (e) { list = []; }
      if (!list.length) { folded.melt++; return false; }
      tile = list[Math.floor(roll('melt-pick') * list.length)] || null;
      try { idx = tile && tile.getAttribute ? Number(tile.getAttribute('data-i')) : -1; } catch (e) { idx = -1; }
    } else {
      const list = tileList();
      if (!list.length) { folded.melt++; return false; }
      for (let k = 0; k < T.MELT_TRIES && !tile; k++) {
        const cand = list[Math.floor(roll('melt-pick') * list.length)];
        let i = null;
        try { i = cand && cand.getAttribute ? cand.getAttribute('data-i') : null; } catch (e) { i = null; }
        if (i == null) i = list.indexOf(cand);
        let ok = false;
        try { ok = !!canMelt(Number(i)); } catch (e) { ok = false; }
        if (ok) { tile = cand; idx = Number(i); }
      }
    }
    if (!tile) { folded.melt++; say('trickster: melt folded (veto)'); return false; }
    const r = tileRect(tile);
    if (!r) { folded.melt++; return false; }
    wax = el('div', 'g-an-tk-wax');
    if (!wax || !placeBox(wax, r, 0)) { wax = null; return false; }
    host.appendChild(wax);
    if (typeof wax.offsetWidth === 'number') void wax.offsetWidth;
    setCls(wax, 'on', true);
    fired.melt++;
    /* the wax sheet slides down the frame: a slide dropped a whole tone */
    cue('slide', 0.28, { pitch: 0.8 });
    /* THE FOLLOW: a melt dealt into a new-sheet transition measured a moving
       frame (rig 2026-08-23: the wax sat between two tiles). Six more reads of
       ONE tile over the melt's life keep the wax on the frame - reads, never
       writes, and never per-frame. */
    const waxNode = wax;
    for (const ms of T.MELT_FOLLOW_MS) {
      after(ms, () => { if (wax === waxNode) { const r2 = tileRect(tile); if (r2) placeBox(waxNode, r2, 0); } });
    }
    if (announce && !meltAnnounced && !reduced) {
      meltAnnounced = true;
      try { announce(tt('an_trick_melt', 'The frame runs like wax'), 2000); } catch (e) { /* noop */ }
    }
    cancel(waxTimer);
    waxTimer = after(T.MELT_MS, () => {
      if (!wax) return;
      setCls(wax, 'on', false);
      setCls(wax, 'reform', true);
      waxTimer = after(T.MELT_REFORM_MS + 40, () => unmelt());
    });
    say('trickster: melt (frame ' + idx + ')');
    return true;
  }
  function unmelt() {
    cancel(waxTimer); waxTimer = 0;
    if (wax) { try { wax.remove(); } catch (e) { /* ignore */ } }
    wax = null;
  }

  /* ------------------------------------------------------ ghost outline */
  let outline = null;
  let refund = null;
  let outlineTimer = 0;
  const outlined = new Set();          // tiles already outlined this round
  function ghostOutline(i) {
    const host = ensureLayer();
    if (!host || !armed()) return false;
    const tile = tileByIndex(i);
    const r = tile ? tileRect(tile) : null;
    if (!r) return false;
    if (!outline) { outline = el('i', 'g-an-tk-outline'); if (outline) host.appendChild(outline); }
    if (!refund) { refund = el('span', 'g-an-tk-refund'); if (refund) host.appendChild(refund); }
    if (!outline || !placeBox(outline, r, 2)) return false;
    setCls(outline, 'on', false);
    if (typeof outline.offsetWidth === 'number') void outline.offsetWidth;
    setCls(outline, 'on', true);
    if (refund) {
      refund.textContent = tt('an_refund', '+1s');
      setVar(refund, '--x', r.cx.toFixed(0) + 'px');
      setVar(refund, '--y', (r.y + 4).toFixed(0) + 'px');
      setCls(refund, 'on', false);
      if (typeof refund.offsetWidth === 'number') void refund.offsetWidth;
      setCls(refund, 'on', true);
    }
    fired.outline++;
    cancel(outlineTimer);
    outlineTimer = after(T.OUTLINE_MS + 60, () => { setCls(outline, 'on', false); setCls(refund, 'on', false); });
    say('trickster: ghost outline (frame ' + i + ')');
    return true;
  }

  /* ------------------------------------------------------------ the ghost */
  let ghost = null;
  let ghostTimer = 0;
  let onMove = null;
  const trail = [];
  let lastMoveAt = 0;
  let lure = null;
  let gx = 0, gy = 0;
  let ghostSeen = false;
  let stallMs = 0;
  function ghostEligible() {
    const host = hostStage();
    return armed() && !reduced && !coarse && tier >= T.GHOST_FROM_TIER && !!host
      && typeof host.addEventListener === 'function' && typeof host.getBoundingClientRect === 'function';
  }
  function pickLure() {
    /* a SEEDED frame - this deck cannot know the odd one, so the lure is noise
       by construction; never an eliminated frame (a dead lure is no lure) */
    const list = tileList().filter((n) => !(n.classList && (n.classList.contains('is-out') || n.classList.contains('is-cold'))));
    if (!list.length) return null;
    return list[Math.floor(roll('lure') * list.length)] || null;
  }
  function ghostTick() {
    if (destroyed || stopped || !ghost) return;
    if (isHalted() || paused || !lastMoveAt) { setCls(ghost, 'on', false); setCls(ghost, 'lure', false); return; }
    const now = nowMs();
    const stalled = (now - lastMoveAt > T.GHOST_STALL_MS) || stallMs >= T.GHOST_STALL_MS;
    if (!stalled) {
      lure = null;
      if (!trail.length) { setCls(ghost, 'on', false); setCls(ghost, 'lure', false); return; }
      let pt = trail[0];
      for (const p of trail) { if (now - p.at >= T.GHOST_TRAIL_MS) pt = p; else break; }
      gx = pt.x; gy = pt.y;
      setCls(ghost, 'on', true); setCls(ghost, 'lure', false);
    } else {
      if (!lure) {
        lure = pickLure();
        if (lure) {
          ghostSeen = true;
          fired.ghost++;
          /* W3 P1-8: ONE breath as the ghost takes a frame, never one per
           * ghostTick. Non-tonal on purpose - a pitched cue would read as a
           * hint about the frame it is lying about. */
          cue('whisper', 0.18, { pitch: 0.85 });
        }
      }
      const r = lure ? tileRect(lure) : null;
      if (r) { gx += (r.cx - gx) * T.GHOST_LERP; gy += (r.cy - gy) * T.GHOST_LERP; }
      setCls(ghost, 'on', true); setCls(ghost, 'lure', true);
    }
    try { ghost.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)'; } catch (e) { /* noop */ }
    while (trail.length > 1 && now - trail[0].at > T.GHOST_KEEP_MS) trail.shift();
  }
  function armGhost() {
    if (!ghostEligible() || ghost) return;
    const host = ensureLayer();
    if (!host) return;
    ghost = el('div', 'g-an-tk-ghost');
    if (!ghost) return;
    host.appendChild(ghost);
    onMove = (e) => {
      if (destroyed) return;
      const base = rectOf(layer);
      if (!base) return;
      const x = Number(e.clientX) - base.left;
      const y = Number(e.clientY) - base.top;
      if (!Number.isFinite(x) || !Number.isFinite(y)) return;
      lastMoveAt = nowMs();
      trail.push({ x, y, at: lastMoveAt });
      lure = null;
    };
    /* a PASSIVE listener on the stage: it reads, it never preventDefaults,
       it never stops propagation - the grid's own handlers see every event */
    try { stageEl.addEventListener('pointermove', onMove, { passive: true }); } catch (e) { try { stageEl.addEventListener('pointermove', onMove); } catch (e2) { onMove = null; } }
    ghostTimer = every(T.GHOST_TICK_MS, ghostTick);
    say('trickster: ghost armed');
  }
  function unghost() { if (ghost) { setCls(ghost, 'on', false); setCls(ghost, 'lure', false); } lure = null; }

  /* ---------------------------------------------------------------- api */
  return {
    /** Deal the class. Call once, when the first round is dealt. */
    start() {
      if (started) return;
      started = true;
      if (!armed()) { say('trickster: disarmed'); return; }
      ensureLayer();
      deals = buildDealPlan(tier, budgetSec, roll);
      for (const deal of deals) after(deal.at, () => attempt(deal, 0));
      try { const s = stats(); if (s && Number(s.secLeft) > budgetSeen) budgetSeen = Number(s.secLeft); } catch (e) { /* ignore */ }
      armGhost();
      say('trickster: dealt ' + deals.length + ' cards (' + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')'
        + (meltArmed ? '' : ' - melt DISARMED (no meltCandidates / canMelt)'));
    },
    /** A new round: every lie on the old sheet comes off. */
    afterRound() {
      if (!armed()) return;
      unmelt();
      unghost();
      outlined.clear();
      stallMs = 0;
      setCls(outline, 'on', false); setCls(refund, 'on', false);
    },
    /** Every tap, after CORE's accounting. CORE marks a tap that landed where
     *  the anomaly WAS with .is-ghost on THAT tile (a tile it has LEFT, never
     *  the live one) - the outline + the +1s pip bloom on it. An explicit
     *  {i, moved:true} does the same. */
    afterTap(info) {
      if (!armed()) return;
      const e = info || {};
      unghost();
      stallMs = 0;
      lastMoveAt = nowMs();
      if (e.moved === true || e.ghost === true || e.reason === 'moved') { ghostOutline(e.i); return; }
      for (const tile of tileList()) {
        let g = false;
        try { g = !!(tile.classList && tile.classList.contains('is-ghost')); } catch (err) { g = false; }
        if (!g || outlined.has(tile)) continue;
        outlined.add(tile);
        let i = null;
        try { i = tile.getAttribute ? tile.getAttribute('data-i') : null; } catch (err) { i = null; }
        ghostOutline(i == null ? tileList().indexOf(tile) : i);
        break;
      }
    },
    /** CORE may call this directly for the refund staging. */
    ghostOutline(i) { if (!armed()) return false; return ghostOutline(i); },
    /** CORE calls every ~500ms with ms since the last tap; 0 resets. */
    stalled(ms) {
      if (!armed() || stopped || paused) return;
      stallMs = Math.max(0, Number(ms) || 0);
      if (stallMs <= 0) unghost();
    },
    /** CORE's pause: the deals ride CORE's pause-aware timers, so this only
     *  hides the live lies (a melt mid-pause would read as a frozen frame). */
    pause() {
      if (paused) return;
      paused = true;
      unghost();
      setCls(outline, 'on', false); setCls(refund, 'on', false);
    },
    resume() {
      if (!paused) return;
      paused = false;
      stallMs = 0;
      lastMoveAt = nowMs();
    },
    stop() {
      stopped = true;
      unmelt();
      unghost();
      disarmClock(true);
      setCls(outline, 'on', false); setCls(refund, 'on', false);
    },
    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
      if (onMove && stageEl && stageEl.removeEventListener) { try { stageEl.removeEventListener('pointermove', onMove); } catch (e) { /* ignore */ } }
      onMove = null;
      unmelt();
      disarmClock(true);
      if (layer) { try { layer.remove(); } catch (e) { /* ignore */ } }
      layer = null; ghost = null; outline = null; refund = null;
    },
    diagnostics() {
      return {
        armed: armed(), sounds: sounds(), cues, tier, deals: deals.slice(), fired: Object.assign({}, fired), folded: Object.assign({}, folded),
        melted: !!wax, ghost: !!ghost, ghostSeen, meltVeto: meltArmed, stage: !!stageEl,
        clock: { armed: clockArmed, lies: clockLies, budget: budgetSeen, observer: !!clockObs },
        lexiconUsed: Array.from(lexiconUsed), lexicon: AN_TRICKSTER_LEX.slice(), layer: !!layer, liveTimers: live.size,
      };
    },
  };
}

export default createAnTrickster;
