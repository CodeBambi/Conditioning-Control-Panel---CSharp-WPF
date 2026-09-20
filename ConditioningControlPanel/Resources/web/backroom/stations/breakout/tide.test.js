import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tidePose, TIDE_ROWS, TIDE_COLS } from './tide.js';

test('Tide rotated faces stay inside the upper field throughout a complete wave', () => {
  for (const [w, h] of [[1280, 720], [960, 540]]) {
    for (let time = 0; time <= 30; time += .2) for (let row = 0; row < TIDE_ROWS; row++) for (let col = 0; col < TIDE_COLS; col++) {
      const b = tidePose(row, col, time, w, h);
      const rx = (Math.abs(Math.cos(b.angle)) * b.w + Math.abs(Math.sin(b.angle)) * b.h) / 2;
      const ry = (Math.abs(Math.sin(b.angle)) * b.w + Math.abs(Math.cos(b.angle)) * b.h) / 2;
      assert.ok(b.x + b.w / 2 - rx > w * .04 && b.x + b.w / 2 + rx < w * .96);
      assert.ok(b.y + b.h / 2 - ry > h * .05 && b.y + b.h / 2 + ry < h * .45);
    }
  }
});

test('Tide ribbons drift oppositely and reduced motion keeps their initial poses', () => {
  for (let row = 0; row < TIDE_ROWS; row++) {
    const a = tidePose(row, 5, 0), b = tidePose(row, 5, 1);
    assert.ok(row % 2 ? b.x < a.x : b.x > a.x);
    assert.deepEqual(tidePose(row, 5, 20, 1280, 720, true), a);
    const next = tidePose(row, 5, 1 + 1 / 120);
    assert.ok(Math.hypot(next.x - b.x, next.y - b.y) < .1, 'small physics steps, not jumps');
  }
});
