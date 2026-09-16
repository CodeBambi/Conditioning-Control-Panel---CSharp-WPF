import test from 'node:test';
import assert from 'node:assert/strict';
import { ALPHABET, SEED_MAX, seedToCode, codeToSeed, normalizeCode, isCode, randomSeed, randomCode } from '../engine/code.js';
import { makeRng } from '../engine/rng.js';

test('alphabet has no look-alike glyphs', () => {
  assert.equal(ALPHABET.length, 32);
  for (const bad of ['0', '1', 'O', 'I']) assert.ok(!ALPHABET.includes(bad), bad);
  assert.equal(new Set(ALPHABET).size, 32);
});

test('code round trips over the whole seed range', () => {
  const rand = makeRng(7);
  const probes = [0, 1, 31, 32, 1023, 1024, SEED_MAX - 1];
  for (let i = 0; i < 2000; i++) probes.push(Math.floor(rand() * SEED_MAX));
  for (const seed of probes) {
    const code = seedToCode(seed);
    assert.match(code, /^CCP-[23456789A-HJ-NP-Z]{4}$/, code);
    assert.equal(codeToSeed(code), seed, code);
  }
});

test('seeds are unique per code across an exhaustive slice', () => {
  const seen = new Set();
  for (let s = 0; s < 5000; s++) seen.add(seedToCode(s));
  assert.equal(seen.size, 5000);
});

test('input is forgiving about case, spacing and the dropped glyphs', () => {
  const seed = codeToSeed('CCP-QJ34');
  assert.equal(codeToSeed('ccp-qj34'), seed);
  assert.equal(codeToSeed('QJ34'), seed);
  assert.equal(codeToSeed(' ccp qj 34 '), seed);
  assert.equal(codeToSeed('CCP-0134'), seed, 'O and 1 fold onto Q and J');
});

test('bad codes are rejected with a readable message', () => {
  for (const bad of ['', 'CCP-', 'CCP-ABCDE', 'AB', null, undefined, 'CCP-@@@@']) {
    assert.equal(isCode(bad), false, String(bad));
    assert.throws(() => codeToSeed(bad), /not a remix code/);
  }
});

test('normalizeCode strips the prefix', () => {
  assert.equal(normalizeCode('CCP-23AB'), '23AB');
  assert.equal(normalizeCode('23ab'), '23AB');
});

test('random helpers stay inside the range', () => {
  const rand = makeRng(2);
  for (let i = 0; i < 500; i++) {
    const s = randomSeed(rand);
    assert.ok(s >= 0 && s < SEED_MAX && Number.isInteger(s));
  }
  assert.ok(isCode(randomCode(makeRng(3))));
});
