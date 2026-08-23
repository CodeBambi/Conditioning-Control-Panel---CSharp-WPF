/* ============================================================================
 * games/misdirection/trickster.js - DECK III of the House Rules, dealt into the
 * shell game. A tracking room is where your own eyes are the witness, so every
 * card here attacks what you READ around the shells - never the shells' truth,
 * never the pick window underneath.
 *
 *   STAT FLICKER     the pot or the streak chip briefly reads slightly off,
 *                    then "corrects" with a static pop to the EXACT text the
 *                    truth source hands back. The ledger never moves;
 *                    confidence does.
 *   CROOKED CLOCK    (the pick-window ring) while a pick window is open this
 *                    deck paints a stopwatch face of its own under the arc whose
 *                    FILL bends: it races through the boring middle (shows less
 *                    time than you really have) and crawls at the end, meeting
 *                    the truth exactly in the last 15% - so the hand itches
 *                    early and the finish is never a lie. The real window is
 *                    index.js's timer and untouched; the bend is a PURE function,
 *                    exported. If index.js hands us its own ring (opts.ringEl)
 *                    that face steps aside (.g-md-tk-bent) only while ours is up.
 *   FAKE SHUFFLE     (tier 3+ cameo) during the pick window two neighbouring
 *                    cups FEINT - the bodies lean toward each other and snap
 *                    back - and nothing moves: --slot / --x and the hitboxes
 *                    are never touched (the lean rides --md-feint on the cup
 *                    BODY, style.js composes it under the lid/face, the hitbox
 *                    is the shell node and stays put). No tell: no cue, no
 *                    flash, no word.
 *   THE MELT         a dealt card that plays on the next swap (the blackout
 *                    zone of a shuffle): one NON-target cup sags like wax for
 *                    ~600ms (a class on the shell; the lid and the tag deform,
 *                    the transform never does) and snaps back. Which cup melts
 *                    is the seed's choice; the target is read (never written)
 *                    through opts.targetSlot so the melt never brands the truth.
 *                    Without that hook the deck melts any cup - it will never
 *                    guess.
 *   GHOST CURSOR     a faint cursor echo trails the real pointer by ~430ms
 *                    during the pick window, and after a ~600ms stall it stops
 *                    trailing and DRIFTS toward a NEAR cup - the neighbour of
 *                    the one under the hand, the seed picks the side - the house
 *                    leaning on your hand. Pure suggestion: pointer-events none,
 *                    it cannot click, the real cursor is never hidden, and it
 *                    dies on the pick. Tier 2+, mouse pointers only.
 *   DECOY REVEAL     is NATIVE - index.js's own schedule (the dossier's
 *                    signature twist, bound by the trackability invariant).
 *                    This deck never lifts a lid.
 *
 * DEALING RULE: flicker / clock / feint / melt are dealt at start() from the
 * seeded plan against the class budget - 2/4/6/8 by tier, at least 15s apart
 * (the House's 4-a-minute cap, stated as a gap), never in the first 10s, never
 * in the last 15s (the bell's honest zone); a card whose moment arrives halted
 * waits politely, then folds. The ghost is stall-reactive (the stall is the
 * player's own) but the side it lures to is the seed's.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - flicker writes chip TEXT and restores via chipText
 *       (the truth source); the ring is a node this deck owns; the feint and
 *       the melt are classes/vars on the cup BODY; nothing reads or writes
 *       pot, streak, round, the pick window or the shuffle plan.
 *   II  input honest  - no card is clickable; every node lives in the deck's
 *       own layer inside the stage, pointer-events:none; the shell node (the
 *       hitbox) is never transformed, moved, resized or delayed.
 *   III never still   - the ghost pulses on the lure; the ring face breathes.
 *   IV  images > text - two proctor lines only (md_trick_melt once, md_trick_seen
 *       after a feint's verdict), through the t() this deck is handed; neutral
 *       fallbacks here; nothing else is rendered as text.
 *   V   seeded        - per-tag mulberry32 off seed+'|md-trickster|<tag>',
 *       append-only tags. A retake replays the identical deal.
 *   VI  exits sacred  - capsOk false disarms the deck (no nodes, no listener,
 *       no timers); reduced motion drops the ghost, the feint and the melt
 *       (motion) and keeps the flicker and the bend (a number and a fill are
 *       not motion); every timer rides the game's registry AND a local set;
 *       pause() restores every live lie, resume() re-arms, destroy() leaves
 *       no node, timer, observer or listener.
 *   VII strings       - md_trick_melt / md_trick_seen (lex.js rows), never
 *       invented here.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const MD_TRICKSTER = Object.freeze({
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  FIRST_DEAL_MS: 10000,
  TAIL_MS: 15000,
  MIN_GAP_MS: 15000,
  MIN_GAP_FLOOR_MS: 9000,        // a short class seats its cards closer, never under this
  RETRY_MS: 1500,
  RETRY_MAX: 6,
  /** Card gates. */
  FLICK_FROM_TIER: 1,
  MELT_FROM_TIER: 1,
  CLOCK_FROM_TIER: 2,
  GHOST_FROM_TIER: 2,
  FEINT_FROM_TIER: 3,
  /** Slot weights per tier over flicker / melt / feint (the clock is one slot). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ flicker: 0.45, melt: 0.55, feint: 0 }),
    2: Object.freeze({ flicker: 0.5, melt: 0.5, feint: 0 }),
    3: Object.freeze({ flicker: 0.36, melt: 0.34, feint: 0.3 }),
    4: Object.freeze({ flicker: 0.32, melt: 0.3, feint: 0.38 }),
  }),
  /** Stat flicker. */
  FLICK_MS: 460,
  /** The crooked ring: bend at mid-window, honest tail, ramp, baked stops. */
  RING_BEND: 0.16,
  RING_HONEST: 0.15,
  RING_RAMP: 0.32,
  RING_STOPS: 20,
  RING_DEFAULT_MS: 4000,
  RING_LATCH_MS: 30000,          // an armed clock waits this long for a pick window, then folds
  /** The feint. */
  FEINT_MS: 260,
  FEINT_PX: 9,
  FEINT_LATCH_MS: 20000,
  /** The melt. */
  MELT_MS: 600,
  MELT_LATCH_MS: 14000,
  /** After a feint's verdict, the one taunt (md_trick_seen) - seeded chance. */
  SEEN_TAUNT_CHANCE: 0.3,
  /** The ghost. */
  GHOST_TICK_MS: 90,
  GHOST_TRAIL_MS: 430,
  GHOST_STALL_MS: 600,
  GHOST_LERP: 0.16,
  GHOST_TRAIL_KEEP_MS: 2200,
  PHASE_POLL_MS: 120,
});

const STYLE_ID = 'g-md-trickster-style';
const STYLE_TEXT = `
@property --md-tk-k{syntax:'<number>';inherits:false;initial-value:1}
.g-md-tk{position:absolute;inset:0;pointer-events:none;z-index:7;overflow:visible}
.g-md-tk *{pointer-events:none}
/* THE CROOKED RING: a stopwatch face under the arc. --md-tk-k is the SHOWN
   fraction left (1 -> 0); the keyframes bake the bend. */
.g-md-tk-ring{position:absolute;left:50%;bottom:4%;width:54px;height:54px;transform:translateX(-50%);
  border-radius:50%;opacity:0;--md-tk-k:1;
  background:conic-gradient(from -90deg, hsl(var(--md-n-hue-b,330) 90% 78% / .9) 0deg, hsl(var(--md-n-hue-b,330) 90% 78% / .9) calc(var(--md-tk-k) * 360deg), rgba(255,255,255,.08) calc(var(--md-tk-k) * 360deg));
  -webkit-mask:radial-gradient(circle, transparent 58%, #000 60%, #000 100%);mask:radial-gradient(circle, transparent 58%, #000 60%, #000 100%);
  filter:drop-shadow(0 0 8px hsl(var(--md-n-hue-b,330) 90% 70% / .6));transition:opacity .2s ease}
.g-md-tk-ring.on{opacity:.9;animation:g-md-tk-bend var(--md-tk-ms,4s) linear 1 forwards}
.g-md-tk-bent{opacity:0 !important}
/* GHOST CURSOR */
.g-md-tk-ghost{position:absolute;left:0;top:0;width:15px;height:19px;opacity:0;will-change:transform;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, rgba(255,105,180,.9), rgba(184,166,232,.55));
  filter:drop-shadow(0 0 6px rgba(255,105,180,.6));transition:transform .34s ease-out, opacity .38s ease}
.g-md-tk-ghost.on{opacity:.3}
.g-md-tk-ghost.lure{opacity:.52;animation:g-md-tk-ghostpulse 1.5s ease-in-out infinite}
@keyframes g-md-tk-ghostpulse{50%{filter:drop-shadow(0 0 12px rgba(255,105,180,.95))}}
/* STAT FLICKER: the static pop the wrong number corrects itself with */
.g-md-tk-flick{text-shadow:0 0 22px rgba(184,166,232,.75), 0 0 3px rgba(255,255,255,.5);animation:g-md-tk-static .16s steps(3) 2}
@keyframes g-md-tk-static{0%{filter:none}40%{filter:brightness(1.5) contrast(1.4)}100%{filter:none}}
@media (prefers-reduced-motion: reduce){
  .g-md-tk-ghost{display:none}
  .g-md-tk-flick{animation:none}
  .g-md-tk-ring.on{animation:none;--md-tk-k:.5}
}
html.arc-reduced .g-md-tk-ghost{display:none}
.g-md-stage.suspended .g-md-tk-ring.on,.g-md-stage.suspended .g-md-tk-ghost.lure{animation-play-state:paused !important}
`;

function ensureStyle(extra) {
  try {
    if (typeof document === 'undefined' || !document.head) return;
    if (document.getElementById(STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = STYLE_TEXT + (extra || '');
    document.head.appendChild(s);
  } catch (e) { /* cosmetic */ }
}

/* ---------------------------------------------------------------- pure ---- */
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }

/**
 * The crooked face, pure: the SHOWN fraction left for a real fraction left
 * (1 = window just opened, 0 = shut). Ahead through the middle (less shown than
 * real), honest for the last `honest` of the window, f(1)=1, monotone for
 * bend*pi < 1.
 */
export function bendRing(fracLeft, bend, honest, ramp) {
  const x = clamp01(fracLeft);
  const H = honest == null ? MD_TRICKSTER.RING_HONEST : honest;
  if (x <= H || x >= 1) return x;
  const A0 = bend == null ? MD_TRICKSTER.RING_BEND : bend;
  const R = ramp == null ? MD_TRICKSTER.RING_RAMP : ramp;
  const r = Math.max(0, Math.min(1, (x - H) / R));
  const A = A0 * r * r * (3 - 2 * r);
  return Math.max(H, Math.min(1, x - A * Math.sin(Math.PI * x)));
}
/** The bend baked into CSS keyframes (N stops), for the ring's animation. Pure. */
export function bendKeyframes(name, stops) {
  const n = Math.max(4, stops | 0);
  let css = '@keyframes ' + name + '{';
  for (let i = 0; i <= n; i++) {
    const p = i / n;                                   // 0 = open, 1 = shut
    css += (p * 100).toFixed(1) + '%{--md-tk-k:' + bendRing(1 - p).toFixed(4) + '}';
  }
  return css + '}';
}
/** The "near" cup for a lure: the slot beside `slot`, the seed's side. Pure. */
export function nearSlot(slot, count, side01) {
  const n = Math.max(2, count | 0);
  const s = Math.max(0, Math.min(n - 1, slot | 0));
  const right = side01 < 0.5;
  if (s === 0) return 1;
  if (s === n - 1) return n - 2;
  return right ? s + 1 : s - 1;
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
function rmVar(n, k) { try { if (n && n.style) n.style.removeProperty(k); } catch (e) { /* noop */ } }
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
function slotOf(shell) {
  try { const n = Number(shell.getAttribute('data-slot')); return Number.isFinite(n) ? n : -1; } catch (e) { return -1; }
}
function nowMs() {
  try { if (typeof performance !== 'undefined' && typeof performance.now === 'function') return performance.now(); } catch (e) { /* fall */ }
  return Date.now();
}
function isCoarse() {
  try { return typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches; } catch (e) { return false; }
}

/**
 * @param {Object} o
 *   seed, tier, timers {after,every,clear}, reduced, capsOk (bool|fn), isHalted () => bool,
 *   coarse (touch), t (the two proctor lines), stats () => {round,pot,streak,secLeft,phase,...},
 *   pickWindow () => {open, elapsedMs, totalMs} (the HONEST window - the ring bends a copy),
 *   chipEl (which) => el, chipText (which) => string, stage (.g-md-stage), table (.g-md-table),
 *   shells () => HTMLElement[] (else queried), targetSlot () => number|null (truth read, optional),
 *   ringEl () => el|null (index.js's own pick ring, optional), announce (text, ms) (optional),
 *   budgetSec, log
 */
export function createMdTrickster(o) {
  const opts = o || {};
  const T = MD_TRICKSTER;
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const tier = clampTier(opts.tier);
  const reduced = !!opts.reduced;
  const coarse = opts.coarse === true || isCoarse();
  const t = typeof opts.t === 'function' ? opts.t : ((k, f) => f);
  const announce = typeof opts.announce === 'function' ? opts.announce : null;
  const pickWindow = typeof opts.pickWindow === 'function' ? opts.pickWindow : () => null;
  const lexiconUsed = new Set();
  function say_t(key, fallback) { lexiconUsed.add(key); return t(key, fallback); }
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const chipEl = typeof opts.chipEl === 'function' ? opts.chipEl : () => null;
  const chipText = typeof opts.chipText === 'function' ? opts.chipText : () => null;
  const targetSlot = typeof opts.targetSlot === 'function' ? opts.targetSlot : () => null;
  const ringElOf = typeof opts.ringEl === 'function' ? opts.ringEl : () => null;
  const stage = opts.stage || null;
  const table = opts.table || stage;
  function capsOkNow() {
    if (typeof opts.capsOk === 'function') { try { return !!opts.capsOk(); } catch (e) { return false; } }
    return opts.capsOk !== false;
  }
  const armed = capsOkNow() && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';

  /* timers: the game's registry + a local set (see casino.js). */
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

  const seedBase = String(opts.seed || 'md') + '|md-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  const fired = { flicker: 0, clock: 0, feint: 0, melt: 0, lure: 0 };
  let deals = [];
  let layer = null;
  let ringEl = null;
  let ghostEl = null;

  /* ------------------------------------------------------------- helpers */
  function shells() {
    let list = [];
    try {
      if (typeof opts.shells === 'function') list = Array.from(opts.shells() || []);
      else if (table && table.querySelectorAll) list = Array.from(table.querySelectorAll('.g-md-shell'));
    } catch (e) { list = []; }
    return list.filter((n) => n && n.style && slotOf(n) >= 0);
  }
  function phaseNow() {
    try {
      const s = stats();
      if (s && s.phase) return String(s.phase);
      if (stage && stage.getAttribute) return String(stage.getAttribute('data-phase') || '');
    } catch (e) { /* fall */ }
    return '';
  }
  function mountLayer() {
    if (layer || !stage || !stage.appendChild) return;
    ensureStyle(bendKeyframes('g-md-tk-bend', T.RING_STOPS));
    layer = el('div', 'g-md-tk', stage);
  }

  /* ------------------------------------------------------------- the deal */
  function buildDeals() {
    const n = T.DEALS[tier] || 2;
    const spanMs = Math.max(60, Number(opts.budgetSec) || 120) * 1000;
    const usable = Math.max(15000, spanMs - T.FIRST_DEAL_MS - T.TAIL_MS);
    /* the gap: 15s, or less when the budget cannot seat every card 15s apart
       (the contract's 2/4/6/8 is the promise; the floor is 9s = under 7/min) */
    const gap = Math.max(T.MIN_GAP_FLOOR_MS, Math.min(T.MIN_GAP_MS, n > 1 ? Math.floor(usable / (n - 1)) : T.MIN_GAP_MS));
    const times = [];
    for (let i = 0; i < n; i++) times.push(T.FIRST_DEAL_MS + roll('when') * usable);
    times.sort((a, b) => a - b);
    for (let i = 1; i < times.length; i++) {
      if (times[i] - times[i - 1] < gap) times[i] = times[i - 1] + gap;
    }
    /* the walk forward can push the tail past the honest zone: walk it back */
    const lastOk = spanMs - T.TAIL_MS;
    if (times[times.length - 1] > lastOk) {
      times[times.length - 1] = lastOk;
      for (let i = times.length - 2; i >= 0; i--) if (times[i + 1] - times[i] < gap) times[i] = times[i + 1] - gap;
      for (let i = 0; i < times.length; i++) times[i] = Math.max(T.FIRST_DEAL_MS, times[i]);
    }
    const clockSlot = tier >= T.CLOCK_FROM_TIER && n > 1 ? Math.floor(roll('clock-slot') * Math.ceil(n / 2)) : -1;
    const w = T.WEIGHTS[tier] || T.WEIGHTS[1];
    const out = [];
    for (let i = 0; i < times.length; i++) {
      const at = Math.round(times[i]);
      if (at > spanMs - T.TAIL_MS) continue;                  // the gap walk pushed it into the honest zone: fold it
      let card;
      if (i === clockSlot) card = 'clock';
      else {
        const r = roll('card');
        card = r < w.flicker ? 'flicker' : r < w.flicker + w.melt ? 'melt' : 'feint';
        if (card === 'feint' && tier < T.FEINT_FROM_TIER) card = 'melt';
      }
      out.push({ at, card });
    }
    return out;
  }
  function attempt(deal, tries) {
    if (destroyed || stopped) return;
    /* a halted class (occlusion, blackout, pause) deals nothing: the card waits */
    if (isHalted() || !stats()) {
      if (tries < T.RETRY_MAX) after(T.RETRY_MS, () => attempt(deal, tries + 1));
      return;
    }
    if (deal.card === 'melt') { armMelt(); return; }
    if (deal.card === 'feint') { armFeint(); return; }
    if (deal.card === 'clock') { armClock(); return; }
    if (deal.card === 'flicker') dealFlicker();
  }

  /* -------------------------------------------------------- stat flicker */
  let flickTimer = 0;
  let flickChip = null;
  function dealFlicker() {
    if (tier < T.FLICK_FROM_TIER) return;
    const s = stats();
    if (!s) return;
    const which = roll('flick-which') < 0.5 ? 'pot' : 'streak';
    const chip = chipEl(which);
    if (!chip || chip.textContent == null) return;
    const truth = chipText(which);
    const n = Number(which === 'pot' ? s.pot : s.streak) || 0;
    const delta = (roll('flick-sign') < 0.5 ? -1 : 1) * (1 + Math.floor(roll('flick-drop') * (which === 'pot' ? 3 : 1)));
    const fake = Math.max(0, n + delta);
    const lie = (typeof truth === 'string' && /\d/.test(truth)) ? truth.replace(/\d[\d,]*/, String(fake)) : String(fake);
    if (!lie || lie === chip.textContent) return;
    try { chip.textContent = lie; } catch (e) { return; }
    flickChip = chip;
    if (!reduced) setCls(chip, 'g-md-tk-flick', true);
    fired.flicker += 1;
    cancel(flickTimer);
    flickTimer = after(T.FLICK_MS, () => { flickTimer = 0; unflick(); });
    say('trickster: stat flicker (' + which + ')');
  }
  function unflick() {
    if (!flickChip) return;
    setCls(flickChip, 'g-md-tk-flick', false);
    const which = flickChip === chipEl('pot') ? 'pot' : 'streak';
    const now = chipText(which);
    if (now != null) { try { flickChip.textContent = now; } catch (e) { /* ignore */ } }
    flickChip = null;
  }

  /* ------------------------------------------------------- crooked clock */
  let clockArmed = false;
  let clockFold = 0;
  let ringOn = false;
  let hostRing = null;
  function armClock() {
    if (clockArmed || tier < T.CLOCK_FROM_TIER) return;
    clockArmed = true;
    fired.clock += 1;
    cancel(clockFold);
    clockFold = after(T.RING_LATCH_MS, () => { clockFold = 0; if (!ringOn) clockArmed = false; });
    say('trickster: the clock goes crooked (armed for the next pick window)');
    if (phaseNow() === 'pick') openRing();
  }
  function openRing() {
    if (!clockArmed || ringOn || destroyed || stopped || paused || isHalted()) return;
    mountLayer();
    if (!layer) return;
    if (!ringEl) ringEl = el('i', 'g-md-tk-ring', layer);
    if (!ringEl) return;
    let ms = T.RING_DEFAULT_MS;
    let elapsed = 0;
    try {
      const pw = pickWindow();
      if (pw && Number(pw.totalMs) > 0) ms = Number(pw.totalMs);
      if (pw && Number(pw.elapsedMs) > 0) elapsed = Math.min(ms, Number(pw.elapsedMs));
    } catch (e) { /* defaults */ }
    setVar(ringEl, '--md-tk-ms', Math.round(ms) + 'ms');
    setCls(ringEl, 'on', false);
    if (typeof ringEl.offsetWidth === 'number') void ringEl.offsetWidth;
    try { ringEl.style.animationDelay = elapsed > 0 ? ('-' + Math.round(elapsed) + 'ms') : ''; } catch (e) { /* noop */ }
    setCls(ringEl, 'on', true);
    ringOn = true;
    hostRing = ringElOf();
    if (hostRing) setCls(hostRing, 'g-md-tk-bent', true);
  }
  function closeRing() {
    if (!ringOn) return;
    ringOn = false;
    setCls(ringEl, 'on', false);
    if (hostRing) setCls(hostRing, 'g-md-tk-bent', false);
    hostRing = null;
    clockArmed = false;
    cancel(clockFold); clockFold = 0;
  }

  /* --------------------------------------------------------------- feint */
  let feintArmed = false;
  let feintFold = 0;
  let feinting = [];
  let feintTimer = 0;
  let feintSeen = false;
  function armFeint() {
    if (feintArmed || tier < T.FEINT_FROM_TIER || reduced) return;
    feintArmed = true;
    cancel(feintFold);
    feintFold = after(T.FEINT_LATCH_MS, () => { feintFold = 0; feintArmed = false; });
    say('trickster: fake shuffle armed');
    if (phaseNow() === 'pick') playFeint();
  }
  function playFeint() {
    if (!feintArmed || destroyed || stopped || paused || isHalted()) return;
    const list = shells().sort((a, b) => slotOf(a) - slotOf(b));
    if (list.length < 2) return;
    const i = Math.floor(roll('feint-pick') * (list.length - 1));
    const a = list[i]; const b = list[i + 1];
    feintArmed = false;
    cancel(feintFold); feintFold = 0;
    setVar(a, '--md-feint', String(T.FEINT_PX));
    setVar(b, '--md-feint', String(-T.FEINT_PX));
    setCls(a, 'g-md-tk-feint', true); setCls(b, 'g-md-tk-feint', true);
    feinting = [a, b];
    fired.feint += 1;
    feintSeen = roll('feint-seen') < T.SEEN_TAUNT_CHANCE;   // the taunt waits for the verdict (no tell)
    cancel(feintTimer);
    feintTimer = after(T.FEINT_MS, () => { feintTimer = 0; unfeint(); });
    say('trickster: fake shuffle (slots ' + slotOf(a) + '/' + slotOf(b) + ', nothing moved)');
  }
  function unfeint() {
    for (const n of feinting) { rmVar(n, '--md-feint'); setCls(n, 'g-md-tk-feint', false); }
    feinting = [];
  }

  /* ---------------------------------------------------------------- melt */
  let meltArmed = false;
  let meltFold = 0;
  let melted = null;
  let meltTimer = 0;
  let meltAnnounced = false;
  function armMelt() {
    if (meltArmed || tier < T.MELT_FROM_TIER || reduced) return;
    meltArmed = true;
    cancel(meltFold);
    meltFold = after(T.MELT_LATCH_MS, () => { meltFold = 0; meltArmed = false; });
    say('trickster: melt armed (plays on the next swap)');
  }
  function playMelt() {
    if (!meltArmed || destroyed || stopped || paused || melted || isHalted()) return;
    let list = shells();
    let tgt = null;
    try { tgt = targetSlot(); } catch (e) { tgt = null; }
    if (tgt != null && Number.isFinite(Number(tgt))) list = list.filter((n) => slotOf(n) !== Number(tgt));
    if (!list.length) return;
    const shell = list[Math.floor(roll('melt-pick') * list.length)];
    meltArmed = false;
    cancel(meltFold); meltFold = 0;
    setCls(shell, 'g-md-tk-melt', true);
    melted = shell;
    fired.melt += 1;
    if (announce && !meltAnnounced) {
      meltAnnounced = true;
      try { announce(say_t('md_trick_melt', 'The lids run like wax'), 1600); } catch (e) { /* ignore */ }
    }
    cancel(meltTimer);
    meltTimer = after(T.MELT_MS, () => { meltTimer = 0; unmelt(); });
    say('trickster: melt (slot ' + slotOf(shell) + ')');
  }
  function unmelt() {
    if (!melted) return;
    setCls(melted, 'g-md-tk-melt', false);
    melted = null;
  }

  /* --------------------------------------------------------- ghost cursor */
  let ghostTimer = 0;
  let onMove = null;
  const trail = [];
  let lastMoveAt = 0;
  let gx = 0; let gy = 0;
  let ghostMode = 'off';
  let lureSlot = -1;
  function ghostEligible() {
    return armed && !reduced && !coarse && tier >= T.GHOST_FROM_TIER && !!stage && typeof stage.addEventListener === 'function';
  }
  function ghostOff() {
    if (ghostMode === 'off') return;
    setCls(ghostEl, 'on', false);
    setCls(ghostEl, 'lure', false);
    ghostMode = 'off';
    lureSlot = -1;
  }
  /** The cup nearest the hand, then its neighbour on the seed's side. */
  function lurePoint() {
    const list = shells();
    if (list.length < 2) return null;
    const s = rectOf(stage);
    if (!s) return null;
    let nearest = null; let best = Infinity;
    for (const n of list) {
      const r = rectOf(n);
      if (!r) continue;
      const cx = r.left + r.width / 2 - s.left; const cy = r.top + r.height / 2 - s.top;
      const d = (cx - gx) * (cx - gx) + (cy - gy) * (cy - gy);
      if (d < best) { best = d; nearest = n; }
    }
    if (!nearest) return null;
    if (lureSlot < 0) lureSlot = nearSlot(slotOf(nearest), list.length, roll('lure-side'));
    const tgt = list.find((n) => slotOf(n) === lureSlot) || nearest;
    const r = rectOf(tgt);
    if (!r) return null;
    return { x: r.left + r.width / 2 - s.left, y: r.top + r.height / 2 - s.top };
  }
  function ghostTick() {
    if (!ghostEl || destroyed || stopped || paused) return;
    if (!capsOkNow() || phaseNow() !== 'pick' || isHalted()) { ghostOff(); return; }
    const t = nowMs();
    const stalled = lastMoveAt > 0 && (t - lastMoveAt) >= T.GHOST_STALL_MS;
    if (stalled) {
      const p = lurePoint();
      if (p) { gx += (p.x - gx) * T.GHOST_LERP; gy += (p.y - gy) * T.GHOST_LERP; }
      if (ghostMode !== 'lure') { fired.lure += 1; ghostMode = 'lure'; }
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', true);
    } else {
      if (!trail.length || !lastMoveAt) { ghostOff(); return; }
      let pt = trail[0];
      for (const p of trail) { if (t - p.at >= T.GHOST_TRAIL_MS) pt = p; else break; }
      gx = pt.x; gy = pt.y;
      ghostMode = 'trail';
      lureSlot = -1;
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', false);
    }
    try { ghostEl.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)'; } catch (e) { /* noop */ }
    while (trail.length > 1 && t - trail[0].at > T.GHOST_TRAIL_KEEP_MS) trail.shift();
  }
  function armGhost() {
    if (!ghostEligible() || ghostEl) return;
    mountLayer();
    if (!layer) return;
    ghostEl = el('i', 'g-md-tk-ghost', layer);
    if (!ghostEl) return;
    onMove = (ev) => {
      if (destroyed) return;
      try {
        const s = stage.getBoundingClientRect();
        const x = Number(ev && ev.clientX) - s.left;
        const y = Number(ev && ev.clientY) - s.top;
        if (!Number.isFinite(x) || !Number.isFinite(y)) return;
        lastMoveAt = nowMs();
        trail.push({ x, y, at: lastMoveAt });
      } catch (e) { /* no rects under the DOM double: the ghost sleeps */ }
    };
    try { stage.addEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    ghostTimer = every(T.GHOST_TICK_MS, ghostTick);
    say('trickster: ghost armed');
  }
  function disarmGhost() {
    if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
    try { if (onMove && stage && stage.removeEventListener) stage.removeEventListener('pointermove', onMove); } catch (e) { /* noop */ }
    onMove = null;
    ghostOff();
  }

  /* ----------------------------------------------------- the phase watch */
  let phaseObs = null;
  let phasePoll = 0;
  let lastPhase = '';
  function onPhase(p) {
    if (p === lastPhase) return;
    lastPhase = p;
    if (p === 'pick') { openRing(); playFeint(); }
    else { closeRing(); unfeint(); ghostOff(); }
  }
  function watchPhase() {
    let hooked = false;
    try {
      if (stage && typeof MutationObserver === 'function' && typeof stage.nodeType === 'number') {
        phaseObs = new MutationObserver(() => onPhase(phaseNow()));
        phaseObs.observe(stage, { attributes: true, attributeFilter: ['data-phase'] });
        hooked = true;
      }
    } catch (e) { phaseObs = null; }
    if (!hooked) phasePoll = every(T.PHASE_POLL_MS, () => onPhase(phaseNow()));
  }
  function unwatchPhase() {
    if (phaseObs) { try { phaseObs.disconnect(); } catch (e) { /* ignore */ } phaseObs = null; }
    if (phasePoll) { stopEvery(phasePoll); phasePoll = 0; }
  }

  function allHonest() {
    unflick();
    closeRing();
    unfeint();
    unmelt();
    ghostOff();
  }

  /* ------------------------------------------------------------------ api */
  return {
    start() {
      if (!armed || destroyed) { say('trickster: disarmed'); return; }
      deals = buildDeals();
      for (const deal of deals) after(deal.at, () => attempt(deal, 0));
      watchPhase();
      armGhost();
      say('trickster: dealt ' + deals.length + ' cards (' + deals.map((d) => d.card + '@' + Math.round(d.at / 1000) + 's').join(', ') + ')');
    },
    /** index.js calls after every swap of the shuffle (the blackout zone). */
    afterSwap() {
      if (!armed || destroyed || stopped) return;
      if (meltArmed) playMelt();
    },
    /** index.js calls after every pick verdict: every lie of the window comes off. */
    afterPick() {
      if (!armed || destroyed) return;
      closeRing();
      unfeint();
      ghostOff();
      lastMoveAt = 0;
      /* the feint's taunt lands AFTER the verdict, never during the window */
      if (feintSeen && announce && !stopped) {
        feintSeen = false;
        try { announce(say_t('md_trick_seen', 'Did you see that?'), 1400); } catch (e) { /* ignore */ }
      }
    },
    /** The class heat: the deck has no magnitude dial, but index.js calls it. */
    setHeat() { /* the lies do not scale with heat; the deal does (tier) */ },
    /** index.js calls every ~500ms with ms since the last input; 0 resets. */
    stalled(ms) {
      if (!armed || destroyed || stopped) return;
      const n = Number(ms) || 0;
      if (n <= 0) { ghostOff(); return; }
      /* the ghost's own stall clock (pointer) is finer; this is the coarse hook
         for a hand that never moved at all: seed the trail at the stage centre */
      if (n >= T.GHOST_STALL_MS && ghostEl && !lastMoveAt && phaseNow() === 'pick' && !isHalted()) {
        const s = rectOf(stage);
        if (s && s.width) { gx = s.width / 2; gy = s.height * 0.75; lastMoveAt = nowMs() - T.GHOST_STALL_MS; trail.push({ x: gx, y: gy, at: lastMoveAt }); }
      }
    },
    pause() {
      paused = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      flickTimer = 0; feintTimer = 0; meltTimer = 0; clockFold = 0; feintFold = 0; meltFold = 0;
      if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
      allHonest();
    },
    resume() {
      paused = false;
      if (!destroyed && !stopped && ghostEligible() && ghostEl && !ghostTimer) ghostTimer = every(T.GHOST_TICK_MS, ghostTick);
    },
    stop() {
      stopped = true;
      meltArmed = false; feintArmed = false; clockArmed = false;
      allHonest();
      disarmGhost();
      unwatchPhase();
    },
    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      allHonest();
      disarmGhost();
      unwatchPhase();
      if (layer) { try { layer.remove(); } catch (e) { /* ignore */ } }
      layer = null; ringEl = null; ghostEl = null;
    },
    diagnostics() {
      return {
        armed, tier, deals: deals.slice(), fired: Object.assign({}, fired),
        clock: { armed: clockArmed, ringOn }, feint: { armed: feintArmed, live: feinting.length },
        melt: { armed: meltArmed, live: !!melted },
        ghost: { armed: !!ghostEl, mode: ghostMode, trail: trail.length, lureSlot },
        phase: lastPhase, observer: !!phaseObs, liveTimers: live.size, lexicon: Array.from(lexiconUsed),
      };
    },
  };
}

export default createMdTrickster;
