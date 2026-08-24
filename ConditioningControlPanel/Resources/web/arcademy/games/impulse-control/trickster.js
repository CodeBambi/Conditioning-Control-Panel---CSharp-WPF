/* ============================================================================
 * games/impulse-control/trickster.js - DECK III of the House Rules, dealt into
 * THE DROP TUBE. A reflex room has exactly one target and exactly one clock,
 * so every card here attacks what the player READS on the way to that target -
 * never the target, never the clock underneath.
 *
 *   THE TELL        (Fake Shuffle) the mouth's load glow takes a colour that
 *                   HINTS the kind while the bubble is still inside the chute:
 *                   pink for a good bubble, gold for the X. Tier 1-2 the tell
 *                   is TRUTHFUL on every deal - the class teaches by telling
 *                   the truth until you trust it. Tier 3+ the seeded plan
 *                   spends card slots on LIES: the lamp shows the other
 *                   colour. Never on the first two deals (schedule.js's
 *                   TEACH_GOOD bubbles), never two lies back to back. The
 *                   reveal, the window and the ledger are index.js's and
 *                   exact; only the lamp lies.
 *   THE CROOKED RING (Crooked Clock skin) on a denied reveal the visible hold
 *                   ring's FACE bends: it races through the boring middle
 *                   (shows less time left than you really have) and crawls as
 *                   the two seconds run out, meeting the truth exactly at the
 *                   last 15% and staying honest from there - so the hand
 *                   itches early and the finish is never a lie. The real
 *                   windowMs is index.js's timer and is untouched; this deck
 *                   paints an overlay ring of its own over a hidden face and
 *                   never writes `--ic-hold` or `--ic-k` (style.js's own
 *                   semantics). The bend is a PURE function, exported.
 *   GHOST CURSOR    a faint cursor echo trails the real pointer by ~430ms
 *                   during a reveal, and on a DENIED reveal after a ~600ms
 *                   stall it stops trailing and DRIFTS toward the bubble - the
 *                   house leaning on your hand to pop the X. Pure suggestion:
 *                   pointer-events none, it cannot click, the real cursor is
 *                   never hidden, and it dies on the next event. Tier 2+,
 *                   mouse pointers only (nothing to echo on a touch screen).
 *   STAT FLICKER    the score chip briefly reads a few points off, then
 *                   "corrects" with a static pop to the EXACT text it had.
 *                   The ledger never moves; confidence does. Tier 3+. The
 *                   casino's payout punch on the same chip is transform-only,
 *                   so the two never collide - and if a real hud() repaint
 *                   lands mid-lie, the restore stands down instead of
 *                   stomping the truth. W2: the pop is AUDIBLE now - a `decoy`
 *                   on the correction, through opts.cue (the game's clamped
 *                   helper). It is this deck's ONLY cue: the tell, the crooked
 *                   ring and the ghost are all lies you READ, and a lie that
 *                   announces itself is not a lie.
 *
 * DEALING RULE: card slots are dealt at start() from the seeded plan against
 * stats().total - budget 2/4/6/8 by tier, at least 4 bubbles apart (this class
 * runs ~3.8s a bubble, so 4 apart is under the House's 4-a-minute cap; a wall
 * clock guard enforces the minute directly as well), never in the first two
 * deals, never during the debrief. A slot whose moment arrives halted is
 * re-queued once and then folds. Tier 1 deals NOTHING: every card gates at
 * tier 2 or 3, which is the point - the truthful tell is the whole of the
 * first year.
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - the tell paints a lamp this deck owns; the ring paints
 *       a ring this deck owns; the flicker writes .g-ic-score TEXT and puts
 *       back the exact string it read. Nothing here reads or writes score,
 *       streak, the tally, the reveal window, the plan or the grade, and no
 *       card can change WHEN index.js's window timer fires.
 *   II  input honest  - the bubble is the one tap target and this deck never
 *       touches it: every node lives in the deck's own layer inside
 *       nodes.stage, pointer-events:none, and the layer is a sibling of the
 *       basin, so nothing decorative can ever sit between a finger and the
 *       bubble. The ghost cannot click. No card moves, resizes or delays it.
 *   III never still   - the lamp breathes, the ghost pulses on the lure.
 *   IV  images over text - this deck renders NO text and invents no lexicon
 *       key (the flicker only re-digits a number the HUD already showed).
 *   V   seeded        - per-tag mulberry32 streams off seed+'|ic-trickster',
 *       append-only tags (a new tag never shifts an old stream; see the
 *       lost-and-found header for why makeTaggedRoll clusters here). A retake
 *       replays the identical deal.
 *   VI  exits sacred  - capsOk false disarms the whole deck (no nodes, no
 *       listener, no timers); reduced motion drops the ghost cursor AND the
 *       crooked ring entirely (the tell is a colour and the flicker is a
 *       number: neither is motion); every timer rides the game's registry via
 *       opts.timers AND a local set, pause() cancels and restores every live
 *       lie, resume() re-arms, destroy() leaves no node, timer or listener.
 *   VII strings       - none. This file has no lexicon rows.
 *
 * ENGINE PLACEMENT: entirely game-local. All four cards are addressed to this
 * class's own nodes (the chute mouth, the hold ring, the score chip, the
 * basin's centre); the engine has no target contract for any of them, and the
 * deck asks the engine for nothing. `opts.engine` is accepted for interface
 * symmetry with casino.js / pressure.js and deliberately unused.
 *
 * THE STYLESHEET is a document-level singleton (`<style id="g-ic-trickster-
 * style">`, style.js's ensureStyle pattern): injected once, never removed on
 * destroy, exactly like the class's own sheet.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const IC_TRICKSTER = Object.freeze({
  /** Dealt card slots per class, by tier (the House budget: 2 -> 8). */
  DEALS: Object.freeze({ 1: 2, 2: 4, 3: 6, 4: 8 }),
  /** Never in the first two deals - schedule.js's TEACH_GOOD bubbles. */
  FIRST_DEAL_IDX: 2,
  /** At least this many bubbles between cards (11-15s at this class's pace).
   *  With ~22 bubbles in a 90s class this alone clips the tier-4 budget of 8
   *  down to the ~5 slots that actually fit, which is the point. */
  MIN_GAP_IDX: 4,
  /** The House's per-minute cap, stated as the law says it: at most 4 fires in
   *  any rolling 60s, plus a floor so two cards never land in one breath. */
  RATE_WINDOW_MS: 60000,
  RATE_MAX: 4,
  MIN_GAP_MS: 4000,
  /** A slot whose moment arrives halted waits for one more, then folds. */
  RETRY_MAX: 1,

  /** Card gates. The tell is truthful below LIE_FROM_TIER, never absent. */
  RING_FROM_TIER: 2,
  LIE_FROM_TIER: 3,
  FLICK_FROM_TIER: 3,

  /** Slot weights per tier, over the eligible cards only (renormalised). */
  WEIGHTS: Object.freeze({
    1: Object.freeze({ lie: 0, ring: 0, flick: 0 }),
    2: Object.freeze({ lie: 0, ring: 1, flick: 0 }),
    3: Object.freeze({ lie: 0.4, ring: 0.32, flick: 0.28 }),
    4: Object.freeze({ lie: 0.42, ring: 0.3, flick: 0.28 }),
  }),

  /** The crooked ring: how far the face runs ahead at mid-window, and the
   *  honest tail (the last 15% is the truth, to the pixel). */
  RING_BEND: 0.16,
  RING_HONEST: 0.15,
  RING_RAMP: 0.32,
  /** How many stops the bend is baked into as CSS keyframes. */
  RING_STOPS: 20,

  /** Stat flicker: how long the wrong number stands, and how far off it is. */
  FLICK_MS: 460,
  FLICK_DELAY_MS: 140,
  FLICK_MIN: 12,
  FLICK_SPAN: 30,

  /** The ghost: tier 2+, mouse only. */
  GHOST_FROM_TIER: 2,
  GHOST_TICK_MS: 90,
  GHOST_TRAIL_MS: 430,
  GHOST_STALL_MS: 600,
  GHOST_LERP: 0.16,
  GHOST_TRAIL_KEEP_MS: 2200,
});

const STYLE_ID = 'g-ic-trickster-style';

/* ---------------------------------------------------------------- the bend */
/**
 * THE CROOKED RING'S FACE, pure.
 *
 * Given the TRUE remaining fraction of the hold window (1 at the reveal, 0 at
 * the end), answer the fraction to PAINT. Properties, all asserted by the
 * suite:
 *   f(1) = 1, f(0) = 0                       - the ends never lie
 *   f(r) = r for r <= honest                 - the last 15% is the truth
 *   f(r) < r for honest < r < 1              - the middle races ahead
 *   monotone non-decreasing in r             - the ring never runs backwards
 * The bend fades IN above the honest zone (smoothstep ramp) so there is no
 * seam where the face visibly re-joins the truth: it arrives, it does not snap.
 * Monotonicity needs bend * pi < 1; the shipped 0.16 leaves plenty of room.
 *
 * @param {number} remain 0..1 true remaining fraction
 * @param {number=} honest honest tail fraction (default RING_HONEST)
 * @param {number=} bend   bend amplitude (default RING_BEND)
 * @returns {number} 0..1 fraction to paint
 */
export function bendHoldFace(remain, honest, bend) {
  const r = Number(remain);
  if (!Number.isFinite(r)) return 0;
  const x = r < 0 ? 0 : r > 1 ? 1 : r;
  const H = Number.isFinite(honest) ? Math.max(0, Math.min(0.9, honest)) : IC_TRICKSTER.RING_HONEST;
  if (x <= H || x >= 1) return x;
  const A0 = Number.isFinite(bend) ? bend : IC_TRICKSTER.RING_BEND;
  const ramp = Math.max(0, Math.min(1, (x - H) / IC_TRICKSTER.RING_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, x - A * Math.sin(Math.PI * x));
}

/** The bend, baked into CSS keyframes: elapsed 0..100% -> painted fraction. */
function ringKeyframes() {
  const n = IC_TRICKSTER.RING_STOPS;
  const rows = [];
  for (let i = 0; i <= n; i++) {
    const e = i / n;                       // elapsed fraction
    const k = bendHoldFace(1 - e);         // painted remaining fraction
    rows.push(Math.round(e * 10000) / 100 + '%{--ic-tk-k:' + (Math.round(k * 1000) / 1000) + '}');
  }
  return '@keyframes g-ic-tk-hold{' + rows.join('') + '}';
}

export const STYLE_TEXT = `
/* THE TRICKSTER LAYER. One layer, three nodes, all pointer-events:none, all
   inside .g-ic so the class's own .suspended blanket freezes them. */
@property --ic-tk-k { syntax: '<number>'; initial-value: 1; inherits: false; }
@property --ic-tk-heat { syntax: '<number>'; initial-value: 0.2; inherits: true; }
.g-ic-tk{position:absolute;inset:0;z-index:4;pointer-events:none;
  --ic-tk-heat:.2;--ic-tk-lamp:rgba(255,105,180,.55)}
.g-ic-tk *{pointer-events:none}

/* THE TELL: the chute mouth is off-frame at the top, so its load glow spills
   in over the frame's crown. Pink means a good bubble is coming. Gold means
   the X. One of those two statements is sometimes a lie. */
/* the bar at the very top edge is the lamp itself; the radial under it is the
   light it throws into the room. A haze alone loses to a pink tube - the bar
   is what makes pink-versus-gold readable at a glance. */
.g-ic-tk-tell{position:absolute;left:50%;top:0;width:min(130vw,150vmin);height:38vmin;
  transform:translateX(-50%);opacity:0;transition:opacity .3s ease;
  background:
    linear-gradient(180deg, var(--ic-tk-lamp) 0 1.2%, transparent 17%),
    radial-gradient(58% 100% at 50% 0%, var(--ic-tk-lamp) 0%, transparent 76%);
  filter:blur(5px)}
.g-ic-tk-tell.on{animation:g-ic-tk-lamp 2.4s ease-in-out infinite alternate}
@keyframes g-ic-tk-lamp{
  from{opacity:calc(.42 + .30*var(--ic-tk-heat,.2))}
  to{opacity:calc(.62 + .38*var(--ic-tk-heat,.2))}}

/* THE CROOKED RING: the same geometry as .g-ic-holdring (the bubble's box
   grown by its -14% inset), painted over a face that has been hidden. Its
   keyframes ARE the bend function, sampled - so the face is CSS, freezes with
   .suspended and costs no per-frame javascript. */
/* CLOSEST-SIDE IS LOAD BEARING. A bare radial-gradient(circle, ...) sizes
   itself farthest-corner, so on a SQUARE box the 82% stop lands at 0.82 of the
   DIAGONAL - about 116% of the box's own radius - and the whole annulus falls
   outside the element: the ring paints four invisible corner slivers and
   nothing else. closest-side pins the gradient to the box's radius, which is
   what a ring wants. (style.js's own .g-ic-holdring has the bare form; that is
   STAGE's file and STAGE's call.) */
.g-ic-tk-ring{position:absolute;left:var(--ic-basin-x,50%);top:var(--ic-basin-y,50%);
  width:calc(var(--ic-basin-d, clamp(132px,17.5vmin,238px)) * 1.28);
  height:calc(var(--ic-basin-d, clamp(132px,17.5vmin,238px)) * 1.28);
  transform:translate(-50%,-50%);border-radius:50%;display:none;
  background:conic-gradient(var(--ic-lav,#B8A6E8) calc(var(--ic-tk-k,1)*360deg), rgba(184,166,232,.16) 0);
  -webkit-mask:radial-gradient(circle closest-side, transparent 0 82%, #000 84%);
  mask:radial-gradient(circle closest-side, transparent 0 82%, #000 84%)}
.g-ic-tk-ring.on{display:block;animation:g-ic-tk-hold var(--ic-tk-dur,2000ms) linear forwards}
${ringKeyframes()}
@supports not (background:conic-gradient(red calc(var(--x)*360deg),blue 0)){
  .g-ic-tk-ring.on{display:none}}
/* the real face steps aside while ours is up - and only while ours is up */
.g-ic-holdring.g-ic-tk-bent{opacity:0}

/* GHOST CURSOR: a will-o-wisp echo of the player's own hand. It cannot click,
   it never hides the real cursor, and it dies on the next event. */
.g-ic-tk-ghost{position:absolute;left:0;top:0;width:15px;height:19px;opacity:0;
  will-change:transform;
  clip-path:polygon(0 0, 0 82%, 22% 64%, 38% 100%, 50% 94%, 34% 60%, 60% 60%);
  background:linear-gradient(160deg, rgba(255,105,180,.9), rgba(184,166,232,.55));
  filter:drop-shadow(0 0 6px rgba(255,105,180,.6));
  transition:transform .34s ease-out, opacity .38s ease}
.g-ic-tk-ghost.on{opacity:.3}
.g-ic-tk-ghost.lure{opacity:.52;animation:g-ic-tk-ghostpulse 1.5s ease-in-out infinite}
@keyframes g-ic-tk-ghostpulse{50%{filter:drop-shadow(0 0 12px rgba(255,105,180,.95))}}

/* STAT FLICKER: the static pop the wrong number corrects itself with. */
.g-ic-score.g-ic-tk-flick{text-shadow:0 0 22px rgba(184,166,232,.75), 0 0 3px rgba(255,255,255,.5);
  animation:g-ic-tk-static .16s steps(3) 2}
@keyframes g-ic-tk-static{0%{filter:none}40%{filter:brightness(1.5) contrast(1.4)}100%{filter:none}}

@media (prefers-reduced-motion: reduce){
  .g-ic-tk-tell.on{animation:none;opacity:calc(.5 + .34*var(--ic-tk-heat,.2))}
  .g-ic-tk-ring.on{animation:none;display:none}
  .g-ic-tk-ghost{display:none}
  .g-ic-score.g-ic-tk-flick{animation:none}
}
`;

/** Inject the deck's sheet once per document (style.js's pattern). */
export function ensureTricksterStyle() {
  try {
    if (typeof document === 'undefined' || !document.head || !document.getElementById) return;
    if (document.getElementById(STYLE_ID)) return;
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = STYLE_TEXT;
    document.head.appendChild(s);
  } catch (e) { /* an unstyled deck simply never shows */ }
}

/* ------------------------------------------------------------------ clock */
/** Resolved at CALL time so the scratch harness's fake clock is honoured. */
function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
      return performance.now();
    }
  } catch (e) { /* fall through */ }
  return Date.now();
}

function clampTier(t) { return Math.max(1, Math.min(4, Math.round(Number(t) || 1))); }

/** Coarse pointer probe (there is no cursor to echo on a touch screen). */
function probeCoarse() {
  try {
    if (typeof window !== 'undefined' && window && typeof window.matchMedia === 'function') {
      const m = window.matchMedia('(pointer: coarse)');
      return !!(m && m.matches);
    }
  } catch (e) { /* noop */ }
  return false;
}

/**
 * @param {Object} o
 * @param {string}   o.seed        the class seed (retakes replay the deal)
 * @param {number}   o.gradeTier   1..4
 * @param {boolean}  o.reduced     reduced motion (ghost + ring off)
 * @param {number=}  o.motionLevel 0..2 (0 is treated as reduced)
 * @param {Object}   o.nodes       render.nodes (stage, basin, holdring, score...)
 * @param {Object=}  o.engine      the deckEngine - accepted, deliberately unused
 * @param {Object}   o.timers      deckTimers {after(ms,fn)->id, every(ms,fn)->id, clear(id)}
 * @param {boolean|Function} o.capsOk  false (or ()=>false) disarms everything
 * @param {Function} o.isHalted    () => bool (paused / suspended / ended)
 * @param {Function} o.stats       () => {idx, total, streak, score, phase}
 * @param {boolean=} o.coarse      touch pointer (else probed from matchMedia)
 * @param {Function=} o.t          unused: this deck renders no text
 * @param {Function=} o.log
 */
export function createIcTrickster(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const nodes = opts.nodes || {};
  const tier = clampTier(opts.gradeTier);
  const motionOff = Number(opts.motionLevel) === 0;
  const reduced = !!opts.reduced || motionOff;
  const coarse = opts.coarse == null ? probeCoarse() : !!opts.coarse;
  const isHalted = typeof opts.isHalted === 'function' ? opts.isHalted : () => false;
  const stats = typeof opts.stats === 'function' ? opts.stats : () => null;
  const capsFn = typeof opts.capsOk === 'function' ? opts.capsOk : () => opts.capsOk !== false;
  const capsOkNow = () => { try { return !!capsFn(); } catch (e) { return false; } };

  const timers = opts.timers || null;
  const armed = capsOkNow() && !!nodes.stage && !!timers && typeof timers.after === 'function'
    && typeof document !== 'undefined';
  /* W2 - THE CUE ROAD. index.js hands down its own clamped helper (the same
     one the casino's chime ladder rides), so this deck asks for sound and
     never holds a node or raises the tier's ceiling.
     THE DECOUPLE (spec 3): sound gates on destroyed/stopped and NOT on capsOk
     - bgIntensity 0 is the player's VISUAL exit (Law VI), never a request for
     a silent school. The honest limit, written down rather than faked: this
     deck's cards are dealt by its own timers and `armed` folds capsOk in at
     CONSTRUCTION, so with the dial at 0 no card is dealt and there is no
     correction to hear. Nothing is muted by the dial; nothing happens. */
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};
  const sounds = () => !destroyed && !stopped;

  /* timers: the game's registry (pause-aware, killed with the class) AND a
     local set, so destroy() can never leak one the registry outlived. */
  const live = new Set();
  const chains = new Set();
  const cancelFn = timers && (timers.clear || timers.cancel);
  function after(ms, fn) {
    if (!armed || destroyed) return 0;
    let id = 0;
    id = timers.after(ms, () => {
      live.delete(id);
      if (!destroyed) { try { fn(); } catch (e) { /* a cosmetic throw is not a class failure */ } }
    });
    if (id) live.add(id);
    return id;
  }
  function cancel(id) {
    if (!id) return;
    live.delete(id);
    try { if (typeof cancelFn === 'function') cancelFn.call(timers, id); } catch (e) { /* noop */ }
  }
  /** every(): the registry's if it has one, else a chain of after()s. */
  function every(ms, fn) {
    if (!armed || destroyed) return 0;
    if (typeof timers.every === 'function') {
      const id = timers.every(ms, () => { if (!destroyed) { try { fn(); } catch (e) { /* noop */ } } });
      if (id) live.add(id);
      return id;
    }
    const handle = { id: 0, on: true };
    const tick = () => {
      if (!handle.on || destroyed) return;
      try { fn(); } catch (e) { /* noop */ }
      handle.id = after(ms, tick);
    };
    handle.id = after(ms, tick);
    chains.add(handle);
    return handle;
  }
  function stopEvery(h) {
    if (!h) return;
    if (typeof h === 'object') { h.on = false; cancel(h.id); chains.delete(h); } else cancel(h);
  }

  /* per-tag mulberry32 streams, append-only tags (Law V) */
  const seedBase = String(opts.seed == null ? 'ic' : opts.seed) + '|ic-trickster|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let stopped = false;
  let paused = false;
  let started = false;
  let heat = 0.2;
  let plan = [];
  let folded = 0;
  let lastFireAt = -1e9;
  const fires = [];             // wall times of spent cards (rolling minute)
  const fired = { lie: 0, ring: 0, flick: 0, lure: 0, tell: 0 };

  let layer = null;
  let tellEl = null;
  let ringEl = null;
  let ghostEl = null;

  /* ------------------------------------------------------------- the nodes */
  const el = (tag, cls, parent) => {
    try {
      const n = document.createElement(tag);
      if (!n) return null;
      if (cls) n.className = cls;
      if (parent && parent.appendChild) parent.appendChild(n);
      return n;
    } catch (e) { return null; }
  };
  const setCls = (n, cls, on) => {
    try { if (n && n.classList) n.classList[on ? 'add' : 'remove'](cls); } catch (e) { /* noop */ }
  };

  function build() {
    ensureTricksterStyle();
    layer = el('div', 'g-ic-tk', nodes.stage);
    if (!layer) return false;
    tellEl = el('i', 'g-ic-tk-tell', layer);
    if (!reduced && tier >= IC_TRICKSTER.RING_FROM_TIER) ringEl = el('i', 'g-ic-tk-ring', layer);
    return true;
  }

  /* ---------------------------------------------------------------- the plan */
  function eligible() {
    const out = [];
    if (tier >= IC_TRICKSTER.LIE_FROM_TIER) out.push('lie');
    if (tier >= IC_TRICKSTER.RING_FROM_TIER && !reduced) out.push('ring');
    if (tier >= IC_TRICKSTER.FLICK_FROM_TIER) out.push('flick');
    return out;
  }

  function pickCard(cards) {
    const w = IC_TRICKSTER.WEIGHTS[tier] || IC_TRICKSTER.WEIGHTS[1];
    let total = 0;
    for (const c of cards) total += Math.max(0, Number(w[c]) || 0);
    if (total <= 0) return cards[Math.floor(roll('card') * cards.length)] || cards[0];
    let x = roll('card') * total;
    for (const c of cards) {
      x -= Math.max(0, Number(w[c]) || 0);
      if (x <= 0) return c;
    }
    return cards[cards.length - 1];
  }

  /**
   * Slots are BUBBLE INDICES, not wall times: this class is a metronome of
   * deals, and an index is the only moment a card can attach to. Spacing is
   * MIN_GAP_IDX bubbles (~15s here), which is also what keeps two tell lies
   * from ever landing back to back.
   */
  function buildPlan() {
    const cards = eligible();
    const s = stats() || {};
    const total = Math.max(0, Math.round(Number(s.total) || 0));
    const budget = IC_TRICKSTER.DEALS[tier] || 0;
    if (!cards.length || !budget || total <= IC_TRICKSTER.FIRST_DEAL_IDX) return [];
    const first = IC_TRICKSTER.FIRST_DEAL_IDX;
    const last = total - 1;
    const span = last - first;
    if (span < 0) return [];
    const at = [];
    for (let i = 0; i < budget; i++) at.push(first + Math.floor(roll('when') * (span + 1)));
    at.sort((a, b) => a - b);
    for (let i = 1; i < at.length; i++) {
      if (at[i] - at[i - 1] < IC_TRICKSTER.MIN_GAP_IDX) at[i] = at[i - 1] + IC_TRICKSTER.MIN_GAP_IDX;
    }
    const out = [];
    for (const a of at) {
      if (a > last) break;
      out.push({ at: a, card: pickCard(cards), used: false, tries: 0 });
    }
    /* belt and braces on "never two lies in a row": the spacing already makes
       adjacent indices impossible, so this only ever fires if MIN_GAP_IDX is
       ever tuned below 2. */
    for (let i = 1; i < out.length; i++) {
      if (out[i].card === 'lie' && out[i - 1].card === 'lie' && out[i].at - out[i - 1].at < 2) {
        out[i].card = cards.indexOf('ring') >= 0 ? 'ring' : cards.indexOf('flick') >= 0 ? 'flick' : 'lie';
      }
    }
    return out;
  }

  /** The first unused slot for this card at (exactly / at or before) an index. */
  function slotFor(card, idx, atOrBefore) {
    for (const slot of plan) {
      if (slot.used || slot.card !== card) continue;
      if (atOrBefore ? slot.at <= idx : slot.at === idx) return slot;
    }
    return null;
  }

  /** The House's gates on any card, at the moment it would fire. */
  function canFire() {
    if (!armed || destroyed || stopped || paused) return false;
    if (!capsOkNow()) return false;
    const s = stats();
    if (s && s.phase === 'debrief') return false;
    const t = nowMs();
    if (t - lastFireAt < IC_TRICKSTER.MIN_GAP_MS) return false;
    while (fires.length && t - fires[0] > IC_TRICKSTER.RATE_WINDOW_MS) fires.shift();
    if (fires.length >= IC_TRICKSTER.RATE_MAX) return false;
    return true;
  }

  /** A slot whose moment is wrong waits for one more, then folds. */
  function requeue(slot, why) {
    slot.tries += 1;
    if (slot.tries > IC_TRICKSTER.RETRY_MAX) {
      slot.used = true;
      folded += 1;
      say('trickster: ' + slot.card + '@' + slot.at + ' folded (' + why + ')');
    }
  }

  function spend(slot) {
    slot.used = true;
    lastFireAt = nowMs();
    fires.push(lastFireAt);
  }

  /* ------------------------------------------------------------- THE TELL */
  let tellOn = false;
  let tellLying = false;
  let tellKind = null;      // the colour SHOWN, not the truth

  function showTell(kindShown, lying) {
    if (!tellEl || !capsOkNow()) return;
    tellKind = kindShown;
    tellLying = !!lying;
    try {
      tellEl.style.setProperty('--ic-tk-lamp',
        kindShown === 'denied' ? 'rgba(247,201,78,.82)' : 'rgba(255,105,180,.8)');
    } catch (e) { /* noop */ }
    setCls(tellEl, 'on', true);
    tellOn = true;
    fired.tell += 1;
  }
  function hideTell() {
    if (!tellOn) return;
    setCls(tellEl, 'on', false);
    tellOn = false;
    tellLying = false;
    tellKind = null;
  }

  /* ------------------------------------------------------ THE CROOKED RING */
  let ringOn = false;

  function bendRing(windowMs) {
    if (!ringEl || reduced || !capsOkNow()) return false;
    const ms = Math.max(200, Math.round(Number(windowMs) || 0) || 2000);
    try {
      ringEl.style.setProperty('--ic-tk-dur', ms + 'ms');
      setCls(nodes.holdring, 'g-ic-tk-bent', true);
      setCls(ringEl, 'on', false);
      void (ringEl.offsetWidth);            // restart the baked bend
      setCls(ringEl, 'on', true);
    } catch (e) { return false; }
    ringOn = true;
    fired.ring += 1;
    return true;
  }
  /** The face always comes back honest - on the outcome, a pause or a death. */
  function straightenRing() {
    if (!ringOn) return;
    setCls(ringEl, 'on', false);
    setCls(nodes.holdring, 'g-ic-tk-bent', false);
    ringOn = false;
  }

  /* -------------------------------------------------------- STAT FLICKER */
  let flickTimer = 0;
  let flickPrev = null;     // the exact string the chip had
  let flickLie = null;      // the exact string we wrote

  function dealFlicker() {
    const chip = nodes.score;
    if (!chip || !capsOkNow()) return false;
    let truth = null;
    try { truth = chip.textContent; } catch (e) { return false; }
    if (truth == null || !/\d/.test(String(truth))) return false;
    const n = parseInt(String(truth).replace(/[^\d-]/g, ''), 10);
    if (!Number.isFinite(n)) return false;
    const sign = roll('flick-sign') < 0.5 ? -1 : 1;
    const delta = sign * (IC_TRICKSTER.FLICK_MIN + Math.floor(roll('flick-mag') * IC_TRICKSTER.FLICK_SPAN));
    const fake = Math.max(0, n + delta);
    const lie = String(truth).replace(/\d[\d,\s]*/, String(fake));
    if (lie === truth) return false;
    try { chip.textContent = lie; } catch (e) { return false; }
    flickPrev = String(truth);
    flickLie = lie;
    if (!reduced) setCls(chip, 'g-ic-tk-flick', true);
    fired.flick += 1;
    cancel(flickTimer);
    flickTimer = after(IC_TRICKSTER.FLICK_MS, restoreFlicker);
    return true;
  }

  /** The static pop. Restores the EXACT text read - and stands down if a real
   *  hud() repaint already put the truth back while the lie was up. */
  function restoreFlicker() {
    flickTimer = 0;
    const chip = nodes.score;
    if (!chip) { flickPrev = null; flickLie = null; return; }
    setCls(chip, 'g-ic-tk-flick', false);
    let corrected = false;
    try {
      if (flickLie != null && chip.textContent === flickLie && flickPrev != null) {
        chip.textContent = flickPrev;
        corrected = true;
      }
    } catch (e) { /* noop */ }
    flickPrev = null;
    flickLie = null;
    /* THE STATIC POP, made true. Only when the LIE was still standing: a real
       hud() repaint that beat us here is the truth arriving on its own, and
       the deck stands down rather than clicking at nothing. */
    if (corrected && sounds()) cue('decoy', 0.35);
  }

  /* --------------------------------------------------------- GHOST CURSOR */
  let ghostTimer = 0;
  let onMove = null;
  let onLeave = null;
  const trail = [];
  let lastMoveAt = 0;
  let gx = 0;
  let gy = 0;
  let ghostMode = 'off';      // off | trail | lure
  let revealKind = null;      // the LIVE reveal's true kind, or null
  let hovering = false;

  function ghostEligible() {
    return armed && !reduced && !coarse && tier >= IC_TRICKSTER.GHOST_FROM_TIER
      && !!nodes.stage && typeof nodes.stage.addEventListener === 'function';
  }

  /** The bubble's centre, in stage coordinates. */
  function basinPoint() {
    try {
      const target = nodes.basin || nodes.bubble;
      const b = target.getBoundingClientRect();
      const s = nodes.stage.getBoundingClientRect();
      if (!b || !s) return null;
      const x = b.left + b.width / 2 - s.left;
      const y = b.top + b.height / 2 - s.top;
      if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
      return { x, y };
    } catch (e) { return null; }
  }

  function ghostOff() {
    if (ghostMode === 'off') return;
    setCls(ghostEl, 'on', false);
    setCls(ghostEl, 'lure', false);
    ghostMode = 'off';
  }

  function ghostTick() {
    if (!ghostEl || destroyed || stopped || paused) return;
    if (!capsOkNow() || !revealKind || isHalted()) { ghostOff(); return; }
    const t = nowMs();
    const stalled = lastMoveAt > 0 && (t - lastMoveAt) >= IC_TRICKSTER.GHOST_STALL_MS;
    const lure = revealKind === 'denied' && stalled && !hovering;
    if (lure) {
      const p = basinPoint();
      if (p) {
        gx += (p.x - gx) * IC_TRICKSTER.GHOST_LERP;
        gy += (p.y - gy) * IC_TRICKSTER.GHOST_LERP;
      }
      if (ghostMode !== 'lure') { fired.lure += 1; ghostMode = 'lure'; }
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', true);
    } else {
      if (!trail.length || !lastMoveAt) { ghostOff(); return; }
      let pt = trail[0];
      for (const p of trail) { if (t - p.at >= IC_TRICKSTER.GHOST_TRAIL_MS) pt = p; else break; }
      gx = pt.x; gy = pt.y;
      ghostMode = 'trail';
      setCls(ghostEl, 'on', true);
      setCls(ghostEl, 'lure', false);
    }
    try {
      ghostEl.style.transform = 'translate3d(' + gx.toFixed(1) + 'px,' + gy.toFixed(1) + 'px,0)';
    } catch (e) { /* noop */ }
    /* prune, but always keep the newest point: a stall must not erase the
       hand's last known position out from under the lure handoff */
    while (trail.length > 1 && t - trail[0].at > IC_TRICKSTER.GHOST_TRAIL_KEEP_MS) trail.shift();
  }

  function armGhost() {
    if (!ghostEligible()) return;
    ghostEl = el('i', 'g-ic-tk-ghost', layer);
    if (!ghostEl) return;
    onMove = (ev) => {
      if (destroyed) return;
      try {
        const s = nodes.stage.getBoundingClientRect();
        const x = Number(ev && ev.clientX) - s.left;
        const y = Number(ev && ev.clientY) - s.top;
        if (!Number.isFinite(x) || !Number.isFinite(y)) return;
        lastMoveAt = nowMs();
        trail.push({ x, y, at: lastMoveAt });
      } catch (e) { /* no rects under the DOM double: the ghost simply sleeps */ }
    };
    onLeave = () => { hovering = false; };
    try {
      nodes.stage.addEventListener('pointermove', onMove);
      nodes.stage.addEventListener('pointerleave', onLeave);
    } catch (e) { /* noop */ }
    ghostTimer = every(IC_TRICKSTER.GHOST_TICK_MS, ghostTick);
    say('trickster: ghost armed');
  }

  function disarmGhost() {
    if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
    try {
      if (onMove && nodes.stage && nodes.stage.removeEventListener) {
        nodes.stage.removeEventListener('pointermove', onMove);
      }
      if (onLeave && nodes.stage && nodes.stage.removeEventListener) {
        nodes.stage.removeEventListener('pointerleave', onLeave);
      }
    } catch (e) { /* noop */ }
    onMove = null;
    onLeave = null;
    ghostOff();
  }

  /** The reveal's theatre ends: the ring straightens, the ghost dies. The
   *  flicker is NOT touched here - it runs on the slide, where the score chip
   *  is static, and index.js's own next hud() repaint is what ends it. */
  function endReveal() {
    straightenRing();
    ghostOff();
    revealKind = null;
  }

  /** Everything honest, right now. pause / end / destroy only. */
  function allHonest() {
    endReveal();
    if (flickTimer) { cancel(flickTimer); flickTimer = 0; }
    restoreFlicker();
  }

  /** Every outcome ends the reveal's theatre. The flicker is dealt at the
   *  LOAD (see load()), because the slide is the only stretch of this class
   *  where the score chip stands still long enough to be misread. */
  function outcome() {
    if (!armed || destroyed || stopped || paused) return;
    endReveal();
    hideTell();
  }

  /* ------------------------------------------------------------------ api */
  return {
    /** Deal the class. index.js calls once, at the first deal. */
    start() {
      if (!armed || destroyed || started) { if (!armed) say('trickster: disarmed'); return; }
      started = true;
      if (!build()) { say('trickster: no layer'); return; }
      plan = buildPlan();
      armGhost();
      say('trickster: dealt ' + plan.length + ' cards'
        + (plan.length ? ' (' + plan.map((d) => d.card + '@' + d.at).join(', ') + ')' : '')
        + ' tier ' + tier + (reduced ? ' reduced' : '') + (coarse ? ' coarse' : ''));
    },

    /** The one dial. Rides the lamp's brightness, nothing else. */
    setHeat(h) {
      const v = Number(h);
      heat = Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : heat;
      try { if (layer) layer.style.setProperty('--ic-tk-heat', String(Math.round(heat * 100) / 100)); }
      catch (e) { /* noop */ }
    },

    /** A bubble is at the mouth: the tell lights, truthfully or not - and the
     *  slide is where a stat flicker gets its quiet moment. */
    load(e) {
      if (!armed || destroyed || stopped || paused) return;
      endReveal();
      hideTell();
      const ev = e || {};
      const kind = ev.kind === 'denied' ? 'denied' : 'good';
      const idx = Math.round(Number(ev.idx) || 0);
      let lying = false;
      if (tier >= IC_TRICKSTER.LIE_FROM_TIER && idx >= IC_TRICKSTER.FIRST_DEAL_IDX) {
        const slot = slotFor('lie', idx, false);
        if (slot) {
          if (!canFire() || isHalted()) requeue(slot, 'halted');
          else { spend(slot); lying = true; fired.lie += 1; say('trickster: the tell lies at ' + idx); }
        }
      }
      showTell(lying ? (kind === 'denied' ? 'good' : 'denied') : kind, lying);

      if (tier >= IC_TRICKSTER.FLICK_FROM_TIER && idx >= IC_TRICKSTER.FIRST_DEAL_IDX) {
        const slot = slotFor('flick', idx, false);
        if (slot) {
          if (!canFire() || isHalted()) requeue(slot, 'halted');
          else {
            spend(slot);
            /* let index.js's own deal-time hud() repaint land first, then lie
               about the number it just wrote */
            after(IC_TRICKSTER.FLICK_DELAY_MS, () => {
              if (destroyed || stopped || paused || !capsOkNow() || isHalted()) return;
              if (dealFlicker()) say('trickster: stat flicker at ' + idx);
            });
          }
        }
      }
    },

    /** The slide: the tell holds through it. Nothing else to do. */
    slide() { /* the lamp is already lit; the chute keeps its own secret */ },

    /** The reveal: the truth arrives, the lamp dies, the ring may bend. */
    reveal(e) {
      if (!armed || destroyed || stopped || paused) return;
      const ev = e || {};
      hideTell();
      revealKind = ev.kind === 'denied' ? 'denied' : 'good';
      hovering = false;
      if (revealKind === 'denied') {
        const idx = Math.round(Number(ev.idx) || 0);
        const slot = slotFor('ring', idx, true);
        if (slot) {
          if (!canFire() || isHalted()) requeue(slot, 'halted');
          else if (bendRing(ev.windowMs)) { spend(slot); say('trickster: crooked ring at ' + idx); }
        }
      }
    },

    pop() { outcome(); },
    drift() { outcome(); },
    denyPass() { outcome(); },
    denyHit() { outcome(); },

    /** The pointer is over the one target: the lure has done its work. */
    hover(on) {
      if (!armed || destroyed) return;
      hovering = !!on;
      if (hovering) lastMoveAt = nowMs();
    },

    /** The class is over: no card fires again, every lie comes off. */
    end() {
      stopped = true;
      hideTell();
      allHonest();
      disarmGhost();
    },

    pause() {
      if (paused) return;
      paused = true;
      if (ghostTimer) { stopEvery(ghostTimer); ghostTimer = 0; }
      hideTell();
      allHonest();
    },

    resume() {
      if (!paused) return;
      paused = false;
      if (!destroyed && !stopped && ghostEligible() && ghostEl && !ghostTimer) {
        ghostTimer = every(IC_TRICKSTER.GHOST_TICK_MS, ghostTick);
      }
    },

    destroy() {
      destroyed = true;
      stopped = true;
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      for (const h of Array.from(chains)) stopEvery(h);
      chains.clear();
      /* the truth goes back on nodes this deck does not own, before it dies */
      try { setCls(nodes.holdring, 'g-ic-tk-bent', false); } catch (e) { /* noop */ }
      try {
        const chip = nodes.score;
        if (chip) {
          setCls(chip, 'g-ic-tk-flick', false);
          if (flickLie != null && chip.textContent === flickLie && flickPrev != null) chip.textContent = flickPrev;
        }
      } catch (e) { /* noop */ }
      flickPrev = null; flickLie = null;
      disarmGhost();
      try { if (layer) layer.remove(); } catch (e) { /* noop */ }
      layer = null; tellEl = null; ringEl = null; ghostEl = null;
      trail.length = 0;
      plan = [];
    },

    /** Diagnostics for the harness and the rig; not part of the contract. */
    diagnostics() {
      return {
        armed, tier, reduced, coarse, heat,
        plan: plan.map((d) => ({ at: d.at, card: d.card, used: d.used, tries: d.tries })),
        fired: Object.assign({}, fired),
        folded,
        tell: { on: tellOn, shown: tellKind, lying: tellLying },
        ring: { on: ringOn },
        ghost: { armed: !!ghostEl, mode: ghostMode, trail: trail.length, lures: fired.lure },
        flicker: { live: flickLie != null },
        cueRoad: typeof opts.cue === 'function',
        revealKind, paused, stopped, destroyed,
      };
    },
  };
}

export default createIcTrickster;
