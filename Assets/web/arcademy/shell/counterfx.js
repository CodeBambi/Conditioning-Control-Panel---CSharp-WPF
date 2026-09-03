/* ============================================================================
 * shell/counterfx.js - THE HOUSE MOVES, cut for the counter and the Locker.
 *
 * The House Book names fifteen moves; this file is the five of them the Prize
 * Counter, the booth and RM 004 actually spend, written once so the three rooms
 * cannot disagree about how a purchase feels. THE BANK (reversed, because this
 * is the one screen where money goes OUT), THE THUD, THE SHIVER, THE GLOW's
 * warm cousin on a wallet that came up short, and THE CHARGE-HOLD.
 *
 * THREE LAWS, and every one of them is somebody else's law kept here:
 *
 *  1. LAW I HOLDS ABOVE THIS FILE. Nothing under this line reads a wallet,
 *     writes a balance, sends a message or decides whether a purchase happened.
 *     THE BANK is handed the two numbers it is to travel BETWEEN and the nodes
 *     it is to travel between them ON; it is a picture of an answer the host
 *     already gave. If the picture and the ledger ever disagree, the picture is
 *     wrong and the next render corrects it, which is the only direction that
 *     error is allowed to run.
 *  2. LAW II HOLDS TOO: nothing here moves a hitbox. THE CHARGE-HOLD draws its
 *     ring INSIDE the button it is charging, `position:absolute; inset:0`, with
 *     no pointer events of its own, so the target a player aims at is the same
 *     rectangle before, during and after the hold.
 *  3. THE BRAKE IS CHECKED BY THE CALLER AND AGAIN HERE. Every entry point
 *     takes `reduced` and `lite` and answers honestly with less: reduced motion
 *     gets the STATE with the travel taken off (and keeps the cue, because
 *     sound is where the beat lives when the physics are gone), and `lite`
 *     keeps the move and drops the particle count. `html.arc-reduced` is a
 *     blanket `animation-duration:.001s !important` in styles.css (trap 92), so
 *     a reduced-motion cut here may never BE an animation - it has to be a
 *     class that declares a resting look, or a road this file does not take at
 *     all. Both shapes are used below and both are commented where they are.
 *
 * NODE-SAFE, like every module the counter imports. The rooms it serves are
 * testable in bare node against a DOM double, so every measurement, every
 * listener and every class write is guarded and a missing capability is a
 * silent no-op rather than a throw. A room that cannot celebrate still sells.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * THE NUMBERS, all of them out of the House Book's own recipe cards.
 * -------------------------------------------------------------------------- */
export const CFX = Object.freeze({
  /** THE THUD: scale 2.1 -> 0.86 -> 1, brightness 2.2 -> 1, 340ms. */
  THUD_MS: 340,
  /** THE SHIVER: translateX +-4px, three cycles, no colour change. */
  SHIVER_MS: 250,
  /** THE ALMOST's gold ghost, the grade letter flickering to the better one. */
  GHOST_MS: 120,
  /** THE GLOW, warm cut: in fast, out slow. */
  GLOW_MS: 480,
  /** The bell tile's swing on a poke. */
  SWING_MS: 500,
  /** THE BANK, per token: 500-650ms arc, 60-80ms stagger, cap SEVEN. */
  BANK_FLY_MS: 560,
  BANK_STAGGER_MS: 70,
  BANK_MIN: 3,
  BANK_MAX: 7,
  /** ...and the cap again on a machine that asked for less. */
  BANK_MAX_LITE: 4,
  /** THE BANK forwards, on the tray's totals: 500ms of counting. */
  COUNTUP_MS: 500,
  /** THE CHARGE-HOLD's budget, and the reduced-motion fill that replaces it. */
  HOLD_MS: 700,
  HOLD_REDUCED_MS: 120,
  /**
   * How long the whole page may be asked to wait on a ceremony, ever.
   *
   * THE ARITHMETIC IT HAS TO COVER, so nobody has to rederive it: the bank's
   * seven tokens land at 6*70 + 560 = 980ms, the dearest tray beat holds 1600
   * after that, and the hand-over adds 200. 2780 is the worst honest case and
   * 3000 is the wall. Anything asking for more than this is a bug, and the
   * answer to a bug here is a late card - never a missing one.
   */
  HOLD_CEILING_MS: 3000,
});

/* ----------------------------------------------------------------------------
 * PLUMBING (the counter's own shapes, so this file imports nothing)
 * -------------------------------------------------------------------------- */

/** ONE AUDIO DOOR (trap 18), same request the three rooms make. Only cue names
 *  already in the SOUNDS table are ever fired (trap 115). */
export function cue(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function el(tag, cls, text) {
  const n = document.createElement(tag);
  if (cls) n.className = cls;
  if (text != null) n.textContent = text;
  return n;
}

function addCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.add === 'function') node.classList.add(cls); }
  catch (e) { /* noop */ }
}

function dropCls(node, cls) {
  try { if (node && node.classList && typeof node.classList.remove === 'function') node.classList.remove(cls); }
  catch (e) { /* noop */ }
}

function setVar(node, name, value) {
  try { if (node && node.style && typeof node.style.setProperty === 'function') node.style.setProperty(name, value); }
  catch (e) { /* noop */ }
}

function later(fn, ms) {
  try { return setTimeout(fn, Math.max(0, Math.round(ms) || 0)); }
  catch (e) { return null; }
}

function drop(id) { try { if (id) clearTimeout(id); } catch (e) { /* noop */ } }

/** Resolve a thunk-or-node into a node, never throwing. */
function pick(v) {
  try { return (typeof v === 'function') ? v() : (v || null); }
  catch (e) { return null; }
}

/** A node's box in viewport pixels, or null when there is nothing to measure -
 *  which is every call under the node double, and is why nothing below is
 *  allowed to assume it got one. */
function rectOf(node) {
  try {
    if (!node || typeof node.getBoundingClientRect !== 'function') return null;
    const b = node.getBoundingClientRect();
    if (!b || !(b.width > 0) || !(b.height > 0)) return null;
    return { x: b.left, y: b.top, w: b.width, h: b.height };
  } catch (e) { return null; }
}

function clampInt(n, lo, hi) {
  const v = Math.round(Number(n));
  if (!Number.isFinite(v)) return lo;
  return Math.max(lo, Math.min(hi, v));
}

function urlFor(rel, fallback) {
  try { return new URL(rel, import.meta.url).href; }
  catch (e) { return fallback; }
}

/* ----------------------------------------------------------------------------
 * THE SHEET, linked once and lazily (recordsroom.js's pattern).
 *
 * IT LINKS ITSELF rather than living in the three rooms' sheets, and that is
 * the point: a thud is a thud in the Locker, at the counter and on the booth's
 * tray, and three copies of one keyframe is three chances for them to drift.
 * The rooms' OWN sheets still own everything that is theirs - the almost row,
 * the marquee, the tiered tray beat - and this holds only the shared atoms.
 * -------------------------------------------------------------------------- */
export function fxSheet(doc, log) {
  try {
    const d = doc || ((typeof document !== 'undefined') ? document : null);
    if (!d || typeof d.createElement !== 'function') return null;
    const had = typeof d.getElementById === 'function' ? d.getElementById('cfx-styles') : null;
    if (had) return had;
    const link = d.createElement('link');
    link.id = 'cfx-styles';
    link.rel = 'stylesheet';
    link.href = urlFor('./counterfx.css', 'shell/counterfx.css');
    const head = d.head || d.body || null;
    if (head && typeof head.appendChild === 'function') head.appendChild(link);
    return link;
  } catch (e) {
    if (typeof log === 'function') log('counter fx stylesheet failed to link');
    return null;
  }
}

/* ----------------------------------------------------------------------------
 * THE ONE-SHOT CLASSES
 *
 * All four are the same shape: put a class on, take it off when the animation
 * has had its time. The timeout is the authority rather than `animationend`,
 * because a node that is re-rendered out from under the move never fires the
 * event and a class that outlives its element is a class that comes back on
 * the next paint of a reused node.
 * -------------------------------------------------------------------------- */

function oneShot(node, cls, ms) {
  if (!node) return null;
  /* THE SHEET COMES WITH THE MOVE. A room that only ever thuds should not have
   * to remember to link a stylesheet first, and the link is idempotent. */
  try { fxSheet(node.ownerDocument || null); } catch (e) { /* noop */ }
  dropCls(node, cls);
  /* Reading `offsetWidth` restarts an animation that is already running - the
   * standard reflow kick. Guarded because the double has no layout. */
  try { void node.offsetWidth; } catch (e) { /* noop */ }
  addCls(node, cls);
  return later(() => dropCls(node, cls), ms);
}

/**
 * THE THUD. The atom of confirmation: it happened, and it happened HERE.
 *
 * TWO CUTS, because the book's numbers are a STAMP's numbers. `scale 2.1 ->
 * 0.86 -> 1` is right for a mark the size of a badge and absurd for a shelf row
 * three hundred pixels wide - a row that grew to twice its width would push
 * every neighbour off the grid for a third of a second. So `mark:true` gets the
 * recipe card verbatim and the default gets the same curve at furniture scale,
 * and the two are fired on the SAME frame so the row flexes as its Yours mark
 * lands on it. One move, two amplitudes.
 *
 * REDUCED MOTION TAKES THE STATE, NOT THE TRAVEL. `html.arc-reduced` kills
 * every animation in the document (styles.css, trap 92), so the reduced cut
 * cannot be a shorter thud - it is `is-confirm`, a declared gold outline that
 * stands for half a second and then goes. The caller still fires the cue.
 */
export function thud(node, opts) {
  const o = opts || {};
  if (!node) return null;
  if (o.reduced) return oneShot(node, 'cfx-confirm', 520);
  return oneShot(node, o.mark ? 'cfx-thud-mark' : 'cfx-thud', CFX.THUD_MS);
}

/**
 * THE SHIVER. Failure with sympathy: three quick shakes, no red, no buzzer.
 * Reduced motion gets nothing at all here on purpose - a shiver IS the motion,
 * there is no state under it to show, and the `bump` cue the caller keeps is
 * the whole of what a player who asked for stillness should get.
 */
export function shiver(node, opts) {
  const o = opts || {};
  if (!node || o.reduced) return null;
  return oneShot(node, 'cfx-shiver', CFX.SHIVER_MS);
}

/**
 * THE GLOW, warm cut. Not the hover glow - this is the one that says LOOK AT
 * THE NUMBER, and it goes on the wallet chip when the wallet is what was
 * wrong. Gold rather than pink because it is about money, not about a verb.
 *
 * It survives reduced motion, and this is the trap-92 shape: the class
 * DECLARES the lit box-shadow and the animation only fades it in and out. With
 * the animation killed the declared value simply stands for its half second,
 * which is a light going on and off - no motion, same meaning.
 */
export function warmGlow(node, opts) {
  const o = opts || {};
  if (!node) return null;
  return oneShot(node, 'cfx-warm', o.reduced ? 600 : CFX.GLOW_MS);
}

/**
 * THE ALMOST's own frame: the row ghosts to gold and snaps back, the way a
 * grade letter flickers to the better one before it settles. 120ms, once, on
 * the press - never on a machine that asked for less, because a near-miss
 * staged with no motion is just a colour that was briefly wrong.
 */
export function ghostGold(node, opts) {
  const o = opts || {};
  if (!node || o.reduced || o.lite) return null;
  return oneShot(node, 'cfx-ghost', CFX.GHOST_MS);
}

/** The bell's answer to being poked: one swing, spring-damped, back to rest. */
export function swing(node, opts) {
  const o = opts || {};
  if (!node || o.reduced) return null;
  return oneShot(node, 'cfx-swing', CFX.SWING_MS);
}

/* ----------------------------------------------------------------------------
 * THE BANK, REVERSED
 *
 * The House Book's floor map asks for exactly this on a shop purchase and it
 * has never existed anywhere in the school: "the cost visibly leaves the
 * wallet, the item thuds into inventory". Forwards it is XP arriving; here it
 * is money going, so the tokens spawn AT the wallet chip and fly to the row,
 * the chip ticks DOWN as each one leaves, and the last one lands with the thud.
 *
 * THE COUNT IS A FEELING, NOT AN AMOUNT (the recipe card's own note): three
 * tokens for a poster, seven for a wide board, and never an eighth however
 * dear the row. A purse that spat out two thousand tickets would be a purse
 * that took two thousand frames to do it.
 *
 * IT MEASURES AT THE MOMENT IT FIRES AND RE-ASKS AT EVERY LANDING. The shelf
 * repaints under this move (a payout can land mid-flight), so the chip and the
 * row are handed in as THUNKS: the flight is already in the air and cannot be
 * re-aimed, but the number that ticks and the row that thuds are whichever
 * nodes are on screen when the moment arrives.
 * -------------------------------------------------------------------------- */

/** How many tokens this cost is worth, 3 to 7. */
export function bankCount(cost, lite) {
  const c = Math.max(0, Number(cost) || 0);
  const hi = lite ? CFX.BANK_MAX_LITE : CFX.BANK_MAX;
  let n = CFX.BANK_MIN;
  if (c >= 60) n = 4;
  if (c >= 200) n = 5;
  if (c >= 600) n = 6;
  if (c >= 1000) n = 7;
  return clampInt(n, CFX.BANK_MIN, hi);
}

/**
 * bankSpend(o) -> the total milliseconds the move will take, 0 when it did not
 * run (no DOM, nothing measurable, or reduced motion).
 *
 * @param {Object} o
 *   chipAt  () -> the wallet chip element the tokens leave from
 *   numAt   () -> the <b> inside it that carries the number
 *   landAt  () -> where the last token lands (the row's parcel)
 *   rowAt   () -> the row that takes the thud (defaults to landAt's owner)
 *   markAt  () -> the Yours mark, which takes the stamp-scale thud with it
 *   cur     't' | 'k' - which glyph flies
 *   cost    the price, for the token count
 *   from/to the two numbers the chip travels between (presentation only)
 *   reduced / lite / log
 */
export function bankSpend(o) {
  const c = o || {};
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || !doc.body || typeof doc.createElement !== 'function') return 0;
  if (c.reduced) return 0;

  const chip = pick(c.chipAt);
  const land = pick(c.landAt);
  const a = rectOf(chip);
  const b = rectOf(land);
  if (!a || !b) return 0;

  fxSheet(doc, c.log);

  const cur = (c.cur === 'k') ? 'k' : 't';
  const n = bankCount(c.cost, c.lite);
  const fly = CFX.BANK_FLY_MS;
  const stagger = CFX.BANK_STAGGER_MS;
  const total = ((n - 1) * stagger) + fly;

  /* THE LAYER HANGS OFF `document.body`, and for the tray beat's reason: every
   * overlay panel in this school is transformed while it slides, and a
   * transformed ancestor becomes the containing block for a fixed child - the
   * flight would be squashed into the panel that opened it. */
  let layer = null;
  try {
    layer = el('div', 'cfx-bank');
    layer.setAttribute('aria-hidden', 'true');
    doc.body.appendChild(layer);
  } catch (e) { return 0; }

  const ax = a.x + (a.w / 2);
  const ay = a.y + (a.h / 2);
  const bx = b.x + (b.w / 2);
  const by = b.y + (b.h / 2);

  for (let i = 0; i < n; i += 1) {
    /* A little spread at the source so five tokens are five tokens and not one
     * token drawn five times. Deterministic per index - no rng import, and the
     * shot rig gets the same picture twice. */
    const spread = ((i % 3) - 1) * 9;
    const rise = ((i % 2) ? -6 : 6);
    const out = el('span', 'cfx-coin');
    try {
      out.style.left = Math.round(ax + spread) + 'px';
      out.style.top = Math.round(ay + rise) + 'px';
      out.style.animationDuration = fly + 'ms';
      out.style.animationDelay = (i * stagger) + 'ms';
    } catch (e) { /* noop */ }
    setVar(out, '--cfx-dx', Math.round(bx - ax - spread) + 'px');

    /* THE ARC IS TWO AXES. The outer span carries X at a constant rate and the
     * inner one carries Y on an ease-in, so the path bows instead of ruling a
     * straight line between two points. One element could not do both without
     * a keyframe per destination. */
    const inner = el('span', 'cfx-coin-i');
    try {
      inner.style.animationDuration = fly + 'ms';
      inner.style.animationDelay = (i * stagger) + 'ms';
    } catch (e) { /* noop */ }
    setVar(inner, '--cfx-dy', Math.round(by - ay - rise) + 'px');

    const glyph = el('i', cur === 'k' ? 'arc-tok' : 'arc-tick', cur === 'k' ? '◉' : null);
    try { glyph.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
    inner.appendChild(glyph);
    out.appendChild(inner);
    try { layer.appendChild(out); } catch (e) { /* noop */ }
  }

  /* THE COUNTER TICKS DOWN AS EACH ONE LEAVES. The render that ran a moment
   * ago already painted the settled number, so the first thing this does is put
   * the OLD one back - which is presentation borrowing a frame it gives back
   * within the second, never a balance being authored. */
  const from = Number(c.from);
  const to = Number(c.to);
  const canTick = Number.isFinite(from) && Number.isFinite(to) && from !== to;
  if (canTick) {
    const num0 = pick(c.numAt);
    try { if (num0) num0.textContent = String(from); } catch (e) { /* noop */ }
  }

  const timers = [];
  for (let i = 0; i < n; i += 1) {
    const last = (i === n - 1);
    timers.push(later(() => {
      if (canTick) {
        const num = pick(c.numAt);
        const step = last ? to : Math.round(from - ((from - to) * ((i + 1) / n)));
        try { if (num) num.textContent = String(step); } catch (e) { /* noop */ }
      }
      if (!last) return;
      /* THE LANDING. The row flexes, the mark stamps, the cue lands on the same
       * frame - the book's rung 2 in one gesture. */
      const row = pick(c.rowAt) || pick(c.landAt);
      const mark = pick(c.markAt);
      thud(row, { reduced: false });
      if (mark) thud(mark, { reduced: false, mark: true });
      cue('thud', 0.42);
      try { if (layer && layer.remove) layer.remove(); } catch (e) { /* noop */ }
    }, (i * stagger) + fly));
  }

  /* A dead-man on the layer itself: a stalled clock must not leave glyphs
   * parked over the shelf for the rest of the sitting. */
  later(() => { try { if (layer && layer.remove) layer.remove(); } catch (e) { /* noop */ } }, total + 900);

  return total;
}

/* ----------------------------------------------------------------------------
 * THE HAND-OVER HOLD
 *
 * ONE CEREMONY, NOT TWO. The bank belongs to the ROW - the money leaving the
 * purse and landing on the thing - and the purchase reveal (shell/reveal.js)
 * belongs to the THING. Fired at the same instant they are two parties talking
 * over each other, which is the Brake's "two celebrations at once cancel each
 * other". So the counter arms a hold when it starts a bank, and the shell's
 * `arcademy-bought` waits it out: money leaves, the tray comes out with the
 * thing in it, and the card whooshes up out of that. One gesture, three
 * strokes, in the order the exchange actually happens.
 *
 * IT IS A CEILING, NEVER A GATE. `boughtHoldMs` can only ever answer a number
 * between 0 and HOLD_CEILING_MS, and the shell's dispatch is a `setTimeout`, so
 * a bug here delays a celebration and can never swallow one. Nothing about the
 * purchase itself is downstream of this: the wallet, the inventory and the row
 * all moved at `settle()`, a beat before the hold was armed.
 * -------------------------------------------------------------------------- */

let holdUntil = 0;

function now() {
  try { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
  catch (e) { return Date.now(); }
}

/** Ask the page to sit on `arcademy-bought` for `ms`. Longest wins; a shorter
 *  arm can never shorten a hold somebody else is already keeping. */
export function armBoughtHold(ms) {
  const n = Math.max(0, Math.min(CFX.HOLD_CEILING_MS, Math.round(Number(ms) || 0)));
  if (!n) return 0;
  const until = now() + n;
  if (until > holdUntil) holdUntil = until;
  return n;
}

/** How much of that hold is left, 0 when there is none. */
export function boughtHoldMs() {
  const left = holdUntil - now();
  if (!(left > 0)) return 0;
  return Math.min(CFX.HOLD_CEILING_MS, Math.round(left));
}

/** Drop any hold on the floor (a screen change, a destroy - test seam too). */
export function clearBoughtHold() { holdUntil = 0; }

/* ----------------------------------------------------------------------------
 * THE BANK, FORWARDS: a number that ARRIVES
 *
 * The audit's Q2 in one function - "watch the currency at the moment of gain;
 * a number that silently swaps is a missing bank". The tray's totals count up
 * from zero once per open. Reduced motion gets the number, immediately, which
 * is the same information with the travel taken off.
 * -------------------------------------------------------------------------- */
export function countUp(node, to, opts) {
  const o = opts || {};
  const target = Math.max(0, Math.round(Number(to) || 0));
  if (!node) return null;
  const write = (v) => { try { node.textContent = String(v); } catch (e) { /* noop */ } };
  if (o.reduced || target <= 0) { write(target); return null; }
  const ms = Math.max(120, Math.round(Number(o.ms) || CFX.COUNTUP_MS));
  const steps = Math.max(2, Math.min(20, target));
  const every = ms / steps;
  write(0);
  const timers = [];
  for (let i = 1; i <= steps; i += 1) {
    const last = (i === steps);
    timers.push(later(() => write(last ? target : Math.round(target * (i / steps))), every * i));
  }
  return { cancel() { for (const t of timers) drop(t); write(target); } };
}

/* ----------------------------------------------------------------------------
 * THE SPARKLE BURST - the garnish, never the event
 * -------------------------------------------------------------------------- */

/** 5-9 sparks fired radially from the middle of `host`, dying before they land.
 *  Never under lite, never under reduced motion, one burst per moment. */
export function sparkBurst(host, opts) {
  const o = opts || {};
  if (!host || o.reduced || o.lite) return 0;
  const doc = (typeof document !== 'undefined') ? document : null;
  if (!doc || typeof doc.createElement !== 'function') return 0;
  fxSheet(doc, o.log);
  const n = clampInt(o.count || 7, 5, 9);
  const wrap = el('div', 'cfx-sparks');
  try { wrap.setAttribute('aria-hidden', 'true'); } catch (e) { /* noop */ }
  for (let i = 0; i < n; i += 1) {
    const s = el('i', 'cfx-spark' + ((i % 2) ? ' is-gold' : ''));
    const ang = (Math.PI * 2 * i) / n;
    const reach = 42 + ((i % 3) * 13);
    setVar(s, '--cfx-sx', Math.round(Math.cos(ang) * reach) + 'px');
    setVar(s, '--cfx-sy', Math.round(Math.sin(ang) * reach) + 'px');
    try { s.style.animationDelay = (i * 14) + 'ms'; } catch (e) { /* noop */ }
    wrap.appendChild(s);
  }
  try { host.appendChild(wrap); } catch (e) { return 0; }
  later(() => { try { wrap.remove(); } catch (e) { /* noop */ } }, 700);
  return n;
}

/* ----------------------------------------------------------------------------
 * THE CHARGE-HOLD
 *
 * The Drop Tube's held verb, promoted to a shared primitive exactly as the
 * House Book asks: a conic ring fills over the budget with a tone rising under
 * it, release early is THE SHIVER and nothing sent, and a completed hold is THE
 * THUD and then the thing happens.
 *
 * WHY A HOLD AT ALL. A two-thousand-token unlock costs a week of good nights
 * and today it costs one click, with the only "are you sure" being the 1.1s
 * "asking the counter" that happens AFTER the decision. The hold is not
 * friction for its own sake - it is the ceremony arriving on the right side of
 * the choice. Cheap rows keep the click, because a held verb on a sixty-ticket
 * poster is friction with no ceremony in it.
 *
 * IT NEVER TOUCHES THE PURCHASE. `o.fire` is called once, on completion, and it
 * is the caller's own `propose()`. Every road that does not complete - a lift,
 * a cancel, a pointer leaving the button, a blur, a destroy - ends with nothing
 * sent and the row exactly as it was. The echo law is upstream of all of it.
 *
 * KEYBOARD IS A FIRST-CLASS ROAD, not an afterthought: Enter or Space held has
 * the same budget and the same two endings. `keydown` repeats are ignored so an
 * OS key-repeat cannot machine-gun the ring, and the caller does NOT wire a
 * click listener for a held row, so there is no second way in to disagree with.
 * -------------------------------------------------------------------------- */
export function chargeHold(btn, opts) {
  const o = opts || {};
  if (!btn) return null;
  const doc = (typeof document !== 'undefined') ? document : null;
  if (doc) fxSheet(doc, o.log);

  const reduced = !!o.reduced;
  const budget = reduced ? CFX.HOLD_REDUCED_MS : Math.max(120, Math.round(Number(o.ms) || CFX.HOLD_MS));

  let timer = null;
  let tones = [];
  let live = false;
  let dead = false;

  /* THE RING. Inside the button, under the label, taking no pointer at any
   * depth - LAW II, the hitbox is the button's and stays the button's. */
  let ring = null;
  try {
    ring = el('span', 'cfx-ring');
    ring.setAttribute('aria-hidden', 'true');
    ring.style.animationDuration = budget + 'ms';
    btn.appendChild(ring);
    addCls(btn, 'cfx-holdable');
  } catch (e) { ring = null; }

  /** The rising tone: five blips up the ratchet over the budget. audio.js's
   *  `pitch` seam multiplies every frequency and never the duration, so this
   *  climbs rather than speeding up (the chime ladder's own law). */
  function tone() {
    if (reduced) return;
    const steps = 5;
    for (let i = 0; i < steps; i += 1) {
      tones.push(later(() => {
        if (!live) return;
        cue('blip', 0.09, { pitch: 0.78 + (i * 0.19) });
      }, (budget / steps) * i));
    }
  }

  function stopTones() { for (const t of tones) drop(t); tones = []; }

  function start() {
    if (dead || live) return;
    live = true;
    addCls(btn, 'is-charging');
    tone();
    timer = later(() => {
      timer = null;
      if (!live) return;
      live = false;
      dropCls(btn, 'is-charging');
      stopTones();
      /* THE THUD, then the ask. In that order: the press is answered before
       * the counter is asked, so the 1.1s wait has already been earned. */
      thud(btn, { reduced, mark: true });
      cue('thud', 0.38);
      try { if (typeof o.fire === 'function') o.fire(); }
      catch (e) { if (typeof o.log === 'function') o.log('charge hold fire threw: ' + ((e && e.message) || e)); }
    }, budget);
  }

  function abort(quiet) {
    if (!live) return;
    live = false;
    drop(timer); timer = null;
    stopTones();
    dropCls(btn, 'is-charging');
    if (quiet) return;
    shiver(btn, { reduced });
    cue('bump', 0.2);
    try { if (typeof o.onShort === 'function') o.onShort(); }
    catch (e) { /* the hint is a courtesy */ }
  }

  const onDown = (ev) => {
    try { if (ev && ev.button != null && ev.button !== 0) return; } catch (e) { /* noop */ }
    start();
  };
  const onUp = () => abort(false);
  const onLeave = () => abort(true);
  const onKeyDown = (ev) => {
    try {
      if (!ev || ev.repeat) return;
      const k = ev.key;
      if (k !== 'Enter' && k !== ' ' && k !== 'Spacebar') return;
      if (typeof ev.preventDefault === 'function') ev.preventDefault();
    } catch (e) { return; }
    start();
  };
  const onKeyUp = (ev) => {
    try {
      const k = ev && ev.key;
      if (k !== 'Enter' && k !== ' ' && k !== 'Spacebar') return;
    } catch (e) { /* noop */ }
    abort(false);
  };

  const bound = [];
  function on(name, fn) {
    try {
      if (typeof btn.addEventListener !== 'function') return;
      btn.addEventListener(name, fn);
      bound.push([name, fn]);
    } catch (e) { /* the DOM double carries no listeners - never fatal */ }
  }

  on('pointerdown', onDown);
  on('pointerup', onUp);
  on('pointercancel', onLeave);
  on('pointerleave', onLeave);
  on('blur', onLeave);
  on('keydown', onKeyDown);
  on('keyup', onKeyUp);

  return {
    /** Is the ring filling right now? (test seam) */
    get charging() { return live; },
    get budget() { return budget; },
    destroy() {
      dead = true;
      abort(true);
      for (const [name, fn] of bound) {
        try { btn.removeEventListener(name, fn); } catch (e) { /* noop */ }
      }
      bound.length = 0;
      try { if (ring && ring.remove) ring.remove(); } catch (e) { /* noop */ }
      dropCls(btn, 'cfx-holdable');
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE REVEAL WATCH
 *
 * `html.arc-reveal-on` is shell/reveal.js's mark for "the purchase card is up",
 * and two things at the counter want to know: the receipt's own "Put it on"
 * (the card offers the same verb, and one screen must not offer it twice) and
 * the shelf's one breath (nothing breathes while a celebration runs, Law III).
 * The class arrives a beat AFTER the buy settles, so this watches for it rather
 * than asking once and being wrong.
 *
 * IT IS A SHORT WATCH ON PURPOSE. If the card has not opened within the window
 * it is not going to - the reveal hangs off the same echo the counter just
 * settled - and a listener left on the document for the rest of the sitting is
 * a leak with an opinion.
 * -------------------------------------------------------------------------- */
export const REVEAL_CLASS = 'arc-reveal-on';

/** Is the purchase card up right now? */
export function revealUp() {
  try {
    const de = (typeof document !== 'undefined') ? document.documentElement : null;
    return !!(de && de.classList && de.classList.contains(REVEAL_CLASS));
  } catch (e) { return false; }
}

/**
 * Call `cb` the moment the card goes up, if it does so within `withinMs`.
 * @returns {?Function} a canceller, or null when there was nothing to watch
 */
export function onRevealOn(cb, withinMs) {
  if (typeof cb !== 'function') return null;
  if (revealUp()) { try { cb(); } catch (e) { /* noop */ } return null; }
  const doc = (typeof document !== 'undefined') ? document : null;
  const de = doc && doc.documentElement;
  const Obs = (typeof MutationObserver === 'function') ? MutationObserver : null;
  if (!de || !Obs) return null;
  let obs = null;
  let bail = null;
  const stop = () => {
    try { if (obs) obs.disconnect(); } catch (e) { /* noop */ }
    obs = null;
    drop(bail); bail = null;
  };
  try {
    obs = new Obs(() => {
      if (!revealUp()) return;
      stop();
      try { cb(); } catch (e) { /* noop */ }
    });
    obs.observe(de, { attributes: true, attributeFilter: ['class'] });
  } catch (e) { return null; }
  bail = later(stop, Math.max(400, Math.round(Number(withinMs) || 3000)));
  return stop;
}

export default {
  CFX, cue, fxSheet, thud, shiver, warmGlow, ghostGold, swing,
  bankCount, bankSpend, armBoughtHold, boughtHoldMs, clearBoughtHold,
  countUp, sparkBurst, chargeHold, revealUp, onRevealOn, REVEAL_CLASS,
};
