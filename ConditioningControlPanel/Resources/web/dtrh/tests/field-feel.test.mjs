// Pure maths of the field feel pass (game/fieldFeel.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  BREATH, SHARD_CAP, FLIGHT, DIM,
  breathPhase, shardCount, flightCount, splitAmount, flightPoint, dimLevels,
} from '../game/fieldFeel.js';

test('breath: every bubble lands inside the period range, somewhere inside its own cycle', () => {
  for (const [a, b] of [[0, 0], [0.5, 0.5], [0.999, 0.999], [2, -1]]) {
    const p = breathPhase(a, b);
    assert.ok(p.durS >= BREATH.minS && p.durS <= BREATH.maxS);
    assert.ok(p.delayS <= 0 && p.delayS >= -p.durS);
  }
  assert.notEqual(breathPhase(0.1, 0.2).delayS, breathPhase(0.1, 0.7).delayS);
});

test('shards: full burst with room, the remainder near the cap, nothing past it', () => {
  assert.equal(shardCount(9, 0, SHARD_CAP), 9);
  assert.equal(shardCount(13, SHARD_CAP - 5, SHARD_CAP), 5);
  assert.equal(shardCount(9, SHARD_CAP, SHARD_CAP), 0);
  assert.equal(shardCount(9, SHARD_CAP + 40, SHARD_CAP), 0);
  let live = 0;
  for (let i = 0; i < 100; i++) live += shardCount(13, live, SHARD_CAP);
  assert.equal(live, SHARD_CAP);
});

test('flights: up to three at full, one at reduced, none at off or when the air is full', () => {
  assert.equal(flightCount(10, 1, 0), FLIGHT.maxPerPop);
  assert.equal(flightCount(2, 1, 0), 2);
  assert.equal(flightCount(10, 0.5, 0), 1);
  assert.equal(flightCount(10, 0, 0), 0);
  assert.equal(flightCount(0, 1, 0), 0);
  assert.equal(flightCount(10, 1, FLIGHT.cap), 0);
  assert.equal(flightCount(10, 1, FLIGHT.cap - 1), 1);
});

test('split: the parts always sum to the whole', () => {
  for (const [amt, n] of [[10, 3], [1, 1], [2, 2], [7, 3], [100, 1]]) {
    const parts = splitAmount(amt, n);
    assert.equal(parts.length, n);
    assert.equal(parts.reduce((a, b) => a + b, 0), amt);
  }
  assert.deepEqual(splitAmount(5, 0), []);
});

test('flight path: starts at the pop, ends in the slot, the bow is zero at both ends', () => {
  const a = flightPoint(100, 400, 20, 30, 0, 46);
  const z = flightPoint(100, 400, 20, 30, 1, 46);
  assert.ok(Math.abs(a.x - 100) < 1e-9 && Math.abs(a.y - 400) < 1e-9);
  assert.ok(Math.abs(z.x - 20) < 1e-9 && Math.abs(z.y - 30) < 1e-9);
  const mid = flightPoint(0, 0, 100, 0, 0.5, 46);
  assert.ok(Math.abs(mid.x - 50) < 1e-9);
  assert.ok(Math.abs(Math.abs(mid.y) - 46) < 1e-9);
  const straight = flightPoint(0, 0, 100, 0, 0.5, 0);
  assert.equal(straight.y, 0);
});

test('dim: full depth at full motion, half the drop otherwise, never brighter than rest', () => {
  const full = dimLevels(1), half = dimLevels(0.5), off = dimLevels(0);
  assert.equal(full.sat, DIM.sat);
  assert.equal(full.bright, DIM.bright);
  assert.ok(half.sat > full.sat && half.sat < 1);
  assert.ok(half.bright > full.bright && half.bright < 1);
  assert.deepEqual(off, half);
  assert.equal(full.ms, DIM.ms);
});
