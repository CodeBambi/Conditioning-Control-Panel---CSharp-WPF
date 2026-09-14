/* ============================================================================
 * layouts/slide.js - Slide.
 *
 * Rows of gifs drift past like the shelves in Lost and Found, every other row
 * going the other way. Nothing enters and nothing leaves, the strip just keeps
 * coming.
 *
 * The loop: each row is its own tiles laid end to end and repeated. Over one
 * loop the row moves by exactly one repeat, so frame `frames` lands where
 * frame 0 started. Rows with fewer tiles move slower.
 *
 * From the seed: the direction pattern and each row's starting offset.
 * Portrait turns the rows into columns, the same way every other layout does.
 * ==========================================================================*/

import { V, VW, VH, rngFor } from './util.js';

const NAME = 'Slide';

/** Rows by tile count: one strip is a crawl, four is a wall. */
function rowsFor(n) {
  return n <= 1 ? 1 : n <= 2 ? 2 : n <= 6 ? 3 : 4;
}

export function rectsAt(cfg, frame) {
  const F = Math.max(1, cfg.frames | 0);
  const n = Math.max(1, cfg.n | 0);
  const u = frame / F;
  const rows = rowsFor(n);
  const rh = VH / rows;
  const tw = Math.round(rh * 1.15);
  const r = rngFor(cfg, 'slide');
  const sign = r.next() < 0.5 ? 1 : -1;
  const out = [];

  for (let k = 0; k < rows; k++) {
    // the tiles that share this row, every `rows`th one
    const tiles = [];
    for (let i = k; i < n; i += rows) tiles.push(i);
    if (!tiles.length) tiles.push(k % n);

    const L = tiles.length * tw;           // one repeat of this row
    const dir = (k % 2 ? -1 : 1) * sign;   // alternate directions
    const phase = r.next() * L;
    const base = (((dir * u * L + phase) % L) + L) % L;
    const y = k * rh + (k ? 2 : 0);
    const h = rh - (k ? 2 : 0) - (k < rows - 1 ? 2 : 0);

    for (let x0 = base - L; x0 < VW; x0 += L) {
      for (let i = 0; i < tiles.length; i++) {
        const x = x0 + i * tw;
        if (x + tw <= 0 || x >= VW) continue;
        out.push(V(cfg, x + 2, y, tw - 4, h, { g: tiles[i], gap: false }));
      }
    }
  }
  return out;
}

export const slide = {
  id: 'slide',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'dear',
  rectsAt,
};

export default slide;
