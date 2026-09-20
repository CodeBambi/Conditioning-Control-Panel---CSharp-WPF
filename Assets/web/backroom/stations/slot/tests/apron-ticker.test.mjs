import test from 'node:test';
import assert from 'node:assert/strict';
import { APRON, createApronTicker } from '../apron-ticker.js';
function rig(reduced = false) {
  const texts = [], scales = [];
  const ctx = { fillRect() {}, save() {}, restore() {}, beginPath() {}, rect() {}, clip() {},
    scale: (...args) => scales.push(args), measureText: text => ({ width: text.length * 27 }), fillText: (...args) => texts.push(args) };
  const canvas = { getContext: () => ctx };
  let changes = 0;
  const ticker = createApronTicker({ canvas, reduced, onDirty: () => changes++ });
  return { ticker, canvas, texts, scales, changes: () => changes };
}
test('apron canvas stays bounded and uploads at most 20 times a second', () => {
  const r = rig();
  for (let t = 0; t < 1000; t++) r.ticker.update(t);
  assert.equal(r.changes(), 20);
  assert.equal(r.canvas.width, APRON.width); assert.equal(r.canvas.height, APRON.height);
  assert.ok(r.texts.at(-1)[1] < r.texts[0][1], 'text travels left');
});
test('station messages replace the previous line and expiry returns to ambient', () => {
  const r = rig(); r.ticker.message('  WIN +3\nSP  '); r.ticker.update(0);
  assert.equal(r.ticker.debug().message, 'WIN +3 SP');
  assert.ok(r.texts[0][0].startsWith('WIN +3 SP'));
  r.ticker.message('Spinning'); r.ticker.update(1);
  assert.ok(r.texts.at(-1)[0].startsWith('Spinning'));
  for (let t = 101; t <= 14101; t += 100) r.ticker.update(t);
  assert.equal(r.ticker.debug().message, '');
});
test('Motion Off holds a centered status without repainting or scrolling', () => {
  const r = rig(true); r.ticker.message('Ready'); r.ticker.update(0);
  for (let t = 100; t < 20000; t += 100) r.ticker.update(t);
  assert.equal(r.changes(), 1); assert.equal(r.ticker.debug().elapsed, 0);
  assert.equal(r.texts[0][1] * r.scales[0][0], APRON.width / 2); assert.equal(r.texts[0][0], 'Ready');
  r.ticker.message('Spinning'); r.ticker.update(20000);
  assert.equal(r.changes(), 2); assert.equal(r.texts.at(-1)[0], 'Spinning');
});
test('background pauses do not jump the display and disposal stops uploads', () => {
  const r = rig(); r.ticker.update(0); r.ticker.update(100000);
  assert.equal(r.ticker.debug().elapsed, 100);
  r.ticker.dispose(); const n = r.changes();
  r.ticker.message('ignored'); r.ticker.update(100100);
  assert.equal(r.changes(), n); assert.equal(r.ticker.debug().message, '');
});
test('responsive mesh aspect compensates glyph shape without resizing the texture', () => {
  const r = rig(true);
  for (const aspect of [23.5, 23.5 / 3, 23.5 * 1.8]) {
    r.ticker.setAspect(aspect); r.ticker.update(r.changes() * 100);
    const scale = r.scales.at(-1)[0];
    assert.ok(Math.abs(scale * aspect / (APRON.width / APRON.height) - 1) < 1e-10);
    assert.ok(Math.abs(r.texts.at(-1)[1] * scale - APRON.width / 2) < 1e-8);
    assert.equal(r.canvas.width, APRON.width); assert.equal(r.canvas.height, APRON.height);
  }
  const n = r.changes(); r.ticker.setAspect(23.5 * 1.8); r.ticker.update(500);
  assert.equal(r.changes(), n, 'unchanged aspect must not force another texture upload');
  for (const bad of [NaN, Infinity, -1, 0]) r.ticker.setAspect(bad);
  r.ticker.update(600); assert.equal(r.changes(), n);
});
