// Sound is opt-in and synthesised. In node there is no AudioContext at all, so
// every call has to be a quiet no-op: that is the same path a locked browser takes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { pitchFor, tick, thud, enabled, setEnabled, listen } from '../ui/sound.js';

test('the ladder is one semitone a roll', () => {
  assert.equal(pitchFor(0), 440);
  assert.equal(pitchFor(7), 440 * Math.pow(2, 7 / 12));
  assert.ok(pitchFor(1) > pitchFor(0));
});

test('the ladder stops climbing after seven', () => {
  assert.equal(pitchFor(20), pitchFor(7));
  assert.equal(pitchFor(8), pitchFor(7));
});

test('a bad step never makes a bad note', () => {
  assert.equal(pitchFor(undefined), 440);
  assert.equal(pitchFor(-3), 440);
  assert.ok(Number.isFinite(pitchFor('x')));
});

test('silent by default, and silent with no AudioContext', () => {
  assert.equal(enabled(), false);
  assert.doesNotThrow(() => { tick(1); thud(); });
  setEnabled(true);
  assert.equal(enabled(), true);
  assert.doesNotThrow(() => { tick(3); thud(); });
  setEnabled(false);
  assert.equal(enabled(), false);
});

test('the roll ladder tolerates a page that never emits a roll', () => {
  const heard = [];
  const ctx = { on: (ev, fn) => { heard.push(ev); return () => {}; } };
  assert.doesNotThrow(() => listen(ctx));
  assert.deepEqual(heard, ['roll']);
  assert.doesNotThrow(() => listen({}));
});
