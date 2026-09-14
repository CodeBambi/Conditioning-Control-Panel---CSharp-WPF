import test from 'node:test';
import assert from 'node:assert/strict';
import { createBlock, validateBlock, EFFECTS, rampAt } from '../engine/blocks.js';
import { AUTO_WEIGHTS } from '../engine/auto.js';
import { ghostAlpha, lifeAt, peakAlpha, ghostSource, render } from '../engine/effects/glitch.js';

const RECT = { x: 0, y: 0, w: 480, h: 270 };

function media(id, frames = 4) {
  return { id, w: 100, h: 100, strip: Array.from({ length: frames }, (_, i) => ({ width: 100, height: 100, i })) };
}

/** A canvas context that records what was drawn and with how much alpha. */
function stubCtx() {
  const calls = [];
  const c = {
    calls,
    canvas: { width: 480, height: 270 },
    globalAlpha: 1,
    globalCompositeOperation: 'source-over',
    fillStyle: '',
    filter: 'none',
    save() {}, restore() {}, beginPath() {}, rect() {}, clip() {},
    setTransform() {}, clearRect() {},
    drawImage() { calls.push({ op: 'drawImage', alpha: c.globalAlpha, comp: c.globalCompositeOperation }); },
    fillRect() { calls.push({ op: 'fillRect', alpha: c.globalAlpha, comp: c.globalCompositeOperation }); },
  };
  return c;
}

function envAt(block, frame, strips, under = null) {
  return {
    fps: 15, frame, frames: 75, size: { w: 480, h: 270 }, rect: RECT,
    neighbours: [], strips, underMedia: under, seed: 4242,
    ramp: rampAt(block, frame),
    buf: (key, w, h) => ({ canvas: { width: w, height: h }, ctx: stubCtx() }),
  };
}

test('glitch is two chips and one knob, and tear is not one of them', () => {
  assert.deepEqual(EFFECTS.glitch.modes, ['ghost', 'double']);
  assert.deepEqual(EFFECTS.glitch.legacyModes, ['tear']);
  assert.equal(EFFECTS.glitch.knobs.length, 1);
  assert.equal(EFFECTS.glitch.knobs[0].key, 'strength');
  assert.ok(!('tear' in AUTO_WEIGHTS.glitchMode), 'the dice never rolls a tear');
});

test('a saved tear still loads and still passes as a block', () => {
  const b = createBlock({ effect: 'glitch', mode: 'tear', target: 'canvas', start: 0, end: 40 }, 75);
  assert.equal(b.mode, 'tear', 'kept, not swapped for ghost');
  assert.equal(validateBlock(b, { frames: 75 }), null);
  assert.equal(createBlock({ effect: 'glitch', mode: 'bogus' }, 75).mode, 'ghost');
});

test('strength maps to a peak opacity around a third at the default', () => {
  assert.ok(Math.abs(peakAlpha({ strength: 50 }) - 0.3) < 0.05, peakAlpha({ strength: 50 }));
  assert.ok(peakAlpha({ strength: 0 }) >= 0.11 && peakAlpha({ strength: 0 }) <= 0.13);
  assert.ok(peakAlpha({ strength: 100 }) >= 0.58 && peakAlpha({ strength: 100 }) <= 0.61);
});

test('the life curve rises over the first 8 percent and leaves over the last 14', () => {
  assert.equal(lifeAt(0), 0);
  assert.ok(lifeAt(0.04) > 0 && lifeAt(0.04) < 1);
  assert.equal(lifeAt(0.5), 1);
  assert.ok(lifeAt(0.95) > 0 && lifeAt(0.95) < 1);
  assert.equal(lifeAt(1), 0);
});

test('a ghost block carries alpha in the middle of its life and none outside it', () => {
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 'canvas', start: 20, end: 60 }, 75);
  assert.equal(ghostAlpha(b, 19), 0, 'before the block');
  assert.equal(ghostAlpha(b, 60), 0, 'end is exclusive');
  assert.equal(ghostAlpha(b, 74), 0, 'after the block');
  const mid = ghostAlpha(b, 40);
  assert.ok(mid > 0.2 && mid < 0.4, `mid life alpha ${mid}`);
});

test('a one frame ghost still shows: no life shaping to fade it out', () => {
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 'canvas', start: 30, end: 31 }, 75);
  assert.ok(ghostAlpha(b, 30) > 0.2);
  assert.equal(ghostAlpha(b, 31), 0);
});

test('the ghost borrows another loop, never the one underneath', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 't1' }, 75);
  const picked = ghostSource(b, envAt(b, 30, strips, strips[0]));
  assert.ok(picked, 'something to borrow');
  assert.notEqual(picked.id, 'm1');
});

test('a strip of one falls back to double: nothing to borrow', () => {
  const only = media('m1');
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 't1' }, 75);
  assert.equal(ghostSource(b, envAt(b, 30, [only], only)), null);
});

test('the dice writes pick, and pick moves the ghost to another loop', () => {
  const strips = [media('m1'), media('m2'), media('m3')];
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 'canvas' }, 75);
  const one = ghostSource(Object.assign({}, b, { params: { strength: 50, pick: 0 } }), envAt(b, 30, strips));
  const two = ghostSource(Object.assign({}, b, { params: { strength: 50, pick: 1 } }), envAt(b, 30, strips));
  assert.notEqual(one.id, two.id);
});

test('ghost draws over the target rect mid life and draws nothing at the edge', () => {
  const strips = [media('m1'), media('m2')];
  const b = createBlock({ effect: 'glitch', mode: 'ghost', target: 't1', start: 10, end: 50 }, 75);

  const mid = stubCtx();
  render(mid, null, b, 0.5, envAt(b, 30, strips, strips[0]));
  const drawn = mid.calls.filter((c) => c.op === 'drawImage');
  assert.equal(drawn.length, 2, 'one pass plus the soft-light pass');
  assert.ok(drawn[0].alpha > 0.2, `alpha ${drawn[0].alpha}`);
  assert.equal(drawn[1].comp, 'soft-light');
  assert.ok(mid.calls.some((c) => c.op === 'fillRect' && c.comp === 'overlay'), 'the pink wash');

  const off = stubCtx();
  render(off, null, b, 1, envAt(b, 50, strips, strips[0]));
  assert.equal(off.calls.length, 0, 'nothing outside the block');
});

test('double blows up what is underneath and needs no strip', () => {
  const b = createBlock({ effect: 'glitch', mode: 'double', target: 'canvas', start: 0, end: 40 }, 75);
  const c = stubCtx();
  render(c, { width: 480, height: 270 }, b, 0.5, envAt(b, 20, []));
  assert.equal(c.calls.filter((x) => x.op === 'drawImage').length, 2);
});

test('tear still tears: bands and scanlines on the frames it picks', () => {
  const b = createBlock({ effect: 'glitch', mode: 'tear', target: 'canvas', start: 0, end: 75, params: { strength: 90 } }, 75);
  let drew = 0;
  for (let f = 5; f < 70; f++) {
    const c = stubCtx();
    render(c, { width: 480, height: 270 }, b, f / 74, envAt(b, f, []));
    if (c.calls.length) drew++;
  }
  assert.ok(drew > 5, `tear fired on ${drew} frames`);
  assert.ok(drew < 65, 'and stayed sparse');
});

test('tear needs the frame underneath and bails without it', () => {
  const b = createBlock({ effect: 'glitch', mode: 'tear', target: 'canvas', start: 0, end: 75 }, 75);
  const c = stubCtx();
  render(c, null, b, 0.5, envAt(b, 37, []));
  assert.equal(c.calls.length, 0);
});
