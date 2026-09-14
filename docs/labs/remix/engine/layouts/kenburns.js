/* ============================================================================
 * layouts/kenburns.js - Ken Burns Wall.
 *
 * Every tile slowly pans and breathes inside its own gif, each on its own
 * path. The grid holds still; the pictures do not.
 *
 * The loop: the grid is the fixed mosaic all the way through, so the only
 * thing moving is the crop window. Each window walks round its path exactly
 * once per loop and the zoom breathes exactly once, both off the same `u`, so
 * the last frame hands the window back where the first one picked it up.
 *
 * The path is a circle in source space, which the source's own shape stretches
 * into the ellipse the pitch drew. Its radius is 90 percent of the room the
 * zoom leaves, so the window never reaches the edge of the gif and never pans
 * onto ground. The breath rides a slower phase than the pan, so a tile is not
 * biggest at the same point of its circle every time.
 *
 * From the seed: each tile's zoom, how hard it breathes, where on the circle
 * it starts and which way round it goes.
 * ==========================================================================*/

import { grid, rngFor } from './util.js';

const NAME = 'Ken Burns Wall';
const ZOOM = [1.25, 1.65];    // how far into the gif a tile sits
const BREATH = [0.05, 0.1];   // how much of that zoom comes and goes over a loop
const LAG = 0.7;              // the breath's phase, against the pan's
const INSET = 0.9;            // share of the safe travel the path uses

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const u = frame / F;
  const base = grid(cfg, n);
  const rng = rngFor(cfg, 'kb');

  return base.map((rect, i) => {
    const zoom = ZOOM[0] + rng.next() * (ZOOM[1] - ZOOM[0]);
    const phase = rng.next() * Math.PI * 2;
    const dir = rng.next() < 0.5 ? 1 : -1;
    const breath = BREATH[0] + rng.next() * (BREATH[1] - BREATH[0]);

    const z = zoom + breath * Math.sin(2 * Math.PI * u + phase * LAG);
    // half the source the zoom leaves over, kept off the edge
    const reach = (1 - 1 / z) / 2 * INSET;
    const a = dir * 2 * Math.PI * u + phase;
    return Object.assign(rect, {
      g: i,
      crop: { cx: 0.5 + reach * Math.cos(a), cy: 0.5 + reach * Math.sin(a), z },
    });
  });
}

export const kenburns = {
  id: 'kenburns',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'dear',
  rectsAt,
};

export default kenburns;
