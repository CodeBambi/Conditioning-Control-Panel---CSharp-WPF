/* ============================================================================
 * layouts/carousel.js - Carousel.
 *
 * The gifs ride a ring that turns towards you. Big and bright at the front,
 * small and dim at the back, and every one of them gets a turn in the light.
 *
 * The loop: the ring makes exactly one full turn per loop, so gif i ends the
 * loop at the angle it started on. The turn is read off `frame % frames`, so
 * the last frame runs the first frame's arithmetic rather than a value that
 * only rounds to it, and the cut is exact.
 *
 * Depth does all the work: d is the cosine of the angle folded into 0..1, and
 * height, alpha and draw order all come off it. Nothing here wants the per
 * copy `ph` the tunnel had to drop: each gif is on the ring once, so there is
 * no second generation to stagger and no seam to cancel.
 *
 * At three seconds four gifs or fewer ride best, past that the ring hurries.
 * It still runs to eight, and the picker says so rather than the code.
 *
 * From the seed: the starting angle and which way the ring turns.
 * ==========================================================================*/

import { V, VW, VH, rngFor } from './util.js';

const NAME = 'Carousel';
const WIDE = 0.36;   // half the ring's width, as a share of the virtual frame
const BACK = 0.36;   // card height at the back, as a share of the short side
const NEAR = 0.46;   // how much taller it is at the front
const AR = 1.33;     // card shape
const DIM = 0.35;    // alpha at the back

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const u = (frame % F) / F;
  const r = rngFor(cfg, 'carousel');
  const a0 = r.next() * Math.PI * 2;
  const dir = r.next() < 0.5 ? 1 : -1;
  const out = [];

  for (let i = 0; i < n; i++) {
    const th = a0 + dir * 2 * Math.PI * (i / n + u);
    const d = (Math.cos(th) + 1) / 2;   // 0 at the back of the ring, 1 at the front
    const h = (BACK + NEAR * d) * VH;
    const w = h * AR;
    const cx = VW / 2 + Math.sin(th) * VW * WIDE;
    out.push(V(cfg, cx - w / 2, VH / 2 - h / 2, w, h,
      { g: i, z: d, a: DIM + (1 - DIM) * d, plate: true, gap: false }));
  }
  return out;
}

export const carousel = {
  backdrop: true,   // sparse on the canvas; a soft copy of the first gif fills the ground
  id: 'carousel',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'dear',
  rectsAt,
};

export default carousel;
