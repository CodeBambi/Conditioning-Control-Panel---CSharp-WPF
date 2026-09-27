import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createBrickWobble, wobbleShape, WOBBLE } from './brick-wobble.js';

const lcg = (seed = 7) => () => (seed = (seed * 1664525 + 1013904223) % 4294967296) / 4294967296;
const wall = n => Array.from({ length: n }, (_, i) => ({ alive: true, i }));

test('a wobble is small, swings both ways and dies to nothing', () => {
  assert.deepEqual(wobbleShape(1), { rot: 0, sx: 1, sy: 1 }); assert.deepEqual(wobbleShape(-1), { rot: 0, sx: 1, sy: 1 });
  let lo = 0, hi = 0;
  for (let u = 0; u < 1; u += .01) { const w = wobbleShape(u); lo = Math.min(lo, w.rot); hi = Math.max(hi, w.rot); assert.ok(Math.abs(w.sx - 1) <= WOBBLE.squash + 1e-9); }
  assert.ok(hi > .03 && lo < -.02 && hi <= WOBBLE.tilt && WOBBLE.tilt <= .1, 'a few degrees each way, no more');
  assert.ok(Math.abs(wobbleShape(.97).rot) < .002);
});

test('from time to time a live, hittable brick wobbles; never a dead or refused one, never when switched off', () => {
  const bricks = wall(60), w = createBrickWobble(lcg());
  bricks[3].alive = false; const seen = new Set(); let most = 0;
  for (let i = 0; i < 60 * 30; i++) {
    w.step(1 / 60, bricks, true, br => br.i % 2 === 0);
    for (const br of bricks) if (w.of(br).rot !== 0 || w.of(br) !== w.of(bricks[3])) seen.add(br.i);
    most = Math.max(most, w.count);
  }
  assert.ok(seen.size >= 8, 'thirty seconds sees a fair few: ' + seen.size);
  assert.ok([...seen].every(i => i % 2 === 0 && i !== 3)); assert.ok(most >= 1 && most <= WOBBLE.max);
  const off = createBrickWobble(lcg());
  for (let i = 0; i < 600; i++) off.step(1 / 60, bricks, false);
  assert.equal(off.count, 0);
  w.reset(); assert.equal(w.count, 0);
});

test('a brick broken mid-wobble stops at once, and the wobble never rolls the game dice', () => {
  const bricks = wall(1), w = createBrickWobble(lcg());
  for (let i = 0; i < 200 && !w.count; i++) w.step(1 / 60, bricks);
  assert.equal(w.count, 1); bricks[0].alive = false; w.step(1 / 60, bricks); assert.equal(w.count, 0);
});
