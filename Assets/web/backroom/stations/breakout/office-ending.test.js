import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createOfficeEnding} from './office-ending.js';
function harness(reduced=false){
 const nodes=[],classes=new Set();let focused=0;
 function make(tag){const events=new Map();const n={tag,hidden:false,readyState:0,plays:0,pauses:0,loads:0,
  append(){},remove(){this.removed=true;},removeAttribute(){},focus(){focused++;},
  play(){this.plays++;return Promise.resolve();},pause(){this.pauses++;},load(){this.loads++;},
  addEventListener(k,fn){events.set(k,fn);},removeEventListener(k){events.delete(k);},emit(k){events.get(k)?.();}};nodes.push(n);return n;}
 const actions={hidden:true,querySelector:()=>({focus(){focused++;}})};
 const root={append(){},classList:{remove(k){classes.delete(k);},toggle(k,on){on?classes.add(k):classes.delete(k);}}};
 const ending=createOfficeEnding(root,{reduced,actions,make});
 const finale={phase:'outro',outroAge:0};return {ending,actions,nodes,classes,finale,get focused(){return focused;}};
}
test('card precedes video, natural end shows still and controls; source is loaded once',()=>{
 const h=harness(),[layer,video,still,skip]=h.nodes;
 h.ending.update(h.finale);assert.equal(video.loads,1);assert.equal(layer.hidden,true);
 h.finale.outroAge=5;h.ending.update(h.finale);assert.equal(video.plays,0);assert.equal(h.actions.hidden,true);
 video.emit('canplay');h.finale.outroAge=7.4;h.ending.update(h.finale);assert.equal(video.plays,1);
 video.emit('playing');assert.equal(layer.hidden,false);assert.equal(h.ending.state,'playing');
 video.emit('ended');assert.equal(h.actions.hidden,false);assert.equal(still.hidden,false);assert.equal(skip.hidden,true);
 h.ending.update(h.finale);assert.equal(video.plays,1);
});
test('reduced motion and skip use final still without playing video',()=>{
 for(const reduced of [true,false]){const h=harness(reduced);h.ending.update(h.finale);
  if(reduced){h.finale.outroAge=8;h.ending.update(h.finale);}else h.ending.skip();
  assert.equal(h.nodes[1].plays,0);assert.equal(h.ending.state,'done');assert.equal(h.actions.hidden,false);}
});
test('missing media and rejected autoplay cannot trap player',async()=>{
 for(const reject of [true,false]){const h=harness(),video=h.nodes[1];h.ending.update(h.finale);
  if(reject){video.readyState=2;video.play=()=>Promise.reject(new Error('blocked'));h.finale.outroAge=8;}else h.finale.outroAge=11;
  h.ending.update(h.finale);await Promise.resolve();assert.equal(h.actions.hidden,false);
  h.nodes[2].emit('error');assert.equal(h.nodes[0].hidden,true);}
});
test('cleanup stops media and ignores late events; new finale resets lifecycle',()=>{
 const h=harness();h.ending.update(h.finale);h.ending.skip();h.ending.update(null);assert.equal(h.actions.hidden,true);
 const next={phase:'outro',outroAge:0};h.ending.update(next);assert.equal(h.nodes[1].loads,2);
 h.ending.dispose();assert.equal(h.nodes[0].removed,true);h.nodes[1].emit('ended');assert.equal(h.actions.hidden,true);
});
test('hidden station pauses a playing reveal and resumes only on return',()=>{
 const h=harness();h.ending.update(h.finale);h.nodes[1].readyState=2;h.finale.outroAge=8;h.ending.update(h.finale);h.nodes[1].emit('playing');
 const before=h.nodes[1].pauses;h.ending.suspend(true);assert.equal(h.nodes[1].pauses,before+1);h.ending.suspend(false);assert.equal(h.nodes[1].plays,2);
});
