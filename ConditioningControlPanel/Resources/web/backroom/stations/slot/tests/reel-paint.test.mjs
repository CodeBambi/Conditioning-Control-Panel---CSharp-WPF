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
  let draws=0, uploads=0, glow=0;
  const ctx=new Proxy({}, {get:(_,name)=> name==='createLinearGradient'?()=>({addColorStop(){}}):()=>{}});
  const context=vm.createContext({
    strips:Array.from({length:3},()=>Array(13).fill(kind)),
    reelCanvas:Array.from({length:3},()=>({getContext:()=>ctx})),
    reelTex:Array.from({length:3},()=>({set needsUpdate(value){if(value)uploads++;}})),
    stopsNow:[6,6,6], hitGlow:()=>glow, ghost:null, kindOf:()=>({kind}),
    reelPaintStamp,reelCellVisible,reelAngles:[0,0,0],spin:null,paintedCells:[[],[],[]],
    reduced:false,stillFx:()=>false,reelMood:'idle',reelMoodAt:0,look:{},cell:{hw:112,hh:128},CW:256,CH:224,
    drawSymbol:()=>draws++,lastPaint:0,
  });
  vm.runInContext(frame,context);
  return {paint:(t,force=false)=>vm.runInContext(`paint(${t},${force})`,context),
    clear(){draws=uploads=0;},counts:()=>({draws,uploads}),glow(value){glow=value;},spin(value){context.spin=value;}};
}
test('actual painter skips static cell drawing and texture uploads between highlights', () => {
  const h=harness('sub');h.paint(0,true);assert.deepEqual(h.counts(),{draws:39,uploads:3});
  h.clear();h.paint(200);assert.deepEqual(h.counts(),{draws:0,uploads:0});
  h.glow(.5);h.paint(210);assert.deepEqual(h.counts(),{draws:3,uploads:3});
  h.clear();h.glow(0);h.paint(220);assert.deepEqual(h.counts(),{draws:3,uploads:3});
});
test('actual painter refreshes nearby animated cells at rest, all cells while spinning', () => {
  const h=harness('gif');h.paint(0,true);h.clear();h.paint(200);
  assert.deepEqual(h.counts(),{draws:15,uploads:3});
  h.clear();h.paint(220);assert.deepEqual(h.counts(),{draws:0,uploads:0});
  h.spin({});h.paint(300);assert.deepEqual(h.counts(),{draws:39,uploads:3});
});
