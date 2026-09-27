import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMouseLock, lockedSteer, readEnabled, LOCK_REFUSALS, LOCK_COOLDOWN_MS, STORE_KEY } from './mouse-lock.js';

function fakeDoc() {
  const listeners = {};
  const doc = {
    pointerLockElement: null,
    addEventListener(n, f) { (listeners[n] ||= []).push(f); },
    removeEventListener(n, f) { listeners[n] = (listeners[n] || []).filter(x => x !== f); },
    fire(n) { for (const f of listeners[n] || []) f(); },
    exitPointerLock() { doc.pointerLockElement = null; doc.fire('pointerlockchange'); },
    count: (n) => (listeners[n] || []).length,
  };
  return doc;
}
function fakeCanvas(doc, { refuse = false } = {}) {
  const canvas = { requests: 0, requestPointerLock() { canvas.requests++; if (refuse) return Promise.reject(new Error('no')); doc.pointerLockElement = canvas; doc.fire('pointerlockchange'); return Promise.resolve(); } };
  return canvas;
}
const tick = () => new Promise(r => setTimeout(r, 0));

test('lockedSteer moves the paddle by the mouse count in field px and never past a wall', () => {
  assert.equal(lockedSteer(400, 10, 2, 85, 1280), 420);
  assert.equal(lockedSteer(400, -10, 2, 85, 1280), 380);
  assert.equal(lockedSteer(100, -500, 1, 85, 1280), 85, 'held at the left wall');
  assert.equal(lockedSteer(1200, 500, 1, 85, 1280), 1195, 'held at the right wall');
  assert.equal(lockedSteer(400, undefined, NaN, 85, 1280), 400, 'garbage is a no-move');
});

test('a mouse press takes the mouse; a finger, a pen, an off switch or a held lock never asks', () => {
  const doc = fakeDoc(), canvas = fakeCanvas(doc), lost = [];
  const lock = createMouseLock({ canvas, doc, now: () => 0, onLost: () => lost.push(1) });
  assert.equal(lock.request({ pointerType: 'touch' }), false);
  assert.equal(lock.request({ pointerType: 'pen' }), false);
  assert.equal(canvas.requests, 0);
  assert.equal(lock.request({ pointerType: 'mouse' }), true);
  assert.equal(lock.locked, true);
  assert.equal(lock.request({ pointerType: 'mouse' }), false, 'already held');
  assert.equal(canvas.requests, 1);
  // Esc (the browser lets go): onLost fires once, the station pauses on it.
  doc.exitPointerLock();
  assert.equal(lock.locked, false); assert.deepEqual(lost, [1]);
  lock.setEnabled(false);
  assert.equal(lock.request({ pointerType: 'mouse' }), false, 'switched off');
  lock.setEnabled(true);
  assert.equal(lock.request({}), true, 'no pointerType (a synthetic press) counts as a mouse');
  lock.dispose();
  assert.equal(lock.locked, false, 'dispose lets go');
  assert.equal(doc.count('pointerlockchange'), 0); assert.equal(doc.count('pointerlockerror'), 0);
  assert.equal(lock.request({ pointerType: 'mouse' }), false, 'dead after dispose');
});

test('refusals: four real ones give up for good, but the cooldown after an Esc exit never counts', async () => {
  let t = 0; const doc = fakeDoc(), canvas = fakeCanvas(doc, { refuse: true });
  const lock = createMouseLock({ canvas, doc, now: () => t });
  for (let i = 0; i < LOCK_REFUSALS - 1; i++) { lock.request({ pointerType: 'mouse' }); await tick(); }
  assert.equal(lock.refused, false);
  lock.request({ pointerType: 'mouse' }); await tick();
  assert.equal(lock.refused, true);
  assert.equal(lock.request({ pointerType: 'mouse' }), false, 'no more asking');
  // A lock that worked once, then Esc, then a refusal inside the cooldown: not counted.
  const doc2 = fakeDoc(), ok = fakeCanvas(doc2), lock2 = createMouseLock({ canvas: ok, doc: doc2, now: () => t });
  lock2.request({ pointerType: 'mouse' }); assert.equal(lock2.locked, true);
  t = 100; doc2.exitPointerLock();
  ok.requestPointerLock = () => Promise.reject(new Error('cooldown'));
  for (let i = 0; i < LOCK_REFUSALS + 2; i++) { t = 100 + i * 100; lock2.request({ pointerType: 'mouse' }); await tick(); }
  assert.equal(lock2.refused, false, 'inside LOCK_COOLDOWN_MS of the exit, refusals are the browser cooling down');
  t = 100 + LOCK_COOLDOWN_MS + 1;
  for (let i = 0; i < LOCK_REFUSALS; i++) { lock2.request({ pointerType: 'mouse' }); await tick(); }
  assert.equal(lock2.refused, true);
  assert.ok(LOCK_COOLDOWN_MS >= 1000);
});

test('the preference is on unless stored off', () => {
  assert.equal(readEnabled(null), true);
  assert.equal(readEnabled({ get: () => null }), true);
  assert.equal(readEnabled({ get: (k) => k === STORE_KEY ? '0' : null }), false);
  assert.equal(readEnabled({ get: () => '1' }), true);
  assert.equal(readEnabled({ get: () => { throw new Error('private'); } }), true);
});

test('an intentional release does not fire onLost; Esc still does', () => {
  const doc = fakeDoc(), canvas = fakeCanvas(doc), lost = [];
  const lock = createMouseLock({ canvas, doc, now: () => 0, onLost: () => lost.push(1) });
  // The ending lets the mouse go for its card: quiet.
  lock.request({ pointerType: 'mouse' }); assert.equal(lock.locked, true);
  lock.release();
  assert.equal(lock.locked, false); assert.deepEqual(lost, [], 'a release the station asked for is not a loss');
  // The switch going off lets go: quiet.
  lock.request({ pointerType: 'mouse' }); lock.setEnabled(false);
  assert.equal(lock.locked, false); assert.deepEqual(lost, []);
  lock.setEnabled(true);
  // Esc after a fresh lock: the browser let go by itself, the station pauses on it.
  lock.request({ pointerType: 'mouse' }); assert.equal(lock.locked, true);
  doc.exitPointerLock();
  assert.deepEqual(lost, [1], 'the flag is spent by the release it answered, a later Esc still counts');
  // dispose lets go: quiet.
  lock.request({ pointerType: 'mouse' }); lock.dispose();
  assert.equal(lock.locked, false); assert.deepEqual(lost, [1]);
});

test('a release whose pointerlockchange lands on a later task is still quiet, and the raf backstop release beside it is a no-op', async () => {
  // Chromium queues pointerlockchange; station.js releases on finaleCoreReached AND again from the frame loop while `locked` still reads true.
  const doc = fakeDoc(), lost = [];
  doc.exitPointerLock = () => { doc.pointerLockElement = null; setTimeout(() => doc.fire('pointerlockchange'), 0); };
  const canvas = fakeCanvas(doc);
  const lock = createMouseLock({ canvas, doc, now: () => 0, onLost: () => lost.push(1) });
  lock.request({ pointerType: 'mouse' }); assert.equal(lock.locked, true);
  lock.release(); lock.release();
  assert.equal(lock.locked, true, 'not yet told');
  await tick();
  assert.equal(lock.locked, false); assert.deepEqual(lost, [], 'the ending release never pauses');
  lock.request({ pointerType: 'mouse' }); doc.pointerLockElement = null; doc.fire('pointerlockchange');
  assert.deepEqual(lost, [1], 'a real loss after it still pauses');
});

test('a page without pointer lock is left alone', () => {
  const doc = fakeDoc();
  const lock = createMouseLock({ canvas: {}, doc });
  assert.equal(lock.request({ pointerType: 'mouse' }), false);
  assert.equal(lock.locked, false);
  lock.release(); lock.dispose();
});
