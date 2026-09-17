/* ============================================================================
 * slice-art.js - THE WEDGES CARRY A PICTURE.
 *
 * The owner's ask (2026-09-16): "the wheel might use custom slices, probably
 * showing a fraction of a gif there, animated, but we need to keep the text
 * visible over them."
 *
 * Each wedge wears its own dealt picture, cover-fitted to the wedge bounds.
 * All wedges share one canvas texture and the deck's bounded decode queue.
 * Pictures stay assigned through a spin, independent of the winning result.
 *
 * THE TEXT IS THE CONSTRAINT, not the decoration, and the wheel has to still be
 * a wheel. Three passes, and between them the result is bounded at BOTH ends:
 *
 *   1. the slice's own enamel colour, painted FIRST and opaque. It is the floor.
 *   2. the picture over it, at 0.72. The first try MULTIPLIED the enamel over the
 *      art, which took a dark frame all the way to black and stopped the wheel
 *      being a wheel at all; the second tried SCREEN, which cannot darken but
 *      also cannot show a dark clip. Laying it down at 0.72 shows the picture
 *      and still leaves every wedge a quarter of its own colour, so the ring keeps
 *      its slice-by-slice identity instead of becoming one screen.
 *   3. a radial SCRIM BAND on the label's own radius (0.475, where prize-art.js
 *      hangs the label plane), feathered to nothing at the hub and at the rim so
 *      the art still reads as art where nobody is reading words.
 *
 * A black frame is a very dark plum wedge, not a hole. A 255-white frame is taken
 * back down by the band where the words are. The label's cream (#FFF3CE,
 * prize-art.js, with its own dark stroke and shadow) carries both ends.
 *
 * Pure canvas: no three, no DOM beyond a 2D context, so it is unit-testable and
 * the scene keeps all the WebGL. The caller owns the clock and the media.
 * ==========================================================================*/

/** 12 Hz, matched to gif-decode.js MAX_FPS: repainting faster cannot show a new pixel. */
export const FACE_MS = 83;
/** The runtime slice ring's radius band, the same two numbers prize-art.js sectorPoints defaults to. */
export const R0 = 0.185, R1 = 0.711;
/** prize-art.js hangs the label plane at r 0.475 and it is 0.38 tall; the band covers it and feathers out. */
const LABEL_R = 0.475, LABEL_REACH = 0.27;
const SCRIM = '26, 12, 38';          // the room's own ink, so the band reads as lacquer and not as grey
const ART_ALPHA = 0.72, SCRIM_ALPHA = 0.5, SHEEN_ALPHA = 0.22;

/** The wedge as a canvas path. Wheel angles run CLOCKWISE FROM THE TOP; canvas arcs run anticlockwise from
 *  the +x axis, so the whole ring is simply turned a quarter: t = a - PI/2, drawn forwards. */
export function wedgePath(ctx, s, cx, cy, k) {
  const gap = Math.min(0.003, s.span * 0.08);      // the same gap the enamel wedge leaves (prize-art.js)
  const t0 = s.start + gap - Math.PI / 2, t1 = s.end - gap - Math.PI / 2;
  ctx.beginPath();
  ctx.arc(cx, cy, R1 * k, t0, t1, false);
  ctx.arc(cx, cy, R0 * k, t1, t0, true);
  ctx.closePath();
}

/**
 * Paint the whole ring. `media` is a deck from shared/hypno/media.js (draw/tick/setStill); `key` is an array of its
 * keys, assigned once per layout so the picture does not change under the player mid-spin. `colorOf(slice)` is
 * prize-art.js prizeColor. Returns true when a picture actually landed: false means the deck has nothing yet,
 * and the caller keeps the enamel it already has rather than flashing an empty ring.
 */
export function drawFace(canvas, layout, media, key, colorOf, now = 0) {
  const ctx = canvas && canvas.getContext && canvas.getContext('2d');
  if (!ctx || !Array.isArray(layout) || !layout.length) return false;
  const S = canvas.width;
  ctx.clearRect(0, 0, S, S);
  if (!media || !key) return false;
  const cx = S / 2, cy = S / 2, k = (S / 2) / R1, R = R1 * k;
  // Both of these live in DISC space, not wedge space, so they are the same for every slice: built once, and
  // the ring reads as one lacquered face rather than as N separately shaded pie pieces.
  const band = ctx.createRadialGradient(cx, cy, (LABEL_R - LABEL_REACH) * k, cx, cy, (LABEL_R + LABEL_REACH) * k);
  band.addColorStop(0, `rgba(${SCRIM}, 0)`);
  band.addColorStop(0.28, `rgba(${SCRIM}, ${SCRIM_ALPHA})`);
  band.addColorStop(0.72, `rgba(${SCRIM}, ${SCRIM_ALPHA})`);
  band.addColorStop(1, `rgba(${SCRIM}, 0)`);
  // Moving lacquer catches the light even when a dealt image is a still. Labels stay above this pass.
  const drift = Math.sin(now * 0.00055) * R * 0.7;
  const sheen = ctx.createLinearGradient(cx - R + drift, cy - R, cx + R + drift, cy + R);
  sheen.addColorStop(0, 'rgba(255, 255, 255, 0)');
  sheen.addColorStop(0.4, `rgba(255, 255, 255, ${SHEEN_ALPHA})`);
  sheen.addColorStop(0.62, `rgba(255, 255, 255, ${SHEEN_ALPHA * 0.25})`);
  sheen.addColorStop(1, 'rgba(255, 255, 255, 0)');
  let painted = false;
  for (const [index, s] of layout.entries()) {
    ctx.save();
    wedgePath(ctx, s, cx, cy, k);
    ctx.clip();
    ctx.fillStyle = colorOf(s);
    ctx.fill();                                  // the enamel, opaque: the wedge's colour is its floor
    // Fit the image to this wedge, then let its curved clip crop the corners.
    const box = sliceBounds(s, cx, cy, k);
    ctx.globalAlpha = ART_ALPHA;
    if (media.draw(ctx, Array.isArray(key) ? key[index] : key, box.x, box.y, box.w, box.h)) painted = true;
    ctx.globalAlpha = 1;
    // draw() begins its own path, and a path is not part of the state save() keeps, so the wedge is laid again.
    wedgePath(ctx, s, cx, cy, k);
    ctx.fillStyle = band;
    ctx.fill();
    // The lacquer sheen. The enamel underneath carries a baked vertex glint (prize-art.js) that an opaque face
    // would cover, so the face brings its own and the wedges keep looking raised rather than printed.
    ctx.fillStyle = sheen;
    ctx.fill();
    ctx.restore();
  }
  return painted;
}

/** The picture a layout wears. Its own seed, never the result's: the fx picture key is a different question
 *  and wheel-check.mjs asserts that one separately. */
export const faceKey = (media, layout) =>
  (media && Array.isArray(layout) && layout.length ? media.pickKey(layout.map(s => s.id).join('|')) : null);

/** Exact annular-sector bounds, including extrema between the endpoints. */
export function sliceBounds(s, cx, cy, k) {
  const angles = [s.start, s.end];
  const q = Math.PI / 2;
  for (let a = Math.ceil(s.start / q) * q; a < s.end; a += q) angles.push(a);
  const points = angles.flatMap(a => [R0, R1].map(r => [cx + Math.sin(a) * r * k, cy - Math.cos(a) * r * k]));
  const xs = points.map(p => p[0]), ys = points.map(p => p[1]);
  const x = Math.min(...xs), y = Math.min(...ys);
  return { x, y, w: Math.max(...xs) - x, h: Math.max(...ys) - y };
}

export function faceKeys(media, layout) {
  if (!media || !layout?.length) return null;
  return layout.map((_, i) => media.keyAt(i));
}
