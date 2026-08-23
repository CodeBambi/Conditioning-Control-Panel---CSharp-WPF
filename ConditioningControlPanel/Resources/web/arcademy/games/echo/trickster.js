/* ============================================================================
 * games/echo/trickster.js - DECK III of the House Rules, dealt into the music
 * room. A memory game is where your own reading stops being a reliable
 * witness, so every card here attacks what the player READS between the
 * playback and the press - never the sequence, never the pads' places, never
 * the key under the finger. (The DECOY ECHO - the signature twist - is NATIVE:
 * index.js deals it from the seeded plan and lights the pad; this deck only
 * deals the floor-map cards around it.)
 *
 *   STAT FLICKER     the LEN chip briefly reads one longer than the truth -
 *                    wishful reading - then corrects with a static pop. The
 *                    ledger never moves; confidence does. Tier 1+.
 *   CROOKED CLOCK    the class clock's FACE bends: it races through the
 *                    boring middle (shows less time than you really have) and
 *                    crawls as the bell nears, meeting the truth exactly at the
 *                    last 15s and staying honest from there. The real budget
 *                    - the bell, the grade - is index.js's and exact. A
 *                    MutationObserver on the chip re-bends the face the moment
 *                    truth lands (before the frame paints); no observer (DOM
 *                    double) and a 250ms poll does the same. If the core hands
 *                    a between-turns timer ring (chipEl('ring') returning a
 *                    node with --ec-k 0..1 - index.js's .g-ec-timer chip), the
 *                    SAME bend is painted onto a twin ring of this deck's at
 *                    the centre of the pad ring for the input window, and the
 *                    honest chip steps aside (.g-ec-tk-bent) while the twin is
 *                    up. Tier 2+.
 *   UNRELIABLE LABEL a pad's .g-ec-word wears ANOTHER pad's word for ~600ms
 *                    during the input turn. The pad's colour, its place on
 *                    the ring and its tone are the truth (Law IV) - the card
 *                    trains the player to stop reading and start looking.
 *                    Dealt ONLY when the pads carry words (a glyph-only face
 *                    is the truth itself and is never lied on). Tier 2+.
 *   GHOST CURSOR     a faint cursor echo trails the real pointer by ~430ms
 *                    during the input turn, and on a stall it stops trailing
 *                    and DRIFTS to a WRONG pad - ring-adjacent to the one that
 *                    is due, so the lure is the near miss the casino stages -
 *                    pulsing, the house leaning on your hand. Pure suggestion:
 *                    pointer-events none, it cannot click, it never hides the
 *                    real cursor, and it dies on the next press. Tier 3+,
 *                    mouse pointers only.
 *
 * DEALING RULE: card slots are dealt at start() from the seeded plan over the
 * class budget - 2/4/6/8 by tier, at least 9s apart (under the House's
 * 4-a-minute cap; a rolling-minute guard enforces it directly too), never in
 * the first 10s, never in the last 15s (the bell's honest zone). The flicker
 * and the label need an INPUT turn to land on: a slot whose moment arrives
 * outside one waits for the next afterPlayback() (at most RETRY_MAX polite
 * re-queues), then folds. The clock latches once and stays. The ghost's
 * slot ARMS it for the rest of the class; the lure itself is stall-reactive
 * (the stall is the player's own) but WHICH wrong pad it drifts to is the
 * seed's choice.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the flicker and the clock write chip TEXT and restore
 *       via chipText (the truth source) and stand down if a real repaint lands
 *       mid-lie; the label writes .g-ec-word only and restores the exact
 *       string it read; nothing reads or writes the sequence, the streak,
 *       the length, the clock budget or the grade; nothing can change WHEN
 *       index.js's own timers fire.
 *   II  input honest  - no card is clickable; the ghost and the twin ring are
 *       pointer-events:none in this deck's own layer over the ring; no pad is
 *       ever moved, covered, resized or delayed; data-state is never written.
 *   III never still   - the ghost pulses on the lure; the flicker pops.
 *   IV  images > text - the two taunts this deck can say (ec_taunt_ghost on
 *       a lure, ec_taunt_label once per class on the first label) come through
 *       the lexicon and land on the ONE proctor line via opts.announce, and
 *       only when the core offers that line.
 *   V   seeded        - per-tag mulberry32 off seed+'|ec-trickster|<tag>',
 *       append-only tags; no Math.random.
 *   VI  exits sacred  - capsOk false disarms the whole deck; reduced motion
 *       drops the ghost and the twin ring entirely (the flicker is a number,
 *       the label is a word, the clock still bends: none of those is motion);
 *       every timer rides the game's registry AND a local set; pause()
 *       restores every live lie; destroy() leaves no node, timer, observer
 *       or listener.
 *   VII lexicon       - ec_taunt_ghost / ec_taunt_label via opts.t.
 *
 * ENGINE PLACEMENT: entirely game-local. Every card is addressed to this
 * class's own nodes; the deck asks the engine for nothing. `opts.engine` is
 * accepted for interface symmetry and unused.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const EC_TRICKSTER = Object.freeze({
  /** Dealt card slots per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** The plan's window: never the first 10s, never the honest tail. */
  FIRST_DEAL_MS: 10000,
  TAIL_MS: 15000,
  MIN_GAP_MS: 9000,
  /** The House's per-minute cap, stated as the law says it. */
  RATE_WINDOW_MS: 60000,
  RATE_MAX: 4,
  /** A slot that lands outside an input turn waits for the next one. */
  RETRY_MAX: 3,
  RETRY_MS: 2500,

  /** Card gates. */
  FLICK_FROM_TIER: 1,
  LABEL_FROM_TIER: 2,
  CLOCK_FROM_TIER: 2,
  GHOST_FROM_TIER: 3,
  /** Slot weights per tier over the eligible cards (renormalised). The clock
   *  and the ghost are ONE slot each (they latch); the rest fill the plan. */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ flick: 1, label: 0 }),
    2: Object.freeze({ flick: 0.5, label: 0.5 }),
    3: Object.freeze({ flick: 0.45, label: 0.55 }),
    4: Object.freeze({ flick: 0.4, label: 0.6 }),
  }),

  /** Stat flicker: how long the wrong number stands. */
  FLICK_MS: 460,
  /** Crooked clock: the bend at mid-budget (fraction of the budget), the
   *  honest zone (the bell's last 15s are true), the ramp, the poll. */
  CLOCK_BEND: 0.14,
  CLOCK_HONEST_SEC: 15,
  CLOCK_RAMP: 0.35,
  CLOCK_POLL_MS: 250,
  /** The twin ring's bend (same pure function, the input window as budget). */
  RING_BEND: 0.16,
  RING_HONEST: 0.15,
  RING_STEPS: 12,
  /** Unreliable label: how long the lie is worn; the taunt chance. */
  LABEL_MS: 600,
  LABEL_TAUNT_CHANCE: 0.5,
  /** The ghost: the trail, the stall that turns it into a lure, its lerp. */
  GHOST_TICK_MS: 90,
  GHOST_TRAIL_MS: 430,
  GHOST_STALL_MS: 1200,
  GHOST_LERP: 0.16,
  GHOST_TRAIL_KEEP_MS: 2200,
  GHOST_TAUNT_CHANCE: 0.35,
});

const STYLE_ID = 'g-ec-trickster-style';
const STYLE_TEXT = `
/* this deck's layer: inside the ring, over the pads, pointer-events:none */
.g-ec-tk{position:absolute;inset:0;pointer-events:none;z-index:3;overflow:visible}
.g-ec-tk *{pointer-events:none}
/* GHOST CURSOR: a will-o-wisp echo of the player's own hand */
.g-ec-tk-ghost{position:absolute;left:0;top:0;width:15px;height:19px;opacity:0;will-change:transform;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, rgba(255,105,180,.9), rgba(184,166,232,.55));
  filter:drop-shadow(0 0 6px rgba(255,105,180,.6));
  transition:transform .34s ease-out, opacity .38s ease}
.g-ec-tk-ghost.on{opacity:.32}
.g-ec-tk-ghost.lure{opacity:.55;animation:g-ec-tk-ghostpulse 1.4s ease-in-out infinite}
@keyframes g-ec-tk-ghostpulse{50%{filter:drop-shadow(0 0 12px rgba(255,105,180,.95))}}
/* THE TWIN RING: the crooked face of an input-window ring, over the real one */
.g-ec-tk-ring{position:absolute;left:50%;top:50%;width:16%;height:16%;border-radius:50%;
  transform:translate(-50%,-50%);opacity:0;
  background:conic-gradient(from -90deg, hsl(264 80% 78% / .9) 0deg, hsl(264 80% 78% / .9) calc(var(--ec-tk-k,1) * 360deg), transparent calc(var(--ec-tk-k,1) * 360deg));
  -webkit-mask:radial-gradient(circle, transparent 0 calc(50% - 4px), #000 calc(50% - 3px) calc(50% - 1px), transparent 50%);
  mask:radial-gradient(circle, transparent 0 calc(50% - 4px), #000 calc(50% - 3px) calc(50% - 1px), transparent 50%);
  transition:opacity .18s ease}
.g-ec-tk-ring.on{opacity:.85}
/* the real face steps aside while ours is up - and only while ours is up */
.g-ec-tk-bent{opacity:0 !important}
/* STAT FLICKER: the static pop the wrong number corrects itself with */
.g-ec-chip.g-ec-tk-flick{text-shadow:0 0 22px rgba(184,166,232,.75), 0 0 3px rgba(255,255,255,.5) !important;
  animation:g-ec-tk-static .16s steps(3) 2}
@keyframes g-ec-tk-static{0%{filter:none}40%{filter:brightness(1.5) contrast(1.4)}100%{filter:none}}
/* UNRELIABLE LABEL: the word shivers while it lies */
.g-ec-word.g-ec-tk-lie{animation:g-ec-tk-lie .6s steps(4) 1}
@keyframes g-ec-tk-lie{0%{opacity:1}25%{opacity:.6;letter-spacing:.2em}50%{opacity:1}75%{opacity:.7}100%{opacity:.9}}
@media (prefers-reduced-motion: reduce){
  .g-ec-tk-ghost,.g-ec-tk-ring{display:none}
  .g-ec-chip.g-ec-tk-flick{animation:none}
  .g-ec-word.g-ec-tk-lie{animation:none}
}
html.arc-reduced .g-ec-tk-ghost,html.arc-reduced .g-ec-tk-ring{display:none}
html.arc-reduced .g-ec-chip.g-ec-tk-flick{animation:none}
html.arc-reduced .g-ec-word.g-ec-tk-lie{animation:none}
.g-ec-stage.suspended .g-ec-tk-ghost{animation-play-state:paused !important}
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
function clampTier(v) { return Math.max(1, Math.min(4, Math.round(Number(v) || 1))); }
function nowMs() {
  try { if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
  return Date.now();
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
function qsa(root, sel) {
  try { if (root && typeof root.querySelectorAll === 'function') return Array.from(root.querySelectorAll(sel)); } catch (e) { /* fall */ }
  return [];
}
function qs(root, sel) {
  try { if (root && typeof root.querySelector === 'function') return root.querySelector(sel) || null; } catch (e) { /* fall */ }
  return null;
}
function padIndexOf(node) {
  try { const n = Number(node.getAttribute('data-pad')); return Number.isFinite(n) ? n : -1; } catch (e) { return -1; }
}

/**
 * The crooked face, pure: displayed seconds-left for a real seconds-left.
 * f(B)=B, f(honest)=honest, monotonic (bend*pi < 1), honest at/after the
 * bell. Ahead through the middle (less time shown than real), crawling as
 * the bell nears, so the face meets the truth instead of snapping to it.
 */
export function bendClock(secLeft, budgetSec, bend, honestSec) {
  const x = Math.max(0, Number(secLeft) || 0);
  const B = Math.max(30, Number(budgetSec) || 105);
  const H = Number.isFinite(honestSec) ? honestSec : EC_TRICKSTER.CLOCK_HONEST_SEC;
  if (x <= H || x >= B) return Math.round(x);
  const A0 = Number.isFinite(bend) ? bend : EC_TRICKSTER.CLOCK_BEND;
  const u = x / B;
  const uh = H / B;
  const ramp = Math.max(0, Math.min(1, (u - uh) / EC_TRICKSTER.CLOCK_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, Math.round(B * (u - A * Math.sin(Math.PI * u))));
}
/** The twin ring's bend: shown fraction-left k' for a true fraction-left k
 *  (ahead through the middle, honest in the last RING_HONEST). Pure. */
export function bendRing(k01, bend, honest) {
  const k = Math.max(0, Math.min(1, Number(k01) || 0));
  const H = Number.isFinite(honest) ? honest : EC_TRICKSTER.RING_HONEST;
  if (k <= H || k >= 1) return k;
  const A0 = Number.isFinite(bend) ? bend : EC_TRICKSTER.RING_BEND;
  const ramp = Math.max(0, Math.min(1, (k - H) / 0.32));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, Math.min(1, k - A * Math.sin(Math.PI * k)));
}
/** A wrong pad next to the due one (ring-adjacent), seeded between the two. Pure. */
export function wrongNeighbour(due, n, roll01) {
  const m = Math.max(2, Math.floor(Number(n) || 6));
  const d = Math.floor(Number(due));
  if (!Number.isFinite(d) || d < 0) return Math.floor((Number(roll01) || 0) * m) % m;
  const left = (d - 1 + m) % m;
  const right = (d + 1) % m;
  return (Number(roll01) || 0) < 0.5 ? left : right;
}

/**
 * @param {Object} o
 *   seed, tier, timers {after, every?, clear|cancel}, reduced, capsOk (bool or fn),
 *   isHalted () => bool, t, stats () => {len, secLeft, streak, best, phase?, budgetSec?},
 *   chipEl (which: 'len'|'clock'|'streak'|'best'|'ring') => el|null,
 *   chipText (which) => honest text, log,
 *   game hooks (optional, every one null-safe; index.js passes the first six):
 *     ring | board   .g-ec-ring (the deck's layer mounts here; the ghost listens
 *                    for pointermove on stage || ring.parentNode || ring)
 *     pads           () => Element[]  (else read off the ring)
 *     padEl          (i) => Element | null
 *     wordEl         (i) => the pad's .g-ec-word (the ONE lie target)
 *     wordText       (i) => the truth string for pad i
 *     restoreWords   () => void  (the core repaints every word from the truth)
 *     announce       (text, ms) => void  (the proctor line)
 *     stats().inputOpen  true while the input turn is open (the flicker / the
 *                    label / the ghost only act then); stats().expect = the index
 *                    due next; stats().expectPad (if ever offered) = the pad due,
 *                    which the lure then avoids - without it the lure's pad is a
 *                    seeded uniform pick (pure noise, no information either way)
 *     stage          .g-ec-stage (optional; phase read off data-phase as a fallback)
 *     nextPad        () => 0..5 | -1 (optional alias of expectPad)
 *     coarse         bool (touch pointer - no ghost)
 *     budgetSec      number
 */
export function createEcTrickster(o) {
  const opts = o || {};
  const T = EC_TRICKSTER;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const t = typeof opts.t === 'function' ? opts.t : (k, f) => (f == null ? k : f);
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const coarse = !!opts.coarse;
  const armedBase = !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  function capsOkNow() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false;
  }
  const armed = armedBase && capsOkNow();
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const announce = typeof opts.announce === 'function' ? opts.announce : null;
  const boardEl = opts.board || opts.ring || null;
  const wordElOf = typeof opts.wordEl === 'function' ? opts.wordEl : null;
  const wordTextOf = typeof opts.wordText === 'function' ? opts.wordText : null;
  const restoreWords = typeof opts.restoreWords === 'function' ? opts.restoreWords : null;
  function duePad() {
    try {
      if (typeof opts.nextPad === 'function') { const v = Math.floor(Number(opts.nextPad())); if (Number.isFinite(v)) return v; }
      const s = stats();
      if (s && s.expectPad != null) { const v = Math.floor(Number(s.expectPad)); if (Number.isFinite(v)) return v; }
    } catch (e) { /* fall */ }
    return -1;
  }
  function pointerHost() {
    if (opts.stage && typeof opts.stage.addEventListener === 'function') return opts.stage;
    try { if (boardEl && boardEl.parentNode && typeof boardEl.parentNode.addEventListener === 'function') return boardEl.parentNode; } catch (e) { /* fall */ }
    return boardEl && typeof boardEl.addEventListener === 'function' ? boardEl : null;
  }

  /* timers: the game's registry + a local set */
  const live = new Set();
  const chains = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  let destroyed = false;
  let stopped = false;
  let paused = false;
  function after(ms, fn) {
    if (!armed || destroyed) return 0;
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
    if (!armed || destroyed) return 0;
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

  const seedBase = String(opts.seed || 'ec') + '|ec-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  const fired = { flick: 0, label: 0, clock: 0, ghost: 0, lure: 0 };
  let deals = [];
  const fireTimes = [];         // rolling-minute guard
  const lexiconUsed = new Set();
  const say_t = (k, f) => { lexiconUsed.add(k); return t(k, f); };
  let labelTaunted = false;

  /* ------------------------------------------------------------ helpers */
  function phaseNow() {
    if (typeof opts.phase === 'function') { try { const p = opts.phase(); if (p) return String(p); } catch (e) { /* fall */ } }
    try { const s = stats(); if (s && s.phase) return String(s.phase); } catch (e) { /* fall */ }
    const host = opts.stage || (boardEl && boardEl.parentNode) || null;
    try { if (host && typeof host.getAttribute === 'function') return String(host.getAttribute('data-phase') || ''); } catch (e) { /* fall */ }
    return '';
  }
  function inInput() {
    try { const s = stats(); if (s && s.inputOpen != null) return !!s.inputOpen; } catch (e) { /* fall */ }
    const p = phaseNow();
    return p === 'input' || p === 'encore';
  }
  function livePads() {
    if (typeof opts.pads === 'function') { try { const p = opts.pads(); if (p && p.length) return Array.from(p); } catch (e) { /* fall */ } }
    return qsa(boardEl, '.g-ec-pad');
  }
  function wordNode(pad, i) {
    if (wordElOf) { try { const w = wordElOf(i); if (w) return w; } catch (e) { /* fall */ } }
    return qs(pad, '.g-ec-word');
  }
  function truthWord(pad, i) {
    if (wordTextOf) { try { return String(wordTextOf(i) || ''); } catch (e) { return ''; } }
    const w = qs(pad, '.g-ec-word');
    return w ? String(w.textContent || '') : '';
  }
  function wordsOn() {
    if (typeof opts.wordsOn === 'function') { try { return !!opts.wordsOn(); } catch (e) { return false; } }
    /* inferred: every pad carries a word of 2+ characters and the words differ */
    const pads = livePads();
    const words = pads.map((p) => truthWord(p, padIndexOf(p)).trim());
    if (!words.length) return false;
    const set = new Set(words);
    return words.every((w) => w.length >= 2) && set.size === words.length;
  }
  function rateOk() {
    const now = nowMs();
    while (fireTimes.length && now - fireTimes[0] > T.RATE_WINDOW_MS) fireTimes.shift();
    return fireTimes.length < T.RATE_MAX;
  }
  function markFire() { fireTimes.push(nowMs()); }

  /* ------------------------------------------------------------- the deal */
  function buildDeals() {
    const n = T.DEALS[tier] || 2;
    const spanMs = Math.max(40, Number(opts.budgetSec) || 105) * 1000;
    const usable = Math.max(15000, spanMs - T.FIRST_DEAL_MS - T.TAIL_MS);
    const times = [];
    for (let i = 0; i < n; i++) times.push(T.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    /* MIN_GAP between cards, and the whole hand still inside the window: a
       gap pass can push the last card into the TAIL, so the hand is slid
       back (the gap shrinks only when n cards cannot fit the window at all) */
    const end = T.FIRST_DEAL_MS + usable;
    const gap = Math.min(T.MIN_GAP_MS, n > 1 ? usable / (n - 1) : T.MIN_GAP_MS);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < gap) times[i] = times[i - 1] + gap;
    }
    const over = times.length ? times[times.length - 1] - end : 0;
    if (over > 0) {
      for (let i = 0; i < times.length; i++) times[i] = Math.max(T.FIRST_DEAL_MS, times[i] - over);
      for (let i = 1; i < times.length; i++) {
        if (times[i] - times[i - 1] < gap) times[i] = times[i - 1] + gap;
      }
    }
    const clockSlot = tier >= T.CLOCK_FROM_TIER && n > 1 ? Math.floor(roll('clock-slot') * Math.ceil(n / 2)) : -1;
    let ghostSlot = -1;
    if (tier >= T.GHOST_FROM_TIER && n > 2) {
      ghostSlot = Math.floor(roll('ghost-slot') * n);
      if (ghostSlot === clockSlot) ghostSlot = (ghostSlot + 1) % n;
    }
    const w = T.WEIGHTS[tier] || T.WEIGHTS[1];
    return times.map((at, i) => {
      let card;
      if (i === clockSlot) card = 'clock';
      else if (i === ghostSlot) card = 'ghost';
      else {
        const r = roll('card');
        card = r < w.flick ? 'flick' : 'label';
        if (card === 'label' && tier < T.LABEL_FROM_TIER) card = 'flick';
      }
      return { at: Math.round(at), card };
    });
  }

  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    if (deal.card === 'clock') { armClock(); return; }
    if (deal.card === 'ghost') { armGhost(); return; }
    /* the flicker and the label want an input turn; otherwise wait politely */
    if (isHalted() || !inInput() || !rateOk()) {
      if (tries < T.RETRY_MAX) { deal.pending = true; after(T.RETRY_MS, () => attempt(deal, tries + 1)); }
      else { deal.pending = false; deal.folded = true; }
      return;
    }
    deal.pending = false;
    if (deal.card === 'flick') dealFlick();
    else if (deal.card === 'label') dealLabel();
  }

  /* -------------------------------------------------------- stat flicker */
  let flickTimer = 0; let flickChip = null; let flickLie = null;
  function dealFlick() {
    if (tier < T.FLICK_FROM_TIER) return;
    const chip = chipEl('len');
    const s = stats();
    if (!chip || !s || chip.textContent == null) return;
    const truth = chipText('len');
    const n = Math.max(0, Math.floor(Number(s.len) || 0));
    const fake = n + 1;
    let lie;
    if (typeof truth === 'string' && /\d/.test(truth)) lie = truth.replace(/\d+/, String(fake));
    else lie = String(fake);
    if (lie === chip.textContent) return;
    try { chip.textContent = lie; } catch (e) { return; }
    flickChip = chip; flickLie = lie;
    if (!reduced) setCls(chip, 'g-ec-tk-flick', true);
    fired.flick += 1;
    markFire();
    cancel(flickTimer);
    flickTimer = after(T.FLICK_MS, () => { flickTimer = 0; endFlick(); });
    say('trickster: stat flicker (len ' + n + ' reads ' + fake + ')');
  }
  function endFlick() {
    if (!flickChip) return;
    setCls(flickChip, 'g-ec-tk-flick', false);
    try {
      /* stand down if a real repaint already landed */
      if (flickChip.textContent === flickLie) {
        const now = chipText('len');
        if (now != null) flickChip.textContent = now;
      }
    } catch (e) { /* noop */ }
    flickChip = null; flickLie = null;
  }

  /* ------------------------------------------------------- crooked clock */
  let clockArmed = false; let clockObs = null; let clockPoll = 0; let lastLie = null;
  let budgetSeen = Number(opts.budgetSec) || 0; let clockLies = 0;
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
    if (!clockArmed || destroyed || stopped || paused) return;
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
    if (clockArmed || tier < T.CLOCK_FROM_TIER) return;
    const chip = chipEl('clock');
    if (!chip) return;
    clockArmed = true;
    fired.clock += 1;
    markFire();
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
    armRing();
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
    disarmRing();
  }
  /* the twin ring: only when the core offers a between-turns ring node */
  let ringEl = null; let ringPoll = 0; let ringReal = null; let ringLies = 0;
  function armRing() {
    if (reduced || ringEl) return;
    const real = chipEl('ring');
    if (!real || !layer) return;
    ringReal = real;
    ringEl = el('i', 'g-ec-tk-ring');
    if (!ringEl) return;
    /* the twin sits at the CENTRE of the ring (where the playback lives) and
       the real chip's honest face steps aside while the twin is up; the real
       chip keeps painting --ec-k, which is the truth the twin bends */
    layer.appendChild(ringEl);
    ringPoll = every(Math.round(1000 / T.RING_STEPS / 2), tickRing);
  }
  function tickRing() {
    if (!ringEl || !ringReal || destroyed || stopped || paused) return;
    let k = NaN;
    try { k = parseFloat(ringReal.style.getPropertyValue('--ec-k')); } catch (e) { k = NaN; }
    if (!Number.isFinite(k) || !inInput()) { setCls(ringEl, 'on', false); setCls(ringReal, 'g-ec-tk-bent', false); return; }
    const shown = bendRing(k, T.RING_BEND, T.RING_HONEST);
    setVar(ringEl, '--ec-tk-k', shown.toFixed(3));
    if (Math.abs(shown - k) > 0.002) ringLies += 1;
    setCls(ringEl, 'on', true);
    setCls(ringReal, 'g-ec-tk-bent', true);
  }
  function disarmRing() {
    if (ringPoll) { stopEvery(ringPoll); ringPoll = 0; }
    if (ringReal) setCls(ringReal, 'g-ec-tk-bent', false);
    if (ringEl) { try { ringEl.remove(); } catch (e) { /* ignore */ } }
    ringEl = null; ringReal = null;
  }

  /* ---------------------------------------------------- unreliable label */
  let labelTimer = 0; let labelNode = null; let labelWas = null; let labelLie = null;
  function dealLabel() {
    if (tier < T.LABEL_FROM_TIER || labelNode) return;
    if (!wordsOn()) { say('trickster: label folds (glyph faces)'); return; }
    const pads = livePads().filter((p) => wordNode(p, padIndexOf(p)));
    if (pads.length < 2) return;
    const a = pads[Math.floor(roll('label-pick') * pads.length)];
    let b = pads[Math.floor(roll('label-pick') * pads.length)];
    if (b === a) b = pads[(pads.indexOf(a) + 1) % pads.length];
    const wa = wordNode(a, padIndexOf(a));
    if (!wa) return;
    const was = truthWord(a, padIndexOf(a));
    const lie = truthWord(b, padIndexOf(b));
    if (!lie || lie === was) return;
    try { wa.textContent = lie; } catch (e) { return; }
    labelNode = wa; labelWas = was; labelLie = lie;
    if (!reduced) setCls(wa, 'g-ec-tk-lie', true);
    fired.label += 1;
    markFire();
    cancel(labelTimer);
    labelTimer = after(T.LABEL_MS, () => { labelTimer = 0; endLabel(); });
    if (announce && !labelTaunted && roll('label-taunt') < T.LABEL_TAUNT_CHANCE) {
      labelTaunted = true;
      try { announce(say_t('ec_taunt_label', 'Read it again. Or do not.'), 1600); } catch (e) { /* ignore */ }
    }
    say('trickster: unreliable label (pad ' + padIndexOf(a) + ' wears pad ' + padIndexOf(b) + ')');
  }
  function endLabel() {
    if (!labelNode) return;
    setCls(labelNode, 'g-ec-tk-lie', false);
    try { if (labelNode.textContent === labelLie) labelNode.textContent = labelWas; } catch (e) { /* ignore */ }
    /* and the core's own truth repaint, when it offers one (belt and braces) */
    if (restoreWords) { try { restoreWords(); } catch (e) { /* ignore */ } }
    labelNode = null; labelWas = null; labelLie = null;
  }

  /* --------------------------------------------------------- ghost cursor */
  let layer = null; let ghostEl = null; let ghostTimer = 0; let onMove = null;
  const trail = []; let lastMoveAt = 0; let gx = 0; let gy = 0;
  let ghostMode = 'off'; let ghostArmed = false; let lurePad = -1; let stallMs = 0;
  let pointerNode = null;
  function ghostEligible() {
    return armed && !reduced && !coarse && tier >= T.GHOST_FROM_TIER && !!pointerHost() && !!layer;
  }
  function padPoint(i) {
    const pads = livePads();
    const pad = pads.find((p) => padIndexOf(p) === i) || null;
    if (!pad) return null;
    try {
      const r = pad.getBoundingClientRect();
      const b = layer.getBoundingClientRect();
      if (!r || !b || !r.width) return null;
      return { x: r.left + r.width / 2 - b.left, y: r.top + r.height / 2 - b.top };
    } catch (e) { return null; }
  }
  function ghostOff() {
    if (ghostMode === 'off') return;
    setCls(ghostEl, 'on', false);
    setCls(ghostEl, 'lure', false);
    ghostMode = 'off';
    lurePad = -1;
  }
  function ghostTick() {
    if (!ghostEl || destroyed || stopped || paused) return;
    if (!capsOkNow() || isHalted() || !inInput()) { ghostOff(); return; }
    const now = nowMs();
    const stalled = (lastMoveAt > 0 && now - lastMoveAt >= T.GHOST_STALL_MS) || stallMs >= T.GHOST_STALL_MS;
    if (stalled) {
      if (lurePad < 0) {
        lurePad = wrongNeighbour(duePad(), livePads().length || 6, roll('lure'));
        fired.lure += 1;
        if (announce && roll('ghost-taunt') < T.GHOST_TAUNT_CHANCE) {
          try { announce(say_t('ec_taunt_ghost', 'This one. Surely this one.'), 1400); } catch (e) { /* ignore */ }
        }
      }
      const p = padPoint(lurePad);
      if (p) { gx += (p.x - gx) * T.GHOST_LERP; gy += (p.y - gy) * T.GHOST_LERP; }
      ghostMode = 'lure';
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', true);
    } else {
      if (!trail.length || !lastMoveAt) { ghostOff(); return; }
      let pt = trail[0];
      for (const q of trail) { if (now - q.at >= T.GHOST_TRAIL_MS) pt = q; else break; }
      gx = pt.x; gy = pt.y;
      ghostMode = 'trail';
      lurePad = -1;
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', false);
    }
    try { ghostEl.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)'; } catch (e) { /* noop */ }
    while (trail.length > 1 && now - trail[0].at > T.GHOST_TRAIL_KEEP_MS) trail.shift();
  }
  function armGhost() {
    if (ghostArmed || !ghostEligible()) return;
    ghostEl = el('i', 'g-ec-tk-ghost');
    if (!ghostEl) return;
    layer.appendChild(ghostEl);
    ghostArmed = true;
    fired.ghost += 1;
    onMove = (ev) => {
      if (destroyed) return;
      try {
        const b = layer.getBoundingClientRect();
        const x = Number(ev && ev.clientX) - b.left;
        const y = Number(ev && ev.clientY) - b.top;
        if (!Number.isFinite(x) || !Number.isFinite(y)) return;
        lastMoveAt = nowMs();
        trail.push({ x, y, at: lastMoveAt });
      } catch (e) { /* no rects under the DOM double */ }
    };
    pointerNode = pointerHost();
    try { if (pointerNode) pointerNode.addEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    ghostTimer = every(T.GHOST_TICK_MS, ghostTick);
    say('trickster: ghost armed');
  }
  function disarmGhost() {
    if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
    try { if (onMove && pointerNode && pointerNode.removeEventListener) pointerNode.removeEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    onMove = null; pointerNode = null;
    ghostOff();
    if (ghostEl) { try { ghostEl.remove(); } catch (e) { /* ignore */ } }
    ghostEl = null;
    ghostArmed = false;
  }

  function mountLayer() {
    if (layer || !boardEl || !boardEl.appendChild) return;
    ensureStyle();
    layer = el('div', 'g-ec-tk');
    if (layer) boardEl.appendChild(layer);
  }

  /* ------------------------------------------------------------------ api */
  const api = {
    /** Deal the class. Call once, when play begins. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      mountLayer();
      deals = buildDeals();
      for (const deal of deals) after(deal.at, () => attempt(deal, 0));
      try { const s = stats(); if (s && Number(s.secLeft) > budgetSeen) budgetSeen = Number(s.secLeft); } catch (e) { /* ignore */ }
      say('trickster: dealt ' + deals.length + ' cards ('
        + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },

    /** index.js calls when a playback ends and the input turn opens. */
    afterPlayback() {
      if (!armed || destroyed || stopped) return;
      stallMs = 0;
      lurePad = -1;
      /* a pending flicker/label gets its turn now */
      for (const deal of deals) {
        if (deal.pending && !deal.folded && (deal.card === 'flick' || deal.card === 'label')) {
          deal.pending = false;
          after(300 + Math.round(roll('late') * 400), () => attempt(deal, T.RETRY_MAX));
        }
      }
    },

    /** index.js calls after the input turn ends (clear or fail): every lie comes off. */
    afterInput() {
      if (!armed || destroyed) return;
      endLabel();
      endFlick();
      ghostOff();
      stallMs = 0;
    },

    /** index.js calls every 500ms with ms since the last press; 0 resets. */
    stalled(ms) {
      if (!armed || destroyed || stopped) return;
      const n = Number(ms) || 0;
      stallMs = n;
      if (n <= 0) { ghostOff(); return; }
    },

    pause() {
      paused = true;
      endLabel();
      endFlick();
      ghostOff();
    },
    resume() { paused = false; if (clockArmed) bendFace(); },

    /** The class is over: no card may fire again, every lie comes off. */
    stop() {
      stopped = true;
      for (const d of deals) { if (d.pending) { d.pending = false; d.folded = true; } }
      endLabel();
      endFlick();
      disarmGhost();
      disarmClock(true);
    },

    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      endLabel();
      endFlick();
      disarmGhost();
      disarmClock(true);
      if (layer) { try { layer.remove(); } catch (e) { /* ignore */ } }
      layer = null;
    },

    diagnostics() {
      return {
        armed, tier, deals: deals.map((d) => ({ at: d.at, card: d.card, pending: !!d.pending, folded: !!d.folded })),
        fired: Object.assign({}, fired),
        clock: { armed: clockArmed, lies: clockLies, budget: budgetSeen, observer: !!clockObs, ring: !!ringEl, ringLies },
        ghost: { armed: ghostArmed, mode: ghostMode, lurePad, trail: trail.length },
        label: !!labelNode, flick: !!flickChip, lexicon: Array.from(lexiconUsed),
        rateWindow: fireTimes.length,
      };
    },
  };
  return api;
}

export default createEcTrickster;
