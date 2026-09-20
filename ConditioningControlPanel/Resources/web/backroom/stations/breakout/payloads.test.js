import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createSubliminals } from './payloads.js';

test('near-ball words use the full landscape width', () => {
  const subs = createSubliminals({ words: [{ text: 'DROP' }], rng: () => .5 });
  const ball = { x: 1100, y: 400 };
  const word = subs.tick(0, 1, true, ball);
  assert.ok(Math.abs(word.x - ball.x) <= 74);
  assert.ok(word.x > 1000);
});
