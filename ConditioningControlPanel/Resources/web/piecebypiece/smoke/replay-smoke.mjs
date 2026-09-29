// node smoke/replay-smoke.mjs - capture replay timing, stagger, highlights, enter/exit motion,
// the per-panel hit and the clock rules.
import assert from 'node:assert/strict';
import { LAYOUTS, SHOTS, REPLAY, hitAt, replayLength, warp, clipTime, panelState, replayAllowed,
  createLayoutDeck, panelCount, scalePoly, orient, placePoly, offStage, slideOf, exitLength } from '../board/replay-plan.js';
import { burstStyle, impactWords, boil } from '../board/replay-fx.js';

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
      else assert.ok(s.light < .7 && s.desat > .3 && !s.lit, `${layout}: panel ${j} dims and greys while ${i} hits`);
    }
  }
  assert.ok(replayLength(layout) < 3.2, `${layout}: the replay stays quick`);
  const end = replayLength(layout) - REPLAY.exit;
  for (const portrait of [false, true]) {
    const tag = `${layout}${portrait ? ' portrait' : ''}`;
    const at = (i, t, e = end, skipped = false) => {
      const s = panelState(layout, i, t, e, { portrait, skipped });
      return { s, poly: placePoly(orient(LAYOUTS[layout][i], portrait), s.scale, s.ox, s.oy).pts };
    };
    const seated = (n - 1) * REPLAY.enterGap + REPLAY.enter;
    assert.ok(seated < hitAt(layout, 0) - .1, `${tag}: every panel has landed before the first hit`);
    for (let i = 0; i < n; i++) {
      // ENTER: off stage at the start, in from its own edge, a swing past the seat, then seated.
      assert.ok(offStage(at(i, 0).poly), `${tag} ${i}: starts off stage`);
      const sl = slideOf(orient(LAYOUTS[layout][i], portrait));
      assert.ok((sl.cx - .5) * sl.dx + (sl.cy - .5) * sl.dy >= 0, `${tag} ${i}: slides in from its own side`);
      let least = Infinity, prevSlide = Infinity, monotoneIn = true;
      for (let t = 0; t <= seated + .2; t += .005) {
        const { s } = at(i, t);
        least = Math.min(least, s.slide);
        if (s.slide > 0 && s.slide > prevSlide + 1e-9) monotoneIn = false;
        prevSlide = s.slide;
      }
      assert.ok(least < -.02 && least > -.2, `${tag} ${i}: overshoots its seat a little (${least.toFixed(3)})`);
      assert.ok(monotoneIn, `${tag} ${i}: comes in without backing off first`);
      const up = at(i, seated + .02).s;
      assert.ok(Math.abs(up.slide) < .005 && up.scale > .99, `${tag} ${i}: seated once the enter is done`);
      assert.equal(at(i, 0).s.seam, 0, `${tag} ${i}: no ink before it arrives`);
      assert.ok(at(i, seated + .1).s.seam > .99, `${tag} ${i}: ink fully drawn once seated`);
      let wiped = false;
      for (let t = 0; t <= seated + .2; t += .01) if (at(i, t).s.wipe >= 0) wiped = true;
      assert.ok(wiped, `${tag} ${i}: a white wipe crosses it as it lands`);
      assert.equal(at(i, hitAt(layout, 0)).s.wipe, -1, `${tag} ${i}: the wipe is over by the first hit`);
      // EXIT: a small pull back, then out the way it came; the ink retracts.
      const ex = at(i, end + .03).s;
      if (i === n - 1) assert.ok(ex.slide < 0, `${tag} ${i}: pulls back before it leaves`);
      assert.ok(offStage(at(i, end + REPLAY.exit).poly), `${tag} ${i}: gone at the end`);
      assert.equal(at(i, end + REPLAY.exit).s.seam, 0, `${tag} ${i}: ink retracted`);
      // A skip mid-replay leaves faster, all at once, with no hits after it.
      const cut = hitAt(layout, 0) + .15;
      assert.ok(offStage(at(i, cut + exitLength(true), cut, true).poly), `${tag} ${i}: a skip is gone in ${exitLength(true)} s`);
      if (i > 0) {
        const late = at(i, hitAt(layout, i) + .02, cut, true).s;
        assert.ok(late.zoom === 1 && late.burst === -1 && late.word.alpha === 0 && !late.lit, `${tag} ${i}: no hit after a skip`);
      }
    }
    // Normal exit is staggered: the last panel leaves first.
    if (n > 1) {
      const t = end + REPLAY.exit * .55;
      assert.ok(at(n - 1, t).s.slide > at(0, t).s.slide, `${tag}: the last panel leaves first`);
    }
  }
  // THE HIT: punch, shake, chroma, burst, word and flash on each panel's own cue, none before.
  for (let i = 0; i < n; i++) {
    const before = panelState(layout, i, hitAt(layout, i) - .1);
    assert.ok(before.zoom === 1 && before.chroma === 0 && before.burst === -1 && before.word.alpha === 0, `${layout} ${i}: quiet before its hit`);
    const on = panelState(layout, i, hitAt(layout, i) + .03);
    assert.ok(on.zoom > 1.05 && on.chroma > 3 && on.burst >= 0 && on.burst < .2 && on.word.alpha === 1 && on.flash > 0,
      `${layout} ${i}: the hit punches`);
    assert.ok(Math.hypot(on.ox, on.oy) > 0 && Math.hypot(on.ox, on.oy) < .045, `${layout} ${i}: the frame shakes, within reason`);
    const after = panelState(layout, i, hitAt(layout, i) + REPLAY.wordSec + .05);
    assert.ok(after.zoom === 1 && after.burst === -1 && after.word.alpha === 0 && Math.hypot(after.ox, after.oy) <= REPLAY.shake * REPLAY.shakeKin * 1.5, `${layout} ${i}: and settles (a neighbour's hit may still jolt it)`);
  }
  assert.ok(exitLength(true) < exitLength(false), `${layout}: a skip leaves faster`);
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
// Impact looks and words.
assert.equal(burstStyle('fling'), 0); assert.equal(burstStyle('squash'), 1); assert.equal(burstStyle('slap'), 2);
for (const impact of ['fling', 'slap', 'squash', undefined]) {
  const w = impactWords(impact, () => .5);
  assert.equal(new Set(w).size, 3, 'three different impact words');
  assert.ok(w.every(x => /^[A-Z]{3,6}$/.test(x)), 'short words, no punctuation');
}
// The ink boils the same way wherever two panels share a corner, and only a little.
const b1 = boil(.6, 0, 1.23), b2 = boil(.6, 0, 1.23);
assert.deepEqual(b1, b2);
assert.ok(Math.abs(b1[0]) <= 1.6 && Math.abs(b1[1]) <= 1.6, 'seam wobble stays small');
assert.deepEqual(boil(.6, 0, 1.2), boil(.6, 0, 1.24), 'the boil steps, it does not crawl');
console.log('replay: stagger, slow motion, highlights, enter/exit, hits, clock rules and layout deck passed');
