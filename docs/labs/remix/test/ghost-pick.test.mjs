import test from 'node:test';
import assert from 'node:assert/strict';
import { createBlock, validateBlock, EFFECTS, defaultParams, rampAt } from '../engine/blocks.js';
import { ghostSource, render, DICE } from '../engine/effects/glitch.js';
import { rerollBlock } from '../engine/vibes.js';

const RECT = { x: 0, y: 0, w: 480, h: 270 };

function media(id, frames = 4) {
  return { id, w: 100, h: 100, strip: Array.from({ length: frames }, (_, i) => ({ width: 100, height: 100, of: id, i })) };
}

/** A canvas context that remembers which strip's frames were drawn. */
function stubCtx() {
  const drawn = [];
  const c = {
    drawn,
    canvas: { width: 480, height: 270 },
    globalAlpha: 1, globalCompositeOperation: 'source-over', fillStyle: '', filter: 'none',
    save() {}, restore() {}, beginPath() {}, rect() {}, clip() {}, setTransform() {}, clearRect() {},
    drawImage(img) { drawn.push(img); },
    fillRect() {},
  };
  return c;
}

const envAt = (block, frame, strips, under = null) => ({
  fps: 15, frame, frames: 75, size: { w: 480, h: 270 }, rect: RECT,
  neighbours: [], strips, underMedia: under, seed: 4242,
  ramp: rampAt(block, frame),
  buf: (key, w, h) => ({ canvas: { width: w, height: h }, ctx: stubCtx() }),
});

const ghostBlock = (params) => createBlock({
  effect: 'glitch', mode: 'ghost', target: 'canvas', start: 0, end: 60, params,
}, 75);

test('a fresh ghost leaves the choice to the dice', () => {
  assert.equal(defaultParams('glitch').source, DICE);
  assert.equal(EFFECTS.glitch.params.source, 'dice');
  assert.equal(validateBlock(ghostBlock({ source: 'm2' }), { frames: 75 }), null);
});

test('an explicit source is the gif the ghost borrows, on every frame', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const b = ghostBlock({ source: 'm3' });
  // the life curve is zero at both ends, so these are frames the ghost is actually up for
  for (const f of [5, 17, 30, 44, 52]) {
    const env = envAt(b, f, strips);
    assert.equal(ghostSource(b, env).id, 'm3', `frame ${f}`);
    const c = stubCtx();
    render(c, null, b, f / 59, env);
    assert.ok(c.drawn.length, `frame ${f} drew something`);
    for (const img of c.drawn) assert.equal(img.of, 'm3', `frame ${f} drew m3`);
  }
});

test('an explicit source beats the dice, whatever the dice rolled', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const env = envAt(ghostBlock(), 30, strips);
  for (const pick of [0, 1, 2, 7, 96]) {
    assert.equal(ghostSource(ghostBlock({ source: 'm1', pick }), env).id, 'm1', `pick ${pick}`);
  }
});

test('a reroll moves the dice but never an explicit source', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const env = envAt(ghostBlock(), 30, strips);
  const pinned = ghostBlock({ source: 'm2', pick: 0 });
  const patch = rerollBlock(pinned, 9001, { frames: 75 });
  assert.equal(patch.params.source, 'm2', 'the reroll keeps the choice');
  const after = Object.assign({}, pinned, patch, { mode: 'ghost' });
  assert.equal(ghostSource(after, env).id, 'm2');
});

test('a source that has left the strip falls back to the dice', () => {
  const strips = [media('m1'), media('m2')];
  const gone = ghostSource(ghostBlock({ source: 'm9', pick: 1 }), envAt(ghostBlock(), 30, strips));
  assert.ok(gone, 'still a ghost');
  assert.equal(gone.id, 'm2', 'the same gif the dice would have taken');
  // a media that is still listed but carries no frames is no use either
  const empty = [{ id: 'm9', w: 10, h: 10, strip: [] }, media('m2')];
  assert.equal(ghostSource(ghostBlock({ source: 'm9', pick: 1 }), envAt(ghostBlock(), 30, empty)).id, 'm2');
});

test('an explicit source may be the gif under the block, which the dice never picks', () => {
  const strips = [media('m1'), media('m2')];
  const env = envAt(ghostBlock(), 30, strips, strips[0]);
  assert.notEqual(ghostSource(ghostBlock(), env).id, 'm1', 'the dice skips what is underneath');
  assert.equal(ghostSource(ghostBlock({ source: 'm1' }), env).id, 'm1', 'a hand on it does not');
});

test('nothing to borrow is still nothing to borrow', () => {
  const only = media('m1');
  assert.equal(ghostSource(ghostBlock({ source: 'm1' }), envAt(ghostBlock(), 30, [only], only)).id, 'm1');
  assert.equal(ghostSource(ghostBlock({ source: 'gone' }), envAt(ghostBlock(), 30, [only], only)), null);
  assert.equal(ghostSource(ghostBlock(), envAt(ghostBlock(), 30, [], null)), null);
});

test('a project saved before the picker renders exactly as it did', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const env = envAt(ghostBlock(), 30, strips, strips[0]);
  for (const pick of [null, 0, 1, 2, 41]) {
    const old = { effect: 'glitch', mode: 'ghost', target: 't1', start: 0, end: 60, params: { strength: 50, pick } };
    const now = { ...old, params: { ...old.params, source: DICE } };
    assert.equal(ghostSource(old, env)?.id, ghostSource(now, env)?.id, `pick ${pick}`);
  }
});

test('the source rides in the block JSON, both ways', () => {
  const b = ghostBlock({ source: 'm2' });
  const back = JSON.parse(JSON.stringify(b));
  assert.equal(back.params.source, 'm2');
  assert.equal(validateBlock(back, { frames: 75 }), null);
  const strips = [media('m1'), media('m2')];
  assert.equal(ghostSource(back, envAt(b, 20, strips)).id, 'm2');
});
