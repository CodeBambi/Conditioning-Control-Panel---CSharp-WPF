import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MAX_TILES, GAP_PX, rectsFor, rectFor, adjacency, stagesFor, stageAtFrame,
  layoutAtFrame, pixelRect, coverFit, lerpRect, shufflePermutation, transposeRect,
} from '../engine/layout.js';

const ORIENTATIONS = ['landscape', 'portrait', 'square'];
const SIZES = { landscape: { w: 480, h: 270 }, portrait: { w: 270, h: 480 }, square: { w: 360, h: 360 } };

test('every stage from 1 to 8 tiles yields that many rects, in bounds', () => {
  for (const o of ORIENTATIONS) {
    for (let n = 1; n <= MAX_TILES; n++) {
      const rects = rectsFor(n, o);
      assert.equal(rects.length, n, `${o} n=${n}`);
      for (const r of rects) {
        assert.ok(r.w > 0 && r.h > 0, 'positive size');
        assert.ok(r.x >= -1e-9 && r.y >= -1e-9, 'inside');
        assert.ok(r.x + r.w <= 1 + 1e-9 && r.y + r.h <= 1 + 1e-9, 'inside');
      }
    }
  }
});

test('the fractions of every stage sum to the whole canvas', () => {
  for (const o of ORIENTATIONS) {
    for (let n = 1; n <= MAX_TILES; n++) {
      const area = rectsFor(n, o).reduce((s, r) => s + r.w * r.h, 0);
      assert.ok(Math.abs(area - 1) < 1e-9, `${o} n=${n} area ${area}`);
    }
  }
});

test('rects never overlap and the gaps are the only uncovered pixels', () => {
  for (const o of ORIENTATIONS) {
    const size = SIZES[o];
    for (let n = 1; n <= MAX_TILES; n++) {
      const rects = rectsFor(n, o).map((r) => pixelRect(r, size, GAP_PX));
      const cover = new Uint8Array(size.w * size.h);
      for (const r of rects) {
        const x0 = Math.round(r.x), y0 = Math.round(r.y);
        for (let y = y0; y < Math.round(r.y + r.h); y++) {
          for (let x = x0; x < Math.round(r.x + r.w); x++) cover[y * size.w + x]++;
        }
      }
      let uncovered = 0, doubled = 0;
      for (let i = 0; i < cover.length; i++) {
        if (cover[i] === 0) uncovered++;
        if (cover[i] > 1) doubled++;
      }
      assert.equal(doubled, 0, `${o} n=${n} overlap`);
      // gutters only: at most one gap line per seam, in either direction
      const maxGutter = MAX_TILES * GAP_PX * (size.w + size.h);
      assert.ok(uncovered <= maxGutter, `${o} n=${n} uncovered ${uncovered}`);
      if (n === 1) assert.equal(uncovered, 0, 'a single tile fills the canvas');
      // the mosaic bleeds to the canvas edge: the corners are always on a tile
      for (const [x, y] of [[0, 0], [size.w - 1, 0], [0, size.h - 1], [size.w - 1, size.h - 1]]) {
        assert.equal(cover[y * size.w + x], 1, `${o} n=${n} corner ${x},${y}`);
      }
    }
  }
});

test('portrait is the landscape table transposed', () => {
  for (let n = 1; n <= MAX_TILES; n++) {
    const land = rectsFor(n, 'landscape');
    const port = rectsFor(n, 'portrait');
    for (let i = 0; i < n; i++) assert.deepEqual(port[i], transposeRect(land[i]));
  }
  assert.deepEqual(rectsFor(4, 'square'), rectsFor(4, 'landscape'));
});

test('the named stages match the spec', () => {
  assert.deepEqual(rectFor(1, 0, 'landscape'), { x: 0, y: 0, w: 1, h: 1 });
  assert.deepEqual(rectsFor(2, 'landscape'), [
    { x: 0, y: 0, w: 0.5, h: 1 }, { x: 0.5, y: 0, w: 0.5, h: 1 },
  ]);
  assert.equal(rectsFor(3, 'landscape')[0].w, 0.5, 'big left');
  assert.equal(rectsFor(3, 'landscape')[0].h, 1);
  assert.deepEqual(rectsFor(2, 'portrait')[0], { x: 0, y: 0, w: 1, h: 0.5 }, 'portrait splits top/bottom');
  // 5 = two on top, three below
  const five = rectsFor(5, 'landscape');
  assert.equal(five.filter((r) => r.y === 0).length, 2);
  assert.equal(five.filter((r) => r.y > 0).length, 3);
  // 7 = three on top, four below
  const seven = rectsFor(7, 'landscape');
  assert.equal(seven.filter((r) => r.y === 0).length, 3);
  assert.equal(seven.filter((r) => r.y > 0).length, 4);
});

test('counts and indexes clamp rather than throwing', () => {
  assert.deepEqual(rectFor(2, 9, 'landscape'), rectsFor(2, 'landscape')[1]);
  assert.equal(rectsFor(0, 'landscape').length, 1);
  assert.equal(rectsFor(99, 'landscape').length, MAX_TILES);
});

test('adjacency is symmetric, self free and matches the obvious cases', () => {
  for (const o of ORIENTATIONS) {
    for (let n = 1; n <= MAX_TILES; n++) {
      const adj = adjacency(n, o);
      assert.equal(adj.length, n);
      for (let i = 0; i < n; i++) {
        assert.ok(!adj[i].includes(i), 'no self');
        for (const j of adj[i]) assert.ok(adj[j].includes(i), `${o} n=${n} ${i}<->${j}`);
        if (n > 1) assert.ok(adj[i].length >= 1, `${o} n=${n} tile ${i} is stranded`);
      }
    }
  }
  assert.deepEqual(adjacency(1, 'landscape'), [[]]);
  assert.deepEqual(adjacency(2, 'landscape'), [[1], [0]]);
  assert.deepEqual(adjacency(4, 'landscape'), [[1, 2], [0, 3], [0, 3], [1, 2]]);
});

test('the grow schedule fits inside the canvas', () => {
  const s = stagesFor(5, 600, 15, 75);
  assert.equal(s.step, 9);
  assert.deepEqual(s.starts, [0, 9, 18, 27, 36]);
  assert.equal(s.enterFrames[0], 0);
  // a short canvas squeezes the step so the last tile still lands
  const tight = stagesFor(8, 900, 15, 45);
  assert.ok(tight.lastFrame < 45, 'last stage inside the loop');
  assert.ok(tight.step >= 1);
  assert.equal(stageAtFrame(s.starts, 0), 0);
  assert.equal(stageAtFrame(s.starts, 8), 0);
  assert.equal(stageAtFrame(s.starts, 9), 1);
  assert.equal(stageAtFrame(s.starts, 999), 4);
});

test('grow reveals tiles over time and settles on the full mosaic', () => {
  const cfg = { tileCount: 4, mode: 'grow', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 1 };
  assert.equal(layoutAtFrame(cfg, 0).slots.length, 1);
  assert.equal(layoutAtFrame(cfg, 9).slots.length, 2);
  assert.equal(layoutAtFrame(cfg, 74).slots.length, 4);
  // mid slide the rects are between the two stages, not popped
  const sliding = layoutAtFrame(cfg, 10).slots[0].rect;
  const settled = layoutAtFrame(cfg, 20).slots[0].rect;
  assert.notDeepEqual(sliding, settled);
});

test('flat shows everything from frame zero', () => {
  const cfg = { tileCount: 3, mode: 'flat', fps: 15, frames: 75, orientation: 'landscape', seed: 1 };
  assert.equal(layoutAtFrame(cfg, 0).slots.length, 3);
  assert.deepEqual(layoutAtFrame(cfg, 0).slots.map((s) => s.tileIndex), [0, 1, 2]);
});

test('mirror repeats the first tile with a stagger', () => {
  const cfg = { tileCount: 4, mode: 'mirror', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 1 };
  const slots = layoutAtFrame(cfg, 30).slots;
  assert.equal(slots.length, 4);
  assert.ok(slots.every((s) => s.tileIndex === 0));
  assert.deepEqual(slots.map((s) => s.frameOffset), [0, 9, 18, 27]);
});

test('shuffle permutes on stage boundaries and repeats for a seed', () => {
  const cfg = { tileCount: 4, mode: 'shuffle', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 77 };
  const a = layoutAtFrame(cfg, 40).slots.map((s) => s.rect);
  const b = layoutAtFrame(cfg, 40).slots.map((s) => s.rect);
  assert.deepEqual(a, b);
  assert.equal(layoutAtFrame(cfg, 0).slots.length, 4);
  const p1 = shufflePermutation(4, 3, 77);
  assert.deepEqual(p1.slice().sort(), [0, 1, 2, 3]);
  assert.deepEqual(shufflePermutation(4, 0, 77), [0, 1, 2, 3]);
});

test('deck cuts one tile at a time', () => {
  const cfg = { tileCount: 3, mode: 'deck', deckHold: 15, fps: 15, frames: 75, orientation: 'landscape', seed: 1 };
  assert.equal(layoutAtFrame(cfg, 0).slots[0].tileIndex, 0);
  assert.equal(layoutAtFrame(cfg, 16).slots[0].tileIndex, 1);
  assert.equal(layoutAtFrame(cfg, 31).slots[0].tileIndex, 2);
  assert.deepEqual(layoutAtFrame(cfg, 0).slots[0].rect, { x: 0, y: 0, w: 1, h: 1 });
});

test('lerpRect and coverFit behave', () => {
  const a = { x: 0, y: 0, w: 1, h: 1 }, b = { x: 1, y: 1, w: 0, h: 0 };
  assert.deepEqual(lerpRect(a, b, 0), a);
  assert.deepEqual(lerpRect(a, b, 1), b);
  const fit = coverFit(100, 50, 200, 200);
  assert.equal(fit.h, 200);
  assert.ok(fit.w >= 200 && fit.x <= 0);
});
