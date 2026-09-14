// The roll: autoCompose is pure and deterministic, stays inside the effect
// budget, never rolls a drain or a caption, and reads layouts from the registry.
// The roll op keeps a history the arrows walk.
import test from 'node:test';
import assert from 'node:assert/strict';
import { autoCompose, AUTO_WEIGHTS, passesGate, coverageOf, longestClean } from '../engine/auto.js';
import { LAYOUTS } from '../engine/layout.js';
import { EFFECTS, validateBlock } from '../engine/blocks.js';
import { createOps, ROLL_CAP } from '../engine/ops.js';
import { codeToSeed, seedToCode } from '../engine/code.js';

function state(n = 3, extra = {}) {
  const tiles = [];
  for (let i = 0; i < n; i++) tiles.push({ id: 't' + i, mediaId: 'm' + i, enterFrame: i * 9, playMode: 'forward' });
  return Object.assign({
    frames: 75, fps: 15, tiles, layout: { mode: 'grow', stageMs: 600 }, loop: 'snap', captionText: '', orientation: 'landscape',
    media: tiles.map((t, i) => ({ id: t.mediaId, w: i % 2 ? 270 : 480, h: i % 2 ? 480 : 270 })),
  }, extra);
}
const strip = (p) => Object.assign({}, p, { blocks: p.blocks.map((b) => Object.assign({}, b, { id: null })) });

test('same seed and same media count give the same patch', () => {
  for (const n of [1, 2, 5, 8]) {
    const a = autoCompose(state(n), codeToSeed('CCP-7K2M'));
    const b = autoCompose(state(n), codeToSeed('CCP-7K2M'));
    assert.deepEqual(strip(a), strip(b), 'tiles ' + n);
  }
});

test('the roll is pure: the state comes back untouched', () => {
  const s = state(4); const snap = JSON.stringify(s);
  for (let i = 0; i < 20; i++) autoCompose(s, i);
  assert.equal(JSON.stringify(s), snap);
});

test('never a drain, never a caption, one to three canvas blocks, legal modes', () => {
  for (const n of [1, 2, 3, 4, 8]) for (let seed = 0; seed < 400; seed++) {
    const p = autoCompose(state(n), seed);
    assert.ok(p.blocks.length >= 1 && p.blocks.length <= 3, `n ${n} seed ${seed}`);
    for (const b of p.blocks) {
      assert.notEqual(b.effect, 'drain'); assert.notEqual(b.effect, 'caption');
      assert.equal(b.target, 'canvas');
      assert.equal(validateBlock(b, { frames: 75 }), null);
      assert.ok(EFFECTS[b.effect].modes.includes(b.mode));
    }
  }
});

test('coverage stays under 70 percent with a clean second, and the gate agrees', () => {
  for (const n of [1, 3, 6]) for (let seed = 0; seed < 400; seed++) {
    const p = autoCompose(state(n), seed);
    assert.ok(coverageOf(p.blocks, 75) <= AUTO_WEIGHTS.coverage + 1e-9, `n ${n} seed ${seed}`);
    assert.ok(longestClean(p.blocks, 75) >= 15, `n ${n} seed ${seed}`);
    assert.ok(passesGate(p, 75, 15));
  }
});

test('never two spirals; ghost and focus need two gifs; knobs in the middle band', () => {
  for (const n of [1, 2, 4]) for (let seed = 0; seed < 400; seed++) {
    const p = autoCompose(state(n), seed);
    assert.ok(p.blocks.filter((b) => b.effect === 'spiral').length <= 1);
    for (const b of p.blocks) {
      if (n < 2) { assert.notEqual(b.mode, 'ghost'); assert.notEqual(b.effect, 'focus'); }
      for (const k of EFFECTS[b.effect].knobs) {
        assert.ok(b.params[k.key] >= 35 && b.params[k.key] <= 70, `${b.effect} ${k.key} ${b.params[k.key]}`);
      }
    }
  }
});

test('snap only lands when a tint ends against the loop end', () => {
  for (let seed = 0; seed < 600; seed++) {
    const p = autoCompose(state(3), seed);
    if (p.loop === 'snap') {
      assert.ok(p.blocks.some((b) => b.effect === 'tint' && 75 - b.end <= AUTO_WEIGHTS.snapWindow), 'seed ' + seed);
    }
  }
});

test('layouts come from the registry: one gif never shuffles or grows, a new layout joins the roll', () => {
  const seen = new Set();
  for (let seed = 0; seed < 300; seed++) seen.add(autoCompose(state(1), seed).layout.mode);
  assert.deepEqual([...seen].sort(), ['flat', 'mirror', 'pinwheel', 'tunnel']);
  LAYOUTS.push({ id: 'slide', name: 'Slide', minTiles: 2 });
  try {
    const modes = new Set();
    for (let seed = 0; seed < 300; seed++) modes.add(autoCompose(state(4), seed).layout.mode);
    assert.ok(modes.has('slide'), 'a registered layout rolls without an auto.js edit');
    for (let seed = 0; seed < 100; seed++) assert.notEqual(autoCompose(state(1), seed).layout.mode, 'slide');
  } finally { LAYOUTS.pop(); }
});

test('more gifs, shorter stages', () => {
  const few = new Set(), many = new Set();
  for (let seed = 0; seed < 200; seed++) {
    few.add(autoCompose(state(2), seed).layout.stageMs);
    many.add(autoCompose(state(8), seed).layout.stageMs);
  }
  assert.ok(Math.min(...few) >= Math.max(...many));
});

/* ------------------------------------------------------------ the op --*/

function ops(n = 3) {
  const st = state(n);
  const s = {
    frames: 75, fps: 15, seed: 4242, orientation: 'landscape', media: st.media, tiles: st.tiles,
    layout: { mode: 'grow', stageMs: 600, deckHold: 15, tree: null, docked: false }, loop: 'snap', blocks: [],
    stampCorner: 'br', captionText: '', shrink: 0,
  };
  let frame = 0; const commits = [];
  const o = createOps({
    state: s, changed() {}, commit: (l) => commits.push(l),
    mediaById: (id) => s.media.find((m) => m.id === id) || null,
    getFrame: () => frame, setFrame: (i) => { frame = i; },
  });
  return { o, s, commits };
}

test('roll applies the patch as one commit and hands back the code', () => {
  const { o, s, commits } = ops();
  const code = o.roll(codeToSeed('CCP-7K2M'));
  assert.equal(code, 'CCP-7K2M'); assert.equal(seedToCode(s.seed), code);
  assert.deepEqual(commits, ['roll']);
  assert.ok(s.blocks.length >= 1);
  assert.deepEqual(strip({ blocks: s.blocks }), strip({ blocks: autoCompose(state(3), codeToSeed('CCP-7K2M')).blocks }));
  assert.equal(o.roll('CCP-7K2M'), 'CCP-7K2M', 'a code string works too');
});

test('a roll keeps the caption and replaces everything else', () => {
  const { o, s } = ops();
  const cap = o.addBlock({ effect: 'caption', mode: 'text', target: 'canvas', start: 0, end: 75, params: { text: 'obey' } });
  o.addBlock({ effect: 'tint', mode: 'wash', target: 'canvas', start: 0, end: 75 });
  o.roll(5);
  assert.ok(s.blocks.some((b) => b.id === cap.id && b.params.text === 'obey'));
  assert.equal(s.blocks.filter((b) => b.effect === 'caption').length, 1);
  assert.ok(!s.blocks.some((b) => b.effect === 'tint' && b.mode === 'wash' && b.start === 0 && b.end === 75));
});

test('history: back, forward, truncate on a new roll, cap', () => {
  const { o, s } = ops();
  assert.equal(o.canRollBack(), false); assert.equal(o.rollBack(), null);
  const a = o.roll(1), b = o.roll(2), c = o.roll(3);
  assert.equal(o.rollHistory().index, 2);
  assert.equal(o.rollBack(), b); assert.equal(seedToCode(s.seed), b);
  assert.equal(o.rollBack(), a); assert.equal(o.canRollBack(), false);
  assert.equal(o.rollForward(), b); assert.equal(o.rollForward(), c); assert.equal(o.rollForward(), null);
  o.rollBack(); o.rollBack();
  const d = o.roll(4);
  assert.deepEqual(o.rollHistory().entries.map((e) => e.code), [a, d]);
  assert.equal(o.canRollForward(), false);
  for (let i = 0; i < ROLL_CAP + 5; i++) o.roll(100 + i);
  assert.equal(o.rollHistory().entries.length, ROLL_CAP);
  assert.equal(o.rollHistory().index, ROLL_CAP - 1);
});

test('roll with replace swaps the current entry instead of adding one', () => {
  const { o } = ops();
  const a = o.roll(1); o.roll(2);
  const b = o.roll(2, { replace: true });
  assert.deepEqual(o.rollHistory().entries.map((e) => e.code), [a, b]);
  o.rollBack(); o.roll(9, { replace: true });
  assert.equal(o.rollHistory().entries.length, 1);
});

test('going back restores that roll exactly', () => {
  const { o, s } = ops();
  o.roll(11); const first = JSON.stringify(strip({ blocks: s.blocks })) + s.layout.mode + s.loop;
  o.roll(12); o.rollBack();
  assert.equal(JSON.stringify(strip({ blocks: s.blocks })) + s.layout.mode + s.loop, first);
});
