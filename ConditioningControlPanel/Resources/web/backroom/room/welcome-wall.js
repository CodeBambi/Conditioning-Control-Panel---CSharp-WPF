/* ============================================================================
 * backroom/room/welcome-wall.js - what the counter's two placards say, pure.
 *
 * The Prize Parlour's apron carries three framed panels under the shelf: the
 * middle one reads SPARKLES / PRIZES in the GLB, the two beside it are bare
 * lining. Each of those two gets one placard, one per page of welcome.js's
 * card (PAGES is the one list): the page's landscape picture on the left, its
 * title and one line on the right, and "Tap to read". welcome-placards.js
 * hangs them; this file is the records and the paint, with no three and no
 * DOM, so the node suite can read the wall (memorabilia-wall.js's split).
 *
 * The panel measurements come from ray-picking the built room at the counter's
 * approach pose (2026-09-18): the lining sits at z -4.74, the inner fill of
 * each side panel spans 1.45 x 0.44 centred at x +-1.72, y 0.54. The placard
 * sits one centimetre proud of the lining and just inside the gold frame.
 * ==========================================================================*/
import { PAGES } from './welcome.js';

/** The apron's side panels, in room metres. */
export const PANEL = Object.freeze({ x: 1.72, y: .536, z: -4.728, size: Object.freeze([1.42, .43]) });
/** The paper, in pixels: the panel's own aspect, so the picture is not stretched. */
export const PAPER = Object.freeze({ w: 1024, h: 310, pad: 14 });

/** One record per page: where it hangs and which words it carries. Pure, so the node suite can read it. */
export const PLACARDS = Object.freeze(PAGES.map((p, i) => Object.freeze({
  id: 'placard-' + p.id, page: i, src: p.hero, titleKey: p.title, subKey: p.sub, readKey: 'br_welcome_read',
  position: Object.freeze([i === 0 ? -PANEL.x : PANEL.x, PANEL.y, PANEL.z]), yaw: 0, size: PANEL.size,
})));

/**
 * Paint one placard: the picture (2:1, cover-cropped) on the left, the words on the right.
 * @param {CanvasRenderingContext2D} ctx
 * @param {{ image?: {width:number,height:number}|null, title: string, sub: string, read: string }} o
 */
export function paintPlacard(ctx, o) {
  const { w, h, pad } = PAPER;
  ctx.fillStyle = '#2a1636'; ctx.fillRect(0, 0, w, h);
  ctx.strokeStyle = '#b99055'; ctx.lineWidth = 3; ctx.strokeRect(4, 4, w - 8, h - 8);
  // The picture box: full paper height, twice as wide as tall, the two pages' own 2:1.
  const box = { x: pad, y: pad, h: h - pad * 2 }; box.w = box.h * 2;
  ctx.fillStyle = '#1a1026'; ctx.fillRect(box.x, box.y, box.w, box.h);
  if (o.image && o.image.width > 0 && o.image.height > 0) {
    const scale = Math.max(box.w / o.image.width, box.h / o.image.height);
    const sw = box.w / scale, sh = box.h / scale;
    ctx.drawImage(o.image, (o.image.width - sw) / 2, (o.image.height - sh) / 2, sw, sh, box.x, box.y, box.w, box.h);
  }
  ctx.strokeStyle = '#d9b26a'; ctx.lineWidth = 2; ctx.strokeRect(box.x + 1, box.y + 1, box.w - 2, box.h - 2);
  // The words: the title, the page's one line (two rows at most), and the invitation at the foot.
  const left = box.x + box.w + 26, width = w - left - pad - 8;
  ctx.textAlign = 'left'; ctx.textBaseline = 'alphabetic';
  ctx.fillStyle = '#f3dfa0'; ctx.font = '600 38px Georgia';
  ctx.fillText(o.title, left, 78, width);
  ctx.fillStyle = '#d8c8e8'; ctx.font = '25px Georgia';
  wrap(ctx, o.sub, width).slice(0, 2).forEach((line, i) => ctx.fillText(line, left, 128 + i * 34, width));
  ctx.fillStyle = '#b99cff'; ctx.font = '600 21px "Segoe UI", system-ui, sans-serif';
  ctx.fillText(String(o.read).toUpperCase(), left, h - pad - 22, width);
}

/** Word-wrap by measureText; a context without one (a stub) gets the whole line back. */
function wrap(ctx, text, width) {
  const words = String(text || '').split(/\s+/).filter(Boolean), lines = [];
  if (typeof ctx.measureText !== 'function') return [words.join(' ')];
  let line = '';
  for (const word of words) {
    const next = line ? line + ' ' + word : word;
    if (line && ctx.measureText(next).width > width) { lines.push(line); line = word; } else line = next;
  }
  if (line) lines.push(line);
  return lines;
}
