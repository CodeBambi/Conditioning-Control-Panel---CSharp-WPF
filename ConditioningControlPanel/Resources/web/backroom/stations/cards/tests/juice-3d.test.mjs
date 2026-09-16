import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  return next(specifier === 'three' ? new URL('../../../../vendor/three/three.module.min.js', import.meta.url).href : specifier, context);
} });
const T = await import('three');
const { createTable3D } = await import('../table-3d.js');
const { REQUIRED } = await import('../../../room/nodes-cards.js');
hooks.deregister();

test('3D suspend discards in-flight landing and fan cues, restoring the authored deck', () => {
  const g = new Proxy({}, { get: (o, k) => o[k] || (() => {}) });
  const canvas = { getContext: () => g, addEventListener() {}, removeEventListener() {}, getBoundingClientRect:()=>({width:1280,height:720}) };
  globalThis.document = { createElement: () => canvas };
  const fixture = new T.Group(), scene = new T.Scene();
  for (const name of REQUIRED) {
    const n = new T.Object3D(); n.name = name; n.userData = { card_width: .135, card_height: .194 }; fixture.add(n);
  }
  const deck = new T.Object3D(); deck.name = 'deck_card_4'; deck.position.set(1, 2, 3); fixture.add(deck);
  const camera=new T.PerspectiveCamera(45,1280/720,.1,100); camera.position.set(0,3,5); camera.lookAt(0,0,0); camera.updateMatrixWorld();
  const cues = [], table = createTable3D({ fixture, scene, canvas, camera, register: () => () => {} }, { onCue: (cue) => cues.push(cue) });
  const draw = (now) => table.draw({ now, still: false, k: 1, gates: {} });
  table.setBets([3]); assert.equal(table.debug().betChips,3);
  table.setBets([6,3]); assert.equal(table.debug().betChips,9);
  table.startFan(0, false); table.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0); draw(100);
  assert.equal(table.hud().total,null,'a flying face-down card contributes no total');
  assert.notDeepEqual(deck.position.toArray(), [1, 2, 3]);
  table.skip(120);
  assert.deepEqual(deck.position.toArray(), [1, 2, 3]);
  assert.equal(table.debug().fan, false); assert.equal(table.debug().cards[0].landed, true);
  assert.equal(table.hud().total,10,'quiet restored face-up card contributes its value');
  draw(130); draw(500); draw(4500); assert.deepEqual(cues, []);
  table.addCard({ owner: 0, slot: 1, code: 'As' }, 5000); draw(5500);
  assert.deepEqual(cues, ['card-land']);
  for(let slot=2;slot<6;slot++)table.addCard({owner:0,slot,code:'2h',settled:true},6000);
  table.setBets([6],6000,true);draw(7000);
  const clear = () => {
    scene.updateMatrixWorld(true);
    const meshes=scene.getObjectByName('cards_runtime').children;
    const chipBoxes=meshes.filter(m=>m.userData.bet && m.userData.bet.removeAt==null).map(m=>new T.Box3().setFromObject(m));
    const faces=meshes.filter(m=>m.geometry?.type==='PlaneGeometry' && m.renderOrder===2);
    for(const chip of chipBoxes)for(const face of faces){
      const b=new T.Box3().setFromObject(face);
      assert.ok(chip.max.x < b.min.x || chip.min.x > b.max.x || chip.max.z < b.min.z || chip.min.z > b.max.z,'chip footprint clears every card');
    }
  };
  clear();
  table.split();for(let slot=1;slot<6;slot++)table.addCard({owner:1,slot,code:'2h',settled:true},7000);
  table.setBets([6,6],7000,true);draw(9000);clear();
  table.dispose(); assert.equal(scene.children.length, 0);
});
