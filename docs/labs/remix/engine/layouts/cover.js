/* ============================================================================
 * layouts/cover.js - Cover.
 *
 * The mosaic is the bed, and the first gif rides across it at 80 percent
 * size, edge to edge, once per loop. A hero pass over a wall of extras.
 *
 * The loop: the hero is fully off canvas at frame 0 and off again by 92
 * percent of the loop, so the cut lands on the still bed and there is nothing
 * to match up. The pass is a smooth ease, slow at both ends.
 *
 * The bed is the fixed table for the other n-1 tiles, so the hero is never
 * also in the wall. Portrait runs the pass top to bottom, the way V stands
 * every other layout on end. From the seed: the direction.
 * ==========================================================================*/

import { clamp01 } from '../rects.js';
import { V, VW, VH, grid, rngFor } from './util.js';

const NAME = 'Cover';
const SIZE = 0.8;  // the hero against the canvas
const LEAD = 0.08; // share of the loop parked off canvas at each end

/** Smoothstep: the pass has no kick at either edge, unlike the cubic eases. */
function smooth(u) {
  const c = clamp01(u);
  return c * c * (3 - 2 * c);
}

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const u = frame / F;
  const r = rngFor(cfg, 'cover');
  const dir = r.next() < 0.5 ? 1 : -1;

  // tiles 1..n-1 are the bed; with a single gif it beds and heroes itself
  const out = grid(cfg, Math.max(1, n - 1))
    .map((rect, i) => Object.assign(rect, { g: n === 1 ? 0 : i + 1 }));

  const p = smooth((u - LEAD) / (1 - 2 * LEAD));
  const hw = VW * SIZE;
  const hh = VH * SIZE;
  const travel = VW + hw;
  const x = dir > 0 ? VW - p * travel : -hw + p * travel;
  out.push(V(cfg, x, (VH - hh) / 2, hw, hh, { g: 0, z: 5, plate: true, gap: false }));
  return out;
}

export const cover = {
  id: 'cover',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'mid',
  rectsAt,
};

export default cover;
