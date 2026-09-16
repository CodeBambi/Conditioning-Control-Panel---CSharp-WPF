/* demo.js - the "Try it" previews: which prizes have one, the frames, still, the end. */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DEMO, demoKind, demoFrame, demoLengthMs } from '../demo.js';
import { CATALOG_V1 } from '../mock-server.js';

const inBox = (s) => s.x >= -0.3 && s.x <= 1.3 && s.y >= -0.3 && s.y <= 1.3 && s.scale > 0 && s.scale <= 1 && s.alpha >= 0 && s.alpha <= 1;

test('only the three effect prizes have a preview', () => {
  assert.deepEqual(CATALOG_V1.map((r) => [r.id, demoKind(r.id)]).filter(([, k]) => k), [['jackpot_remix', 'remix'], ['flashes_v2', 'flashes'], ['bubbles_v2', 'bubbles']]);
  for (const id of ['rt_demo', 'high_roller', 'rt_bundle_1', '', null, undefined, 'fx.bubble.rain']) assert.equal(demoKind(id), null, String(id));
});

test('every frame stays in the box and ends at DEMO.ms; still shows one settled frame and ends sooner', () => {
  for (const kind of ['remix', 'flashes', 'bubbles']) {
    let seen = 0;
    for (let ms = 0; ms < DEMO.ms; ms += 50) {
      const f = demoFrame(kind, ms);
      assert.ok(f.length >= 1 && f.length <= DEMO.sprites, kind + ' ' + ms);
      assert.ok(f.every(inBox), kind + ' in the box at ' + ms);
      assert.ok(f.every((s) => Number.isInteger(s.pic) && s.pic >= 0 && s.pic < DEMO.sprites && typeof s.kind === 'string'));
      seen++;
    }
    assert.ok(seen > 50);
    assert.deepEqual(demoFrame(kind, DEMO.ms), [], kind + ' is over at DEMO.ms');
    const a = demoFrame(kind, 100, { still: true }), b = demoFrame(kind, 1500, { still: true });
    assert.deepEqual(a, b, kind + ': still is one settled frame');
    assert.ok(a.length >= 1 && a.every(inBox));
    assert.deepEqual(demoFrame(kind, DEMO.stillMs, { still: true }), [], kind + ' still is over at DEMO.stillMs');
  }
  assert.deepEqual(demoFrame('nothing', 10), []);
  assert.deepEqual(demoFrame('remix', -5).length, 4, 'a negative time reads as 0');
  assert.equal(demoLengthMs(), DEMO.ms); assert.equal(demoLengthMs(true), DEMO.stillMs);
});

test('the three choreographies read as themselves: a mosaic that swaps, a flash that bounces then swings, bubbles that rain then spiral in', () => {
  const r0 = demoFrame('remix', 0), r1 = demoFrame('remix', 600);
  assert.equal(r0.length, 4);
  assert.ok(r0.some((s, i) => s.x !== r1[i].x || s.y !== r1[i].y), 'the tiles move between swaps');
  const f = demoFrame('flashes', 1000), f2 = demoFrame('flashes', 1400), swing = demoFrame('flashes', 3000);
  assert.equal(f.length, 1); assert.ok(f[0].x !== f2[0].x || f[0].y !== f2[0].y, 'drifting');
  assert.equal(f[0].rot, 0); assert.equal(swing[0].pivot, 'top'); assert.ok(Math.abs(swing[0].rot) > 0, 'swinging from the top');
  const rain = demoFrame('bubbles', 500), later = demoFrame('bubbles', 900), spiral = demoFrame('bubbles', 4700);
  assert.equal(rain.length, 4);
  assert.ok(rain.some((s, i) => later[i].y !== s.y), 'falling');
  assert.ok(spiral.every((s) => Math.hypot(s.x - 0.5, s.y - 0.5) < 0.12 && s.alpha < 1), 'winding in to the centre and fading');
});
