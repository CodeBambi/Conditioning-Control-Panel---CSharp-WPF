/* symbols.js - paints one reel cell for a symbol id (CONTRACT.md section 5), ported from the
 * preview's drawSymbol. The canvas is already centred on the cell and rotated and reflected by the
 * caller. `look` carries the dealt media: look.gif(i) -> a drawable (an animated <img> or null) and
 * look.word(i) -> text or null. Missing media gets the preview's fallback art.
 *
 * The T cabinet shows a large near-square central card and shallow neighbouring slivers.
 * All artwork uses CELL's box; picture media contains its native aspect without stretching.
 * The prize legend passes its own smaller box. */

import { fitText } from '../../shared/text/wrap.js';
import { createLoomKit } from '../../shared/hypno/loom.js';

/** The reel cell's half extents, in the canvas units every painter here works in. hw runs along the DRUM's
 *  width (horizontal on screen), hh along its circumference (vertical). scene.js sizes its reel canvas from
 *  these, so the texel density stays square and the art is never stretched. */
// Near-square picture cards fit the tall cabinet windows without stretched media.
export const CELL = { hw: 112, hh: 128 };

/** CONTAIN a picture of this aspect inside the cell, with a hair of margin. This one function is why the
 *  T pays off: the cell is 1.75:1 now, so a 16:9 clip comes out limited by the cell's HEIGHT and fills
 *  almost the whole width, where a square cell limited it by width and left two thirds of the glass empty. */
function fitBox(aspect, cell) {
  const W = cell.hw * 2 * 0.94, H = cell.hh * 2 * 0.94;
  const k = Math.min(W / aspect, H);
  return { w: k * aspect, h: k };
}

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
const SPIRAL_PRESETS = ['screen', 'candy', 'pinwheel', 'mint', 'ribbon', 'star', 'whirl', 'wake', 'hub'];
let spiralDeal = 0;
export function rotateSpiralDeal() { spiralDeal = (spiralDeal + 4) % SPIRAL_PRESETS.length; }
const SPIRAL_TILE = 256;
let loomKit = null;
const spiralTiles = new Map();   // preset -> { canvas, at }

function spiralTile(n, t, reduced) {
  if (typeof document === 'undefined' || !document.createElement) return null;
  const name = SPIRAL_PRESETS[(n + spiralDeal) % SPIRAL_PRESETS.length];
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
function drawnCoil(ctx, n, t, reduced, cell) {
  const reach = Math.max(cell.hw, cell.hh) / 128;
  ctx.strokeStyle = n % 2 ? '#f49aca' : '#c4a6ef'; ctx.lineWidth = 12; ctx.beginPath();
  for (let k = 0; k < 200; k++) {
    const a = k * 0.12 + n * 2.1 + (reduced ? 0 : t / 2400), r = k * 0.48 * reach;
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

function fallbackTile(ctx, n, t, reduced, cell) {
  const aspect = [1.6, 0.72, 1, 1.35][n % 4], box = fitBox(aspect, cell), w = box.w, h = box.h;
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
  const g = ctx.createLinearGradient(-w / 2, -h / 2, w / 2, h / 2);
  g.addColorStop(0, TILE[n % 4]); g.addColorStop(1, '#342044');
  ctx.fillStyle = g; ctx.fillRect(-w / 2, -h / 2, w, h);
  const tt = reduced ? 0 : t;
  for (let k = 0; k < 5; k++) {
    ctx.strokeStyle = k % 2 ? '#ffcfeb' : '#e6c3ff'; ctx.lineWidth = 4; ctx.beginPath();
    ctx.arc(Math.sin(tt / 1300 + k + n) * w * 0.22, Math.cos(tt / 1700 + k) * h * 0.22, (18 + k * 15) * Math.min(w, h) / 220, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.restore();
}

/* The subliminal / trigger phrases the player set can be long ("I CANT RESIST MY TRIGGERS"). They wrap into
 * up to three lines at the largest font that fits the glyph box instead of being squeezed onto one line;
 * anything past SUB_WRAP_AT characters takes at least two lines (shared/text/wrap.js). The layout is memoised
 * per phrase, so the per-frame repaint (THE GLYPH HIT, A2's ghost) costs one measure pass per phrase. */
const SUB_WRAP_AT = 12;
const subFont = (px) => `700 ${px}px Segoe UI, Arial, sans-serif`;
const subFits = new Map();

/** The wrapped block for one phrase in the reel cell: { size, lines }. Memoised on the phrase AND the box,
 *  because the same trigger is laid out twice at two sizes now (the reel, and the paytable's legend cell). */
export function phraseLayout(ctx, text, cell = CELL) {
  const width = cell.hw * 2 * 0.86, height = cell.hh * 2 * 0.77;
  const key = `${Math.round(width)}x${Math.round(height)}|${text}`;
  const hit = subFits.get(key);
  if (hit) return hit;
  const measure = (str, size) => { ctx.font = subFont(size); return ctx.measureText(str).width; };
  const words=String(text).trim().split(/\s+/),longest=Math.max(...words.map(word=>word.length),1);
  const wordWidth=Math.max(...words.map(word=>measure(word,100)/100),1);
  // fitText estimates line capacity from average glyph width, so constrain that too.
  const average=measure(String(text),100)/100/Math.max(String(text).length,1);
  const max = Math.min(Math.max(16, Math.round(height / 2.1)),Math.floor(width/wordWidth),Math.floor(width/(average*(longest+.05))));
  const fit = fitText(text, { measure, width, height, maxLines: 3,
    minLines: text.length > SUB_WRAP_AT ? 2 : 1, min: Math.min(13, max), max, lineHeight: 1.12 });
  if (subFits.size > 64) subFits.clear();
  subFits.set(key, fit);
  return fit;
}

/** The dealt phrase, centred in the cell, wrapped and scaled to fit. Same colour as the one-line version. */
function drawPhrase(ctx, text, cell) {
  const fit = phraseLayout(ctx, text, cell);
  ctx.fillStyle = '#ffd7eb'; ctx.font = subFont(fit.size);
  ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
  const step = fit.size * 1.12, top = -(fit.lines.length - 1) * step / 2;
  fit.lines.forEach((l, i) => ctx.fillText(l, 0, top + i * step, cell.hw * 2 * 0.86));
}

// One decoded frame supplies both layers. Contain the subject over a blurred cover fill.
function drawMedia(ctx, img, cell) {
  const iw = img.naturalWidth || img.width || 1, ih = img.naturalHeight || img.height || 1;
  const w = cell.hw * 1.88, h = cell.hh * 1.88, box = fitBox(iw / ih, cell);
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, Math.min(12, h / 8)); ctx.clip();
  if (Math.abs(box.w - w) > 1 || Math.abs(box.h - h) > 1) {
    const scale = Math.max(w / iw, h / ih) * 1.14;
    ctx.filter = 'blur(9px) brightness(.65)';
    ctx.drawImage(img, -iw * scale / 2, -ih * scale / 2, iw * scale, ih * scale);
    ctx.filter = 'none';
  }
  ctx.drawImage(img, -box.w / 2, -box.h / 2, box.w, box.h);
  ctx.restore();
}

// Atlas expressions: neutral, smile, cute, squint, stars, hearts and jackpot.
export function reelFace(t, { reduced = false, faceMood = 'idle', faceAge = 0 } = {}) {
  if (reduced) return { frame: faceMood === 'jackpot' ? 10 : faceMood === 'near' ? 2 : 3, lift: 0 };
  if (faceMood === 'near' && faceAge < 1700) {
    return { frame: [3, 2, 2, 3][Math.min(3, Math.floor(faceAge / 425))], lift: 0 };
  }
  if (faceMood === 'jackpot' && faceAge < 6000) {
    return { frame: [10, 5, 10, 7][Math.floor(faceAge / 380) % 4], lift: Math.sin(faceAge / 150) * 3 };
  }
  const cycle = Math.floor(t / 4200) % 3, at = t % 4200;
  const frames = [[3, 2, 3], [3, 0, 3], [3, 1, 3]][cycle];
  return { frame: at < 3200 ? 3 : frames[Math.min(2, Math.floor((at - 3200) / 334))], lift: Math.sin(t / 900) * 1.5 };
}

export function drawSymbol(ctx, id, t, look = {}, cell = CELL) {
  const { kind, n } = kindOf(id);
  ctx.fillStyle = '#271632'; ctx.fillRect(-cell.hw, -cell.hh, cell.hw * 2, cell.hh * 2);
  if (kind === 'gif') {
    const img = look.gif && look.gif(n);
    if (img) drawMedia(ctx, img, cell, !!look.cover); else fallbackTile(ctx, n, t, look.reduced, cell);
  } else if (kind === 'spiral') {
    const tile = spiralTile(n, t, look.reduced);
    if (tile) {
      // COVER, not contain: a Loom field has no subject to crop off, so it takes the whole wide cell and the
      // ring keeps reading as one turning thing rather than as a coin sitting in the middle of a letterbox.
      const w = cell.hw * 2 * 0.94, h = cell.hh * 2 * 0.94, s = Math.max(w, h);
      ctx.save();
      ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
      ctx.drawImage(tile, -s / 2, -s / 2, s, s);
      ctx.restore();
    } else drawnCoil(ctx, n, t, look.reduced, cell);
  } else if (kind === 'sub') {
    const text = (look.word && look.word(n)) || WORDS[n % 4];
    drawPhrase(ctx, String(text).toUpperCase(), cell);
  } else if (kind === 'emi') {
    ctx.fillStyle = '#534467'; ctx.beginPath(); ctx.roundRect(-95, -87, 190, 170, 22); ctx.fill();
    const face = reelFace(t, look);
    if (look.face) ctx.drawImage(look.face, face.frame * 152, 0, 152, 137, -77, -68 + face.lift, 154, 136);
    ctx.fillStyle = '#f094c4'; ctx.beginPath(); ctx.arc(0, -108, 12, 0, 7); ctx.fill();
  } else if (kind === 'melt') {
    ctx.fillStyle = '#b48acb'; ctx.beginPath(); ctx.roundRect(-75, -68, 150, 75, 16); ctx.fill();
    for (let k = 0; k < 4; k++) { ctx.beginPath(); ctx.roundRect(-75 + k * 40, -5, 25, 40 + (k % 2) * 35, 12); ctx.fill(); }
    ctx.fillStyle = '#261330'; ctx.fillRect(-45, -42, 20, 12); ctx.fillRect(25, -42, 20, 12);
  }
}
