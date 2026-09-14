import test from 'node:test';
import assert from 'node:assert/strict';
import {
  abortError, encodeGif, exportVideo, canRecordVideo, NO_VIDEO, delaysFor, frameIndices, READY_TIMEOUT_MS,
} from '../engine/export.js';

test('an aborted export rejects with an AbortError, like fetch', () => {
  const err = abortError();
  assert.equal(err.name, 'AbortError');
  assert.ok(err instanceof Error);
});

test('encodeGif checks the signal before it touches a canvas', async () => {
  // node has no document; an already aborted signal must reject before makeCanvas would throw
  await assert.rejects(
    () => encodeGif({ signal: { aborted: true }, size: { w: 4, h: 4 }, frames: 2, masterFps: 15, drawFrame() {} }),
    (err) => err.name === 'AbortError',
  );
});

test('video export is refused where the recording path is missing', async () => {
  assert.equal(canRecordVideo(), false);
  await assert.rejects(() => exportVideo({ size: { w: 4, h: 4 }, frames: 2, drawFrame() {} }), { message: NO_VIDEO });
});

test('the worker gets eight seconds to say ready before the main thread takes over', () => {
  assert.equal(READY_TIMEOUT_MS, 8000);
});

test('gif delays average the master rate in whole centiseconds', () => {
  const d = delaysFor(15, 15);
  assert.ok(d.every((v) => v % 10 === 0 && v >= 20));
  assert.equal(d.reduce((a, b) => a + b, 0), 1000);
  assert.deepEqual(frameIndices(75, 12, 15).length, 60);
});
