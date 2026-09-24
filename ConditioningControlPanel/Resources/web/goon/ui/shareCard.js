/* ============================================================================
 * ui/shareCard.js - the end-of-match share card, drawn to a canvas (2026-09-24).
 *
 * 1200 x 630 (the Discord / OpenGraph shape), painted at 2x so it stays crisp on
 * a phone and in a zoomed embed. Everything on it comes from ui/shareWords.js
 * buildShareData(); nothing here reads a match.
 *
 * PUBLIC BY DEFAULT. The card goes into public channels, so it never carries a
 * match picture: two faces (only `data:` images the host handed us; anything else
 * is a monogram tile), two names, two scores, a few numbers and one tame word.
 * The only art is an abstract spiral in the flavour's tint.
 *
 * COPY: navigator.clipboard first (a plain browser, and WebView2 when it lets
 * us). If that throws and a host is there, the PNG goes over the bridge as
 * `share-card {action:'copy'}` and GoonHostService puts it on the Windows
 * clipboard as PNG + bitmap, so Discord pastes a picture. SAVE goes to the host's
 * save dialog when hosted, and to a plain download otherwise.
 *
 * Nothing here may throw into the recap: every entry point is try-wrapped and
 * reports a result object instead.
 * ==========================================================================*/

import {
  CARD_W, CARD_H, FLAVOUR_TINTS, FLAVOUR_NAMES, flavourWord, statLines, cardDate, verdictLine,
} from './shareWords.js';
import { tileColorFor, initialOf } from './avatar.js';
import * as bridge from '../bridge.js';

export const CARD_SCALE = 2;
export const CARD_FONT = 'GGCard';
const FONT_STACK = CARD_FONT + ", 'Segoe UI', system-ui, sans-serif";
const PNG_PREFIX = 'data:image/png;base64,';
const HOST_TIMEOUT_MS = 6000;

const doc = () => (typeof document !== 'undefined' ? document : null);

/* ---------------------------------------------------------------- loading */

let fontPromise = null;
/** The card's face, loaded once. Resolves true when it is usable, false on the fallback stack. */
export function loadCardFont() {
  if (fontPromise) return fontPromise;
  fontPromise = (async () => {
    try {
      const d = doc();
      if (!d || !d.fonts || typeof FontFace !== 'function') return false;
      const url = new URL('../assets/fonts/fredoka-latin.woff2', import.meta.url).href;
      const face = new FontFace(CARD_FONT, 'url(' + url + ')', { weight: '300 700' });
      await face.load();
      d.fonts.add(face);
      await Promise.all([d.fonts.load('600 40px ' + CARD_FONT), d.fonts.load('600 20px "Segoe UI"')]);
      return true;
    } catch (_e) { return false; }
  })();
  return fontPromise;
}

function loadImage(src) {
  return new Promise((resolve) => {
    if (!src || typeof Image !== 'function') { resolve(null); return; }
    const img = new Image();
    let done = false;
    const end = (v) => { if (!done) { done = true; resolve(v); } };
    img.onload = () => end(img.naturalWidth > 0 ? img : null);
    img.onerror = () => end(null);
    setTimeout(() => end(null), 4000);
    img.src = src;
  });
}

let logoPromise = null;
function loadLogo() {
  if (!logoPromise) {
    let url = '';
    try { url = new URL('../assets/goon_game_logo.png', import.meta.url).href; } catch (_e) { url = ''; }
    logoPromise = loadImage(url);
  }
  return logoPromise;
}

/* ---------------------------------------------------------------- drawing */

function rgba(hex, a) {
  const h = String(hex || '#ff69b4').replace('#', '');
  const n = parseInt(h.length === 3 ? h.split('').map((c) => c + c).join('') : h, 16) || 0;
  return 'rgba(' + ((n >> 16) & 255) + ', ' + ((n >> 8) & 255) + ', ' + (n & 255) + ', ' + a + ')';
}

function roundRect(g, x, y, w, h, r) {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}

/** Shrink the font until `text` fits `maxW`. Returns the size it settled on. */
function fitFont(g, text, maxW, size, weight, min = 12) {
  let s = size;
  for (; s > min; s -= 2) {
    g.font = weight + ' ' + s + 'px ' + FONT_STACK;
    if (g.measureText(text).width <= maxW) break;
  }
  g.font = weight + ' ' + s + 'px ' + FONT_STACK;
  return s;
}

function spacedText(g, text, x, y, spacing) {
  // letterSpacing where the canvas has it; the plain string where it does not.
  try { g.letterSpacing = spacing + 'px'; } catch (_e) { /* older canvas */ }
  g.fillText(text, x, y);
  try { g.letterSpacing = '0px'; } catch (_e) { /* older canvas */ }
}

function drawBackground(g, tint, seedTurn) {
  const bg = g.createLinearGradient(0, 0, CARD_W, CARD_H);
  bg.addColorStop(0, '#1d0b29');
  bg.addColorStop(0.55, '#12071b');
  bg.addColorStop(1, '#0a0510');
  g.fillStyle = bg;
  g.fillRect(0, 0, CARD_W, CARD_H);

  const glow = g.createRadialGradient(600, 250, 20, 600, 250, 560);
  glow.addColorStop(0, rgba(tint, 0.30));
  glow.addColorStop(0.45, rgba(tint, 0.10));
  glow.addColorStop(1, rgba(tint, 0));
  g.fillStyle = glow;
  g.fillRect(0, 0, CARD_W, CARD_H);

  // The spiral: four arms, fading out as they wind. Abstract on purpose.
  g.save();
  g.translate(600, 262);
  g.rotate(seedTurn);
  g.lineCap = 'butt';
  g.lineJoin = 'round';
  // Each arm is drawn as short CONTINUOUS runs (one path each): per-segment
  // strokes double their alpha where the caps overlap and read as a dotted line.
  for (let arm = 0; arm < 4; arm++) {
    const base = (arm / 4) * Math.PI * 2;
    for (let t0 = 0.2; t0 < 5.2; t0 += 0.5) {
      g.strokeStyle = rgba(arm % 2 ? '#ff69b4' : tint, 0.20 * (1 - t0 / 5.4));
      g.lineWidth = 1.5 + t0 * 1.6;
      g.beginPath();
      for (let t = t0; t <= t0 + 0.52; t += 0.04) {
        const r = 14 + t * 62;
        const x = Math.cos(base + t) * r, y = Math.sin(base + t) * r;
        if (t === t0) g.moveTo(x, y); else g.lineTo(x, y);
      }
      g.stroke();
    }
  }
  g.restore();

  // Vignette, so the edges read as a card and not a crop.
  const vig = g.createRadialGradient(600, 315, 260, 600, 315, 760);
  vig.addColorStop(0, 'rgba(0,0,0,0)');
  vig.addColorStop(1, 'rgba(0,0,0,0.55)');
  g.fillStyle = vig;
  g.fillRect(0, 0, CARD_W, CARD_H);

  // Frame.
  roundRect(g, 14, 14, CARD_W - 28, CARD_H - 28, 30);
  g.strokeStyle = rgba(tint, 0.55);
  g.lineWidth = 2;
  g.stroke();
}

const GOLD = '#ffd45e';

function drawPlayer(g, p, { cx, cy, won, lost, tint, img }) {
  const r = won ? 104 : lost ? 90 : 96;
  g.save();
  g.globalAlpha = lost ? 0.78 : 1;

  // Halo.
  const halo = g.createRadialGradient(cx, cy, r * 0.6, cx, cy, r * 1.7);
  halo.addColorStop(0, rgba(won ? GOLD : tint, won ? 0.45 : 0.22));
  halo.addColorStop(1, rgba(won ? GOLD : tint, 0));
  g.fillStyle = halo;
  g.fillRect(cx - r * 2, cy - r * 2, r * 4, r * 4);

  // Face.
  g.save();
  g.beginPath();
  g.arc(cx, cy, r, 0, Math.PI * 2);
  g.clip();
  if (img) {
    const s = Math.max((r * 2) / img.naturalWidth, (r * 2) / img.naturalHeight);
    const w = img.naturalWidth * s;
    const h = img.naturalHeight * s;
    g.drawImage(img, cx - w / 2, cy - h / 2, w, h);
  } else {
    g.fillStyle = tileColorFor(p.name);
    g.fillRect(cx - r, cy - r, r * 2, r * 2);
    const shine = g.createLinearGradient(cx, cy - r, cx, cy + r);
    shine.addColorStop(0, 'rgba(255,255,255,0.22)');
    shine.addColorStop(1, 'rgba(0,0,0,0.25)');
    g.fillStyle = shine;
    g.fillRect(cx - r, cy - r, r * 2, r * 2);
    g.fillStyle = '#fff';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.font = '600 ' + Math.round(r * 1.05) + 'px ' + FONT_STACK;
    g.fillText(initialOf(p.name), cx, cy + r * 0.05);
  }
  g.restore();

  // Ring.
  g.beginPath();
  g.arc(cx, cy, r + 5, 0, Math.PI * 2);
  g.strokeStyle = won ? GOLD : rgba(tint, 0.85);
  g.lineWidth = won ? 8 : 4;
  g.stroke();

  // Name.
  g.textAlign = 'center';
  g.textBaseline = 'alphabetic';
  g.fillStyle = '#fff';
  fitFont(g, p.name, 300, 34, '600');
  g.fillText(p.name, cx, cy + r + 52);

  // Score.
  g.fillStyle = won ? GOLD : '#e8d8ee';
  if (won) { g.shadowColor = rgba(GOLD, 0.6); g.shadowBlur = 24; }
  fitFont(g, String(p.score), 300, 88, '700');
  g.fillText(String(p.score), cx, cy + r + 140);
  g.shadowBlur = 0;
  g.restore();

  // The pill, drawn at full alpha even on a dimmed side: it never belongs to the loser.
  if (won) {
    const label = 'WINNER';
    g.font = '700 17px ' + FONT_STACK;
    const w = 118;
    const y = cy - r - 44;
    roundRect(g, cx - w / 2, y, w, 30, 15);
    g.fillStyle = GOLD;
    g.fill();
    g.fillStyle = '#2a1204';
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    spacedText(g, label, cx, y + 16, 3);
  }
}

function drawCentre(g, data, word, tint) {
  g.textAlign = 'center';

  // Flavour name.
  g.textBaseline = 'alphabetic';
  g.fillStyle = tint;
  g.font = '600 19px ' + FONT_STACK;
  spacedText(g, (FLAVOUR_NAMES[data.flavour] || 'Goon Game').toUpperCase(), 600, 96, 6);

  // The word.
  const size = fitFont(g, word, 560, 92, '700', 40);
  const grad = g.createLinearGradient(600 - 280, 0, 600 + 280, 0);
  grad.addColorStop(0, '#ff69b4');
  grad.addColorStop(0.5, '#ffe0f0');
  grad.addColorStop(1, tint);
  g.save();
  g.shadowColor = rgba(tint, 0.7);
  g.shadowBlur = 30;
  g.fillStyle = grad;
  g.fillText(word, 600, 100 + size * 0.95);
  g.restore();

  // Verdict. Time survived is deliberately NOT on the card: sending and duels tell it.
  g.fillStyle = '#e8d8ee';
  fitFont(g, verdictLine(data.outcome, data.you.name, data.them.name), 520, 25, '500');
  g.fillText(verdictLine(data.outcome, data.you.name, data.them.name), 600, 236);

  // Stat table: you | label | them.
  const lines = statLines(data);
  const top = 300;
  const rowH = 50;
  lines.forEach((line, i) => {
    const y = top + i * rowH;
    roundRect(g, 404, y - 30, 392, 42, 21);
    g.fillStyle = 'rgba(14, 7, 22, 0.72)';
    g.fill();
    g.fillStyle = 'rgba(255,255,255,0.05)';
    g.fill();
    g.strokeStyle = rgba(tint, 0.22);
    g.lineWidth = 1;
    g.stroke();

    const youBig = line.you != null && line.them != null && line.you > line.them;
    const themBig = line.you != null && line.them != null && line.them > line.you;
    g.textBaseline = 'alphabetic';
    g.font = '700 25px ' + FONT_STACK;
    g.textAlign = 'left';
    g.fillStyle = youBig ? GOLD : '#fff';
    g.fillText(line.you == null ? '-' : String(line.you), 426, y);
    g.textAlign = 'right';
    g.fillStyle = themBig ? GOLD : '#fff';
    g.fillText(line.them == null ? '-' : String(line.them), 774, y);
    g.textAlign = 'center';
    g.fillStyle = '#cbb3d6';
    fitFont(g, line.label.toUpperCase(), 230, 15, '600', 10);
    spacedText(g, line.label.toUpperCase(), 600, y - 3, 1.5);
  });

  // Highlights, as small pills under the table.
  const pills = data.highlights || [];
  if (pills.length) {
    g.font = '600 16px ' + FONT_STACK;
    const pad = 18;
    const widths = pills.map((h) => g.measureText(h).width + pad * 2);
    const gap = 10;
    let x = 600 - (widths.reduce((a, b) => a + b, 0) + gap * (pills.length - 1)) / 2;
    const y = top + Math.max(lines.length, 1) * rowH - 12;
    pills.forEach((h, i) => {
      roundRect(g, x, y, widths[i], 32, 16);
      g.fillStyle = rgba(tint, 0.18);
      g.fill();
      g.strokeStyle = rgba(tint, 0.6);
      g.stroke();
      g.fillStyle = '#fff';
      g.textAlign = 'center';
      g.textBaseline = 'middle';
      g.fillText(h, x + widths[i] / 2, y + 17);
      x += widths[i] + gap;
    });
  }
}

/** The logo's crop with its flat dark ground keyed to transparent (soft ramp on brightness). */
function keyedLogo(logo, sx, sy, sw, sh) {
  try {
    const c = doc().createElement('canvas');
    c.width = Math.round(sw); c.height = Math.round(sh);
    const x = c.getContext('2d');
    x.drawImage(logo, sx, sy, sw, sh, 0, 0, c.width, c.height);
    const im = x.getImageData(0, 0, c.width, c.height);
    const p = im.data;
    for (let i = 0; i < p.length; i += 4) {
      const v = Math.max(p[i], p[i + 1], p[i + 2]);
      p[i + 3] = Math.round(p[i + 3] * Math.max(0, Math.min(1, (v - 40) / 50)));
    }
    x.putImageData(im, 0, 0);
    return c;
  } catch (_e) { return null; }
}

function drawChrome(g, data, logo) {
  // Mark, top left.
  if (logo) {
    // The logo is a 1024 square on a flat dark ground: crop to the lettering and
    // 'lighten' it on, so the ground vanishes into the card instead of boxing it.
    const sx = logo.naturalWidth * 0.07, sy = logo.naturalHeight * 0.26;
    const sw = logo.naturalWidth * 0.87, sh = logo.naturalHeight * 0.60;
    const w = 150, h = w * (sh / sw);
    const keyed = keyedLogo(logo, sx, sy, sw, sh);
    if (keyed) g.drawImage(keyed, 34, 26, w, h);
    else g.drawImage(logo, sx, sy, sw, sh, 34, 26, w, h);
  } else {
    g.fillStyle = '#fff';
    g.textAlign = 'left';
    g.textBaseline = 'alphabetic';
    g.font = '700 24px ' + FONT_STACK;
    spacedText(g, 'GOON GAME', 44, 70, 3);
  }
  // Date and home, top right.
  g.textAlign = 'right';
  g.textBaseline = 'alphabetic';
  g.fillStyle = '#cbb3d6';
  g.font = '600 18px ' + FONT_STACK;
  g.fillText(cardDate(data.date), CARD_W - 44, 60);
  g.fillStyle = '#907a9e';
  g.font = '500 16px ' + FONT_STACK;
  g.fillText('CC Labs  -  cclabs.app', CARD_W - 44, 84);
}

/** Paint `data` onto a 2D context already scaled to card units. Pure drawing, no awaits. */
export function paintCard(g, data, { you = null, them = null, logo = null } = {}) {
  const tint = FLAVOUR_TINTS[data.flavour] || FLAVOUR_TINTS.plain;
  const word = flavourWord(data.flavour, data.outcome, data.seed);
  const turn = (Number(BigInt.asUintN(16, typeof data.seed === 'bigint' ? data.seed : BigInt(Math.floor(Number(data.seed) || 0)))) / 65536) * Math.PI * 2;
  drawBackground(g, tint, turn);
  drawChrome(g, data, logo);
  const w = data.outcome === 'w';
  const l = data.outcome === 'l';
  drawPlayer(g, data.you, { cx: 222, cy: 300, won: w, lost: l, tint, img: you });
  drawPlayer(g, data.them, { cx: 978, cy: 300, won: l, lost: w, tint, img: them });
  drawCentre(g, data, word, tint);
  return word;
}

/**
 * The card, fonts and faces loaded first.
 * @returns {Promise<{canvas: HTMLCanvasElement, word: string}|null>}
 */
export async function renderCard(data) {
  try {
    const d = doc();
    if (!d) return null;
    const [, logo, you, them] = await Promise.all([
      loadCardFont(), loadLogo(), loadImage(data.you.avatarUrl), loadImage(data.them.avatarUrl),
    ]);
    const canvas = d.createElement('canvas');
    canvas.width = CARD_W * CARD_SCALE;
    canvas.height = CARD_H * CARD_SCALE;
    const g = canvas.getContext('2d');
    if (!g) return null;
    g.scale(CARD_SCALE, CARD_SCALE);
    const word = paintCard(g, data, { you, them, logo });
    return { canvas, word };
  } catch (_e) { return null; }
}

/* ------------------------------------------------------------ copy / save */

export function canvasBlob(canvas) {
  return new Promise((resolve, reject) => {
    try { canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('no blob'))), 'image/png'); }
    catch (e) { reject(e); }
  });
}

function pngBase64(canvas) {
  const url = canvas.toDataURL('image/png');
  return url.startsWith(PNG_PREFIX) ? url.slice(PNG_PREFIX.length) : '';
}

let hostSeq = 0;
const pending = new Map();
let listening = false;
function listen() {
  if (listening) return;
  listening = true;
  try {
    bridge.on('share-card-result', (m) => {
      const fn = pending.get(String(m.id || ''));
      if (fn) { pending.delete(String(m.id || '')); fn({ ok: !!m.ok, error: String(m.error || '') }); }
    });
  } catch (_e) { /* someone else registered it; we simply never hear back and time out */ }
}

/** Hand the PNG to the host. Resolves { ok, error } ('cancelled' for a closed save dialog). */
function askHost(action, canvas, name) {
  listen();
  return new Promise((resolve) => {
    const id = 'sc' + (++hostSeq);
    const timer = setTimeout(() => { pending.delete(id); resolve({ ok: false, error: 'timeout' }); }, action === 'save' ? 120000 : HOST_TIMEOUT_MS);
    pending.set(id, (r) => { clearTimeout(timer); resolve(r); });
    try {
      bridge.send({ type: 'share-card', id, action, name, png: pngBase64(canvas) });
    } catch (e) {
      clearTimeout(timer);
      pending.delete(id);
      resolve({ ok: false, error: String((e && e.message) || e) });
    }
  });
}

/** Copy the card as an image. @returns {Promise<{ok:boolean, via:string, error?:string}>} */
export async function copyCard(canvas) {
  try {
    if (typeof navigator !== 'undefined' && navigator.clipboard && typeof navigator.clipboard.write === 'function'
      && typeof ClipboardItem === 'function') {
      // The blob goes in as a PROMISE so the write keeps the click's user activation.
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': canvasBlob(canvas) })]);
      return { ok: true, via: 'browser' };
    }
  } catch (_e) { /* WebView2 can refuse; the host is the next road */ }
  if (bridge.isHosted) {
    const r = await askHost('copy', canvas, '');
    return { ok: r.ok, via: 'host', error: r.error };
  }
  return { ok: false, via: 'none', error: 'no clipboard' };
}

/** Save the card as a PNG. @returns {Promise<{ok:boolean, via:string, error?:string}>} */
export async function saveCard(canvas, fileName) {
  const name = String(fileName || 'goon-game.png').replace(/[^a-zA-Z0-9._-]+/g, '-');
  if (bridge.isHosted) {
    const r = await askHost('save', canvas, name);
    return { ok: r.ok, via: 'host', error: r.error };
  }
  try {
    const d = doc();
    const blob = await canvasBlob(canvas);
    const url = URL.createObjectURL(blob);
    const a = d.createElement('a');
    a.href = url;
    a.download = name;
    d.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => { try { URL.revokeObjectURL(url); } catch (_e) { /* gone */ } }, 5000);
    return { ok: true, via: 'download' };
  } catch (e) { return { ok: false, via: 'download', error: String((e && e.message) || e) }; }
}

/** 'goon-game-2026-09-24.png' */
export function cardFileName(ms) {
  const d = new Date(Number(ms) || Date.now());
  const p = (n) => String(n).padStart(2, '0');
  return 'goon-game-' + d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + '.png';
}
