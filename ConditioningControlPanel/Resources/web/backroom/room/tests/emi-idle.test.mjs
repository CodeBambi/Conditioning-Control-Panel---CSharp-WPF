import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../../../vendor/three/three.module.min.js';
import { createEmiIdle } from '../emi-idle.js';

const names = { counter: 'golden_emi_attendant', wheel: 'emi_topper', cards: 'emi_dealer', roulette: 'emi_dealer' };
function fixture(id) {
  const model = new T.Group(), root = new T.Group(); root.name = names[id];
  root.position.set(.72, .8, -.99); root.rotation.y = .3; root.scale.setScalar(.82);
  const face = new T.Mesh(new T.BoxGeometry(.2,.2,.01), new T.MeshStandardMaterial());
  face.name = 'EMI_glass'; face.position.set(0,.6,.09); face.scale.set(.0002,.0003,.0001);
  root.add(face); model.add(root); model.updateMatrixWorld(true);
  const before = face.matrixWorld.clone(), local = face.matrix.clone(), rootLocal = root.matrix.clone();
  const material = face.material, atlas = new T.Texture();
  const controller = createEmiIdle({model,row:{id},atlas});
  return {model,root,face,before,local,rootLocal,material,atlas,controller};
}
function nearMatrix(a,b) { a.elements.forEach((v,i)=>assert.ok(Math.abs(v-b.elements[i]) < 1e-12)); }
test('all four NPC wrappers preserve rest placement, quantization transforms and disposal', () => {
  for (const id of Object.keys(names)) {
    const f = fixture(id); f.model.updateMatrixWorld(true); nearMatrix(f.face.matrixWorld,f.before);
    for(let i=0;i<60;i++) f.controller.update(1/60);
    f.model.updateMatrixWorld(true); nearMatrix(f.face.matrix,f.local); nearMatrix(f.root.matrix,f.rootLocal);
    assert.notDeepEqual(f.face.matrixWorld.elements,f.before.elements);
    f.controller.update(1/60,true); f.model.updateMatrixWorld(true); nearMatrix(f.face.matrixWorld,f.before);
    f.controller.dispose(); f.controller.dispose(); f.model.updateMatrixWorld(true);
    nearMatrix(f.face.matrixWorld,f.before); assert.equal(f.root.parent,f.model); assert.equal(f.face.material,f.material);
  }
});
test('motion off settles and freezes phase; large resume delta is bounded', () => {
  const f=fixture('wheel'); f.controller.update(.02); const t=f.controller.debug().phase;
  f.controller.update(600,true); assert.equal(f.controller.debug().phase,t);
  assert.deepEqual(f.controller.debug().pose,[0,0,0]);
  f.controller.update(600); assert.ok(f.controller.debug().phase-t <= .0500001);
  f.controller.dispose();
});
test('NPC faces are independent, blink and shared atlas remain isolated', () => {
  const a=fixture('counter'),b=fixture('cards'); let blink=false;
  for(let i=0;i<600;i++) { a.controller.update(.02); blink ||= a.controller.debug().blink; }
  assert.ok(blink); assert.notEqual(a.face.material.map,b.face.material.map);
  assert.deepEqual(a.atlas.offset.toArray(),[0,0]);
  a.controller.update(0,true); assert.equal(a.controller.debug().blink,false);
  a.controller.dispose(); b.controller.dispose();
});
test('slot cabinet face screens are not animated as NPCs', () => {
  assert.equal(createEmiIdle({model:new T.Group(),row:{id:'slot'}}),null);
});

test('real shoulder children articulate without changing authored mesh transforms',()=>{
 const model=new T.Group(),root=new T.Group();root.name='golden_emi_attendant';model.add(root);
 for(const side of ['L','R']) {const shoulder=new T.Group();shoulder.name='shoulder'+side;shoulder.position.set(side==='L'?-.39:.39,.49,0);const hand=new T.Mesh(new T.BoxGeometry(.1,.1,.1),new T.MeshBasicMaterial());hand.name='hand'+side;hand.position.y=-.25;shoulder.add(hand);root.add(shoulder);}
 model.updateMatrixWorld(true);const hand=root.getObjectByName('handR'),local=hand.matrix.clone(),initial=hand.getWorldPosition(new T.Vector3());
 const actor=createEmiIdle({model,row:{id:'counter'}});assert.ok(actor.debug().articulated);assert.ok(actor.trigger('greet'));
 for(let i=0;i<60;i++)actor.update(1/60);model.updateMatrixWorld(true);
 nearMatrix(hand.matrix,local);assert.ok(hand.getWorldPosition(new T.Vector3()).y>initial.y+.2);
 assert.equal(root.getObjectByName('emi_feather_duster').visible,false);
 actor.update(.01,true);model.updateMatrixWorld(true);assert.ok(hand.getWorldPosition(new T.Vector3()).distanceTo(initial)<1e-10);
 assert.deepEqual(actor.debug().arms,[0,0]);actor.dispose();
});
