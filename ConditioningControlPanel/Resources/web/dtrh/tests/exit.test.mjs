import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { createExit, installExitHold } from '../exit.js';
import { createRunBooker } from '../game/runExit.js';

/* The Escape hold, executed. Beppu, 2026-09-20: holding Escape out of an endless
 * descent banked nothing, because boot.js called shutdown() straight past endRun.
 * These drive the real exit path against a stub window and a real booker, and the
 * assertion that matters is the ORDER: run-ended, carrying abandoned, then exit. */

/** The three lines of `window` this path uses. */
function stubWindow() {
  const handlers = new Map();
  return {
    addEventListener: (type, fn) => handlers.set(type, fn),
    removeEventListener: (type) => handlers.delete(type),
    fire: (type, event) => { const fn = handlers.get(type); if (fn) fn(event); },
    has: (type) => handlers.has(type),
  };
}

/** A clock the test winds by hand. */
function stubClock() {
  const timers = new Map();
  let next = 1;
  return {
    setTimer: (fn, ms) => { const id = next++; timers.set(id, { fn, ms }); return id; },
    clearTimer: (id) => timers.delete(id),
    runAll: () => { const due = [...timers.values()]; timers.clear(); due.forEach((t) => t.fn()); },
    pending: () => timers.size,
  };
}

/** A run brain with the one method the exit path calls, over the real booker. */
function stubGame(send, { falling = true } = {}) {
  const booker = createRunBooker(send);
  if (falling) booker.begin();
  return {
    abandonRun: () => booker.end({ score: 4200, elapsedSec: 275 }, { abandoned: true }),
    booker,
  };
}

test('holding Escape books the descent, then leaves', () => {
  const sent = [];
  const win = stubWindow();
  const clock = stubClock();
  const game = stubGame((m) => sent.push(m));
  const leave = createExit({ send: (m) => sent.push(m), getGame: () => game, shutdown: () => {} });

  installExitHold({ target: win, leave, holdMs: 1200, setTimer: clock.setTimer, clearTimer: clock.clearTimer });
  win.fire('keydown', { key: 'Escape' });
  clock.runAll();

  assert.deepEqual(sent.map((m) => m.type), ['run-ended', 'exit']);
  assert.equal(sent[0].abandoned, true);
  assert.equal(sent[0].score, 4200);
});

test('a tap is not an exit - nothing is sent and nothing is booked', () => {
  const sent = [];
  const win = stubWindow();
  const clock = stubClock();
  const game = stubGame((m) => sent.push(m));
  const leave = createExit({ send: (m) => sent.push(m), getGame: () => game, shutdown: () => {} });

  installExitHold({ target: win, leave, setTimer: clock.setTimer, clearTimer: clock.clearTimer });
  win.fire('keydown', { key: 'Escape' });
  win.fire('keyup', { key: 'Escape' });
  clock.runAll();

  assert.deepEqual(sent, []);
  assert.equal(game.booker.booked, false);
});

test('a key repeat does not restart the hold into a second exit', () => {
  const sent = [];
  const win = stubWindow();
  const clock = stubClock();
  const leave = createExit({ send: (m) => sent.push(m), getGame: () => null, shutdown: () => {} });

  installExitHold({ target: win, leave, setTimer: clock.setTimer, clearTimer: clock.clearTimer });
  win.fire('keydown', { key: 'Escape' });
  win.fire('keydown', { key: 'Escape', repeat: true });
  assert.equal(clock.pending(), 1);
  clock.runAll();

  assert.deepEqual(sent.map((m) => m.type), ['exit']);
});

test('leaving from the hub sends exit and books nothing', () => {
  const sent = [];
  const leave = createExit({ send: (m) => sent.push(m), getGame: () => null, shutdown: () => {} });

  assert.equal(leave(), false);
  assert.deepEqual(sent.map((m) => m.type), ['exit']);
});

test('a booking that throws still lets the player out', () => {
  const sent = [];
  const game = { abandonRun: () => { throw new Error('the brain is gone'); } };
  let shutdownRan = false;
  const leave = createExit({ send: (m) => sent.push(m), getGame: () => game, shutdown: () => { shutdownRan = true; } });

  assert.equal(leave(), false);
  assert.deepEqual(sent.map((m) => m.type), ['exit']);
  assert.equal(shutdownRan, true);
});

test('the disposer takes the listeners back off', () => {
  const win = stubWindow();
  const clock = stubClock();
  const off = installExitHold({ target: win, leave: () => {}, setTimer: clock.setTimer, clearTimer: clock.clearTimer });

  assert.equal(win.has('keydown'), true);
  off();
  assert.equal(win.has('keydown'), false);
  assert.equal(clock.pending(), 0);
});

test('boot.js wires both halves rather than keeping its own copy', () => {
  // The house pattern from room/tests/entry.test.mjs: execute the module, and check that
  // the caller really imports and calls it, so this file cannot pass over dead code.
  const src = readFileSync(fileURLToPath(new URL('../boot.js', import.meta.url)), 'utf8');

  assert.match(src, /import \{ createExit, installExitHold \} from '\.\/exit\.js'/);
  assert.match(src, /createExit\(\{/);
  assert.match(src, /installExitHold\(\{ target: window, leave \}\)/);
  // ...and that the old hand-rolled copy is gone.
  assert.doesNotMatch(src, /escTimer/);
});
