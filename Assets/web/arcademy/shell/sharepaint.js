/* ============================================================================
 * shell/sharepaint.js - THE SLIP, painted.
 *
 * The browser half of the share card: `shell/sharecard.js` decides WHERE every
 * mark on the paper goes (pure maths, asserted in node), and this file puts ink
 * on it - the grain, the crest, the rubber stamps, the foil seal, the torn
 * edge - then exports the PNG under the school's 400 KB ceiling.
 *
 * Nothing here reads the lexicon either. The anonymity law lives one file over
 * and this half never has a chance to break it: it letters what it is handed.
 *
 * Everything below needs a real browser, which is exactly why it is not in the
 * same file as the maths - the headless DOM double every suite runs on has no
 * canvas, and importing this from reportcard.js would take the report card down
 * with it if it did anything at import time. It does not: `canRenderCard()` is
 * the gate, and the report card asks before it draws a button.
 * ==========================================================================*/

import {
  CARD_W, CARD_H, MAX_BYTES, PAD_R, TEAR_H,
  INK, DISPLAY_FACE, BODY_FACE, MONO_FACE, layoutCard,
} from './sharecard.js';
import makeRng from '../core/rng.js';

/** True when this host can actually draw and export a PNG. */
export function canRenderCard() {
  try {
    if (typeof document === 'undefined' || typeof document.createElement !== 'function') return false;
    const c = document.createElement('canvas');
    return !!(c && typeof c.getContext === 'function' && c.getContext('2d'));
  } catch (e) { return false; }
}

/** Load one same-origin image, or null. Never throws, never hangs forever. */
function loadArt(src) {
  return new Promise((resolve) => {
    try {
      if (typeof Image !== 'function') { resolve(null); return; }
      const img = new Image();
      let settled = false;
      const done = (v) => { if (!settled) { settled = true; resolve(v); } };
      img.onload = () => done(img);
      img.onerror = () => done(null);
      setTimeout(() => done(null), 4000);
      img.src = src;
    } catch (e) { resolve(null); }
  });
}

/** Ask for the bundled display face once. A miss is a fallback, not an error. */
async function warmFace() {
  try {
    if (typeof document === 'undefined' || !document.fonts || typeof document.fonts.load !== 'function') return;
    await Promise.race([
      Promise.all([
        document.fonts.load("400 64px 'Arcademy Display'"),
        document.fonts.load("400 30px 'Arcademy Display'"),
      ]),
      new Promise((r) => setTimeout(r, 1200)),
    ]);
  } catch (e) { /* the fallback stack is a real look, not a failure */ }
}

function tracked(ctx, text, cx, y, spacing, align) {
  const str = String(text || '');
  let total = 0;
  for (const ch of str) total += ctx.measureText(ch).width + spacing;
  total -= spacing;
  let x = align === 'left' ? cx : cx - total / 2;
  for (const ch of str) {
    ctx.fillText(ch, x, y);
    x += ctx.measureText(ch).width + spacing;
  }
  return total;
}

/** The dotted leader between a class name and its stamp. */
function leader(ctx, x1, x2, y) {
  if (x2 - x1 < 20) return;
  ctx.save();
  ctx.strokeStyle = INK.inkFaint;
  ctx.globalAlpha = 0.55;
  ctx.lineWidth = 2;
  ctx.setLineDash([2, 10]);
  ctx.beginPath();
  ctx.moveTo(x1, y);
  ctx.lineTo(x2, y);
  ctx.stroke();
  ctx.restore();
}

/** The rubber stamp: a tilted ring, an ink-bleed edge and the letter. */
function rubberStamp(ctx, box, letter) {
  if (!letter) {
    ctx.save();
    ctx.strokeStyle = INK.inkFaint;
    ctx.globalAlpha = 0.4;
    ctx.lineWidth = 2;
    ctx.setLineDash([5, 7]);
    ctx.beginPath();
    ctx.arc(box.cx, box.cy, box.r * 0.78, 0, Math.PI * 2);
    ctx.stroke();
    ctx.restore();
    return;
  }
  ctx.save();
  ctx.translate(box.cx, box.cy);
  ctx.rotate(box.tilt);
  /* THE BLEED. A real rubber stamp lays down more ink than the die, so the
   * outer edge is a soft second pass under the crisp one. */
  ctx.globalAlpha = 0.22;
  ctx.strokeStyle = INK.stamp;
  ctx.lineWidth = 11;
  ctx.beginPath();
  ctx.arc(0, 0, box.r * 0.82, 0, Math.PI * 2);
  ctx.stroke();
  ctx.globalAlpha = 0.92;
  ctx.lineWidth = 5;
  ctx.beginPath();
  ctx.arc(0, 0, box.r * 0.82, 0, Math.PI * 2);
  ctx.stroke();
  ctx.fillStyle = INK.stamp;
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.font = '400 ' + Math.round(box.r * 1.06) + "px " + DISPLAY_FACE;
  ctx.fillText(letter, 0, box.r * 0.06);
  ctx.restore();
}

/** The embossed gold seal. Foil, a rope edge, and no baked lexicon in sight. */
function foilSeal(ctx, seal) {
  const { cx, cy, r } = seal;
  ctx.save();
  /* the foil: a lit rim and a shadowed one, which is what reads as embossing */
  const g = ctx.createLinearGradient(cx - r, cy - r, cx + r, cy + r);
  g.addColorStop(0, '#FFDF92');
  g.addColorStop(0.45, INK.gold);
  g.addColorStop(1, INK.goldDeep);
  ctx.fillStyle = g;
  ctx.beginPath();
  ctx.arc(cx, cy, r, 0, Math.PI * 2);
  ctx.fill();
  ctx.strokeStyle = INK.goldDeep;
  ctx.lineWidth = 3;
  ctx.stroke();
  /* the rope edge - 40 teeth, the way a real seal is milled */
  ctx.strokeStyle = 'rgba(255,255,255,.5)';
  ctx.lineWidth = 2;
  for (let i = 0; i < 40; i += 1) {
    const a = (i / 40) * Math.PI * 2;
    ctx.beginPath();
    ctx.moveTo(cx + Math.cos(a) * (r - 12), cy + Math.sin(a) * (r - 12));
    ctx.lineTo(cx + Math.cos(a) * (r - 3), cy + Math.sin(a) * (r - 3));
    ctx.stroke();
  }
  ctx.strokeStyle = 'rgba(26,26,46,.35)';
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.arc(cx, cy, r - 18, 0, Math.PI * 2);
  ctx.stroke();
  /* the words, pressed IN: a dark line offset up by one px under a light one */
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  const lines = ['PERFECT', 'ATTENDANCE'];
  const maxW = (r - 26) * 2;
  lines.forEach((line, i) => {
    /* FIT, NEVER CROP. The longer word decides its own size - a seal whose
     * legend runs off the milled edge reads as a rendering bug, not a seal. */
    let size = 24;
    while (size > 12) {
      ctx.font = '400 ' + size + 'px ' + DISPLAY_FACE;
      if (ctx.measureText(line).width + line.length * 2 <= maxW) break;
      size -= 1;
    }
    const ly = cy - 15 + i * 32;
    ctx.fillStyle = 'rgba(90,62,10,.75)';
    tracked(ctx, line, cx, ly + 1, 2);
    ctx.fillStyle = '#FFF3D0';
    tracked(ctx, line, cx, ly, 2);
  });
  ctx.fillStyle = 'rgba(90,62,10,.6)';
  ctx.font = '400 16px ' + MONO_FACE;
  ctx.fillText('*', cx, cy + 44);
  ctx.restore();
}

/** One meter segment: the school's own star stamp, or an empty ruled well. */
function meterCell(ctx, cell, stampArt) {
  const { x, y, size, on } = cell;
  if (on && stampArt) {
    ctx.save();
    /* THE STAR IS PIXEL ART, so it is resampled the way this school resamples
     * pixel art everywhere else - nearest neighbour. Smoothing a 640px sprite
     * down to 62px mints a few thousand in-between shades, which is both softer
     * than the house look and, ten of them across a sheet, ~70 KB of PNG. */
    ctx.imageSmoothingEnabled = false;
    ctx.globalAlpha = 0.95;
    ctx.drawImage(stampArt, x, y, size, size);
    ctx.restore();
    return;
  }
  ctx.save();
  ctx.translate(x + size / 2, y + size / 2);
  if (on) {
    /* no art loaded - draw the star rather than lose the segment */
    ctx.fillStyle = INK.stamp;
    ctx.beginPath();
    for (let i = 0; i < 10; i += 1) {
      const rr = i % 2 === 0 ? size * 0.46 : size * 0.2;
      const a = -Math.PI / 2 + (i * Math.PI) / 5;
      const px = Math.cos(a) * rr;
      const py = Math.sin(a) * rr;
      if (i === 0) ctx.moveTo(px, py); else ctx.lineTo(px, py);
    }
    ctx.closePath();
    ctx.fill();
  } else {
    ctx.strokeStyle = INK.inkFaint;
    ctx.globalAlpha = 0.45;
    ctx.lineWidth = 2;
    ctx.setLineDash([3, 6]);
    ctx.beginPath();
    ctx.arc(0, 0, size * 0.38, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.restore();
}

/** The paper's own outline: square at the top, torn along the bottom. */
function paperPath(ctx, L, rng) {
  ctx.beginPath();
  ctx.moveTo(0, 0);
  ctx.lineTo(L.w, 0);
  ctx.lineTo(L.w, L.tearY);
  /* A TEAR IS A WALK, NOT NOISE. Sampling the rng fresh at every step draws a
   * saw blade; carrying the last height and nudging it draws paper. */
  const STEPS = 52;
  let jag = TEAR_H * 0.45;
  for (let i = STEPS; i >= 0; i -= 1) {
    const x = (L.w / STEPS) * i;
    jag = Math.max(6, Math.min(TEAR_H - 4, jag + (rng() - 0.5) * TEAR_H * 0.7));
    ctx.lineTo(x, L.tearY + jag);
  }
  ctx.lineTo(0, L.tearY);
  ctx.closePath();
}

/**
 * Draw the slip onto a canvas.
 *
 * @param {Object} state   see layoutCard()
 * @param {Object=} opts   {scale, art} - `art:false` draws without the two
 *                         bundled PNGs, which is the retry path for a host that
 *                         refuses to export a canvas an image has touched.
 * @returns {Promise<?HTMLCanvasElement>}
 */
export async function drawShareCard(state, opts) {
  if (!canRenderCard()) return null;
  const o = opts || {};
  const scale = Math.max(1, Math.min(2, Number(o.scale) || 1));
  const L = layoutCard(state);
  const base = (typeof o.base === 'string' && o.base) ? o.base : './';

  const wantArt = o.art !== false;
  const [markArt, stampArt] = wantArt
    ? await Promise.all([
      loadArt(base + 'art/arcademy-wordmark.png'),
      loadArt(base + 'art/punchcard/stamp.png'),
    ])
    : [null, null];
  await warmFace();

  const canvas = document.createElement('canvas');
  canvas.width = Math.round(CARD_W * scale);
  canvas.height = Math.round(CARD_H * scale);
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.scale(scale, scale);
  ctx.textBaseline = 'alphabetic';

  const rng = makeRng('arcademy-share-paper|' + String((state && state.dateLabel) || ''));

  ctx.save();
  paperPath(ctx, L, makeRng('arcademy-share-tear|' + String((state && state.dateLabel) || '')));
  ctx.clip();

  /* --- the stock --------------------------------------------------------- */
  /* THE STOCK IS ONE FLAT CREAM, and that is a FILE-SIZE decision as much as a
   * look. Skia DITHERS every canvas gradient - it sprays a pixel of noise over
   * the whole ramp so a ramp never bands - and noise is exactly what a PNG
   * cannot pack: the two versions of this fill that were gradients exported at
   * 1.25 MB and 485 KB against a 400 KB ceiling. The depth the ramp was buying
   * comes back below, as banded flecks, for about 8 KB. */
  ctx.fillStyle = INK.paper;
  ctx.fillRect(0, 0, CARD_W, CARD_H);

  /* THE GRAIN, and it is SPARSE ON PURPOSE. Full-field noise is incompressible
   * and would push a 1200x1600 PNG past the 400 KB the owner set; 2200 soft
   * flecks read as paper and cost almost nothing. */
  ctx.save();
  ctx.fillStyle = '#7A6A4E';
  for (let i = 0; i < 900; i += 1) {
    /* three alpha steps, not a continuum: a fleck that can be any of 200 shades
     * is 200 palette entries the encoder has to carry for no visible gain */
    ctx.globalAlpha = 0.03 + Math.floor(rng() * 3) * 0.02;
    ctx.fillRect(Math.round(rng() * CARD_W), Math.round(rng() * CARD_H),
      1 + Math.round(rng()), 1 + Math.round(rng()));
  }
  ctx.restore();

  /* the stub's shadow line, then THE ONE PINK THING */
  ctx.save();
  ctx.strokeStyle = INK.inkFaint;
  ctx.globalAlpha = 0.5;
  ctx.lineWidth = 1.5;
  ctx.setLineDash([9, 9]);
  ctx.beginPath();
  ctx.moveTo(0, L.perf.y);
  ctx.lineTo(CARD_W, L.perf.y);
  ctx.stroke();
  ctx.restore();

  ctx.save();
  ctx.strokeStyle = INK.pink;
  ctx.globalAlpha = 0.55;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(L.margin.x, L.margin.top);
  ctx.lineTo(L.margin.x, L.margin.bottom);
  ctx.stroke();
  ctx.restore();

  /* --- the crest --------------------------------------------------------- */
  if (markArt && markArt.width) {
    const ratio = markArt.height / markArt.width;
    const w = L.mark.w;
    const h = Math.min(L.mark.h, w * ratio);
    ctx.drawImage(markArt, L.mark.x, L.mark.y + (L.mark.h - h) / 2, w, h);
  } else {
    ctx.save();
    ctx.fillStyle = INK.ink;
    ctx.textAlign = 'center';
    ctx.font = '400 64px ' + DISPLAY_FACE;
    tracked(ctx, 'THE ARCADEMY', CARD_W / 2, L.mark.y + L.mark.h * 0.62, 6);
    ctx.restore();
  }

  /* --- kicker, rule, date, identity -------------------------------------- */
  ctx.save();
  ctx.fillStyle = INK.ink;
  ctx.textAlign = 'center';
  ctx.font = '400 ' + L.kicker.size + 'px ' + DISPLAY_FACE;
  tracked(ctx, L.kicker.text, L.kicker.x, L.kicker.y, 8);
  ctx.restore();

  ctx.save();
  ctx.strokeStyle = INK.pink;
  ctx.lineWidth = 3;
  ctx.beginPath();
  ctx.moveTo(L.kickerRule.x, L.kickerRule.y);
  ctx.lineTo(L.kickerRule.x + L.kickerRule.w, L.kickerRule.y);
  ctx.stroke();
  ctx.restore();

  ctx.save();
  ctx.fillStyle = INK.inkDim;
  ctx.textAlign = 'center';
  ctx.font = '400 ' + L.date.size + 'px ' + MONO_FACE;
  ctx.fillText(String((state && state.dateLabel) || ''), L.date.x, L.date.y);
  if (L.ident) {
    const id = state.identity || {};
    ctx.fillStyle = INK.ink;
    ctx.font = '400 ' + L.ident.size + 'px ' + BODY_FACE;
    const line = [id.name, id.number].filter(Boolean).join('   No. ');
    ctx.fillText(line, L.ident.x, L.ident.y);
  }
  ctx.restore();

  /* --- the table --------------------------------------------------------- */
  ctx.save();
  ctx.fillStyle = INK.inkFaint;
  ctx.font = '400 ' + L.head.size + 'px ' + MONO_FACE;
  ctx.textAlign = 'left';
  tracked(ctx, 'CLASS', L.head.left, L.head.y, 4, 'left');
  ctx.textAlign = 'center';
  tracked(ctx, 'GRADE', L.head.gradeX, L.head.y, 4);
  ctx.restore();

  ctx.save();
  ctx.strokeStyle = INK.inkDim;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(L.headRule.x, L.headRule.y);
  ctx.lineTo(L.headRule.x + L.headRule.w, L.headRule.y);
  ctx.stroke();
  ctx.restore();

  for (const row of L.rows) {
    ctx.save();
    ctx.fillStyle = INK.ink;
    ctx.textAlign = 'left';
    ctx.font = '400 30px ' + BODY_FACE;
    const nameW = ctx.measureText(row.name).width;
    ctx.fillText(row.name, L.contentX, row.textY);
    ctx.restore();
    leader(ctx, L.contentX + nameW + 18, row.stamp.cx - row.stamp.r - 10, row.textY - 8);
    rubberStamp(ctx, row.stamp, row.grade);
    ctx.save();
    ctx.strokeStyle = INK.inkDim;
    ctx.globalAlpha = 0.35;
    ctx.lineWidth = 1.5;
    ctx.setLineDash([4, 6]);
    ctx.beginPath();
    ctx.moveTo(L.contentX, row.ruleY);
    ctx.lineTo(CARD_W - PAD_R, row.ruleY);
    ctx.stroke();
    ctx.restore();
  }

  /* --- attendance -------------------------------------------------------- */
  ctx.save();
  ctx.fillStyle = INK.inkFaint;
  ctx.font = '400 ' + L.attend.size + 'px ' + MONO_FACE;
  ctx.textAlign = 'left';
  tracked(ctx, 'ATTENDANCE', L.attend.x, L.attend.y, 4, 'left');
  ctx.fillStyle = INK.ink;
  ctx.textAlign = 'right';
  ctx.font = '400 26px ' + DISPLAY_FACE;
  ctx.fillText(String(Math.max(0, Math.round(Number((state && state.streak) || 0)))) + ' DAYS',
    L.attend.countX, L.attend.y + 2);
  ctx.restore();

  for (const cell of L.meter.cells) meterCell(ctx, cell, stampArt);

  /* --- remarks ----------------------------------------------------------- */
  ctx.save();
  ctx.fillStyle = INK.inkFaint;
  ctx.font = '400 ' + L.remarks.size + 'px ' + MONO_FACE;
  ctx.textAlign = 'left';
  tracked(ctx, 'REMARKS', L.remarks.x, L.remarks.y, 4, 'left');
  ctx.strokeStyle = INK.inkDim;
  ctx.globalAlpha = 0.3;
  ctx.lineWidth = 1.5;
  for (const ly of L.remarks.lines) {
    ctx.beginPath();
    ctx.moveTo(L.remarks.x, ly);
    ctx.lineTo(L.remarks.right, ly);
    ctx.stroke();
  }
  ctx.restore();

  if (L.seal) foilSeal(ctx, L.seal);

  /* --- the date line ----------------------------------------------------- */
  ctx.save();
  ctx.strokeStyle = INK.inkDim;
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(L.foot.x, L.foot.ruleY);
  ctx.lineTo(L.foot.x + L.foot.w, L.foot.ruleY);
  ctx.stroke();
  ctx.fillStyle = INK.inkFaint;
  ctx.font = '400 ' + L.foot.size + 'px ' + MONO_FACE;
  ctx.textAlign = 'left';
  tracked(ctx, 'SIGNED, THE OFFICE OF RECORDS', L.foot.x, L.foot.labelY, 3, 'left');
  ctx.textAlign = 'right';
  ctx.fillText('DATED ' + String((state && state.dateLabel) || ''),
    L.foot.x + L.foot.w, L.foot.ruleY - 12);
  ctx.restore();

  ctx.restore();   // the clip

  /* --- the perforation, punched THROUGH ---------------------------------- */
  ctx.save();
  ctx.globalCompositeOperation = 'destination-out';
  for (const x of L.perf.xs) {
    ctx.beginPath();
    ctx.arc(x, L.perf.y, L.perf.r, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();

  return canvas;
}

/** Canvas -> PNG blob. Resolves null rather than throwing on a tainted canvas. */
export function canvasToBlob(canvas) {
  return new Promise((resolve) => {
    try {
      if (!canvas || typeof canvas.toBlob !== 'function') { resolve(null); return; }
      canvas.toBlob((b) => resolve(b || null), 'image/png');
    } catch (e) { resolve(null); }
  });
}

/**
 * THE ONE ENTRY POINT the report card calls.
 *
 * Draws at 2x on a retina device, exports, and - if that PNG came out over the
 * 400 KB ceiling - redraws at 1x rather than handing a chat client something it
 * will re-compress into mush. A canvas the host refuses to export (an image the
 * browser decided was cross-origin) is retried once with `art:false`, which is
 * the same slip with the wordmark drawn as type.
 *
 * @returns {Promise<?{blob:Blob, canvas:HTMLCanvasElement, scale:number}>}
 */
export async function renderShareCard(state, opts) {
  const o = opts || {};
  let scale = Number(o.scale) || 0;
  if (!scale) {
    let dpr = 1;
    try { dpr = Number(window.devicePixelRatio) || 1; } catch (e) { dpr = 1; }
    scale = dpr >= 2 ? 2 : 1;
  }
  for (const art of [true, false]) {
    const canvas = await drawShareCard(state, Object.assign({}, o, { scale, art }));
    if (!canvas) return null;
    let blob = await canvasToBlob(canvas);
    if (!blob && art) continue;          // tainted - redraw without the PNGs
    if (!blob) return null;
    if (blob.size > MAX_BYTES && scale > 1) {
      const small = await drawShareCard(state, Object.assign({}, o, { scale: 1, art }));
      const smallBlob = small ? await canvasToBlob(small) : null;
      if (smallBlob) return { blob: smallBlob, canvas: small, scale: 1 };
    }
    return { blob, canvas, scale };
  }
  return null;
}

export default renderShareCard;
