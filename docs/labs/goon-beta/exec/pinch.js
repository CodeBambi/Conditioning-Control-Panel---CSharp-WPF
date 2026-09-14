/* ============================================================================
 * exec/pinch.js — TWO-FINGER PINCH, as arithmetic.
 *
 * The problem it exists for: on a phone the opponent's payloads open media you
 * cannot inspect. A video window is born at ~22vw and a flash at 43.7vmin, and a
 * touch device has no wheel — so every resize affordance exec/ ships (videos.js
 * onWheel, flashes.js onWheel) is a DESKTOP-ONLY affordance. Pinch is the mobile
 * half of exactly that gesture, and it deliberately drives the SAME size vars
 * (--gg-vwin-w, --gg-flash-size) rather than stacking a transform scale, because
 * transform in both modules is already spoken for by the drag offset (and, in
 * flashes, the tilt and the hold lift).
 *
 * EVERYTHING HERE IS PURE. No DOM, no globals, no clock — one exception, and it
 * is quarantined below: safeInsets() reads the browser's safe-area insets and is
 * the only function in this file that touches a document. It is called at pinch
 * START (never per move), it caches, and it answers zeroes on any host that
 * cannot tell it — which is what makes this module import-safe under plain node
 * and assertable without a browser.
 *
 * UNIT-AGNOSTIC ON PURPOSE. clampSize() speaks "size", not px: videos.js sizes in
 * px and flashes.js sizes in vmin, and neither one should have to convert to
 * share a clamp. Only the viewport cap is inherently px, so pxToVmin() is the one
 * bridge and flashes.js is its only caller.
 *
 * THE GESTURE MODEL the two callers implement on top of this (documented here
 * because it is the same in both, and diverging would be the bug):
 *
 *   · TOUCH ONLY. Both pointers must be `touch`/`pen` — a mouse never pinches,
 *     so desktop behaviour is bit-for-bit what it was.
 *   · The SECOND finger on the surface the FIRST one is already pressing starts
 *     it. A second finger anywhere else is ignored exactly as it always was.
 *   · A pinch GRABS first (the surface leaves the keyframe world: drift frozen,
 *     offset folded, JS owns the transform) so the geometry underneath is a
 *     number and not an animation.
 *   · Below PINCH_SLOP_PX of spread change nothing happens: two fingers resting
 *     is not a gesture, and a size that twitched on touchdown reads as a bug.
 *   · Scale is anchored on the MIDPOINT — the pixel between the fingers stays
 *     between the fingers (zoomAbout) — and the midpoint's own travel pans the
 *     surface (panDelta). That is the whole "drag while zoomed", and it costs
 *     nothing because it falls out of the same two points.
 *   · The result is CLAMPED into the safe viewport (clampRect), so a pinched-open
 *     window can never be pushed off the right edge — the exact overflow the
 *     mobile HUD rework was fixing.
 *   · Lifting EITHER finger ends the whole gesture, and the release is a CANCEL:
 *     a pinch must never post the click that a one-finger press would (a mute on
 *     a video window, a hydra split on a flash).
 *
 * PURELY LOCAL PRESENTATION. Nothing here is on the wire, in a receipt, or in a
 * payload frame; a pinch changes what this player sees and nothing else at all.
 * ==========================================================================*/

/* --- the dials. Exported because the self-test asserts against these numbers
   rather than re-typing them. ---------------------------------------------- */
export const PINCH_MIN_FACTOR = 0.5;   // never smaller than half its born size
export const PINCH_MAX_FACTOR = 3;     // …nor more than 3x it (the wheel stops at 2.5)
export const PINCH_SLOP_PX = 10;       // spread must change this much before it is a pinch
export const PINCH_EDGE_PAD_PX = 8;    // a pinched-open surface never sits flush to the edge
export const PINCH_KEEP_PX = 64;       // this much of a panned surface stays on screen
export const PINCH_RATIO_MIN = 0.05;   // a degenerate distance can never explode the maths
export const PINCH_RATIO_MAX = 20;

const num = (v, dflt) => (typeof v === 'number' && v === v && v !== Infinity && v !== -Infinity ? v : dflt);
const clamp = (v, lo, hi) => (v < lo ? lo : (v > hi ? hi : v));

/** Two pointers of these types pinch; a mouse never does. */
export function isPinchPointer(type) {
  return type === 'touch' || type === 'pen';
}

/** Both halves of the gesture must be fingers, and they must be DIFFERENT ones. */
export function pinchEligible(aType, bType, aId, bId) {
  if (!isPinchPointer(aType) || !isPinchPointer(bType)) return false;
  if (aId == null || bId == null) return false;
  return aId !== bId;
}

/** Finger spread, in px. */
export function pinchDistance(ax, ay, bx, by) {
  const dx = num(bx, 0) - num(ax, 0);
  const dy = num(by, 0) - num(ay, 0);
  return Math.hypot(dx, dy);
}

/** The pixel the gesture is anchored on. */
export function pinchMidpoint(ax, ay, bx, by) {
  return { x: (num(ax, 0) + num(bx, 0)) / 2, y: (num(ay, 0) + num(by, 0)) / 2 };
}

/** Has the spread moved far enough to be a pinch rather than two resting fingers? */
export function pinchStarted(startDist, dist, slop) {
  const s = num(slop, PINCH_SLOP_PX);
  return Math.abs(num(dist, 0) - num(startDist, 0)) > s;
}

/**
 * spread -> scale. Guarded at both ends: a zero (or absurd) start distance is a
 * host telling us nothing useful, and 1 — "leave it exactly as it is" — is always
 * the safe answer to that.
 */
export function pinchRatio(startDist, dist) {
  const a = num(startDist, 0);
  const b = num(dist, 0);
  if (!(a > 0) || !(b > 0)) return 1;
  return clamp(b / a, PINCH_RATIO_MIN, PINCH_RATIO_MAX);
}

/** Insets in the shape the rest of this file wants, from anything at all. */
function normInsets(i) {
  const o = i || {};
  return {
    top: Math.max(0, num(o.top, 0)),
    right: Math.max(0, num(o.right, 0)),
    bottom: Math.max(0, num(o.bottom, 0)),
    left: Math.max(0, num(o.left, 0)),
  };
}

/**
 * THE HARD CEILING: the widest a surface of this aspect may be drawn without
 * leaving the safe viewport. `aspect` is HEIGHT / WIDTH (9/16 for a video window,
 * 1 for a flash's square box); 0 or missing means "square".
 *
 * The insets are the phone's, the pad is ours — a window whose edge is exactly
 * the screen's edge reads as clipped even when it is not.
 */
export function viewportCap(vw, vh, insets, aspect, pad) {
  const W = num(vw, 0) > 0 ? vw : 0;
  const H = num(vh, 0) > 0 ? vh : 0;
  if (!(W > 0) || !(H > 0)) return 0;               // no viewport, no ceiling
  const i = normInsets(insets);
  const p = Math.max(0, num(pad, PINCH_EDGE_PAD_PX));
  const availW = Math.max(40, W - i.left - i.right - p * 2);
  const availH = Math.max(40, H - i.top - i.bottom - p * 2);
  const a = num(aspect, 0) > 0 ? aspect : 1;
  return Math.min(availW, availH / a);
}

/**
 * The size a pinch is allowed to land on. Floor and ceiling are FACTORS of the
 * surface's own born size (so a flash that hatched small still zooms a sensible
 * amount), and the viewport cap outranks BOTH — including the floor, because
 * "never bigger than the screen" is the promise that keeps a pinched window from
 * overflowing the right edge on a phone.
 *
 * @param {number} want    the unclamped size the ratio asked for
 * @param {number} base    the surface's born size, same unit
 * @param {number} [cap]   the viewport ceiling, same unit; <=0 = none
 */
export function clampSize(want, base, cap, minFactor, maxFactor) {
  const b = num(base, 0);
  if (!(b > 0)) return Math.max(0, num(want, 0));
  let lo = b * Math.max(0, num(minFactor, PINCH_MIN_FACTOR));
  let hi = b * Math.max(0, num(maxFactor, PINCH_MAX_FACTOR));
  if (hi < lo) hi = lo;
  const c = num(cap, 0);
  if (c > 0) {
    if (hi > c) hi = c;
    if (lo > hi) lo = hi;                            // a screen smaller than the floor wins
  }
  return clamp(num(want, lo), lo, hi);
}

/** px -> vmin, for the one caller that sizes in vmin. 0 on a host without a box. */
export function pxToVmin(px, vw, vh) {
  const W = num(vw, 0) > 0 ? vw : 0;
  const H = num(vh, 0) > 0 ? vh : 0;
  const m = Math.min(W, H);
  if (!(m > 0)) return 0;
  return (num(px, 0) / m) * 100;
}

/** How far the fingers carried the surface between two samples. */
export function panDelta(midX0, midY0, midX1, midY1) {
  return { x: num(midX1, 0) - num(midX0, 0), y: num(midY1, 0) - num(midY0, 0) };
}

/**
 * Scale about a fixed point: the pixel under `(mx,my)` stays under `(mx,my)`.
 * `(x,y)` is any corner of the surface (top-left for a video window, the CENTRE
 * for a flash — the maths does not care which, only that the caller is
 * consistent), `k` the ratio between the new size and the old one.
 */
export function zoomAbout(x, y, mx, my, k) {
  const s = num(k, 1);
  const px = num(mx, 0), py = num(my, 0);
  return { x: px - (px - num(x, 0)) * s, y: py - (py - num(y, 0)) * s };
}

/**
 * KEEP IT ON SCREEN — the promise that a pinched-open window cannot end up
 * hanging off the right edge of a phone, which is the exact overflow the mobile
 * HUD rework spent its afternoon on.
 *
 * TWO RULES, and which one applies is a fact about the surface, not a policy:
 *   · it FITS in the safe box (the common case — clampSize's cap guarantees it
 *     for anything a pinch produced): it is held WHOLLY inside. No overflow, on
 *     any edge, at any zoom.
 *   · it does NOT fit (a wheel-grown desktop window, a surface bigger than a
 *     short landscape viewport): containment is impossible, so the weaker
 *     promise applies — at least `keep` px of it stays reachable on every side.
 * A surface smaller than `keep` is held wholly inside either way: clamping to
 * "64px of a 40px window" would let it leave the screen entirely.
 */
export function clampRect(x, y, w, h, vw, vh, insets, keep) {
  const W = num(vw, 0) > 0 ? vw : 0;
  const H = num(vh, 0) > 0 ? vh : 0;
  const rx = num(x, 0), ry = num(y, 0);
  if (!(W > 0) || !(H > 0)) return { x: rx, y: ry };
  const i = normInsets(insets);
  const rw = Math.max(0, num(w, 0));
  const rh = Math.max(0, num(h, 0));
  const k = Math.max(0, num(keep, PINCH_KEEP_PX));
  const availW = Math.max(0, W - i.left - i.right);
  const availH = Math.max(0, H - i.top - i.bottom);

  let minX, maxX, minY, maxY;
  if (rw <= availW) { minX = i.left; maxX = i.left + availW - rw; }
  else { const kx = Math.min(k, rw); minX = i.left + kx - rw; maxX = W - i.right - kx; }
  if (rh <= availH) { minY = i.top; maxY = i.top + availH - rh; }
  else { const ky = Math.min(k, rh); minY = i.top + ky - rh; maxY = H - i.bottom - ky; }
  if (maxX < minX) maxX = minX;
  if (maxY < minY) maxY = minY;
  return { x: clamp(rx, minX, maxX), y: clamp(ry, minY, maxY) };
}

/**
 * ONE PINCH STEP, end to end, as a pure function — this is what both callers
 * actually run per pointermove, and the reason there is no gesture arithmetic
 * left in either DOM module.
 *
 * @param {object} g the gesture: {startDist, startSize, base, midX, midY,
 *        anchorX, anchorY, size} — anchor is the surface's current top-left
 *        (videos) or centre (flashes), `size` its current size in ITS unit.
 * @param {object} pt where the fingers are NOW: {ax, ay, bx, by}
 * @param {object} box the world: {vw, vh, insets, aspect, cap, keep, centred,
 *        pxPerUnit} — `cap` is the size ceiling in the surface's unit
 *        (viewportCap converted by the caller), `pxPerUnit` turns that unit into
 *        px for the on-screen clamp (1 for a px-sized window), `aspect` is
 *        height/width, `centred` true when the anchor is a centre.
 * @returns {{size:number, x:number, y:number, midX:number, midY:number, ratio:number}}
 */
export function pinchStep(g, pt, box) {
  const s = g || {};
  const p = pt || {};
  const b = box || {};
  const dist = pinchDistance(p.ax, p.ay, p.bx, p.by);
  const mid = pinchMidpoint(p.ax, p.ay, p.bx, p.by);
  const ratio = pinchRatio(s.startDist, dist);
  const cur = num(s.size, 0) > 0 ? s.size : num(s.startSize, 0);
  const size = clampSize(num(s.startSize, cur) * ratio, num(s.base, cur), num(b.cap, 0),
    num(b.minFactor, PINCH_MIN_FACTOR), num(b.maxFactor, PINCH_MAX_FACTOR));
  const k = cur > 0 ? size / cur : 1;

  // 1. the fingers carried it; 2. it grew/shrank about where they are now.
  const pan = panDelta(s.midX, s.midY, mid.x, mid.y);
  const moved = zoomAbout(num(s.anchorX, 0) + pan.x, num(s.anchorY, 0) + pan.y, mid.x, mid.y, k);

  // 3. and it stays on screen. A centred anchor is clamped as the rect it implies.
  const aspect = num(b.aspect, 0) > 0 ? b.aspect : 1;
  const perUnit = num(b.pxPerUnit, 0) > 0 ? b.pxPerUnit : 1;
  const wPx = size * perUnit;
  const hPx = wPx * aspect;
  const centred = !!b.centred;
  const rect = clampRect(centred ? moved.x - wPx / 2 : moved.x, centred ? moved.y - hPx / 2 : moved.y,
    wPx, hPx, b.vw, b.vh, b.insets, num(b.keep, PINCH_KEEP_PX));
  return {
    size,
    ratio,
    x: centred ? rect.x + wPx / 2 : rect.x,
    y: centred ? rect.y + hPx / 2 : rect.y,
    midX: mid.x,
    midY: mid.y,
  };
}

/* ===========================================================================
 * THE ONE IMPURE FUNCTION. Quarantined here, at the bottom, so the rest of the
 * file stays assertable without a browser.
 *
 * There is no way to read env(safe-area-inset-*) from script, so we ask the
 * layout engine the only way it answers: a throwaway fixed node whose padding IS
 * the four insets. It is created, measured and removed inside one call, and the
 * answer is cached against the viewport size (a rotation changes both, which is
 * exactly when the insets change too).
 *
 * Every failure mode — no document, no getComputedStyle (node), a host that has
 * never heard of env() — lands on the same zeroes, and zeroes are the correct
 * answer for every desktop this app has ever run on.
 * ========================================================================= */
const ZERO_INSETS = { top: 0, right: 0, bottom: 0, left: 0 };
let insetCache = null;      // {key, value}

/** @returns {{top:number,right:number,bottom:number,left:number}} never null */
export function safeInsets(vw, vh) {
  const key = `${Math.round(num(vw, 0))}x${Math.round(num(vh, 0))}`;
  if (insetCache && insetCache.key === key) return insetCache.value;
  let out = ZERO_INSETS;
  try {
    if (typeof document !== 'undefined' && document && document.body
        && typeof document.createElement === 'function'
        && typeof getComputedStyle === 'function') {
      const probe = document.createElement('div');
      probe.style.setProperty('position', 'fixed');
      probe.style.setProperty('top', '0');
      probe.style.setProperty('left', '0');
      probe.style.setProperty('width', '0');
      probe.style.setProperty('height', '0');
      probe.style.setProperty('visibility', 'hidden');
      probe.style.setProperty('pointer-events', 'none');
      probe.style.setProperty('padding-top', 'env(safe-area-inset-top, 0px)');
      probe.style.setProperty('padding-right', 'env(safe-area-inset-right, 0px)');
      probe.style.setProperty('padding-bottom', 'env(safe-area-inset-bottom, 0px)');
      probe.style.setProperty('padding-left', 'env(safe-area-inset-left, 0px)');
      document.body.appendChild(probe);
      const cs = getComputedStyle(probe);
      out = normInsets({
        top: parseFloat(cs.paddingTop),
        right: parseFloat(cs.paddingRight),
        bottom: parseFloat(cs.paddingBottom),
        left: parseFloat(cs.paddingLeft),
      });
      try { probe.remove(); } catch (_e) { /* ignore */ }
    }
  } catch (_e) { out = ZERO_INSETS; }
  insetCache = { key, value: out };
  return out;
}

/** Test seam: forget what the last measurement said. */
export function resetSafeInsets() {
  insetCache = null;
}
