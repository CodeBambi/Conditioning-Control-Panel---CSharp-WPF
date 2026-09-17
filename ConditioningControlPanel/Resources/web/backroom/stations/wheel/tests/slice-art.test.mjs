import test from 'node:test';
import assert from 'node:assert/strict';
import { drawFace, wedgePath, faceKey, faceKeys, sliceBounds, FACE_MS, R0, R1 } from '../slice-art.js';

/** A recording 2D context: every call in order, so the ORDER of clip, picture, tint and scrim can be asserted. */
function recorder() {
  const calls = [];
  const put = (name) => (...a) => { calls.push({ name, a }); };
  const ctx = {
    calls, canvas: null,
    globalAlpha: 1, globalCompositeOperation: 'source-over', fillStyle: null,
    save: put('save'), restore: put('restore'), clip: put('clip'), fill: put('fill'),
    beginPath: put('beginPath'), closePath: put('closePath'), arc: put('arc'),
    clearRect: put('clearRect'), rect: put('rect'), drawImage: put('drawImage'),
    createRadialGradient: (...a) => { const g = { stops: [], addColorStop(o, c) { this.stops.push([o, c]); } }; calls.push({ name: 'gradient', a, g }); return g; },
    createLinearGradient: (...a) => { const g = { stops: [], addColorStop(o, c) { this.stops.push([o, c]); } }; calls.push({ name: 'sheen', a, g }); return g; },
  };
  return ctx;
}
const canvasWith = (ctx, size = 512) => { const c = { width: size, height: size, getContext: () => ctx }; ctx.canvas = c; return c; };

const slice = (i, n, id) => ({ index: i, id: id || `s${i}`, start: (i * 2 * Math.PI) / n, end: ((i + 1) * 2 * Math.PI) / n,
  mid: ((i + 0.5) * 2 * Math.PI) / n, span: (2 * Math.PI) / n });
const LAYOUT = Array.from({ length: 4 }, (_, i) => slice(i, 4));
const COLOR = () => '#963A80';
// Stands in for a shared/hypno/media.js deck: draw() begins its OWN path and clips, exactly as the real one does,
// so the test sees the same path-destroying behaviour the module has to survive.
const deckStub = (drew = true) => ({
  drawn: 0,
  draw(ctx2d, key, x, y, w, h) {
    this.drawn++; this.last = [key, x, y, w, h];
    if (!drew) return false;
    ctx2d.save(); ctx2d.beginPath(); ctx2d.rect(x, y, w, h); ctx2d.clip();
    ctx2d.drawImage({}, x, y, w, h); ctx2d.restore();
    return true;
  },
  pickKey: (seed) => 'g' + seed.length,
});

test('the wedge clips to the ring band, and only to it', () => {
  const ctx = recorder();
  wedgePath(ctx, LAYOUT[0], 100, 100, 140);
  const arcs = ctx.calls.filter(c => c.name === 'arc');
  assert.equal(arcs.length, 2);
  assert.equal(arcs[0].a[2], R1 * 140, 'the outer arc is the ring outer radius');
  assert.equal(arcs[1].a[2], R0 * 140, 'the inner arc is the ring inner radius');
  assert.equal(arcs[0].a[5], false, 'the outer arc runs forwards');
  assert.equal(arcs[1].a[5], true, 'the inner arc comes back the other way, so the shape closes');
});

test('the wheel angle is the canvas angle turned a quarter (clockwise from the top)', () => {
  const ctx = recorder();
  wedgePath(ctx, { start: 0, end: Math.PI / 2, span: Math.PI / 2 }, 0, 0, 1);
  const [outer] = ctx.calls.filter(c => c.name === 'arc');
  assert.ok(Math.abs(outer.a[3] - (-Math.PI / 2)) < 0.01, 'a slice starting at the top starts at canvas -PI/2');
});

test('every slice is painted inside its own save/clip, and nothing leaks between them', () => {
  const ctx = recorder();
  const deck = deckStub();
  assert.equal(drawFace(canvasWith(ctx), LAYOUT, deck, 'g4', COLOR), true);
  // Two of each per wedge: the module's own, and the one the deck takes for its cover-fit rect.
  assert.equal(ctx.calls.filter(c => c.name === 'save').length, LAYOUT.length * 2);
  assert.equal(ctx.calls.filter(c => c.name === 'restore').length, LAYOUT.length * 2);
  assert.equal(ctx.calls.filter(c => c.name === 'clip').length, LAYOUT.length * 2);
  assert.equal(deck.drawn, LAYOUT.length, 'one picture draw per wedge');
  assert.equal(ctx.globalAlpha, 1, 'the alpha is put back');
  assert.equal(ctx.globalCompositeOperation, 'source-over', 'the composite mode is put back');
});

test('the enamel is under the picture and the scrim is over it, in one clip (Brake 9 over any frame)', () => {
  const ctx = recorder();
  drawFace(canvasWith(ctx), [LAYOUT[0]], deckStub(), 'g4', COLOR);
  const order = ctx.calls.map(c => c.name);
  const save = order.indexOf('save'), pic = order.indexOf('drawImage'), restore = order.lastIndexOf('restore');
  const fills = order.reduce((a, n, i2) => (n === 'fill' ? a.concat(i2) : a), []);
  assert.equal(fills.length, 3, 'the enamel floor, the scrim band and the sheen');
  assert.ok(save < fills[0] && fills[0] < pic, 'the enamel floor is laid inside the clip and under the picture');
  assert.ok(pic < fills[1] && fills[1] < fills[2], 'the scrim and the sheen go over the picture, never under it');
  assert.ok(fills[2] < restore, 'every pass is inside the wedge clip');
});

test('the scrim is feathered to nothing at the hub and the rim, so the art still reads as art', () => {
  const ctx = recorder();
  drawFace(canvasWith(ctx), [LAYOUT[0]], deckStub(), 'g4', COLOR);
  const g = ctx.calls.find(c => c.name === 'gradient');
  assert.ok(g, 'a radial band is built');
  const inner = g.a[2], outer = g.a[5], k = 256 / R1;
  assert.ok(inner > R0 * k, 'the band starts outside the hub, so the art reads there');
  assert.ok(outer > inner && outer > R1 * k * 0.9, 'and reaches the rim');
  // The feather is the whole guarantee: transparent at both ends, opaque across the label's own radius.
  assert.match(g.g.stops[0][1], /, 0\)$/, 'transparent at the hub end');
  assert.match(g.g.stops.at(-1)[1], /, 0\)$/, 'transparent at the rim end');
  assert.ok(g.g.stops.slice(1, -1).every(([, c]) => /0\.5\)$/.test(c)), 'opaque where the label sits');
});

test('the wedge path is laid again before the fills, because draw() begins its own path', () => {
  const ctx = recorder();
  drawFace(canvasWith(ctx), [LAYOUT[0]], deckStub(), 'g4', COLOR);
  const order = ctx.calls.map(c => c.name);
  const pic = order.indexOf('drawImage');
  assert.ok(order.indexOf('arc', pic) > pic, 'the ring is re-pathed after the picture');
});

test('no media, or nothing decoded yet, leaves the enamel alone rather than flashing an empty ring', () => {
  const ctx = recorder();
  assert.equal(drawFace(canvasWith(ctx), LAYOUT, null, 'g4', COLOR), false);
  assert.equal(drawFace(canvasWith(ctx), LAYOUT, deckStub(), null, COLOR), false);
  assert.equal(drawFace(canvasWith(ctx), LAYOUT, deckStub(false), 'g4', COLOR), false, 'a deck with nothing to show reports false');
  assert.equal(drawFace(canvasWith(ctx), [], deckStub(), 'g4', COLOR), false);
});

test('the picture a layout wears is stable, and is its OWN seed, not the result key', () => {
  const deck = deckStub();
  const a = faceKey(deck, LAYOUT), b = faceKey(deck, LAYOUT);
  assert.equal(a, b, 'the same table wears the same picture');
  assert.equal(faceKey(deck, null), null);
  assert.equal(faceKey(null, LAYOUT), null);
});

test('the repaint clock is the decoder clock: 12 Hz, not the frame rate', () => {
  assert.equal(FACE_MS, 83, 'gif-decode.js MAX_FPS is 12, so 83 ms is one frame of the source');
});


test('each slice has its own stable picture and cover-fit bounds', () => {
  const deck = { ...deckStub(), keyAt: i => 'g' + i };
  const keys = faceKeys(deck, LAYOUT);
  assert.deepEqual(keys, ['g0','g1','g2','g3']);
  const calls = []; deck.draw = (_c, ...a) => { calls.push(a); return true; };
  drawFace(canvasWith(recorder()), LAYOUT, deck, keys, COLOR);
  assert.deepEqual(calls.map(c => c[0]), keys);
  for (const c of calls) { assert.ok(c[3] <= 257 && c[4] <= 257); }
});

test('slice crop contains the complete curved wedge, including cardinal extrema', () => {
  const s = {start: -.2, end: 2.5, span: 2.7};
  const b = sliceBounds(s, 256, 256, 256 / R1);
  for(let a=s.start;a<s.end;a+=.01) for(const r of [R0,R1]) {
    const x=256+Math.sin(a)*r*256/R1, y=256-Math.cos(a)*r*256/R1;
    assert.ok(x>=b.x-1e-6 && x<=b.x+b.w+1e-6 && y>=b.y-1e-6 && y<=b.y+b.h+1e-6);
  }
});


test('moving lacquer changes the face highlight without changing the dealt pictures', () => {
  const still = recorder(), moving = recorder(), a = deckStub(), b = deckStub();
  drawFace(canvasWith(still), LAYOUT, a, 'same-picture', COLOR, 0);
  drawFace(canvasWith(moving), LAYOUT, b, 'same-picture', COLOR, 1700);
  assert.notDeepEqual(still.calls.find(c => c.name === 'sheen').a, moving.calls.find(c => c.name === 'sheen').a);
  assert.deepEqual(a.last, b.last, 'the moving light never crops, deals or substitutes the result picture');
});
