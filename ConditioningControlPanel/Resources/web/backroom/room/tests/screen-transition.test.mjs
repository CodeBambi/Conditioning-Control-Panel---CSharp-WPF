import {test} from 'node:test';
import assert from 'node:assert/strict';
import {screenTransition} from '../screen-transition.js';
test('picture switches beneath static and settles cleanly on next cycle',()=>{
  assert.equal(screenTransition(8.4).glitch,0);
  const before=screenTransition(8.7),after=screenTransition(8.8);
  assert.equal(before.mix,0);assert.equal(after.mix,1);
  assert.ok(before.glitch>.8 && after.glitch>.8);
  assert.deepEqual(screenTransition(9),{index:1,mix:0,glitch:0,frame:162});
});
test('still and single-picture feeds never glitch',()=>{
  for(const t of [0,8.7,8.8,90,999]){
    const held=screenTransition(t,9,true),moving=screenTransition(t);
    assert.equal(held.glitch,0);assert.equal(held.frame,0);
    assert.equal(held.index,moving.index);assert.equal(held.mix,moving.mix);
    assert.equal(screenTransition(t,9,false,1).glitch,0);
    assert.equal(screenTransition(t,9,false,1).mix,0);
  }
});
test('wall banks stagger and short projector cycles stay bounded',()=>{
  assert.notEqual(screenTransition(8.8,9,false,8,0).glitch,screenTransition(8.8,9,false,8,1).glitch);
  for(let t=0;t<20;t+=.013){
    const s=screenTransition(t,4.5,false,8,3);
    assert.ok(s.glitch>=0 && s.glitch<=1);
    assert.ok(s.mix===0 || s.mix===1);
  }
});
