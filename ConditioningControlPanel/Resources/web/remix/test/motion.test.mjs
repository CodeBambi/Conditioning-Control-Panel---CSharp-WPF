import test from 'node:test';
import assert from 'node:assert/strict';
import { MOTION_LAYOUTS, motionLayout, motionCfg, slotsFrom, LAYOUTS, layoutAtFrame } from '../engine/layout.js';
import { AUTO_WEIGHTS, autoCompose } from '../engine/auto.js';
import { Renderer } from '../engine/render.js';

const ORIENTATIONS = ['landscape', 'portrait'];
const LENGTHS = [45, 75, 120];
const SEEDS = [0x51ee, 0xb00b, 12345];

/**
 * What a viewer would see, as a comparable string per rect: invisible and off
 * canvas rects dropped, anything hidden behind a full frame cover dropped,
 * then sorted by draw order. Ported from remix/docs/check.js, which is what
 * the pitch was validated with. `ph` is compared as the offset the layout
 * asked for: turning it into a source frame is the clock's job, not this.
 */
function sig(rects) {
  let list = rects.map((r, i) => ({ r, i }))
    .filter((o) => o.r.a === undefined || o.r.a > 0.0005)
    .filter((o) => !(o.r.x + o.r.w <= 0 || o.r.x >= 1 || o.r.y + o.r.h <= 0 || o.r.y >= 1));
  list.sort((p, q) => ((p.r.z || 0) - (q.r.z || 0)) || (p.i - q.i));
  // everything under an opaque, untilted, full frame rect is invisible
  let cut = -1;
  list.forEach((o, k) => {
    const r = o.r;
    if (!r.rot && (r.a === undefined || r.a >= 0.9995) && !r.sc
      && r.x <= 1e-6 && r.y <= 1e-6 && r.x + r.w >= 1 - 1e-6 && r.y + r.h >= 1 - 1e-6) cut = k;
  });
  if (cut > 0) list = list.slice(cut);
  const rows = list.map((o) => {
    const r = o.r;
    const ph = r.ph || 0;
    const cr = r.crop ? [r.crop.cx, r.crop.cy, r.crop.z] : [0.5, 0.5, 1];
    let rot = ((((r.rot || 0) % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI));
    if (rot > 2 * Math.PI - 1e-6) rot = 0;
    return {
      z: r.z || 0,
      s: [r.g, r.x, r.y, r.w, r.h, rot, r.sc || 1, ph, cr[0], cr[1], cr[2]]
        .map((v) => +v.toFixed(5)).join(','),
    };
  });
  // rects sharing a z are drawn in list order; compare them as a set
  rows.sort((p, q) => (p.z - q.z) || (p.s < q.s ? -1 : p.s > q.s ? 1 : 0));
  return rows.map((o) => o.s).join('|');
}

function cfgFor(n, frames, orientation, seed) {
  return motionCfg({ n, frames, stageFrames: 9, seed, orientation });
}

/* --------------------------------------------------------------- sweep ---*/

for (const lay of MOTION_LAYOUTS) {
  test(`${lay.id} draws a sane frame everywhere and closes its loop`, () => {
    for (const seed of SEEDS) {
      for (const orientation of ORIENTATIONS) {
        for (const frames of LENGTHS) {
          for (let n = 2; n <= 8; n++) {
            const cfg = cfgFor(n, frames, orientation, seed);
            const where = `${lay.id} n=${n} ${orientation} F=${frames} seed=${seed}`;
            const seen = new Set();
            let first = null;
            for (let f = 0; f <= frames; f++) {
              const rects = lay.rectsAt(cfg, f);
              assert.ok(Array.isArray(rects) && rects.length, `${where} f=${f} empty`);
              let onCanvas = 0;
              for (const r of rects) {
                for (const k of ['x', 'y', 'w', 'h']) {
                  assert.ok(Number.isFinite(r[k]), `${where} f=${f} ${k} is ${r[k]}`);
                }
                assert.ok(r.w > 0 && r.h > 0, `${where} f=${f} size ${r.w}x${r.h}`);
                assert.ok(Number.isInteger(r.g) && r.g >= 0 && r.g < n, `${where} f=${f} gif ${r.g}`);
                if (r.a != null) assert.ok(r.a >= 0 && r.a <= 1, `${where} f=${f} alpha ${r.a}`);
                seen.add(r.g);
                const vis = !(r.x + r.w <= 0 || r.x >= 1 || r.y + r.h <= 0 || r.y >= 1)
                  && (r.a === undefined || r.a > 0.0005);
                if (vis) onCanvas++;
              }
              assert.ok(onCanvas > 0, `${where} f=${f} nothing on the canvas`);
              if (f === 0) first = sig(rects);
              if (f === frames) assert.equal(sig(rects), first, `${where} loop pops`);
            }
            // a layout with a tile cap shows that many; tunnel runs out of rings past three
            const want = Math.min(n, lay.maxTiles);
            assert.equal(seen.size, want, `${where} only ${seen.size} of ${want} gifs ever show`);
          }
        }
      }
    }
  });

  test(`${lay.id} is deterministic from the seed`, () => {
    const a = cfgFor(5, 75, 'landscape', 99);
    const b = cfgFor(5, 75, 'landscape', 99);
    const c = cfgFor(5, 75, 'landscape', 100);
    assert.deepEqual(lay.rectsAt(a, 33), lay.rectsAt(b, 33));
    if (lay.id !== 'spread') { // spread takes nothing from the seed
      assert.notDeepEqual(lay.rectsAt(a, 33), lay.rectsAt(c, 33));
    }
  });
}

/* ------------------------------------------------------------ registry ---*/

test('every motion layout keeps the registry contract', () => {
  assert.deepEqual(MOTION_LAYOUTS.map((l) => l.id), ['stack', 'spread', 'slide', 'ripple', 'swallow', 'cover', 'tunnel', 'deal', 'carousel', 'hop', 'kenburns', 'pinwheel']);
  for (const l of MOTION_LAYOUTS) {
    assert.equal(typeof l.name, 'string');
    assert.ok(l.minTiles >= 1 && l.maxTiles <= 8 && l.minTiles <= l.maxTiles, l.id);
    assert.ok(['cheap', 'mid', 'dear'].includes(l.cost), `${l.id} cost ${l.cost}`);
    assert.equal(typeof l.rectsAt, 'function');
    assert.equal(motionLayout(l.id), l);
  }
  assert.equal(motionLayout('grow'), null);
  // and they are in LAYOUTS, so the picker and the roll see them
  const ids = LAYOUTS.map((l) => l.id);
  for (const l of MOTION_LAYOUTS) assert.ok(ids.includes(l.id), l.id);
  for (const l of LAYOUTS) assert.ok(['cheap', 'mid', 'dear'].includes(l.cost), l.id);
});

test('motionCfg fills in the shape a layout runs on', () => {
  const c = motionCfg({ n: 3, frames: 75, stageFrames: 9, seed: 7, orientation: 'square' });
  assert.deepEqual(c, { n: 3, frames: 75, stageFrames: 9, seed: 7, orientation: 'landscape', w: 480, h: 270 });
  assert.equal(motionCfg({ n: 1, frames: 1, stageFrames: 1, seed: 0, orientation: 'portrait' }).w, 270);
});

test('slotsFrom spells the short pitch names out', () => {
  const [slot] = slotsFrom([{ g: 2, x: 0.1, y: 0.2, w: 0.3, h: 0.4, a: 0.5, sc: 1.5, rot: 0.2, z: 3, ph: 4, gap: false, plate: true }], 4);
  assert.equal(slot.tileIndex, 2);
  assert.deepEqual(slot.rect, { x: 0.1, y: 0.2, w: 0.3, h: 0.4 });
  assert.equal(slot.alpha, 0.5);
  assert.equal(slot.scale, 1.5);
  assert.equal(slot.rot, 0.2);
  assert.equal(slot.z, 3);
  assert.equal(slot.frameOffset, 4);
  assert.equal(slot.gap, false);
  assert.equal(slot.plate, true);
  // a plain rect stays plain, so the old four are untouched
  const [plain] = slotsFrom([{ g: 0, x: 0, y: 0, w: 1, h: 1 }], 1);
  assert.deepEqual(plain, { tileIndex: 0, rect: { x: 0, y: 0, w: 1, h: 1 }, frameOffset: 0 });
  // an out of range gif index is clamped, never dropped on the floor
  assert.equal(slotsFrom([{ g: 9, x: 0, y: 0, w: 1, h: 1 }], 3)[0].tileIndex, 2);
});

test('layoutAtFrame serves the motion layouts and leaves the old four alone', () => {
  const cfg = { tileCount: 4, mode: 'stack', stageMs: 600, fps: 15, frames: 75, orientation: 'landscape', seed: 7 };
  const lay = layoutAtFrame(cfg, 30);
  assert.ok(lay.slots.length >= 4);
  assert.ok(lay.slots.every((s) => s.tileIndex >= 0 && s.tileIndex < 4));
  assert.ok(lay.slots.some((s) => s.rot));
  // the frame past the end wraps to the head rather than running off it
  assert.deepEqual(layoutAtFrame(cfg, 75).slots, layoutAtFrame(cfg, 0).slots);
  // grow still hands back bare slots
  const grow = layoutAtFrame(Object.assign({}, cfg, { mode: 'grow' }), 30).slots[0];
  assert.deepEqual(Object.keys(grow).sort(), ['frameOffset', 'rect', 'tileIndex']);
});

/* ------------------------------------------------------------- the roll --*/

test('the roll reaches every motion layout, and not with one gif', () => {
  const tiles = (k) => Array.from({ length: k }, (_, i) => ({ id: 't' + i }));
  const modes = (k, extra) => {
    const seen = new Set();
    for (let s = 0; s < 300; s++) {
      seen.add(autoCompose(Object.assign({ frames: 75, fps: 15, tiles: tiles(k), orientation: 'landscape', media: [] }, extra), s).layout.mode);
    }
    return seen;
  };
  const four = modes(4);
  for (const l of MOTION_LAYOUTS) {
    if (l.maxTiles >= 4) assert.ok(four.has(l.id), `4 gifs never rolled ${l.id}`);
  }
  const three = modes(3);
  for (const l of MOTION_LAYOUTS) assert.ok(three.has(l.id), `3 gifs never rolled ${l.id}`);
  const one = modes(1);
  for (const l of MOTION_LAYOUTS) {
    // one gif is mirror country: only a layout that says minTiles 1 belongs there
    if (l.minTiles > 1) assert.ok(!one.has(l.id), `1 gif rolled ${l.id}`);
    else assert.ok(one.has(l.id), `1 gif never rolled ${l.id}`);
  }
});

test('Discord-safe halves the weight of a dear layout', () => {
  const state = { frames: 75, fps: 15, tiles: [1, 2, 3, 4].map((i) => ({ id: 't' + i })), orientation: 'landscape', media: [] };
  const count = (extra) => {
    let hits = 0;
    for (let s = 0; s < 600; s++) if (autoCompose(Object.assign({}, state, extra), s).layout.mode === 'slide') hits++;
    return hits;
  };
  const open = count({});
  const safe = count({ discordSafe: true });
  assert.equal(AUTO_WEIGHTS.dearPenalty, 0.5);
  assert.ok(safe < open * 0.85, `slide rolled ${safe} times safe against ${open} open`);
  assert.ok(safe > 0, 'a dear layout still rolls sometimes');
});

/* ----------------------------------------------------------- the render -*/

/** A canvas context that records the transforms and draws it was asked for. */
function stubCtx() {
  const calls = [];
  const c = {
    calls, canvas: { width: 480, height: 270 },
    globalAlpha: 1, globalCompositeOperation: 'source-over', fillStyle: '', filter: 'none',
    save() { calls.push({ op: 'save' }); }, restore() { calls.push({ op: 'restore' }); },
    beginPath() {}, rect() {}, clip() {}, setTransform() {}, clearRect() {},
    translate(x, y) { calls.push({ op: 'translate', x, y }); },
    rotate(a) { calls.push({ op: 'rotate', a }); },
    scale() {}, textAlign: '', textBaseline: '', font: '', fillText() {},
    drawImage(img, x, y, w, h) { calls.push({ op: 'drawImage', alpha: c.globalAlpha, x, y, w, h }); },
    fillRect(x, y, w, h) { calls.push({ op: 'fillRect', alpha: c.globalAlpha, x, y, w, h }); },
  };
  return c;
}

function renderState(mode, n) {
  const strip = Array.from({ length: 4 }, (_, i) => ({ width: 100, height: 75, i }));
  const media = new Map();
  const tiles = [];
  for (let i = 0; i < n; i++) {
    media.set('m' + i, { id: 'm' + i, w: 100, h: 75, strip });
    tiles.push({ id: 't' + i, mediaId: 'm' + i, playMode: 'forward', enterFrame: 0 });
  }
  return {
    size: { w: 480, h: 270 }, frames: 75, fps: 15, seed: 4242, code: 'CCP-TEST',
    tiles, media, layout: { mode, stageMs: 600, tree: null }, loop: 'clean',
    blocks: [], orientation: 'landscape', showStamp: false, captionText: '',
  };
}

test('a tilted, faded, scaled slot draws without throwing', () => {
  const r = new Renderer();
  for (const mode of ['stack', 'spread', 'slide', 'ripple', 'swallow', 'cover', 'tunnel', 'deal', 'carousel', 'hop', 'kenburns', 'pinwheel']) {
    const state = renderState(mode, 5);
    for (const f of [0, 1, 3, 20, 40, 74]) {
      const ctx = stubCtx();
      r.render(ctx, f, state);
      assert.ok(ctx.calls.some((c) => c.op === 'drawImage'), `${mode} f=${f} drew nothing`);
    }
  }
  // stack tilts its cards and fades the one that is landing
  const ctx = stubCtx();
  r.render(ctx, 1, renderState('stack', 4));
  assert.ok(ctx.calls.some((c) => c.op === 'rotate' && c.a !== 0), 'no tilt');
  assert.ok(ctx.calls.some((c) => c.op === 'drawImage' && c.alpha > 0 && c.alpha < 1), 'no fade');
});

test('the old four still draw the plain cover fitted tile', () => {
  const r = new Renderer();
  const ctx = stubCtx();
  r.render(ctx, 40, renderState('flat', 4));
  const draws = ctx.calls.filter((c) => c.op === 'drawImage');
  assert.equal(draws.length, 4);
  assert.ok(draws.every((c) => c.alpha === 1), 'no stray alpha');
  assert.ok(!ctx.calls.some((c) => c.op === 'rotate'), 'no stray rotate');
});

test('a sparse layout paints a full canvas backdrop under its tiles, a grid does not', () => {
  const r = new Renderer();
  const full = (c) => c.op === 'drawImage' && c.x <= 0 && c.y <= 0 && c.w >= 480 && c.h >= 270;
  for (const mode of ['stack', 'deal', 'carousel', 'pinwheel']) {
    const ctx = stubCtx();
    r.render(ctx, 20, renderState(mode, 3));
    const draws = ctx.calls.filter((c) => c.op === 'drawImage');
    assert.ok(full(draws[0]), `${mode}: first draw is not the backdrop`);
    assert.ok(draws[0].alpha < 1, `${mode}: backdrop drawn at full strength`);
  }
  for (const mode of ['grow', 'flat', 'slide', 'ripple']) {
    const ctx = stubCtx();
    r.render(ctx, 20, renderState(mode, 3));
    assert.ok(!ctx.calls.filter((c) => c.op === 'drawImage').some(full), `${mode}: drew a backdrop`);
  }
});
