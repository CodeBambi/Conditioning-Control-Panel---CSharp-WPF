/* node --test well.test.js - the spiral well on the sim side: it carries a dealt picture index and it
   still captures, orbits and releases the ball. Nothing visual (the whirlwind itself is render-well.js). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGame } from './game.js';

const MEDIA_COUNT = 8;                                   // createMedia caps residents at 8; the sim deals indices into that
const seeded = (seed = 7) => () => { seed = (seed * 16807) % 2147483647; return (seed - 1) / 2147483646; };
function make(opts = {}) {
  const events = [];
  const audio = new Proxy({ beat: { spb: 60 / 96 } }, { get: (t, k) => k in t ? t[k] : () => {} });
  const game = createGame({ rng: seeded(), audio, onEvent: (n, d) => events.push([n, d]), ...opts });
  return { game, events, names: () => events.map(e => e[0]).filter(n => n !== 'hit') };
}

test('a well spawns with a picture index inside the dealt count', () => {
  const { game } = make({ saturation: 0.8 });
  for (let i = 0; i < 40; i++) {
    const w = game.spawnWellNow();
    assert.equal(Number.isInteger(w.gif), true, 'gif is an int, the renderer looks it up in the deal');
    assert.ok(w.gif >= 0 && w.gif < MEDIA_COUNT, `gif ${w.gif} out of range`);
    assert.equal(w.r, 70); assert.equal(w.pull, 110); assert.equal(w.captured, null);
  }
});

test('a well spawns on its own once the spirals rung is up', () => {
  const { game } = make({ saturation: 0.8 });
  game.launchNow();
  for (let i = 0; i < 60 * 8 && !game.snapshot().well; i++) game.step(1 / 60, {});
  const w = game.snapshot().well;
  assert.ok(w, 'a well should appear within the first 8 seconds at saturation 0.8');
  assert.ok(w.gif >= 0 && w.gif < MEDIA_COUNT);
});

test('the well still captures the ball, orbits it and pays out on release', () => {
  const { game, names } = make({ saturation: 0.8 });
  const w = game.spawnWellNow();
  game.launchNow();
  const s = game.snapshot(), b = s.balls[0];
  b.x = w.x; b.y = w.y + 60; b.vx = 0; b.vy = -400; b.stuck = false;
  for (let i = 0; i < 60 && !b.orbit; i++) game.step(1 / 60, {});
  assert.ok(b.orbit, 'the ball entering the pull radius is captured');
  assert.equal(w.captured, b);
  assert.ok(names().includes('capture'));
  const satAtCapture = s.sat;
  for (let i = 0; i < 60 * 6 && !names().includes('spiral'); i++) game.step(1 / 60, {});
  assert.ok(names().includes('spiral'), 'the orbit completes and the well releases the ball');
  assert.equal(b.orbit, null);
  assert.ok(Math.abs(s.sat - Math.min(1, satAtCapture + 0.05)) < 1e-9, 'a completed orbit is worth 0.05 saturation');
});

test('the swirl spins faster while it holds a ball', () => {
  const { game } = make({ saturation: 0.8 });
  const w = game.spawnWellNow();
  game.launchNow();
  const s = game.snapshot(), b = s.balls[0];
  b.x = w.x - 300; b.y = 60; b.vx = 0; b.vy = 0; b.stuck = false;    // parked far away, no capture
  const before = w.rot;
  game.step(0.1, {});
  const idle = w.rot - before;
  w.captured = b;
  const at = w.rot;
  game.step(0.1, {});
  assert.ok(w.rot - at > idle * 2, 'a captured ball winds the picture up');
});
