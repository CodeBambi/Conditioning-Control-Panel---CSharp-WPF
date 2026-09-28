import test from 'node:test';
import assert from 'node:assert/strict';
import { rotateWanted } from '../rotate.js';

test('a phone held sideways gets the upright nudge', () => {
  assert.equal(rotateWanted({ touch: true, w: 844, h: 390 }), true);
});

test('a phone already upright does not', () => {
  assert.equal(rotateWanted({ touch: true, w: 390, h: 844 }), false);
});

test('a maximised 1920x1080 desktop with a touch screen or pen does not (ticket 2026-09-22)', () => {
  assert.equal(rotateWanted({ touch: true, w: 1920, h: 969 }), false);
});

test('no touch never nudges', () => {
  assert.equal(rotateWanted({ touch: false, w: 844, h: 390 }), false);
});
