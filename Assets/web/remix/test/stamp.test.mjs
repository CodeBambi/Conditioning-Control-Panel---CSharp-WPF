// The stamp's landing: pure, seed free, and the same on the preview as on the export.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { stampPose, stampRect, REST_POSE, LAND_FRAMES } from '../engine/stamp.js';
import { STAMP_LAND_DEFAULT } from '../engine/render.js';

const FRAMES = 75; // 5 s at 15 fps

test('the stamp starts big and half lit on frame 0', () => {
  const p = stampPose(0, FRAMES);
  assert.equal(p.scale, 1.35);
  assert.equal(p.alpha, 0.4);
});

test('it is at rest by frame 4 and stays there', () => {
  for (const f of [LAND_FRAMES, 5, 30, 74]) {
    assert.deepEqual(stampPose(f, FRAMES), REST_POSE, `frame ${f}`);
  }
  assert.equal(REST_POSE.scale, 1);
  assert.equal(REST_POSE.alpha, 0.9);
});

test('it only ever presses down, never back up', () => {
  let last = stampPose(0, FRAMES);
  for (let f = 1; f <= 6; f++) {
    const p = stampPose(f, FRAMES);
    assert.ok(p.scale <= last.scale, `scale grew at frame ${f}`);
    assert.ok(p.alpha >= last.alpha, `alpha dropped at frame ${f}`);
    assert.ok(p.scale >= 1 && p.alpha <= 0.9);
    last = p;
  }
});

test('a loop under a second never lands', () => {
  assert.deepEqual(stampPose(0, 14), REST_POSE);
  assert.deepEqual(stampPose(0, 0), REST_POSE);
  assert.deepEqual(stampPose(0, undefined), REST_POSE);
});

test('the rect a caption stays clear of is the rest rect', () => {
  const size = { w: 480, h: 270 };
  const r = stampRect(null, { size });
  assert.ok(r.w > 0 && r.h > 0);
  assert.equal(r.x, Math.round((size.w - r.w) / 2));
  assert.ok(r.y + r.h < size.h);
});

test('the landing is an owner call and ships off', () => {
  assert.equal(STAMP_LAND_DEFAULT, false);
});
