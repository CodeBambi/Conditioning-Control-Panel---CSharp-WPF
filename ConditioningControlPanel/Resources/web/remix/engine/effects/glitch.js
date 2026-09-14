/* ============================================================================
 * effects/glitch.js - a ghost over the picture, not noise.
 *
 * The house glitch is the DtRH one: a second loop laid over the whole frame,
 * faded, cover fitted, ramping in fast, holding, then leaving. Two loops share
 * one screen and the eye cannot decide which one it is watching.
 *
 *   ghost   another gif from the strip, over the whole target rect
 *   double  the picture underneath, blown to twice its size, over itself
 *   tear    the old cut, bands of the picture shoved sideways. Off the panel
 *           and out of the dice since 2026-09-07; kept here so a saved remix
 *           that has one still plays.
 *
 * The colour pop is a second draw under soft-light plus a thin pink wash,
 * never `ctx.filter`, because iOS Safari has no canvas filter. Two drawImage
 * calls a frame and one fillRect, no per pixel work.
 * ==========================================================================*/

import { hash01 } from '../rng.js';
import { coverFit } from '../layout.js';
import { channelCopy, knob, clamp, smoothstep, rgba } from './util.js';

/** `params.source` when the seed still owns the choice. */
export const DICE = 'dice';

/** Peak opacity for the strength knob: 0..100 -> about 0.12..0.6, 50 -> ~0.31. */
export function peakAlpha(params) {
  const k = knob(params, 'strength', 50);
  return 0.12 + 0.48 * Math.pow(k, 1.35);
}

/**
 * The DtRH life curve: in over the first 8 percent of the block, hold, then
 * out over the last 14, so a long ghost breathes instead of sitting flat.
 */
export function lifeAt(t) {
  const u = clamp(t, 0, 1);
  const IN = 0.08;
  const OUT = 0.14;
  if (u < IN) return smoothstep(u / IN);
  if (u > 1 - OUT) return smoothstep((1 - u) / OUT);
  return 1;
}

/**
 * What the ghost is worth on one frame of one block, 0 when the frame sits
 * outside the block. Pure, so the shape can be tested without a canvas.
 */
export function ghostAlpha(block, frame) {
  const len = Math.max(1, (block.end | 0) - (block.start | 0));
  if (frame < block.start || frame >= block.end) return 0;
  const peak = peakAlpha(block.params);
  if (len <= 1) return peak;
  const t = clamp((frame - block.start) / (len - 1), 0, 1);
  return peak * lifeAt(t) * rampOf(len, frame - block.start);
}

/** The block's own 3 frame ramp, repeated here so ghostAlpha stays pure. */
function rampOf(len, k) {
  const r = Math.max(1, Math.min(3, Math.floor(len / 2)));
  const inU = Math.min(1, (k + 1) / (r + 1));
  const outU = Math.min(1, (len - k) / (r + 1));
  return smoothstep(Math.min(inU, outU));
}

/**
 * Which loop the ghost borrows.
 *
 * `params.source` is the user's own answer: `dice` (or nothing, which is what
 * every project saved before this existed says) leaves it to the seed, and a
 * media id pins it to that gif for good, so a reroll no longer moves it. If
 * that gif leaves the strip the id finds nothing and the dice takes over
 * again, which is also what an old project does.
 *
 * The dice picks from the strip minus whatever sits under this block, using
 * `params.pick`, which the roll and the panel dice write. Null when there is
 * nothing to borrow, and then `ghost` falls back to the `double` look.
 */
export function ghostSource(block, env) {
  const all = (env && env.strips) || [];
  const underId = env && env.underMedia ? env.underMedia.id : null;
  const params = (block && block.params) || {};
  if (params.source && params.source !== DICE) {
    const chosen = all.find((m) => m && m.id === params.source && m.strip && m.strip.length);
    if (chosen) return chosen;
  }
  const pool = all.filter((m) => m && m.strip && m.strip.length && m.id !== underId);
  if (!pool.length) return null;
  const p = params.pick;
  const n = Number.isFinite(p)
    ? Math.abs(Math.round(p))
    : Math.floor(hash01(env.seed, 0, 7) * 997);
  return pool[n % pool.length];
}

export function render(ctx, src, block, t, env) {
  if (block.mode === 'tear') return tear(ctx, src, block, env);

  const rect = env.rect;
  const alpha = ghostAlpha(block, env.frame);
  if (alpha <= 0.004) return;

  const media = block.mode === 'double' ? null : ghostSource(block, env);
  const img = media
    ? media.strip[((env.frame % media.strip.length) + media.strip.length) % media.strip.length]
      || media.strip[0]
    : null;

  ctx.save();
  if (img) {
    // cover fit the borrowed loop across the whole target rect
    const fit = coverFit(media.w || img.width, media.h || img.height, rect.w, rect.h);
    twice(ctx, img, null, rect.x + fit.x, rect.y + fit.y, fit.w, fit.h, alpha);
  } else if (src) {
    // `double`, or a strip with nothing to lend: the picture underneath at 2x
    twice(ctx, src, rect, rect.x - rect.w / 2, rect.y - rect.h / 2, rect.w * 2, rect.h * 2, alpha);
  }
  if (img || src) pinkWash(ctx, rect, alpha);
  ctx.restore();
}

/** The ghost, then the same ghost under soft-light: colour and contrast lift. */
function twice(ctx, img, srcRect, x, y, w, h, alpha) {
  const draw = () => {
    if (srcRect) ctx.drawImage(img, srcRect.x, srcRect.y, srcRect.w, srcRect.h, x, y, w, h);
    else ctx.drawImage(img, x, y, w, h);
  };
  ctx.globalCompositeOperation = 'source-over';
  ctx.globalAlpha = alpha;
  draw();
  ctx.globalCompositeOperation = 'soft-light';
  ctx.globalAlpha = alpha * 0.55;
  draw();
}

/** A few percent of pink over the lot: the hue shift without a filter. */
function pinkWash(ctx, rect, alpha) {
  ctx.globalCompositeOperation = 'overlay';
  ctx.globalAlpha = 1;
  ctx.fillStyle = rgba('#FF69B4', Math.min(0.09, alpha * 0.22));
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.globalCompositeOperation = 'source-over';
}

/* ------------------------------------------------------------------ tear -*/
/* The old glitch. No chip offers it any more and no roll produces one, but the
 * renderer keeps it for saved remixes. Most frames get nothing; the frames that
 * do get one to three hard edged bands shoved sideways. */

function tear(ctx, src, block, env) {
  if (!src) return;
  const s = knob(block.params, 'strength', 50) * env.ramp;
  if (s <= 0.02) return;
  const rect = env.rect;
  const f = env.frame;
  const seed = env.seed;

  // does this frame tear at all
  if (hash01(seed, f, 1) > 0.28 + 0.55 * s) return;

  const bands = 1 + Math.floor(hash01(seed, f, 2) * (1 + Math.round(s * 2)));
  for (let i = 0; i < bands; i++) {
    const r1 = hash01(seed, f, 10 + i);
    const r2 = hash01(seed, f, 20 + i);
    const r3 = hash01(seed, f, 30 + i);
    // most tears are hairlines; a couple are wide. Both sharp.
    const thin = r2 < 0.55;
    const bh = thin
      ? clamp(Math.round(2 + r2 * 9), 2, Math.round(rect.h))
      : clamp(Math.round(rect.h * (0.05 + r2 * 0.12 * (0.4 + s))), 3, Math.round(rect.h));
    const by = rect.y + Math.floor(r1 * (rect.h - bh));
    tearBand(ctx, src, rect, by, bh, r3, s, env, seed, f, i);
  }
}

function tearBand(ctx, src, rect, by, bh, r, s, env, seed, f, i) {
  const dx = Math.round((r - 0.5) * 2 * rect.w * 0.22 * (0.35 + s));
  if (!dx) return;
  const band = { x: rect.x, y: by, w: rect.w, h: bh };

  ctx.save();
  ctx.beginPath();
  ctx.rect(band.x, band.y, band.w, band.h);
  ctx.clip();
  // the slice, pushed sideways, wrapping so the tile never shows ground
  ctx.drawImage(src, band.x, band.y, band.w, band.h, band.x + dx, band.y, band.w, band.h);
  ctx.drawImage(src, band.x, band.y, band.w, band.h,
    band.x + dx - Math.sign(dx) * rect.w, band.y, band.w, band.h);

  // rgb split, one or two pixels, only on the slice
  const split = 1 + Math.round(s * 3);
  ctx.globalCompositeOperation = 'lighter';
  ctx.globalAlpha = 0.22;
  ctx.drawImage(channelCopy(src, band, '#FF2040', env, 'g-r'),
    band.x + dx + split, band.y, band.w, band.h);
  ctx.drawImage(channelCopy(src, band, '#20A0FF', env, 'g-b'),
    band.x + dx - split, band.y, band.w, band.h);
  ctx.globalCompositeOperation = 'source-over';

  // sharp scanlines over the slice
  ctx.globalAlpha = 0.28 + 0.3 * s;
  ctx.fillStyle = '#0B0B18';
  for (let y = 0; y < bh; y += 3) ctx.fillRect(band.x, band.y + y, band.w, 1);
  if (hash01(seed, f, 40 + i) > 0.78) {
    ctx.globalAlpha = 0.35;
    ctx.fillStyle = '#F2EBDD';
    ctx.fillRect(band.x, band.y + (bh > 2 ? 1 : 0), band.w, 1);
  }
  ctx.restore();
}
