import test from 'node:test';
import assert from 'node:assert/strict';
import { COMBOS, PAY_MS, CELL_W, CELL_H, comboSize, paintCombo } from '../paytable.js';
import { CELL } from '../symbols.js';

/** A recording 2D context: every call in order, so the ORDER of card, picture and frame can be asserted. */
function recorder() {
  const calls = [];
  const put = (name) => (...a) => { calls.push({ name, a }); };
  const grad = () => ({ stops: [], addColorStop(o, c) { this.stops.push([o, c]); } });
  return {
    calls, canvas: null,
    globalAlpha: 1, fillStyle: null, strokeStyle: null, lineWidth: 1, font: '', textAlign: '', textBaseline: '',
    save: put('save'), restore: put('restore'), clip: put('clip'), fill: put('fill'), stroke: put('stroke'),
    beginPath: put('beginPath'), closePath: put('closePath'), roundRect: put('roundRect'), rect: put('rect'),
    arc: put('arc'), moveTo: put('moveTo'), lineTo: put('lineTo'), fillRect: put('fillRect'), strokeRect: put('strokeRect'),
    clearRect: put('clearRect'), drawImage: put('drawImage'), fillText: put('fillText'), setLineDash: put('setLineDash'),
    setTransform: put('setTransform'), translate: put('translate'), scale: put('scale'), rotate: put('rotate'),
    measureText: (s) => ({ width: s.length * 7 }),
    createLinearGradient: (...a) => { const g = grad(); calls.push({ name: 'linear', a, g }); return g; },
    createRadialGradient: (...a) => { const g = grad(); calls.push({ name: 'radial', a, g }); return g; },
  };
}
const canvasWith = (ctx, w = 200, h = 40) => { const c = { width: w, height: h, getContext: () => ctx }; ctx.canvas = c; return c; };
const fillsOf = (ctx) => ctx.calls.filter(c => c.name === 'fillStyleSet');

test('the legend rows ARE the contract table, in its order and three cells wide', () => {
  // CONTRACT.md section 4, plus emi2 (10.16's re-spin row), which the published table carries.
  const want = ['emi3', 'emi2', 'gif3same', 'sub3', 'spiral3', 'gif3', 'sub2', 'spiral2', 'melt'];
  assert.deepEqual(Object.keys(COMBOS), want);
  for (const [id, ids] of Object.entries(COMBOS)) assert.equal(ids.length, 3, `${id} is a three cell row`);
});

test('the rows say what they mean: same gif three times, a mix of three, a pair and a wildcard', () => {
  assert.deepEqual(COMBOS.gif3same, ['gif1', 'gif1', 'gif1'], '3 of the SAME gif is the same id three times');
  assert.equal(new Set(COMBOS.gif3).size, 3, '3 GIFs any mix is three different ids');
  assert.equal(COMBOS.sub2.filter(i => i === 'any').length, 1, 'exactly two triggers is two and a wildcard');
  assert.equal(COMBOS.spiral2.filter(i => i === 'any').length, 1);
  assert.equal(COMBOS.emi2[2], 'any', 'two EMI pays whatever the third reel does');
});

test('the legend cell is the REEL cell shrunk, not a second aspect ratio', () => {
  // If these drift apart the legend stops being a picture of the glass, which is the whole point of it.
  assert.equal(CELL_H, Math.round(CELL_W * CELL.hh / CELL.hw));
  assert.ok(Math.abs(CELL_W / CELL_H - CELL.hw / CELL.hh) < 0.05, 'same aspect as the reel cell');
});

test('the canvas a row needs counts the cells, the gaps and the padding', () => {
  const three = comboSize(3), one = comboSize(1);
  assert.equal(three.w - one.w, 2 * CELL_W + 2 * 5, 'two more cells and two gaps');
  assert.equal(three.h, one.h, 'a row is one cell tall whatever it holds');
});

test('a flash cell is a POLAROID: a card laid down first, then the picture inside it', () => {
  const ctx = recorder();
  paintCombo(canvasWith(ctx), ['gif0'], 0, {});
  const order = ctx.calls.map(c => c.name);
  const rotate = order.indexOf('rotate'), card = order.indexOf('fill'), clip = order.indexOf('clip');
  assert.ok(rotate >= 0, 'the photo is tilted');
  assert.ok(rotate < card, 'the tilt is applied before the card is laid');
  assert.ok(card < clip, 'the card is under the picture window, not over it');
  assert.ok(order.indexOf('stroke') > card, 'and it takes an edge');
});

test('a spiral or a trigger cell is NOT a polaroid: no card, no tilt', () => {
  for (const id of ['spiral0', 'sub0', 'emi', 'melt']) {
    const ctx = recorder();
    paintCombo(canvasWith(ctx), [id], 0, {});
    assert.equal(ctx.calls.filter(c => c.name === 'rotate').length, 0, `${id} is not tilted`);
  }
});

test('the wildcard is dashed and EMPTY: a symbol there would read as part of the combination', () => {
  const ctx = recorder();
  paintCombo(canvasWith(ctx), ['any'], 0, {});
  assert.equal(ctx.calls.filter(c => c.name === 'setLineDash').length, 1);
  assert.equal(ctx.calls.filter(c => c.name === 'drawImage').length, 0, 'nothing is drawn in it');
  const text = ctx.calls.find(c => c.name === 'fillText');
  assert.equal(text.a[0], '?', 'it says so in text as well (Brake 9)');
});

test('the tilt is a constant per COLUMN: a legend that shuffles when you reopen it is a toy', () => {
  const a = recorder(), b = recorder();
  paintCombo(canvasWith(a), COMBOS.gif3, 0, {});
  paintCombo(canvasWith(b), COMBOS.gif3, 9999, {});
  const tilts = (c) => c.calls.filter(x => x.name === 'rotate').map(x => x.a[0]);
  assert.equal(tilts(a).length, 3);
  assert.deepEqual(tilts(a), tilts(b), 'the same row wears the same tilts at any time');
  assert.equal(new Set(tilts(a)).size, 3, 'and the three columns differ, so it reads as a stack of photos');
});

test('the device ratio is applied once, as a transform, and the row is cleared first', () => {
  const ctx = recorder();
  paintCombo(canvasWith(ctx), COMBOS.sub3, 0, {}, 2);
  const t = ctx.calls.find(c => c.name === 'setTransform');
  assert.deepEqual(t.a, [2, 0, 0, 2, 0, 0]);
  const clear = ctx.calls.findIndex(c => c.name === 'clearRect');
  assert.ok(clear >= 0 && clear < ctx.calls.findIndex(c => c.name === 'save'), 'cleared before anything is laid');
  assert.deepEqual(ctx.calls[clear].a, [0, 0, comboSize(3).w, comboSize(3).h], 'in CSS px, not device px');
});

test('every cell is painted inside its own save/restore, so nothing leaks along the row', () => {
  const ctx = recorder();
  paintCombo(canvasWith(ctx), COMBOS.spiral3, 0, {});
  const saves = ctx.calls.filter(c => c.name === 'save').length;
  assert.equal(saves, ctx.calls.filter(c => c.name === 'restore').length, 'balanced');
  assert.equal(ctx.globalAlpha, 1, 'the alpha is put back');
});

test('nothing to paint on is false, and the caller keeps whatever the legend already showed', () => {
  assert.equal(paintCombo(null, COMBOS.emi3, 0, {}), false);
  assert.equal(paintCombo(canvasWith(recorder()), [], 0, {}), false);
  assert.equal(paintCombo(canvasWith(recorder()), null, 0, {}), false);
});

test('the repaint clock is the decoder clock: 12 Hz, not the frame rate', () => {
  assert.equal(PAY_MS, 83, 'room/gif-decode.js MAX_FPS is 12, so 83 ms is one frame of the source');
});
