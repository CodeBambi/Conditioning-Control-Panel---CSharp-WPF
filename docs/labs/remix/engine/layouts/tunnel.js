/* ============================================================================
 * layouts/tunnel.js - Mirror Tunnel.
 *
 * One gif nested inside itself, and the camera keeps pushing in. Mirror turned
 * into a zoom that never arrives.
 *
 * The loop: every ring is a fixed fraction of the one outside it, and over one
 * loop the tunnel zooms by exactly `steps` rings, so ring k ends where ring
 * k - steps began. The innermost rings fade in as they are born, the outermost
 * grow past the canvas and are covered by the ring behind them, so the cut has
 * nothing to show.
 *
 * With one gif the rings are all the same tile and the zoom takes one step per
 * loop. With two or three the rings alternate and the zoom takes two or three
 * steps, so the gif on a ring is the gif that was one ring out. Past the third
 * there is no ring left to put one on, which is what `maxTiles` says.
 *
 * The pitch also staggered each generation of rings a few frames into the
 * gif's own loop, and cancelled the seam with `(S - F % S) % S`. That needs S,
 * the source loop length, which here is the media strip: per tile, and nothing
 * a layout is handed. Without it the stagger would jump at the cut, so the
 * rings run in lockstep and the twist carries the depth instead.
 *
 * From the seed: the twist.
 * ==========================================================================*/

import { rngFor } from './util.js';

const NAME = 'Mirror Tunnel';
const RINGS = 8;           // rings past the ones the zoom eats each loop
const STEP = [0, 0.68, 0.74, 0.78];  // ring to ring shrink, by gifs on the rings
const GONE = 2.4;          // a ring this much wider than the canvas is dropped
const TWIST = 0.22;        // radians of lean per ring, at most

export function rectsAt(cfg, frame) {
  const F = Math.max(1, cfg.frames | 0);
  const u = frame / F;
  const steps = Math.min(3, Math.max(1, cfg.n | 0));
  const r = STEP[steps];
  const rings = RINGS + steps;
  const twist = (rngFor(cfg, 'tunnel').next() * 2 - 1) * TWIST;
  const out = [];

  for (let k = 0; k <= rings; k++) {
    // depth: ring k has been pulled `steps * u` rings towards the camera
    const d = k - steps * u;
    const s = Math.pow(r, d);
    if (s > GONE) continue;
    out.push({
      x: 0.5 - s / 2, y: 0.5 - s / 2, w: s, h: s,
      g: k % steps, z: k, rot: twist * d, gap: false,
      a: k > rings - steps ? u : 1,
    });
  }
  return out;
}

export const tunnel = {
  id: 'tunnel',
  name: NAME,
  minTiles: 1,
  maxTiles: 3,
  cost: 'dear',
  rectsAt,
};

export default tunnel;
