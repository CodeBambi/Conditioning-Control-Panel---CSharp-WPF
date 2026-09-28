// node smoke/replay-smoke.mjs - capture replay timing, stagger, highlights and the clock rules.
import assert from 'node:assert/strict';
import { LAYOUTS, SHOTS, REPLAY, hitAt, replayLength, warp, clipTime, panelState, replayAllowed,
  createLayoutDeck, panelCount, scalePoly, orient } from '../board/replay-plan.js';

// Warp is monotonic, odd, and slow at contact.
for (let x = -3; x < 3; x += .01) assert.ok(warp(x + .01) > warp(x), 'warp never runs backwards');
assert.ok(Math.abs(warp(.5) + warp(-.5)) < 1e-9, 'warp is symmetric about the hit');
assert.ok(warp(.05) / .05 < .4, 'contact plays in slow motion');
assert.ok(Math.abs((warp(2) - warp(1.5)) / .5 - 1) < .01, 'far from contact plays at full speed');

const clip = { duration: 3.2, hit: 1.4 };
for (const layout of Object.keys(LAYOUTS)) {
  const n = panelCount(layout);
  assert.equal(SHOTS[layout].length, n, `${layout}: one camera per panel`);
  for (let i = 0; i < n; i++) {
    // Each panel shows the hit exactly at its own staggered moment.
    assert.ok(Math.abs(clipTime(layout, i, hitAt(layout, i), clip) - clip.hit) < 1e-9, `${layout} ${i}: hit lands on cue`);
    if (i) {
      assert.ok(hitAt(layout, i) > hitAt(layout, i - 1) + .3, `${layout}: panel ${i} hits after panel ${i - 1}`);
      assert.ok(clipTime(layout, i, 0, clip) < clipTime(layout, i - 1, 0, clip), `${layout}: later panels start further back`);
    }
    // Every panel keeps playing through the replay; nothing leaves the clip.
    let prev = -1;
    for (let t = 0; t <= replayLength(layout); t += .02) {
      const c = clipTime(layout, i, t, clip);
      assert.ok(c >= 0 && c <= clip.duration && c >= prev, `${layout} ${i}: clip time in range and forward`);
      prev = c;
    }
    assert.ok(clipTime(layout, i, replayLength(layout) - REPLAY.exit, clip) > clip.hit + .35, `${layout} ${i}: action keeps going after its hit`);
  }
  // At each panel's hit, that panel is lit and every other panel is dimmed.
  for (let i = 0; i < n && n > 1; i++) {
    const t = hitAt(layout, i) + .05;
    for (let j = 0; j < n; j++) {
      const s = panelState(layout, j, t);
      if (j === i) assert.ok(s.lit && s.light === 1 && s.flash > 0, `${layout}: panel ${i} lit on its hit`);
      else assert.ok(s.light < .5, `${layout}: panel ${j} dims while ${i} hits`);
    }
  }
  assert.ok(panelState(layout, 0, replayLength(layout)).scale < .01, `${layout}: panels are gone at the end`);
  assert.ok(panelState(layout, n - 1, .6).scale > .9, `${layout}: all panels are up early`);
  assert.ok(replayLength(layout) < 3.2, `${layout}: the replay stays quick`);
}

// Clock and seat rules.
assert.equal(replayAllowed({}), 'full');
assert.equal(replayAllowed({ online: true }), 'corner');
assert.equal(replayAllowed({ leastMs: 20000 }), 'corner');
assert.equal(replayAllowed({ leastMs: 9000 }), 'off');
assert.equal(replayAllowed({ reduced: true }), 'off');
assert.equal(replayAllowed({ enabled: false }), 'off');

// The deck mixes all three and never repeats back to back.
for (const random of [() => 0, () => .999, Math.random]) {
  const deck = createLayoutDeck(random);
  let last = null; const seen = new Set();
  for (let k = 0; k < 60; k++) { const l = deck(); assert.notEqual(l, last, 'no layout twice in a row'); seen.add(l); last = l; }
  assert.equal(seen.size, 3, 'every layout comes round');
  assert.equal(deck('corner'), 'corner', 'a forced corner is a corner');
}

// Geometry helpers.
const sq = [[0, 0], [1, 0], [1, 1], [0, 1]];
assert.deepEqual(scalePoly(sq, 0).pts, [[.5, .5], [.5, .5], [.5, .5], [.5, .5]]);
assert.deepEqual(orient([[.2, .7]], true), [[.7, .2]]);
console.log('replay: stagger, slow motion, highlights, clock rules and layout deck passed');
