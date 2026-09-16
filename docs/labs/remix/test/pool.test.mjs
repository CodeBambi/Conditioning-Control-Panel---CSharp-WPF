// The pool: pickSet is pure and deterministic, pins come first, the count
// clamps, a pool that fits keeps its order. The roll op remembers which gifs
// it was rolled on and puts them back when the arrows walk.
import test from 'node:test';
import assert from 'node:assert/strict';
import { pickSet, defaultCount, clampCountFor, sameSet, POOL_CAP } from '../engine/pool.js';
import { createOps } from '../engine/ops.js';
import { codeToSeed } from '../engine/code.js';

const pool = (n) => Array.from({ length: n }, (_, i) => ({ id: 'p' + i }));

test('same seed, same pool, same pins: same picks', () => {
  const a = pickSet({ pool: pool(30), pins: ['p4'], count: 5, seed: codeToSeed('CCP-7K2M') });
  const b = pickSet({ pool: pool(30), pins: ['p4'], count: 5, seed: codeToSeed('CCP-7K2M') });
  assert.deepEqual(a, b);
  assert.equal(a.length, 5);
  assert.equal(a[0], 'p4', 'the pin leads');
  assert.equal(new Set(a).size, 5, 'no repeats');
  const c = pickSet({ pool: pool(30), pins: ['p4'], count: 5, seed: codeToSeed('CCP-7K2N') });
  assert.notDeepEqual(a, c, 'another code draws another hand');
});

test('a pool that fits goes on whole, in pool order, whatever the seed', () => {
  for (const seed of [1, 2, 99]) assert.deepEqual(pickSet({ pool: pool(5), seed }), ['p0', 'p1', 'p2', 'p3', 'p4']);
  assert.deepEqual(pickSet({ pool: pool(8), seed: 3 }).length, 8);
  assert.deepEqual(pickSet({ pool: pool(9), seed: 3 }).length, 8, 'eight at most');
});

test('pins lead in pin order and the draw fills up to the count', () => {
  const set = pickSet({ pool: pool(20), pins: ['p9', 'p2', 'zzz'], count: 4, seed: 7 });
  assert.deepEqual(set.slice(0, 2), ['p9', 'p2']);
  assert.equal(set.length, 4);
  assert.ok(!set.slice(2).includes('p9') && !set.slice(2).includes('p2'), 'a pin is never drawn twice');
});

test('pins alone when the count is theirs; the count never goes under the pins', () => {
  assert.deepEqual(pickSet({ pool: pool(20), pins: ['p3', 'p1'], count: 2, seed: 1 }), ['p3', 'p1']);
  assert.deepEqual(pickSet({ pool: pool(20), pins: ['p3', 'p1', 'p5'], count: 1, seed: 1 }), ['p3', 'p1', 'p5']);
  assert.equal(clampCountFor(1, 20, 3), 3);
  assert.equal(clampCountFor(50, 20, 0), 8);
  assert.equal(clampCountFor(5, 3, 0), 3);
  assert.equal(clampCountFor(0, 3, 0), 1);
  assert.equal(clampCountFor(NaN, 6, 0), 6);
});

test('defaultCount is the pool, eight at most, never under the pins', () => {
  assert.equal(defaultCount(3), 3); assert.equal(defaultCount(40), 8); assert.equal(defaultCount(0), 1);
  assert.equal(defaultCount(40, 8), 8);
});

test('an empty pool picks nothing; ids and objects both work; POOL_CAP is a hundred', () => {
  assert.deepEqual(pickSet({ pool: [], seed: 1 }), []);
  assert.deepEqual(pickSet({ pool: ['a', 'b'], seed: 1 }), ['a', 'b']);
  assert.equal(POOL_CAP, 100);
  assert.ok(sameSet(['a', 'b'], ['a', 'b']) && !sameSet(['a', 'b'], ['b', 'a']) && !sameSet(['a'], ['a', 'b']));
});

// ---- the roll op remembers its set

function ops(n = 3) {
  const tiles = [];
  for (let i = 0; i < n; i++) tiles.push({ id: 't' + i, mediaId: 'm' + i, enterFrame: 0, playMode: 'forward' });
  const s = {
    orientation: 'landscape', frames: 75, fps: 15, seed: 1, tiles, media: tiles.map((t) => ({ id: t.mediaId, w: 480, h: 270 })),
    layout: { mode: 'grow', stageMs: 600, deckHold: 15, tree: null, docked: false, flip: false }, loop: 'snap', blocks: [],
    stampCorner: 'br', captionText: '', shrink: 0,
  };
  const sets = [];
  const setMediaSet = (ids) => {
    sets.push(ids.slice());
    s.tiles = ids.map((id) => s.tiles.find((t) => t.mediaId === id) || { id: 't-' + id, mediaId: id, enterFrame: 0, playMode: 'forward' });
  };
  let frame = 0;
  const o = createOps({ state: s, changed() {}, commit() {}, mediaById: (id) => s.media.find((m) => m.id === id) || null, setMediaSet, getFrame: () => frame, setFrame: (i) => { frame = i; } });
  return { o, s, sets, setMediaSet };
}

test('a roll records the gifs it was rolled on, and the arrows put them back', () => {
  const { o, s, sets, setMediaSet } = ops(3);
  o.roll(codeToSeed('CCP-7K2M'));
  assert.deepEqual(o.rollHistory().entries[0].set, ['m0', 'm1', 'm2']);
  // the pool lane swaps the set, then rolls
  s.media.push({ id: 'm7', w: 480, h: 270 });
  setMediaSet(['m7', 'm1']);
  o.roll(codeToSeed('CCP-7K2N'));
  assert.deepEqual(o.rollHistory().entries[1].set, ['m7', 'm1']);
  assert.deepEqual(o.peekRoll(-1), { seed: codeToSeed('CCP-7K2M'), code: 'CCP-7K2M', set: ['m0', 'm1', 'm2'] });
  assert.equal(o.peekRoll(1), null);
  assert.equal(o.peekRoll(0).code, 'CCP-7K2N');
  sets.length = 0;
  o.rollBack();
  assert.deepEqual(sets, [['m0', 'm1', 'm2']], 'back restores the first set');
  assert.deepEqual(s.tiles.map((t) => t.mediaId), ['m0', 'm1', 'm2']);
  assert.equal(s.tiles[1].id, 't1', 'a tile the clip still had is kept');
  o.rollForward();
  assert.deepEqual(s.tiles.map((t) => t.mediaId), ['m7', 'm1']);
});

test('without a setMediaSet in the ctx the roll still works as before', () => {
  const { o, s } = (() => {
    const r = ops(2); delete r.o.setMediaSet; return r;
  })();
  o.roll(5); o.roll(6); o.rollBack();
  assert.equal(s.tiles.length, 2);
});
