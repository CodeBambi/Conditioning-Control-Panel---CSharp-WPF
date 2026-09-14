import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tableLayout, slotXY } from '../table.js';

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
