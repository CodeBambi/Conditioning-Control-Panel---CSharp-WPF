import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import * as T from '../../../vendor/three/three.module.min.js';
const url=new URL('../prize-display.js',import.meta.url);
const source=(await readFile(url,'utf8')).replace("from 'three'",`from '${new URL('../../../vendor/three/three.module.min.js',import.meta.url).href}'`)
 .replace(/from '(\.\.?\/[^']+)'/g,(_,p)=>`from '${new URL(p,url).href}'`);
const {createPrizeDisplay}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
globalThis.document={createElement:()=>({getContext:()=>({fillRect(){},strokeRect(){},fillText(){}})})};
function setup(){
 const scene=new T.Scene(),counter=new T.Group(),geometry=new T.BoxGeometry(.4,.5,.2),material=new T.MeshStandardMaterial();scene.add(counter);
 for(const name of ['racing_cabinet','shelf_rt_demo','shelf_flashes_v2','shelf_rt_bundle_1','shelf_rt_bundle_2','shelf_rt_bundle_3']){
  const g=new T.Group();g.name=name;const mesh=new T.Mesh(geometry,material);mesh.position.y=.25;g.add(mesh);counter.add(g);
 }
 const display=createPrizeDisplay({scene,counter,lex:(_,fallback)=>fallback});
 return {scene,counter,geometry,material,display};
}
test('an owned bundle reveals only its display and a granted demo hides its mockup',()=>{
 const {display,counter}=setup();display.apply({owned:['rt_bundle_2'],demo:true},'rt_bundle_2');
 assert.equal(display.cabinet.visible,true);assert.equal(display.rack.getObjectByName('owned_rt_bundle_2').visible,true);
 assert.equal(display.rack.getObjectByName('owned_rt_bundle_1').visible,false);assert.equal(counter.getObjectByName('shelf_rt_demo').visible,false);
 const materials=[];display.cabinet.traverse(n=>{if(n.material)materials.push(...[].concat(n.material));});
 display.update(.4,false);const alpha=materials[0].opacity;assert.ok(alpha>0&&alpha<1);
 display.apply({owned:['rt_bundle_2'],demo:true});assert.equal(materials[0].opacity,alpha,'refresh never restarts a reveal');
 display.update(0,true);assert.equal(materials[0].opacity,1,'motion off settles immediately');display.dispose();
});
test('revocation mid-reveal and a later restored grant never leave a translucent cabinet',()=>{
 const {display}=setup();display.apply({owned:['rt_demo'],demo:true},'rt_demo');display.update(.2,false);
 display.apply({owned:[],demo:false});assert.equal(display.cabinet.visible,false);
 display.apply({owned:['rt_demo'],demo:true});assert.equal(display.cabinet.position.y,0);
 display.cabinet.traverse(n=>{for(const m of [].concat(n.material||[]))assert.equal(m.opacity,1);});display.dispose();
});
test('cleanup never disposes the borrowed prize meshes or source materials',()=>{
 const {display,geometry,material,counter}=setup();let disposed=0;geometry.addEventListener('dispose',()=>disposed++);material.addEventListener('dispose',()=>disposed++);
 display.apply({owned:['flashes_v2'],demo:false},'flashes_v2');display.update(2,false);
 assert.equal(counter.getObjectByName('shelf_flashes_v2').parent.position.y,0);
 display.dispose();display.dispose();assert.equal(disposed,0);
});

test('purchased expansion travels to its shelf, sparkles expire, and motion off settles both',()=>{
 const {display,scene}=setup();display.apply({owned:['rt_bundle_1'],demo:true},'rt_bundle_1');
 const model=display.rack.getObjectByName('owned_rt_bundle_1'),start=model.position.clone();
 assert.equal(scene.getObjectByName('prize_sparkles').count,64);
 display.update(.5,false);assert.ok(model.position.distanceTo(start)>.1);
 display.update(0,true);assert.equal(model.position.y,.68);assert.equal(model.rotation.y,0);
 assert.equal(scene.getObjectByName('prize_sparkles').count,0);
 display.apply({owned:['rt_bundle_1'],demo:true});assert.equal(scene.getObjectByName('prize_sparkles').count,0,'state refresh cannot replay particles');
 display.dispose();assert.equal(scene.getObjectByName('prize_sparkles'),undefined);
});
