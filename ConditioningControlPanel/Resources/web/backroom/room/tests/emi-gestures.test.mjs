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
// The idle rotation now plays the authored reactions (look, bow, present, wave), so the old
// "tiny sway" ceiling (yaw .085, pitch .05, roll .016) no longer describes it: `look` sweeps the
// head .34 rad and `wave` lifts a whole shoulder. These are the bounds idle really needs, with
// `present` kept in the rotation; anything wilder than a mascot pottering about still fails here.
// Click-driven reactions (sampleEmiReaction) stay free: only the unattended loop is bounded.
const IDLE_BOUNDS={yaw:.34,pitch:.16,roll:.045};
const PERIODS={counter:27,wheel:19,cards:23,roulette:25};
test('the unattended idle loop stays inside its bounds on every station',()=>{
 for(const [id,period] of Object.entries(PERIODS)){
  for(let t=0;t<=period*3;t+=1/60){
   const g=sampleEmiGesture(id,t),where=id+' at '+t.toFixed(3)+' s';
   for(const [key,bound] of Object.entries(IDLE_BOUNDS))assert.ok(Math.abs(g[key])<=bound+1e-9,where+' '+key+' '+g[key]);
   assert.equal(g.lift,0,where+' lift');
   // 3 is the rest face, so a routine naming it reads exactly like the null the others return.
   assert.ok(g.face===null||g.face===3,where+' face '+g.face);
  }
 }
});
