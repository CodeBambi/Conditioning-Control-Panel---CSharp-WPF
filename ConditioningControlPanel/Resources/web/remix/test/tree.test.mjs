/* The split tree: the default has to lay out exactly like the old fixed table,
 * pruning has to collapse cleanly, and docking has to be reversible. */

import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
  defaultTree, treeRects, tableRects, leavesOf, pruneTree, removeFromTree,
  swapInTree, dockInTree, ensureTree, adjacencyFromRects, isLeaf, flipTree,
  layoutAtFrame, rectsAtFrame, MAX_TILES, DOCK_SIDES,
} from '../engine/layout.js';

const ids = (n) => Array.from({ length: n }, (_, i) => 't' + i);
const byId = (rects) => new Map(rects.map((r) => [r.id, r.rect]));

function sameRect(a, b, what) {
  for (const k of ['x', 'y', 'w', 'h']) {
    assert.ok(Math.abs(a[k] - b[k]) < 1e-9, `${what} ${k}: ${a[k]} vs ${b[k]}`);
  }
}

test('the default tree lays out exactly like the fixed table', () => {
  for (const orientation of ['landscape', 'portrait']) {
    for (let n = 1; n <= MAX_TILES; n++) {
      const tree = defaultTree(ids(n), orientation);
      const got = byId(treeRects(tree));
      const want = tableRects(n, orientation);
      assert.equal(got.size, n);
      for (let i = 0; i < n; i++) sameRect(got.get('t' + i), want[i], `${orientation} n=${n} tile ${i}`);
    }
  }
});

test('leaves come back in reading order', () => {
  assert.deepEqual(leavesOf(defaultTree(ids(6), 'landscape')), ids(6));
  assert.deepEqual(leavesOf('only'), ['only']);
  assert.deepEqual(leavesOf(null), []);
});

test('portrait is the landscape tree with every split turned', () => {
  const land = defaultTree(ids(5), 'landscape');
  assert.deepEqual(defaultTree(ids(5), 'portrait'), flipTree(land));
});

test('pruning collapses a node left with one child, and still fills the canvas', () => {
  const tree = defaultTree(ids(4), 'landscape');
  for (let keep = 1; keep <= 4; keep++) {
    const rects = treeRects(pruneTree(tree, ids(keep)));
    assert.equal(rects.length, keep);
    const area = rects.reduce((sum, r) => sum + r.rect.w * r.rect.h, 0);
    assert.ok(Math.abs(area - 1) < 1e-9, `stage ${keep} covers ${area}`);
  }
});

test('one tile left is a bare leaf, no tile left is nothing', () => {
  const tree = defaultTree(ids(3), 'landscape');
  assert.ok(isLeaf(pruneTree(tree, ['t2'])));
  assert.equal(pruneTree(tree, []), null);
  assert.equal(removeFromTree('t0', 't0'), null);
});

test('swap trades two leaves and is its own undo', () => {
  const tree = defaultTree(ids(4), 'landscape');
  const swapped = swapInTree(tree, 't0', 't3');
  const a = byId(treeRects(tree));
  const b = byId(treeRects(swapped));
  sameRect(b.get('t3'), a.get('t0'), 'swapped t3');
  sameRect(b.get('t0'), a.get('t3'), 'swapped t0');
  assert.deepEqual(swapInTree(swapped, 't0', 't3'), tree);
});

test('dock splits the target and never loses or repeats a tile', () => {
  for (const side of DOCK_SIDES) {
    const tree = defaultTree(ids(4), 'landscape');
    const docked = dockInTree(tree, 't0', 't3', side);
    const leaves = leavesOf(docked);
    assert.equal(leaves.length, 4);
    assert.deepEqual([...leaves].sort(), ids(4));
    const rects = byId(treeRects(docked));
    const before = byId(treeRects(tree));
    const target = before.get('t3');
    const moved = rects.get('t0');
    const half = rects.get('t3');
    // the two of them together fill exactly the slot the target had
    const area = moved.w * moved.h + half.w * half.h;
    assert.ok(Math.abs(area - target.w * target.h) < 1e-9, side + ' pair area');
    if (side === 'left') assert.ok(moved.x < half.x);
    if (side === 'right') assert.ok(moved.x > half.x);
    if (side === 'top') assert.ok(moved.y < half.y);
    if (side === 'bottom') assert.ok(moved.y > half.y);
  }
});

test('dock then remove the docked tile puts the rest back where they were', () => {
  const tree = defaultTree(ids(4), 'landscape');
  const docked = dockInTree(tree, 't0', 't3', 'bottom');
  const back = removeFromTree(docked, 't0');
  const want = byId(treeRects(pruneTree(tree, ['t1', 't2', 't3'])));
  const got = byId(treeRects(back));
  for (const id of ['t1', 't2', 't3']) sameRect(got.get(id), want.get(id), 'restored ' + id);
});

test('dock leaves the tree it was given alone', () => {
  const tree = defaultTree(ids(4), 'landscape');
  const copy = JSON.parse(JSON.stringify(tree));
  dockInTree(tree, 't0', 't3', 'left');
  swapInTree(tree, 't1', 't2');
  pruneTree(tree, ['t0']);
  assert.deepEqual(tree, copy);
});

test('a dock nobody can act on is a no-op', () => {
  const tree = defaultTree(ids(3), 'landscape');
  assert.equal(dockInTree(tree, 't0', 't0', 'left'), tree);
  assert.equal(dockInTree(tree, 't0', 'nope', 'left'), tree);
  assert.equal(dockInTree(tree, 't0', 't1', 'sideways'), tree);
});

test('ensureTree drops tiles that went and finds room for tiles that came', () => {
  const tree = defaultTree(ids(4), 'landscape');
  const fewer = ensureTree(tree, ['t0', 't2'], 'landscape');
  assert.deepEqual(leavesOf(fewer).sort(), ['t0', 't2']);

  const more = ensureTree(tree, ids(5), 'landscape');
  assert.deepEqual(leavesOf(more).sort(), ids(5).sort());
  const rects = treeRects(more);
  assert.equal(rects.length, 5);
  const area = rects.reduce((sum, r) => sum + r.rect.w * r.rect.h, 0);
  assert.ok(Math.abs(area - 1) < 1e-9);

  assert.equal(ensureTree(null, [], 'landscape'), null);
  assert.deepEqual(ensureTree(null, ids(2), 'landscape'), defaultTree(ids(2), 'landscape'));
});

test('adjacency off the tree rects finds the shared edges', () => {
  const rects = treeRects(defaultTree(ids(4), 'landscape')).map((r) => r.rect);
  const adj = adjacencyFromRects(rects, 1e-6);
  // a 2x2: every tile touches the two beside it, not the one across the corner
  assert.deepEqual(adj[0].sort(), [1, 2]);
  assert.deepEqual(adj[3].sort(), [1, 2]);
});

test('a grow stage is the tree with the tiles that have not arrived pruned out', () => {
  const cfg = {
    tileIds: ids(4), tree: defaultTree(ids(4), 'landscape'),
    mode: 'grow', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 1,
  };
  const first = layoutAtFrame(cfg, 0);
  assert.equal(first.slots.length, 1);
  sameRect(first.slots[0].rect, { x: 0, y: 0, w: 1, h: 1 }, 'stage 0');

  const last = layoutAtFrame(cfg, 74);
  assert.equal(last.slots.length, 4);
  const want = tableRects(4, 'landscape');
  last.slots.forEach((s, i) => sameRect(s.rect, want[i], 'final ' + i));
});

test('a docked tree drives the frame layout, not the table', () => {
  const tree = dockInTree(defaultTree(ids(2), 'landscape'), 't0', 't1', 'top');
  const out = layoutAtFrame({
    tileIds: ids(2), tree, mode: 'flat', fps: 15, frames: 45, orientation: 'landscape', seed: 1,
  }, 0);
  const t0 = out.slots.find((s) => s.tileIndex === 0).rect;
  const t1 = out.slots.find((s) => s.tileIndex === 1).rect;
  sameRect(t0, { x: 0, y: 0, w: 1, h: 0.5 }, 'docked t0');
  sameRect(t1, { x: 0, y: 0.5, w: 1, h: 0.5 }, 'docked t1');
});

test('a pixel gap is cut out of the split, not off the canvas', () => {
  const tree = defaultTree(ids(2), 'landscape');
  const rects = treeRects(tree, { x: 0, y: 0, w: 480, h: 270 }, 4);
  sameRect(rects[0].rect, { x: 0, y: 0, w: 238, h: 270 }, 'left half');
  sameRect(rects[1].rect, { x: 242, y: 0, w: 238, h: 270 }, 'right half');
});

test('rectsAtFrame hands the overlay one rect per tile, null for tiles not on yet', () => {
  const cfg = { tileIds: ids(4), tree: null, mode: 'grow', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 1 };
  const early = rectsAtFrame(cfg, 0);
  assert.equal(early.length, 4);
  sameRect(early[0], { x: 0, y: 0, w: 1, h: 1 }, 'first tile alone');
  assert.deepEqual(early.slice(1), [null, null, null]);
  const late = rectsAtFrame(cfg, 74);
  const want = tableRects(4, 'landscape');
  late.forEach((r, i) => sameRect(r, want[i], 'tile ' + i));
  assert.deepEqual(rectsAtFrame({ tileIds: [], mode: 'flat', fps: 15, frames: 45 }, 3), []);
});

test('rectsAtFrame follows a docked tree and keeps mirror tiles selectable', () => {
  const tree = dockInTree(defaultTree(ids(2), 'landscape'), 't0', 't1', 'top');
  const docked = rectsAtFrame({ tileIds: ids(2), tree, mode: 'flat', fps: 15, frames: 45, orientation: 'landscape', seed: 1 }, 0);
  sameRect(docked[0], { x: 0, y: 0, w: 1, h: 0.5 }, 'docked t0');
  sameRect(docked[1], { x: 0, y: 0.5, w: 1, h: 0.5 }, 'docked t1');
  const mirror = rectsAtFrame({ tileIds: ids(3), tree: null, mode: 'mirror', fps: 15, frames: 45, orientation: 'landscape', seed: 1 }, 10);
  assert.equal(mirror.filter(Boolean).length, 3);
  const want = tableRects(3, 'landscape');
  mirror.forEach((r, i) => sameRect(r, want[i], 'mirror slot ' + i));
});
