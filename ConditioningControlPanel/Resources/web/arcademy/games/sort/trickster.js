/* ============================================================================
 * games/sort/trickster.js - DECK III of the House Rules, dealt into SORT.
 *
 * A sort has exactly one verb and exactly one truth: the tag on the row. So
 * every card here attacks what the player READS on the way to the swipe, and
 * NONE of them touches the pairing. Seven cards:
 *
 *   THE FREEZE          a loop card holds its poster for 400ms and then plays.
 *                       A real <video> is really paused; a gif cannot be, so a
 *                       flat frost plate is what stops the eye (never a filter
 *                       over a live decode, trap 36). The ring keeps closing
 *                       the whole time - the freeze costs you the read, not
 *                       the clock, and the clock never lied.
 *   THE GLIMPSE         a trigger word sub-flashes over a NOISE card. Your own
 *                       word, on the pile that is not yours. The engine owns
 *                       the alpha and the duration; we own only the choice.
 *   THE DOPPELGANGER    a card you have already seen arrives MIRRORED. The tag
 *                       is untouched, the url is untouched, the answer is
 *                       identical - only your certainty moves.
 *   THE GHOST CARD      stall 900ms on a card and a ghost of it drifts one way,
 *                       as if it had already gone. It cannot be grabbed, it
 *                       cannot be swiped, and it changes nothing at all.
 *   THE STAT FLICKER    the chain chip reads a few links off for 120ms and then
 *                       corrects itself. The ledger never moved; confidence
 *                       did. If a real repaint lands mid-lie the restore
 *                       STANDS DOWN rather than stomping the truth.
 *   THE CROOKED RING    the ring's FACE bends: it races through the boring
 *                       middle (shows less time left than you have) and meets
 *                       the truth exactly at the last 15%, so the hand itches
 *                       early and the finish is never a lie. index.js keeps
 *                       writing the TRUE fraction into --sort-ring every tick;
 *                       we write a SECOND variable and the sheet switches which
 *                       one the arc reads while the card is crooked. The
 *                       verdict, the PERFECT window and the pass are all read
 *                       off the room's own clock and are untouched.
 *   THE UNRELIABLE LABEL the WORD under a stamp glyph lies for one card: YES
 *                       wears the NO word and NO wears the YES word. The GLYPH
 *                       (a heart and a slash) is the truth and never moves.
 *                       This is Law IV taught directly: stop reading, look.
 *
 * DEALING RULE (House Rules Deck III + the contract): slots are dealt ONCE, at
 * start, from the seeded plan - never live rng. Budget 0 / 4 / 6 / 8 by tier
 * (tier 1 deals NOTHING and that is the point: the first year is honest), at
 * least 5 CARDS apart, and never before the eighth card of the class. A slot
 * whose card cannot carry it (a still for the freeze, a target for the glimpse,
 * a first-sighting for the doppelganger) waits for one more card and then
 * folds. A retake replays the identical deal.
 *
 * THE WINDOW IS THE CLASS, NOT A CONSTANT. The slots are card INDICES, and how
 * many cards a class contains is a function of its BUDGET and its tier's ring
 * pace - so the window has to be too. It used to be
 * `FIRST_CARD + MIN_GAP * budget + 12`, a span sized by eye for the old 120s
 * bell; at the 180s budget that same span covered barely the first 30% of the
 * class and every lie in the room was spent before the halfway mark.
 * `expectedCards` below reads the pace straight off chain.js and the plan is
 * laid over the first WINDOW_FRAC of it. The DEAL COUNTS are deliberately
 * unchanged: 8 lies over the first ~150 cards of a tier-4 class reads as a RATE
 * (about one every 19 cards, ~17s), which is what a trickster deck is for.
 *
 * TABLE LAW AUDIT (House Rules)
 *   I   ledger honest - nothing here reads or writes chain, rung, accuracy,
 *       the grade or card.tag, and no card can change WHEN the ring closes or
 *       WHAT the room scores. The flicker writes the chip's TEXT and puts back
 *       the exact string it read. The crooked ring paints a variable the room
 *       does not read.
 *   II  input honest  - the top card is the one hit target and no card here
 *       moves it, covers it, resizes it or delays it. The frost and the ghost
 *       are pointer-events:none; the ghost cannot be clicked; the label swaps
 *       TEXT inside a stamp that was already pointer-events:none.
 *   III never still   - the ghost drifts, the frost breathes with the card.
 *   IV  images/glyphs - the label card is the law itself; the glimpse renders
 *       one of the player's OWN trigger words, never a string of ours.
 *   V   seeded        - per-tag mulberry32 off seed+'|sort-trickster|<tag>',
 *       append-only. The plan is a function of the seed and the tier alone.
 *   VI  exits sacred  - bgIntensity 0 disarms the deck; REDUCED MOTION leaves
 *       exactly two cards standing, the two that are not motion (the flicker
 *       is a number, the label is a word); every timer rides the game's
 *       registry, so a freeze kills them; destroy() puts every live lie back.
 *   VII lexicon       - this file renders NO string of its own. The glimpse's
 *       word is the player's; the label's two words are the room's own
 *       sort_stamp_yes / sort_stamp_no, swapped.
 *
 * THE LATE-BUILD GUARD: index.js imports its decks DYNAMICALLY, so the room can
 * open (and call start()) before this module exists. The deal handler arms the
 * deck if start() never reached it.
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';
import { capForTier, rungForStreak, ringMsFor } from './chain.js';

export const TRICKSTER = Object.freeze({
  /** Dealt slots per class, by tier. Tier 1 deals nothing at all. */
  DEALS: Object.freeze({ 1: 0, 2: 4, 3: 6, 4: 8 }),
  /** Never before this card of the class. */
  FIRST_CARD: 8,
  /** At least this many cards between two slots. */
  MIN_GAP: 5,
  /** A slot whose card cannot carry it waits this many cards, then folds. */
  RETRY_MAX: 1,

  /* ----------------------------------------------------------- THE WINDOW --
   * How much of a class may carry a lie, and the pace model that answers how
   * long a class IS. Read only by expectedCards() and buildPlan().
   */
  /** The plan covers the first this-much of the class's expected card count.
   *  Not 1.0: the last stretch is where a player is holding their best rung and
   *  the room wants that finish clean, and a slot dealt past the last card the
   *  class actually reaches is a lie that never happens. */
  WINDOW_FRAC: 0.78,
  /** The budget buildPlan assumes when a caller hands it none. Mirrors
   *  index.js's own `timeBudgetSec` (and registry.js GAME_META.sort). */
  DEFAULT_BUDGET_SEC: 180,
  /** THE PACE, and it is two numbers because a card costs two things. A
   *  competent player swipes inside the gold arc (chain.js: the last 40% of
   *  the ring), so they spend most of a ring but never all of it... */
  PACE_RING_FRAC: 0.85,
  /** ...plus the part of a card that is not the ring at all: the fling, the
   *  thud into the wall and the next card's spring. Sized off swipe.js's own
   *  travel, and it is why a 0.75s ring is not a 0.75s card. */
  PACE_OVERHEAD_MS: 180,
  /** A guard rail on the pace walk, never a design number: the loop below may
   *  not run past this many cards whatever it is handed. */
  MAX_EXPECTED_CARDS: 900,

  /** The seven cards. */
  CARDS: Object.freeze(['freeze', 'glimpse', 'doppel', 'ghost', 'flicker', 'crooked', 'label']),
  /** The two that survive reduced motion: a number and a word, not motion. */
  STILL_CARDS: Object.freeze(['flicker', 'label']),
  /** Deal weights over the eligible cards, renormalised. */
  WEIGHTS: Object.freeze({
    freeze: 0.16, glimpse: 0.14, doppel: 0.16, ghost: 0.14,
    flicker: 0.14, crooked: 0.14, label: 0.12,
  }),

  /** THE FREEZE: how long the poster holds. */
  FREEZE_MS: 400,
  /** THE GHOST: the stall that summons it, and how long it drifts. */
  GHOST_STALL_MS: 900,
  GHOST_MS: 1400,
  GHOST_DX: Object.freeze([70, 130]),
  GHOST_ROT: Object.freeze([5, 11]),
  /** THE STAT FLICKER: how long the wrong number stands, and how far off. */
  FLICK_MS: 120,
  FLICK_MIN: 2,
  FLICK_SPAN: 7,
  /** THE CROOKED RING: the honest tail, the bend depth, the ramp, the tick. */
  RING_HONEST: 0.15,
  RING_BEND: 0.16,
  RING_RAMP: 0.32,
  RING_TICK_MS: 40,
});

/* ============================================================================
 * THE BEND, PURE. Given the TRUE remaining fraction of the ring (1 when the
 * card arrives, 0 when it closes), answer the fraction to PAINT.
 *
 *   f(1) = 1 and f(0) = 0                the ends never lie
 *   f(r) = r for r <= honest             the last 15% is the truth, exactly
 *   f(r) < r for honest < r < 1          the middle races ahead
 *   monotone non-decreasing in r         the face never runs backwards
 *
 * The bend fades IN above the honest zone on a smoothstep ramp, so the face
 * ARRIVES at the truth rather than snapping onto it. Monotonicity needs
 * bend * pi < 1; the shipped 0.16 leaves plenty of room.
 * ==========================================================================*/
export function bendRingFace(remain, honest, bend) {
  const r = Number(remain);
  if (!isFinite(r)) return 0;
  const x = r < 0 ? 0 : r > 1 ? 1 : r;
  const H = isFinite(honest) ? Math.max(0, Math.min(0.9, honest)) : TRICKSTER.RING_HONEST;
  if (x <= H || x >= 1) return x;
  const A0 = isFinite(bend) ? bend : TRICKSTER.RING_BEND;
  const ramp = Math.max(0, Math.min(1, (x - H) / TRICKSTER.RING_RAMP));
  const A = A0 * ramp * ramp * (3 - 2 * ramp);
  return Math.max(H, x - A * Math.sin(Math.PI * x));
}

/* ============================================================================
 * HOW LONG A CLASS IS, IN CARDS. PURE, and the whole reason this file imports
 * chain.js: the ring pace lives there and a second copy of it here would rot.
 *
 * THE WALK. A card costs `ring * PACE_RING_FRAC + PACE_OVERHEAD_MS`, and the
 * ring is whatever rung the player's clean streak has earned (chain.js's
 * RUNG_STEPS ladder, ceilinged by the tier's own cap). So the model walks the
 * climb the way a player actually climbs it - card 1 is a 2.4s ring and card 35
 * is a 0.75s one - instead of pretending the whole class runs at the floor
 * ring, which would over-count a tier-4 class by about a fifth.
 *
 * It assumes a CLEAN run, and that is the right end of the band to model from:
 * a player who is wrong a lot plays a SHORTER class in cards, so the plan runs
 * out of class before it runs out of slots and the last slots simply never
 * arrive - a case this deck already has a path for. Modelling the sloppy player
 * instead would crowd every lie back into the opening for everybody else.
 *
 * WHAT IT ANSWERS at the shipped 180s budget (and at the 120s budget this room
 * used to run, for the record):
 *
 *     tier   cap rung   floor ring     120s     180s
 *       1        5         1200ms       ~95     ~143
 *       2        6         1050ms      ~102     ~157
 *       3        7          900ms      ~112     ~175
 *       4        8          750ms      ~124     ~197
 *
 * @param {number} budgetSec  the class's own timeBudgetSec
 * @param {number} tier       grade tier 1..4
 * @returns {number} cards a clean, competent run of that class reaches
 * ==========================================================================*/
export function expectedCards(budgetSec, tier) {
  const tr = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const secs = Number(budgetSec);
  const sec = isFinite(secs) && secs > 0 ? Math.min(600, secs) : TRICKSTER.DEFAULT_BUDGET_SEC;
  const cap = capForTier(tr);
  let left = sec * 1000;
  let n = 0;
  while (n < TRICKSTER.MAX_EXPECTED_CARDS) {
    const cost = ringMsFor(rungForStreak(n, cap)) * TRICKSTER.PACE_RING_FRAC
      + TRICKSTER.PACE_OVERHEAD_MS;
    if (!(cost > 0) || cost > left) break;           // the bell lands mid-card
    left -= cost;
    n += 1;
  }
  return n;
}

/* ============================================================================
 * THE PLAN, PURE. Slots are CARD INDICES, because a card is the only moment a
 * lie can attach to in this room. Same seed and tier and BUDGET, same deal,
 * forever - `budgetSec` joins the plan's inputs, and the room reads it off the
 * class state it already holds, so a retake of the same class deals the same
 * lies in the same places.
 * ==========================================================================*/
export function buildPlan({ seed, tier, reduced, budgetSec } = {}) {
  const tr = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  const budget = TRICKSTER.DEALS[tr] || 0;
  const cards = reduced ? TRICKSTER.STILL_CARDS.slice() : TRICKSTER.CARDS.slice();
  if (!budget || !cards.length) return [];
  const rWhen = makeRng(String(seed || 'sort') + '|sort-trickster|when');
  const rWhat = makeRng(String(seed || 'sort') + '|sort-trickster|what');

  /* THE WINDOW. `span` is the EXCLUSIVE end of the draw below, so the last card
     that can carry a lie is span - 1.
       window  the first WINDOW_FRAC of the class THIS budget and tier deal
       floorSp the old fixed span, kept as a FLOOR and nothing else: it is the
               narrowest window `budget` slots MIN_GAP apart can fit in at all,
               so a missing or nonsense budget can never deal a plan tighter
               than the deck was designed to allow
     At 180s the window always wins - tier 4 is 8 + floor(.78 * 197) = 161
     against a floor of 60 - and the eight slots land ~19 cards apart instead
     of ~6, which is the difference between a rate and a burst. */
  const expect = expectedCards(budgetSec, tr);
  /* NOT named `window`: this module is loaded in a browser and a local const by
     that name would shadow the global for the whole function. */
  const windowEnd = TRICKSTER.FIRST_CARD + Math.floor(TRICKSTER.WINDOW_FRAC * expect);
  const floorSp = TRICKSTER.FIRST_CARD + TRICKSTER.MIN_GAP * budget + 12;
  const span = Math.max(floorSp, windowEnd);
  const at = [];
  for (let i = 0; i < budget; i++) {
    at.push(TRICKSTER.FIRST_CARD + Math.floor(rWhen() * (span - TRICKSTER.FIRST_CARD)));
  }
  at.sort((a, b) => a - b);
  for (let i = 1; i < at.length; i++) {
    if (at[i] - at[i - 1] < TRICKSTER.MIN_GAP) at[i] = at[i - 1] + TRICKSTER.MIN_GAP;
  }

  const pick = () => {
    let total = 0;
    for (const c of cards) total += Math.max(0, Number(TRICKSTER.WEIGHTS[c]) || 0);
    if (total <= 0) return cards[Math.floor(rWhat() * cards.length)] || cards[0];
    let x = rWhat() * total;
    for (const c of cards) {
      x -= Math.max(0, Number(TRICKSTER.WEIGHTS[c]) || 0);
      if (x <= 0) return c;
    }
    return cards[cards.length - 1];
  };

  const out = [];
  let prev = null;
  for (const a of at) {
    let card = pick();
    /* never the same card twice running: a lie you can predict is a tell */
    if (card === prev && cards.length > 1) {
      const i = cards.indexOf(card);
      card = cards[(i + 1) % cards.length];
    }
    prev = card;
    out.push({ at: a, card, used: false, tries: 0 });
  }
  return out;
}

/* ------------------------------------------------------------------ tools -- */
function el(tag, cls) {
  try {
    if (typeof document === 'undefined' || !document.createElement) return null;
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function setAttr(node, k, v) { try { if (node && node.setAttribute) node.setAttribute(k, String(v)); } catch (e) { /* DOM double */ } }
function delAttr(node, k) { try { if (node && node.removeAttribute) node.removeAttribute(k); } catch (e) { /* noop */ } }
function setVar(node, k, v) { try { if (node && node.style && node.style.setProperty) node.style.setProperty(k, String(v)); } catch (e) { /* noop */ } }
function getVar(node, k) {
  try {
    if (node && node.style && typeof node.style.getPropertyValue === 'function') {
      return node.style.getPropertyValue(k);
    }
  } catch (e) { /* noop */ }
  return '';
}
function addCls(node, c) { try { if (node && node.classList) node.classList.add(c); } catch (e) { /* noop */ } }
function delCls(node, c) { try { if (node && node.classList) node.classList.remove(c); } catch (e) { /* noop */ } }
function drop(node) { try { if (node && node.remove) node.remove(); } catch (e) { /* noop */ } }
function lerp(band, x) { const t = x < 0 ? 0 : x > 1 ? 1 : x; return band[0] + (band[1] - band[0]) * t; }
/**
 * Find the first descendant carrying a class. Walks `children` BY INDEX and
 * never with Array.isArray (trap 49: a shim hands back an Array and a browser
 * an HTMLCollection, and the guard that tested for one went green on the wrong
 * world). querySelector is not used either - the DOM double answers null.
 */
function findByClass(root, cls, depth) {
  if (!root) return null;
  const kids = root.children;
  if (!kids) return null;
  const d = depth == null ? 4 : depth;
  for (let i = 0; i < kids.length; i++) {
    const k = kids[i];
    if (!k) continue;
    try { if (k.classList && k.classList.contains(cls)) return k; } catch (e) { /* noop */ }
  }
  if (d <= 0) return null;
  for (let i = 0; i < kids.length; i++) {
    const found = findByClass(kids[i], cls, d - 1);
    if (found) return found;
  }
  return null;
}
function isVideoUrl(url, mime) {
  if (mime && /^video\//i.test(String(mime))) return true;
  return /\.(mp4|webm|m4v|mov)(\?|#|$)/i.test(String(url || ''));
}

/* ============================================================================
 * THE DECK
 * ==========================================================================*/
export function create(o) {
  const bag = o || {};
  const ctx = bag.ctx || {};
  const bus = bag.bus || { on() { return () => {}; } };
  const readState = typeof bag.S === 'function' ? bag.S : () => null;
  const timers = bag.timers || null;
  const engine = bag.engine || null;
  const reduced = !!bag.reduced;
  const say = typeof bag.log === 'function' ? bag.log : () => {};
  const t = typeof bag.t === 'function' ? bag.t : (k, f) => (f == null ? k : f);

  const armedBase = !!timers && typeof timers.after === 'function'
    && typeof document !== 'undefined';

  function capsOk() {
    let v = null;
    try {
      const ch = engine && typeof engine.channels === 'function' ? engine.channels() : null;
      if (ch && ch.bgIntensity != null) v = Number(ch.bgIntensity);
    } catch (e) { /* noop */ }
    if (v == null) {
      try { if (ctx.caps && ctx.caps.bgIntensity != null) v = Number(ctx.caps.bgIntensity); }
      catch (e) { /* noop */ }
    }
    if (v == null || !isFinite(v)) return true;
    return v > 0.001;
  }

  const seed = (() => { try { return String((readState() || {}).seed || 'sort'); } catch (e) { return 'sort'; } })();
  const tier = (() => { try { return Math.max(1, Math.min(4, Math.round(Number((readState() || {}).gradeTier) || 1))); } catch (e) { return 1; } })();
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seed + '|sort-trickster|' + tag); streams.set(tag, s); }
    return s();
  };

  /* ---- state -------------------------------------------------------------- */
  let destroyed = false;
  let started = false;
  let stopped = false;
  let paused = false;
  let plan = [];
  /* what the plan above was laid over. Diagnostics only - the deal itself is
     already frozen into `plan` and nothing reads these back. */
  let planBudgetSec = 0;
  let planExpected = 0;
  let folded = 0;
  let played = 0;                     // cards ARMED so far: the slot index
  let topNode = null;
  let topCard = null;
  const dealtCards = [];              // what actually fired, for diagnostics
  const fired = {};

  /* live lies, each with its own undo */
  let freezeUndo = null;
  let ghostTimer = 0;
  let ghostNode = null;
  let ghostAlive = false;
  let flickTimer = 0;
  let flickLie = null;
  let flickPrev = null;
  let crookedBox = null;
  let crookedTimer = 0;
  const offs = [];

  const halted = () => destroyed || stopped || paused || !armedBase;
  function after(ms, fn) {
    if (!armedBase || destroyed) return 0;
    try { return timers.after(ms, () => { if (!destroyed) fn(); }); }
    catch (e) { return 0; }
  }
  function every(ms, fn) {
    if (!armedBase || destroyed || typeof timers.every !== 'function') return 0;
    try { return timers.every(ms, () => { if (!destroyed) fn(); }); }
    catch (e) { return 0; }
  }
  function cancel(id) {
    if (!id || !timers) return;
    try { if (typeof timers.clear === 'function') timers.clear(id); } catch (e) { /* noop */ }
  }
  function nodes() { const s = readState(); return (s && s.nodes) || null; }
  function count(k) { fired[k] = (fired[k] || 0) + 1; }

  /* ---- the slot ledger ---------------------------------------------------- */
  /** The first unused slot due at or before this card. */
  function slotDue(idx) {
    for (const s of plan) { if (!s.used && s.at <= idx) return s; }
    return null;
  }
  function requeue(slot, why) {
    slot.tries += 1;
    if (slot.tries > TRICKSTER.RETRY_MAX) {
      slot.used = true;
      folded += 1;
      say('trickster: ' + slot.card + '@' + slot.at + ' folded (' + why + ')');
    }
  }
  function spend(slot, card) {
    slot.used = true;
    count(slot.card);
    dealtCards.push({ at: slot.at, card: slot.card, on: played, url: card ? card.url : null });
    say('trickster: ' + slot.card + ' on card ' + played);
  }

  /** Can this card carry this lie right now? */
  function suits(kind, card) {
    if (!card) return false;
    if (kind === 'freeze') return card.kind === 'loop';
    if (kind === 'glimpse') return card.tag === 'noise' && !!wordPool().length;
    if (kind === 'doppel') return !!card.seen;
    return true;
  }

  function wordPool() {
    const out = [];
    try {
      const tg = ctx.triggers;
      if (tg && tg.length) {
        for (let i = 0; i < tg.length; i++) {
          const w = tg[i] && tg[i].text ? String(tg[i].text) : '';
          if (w) out.push(w);
        }
      }
    } catch (e) { /* noop */ }
    if (!out.length) {
      try {
        const ws = ctx.words;
        if (ws && ws.length) for (let i = 0; i < ws.length; i++) if (ws[i]) out.push(String(ws[i]));
      } catch (e) { /* noop */ }
    }
    return out;
  }

  /* ============================================================ THE FREEZE = */
  function playFreeze(node, card) {
    if (!node) return false;
    const face = findByClass(node, 'g-sort-face', 2);
    const frost = el('div', 'g-sort-tk-frost');
    setAttr(node, 'data-tk-freeze', '1');
    if (frost) { try { node.appendChild(frost); } catch (e) { /* noop */ } }
    /* a REAL video is really paused; that is the honest half of this card */
    let wasVideo = false;
    try {
      if (face && face.tagName === 'VIDEO' && typeof face.pause === 'function') { face.pause(); wasVideo = true; }
    } catch (e) { /* noop */ }
    const undo = () => {
      delAttr(node, 'data-tk-freeze');
      drop(frost);
      if (wasVideo && face && typeof face.play === 'function') {
        try { const p = face.play(); if (p && p.catch) p.catch(() => {}); } catch (e) { /* autoplay policy */ }
      }
      freezeUndo = null;
    };
    freezeUndo = undo;
    after(TRICKSTER.FREEZE_MS, () => { if (freezeUndo === undo) undo(); });
    return true;
  }

  /* =========================================================== THE GLIMPSE = */
  function playGlimpse() {
    const words = wordPool();
    if (!words.length || !engine || typeof engine.fire !== 'function') return false;
    const word = words[Math.floor(roll('glimpse') * words.length) % words.length];
    const n = nodes();
    /* the ENGINE owns the alpha and the duration (its own sub-flash spec, the
       shortest thing it draws); we own the word and nothing else. It is welded
       clickSafe on the way out, so it can never take a press off the card. */
    engine.fire('sub_flash', {
      text: String(word),
      image: false,
      variant: 'centre',
      anchor: n && n.playfield ? n.playfield : undefined,
    });
    return true;
  }

  /* ======================================================= THE DOPPELGANGER */
  function playDoppel(node) {
    if (!node) return false;
    setAttr(node, 'data-tk-mirror', '1');
    return true;
  }

  /* ============================================================== THE GHOST */
  function armGhost(node, card) {
    cancel(ghostTimer);
    ghostTimer = after(TRICKSTER.GHOST_STALL_MS, () => {
      ghostTimer = 0;
      if (halted() || !ghostAlive) return;
      mintGhost(node, card);
    });
    ghostAlive = true;
    return true;
  }
  function mintGhost(node, card) {
    const n = nodes();
    const host = n && n.stack ? n.stack : null;
    if (!host) return;
    drop(ghostNode);
    ghostNode = el('div', 'g-sort-tk-ghost');
    if (!ghostNode) return;
    const dx = Math.round(lerp(TRICKSTER.GHOST_DX, roll('ghost-dx'))) * (roll('ghost-side') < 0.5 ? -1 : 1);
    const rot = (lerp(TRICKSTER.GHOST_ROT, roll('ghost-rot')) * (dx < 0 ? -1 : 1)).toFixed(1);
    setVar(ghostNode, '--sort-ghost-dx', dx + 'px');
    setVar(ghostNode, '--sort-ghost-r', rot + 'deg');
    /* NEVER a second decode (trap 36): a video ghost is its drawn back alone. */
    if (card && card.url && !isVideoUrl(card.url, card.mime)) {
      const img = el('img', '');
      if (img) {
        img.alt = '';
        setAttr(img, 'decoding', 'async');
        setAttr(img, 'draggable', 'false');
        if (typeof img.addEventListener === 'function') {
          img.addEventListener('error', () => drop(img));
        }
        img.src = card.url;
        ghostNode.appendChild(img);
      }
    }
    try { host.appendChild(ghostNode); } catch (e) { /* noop */ }
    const mine = ghostNode;
    after(TRICKSTER.GHOST_MS + 80, () => { if (ghostNode === mine) { drop(mine); ghostNode = null; } });
  }
  function killGhost() {
    ghostAlive = false;
    cancel(ghostTimer);
    ghostTimer = 0;
  }

  /* ======================================================== THE STAT FLICKER */
  function playFlicker() {
    const n = nodes();
    const chip = n && n.chipChain ? n.chipChain : null;
    const box = chip && chip.value ? chip.value : null;
    if (!box) return false;
    const truth = String(box.textContent == null ? '' : box.textContent);
    const now = Math.max(0, Math.round(Number(truth) || 0));
    const off = TRICKSTER.FLICK_MIN + Math.floor(roll('flick') * TRICKSTER.FLICK_SPAN);
    const lie = String(Math.max(0, now + (roll('flick-sign') < 0.5 ? -off : off)));
    if (lie === truth) return false;
    flickPrev = truth;
    flickLie = lie;
    try { box.textContent = lie; } catch (e) { return false; }
    addCls(chip.el, 'is-flick');
    cancel(flickTimer);
    flickTimer = after(TRICKSTER.FLICK_MS, () => {
      flickTimer = 0;
      delCls(chip.el, 'is-flick');
      /* STAND DOWN if a real repaint already landed: the ledger outranks us. */
      try { if (String(box.textContent) === flickLie) box.textContent = flickPrev; }
      catch (e) { /* noop */ }
      flickLie = null; flickPrev = null;
    });
    return true;
  }
  function endFlicker() {
    if (flickTimer) { cancel(flickTimer); flickTimer = 0; }
    const n = nodes();
    const chip = n && n.chipChain ? n.chipChain : null;
    if (chip) delCls(chip.el, 'is-flick');
    if (flickLie != null && chip && chip.value) {
      try { if (String(chip.value.textContent) === flickLie) chip.value.textContent = flickPrev; }
      catch (e) { /* noop */ }
    }
    flickLie = null; flickPrev = null;
  }

  /* ======================================================= THE CROOKED RING */
  function playCrooked(node) {
    if (!node) return false;
    const box = findByClass(node, 'g-sort-ringbox', 2);
    if (!box) return false;
    endCrooked();
    crookedBox = box;
    setAttr(box, 'data-crooked', '1');
    setVar(box, '--sort-ring-bend', '1');
    /* THE ROOM'S OWN NUMBER IS THE INPUT. index.js writes the true remaining
     * fraction into --sort-ring every tick; we read it back and paint the bent
     * twin. That way our tick rate is a refresh rate and never a second clock,
     * and a freeze that stops the room's ticks stops ours with it. */
    crookedTimer = every(TRICKSTER.RING_TICK_MS, () => {
      if (halted() || crookedBox !== box) return;
      const raw = getVar(box, '--sort-ring');
      const truth = raw === '' ? 1 : Number(raw);
      const face = bendRingFace(isFinite(truth) ? truth : 1);
      setVar(box, '--sort-ring-bend', String(Math.round(face * 1000) / 1000));
    });
    return true;
  }
  function endCrooked() {
    if (crookedTimer) { cancel(crookedTimer); crookedTimer = 0; }
    if (crookedBox) { delAttr(crookedBox, 'data-crooked'); crookedBox = null; }
  }

  /* ==================================================== THE UNRELIABLE LABEL */
  function playLabel(node) {
    if (!node) return false;
    const yes = findByClass(node, 'yes', 2);
    const no = findByClass(node, 'no', 2);
    if (!yes || !no) return false;
    const yw = findByClass(yes, 'g-sort-word-t', 2);
    const nw = findByClass(no, 'g-sort-word-t', 2);
    if (!yw || !nw) return false;
    /* THE GLYPH IS THE TRUTH AND IT DOES NOT MOVE. Only the two WORDS trade
       places, and only on this one card - it leaves with the card. */
    try {
      yw.textContent = t('sort_stamp_no', 'NO');
      nw.textContent = t('sort_stamp_yes', 'YES');
    } catch (e) { return false; }
    setAttr(yes, 'data-tk-lie', '1');
    setAttr(no, 'data-tk-lie', '1');
    return true;
  }

  /* =============================================================== THE DEAL */
  const PLAYERS = {
    freeze: (node, card) => playFreeze(node, card),
    glimpse: () => playGlimpse(),
    doppel: (node) => playDoppel(node),
    ghost: (node, card) => armGhost(node, card),
    flicker: () => playFlicker(),
    crooked: (node) => playCrooked(node),
    label: (node) => playLabel(node),
  };

  function ensureStarted() {
    if (started || destroyed || stopped) return;
    api.start();
  }

  function onDeal(ev) {
    ensureStarted();
    /* every lie belongs to ONE card: the last one leaves with it */
    clearLive();
    topNode = (ev && ev.node) || null;
    topCard = (ev && ev.card) || null;
    const idx = played;
    played += 1;
    if (halted() || !capsOk() || !plan.length) return;
    const slot = slotDue(idx);
    if (!slot) return;
    if (!suits(slot.card, topCard)) { requeue(slot, 'the card cannot carry it'); return; }
    const play = PLAYERS[slot.card];
    if (!play) { requeue(slot, 'no such card'); return; }
    let ok = false;
    try { ok = !!play(topNode, topCard); }
    catch (e) { ok = false; say('trickster: ' + slot.card + ' threw: ' + ((e && e.message) || e)); }
    if (ok) spend(slot, topCard);
    else requeue(slot, 'the room refused it');
  }

  /** A ghost only ever haunts a card nobody touched. */
  function onGrab() { killGhost(); }
  function onDrag() { killGhost(); }

  /** Every live lie is scoped to its card and dies when the card leaves. */
  function clearLive() {
    if (freezeUndo) { try { freezeUndo(); } catch (e) { freezeUndo = null; } }
    killGhost();
    drop(ghostNode); ghostNode = null;
    endCrooked();
    endFlicker();
  }

  /* ================================================================ THE API */
  const api = {
    start() {
      if (destroyed || started) return;
      started = true;
      stopped = false;
      /* THE BUDGET IS READ HERE, not at create(): the room owns the clock and
         `budgetMs` is only on the state once start() has run (index.js sets it
         in start(), and both the ordinary start and the late-build guard land
         after that). A state that cannot answer falls back to
         DEFAULT_BUDGET_SEC rather than to a window of nothing. */
      const budgetSec = (() => {
        try {
          const ms = Number((readState() || {}).budgetMs);
          return isFinite(ms) && ms > 0 ? ms / 1000 : TRICKSTER.DEFAULT_BUDGET_SEC;
        } catch (e) { return TRICKSTER.DEFAULT_BUDGET_SEC; }
      })();
      planBudgetSec = budgetSec;
      planExpected = expectedCards(budgetSec, tier);
      plan = buildPlan({ seed, tier, reduced, budgetSec });
      say('trickster: dealt ' + plan.length + ' slots'
        + (plan.length ? ' (' + plan.map((s) => s.card + '@' + s.at).join(', ') + ')' : '')
        + ' tier ' + tier + ', budget ' + Math.round(budgetSec) + 's over ~'
        + planExpected + ' cards'
        + (reduced ? ' reduced' : '') + (capsOk() ? '' : ' CAPPED'));
    },

    /** This deck has no heat: a lie is not louder because the room is hotter. */
    setHeat() {},

    pause() { paused = true; },
    resume() { paused = false; },

    end() {
      if (stopped) return;
      stopped = true;
      clearLive();
      say('trickster: ' + dealtCards.length + ' cards played, ' + folded + ' folded');
    },

    destroy() {
      if (!destroyed) { try { api.end(); } catch (e) { /* noop */ } }
      destroyed = true;
      clearLive();
      for (const off of offs) { try { off(); } catch (e) { /* noop */ } }
      offs.length = 0;
      plan = [];
      topNode = null; topCard = null;
    },

    diagnostics() {
      return {
        armed: armedBase && capsOk(),
        started, stopped, paused, destroyed, reduced, tier,
        budget: TRICKSTER.DEALS[tier] || 0,
        /* the window the plan was laid over, so a harness can assert the deal
           is a RATE over the whole class and not a burst in its opening */
        budgetSec: planBudgetSec,
        expectedCards: planExpected,
        lastSlot: plan.length ? plan[plan.length - 1].at : 0,
        played, folded,
        plan: plan.map((s) => ({ at: s.at, card: s.card, used: s.used, tries: s.tries })),
        dealt: dealtCards.slice(),
        fired: Object.assign({}, fired),
        live: {
          freeze: !!freezeUndo,
          ghost: !!ghostNode,
          crooked: !!crookedBox,
          flicker: flickLie != null,
        },
      };
    },
  };

  offs.push(bus.on('deal', onDeal));
  offs.push(bus.on('grab', onGrab));
  offs.push(bus.on('drag', onDrag));
  offs.push(bus.on('commit', clearLive));
  offs.push(bus.on('pass', clearLive));
  offs.push(bus.on('end', () => { try { api.end(); } catch (e) { /* noop */ } }));

  return api;
}

export default { TRICKSTER, create, buildPlan, bendRingFace, expectedCards };
