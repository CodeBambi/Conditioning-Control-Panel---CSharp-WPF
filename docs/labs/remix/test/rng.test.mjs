import test from 'node:test';
import assert from 'node:assert/strict';
import { makeRng, mixSeed, rngFrom, hash01, noise1, fbm1 } from '../engine/rng.js';

test('mulberry32 is deterministic and stays in range', () => {
  const a = makeRng(12345);
  const b = makeRng(12345);
  for (let i = 0; i < 500; i++) {
    const v = a();
    assert.equal(v, b());
    assert.ok(v >= 0 && v < 1, 'value in [0,1)');
  }
});

test('different seeds give different streams', () => {
  const a = makeRng(1), b = makeRng(2);
  let same = 0;
  for (let i = 0; i < 100; i++) if (a() === b()) same++;
  assert.equal(same, 0);
});

test('mixSeed fans one seed into independent labelled streams', () => {
  assert.notEqual(mixSeed(7, 'spiral'), mixSeed(7, 'glitch'));
  assert.equal(mixSeed(7, 'spiral'), mixSeed(7, 'spiral'));
  assert.ok(mixSeed(0, '') >= 0);
});

test('rngFrom helpers respect their bounds', () => {
  const r = rngFrom(99, 'test');
  for (let i = 0; i < 200; i++) {
    const f = r.range(2, 5);
    assert.ok(f >= 2 && f < 5);
    const n = r.int(3, 7);
    assert.ok(Number.isInteger(n) && n >= 3 && n <= 7);
    assert.ok(['a', 'b', 'c'].includes(r.pick(['a', 'b', 'c'])));
  }
});

test('shuffle keeps every element and does not touch the input', () => {
  const src = [0, 1, 2, 3, 4, 5, 6, 7];
  const out = rngFrom(5, 'x').shuffle(src);
  assert.deepEqual(src, [0, 1, 2, 3, 4, 5, 6, 7]);
  assert.deepEqual(out.slice().sort((a, b) => a - b), src);
});

test('shuffle is seed stable', () => {
  const a = rngFrom(42, 'shuffle:1').shuffle([1, 2, 3, 4, 5]);
  const b = rngFrom(42, 'shuffle:1').shuffle([1, 2, 3, 4, 5]);
  assert.deepEqual(a, b);
});

test('sample returns k distinct ascending indices', () => {
  const s = rngFrom(3, 's').sample(10, 4);
  assert.equal(s.length, 4);
  assert.deepEqual(s, s.slice().sort((a, b) => a - b));
  assert.equal(new Set(s).size, 4);
  assert.equal(rngFrom(3, 's').sample(2, 9).length, 2);
});

test('hash01 and the noise helpers stay in range and repeat', () => {
  for (let i = 0; i < 300; i++) {
    const v = hash01(11, i, i * 3);
    assert.ok(v >= 0 && v < 1);
    assert.equal(v, hash01(11, i, i * 3));
  }
  for (let x = 0; x < 40; x += 0.37) {
    assert.ok(noise1(4, x) >= 0 && noise1(4, x) <= 1);
    assert.ok(fbm1(4, x) >= 0 && fbm1(4, x) <= 1);
  }
});
