import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { reelCellVisible, reelPaintStamp } from '../reel-paint.js';

test('visible window wraps correctly at either end, including recoil neighbours', () => {
  const angle = k => ((k + .5) / 13 - .5) * Math.PI * 2;
  for (const stop of [0, 6, 12]) {
    const visible = Array.from({length:13}, (_,i)=>i).filter(i=>reelCellVisible(i,13,angle(stop)));
    assert.equal(visible.length,5);
    for (const delta of [-2,-1,0,1,2]) assert.ok(visible.includes((stop+delta+13)%13));
  }
  assert.ok(Array.from({length:13},(_,i)=>reelCellVisible(i,13,0,true)).every(Boolean));
});
test('static pixels stay cached and highlights return to their original stamp', () => {
  for (const kind of ['sub','melt','unknown']) assert.equal(reelPaintStamp(kind,0,false),reelPaintStamp(kind,300,false));
  assert.notEqual(reelPaintStamp('sub',0,false,.4),reelPaintStamp('sub',0,false));
  assert.equal(reelPaintStamp('gif',0,true),reelPaintStamp('gif',300,true));
  assert.notEqual(reelPaintStamp('gif',0,false),reelPaintStamp('gif',300,false));
});
function harness(kind) {
  const source=readFileSync(new URL('../scene.js',import.meta.url),'utf8');
  const frame=source.slice(source.indexOf('  function paint(t, force = false) {'),source.indexOf('  function setStrips(next)'));
  let draws=0, uploads=0, patched=0, glow=0;
  const ctx=new Proxy({}, {get:(_,name)=> name==='createLinearGradient'?()=>({addColorStop(){}}):()=>{}});
  const context=vm.createContext({
    strips:Array.from({length:3},()=>Array(13).fill(kind)),
    reelCanvas:Array.from({length:3},()=>({getContext:()=>ctx})),
    reelTex:Array.from({length:3},()=>({set needsUpdate(value){if(value)uploads++;}})),
    stopsNow:[6,6,6], hitGlow:()=>glow, ghost:null, kindOf:()=>({kind}),
    reelPaintStamp,reelCellVisible,reelAngles:[0,0,0],spin:null,paintedCells:[[],[],[]],
    reduced:false,stillFx:()=>false,reelMood:'idle',reelMoodAt:0,look:{},cell:{hw:112,hh:128},CW:256,CH:224,
    drawSymbol:()=>draws++,lastPaint:0,
    // The painter's in-place upload (scene.js uploadCells): a few changed cells are patched one by one, more
    // than six go up as the whole strip. Mirrors the real threshold so these counts mean what the GPU sees.
    uploadCells:(r,cells)=>{ if(cells.length>6)uploads++; else patched+=cells.length; },
  });
  vm.runInContext(frame,context);
  return {paint:(t,force=false)=>vm.runInContext(`paint(${t},${force})`,context),
    clear(){draws=uploads=patched=0;},counts:()=>({draws,uploads,patched}),glow(value){glow=value;},spin(value){context.spin=value;}};
}
test('actual painter skips static cell drawing and texture uploads between highlights', () => {
  const h=harness('sub');h.paint(0,true);assert.deepEqual(h.counts(),{draws:39,uploads:3,patched:0});
  h.clear();h.paint(200);assert.deepEqual(h.counts(),{draws:0,uploads:0,patched:0});
  h.glow(.5);h.paint(210);assert.deepEqual(h.counts(),{draws:3,uploads:0,patched:3});
  h.clear();h.glow(0);h.paint(220);assert.deepEqual(h.counts(),{draws:3,uploads:0,patched:3});
});
test('actual painter refreshes nearby animated cells at rest, all cells while spinning', () => {
  const h=harness('gif');h.paint(0,true);h.clear();h.paint(200);
  // Five live cells a reel at rest: patched in place, never the whole 13-cell strip.
  assert.deepEqual(h.counts(),{draws:15,uploads:0,patched:15});
  h.clear();h.paint(220);assert.deepEqual(h.counts(),{draws:0,uploads:0,patched:0});
  h.spin({});h.paint(300);assert.deepEqual(h.counts(),{draws:39,uploads:3,patched:0});
});

test('entry leaves the borrowed reel art alone until strips arrive', () => {
  const source = readFileSync(new URL('../scene.js', import.meta.url), 'utf8');
  const entry = source.slice(source.indexOf('  function setStrips(next)'), source.indexOf('  // Keep the final dealt picture'));
  let swaps = 0;
  vm.runInNewContext(entry, { setStrips: () => swaps++ });
  assert.equal(swaps, 0);
});

test('shared slot follows delayed viewport and stretch changes without browser events', () => {
  const source = readFileSync(new URL('../scene.js', import.meta.url), 'utf8');
  const registration = source.slice(source.indexOf('  if (shared) releaseView = shared.register('), source.indexOf('  else raf = requestAnimationFrame(loop);'));
  let view, resizes = 0, updates = 0;
  const canvas = { clientWidth: 390, clientHeight: 844 };
  const fixture = { userData: { slotStretch: 3, slotStretchX: 1 } };
  const shared = { ready: true, fixture, register(v) { view = v; } };
  vm.runInNewContext(registration, { shared, canvas, sharedSize: '', performance: { now: () => 0 },
    resize: () => resizes++, update: () => updates++, look: {}, frameFailed: false, console });
  view.update(); view.update();
  assert.equal(resizes, 1);
  canvas.clientWidth = 844; canvas.clientHeight = 390;
  fixture.userData.slotStretch = 1; fixture.userData.slotStretchX = 1.6;
  view.update();
  assert.equal(resizes, 2);
  fixture.userData.slotStretchX = 1.7;
  view.update(); view.update();
  assert.equal(resizes, 3);
  assert.equal(updates, 5);
});
