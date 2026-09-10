/* ============================================================================
 * board/frame.js - keeping the whole board in the picture on any screen.
 *
 * The camera presets were tuned on a wide window: radius 12, a 40 degree
 * lens. Stand a phone up and the picture is half as wide as it is tall, the
 * horizontal field drops to about 19 degrees, and no preset can see the a
 * and h files at once. Two pure numbers put that right, and both come out as
 * exactly 1 (no change at all) on the wide window the game was tuned on:
 *
 *   fovForAspect(aspect)          the vertical lens angle: opens on a narrow
 *                                 screen so the horizontal field never drops
 *                                 under hfovHalfDeg, capped at fovMax so the
 *                                 perspective never goes fish-eye;
 *   fitScaleFor(aspect, fovDeg)   how much further back every preset stands
 *                                 so a board `reach` wide fits the horizontal
 *                                 field with a margin, relative to the radius
 *                                 the presets were tuned at.
 *
 * No three in here, so smoke/room-smoke.mjs can pin the numbers.
 * ==========================================================================*/

const DEG = Math.PI / 180;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));

/** Every number the framing is made of. One place, on purpose. */
export const FRAME = Object.freeze({
  reach: 5.9,          // half the board's width as seen corner-on: the plinth
                       // is 4.75 to its edge, and the rim coordinates sit on it
  base: 12,            // the radius the presets were tuned at (11.5 .. 12.6)
  margin: 1.06,        // a little air either side of the a and h files
  hfovHalfDeg: 24,     // the horizontal half-field the lens will not drop under
  fovMin: 40,          // the lens the game was tuned with, and its floor
  fovMax: 62,          // wider than this and the men at the near edge distort
});

/** The vertical field of view, in degrees, for a viewport `aspect` wide/tall. */
export function fovForAspect(aspect, F = FRAME) {
  const a = Math.max(0.2, Number(aspect) || 1);
  const want = (2 * Math.atan(Math.tan(F.hfovHalfDeg * DEG) / a)) / DEG;
  return clamp(want, F.fovMin, F.fovMax);
}

/** The factor every preset radius is multiplied by so the board fits. >= 1. */
export function fitScaleFor(aspect, fovDeg, F = FRAME) {
  const a = Math.max(0.2, Number(aspect) || 1);
  const half = Math.atan(Math.tan((Number(fovDeg) || F.fovMin) * DEG * 0.5) * a);
  const needed = (F.margin * F.reach) / Math.max(1e-3, Math.tan(half));
  return Math.max(1, needed / F.base);
}
