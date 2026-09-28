// Capture replay timing and layout. Pure: no THREE, no DOM, so node can check it.
//
// A capture is recorded while it plays, then shown again in two or three tilted
// comic panels (or one corner inset). The panels are STAGGERED (owner,
// 2026-09-28): every panel shows the same capture from its own camera, but the
// hit lands in panel 1 first, then panel 2, then panel 3, so the eye can go
// big panel, second, third and see the impact in each. A later panel starts
// further back in the clip, and every panel keeps playing after its own hit.
// The panel taking its hit is lit; the others dim until it has passed.

export const LAYOUTS = Object.freeze({
  // Normalised 0..1 stage coordinates, top-down, convex, in order round each
  // panel. Panel 0 is always the big one; the seams are shared edges.
  trio: [
    [[0, 0], [.6, 0], [.52, 1], [0, 1]],
    [[.6, 0], [1, 0], [1, .49], [.5576, .53]],
    [[.5576, .53], [1, .49], [1, 1], [.52, 1]],
  ],
  duo: [
    [[0, 0], [.62, 0], [.4, 1], [0, 1]],
    [[.62, 0], [1, 0], [1, 1], [.4, 1]],
  ],
  corner: [
    [[.62, .56], [.975, .54], [.975, .94], [.605, .94]],
  ],
});
// Which camera each panel uses, in panel order.
export const SHOTS = Object.freeze({ trio: ['low', 'chase', 'top'], duo: ['low', 'chase'], corner: ['low'] });

export const REPLAY = Object.freeze({
  lead: .62,        // replay start to panel 0's hit
  stagger: .46,     // each later panel hits this much after the one before
  tail: .95,        // after the last hit, before the panels leave
  exit: .32,        // the panels sliding back out, stagger included
  exitGap: .04,     // last panel leaves first, the next this much later
  exitSkip: .16,    // a skipped replay leaves all at once, this fast
  enter: .34,       // a panel sliding in from its own edge
  enterGap: .07,    // and the next one a beat behind
  overshoot: 1.35,  // how far past its seat a panel swings on arrival (easeOutBack c1)
  slow: .28,        // playback speed at the moment of contact
  slowWidth: .2,    // seconds either side of contact the slow-down spans
  preRoll: 1.6,     // the furthest back into the clip a panel will start
  lit: [-.06, .4],  // a panel is lit from just before its hit to just after
  dim: .56,         // brightness of the panels not taking their hit
  desat: .7,        // and how much colour they lose
  rest: .9,         // brightness when nobody is taking a hit
  flash: .3,        // white flash on a panel's own hit
  flashSec: .2,
  // the hit, per panel
  punch: .2,        // zoom pulse at contact (1 + punch)
  punchSec: .4,
  shake: .026,      // frame shake, fraction of the stage (owner, 2026-09-29: more)
  shakeSec: .42,
  shakeKin: .35,    // the other panels jolt this much with a neighbour's hit
  shakeHz: 23,
  chroma: 9,        // chromatic split at contact, px at the panel rim
  chromaSec: .32,
  burstSec: .46,    // speed lines / halftone ring rushing out
  wordSec: .5,      // the comic impact word
  // the frame
  wipeSec: .2,      // white wipe across a panel as it lands
  seamSec: .26,     // ink seams drawing on
});

export function panelCount(layout) { return (LAYOUTS[layout] || LAYOUTS.corner).length; }

/** Presentation time (from replay start) at which panel i takes its hit. */
export function hitAt(layout, i) { return REPLAY.lead + i * (layout === 'duo' ? REPLAY.stagger * 1.15 : REPLAY.stagger); }

/** Total presentation length, exit included. */
export function replayLength(layout) {
  return hitAt(layout, panelCount(layout) - 1) + REPLAY.tail + REPLAY.exit;
}

// Playback speed around a panel's hit, and its integral. x is presentation time
// relative to the panel's own hit; warp(x) is clip time relative to the clip's hit.
const speed = x => 1 - (1 - REPLAY.slow) * Math.exp(-((x / REPLAY.slowWidth) ** 2));
const WARP_STEP = 1 / 240, WARP_SPAN = 4;
const WARP = (() => {
  const n = Math.ceil(WARP_SPAN / WARP_STEP), table = new Float64Array(n + 1);
  let w = 0;
  for (let i = 1; i <= n; i++) { const x = (i - .5) * WARP_STEP; w += speed(x) * WARP_STEP; table[i] = w; }
  return table;
})();
export function warp(x) {
  const s = Math.sign(x), a = Math.abs(x);
  if (a >= WARP_SPAN) return s * (WARP[WARP.length - 1] + (a - WARP_SPAN));
  const f = a / WARP_STEP, i = Math.floor(f), t = f - i;
  return s * (WARP[i] + (WARP[i + 1] - WARP[i]) * t);   // speed is even, so warp is odd
}
export function speedAt(x) { return speed(x); }

/**
 * Clip time for panel i at presentation time t (seconds since the replay began).
 * `clip` = { duration, hit }.
 */
export function clipTime(layout, i, t, clip) {
  const r = clip.hit + warp(t - hitAt(layout, i));
  return Math.max(Math.max(0, clip.hit - REPLAY.preRoll), Math.min(clip.duration, r));
}

const clamp01 = v => Math.max(0, Math.min(1, v));
const easeOutBack = (t, c1 = 1.7) => { const c3 = c1 + 1; return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2; };
const easeInBack = (t, c1 = .8) => (c1 + 1) * t ** 3 - c1 * t ** 2;
const easeIn = t => t * t * t;
const easeOut = t => 1 - (1 - t) ** 3;

/** How long the panels take to leave: a skip is snappier. */
export function exitLength(skipped = false) { return skipped ? REPLAY.exitSkip : REPLAY.exit; }

/**
 * Where a panel comes from and goes back to: straight out from the stage centre
 * through its own centroid, far enough that every corner has left the stage.
 * Returns { dx, dy } unit direction and `dist` in stage units.
 */
export function slideOf(pts) {
  const cx = pts.reduce((a, p) => a + p[0], 0) / pts.length, cy = pts.reduce((a, p) => a + p[1], 0) / pts.length;
  let dx = cx - .5, dy = cy - .5;
  const len = Math.hypot(dx, dy) || 1;
  dx /= len; dy /= len;
  if (Math.hypot(cx - .5, cy - .5) < 1e-3) { dx = -1; dy = 0; }
  // the shortest travel that takes every corner off the stage, plus a hair
  let lo = 0, hi = 3;
  for (let k = 0; k < 30; k++) {
    const m = (lo + hi) / 2;
    if (offStage(pts.map(([x, y]) => [x + dx * m, y + dy * m]))) hi = m; else lo = m;
  }
  return { dx, dy, dist: hi + .03, cx, cy };
}

/** A polygon moved by (dx, dy) and scaled about its centroid. */
export function placePoly(pts, s = 1, ox = 0, oy = 0) {
  const { pts: sp, cx, cy } = scalePoly(pts, s);
  return { pts: sp.map(([x, y]) => [x + ox, y + oy]), cx: cx + ox, cy: cy + oy };
}
/** True when no part of the polygon is on the 0..1 stage. */
export function offStage(pts) {
  const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
  return Math.max(...xs) <= 0 || Math.min(...xs) >= 1 || Math.max(...ys) <= 0 || Math.min(...ys) >= 1;
}

/**
 * Everything a panel needs for one frame. `end` is when the exit began (early on
 * a skip). Motion: `ox, oy` offset of the whole panel (slide + shake, stage
 * units), `scale` about its centroid. Look: `light`, `desat`, `flash`, `zoom`
 * (picture punch), `chroma` (px), `burst` 0..1 or -1, `wipe` 0..1 or -1,
 * `seam` 0..1 (ink drawn), `word` { alpha, scale } for the impact word.
 */
export function panelState(layout, i, t, end = replayLength(layout) - REPLAY.exit, { portrait = false, skipped = false } = {}) {
  const n = panelCount(layout);
  const pts = orient((LAYOUTS[layout] || LAYOUTS.corner)[i], portrait);
  const sl = slideOf(pts);
  // ENTER: in from its own edge with a swing past the seat
  const e = clamp01((t - i * REPLAY.enterGap) / REPLAY.enter);
  let slide = 1 - easeOutBack(e, REPLAY.overshoot);
  let scale = .9 + .1 * easeOut(e);
  // EXIT: a little pull back, then out the way it came; a skip goes all at once, fast
  const exLen = exitLength(skipped);
  const gap = skipped ? 0 : REPLAY.exitGap;
  const dur = exLen - (n - 1) * gap;
  const x = t >= end ? clamp01((t - end - (n - 1 - i) * gap) / dur) : 0;
  if (x > 0) {
    slide += skipped ? easeIn(x) : easeInBack(x);
    scale *= 1 - .12 * easeIn(x);
  }
  const litOf = j => { const d = t - hitAt(layout, j); return d >= REPLAY.lit[0] && d <= REPLAY.lit[1] && hitAt(layout, j) < end; };
  let light = REPLAY.rest, desat = 0;
  if (n > 1) {
    if (litOf(i)) light = 1;
    else for (let j = 0; j < n; j++) if (j !== i && litOf(j)) { light = REPLAY.dim; desat = REPLAY.desat; break; }
  } else light = 1;
  // THE HIT, only if the replay reached it before any skip
  const h = t - hitAt(layout, i), hit = h >= 0 && hitAt(layout, i) < end;
  const decay = (sec, pow = 2) => (hit && h < sec ? (1 - h / sec) ** pow : 0);
  const flash = hit ? REPLAY.flash * Math.max(0, 1 - h / REPLAY.flashSec) : 0;
  const zoom = 1 + REPLAY.punch * decay(REPLAY.punchSec);
  const amp = REPLAY.shake * decay(REPLAY.shakeSec), w = h * Math.PI * 2 * REPLAY.shakeHz;
  let sx = amp * Math.sin(w + i * 1.7), sy = amp * Math.cos(w * 1.31 + i * 2.3);
  // a hit shakes the whole page a little: every other panel takes a smaller jolt
  for (let j = 0; j < n; j++) {
    if (j === i || hitAt(layout, j) >= end) continue;
    const hj = t - hitAt(layout, j);
    if (hj < 0 || hj >= REPLAY.shakeSec) continue;
    const aj = REPLAY.shake * REPLAY.shakeKin * (1 - hj / REPLAY.shakeSec) ** 2, wj = hj * Math.PI * 2 * REPLAY.shakeHz;
    sx += aj * Math.sin(wj + i * 2.9); sy += aj * Math.cos(wj * 1.17 + i * .7);
  }
  const chroma = REPLAY.chroma * decay(REPLAY.chromaSec, 1.5);
  const burst = hit && h < REPLAY.burstSec ? h / REPLAY.burstSec : -1;
  const word = hit && h < REPLAY.wordSec
    ? { alpha: 1 - clamp01((h - REPLAY.wordSec * .6) / (REPLAY.wordSec * .4)), scale: easeOutBack(clamp01(h / .14), 2.6) * (1 + .06 * (h / REPLAY.wordSec)) }
    : { alpha: 0, scale: 0 };
  // THE FRAME: a white wipe as it lands, ink drawing on, and back off on the way out
  const wt = (t - i * REPLAY.enterGap - REPLAY.enter * .5) / REPLAY.wipeSec;
  const wipe = wt >= 0 && wt <= 1 ? wt : -1;
  const seam = easeOut(clamp01((t - i * REPLAY.enterGap - .06) / REPLAY.seamSec)) * (1 - clamp01(x * 1.7));
  const ox = sl.dx * sl.dist * slide + sx, oy = sl.dy * sl.dist * slide + sy;
  return { scale, ox, oy, slide, dir: [sl.dx, sl.dy], light, desat, lit: n > 1 && litOf(i), flash, zoom, chroma, burst, wipe, seam, word };
}

/**
 * What the clock and the seat allow: 'full' (any layout), 'corner', or 'off'.
 * Online clocks belong to the server and never pause, so online is the corner.
 */
export function replayAllowed({ enabled = true, reduced = false, online = false, leastMs = Infinity } = {}) {
  if (!enabled || reduced || leastMs < 10000) return 'off';
  if (online || leastMs < 30000) return 'corner';
  return 'full';
}

/** A shuffled bag of the three layouts; never the same one twice in a row. */
export function createLayoutDeck(random = Math.random) {
  let bag = [], last = null;
  return (allowed = 'full') => {
    if (allowed === 'corner') return 'corner';
    if (!bag.length) {
      bag = ['trio', 'duo', 'corner'];
      for (let i = bag.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [bag[i], bag[j]] = [bag[j], bag[i]]; }
      if (bag[bag.length - 1] === last) [bag[0], bag[bag.length - 1]] = [bag[bag.length - 1], bag[0]];
    }
    last = bag.pop();
    return last;
  };
}

/**
 * The replay lens zooms with the action (owner, 2026-09-29: "play more with zoom"):
 * it creeps in over the run-up, crashes in at contact, and eases back out after.
 * x = clip time relative to the hit; returns a field-of-view multiplier (< 1 = closer).
 */
export const LENS = Object.freeze({ creep: .14, creepSec: .9, crash: .16, crashSec: .45, release: .9 });
export function lensZoom(x) {
  const run = x < 0 ? clamp01(1 + x / LENS.creepSec) : 1;
  const creep = LENS.creep * run * run * (3 - 2 * run);
  const crash = x >= 0 ? LENS.crash * Math.max(0, 1 - x / LENS.crashSec) ** 2 : 0;
  const out = x > 0 ? clamp01(x / LENS.release) : 0;
  return 1 - (creep * (1 - .6 * out * out * (3 - 2 * out)) + crash);
}

/** A panel scaled about its own centroid, for the punch in and out. */
export function scalePoly(pts, s) {
  const cx = pts.reduce((a, p) => a + p[0], 0) / pts.length, cy = pts.reduce((a, p) => a + p[1], 0) / pts.length;
  return { pts: pts.map(([x, y]) => [cx + (x - cx) * s, cy + (y - cy) * s]), cx, cy };
}
/** A portrait stage turns every layout on its side. */
export function orient(pts, portrait) { return portrait ? pts.map(([x, y]) => [y, x]) : pts; }
