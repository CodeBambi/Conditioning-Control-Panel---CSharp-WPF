/* ============================================================================
 * games/composure/trickster.js - DECK III of the House Rules: the cards the
 * floor map deals the studio. A sliding picture is where your eyes stop being
 * reliable witnesses of where things ARE, so every card here attacks the
 * mental model, never the board.
 *
 *   THE FALSE PREVIEW (native, the signature) a glitch_swap-style shudder -
 *                   rgbsplit / vhsroll / datamosh, seeded - paints GHOSTS of
 *                   real tiles in WRONG slots on the .g-cp-preview lie layer
 *                   (cloned faces, --pr/--pc, pointer-events:none) for half a
 *                   second, then they dissolve and the truth is exactly where
 *                   it always was. A dealt preview ARMS and plays on the next
 *                   afterSlide() or washOn(true) - the lie rides the move or
 *                   hides under the wash - and folds into playing on its own
 *                   if neither comes. TIER 4: one deal per class is THE FAKE
 *                   SOLVED BOARD - every loose tile ghosted at its HOME, seams
 *                   evaporating, a sweep of light - and it was a lie. The
 *                   engine's own glitch_swap is fired ON THE PREVIEW LAYER for
 *                   its sfx + dressing (targets = the lie layer, never a tile);
 *                   the ghosts are CSS. Truth --r/--c are READ, never written.
 *                   Reduced motion: a plain crossfade ghost at half presence,
 *                   no shudder (style.js .is-plain).
 *   STAT FLICKER    the moves chip reads a few off, then corrects itself with
 *                   a static pop. The ledger never moves; confidence does.
 *   CROOKED CLOCK   (timed, tier 2+) the clock FACE bends: it races through
 *                   the boring middle and crawls as the bell nears, honest for
 *                   the last 20s. The real budget is index.js's and exact. A
 *                   MutationObserver re-bends the face the moment truth lands
 *                   (the Deep End posture); no observer -> a 250ms poll.
 *   THE MELT        stall >= 3s and one LOOSE tile visibly drips (.g-cp-melt;
 *                   the body and the face deform, never the transform). Snaps
 *                   back on the next move. Nothing is lost.
 *   GHOST CURSOR    stall >= 4.5s (tier 2+) and a faint hand suggests the
 *                   WORST slide: of the tiles beside the gap, the one whose
 *                   move pulls a LOCKED piece out of its home, else the one
 *                   that lands farthest from its own. Pure decoration in the
 *                   preview layer; it cannot input; it dies on the next move.
 *
 * DEALING RULE: previews, flickers and the clock are dealt by the seeded plan
 * (budget 2/4/6/8 per tier, never more than ~6 a minute, never in the first
 * 10s or the last 20s of a timed class; zen halves the budget, drops the
 * clock and cycles the plan); a deal whose moment is wrong (halted) re-queues
 * politely, then folds. The melt and the ghost are stall-reactive - the stall
 * is the player's own - but WHICH tile melts and WHICH worst slide the ghost
 * takes are the seed's choice.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the flicker / the clock write chip TEXT and restore
 *       via chipText (the truth source); the preview writes only its own
 *       ghosts (--pr/--pc on nodes it owns); nothing reads or writes moves,
 *       locks, the clock budget or the board.
 *   II  input honest  - no card is clickable; every node lives in the
 *       pointer-events:none preview layer; a tile's --r/--c, data-home and
 *       data-id are read, never written.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|cp-trickster|'.
 *       A retake replays the identical deal.
 *   VI  exits sacred  - bgIntensity 0 disarms the deck; reduced motion makes
 *       the preview a plain crossfade ghost, drops the flicker pop, the melt's
 *       drip and the ghost cursor (the clock still bends: a number is not
 *       motion); every timer lives in the game's registry AND a local set, so
 *       destroy() cannot leak one.
 *   VII strings      - cp_trick_preview / cp_trick_seen / cp_trick_melt (rows
 *       CORE ships in lex.js) through the t() this deck is handed. This file
 *       invents no text.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const CP_TRICKSTER = Object.freeze({
  /** Dealt card events per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  MIN_GAP_MS: 9000,
  FIRST_DEAL_MS: 10000,
  TAIL_MS: 20000,
  BUDGET_MS: 120000,
  RETRY_MS: 2000,
  RETRY_MAX: 8,
  /** Card weights by tier (preview / flicker; the clock is one slot). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ preview: 0.65, flicker: 0.35 }),
    2: Object.freeze({ preview: 0.6, flicker: 0.4 }),
    3: Object.freeze({ preview: 0.62, flicker: 0.38 }),
    4: Object.freeze({ preview: 0.66, flicker: 0.34 }),
  }),
  CLOCK_FROM_TIER: 2,
  SOLVED_FROM_TIER: 4,
  GHOST_FROM_TIER: 2,
  MELT_FROM_TIER: 1,
  /** The false preview. */
  PREVIEW_MS: Object.freeze({ 1: 420, 2: 480, 3: 560, 4: 640 }),
  PREVIEW_TILES: Object.freeze({ 1: 2, 2: 3, 3: 3, 4: 4 }),
  PREVIEW_FOLD_MS: 8000,
  PREVIEW_FADE_MS: 240,
  PREVIEW_ALPHA: 0.82,
  SOLVED_MS: 720,
  SOLVED_MIN_FRAC: 0.4,
  SOLVED_FOLD_MS: 14000,
  VARIANTS: Object.freeze(['rgb', 'vhs', 'mosh']),
  ENGINE_VARIANT: Object.freeze({ rgb: 'rgbsplit', vhs: 'vhsroll', mosh: 'datamosh' }),
  TAUNT_CHANCE: 0.3,
  /** Stat flicker. */
  FLICKER_MS: 450,
  /** Crooked clock. */
  CLOCK_BEND: 0.12,
  CLOCK_HONEST_SEC: 20,
  CLOCK_RAMP: 0.35,
  CLOCK_POLL_MS: 250,
  /** The melt / the ghost: stall thresholds. */
  MELT_STALL_MS: 3000,
  GHOST_STALL_MS: 4500,
  /** Zen: half the deals, the plan cycles, no clock card. */
  ZEN_BUDGET_MUL: 0.5,
});

const STYLE_ID = 'g-cp-trickster-style';
const STYLE_TEXT = `
/* ---- THE LIE LAYER (trickster) --------------------------------------------
   Over the board, never a pointer. Ghost tiles (.g-cp-pv) carry --pr/--pc and
   a cloned face; the layer's variant class is the engine's glitch look redrawn
   here (rgbsplit / vhsroll / datamosh), .is-plain is the reduced-motion
   crossfade ghost. Truth tiles are never touched. */
.g-cp-preview{position:absolute;inset:0;z-index:4;pointer-events:none;overflow:visible;
  --cp-tile:calc((var(--cp-board) - (var(--cp-n,3) - 1) * var(--cp-gap)) / var(--cp-n,3));
  --cp-step:calc(var(--cp-tile) + var(--cp-gap))}
.g-cp-frame > .g-cp-preview{inset:calc(var(--cp-mat) + var(--cp-wood))}
.g-cp-preview *{pointer-events:none}
.g-cp-pv{position:absolute;left:0;top:0;width:var(--cp-tile);height:var(--cp-tile);opacity:0;
  transform:translate3d(calc(var(--pc,0) * var(--cp-step)), calc(var(--pr,0) * var(--cp-step)), 0);
  transition:opacity .18s ease;will-change:opacity,transform;overflow:hidden;border-radius:2px;
  background:color-mix(in srgb, var(--panel), black 20%);
  box-shadow:0 2px 6px rgba(0,0,0,.5), inset 0 1px 0 rgba(255,255,255,.14)}
/* a ghost is pictorial: its cloned numeral is hidden so the lie never doubles a number */
.g-cp-pv .g-cp-num{visibility:hidden}
.g-cp-pv .g-cp-face{inset:0;border-radius:2px}
.g-cp-preview.is-on .g-cp-pv{opacity:var(--cp-pv-a,.82)}
.g-cp-preview.is-on.g-cp-pv-rgb .g-cp-pv{animation:g-cp-pvrgb var(--cp-pv-ms,520ms) steps(2,end) 1;
  filter:contrast(1.15);box-shadow:-3px 0 rgba(255,0,80,.7),3px 0 rgba(0,255,220,.7)}
@keyframes g-cp-pvrgb{0%,100%{transform:translate3d(calc(var(--pc,0) * var(--cp-step)), calc(var(--pr,0) * var(--cp-step)), 0)}
  25%{transform:translate3d(calc(var(--pc,0) * var(--cp-step) - 2px), calc(var(--pr,0) * var(--cp-step) + 1px), 0)}
  50%{transform:translate3d(calc(var(--pc,0) * var(--cp-step) + 2px), calc(var(--pr,0) * var(--cp-step) - 1px), 0)}
  75%{transform:translate3d(calc(var(--pc,0) * var(--cp-step) - 1px), calc(var(--pr,0) * var(--cp-step) - 2px), 0)}}
.g-cp-preview.is-on.g-cp-pv-vhs{animation:g-cp-pvvhs var(--cp-pv-ms,520ms) linear 1}
@keyframes g-cp-pvvhs{0%{clip-path:inset(0 0 0 0)}30%{clip-path:inset(18% 0 42% 0);transform:translateX(6px)}
  60%{clip-path:inset(52% 0 12% 0);transform:translateX(-5px)}100%{clip-path:inset(0 0 0 0);transform:none}}
.g-cp-preview.is-on.g-cp-pv-mosh .g-cp-pv{animation:g-cp-pvmosh var(--cp-pv-ms,520ms) steps(3,end) 1}
@keyframes g-cp-pvmosh{0%{filter:saturate(1.6) hue-rotate(0)}50%{filter:saturate(2.2) hue-rotate(40deg) blur(1px)}100%{filter:none}}
/* the fake-solved board: every ghost at home, the seams evaporating, a sweep */
.g-cp-preview.g-cp-pv-solved .g-cp-pv{border-radius:0;box-shadow:none;width:calc(var(--cp-tile) + var(--cp-gap));height:calc(var(--cp-tile) + var(--cp-gap))}
.g-cp-preview.g-cp-pv-solved::after{content:"";position:absolute;inset:0;opacity:0;
  background:linear-gradient(115deg, transparent 30%, rgba(255,255,255,.22) 50%, transparent 70%);background-size:300% 100%}
.g-cp-preview.is-on.g-cp-pv-solved::after{animation:g-cp-pvsweep var(--cp-pv-ms,520ms) ease-out 1}
@keyframes g-cp-pvsweep{0%{opacity:.9;background-position:140% 0}100%{opacity:0;background-position:-40% 0}}
/* reduced: a plain crossfade ghost, half presence, no shudder */
.g-cp-preview.is-plain .g-cp-pv{transition:opacity .35s ease}
.g-cp-preview.is-plain.is-on .g-cp-pv{opacity:calc(var(--cp-pv-a,.82) * .55);animation:none;filter:none;box-shadow:none}
.g-cp-preview.is-plain{animation:none !important}
/* GHOST CURSOR: a faint hand suggesting the worst slide - from the tile's
   cell (--gr0/--gc0) toward the gap (--gr1/--gc1), over and over until the
   player moves. Grid units, so it lands headless too. It cannot input. */
.g-cp-ghost{position:absolute;width:18px;height:18px;border-radius:50%;opacity:0;
  left:calc((var(--gc0,0) + .5) * var(--cp-step) - 9px);top:calc((var(--gr0,0) + .5) * var(--cp-step) - 9px);
  --gdx:calc((var(--gc1,0) - var(--gc0,0)) * var(--cp-step));
  --gdy:calc((var(--gr1,0) - var(--gr0,0)) * var(--cp-step));
  background:radial-gradient(circle, rgba(255,255,255,.9), color-mix(in srgb, var(--lav), transparent 30%) 50%, transparent 72%);
  box-shadow:0 0 14px color-mix(in srgb, var(--lav), transparent 40%);
  animation:g-cp-ghost 1.6s ease-in-out infinite}
.g-cp-ghost::before{content:"";position:absolute;left:50%;top:50%;width:calc(var(--cp-step) * .5);height:3px;
  margin-top:-1.5px;transform-origin:0 50%;transform:rotate(calc(var(--ga,0) * 1deg + 180deg));
  background:linear-gradient(90deg, color-mix(in srgb, var(--lav), transparent 25%), transparent);border-radius:2px}
.g-cp-ghost::after{content:"";position:absolute;left:50%;top:50%;width:10px;height:10px;margin:-5px 0 0 -5px;
  border-right:2px solid var(--ink);border-top:2px solid var(--ink);opacity:.8;
  transform:rotate(calc(var(--ga,0) * 1deg - 45deg))}
@keyframes g-cp-ghost{
  0%{opacity:0;transform:translate3d(0,0,0)}
  20%{opacity:.75}
  70%{opacity:.6}
  100%{opacity:0;transform:translate3d(var(--gdx,0px), var(--gdy,0px), 0)}}

/* ---- TRICKSTER (Deck III) dressing --------------------------------------- */
/* STAT FLICKER: one beat of chromatic static on the lying chip */
.g-cp-chip.g-cp-statlie{color:var(--lav);text-shadow:-1.5px 0 var(--pink),1.5px 0 #6EE8E0;
  animation:g-cp-statlie .45s steps(3) 1}
@keyframes g-cp-statlie{0%{opacity:1}50%{opacity:.55}100%{opacity:1}}
/* CROOKED CLOCK: no class at all - a number is not motion */
/* THE MELT: a loose tile sags and drips while the player stalls; the body and
   the face deform (transform-origin bottom), a drip forms under it. Snaps back
   on the next move (class removed, the .2s transition). */
.g-cp-tile.g-cp-melt{z-index:3}
.g-cp-tile.g-cp-melt .g-cp-face,.g-cp-tile.g-cp-melt::before{transform-origin:50% 100%;
  animation:g-cp-meltbody 2.4s ease-in-out infinite alternate}
.g-cp-tile.g-cp-melt .g-cp-face{filter:saturate(.85) brightness(.95)}
@keyframes g-cp-meltbody{from{transform:scaleY(1) skewX(0)}to{transform:scaleY(1.08) skewX(-3deg) translateY(3%);
  border-radius:2px 2px 12px 14px}}
.g-cp-tile.g-cp-melt::after{opacity:.85;border:0;inset:auto auto -1.1em 50%;width:.45em;height:1.4em;margin-left:-.2em;
  font-size:clamp(8px, calc(var(--cp-tile) * .16), 22px);border-radius:40% 40% 50% 50%;
  background:hsla(var(--cp-hue-b),50%,60%,.8);animation:g-cp-drip 2.4s ease-in infinite}
@keyframes g-cp-drip{0%{transform:scaleY(.2) translateY(0);opacity:0}40%{opacity:.85}100%{transform:scaleY(1.3) translateY(120%);opacity:0}}
/* reduced motion (both gates) */
html.arc-reduced .g-cp-ghost{opacity:0}
html.arc-reduced .g-cp-tile.g-cp-melt .g-cp-face{transform:scaleY(1.03);filter:saturate(.85)}
html.arc-reduced .g-cp-tile.g-cp-melt::after{opacity:0}
html.arc-reduced .g-cp-preview .g-cp-pv{animation:none !important;filter:none;box-shadow:none}
@media (prefers-reduced-motion: reduce){
  .g-cp-ghost{opacity:0}
  .g-cp-tile.g-cp-melt .g-cp-face{transform:scaleY(1.03);filter:saturate(.85)}
  .g-cp-tile.g-cp-melt::after{opacity:0}
  .g-cp-preview .g-cp-pv{animation:none !important;filter:none;box-shadow:none}
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

function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }
function hasClass(node, cls) {
  try { return !!(node && node.classList && node.classList.contains(cls)); } catch (e) { return false; }
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
function idOf(tile) { try { return tile.getAttribute('data-id'); } catch (e) { return null; } }
function dist(a, b) { return Math.abs(a.r - b.r) + Math.abs(a.c - b.c); }

/**
 * The crooked face, pure: displayed seconds-left for a real seconds-left.
 * f(B)=B, f(honest)=honest, monotonic (bend*pi < 1), honest at/after the
 * bell. Ahead through the middle (less time shown than real), crawling as
 * the bell nears, so the face meets the truth instead of snapping to it.
 */
export function bendClock(secLeft, budgetSec, bend, honestSec) {
  const x = Math.max(0, Number(secLeft) || 0);
  const B = Math.max(30, Number(budgetSec) || 120);
  const H = Number.isFinite(honestSec) ? honestSec : CP_TRICKSTER.CLOCK_HONEST_SEC;
  if (x <= H || x >= B) return Math.round(x);
  const A0 = Number.isFinite(bend) ? bend : CP_TRICKSTER.CLOCK_BEND;
  const u = x / B;
  const uh = H / B;
  const ramp = Math.max(0, Math.min(1, (u - uh) / CP_TRICKSTER.CLOCK_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, Math.round(B * (u - A * Math.sin(Math.PI * u))));
}

/**
 * The house-preferred WORST slide, pure. `tiles` = [{id, r, c, home:{r,c},
 * locked}], `blank` = {r,c}. Returns {id, from:{r,c}, to:{r,c}, score} for the
 * tile beside the gap whose move hurts most (a locked piece leaving home is
 * worst; else the biggest growth in distance-from-home), or null. `pick` is a
 * 0..1 roll that breaks ties deterministically.
 */
export function worstSlide(tiles, blank, pick) {
  if (!blank || !Array.isArray(tiles)) return null;
  const cands = [];
  for (const tl of tiles) {
    if (!tl || !tl.home) continue;
    if (dist(tl, blank) !== 1) continue;
    const d0 = dist(tl, tl.home);
    const d1 = dist(blank, tl.home);
    const score = (tl.locked ? 10 : 0) + (d1 - d0);
    cands.push({ id: tl.id, from: { r: tl.r, c: tl.c }, to: { r: blank.r, c: blank.c }, score });
  }
  if (!cands.length) return null;
  let best = -Infinity;
  for (const c of cands) best = Math.max(best, c.score);
  const top = cands.filter((c) => c.score === best);
  const ix = Math.min(top.length - 1, Math.floor((Number(pick) || 0) * top.length));
  return top[ix];
}

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (retakes replay)
 * @param {number}   o.tier        1..4
 * @param {Object}   o.timers      {after(ms,fn)->id, every?(ms,fn)->id, clear|cancel(id)}
 * @param {boolean}  o.reduced     reduced motion
 * @param {boolean}  o.capsOk      false when bgIntensity is capped to 0
 * @param {Function} o.isHalted    () => bool (dead/paused/ended/busy)
 * @param {Function} o.stats       () => {moves, locked, n, secLeft, budgetSec?, mode?, lockedFrac?} - the TRUTH
 * @param {Function} o.chipEl      (which: 'moves'|'clock'|'locked'|'calm') => element|null
 * @param {Function} o.chipText    (which) => the honest text (repaint source)
 * @param {Object|Function} o.preview  the .g-cp-preview lie layer (or () => it)
 * @param {Function} o.tiles       () => HTMLElement[] (live tiles)
 * @param {Function=} o.announce   (text, ms) => void (optional proctor line)
 * @param {Object=}  o.engine      CORE's deckEngine (fire used for glitch_swap on the lie layer)
 * @param {Function=} o.t          ctx.lexicon (optional; English fallbacks here)
 * @param {number=}  o.budgetSec   class budget (else stats().budgetSec, else 120)
 * @param {string=}  o.mode        'timed'|'zen' (else stats().mode, else 'timed')
 * @param {Function=} o.log
 */
export function createCpTrickster(o) {
  const opts = o || {};
  const T = CP_TRICKSTER;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const armed = !!opts.capsOk && !!opts.timers && typeof opts.timers.after === 'function'
    && typeof document !== 'undefined';
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const tilesOf = typeof opts.tiles === 'function' ? opts.tiles : () => [];
  const announce = typeof opts.announce === 'function' ? opts.announce : null;
  const eng = opts.engine || null;
  function previewEl() {
    try { return typeof opts.preview === 'function' ? opts.preview() : opts.preview || null; } catch (e) { return null; }
  }
  function modeNow() {
    if (opts.mode === 'zen' || opts.mode === 'timed') return opts.mode;
    try { const s = stats(); if (s && (s.mode === 'zen' || s.mode === 'timed')) return s.mode; } catch (e) { /* ignore */ }
    // index.js's stats() carries no mode: read the stage's data-mode through the lie layer's ancestry
    try {
      let el = previewEl();
      for (let i = 0; el && i < 6; i++) {
        const m = typeof el.getAttribute === 'function' ? el.getAttribute('data-mode') : null;
        if (m === 'zen' || m === 'timed') return m;
        el = el.parentNode || null;
      }
    } catch (e) { /* ignore */ }
    return 'timed';
  }

  /* timers: the game's registry + a local set. */
  const live = new Set();
  const chains = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  function after(ms, fn) {
    if (!armed) return 0;
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
  function every(ms, fn) {
    if (!armed) return 0;
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

  const seedBase = String(opts.seed || 'cp') + '|cp-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let stopped = false;
  let started = false;
  const fired = { preview: 0, solved: 0, flicker: 0, clock: 0, melt: 0, ghost: 0 };
  let deals = [];
  let cycle = 0;
  let solvedPlayed = false;
  let washIsOn = false;
  const lexiconUsed = new Set();
  function say_t(key, fallback) { lexiconUsed.add(key); return t(key, fallback); }

  /* ------------------------------------------------------------- the deal */
  function budgetMs() {
    let b = Number(opts.budgetSec) || 0;
    if (!b) { try { const s = stats(); b = Number(s && s.budgetSec) || 0; } catch (e) { b = 0; } }
    return Math.max(40000, (b || 120) * 1000);
  }
  function buildDeals(cyc) {
    const zen = modeNow() === 'zen';
    let n = T.DEALS[tier] || 2;
    if (zen) n = Math.max(1, Math.ceil(n * T.ZEN_BUDGET_MUL));
    const span = zen ? T.BUDGET_MS : budgetMs();
    const usable = Math.max(20000, span - T.FIRST_DEAL_MS - T.TAIL_MS);
    const base = cyc * T.BUDGET_MS;
    const times = [];
    for (let i = 0; i < n; i++) times.push(T.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < T.MIN_GAP_MS) times[i] = times[i - 1] + T.MIN_GAP_MS;
    }
    const clockSlot = (!zen && cyc === 0 && tier >= T.CLOCK_FROM_TIER && n > 1)
      ? Math.floor(roll('clock-slot') * Math.ceil(n / 2)) : -1;
    const w = T.WEIGHTS[tier] || T.WEIGHTS[1];
    const out = times.map((at, i) => {
      let card;
      if (i === clockSlot) card = 'clock';
      else card = roll('card') < w.preview ? 'preview' : 'flicker';
      return { at: Math.round(base + at), card };
    });
    // tier 4: the LAST preview of the first cycle is the fake-solved board
    if (tier >= T.SOLVED_FROM_TIER && cyc === 0) {
      for (let i = out.length - 1; i >= 0; i--) { if (out[i].card === 'preview') { out[i].card = 'solved'; break; } }
    }
    return out;
  }
  function scheduleDeals(list) {
    const base = cycle * T.BUDGET_MS;
    for (const deal of list) after(Math.max(0, deal.at - base), () => attempt(deal, 0));
  }
  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    if (deal.card === 'preview') { armPreview('wrong'); return; }
    if (deal.card === 'solved') { armPreview('solved'); return; }
    if (isHalted() || !stats()) {
      if (tries < T.RETRY_MAX) after(T.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (deal.card === 'flicker') dealFlicker();
    else if (deal.card === 'clock') armClock();
  }

  /* --------------------------------------------------------- the tiles */
  function liveTiles() {
    let list = [];
    try { list = Array.from(tilesOf() || []); } catch (e) { list = []; }
    return list.filter((n) => n && n.style);
  }
  function gridN(list) {
    try { const s = stats(); if (s && Number(s.n) >= 3) return Math.round(Number(s.n)); } catch (e) { /* ignore */ }
    const k = (list || liveTiles()).length + 1;
    const n = Math.round(Math.sqrt(k));
    return n >= 3 ? n : 3;
  }
  function model() {
    const list = liveTiles();
    const n = gridN(list);
    const tiles = [];
    const occ = new Set();
    for (const el of list) {
      const g = gridOf(el);
      const home = homeOf(el, n);
      if (!g) continue;
      occ.add(g.r * n + g.c);
      tiles.push({ el, id: idOf(el), r: g.r, c: g.c, home, locked: hasClass(el, 'is-locked') });
    }
    let blank = null;
    for (let p = 0; p < n * n; p++) { if (!occ.has(p)) { blank = { r: Math.floor(p / n), c: p % n }; break; } }
    return { n, tiles, blank };
  }
  function lockedFrac(m) {
    try { const s = stats(); if (s && Number.isFinite(Number(s.lockedFrac))) return Number(s.lockedFrac); } catch (e) { /* ignore */ }
    const mm = m || model();
    const total = Math.max(1, mm.n * mm.n - 1);
    let k = 0;
    for (const tl of mm.tiles) if (tl.locked) k += 1;
    return k / total;
  }

  /* -------------------------------------------------- the false preview */
  let previewArmed = null;          // 'wrong' | 'solved' | null
  let previewFold = 0;
  let previewOn = false;
  let previewTimer = 0;
  let previewClear = 0;
  let ghosts = [];
  let previewVariant = null;

  function armPreview(kind) {
    if (previewArmed === 'solved') return;         // the big one keeps its slot
    previewArmed = kind;
    if (previewFold) cancel(previewFold);
    const fold = kind === 'solved' ? T.SOLVED_FOLD_MS : T.PREVIEW_FOLD_MS;
    previewFold = after(fold, () => {
      previewFold = 0;
      // nobody moved, no wash came: the lie plays on its own
      if (previewArmed && !isHalted()) playPreview();
    });
    say('trickster: preview armed (' + kind + ')');
  }
  function cloneFace(tile) {
    let face = null;
    try { face = tile.querySelector ? tile.querySelector('.g-cp-face') : null; } catch (e) { face = null; }
    if (!face || typeof face.cloneNode !== 'function') return null;
    let copy = null;
    try { copy = face.cloneNode(true); } catch (e) { return null; }
    // a VIDEO face never gets a second decoder (the 30Hz trap): the ghost is a plate
    try {
      const vids = copy.querySelectorAll ? copy.querySelectorAll('video') : [];
      for (const v of Array.from(vids || [])) { try { v.remove(); } catch (e) { /* ignore */ } }
    } catch (e) { /* ignore */ }
    return copy;
  }
  function clearGhosts() {
    for (const g of ghosts) { try { g.remove(); } catch (e) { /* ignore */ } }
    ghosts = [];
    const pv = previewEl();
    if (pv && pv.classList) {
      pv.classList.remove('is-on', 'is-plain', 'g-cp-pv-solved', 'g-cp-pv-rgb', 'g-cp-pv-vhs', 'g-cp-pv-mosh');
    }
    previewOn = false;
  }
  function playPreview() {
    const kind = previewArmed;
    previewArmed = null;
    if (previewFold) { cancel(previewFold); previewFold = 0; }
    if (!kind || destroyed || stopped || previewOn) return false;
    const pv = previewEl();
    if (!pv || !pv.appendChild) return false;
    const m = model();
    if (!m.tiles.length) return false;
    let solved = kind === 'solved' && !solvedPlayed && lockedFrac(m) >= T.SOLVED_MIN_FRAC;
    if (kind === 'solved' && !solved && solvedPlayed) return false;
    // stage the ghosts: {tile, r, c}
    const plan = [];
    if (solved) {
      for (const tl of m.tiles) if (!tl.locked && tl.home) plan.push({ tl, r: tl.home.r, c: tl.home.c });
      if (!plan.length) return false;
    } else {
      const loose = m.tiles.filter((tl) => !tl.locked);
      const pool = loose.length >= 2 ? loose : m.tiles.slice();
      if (pool.length < 2) return false;
      const k = Math.min(pool.length, T.PREVIEW_TILES[tier] || 2);
      // seeded pick of k tiles, then a cyclic shift of their slots (a derangement)
      const picked = [];
      const bag = pool.slice();
      for (let i = 0; i < k; i++) {
        const ix = Math.min(bag.length - 1, Math.floor(roll('pv-pick') * bag.length));
        picked.push(bag.splice(ix, 1)[0]);
      }
      for (let i = 0; i < picked.length; i++) {
        const dst = picked[(i + 1) % picked.length];
        plan.push({ tl: picked[i], r: dst.r, c: dst.c });
      }
      // sometimes the blank is the lie: one more ghost sits in the gap
      if (m.blank && roll('pv-blank') < 0.5) {
        const extra = loose.find((tl) => picked.indexOf(tl) < 0) || null;
        if (extra) plan.push({ tl: extra, r: m.blank.r, c: m.blank.c });
      }
    }
    clearGhosts();
    let staged = 0;
    for (const p of plan) {
      let g = null;
      try { g = document.createElement('div'); g.className = 'g-cp-pv'; } catch (e) { g = null; }
      if (!g || !g.style) continue;
      g.style.setProperty('--pr', String(p.r));
      g.style.setProperty('--pc', String(p.c));
      // carry the tile's own viewport vars so the ghost clips the same square
      try {
        const hr = p.tl.el.style.getPropertyValue('--hr'); const hc = p.tl.el.style.getPropertyValue('--hc');
        if (hr) g.style.setProperty('--hr', hr);
        if (hc) g.style.setProperty('--hc', hc);
      } catch (e) { /* ignore */ }
      const face = cloneFace(p.tl.el);
      if (face) g.appendChild(face);
      pv.appendChild(g);
      ghosts.push(g);
      staged += 1;
    }
    if (!staged) return false;
    const variant = T.VARIANTS[Math.min(T.VARIANTS.length - 1, Math.floor(roll('pv-variant') * T.VARIANTS.length))];
    previewVariant = variant;
    const ms = solved ? T.SOLVED_MS : (T.PREVIEW_MS[tier] || 480);
    if (pv.style) { pv.style.setProperty('--cp-pv-ms', ms + 'ms'); pv.style.setProperty('--cp-pv-a', String(T.PREVIEW_ALPHA)); }
    if (pv.classList) {
      if (reduced) pv.classList.add('is-plain'); else pv.classList.add('g-cp-pv-' + variant);
      if (solved) pv.classList.add('g-cp-pv-solved');
      if (typeof pv.offsetWidth === 'number') void pv.offsetWidth;   // the ghosts start at 0 and fade in
      pv.classList.add('is-on');
    }
    previewOn = true;
    // the engine's own glitch on the LIE LAYER: its sfx + dressing, never a tile
    if (eng && typeof eng.fire === 'function' && !reduced) {
      try {
        eng.fire('glitch_swap', {
          targets: pv, variant: T.ENGINE_VARIANT[variant] || 'rgbsplit', seconds: Math.min(1.2, ms / 1000),
          strength: solved ? 0.7 : 0.45, onSwap() {}, sfx: true,
        });
      } catch (e) { /* a refused fire is not an error */ }
    }
    if (solved) { fired.solved += 1; solvedPlayed = true; } else fired.preview += 1;
    if (previewTimer) cancel(previewTimer);
    previewTimer = after(ms, () => {
      previewTimer = 0;
      if (pv.classList) pv.classList.remove('is-on');
      if (previewClear) cancel(previewClear);
      previewClear = after(T.PREVIEW_FADE_MS, () => { previewClear = 0; clearGhosts(); });
    });
    if (announce && !isHalted()) {
      if (solved) { try { announce(say_t('cp_trick_seen', 'That is not where that piece is.'), 1800); } catch (e) { /* ignore */ } }
      else if (roll('taunt') < T.TAUNT_CHANCE) { try { announce(say_t('cp_trick_preview', 'Did it move?'), 1400); } catch (e) { /* ignore */ } }
    }
    say('trickster: false preview (' + (solved ? 'FAKE SOLVED, ' : '') + staged + ' ghosts, ' + variant + ')');
    return true;
  }

  /* -------------------------------------------------------- stat flicker */
  function dealFlicker() {
    const s = stats();
    if (!s) return;
    const chip = chipEl('moves');
    if (!chip || chip.textContent == null) return;
    const truth = chipText('moves');
    const n = Number(s.moves) || 0;
    const delta = (roll('flick-sign') < 0.5 ? -1 : 1) * (1 + Math.floor(roll('flick-drop') * 4));
    const fake = Math.max(0, n + delta);
    const lie = (typeof truth === 'string' && /\d/.test(truth)) ? truth.replace(/\d[\d,\s]*/, String(fake)) : String(fake);
    if (!lie || lie === chip.textContent) return;
    try { chip.textContent = lie; } catch (e) { return; }
    if (!reduced && chip.classList) chip.classList.add('g-cp-statlie');
    fired.flicker += 1;
    after(T.FLICKER_MS, () => {
      try { if (chip.classList) chip.classList.remove('g-cp-statlie'); } catch (e) { /* ignore */ }
      const now = chipText('moves');
      if (now != null) { try { chip.textContent = now; } catch (e) { /* ignore */ } }
    });
    say('trickster: stat flicker (moves ' + n + ' -> ' + fake + ')');
  }

  /* ------------------------------------------------------- crooked clock */
  let clockArmed = false;
  let clockObs = null;
  let clockPoll = 0;
  let lastLie = null;
  let budgetSeen = Number(opts.budgetSec) || 0;
  let clockLies = 0;

  function formatLike(sec, truth) {
    const s = Math.max(0, Math.round(sec));
    if (typeof truth === 'string' && /^\d+s$/.test(truth.trim())) return s + 's';
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
    try { chip.textContent = text; clockLies += 1; } catch (e) { /* ignore */ }
  }
  function armClock() {
    if (clockArmed || tier < T.CLOCK_FROM_TIER || modeNow() === 'zen') return;
    const chip = chipEl('clock');
    if (!chip) return;
    clockArmed = true;
    fired.clock += 1;
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
    say('trickster: the clock goes crooked (' + (hooked ? 'observer' : 'poll') + ')');
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

  /* ------------------------------------------------------------- the melt */
  let melted = null;
  let meltAnnounced = false;
  function dealMelt() {
    if (melted || tier < T.MELT_FROM_TIER) return;
    const m = model();
    const loose = m.tiles.filter((tl) => !tl.locked);
    if (!loose.length) return;
    const pick = loose[Math.min(loose.length - 1, Math.floor(roll('melt-pick') * loose.length))];
    if (!pick || !pick.el.classList) return;
    try { pick.el.classList.add('g-cp-melt'); } catch (e) { return; }
    melted = pick.el;
    fired.melt += 1;
    if (announce && !meltAnnounced && !reduced) {
      meltAnnounced = true;
      try { announce(say_t('cp_trick_melt', 'One of them is running.'), 2000); } catch (e) { /* ignore */ }
    }
    say('trickster: melt (tile ' + pick.id + ')');
  }
  function unmelt() {
    if (!melted) return;
    try { melted.classList.remove('g-cp-melt'); } catch (e) { /* ignore */ }
    melted = null;
  }

  /* ------------------------------------------------------------ the ghost */
  let ghost = null;
  let lastWorst = null;
  function dealGhost() {
    if (ghost || reduced || tier < T.GHOST_FROM_TIER) return;
    const pv = previewEl();
    if (!pv || !pv.appendChild) return;
    const m = model();
    const worst = worstSlide(m.tiles, m.blank, roll('ghost-pick'));
    if (!worst) return;
    let node = null;
    try { node = document.createElement('i'); node.className = 'g-cp-ghost'; } catch (e) { return; }
    if (!node.style) return;
    node.style.setProperty('--gr0', String(worst.from.r));
    node.style.setProperty('--gc0', String(worst.from.c));
    node.style.setProperty('--gr1', String(worst.to.r));
    node.style.setProperty('--gc1', String(worst.to.c));
    const dx = worst.to.c - worst.from.c; const dy = worst.to.r - worst.from.r;
    node.style.setProperty('--ga', String(dx > 0 ? 0 : dx < 0 ? 180 : dy > 0 ? 90 : 270));
    pv.appendChild(node);
    ghost = node;
    lastWorst = worst;
    fired.ghost += 1;
    say('trickster: ghost cursor (tile ' + worst.id + ', score ' + worst.score + ')');
  }
  function unghost() {
    if (!ghost) return;
    try { ghost.remove(); } catch (e) { /* ignore */ }
    ghost = null;
  }

  /* ------------------------------------------------------------------ api */
  return {
    /** Deal the class. Call once, when play begins. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      if (started) return;
      started = true;
      ensureStyle();
      deals = buildDeals(0);
      scheduleDeals(deals);
      try { const s = stats(); if (s && Number(s.secLeft) > budgetSeen) budgetSeen = Number(s.secLeft); } catch (e) { /* ignore */ }
      // zen cycles the plan (the class has no bell); timed deals once
      if (modeNow() === 'zen') {
        const recycle = () => {
          if (destroyed || stopped) return;
          cycle += 1;
          const more = buildDeals(cycle);
          deals = deals.concat(more);
          scheduleDeals(more);
          after(T.BUDGET_MS, recycle);
        };
        after(T.BUDGET_MS, recycle);
      }
      say('trickster: dealt ' + deals.length + ' cards ('
        + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },

    /**
     * index.js calls after every legal slide, after it has written --r/--c and
     * the state classes. Stall theatre ends; an armed preview plays (the lie
     * rides the move).
     */
    afterSlide() {
      if (!armed || destroyed) return;
      unmelt();
      unghost();
      if (!stopped && previewArmed) playPreview();
    },

    /** index.js calls every ~500ms with ms since the last move; 0 resets. */
    stalled(ms) {
      if (!armed || destroyed || stopped) return;
      const n = Number(ms) || 0;
      if (n <= 0) { unmelt(); unghost(); return; }
      if (isHalted()) return;
      if (n >= T.MELT_STALL_MS) dealMelt();
      if (n >= T.GHOST_STALL_MS) dealGhost();
    },

    /** A wash is landing (on) or lifting (off). An armed preview hides under it. */
    washOn(on) {
      if (!armed || destroyed) return;
      washIsOn = !!on;
      if (washIsOn && !stopped && previewArmed && !isHalted()) playPreview();
    },

    /** The class is over: no card may fire again, every lie comes off. */
    stop() {
      stopped = true;
      previewArmed = null;
      if (previewFold) { cancel(previewFold); previewFold = 0; }
      if (previewTimer) { cancel(previewTimer); previewTimer = 0; }
      if (previewClear) { cancel(previewClear); previewClear = 0; }
      clearGhosts();
      unmelt();
      unghost();
      disarmClock(true);
    },

    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      clearGhosts();
      unmelt();
      unghost();
      disarmClock(true);
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, tier, started, deals: deals.slice(), fired: Object.assign({}, fired),
        previewArmed, previewOn, previewVariant, ghostsLive: ghosts.length, solvedPlayed,
        melted: !!melted, ghost: !!ghost, lastWorst, washOn: washIsOn,
        clock: { armed: clockArmed, lies: clockLies, budget: budgetSeen, observer: !!clockObs },
        lexicon: Array.from(lexiconUsed), timers: live.size,
      };
    },
  };
}

export default createCpTrickster;
