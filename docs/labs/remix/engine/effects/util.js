/* ============================================================================
 * effects/util.js - the bits every effect needs.
 *
 * Blur is the expensive one, so it is done the cheap way on purpose: shrink
 * the region into a scratch canvas, let the browser's bilinear filter do the
 * averaging, blur the small copy with ctx.filter where the browser has it,
 * then draw it back up. On a 480x270 canvas that is well under a millisecond
 * and Safari, which has no canvas filter, still gets a soft result.
 * ==========================================================================*/

import { TINT_COLOURS } from '../blocks.js';

export const GROUND = '#14142B';
export const INK = '#F2EBDD';

/**
 * The caption faces. The first three are system stacks and are what every
 * remix made before 2026-09-07 draws with, so they never change. The last four
 * ship with the room (`engine/fonts.js`) and look the same on every machine;
 * each one keeps a stack behind it in case the file never arrives.
 */
export const FONTS = {
  display: "'Bahnschrift Condensed','Arial Narrow','Arial Black',Impact,sans-serif",
  mono: "'Cascadia Mono',Consolas,monospace",
  hand: "'Segoe Print','Bradley Hand',cursive",
  block: "'Anton','Arial Narrow','Arial Black',Impact,sans-serif",
  script: "'Pacifico','Segoe Script','Bradley Hand',cursive",
  round: "'Fredoka','Trebuchet MS','Segoe UI',sans-serif",
  pixel: "'Press Start 2P','Cascadia Mono',Consolas,monospace",
};

export const HAS_FILTER = (() => {
  try {
    const c = document.createElement('canvas').getContext('2d');
    c.filter = 'blur(2px)';
    return c.filter === 'blur(2px)';
  } catch { return false; }
})();

/** '#FF69B4' -> {r,g,b} */
export function hexToRgb(hex) {
  const h = String(hex || '').replace('#', '');
  const s = h.length === 3 ? h[0] + h[0] + h[1] + h[1] + h[2] + h[2] : h;
  const n = parseInt(s || '000000', 16);
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}

export function rgba(hex, a) {
  const c = hexToRgb(hex);
  return `rgba(${c.r},${c.g},${c.b},${a})`;
}

/** Mix towards black, for the multiply half of a wash. */
export function shade(hex, k) {
  const c = hexToRgb(hex);
  return `rgb(${Math.round(c.r * k)},${Math.round(c.g * k)},${Math.round(c.b * k)})`;
}

/**
 * The colour a block asks for: a named brand hue, or one pulled from media.
 * `table` is the names that block understands; captions pass their own, which
 * is the tint three plus white and black.
 */
export function colourFor(params, env, table = TINT_COLOURS) {
  const name = (params && params.colour) || 'pink';
  if (name === 'custom') return (params && params.custom) || (env && env.mediaColour) || table.pink;
  return table[name] || TINT_COLOURS[name] || name || table.pink;
}

/** How bright a colour is, 0..1. */
export function luma(hex) {
  const c = hexToRgb(hex);
  return (c.r * 0.299 + c.g * 0.587 + c.b * 0.114) / 255;
}

/** The one of ground and ink that reads against `hex`. */
export function contrastTo(hex) {
  return luma(hex) > 0.5 ? GROUND : INK;
}

/** Corner name -> the point it sits at inside a rect. */
export function cornerPoint(corner, rect) {
  const left = corner === 'tl' || corner === 'bl';
  const top = corner === 'tl' || corner === 'tr';
  return { x: rect.x + (left ? 0 : rect.w), y: rect.y + (top ? 0 : rect.h) };
}

export function clamp(v, lo, hi) { return v < lo ? lo : v > hi ? hi : v; }
export function smoothstep(u) { const c = clamp(u, 0, 1); return c * c * (3 - 2 * c); }
export function easeInOut(u) { const c = clamp(u, 0, 1); return c < 0.5 ? 2 * c * c : 1 - Math.pow(-2 * c + 2, 2) / 2; }

/** Knob 0..100 -> 0..1. */
export function knob(params, key, def = 50) {
  const v = params && params[key] != null ? params[key] : def;
  return clamp(v / 100, 0, 1);
}

/**
 * A blurred copy of one region of `src`, returned at region size.
 * `px` is the blur radius in output pixels.
 */
export function blurredCopy(src, rect, px, env) {
  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const radius = Math.max(0.5, px);
  const scale = clamp(radius / 2, 1, 12);
  const sw = Math.max(2, Math.round(w / scale));
  const sh = Math.max(2, Math.round(h / scale));
  const small = env.buf('blur-small', sw, sh);
  small.ctx.clearRect(0, 0, sw, sh);
  small.ctx.filter = 'none';
  small.ctx.imageSmoothingEnabled = true;
  small.ctx.imageSmoothingQuality = 'medium';
  small.ctx.drawImage(src, rect.x, rect.y, rect.w, rect.h, 0, 0, sw, sh);

  const out = env.buf('blur-out', w, h);
  out.ctx.clearRect(0, 0, w, h);
  out.ctx.filter = HAS_FILTER ? `blur(${(radius / scale).toFixed(2)}px)` : 'none';
  out.ctx.imageSmoothingEnabled = true;
  out.ctx.imageSmoothingQuality = 'high';
  out.ctx.drawImage(small.canvas, 0, 0, sw, sh, 0, 0, w, h);
  if (!HAS_FILTER) {
    // one more pass at a slight offset softens the bilinear stair stepping
    out.ctx.globalAlpha = 0.5;
    out.ctx.drawImage(small.canvas, 0, 0, sw, sh, -1, -1, w + 2, h + 2);
    out.ctx.globalAlpha = 1;
  }
  out.ctx.filter = 'none';
  return out.canvas;
}

/** One channel of a region, as a canvas, for cheap rgb splits. */
export function channelCopy(src, rect, colour, env, key = 'chan') {
  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const b = env.buf(key, w, h);
  b.ctx.globalCompositeOperation = 'source-over';
  b.ctx.clearRect(0, 0, w, h);
  b.ctx.drawImage(src, rect.x, rect.y, rect.w, rect.h, 0, 0, w, h);
  b.ctx.globalCompositeOperation = 'multiply';
  b.ctx.fillStyle = colour;
  b.ctx.fillRect(0, 0, w, h);
  b.ctx.globalCompositeOperation = 'source-over';
  return b.canvas;
}

/** Point on a polyline at u in 0..1, by segment length. */
export function samplePath(path, u) {
  const pts = Array.isArray(path) && path.length ? path : [{ x: 0.5, y: 0.5 }];
  if (pts.length === 1) return { x: pts[0].x, y: pts[0].y };
  const lens = [];
  let total = 0;
  for (let i = 1; i < pts.length; i++) {
    const d = Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
    lens.push(d);
    total += d;
  }
  if (total <= 0) return { x: pts[0].x, y: pts[0].y };
  let want = clamp(u, 0, 1) * total;
  for (let i = 0; i < lens.length; i++) {
    if (want <= lens[i] || i === lens.length - 1) {
      const k = lens[i] > 0 ? want / lens[i] : 0;
      return {
        x: pts[i].x + (pts[i + 1].x - pts[i].x) * k,
        y: pts[i].y + (pts[i + 1].y - pts[i].y) * k,
      };
    }
    want -= lens[i];
  }
  return { x: pts[pts.length - 1].x, y: pts[pts.length - 1].y };
}

/** Run `fn` with the context clipped to a rect, then put it back. */
export function withClip(ctx, rect, fn) {
  ctx.save();
  ctx.beginPath();
  ctx.rect(rect.x, rect.y, rect.w, rect.h);
  ctx.clip();
  try { fn(); } finally { ctx.restore(); }
}

/** Fit a single line of text to a width by walking the size down. */
export function fitText(ctx, text, maxW, startPx, font) {
  let px = startPx;
  for (let i = 0; i < 24 && px > 8; i++) {
    ctx.font = `700 ${Math.round(px)}px ${font}`;
    if (ctx.measureText(text).width <= maxW) break;
    px *= 0.92;
  }
  return Math.round(px);
}
