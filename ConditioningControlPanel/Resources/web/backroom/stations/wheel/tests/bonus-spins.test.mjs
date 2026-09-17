import test from 'node:test';
import assert from 'node:assert/strict';
import { bonusSpinsOf, canSpinWheel } from '../wheel.js';
test('daily wheel and earned credits remain independent', () => {
  assert.equal(canSpinWheel(null), false);
  assert.equal(canSpinWheel({ spun: false, bonusSpins: 0 }), true);
  assert.equal(canSpinWheel({ spun: true }), false);
  assert.equal(canSpinWheel({ spun: true, bonusSpins: 2 }), true);
  assert.equal(canSpinWheel({ spun: true, bonusSpins: 0 }), false);
});
test('invalid credits never unlock a spent daily wheel', () => {
  for (const bonusSpins of [-1, 0.5, Infinity, NaN, 'bad']) {
    assert.equal(bonusSpinsOf({ bonusSpins }), 0);
    assert.equal(canSpinWheel({ spun: true, bonusSpins }), false);
  }
});
