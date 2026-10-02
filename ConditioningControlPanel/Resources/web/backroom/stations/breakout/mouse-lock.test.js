import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMouseLock, lockedSteer, readEnabled, clampStep, lockedMovement, LOCK_MAX_STEP, LOCK_REFUSALS, LOCK_COOLDOWN_MS, STORE_KEY } from './mouse-lock.js';

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

/* ccp-bugs #1337: the paddle jumped in its direction of travel. A spurious movementX burst under pointer lock (and the
 * cursor's jump into lock) went straight into the paddle target. */
const moveEvent = (movementX, reports) => ({ movementX, ...(reports ? { getCoalescedEvents: () => reports.map(v => ({ movementX: v })) } : {}) });

test('the cap sits above a flat-out human sweep on the slowest common mouse', () => {
  const sweepPxPerMs = 1920 / 120;                   // a whole 1920 px screen in 120 ms
  assert.ok(LOCK_MAX_STEP > sweepPxPerMs * 8, '125 Hz report (8 ms) of that sweep passes untouched');
  assert.ok(LOCK_MAX_STEP > sweepPxPerMs * 1, '1000 Hz report passes untouched');
  assert.ok(LOCK_MAX_STEP < 400, 'a re-centre warp of hundreds of px does not');
});

test('clampStep leaves normal reports alone, holds a burst to the cap, and treats garbage as no move', () => {
  for (const v of [0, 1, -1, 12.5, -37, 128, -128, LOCK_MAX_STEP]) assert.equal(clampStep(v), v);
  assert.equal(clampStep(900), LOCK_MAX_STEP);
  assert.equal(clampStep(-2400), -LOCK_MAX_STEP);
  assert.equal(clampStep(undefined), 0);
  assert.equal(clampStep(NaN), 0);
  assert.equal(clampStep(Infinity), 0);
  assert.equal(clampStep('x'), 0);
});

test('lockedMovement clamps each raw report, so a long frame keeps all of the real travel', () => {
  // A heavy pendulum frame folds twelve 1000 Hz reports of 30 px into one event of 360: none is a spike, all of it counts.
  assert.equal(lockedMovement(moveEvent(360, Array(12).fill(30))), 360);
  // The same frame with one spurious 700 px report inside it: only that report is cut.
  assert.equal(lockedMovement(moveEvent(30 * 11 + 700, [...Array(11).fill(30), 700])), 30 * 11 + LOCK_MAX_STEP);
  assert.equal(lockedMovement(moveEvent(-30 * 11 - 700, [...Array(11).fill(-30), -700])), -30 * 11 - LOCK_MAX_STEP);
});

test('lockedMovement falls back to the event itself when the coalesced list is absent, empty or disagrees', () => {
  assert.equal(lockedMovement(moveEvent(40)), 40, 'no coalesced list');
  assert.equal(lockedMovement(moveEvent(900)), LOCK_MAX_STEP, 'no coalesced list, a burst is still cut');
  assert.equal(lockedMovement(moveEvent(40, [])), 40, 'empty list (a synthetic event)');
  assert.equal(lockedMovement(moveEvent(90, [0, 0, 0])), 90, 'a build that reports 0 per entry never freezes the paddle');
  assert.equal(lockedMovement({ movementX: 50, getCoalescedEvents() { throw new Error('nope'); } }), 50);
  assert.equal(lockedMovement(null), 0);
  assert.equal(lockedMovement({}), 0);
});

test('normal movement is untouched: every ordinary report reaches the paddle exactly', () => {
  let x = 640;
  for (const v of [3, 8, 14, 22, 31, 22, 14, 8, 3, -5, -18, -40, -18, -5]) x = lockedSteer(x, lockedMovement(moveEvent(v, [v])), 1, 85, 1280);
  assert.equal(x, 640 + (3 + 8 + 14 + 22 + 31 + 22 + 14 + 8 + 3) - (5 + 18 + 40 + 18 + 5));
});

test('a lock just taken drops its first move (the cursor jump), then steers every move after it', () => {
  const doc = fakeDoc(), canvas = fakeCanvas(doc);
  const lock = createMouseLock({ canvas, doc, now: () => 0 });
  lock.request({ pointerType: 'mouse' });
  assert.equal(lock.movement(moveEvent(140)), 0, 'the first move after the lock is the jump into lock');
  assert.equal(lock.movement(moveEvent(20)), 20);
  assert.equal(lock.movement(moveEvent(20)), 20, 'only one move is dropped');
  assert.equal(lock.movement(moveEvent(1000)), LOCK_MAX_STEP, 'a burst mid-play is held to the cap');
  // Losing the lock and taking it back drops the first move again; staying locked never does.
  doc.pointerLockElement = null; doc.fire('pointerlockchange');
  lock.request({ pointerType: 'mouse' });
  assert.equal(lock.movement(moveEvent(-90)), 0);
  assert.equal(lock.movement(moveEvent(-15)), -15);
  doc.fire('pointerlockchange');                     // a repeat change while still locked is not a new lock
  assert.equal(lock.movement(moveEvent(-15)), -15);
  lock.dispose();
});
