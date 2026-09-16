/* ============================================================================
 * layouts/hop.js - Ring Hop.
 *
 * Shuffle with a plan. Every tile hops one slot around the mosaic each beat,
 * so the whole grid turns like a wheel instead of swapping at random.
 *
 * The loop: the slot ring has n places and the loop holds n hops, so the last
 * hop puts every tile back in the slot it held at frame 0. The beat is read
 * off `frame % frames`, so the last frame runs the first frame's arithmetic
 * rather than a value that only rounds to it, and the cut is exact. A hop is
 * four frames of ease with a small lift; between hops the grid is still.
 *
 * The ring is the fixed table walked as a snake: rows when there are one or
 * two of them, columns otherwise, every other run reversed, so a hop is always
 * into the slot next door and never across the frame.
 *
 * Nothing here wants the per copy `ph` the tunnel had to drop: a tile is on
 * the canvas once, so there is no second generation to stagger.
 *
 * From the seed: which way the wheel turns, which slot the first tile starts
 * in, and how high a hop lifts. The pitch left the last two fixed, but the
 * direction alone is one bit, and one bit puts nearby seeds on the same wheel
 * far too often (the same widening Ripple and Deal wanted).
 * ==========================================================================*/

import { easeOut, lerpRect } from '../rects.js';
import { grid, rngFor } from './util.js';

const NAME = 'Ring Hop';
const HOP = 4;       // frames one hop takes
const LIFT = [0.06, 0.05];  // how far a hopping tile swells: least, plus up to

/** Group rects by a rounded coordinate, in ascending order of that key. */
function lanes(rects, key, along) {
  const by = new Map();
  rects.forEach((r, i) => {
    const k = Math.round(r[key] * 1000);
    if (!by.has(k)) by.set(k, []);
    by.get(k).push(i);
  });
  return [...by.keys()].sort((a, b) => a - b)
    .map((k) => by.get(k).sort((a, b) => rects[a][along] - rects[b][along]));
}

/**
 * The grid rects walked as a ring: snake the rows if there are one or two of
 * them, else snake the columns, so consecutive slots always touch.
 */
export function ring(rects) {
  let groups = lanes(rects, 'y', 'x');
  if (groups.length > 2) groups = lanes(rects, 'x', 'y');
  const order = [];
  groups.forEach((g, gi) => {
    if (gi % 2) g.reverse();
    for (const i of g) order.push(i);
  });
  return order.map((i) => rects[i]);
}

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const f = frame % F;
  const slots = ring(grid(cfg, n));
  if (n === 1) return [Object.assign({ g: 0 }, slots[0])];

  const r = rngFor(cfg, 'hop');
  const dir = r.next() < 0.5 ? 1 : -1;
  const start = Math.floor(r.next() * n);
  const lift = LIFT[0] + LIFT[1] * r.next();
  const per = F / n;
  const j = Math.min(n - 1, Math.floor(f / per));
  const u = easeOut((f - Math.round(j * per)) / HOP);
  const flying = u < 1;
  const out = [];

  for (let i = 0; i < n; i++) {
    const at = (k) => slots[((((i + start + dir * k) % n) + n) % n)];
    const now = at(j);
    const rect = flying ? lerpRect(at(j - 1), now, u)
      : { x: now.x, y: now.y, w: now.w, h: now.h };
    out.push(Object.assign(rect, {
      g: i,
      gap: true,
      sc: flying ? 1 + lift * Math.sin(u * Math.PI) : 1,
      z: flying ? 1 : 0,
    }));
  }
  return out;
}

export const hop = {
  id: 'hop',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'mid',
  rectsAt,
};

export default hop;
