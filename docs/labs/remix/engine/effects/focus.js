/* ============================================================================
 * effects/focus.js - one soft circle travels a dragged path.
 *
 *   ring  everything outside the circle is blurred, the circle is the only
 *         sharp thing on screen and it drifts across the mosaic
 *   dark  everything outside is dropped toward the ground colour instead
 *
 * The edge is a gradient, not a hard circle, so it reads as attention rather
 * than as a vignette cut out with scissors.
 * ==========================================================================*/

import { blurredCopy, samplePath, knob, easeInOut, clamp } from './util.js';

export function render(ctx, src, block, t, env) {
  const a = env.ramp;
  if (a <= 0.01) return;
  const rect = env.rect;
  const p = block.params || {};
  const k = knob(p, 'radius', 40);

  const at = samplePath(p.path, easeInOut(t));
  const cx = rect.x + clamp(at.x, 0, 1) * rect.w;
  const cy = rect.y + clamp(at.y, 0, 1) * rect.h;
  const r = Math.max(12, Math.min(rect.w, rect.h) * (0.16 + k * 0.55));

  if (block.mode === 'dark') {
    ctx.save();
    // clear right out to the radius, then away fast: the circle has to be an
    // opening, not a soft dip in an evenly dim frame
    const g = ctx.createRadialGradient(cx, cy, r * 0.72, cx, cy, r * 2.1);
    g.addColorStop(0, 'rgba(10,10,22,0)');
    g.addColorStop(0.3, `rgba(10,10,22,${(0.3 * a).toFixed(3)})`);
    g.addColorStop(0.68, `rgba(10,10,22,${(0.68 * a).toFixed(3)})`);
    g.addColorStop(1, `rgba(10,10,22,${(0.86 * a).toFixed(3)})`);
    ctx.fillStyle = g;
    ctx.fillRect(rect.x, rect.y, rect.w, rect.h);

    const lift = ctx.createRadialGradient(cx, cy, 0, cx, cy, r * 1.05);
    lift.addColorStop(0, `rgba(255,255,255,${(0.09 * a).toFixed(3)})`);
    lift.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.globalCompositeOperation = 'lighter';
    ctx.fillStyle = lift;
    ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
    ctx.restore();
    return;
  }

  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const blurred = blurredCopy(src, rect, 4 + 9 * (1 - k * 0.5), env);

  // the sharp disc, masked with a soft edge, sits back on top of the blur
  const disc = env.buf('focus-disc', w, h);
  const d = disc.ctx;
  d.setTransform(1, 0, 0, 1, 0, 0);
  d.globalCompositeOperation = 'source-over';
  d.globalAlpha = 1;
  d.clearRect(0, 0, w, h);
  d.drawImage(src, rect.x, rect.y, rect.w, rect.h, 0, 0, w, h);
  const mask = d.createRadialGradient(cx - rect.x, cy - rect.y, r * 0.55, cx - rect.x, cy - rect.y, r * 1.15);
  mask.addColorStop(0, 'rgba(0,0,0,1)');
  mask.addColorStop(0.7, 'rgba(0,0,0,0.85)');
  mask.addColorStop(1, 'rgba(0,0,0,0)');
  d.globalCompositeOperation = 'destination-in';
  d.fillStyle = mask;
  d.fillRect(0, 0, w, h);
  d.globalCompositeOperation = 'source-over';

  ctx.save();
  ctx.globalAlpha = a;
  ctx.drawImage(blurred, 0, 0, blurred.width, blurred.height, rect.x, rect.y, rect.w, rect.h);

  // blur alone disappears on soft material, so the outside also falls away in
  // light: a gentle spotlight that starts where the sharp edge ends
  const fall = ctx.createRadialGradient(cx, cy, r * 0.8, cx, cy, r * 2.4);
  fall.addColorStop(0, 'rgba(12,12,26,0)');
  fall.addColorStop(0.45, `rgba(12,12,26,${(0.2 * a).toFixed(3)})`);
  fall.addColorStop(1, `rgba(12,12,26,${(0.46 * a).toFixed(3)})`);
  ctx.globalAlpha = 1;
  ctx.fillStyle = fall;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);

  ctx.globalAlpha = a;
  ctx.drawImage(disc.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);

  // and the inside lifts, so the circle looks lit rather than cut out
  const lift = ctx.createRadialGradient(cx, cy, 0, cx, cy, r * 1.1);
  lift.addColorStop(0, `rgba(255,255,255,${(0.1 * a).toFixed(3)})`);
  lift.addColorStop(0.6, `rgba(255,255,255,${(0.045 * a).toFixed(3)})`);
  lift.addColorStop(1, 'rgba(255,255,255,0)');
  ctx.globalCompositeOperation = 'lighter';
  ctx.globalAlpha = 1;
  ctx.fillStyle = lift;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.restore();
}
