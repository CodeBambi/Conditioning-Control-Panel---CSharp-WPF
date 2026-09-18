import {test} from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../../../vendor/three/three.module.min.js';
import {createSlotEmiIdle} from '../slot-emi-idle.js';
function fixture(){const holder=new T.Group(),root=new T.Group();root.name='emi_topper';root.rotation.y=.3;holder.add(root);for(const name of ['shoulderL','shoulderR']){const node=new T.Group();node.name=name;node.rotation.z=.2;root.add(node);}let clicks=0;const actor=createSlotEmiIdle({fixture:holder,onClick:()=>clicks++});return {holder,root,actor,clicks:()=>clicks};}
test('walking slot visibly idles and click raises an authored shoulder',()=>{
 const {actor,root}=fixture();const rest=root.rotation.toArray();
 for(let i=0;i<60;i++)actor.update(1/60);
 assert.notDeepEqual(root.rotation.toArray(),rest);
 actor.trigger('greet');for(let i=0;i<60;i++)actor.update(1/60);
 assert.ok(root.getObjectByName('shoulderR').rotation.z>2);
 actor.update(.01,true);assert.deepEqual(root.rotation.toArray(),rest);
 assert.equal(root.getObjectByName('shoulderR').rotation.z,.2);actor.dispose();
});
test('seated slot owns transforms and clicks; return to walking begins at rest',()=>{
 const {actor,root,holder,clicks}=fixture();actor.trigger('greet');actor.update(.05);
 actor.settle();holder.userData.slotPlaying=true;root.rotation.z=1.5;
 actor.update(.05);actor.update(.05,true);assert.equal(root.rotation.z,1.5);
 actor.trigger();assert.equal(clicks(),1);assert.equal(root.rotation.z,1.5);
 root.rotation.z=0;holder.userData.slotPlaying=false;actor.update(.05);
 assert.ok(Math.abs(root.rotation.z)<.1);actor.dispose();assert.equal(root.rotation.z,0);
});
