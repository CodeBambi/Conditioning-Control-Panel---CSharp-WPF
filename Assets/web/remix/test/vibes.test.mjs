import test from 'node:test';
import assert from 'node:assert/strict';
import { VIBES, vibePatch, rerollBlock, surprisePatch } from '../engine/vibes.js';
import { validateBlock, createBlock, EFFECTS } from '../engine/blocks.js';
import { codeToSeed } from '../engine/code.js';

function state(n = 4) {
  const tiles = [];
  for (let i = 0; i < n; i++) tiles.push({ id: 't' + i, mediaId: 'm' + i, enterFrame: i * 9, playMode: 'forward' });
  return { frames: 75, fps: 15, tiles, layout: { mode: 'grow', stageMs: 600 }, loop: 'snap', captionText: '' };
}

test('there are four vibes and each one names itself', () => {
  assert.equal(VIBES.length, 4);
  assert.deepEqual(VIBES.map((v) => v.id), ['grow', 'haunt', 'flashdeck', 'didyouseeit']);
  for (const v of VIBES) {
    assert.ok(v.name && v.line);
    assert.ok(!/bank/i.test(v.line), 'copy rule');
    assert.ok(!v.line.includes('—'), 'no em dash');
  }
});

test('every vibe produces a legal timeline', () => {
  for (const v of VIBES) {
    const patch = vibePatch(v.id, state(), 4242);
    assert.ok(patch.frames >= 45 && patch.frames <= 120, v.id);
    assert.ok(['clean', 'snap', 'seamless'].includes(patch.loop), v.id);
    assert.ok(patch.layout && patch.layout.mode, v.id);
    for (const b of patch.blocks) {
      assert.equal(validateBlock(b, { frames: patch.frames }), null, `${v.id} ${b.effect}`);
      assert.ok(EFFECTS[b.effect].modes.includes(b.mode));
    }
  }
});

test('vibes are pure: the input state comes back untouched', () => {
  const s = state();
  const snapshot = JSON.stringify(s);
  for (const v of VIBES) vibePatch(v.id, s, 1);
  assert.equal(JSON.stringify(s), snapshot);
});

test('the same seed always lays down the same timeline', () => {
  for (const v of VIBES) {
    const a = vibePatch(v.id, state(), codeToSeed('CCP-7K2M'));
    const b = vibePatch(v.id, state(), codeToSeed('CCP-7K2M'));
    assert.deepEqual(strip(a), strip(b), v.id);
  }
  function strip(p) {
    return Object.assign({}, p, { blocks: p.blocks.map((b) => Object.assign({}, b, { id: null })) });
  }
});

test('grow creeps colour in over the back of the loop and snaps at the seam', () => {
  const p = vibePatch('grow', state(), 9);
  assert.equal(p.layout.mode, 'grow');
  assert.equal(p.layout.stageMs, 600);
  assert.equal(p.loop, 'snap');
  assert.equal(p.blocks.length, 1);
  assert.equal(p.blocks[0].effect, 'tint');
  assert.equal(p.blocks[0].mode, 'creep');
  assert.equal(p.blocks[0].start, 30);
  assert.equal(p.blocks[0].end, 75);
});

test('haunt floats a ghost over the back half and creeps colour over the last third', () => {
  const p = vibePatch('haunt', state(5), 9);
  assert.equal(p.loop, 'snap');
  const ghost = p.blocks.find((b) => b.effect === 'glitch');
  assert.equal(ghost.mode, 'ghost');
  assert.equal(ghost.params.strength, 55);
  assert.equal(ghost.start, 38);
  assert.equal(ghost.end, p.frames);
  const creep = p.blocks.find((b) => b.effect === 'tint');
  assert.equal(creep.mode, 'creep');
  assert.equal(creep.start, 50);
  assert.equal(creep.end, p.frames);
  assert.ok(creep.start > ghost.start, 'the colour arrives after the ghost');
});

test('no vibe lays down a drain, shelved effects stay off the on ramp', () => {
  for (const v of VIBES) {
    for (const b of vibePatch(v.id, state(5), 77).blocks) assert.notEqual(b.effect, 'drain', v.id);
  }
  for (let i = 0; i < 60; i++) {
    for (const b of surprisePatch(state(4), i * 977 + 3).blocks) assert.notEqual(b.effect, 'drain');
  }
});

test('flash deck cuts one gif at a time with a word between each', () => {
  const p = vibePatch('flashdeck', state(3), 9);
  assert.equal(p.layout.mode, 'deck');
  assert.equal(p.layout.deckHold, 15);
  assert.equal(p.loop, 'clean');
  const caps = p.blocks.filter((b) => b.effect === 'caption');
  assert.ok(caps.length >= 3);
  for (const c of caps) {
    assert.equal(c.mode, 'flash');
    assert.equal(c.end - c.start, 1);
    assert.ok(c.params.text.length > 0);
  }
  assert.deepEqual(caps.map((c) => c.start), [15, 30, 45, 60]);
});

test('flash deck uses the caption words when there are any', () => {
  const s = state(3);
  s.captionText = 'sink deeper now';
  const words = new Set(vibePatch('flashdeck', s, 9).blocks.map((b) => b.params.text));
  for (const w of words) assert.ok(['sink', 'deeper', 'now'].includes(w), w);
});

test('flash deck falls back to house words with a single gif', () => {
  const p = vibePatch('flashdeck', state(1), 9);
  assert.equal(p.blocks.length, 0, 'nothing to cut between');
});

test('did you see it drifts a focus ring and hides one frame late', () => {
  const p = vibePatch('didyouseeit', state(), 9);
  assert.equal(p.layout.stageMs, 900);
  assert.equal(p.loop, 'seamless');
  const focus = p.blocks.find((b) => b.effect === 'focus');
  assert.equal(focus.mode, 'ring');
  assert.equal(focus.params.path.length, 3);
  for (const pt of focus.params.path) {
    assert.ok(pt.x >= 0 && pt.x <= 1 && pt.y >= 0 && pt.y <= 1);
  }
  const flash = p.blocks.find((b) => b.effect === 'caption');
  assert.equal(flash.start, 47);
  assert.equal(flash.end, 48);
});

test('an unknown vibe is refused', () => {
  assert.throws(() => vibePatch('nope', state(), 1), /Unknown vibe/);
});

test('rerolling a block changes its mode and one knob, and stays legal', () => {
  const b = createBlock({ effect: 'spiral', mode: 'over', start: 0, end: 75 }, 75);
  const p = rerollBlock(b, 1234);
  assert.ok(EFFECTS.spiral.modes.includes(p.mode));
  assert.ok(p.params.style >= 0 && p.params.style <= 2);
  assert.ok([1, -1].includes(p.params.dir));
  const merged = Object.assign({}, b, p);
  assert.equal(validateBlock(merged, { frames: 75 }), null);
  assert.deepEqual(rerollBlock(b, 1234), rerollBlock(b, 1234), 'seed stable');
});

test('rerolling a caption into a flash shortens it to one frame', () => {
  const b = createBlock({ effect: 'caption', mode: 'text', start: 10, end: 40 }, 75);
  for (let seed = 0; seed < 40; seed++) {
    const p = rerollBlock(b, seed);
    if (p.mode === 'flash') {
      assert.equal(p.end, 11);
      return;
    }
  }
  assert.fail('no flash in 40 rolls');
});

test('surprise picks a vibe and rerolls it, deterministically', () => {
  const a = surprisePatch(state(), 555);
  const b = surprisePatch(state(), 555);
  assert.equal(a.vibe, b.vibe);
  assert.ok(VIBES.some((v) => v.id === a.vibe));
  for (const blk of a.blocks) assert.equal(validateBlock(blk, { frames: a.frames }), null);
  assert.deepEqual(a.blocks.map((x) => [x.effect, x.mode, x.start, x.end]),
    b.blocks.map((x) => [x.effect, x.mode, x.start, x.end]));
});

test('surprise reaches more than one vibe across seeds', () => {
  const seen = new Set();
  for (let s = 0; s < 60; s++) seen.add(surprisePatch(state(), s).vibe);
  assert.ok(seen.size >= 3, [...seen].join(','));
});
