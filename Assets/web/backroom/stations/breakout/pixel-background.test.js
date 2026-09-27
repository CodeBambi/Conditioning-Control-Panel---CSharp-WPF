import { test } from 'node:test';
import assert from 'node:assert/strict';
import { backgroundMotion, createPixelWallpaper } from './pixel-background.js';
import { createLandscape } from './landscape.js';

function context(log = []) {
  return new Proxy({}, { get(target, key) {
    if (key in target) return target[key];
    if (key === 'createLinearGradient') return () => ({ addColorStop() {} });
    if (key === 'getTransform') return () => null;
    return (...args) => log.push([key, ...args]);
  } });
}
function withCanvas(run) {
  const previous = globalThis.document;
  let allocations = 0;
  globalThis.document = { createElement() { allocations++; return { getContext: () => context() }; } };
  try { run(() => allocations); } finally { globalThis.document = previous; }
}
test('motion waits five seconds, ramps gradually and uses the supplied beat phase', () => {
  assert.deepEqual(backgroundMotion(5, 0), { ramp: 0, pulse: 0 });
  assert.equal(backgroundMotion(14, 0).ramp, .5);
  assert.equal(backgroundMotion(23, 0).pulse, 1);
  assert.equal(backgroundMotion(23, 1).pulse, 0);
  assert.deepEqual(backgroundMotion(100, 0, true), { ramp: 0, pulse: 0 });
});
test('reduced motion stays identical over time and steady frames allocate no canvases', () => withCanvas(count => {
  const wallpaper = createPixelWallpaper(1280, 720);
  const first = [], second = [], allocated = count();
  wallpaper.draw(context(first), 2, 0, true);
  wallpaper.draw(context(second), 80, .5, true);
  assert.deepEqual(second, first);
  for (let i = 0; i < 120; i++) wallpaper.draw(context(), i / 60 + 10, (i % 30) / 30, false);
  assert.equal(count(), allocated);
}));
test('grey and colour glyphs drift independently without a shared beat bounce', () => withCanvas(() => {
  const wallpaper=createPixelWallpaper(1280,720);
  for(const mono of [true,false]){
    const a=[],b=[],same=[];
    wallpaper.symbols(context(a),mono,10,0,false);
    wallpaper.symbols(context(b),mono,11,0,false);
    wallpaper.symbols(context(same),mono,10,.8,false);
    assert.deepEqual(a,same,'beat must not move every glyph together');
    const ys=x=>x.filter(op=>op[0]==='drawImage').map(op=>op[3]);
    const first=ys(a),delta=ys(b).map((y,i)=>y-first[i]);
    assert.ok(delta.some(d=>d>0)&&delta.some(d=>d<0),'glyphs move in different directions');
  }
}));

import { mixedShardCount } from './landscape.js';
test('ending starts 95 percent grey and reveals colour monotonically', () => {
  assert.equal(mixedShardCount(0), 456);
  assert.equal(mixedShardCount(.5), 228);
  assert.equal(mixedShardCount(1), 0);
  assert.equal(mixedShardCount(NaN), 456);
  for (let i = 1; i <= 100; i++) assert.ok(mixedShardCount(i / 100) <= mixedShardCount((i - 1) / 100));
});
test('mixed ending cache changes with progress and reuses unchanged stages', () => withCanvas(count => {
  const landscape = createLandscape(1280, 720, () => .5, true);
  const log = [], g = context(log);
  g.getTransform = () => ({ a: 1, d: 1, b: 0, c: 0, e: 0, f: 0 });
  landscape.draw(g, false, .016, true, 1, 0);
  const first = log.findLast(op => op[0] === 'drawImage')[1], allocated = count();
  landscape.draw(g, false, .016, true, 1, .05);
  assert.equal(count(), allocated);
  assert.equal(log.findLast(op => op[0] === 'drawImage')[1], first);
  landscape.draw(g, false, .016, true, 1, .5);
  assert.notEqual(log.findLast(op => op[0] === 'drawImage')[1], first);
  const changed = count();
  landscape.draw(g, false, .016, true, 1, .55);
  assert.equal(count(), changed);
}));
