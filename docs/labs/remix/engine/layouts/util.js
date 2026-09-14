/* ============================================================================
 * layouts/util.js - what a motion layout gets to lean on.
 *
 * Imports only rects.js and rng.js, never layout.js: layout.js pulls the
 * motion layouts in, so anything here reaching back would be a cycle.
 *
 * The motion maths is written in a virtual 480x270 landscape space, the way
 * the pitch was drawn and picked. `V` turns that into canvas fractions and
 * stands it on end for portrait, so one set of numbers serves both.
 * ==========================================================================*/

import { rngFrom } from '../rng.js';
import { tableRects, transposeRect, clampCount } from '../rects.js';

export const VW = 480;
export const VH = 270;

/** One stream for a layout, stable per (seed, layout id). */
export function rngFor(cfg, tag) {
  return rngFrom(cfg.seed >>> 0, 'motion:' + tag);
}

/** Virtual 480x270 px -> a canvas fraction rect, transposed for portrait. */
export function V(cfg, x, y, w, h, extra) {
  let r = { x: x / VW, y: y / VH, w: w / VW, h: h / VH };
  if (cfg.orientation === 'portrait') r = transposeRect(r);
  return Object.assign(r, extra || {});
}

/** The fixed mosaic table for `count` tiles, drawn with the mosaic gap. */
export function grid(cfg, count) {
  return tableRects(clampCount(count), cfg.orientation).map((r) => ({
    x: r.x, y: r.y, w: r.w, h: r.h, gap: true,
  }));
}
