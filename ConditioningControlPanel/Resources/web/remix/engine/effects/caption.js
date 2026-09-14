/* ============================================================================
 * effects/caption.js - one word, three ways.
 *
 *   text    the word on the picture, in the chosen face and colour, with a
 *           soft glow of its own and a rim under it so it stays readable
 *           on any frame: dark under a light word, cream under a dark one
 *   window  a colour plate with the word cut out of it, the picture showing
 *           through the letters
 *   flash   a single frame, the whole plate in colour with the word on it
 *
 * Position comes from a drag on the canvas, never from a typed number. A fresh
 * caption starts centre bottom, at 78 percent of the height, above the stamp pill at the bottom.
 * ==========================================================================*/

import { CAPTION_COLOURS } from '../blocks.js';
import { FONTS, GROUND, colourFor, contrastTo, knob, rgba, fitText, clamp } from './util.js';

export function render(ctx, src, block, t, env) {
  const p = block.params || {};
  const text = String(p.text == null ? '' : p.text).trim();
  if (!text) return;
  const a = env.ramp;
  if (a <= 0.01) return;

  const rect = env.rect;
  const colour = colourFor(p, env, CAPTION_COLOURS);
  const font = FONTS[p.font] || FONTS.display;
  const glow = knob(p, 'glow', 45);
  const sizeK = knob(p, 'size', 55);

  if (block.mode === 'flash') { flash(ctx, rect, text, colour, font, sizeK, a); return; }
  if (block.mode === 'window') { windowed(ctx, src, rect, text, colour, font, sizeK, p, env, a); return; }
  plain(ctx, rect, text, colour, font, glow, sizeK, p, a);
}

function place(rect, p) {
  const pos = p.pos || { x: 0.5, y: 0.78 };
  return {
    x: rect.x + clamp(pos.x, 0, 1) * rect.w,
    y: rect.y + clamp(pos.y, 0, 1) * rect.h,
  };
}

function sizeFor(rect, sizeK) {
  return Math.max(10, rect.h * (0.07 + sizeK * 0.26));
}

function plain(ctx, rect, text, colour, font, glow, sizeK, p, a) {
  const at = place(rect, p);
  const rim = contrastTo(colour);
  ctx.save();
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const px = fitText(ctx, text, rect.w * 0.92, sizeFor(rect, sizeK), font);
  ctx.font = `700 ${px}px ${font}`;
  ctx.globalAlpha = a;
  if (glow > 0.02) {
    // a wide soft bloom, then a tight hot one, so the word sits in the light.
    // The light is the ink's own colour, so a pink word glows pink and a black
    // one only picks up a rim of the cream it is drawn against.
    ctx.globalCompositeOperation = 'lighter';
    ctx.shadowColor = rgba(glowColour(colour, rim), 1);
    ctx.fillStyle = rgba(glowColour(colour, rim), 0.5);
    ctx.shadowBlur = 10 + glow * 60;
    ctx.fillText(text, at.x, at.y);
    ctx.fillText(text, at.x, at.y);
    ctx.shadowBlur = 3 + glow * 16;
    ctx.fillStyle = rgba(glowColour(colour, rim), 0.75);
    ctx.fillText(text, at.x, at.y);
    ctx.globalCompositeOperation = 'source-over';
  }
  ctx.shadowBlur = 0;
  // a rim under the ink, so it reads on a bright frame too: dark under a light
  // word, cream under a dark one
  ctx.lineJoin = 'round';
  ctx.lineWidth = Math.max(1.5, px * 0.055);
  ctx.strokeStyle = rgba(rim, rim === GROUND ? 0.62 : 0.72);
  ctx.strokeText(text, at.x, at.y);
  ctx.fillStyle = colour;
  ctx.fillText(text, at.x, at.y);
  ctx.restore();
}

/**
 * What the bloom is made of. `lighter` only ever adds, so a dark ink would
 * bloom into nothing; a dark word borrows its rim colour instead and comes out
 * with a halo around it.
 */
function glowColour(colour, rim) {
  return rim === GROUND ? colour : rim;
}

function windowed(ctx, src, rect, text, colour, font, sizeK, p, env, a) {
  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const b = env.buf('caption-window', w, h);
  const c = b.ctx;
  c.setTransform(1, 0, 0, 1, 0, 0);
  c.globalCompositeOperation = 'source-over';
  c.globalAlpha = 1;
  c.clearRect(0, 0, w, h);
  c.fillStyle = colour;
  c.fillRect(0, 0, w, h);

  // the plate keeps a ghost of the picture's darks, so it is a plate over
  // something and not a flat fill
  if (src) {
    c.globalCompositeOperation = 'multiply';
    c.globalAlpha = 0.34;
    c.drawImage(src, rect.x, rect.y, rect.w, rect.h, 0, 0, w, h);
    c.globalAlpha = 1;
    c.globalCompositeOperation = 'source-over';
  }

  // and it sinks towards the corners
  const vig = c.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.15, w / 2, h / 2, Math.max(w, h) * 0.7);
  vig.addColorStop(0, rgba(GROUND, 0));
  vig.addColorStop(0.6, rgba(GROUND, 0.16));
  vig.addColorStop(1, rgba(GROUND, 0.46));
  c.fillStyle = vig;
  c.fillRect(0, 0, w, h);

  const at = place({ x: 0, y: 0, w, h }, p);
  c.textAlign = 'center';
  c.textBaseline = 'middle';
  const px = fitText(c, text, w * 0.92, sizeFor({ h }, sizeK) * 1.25, font);
  c.font = `700 ${px}px ${font}`;
  c.lineJoin = 'round';
  c.globalCompositeOperation = 'destination-out';
  // feather first: two soft strokes outside the letter thin the plate a little
  // before the cut, so the edge is not a razor
  c.strokeStyle = '#000';
  c.globalAlpha = 0.2;
  c.lineWidth = Math.max(2, px * 0.09);
  c.strokeText(text, at.x, at.y);
  c.globalAlpha = 0.4;
  c.lineWidth = Math.max(1.5, px * 0.04);
  c.strokeText(text, at.x, at.y);
  c.globalAlpha = 1;
  c.fillStyle = '#000';
  c.fillText(text, at.x, at.y);
  c.globalCompositeOperation = 'source-over';

  ctx.save();
  ctx.globalAlpha = 0.9 * a;
  ctx.drawImage(b.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);
  // a rim on the letters, so the cut has an edge to it
  ctx.globalAlpha = 0.5 * a;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = `700 ${px}px ${font}`;
  ctx.lineJoin = 'round';
  ctx.lineWidth = Math.max(1, px * 0.02);
  ctx.strokeStyle = rgba(contrastTo(colour), 0.55);
  ctx.strokeText(text, rect.x + at.x, rect.y + at.y);
  ctx.restore();
}

function flash(ctx, rect, text, colour, font, sizeK, a) {
  ctx.save();
  ctx.globalAlpha = a;
  ctx.fillStyle = colour;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  // a hot centre, so the plate has a middle to look at
  const hot = ctx.createRadialGradient(cx, cy, 0, cx, cy, Math.max(rect.w, rect.h) * 0.6);
  hot.addColorStop(0, rgba('#FFFFFF', 0.28));
  hot.addColorStop(0.55, rgba('#FFFFFF', 0.06));
  hot.addColorStop(1, rgba(GROUND, 0.22));
  ctx.fillStyle = hot;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const px = fitText(ctx, text, rect.w * 0.88, sizeFor(rect, sizeK) * 1.35, font);
  ctx.font = `700 ${px}px ${font}`;
  // the word is whichever of ground and ink reads on the plate, so a black
  // plate does not swallow it
  ctx.fillStyle = contrastTo(colour);
  ctx.fillText(text, cx, cy);
  ctx.restore();
}
