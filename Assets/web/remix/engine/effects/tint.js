/* ============================================================================
 * effects/tint.js - colour on the picture.
 *
 *   wash   a grade: multiply pulls everything toward the hue, a screen pass
 *          lifts the blacks into it so the picture keeps its contrast.
 *   creep  the house motif. Colour bleeds in from one corner as a radial that
 *          grows with the block, with a faint spiral turning inside the stain.
 *          Screened, so it reads as light coming in rather than paint.
 * ==========================================================================*/

import { drawSpiralArms } from './spiral.js';
import { colourFor, cornerPoint, knob, rgba, shade } from './util.js';

export function render(ctx, src, block, t, env) {
  const a = knob(block.params, 'intensity', 65) * env.ramp;
  if (a <= 0.005) return;
  const colour = colourFor(block.params, env);
  if (block.mode === 'creep') creep(ctx, block, t, env, colour, a);
  else wash(ctx, env.rect, colour, a);
}

function wash(ctx, rect, colour, a) {
  ctx.save();
  ctx.globalCompositeOperation = 'multiply';
  ctx.globalAlpha = 0.6 * a;
  ctx.fillStyle = colour;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.globalCompositeOperation = 'screen';
  ctx.globalAlpha = 0.32 * a;
  ctx.fillStyle = shade(colour, 0.6);
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.restore();
}

function creep(ctx, block, t, env, colour, a) {
  const rect = env.rect;
  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const corner = cornerPoint((block.params && block.params.corner) || 'tl', { x: 0, y: 0, w, h });
  const diag = Math.hypot(w, h);
  // stops short of the far corner even at the end, so the stain keeps a
  // direction and you can still see where it came in from
  const reach = Math.max(1, diag * (0.2 + 0.72 * t));

  const b = env.buf('tint-creep', w, h);
  const c = b.ctx;
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.globalCompositeOperation = 'source-over';
  c.globalAlpha = 1;
  c.clearRect(0, 0, w, h);

  // the spiral turns inside the core of the stain, never out at the edge
  c.fillStyle = rgba(colour, 1);
  c.strokeStyle = rgba(colour, 1);
  c.globalAlpha = 0.16;
  drawSpiralArms(c, {
    cx: corner.x, cy: corner.y, r: reach * 0.5,
    style: 1, phase: env.frame * 0.045, dir: 1, steps: 140,
  });
  c.globalAlpha = 1;

  // the stain: hot at the corner, a bright front at the leading edge
  const plate = c.createRadialGradient(corner.x, corner.y, 0, corner.x, corner.y, reach);
  plate.addColorStop(0, rgba('#FFFFFF', 0.42));
  plate.addColorStop(0.2, rgba(colour, 0.98));
  plate.addColorStop(0.62, rgba(colour, 0.7));
  plate.addColorStop(0.86, rgba(colour, 0.48));
  plate.addColorStop(0.93, rgba('#FFFFFF', 0.3));
  plate.addColorStop(1, rgba(colour, 0));
  c.fillStyle = plate;
  c.fillRect(0, 0, w, h);

  // and a soft front so it bleeds outward instead of stopping at a line
  const mask = c.createRadialGradient(corner.x, corner.y, 0, corner.x, corner.y, reach);
  mask.addColorStop(0, 'rgba(0,0,0,1)');
  mask.addColorStop(0.7, 'rgba(0,0,0,0.92)');
  mask.addColorStop(0.9, 'rgba(0,0,0,0.55)');
  mask.addColorStop(1, 'rgba(0,0,0,0)');
  c.globalCompositeOperation = 'destination-in';
  c.fillStyle = mask;
  c.fillRect(0, 0, w, h);
  c.globalCompositeOperation = 'source-over';

  ctx.save();
  ctx.globalCompositeOperation = 'screen';
  ctx.globalAlpha = a;
  ctx.drawImage(b.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);
  // a little multiply keeps the far side of the stain from washing out
  ctx.globalCompositeOperation = 'multiply';
  ctx.globalAlpha = a * 0.34;
  ctx.drawImage(b.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);
  ctx.restore();
}
