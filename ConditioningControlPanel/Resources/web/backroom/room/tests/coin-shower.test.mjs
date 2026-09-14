import {test} from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../../../vendor/three/three.module.min.js';
import {createCoinShower} from '../coin-shower.js';
test('only paid revealed results create coins, scaled and capped by tier',()=>{
 const root=new T.Group(),p=createCoinShower(root);
 for(const amount of [0,-1,NaN,Infinity])assert.equal(p.start(amount,4),false);
 const counts=[];for(let tier=1;tier<=4;tier++){p.start(10,tier);counts.push(p.debug().count);}
 assert.deepEqual(counts,[7,16,32,64]);p.dispose();assert.equal(root.children.length,0);
});
test('coins stay inside tray, settle above its base, and clear with motion off or a new spin',()=>{
 const root=new T.Group(),p=createCoinShower(root);p.start(400,4);const mesh=root.children[0],m=new T.Matrix4(),pos=new T.Vector3();
 for(let f=0;f<220;f++){p.update(1/60);for(let i=0;i<mesh.count;i++){mesh.getMatrixAt(i,m);pos.setFromMatrixPosition(m);assert.ok(Math.abs(pos.x)<.24&&pos.z>.29&&pos.z<.61&&pos.y>=.275);}}
 p.start(2,1);p.update(.02,true);assert.equal(mesh.count,0);assert.equal(p.debug().active,true);
 p.clear();assert.equal(p.debug().active,false);assert.equal(mesh.count,0);p.dispose();
});
