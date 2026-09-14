/* ============================================================================
 * effects/drain.js - the picture sags and runs.
 *
 * Every column of the picture is given its own sag, taken from smooth noise
 * across the width, so the top edge goes ragged and the content slides down
 * and stretches as it goes. The gap a sagging column opens at the top is
 * filled with that column's own top row, stretched, which is what turns a
 * slide into a run. The lower a pixel has travelled the blurrier and darker
 * it gets, so the bottom of the frame reads as paint that has already gone.
 *
 *   soft    an even sag, the whole picture running at once
 *   drip    a sparse handful of columns run much further than their neighbours
 *   spread  on a tile, the run also soaks into the tiles it touches
 * ==========================================================================*/

import { fbm1, hash01 } from '../rng.js';
import { blurredCopy, knob, clamp } from './util.js';

export const bleeds = true;

/** Spread reaches past the tile, so the compositor widens the clip for it. */
export function clip(block, env) {
  if (block.mode !== 'spread' || !env.neighbours || !env.neighbours.length) return env.rect;
  let x0 = env.rect.x, y0 = env.rect.y, x1 = env.rect.x + env.rect.w, y1 = env.rect.y + env.rect.h;
  for (const n of env.neighbours) {
    x0 = Math.min(x0, n.x); y0 = Math.min(y0, n.y);
    x1 = Math.max(x1, n.x + n.w); y1 = Math.max(y1, n.y + n.h);
  }
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
}

export function render(ctx, src, block, t, env) {
  const s = knob(block.params, 'strength', 60) * env.ramp;
  if (s <= 0.005) return;

  const rect = env.rect;
  const seed = env.seed;
  const drip = block.mode === 'drip';

  // the run deepens across the block, but never starts from nothing
  const amount = s * (0.35 + 0.65 * clamp(t, 0, 1));
  const w = Math.max(1, Math.round(rect.w));
  const h = Math.max(1, Math.round(rect.h));
  const maxSag = h * (drip ? 0.30 : 0.38) * amount;
  if (maxSag < 0.6) return;

  // a clean copy of the region to pull columns out of
  const base = env.buf('drain-base', w, h);
  base.ctx.globalCompositeOperation = 'source-over';
  base.ctx.clearRect(0, 0, w, h);
  base.ctx.drawImage(src, rect.x, rect.y, rect.w, rect.h, 0, 0, w, h);

  const melt = env.buf('drain-melt', w, h);
  const m = melt.ctx;
  m.globalCompositeOperation = 'source-over';
  m.clearRect(0, 0, w, h);
  m.imageSmoothingEnabled = true;
  m.imageSmoothingQuality = 'medium';

  // narrow strips, smooth noise: the sag varies over about two waves across
  // the width, so neighbours differ by a hair and no comb edge appears
  const strips = clamp(Math.round(w / 4), 16, 140);
  const cw = w / strips;
  const phase = env.frame * 0.02;

  // a handful of narrow tongues that run further than the rest, each one a
  // soft bump across a few strips so its edges never go square
  const runs = [];
  if (drip) {
    const count = 2 + Math.floor(hash01(seed, 7) * 3);
    for (let j = 0; j < count; j++) {
      runs.push({
        at: hash01(seed ^ 0x51ed, j) * strips,
        peak: maxSag * (0.55 + 0.75 * hash01(seed ^ 0x9e37, j)),
        wide: strips * (0.014 + 0.022 * hash01(seed ^ 0x2f1d, j)),
      });
    }
  }

  for (let i = 0; i < strips; i++) {
    const u = (i + 0.5) / strips;
    const n = 0.68 * fbm1(seed, u * 1.7 + phase) + 0.32 * fbm1(seed ^ 0x51, u * 6.5 + phase * 1.7);
    let sag = maxSag * (0.12 + 0.88 * Math.pow(n, 1.5));
    for (const r of runs) {
      const d = (i - r.at) / r.wide;
      if (Math.abs(d) < 3.2) sag += r.peak * Math.exp(-d * d);
    }
    sag = Math.min(h * 0.78, sag);
    const sx = i * cw;
    const sw = cw + 1.4; // overlap, so no seam shows between strips

    // the column slides down and stretches a little as it falls
    m.drawImage(base.canvas, sx, 0, sw, h, sx, sag, sw, h + sag * 0.45);
    // its own top row, stretched, fills the gap it left behind
    if (sag > 0.5) m.drawImage(base.canvas, sx, 0, sw, 1, sx, 0, sw, sag + 1);
  }

  // the further it has run the softer it gets
  const blurPx = 1.2 + amount * (block.mode === 'soft' ? 6 : 4);
  const soft = blurredCopy(melt.canvas, { x: 0, y: 0, w, h }, blurPx, env);
  const sctx = soft.getContext('2d');
  const g = sctx.createLinearGradient(0, 0, 0, h);
  g.addColorStop(0, `rgba(0,0,0,${(0.10 * amount).toFixed(3)})`);
  g.addColorStop(0.4, `rgba(0,0,0,${(0.18 * amount).toFixed(3)})`);
  g.addColorStop(1, `rgba(0,0,0,${(0.9 * amount).toFixed(3)})`);
  sctx.globalCompositeOperation = 'destination-in';
  sctx.fillStyle = g;
  sctx.fillRect(0, 0, w, h);
  sctx.globalCompositeOperation = 'source-over';
  m.drawImage(soft, 0, 0);

  ctx.save();
  ctx.globalCompositeOperation = 'source-over';
  ctx.drawImage(melt.canvas, 0, 0, w, h, rect.x, rect.y, rect.w, rect.h);

  // and it drains into the dark at the bottom
  const dim = ctx.createLinearGradient(0, rect.y + rect.h * 0.45, 0, rect.y + rect.h);
  dim.addColorStop(0, 'rgba(12,12,26,0)');
  dim.addColorStop(1, `rgba(12,12,26,${(0.34 * amount).toFixed(3)})`);
  ctx.fillStyle = dim;
  ctx.fillRect(rect.x, rect.y, rect.w, rect.h);
  ctx.restore();

  if (block.mode === 'spread' && env.neighbours && env.neighbours.length) {
    bleedInto(ctx, melt.canvas, rect, env, amount);
  }
}

/** Half strength copy of the run pushed into each touching tile, fading away
 *  from the shared edge so it looks like it soaked across rather than pasted. */
function bleedInto(ctx, melt, rect, env, s) {
  const alpha = 0.5 * s;
  for (const n of env.neighbours) {
    const w = Math.max(1, Math.round(n.w));
    const h = Math.max(1, Math.round(n.h));
    const b = env.buf('drain-bleed', w, h);
    b.ctx.globalCompositeOperation = 'source-over';
    b.ctx.clearRect(0, 0, w, h);
    b.ctx.drawImage(melt, 0, 0, melt.width, melt.height, 0, 0, w, h);

    // fade out with distance from the edge the two tiles share
    const dir = edgeDirection(rect, n);
    const g = b.ctx.createLinearGradient(
      dir.x0 * w, dir.y0 * h, dir.x1 * w, dir.y1 * h,
    );
    g.addColorStop(0, 'rgba(0,0,0,1)');
    g.addColorStop(0.55, 'rgba(0,0,0,0.35)');
    g.addColorStop(1, 'rgba(0,0,0,0)');
    b.ctx.globalCompositeOperation = 'destination-in';
    b.ctx.fillStyle = g;
    b.ctx.fillRect(0, 0, w, h);
    b.ctx.globalCompositeOperation = 'source-over';

    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.drawImage(b.canvas, 0, 0, w, h, n.x, n.y, n.w, n.h);
    ctx.restore();
  }
}

/** Gradient axis running away from the edge `a` and `b` share. */
function edgeDirection(a, b) {
  const eps = 0.75;
  if (Math.abs(a.x + a.w - b.x) < eps) return { x0: 0, y0: 0, x1: 1, y1: 0 };
  if (Math.abs(b.x + b.w - a.x) < eps) return { x0: 1, y0: 0, x1: 0, y1: 0 };
  if (Math.abs(a.y + a.h - b.y) < eps) return { x0: 0, y0: 0, x1: 0, y1: 1 };
  return { x0: 0, y0: 1, x1: 0, y1: 0 };
}
