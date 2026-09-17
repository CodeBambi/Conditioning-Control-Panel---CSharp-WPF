import test from 'node:test';
import assert from 'node:assert/strict';
import { celebrationCounts, echoReels, ECHO_MS } from '../celebration.js';
import { FLOW } from '../feel.js';
test('wins scale, losses and still mode do not shower',()=>{
  for(let tier=0;tier<=4;tier++) {
    assert.deepEqual(celebrationCounts(tier,0),{sparks:0,confetti:0});
    assert.deepEqual(celebrationCounts(tier,100,true),{sparks:0,confetti:0});
  }
  assert.equal(celebrationCounts(1,1).confetti,0);
  assert.ok(celebrationCounts(3,40).confetti>0);
  assert.ok(celebrationCounts(4,400).sparks>celebrationCounts(3,40).sparks);
  assert.ok(Object.values(celebrationCounts(4,400)).reduce((a,b)=>a+b)<=420);
});
test('echo comes only from symbols matching the effect, including a two-GIF tease',()=>{
  assert.deepEqual(echoReels(['gif0','spiral0','gif2'],[{id:'fx.gif_burst',args:{count:1}}]),[0,2]);
  assert.deepEqual(echoReels(['gif0','spiral0','spiral2'],[{id:'fx.spiral_full'}]),[1,2]);
  assert.deepEqual(echoReels(['gif0','spiral0','gif2'],[]),[]);
  assert.deepEqual(echoReels(['sub0','sub1','emi'],[{id:'fx.sub_pair'}]),[0,1]);
});

test('reel echoes finish before the host effects start',()=>{
  assert.ok(ECHO_MS > 0 && ECHO_MS <= FLOW.FX_DELAY_MS);
});
