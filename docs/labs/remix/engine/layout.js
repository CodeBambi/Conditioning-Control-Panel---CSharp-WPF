/* ============================================================================
 * layout.js - the split tree, and the stage table it is built from.
 *
 * The mosaic is a tree: a leaf is a tile id, a node is
 * `{ dir:'row'|'col', a, b, ratio }`. Rects are always walked out of the tree,
 * in fractions of the canvas (0..1). `defaultTree` builds the tree that
 * reproduces the old fixed stage table exactly, so nothing moves until
 * somebody docks a tile.
 *
 * A Grow stage is the same tree with the tiles that have not entered yet
 * pruned out, and a node left with one child collapses into that child.
 * Removing a tile collapses the same way.
 *
 * Portrait is the landscape tree with every split turned the other way, which
 * is the same thing as transposing every rect. Square uses landscape.
 *
 * Pure. No DOM. The compositor asks for `layoutAtFrame` once per frame and
 * draws what it gets back.
 * ==========================================================================*/

import { rngFrom } from './rng.js';
import {
  MAX_TILES, GAP_PX, FULL, ROWS, clampCount, tableRects, transposeRect,
  easeOut, easeInOut, lerpRect, seedRect, exitRect, stageAtFrame,
} from './rects.js';
import { MOTION_LAYOUTS, motionLayout, motionCfg, slotsFrom } from './layouts/index.js';

export {
  MAX_TILES, GAP_PX, FULL, clampCount, tableRects, transposeRect,
  easeOut, easeInOut, lerpRect, seedRect, exitRect, stageAtFrame,
};
export { MOTION_LAYOUTS, motionLayout, motionCfg, slotsFrom };

/**
 * The layout registry: every mode a picker or the auto roll may offer, in
 * picker order. `minTiles` is the fewest tiles the mode makes sense with.
 * `deck` is an engine extension (Flash Deck) and is not registered here.
 * A new motion layout joins the roll by taking a row in this table.
 */
export const LAYOUTS = [
  { id: 'grow', name: 'Grow', minTiles: 1, maxTiles: MAX_TILES, cost: 'cheap' },
  { id: 'flat', name: 'Flat', minTiles: 1, maxTiles: MAX_TILES, cost: 'cheap' },
  { id: 'shuffle', name: 'Shuffle', minTiles: 2, maxTiles: MAX_TILES, cost: 'cheap' },
  { id: 'mirror', name: 'Mirror', minTiles: 1, maxTiles: MAX_TILES, cost: 'mid' },
  ...MOTION_LAYOUTS.map((l) => ({
    id: l.id, name: l.name, minTiles: l.minTiles, maxTiles: l.maxTiles, cost: l.cost,
  })),
];
export const SLIDE_FRAMES = 4;

/**
 * Rects. Two callers, two shapes:
 *   rectsFor(n, orientation)        the fixed table, an array of rects
 *   rectsFor(tree, bounds, gap)     the tree, an array of { id, rect }
 */
export function rectsFor(a, b, c) {
  if (typeof a === 'number') return tableRects(a, b);
  return treeRects(a, b, c);
}

/** One rect of the fixed table. `i` is the tile's index in the stage. */
export function rectFor(n, i, orientation = 'landscape') {
  const all = tableRects(n, orientation);
  return all[Math.max(0, Math.min(all.length - 1, i | 0))];
}

/* ----------------------------------------------------------- the tree ----*/

export function isLeaf(node) { return typeof node === 'string'; }

/** Every tile id in the tree, left to right and top to bottom. */
export function leavesOf(tree) {
  const out = [];
  (function walk(n) {
    if (n == null) return;
    if (isLeaf(n)) { out.push(n); return; }
    walk(n.a); walk(n.b);
  }(tree));
  return out;
}

function split(dir, a, b, ratio) {
  return { dir, a, b, ratio: Math.max(0.05, Math.min(0.95, ratio)) };
}

/** Even n-way split of one strip, as nested two-way splits. */
function evenSplit(ids, dir) {
  if (ids.length === 1) return ids[0];
  const k = Math.floor(ids.length / 2);
  return split(dir, evenSplit(ids.slice(0, k), dir), evenSplit(ids.slice(k), dir), k / ids.length);
}

/** Turn every split the other way: the tree equivalent of transposing rects. */
export function flipTree(tree) {
  if (tree == null || isLeaf(tree)) return tree;
  return {
    dir: tree.dir === 'row' ? 'col' : 'row',
    a: flipTree(tree.a),
    b: flipTree(tree.b),
    ratio: tree.ratio,
  };
}

/**
 * The tree for `tileIds` that lays out exactly like the fixed stage table.
 * Landscape is built here; portrait is that tree flipped.
 */
export function defaultTree(tileIds, orientation = 'landscape') {
  const ids = (tileIds || []).slice(0, MAX_TILES);
  if (!ids.length) return null;
  let tree;
  if (ids.length === 1) {
    tree = ids[0];
  } else if (ids.length === 3) {
    tree = split('row', ids[0], split('col', ids[1], ids[2], 0.5), 0.5);
  } else {
    const rows = ROWS[ids.length];
    if (!rows) throw new Error('tile count out of range');
    if (rows.length === 1) {
      tree = evenSplit(ids, 'row');
    } else {
      let at = 0;
      const strips = rows.map((count) => {
        const slice = ids.slice(at, at + count);
        at += count;
        return evenSplit(slice, 'row');
      });
      tree = strips[strips.length - 1];
      for (let i = strips.length - 2; i >= 0; i--) {
        tree = split('col', strips[i], tree, 1 / (strips.length - i));
      }
    }
  }
  return orientation === 'portrait' ? flipTree(tree) : tree;
}

/**
 * Walk a tree into rects. `bounds` defaults to the whole canvas in fractions;
 * pass pixel bounds and a pixel `gap` to get the gutters cut in as you go.
 */
export function treeRects(tree, bounds = FULL, gap = 0) {
  const out = [];
  (function walk(node, b) {
    if (node == null) return;
    if (isLeaf(node)) { out.push({ id: node, rect: b }); return; }
    const r = Math.max(0.05, Math.min(0.95, node.ratio == null ? 0.5 : node.ratio));
    if (node.dir === 'col') {
      const avail = Math.max(0, b.h - gap);
      const ah = avail * r;
      walk(node.a, { x: b.x, y: b.y, w: b.w, h: ah });
      walk(node.b, { x: b.x, y: b.y + ah + gap, w: b.w, h: avail - ah });
    } else {
      const avail = Math.max(0, b.w - gap);
      const aw = avail * r;
      walk(node.a, { x: b.x, y: b.y, w: aw, h: b.h });
      walk(node.b, { x: b.x + aw + gap, y: b.y, w: avail - aw, h: b.h });
    }
  }(tree, bounds));
  return out;
}

/** Keep only these leaves. A node left with one child becomes that child. */
export function pruneTree(tree, keep) {
  const set = keep instanceof Set ? keep : new Set(keep || []);
  return (function walk(n) {
    if (n == null) return null;
    if (isLeaf(n)) return set.has(n) ? n : null;
    const a = walk(n.a), b = walk(n.b);
    if (a && b) return { dir: n.dir, a, b, ratio: n.ratio };
    return a || b;
  }(tree));
}

/** Drop one tile and collapse behind it. */
export function removeFromTree(tree, id) {
  const keep = new Set(leavesOf(tree));
  keep.delete(id);
  return pruneTree(tree, keep);
}

/** The two leaves trade places. */
export function swapInTree(tree, aId, bId) {
  if (aId === bId) return tree;
  return (function walk(n) {
    if (n == null) return null;
    if (isLeaf(n)) return n === aId ? bId : n === bId ? aId : n;
    return { dir: n.dir, a: walk(n.a), b: walk(n.b), ratio: n.ratio };
  }(tree));
}

export const DOCK_SIDES = ['left', 'right', 'top', 'bottom'];

/**
 * Move `movingId` next to `targetId`. The moving leaf leaves its old place
 * (which collapses), and the target leaf becomes a split holding both.
 */
export function dockInTree(tree, movingId, targetId, side) {
  if (!DOCK_SIDES.includes(side)) return tree;
  if (movingId === targetId) return tree;
  const leaves = leavesOf(tree);
  if (leaves.indexOf(movingId) < 0 || leaves.indexOf(targetId) < 0) return tree;

  const pulled = removeFromTree(tree, movingId);
  if (pulled == null) return tree;
  const dir = side === 'left' || side === 'right' ? 'row' : 'col';
  const first = side === 'left' || side === 'top';
  const pair = split(dir, first ? movingId : targetId, first ? targetId : movingId, 0.5);

  return (function walk(n) {
    if (n == null) return null;
    if (isLeaf(n)) return n === targetId ? pair : n;
    return { dir: n.dir, a: walk(n.a), b: walk(n.b), ratio: n.ratio };
  }(pulled));
}

/** Repair a tree against the tile list: unknown leaves out, missing ones in. */
export function ensureTree(tree, tileIds, orientation = 'landscape') {
  const want = (tileIds || []).slice(0, MAX_TILES);
  if (!want.length) return null;
  let next = tree ? pruneTree(tree, new Set(want)) : null;
  if (next == null) return defaultTree(want, orientation);
  const have = new Set(leavesOf(next));
  const missing = want.filter((id) => !have.has(id));
  if (!missing.length) return next;
  // a tile with no home is docked under (or beside) everything already there
  const dir = orientation === 'portrait' ? 'row' : 'col';
  for (const id of missing) next = split(dir, next, id, 2 / 3);
  return next;
}

/** Which rects touch which, by shared edge, within `tol` units. */
export function adjacencyFromRects(rects, tol = 1e-6) {
  const out = rects.map(() => []);
  for (let i = 0; i < rects.length; i++) {
    for (let j = i + 1; j < rects.length; j++) {
      const a = rects[i], b = rects[j];
      const vGap = Math.min(Math.abs(a.x + a.w - b.x), Math.abs(b.x + b.w - a.x));
      const hGap = Math.min(Math.abs(a.y + a.h - b.y), Math.abs(b.y + b.h - a.y));
      const share = Math.max(tol, 1e-6);
      const vShare = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y) > share;
      const hShare = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x) > share;
      if ((vGap <= tol && vShare) || (hGap <= tol && hShare)) { out[i].push(j); out[j].push(i); }
    }
  }
  return out;
}

/** Which tiles touch which, by shared edge. `[[1,2],[0],...]` */
export function adjacency(n, orientation = 'landscape') {
  return adjacencyFromRects(tableRects(n, orientation), 1e-6);
}

/* --------------------------------------------------------------- stages --*/

/**
 * Grow schedule. Stage k (0 based) shows k+1 tiles and starts at starts[k].
 * If the canvas is too short for one stage per tile the step shrinks so the
 * last tile still lands before the loop, rather than never arriving.
 */
export function stagesFor(tileCount, stageMs, fps, frames) {
  const n = clampCount(tileCount);
  const wanted = Math.max(1, Math.round((stageMs / 1000) * fps));
  const room = n > 1 ? Math.max(1, Math.floor((frames - 1) / (n - 1))) : wanted;
  const step = Math.min(wanted, room);
  const starts = [];
  for (let k = 0; k < n; k++) starts.push(Math.min(frames - 1, k * step));
  return { step, starts, enterFrames: starts.slice(), stages: n, lastFrame: starts[n - 1] };
}

/** Seeded slot permutation for the shuffle layout, stable per (n, stage). */
export function shufflePermutation(n, stage, seed) {
  const idx = [];
  for (let i = 0; i < n; i++) idx.push(i);
  if (stage <= 0) return idx;
  return rngFrom(seed, 'shuffle:' + stage).shuffle(idx);
}

/* ---------------------------------------------------------- per frame ----*/

/**
 * What to draw this frame.
 *
 * cfg: { tileIds, tree, tileCount, mode, stageMs, fps, frames, orientation,
 *        seed, flip }
 *   `tileIds` + `tree` is the real path; without them the fixed table is used,
 *   which is what the pure tests and any caller that has no tree still get.
 *   `flip` turns the finished slots around, once, for every mode: see flipSlot.
 * returns { stage, stages, slots:[{ tileIndex, rect, frameOffset }] }
 *   tileIndex indexes the project's tiles[]; mirror points every slot at 0.
 *   frameOffset shifts that slot's source clock (mirror's droste stagger).
 */
export function layoutAtFrame(cfg, frame) {
  const lay = composeAtFrame(cfg, frame);
  if (!cfg.flip) return lay;
  return Object.assign({}, lay, { slots: lay.slots.map(flipSlot) });
}

/**
 * One slot, turned around. The slot is mirrored across the middle of the
 * frame, its tilt is negated, and any field that reads left to right turns
 * over with it: `crop.cx` pans the other way, so a Ken Burns walk that went
 * right now goes left. The picture itself is never mirrored, so a face stays
 * a face and only the choreography changes hands.
 */
export function flipSlot(slot) {
  const r = slot.rect;
  const out = Object.assign({}, slot, {
    rect: { x: 1 - r.x - r.w, y: r.y, w: r.w, h: r.h },
  });
  if (slot.rot) out.rot = -slot.rot;
  if (slot.crop && slot.crop.cx != null) {
    out.crop = Object.assign({}, slot.crop, { cx: 1 - slot.crop.cx });
  }
  return out;
}

function composeAtFrame(cfg, frame) {
  const ids = cfg.tileIds && cfg.tileIds.length ? cfg.tileIds.slice(0, MAX_TILES) : null;
  const n = clampCount(ids ? ids.length : cfg.tileCount);
  const orientation = cfg.orientation || 'landscape';
  const mode = cfg.mode || 'grow';
  const fps = cfg.fps || 15;
  const frames = Math.max(1, cfg.frames || 75);
  const seed = cfg.seed >>> 0;
  const sched = stagesFor(n, cfg.stageMs || 600, fps, frames);
  const tree = ids ? ensureTree(cfg.tree, ids, orientation) : null;

  // motion layouts: one module each, in layouts/. They read the fixed table,
  // not the dock tree, and hand back slots with alpha, tilt, scale and order.
  const motion = motionLayout(mode);
  if (motion && n >= (motion.minTiles || 1)) {
    const mcfg = motionCfg({
      n, frames, stageFrames: sched.step, seed, orientation, size: cfg.size,
    });
    const f = ((frame % frames) + frames) % frames;
    return { stage: n - 1, stages: n, slots: slotsFrom(motion.rectsAt(mcfg, f), n), backdrop: !!motion.backdrop };
  }

  /** Rects for the first `count` tiles, by tile index, holes as null. */
  const stageRects = (count) => {
    if (!tree) return tableRects(count, orientation);
    const out = new Array(n).fill(null);
    const kept = pruneTree(tree, new Set(ids.slice(0, count)));
    for (const r of treeRects(kept)) out[ids.indexOf(r.id)] = r.rect;
    return out;
  };

  if (mode === 'flat') {
    const rects = stageRects(n);
    const slots = [];
    for (let i = 0; i < n; i++) if (rects[i]) slots.push({ tileIndex: i, rect: rects[i], frameOffset: 0 });
    return { stage: n - 1, stages: n, slots };
  }

  if (mode === 'deck') {
    // engine extension used by the Flash Deck vibe: one tile at a time, full
    // frame, cut every `deckHold` frames. Not offered in the layout picker.
    const hold = Math.max(1, cfg.deckHold || 15);
    const which = Math.floor(frame / hold) % n;
    return {
      stage: which,
      stages: n,
      slots: [{ tileIndex: which, rect: { x: 0, y: 0, w: 1, h: 1 }, frameOffset: 0 }],
    };
  }

  if (mode === 'mirror') {
    const rects = stageRects(n);
    const slots = [];
    for (let i = 0; i < n; i++) {
      if (rects[i]) slots.push({ tileIndex: 0, rect: rects[i], frameOffset: i * sched.step });
    }
    return { stage: n - 1, stages: n, slots };
  }

  if (mode === 'shuffle') {
    // every tile on from frame 0; slots swap on each stage boundary
    const stage = Math.floor(frame / Math.max(1, sched.step));
    const rects = stageRects(n);
    const now = shufflePermutation(n, stage, seed);
    const prev = shufflePermutation(n, stage - 1, seed);
    const u = easeOut((frame - stage * sched.step) / SLIDE_FRAMES);
    const slots = [];
    for (let i = 0; i < n; i++) {
      const to = rects[now[i]];
      if (!to) continue;
      const from = stage > 0 ? (rects[prev[i]] || to) : to;
      slots.push({ tileIndex: i, rect: u >= 1 ? to : lerpRect(from, to, u), frameOffset: 0 });
    }
    return { stage, stages: n, slots };
  }

  // grow: the tree with the tiles that have not entered yet pruned out
  const stage = stageAtFrame(sched.starts, frame);
  const toRects = stageRects(stage + 1);
  const fromRects = stage > 0 ? stageRects(stage) : toRects;
  const u = stage > 0 ? easeOut((frame - sched.starts[stage]) / SLIDE_FRAMES) : 1;
  const slots = [];
  for (let i = 0; i <= stage && i < n; i++) {
    const to = toRects[i];
    if (!to) continue;
    if (u >= 1) { slots.push({ tileIndex: i, rect: to, frameOffset: 0 }); continue; }
    const from = fromRects[i] || seedRect(to);
    slots.push({ tileIndex: i, rect: lerpRect(from, to, u), frameOffset: 0 });
  }
  return { stage, stages: n, slots };
}

/* ------------------------------------------------------------- pixels ----*/

/**
 * Rects by tile index for one frame, null where a tile is not on screen.
 * This is what a hit overlay wants: one rect per tile, in tile order. In
 * mirror mode every slot shows tile 0, so slot i is handed to tile i there,
 * which keeps every tile selectable.
 */
export function rectsAtFrame(cfg, frame) {
  const n = cfg.tileIds ? cfg.tileIds.length : (cfg.tileCount | 0);
  const out = new Array(n).fill(null);
  if (!n) return out;
  const lay = layoutAtFrame(cfg, frame);
  if ((cfg.mode || 'grow') === 'mirror') lay.slots.forEach((s, i) => { if (i < n) out[i] = s.rect; });
  else for (const s of lay.slots) out[s.tileIndex] = s.rect;
  return out;
}

/**
 * Fraction rect -> pixel rect, inset by half the gap on internal edges only,
 * so the mosaic keeps its gutters but still bleeds to the canvas edge.
 */
export function pixelRect(rect, size, gap = GAP_PX) {
  const eps = 1e-4;
  const g = gap / 2;
  const l = rect.x <= eps ? 0 : g;
  const t = rect.y <= eps ? 0 : g;
  const r = rect.x + rect.w >= 1 - eps ? 0 : g;
  const b = rect.y + rect.h >= 1 - eps ? 0 : g;
  const x = rect.x * size.w + l;
  const y = rect.y * size.h + t;
  return {
    x, y,
    w: Math.max(1, rect.w * size.w - l - r),
    h: Math.max(1, rect.h * size.h - t - b),
  };
}

/** Cover fit: source w/h into a destination rect, centre cropped. */
export function coverFit(sw, sh, dw, dh) {
  const scale = Math.max(dw / sw, dh / sh);
  const w = sw * scale, h = sh * scale;
  return { x: (dw - w) / 2, y: (dh - h) / 2, w, h };
}
