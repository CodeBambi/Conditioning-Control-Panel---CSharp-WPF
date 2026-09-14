import {test} from 'node:test';
import assert from 'node:assert/strict';
import {EMI_REACTIONS,sampleEmiGesture,sampleEmiReaction} from '../emi-gestures.js';
test('reactions return smoothly to neutral at both ends',()=>{
 for(const [kind,duration] of Object.entries(EMI_REACTIONS)){
  for(const boundary of [0,duration]){
   const a=sampleEmiReaction(kind,boundary-.00001),b=sampleEmiReaction(kind,boundary+.00001);
   for(const key of ['yaw','pitch','roll','left','right','reach','sweep'])assert.ok(Math.abs(a[key]-b[key])<1e-7,kind+' '+key);
  }
 }
});
test('wave raises a real shoulder and dust raises before showing the brush',()=>{
 assert.ok(sampleEmiReaction('wave',1).right>1.5);
 const before=sampleEmiReaction('dust',.7),brush=sampleEmiReaction('dust',2.5);
 assert.ok(before.dust>0);assert.equal(before.tool,0);
 assert.equal(brush.dust,1);assert.equal(brush.tool,1);assert.notEqual(brush.brushX,sampleEmiReaction('dust',3).brushX);
});
test('station routines differ and invalid time never produces NaN',()=>{
 const signatures=['counter','wheel','cards','roulette'].map(id=>JSON.stringify(Array.from({length:150},(_,i)=>sampleEmiGesture(id,i))));
 assert.equal(new Set(signatures).size,4);
 for(const v of [-1,NaN,Infinity])assert.equal(sampleEmiGesture('counter',v).yaw,0);
});
