/* backroom/room/tests/welcome-placards.test.mjs - the first-visit card's first and last pages, framed on the counter.
 * node --test ConditioningControlPanel/Resources/web/backroom/room/tests/*.test.mjs */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { PLACARDS, PANEL, PAPER, paintPlacard } from '../welcome-wall.js';
import { PAGES, FALLBACK } from '../welcome.js';

/* The counter as stations.json places it: the placards must hang on its apron, not float in the room. */
const counter = JSON.parse(readFileSync(new URL('../../stations.json', import.meta.url), 'utf8')).find(r => r.id === 'counter');

test('two placards, the room and the Parlour, mirrored across the SPARKLES / PRIZES panel, on the counter apron', () => {
  assert.equal(PAGES.length, 3, 'three pages: the room, pictures and sparkles, the Parlour');
  assert.deepEqual(PAGES.map(p => p.id), ['welcome', 'media', 'prizes']);
  assert.equal(PLACARDS.length, 2, 'the middle page hangs nowhere');
  assert.deepEqual(PLACARDS.map(p => p.page), [0, 2], 'each placard opens the card at its own index in PAGES');
  assert.deepEqual(PLACARDS.map(p => p.id), ['placard-welcome', 'placard-prizes']);
  assert.deepEqual(PAGES.filter(p => p.placard).map(p => 'placard-' + p.id), PLACARDS.map(p => p.id));
  const [left, right] = PLACARDS;
  assert.equal(left.position[0], -right.position[0], 'the two panels sit either side of the middle one');
  assert.ok(left.position[0] < 0 && right.position[0] > 0, 'the room on the left, the Parlour on the right');
  for (const p of PLACARDS) {
    assert.equal(p.position[1], PANEL.y); assert.equal(p.position[2], PANEL.z); assert.equal(p.yaw, 0);
    const { min, max } = counter.fixture.bounds;
    assert.ok(p.position[0] - p.size[0] / 2 > min[0] && p.position[0] + p.size[0] / 2 < max[0], p.id + ' inside the counter, x');
    assert.ok(p.position[1] > min[1] && p.position[1] < max[1], p.id + ' inside the counter, y');
    assert.ok(p.position[2] > max[2] - .4 && p.position[2] <= max[2], p.id + ' on the front face (z ' + max[2] + ')');
    assert.ok(p.size[0] / p.size[1] > 3 && p.size[0] / p.size[1] < 3.6, 'a landscape panel');
  }
});

test('each placard carries its page: the picture on disk, the title and the line in the lexicon', () => {
  for (const p of PLACARDS) {
    assert.equal(p.src, PAGES[p.page].hero);
    assert.ok(existsSync(new URL('../../' + p.src, import.meta.url)), p.src + ' exists');
    assert.equal(p.titleKey, PAGES[p.page].title); assert.equal(p.subKey, PAGES[p.page].sub);
    for (const k of [p.titleKey, p.subKey, p.readKey]) assert.ok(FALLBACK[k], k + ' has an English fallback');
  }
  assert.equal(FALLBACK[PLACARDS[1].titleKey], 'The Prize Parlour');
});

/* A context that records what was drawn where. */
function recorder() {
  const ops = [];
  return { ops, ctx: new Proxy({}, { get: (_, name) => name === 'measureText' ? (t) => ({ width: t.length * 12 })
    : (...args) => { ops.push([name, ...args]); } }) };
}

test('paintPlacard: a 2:1 picture box the paper\'s full height, the title, the line, and the invitation', () => {
  const { ops, ctx } = recorder();
  paintPlacard(ctx, { image: { width: 1024, height: 512 }, title: 'The Prize Parlour', sub: 'What your sparkles buy.', read: 'Tap to read' });
  const draw = ops.find(o => o[0] === 'drawImage');
  assert.ok(draw, 'the picture is drawn');
  const [, , sx, sy, sw, sh, dx, dy, dw, dh] = draw;
  assert.equal(dh, PAPER.h - PAPER.pad * 2); assert.equal(dw, dh * 2);
  assert.equal(dx, PAPER.pad); assert.equal(dy, PAPER.pad);
  assert.deepEqual([sx, sy, sw, sh], [0, 0, 1024, 512], 'a 2:1 picture fills the box without a crop');
  const texts = ops.filter(o => o[0] === 'fillText').map(o => o[1]);
  assert.deepEqual(texts, ['The Prize Parlour', 'What your sparkles buy.', 'TAP TO READ']);
  assert.ok(ops.find(o => o[0] === 'fillText' && o[2] > PAPER.pad + dw), 'the words sit to the right of the picture');
});

test('paintPlacard: a taller picture is cover-cropped, a missing one leaves the box and the words', () => {
  const tall = recorder();
  paintPlacard(tall.ctx, { image: { width: 688, height: 384 }, title: 't', sub: 's', read: 'r' });
  const [, , sx, sy, sw, sh] = tall.ops.find(o => o[0] === 'drawImage');
  assert.equal(sw, 688); assert.ok(sh < 384 && sy > 0 && sx === 0, 'the 16:9 hero loses a little top and bottom, never a side');
  const none = recorder();
  paintPlacard(none.ctx, { image: null, title: 't', sub: 's', read: 'r' });
  assert.ok(!none.ops.find(o => o[0] === 'drawImage'));
  assert.equal(none.ops.filter(o => o[0] === 'fillText').length, 3);
});
