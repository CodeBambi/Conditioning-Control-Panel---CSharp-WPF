/* ============================================================================
 * layouts/swallow.js - Swallow.
 *
 * The mosaic sits still, then one tile grows until it covers nearly
 * everything, holds, and shrinks back into its slot. Then another takes its
 * turn.
 *
 * The loop: each turn is a closed trip out and back, so the mosaic is
 * untouched at both ends of the loop. Turns are capped by length, one per 12
 * frames, so a short canvas takes three turns rather than eight rushed ones.
 * Grow 3 frames, hold, shrink 3, rest 2.
 *
 * From the seed: which tiles take a turn, and in what order.
 * ==========================================================================*/

import { clamp01, easeOut, lerpRect } from '../rects.js';
import { grid, rngFor } from './util.js';

const NAME = 'Swallow';
const GROW = 3;    // frames the grow takes, and the shrink again
const REST = 2;    // still frames closing every turn
const PER = 12;    // frames a turn needs before the length buys another one
const BIG = { x: 0.07, y: 0.07, w: 0.86, h: 0.86 };

/** The mirror of easeOut, for the shrink: slow to leave, quick to land. */
function easeIn(u) {
  const c = clamp01(u);
  return c * c * c;
}

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const base = grid(cfg, n);
  const turns = Math.min(n, Math.max(1, Math.floor(F / PER)));
  const r = rngFor(cfg, 'swallow');
  const ids = [];
  for (let i = 0; i < n; i++) ids.push(i);
  const order = r.shuffle(ids).slice(0, turns);

  const per = F / turns;
  const j = Math.min(turns - 1, Math.floor(frame / per));
  const t0 = Math.round(j * per);
  const t1 = Math.round((j + 1) * per);
  const who = order[j];
  const local = frame - t0;
  const len = t1 - t0;
  // a very short turn shortens the ramps rather than overrunning its slot
  const g = Math.max(1, Math.min(GROW, Math.floor((len - REST) / 2)));
  const hold = Math.max(0, len - 2 * g - REST);

  let p = 0;
  if (local < g) p = easeOut(local / g);
  else if (local < g + hold) p = 1;
  else if (local < g + hold + g) p = 1 - easeIn((local - g - hold) / g);

  return base.map((rect, i) => {
    if (i !== who || p <= 0) return Object.assign(rect, { g: i });
    // on top, on a plate, and off the gutters once it is out of its slot
    return Object.assign(lerpRect(rect, BIG, p), { g: i, gap: p < 1, z: 5, plate: true });
  });
}

export const swallow = {
  id: 'swallow',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'mid',
  rectsAt,
};

export default swallow;
