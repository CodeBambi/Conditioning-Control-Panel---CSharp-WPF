/* ============================================================================
 * layouts/index.js - the motion layout registry.
 *
 * A motion layout is one module exporting
 *
 *     { id, name, minTiles, maxTiles, cost, rectsAt(cfg, frame) }
 *
 * `cfg` is built once per project state by `motionCfg`:
 *
 *     { n, frames, stageFrames, seed, orientation, w, h }
 *
 * `rectsAt` is pure and deterministic from cfg alone, and returns one entry
 * per thing to draw this frame, in canvas fractions:
 *
 *     { g, x, y, w, h, a?, rot?, sc?, z?, ph?, crop?, gap?, plate? }
 *   A layout that leaves ground showing (a pile, a fan, a ring) sets `backdrop: true`
 *   on its module and the compositor paints a soft, dim copy of the first gif under it.
 *
 * `g` is the tile index. The loop must close: the list at frame `frames` has
 * to draw the same picture as the list at frame 0.
 *
 * To add one: drop the file in this folder, add it to MOTION_LAYOUTS below,
 * and give it a line in AUTO_WEIGHTS.layouts. Nothing else.
 * ==========================================================================*/

import { stack } from './stack.js';
import { spread } from './spread.js';
import { slide } from './slide.js';
import { ripple } from './ripple.js';
import { swallow } from './swallow.js';
import { cover } from './cover.js';
import { tunnel } from './tunnel.js';
import { deal } from './deal.js';
import { carousel } from './carousel.js';
import { hop } from './hop.js';
import { kenburns } from './kenburns.js';
import { pinwheel } from './pinwheel.js';

export const MOTION_LAYOUTS = [stack, spread, slide, ripple, swallow, cover, tunnel, deal, carousel, hop, kenburns, pinwheel];

const BY_ID = new Map(MOTION_LAYOUTS.map((l) => [l.id, l]));

/** The motion layout with this id, or null for one of the old four. */
export function motionLayout(id) {
  return BY_ID.get(id) || null;
}

const NOMINAL = { landscape: { w: 480, h: 270 }, portrait: { w: 270, h: 480 } };

/**
 * The config a motion layout runs on. `size` is optional: without it the
 * nominal output shape is used, which is all any layout here needs.
 */
export function motionCfg(o) {
  const orientation = o.orientation === 'portrait' ? 'portrait' : 'landscape';
  const size = o.size || NOMINAL[orientation];
  return {
    n: Math.max(1, o.n | 0),
    frames: Math.max(1, o.frames | 0),
    stageFrames: Math.max(1, o.stageFrames | 0),
    seed: o.seed >>> 0,
    orientation,
    w: size.w,
    h: size.h,
  };
}

/**
 * Motion rects -> the slots the compositor draws. The short pitch names are
 * spelled out here and nowhere else: `g` becomes `tileIndex`, `a` `alpha`,
 * `sc` `scale`, `ph` the existing `frameOffset`. `rot`, `z`, `crop`, `gap`
 * and `plate` keep their names.
 */
export function slotsFrom(rects, n) {
  const slots = [];
  for (const r of rects || []) {
    if (!r || !isFinite(r.x) || !isFinite(r.y) || !isFinite(r.w) || !isFinite(r.h)) continue;
    const slot = {
      tileIndex: Math.max(0, Math.min(n - 1, r.g | 0)),
      rect: { x: r.x, y: r.y, w: r.w, h: r.h },
      frameOffset: r.ph | 0,
    };
    if (r.a != null) slot.alpha = r.a;
    if (r.rot) slot.rot = r.rot;
    if (r.sc != null && r.sc !== 1) slot.scale = r.sc;
    if (r.z != null) slot.z = r.z;
    if (r.crop) slot.crop = r.crop;
    if (r.gap === false) slot.gap = false;
    if (r.plate) slot.plate = true;
    slots.push(slot);
  }
  return slots;
}
