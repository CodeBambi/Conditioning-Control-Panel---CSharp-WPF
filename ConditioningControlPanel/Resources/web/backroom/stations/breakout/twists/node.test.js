import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST from './node.js';
import RENDER from './node-render.js';

/* SCAFFOLD PLACEHOLDER. The node lane replaces these with real sim tests. */
test('node: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'node');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
});
