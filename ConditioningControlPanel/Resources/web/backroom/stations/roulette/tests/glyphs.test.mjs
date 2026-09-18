/* glyphs.test.mjs - THE POCKET GLYPHS (glyphs.js, GLYPHS.md): the pocket -> glyph -> effect key, deterministic, page-side.
 *   node --test ConditioningControlPanel/Resources/web/backroom/stations/roulette/tests/glyphs.test.mjs */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { GLYPHS, GLYPH_RING, GLYPH_IDS, glyphFor, glyphFx, paintGlyph } from '../glyphs.js';
import { FX, FX_GATE, FX_RECIPE, fxPlan, calloutFor } from '../feel.js';

const WHEEL = [0, 32, 15, 19, 4, 21, 2, 25, 17, 34, 6, 27, 13, 36, 11, 30, 8, 23, 10, 5, 24, 16, 33, 1, 20, 14, 31, 9, 22, 18, 29, 7, 28, 12, 35, 3, 26];
const HOST_IDS = ['fx.gif_burst', 'fx.gif_from', 'fx.gif_storm', 'fx.haze', 'fx.jackpot', 'fx.loom_spiral', 'fx.melt', 'fx.spiral_brief', 'fx.spiral_full', 'fx.sub_cascade', 'fx.sub_pair', 'fx.sub_single', 'fx.wash'];

test('four glyphs, one existing host id each, frozen', () => {
  assert.deepEqual(GLYPH_RING, ['spiral', 'eye', 'bubble', 'drop']);
  assert.deepEqual([...GLYPH_IDS].sort(), [...GLYPH_RING].sort());
  assert.ok(Object.isFrozen(GLYPHS) && Object.isFrozen(GLYPH_RING) && Object.isFrozen(GLYPHS.spiral));
  for (const id of GLYPH_RING) assert.ok(HOST_IDS.includes(GLYPHS[id].fx) && FX_GATE[GLYPHS[id].fx] && FX.COOLDOWN_MS[GLYPHS[id].fx] > 0, id + ' maps to a host id the bridge handles, with a cooldown');
  assert.equal(new Set(GLYPH_RING.map((id) => GLYPHS[id].fx)).size, 4, 'no two glyphs share an effect');
  assert.deepEqual(GLYPH_RING.map((id) => GLYPHS[id].fx), ['fx.spiral_brief', 'fx.gif_burst', 'fx.sub_pair', 'fx.melt']);
});

test('every one of the 37 pockets has its glyph by number: 0 none, 1..36 round the ring, nine of each', () => {
  assert.equal(glyphFor(0), null, 'the house pocket does nothing for you');
  assert.equal(glyphFx(0), null);
  const seen = new Map();
  for (let n = 1; n <= 36; n++) {
    const id = glyphFor(n);
    assert.equal(id, GLYPH_RING[(n - 1) % 4], 'pocket ' + n);
    assert.equal(glyphFx(n), GLYPHS[id].fx);
    seen.set(id, (seen.get(id) || 0) + 1);
  }
  assert.deepEqual([...seen.values()], [9, 9, 9, 9]);
  assert.equal(WHEEL.length, 37);
  assert.equal(WHEEL.map(glyphFor).filter(Boolean).length, 36, 'the server order changes nothing: the number is the key');
  assert.equal(glyphFor(17), 'spiral'); assert.equal(glyphFor(32), 'drop'); assert.equal(glyphFor(2), 'eye'); assert.equal(glyphFor(3), 'bubble');
});

test('the key is deterministic and refuses anything that is not a pocket', () => {
  for (let k = 0; k < 3; k++) assert.deepEqual(WHEEL.map(glyphFor), WHEEL.map(glyphFor));
  for (const bad of [-1, 37, 1.5, NaN, null, undefined, '17x', {}]) assert.equal(glyphFor(bad), null, String(bad));
  assert.equal(glyphFor('17'), 'spiral', 'a numeric string reads as its number');
});

test('the glyph beat (feel.fxPlan): the pocket picks the id, Calm strips the eye, nothing knows the pay, no storm', () => {
  assert.deepEqual(FX_RECIPE.glyph, [], 'the row is filled per pocket');
  for (let n = 0; n <= 36; n++) {
    const plan = fxPlan('glyph', { pocket: n, streak: 9, full: true });
    assert.deepEqual(plan.map((s) => s.fx), n === 0 ? [] : [glyphFx(n)], 'pocket ' + n);
    assert.ok(plan.every((s) => !s.gif && !s.words && !s.once));
  }
  assert.deepEqual(fxPlan('glyph', { pocket: 2, calm: true }), [], 'Calm strips the flash burst');
  assert.deepEqual(fxPlan('glyph', { pocket: 1, calm: true }).map((s) => s.fx), ['fx.spiral_brief'], 'Calm keeps the spiral');
  assert.deepEqual(fxPlan('glyph', {}), [], 'no pocket, no effect');
  assert.equal(calloutFor('glyph', { streak: 9 }), null, 'the glyph never names a landing; the callout does');
  assert.deepEqual(fxPlan('land.win').map((s) => s.fx), [], 'the glyph IS the fullscreen step of an outside win now');
  assert.deepEqual(fxPlan('land.straight').map((s) => s.fx), [], 'and of a straight-up hit');
});

test('every glyph paints, on a plain 2d context, inside its box', () => {
  for (const id of GLYPH_RING) {
    const calls = [], xs = [], ys = [];
    const note = (name) => (...a) => { calls.push(name); if (['moveTo', 'lineTo', 'arc'].includes(name)) { xs.push(a[0]); ys.push(a[1]); } };
    const g = new Proxy({}, { get: (_, name) => (name === 'lineWidth' ? 0 : note(name)), set: () => true });
    assert.equal(paintGlyph(g, id, 64), true, id);
    assert.ok(calls.includes('stroke') || calls.includes('fill'), id + ' draws something');
    assert.ok(xs.every((v) => v >= 0 && v <= 64) && ys.every((v) => v >= 0 && v <= 64), id + ' stays inside its box');
  }
  assert.equal(paintGlyph(new Proxy({}, { get: () => () => {}, set: () => true }), 'moon', 64), false, 'an unknown glyph paints nothing');
});

test('a glyph is traced twice: a dark contour under the cream mark, so it reads on both halves of the wheel', () => {
  for (const id of GLYPH_RING) {
    const sets = [], calls = [];
    const g = new Proxy({}, {
      get: (_, name) => (name === 'lineWidth' ? 0 : (...a) => { calls.push(name); void a; }),
      set: (_, name, v) => { sets.push([name, v]); return true; },
    });
    assert.equal(paintGlyph(g, id, 64), true, id);
    const strokes = sets.filter(([k]) => k === 'strokeStyle').map(([, v]) => v);
    const widths = sets.filter(([k]) => k === 'lineWidth').map(([, v]) => v);
    assert.equal(strokes.length, 2, id + ': two passes');
    assert.notEqual(strokes[0], '#ffffff', id + ': the first pass is the dark contour');
    assert.equal(strokes[1], '#ffffff', id + ': the cream goes on top');
    assert.ok(widths[0] > widths[1], id + ': the contour is the wider line, so it shows as an edge');
    assert.equal(calls.filter((n) => n === 'clearRect').length, 1, id + ': the box is cleared once, not per pass');
  }
});
