import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ENDLESS_SAVE_KEY, validateCheckpoint, readCheckpoint, writeCheckpoint, checkpointFromSnapshot, newEndlessSeed, previewCheckpoint } from './endless-save.js';
const saved = { version: 1, seed: 2709, from: 4, savedSaturation: .63, bestCombo: 17 };

test('checkpoint round-trips only the board boundary fields', () => {
  const data = new Map(), storage = { get: k => data.get(k), set: (k,v) => data.set(k,v) };
  assert.equal(writeCheckpoint(storage, { ...saved, bricks: ['do not save a partial board'] }), true);
  assert.deepEqual(readCheckpoint(storage), saved);
  assert.equal(data.has(ENDLESS_SAVE_KEY), true);
});

test('damaged, stale and out-of-range saves are ignored', () => {
  for (const value of [null, [], {}, { ...saved, version: 2 }, { ...saved, seed: -1 },
    { ...saved, seed: 4294967296 }, { ...saved, seed: '2709' }, { ...saved, from: Infinity },
    { ...saved, from: 1000000 }, { ...saved, from: .5 }, { ...saved, savedSaturation: null },
    { ...saved, savedSaturation: NaN }, { ...saved, savedSaturation: 1.1 }, { ...saved, bestCombo: -1 }]) {
    assert.equal(validateCheckpoint(value), null);
  }
  assert.equal(readCheckpoint({ get: () => '{bad' }), null);
  assert.equal(readCheckpoint({ get: () => { throw Error('blocked'); } }), null);
  assert.equal(writeCheckpoint({ set: () => { throw Error('full'); } }, saved), false);
  assert.equal(writeCheckpoint({ set: () => false }, saved), false);
});

test('grey checkpoints retain the recovered colour and Story produces no checkpoint', () => {
  const s = { endless: true, endlessSeed: 2709, stats: { walls: 4 }, state: 'grey', sat: 0, savedSat: .63, comboBest: 17 };
  assert.deepEqual(checkpointFromSnapshot(s), saved);
  assert.equal(checkpointFromSnapshot({ ...s, endless: false }), null);
  assert.equal(checkpointFromSnapshot({ ...s, endlessSeed: NaN }), null);
  assert.equal(checkpointFromSnapshot({ ...s, state: 'colour', sat: .8 }).savedSaturation, .8);
});

test('preview board is one-based and does not accept malformed seeds or indices', () => {
  assert.deepEqual(previewCheckpoint(new URLSearchParams('endless=1&seed=2709&board=1')), { ...saved, from: 0, savedSaturation: .15, bestCombo: 0 });
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&seed=2709&sat=.9')).savedSaturation, .9);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&seed=2709&sat=9')).savedSaturation, 1);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&seed=2709&sat=-9')).savedSaturation, 0);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&seed=2709&sat=bad')).savedSaturation, .15);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&board=0')), null);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1&seed=oops')), null);
  assert.equal(previewCheckpoint(new URLSearchParams('board=3')), null);
  assert.equal(previewCheckpoint(new URLSearchParams('endless=1')), null);
});

test('new runs receive a uint32 seed different from the previous run', () => {
  let previous = 2709;
  for (let i = 0; i < 32; i++) {
    const next = newEndlessSeed(previous);
    assert.ok(Number.isInteger(next) && next >= 0 && next <= 0xffffffff);
    assert.notEqual(next, previous); previous = next;
  }
});
