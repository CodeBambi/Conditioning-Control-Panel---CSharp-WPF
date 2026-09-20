import test from 'node:test';
import assert from 'node:assert/strict';
import { chipLanding, traceProgress } from '../juice.js';
test('a chip lands at 120ms, has a bounded rebound and settles fully', () => {
  assert.equal(chipLanding(0).lift,2);
  assert.equal(chipLanding(120).lift,0);
  assert.ok(chipLanding(195).lift>0);
  for(let t=0;t<600;t++){const p=chipLanding(t);assert.ok(p.lift>=0&&p.lift<=2);assert.ok(Math.abs(p.tilt)<=.18);}
  assert.deepEqual(chipLanding(420),{lift:0,tilt:0});
});
test('still chips and suspended trace remain at rest, with no replay tail', () => {
  for(const t of [-1,0,70,300,650,1000])assert.deepEqual(chipLanding(t,true),{lift:0,tilt:0});
  assert.equal(traceProgress(200,true),null);
  assert.equal(traceProgress(-1),null);assert.equal(traceProgress(650),null);
  assert.equal(traceProgress(325),.5);
});
