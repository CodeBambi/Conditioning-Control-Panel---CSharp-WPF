/* ============================================================================
 * layouts/stack.js - Stack.
 *
 * The gifs drop onto a pile one after another, each at its own spot and tilt.
 * The pile never empties; the next card just lands on top of it.
 *
 * The loop: every card has a fixed landing spot. After all n have landed once
 * the pile is in the same order at the same spots as frame 0, so the loop
 * closes without a cut. A landing runs `LAND` frames: the card comes in big
 * and faint and settles, while the copy already on the pile holds underneath.
 *
 * From the seed: every spot and every tilt.
 * ==========================================================================*/

import { easeOut } from '../rects.js';
import { V, VW, VH, rngFor } from './util.js';

const NAME = 'Stack';
const LAND = 5;   // frames a landing takes
const DROP = 24;  // virtual px the fresh card falls through

export function rectsAt(cfg, frame) {
  const n = Math.max(1, cfg.n | 0);
  const F = Math.max(1, cfg.frames | 0);
  const r = rngFor(cfg, 'stack');
  const cw = Math.round(VH * 0.78);
  const ch = Math.round(cw * 0.75);

  const spots = [];
  for (let k = 0; k < n; k++) {
    spots.push({
      x: VW * (0.3 + 0.4 * r.next()),
      y: VH * (0.32 + 0.36 * r.next()),
      rot: (r.next() * 2 - 1) * 0.4,
    });
  }

  const per = F / n;
  const last = Math.min(n - 1, Math.floor(frame / per));
  const out = [];

  for (let k = 0; k < n; k++) {
    const t = Math.round(k * per);
    const s = spots[k];
    // draw order counts back from whichever card landed most recently
    const z = (((k - last - 1) % n) + n) % n;
    if (frame >= t && frame < t + LAND) {
      const p = easeOut((frame - t) / LAND);
      // the old copy stays at the bottom of the pile while the fresh copy drops on top
      out.push(V(cfg, s.x - cw / 2, s.y - ch / 2, cw, ch,
        { g: k, z: -1, rot: s.rot, plate: true, gap: false }));
      out.push(V(cfg, s.x - cw / 2, s.y - DROP * (1 - p) - ch / 2, cw, ch,
        { g: k, z: n, a: p, sc: 1.5 - 0.5 * p, rot: s.rot, plate: true, gap: false }));
    } else {
      out.push(V(cfg, s.x - cw / 2, s.y - ch / 2, cw, ch,
        { g: k, z, rot: s.rot, plate: true, gap: false }));
    }
  }
  return out;
}

export const stack = {
  backdrop: true,   // the pile leaves ground showing; a soft copy of the first gif fills it
  id: 'stack',
  name: NAME,
  minTiles: 2,
  maxTiles: 8,
  cost: 'cheap',
  rectsAt,
};

export default stack;
