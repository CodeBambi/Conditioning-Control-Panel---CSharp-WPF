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

test('every idle station gestures within four seconds and stays active regularly',()=>{
 for(const id of ['counter','wheel','cards','roulette']) {
  let active=0,early=false;
  for(let t=0;t<60;t+=.1){const g=sampleEmiGesture(id,t);const moves=Math.abs(g.left)+Math.abs(g.right)+Math.abs(g.yaw)> .05; if(moves){active++;early ||= t<4;}}
  assert.ok(early,id);assert.ok(active>130,id+' active frames '+active);
 }
});
test('new result poses are distinct and all animated channels settle',()=>{
 const kinds=['cheer','curious','shrug','anticipation'];
 assert.equal(new Set(kinds.map(k=>JSON.stringify(sampleEmiReaction(k,1)))).size,kinds.length);
 for(const kind of kinds)for(const t of [0,EMI_REACTIONS[kind]]) {
  const pose=sampleEmiReaction(kind,t);for(const [key,value] of Object.entries(pose))if(typeof value==='number')assert.equal(value,0,kind+' '+key);
 }
});
