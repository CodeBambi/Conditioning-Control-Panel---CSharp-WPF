import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createSoftwareFields} from './software-fields.js';
test('software fields transfer once per recipe while rotation and blend stay live',()=>{
 let transfers=0,draws=0;const rotations=[];
 const kit={draw(){transfers++;return true;}};
 const cache=createSoftwareFields(()=>({getContext:()=>({})}));
 const ctx={globalAlpha:1,save(){},restore(){this.globalAlpha=1;},rotate(a){rotations.push(a);},drawImage(){draws++;}};
 for(let i=0;i<120;i++){cache.draw(kit,ctx,'a',150,i*.01);cache.draw(kit,ctx,'b',150,i*.01,i/120);}
 assert.equal(transfers,2);assert.equal(draws,240);assert.ok(rotations.at(-1)>1);
 cache.invalidate('b');cache.draw(kit,ctx,'b',150,2);assert.equal(transfers,3);
 cache.draw(kit,ctx,'a',180,3);assert.equal(transfers,3,'size changes reuse original field');
 cache.clear();cache.draw(kit,ctx,'a',180,3);assert.equal(transfers,4);
});
test('failed field paints retry instead of caching an invisible source',()=>{
 let calls=0;const cache=createSoftwareFields(()=>({getContext:()=>({})}));
 const kit={draw(){return ++calls>1;}};
 const ctx={globalAlpha:1,save(){},restore(){},rotate(){},drawImage(){}};
 assert.equal(cache.draw(kit,ctx,'a',100,0),false);
 assert.equal(cache.draw(kit,ctx,'a',100,0),true);assert.equal(calls,2);
});
