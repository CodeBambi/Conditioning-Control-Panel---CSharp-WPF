import test from 'node:test';
import assert from 'node:assert/strict';
import TWIST from './crumble.js';
import RENDER from './crumble-render.js';

/* SCAFFOLD PLACEHOLDER. The crumble lane replaces these with real sim tests. */
test('crumble: the module keeps the shape twists/CONTRACT.md froze', () => {
  assert.equal(TWIST.id, 'crumble');
  for (const hook of ['build', 'onHit', 'onBreak', 'update', 'onCatch', 'wallCleared'])
    if (TWIST[hook] !== undefined) assert.equal(typeof TWIST[hook], 'function', hook + ' must be a function or absent');
  for (const hook of ['brick', 'under', 'over'])
    if (RENDER[hook] !== undefined) assert.equal(typeof RENDER[hook], 'function', hook + ' must be a function or absent');
});
