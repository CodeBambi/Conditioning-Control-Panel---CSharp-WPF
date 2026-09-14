/* ============================================================================
 * rects.js - the fixed stage table and the geometry every layout shares.
 *
 * Split out of layout.js so the motion layouts in `layouts/` can reach the
 * table and the easings without importing layout.js, which imports them back.
 * layout.js re-exports everything here, so nothing outside this folder needs
 * to know the file exists.
 *
 * Pure. Rects are fractions of the canvas (0..1).
 * ==========================================================================*/

export const MAX_TILES = 8;
export const GAP_PX = 2;
export const FULL = { x: 0, y: 0, w: 1, h: 1 };

/* Row specs for landscape: each entry is the cell count of one row, rows share
 * the height evenly. Count 3 is the one hand-placed case (big left). */
export const ROWS = {
  1: [1],
  2: [2],
  4: [2, 2],
  5: [2, 3],
  6: [3, 3],
  7: [3, 4],
  8: [4, 4],
};

export function clampCount(n) {
  return Math.max(1, Math.min(MAX_TILES, n | 0));
}

function landscapeRects(n) {
  if (n === 3) {
    return [
      { x: 0, y: 0, w: 0.5, h: 1 },
      { x: 0.5, y: 0, w: 0.5, h: 0.5 },
      { x: 0.5, y: 0.5, w: 0.5, h: 0.5 },
    ];
  }
  const rows = ROWS[n];
  if (!rows) throw new Error('tile count out of range');
  const out = [];
  const rh = 1 / rows.length;
  for (let r = 0; r < rows.length; r++) {
    const cw = 1 / rows[r];
    for (let c = 0; c < rows[r]; c++) {
      out.push({ x: c * cw, y: r * rh, w: cw, h: rh });
    }
  }
  return out;
}

/** Transpose a rect: portrait is the landscape stage turned on its side. */
export function transposeRect(r) {
  return { x: r.y, y: r.x, w: r.h, h: r.w };
}

/** Every rect of the fixed table for `n` tiles. */
export function tableRects(n, orientation = 'landscape') {
  const base = landscapeRects(clampCount(n));
  return orientation === 'portrait' ? base.map(transposeRect) : base;
}

/* ------------------------------------------------------------- easings ---*/

export function clamp01(u) {
  return u < 0 ? 0 : u > 1 ? 1 : u;
}

export function easeOut(u) {
  const c = clamp01(u);
  return 1 - Math.pow(1 - c, 3);
}

export function easeInOut(u) {
  const c = clamp01(u);
  return c < 0.5 ? 4 * c * c * c : 1 - Math.pow(-2 * c + 2, 3) / 2;
}

export function lerp(a, b, u) {
  return a + (b - a) * u;
}

export function lerpRect(a, b, u) {
  return {
    x: a.x + (b.x - a.x) * u,
    y: a.y + (b.y - a.y) * u,
    w: a.w + (b.w - a.w) * u,
    h: a.h + (b.h - a.h) * u,
  };
}

/** A zero size rect at the centre of `r`: what a tile grows out of. */
export function seedRect(r) {
  return { x: r.x + r.w / 2, y: r.y + r.h / 2, w: 0, h: 0 };
}

/** The rect pushed just off canvas past its nearest edge: what an exit is. */
export function exitRect(r) {
  const cx = r.x + r.w / 2;
  const cy = r.y + r.h / 2;
  const d = [cx, 1 - cx, cy, 1 - cy];
  let m = 0;
  for (let i = 1; i < 4; i++) if (d[i] < d[m]) m = i;
  const o = { x: r.x, y: r.y, w: r.w, h: r.h };
  if (m === 0) o.x = -r.w;
  else if (m === 1) o.x = 1;
  else if (m === 2) o.y = -r.h;
  else o.y = 1;
  return o;
}

/** The stage index live at `frame` (0 based, capped at the last stage). */
export function stageAtFrame(starts, frame) {
  let s = 0;
  for (let k = 0; k < starts.length; k++) if (frame >= starts[k]) s = k;
  return s;
}
