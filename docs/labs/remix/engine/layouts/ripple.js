/* ============================================================================
 * layouts/ripple.js - Ripple.
 *
 * A wave rolls across the mosaic from one corner: each tile puffs up as it
 * passes and settles behind it. Then the grid rests until the next loop.
 *
 * The loop: the wave starts at frame 0 and is done by 55 percent of the loop,
 * and the rest of it is the still mosaic, which is also what frame 0 shows.
 * Each bump is 8 frames, out and back, so no tile is mid puff at either end.
 *
 * From the seed: the starting corner, how much the front leans towards the
 * long side, and how far a tile puffs. The lean is the one thing the pitch
 * left fixed at half and half: four corners alone make two nearby seeds land
 * on the same wave far too often.
 * ==========================================================================*/

import { grid, rngFor } from './util.js';

const NAME = 'Ripple';
const BUMP = 8;      // frames one tile's puff takes, out and back
const SPAN = 0.55;   // share of the loop the wave has crossed the grid by

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const base = grid(cfg, n);
  const r = rngFor(cfg, 'ripple');
  const corner = Math.floor(r.next() * 4);
  const lean = 0.35 + 0.3 * r.next();   // how much x leads y across the front
  const lift = 0.13 + 0.06 * r.next();  // how tall the puff is
  const span = Math.max(1, Math.round(F * SPAN) - BUMP);

  return base.map((rect, i) => {
    // distance from the wave's corner, so tiles near it go first
    const cx = rect.x + rect.w / 2;
    const cy = rect.y + rect.h / 2;
    const dx = (corner & 1) ? 1 - cx : cx;
    const dy = (corner & 2) ? 1 - cy : cy;
    const d = dx * lean + dy * (1 - lean);
    const t = Math.round(d * span);
    const p = (frame - t) / BUMP;
    const o = Object.assign(rect, { g: i });
    if (p > 0 && p < 1) {
      const s = Math.sin(p * Math.PI);
      o.sc = 1 + lift * s;
      o.rot = ((corner & 1) ? -1 : 1) * 0.05 * s;
      o.z = 1;
    }
    return o;
  });
}

export const ripple = {
  id: 'ripple',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'cheap',
  rectsAt,
};

export default ripple;
