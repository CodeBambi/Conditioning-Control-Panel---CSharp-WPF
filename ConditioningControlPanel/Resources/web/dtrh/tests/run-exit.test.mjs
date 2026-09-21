import test from 'node:test';
import assert from 'node:assert/strict';

import { createRunBooker } from '../game/runExit.js';

/* The endless-descent run counter (Beppu, 2026-09-20). A timed run reaches its own
 * clock and books at the recap; an endless one has no clock, so leaving is how it
 * always ends. Every exit books now, which is only safe if booking is once-only. */

function recorder() {
  const sent = [];
  return { sent, send: (m) => sent.push(m) };
}

test('nothing is bookable before a descent begins', () => {
  const r = recorder();
  const b = createRunBooker(r.send);

  assert.equal(b.booked, true);
  assert.equal(b.end({ score: 10 }), false);
  assert.equal(b.progress({ score: 10 }), false);
  assert.deepEqual(r.sent, []);
});

test('the first exit books the run', () => {
  const r = recorder();
  const b = createRunBooker(r.send);
  b.begin();

  assert.equal(b.end({ score: 42 }, { abandoned: true }), true);
  assert.equal(r.sent.length, 1);
  assert.equal(r.sent[0].type, 'run-ended');
  assert.equal(r.sent[0].score, 42);
  assert.equal(r.sent[0].abandoned, true);
});

test('a second exit books nothing - the recap and the Escape hold cannot both pay', () => {
  const r = recorder();
  const b = createRunBooker(r.send);
  b.begin();

  assert.equal(b.end({ score: 42 }), true);
  assert.equal(b.end({ score: 42 }, { abandoned: true }), false);
  assert.equal(r.sent.filter((m) => m.type === 'run-ended').length, 1);
});

test('progress pings stop the moment the run is booked', () => {
  const r = recorder();
  const b = createRunBooker(r.send);
  b.begin();

  assert.equal(b.progress({ score: 1 }), true);
  assert.equal(b.progress({ score: 5 }), true);
  b.end({ score: 9 });
  assert.equal(b.progress({ score: 9 }), false);

  assert.deepEqual(r.sent.map((m) => m.type), ['run-progress', 'run-progress', 'run-ended']);
});

test('a new descent is bookable again', () => {
  const r = recorder();
  const b = createRunBooker(r.send);
  b.begin();
  b.end({ score: 1 });
  b.begin();

  assert.equal(b.end({ score: 2 }), true);
  assert.equal(r.sent.filter((m) => m.type === 'run-ended').length, 2);
});
