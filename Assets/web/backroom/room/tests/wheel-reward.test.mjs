import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import * as T from '../../../vendor/three/three.module.min.js';
const url=new URL('../stations/wheel/room-reward.js',new URL('../',import.meta.url));
const source=(await readFile(url,'utf8')).replace("from 'three'",`from '${new URL('../../../vendor/three/three.module.min.js',import.meta.url).href}'`);
const {createRoomReward}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
function fixture(){const scene=new T.Scene(),camera=new T.PerspectiveCamera(50,.5,.1,100);const model=new T.Group();model.name='prop_monstera';const geometry=new T.BoxGeometry(.5,1,.5),map=new T.Texture(),material=new T.MeshStandardMaterial({map});model.add(new T.Mesh(geometry,material));model.position.set(5,2,8);model.updateMatrix();model.matrixAutoUpdate=false;scene.add(model);let calls=0;const stage={scene,camera,emi:{trigger(){calls++;}}};return {stage,model,geometry,map,material,get calls(){return calls;}};}
test('actual decoration miniature borrows resources and repeated cleanup never disposes the original',()=>{
 const f=fixture(),disposed=[];for(const x of [f.geometry,f.map,f.material])x.addEventListener('dispose',()=>disposed.push(x));
 const view=createRoomReward(f.stage,()=>null);
 for(let i=0;i<3;i++){assert.equal(view.reveal({reward:{kind:'decoration',decorationId:'monstera'}},true),true);assert.ok(f.stage.scene.getObjectByName('wheel_reward').getObjectByName('prop_monstera'));view.skip();}
 view.dispose();view.dispose();assert.deepEqual(disposed,[]);assert.equal(f.model.parent,f.stage.scene);assert.equal(f.stage.scene.getObjectByName('wheel_reward'),undefined);
});
test('missing decorations and capped fallback never invent a miniature; still suppresses gesture',()=>{
 const f=fixture(),v=createRoomReward(f.stage,()=>null);
 assert.equal(v.reveal({reward:{kind:'decoration',decorationId:'missing'}}),false);assert.equal(v.reveal({reward:{kind:'decoration',decorationId:'monstera',fallback:true}}),false);
 v.reveal({reward:{kind:'nothing'}},true);assert.equal(f.calls,0);v.reveal({reward:{kind:'nothing'}});assert.equal(f.calls,1);v.dispose();
});
test('double eyes share actual Loom pixels and clear on skip',()=>{const f=fixture(),canvas={width:256,height:256};const v=createRoomReward(f.stage,()=>canvas);assert.equal(v.reveal({reward:{kind:'double'}},true),true);const g=f.stage.scene.getObjectByName('wheel_reward');assert.equal(g.children.length,2);assert.equal(g.children[0].material.map.image,canvas);v.skip();assert.equal(g.visible,false);v.dispose();});

test('live still settles a raised cloche and motion resume cannot replay',()=>{const f=fixture(),v=createRoomReward(f.stage,()=>null);v.reveal({reward:{kind:'decoration',decorationId:'monstera'}});const g=f.stage.scene.getObjectByName('wheel_reward'),lid=g.children.find(n=>n.geometry?.type==='SphereGeometry');v.setStill(true);assert.ok(Math.abs(lid.position.y-.155)<1e-9);v.setStill(false);v.update(performance.now());assert.ok(Math.abs(lid.position.y-.155)<1e-9);v.dispose();});
