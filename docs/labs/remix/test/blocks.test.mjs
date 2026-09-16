import test from 'node:test';
import assert from 'node:assert/strict';
import {
  EFFECTS, EFFECT_NAMES, defaultParams, createBlock, clampBlock, validateBlock,
  scaleBlocks, rampAt, rampFrames, progressAt, blockLength, blocksAt, isActive,
  defaultBlockFor, TINT_COLOURS,
} from '../engine/blocks.js';

test('the six effects match the panel contract: modes, at most two knobs', () => {
  assert.deepEqual(EFFECT_NAMES, ['drain', 'tint', 'spiral', 'glitch', 'caption', 'focus']);
  for (const name of EFFECT_NAMES) {
    const spec = EFFECTS[name];
    assert.ok(spec.modes.length >= 2 || name === 'drain', name);
    assert.ok(spec.knobs.length >= 1 && spec.knobs.length <= 2, `${name} knob count`);
    for (const k of spec.knobs) assert.ok(k.def >= 0 && k.def <= 100, `${name}.${k.key}`);
  }
  assert.deepEqual(Object.keys(TINT_COLOURS), ['pink', 'lavender', 'gold']);
});

test('defaultParams gives every knob a value and deep copies the extras', () => {
  const a = defaultParams('caption');
  const b = defaultParams('caption');
  a.pos.x = 0.1;
  assert.equal(b.pos.x, 0.5, 'no shared object between blocks');
  assert.equal(a.glow, EFFECTS.caption.knobs[0].def);
  assert.throws(() => defaultParams('nope'), /Unknown effect/);
});

test('createBlock fills in the gaps and rejects nonsense', () => {
  const b = createBlock({ effect: 'drain', target: 'canvas' }, 75);
  assert.equal(b.mode, 'soft');
  assert.equal(b.start, 0);
  assert.equal(b.end, 75);
  assert.ok(b.id);
  assert.equal(b.params.strength, 60);
  assert.equal(createBlock({ effect: 'tint', mode: 'bogus' }, 75).mode, 'wash');
  assert.throws(() => createBlock({ effect: 'nope' }, 75), /Unknown effect/);
});

test('blocks clamp to the canvas and never fall below one frame', () => {
  assert.deepEqual(pick(clampBlock({ start: -5, end: 900 }, 75)), { start: 0, end: 75 });
  assert.deepEqual(pick(clampBlock({ start: 40, end: 40 }, 75)), { start: 40, end: 41 });
  assert.deepEqual(pick(clampBlock({ start: 74, end: 74 }, 75)), { start: 74, end: 75 });
  assert.deepEqual(pick(clampBlock({ start: 200, end: 201 }, 75)), { start: 74, end: 75 });
  assert.deepEqual(pick(clampBlock({ start: 10, end: 5 }, 75)), { start: 10, end: 11 });
  function pick(b) { return { start: b.start, end: b.end }; }
});

test('shortening the canvas scales blocks and keeps them legal', () => {
  const blocks = [
    createBlock({ effect: 'drain', start: 0, end: 75 }, 75),
    createBlock({ effect: 'tint', start: 30, end: 75 }, 75),
    createBlock({ effect: 'caption', mode: 'flash', start: 46, end: 47 }, 75),
  ];
  const scaled = scaleBlocks(blocks, 75, 45);
  assert.deepEqual(scaled.map((b) => [b.start, b.end]), [[0, 45], [18, 45], [28, 29]]);
  for (const b of scaled) {
    assert.equal(validateBlock(b, { frames: 45 }), null);
    assert.ok(b.end - b.start >= 1);
  }
  const grown = scaleBlocks(blocks, 75, 120);
  assert.deepEqual(grown[0], Object.assign({}, grown[0], { start: 0, end: 120 }));
});

test('validateBlock names the problem in plain words', () => {
  const ok = createBlock({ effect: 'focus', target: 't1', start: 0, end: 10 }, 75);
  assert.equal(validateBlock(ok, { frames: 75, tileIds: ['t1'] }), null);
  assert.match(validateBlock({ effect: 'nope' }), /Unknown effect/);
  assert.match(validateBlock({ effect: 'drain', mode: 'zzz' }), /Unknown mode/);
  assert.match(validateBlock({ effect: 'drain', mode: 'soft', start: 0, end: 0 }), /at least one frame/);
  assert.match(validateBlock({ effect: 'drain', mode: 'soft', start: 0, end: 900 }, { frames: 75 }), /past the loop/);
  assert.match(validateBlock(Object.assign({}, ok, { target: 'gone' }), { frames: 75, tileIds: ['t1'] }), /not on the canvas/);
});

test('the ramp eases in and out and never leaves 0..1', () => {
  const b = createBlock({ effect: 'drain', start: 10, end: 40 }, 75);
  assert.equal(rampFrames(b), 3);
  assert.ok(rampAt(b, 10) > 0 && rampAt(b, 10) < 0.5, 'starts low');
  assert.equal(rampAt(b, 25), 1, 'full in the middle');
  assert.ok(rampAt(b, 39) < 0.5, 'ends low');
  assert.equal(rampAt(b, 9), 0);
  assert.equal(rampAt(b, 40), 0);
  let prev = -1;
  for (let f = 10; f <= 25; f++) {
    const v = rampAt(b, f);
    assert.ok(v >= prev, 'monotone in');
    assert.ok(v >= 0 && v <= 1);
    prev = v;
  }
});

test('a one frame block is a flash at full strength', () => {
  const flash = createBlock({ effect: 'caption', mode: 'flash', start: 46, end: 47 }, 75);
  assert.equal(blockLength(flash), 1);
  assert.equal(rampFrames(flash), 0);
  assert.equal(rampAt(flash, 46), 1);
  assert.equal(progressAt(flash, 46), 1);
});

test('short blocks shrink their ramp instead of never reaching full', () => {
  const b = createBlock({ effect: 'drain', start: 0, end: 4 }, 75);
  assert.equal(rampFrames(b), 2);
  assert.ok(rampAt(b, 1) > 0.3);
});

test('progress runs 0 to 1 across the block', () => {
  const b = createBlock({ effect: 'tint', start: 20, end: 30 }, 75);
  assert.equal(progressAt(b, 20), 0);
  assert.equal(progressAt(b, 29), 1);
  assert.ok(Math.abs(progressAt(b, 25) - 5 / 9) < 1e-9);
  assert.equal(progressAt(b, 0), 0);
  assert.equal(progressAt(b, 99), 1);
});

test('blocksAt filters by frame and target in timeline order', () => {
  const list = [
    createBlock({ effect: 'drain', target: 'canvas', start: 20, end: 30 }, 75),
    createBlock({ effect: 'tint', target: 'canvas', start: 0, end: 75 }, 75),
    createBlock({ effect: 'glitch', target: 't1', start: 20, end: 30 }, 75),
  ];
  assert.deepEqual(blocksAt(list, 25, 'canvas').map((b) => b.effect), ['tint', 'drain']);
  assert.deepEqual(blocksAt(list, 25, 't1').map((b) => b.effect), ['glitch']);
  assert.deepEqual(blocksAt(list, 5, 'canvas').map((b) => b.effect), ['tint']);
  assert.equal(isActive(list[0], 30), false);
  assert.equal(isActive(list[0], 29), true);
});

test('default blocks follow the panel rules', () => {
  assert.deepEqual(span(defaultBlockFor('drain', { frames: 75, target: 'canvas' })), [0, 75]);
  assert.deepEqual(span(defaultBlockFor('drain', { frames: 75, target: 't2', enterFrame: 18 })), [18, 75]);
  const flash = defaultBlockFor('caption', { frames: 75, mode: 'flash' });
  assert.deepEqual(span(flash), [47, 48]);
  const flash45 = defaultBlockFor('caption', { frames: 45, mode: 'flash' });
  assert.deepEqual(span(flash45), [28, 29]);
  function span(b) { return [b.start, b.end]; }
});
