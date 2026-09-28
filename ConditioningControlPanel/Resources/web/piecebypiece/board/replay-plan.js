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
  exit: .3,         // the panels shrinking away
  enter: .3,        // a panel punching in
  enterGap: .08,    // and the next one a beat behind
  slow: .28,        // playback speed at the moment of contact
  slowWidth: .2,    // seconds either side of contact the slow-down spans
  preRoll: 1.6,     // the furthest back into the clip a panel will start
  lit: [-.06, .4],  // a panel is lit from just before its hit to just after
  dim: .38,         // brightness of the panels not taking their hit
  rest: .9,         // brightness when nobody is taking a hit
  flash: .42,       // white flash on a panel's own hit
  flashSec: .2,
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
const easeOutBack = t => { const c1 = 1.7, c3 = c1 + 1; return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2; };
const easeIn = t => t * t * t;

/**
 * Everything a panel needs for one frame: its scale (entering and leaving), how
 * lit it is, and its own flash. `end` is when the exit began, if it was cut short.
 */
export function panelState(layout, i, t, end = replayLength(layout) - REPLAY.exit) {
  const n = panelCount(layout);
  const enter = easeOutBack(clamp01((t - i * REPLAY.enterGap) / REPLAY.enter));
  const leave = 1 - easeIn(clamp01((t - end - (n - 1 - i) * .04) / (REPLAY.exit - .08)));
  const litOf = j => { const x = t - hitAt(layout, j); return x >= REPLAY.lit[0] && x <= REPLAY.lit[1]; };
  let light = REPLAY.rest;
  if (n > 1) {
    if (litOf(i)) light = 1;
    else for (let j = 0; j < n; j++) if (j !== i && litOf(j)) { light = REPLAY.dim; break; }
  } else light = 1;
  const x = t - hitAt(layout, i);
  const own = x >= 0 ? REPLAY.flash * Math.max(0, 1 - x / REPLAY.flashSec) : 0;
  const entry = t >= i * REPLAY.enterGap ? .8 * Math.max(0, 1 - (t - i * REPLAY.enterGap) / .18) : 0;
  return { scale: Math.max(0, enter * leave), light, lit: n > 1 && litOf(i), flash: Math.max(own, entry) };
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

/** A panel scaled about its own centroid, for the punch in and out. */
export function scalePoly(pts, s) {
  const cx = pts.reduce((a, p) => a + p[0], 0) / pts.length, cy = pts.reduce((a, p) => a + p[1], 0) / pts.length;
  return { pts: pts.map(([x, y]) => [cx + (x - cx) * s, cy + (y - cy) * s]), cx, cy };
}
/** A portrait stage turns every layout on its side. */
export function orient(pts, portrait) { return portrait ? pts.map(([x, y]) => [y, x]) : pts; }
