/* ============================================================================
 * render.js - the compositor.
 *
 * One frame, in order:
 *   ground -> tiles (cover fitted, clipped, gap between them)
 *          -> tile target blocks, inside that tile's clip
 *          -> canvas target blocks, on the composite
 *          -> the loop marker treatment on the tail frames
 *          -> the stamp
 *
 * Effects that read pixels get a snapshot of the canvas as it stood before
 * they ran, so a melt or a tear samples the picture rather than its own
 * output. Scratch canvases are pooled and reused, never allocated per frame.
 * ==========================================================================*/

import { layoutAtFrame, pixelRect, adjacencyFromRects, GAP_PX } from './layout.js';
import { sourceIndexFor, loopPhase } from './clock.js';
import { blocksAt, rampAt, progressAt, TINT_COLOURS } from './blocks.js';
import { mixSeed } from './rng.js';
import { drawStamp, stampPose, REST_POSE } from './stamp.js';
import * as drain from './effects/drain.js';
import * as tint from './effects/tint.js';
import * as spiral from './effects/spiral.js';
import * as glitch from './effects/glitch.js';
import * as caption from './effects/caption.js';
import * as focus from './effects/focus.js';

export const GROUND = '#14142B';
// the backdrop under a sparse layout: how far the first gif shrinks on its way
// to soft, how much of it shows, and how much ground washes back over it
const BACKDROP_SHRINK = 12;
const BACKDROP_ALPHA = 0.9;
const BACKDROP_WASH = 0.55;
// the ground border a card carries onto a pile, so it reads as a card
const PLATE_PX = 3;

const EFFECT_MODULES = { drain, tint, spiral, glitch, caption, focus };
// the three that only paint; no snapshot needed before they run
/**
 * Owner call: the stamp's five frame landing at the head of every loop.
 * Off until it is called for. Flip this to true (or set `stampLand: true` on the
 * render state) and the preview and the export get it together, since both read
 * the same state.
 */
export const STAMP_LAND_DEFAULT = false;

// effects that paint over the frame and never read it back
const NO_SOURCE = new Set(['tint', 'spiral']);

export class Renderer {
  constructor() {
    this.pool = new Map();
    this.tailGuard = false;
  }

  /** A pooled scratch canvas, grown as needed, never shrunk. */
  buf(key, w, h) {
    const wi = Math.max(1, Math.ceil(w));
    const hi = Math.max(1, Math.ceil(h));
    let b = this.pool.get(key);
    if (!b) {
      const canvas = document.createElement('canvas');
      b = { canvas, ctx: canvas.getContext('2d'), w: 0, h: 0 };
      this.pool.set(key, b);
    }
    if (b.w !== wi || b.h !== hi) {
      b.canvas.width = wi;
      b.canvas.height = hi;
      b.w = wi; b.h = hi;
    }
    b.ctx.setTransform(1, 0, 0, 1, 0, 0);
    b.ctx.globalAlpha = 1;
    b.ctx.globalCompositeOperation = 'source-over';
    b.ctx.filter = 'none';
    return b;
  }

  /** Drop every scratch canvas. */
  dispose() {
    for (const b of this.pool.values()) { b.canvas.width = 0; b.canvas.height = 0; }
    this.pool.clear();
  }

  /**
   * Draw one frame.
   *
   * state: { size, frames, fps, seed, code, tiles, media (Map or fn), layout,
   *          loop, blocks, stampCorner, captionText, showStamp, stampLand }
   */
  render(ctx, frame, state) {
    const size = state.size;
    const f = Math.max(0, Math.min(state.frames - 1, Math.round(frame)));

    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = 'source-over';
    ctx.filter = 'none';
    ctx.fillStyle = GROUND;
    ctx.fillRect(0, 0, size.w, size.h);

    const tiles = state.tiles || [];
    const lay = tiles.length ? layoutAtFrame({
      tileCount: tiles.length,
      tileIds: tiles.map((t) => t.id),
      tree: state.layout && state.layout.tree,
      mode: (state.layout && state.layout.mode) || 'grow',
      stageMs: (state.layout && state.layout.stageMs) || 600,
      deckHold: state.layout && state.layout.deckHold,
      flip: !!(state.layout && state.layout.flip),
      fps: state.fps,
      frames: state.frames,
      orientation: state.orientation,
      seed: state.seed,
      size,
    }, f) : null;
    const slots = lay ? lay.slots : [];

    // a sparse layout gets a soft copy of the first gif under it, so the ground
    // never reads as empty
    if (lay && lay.backdrop) this.drawBackdrop(ctx, tiles, state, f);

    // a motion layout may ask for a slot with no gutters cut into it
    const rects = slots.map((s) => (s.gap === false ? rawRect(s.rect, size) : pixelRect(s.rect, size, GAP_PX)));
    // adjacency from the rects actually on screen this frame, so it stays right
    // through a slide, a shuffle and every half finished grow stage
    const adj = adjacencyFromRects(rects, GAP_PX + 1.5);

    // tiles, back to front: z first, then the order the layout listed them
    const order = slots.map((_, i) => i)
      .sort((a, b) => ((slots[a].z || 0) - (slots[b].z || 0)) || (a - b));
    for (const i of order) this.drawSlot(ctx, slots[i], rects[i], tiles, state, f);

    const blocks = state.blocks || [];
    // the strip, once per frame: what a ghost has to borrow from
    const strips = stripList(state, tiles);

    // tile target blocks, inside their tile
    for (let i = 0; i < slots.length; i++) {
      const tile = tiles[slots[i].tileIndex];
      if (!tile) continue;
      const list = blocksAt(blocks, f, tile.id);
      if (!list.length) continue;
      const neighbours = (adj[i] || []).map((j) => rects[j]);
      for (const block of list) this.applyBlock(ctx, block, f, state, rects[i], neighbours, strips);
    }

    // canvas target blocks, on the composite
    for (const block of blocksAt(blocks, f, 'canvas')) {
      this.applyBlock(ctx, block, f, state, { x: 0, y: 0, w: size.w, h: size.h }, [], strips);
    }

    this.loopMarker(ctx, f, state);

    if (state.showStamp !== false) {
      const land = state.stampLand === undefined ? STAMP_LAND_DEFAULT : !!state.stampLand;
      const pose = land ? stampPose(f, state.frames) : REST_POSE;
      drawStamp(ctx, { code: state.code, corner: state.stampCorner, size, scale: state.stampScale, pose });
    }
    ctx.restore();
  }

  /**
   * The backdrop under a sparse layout: the first gif cover fitted over the
   * whole canvas, softened by a trip through a tiny scratch canvas (no
   * ctx.filter, iOS), then washed with the ground so the tiles stay the
   * picture. In node, with no canvas to scratch on, it is a plain dim draw.
   */
  drawBackdrop(ctx, tiles, state, f) {
    const tile = tiles[0];
    const media = tile && getMedia(state, tile.mediaId);
    if (!media || !media.strip || !media.strip.length) return;
    const size = state.size;
    const idx = sourceIndexFor(tile.playMode || 'forward', f, tile.enterFrame || 0, media.strip.length, 0);
    const img = media.strip[idx] || media.strip[0];
    const sw = media.w || img.width || 1;
    const sh = media.h || img.height || 1;
    const fit = Math.max(size.w / sw, size.h / sh);
    const dw = sw * fit, dh = sh * fit;
    const dx = (size.w - dw) / 2, dy = (size.h - dh) / 2;

    const scratch = this.backdropScratch(size);
    ctx.save();
    if (scratch) {
      const sc = scratch.getContext('2d');
      sc.imageSmoothingEnabled = true;
      sc.clearRect(0, 0, scratch.width, scratch.height);
      const k = scratch.width / size.w;
      sc.drawImage(img, dx * k, dy * k, dw * k, dh * k);
      ctx.imageSmoothingEnabled = true;
      ctx.globalAlpha = BACKDROP_ALPHA;
      ctx.drawImage(scratch, 0, 0, size.w, size.h);
    } else {
      ctx.globalAlpha = BACKDROP_ALPHA;
      ctx.drawImage(img, dx, dy, dw, dh);
    }
    ctx.globalAlpha = BACKDROP_WASH;
    ctx.fillStyle = GROUND;
    ctx.fillRect(0, 0, size.w, size.h);
    ctx.restore();
  }

  /** A tiny canvas the backdrop bounces through; null where there is none to make. */
  backdropScratch(size) {
    const w = Math.max(8, Math.round(size.w / BACKDROP_SHRINK));
    const h = Math.max(8, Math.round(size.h / BACKDROP_SHRINK));
    let c = this._backdrop;
    if (c && c.width === w && c.height === h) return c;
    try {
      if (typeof document !== 'undefined' && document.createElement) {
        c = document.createElement('canvas'); c.width = w; c.height = h;
      } else if (typeof OffscreenCanvas !== 'undefined') {
        c = new OffscreenCanvas(w, h);
      } else return null;
    } catch { return null; }
    this._backdrop = c;
    return c;
  }

  /**
   * One tile. The slot's alpha, tilt, scale, plate and crop are honoured;
   * with none of them present this is the plain cover fitted, clipped tile
   * the old four layouts have always drawn.
   */
  drawSlot(ctx, slot, pr, tiles, state, f) {
    const tile = tiles[slot.tileIndex];
    if (!tile) return;
    const alpha = slot.alpha == null ? 1 : slot.alpha;
    if (!(alpha > 0.002)) return;
    const size = state.size;
    const sc = slot.scale || 1;
    const w = pr.w * sc, h = pr.h * sc;
    const cx = pr.x + pr.w / 2, cy = pr.y + pr.h / 2;
    // a slot can sit off canvas mid slide; skip it rather than clip a whole tile
    const reach = Math.hypot(w, h) / 2;
    if (cx + reach < 0 || cx - reach > size.w || cy + reach < 0 || cy - reach > size.h) return;

    const media = getMedia(state, tile.mediaId);
    ctx.save();
    ctx.globalAlpha = alpha;
    ctx.translate(cx, cy);
    if (slot.rot) ctx.rotate(slot.rot);
    if (slot.plate) {
      ctx.fillStyle = GROUND;
      ctx.fillRect(-w / 2 - PLATE_PX, -h / 2 - PLATE_PX, w + PLATE_PX * 2, h + PLATE_PX * 2);
    }
    // clip after the transform, so a tilted slot is clipped to its tilted rect
    ctx.beginPath();
    ctx.rect(-w / 2, -h / 2, w, h);
    ctx.clip();
    if (media && media.strip && media.strip.length) {
      const idx = sourceIndexFor(tile.playMode || 'forward', f, tile.enterFrame || 0,
        media.strip.length, slot.frameOffset || 0);
      const img = media.strip[idx] || media.strip[0];
      const sw = media.w || img.width || 1;
      const sh = media.h || img.height || 1;
      const crop = slot.crop || null;
      // cover fit, then the crop pans and zooms inside it
      const fit = Math.max(w / sw, h / sh) * (crop && crop.z ? crop.z : 1);
      const dw = sw * fit, dh = sh * fit;
      const px = crop && crop.cx != null ? crop.cx : 0.5;
      const py = crop && crop.cy != null ? crop.cy : 0.5;
      ctx.drawImage(img, -px * dw, -py * dh, dw, dh);
    } else {
      ctx.fillStyle = '#1A1A2E';
      ctx.fillRect(-w / 2, -h / 2, w, h);
    }
    ctx.restore();
  }

  applyBlock(ctx, block, frame, state, rect, neighbours, strips) {
    const mod = EFFECT_MODULES[block.effect];
    if (!mod || typeof mod.render !== 'function') return;
    const ramp = rampAt(block, frame);
    if (ramp <= 0) return;
    const size = state.size;

    const env = {
      fps: state.fps,
      frame,
      frames: state.frames,
      size,
      rect,
      neighbours: neighbours || [],
      // the whole strip, and the one loop this block already sits on top of
      strips: strips || stripList(state, state.tiles || []),
      underMedia: underMediaFor(state, block.target),
      seed: mixSeed(state.seed, block.id),
      ramp,
      mediaColour: state.mediaColour,
      spiralImage: block.params && block.params.mediaId ? spiralImageFor(state, block.params.mediaId) : null,
      buf: (key, w, h) => this.buf(key, w, h),
    };

    let src = null;
    if (!NO_SOURCE.has(block.effect)) {
      const snap = this.buf('src', size.w, size.h);
      snap.ctx.clearRect(0, 0, size.w, size.h);
      snap.ctx.drawImage(ctx.canvas, 0, 0);
      src = snap.canvas;
    }

    const clipRect = typeof mod.clip === 'function' ? mod.clip(block, env) : rect;
    ctx.save();
    ctx.beginPath();
    ctx.rect(clipRect.x, clipRect.y, clipRect.w, clipRect.h);
    ctx.clip();
    try {
      mod.render(ctx, src, block, progressAt(block, frame), env);
    } catch (err) {
      // one bad block must never take the whole frame down
      if (typeof console !== 'undefined') console.warn('effect failed', block.effect, err);
    }
    ctx.restore();
  }

  /** clean cuts, snap flashes the tint colour, seamless folds the head back in. */
  loopMarker(ctx, frame, state) {
    const phase = loopPhase(state.loop, frame, state.frames);
    if (!phase) return;
    const size = state.size;

    if (phase.kind === 'snap') {
      const colour = snapColour(state);
      ctx.save();
      ctx.globalAlpha = 0.55 + 0.45 * phase.u;
      ctx.fillStyle = colour;
      ctx.fillRect(0, 0, size.w, size.h);
      const word = snapWord(state);
      if (word) {
        ctx.globalAlpha = 1;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        const px = Math.round(size.h * 0.22);
        ctx.font = `700 ${px}px 'Bahnschrift Condensed','Arial Narrow','Arial Black',Impact,sans-serif`;
        ctx.fillStyle = GROUND;
        ctx.fillText(word, size.w / 2, size.h / 2);
      }
      ctx.restore();
      return;
    }

    if (phase.kind === 'seamless' && !this.tailGuard) {
      this.tailGuard = true;
      try {
        const head = this.buf('seam', size.w, size.h);
        head.ctx.clearRect(0, 0, size.w, size.h);
        this.render(head.ctx, phase.mixFrame, Object.assign({}, state, { loop: 'clean', showStamp: false }));
        ctx.save();
        ctx.globalAlpha = phase.u * 0.92;
        ctx.drawImage(head.canvas, 0, 0);
        ctx.restore();
      } finally {
        this.tailGuard = false;
      }
    }
  }
}

/** Fraction rect -> pixel rect with no gutters cut: what `gap: false` asks for. */
function rawRect(rect, size) {
  return {
    x: rect.x * size.w,
    y: rect.y * size.h,
    w: Math.max(1, rect.w * size.w),
    h: Math.max(1, rect.h * size.h),
  };
}

/** Which pixel rects share an edge, gap allowed for. */
function snapColour(state) {
  const tintBlock = (state.blocks || []).filter((b) => b.effect === 'tint').pop();
  const name = tintBlock && tintBlock.params ? tintBlock.params.colour : 'pink';
  if (name === 'custom') return state.mediaColour || TINT_COLOURS.pink;
  return TINT_COLOURS[name] || TINT_COLOURS.pink;
}

function snapWord(state) {
  const caps = (state.blocks || []).filter((b) => b.effect === 'caption' && b.params && b.params.text);
  if (caps.length) return String(caps[caps.length - 1].params.text).split(/\s+/)[0];
  const text = String(state.captionText || '').trim();
  return text ? text.split(/\s+/)[0] : 'drop';
}

/** Every distinct media on the canvas that has frames, in tile order. */
function stripList(state, tiles) {
  const out = [];
  const seen = new Set();
  for (const t of tiles || []) {
    if (!t || seen.has(t.mediaId)) continue;
    seen.add(t.mediaId);
    const m = getMedia(state, t.mediaId);
    if (m && m.strip && m.strip.length) out.push(m);
  }
  return out;
}

/** The media under a block: its tile's, or null when it paints the canvas. */
function underMediaFor(state, target) {
  if (!target || target === 'canvas') return null;
  const tile = (state.tiles || []).find((t) => t.id === target);
  return tile ? getMedia(state, tile.mediaId) : null;
}

function getMedia(state, id) {
  if (!state.media) return null;
  if (typeof state.media === 'function') return state.media(id);
  if (typeof state.media.get === 'function') return state.media.get(id);
  return state.media[id] || null;
}

function spiralImageFor(state, id) {
  const m = getMedia(state, id);
  return m && m.strip && m.strip.length ? m.strip[0] : null;
}
