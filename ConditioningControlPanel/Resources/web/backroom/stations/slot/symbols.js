/* symbols.js - paints one reel cell for a symbol id (CONTRACT.md section 5), ported from the
 * preview's drawSymbol. The canvas is already centred on the cell (-128..128) and rotated and
 * reflected by the caller. `look` carries the dealt media: look.gif(i) -> a drawable (an animated
 * <img> or null) and look.word(i) -> text or null. Missing media gets the preview's fallback art. */

import { fitText } from '../../shared/text/wrap.js';
import { createLoomKit } from '../../shared/hypno/loom.js';

const TILE = ['#ec79b3', '#8160c6', '#c698db', '#53a5b3'];
export const WORDS = ['DROP', 'RELAX', 'LET GO', 'SINK'];

export function kindOf(id) {
  const m = /^(gif|sub|spiral)(\d)$/.exec(id || '');
  if (m) return { kind: m[1], n: Number(m[2]) };
  return { kind: id === 'emi' || id === 'melt' ? id : 'unknown', n: 0 };
}

/* THE REEL SPIRALS ARE LOOM FIELDS (owner, 2026-09-16: the drawn coil below read as a placeholder).
 * Every spiral the Back Room shows comes from the Loom (CONTRACT 10.13.D), so a reel cell asks for one
 * too. Cost (loom law 7): each preset is painted into ONE 256 px tile per repaint clock, and every cell
 * showing that preset draws the same tile - a strip of thirteen spirals costs one GL render, not
 * thirteen. No WebGL, no document, or a kit that will not paint: the drawn coil stays as the fallback. */
const SPIRAL_PRESETS = ['screen', 'whirl', 'wake', 'hub'];
const SPIRAL_TILE = 256;
let loomKit = null;
const spiralTiles = new Map();   // preset -> { canvas, at }

function spiralTile(n, t, reduced) {
  if (typeof document === 'undefined' || !document.createElement) return null;
  const name = SPIRAL_PRESETS[n % SPIRAL_PRESETS.length];
  if (!loomKit) loomKit = createLoomKit({ still: !!reduced });
  loomKit.setStill(!!reduced);
  let tile = spiralTiles.get(name);
  if (!tile) {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = SPIRAL_TILE;
    tile = { canvas, at: NaN };
    spiralTiles.set(name, tile);
  }
  const now = reduced ? 0 : t;
  const painted = Number.isFinite(tile.at);
  if (tile.at !== now) {
    // A paint that will not run keeps the last good tile rather than blinking back to the coil.
    if (!loomKit.paint(tile.canvas, name, { now })) return painted ? tile.canvas : null;
    tile.at = now;
  }
  return tile.canvas;
}

/** The drawn coil: kept for a page with no WebGL and no 2D fallback either. */
function drawnCoil(ctx, n, t, reduced) {
  ctx.strokeStyle = n % 2 ? '#f49aca' : '#c4a6ef'; ctx.lineWidth = 12; ctx.beginPath();
  for (let k = 0; k < 200; k++) {
    const a = k * 0.12 + n * 2.1 + (reduced ? 0 : t / 2400), r = k * 0.48;
    k ? ctx.lineTo(Math.cos(a) * r, Math.sin(a) * r) : ctx.moveTo(Math.cos(a) * r, Math.sin(a) * r);
  }
  ctx.stroke();
}

/** Free the page's Loom context and the reel tiles. The slot's scene dispose() calls it. */
export function disposeSpirals() {
  if (loomKit) loomKit.dispose();
  loomKit = null;
  for (const tile of spiralTiles.values()) tile.canvas.width = tile.canvas.height = 1;
  spiralTiles.clear();
}

function fallbackTile(ctx, n, t, reduced) {
  const aspect = [1.6, 0.72, 1, 1.35][n % 4], w = 220 * Math.min(1, aspect), h = 220 * Math.min(1, 1 / aspect);
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
  const g = ctx.createLinearGradient(-w / 2, -h / 2, w / 2, h / 2);
  g.addColorStop(0, TILE[n % 4]); g.addColorStop(1, '#342044');
  ctx.fillStyle = g; ctx.fillRect(-w / 2, -h / 2, w, h);
  const tt = reduced ? 0 : t;
  for (let k = 0; k < 5; k++) {
    ctx.strokeStyle = k % 2 ? '#ffcfeb' : '#e6c3ff'; ctx.lineWidth = 4; ctx.beginPath();
    ctx.arc(Math.sin(tt / 1300 + k + n) * w * 0.22, Math.cos(tt / 1700 + k) * h * 0.22, 18 + k * 15, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.restore();
}

/* The subliminal / trigger phrases the player set can be long ("I CANT RESIST MY TRIGGERS"). They wrap into
 * up to three lines at the largest font that fits the glyph box instead of being squeezed onto one line;
 * anything past SUB_WRAP_AT characters takes at least two lines (shared/text/wrap.js). The layout is memoised
 * per phrase, so the per-frame repaint (THE GLYPH HIT, A2's ghost) costs one measure pass per phrase. */
const SUB_BOX = { w: 220, h: 196 };
const SUB_WRAP_AT = 12;
const subFont = (px) => `700 ${px}px Segoe UI, Arial, sans-serif`;
const subFits = new Map();

/** The wrapped block for one phrase in the reel cell: { size, lines }. Memoised; exported for the tests. */
export function phraseLayout(ctx, text) {
  const hit = subFits.get(text);
  if (hit) return hit;
  const measure = (str, size) => { ctx.font = subFont(size); return ctx.measureText(str).width; };
  const fit = fitText(text, { measure, width: SUB_BOX.w, height: SUB_BOX.h, maxLines: 3,
    minLines: text.length > SUB_WRAP_AT ? 2 : 1, min: 13, max: 42, lineHeight: 1.12 });
  if (subFits.size > 64) subFits.clear();
  subFits.set(text, fit);
  return fit;
}

/** The dealt phrase, centred in the cell, wrapped and scaled to fit. Same colour as the one-line version. */
function drawPhrase(ctx, text) {
  const fit = phraseLayout(ctx, text);
  ctx.fillStyle = '#ffd7eb'; ctx.font = subFont(fit.size);
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  const step = fit.size * 1.12, top = -(fit.lines.length - 1) * step / 2;
  fit.lines.forEach((l, i) => ctx.fillText(l, 0, top + i * step, SUB_BOX.w));
}

function drawMedia(ctx, img) {
  const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
  const s = 220 / Math.max(iw, ih), w = iw * s, h = ih * s;
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
  ctx.drawImage(img, -w / 2, -h / 2, w, h);
  ctx.restore();
}

export function drawSymbol(ctx, id, t, look = {}) {
  const { kind, n } = kindOf(id);
  ctx.fillStyle = '#271632'; ctx.fillRect(-128, -128, 256, 256);
  if (kind === 'gif') {
    const img = look.gif && look.gif(n);
    if (img) drawMedia(ctx, img); else fallbackTile(ctx, n, t, look.reduced);
  } else if (kind === 'spiral') {
    const tile = spiralTile(n, t, look.reduced);
    if (tile) {
      ctx.save();
      ctx.beginPath(); ctx.roundRect(-112, -112, 224, 224, 12); ctx.clip();
      ctx.drawImage(tile, -112, -112, 224, 224);
      ctx.restore();
    } else drawnCoil(ctx, n, t, look.reduced);
  } else if (kind === 'sub') {
    const text = (look.word && look.word(n)) || WORDS[n % 4];
    drawPhrase(ctx, String(text).toUpperCase());
  } else if (kind === 'emi') {
    ctx.fillStyle = '#534467'; ctx.beginPath(); ctx.roundRect(-95, -87, 190, 170, 22); ctx.fill();
    if (look.face) ctx.drawImage(look.face, 3 * 152, 0, 152, 137, -77, -68, 154, 136);
    ctx.fillStyle = '#f094c4'; ctx.beginPath(); ctx.arc(0, -108, 12, 0, 7); ctx.fill();
  } else if (kind === 'melt') {
    ctx.fillStyle = '#b48acb'; ctx.beginPath(); ctx.roundRect(-75, -68, 150, 75, 16); ctx.fill();
    for (let k = 0; k < 4; k++) { ctx.beginPath(); ctx.roundRect(-75 + k * 40, -5, 25, 40 + (k % 2) * 35, 12); ctx.fill(); }
    ctx.fillStyle = '#261330'; ctx.fillRect(-45, -42, 20, 12); ctx.fillRect(25, -42, 20, 12);
  }
}
