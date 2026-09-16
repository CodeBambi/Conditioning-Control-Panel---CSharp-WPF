import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tableLayout, slotXY, createTable } from '../table.js';

test('the table fits a 1280 x 720 station view: dealer above the print, player below, spot clear of the controls', () => {
  const L = tableLayout(1280, 720);
  assert.ok(L.cw <= 92 && Math.abs(L.ch - L.cw * 1.4) < 1e-9);
  assert.ok(L.dealerY + L.ch / 2 < 720 * 0.49, 'dealer cards end above the printed arc');
  assert.ok(L.playerY - L.ch / 2 > 720 * 0.52, 'player cards start below it');
  assert.ok(L.spot.y + L.spot.r * 1.6 < 720 - 18 && L.shoe.x + L.cw * 0.75 < 1280);
});

test('slots centre each row; split hands sit side by side and never overlap', () => {
  const L = tableLayout(1280, 720);
  assert.deepEqual(slotXY(L, 'd', 0, 1), [640, L.dealerY]);
  const [a] = slotXY(L, 0, 0, 2), [b] = slotXY(L, 0, 1, 2);
  assert.ok(Math.abs((a + b) / 2 - 640) < 1e-9 && b > a);
  const h0 = [0, 1, 2, 3, 4, 5].map((i) => slotXY(L, 0, i, 6, 2)[0]), h1 = [0, 1].map((i) => slotXY(L, 1, i, 2, 2)[0]);
  assert.ok(Math.max(...h0) + L.cw / 2 < Math.min(...h1) - L.cw / 2, 'a six-card hand 1 ends before hand 2 starts');
  assert.ok(Math.min(...h0) - L.cw / 2 > 0 && Math.max(...h1) + L.cw / 2 < 1280, 'a six-card Charlie still fits');
});

test('cardRect is the resting spot, known on the frame a card is put down (a still bloom has no drawn frame yet)', () => {
  const t = createTable({ getContext: () => ({}), getBoundingClientRect: () => ({ width: 0, height: 0 }) });
  t.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0); t.addCard({ owner: 0, slot: 1, code: 'As', settled: true }, 0);
  const r = t.cardRect(0, 1), L = tableLayout(0, 0), [x, y] = slotXY(L, 0, 1, 2);
  assert.deepEqual(r, { x: x - L.cw / 2, y: y - L.ch / 2, w: L.cw, h: L.ch });
  assert.deepEqual(t.debug().cards.map((c) => [c.landed, c.face]), [[false, false], [true, true]], 'settled: landed and face up');
  assert.equal(t.cardRect(1, 0), null);
});

test('potRect is THE BANK\'s origin: the chip spot, in cardRect\'s space, with or without a bet on it', () => {
  const t = createTable({ getContext: () => ({}), getBoundingClientRect: () => ({ width: 0, height: 0 }) });
  const L = tableLayout(0, 0), r = t.potRect(), ring = L.spot.r * 1.6;
  assert.deepEqual(r, { x: L.spot.x - ring, y: L.spot.y - ring, w: ring * 2, h: ring * 2 });
  assert.equal(r.x + r.w / 2, L.spot.x, 'centred on the spot the bets sit on');
  assert.equal(r.y + r.h / 2, L.spot.y);
  t.setBets([1, 1]);
  assert.deepEqual(t.potRect(), r, 'a split bets on the same spot, so the tokens leave from the same place');
  t.clear();
  assert.deepEqual(t.potRect(), r, 'never null: the spot is printed whether a bet is down or not');
});

test('the drawn table agrees: the printed ring is the box potRect hands THE BANK', () => {
  const L = tableLayout(1280, 720);
  assert.ok(L.spot.r * 1.6 > 8, 'the tokens leave from something bigger than a point');
  assert.ok(L.spot.y + L.spot.r * 1.6 < 720, 'the origin is on the table, never off the bottom of it');
});
