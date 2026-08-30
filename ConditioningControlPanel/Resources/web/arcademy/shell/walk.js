/* ============================================================================
 * shell/walk.js - THE WALK (planning/arcademy/ORIENTATION.md §2).
 *
 * A player miniature that actually crosses the campus. Click a door and YOU
 * walk there, along the same corridor the nightly route and the student body
 * walk, dragging a gold dashed line behind you; the class starts when you
 * arrive. Where you have already been tonight stays on the map as a faint
 * residue until the run ends.
 *
 * FIVE LAWS, and every one of them is load-bearing:
 *
 *  1. ONE CORRIDOR GRAMMAR. Every coordinate in this file came out of
 *     `campus.js` - `walkLegs`, `gateLegs`, `stopAnchor`, `CAMPUS_GATE`. This
 *     module invents no geometry at all, for ghosts.js's LAW 2's reason: a
 *     second pathing system is precisely how a student ends up inside a wall.
 *
 *  2. ONE SPRITE. The miniature is `buildStudentSprite` from ghosts.js, the
 *     same body every ghost wears, so a mod that reskins the student body
 *     reskins you with it. `.gh-you` is the difference and it is CSS only:
 *     full opacity, a gold ring on the floor under the feet, ~1.15x, no name.
 *
 *  3. THE WALK IS DECORATION AND THE LAUNCH IS NOT. `onDone` fires EXACTLY
 *     once per `walkTo` - on arrival, on a skip, or out of `destroy()` with a
 *     walk still in the air. A class launch is never hostage to an animation,
 *     which is also why a walker built on a null mount still answers.
 *
 *  4. ANY INPUT SNAPS IT (attract Law II, byte for byte). One pointer or key
 *     event anywhere on the document ends the walk at its destination and
 *     fires `onDone` immediately. Repeat navigation must never feel taxed.
 *
 *  5. THE TRACE IS THE WAKE. The gold polyline GROWS behind the miniature -
 *     points appended as progress is made, never a mask or a draw-on trick -
 *     so what you see is literally where you have been. On arrival it fades
 *     into the residue, which is DATA (shell.js holds the list across screens)
 *     and is re-rendered from that data on every campus mount.
 *
 * NODE-SAFE BY CONSTRUCTION (ghosts.js's discipline): every document / rAF /
 * setTimeout touch is guarded, and the pure halves (`flattenLegs`,
 * `walkDurationMs`, `pushResidue`, `pathLength`, `walkAt`) import clean with
 * no DOM at all.
 *
 * NO NEW RENDER SURFACES (trap 36's family): rects and ONE polyline, animated
 * with transform and opacity. No filters, no blend modes, no second canvas.
 * The styling lives in styles.css, not in a JS template - so there is no CSS
 * comment in this file for a backtick to break (trap 37).
 *
 * THE COSMETICS (Counter Stock, 2026-08-26) obey all five laws and the render
 * rule above without exception, which is why they are three small things and
 * not one big one:
 *
 *   away_colors    - CSS ONLY. One attribute on the group (`data-cos-away`),
 *                    styles.css does the rest. No JS at all past the attribute.
 *   sparkler_steps - 3-unit rects out of TWO FIXED pools (sparkPlan), spawned
 *                    on the beat the walk already counts - never on a timer of
 *                    its own and never per frame - and faded by a CSS keyframe
 *                    on transform + opacity. Both pools are allocated once and
 *                    recycled; nothing here can grow the node count of a walk.
 *                    The TRAIL pool rides the footfall and its half-step; the
 *                    BEAT pool holds the departure puff and the arrival ring,
 *                    and it is a second pool so that a burst can never evict a
 *                    trail spark still in its fade. Plus `data-cos-spark`,
 *                    which is CSS ONLY: a dotted gold grammar on the wake and
 *                    on the night's residue. (Re-dialled 2026-08-30 - see the
 *                    dials.)
 *   ghost_walk     - `data-cos-ghost` (CSS opacity) plus at most ECHO_COUNT
 *                    afterimages, which are the SAME sprite builder standing at
 *                    positions this file already knows how to compute (walkAt
 *                    at t - lag). No trail buffer, no filter, no blur: an
 *                    afterimage is a body a beat behind you, dimmed by CSS.
 *
 * REDUCED MOTION deletes every animated half of all three. LITE does NOT, and
 * that is the 2026-08-30 correction: it thins the sparkler to its old density
 * (counterfx.js's law - lite keeps the move and drops the particle count)
 * rather than deleting it, because `performanceMode` arrives from C# off an
 * AUTO tier that a busy session trips on its own and `init` is built once. The
 * STATIC halves (a fill swap, a base opacity, a dash pattern) are not animation
 * and survive both, exactly as the rest of the page treats a colour. Ownership
 * is a plain bag of booleans handed in at construction - this file never reads
 * a store, a wallet or an inventory, and would not know what a sku was.
 * ==========================================================================*/

import { CAMPUS_GATE, walkLegs, gateLegs, stopAnchor, spriteTurn } from './campus.js';
import { onDeviceChange } from '../core/device.js';
import { buildStudentSprite, easeInOutSine } from './ghosts.js';

const SVGNS = 'http://www.w3.org/2000/svg';

/* ----------------------------------------------------------------------------
 * DIALS. Everything a play-test would want to move lives here.
 * -------------------------------------------------------------------------- */
/** The floor and the ceiling on one walk, in ms (ORIENTATION §2.3).
 *  OWNER ORDER 2026-08-24 ("the walking animation when we select a room is too
 *  fast - double the time it takes"): floor 350 -> 700, cap 900 -> 1800, and
 *  WALK_SPEED halved below. All three move TOGETHER or the doubling is not
 *  uniform - the band's unclamped span is WALK_MS x WALK_SPEED, so halving the
 *  speed while doubling the two clamps leaves exactly the same distances inside
 *  the band and makes EVERY walk on the map take exactly twice as long. */
export const WALK_MS_MIN = 700;
export const WALK_MS_CAP = 1800;
/**
 * Walking speed in viewBox units per second. It was 1400 (fast on purpose); the
 * owner's 2026-08-24 ruling halved it to 700 with the two clamps doubled, so
 * the shape of the band is untouched and only the pace changed. The plan is
 * 1440 units across and a corridor crossing is ~1000 of them. At 700 u/s the
 * shortest hop on the map (~200 units, one office counter to the other) still
 * lands on the floor (now 700ms) and the longest (the gate to the west wing,
 * ~1400) still lands on the cap (now 1800ms), so the whole map fits INSIDE the
 * band instead of pinning it flat. This is now an unhurried crossing rather
 * than a dash - it is still not a ghost's amble, and nothing waits on a timer
 * of its own: every consumer waits on `onDone`, which fires off this number.
 */
export const WALK_SPEED = 700;
/** How many residue polylines the map remembers, FIFO (ORIENTATION §2.4). */
export const RESIDUE_MAX = 12;
/** The active trace's fade into residue, on arrival. */
export const TRACE_FADE_MS = 600;
/** The miniature, against a ghost's 1.0. */
export const YOU_SCALE = 1.15;
/** The bob, in viewBox units, while moving. One unit is about one pixel. */
const BOB_UNITS = 1;
/** One bob cycle, ms. */
const BOB_MS = 220;
/** The target key that means "walk out of the building". */
export const GATE_KEY = 'gate';

/* ---- COSMETIC DIALS (Counter Stock). Budget caps, and they are caps and not
 * targets: every one of them is the number of NODES the effect may ever own. */
/* SPARKLER STEPS, RE-DIALLED 2026-08-30. OWNER ORDER: "the upgrade that gives a
 * sparkly trail while we move does actually nothing or changes very little, we
 * need more FX". It was never broken - it rendered, and it was simply an order
 * of magnitude too small and too sparse to find. A 700-1800ms walk fired ONE
 * 1x1 rect per 220ms footfall, so 3-8 marks for a whole crossing, each of them
 * about 1.3 CSS px on a 1920 window - UNDER a free gold wake at stroke-width
 * 2.5, next to a counter spark of 5px and an engine spark of 8px. The 250
 * tickets bought the least visible mark on the plan.
 *
 * WHAT DID NOT MOVE: rects only, out of pools that are still FIXED and still
 * minted once; struck off the footfall this file already counts and never off a
 * clock of its own; faded by transform and opacity with no filter, no blend and
 * no second surface; and not one live roll - every scatter below is a pure
 * function of the emission index (see sparkScatter / sparkHue). */

/** Live TRAIL sparks at once. The pool is minted once and recycled round robin. */
export const SPARK_MAX = 28;
/** The trail pool on a lite machine: today's density, at the new size. */
export const SPARK_MAX_LITE = 10;
/**
 * How long one spark lives, ms.
 *
 * THIS NUMBER AND `campus-sparkfade`'s DURATION IN styles.css ARE THE SAME
 * NUMBER, and they move together or not at all: a fade longer than the pool's
 * turnaround is a rect yanked back to the walker's feet half lit. The
 * turnaround is `pool / per-beat x beat`, and both profiles clear it:
 *   full   28 / 3 x 110ms = 1027ms  >  900ms
 *   lite   10 / 1 x 220ms = 2200ms  >  900ms
 * The BEAT pool below is deliberately NOT in that sum - it is a second fixed
 * pool precisely so a 10-spark arrival cannot evict a trail still fading.
 */
export const SPARK_MS = 900;
/**
 * THE BEAT POOL, and it is its own pool for arithmetic rather than taste: the
 * departure puff and the arrival ring are BURSTS, and a burst drawn off the
 * trail pool would recycle a third of that pool in a single turn and cut every
 * spark still in the air. Fifteen nodes, and the two beats own DISJOINT index
 * ranges inside them - puff at the head, arrival at the tail - so within one
 * walk nothing in here is recycled at all. See burstRing().
 */
export const BURST_MAX = 15;
/** ... and on a lite machine. Same disjoint layout, fewer nodes. */
export const BURST_MAX_LITE = 8;

/**
 * THE SPARK BUDGET: one pure function, both profiles, every number a spark can
 * cost. Node-safe and testable, and nothing downstream reads a flag twice.
 *
 * `beatMs` is the EMISSION cadence and it is not the footfall. At full fat it
 * is HALF a footfall, so the trail is laid every 110ms rather than every 220
 * and the walker drags a line of grit instead of a dotted line of single
 * pixels. Under lite it IS the footfall: one strike per step, which is exactly
 * what the whole effect used to be, and which is the house doctrine for lite
 * (counterfx.js: "lite keeps the move and drops the particle count" - never
 * "lite deletes the move").
 *
 * @param {boolean=} lite
 * @returns {{pool:number, per:number, beatMs:number, burst:number,
 *            puff:number, arrive:number, skip:number}}
 */
export function sparkPlan(lite) {
  return lite === true
    ? {
      pool: SPARK_MAX_LITE, per: 1, beatMs: BOB_MS,
      burst: BURST_MAX_LITE, puff: 3, arrive: 5, skip: 3,
    }
    : {
      pool: SPARK_MAX, per: 3, beatMs: BOB_MS / 2,
      burst: BURST_MAX, puff: 5, arrive: 10, skip: 5,
    };
}

/**
 * WHERE ONE SPARK GOES AS IT DIES, as CSS custom properties the keyframe reads.
 * PURE, and pure is the point: nothing on this page rolls live where two
 * players could compare it (ArcademyEconomy's law), and a walk is the map
 * everybody sees. `n` is the emission ordinal, so the scatter is reproducible
 * in a suite and identical on every machine on every night.
 *
 * dx / dy are viewBox units (a CSS transform on an SVG element reads `px` as
 * user units), r is degrees, s is the per-node size multiplier that stops 28
 * identical squares reading as a stencil.
 *
 * @param {number} n
 * @returns {{dx:number, dy:number, r:number, s:number}}
 */
export function sparkScatter(n) {
  const i = Math.max(0, Math.floor(Number(n) || 0));
  return {
    dx: ((i % 5) - 2) * 2.5,
    dy: -6 - ((i % 3) * 2),
    r: (i % 4) * 45,
    s: [0.7, 1, 1.4][i % 3],
  };
}

/**
 * WHAT COLOUR ONE SPARK IS, as a class name - so the palette is CSS's and this
 * file never names a colour (styles.css's law: NOT ONE HEX). Gold and pink
 * alternate and every fifth is lavender, which is counterfx.css's `.is-gold`
 * alternation with one more token in the rotation.
 *
 * @param {number} n
 * @returns {string}
 */
export function sparkHue(n) {
  const i = Math.max(0, Math.floor(Number(n) || 0));
  if (i % 5 === 4) return 'is-lav';
  return (i % 2) ? 'is-pink' : 'is-gold';
}
/** Afterimages behind the ghost walk. Two, and the second is nearly gone. */
export const ECHO_COUNT = 2;
/** How far behind the body each afterimage stands, ms per step. */
export const ECHO_LAG_MS = 90;

/** The document events that snap a walk to its end (attract Law II's list). */
const SNAP_EVENTS = ['pointerdown', 'pointerup', 'wheel', 'touchstart', 'keydown'];

/* ONE AUDIO DOOR (W3 P0-30, shell/ceremonies.js's pattern verbatim). The walk
 * had no voice at all: you crossed the whole campus in silence and arrived at a
 * class without ever having been anywhere. shell/audio.js owns the only audio
 * node on the page (trap 18), so this is a REQUEST on `document` - guarded like
 * every other touch in this file, because the pure half must import into node
 * with no DOM under it. A dropped cue is not an error. */
function sfx(name, level, extra) {
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

/* ============================================================================
 * THE PURE HALF - no DOM, node-testable, and the only place the numbers live.
 * ==========================================================================*/

/** Total length of a point list, in viewBox units. Pure. */
export function pathLength(pts) {
  if (!pts || pts.length < 2) return 0;
  let n = 0;
  for (let i = 1; i < pts.length; i++) {
    const dx = pts[i][0] - pts[i - 1][0];
    const dy = pts[i][1] - pts[i - 1][1];
    n += Math.sqrt(dx * dx + dy * dy);
  }
  return n;
}

/**
 * THE WHOLE PATH as one point list, `from` included, in campus.js's grammar.
 *
 * `target` is a room key (a class room or one of the two office counters -
 * campus.js's `walkLegs` knows both), the string `'gate'` for the way out, or
 * a literal `[x, y]` for a caller that already holds a point. A target that is
 * already underfoot, or a key the campus has never heard of, answers an EMPTY
 * list - which the walker reads as "nothing to walk", never as an error. Pure.
 *
 * @param {Array<number>} from
 * @param {string|Array<number>} target
 * @returns {Array<Array<number>>}
 */
export function flattenLegs(from, target) {
  if (!from || from.length < 2) return [];
  const start = [Number(from[0]), Number(from[1])];
  if (!Number.isFinite(start[0]) || !Number.isFinite(start[1])) return [];
  let legs;
  if (Array.isArray(target)) {
    legs = target.length >= 2 ? [[Number(target[0]), Number(target[1])]] : [];
  } else if (target === GATE_KEY) {
    legs = gateLegs(start);
  } else {
    legs = walkLegs(start, target);
  }
  if (!legs || !legs.length) return [];
  const out = [start];
  for (const p of legs) {
    if (!p || p.length < 2 || !Number.isFinite(p[0]) || !Number.isFinite(p[1])) continue;
    const last = out[out.length - 1];
    if (Math.abs(last[0] - p[0]) < 0.01 && Math.abs(last[1] - p[1]) < 0.01) continue;
    out.push([p[0], p[1]]);
  }
  return out.length >= 2 ? out : [];
}

/**
 * How long a path of `len` units takes. `override` (Orientation Day's watchable
 * ~2200ms walk) wins outright - it is the ONE walk that ignores the cap. Pure.
 */
export function walkDurationMs(len, override) {
  const o = Number(override);
  if (Number.isFinite(o) && o > 0) return Math.round(o);
  const l = Number(len);
  if (!Number.isFinite(l) || l <= 0) return WALK_MS_MIN;
  return Math.round(Math.max(WALK_MS_MIN, Math.min(WALK_MS_CAP, (l / WALK_SPEED) * 1000)));
}

/**
 * Append one finished trace to the residue, FIFO at RESIDUE_MAX. Returns a NEW
 * list (the caller's is never mutated - shell.js hands the same array to a
 * fresh walker on every campus mount). A malformed entry is dropped. Pure.
 */
export function pushResidue(list, entry) {
  const out = Array.isArray(list) ? list.slice() : [];
  if (!entry || !Array.isArray(entry.pts) || entry.pts.length < 2) return out;
  out.push({
    to: entry.to == null ? null : String(entry.to),
    pts: entry.pts.map((p) => [p[0], p[1]]),
  });
  while (out.length > RESIDUE_MAX) out.shift();
  return out;
}

/** A point list as an SVG `points` attribute. One decimal, like campus.js. */
export function pointsAttr(pts) {
  if (!pts || !pts.length) return '';
  const r = (n) => Math.round(n * 10) / 10;
  return pts.map((p) => r(p[0]) + ',' + r(p[1])).join(' ');
}

/**
 * Where a walk along `legs` is at `t` ms of `total`, easeInOutSine PER LEG (the
 * ghosts' easing, and the only one in this file - a corner is a step, not a
 * skid). Answers the position, the leg index and the facing. Pure.
 */
export function walkAt(legs, t, total) {
  const n = (legs && legs.length) || 0;
  if (n < 2) return { x: 0, y: 0, leg: 0, facing: 1, done: true };
  const lens = [];
  let whole = 0;
  for (let i = 1; i < n; i++) {
    const dx = legs[i][0] - legs[i - 1][0];
    const dy = legs[i][1] - legs[i - 1][1];
    const l = Math.sqrt(dx * dx + dy * dy);
    lens.push(l);
    whole += l;
  }
  const end = legs[n - 1];
  if (!(whole > 0)) return { x: end[0], y: end[1], leg: n - 2, facing: 1, done: true };
  const ms = Math.max(1, Number(total) || 1);
  const clamped = Math.max(0, Math.min(ms, Number(t) || 0));
  let acc = 0;
  for (let i = 0; i < lens.length; i++) {
    const legMs = ms * (lens[i] / whole);
    if (clamped <= acc + legMs || i === lens.length - 1) {
      const p = legMs > 0 ? easeInOutSine((clamped - acc) / legMs) : 1;
      const a = legs[i];
      const b = legs[i + 1];
      const dx = b[0] - a[0];
      return {
        x: a[0] + dx * p,
        y: a[1] + (b[1] - a[1]) * p,
        leg: i,
        facing: Math.abs(dx) < 0.5 ? 0 : (dx > 0 ? 1 : -1),
        done: clamped >= ms,
      };
    }
    acc += legMs;
  }
  return { x: end[0], y: end[1], leg: n - 2, facing: 1, done: true };
}

/**
 * THE COSMETIC BAG, shaped. Three booleans and nothing else: this module is
 * handed what the player OWNS, never a sku, never an inventory row and never a
 * function that reaches a store. Anything missing, misspelled or merely truthy
 * is OFF - a cosmetic that switches itself on because the shell handed down a
 * `1` instead of a `true` is a cosmetic nobody bought. Pure.
 *
 * A function is accepted as well as an object, so the shell may hand down a
 * live getter and a purchase made mid-session lights on the next campus mount
 * without a reload (contract §4). It is read ONCE per walker.
 *
 * @param {Object|Function=} bag {awayColors, sparklerSteps, ghostWalk}
 * @returns {{awayColors:boolean, sparklerSteps:boolean, ghostWalk:boolean}}
 */
export function normaliseCosmetics(bag) {
  let b = bag;
  if (typeof b === 'function') {
    try { b = b(); } catch (e) { b = null; }
  }
  const o = (b && typeof b === 'object') ? b : {};
  return {
    awayColors: o.awayColors === true,
    sparklerSteps: o.sparklerSteps === true,
    ghostWalk: o.ghostWalk === true,
  };
}

/** The plaque a room key stands at - the miniature's resting place. Pure. */
export function roomStop(key) {
  try {
    const a = stopAnchor(key);
    return (a && a.length >= 2 && Number.isFinite(a[0])) ? [a[0], a[1]] : null;
  } catch (e) { return null; }
}

/* ============================================================================
 * THE LAYER
 * ==========================================================================*/

function svgNode(tag, attrs, cls) {
  let n = null;
  try {
    if (typeof document === 'undefined' || !document) return null;
    n = document.createElementNS
      ? document.createElementNS(SVGNS, tag)
      : document.createElement(tag);
  } catch (e) { return null; }
  if (!n) return null;
  try {
    if (cls) n.setAttribute('class', cls);
    if (attrs) for (const k of Object.keys(attrs)) n.setAttribute(k, String(attrs[k]));
  } catch (e) { /* noop */ }
  return n;
}

function setAttr(node, k, v) {
  if (!node) return;
  try { node.setAttribute(k, String(v)); } catch (e) { /* noop */ }
}

function dropAttr(node, k) {
  if (!node) return;
  try { if (typeof node.removeAttribute === 'function') node.removeAttribute(k); }
  catch (e) { /* noop */ }
}

/* One CSS custom property, guarded like every other DOM touch in this file so
 * the pure halves still import into node. Written ONCE PER STRIKE and never per
 * frame - the keyframe reads them, so the scatter costs no JS while it plays
 * (counterfx.js's setVar, same shape). */
function setVar(node, k, v) {
  if (!node) return;
  try {
    if (node.style && typeof node.style.setProperty === 'function') {
      node.style.setProperty(k, String(v));
    }
  } catch (e) { /* noop */ }
}

/**
 * @param {Object} o
 * @param {Element=} o.mount        campus.walkMount - the group above the ghosts
 * @param {boolean=} o.reducedMotion  no animation: the trace appears whole and
 *   the miniature snaps to the door (ORIENTATION §2.3)
 * @param {string=} o.spriteId      the seed the body is dealt from
 * @param {Object|Function=} o.cosmetics  what the player OWNS off the Prize
 *   Counter: {awayColors, sparklerSteps, ghostWalk}. See normaliseCosmetics -
 *   absent is the whole school's ordinary miniature, which is the default.
 * @param {boolean=} o.lite         performance mode. It gates the COSMETICS and
 *   nothing else (see the note beside it below): a lite machine keeps its walk,
 *   keeps a thinner sparkler (sparkPlan) and loses only the afterimages.
 * @param {Function=} o.log
 * @param {Object=} o.clock         test seam {now(), raf(fn), caf(id)}
 * @returns {{mountAt, at, walkTo, skip, setResidue, residue, walking, destroy}}
 */
export function createWalker(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const mount = opts.mount || null;
  const spriteId = String(opts.spriteId == null ? 'self' : opts.spriteId);

  const nowFn = () => {
    try {
      if (typeof performance !== 'undefined' && performance
        && typeof performance.now === 'function') return performance.now();
    } catch (e) { /* noop */ }
    return Date.now();
  };
  const clock = opts.clock || {
    now: nowFn,
    raf: (fn) => {
      try {
        if (typeof requestAnimationFrame === 'function') return requestAnimationFrame(fn);
      } catch (e) { /* noop */ }
      try {
        if (typeof setTimeout === 'function') return setTimeout(() => fn(nowFn()), 16);
      } catch (e) { /* noop */ }
      return 0;
    },
    caf: (id) => {
      if (!id) return;
      try { if (typeof cancelAnimationFrame === 'function') cancelAnimationFrame(id); } catch (e) { /* noop */ }
      try { if (typeof clearTimeout === 'function') clearTimeout(id); } catch (e) { /* noop */ }
    },
  };
  /** Reduced motion (and a lit-down machine) take the snap path. */
  const still = !!(opts.reducedMotion || opts.lowPerf);
  /** What the player bought (Counter Stock). Read once, per walker. */
  const cos = normaliseCosmetics(opts.cosmetics);
  /**
   * MAY THE COSMETICS MOVE? `still` above is the reduced-motion rung and it
   * already stops the walk itself animating; this one is narrower ON PURPOSE
   * and is read by NOTHING but the two animated cosmetics, so a performance-mode
   * player still crosses the campus exactly the way they always did. Never fold
   * `opts.lite` into `still` - that would be a silent regression of the whole
   * walk for a feature that is glitter.
   */
  const cosMoves = !still && opts.lite !== true;
  /**
   * THE SPARKLER'S OWN GATE, and it is deliberately one rung shorter than
   * `cosMoves` above (2026-08-30).
   *
   * `cosMoves` DELETED the sparkler under lite, and lite is not the rare, opted
   * -in state it reads as: `ArcademyHostService` sends `performanceMode` as
   * `PerformanceProfile.CurrentTier != Quality`, that tier escalates off
   * `AutoPerformanceMode` (which DEFAULTS TRUE) the moment flash windows plus
   * ambient bubbles reach 8, and `init` is built ONCE per Arcademy open. So a
   * player who opened the school during any busy session silently lost the
   * thing they bought for the whole session, with no setting they could see and
   * no way to get it back but a relaunch. That is the likeliest reading of
   * "does actually nothing".
   *
   * The house answer is counterfx.js's, not a special case for this file: lite
   * keeps the move and drops the particle count (engine/ceremonies.js halves
   * rather than skips). So a lite machine mints the SMALL pools out of
   * `sparkPlan(true)` - today's one-per-footfall density, at the new size - and
   * a busy session costs the player a little glitter instead of all of it.
   *
   * REDUCED MOTION IS UNTOUCHED and stays inside `still`: that rung deletes the
   * pool outright and it is correct.
   */
  const cosSparks = !still;
  /** Every number this walker's sparks may cost, resolved once. */
  const plan = sparkPlan(opts.lite === true);
  /**
   * IS THIS A BROWSER AT ALL? ghosts.js's law, and it earns its keep twice
   * here. With no `requestAnimationFrame` and no injected clock (node, the DOM
   * double, a headless run) there is nothing to animate INTO - and, more
   * importantly, this module is a funnel that a class launch passes through:
   * where there is no paint it must be byte-for-byte TRANSPARENT, handing the
   * launch straight back on the same turn the door was clicked, exactly as the
   * shell did before The Walk existed. A missing frame clock is not a slow
   * machine, it is not a browser (trap 36's corollary).
   */
  const hasClock = !!opts.clock || (typeof requestAnimationFrame === 'function');

  let destroyed = false;
  let pos = [CAMPUS_GATE[0], CAMPUS_GATE[1]];
  let facing = 1;
  let residueList = [];
  /** the walk currently in the air, or null */
  let run = null;
  let bound = false;
  let armId = 0;
  const fadeTimers = [];

  /* ------------------------------------------------------------- the nodes */
  const residueLayer = svgNode('g', null, 'campus-walkresidue');
  const trace = svgNode('polyline', { points: '', fill: 'none' }, 'campus-trace');
  const you = svgNode('g', { transform: 'translate(' + pos[0] + ',' + pos[1] + ')' + spriteTurn() }, 'gh-you');
  const youRing = svgNode('ellipse', { cx: 0, cy: 0.6, rx: 7, ry: 2.6 }, 'gh-youring');
  const youBody = svgNode('g',
    { transform: 'scale(' + YOU_SCALE + ',' + YOU_SCALE + ')' }, 'gh-youbody');
  let sprite = null;
  try { sprite = buildStudentSprite('self|' + spriteId); }
  catch (e) { say('walk: sprite failed (' + ((e && e.message) || e) + ')'); }
  if (youBody && sprite) youBody.appendChild(sprite);
  if (you) {
    if (youRing) you.appendChild(youRing);
    if (youBody) you.appendChild(youBody);
  }

  /* ---------------------------------------------------- the cosmetic layers
   * MINTED ONLY FOR A PLAYER WHO BOUGHT THE THING. An ordinary miniature's node
   * count is byte for byte what it was before this wave, which is the whole of
   * why a restock may touch this file at all: the school that owns nothing pays
   * nothing, and the pools below are FIXED so the school that owns everything
   * pays a constant. */
  const wantsSparks = cos.sparklerSteps && cosSparks;
  const wantsEchoes = cos.ghostWalk && cosMoves;
  const sparkLayer = wantsSparks ? svgNode('g', null, 'campus-sparks') : null;
  const echoLayer = wantsEchoes ? svgNode('g', null, 'campus-walkechoes') : null;
  /** The TRAIL pool, allocated once and recycled round robin off the beat. */
  const sparks = [];
  let sparkAt = 0;
  /** The BEAT pool: the departure puff and the arrival ring, fixed indices. */
  const bursts = [];
  /** The afterimages, nearest first. Same sprite, same seed, same body. */
  const echoes = [];

  /* THREE UNITS, not one. The old rect was a single viewBox unit - about 1.3
   * CSS px on a 1920 window - and `shape-rendering:crispEdges` (now off in
   * styles.css) then quantised what was left of it away. Three units with the
   * per-node `--sp-s` of 0.7 / 1.0 / 1.4 lands the family between 2 and 4.2
   * units, which is the counter's own 5px spark read at plan scale. */
  function mintSpark(layer, into) {
    const g = svgNode('g', { transform: 'translate(0,0)' }, 'campus-spark');
    /* `opacity` starts at 0 as a presentation ATTRIBUTE, which the keyframe
     * outranks the moment a spark is struck and which is what the rect falls
     * back to when it is not. */
    const r = svgNode('rect',
      { x: -1.5, y: -1.5, width: 3, height: 3, opacity: 0 }, 'campus-spark-i');
    if (!g || !r) return;
    g.appendChild(r);
    layer.appendChild(g);
    into.push({ g, r, flip: false });
  }

  if (sparkLayer) {
    for (let i = 0; i < plan.pool; i += 1) mintSpark(sparkLayer, sparks);
    for (let i = 0; i < plan.burst; i += 1) mintSpark(sparkLayer, bursts);
  }

  if (echoLayer) {
    for (let i = 0; i < ECHO_COUNT; i += 1) {
      const g = svgNode('g',
        { transform: 'translate(' + pos[0] + ',' + pos[1] + ')' + spriteTurn() },
        /* `gh-you` as well, so an afterimage wears YOUR palette (and your away
         * colours) rather than a stranger's - one sprite, one wardrobe. The
         * drop-shadow that class carries is cancelled in styles.css: an
         * afterimage is never filtered (this file's render rule). */
        'gh-you gh-echo gh-echo' + (i + 1));
      const b = svgNode('g',
        { transform: 'scale(' + YOU_SCALE + ',' + YOU_SCALE + ')' }, 'gh-youbody');
      let s = null;
      try { s = buildStudentSprite('self|' + spriteId); }
      catch (e) { s = null; }
      if (!g || !b || !s) continue;
      b.appendChild(s);
      g.appendChild(b);
      echoLayer.appendChild(g);
      echoes.push({ g, body: b });
    }
  }

  /** The owned-state attributes. CSS does every visible thing they cause. */
  function paintCosmetics() {
    const marks = [[you, 'data-cos-away', cos.awayColors], [you, 'data-cos-ghost', cos.ghostWalk]];
    for (const [node, key, on] of marks) {
      if (on) setAttr(node, key, '1'); else dropAttr(node, key);
    }
    /* An afterimage is you, so it wears your kit too. */
    for (const e of echoes) {
      if (cos.awayColors) setAttr(e.g, 'data-cos-away', '1'); else dropAttr(e.g, 'data-cos-away');
    }
    /* THE RESIDUE (2026-08-30). The wake and the night's old wakes go over to a
     * dotted gold grammar when the sparkler is owned - the grit stays on the
     * floor after the sparks have gone out, which is what makes the purchase
     * legible on a plan you are only LOOKING at.
     *
     * THIS ONE IS NOT GATED ON MOTION, and that is the away_colors precedent
     * followed exactly (styles.css's note beside the reduced-motion belts): a
     * dash pattern and a fill are COLOUR, not animation, so they survive
     * reduced motion and lite the way the away kit and the see-through body
     * already do. The march that carries them was already switched off under
     * `html.arc-reduced .campus-trace`, and it stays off.
     *
     * TWO NODES, because `renderResidue` re-mints its polylines on every call
     * and would drop an attribute written to them: the flag lives on the LAYER
     * and CSS descends from there. Neither is a new element. */
    const spark = cos.sparklerSteps;
    for (const node of [trace, residueLayer]) {
      if (spark) setAttr(node, 'data-cos-spark', '1'); else dropAttr(node, 'data-cos-spark');
    }
  }
  paintCosmetics();

  if (mount) {
    try {
      if (residueLayer) mount.appendChild(residueLayer);
      if (trace) mount.appendChild(trace);
      /* Under the miniature, over the wake: an afterimage is behind you and a
       * spark is on the floor you left. */
      if (echoLayer) mount.appendChild(echoLayer);
      if (sparkLayer) mount.appendChild(sparkLayer);
      if (you) mount.appendChild(you);
    } catch (e) { say('walk: mount refused (' + ((e && e.message) || e) + ')'); }
  }

  /* ----------------------------------------------------------- the drawing */

  function place() {
    if (!you) return;
    /* THE UPRIGHT CAMPUS turns the plan a quarter turn on a phone held
     * portrait; the walker takes the same turn back or it crosses the quad
     * lying on its side. One appended term, at every site that writes a
     * position - see spriteTurn() in shell/campus.js. */
    setAttr(you, 'transform', 'translate('
      + (Math.round(pos[0] * 10) / 10) + ',' + (Math.round(pos[1] * 10) / 10) + ')' + spriteTurn());
  }

  function draw(bobMs) {
    place();
    if (!youBody) return;
    const sx = facing < 0 ? -YOU_SCALE : YOU_SCALE;
    let bob = 0;
    if (bobMs != null && !still) {
      bob = -Math.abs(Math.sin((bobMs / BOB_MS) * Math.PI)) * BOB_UNITS;
    }
    setAttr(youBody, 'transform',
      'scale(' + sx + ',' + YOU_SCALE + ') translate(0,' + (Math.round(bob * 100) / 100) + ')');
  }

  function paintTrace(pts) { setAttr(trace, 'points', pointsAttr(pts)); }

  /* -------------------------------------------------------- the cosmetics */

  /**
   * ONE SPARK, off the pool, at the feet. Struck from the FOOTFALL the walk
   * already counts (frame() below) and never from a timer of its own: the
   * sparkler is the step, so a machine that drops frames drops sparks with them
   * rather than dumping a backlog on the floor.
   *
   * THE SCATTER IS THE STEP NUMBER, NOT A ROLL. Nothing on this page rolls live
   * where two players could compare it, and a walk is the map everybody sees;
   * `n` is the footfall index, which gives left / centre / right for free and
   * makes the whole effect reproducible in a suite.
   */
  function fireSpark(pool, slot, x, y, n) {
    if (!pool.length) return;
    const s = pool[((slot % pool.length) + pool.length) % pool.length];
    if (!s) return;
    const sc = sparkScatter(n);
    setAttr(s.g, 'transform', 'translate('
      + (Math.round(x * 10) / 10) + ',' + (Math.round(y * 10) / 10) + ')');
    /* WHERE IT DIES, handed to the keyframe as four custom properties. Written
     * once, at the strike; the animation then plays with no JS behind it. */
    setVar(s.r, '--sp-dx', sc.dx + 'px');
    setVar(s.r, '--sp-dy', sc.dy + 'px');
    setVar(s.r, '--sp-r', sc.r + 'deg');
    setVar(s.r, '--sp-s', sc.s);
    /* TWO CLASSES, ALTERNATING, and they carry the same keyframe. Re-adding the
     * class a spark already wears does not restart a finished animation without
     * a forced reflow (which is a layout read per step); flipping between two
     * identical names restarts it by definition and reads nothing. The hue
     * class rides the same write, so the colour still costs zero extra work. */
    s.flip = !s.flip;
    setAttr(s.r, 'class',
      'campus-spark-i ' + sparkHue(n) + ' ' + (s.flip ? 'is-a' : 'is-b'));
  }

  /**
   * THE TRAIL, one beat's worth. Struck from the beat the walk already counts
   * (frame() below) and never from a timer of its own: the sparkler IS the
   * step, so a machine that drops frames drops sparks with them rather than
   * dumping a backlog on the floor.
   *
   * THE SCATTER IS THE EMISSION NUMBER, NOT A ROLL - `sparkAt` doubles as the
   * round-robin slot and as sparkScatter's / sparkHue's index, which is what
   * keeps the whole effect reproducible in a suite and identical on every
   * machine. The spawn jitter is the same pure `((n % 3) - 1)` it always was.
   */
  function strikeTrail(x, y, count) {
    if (!sparks.length) return;
    for (let i = 0; i < count; i += 1) {
      const n = sparkAt;
      sparkAt += 1;
      fireSpark(sparks, n, x + (((n % 3) - 1) * 1.1), y, n);
    }
  }

  /**
   * THE TWO BEATS: a puff under the feet as you leave, a ring at the door when
   * you land. Off the BEAT pool, at FIXED indices, so a burst can never evict a
   * trail spark that is still fading.
   *
   * `base` is where in that pool the beat starts, and the two callers pick
   * ranges that do not overlap: the puff takes `[0, puff)` and an arrival takes
   * the tail `[burst - count, burst)`. `burst - count` is >= `puff` in both
   * profiles (15-10=5 >= 5, and 8-5=3 >= 3), so inside one walk nothing here is
   * recycled at all. Across two walks the only pair that can touch is puff to
   * puff, which needs a whole crossing plus a click to have happened in under
   * SPARK_MS, and what it would clip is five dots at a door already behind you.
   *
   * The ring is `2 pi i / count` - the same shape counterfx's sparkBurst draws,
   * and just as seeded: no roll, no live randomness anywhere in it.
   */
  function burstRing(x, y, count, base, reach0) {
    if (!bursts.length || !(count > 0)) return;
    for (let i = 0; i < count; i += 1) {
      const ang = (Math.PI * 2 * i) / count;
      const reach = reach0 + ((i % 4) * 2);
      const n = base + i;
      fireSpark(bursts, n, x + Math.cos(ang) * reach, y + Math.sin(ang) * reach, n);
    }
  }

  /**
   * The afterimages, at t minus their lag. `walkAt` is pure and clamps, so this
   * needs no history buffer at all: an echo is simply where the walk WAS, asked
   * for the same way the body asks where it is. Facing is the body's - a
   * miniature that turned a corner turns its whole wake with it.
   */
  function drawEchoes(r, t) {
    if (!echoes.length || !r) return;
    const sx = facing < 0 ? -YOU_SCALE : YOU_SCALE;
    for (let i = 0; i < echoes.length; i += 1) {
      const at = walkAt(r.legs, t - ECHO_LAG_MS * (i + 1), r.total);
      setAttr(echoes[i].g, 'transform', 'translate('
        + (Math.round(at.x * 10) / 10) + ',' + (Math.round(at.y * 10) / 10) + ')' + spriteTurn());
      setAttr(echoes[i].body, 'transform', 'scale(' + sx + ',' + YOU_SCALE + ')');
    }
  }

  /** The wake is only there while there is a walk to be behind. */
  function showEchoes(on) {
    if (!echoLayer) return;
    try {
      if (on) echoLayer.setAttribute('class', 'campus-walkechoes is-walking');
      else echoLayer.setAttribute('class', 'campus-walkechoes');
    } catch (e) { /* noop */ }
  }

  function renderResidue() {
    if (!residueLayer) return;
    try { residueLayer.textContent = ''; } catch (e) { /* noop */ }
    for (const entry of residueList) {
      const line = svgNode('polyline',
        { points: pointsAttr(entry.pts), fill: 'none' }, 'campus-trace residue');
      if (line) { try { residueLayer.appendChild(line); } catch (e) { /* noop */ } }
    }
  }

  /* ---------------------------------------------------------- the listener
   * ARMED ON A TIMEOUT, NOT INLINE. `walkTo` is called from inside the click
   * that started it (the class card's GO button), and that click is still
   * bubbling: a listener added on `document` right here would catch its own
   * cause and snap the walk before the first frame ever ran. One turn of the
   * event loop later the dispatch is over and every event we see is a NEW one.
   * ------------------------------------------------------------------------ */
  function onSnapInput() { skip(); }

  function armSnap() {
    if (bound || armId) return;
    try {
      if (typeof setTimeout !== 'function') return;
      armId = setTimeout(() => {
        armId = 0;
        if (destroyed || !run) return;
        try {
          if (typeof document === 'undefined' || !document
            || typeof document.addEventListener !== 'function') return;
          SNAP_EVENTS.forEach((n) => document.addEventListener(n, onSnapInput, true));
          bound = true;
        } catch (e) { /* noop */ }
      }, 0);
    } catch (e) { /* noop */ }
  }

  function disarmSnap() {
    if (armId) { try { clearTimeout(armId); } catch (e) { /* noop */ } armId = 0; }
    if (!bound) return;
    bound = false;
    try { SNAP_EVENTS.forEach((n) => document.removeEventListener(n, onSnapInput, true)); }
    catch (e) { /* noop */ }
  }

  /* ------------------------------------------------------------ the finish */

  /**
   * THE RESIDUE HAND-OFF. The finished trace fades where it stands and its
   * geometry is banked as DATA in the same beat - so a fade cut short (a screen
   * change, a second walk) still leaves the line on the map. The DOM half is a
   * class swap; the data half is what survives the campus.
   */
  function retire(r) {
    residueList = pushResidue(residueList, { to: r.to, pts: r.legs });
    if (!trace) { renderResidue(); return; }
    if (still || !hasClock || typeof setTimeout !== 'function') {
      paintTrace([]);
      renderResidue();
      return;
    }
    try { trace.setAttribute('class', 'campus-trace fading'); } catch (e) { /* noop */ }
    const id = setTimeout(() => {
      if (destroyed) return;
      try { trace.setAttribute('class', 'campus-trace'); } catch (e) { /* noop */ }
      paintTrace([]);
      renderResidue();
    }, TRACE_FADE_MS);
    fadeTimers.push(id);
  }

  /**
   * ONE EXIT for every walk: the position lands on the destination, the trace
   * is completed and retired to residue, the listener comes off, and `onDone`
   * fires - EXACTLY once, whichever door we came in through.
   */
  function finish(reason) {
    const r = run;
    if (!r) return;
    run = null;
    if (r.rafId) { try { clock.caf(r.rafId); } catch (e) { /* noop */ } r.rafId = 0; }
    disarmSnap();
    const end = r.legs[r.legs.length - 1];
    pos = [end[0], end[1]];
    const last = r.legs[r.legs.length - 2];
    if (last) {
      const dx = end[0] - last[0];
      if (Math.abs(dx) >= 0.5) facing = dx > 0 ? 1 : -1;
    }
    draw(null);
    /* THE WAKE CATCHES UP AND GOES OUT. An afterimage of a body that has
     * stopped is a second student standing in the corridor, so the echoes are
     * landed on the destination and then faded by their own class. */
    drawEchoes(r, r.total);
    showEchoes(false);
    paintTrace(r.legs);
    /* W3 P0-30: ARRIVAL. Two slowing footfalls and the room's air under them,
     * so the walk lands rather than stopping. A SKIP gets one clipped step -
     * the player cut the crossing short and the sound is cut with it - and a
     * walk that was superseded, torn down or never animated says nothing at
     * all, because nobody arrived anywhere. One dispatch: the follow-ups ride
     * the mixer's own timeline, so this file owns no cue timer. */
    /* THE ARRIVAL RING rides the same two branches (2026-08-30), because the
     * sound was already saying the right thing and the picture was saying
     * nothing. A SKIP is the case that mattered: LAW 4 above snaps the whole
     * walk to its end on the first pointer or key press, so a player who clicks
     * a door and then clicks anything else saw the entire sparkler - all three
     * to eight pixels of it - never render. That player is the impatient one,
     * which is to say most of them on most nights. Now the walk lands with a
     * ring at the door whether it was watched or cut short. Off the BEAT pool
     * at its tail indices, so nothing still fading behind us is evicted. */
    if (!still) {
      if (reason === 'skipped') {
        sfx('step', 0.05, { pitch: 0.92 });
        burstRing(pos[0], pos[1], plan.skip, plan.burst - plan.skip, 8);
      } else if (reason === 'arrived') {
        sfx('step', 0.07, {
          pitch: 0.94,
          steps: [
            { atMs: 150, pitch: 0.88 },
            { atMs: 320, name: 'pad', level: 0.08, pitch: 1 },
          ],
        });
        burstRing(pos[0], pos[1], plan.arrive, plan.burst - plan.arrive, 8);
      }
    }
    retire(r);
    if (!r.fired) {
      r.fired = true;
      if (typeof r.onDone === 'function') {
        try { r.onDone(); } catch (e) { say('walk onDone threw: ' + ((e && e.message) || e)); }
      }
    }
    say('walk: ' + r.to + ' (' + reason + ')');
  }

  /* -------------------------------------------------------------- the walk */

  function frame() {
    const r = run;
    if (destroyed || !r) return;
    const t = clock.now() - r.t0;
    if (t >= r.total) { finish('arrived'); return; }
    const at = walkAt(r.legs, t, r.total);
    pos = [at.x, at.y];
    if (at.facing) facing = at.facing;
    draw(t);
    /* W3 P0-30: THE FOOTFALL, ON THE BOB AND NOT ON THE FRAME. This loop runs
     * at the frame clock; the bob runs at BOB_MS. A cue per frame would be a
     * machine gun (trap 116), so it fires on the phase PEAK of the bob - the
     * top of each half-cycle, at BOB_MS/2 + k*BOB_MS - which is one footfall
     * per step of the animation and nothing else. Two feet, so the pitch
     * alternates; quiet, because it is distant lino under a miniature. Reduced
     * motion never bobs, so it never walks either. */
    if (!still) {
      const feet = Math.floor((t + BOB_MS / 2) / BOB_MS);
      if (feet > r.feet) {
        r.feet = feet;
        sfx('step', 0.06, { pitch: (feet % 2) ? 0.96 : 1.04 });
      }
      /* THE SPARKLER RIDES THE BEAT, which is the footfall AND its half-step
       * (plan.beatMs; under lite it is the footfall itself). Still no second
       * clock and still nothing per frame - the beat is read off the same `t`
       * the bob is, and the pool is what caps what a long walk leaves behind.
       * The FOOT sound is untouched above: the cadence of the ear did not
       * change when the cadence of the eye did. */
      if (sparks.length) {
        const beat = Math.floor((t + BOB_MS / 2) / plan.beatMs);
        if (beat > r.beat) {
          r.beat = beat;
          strikeTrail(at.x, at.y, plan.per);
        }
      }
    }
    drawEchoes(r, t);
    paintTrace(r.legs.slice(0, at.leg + 1).concat([[at.x, at.y]]));
    r.rafId = clock.raf(frame);
    /* No frame clock answered mid-walk. Rather than freeze half way across the
     * hall, land it: an unanimated walk is still a walk that arrived. */
    if (!r.rafId) finish('no frame clock');
  }

  function soon(fn) {
    try {
      if (hasClock && typeof setTimeout === 'function') { setTimeout(fn, 0); return; }
    } catch (e) { /* noop */ }
    try { fn(); } catch (e) { /* noop */ }
  }

  /* ------------------------------------------------------------------- API */

  function skip() { if (run) finish('skipped'); }

  function walkTo(target, options) {
    const cfg = options || {};
    const onDone = typeof cfg.onDone === 'function' ? cfg.onDone : null;
    const done = () => {
      if (!onDone) return;
      try { onDone(); } catch (e) { say('walk onDone threw: ' + ((e && e.message) || e)); }
    };
    if (destroyed) { soon(done); return false; }
    /* A walk already in the air is FINISHED first, never abandoned: its own
     * onDone is owed exactly once, and dropping it would lose a class launch. */
    if (run) finish('superseded');

    let legs = [];
    try { legs = flattenLegs(pos, target); } catch (e) { legs = []; }
    /* DECORATION, NEVER A GATE (ORIENTATION §2.3). No mount, no nodes, no legs,
     * an unknown room, a target already underfoot - every one of them still
     * fires onDone, because the thing on the other side of it is a class. */
    if (!mount || !you || legs.length < 2) {
      if (legs.length >= 2) pos = [legs[legs.length - 1][0], legs[legs.length - 1][1]];
      soon(done);
      return false;
    }

    const total = walkDurationMs(pathLength(legs), cfg.durationMs);
    run = {
      legs,
      total,
      t0: clock.now(),
      rafId: 0,
      onDone,
      fired: false,
      /** How many footfalls this walk has already sounded (W3 P0-30). */
      feet: 0,
      /* THE BEAT INDEX AT t=0, so the first cluster lands one whole beat after
       * the departure puff instead of on top of it. Full fat that is 1 (the
       * beat is 110ms, so beat 1 is already behind us at t=0 and the trail
       * starts at t=110); under lite it is 0, and the emission then falls on
       * the footfalls themselves at 110, 330, 550 - exactly where the single
       * spark used to fall. One expression, both profiles. */
      beat: Math.floor((BOB_MS / 2) / plan.beatMs),
      to: Array.isArray(target) ? 'point' : String(target),
    };

    if (!hasClock) {
      /* NOT A BROWSER. Land it on this turn: the trace, the residue and the
       * position are all real, the launch is not deferred by a frame that will
       * never come. See `hasClock` above. */
      finish('no frame clock');
      return false;
    }

    if (still) {
      /* REDUCED MOTION: the whole trace at once, the miniature standing where
       * it ends, and onDone on the next turn - so a caller is never re-entered
       * from inside its own call. */
      paintTrace(legs);
      const r = run;
      soon(() => { if (run === r) finish('reduced motion'); });
      return true;
    }

    draw(0);
    /* THE DEPARTURE PUFF. The trail used to begin on the first footfall, 110ms
     * and a body-length after the door was clicked, so the effect you bought
     * had no opening beat at all - you were already moving before anything lit.
     * Five off the beat pool, under the feet, at t=0. */
    burstRing(legs[0][0], legs[0][1], plan.puff, 0, 4);
    drawEchoes(run, 0);
    showEchoes(true);
    paintTrace([legs[0], legs[0]]);
    armSnap();
    run.rafId = clock.raf(frame);
    if (!run.rafId) { finish('no frame clock'); return false; }
    return true;
  }

  function mountAt(pt) {
    const ok = pt && pt.length >= 2
      && Number.isFinite(Number(pt[0])) && Number.isFinite(Number(pt[1]));
    pos = ok ? [Number(pt[0]), Number(pt[1])] : [CAMPUS_GATE[0], CAMPUS_GATE[1]];
    draw(null);
    return pos.slice();
  }

  function setResidue(list) {
    residueList = [];
    if (Array.isArray(list)) for (const e of list) residueList = pushResidue(residueList, e);
    renderResidue();
    return residueList.slice();
  }

  /* A walker standing still is only ever written once, so a phone turned while
   * it stands there needs the position re-written. draw() does exactly that. */
  const unorient = onDeviceChange(() => { try { draw(null); } catch (e) { /* noop */ } });

  function destroy() {
    if (destroyed) return;
    try { unorient(); } catch (e) { /* noop */ }
    /* A PENDING LAUNCH IS NEVER LOST. The screen is going away, but the class
     * on the other side of this walk was already committed to - finish() pays
     * the onDone before anything else is torn down. `destroyed` is set AFTER,
     * so finish()'s own bookkeeping runs normally. */
    if (run) finish('destroyed');
    destroyed = true;
    disarmSnap();
    fadeTimers.forEach((id) => { try { clearTimeout(id); } catch (e) { /* noop */ } });
    fadeTimers.length = 0;
    try { if (you) you.remove(); } catch (e) { /* noop */ }
    try { if (trace) trace.remove(); } catch (e) { /* noop */ }
    try { if (residueLayer) residueLayer.remove(); } catch (e) { /* noop */ }
    /* The cosmetics leave with the walker - the pool is nodes, and a pool that
     * outlived its layer would be glitter on a campus that is not there. */
    try { if (sparkLayer) sparkLayer.remove(); } catch (e) { /* noop */ }
    try { if (echoLayer) echoLayer.remove(); } catch (e) { /* noop */ }
    sparks.length = 0;
    bursts.length = 0;
    echoes.length = 0;
  }

  draw(null);

  return {
    mountAt,
    /** Where the miniature is standing, in viewBox units. */
    at() { return pos.slice(); },
    walkTo,
    skip,
    setResidue,
    /** The residue as DATA - shell.js keeps this across screens. */
    residue() { return residueList.slice(); },
    /** Test seam: is a walk in the air? */
    walking() { return !!run; },
    destroy,
    /** What the miniature is wearing, and what it actually built for it.
     *  Test seam: `cosmetics` is what was asked for, the counts are what the
     *  budget allowed. Reduced motion is 0 across the board; LITE is 0 echoes
     *  but a THINNED sparkler (2026-08-30), so a suite can tell the two rungs
     *  apart instead of reading one zero for both. */
    cosmetics() {
      return {
        awayColors: cos.awayColors,
        sparklerSteps: cos.sparklerSteps,
        ghostWalk: cos.ghostWalk,
        moves: cosMoves,
        sparkMoves: cosSparks,
        sparks: sparks.length,
        bursts: bursts.length,
        echoes: echoes.length,
      };
    },
    diagnostics() {
      return {
        still, mounted: !!mount, walking: !!run, bound,
        at: pos.slice(), facing, residue: residueList.length,
        cos: {
          away: cos.awayColors, spark: sparks.length,
          burst: bursts.length, echo: echoes.length,
        },
      };
    },
  };
}

export default createWalker;
