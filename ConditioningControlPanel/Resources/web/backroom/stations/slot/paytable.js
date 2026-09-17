/* ============================================================================
 * paytable.js - WHAT THE COMBINATIONS LOOK LIKE.
 *
 * The owner's ask (2026-09-16): "show the prize payout with simple mockups -
 * polaroids for flashes, real spirals from the loom for spirals, the words for
 * the triggers - and what combinations they can get and what it pays."
 *
 * So the paytable stopped being a list of nouns ("3 of the same GIF") and became
 * a PICTURE OF THE ROW that pays. Every cell here is painted by the reels' own
 * `drawSymbol` (symbols.js) at the reels' own cell box, scaled down: a spiral in
 * this panel is the same Loom field the reel shows (CONTRACT 10.13.D), a trigger
 * cell is the player's OWN dealt word, and a flash cell is the player's OWN
 * dealt GIF. Hand-drawn mock-ups would drift from the reels the first time
 * either changed, and this legend is meant to be read against the glass.
 *
 * THE ONE THING ADDED ON TOP is the polaroid: a flash is a photo, so it wears a
 * cream card with a bottom lip and a degree of tilt. At 48 px that reads as
 * "a picture you were shown", where the reels' own rounded tile reads as nothing
 * in particular. The tilt is a constant per COLUMN, never random: a legend that
 * shuffles when you reopen it is a toy.
 *
 * `any` is the wildcard (`emi2` is two EMI and ANYTHING, `sub2` is EXACTLY two
 * triggers). It is dashed and empty rather than a third symbol, because a real
 * symbol there would read as part of the combination.
 *
 * Pure canvas: no DOM beyond a 2D context, no three, so node:test holds it. The
 * caller owns the clock, the deal and the device ratio.
 * ==========================================================================*/

import { drawSymbol, kindOf, CELL } from './symbols.js';

/** 12 Hz, the decode clock (room/gif-decode.js MAX_FPS): a flash cell cannot show a new pixel faster. */
export const PAY_MS = 83;
/** One legend cell in CSS px, at the REELS' own aspect, so the panel is a miniature and not a re-draw. */
export const CELL_W = 48, CELL_H = Math.round(48 * CELL.hh / CELL.hw);   // 48 x 27
export const GAP = 5, PAD = 3;

/* THE ROWS, and they are CONTRACT section 4's rows in its order. The ids are reel symbol ids, so each row
 * carries its own dealt media: `gif1` three times is literally "3 of the SAME gif", `gif0 gif1 gif2` is
 * "3 GIFs, any mix", and the difference can be SEEN instead of read. */
export const COMBOS = {
  emi3: ['emi', 'emi', 'emi'],
  emi2: ['emi', 'emi', 'any'],
  gif3same: ['gif1', 'gif1', 'gif1'],
  sub3: ['sub0', 'sub1', 'sub2'],
  spiral3: ['spiral0', 'spiral1', 'spiral2'],
  gif3: ['gif0', 'gif1', 'gif2'],
  sub2: ['sub0', 'sub1', 'any'],
  spiral2: ['spiral0', 'spiral1', 'any'],
  melt: ['melt', 'any', 'any'],
};

const TILT = [-0.05, 0.028, -0.018];     // per COLUMN, never random
const CARD = '#f4ead9', CARD_EDGE = '#00000040';

/** The canvas a row of `n` cells needs, in CSS px. The caller multiplies by the device ratio. */
export const comboSize = (n = 3) => ({ w: n * CELL_W + Math.max(0, n - 1) * GAP + PAD * 2, h: CELL_H + PAD * 2 });

/** The reel's own cell, contained in this rect and clipped to it. The scale is uniform and taken off the
 *  REEL cell box, so the legend cell is the reel cell shrunk - same fit, same margins, same composition.
 *  A dealt GIF that cannot be read (tainted, mid-load) throws inside drawSymbol; the cell keeps its ground
 *  rather than costing the whole row. */
function symbolInto(g, id, x, y, w, h, r, t, look) {
  g.save();
  g.beginPath(); g.roundRect(x, y, w, h, r); g.clip();
  g.fillStyle = '#271632'; g.fill();
  g.translate(x + w / 2, y + h / 2);
  const k = Math.min(w / (CELL.hw * 2), h / (CELL.hh * 2));
  g.scale(k, k);
  try { drawSymbol(g, id, t, look); } catch { /* the ground stays, the row survives */ }
  g.restore();
}

/** A flash: a photo, so it is a photo. Cream card, fat bottom lip, a degree of tilt. */
function polaroid(g, id, x, y, w, h, t, look, tilt) {
  const pad = h * 0.07, lip = h * 0.17;
  g.save();
  g.translate(x + w / 2, y + h / 2);
  g.rotate(tilt);
  g.translate(-w / 2, -h / 2);
  g.fillStyle = CARD;
  g.strokeStyle = CARD_EDGE;
  g.beginPath(); g.roundRect(0.5, 0.5, w - 1, h - 1, h * 0.1);
  g.fill(); g.stroke();
  symbolInto(g, id, pad, pad, w - pad * 2, h - pad - lip, h * 0.05, t, look);
  g.restore();
}

/** The wildcard: dashed and EMPTY. A symbol here would read as part of the combination. */
function anyCell(g, x, y, w, h) {
  g.save();
  g.setLineDash([h * 0.16, h * 0.12]);
  g.strokeStyle = '#8d6ba3'; g.lineWidth = Math.max(1, h * 0.055);
  g.beginPath(); g.roundRect(x + 1, y + 1, w - 2, h - 2, h * 0.2);
  g.stroke();
  g.restore();
  g.fillStyle = '#a98cbb';
  g.font = `600 ${Math.round(h * 0.52)}px Segoe UI, Arial, sans-serif`;
  g.textAlign = 'center'; g.textBaseline = 'middle';
  g.fillText('?', x + w / 2, y + h / 2 + h * 0.04);
}

/**
 * Paint one combination row. `ids` is a COMBOS entry; `look` is the reels' own look object
 * ({ gif, word, reduced, face }), so the panel and the glass always agree. `ratio` is devicePixelRatio:
 * the canvas is sized in device px by the caller and the transform is set here, once, in CSS px.
 * Returns true when a row was drawn - a missing context or an empty row is false, and the caller keeps
 * whatever was there rather than blanking the legend.
 */
export function paintCombo(canvas, ids, t, look = {}, ratio = 1) {
  const g = canvas && canvas.getContext && canvas.getContext('2d');
  if (!g || !Array.isArray(ids) || !ids.length) return false;
  const size = comboSize(ids.length);
  g.setTransform(ratio, 0, 0, ratio, 0, 0);
  g.clearRect(0, 0, size.w, size.h);
  ids.forEach((id, i) => {
    const x = PAD + i * (CELL_W + GAP), y = PAD;
    if (id === 'any') anyCell(g, x, y, CELL_W, CELL_H);
    else if (kindOf(id).kind === 'gif') polaroid(g, id, x, y, CELL_W, CELL_H, t, look, TILT[i % TILT.length]);
    else symbolInto(g, id, x, y, CELL_W, CELL_H, CELL_H * 0.2, t, look);
  });
  return true;
}
