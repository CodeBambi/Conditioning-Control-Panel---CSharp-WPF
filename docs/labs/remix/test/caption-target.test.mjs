// Owner call, 2026-09-07: a caption always paints the whole canvas. A word
// inside one tile of a mosaic is too small to read, so the engine ignores the
// target it is handed rather than trusting every caller to remember.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createOps } from '../engine/ops.js';
import { defaultParams } from '../engine/blocks.js';
import { stampRect } from '../engine/stamp.js';

function ops(n = 3) {
  const tiles = [];
  for (let i = 0; i < n; i++) tiles.push({ id: 't' + i, mediaId: 'm' + i, enterFrame: i * 9, playMode: 'forward' });
  const state = {
    frames: 75, fps: 15, seed: 4242, orientation: 'landscape',
    media: tiles.map((t) => ({ id: t.mediaId, name: t.mediaId, strip: [{}, {}] })),
    tiles, layout: { mode: 'grow', stageMs: 600 }, loop: 'snap', blocks: [],
    stampCorner: 'br', captionText: '', shrink: 0,
  };
  let frame = 0;
  return createOps({
    state,
    changed() {},
    commit() {},
    mediaById: (id) => state.media.find((m) => m.id === id) || null,
    getFrame: () => frame,
    setFrame: (i) => { frame = i; },
  });
}

test('a caption asked for a tile lands on the canvas anyway', () => {
  const o = ops();
  const b = o.addBlock({ effect: 'caption', mode: 'text', target: 't1', start: 0, end: 40 });
  assert.equal(b.target, 'canvas');
});

test('blockFor a tile hands back the canvas caption, never a second one', () => {
  const o = ops();
  const a = o.blockFor('caption', 'canvas');
  const b = o.blockFor('caption', 't2');
  assert.equal(b.id, a.id);
  assert.equal(b.target, 'canvas');
});

test('every other effect still takes the target it is given', () => {
  const o = ops();
  for (const effect of ['tint', 'spiral', 'glitch', 'focus', 'drain']) {
    assert.equal(o.addBlock({ effect, target: 't1', start: 0, end: 30 }).target, 't1', effect);
  }
});

test('a caption flash forced to the canvas is still one frame', () => {
  const o = ops();
  const b = o.blockFor('caption', 't0');
  assert.equal(b.target, 'canvas');
  const flash = o.addBlock({ effect: 'caption', mode: 'flash', target: 't1', start: 46, end: 47 });
  assert.equal(flash.target, 'canvas');
  assert.equal(flash.end - flash.start, 1);
});

// Owner call, 2026-09-07: a new caption sits centre bottom, not dead centre,
// and it never lands on top of the stamp in the corner.
test('a fresh caption starts centred across and low down', () => {
  const pos = defaultParams('caption').pos;
  assert.equal(pos.x, 0.5);
  assert.ok(pos.y > 0.7 && pos.y < 0.85, `y is ${pos.y}`);
  assert.equal(pos.y, 0.78);
  assert.equal(ops().blockFor('caption', 'canvas').params.pos.y, 0.78);
});

test('the default word clears the stamp on both canvas shapes', () => {
  const p = defaultParams('caption');
  for (const size of [{ w: 480, h: 270 }, { w: 270, h: 480 }]) {
    // what caption.js draws: a middle baseline, the word about 0.21 of the height tall
    const half = size.h * (0.07 + (p.size / 100) * 0.26) / 2;
    const bottom = p.pos.y * size.h + half;
    const stamp = stampRect(null, { size, corner: 'br' });
    assert.ok(bottom < stamp.y, `word bottom ${bottom} vs stamp top ${stamp.y} on ${size.w}x${size.h}`);
  }
});
