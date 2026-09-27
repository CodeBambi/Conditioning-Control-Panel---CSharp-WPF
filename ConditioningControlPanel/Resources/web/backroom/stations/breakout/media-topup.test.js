import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMedia } from './payloads.js';

const fakeSrc = () => ({ canvas: {}, animated: true, tick() {}, dispose() {} });
const decode = async () => fakeSrc();
const gifs = (n) => Array.from({ length: n }, (_, i) => ({ key: 'g' + i, url: 'https://ccp.assets/.temp/' + i + '.webm' }));

function rig(sizes) {
  const timers = [];
  let calls = 0;
  const ctx = { media: async () => ({ gifs: gifs(sizes[Math.min(calls++, sizes.length - 1)]), words: [] }) };
  const media = createMedia({ ctx, decode, topUpMs: [1, 1, 1], setTimer: (f) => { timers.push(f); return timers.length; }, clearTimer: () => {} });
  const run = async () => { while (timers.length) await timers.shift()(); };
  return { media, run, calls: () => calls, timers };
}

test('a short first deal (cold online pool) is topped up to a full deck', async () => {
  const r = rig([1, 3, 8]);
  await r.media.load();
  assert.equal(r.media.count(), 1);
  await r.run();
  assert.equal(r.media.count(), 8);
  assert.equal(new Set(r.media.keys()).size, 8);
});

test('a full first deal never asks again', async () => {
  const r = rig([8]);
  await r.media.load();
  await r.run();
  assert.equal(r.calls(), 1);
});

test('a top-up that comes back no bigger keeps what is on screen, and the tries are bounded', async () => {
  const r = rig([2, 1, 2, 2, 2, 2]);
  await r.media.load();
  const before = r.media.keys();
  await r.run();
  assert.deepEqual(r.media.keys(), before);
  assert.equal(r.calls(), 4);   // the first deal plus three tries
});

test('dispose cancels a pending top-up', async () => {
  let cleared = 0;
  const ctx = { media: async () => ({ gifs: gifs(1), words: [] }) };
  const media = createMedia({ ctx, decode, topUpMs: [1], setTimer: () => 7, clearTimer: (t) => { if (t === 7) cleared++; } });
  await media.load();
  media.dispose();
  assert.equal(cleared, 1);
});
