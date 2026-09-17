import test from 'node:test';
import assert from 'node:assert/strict';
import { celebrationCounts, echoReels, ECHO_MS, ECHO_GAP_MS, echoDuration, echoSample, rollEcho } from '../celebration.js';
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
  assert.equal(echoDuration(0), 0);
  assert.equal(echoDuration(3), ECHO_MS + 2 * ECHO_GAP_MS);
  assert.equal(ECHO_GAP_MS, 300);
  assert.equal(echoSample(-.1), 0);
  assert.equal(echoSample(0), 0);
  assert.equal(echoSample(1), 0);
  assert.ok(echoSample(.8) < echoSample(.4));
  for (let i=0;i<=100;i++) assert.ok(echoSample(i/100) <= .5);
  assert.equal(rollEcho(() => 0), true);
  assert.equal(rollEcho(() => 1/15), false);
  assert.equal(Array.from({length:1500}, (_,i)=>rollEcho(()=>i/1500)).filter(Boolean).length,100);
});
