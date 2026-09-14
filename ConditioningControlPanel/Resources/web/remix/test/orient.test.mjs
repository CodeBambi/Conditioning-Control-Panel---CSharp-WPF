// The canvas takes its shape from the first gif that lands. Wide is landscape,
// tall is portrait, anything in between is square. Owner call, 2026-09-07.
import test from 'node:test';
import assert from 'node:assert/strict';
import { pickOrientation } from '../engine/ops.js';

test('the usual shapes land where you would expect', () => {
  assert.equal(pickOrientation(1920, 1080), 'landscape');
  assert.equal(pickOrientation(640, 360), 'landscape');
  assert.equal(pickOrientation(1080, 1920), 'portrait');
  assert.equal(pickOrientation(720, 1280), 'portrait');
  assert.equal(pickOrientation(500, 500), 'square');
  assert.equal(pickOrientation(540, 480), 'square');
});

test('the cuts sit at 1.2 and 0.83', () => {
  assert.equal(pickOrientation(121, 100), 'landscape');
  assert.equal(pickOrientation(120, 100), 'square', '1.2 itself is still square');
  assert.equal(pickOrientation(82, 100), 'portrait');
  assert.equal(pickOrientation(83, 100), 'square', '0.83 itself is still square');
});

test('nonsense sizes fall back to landscape instead of throwing', () => {
  for (const [w, h] of [[0, 0], [100, 0], [NaN, 100], [-4, 9], [undefined, undefined]]) {
    assert.equal(pickOrientation(w, h), 'landscape');
  }
});
