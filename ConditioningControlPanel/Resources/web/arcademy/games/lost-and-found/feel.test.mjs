import test from 'node:test';
import assert from 'node:assert/strict';
import { deal, references, peel } from './feel-model.js';

test('every deal has unique art, reachable references, and clears in any target order', () => {
  for (let seed = 0; seed < 120; seed++) {
    const state = deal(String(seed));
    assert.equal(new Set(state.piles.flat()).size, 27);
    references(state);
    for (let turn = 0; turn < 27; turn++) {
      assert.ok(state.targets.length > 0);
      assert.equal(new Set(state.targets).size, state.targets.length);
      const target = state.targets[(seed + turn) % state.targets.length];
      const seat = state.piles.findIndex(p => p.at(-1) === target);
      assert.ok(seat >= 0);
      assert.equal(peel(state, seat), true);
    }
    assert.equal(state.found, 27);
    assert.deepEqual(state.targets, []);
    assert.equal(state.piles.flat().length, 0);
  }
});

test('a wrong picture stays in place, and replay deals the same board', () => {
  const state = deal('repeat');
  assert.deepEqual(state, deal('repeat'));
  references(state);
  const seat = state.piles.findIndex(p => !state.targets.includes(p.at(-1)));
  const before = structuredClone(state.piles);
  assert.equal(peel(state, seat), false);
  assert.deepEqual(state.piles, before);
  assert.equal(state.misses, 1);
  assert.equal(state.found, 0);
});
