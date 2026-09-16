import test from 'node:test';
import assert from 'node:assert/strict';
import { startRecoil } from '../juice.js';
test('cabinet reacts against either starting direction and returns to exact rest', () => {
  assert.ok(startRecoil(100,1)<0);assert.ok(startRecoil(100,-1)>0);
  for(let t=0;t<=600;t++)assert.ok(Math.abs(startRecoil(t))<=.035);
  assert.equal(startRecoil(600),0);assert.equal(startRecoil(-1),0);
});
test('Calm and Off have no recoil at any point in the start', () => {
  for(let t=0;t<600;t++)assert.equal(startRecoil(t,1,true),0);
});
