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
 * ==========================================================================*/

import { CAMPUS_GATE, walkLegs, gateLegs, stopAnchor } from './campus.js';
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

/**
 * @param {Object} o
 * @param {Element=} o.mount        campus.walkMount - the group above the ghosts
 * @param {boolean=} o.reducedMotion  no animation: the trace appears whole and
 *   the miniature snaps to the door (ORIENTATION §2.3)
 * @param {string=} o.spriteId      the seed the body is dealt from
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
  const you = svgNode('g', { transform: 'translate(' + pos[0] + ',' + pos[1] + ')' }, 'gh-you');
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
  if (mount) {
    try {
      if (residueLayer) mount.appendChild(residueLayer);
      if (trace) mount.appendChild(trace);
      if (you) mount.appendChild(you);
    } catch (e) { say('walk: mount refused (' + ((e && e.message) || e) + ')'); }
  }

  /* ----------------------------------------------------------- the drawing */

  function place() {
    if (!you) return;
    setAttr(you, 'transform', 'translate('
      + (Math.round(pos[0] * 10) / 10) + ',' + (Math.round(pos[1] * 10) / 10) + ')');
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
    paintTrace(r.legs);
    /* W3 P0-30: ARRIVAL. Two slowing footfalls and the room's air under them,
     * so the walk lands rather than stopping. A SKIP gets one clipped step -
     * the player cut the crossing short and the sound is cut with it - and a
     * walk that was superseded, torn down or never animated says nothing at
     * all, because nobody arrived anywhere. One dispatch: the follow-ups ride
     * the mixer's own timeline, so this file owns no cue timer. */
    if (!still) {
      if (reason === 'skipped') {
        sfx('step', 0.05, { pitch: 0.92 });
      } else if (reason === 'arrived') {
        sfx('step', 0.07, {
          pitch: 0.94,
          steps: [
            { atMs: 150, pitch: 0.88 },
            { atMs: 320, name: 'pad', level: 0.08, pitch: 1 },
          ],
        });
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
    }
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

  function destroy() {
    if (destroyed) return;
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
    diagnostics() {
      return {
        still, mounted: !!mount, walking: !!run, bound,
        at: pos.slice(), facing, residue: residueList.length,
      };
    },
  };
}

export default createWalker;
