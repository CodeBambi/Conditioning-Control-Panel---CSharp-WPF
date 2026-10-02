/* ============================================================================
 * smoke/boardmask-smoke.mjs - node tests for the board mask (layers/boardmask.js).
 *
 *   node ConditioningControlPanel/Resources/web/piecebypiece/smoke/boardmask-smoke.mjs
 *
 * Pure geometry only: the hull, the board footprint from a fake camera, the
 * mask image and the "has it moved" gate. The pixels are checked by a headless
 * screenshot at a full meter.
 * ==========================================================================*/

import assert from 'node:assert/strict';
import { hull, boardHull, maskSvg, moved, BOARD_MASK, createBoardMask } from '../ramp/layers/boardmask.js';
import { RAMP_TUNING, clamp01 } from '../ramp/meter.js';
import { sustainedFor } from '../ramp/schedule.js';

let passed = 0;
const check = (name, fn) => { fn(); passed += 1; void name; };

// a flat top-down camera: file a..h -> x 100..800, rank 1..8 -> y 800..100; height lifts y
const FILES = 'abcdefgh';
const flat = (sq, h = 0) => ({ x: 100 + FILES.indexOf(sq[0]) * 100, y: 800 - (Number(sq[1]) - 1) * 100 - h * 40 });

check('hull drops the inside point and keeps the square', () => {
  const out = hull([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }, { x: 0, y: 10 }, { x: 5, y: 5 }]);
  assert.equal(out.length, 4);
});

check('the footprint covers the whole board, edge to edge', () => {
  const poly = boardHull(flat, 1);
  assert.ok(poly && poly.length >= 4);
  const xs = poly.map((q) => q.x), ys = poly.map((q) => q.y);
  // corner centres at 100 and 800, edges half a square further out
  assert.ok(Math.min(...xs) <= 50.01 && Math.max(...xs) >= 849.99, 'x span ' + Math.min(...xs) + '..' + Math.max(...xs));
  // piece height lifts the far rank up the screen: the tops of the back rank are inside too
  assert.ok(Math.min(...ys) < 50, 'top ' + Math.min(...ys));
  assert.ok(Math.max(...ys) >= 849.99, 'bottom ' + Math.max(...ys));
});

check('a broken projection gives no mask rather than a wrong one', () => {
  assert.equal(boardHull(() => ({ x: NaN, y: 0 })), null);
  assert.equal(boardHull(() => { throw new Error('no camera'); }), null);
  assert.equal(boardHull(() => ({ x: 5, y: 5 })), null);   // a point, not a board
});

check('the mask image thins the board and keeps the rest full', () => {
  const css = maskSvg(boardHull(flat, 1), 1280, 860);
  assert.match(css, /^url\("data:image\/svg\+xml,/);
  const svg = decodeURIComponent(css.slice('url("data:image/svg+xml,'.length, -2));
  assert.match(svg, new RegExp(`fill-opacity='${BOARD_MASK.inside}'`));
  assert.match(svg, /fill-rule='evenodd'/);
  assert.match(svg, /feGaussianBlur/);
  assert.ok(BOARD_MASK.inside > 0.15 && BOARD_MASK.inside < 0.6, 'the board still sees a distraction, just a thin one');
});

check('the moved gate ignores jitter and catches a real move', () => {
  const a = boardHull(flat, 1);
  const jitter = a.map((q) => ({ x: q.x + 1, y: q.y - 1 }));
  const shift = a.map((q) => ({ x: q.x + 12, y: q.y }));
  assert.equal(moved(a, jitter), false);
  assert.equal(moved(a, shift), true);
  assert.equal(moved(null, a), true);
});

check('createBoardMask writes both planes once and skips a still board', () => {
  globalThis.window = { innerWidth: 1280, innerHeight: 860 };
  const planes = [{ style: {} }, { style: {} }];
  const m = createBoardMask({ board: { projectSquare: flat }, planes });
  assert.equal(m.update(), true);
  assert.match(planes[0].style.maskImage, /^url\(/);
  assert.equal(planes[1].style.maskImage, planes[0].style.maskImage);
  assert.equal(m.update(), false, 'nothing moved: no rewrite');
  m.dispose();
  assert.equal(planes[0].style.maskImage, '');
  delete globalThis.window;
});

check('owner 2026-10-02: the full-screen layers stay well under the board', () => {
  const top = sustainedFor(1);
  assert.ok(top.blur.px <= 1, 'blur on the board stays under a pixel: ' + top.blur.px);
  assert.ok(top.melt.alpha <= 0.15, 'pink wash peak ' + top.melt.alpha);
  assert.ok(top.spiral.alpha + top.overlay.alpha <= 0.22 + 1e-9, 'veils ' + (top.spiral.alpha + top.overlay.alpha));
  assert.ok(clamp01(RAMP_TUNING.veilBudget) <= 0.25);
});

console.log(`boardmask-smoke: ${passed} passed`);
