/* ============================================================================
 * stamp.js - the mark every export carries.
 *
 * A small ground pill at the bottom centre reading "cclabs.app/remix" in the
 * display face, pink, opacity 0.9, six pixel inset at output resolution. It is
 * the only link a gif needs: no code on the picture (the code lives in the app
 * and a friend types it there). It cannot be removed or moved.
 *
 * `corner` and `code` are still accepted so old projects load; both are
 * ignored. Drawn with canvas text so it needs no asset at runtime.
 *
 * The stamp can also land: over the first five frames of a loop it presses on
 * from 1.35x and half lit to its rest pose (`stampPose`). Pure, seed free and
 * identical on the preview and the export. Off unless the render state asks
 * for it (see STAMP_LAND_DEFAULT in render.js).
 * ==========================================================================*/

export const CORNERS = ['br', 'bl', 'tr', 'tl'];
export const INSET = 6;
export const STAMP_TEXT = 'cclabs.app/remix';

/** The stamp at rest: full size, the opacity it has always had. */
export const REST_POSE = { scale: 1, alpha: 0.9 };
/** Frames the landing takes: 5 at 15 fps is 333 ms, the house 340. */
export const LAND_FRAMES = 4;

const clamp01 = u => (u < 0 ? 0 : u > 1 ? 1 : u);
// the same in-out curve effects/util.js uses, inlined so this module stays DOM free
const easeInOut = u => { const c = clamp01(u); return c < 0.5 ? 2 * c * c : 1 - Math.pow(-2 * c + 2, 2) / 2; };

/**
 * The pose the stamp holds on one frame. Pure: same frame, same pose, forever.
 * `frames` is the loop length; a loop shorter than a second never lands.
 */
export function stampPose(frame, frames) {
  const f = Math.round(Number(frame) || 0);
  if (!(frames >= 15) || f >= LAND_FRAMES) return REST_POSE;
  const t = easeInOut(clamp01(f / LAND_FRAMES));
  return { scale: 1.35 - 0.35 * t, alpha: 0.4 + 0.5 * t };
}

const PINK = '#FF69B4';
const GROUND = '#14142B';
const DISPLAY = "'Bahnschrift Condensed','Arial Narrow','Arial Black',Impact,sans-serif";

/** Kept for old callers; the stamp no longer moves. */
export function nextCorner(corner) {
  const i = CORNERS.indexOf(corner);
  return CORNERS[(i < 0 ? 0 : i + 1) % CORNERS.length];
}

function metrics(size, scale) {
  const k = scale || Math.max(0.8, Math.min(size.w, size.h) / 270);
  const fontPx = Math.round(9 * k);
  const pad = Math.round(6 * k);
  const h = Math.round(fontPx + Math.round(4 * k));
  return { k, fontPx, pad, h, inset: Math.round(INSET * k) };
}

/**
 * Draw the stamp.
 * opts: { size:{w,h}, scale, pose }  scale lifts it on a big preview, pose is
 * the landing (default: at rest). code / corner ignored.
 */
export function drawStamp(ctx, opts) {
  const size = opts.size;
  const pose = opts.pose || REST_POSE;
  const { k, fontPx, pad, h, inset } = metrics(size, opts.scale);

  ctx.save();
  ctx.globalAlpha = pose.alpha;
  ctx.textBaseline = 'middle';
  ctx.font = `700 ${fontPx}px ${DISPLAY}`;
  const wText = ctx.measureText(STAMP_TEXT).width;
  const w = Math.round(pad + wText + pad);
  const x = Math.round((size.w - w) / 2);
  const y = size.h - inset - h;
  // it grows from the pill's own centre, so the bottom inset stays where it is
  if (pose.scale !== 1) {
    const cx = x + w / 2, cy = y + h / 2;
    ctx.translate(cx, cy); ctx.scale(pose.scale, pose.scale); ctx.translate(-cx, -cy);
  }

  pill(ctx, x, y, w, h, h / 2);
  ctx.fillStyle = GROUND;
  ctx.globalAlpha = pose.alpha * 0.8;
  ctx.fill();
  ctx.globalAlpha = pose.alpha;
  ctx.lineWidth = Math.max(1, k);
  ctx.strokeStyle = 'rgba(255,105,180,0.45)';
  ctx.stroke();

  ctx.textAlign = 'left';
  ctx.fillStyle = PINK;
  ctx.fillText(STAMP_TEXT, x + pad, y + h / 2 + 0.5 * k);
  ctx.restore();
}

/** The box the stamp occupies at rest, so a caption can stay clear of it. */
export function stampRect(ctx, opts) {
  const size = opts.size;
  const { fontPx, pad, h, inset } = metrics(size, opts.scale);
  // no ctx in node: the text is about 7.4 em wide in the condensed face
  const w = Math.round(pad + fontPx * 7.4 + pad);
  return { x: Math.round((size.w - w) / 2), y: size.h - inset - h, w, h };
}

function pill(ctx, x, y, w, h, r) {
  const rr = Math.min(r, h / 2, w / 2);
  ctx.beginPath();
  ctx.moveTo(x + rr, y);
  ctx.lineTo(x + w - rr, y);
  ctx.arcTo(x + w, y, x + w, y + rr, rr);
  ctx.lineTo(x + w, y + h - rr);
  ctx.arcTo(x + w, y + h, x + w - rr, y + h, rr);
  ctx.lineTo(x + rr, y + h);
  ctx.arcTo(x, y + h, x, y + h - rr, rr);
  ctx.lineTo(x, y + rr);
  ctx.arcTo(x, y, x + rr, y, rr);
  ctx.closePath();
}
