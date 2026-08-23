/* ============================================================================
 * games/instant-recall/trickster.js - DECK III of the House Rules, dealt into
 * THE VIGIL. A recall game is where your own eyes stop being reliable
 * witnesses, so every card here attacks the READING of the slip and the HUD -
 * never the ledger, never the options' order, never the window underneath.
 *
 *   STAT FLICKER     the stops chip (or the clock) briefly reads one off, then
 *                    "corrects" with a static pop to the EXACT text it had. The
 *                    ledger never moves; confidence does. Dealt into the VIGIL
 *                    (between stops), never onto a live slip.
 *   CROOKED CLOCK    the answer-window ring's FACE bends: it races through the
 *                    boring middle (shows less time left than you really have)
 *                    and crawls as the window runs out, meeting the truth
 *                    exactly at the last 15% and staying honest from there.
 *                    The real window is CORE's timer and untouched; this deck
 *                    paints an overlay ring of its own over the real face
 *                    (.g-ir-timer wears .g-ir-tk-bent while ours is up) and
 *                    never writes --ir-t. The bend is a PURE function, exported.
 *                    Painted at 8Hz from the game's pause-aware timers (the
 *                    PERF LAW's ceiling for JS-driven paint) - reads
 *                    stats().windowLeftMs / windowMs; folds if CORE never
 *                    reports them.
 *   UNRELIABLE LABEL an option's TEXT briefly wears another option's text.
 *                    The keycap (drawn from data-i by style.js) and the hitbox
 *                    are the truth - Law IV, and the law of this card: the lie
 *                    SNAPS TO TRUTH BEFORE THE WINDOW'S LAST 40% (it ends by
 *                    60% of the window, with a margin), so the honest read is
 *                    always on the glass while the ring is low. Restores the
 *                    EXACT string it read; if CORE repainted the option under
 *                    the lie, the restore stands down instead of stomping it.
 *   GHOST CURSOR     (tier 3+, mouse only) a faint cursor echo trails the real
 *                    pointer by ~430ms during a stop, and after a ~600ms stall
 *                    it stops trailing and DRIFTS toward an option the house
 *                    prefers - a WRONG one when CORE tells us the truth
 *                    (stats().truth = the true data-i), else the one farthest
 *                    from the hand. Pure suggestion: pointer-events none, it
 *                    cannot click, the real cursor is never hidden, it dies on
 *                    the answer.
 *   DECOY PLANTS     are NATIVE to CORE (SetPiece-gated sub_flash / audio over
 *                    the freeze). Nothing here; casino.plantResisted() pays the
 *                    resist.
 *
 * DEALING RULE: card slots are dealt at start() from the seeded plan: budget
 * 2/4/6/8 by tier, at most 4 in any rolling 60s, a 4s floor between two cards.
 * A VIGIL card (flicker) is dealt at a seeded time inside the class, never in
 * the first 8s or the last 12s, and re-queues politely (then folds) if its
 * moment arrives halted or on a live slip. A STOP card (label / clock / ghost)
 * is dealt to a seeded STOP ORDINAL (1-based) and plays from afterStop(); an
 * ordinal the class never reaches simply folds. Tier 1 deals flicker only;
 * the clock and the label from tier 2; the ghost from tier 3.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the flicker writes chip TEXT and restores via chipText
 *       (the truth source); the label writes .g-ir-opt TEXT and restores the
 *       string it read (or stands down); the ring is a node this deck owns.
 *       Nothing reads or writes the ledger, the stops, the streak, the window
 *       timer, the options' order, data-i or the grade.
 *   II  input honest  - no card is clickable; the ghost and the ring are
 *       pointer-events:none; nothing is mounted inside an option; no hitbox
 *       moves, resizes or is covered by a hit-testing node.
 *   III never still   - the ghost pulses on the lure, the ring runs.
 *   IV  images > text - this deck renders NO text of its own and mints no
 *       lexicon key (the label only re-wears a string the slip already shows).
 *   V   seeded        - per-tag mulberry32 streams off seed+'|ir-trickster|',
 *       append-only tags. A retake replays the identical deal.
 *   VI  exits sacred  - capsOk false disarms the whole deck (no nodes, no
 *       listener, no timers); reduced motion drops the ghost cursor AND the
 *       crooked ring entirely (the flicker is a number and the label a string:
 *       neither is motion); every timer rides the game's registry via
 *       opts.timers AND a local set; stop() restores every live lie; destroy()
 *       leaves no node, timer or listener.
 *   VII strings       - none. This file has no lexicon rows.
 *   PERF LAW          - nothing here touches .g-ir-tile; the only JS-driven
 *       paint is the ring at 8Hz and the ghost at 8Hz, both chrome.
 *
 * ENGINE PLACEMENT: entirely game-local. `opts.engine` is accepted for
 * interface symmetry and deliberately unused.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const IR_TRICKSTER = Object.freeze({
  /** Dealt card slots per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** The House's per-minute cap + a floor so two cards never land in one breath. */
  RATE_WINDOW_MS: 60000,
  RATE_MAX: 4,
  MIN_GAP_MS: 4000,
  /** Vigil cards: never in the first 8s, never in the last 12s. */
  FIRST_MS: 8000,
  TAIL_MS: 12000,
  /** A vigil card whose moment arrives halted / on a slip waits, then folds. */
  RETRY_MS: 1500,
  RETRY_MAX: 6,
  /** Card gates. */
  LABEL_FROM_TIER: 2,
  CLOCK_FROM_TIER: 2,
  GHOST_FROM_TIER: 3,
  /** Slot weights per tier (renormalised over the eligible cards). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ flicker: 1, label: 0, clock: 0, ghost: 0 }),
    2: Object.freeze({ flicker: 0.34, label: 0.36, clock: 0.3, ghost: 0 }),
    3: Object.freeze({ flicker: 0.22, label: 0.32, clock: 0.22, ghost: 0.24 }),
    4: Object.freeze({ flicker: 0.2, label: 0.32, clock: 0.22, ghost: 0.26 }),
  }),
  /** Stat flicker: how long the wrong number stands. */
  FLICKER_MS: 420,
  /** Unreliable label: the lie starts a beat after the slip lands and MUST be
   *  off before this fraction of the window has elapsed (Law: before the last
   *  40% - i.e. by 60% - with a margin). */
  LABEL_START_MS: Object.freeze([240, 600]),
  LABEL_MS: 700,
  LABEL_SNAP_FRAC: 0.6,
  LABEL_SNAP_MARGIN_MS: 150,
  /** The window fallback by tier when stats() carries no windowMs. */
  WINDOW_FALLBACK_MS: Object.freeze({ 1: 6000, 2: 6000, 3: 5000, 4: 4000 }),
  /** The crooked ring: how far the face runs ahead at mid-window, and the
   *  honest tail (the last 15% is the truth, to the pixel). */
  RING_BEND: 0.16,
  RING_HONEST: 0.15,
  RING_RAMP: 0.32,
  RING_TICK_MS: 125,                          // 8Hz - the PERF LAW ceiling
  /** The ghost: trail lag, stall before the lure, lure easing, tick (8Hz). */
  GHOST_TRAIL_MS: 430,
  GHOST_TRAIL_KEEP_MS: 1400,
  GHOST_STALL_MS: 600,
  GHOST_LERP: 0.18,
  GHOST_TICK_MS: 125,
});

const STYLE_ID = 'g-ir-trickster-style';
const STYLE_TEXT = `
/* CROOKED CLOCK: an overlay ring on the slip over the real face. It reads
   --ir-tk-k (1 full -> 0 out), written at 8Hz by the deck; the real .g-ir-timer
   steps aside ONLY while ours is up. */
.g-ir-tk-ring{position:absolute;right:-14px;top:-14px;width:44px;height:44px;border-radius:50%;pointer-events:none;opacity:0;
  background:conic-gradient(var(--pink) calc(var(--ir-tk-k,1) * 360deg), rgba(60,40,90,.18) 0);
  -webkit-mask:radial-gradient(circle, transparent 13px, #000 14px);mask:radial-gradient(circle, transparent 13px, #000 14px);
  box-shadow:0 0 14px rgba(255,105,180,.35);z-index:3}
.g-ir-tk-ring.is-on{opacity:1}
.g-ir-timer.g-ir-tk-bent{opacity:0}

/* GHOST CURSOR: a will-o-wisp echo of the player's own hand. It cannot click,
   it never hides the real cursor, and it dies on the answer. */
.g-ir-tk-ghost{position:absolute;left:0;top:0;width:15px;height:19px;opacity:0;pointer-events:none;z-index:9;
  will-change:transform;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, rgba(255,105,180,.9), rgba(184,166,232,.55));
  filter:drop-shadow(0 0 6px rgba(255,105,180,.6));
  transition:transform .3s ease-out, opacity .38s ease}
.g-ir-tk-ghost.is-on{opacity:.32}
.g-ir-tk-ghost.is-lure{opacity:.55;animation:g-ir-tk-ghostpulse 1.5s ease-in-out infinite}
@keyframes g-ir-tk-ghostpulse{50%{filter:drop-shadow(0 0 12px rgba(255,105,180,.95))}}

/* STAT FLICKER: the static pop the wrong number corrects itself with. */
.g-ir-chip.g-ir-tk-flick{text-shadow:0 0 22px rgba(184,166,232,.75), 0 0 3px rgba(255,255,255,.5);
  animation:g-ir-tk-static .16s steps(3) 2}
@keyframes g-ir-tk-static{0%{filter:none}40%{filter:brightness(1.5) contrast(1.4)}100%{filter:none}}
/* UNRELIABLE LABEL: a shiver while the wrong words are worn (the keycap is still) */
.g-ir-opt.g-ir-tk-lie{animation:g-ir-tk-shiver .5s ease-in-out infinite}
@keyframes g-ir-tk-shiver{0%,100%{text-shadow:none}50%{text-shadow:0 0 8px rgba(184,166,232,.6)}}

@media (prefers-reduced-motion: reduce){
  .g-ir-tk-ring{opacity:0 !important}
  .g-ir-timer.g-ir-tk-bent{opacity:1}
  .g-ir-tk-ghost{opacity:0 !important;animation:none}
  .g-ir-chip.g-ir-tk-flick,.g-ir-opt.g-ir-tk-lie{animation:none}
}
html.arc-reduced .g-ir-tk-ring{opacity:0 !important}
html.arc-reduced .g-ir-timer.g-ir-tk-bent{opacity:1}
html.arc-reduced .g-ir-tk-ghost{opacity:0 !important;animation:none}
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
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function lerp(band, t) { return band[0] + (band[1] - band[0]) * clamp01(t); }

/**
 * The crooked face, pure: the SHOWN fraction-left for a TRUE fraction-left.
 * f(1)=1, f(honest)=honest, monotonic (bend*pi < 1), honest at/after the
 * honest tail. Ahead through the middle (less time shown than real), crawling
 * as the window runs out, so the face meets the truth instead of snapping.
 */
export function bendRing(trueLeft, bend, honest) {
  const x = clamp01(trueLeft);
  const H = Number.isFinite(honest) ? honest : IR_TRICKSTER.RING_HONEST;
  if (x <= H || x >= 1) return x;
  const A0 = Number.isFinite(bend) ? bend : IR_TRICKSTER.RING_BEND;
  const ramp = clamp01((x - H) / IR_TRICKSTER.RING_RAMP);
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, x - A * Math.sin(Math.PI * x));
}
/** When the label lie must be OFF (ms after the slip landed). Pure. */
export function labelSnapDeadline(windowMs) {
  const w = Math.max(1000, Number(windowMs) || 0);
  return Math.round(w * IR_TRICKSTER.LABEL_SNAP_FRAC - IR_TRICKSTER.LABEL_SNAP_MARGIN_MS);
}

function el(tag, cls, parent) {
  try {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    if (parent && parent.appendChild) parent.appendChild(n);
    return n;
  } catch (e) { return null; }
}
function setCls(n, cls, on) { try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ } }
function setVar(n, k, v) { try { if (n && n.style) n.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
function nowMs() {
  try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
  return Date.now();
}
function probeCoarse() {
  try { return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches; } catch (e) { return false; }
}

/**
 * @param {Object} o
 *   seed, tier, timers {after,every,clear}, reduced, capsOk (bool | fn), isHalted () => bool,
 *   t (unused - no strings), stats () => {secLeft, budgetSec?, stops, stopsTotal|total, streak,
 *   inStop|frozen, windowMs?, windowLeftMs?, truth?}, windowLeft? () => ms (CORE's live
 *   accessor), chipEl (which: clock|stops|density|timer) => el, chipText (which) => string,
 *   optEls () => el[], optText (i) => string, stopEl?, stage?, announce?, log.
 *   Without stopEl the slip is FOUND from chipEl('timer') / option 0 (CORE hands neither
 *   stopEl nor stage).
 */
export function createIrTrickster(o) {
  const opts = o || {};
  const K = IR_TRICKSTER;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const coarse = probeCoarse();
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const optEls = typeof opts.optEls === 'function' ? opts.optEls : () => [];
  const optText = typeof opts.optText === 'function' ? opts.optText : () => null;
  const windowLeftFn = typeof opts.windowLeft === 'function' ? opts.windowLeft : null;
  /** THE SLIP. CORE hands no stopEl: it is found from the handles it does hand
   *  over - the real timer (chipEl('timer')) sits inside it, so does option 0. */
  function stopHost() {
    if (opts.stopEl) return opts.stopEl;
    try { const tm = chipEl('timer'); if (tm && tm.parentNode) return tm.parentNode; } catch (e) { /* noop */ }
    try {
      const els = optEls() || [];
      const b = els[0];
      if (b && typeof b.closest === 'function') { const h = b.closest('.g-ir-stop'); if (h) return h; }
      if (b && b.parentNode && b.parentNode.parentNode) return b.parentNode.parentNode;
    } catch (e) { /* noop */ }
    try {
      const c = chipEl('clock');
      if (c && typeof c.closest === 'function') { const st = c.closest('.g-ir-stage'); if (st && st.querySelector) { const h = st.querySelector('.g-ir-stop'); if (h) return h; } }
    } catch (e) { /* noop */ }
    return opts.stage || null;
  }
  /** ms left in the live window, from stats() or CORE's accessor; null when unknown. */
  function winLeftMs() {
    try { const st = stats(); if (st && Number.isFinite(Number(st.windowLeftMs))) return Number(st.windowLeftMs); } catch (e) { /* noop */ }
    if (windowLeftFn) { try { const v = Number(windowLeftFn()); if (Number.isFinite(v)) return v; } catch (e) { /* noop */ } }
    return null;
  }
  function capsOkNow() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false;
  }
  const armed = capsOkNow() && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';

  /* ---- timers: the game's registry + a local set ---------------------- */
  const live = new Set();
  const chains = new Set();
  const cancelFn = opts.timers && (opts.timers.clear || opts.timers.cancel);
  let destroyed = false;
  let stopped = false;
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
  function cancelAll() {
    for (const id of Array.from(live)) cancel(id);
    live.clear();
    for (const h of Array.from(chains)) stopEvery(h);
  }

  /* ---- seeded streams (Law V; append-only tags) ----------------------- */
  const seedBase = String(opts.seed || 'ir') + '|ir-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  const fired = { flicker: 0, label: 0, clock: 0, ghost: 0, lure: 0, folded: 0 };
  const fireTimes = [];                 // rolling per-minute cap
  function rateOk() {
    const t = nowMs();
    while (fireTimes.length && t - fireTimes[0] > K.RATE_WINDOW_MS) fireTimes.shift();
    if (fireTimes.length >= K.RATE_MAX) return false;
    if (fireTimes.length && t - fireTimes[fireTimes.length - 1] < K.MIN_GAP_MS) return false;
    return true;
  }
  function mark() { fireTimes.push(nowMs()); }

  /* ------------------------------------------------------------- the deal */
  let plan = [];
  let stopOrdinal = 0;               // stops seen so far (1-based after the first)
  let inStop = false;
  let stopAt = 0;                    // nowMs when the slip landed
  let windowNow = 0;
  function windowFallback() { return K.WINDOW_FALLBACK_MS[tier] || 5000; }
  function stopsTotalGuess() {
    try { const s = stats(); if (s && Number(s.stopsTotal) > 0) return Number(s.stopsTotal); if (s && Number(s.total) > 0) return Number(s.total); } catch (e) { /* noop */ }
    return ({ 1: 2, 2: 3, 3: 4, 4: 3 })[tier] || 3;
  }
  function budgetMs() {
    try { const s = stats(); if (s && Number(s.budgetSec) > 0) return Number(s.budgetSec) * 1000; } catch (e) { /* noop */ }
    return 120000;
  }
  function buildPlan() {
    const n = K.DEALS[tier] || 2;
    const w = K.WEIGHTS[tier] || K.WEIGHTS[1];
    const eligible = [['flicker', w.flicker], ['label', tier >= K.LABEL_FROM_TIER ? w.label : 0],
      ['clock', tier >= K.CLOCK_FROM_TIER ? w.clock : 0], ['ghost', tier >= K.GHOST_FROM_TIER && !reduced && !coarse ? w.ghost : 0]];
    let total = 0;
    for (const e of eligible) total += e[1];
    const total_stops = stopsTotalGuess();
    const span = budgetMs();
    const usable = Math.max(10000, span - K.FIRST_MS - K.TAIL_MS);
    const out = [];
    const stopsUsed = {};              // ordinal -> {label, clock, ghost} one of each per stop at most
    for (let i = 0; i < n; i++) {
      let r = roll('card') * total;
      let card = 'flicker';
      for (const e of eligible) { r -= e[1]; if (r <= 0 && e[1] > 0) { card = e[0]; break; } }
      if (card === 'flicker') {
        out.push({ card, at: Math.round(K.FIRST_MS + roll('when') * usable), ordinal: 0, fired: false });
      } else {
        // a stop card: a seeded ordinal; one of each card per stop, else re-roll once then fold to flicker
        let ord = 1 + Math.floor(roll('ord') * total_stops);
        if (stopsUsed[ord] && stopsUsed[ord][card]) ord = 1 + Math.floor(roll('ord') * total_stops);
        if (stopsUsed[ord] && stopsUsed[ord][card]) {
          out.push({ card: 'flicker', at: Math.round(K.FIRST_MS + roll('when') * usable), ordinal: 0, fired: false });
          continue;
        }
        stopsUsed[ord] = stopsUsed[ord] || {};
        stopsUsed[ord][card] = true;
        out.push({ card, at: 0, ordinal: ord, fired: false, delay: Math.round(lerp(K.LABEL_START_MS, roll('delay'))) });
      }
    }
    // vigil cards: sort + enforce the floor
    const vigil = out.filter((d) => d.card === 'flicker').sort((a, b) => a.at - b.at);
    for (let i = 1; i < vigil.length; i++) if (vigil[i].at - vigil[i - 1].at < K.MIN_GAP_MS) vigil[i].at = vigil[i - 1].at + K.MIN_GAP_MS;
    return out;
  }

  /* -------------------------------------------------------- stat flicker */
  let flickTimer = 0;
  let flickChip = null;
  let flickPrev = null;
  let flickLie = null;
  function dealFlicker(deal, tries) {
    if (destroyed || stopped) return;
    if (isHalted() || inStop) {
      if (tries < K.RETRY_MAX) after(K.RETRY_MS, () => dealFlicker(deal, tries + 1));
      else fired.folded++;
      return;
    }
    if (!rateOk()) { if (tries < K.RETRY_MAX) after(K.RETRY_MS, () => dealFlicker(deal, tries + 1)); else fired.folded++; return; }
    const which = roll('flick-which') < 0.6 ? 'stops' : 'clock';
    const chip = chipEl(which);
    const truth = chipText(which);
    if (!chip || typeof truth !== 'string' || !/\d/.test(truth)) { fired.folded++; return; }
    const m = truth.match(/\d+/);
    if (!m) { fired.folded++; return; }
    const nval = Number(m[0]);
    const delta = (roll('flick-sign') < 0.5 ? -1 : 1);
    const fake = Math.max(0, nval + delta);
    const lie = truth.replace(/\d+/, String(fake).padStart(m[0].length, '0'));
    if (lie === truth) { fired.folded++; return; }
    try { chip.textContent = lie; } catch (e) { return; }
    flickChip = chip; flickPrev = truth; flickLie = lie;
    if (!reduced) setCls(chip, 'g-ir-tk-flick', true);
    fired.flicker++;
    mark();
    deal.fired = true;
    cancel(flickTimer);
    flickTimer = after(K.FLICKER_MS, () => { flickTimer = 0; restoreFlick(); });
    say('trickster: stat flicker (' + which + ')');
  }
  function restoreFlick() {
    if (!flickChip) return;
    setCls(flickChip, 'g-ir-tk-flick', false);
    try {
      // stand down if a real repaint already landed under the lie
      if (flickChip.textContent === flickLie) {
        const now = chipText(flickChip === chipEl('stops') ? 'stops' : 'clock');
        flickChip.textContent = typeof now === 'string' ? now : flickPrev;
      }
    } catch (e) { /* noop */ }
    flickChip = null; flickPrev = null; flickLie = null;
  }

  /* ------------------------------------------------------- crooked clock */
  let ring = null;
  let ringTick = 0;
  let ringOn = false;
  let ringWrites = 0;
  let lastRingK = -1;
  function realTimer() {
    try { const tm = chipEl('timer'); if (tm) return tm; } catch (e) { /* noop */ }
    try {
      const s = stopHost();
      if (s && typeof s.querySelector === 'function') return s.querySelector('.g-ir-timer');
    } catch (e) { /* noop */ }
    return null;
  }
  function armRing() {
    if (reduced || ringOn || destroyed || stopped) return false;
    const face = realTimer();
    const host = face && face.parentNode ? face.parentNode : stopHost();
    if (!host || !host.appendChild) return false;
    if (!ring) { ensureStyle(); ring = el('i', 'g-ir-tk-ring', host); }
    if (!ring) return false;
    setCls(face, 'g-ir-tk-bent', true);
    setCls(ring, 'is-on', true);
    ringOn = true;
    lastRingK = -1;
    paintRing();
    ringTick = every(K.RING_TICK_MS, paintRing);
    fired.clock++;
    mark();
    say('trickster: the ring goes crooked');
    return true;
  }
  function paintRing() {
    if (!ringOn || destroyed) return;
    let s = null;
    try { s = stats(); } catch (e) { s = null; }
    let left = null;
    const wl = winLeftMs();
    if (s && Number.isFinite(Number(s.windowLeftMs)) && Number(s.windowMs) > 0) left = Number(s.windowLeftMs) / Number(s.windowMs);
    else if (wl != null && windowNow > 0) left = wl / windowNow;
    else if (windowNow > 0) left = 1 - (nowMs() - stopAt) / windowNow;
    if (left == null) { straightenRing(); return; }
    const k = bendRing(clamp01(left));
    const q = Math.round(k * 200) / 200;
    if (q === lastRingK) return;
    lastRingK = q;
    setVar(ring, '--ir-tk-k', q.toFixed(3));
    ringWrites++;
  }
  function straightenRing() {
    if (!ringOn) return;
    ringOn = false;
    if (ringTick) { stopEvery(ringTick); ringTick = 0; }
    setCls(ring, 'is-on', false);
    setCls(realTimer(), 'g-ir-tk-bent', false);
  }

  /* ---------------------------------------------------- unreliable label */
  let labelTimer = 0;
  let labelStartTimer = 0;
  let labelEl = null;
  let labelBtn = null;
  let labelPrev = null;
  let labelLie = null;
  let lastLabel = null;              // {startedAt, endedAt, deadline, windowMs} for diagnostics
  /** The node whose TEXT the label rewrites: CORE's .g-ir-opt-t span when the
   *  option has one, the bare button when it has no element children, nothing
   *  when it shows a face (img/video) - a face is never rewritten. */
  function labelSurface(btn) {
    try {
      if (btn && typeof btn.querySelector === 'function') {
        const span = btn.querySelector('.g-ir-opt-t');
        if (span) {
          if (span.querySelector && span.querySelector('img,video,canvas')) return null;
          return span;
        }
      }
      if (btn && btn.children && btn.children.length) return null;
      return btn || null;
    } catch (e) { return null; }
  }
  function dealLabel(deal) {
    if (destroyed || stopped || !inStop) return;
    const deadline0 = labelSnapDeadline(windowNow);
    const startAt = Math.min(Math.max(Number(deal.delay) || K.LABEL_START_MS[0], K.LABEL_START_MS[0]), Math.max(0, deadline0 - 200));
    if (Math.min(startAt + K.LABEL_MS, deadline0) - startAt < 120) { fired.folded++; deal.fired = true; return; }
    const ordinalStart = stopOrdinal;
    labelStartTimer = after(startAt, () => {
      labelStartTimer = 0;
      if (destroyed || stopped || !inStop || stopOrdinal !== ordinalStart) { fired.folded++; return; }
      /* the window may have started AFTER the slip beat (CORE deals +240ms):
       * bound it from the live left so the deadline is never later than truth */
      const elapsed = nowMs() - stopAt;
      const wl = winLeftMs();
      if (wl != null && wl > 0) windowNow = Math.min(windowNow, wl + elapsed);
      const deadline = labelSnapDeadline(windowNow);
      const endAt = Math.min(elapsed + K.LABEL_MS, deadline);
      if (endAt - elapsed < 120) { fired.folded++; return; }
      let els = [];
      try { els = Array.from(optEls() || []); } catch (e) { els = []; }
      els = els.filter((n) => n && n.textContent != null);
      if (els.length < 2) { fired.folded++; return; }
      const a = Math.floor(roll('label-a') * els.length);
      let b = Math.floor(roll('label-b') * (els.length - 1));
      if (b >= a) b += 1;
      const target = els[a];
      const donor = els[b];
      let truth = null; let lie = null;
      try {
        const ti = Number(target.getAttribute('data-i'));
        const di = Number(donor.getAttribute('data-i'));
        truth = optText(ti); lie = optText(di);
      } catch (e) { /* noop */ }
      const surface = labelSurface(target);
      const donorSurface = labelSurface(donor);
      if (!surface) { fired.folded++; return; }          // a media option: never rewrite a face
      if (typeof truth !== 'string') { try { truth = surface.textContent; } catch (e) { truth = null; } }
      if (typeof lie !== 'string') { try { lie = donorSurface ? donorSurface.textContent : null; } catch (e) { lie = null; } }
      if (typeof truth !== 'string' || typeof lie !== 'string' || !lie || lie === truth) { fired.folded++; return; }
      try { if (surface.textContent !== truth) { fired.folded++; return; } } catch (e) { return; }
      try { surface.textContent = lie; } catch (e) { return; }
      labelEl = surface; labelPrev = truth; labelLie = lie;
      if (!reduced) setCls(target, 'g-ir-tk-lie', true);
      labelBtn = target;
      fired.label++;
      mark();
      const startedAt = nowMs() - stopAt;
      lastLabel = { startedAt: Math.round(startedAt), deadline, windowMs: windowNow, endedAt: null };
      cancel(labelTimer);
      labelTimer = after(Math.max(60, endAt - elapsed), () => { labelTimer = 0; restoreLabel(); });
      say('trickster: unreliable label (' + a + ' wears ' + b + ', off by ' + endAt + 'ms of ' + windowNow + ')');
    });
    deal.fired = true;
  }
  function restoreLabel() {
    if (labelStartTimer) { cancel(labelStartTimer); labelStartTimer = 0; }
    if (labelTimer) { cancel(labelTimer); labelTimer = 0; }
    if (!labelEl) return;
    setCls(labelBtn || labelEl, 'g-ir-tk-lie', false);
    try {
      // stand down if CORE repainted the option under the lie
      if (labelEl.textContent === labelLie) labelEl.textContent = labelPrev;
    } catch (e) { /* noop */ }
    if (lastLabel && lastLabel.endedAt == null) lastLabel.endedAt = Math.round(nowMs() - stopAt);
    labelEl = null; labelBtn = null; labelPrev = null; labelLie = null;
  }

  /* --------------------------------------------------------- ghost cursor */
  let ghostEl = null;
  let ghostTimer = 0;
  let onMove = null;
  const trail = [];
  let lastMoveAt = 0;
  let gx = 0; let gy = 0;
  let ghostMode = 'off';
  let ghostArmedThisStop = false;
  function ghostHost() { return stopHost(); }
  function ghostEligible() {
    return armed && !reduced && !coarse && tier >= K.GHOST_FROM_TIER && !!ghostHost()
      && typeof ghostHost().addEventListener === 'function';
  }
  /** The house's preferred option, in host coordinates: a WRONG one when the
   *  truth is known, else the one farthest from the hand. */
  function lurePoint() {
    let els = [];
    try { els = Array.from(optEls() || []); } catch (e) { els = []; }
    if (!els.length) return null;
    let truth = null;
    try { const s = stats(); if (s && s.truth != null && Number.isFinite(Number(s.truth))) truth = Number(s.truth); } catch (e) { truth = null; }
    const host = rectOf(ghostHost());
    if (!host) return null;
    const pts = [];
    for (const n of els) {
      const r = rectOf(n);
      if (!r || !r.width) continue;
      let di = NaN;
      try { di = Number(n.getAttribute('data-i')); } catch (e) { di = NaN; }
      pts.push({ x: r.left + r.width / 2 - host.left, y: r.top + r.height / 2 - host.top, i: di });
    }
    if (!pts.length) return null;
    if (truth != null) {
      const wrong = pts.filter((p) => p.i !== truth);
      if (wrong.length) return wrong[Math.floor(roll('ghost-pick') * wrong.length)];
    }
    let best = pts[0]; let bd = -1;
    for (const p of pts) { const d = (p.x - gx) * (p.x - gx) + (p.y - gy) * (p.y - gy); if (d > bd) { bd = d; best = p; } }
    return best;
  }
  function ghostOff() {
    if (ghostMode === 'off') return;
    setCls(ghostEl, 'is-on', false);
    setCls(ghostEl, 'is-lure', false);
    ghostMode = 'off';
  }
  function ghostTick() {
    if (!ghostEl || destroyed || stopped) return;
    if (!capsOkNow() || !inStop || isHalted()) { ghostOff(); return; }
    const t = nowMs();
    const stalled = lastMoveAt > 0 && (t - lastMoveAt) >= K.GHOST_STALL_MS;
    if (stalled) {
      const p = lurePoint();
      if (p) { gx += (p.x - gx) * K.GHOST_LERP; gy += (p.y - gy) * K.GHOST_LERP; }
      if (ghostMode !== 'lure') { fired.lure++; ghostMode = 'lure'; }
      setCls(ghostEl, 'is-on', true);
      setCls(ghostEl, 'is-lure', true);
    } else {
      if (!trail.length || !lastMoveAt) { ghostOff(); return; }
      let pt = trail[0];
      for (const p of trail) { if (t - p.at >= K.GHOST_TRAIL_MS) pt = p; else break; }
      gx = pt.x; gy = pt.y;
      ghostMode = 'trail';
      setCls(ghostEl, 'is-on', true);
      setCls(ghostEl, 'is-lure', false);
    }
    try { ghostEl.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)'; } catch (e) { /* noop */ }
    while (trail.length > 1 && t - trail[0].at > K.GHOST_TRAIL_KEEP_MS) trail.shift();
  }
  function armGhost() {
    if (!ghostEligible() || ghostArmedThisStop) return false;
    const host = ghostHost();
    if (!ghostEl) {
      ensureStyle();
      ghostEl = el('i', 'g-ir-tk-ghost', host);
      if (!ghostEl) return false;
      onMove = (ev) => {
        if (destroyed) return;
        try {
          const s = rectOf(host);
          if (!s) return;
          const x = Number(ev && ev.clientX) - s.left;
          const y = Number(ev && ev.clientY) - s.top;
          if (!Number.isFinite(x) || !Number.isFinite(y)) return;
          lastMoveAt = nowMs();
          trail.push({ x, y, at: lastMoveAt });
        } catch (e) { /* no rects under the DOM double */ }
      };
      try { host.addEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    }
    ghostArmedThisStop = true;
    lastMoveAt = nowMs();       // a stall starts counting from the slip landing
    ghostTimer = every(K.GHOST_TICK_MS, ghostTick);
    fired.ghost++;
    mark();
    say('trickster: ghost armed');
    return true;
  }
  function disarmGhost() {
    if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
    ghostOff();
    ghostArmedThisStop = false;
  }
  function dropGhost() {
    disarmGhost();
    try {
      const host = ghostHost();
      if (onMove && host && host.removeEventListener) host.removeEventListener('pointermove', onMove);
    } catch (e) { /* noop */ }
    onMove = null;
    if (ghostEl) { try { ghostEl.remove(); } catch (e) { /* noop */ } }
    ghostEl = null;
    trail.length = 0;
  }

  /* ------------------------------------------------------------------ api */
  return {
    /** Deal the class. Call once, when the vigil begins. */
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      plan = buildPlan();
      for (const d of plan) if (d.card === 'flicker') after(d.at, () => dealFlicker(d, 0));
      say('trickster: dealt ' + plan.length + ' cards ('
        + plan.map((d) => d.card + (d.ordinal ? '@stop' + d.ordinal : '@' + Math.round(d.at / 1000) + 's')).join(', ') + ')');
    },
    /** The slip landed (CORE calls after the options are in the DOM). */
    afterStop() {
      if (!armed || destroyed || stopped) return;
      stopOrdinal += 1;
      inStop = true;
      stopAt = nowMs();
      let s = null;
      try { s = stats(); } catch (e) { s = null; }
      windowNow = s && Number(s.windowMs) > 0 ? Number(s.windowMs) : windowFallback();
      for (const d of plan) {
        if (d.fired || d.ordinal !== stopOrdinal) continue;
        if (!rateOk()) { fired.folded++; d.fired = true; continue; }
        if (d.card === 'label') dealLabel(d);
        else if (d.card === 'clock') { d.fired = true; if (!armRing()) fired.folded++; }
        else if (d.card === 'ghost') { d.fired = true; if (!armGhost()) fired.folded++; }
      }
    },
    /** The answer committed (or timed out): every stop lie comes off. */
    afterAnswer() {
      if (!armed || destroyed) return;
      inStop = false;
      restoreLabel();
      straightenRing();
      disarmGhost();
    },
    /** ms since the last input; 0 resets. (Accepted for the contract; the
     *  ghost's own stall clock runs on pointermove - this only ends theatre.) */
    stalled(ms) {
      if (!armed || destroyed) return;
      if ((Number(ms) || 0) <= 0) ghostOff();
    },
    /** The class is over: no card may fire again, every lie comes off. */
    stop() {
      stopped = true;
      inStop = false;
      cancelAll();
      restoreFlick();
      restoreLabel();
      straightenRing();
      disarmGhost();
    },
    destroy() {
      destroyed = true;
      stopped = true;
      cancelAll();
      restoreFlick();
      restoreLabel();
      straightenRing();
      dropGhost();
      if (ring) { try { ring.remove(); } catch (e) { /* noop */ } }
      ring = null;
    },
    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, tier, reduced, coarse,
        plan: plan.map((d) => ({ card: d.card, at: d.at, ordinal: d.ordinal, fired: d.fired })),
        fired: Object.assign({}, fired), stopOrdinal, inStop, windowMs: windowNow,
        ring: { on: ringOn, writes: ringWrites, lastK: lastRingK },
        label: lastLabel ? Object.assign({}, lastLabel) : null, labelLive: !!labelEl,
        ghost: { el: !!ghostEl, mode: ghostMode, armed: ghostArmedThisStop },
        flickLive: !!flickChip, timers: live.size,
      };
    },
  };
}

export default createIrTrickster;
