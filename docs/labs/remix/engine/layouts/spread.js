/* ============================================================================
 * layouts/spread.js - Spread.
 *
 * One gif fills the frame, then it splits and the others slide in from the
 * edges until the whole mosaic is up. In the last frames the first gif
 * swallows the mosaic and you are back at the start.
 *
 * The loop: the frame is the same one-gif picture at frame 0 and at frame
 * `frames`. The fold at the end moves every rect back the way it came, so no
 * crossfade is needed and the cut is invisible.
 *
 * Grow's split table and stage schedule, with two changes: newcomers slide in
 * from their nearest edge instead of growing from a point, and the fold
 * returns tile 0 to the full frame while the others exit. Nothing seeded: the
 * order is the tile order, which is the order they were dropped in.
 * ==========================================================================*/

import { FULL, easeOut, easeInOut, lerpRect, exitRect, stageAtFrame } from '../rects.js';
import { grid } from './util.js';

const NAME = 'Spread';
const FOLD = 5;   // frames the swallow takes
const SLIDE = 4;  // frames a newcomer slides in over

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const out = [];

  if (n === 1) return [Object.assign(grid(cfg, 1)[0], { g: 0 })];

  const foldStart = F - FOLD;
  const step = Math.max(3, Math.min(9, Math.floor((foldStart - 8) / (n - 1))));
  const starts = [];
  for (let k = 0; k < n; k++) starts.push(k * step);

  if (frame >= foldStart) {
    const u = easeInOut((frame - foldStart + 1) / FOLD);
    const from = grid(cfg, n);
    for (let i = 0; i < n; i++) {
      const to = i === 0 ? FULL : exitRect(from[i]);
      out.push(Object.assign(lerpRect(from[i], to, u), {
        g: i, z: i === 0 ? n : i, gap: u < 1,
      }));
    }
    return out;
  }

  const stage = stageAtFrame(starts, frame);
  const toR = grid(cfg, stage + 1);
  const fromR = stage > 0 ? grid(cfg, stage) : toR;
  const u = stage > 0 ? easeOut((frame - starts[stage]) / SLIDE) : 1;
  for (let i = 0; i <= stage; i++) {
    const to = toR[i];
    if (!to) continue;
    // the tile that is new this stage has no old rect: it comes in off canvas
    const fr = fromR[i] || exitRect(to);
    const rect = u >= 1 ? { x: to.x, y: to.y, w: to.w, h: to.h } : lerpRect(fr, to, u);
    out.push(Object.assign(rect, { g: i, z: i, gap: true }));
  }
  return out;
}

export const spread = {
  id: 'spread',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'cheap',
  rectsAt,
};

export default spread;
