import test from 'node:test';
import assert from 'node:assert/strict';
import {
  FPS, DURATIONS, PLAY_MODES, framesForSeconds, secondsForFrames,
  resampleIndices, totalDuration, sourceIndexFor, loopPhase,
} from '../engine/clock.js';

test('the master clock is 15 fps and the three lengths line up', () => {
  assert.equal(FPS, 15);
  assert.deepEqual(DURATIONS, [3, 5, 8]);
  assert.deepEqual(DURATIONS.map((s) => framesForSeconds(s)), [45, 75, 120]);
  assert.equal(secondsForFrames(75), 5);
});

test('a still image resamples to one frame repeated', () => {
  assert.deepEqual(resampleIndices([], 5), [0, 0, 0, 0, 0]);
  assert.deepEqual(resampleIndices([Infinity], 3), [0, 0, 0]);
});

test('a source resampled to 15 fps loops to fill the canvas', () => {
  // 4 frames at 100 ms = 400 ms of source, canvas is 75 frames = 5000 ms
  const idx = resampleIndices([100, 100, 100, 100], 75);
  assert.equal(idx.length, 75);
  assert.ok(idx.every((v) => v >= 0 && v < 4));
  assert.equal(idx[0], 0);
  // 66.7 ms per master frame: frame 1 is at 66 ms -> source 0, frame 2 at 133 -> source 1
  assert.equal(idx[1], 0);
  assert.equal(idx[2], 1);
  // the source wraps at 400 ms, which is master frame 6
  assert.equal(idx[6], 0);
});

test('a slow source holds each of its frames for several master frames', () => {
  const idx = resampleIndices([500, 500], 30);
  assert.equal(idx[0], 0);
  assert.equal(idx[7], 0);
  assert.equal(idx[8], 1);
  assert.equal(idx[15], 0, 'wraps after 1000 ms');
});

test('resample is monotone inside a loop and never out of range', () => {
  const dur = [40, 90, 160, 30, 70];
  const idx = resampleIndices(dur, 120);
  for (const v of idx) assert.ok(Number.isInteger(v) && v >= 0 && v < dur.length);
  assert.equal(totalDuration(dur), 390);
  assert.equal(totalDuration([5]), 10, 'gif delays under 10 ms are floored');
});

test('forward runs from canvas frame zero, hold waits for the tile to land', () => {
  assert.equal(sourceIndexFor('forward', 0, 10, 6), 0);
  assert.equal(sourceIndexFor('forward', 10, 10, 6), 4);
  assert.equal(sourceIndexFor('hold', 10, 10, 6), 0);
  assert.equal(sourceIndexFor('hold', 13, 10, 6), 3);
  assert.equal(sourceIndexFor('hold', 2, 10, 6), 0, 'before it lands it sits on frame one');
});

test('rewind and boomerang', () => {
  assert.equal(sourceIndexFor('rewind', 0, 0, 5), 4);
  assert.equal(sourceIndexFor('rewind', 1, 0, 5), 3);
  assert.equal(sourceIndexFor('rewind', 5, 0, 5), 4);
  const bounce = [];
  for (let f = 0; f < 10; f++) bounce.push(sourceIndexFor('boomerang', f, 0, 4));
  assert.deepEqual(bounce, [0, 1, 2, 3, 2, 1, 0, 1, 2, 3]);
});

test('every play mode is safe on a single frame source', () => {
  for (const m of PLAY_MODES) {
    for (let f = 0; f < 20; f++) assert.equal(sourceIndexFor(m, f, 3, 1), 0, m);
  }
});

test('the mirror offset shifts a slot into its own part of the loop', () => {
  assert.equal(sourceIndexFor('forward', 0, 0, 8, 3), 3);
  assert.equal(sourceIndexFor('forward', 7, 0, 8, 3), 2);
});

test('loop markers only touch the tail', () => {
  assert.equal(loopPhase('clean', 74, 75), null);
  assert.equal(loopPhase('snap', 71, 75), null);
  assert.ok(loopPhase('snap', 72, 75));
  assert.equal(loopPhase('snap', 74, 75).u, 1);
  assert.ok(loopPhase('seamless', 69, 75));
  assert.equal(loopPhase('seamless', 68, 75), null);
  assert.equal(loopPhase('seamless', 69, 75).mixFrame, 0);
});

test('a longer canvas re-decodes only the strips that hit the old cap with source to spare', async () => {
  const { stripNeedsMore } = await import('../engine/clock.js');
  // a 2 s gif on a 5 s canvas is 30 frames and loops: nothing missing at 8 s
  assert.equal(stripNeedsMore(30, 2000, 75, 120), false);
  // a 10 s gif was capped at 75: it has more to show at 120
  assert.equal(stripNeedsMore(75, 10000, 75, 120), true);
  // a 5 s gif fit the 5 s canvas exactly: complete, nothing to add
  assert.equal(stripNeedsMore(75, 5000, 75, 120), false);
  // a still, a shorter canvas, or a strip already long enough: no
  assert.equal(stripNeedsMore(1, 0, 75, 120), false);
  assert.equal(stripNeedsMore(75, 10000, 75, 45), false);
  assert.equal(stripNeedsMore(120, 10000, 45, 75), false);
});
