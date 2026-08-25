/* ============================================================================
 * games/the-deep-end/casino.js - DECK II of the House Rules for the pool: the
 * lighting rig. The trickster (trickster.js) lies about the water; this file
 * lights it.
 *
 *   POOL IDENTITY   seeded per CLASS seed (Deck I): a hue pair on the
 *                   violet->rose arc (~6% of pools leave the arc for a teal
 *                   night - a bonus round for loading in), a breathing period,
 *                   a drift period, a spin sign, and a PATTERN JOURNEY of 2-4
 *                   stops through one caustic morph space (bands, god-rays,
 *                   lens caustics, a slow vortex, drifting motes - the veil of
 *                   surface light is always on). The journey is walked by the
 *                   class's own heat: the room evolves as you sink. Same seed,
 *                   same pool; a retake dives the identical water.
 *   THE MORPH       every stop is a point in ONE prop space (six family
 *                   alphas + tilt + scale) painted as registered custom props
 *                   on the stage, so a stop change TWEENS (style.js gives the
 *                   props 3-4s transitions) and the water transforms instead
 *                   of cutting.
 *   MARQUEE CHASE   a bulb-chase frame hugging the board. Crawls at low heat,
 *                   spins up with the depth line, goes gold and frantic for the
 *                   bell and the ceiling, and sighs out - never cuts - on a
 *                   dim-out. W2: the frame LIGHTING (a `chime`) and the SIGH
 *                   (a pitched-down `slide`) are this deck's own two cues -
 *                   requested through opts.cue, which is the game's clamped
 *                   helper. Every other beat here is already voiced by
 *                   index.js on the same frame and stays its.
 *   PAYOUT LIGHT    a merge pays light scaled by its chain link: the marquee
 *                   flashes, the water pulses, bubbles rise from the tile. A
 *                   new deepest tier pulses heavier and steps the whole room
 *                   one stop darker (--de-n-depth). The descending chime is
 *                   index.js's (audio is the engine's, never this file's).
 *   THE ALMOST      near-miss staging: two equal deepest tiles, adjacent and
 *                   blocked, LEAN toward each other (a 6% translate composed
 *                   into style.js's transform through --de-lean-x/y - the
 *                   TILE-level pair; the bench's own lean rides the separate
 *                   --de-bench-lean-x/y so it can never bleed into a tile)
 *                   while the square hums with pink light. The tiles never
 *                   change position; the lean is cleared on a deadline.
 *   THE CURRENT     (pass 3) every legal slide sends a flock of 10-16 drawn
 *                   chevrons through the room the way the board went: 4-5
 *                   seeded streamlines laid across the direction of travel,
 *                   upstream starting first so the flock sweeps, dimmed to a
 *                   whisper where it crosses the board, gone in 800ms. Two
 *                   flocks at most - a third slide recycles the oldest.
 *   EXHALE / RESURFACE / CEILING  the room calms (slower breath, slower
 *                   chase), the board drains (light runs out the bottom while
 *                   bubbles rise), the royal (gold flood, gold frame, a column
 *                   of bubbles).
 *
 * TABLE LAW AUDIT (House Rules):
 *   I   ledger honest - nothing here reads or writes the board, score, chain,
 *       depth or grade; index.js calls in AFTER its own accounting.
 *   II  input honest  - every node is pointer-events:none; --r/--c, data-tier
 *       and the tile's own nodes are never written; the lean rides vars that
 *       style.js composes and this file clears.
 *   III never still   - the veil breathes, every family drifts, the deep tiles
 *       shimmer (style.js), the marquee crawls even at heat 0.
 *   V   seeded        - per-tag mulberry32 streams off seed+'|de-casino'
 *       (the DV/L&F discipline; core makeTaggedRoll clusters). Append-only:
 *       new tags never shift old streams.
 *   VI  exits sacred  - bgIntensity 0 disarms the rig; reduced motion keeps a
 *       static dim veil + frame (style.js kills the animations, we skip the
 *       bubbles, the lean and the flashes); the stage's .suspended rule
 *       freezes the chase with everything else; every timer lives in the
 *       game's registry AND in a local set, so destroy() cannot leak one.
 *   VII strings      - this file renders no text at all.
 *
 * ENGINE PLACEMENT: game-local BY CHOICE (fourth marquee in the house; see the
 * lost-and-found header for the promotion plan).
 * ==========================================================================*/

import { makeRng } from '../../core/rng.js';

export const DE_CASINO = Object.freeze({
  /** Marquee pace band: heat 0 -> lazy crawl, heat 1 -> hungry. Seconds. */
  MQ_T_SLOW: 2.9,
  MQ_T_FAST: 0.85,
  /** Presence band (opacity): the pool glows before it blazes. */
  MQ_A_LO: 0.24,
  MQ_A_HI: 0.82,
  /** The bell / the ceiling: gold, faster than heat could ever push it. */
  MQ_T_BELL: 0.45,
  MQ_A_BELL: 0.95,
  /** Exhale: the chase slows by this factor while the room calms. */
  MQ_CALM_MUL: 1.6,
  /** Payout pulse length (outlives the .6s CSS animation). */
  FLASH_MS: 700,
  DEEP_MS: 1350,
  /** The almost: how long the lean + hum hold before the tiles settle. */
  ALMOST_MS: 1400,
  /** The drain. */
  DRAIN_MS: 1600,
  /** Bubbles: live budget and per-event counts. */
  MAX_BUBBLES: 44,
  BUB_MERGE_BASE: 3,
  BUB_DEEP: 7,
  BUB_RESURFACE: 14,
  BUB_ROYAL: 24,
  /** Off-arc pools (teal night): ~1 in 16. */
  OFF_ARC: 0.06,
  /** The journey: 2-4 stops through the caustic families. */
  STOPS_MIN: 2,
  STOPS_SPAN: 3,
  /** Hysteresis on the heat -> stop mapping, so a jittering heat never
   *  ping-pongs the morph. */
  STOP_HYST: 0.05,
  FAMILIES: Object.freeze(['bands', 'rays', 'lens', 'vortex', 'motes']),
  /** Pass 2 - THE SLIDE: the bench leans toward the move (style.js composes
   *  --de-bench-lean-x/y on the bench into a 2deg tilt + a 6px shove) and
   *  springs back when the lean clears. Magnitude = base + per tile moved +
   *  per cell of distance, capped at 1. The pair is DISTINCT from the tiles'
   *  --de-lean-x/y (the almost) so a bench lean can never inherit into every
   *  tile's positional transform. */
  LEAN_MS: 260,
  LEAN_MAG_BASE: 0.45,
  LEAN_MAG_STEP: 0.14,
  LEAN_MAG_DIST: 0.05,
  /** Pass 2 - THE WALL: the blocked edge flashes (the .g-de-wall node in the
   *  overlay, a transition so reduced motion still sees it) and the bench
   *  recoils INTO the wall for a beat. */
  WALL_MS: 300,
  BUMP_LEAN: 0.6,
  BUMP_LEAN_MS: 170,
  /** Pass 3 - THE CURRENT: a flock of chevrons crosses the room the way the
   *  board just went. Lanes, not a scatter: the arrows sit on 4-5 streamlines
   *  running across the direction of travel and start upstream first, so the
   *  flock reads as one current instead of as confetti. */
  FLOW_MS: 800,
  FLOW_MIN: 10,
  FLOW_SPAN: 7,            // 10..16 arrows
  FLOW_LANES_MIN: 4,
  FLOW_LANES_SPAN: 2,      // 4..5 streamlines
  FLOW_STAGGER: 140,
  FLOW_DRIFT_MIN: 18,
  FLOW_DRIFT_SPAN: 22,     // 18..40px of travel
  FLOW_SIZE_MIN: 20,
  FLOW_SIZE_SPAN: 16,
  /** Presence: full in the margins, a whisper over the board (the tiles are
   *  the thing being read; the current is weather). */
  FLOW_A_EDGE: 0.55,
  FLOW_A_OVER: 0.3,
  /** Never more than two flocks alive; a third slide recycles the oldest. */
  FLOW_MAX: 2,
  /** Reduced motion: one fade in place, in then out, still 800ms total. */
  FLOW_FADE_MS: 400,
});

/** Move direction -> unit vector (x right, y down). */
const DIRV = Object.freeze({
  up: { x: 0, y: -1 }, down: { x: 0, y: 1 }, left: { x: -1, y: 0 }, right: { x: 1, y: 0 },
});
const WALL_CLASSES = ['g-de-wall-up', 'g-de-wall-down', 'g-de-wall-left', 'g-de-wall-right'];
/** Which way a chevron points. 0deg is the glyph's own resting direction. */
const DIRROT = Object.freeze({ right: 0, down: 90, left: 180, up: 270 });

function el(tag, cls) {
  try {
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    return n;
  } catch (e) { return null; }
}
function clamp01(v) { const n = Number(v) || 0; return n < 0 ? 0 : n > 1 ? 1 : n; }
function rectOf(node) {
  try { return node && typeof node.getBoundingClientRect === 'function' ? node.getBoundingClientRect() : null; }
  catch (e) { return null; }
}
/** A tile's honest row/col, read (never written) off A's inline vars. */
function gridOf(tile) {
  if (!tile || !tile.style) return null;
  try {
    const r = parseFloat(tile.style.getPropertyValue('--r'));
    const c = parseFloat(tile.style.getPropertyValue('--c'));
    if (Number.isFinite(r) && Number.isFinite(c)) return { r, c };
  } catch (e) { /* fall through */ }
  return null;
}

/**
 * @param {Object} o
 * @param {string}   o.seed      the class seed (retakes replay the pool)
 * @param {number}   o.tier      1..4
 * @param {Object}   o.stage     .g-de-stage (identity props land here)
 * @param {Object}   o.bench     .g-de-bench (marquee + overlay host)
 * @param {Object}   o.board     .g-de-board (geometry reference)
 * @param {Object}   o.backdrop  .g-de-backdrop (the lighting layers)
 * @param {Object}   o.timers    {after(ms,fn)->id, every?, clear|cancel(id)}
 * @param {boolean}  o.reduced   reduced motion
 * @param {boolean}  o.capsOk    false when bgIntensity is capped to 0
 * @param {Function=} o.cue       cue(name, level, extra) - the GAME's clamped
 *                                audio helper (index.js `tick`). The deck asks;
 *                                it never holds an audio node and never raises
 *                                the tier's ceiling.
 * @param {Function=} o.log
 */
export function createDeCasino(o) {
  const opts = o || {};
  const say = typeof opts.log === 'function' ? opts.log : () => {};
  const reduced = !!opts.reduced;
  /* pass 6 - THE TOUCH RUNG: the game's own touch flag. On a phone the flock
     is skipped whole (10-16 filtered nodes + two forced layouts per slide). */
  const touchDev = !!opts.touch;
  const armed = !!opts.capsOk && !!opts.stage && !!opts.bench && !!opts.backdrop
    && !!opts.timers && typeof opts.timers.after === 'function' && typeof document !== 'undefined';
  /* W2 - THE CUE ROAD, AND THE DECOUPLE. `armed` folds capsOk in, and
     bgIntensity 0 is the player's VISUAL exit (Law VI) - it is not a request
     for a silent school. So sound gates on `destroyed` alone: the rig may be
     dark and still sigh. Nothing below this line ever raises a level. */
  const cue = typeof opts.cue === 'function' ? opts.cue : () => {};
  const sounds = () => !destroyed;

  /* timers: the game's registry (pause-aware) + a local set so destroy() can
     drop every one of ours without knowing the registry's shape. The
     contract names clear(); DV's deckTimers names cancel() - accept both. */
  const live = new Set();
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

  const seedBase = String(opts.seed || 'de') + '|de-casino|';
  const streams = new Map();
  const roll = (tag) => {
    let s = streams.get(tag);
    if (!s) { s = makeRng(seedBase + tag); streams.set(tag, s); }
    return s();
  };

  let destroyed = false;
  let started = false;
  let startCued = false;         // W2: the frame lights ONCE, armed or not
  let mq = null;
  let cs = null;                 // the overlay in the bench
  const layers = {};             // backdrop family layers by name
  const props = new Set();       // every --de-n-* we painted (for removal)
  const stageClasses = new Set();
  let bellOn = false;
  let calmOn = false;
  let royalOn = false;
  let outOn = false;
  let lastHeat = 0;
  let stopIx = -1;
  let flashTimer = 0;
  let deepTimer = 0;
  let humTimer = 0;
  let drainTimer = 0;
  let bubbles = 0;
  let identity = null;
  let leaning = [];
  let wall = null;               // the edge-flash frame in the overlay
  let leanTimer = 0;
  let wallTimer = 0;
  let benchLeaning = false;
  let slides = 0;
  let bumps = 0;
  let flow = null;               // the arrow layer in the overlay
  const flocks = [];             // [{nodes, timers}] - at most FLOW_MAX
  let flows = 0;
  /* perf (owner's phone, 2026-08-25): the bench/board rects used to be read
     with getBoundingClientRect INSIDE the merge/slide path - right after the
     move handler dirtied every tile's style, so each read was a forced layout
     flush at the worst possible moment. The geometry is now measured ONCE
     (lazily, then cached) and only invalidated by resize/orientation. */
  let geom = null;               // {bw,bh,gx,gy,gw,gh,n,tile,step} | null
  let resizeBound = false;
  const invalidateGeom = () => { geom = null; };
  function measureGeom() {
    const bench = rectOf(opts.bench);
    if (!bench || !bench.width || !bench.height) { geom = null; return null; }
    const board = rectOf(opts.board);
    const g = {
      bw: bench.width, bh: bench.height,
      gx: board ? board.left - bench.left : 0,
      gy: board ? board.top - bench.top : 0,
      gw: board && board.width ? board.width : 0,
      gh: board && board.height ? board.height : 0,
      n: 0, tile: 0, step: 0,
    };
    // the grid step, derived once: n from the cell count, the gap from the
    // board's computed style - so anchorOf never has to measure a tile again
    try {
      const cells = opts.board && typeof opts.board.querySelectorAll === 'function'
        ? opts.board.querySelectorAll('.g-de-cell').length : 0;
      if (cells > 0) g.n = Math.round(Math.sqrt(cells));
    } catch (e) { /* stays 0 */ }
    if (g.n > 0 && g.gw > 0) {
      let gap = 0;
      try {
        if (typeof getComputedStyle === 'function') {
          gap = parseFloat(getComputedStyle(opts.board).columnGap) || 0;
        }
      } catch (e) { gap = 0; }
      g.tile = (g.gw - (g.n - 1) * gap) / g.n;
      g.step = g.tile + gap;
    }
    geom = g;
    return g;
  }
  function geomNow() { return geom || measureGeom(); }
  function bindResize() {
    if (resizeBound || typeof window === 'undefined' || !window.addEventListener) return;
    try {
      window.addEventListener('resize', invalidateGeom);
      window.addEventListener('orientationchange', invalidateGeom);
      resizeBound = true;
    } catch (e) { /* stays unbound; geomNow simply keeps the first measure */ }
  }
  function unbindResize() {
    if (!resizeBound) return;
    resizeBound = false;
    try {
      window.removeEventListener('resize', invalidateGeom);
      window.removeEventListener('orientationchange', invalidateGeom);
    } catch (e) { /* ignore */ }
  }

  /* ---------------------------------------------------- the pool's identity */
  function setProp(k, v) {
    if (!opts.stage || !opts.stage.style) return;
    try { opts.stage.style.setProperty(k, v); props.add(k); } catch (e) { /* ignore */ }
  }
  function stageClass(name, on) {
    if (!opts.stage || !opts.stage.classList) return;
    try {
      if (on) { opts.stage.classList.add(name); stageClasses.add(name); }
      else { opts.stage.classList.remove(name); stageClasses.delete(name); }
    } catch (e) { /* ignore */ }
  }

  /** Draw the identity in a FIXED order (append-only, or every pool reskins). */
  function drawIdentity() {
    const offArc = roll('arc') < DE_CASINO.OFF_ARC;
    let hueA;
    let hueB;
    if (offArc) {
      hueA = 172 + roll('hue') * 24;                                   // teal night
      hueB = Math.max(160, Math.min(205, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (10 + roll('hue2') * 14)));
    } else {
      hueA = 255 + roll('hue') * 90;                                   // 255 violet .. 345 rose
      hueB = Math.max(245, Math.min(350, hueA + (roll('hue2') < 0.5 ? -1 : 1) * (18 + roll('hue2') * 22)));
    }
    const breath = 6.5 + roll('breath') * 4.5;                         // 6.5..11s
    const drift = 16 + roll('drift') * 14;                             // 16..30s
    const kb = 22 + roll('kb') * 12;                                   // 22..34s
    const spindir = roll('spin') < 0.35 ? -1 : 1;
    const veil = 0.5 + roll('veil') * 0.3;
    /* the journey: 2-4 DISTINCT family stops, each with its genes jittered
       once so two pools sharing a family still differ */
    const stops = DE_CASINO.STOPS_MIN + Math.floor(roll('stops') * DE_CASINO.STOPS_SPAN);
    const pool = DE_CASINO.FAMILIES.slice();
    for (let i = pool.length - 1; i > 0; i--) {
      const j = Math.min(i, Math.floor(roll('shuffle') * (i + 1)));
      const sw = pool[i]; pool[i] = pool[j]; pool[j] = sw;
    }
    const journey = pool.slice(0, stops).map((fam, i) => ({
      fam,
      alpha: 0.5 + roll('alpha') * 0.4,
      // the next stop's family pre-blends in, so the morph reads as growth
      next: pool[(i + 1) % stops],
      nextAlpha: 0.15 + roll('next') * 0.2,
      tilt: (roll('tilt') < 0.5 ? -1 : 1) * (6 + roll('tilt') * 30),
      scale: 0.8 + roll('scale') * 0.6,
    }));
    identity = {
      offArc, hueA: Math.round(hueA), hueB: Math.round(hueB),
      breath: +breath.toFixed(1), drift: +drift.toFixed(1), kb: +kb.toFixed(1), spindir, veil: +veil.toFixed(2),
      journey,
    };
  }

  function dressPool() {
    if (!identity) drawIdentity();
    const id = identity;
    const sat = id.offArc ? 58 : 66;
    setProp('--de-n-hue-a', String(id.hueA));
    setProp('--de-n-hue-b', String(id.hueB));
    setProp('--de-n-la', 'hsla(' + id.hueA + ',' + sat + '%,72%,.30)');
    setProp('--de-n-lb', 'hsla(' + id.hueB + ',' + (sat + 6) + '%,68%,.22)');
    setProp('--de-n-mq', 'hsl(' + id.hueA + ',80%,72%)');
    setProp('--de-n-bub', 'hsla(' + id.hueB + ',70%,80%,.7)');
    setProp('--de-n-breath', id.breath + 's');
    setProp('--de-n-drift', id.drift + 's');
    setProp('--de-n-kb', id.kb + 's');
    setProp('--de-n-spindir', String(id.spindir));
    setProp('--de-n-a-veil', String(id.veil));
    setProp('--de-n-depth', '0');
    paintStop(0);
    say('casino: pool dressed (hue ' + id.hueA + '/' + id.hueB + (id.offArc ? ' OFF-ARC' : '')
      + ', journey ' + id.journey.map((s) => s.fam).join('>') + ')');
  }

  /** Paint one journey stop as the family alphas + genes. The registered
   *  props tween (style.js), so this is the whole morph. */
  function paintStop(ix) {
    if (!identity) return;
    const stop = identity.journey[Math.max(0, Math.min(identity.journey.length - 1, ix))];
    if (!stop) return;
    stopIx = ix;
    for (const fam of DE_CASINO.FAMILIES) {
      const a = fam === stop.fam ? stop.alpha : fam === stop.next ? stop.nextAlpha : 0;
      setProp('--de-n-a-' + fam, a.toFixed(2));
    }
    setProp('--de-n-tilt', stop.tilt.toFixed(1));
    setProp('--de-n-scale', stop.scale.toFixed(2));
  }

  /** heat -> stop, with hysteresis so a jitter never ping-pongs the water. */
  function walkJourney(h) {
    if (!identity) return;
    const n = identity.journey.length;
    if (n < 2) return;
    const raw = Math.min(n - 1, Math.floor(h * n));
    if (stopIx < 0) { paintStop(raw); return; }
    if (raw > stopIx && h >= (stopIx + 1) / n + DE_CASINO.STOP_HYST) paintStop(stopIx + 1);
    else if (raw < stopIx && h <= stopIx / n - DE_CASINO.STOP_HYST) paintStop(stopIx - 1);
  }

  /* ------------------------------------------------------------- the DOM */
  function mountBackdrop() {
    const host = opts.backdrop;
    if (!host || !host.appendChild) return;
    const order = ['veil', 'bands', 'rays', 'lens', 'vortex', 'motes', 'dark', 'flash', 'royal', 'vig'];
    for (const name of order) {
      const n = el('div', 'g-de-bd g-de-bd-' + name);
      if (!n) continue;
      layers[name] = n;
      host.appendChild(n);
    }
  }

  function mountMarquee() {
    if (mq || !opts.bench.appendChild) return;
    mq = el('div', 'g-de-mq');
    if (!mq) return;
    for (const cls of ['mq-t', 'mq-r', 'mq-b', 'mq-l']) {
      const bar = el('i', cls);
      if (bar) mq.appendChild(bar);
    }
    // seeded phase: the chase never starts on the same bulb twice
    if (mq.style) mq.style.setProperty('--g-de-mqp', (roll('mq-phase') * -2.9).toFixed(2) + 's');
    opts.bench.appendChild(mq);
  }

  function mountOverlay() {
    if (cs || !opts.bench.appendChild) return;
    cs = el('div', 'g-de-cs');
    if (!cs) return;
    opts.bench.appendChild(cs);
    wall = el('i', 'g-de-wall');
    if (wall && cs.appendChild) cs.appendChild(wall);
    // pass 3: the current's own layer, a sibling of the wall - inside the
    // stage, so .suspended freezes the flock with the rest of the room
    flow = el('div', 'g-de-flow');
    if (flow && cs.appendChild) cs.appendChild(flow);
  }

  /* ------------------------------------------------------ the bench lean */
  /** The bench leans (x, y in -1..1) and springs back after ms. One lean at a
   *  time: a new one replaces the old deadline, never stacks.
   *  BUG FIX (2026-08-25): this used to write --de-lean-x/y - the SAME names
   *  every tile's positional transform reads for the almost's tile lean. The
   *  custom props inherited from the bench into all 16-25 tiles, so every
   *  slide gave every tile an extra transform transition (and again on the
   *  spring-back). The bench pair is now its own name. */
  function leanBench(x, y, ms) {
    if (!opts.bench || !opts.bench.style) return;
    try {
      opts.bench.style.setProperty('--de-bench-lean-x', x.toFixed(2));
      opts.bench.style.setProperty('--de-bench-lean-y', y.toFixed(2));
      benchLeaning = true;
    } catch (e) { return; }
    if (leanTimer) cancel(leanTimer);
    leanTimer = after(ms, () => { leanTimer = 0; clearBenchLean(); });
  }
  function clearBenchLean() {
    benchLeaning = false;
    if (!opts.bench || !opts.bench.style) return;
    try { opts.bench.style.removeProperty('--de-bench-lean-x'); opts.bench.style.removeProperty('--de-bench-lean-y'); } catch (e) { /* ignore */ }
  }
  function clearWall() {
    if (!wall || !wall.classList) return;
    wall.classList.remove('g-de-wall-on');
  }

  function paintMarquee() {
    if (!mq || !mq.style) return;
    const calm = calmOn ? DE_CASINO.MQ_CALM_MUL : 1;
    const t = bellOn ? DE_CASINO.MQ_T_BELL
      : (DE_CASINO.MQ_T_SLOW - (DE_CASINO.MQ_T_SLOW - DE_CASINO.MQ_T_FAST) * lastHeat) * calm;
    const a = bellOn ? DE_CASINO.MQ_A_BELL
      : DE_CASINO.MQ_A_LO + (DE_CASINO.MQ_A_HI - DE_CASINO.MQ_A_LO) * lastHeat;
    mq.style.setProperty('--g-de-mqt', t.toFixed(2) + 's');
    mq.style.setProperty('--g-de-mqa', a.toFixed(2));
  }

  function flashMarquee(strength) {
    if (!mq || !mq.classList) return;
    mq.classList.remove('g-de-mq-flash');
    if (typeof mq.offsetWidth === 'number') void mq.offsetWidth;   // restart the CSS animation
    mq.style.setProperty('--g-de-mqf', String(Math.max(1, Math.min(2.2, strength))));
    mq.classList.add('g-de-mq-flash');
    if (flashTimer) cancel(flashTimer);
    flashTimer = after(DE_CASINO.FLASH_MS, () => {
      flashTimer = 0;
      if (mq && mq.classList) mq.classList.remove('g-de-mq-flash');
    });
  }

  /** The water pulses: the flash layer runs its keyframe once. */
  function pulseWater(strength, deep) {
    const f = layers.flash;
    if (!f || !f.classList || reduced) return;
    f.classList.remove('g-de-on', 'g-de-deep');
    if (typeof f.offsetWidth === 'number') void f.offsetWidth;
    setProp('--de-n-pay', clamp01(strength).toFixed(2));
    f.classList.add(deep ? 'g-de-deep' : 'g-de-on');
    if (deepTimer) cancel(deepTimer);
    deepTimer = after(deep ? DE_CASINO.DEEP_MS : DE_CASINO.FLASH_MS, () => {
      deepTimer = 0;
      if (f.classList) f.classList.remove('g-de-on', 'g-de-deep');
    });
  }

  /* ------------------------------------------------------------- bubbles */
  /**
   * Bubbles rise from a point in the bench (bench-relative px). Seeded size,
   * wobble and duration; budgeted so a chain storm never floods the layer.
   */
  function bubblesAt(x, y, count, tier, spread) {
    if (!cs || !cs.appendChild || reduced) return 0;
    let made = 0;
    for (let i = 0; i < count; i++) {
      if (bubbles >= DE_CASINO.MAX_BUBBLES) break;
      const b = el('i', 'g-de-bub');
      if (!b || !b.style) break;
      const s = (5 + roll('bub-s') * 9) * (1 + Math.max(0, Math.min(11, Number(tier) || 1)) / 14);
      const d = 1.1 + roll('bub-d') * 1.2;
      const bx = x + (roll('bub-x') - 0.5) * (spread || 40);
      const by = y + (roll('bub-y') - 0.5) * (spread ? spread * 0.4 : 20);
      b.style.setProperty('--x', bx.toFixed(0) + 'px');
      b.style.setProperty('--y', by.toFixed(0) + 'px');
      b.style.setProperty('--s', s.toFixed(1) + 'px');
      b.style.setProperty('--d', d.toFixed(2) + 's');
      b.style.setProperty('--h', Math.max(40, by * (0.55 + roll('bub-h') * 0.45)).toFixed(0) + 'px');
      b.style.setProperty('--wx', ((roll('bub-w') - 0.5) * 36).toFixed(0) + 'px');
      cs.appendChild(b);
      bubbles += 1;
      made += 1;
      after(d * 1000 + 120, () => { bubbles = Math.max(0, bubbles - 1); try { b.remove(); } catch (e) { /* ignore */ } });
    }
    return made;
  }

  /** Bench-relative centre of a tile (or of the board when the tile is gone).
   *  BUG FIX (2026-08-25): this used to getBoundingClientRect a MID-TRANSITION
   *  tile - a forced layout in the merge path AND an interpolated position, so
   *  the bubbles rose from wherever the slide happened to be. The anchor is
   *  now derived from the tile's honest --r/--c against the cached grid step:
   *  no layout flush, and the bubbles anchor to the TRUE cell. */
  function anchorOf(tileEl) {
    const g = geomNow();
    if (!g) return null;
    const cell = gridOf(tileEl);
    if (cell && g.step > 0) {
      return {
        x: g.gx + cell.c * g.step + g.tile / 2,
        y: g.gy + cell.r * g.step + g.tile / 2,
        w: g.tile, h: g.tile,
      };
    }
    if (g.gw) return { x: g.gx + g.gw / 2, y: g.gy + g.gh / 2, w: g.gw, h: g.gh };
    return { x: g.bw / 2, y: g.bh / 2, w: 0, h: 0 };
  }

  /* ------------------------------------------------------------- the lean */
  function clearLean() {
    for (const tile of leaning) {
      try { tile.style.removeProperty('--de-lean-x'); tile.style.removeProperty('--de-lean-y'); } catch (e) { /* ignore */ }
    }
    leaning = [];
  }

  /* ---------------------------------------------------------- the current */
  /** Put one flock out: its deadline, its fade timer, its nodes. */
  function killFlock(flock) {
    if (!flock) return;
    const ix = flocks.indexOf(flock);
    if (ix >= 0) flocks.splice(ix, 1);
    for (const id of flock.timers) cancel(id);
    flock.timers.length = 0;
    for (const node of flock.nodes) { try { node.remove(); } catch (e) { /* ignore */ } }
    flock.nodes.length = 0;
  }
  function killFlocks() { while (flocks.length) killFlock(flocks[0]); }

  /**
   * THE CURRENT. The arrows ride 4-5 streamlines laid ACROSS the direction of
   * travel and spread along it, all pointing and drifting the one way; the
   * ones that fall over the board are dimmed to a whisper so the tiles still
   * read. Upstream starts first, so the flock sweeps instead of popping.
   * Everything is drawn from the pool's own seeded streams (Law V) and every
   * node is removed on the casino's own pause-aware timer.
   */
  function spawnFlock(dir, v) {
    if (!flow || !flow.appendChild) return 0;
    while (flocks.length >= DE_CASINO.FLOW_MAX) killFlock(flocks[0]);
    const horiz = v.x !== 0;
    const g = geomNow();               // cached: no forced layout in the slide path
    let bw = g && g.bw ? g.bw : 0;
    let bh = g && g.bh ? g.bh : 0;
    if (!bw || !bh) { bw = 620; bh = 620; }
    let gw = g && g.gw ? g.gw : 0;
    let gh = g && g.gh ? g.gh : 0;
    let gx = g ? g.gx : 0;
    let gy = g ? g.gy : 0;
    if (!gw || !gh || (gw >= bw - 10 && gh >= bh - 10)) {
      /* nothing measurable to sit beside: assume the square owns the middle
         76% of the bench, which is what the layout gives it */
      gw = bw * 0.76; gh = bh * 0.76; gx = (bw - gw) / 2; gy = (bh - gh) / 2;
    }
    const count = DE_CASINO.FLOW_MIN + Math.floor(roll('flow-n') * (DE_CASINO.FLOW_SPAN + 1));
    const lanes = DE_CASINO.FLOW_LANES_MIN + Math.floor(roll('flow-lanes') * DE_CASINO.FLOW_LANES_SPAN);
    const perLane = Math.max(1, Math.ceil(count / lanes));
    const alongSpan = horiz ? bw : bh;
    const acrossSpan = horiz ? bh : bw;
    const downstream = (v.x + v.y) > 0;
    const rot = String(DIRROT[dir] || 0) + 'deg';
    const nodes = [];
    for (let i = 0; i < count; i++) {
      const node = el('i', 'g-de-arrow');
      if (!node || !node.style) break;
      const lane = i % lanes;
      const slot = Math.floor(i / lanes);
      const across = ((lane + 0.5) / lanes + (roll('flow-p') - 0.5) * 0.11) * acrossSpan;
      const alongF = (slot + 0.5) / perLane + (roll('flow-a') - 0.5) * (0.22 / perLane);
      const x = horiz ? alongF * alongSpan : across;
      const y = horiz ? across : alongF * alongSpan;
      const over = x > gx && x < gx + gw && y > gy && y < gy + gh;
      const size = (DE_CASINO.FLOW_SIZE_MIN + roll('flow-s') * DE_CASINO.FLOW_SIZE_SPAN) * (over ? 0.8 : 1);
      const drift = DE_CASINO.FLOW_DRIFT_MIN + roll('flow-d') * DE_CASINO.FLOW_DRIFT_SPAN;
      const wave = Math.max(0, Math.min(1, downstream ? alongF : 1 - alongF));
      node.style.setProperty('--de-fa-x', x.toFixed(0) + 'px');
      node.style.setProperty('--de-fa-y', y.toFixed(0) + 'px');
      node.style.setProperty('--de-fa-s', size.toFixed(0) + 'px');
      node.style.setProperty('--de-fa-rot', rot);
      node.style.setProperty('--de-fa-a', (over ? DE_CASINO.FLOW_A_OVER : DE_CASINO.FLOW_A_EDGE).toFixed(2));
      if (!reduced) {
        node.style.setProperty('--de-fa-dx', (v.x * drift).toFixed(0) + 'px');
        node.style.setProperty('--de-fa-dy', (v.y * drift).toFixed(0) + 'px');
        node.style.setProperty('--de-fa-d', (wave * DE_CASINO.FLOW_STAGGER).toFixed(0) + 'ms');
      }
      flow.appendChild(node);
      nodes.push(node);
    }
    if (!nodes.length) return 0;
    flows += 1;
    const flock = { nodes, timers: [] };
    flocks.push(flock);
    if (reduced) {
      /* the keyframe is killed under reduced motion, so the fade is a plain
         transition: on now, off on the halfway deadline, gone at 800ms */
      if (typeof flow.offsetWidth === 'number') void flow.offsetWidth;
      for (const node of nodes) { if (node.classList) node.classList.add('is-in'); }
      flock.timers.push(after(DE_CASINO.FLOW_FADE_MS, () => {
        for (const node of nodes) { if (node.classList) node.classList.remove('is-in'); }
      }));
    }
    flock.timers.push(after(DE_CASINO.FLOW_MS + DE_CASINO.FLOW_STAGGER + 80, () => killFlock(flock)));
    return nodes.length;
  }

  /* ---------------------------------------------------------------- api */
  return {
    /** Dress the pool + light the frame. Call when play arms. */
    start() {
      if (destroyed) return;
      /* THE FRAME LIGHTS: the marquee's bulb-chase comes up as the water
         opens. One clean bell, once - `startCued` guards a second call, and
         the cue sits ABOVE the armed gate so a capped-background class still
         hears the room open. */
      if (!startCued) { startCued = true; cue('chime', 0.3); }
      if (!armed) { say('casino: disarmed'); return; }
      if (started) return;
      started = true;
      mountBackdrop();
      mountMarquee();
      mountOverlay();
      dressPool();
      paintMarquee();
      bindResize();
      measureGeom();     // one forced layout HERE, not per merge (resize re-measures)
      say('casino: marquee lit, ' + Object.keys(layers).length + ' layers');
    },

    /** Ride the class's own heat curve. index.js calls from its heat(). */
    setHeat(h) {
      lastHeat = clamp01(h);
      paintMarquee();
      walkJourney(lastHeat);
    },

    /** A merge pays light scaled by its chain link. */
    merge(m) {
      if (!armed || destroyed || !started) return;
      const info = m || {};
      const link = Math.max(1, Math.min(8, Number(info.link) || 1));
      const tier = Math.max(1, Math.min(11, Number(info.tier) || 1));
      flashMarquee(1 + 0.12 * link);
      pulseWater(0.28 + 0.08 * link + (tier >= 6 ? 0.1 : 0), false);
      const a = anchorOf(info.tileEl);
      if (a) bubblesAt(a.x, a.y, DE_CASINO.BUB_MERGE_BASE + Math.min(5, link) + (tier >= 6 ? 2 : 0), tier, a.w * 0.6);
    },

    /** A new deepest tier: heavier pulse, and the room steps one stop darker. */
    newDeepest(tier, tileEl) {
      if (!armed || destroyed || !started) return;
      const t = Math.max(1, Math.min(11, Number(tier) || 1));
      setProp('--de-n-depth', (t / 11).toFixed(3));
      flashMarquee(1.6 + t / 22);
      pulseWater(0.7, true);
      const a = anchorOf(tileEl);
      if (a) bubblesAt(a.x, a.y, DE_CASINO.BUB_DEEP, t, (a.w || 60) * 0.8);
    },

    /**
     * The almost: two equal deepest tiles, adjacent but blocked, lean toward
     * each other while the square hums. Position never changes - the lean is
     * a 6% translate style.js composes from --de-lean-x/y, cleared here.
     */
    almost(a, b) {
      if (!armed || destroyed || !started || reduced || !a || !b || !a.style || !b.style) return;
      clearLean();
      const ga = gridOf(a);
      const gb = gridOf(b);
      if (ga && gb) {
        const dx = Math.sign(gb.c - ga.c);
        const dy = Math.sign(gb.r - ga.r);
        try {
          a.style.setProperty('--de-lean-x', String(dx)); a.style.setProperty('--de-lean-y', String(dy));
          b.style.setProperty('--de-lean-x', String(-dx)); b.style.setProperty('--de-lean-y', String(-dy));
          leaning = [a, b];
        } catch (e) { /* ignore */ }
      }
      if (cs && cs.classList) {
        cs.classList.remove('g-de-hum');
        if (typeof cs.offsetWidth === 'number') void cs.offsetWidth;
        cs.classList.add('g-de-hum');
      }
      if (humTimer) cancel(humTimer);
      humTimer = after(DE_CASINO.ALMOST_MS, () => {
        humTimer = 0;
        clearLean();
        if (cs && cs.classList) cs.classList.remove('g-de-hum');
      });
    },

    /**
     * Pass 2 - THE SLIDE: every legal move leans the bench toward the
     * direction of travel and lets it spring back. The tiles' own trails and
     * squash are style.js's (index.js marks them); the room's weight is ours.
     * Reduced motion: no lean (style.js also pins the bench flat).
     * Pass 3 - THE CURRENT rides the same call: a flock of arrows crosses the
     * room the way the board went. It survives reduced motion as a fade.
     */
    slide(dir, count, distance) {
      if (!armed || destroyed || !started) return;
      slides += 1;
      const v = DIRV[String(dir)];
      if (!v) return;
      /* pass 6: no flock on a touch device - 10-16 drop-shadowed nodes per
         slide (cap 32 live) was the single heaviest per-slide cost on a phone.
         The bench lean below survives (style.js flattens it under ae-touch). */
      if (!touchDev) spawnFlock(String(dir), v);
      if (reduced) return;
      const n = Math.max(1, Math.min(4, Number(count) || 1));
      const d = Math.max(1, Math.min(4, Number(distance) || 1));
      const mag = Math.min(1, DE_CASINO.LEAN_MAG_BASE + DE_CASINO.LEAN_MAG_STEP * n + DE_CASINO.LEAN_MAG_DIST * d);
      leanBench(v.x * mag, v.y * mag, DE_CASINO.LEAN_MS);
    },

    /**
     * Pass 2 - THE WALL: a move that slid nothing. The blocked edge flashes
     * (a transition, so reduced motion keeps the flash) and the bench recoils
     * into the wall for a beat (skipped under reduced motion). The board's
     * shake and the `bump` cue are index.js's.
     */
    bump(dir) {
      if (!armed || destroyed || !started) return;
      bumps += 1;
      const v = DIRV[String(dir)];
      if (!v) return;
      if (!reduced) leanBench(v.x * DE_CASINO.BUMP_LEAN, v.y * DE_CASINO.BUMP_LEAN, DE_CASINO.BUMP_LEAN_MS);
      if (wall && wall.classList) {
        wall.classList.remove('g-de-wall-on', ...WALL_CLASSES);
        if (typeof wall.offsetWidth === 'number') void wall.offsetWidth;   // restart the transition
        wall.classList.add('g-de-wall-' + dir, 'g-de-wall-on');
      }
      if (wallTimer) cancel(wallTimer);
      wallTimer = after(DE_CASINO.WALL_MS, () => { wallTimer = 0; clearWall(); });
    },

    /** The mercy breath: the room calms (slower breath, slower chase, brighter). */
    exhale(on) {
      calmOn = !!on;
      stageClass('g-de-calm', calmOn);
      paintMarquee();
    },

    /** The board drains: light runs out the bottom, bubbles rise, then reset. */
    resurface() {
      if (!armed || destroyed || !started) return;
      clearLean();
      if (cs && cs.classList && !reduced) {
        cs.classList.remove('g-de-draining');
        if (typeof cs.offsetWidth === 'number') void cs.offsetWidth;
        cs.classList.add('g-de-draining');
      }
      const g = geomNow();
      if (g && g.gw) {
        const cx = g.gx + g.gw / 2;
        const cy = g.gy + g.gh * 0.85;
        bubblesAt(cx, cy, DE_CASINO.BUB_RESURFACE, 6, g.gw * 0.9);
      }
      // the frame dips with the water, then heat repaints it
      if (mq && mq.style) mq.style.setProperty('--g-de-mqa', '0.12');
      if (drainTimer) cancel(drainTimer);
      drainTimer = after(DE_CASINO.DRAIN_MS, () => {
        drainTimer = 0;
        if (cs && cs.classList) cs.classList.remove('g-de-draining');
        paintMarquee();
      });
    },

    /** The last 20s: gold frame, frantic chase. */
    bell(on) {
      bellOn = !!on;
      if (mq && mq.classList) { if (bellOn) mq.classList.add('g-de-mq-bell'); else mq.classList.remove('g-de-mq-bell'); }
      paintMarquee();
    },

    /** The royal: tier 11 ends the class in gold. Holds until dimOut/stop. */
    ceiling() {
      if (!armed || destroyed || !started) return;
      royalOn = true;
      bellOn = true;
      stageClass('g-de-royal', true);
      if (cs && cs.classList) cs.classList.add('g-de-royal');
      if (mq && mq.classList) mq.classList.add('g-de-mq-bell');
      paintMarquee();
      flashMarquee(2.2);
      pulseWater(1, true);
      const g = geomNow();
      if (g && g.gw) {
        bubblesAt(g.gx + g.gw / 2, g.gy + g.gh * 0.9, DE_CASINO.BUB_ROYAL, 11, g.gw);
      }
    },

    /** The bell took the board: the rig sighs out instead of cutting. */
    dimOut() {
      /* THE SIGH. The header has promised for two passes that this frame
         "sighs out - never cuts": a `slide` pitched DOWN is that sigh, the
         same whoosh the board's moves use, falling instead of travelling. */
      if (sounds()) cue('slide', 0.3, { pitch: 0.8 });
      outOn = true;
      bellOn = false;
      royalOn = false;
      clearLean();
      if (mq && mq.classList) {
        mq.classList.remove('g-de-mq-bell', 'g-de-mq-flash');
        mq.classList.add('g-de-mq-out');
      }
      if (cs && cs.classList) cs.classList.remove('g-de-royal', 'g-de-hum', 'g-de-draining');
      stageClass('g-de-royal', false);
      stageClass('g-de-calm', false);
      stageClass('g-de-out', true);
    },

    /** The class is over; nothing may pulse again. */
    stop() {
      if (flashTimer) { cancel(flashTimer); flashTimer = 0; }
      if (deepTimer) { cancel(deepTimer); deepTimer = 0; }
      if (humTimer) { cancel(humTimer); humTimer = 0; }
      if (drainTimer) { cancel(drainTimer); drainTimer = 0; }
      if (leanTimer) { cancel(leanTimer); leanTimer = 0; }
      if (wallTimer) { cancel(wallTimer); wallTimer = 0; }
      clearLean();
      clearBenchLean();
      clearWall();
      killFlocks();
      if (mq && mq.classList) mq.classList.remove('g-de-mq-flash');
      if (cs && cs.classList) cs.classList.remove('g-de-hum', 'g-de-draining');
    },

    destroy() {
      destroyed = true;
      unbindResize();
      for (const id of Array.from(live)) cancel(id);
      live.clear();
      clearLean();
      clearBenchLean();
      killFlocks();
      if (flow) { try { flow.remove(); } catch (e) { /* ignore */ } }
      flow = null;
      wall = null;
      if (mq) { try { mq.remove(); } catch (e) { /* ignore */ } }
      if (cs) { try { cs.remove(); } catch (e) { /* ignore */ } }
      for (const k of Object.keys(layers)) { try { layers[k].remove(); } catch (e) { /* ignore */ } delete layers[k]; }
      mq = null; cs = null; bubbles = 0;
      for (const name of Array.from(stageClasses)) stageClass(name, false);
      if (opts.stage && opts.stage.style) {
        for (const k of props) { try { opts.stage.style.removeProperty(k); } catch (e) { /* ignore */ } }
      }
      props.clear();
    },

    /** Diagnostics for the harness; not part of the module contract. */
    diagnostics() {
      return {
        armed, started, startCued, marquee: !!mq, overlay: !!cs, layers: Object.keys(layers).length,
        bell: bellOn, calm: calmOn, royal: royalOn, out: outOn,
        heat: lastHeat, stop: stopIx, bubbles, leaning: leaning.length, identity,
        wall: !!wall, benchLeaning, slides, bumps,
        flow: !!flow, flows, flocks: flocks.length,
        arrowsLive: flocks.reduce((sum, f) => sum + f.nodes.length, 0),
      };
    },
  };
}

export default createDeCasino;
