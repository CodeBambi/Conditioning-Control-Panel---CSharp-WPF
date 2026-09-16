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
  const canvas = { getContext: () => g, addEventListener() {}, removeEventListener() {} };
  globalThis.document = { createElement: () => canvas };
  const fixture = new T.Group(), scene = new T.Scene();
  for (const name of REQUIRED) {
    const n = new T.Object3D(); n.name = name; n.userData = { card_width: .135, card_height: .194 }; fixture.add(n);
  }
  const deck = new T.Object3D(); deck.name = 'deck_card_4'; deck.position.set(1, 2, 3); fixture.add(deck);
  const cues = [], table = createTable3D({ fixture, scene, canvas, register: () => () => {} }, { onCue: (cue) => cues.push(cue) });
  const draw = (now) => table.draw({ now, still: false, k: 1, gates: {} });
  table.startFan(0, false); table.addCard({ owner: 0, slot: 0, code: 'Kh' }, 0); draw(100);
  assert.notDeepEqual(deck.position.toArray(), [1, 2, 3]);
  table.skip(120);
  assert.deepEqual(deck.position.toArray(), [1, 2, 3]);
  assert.equal(table.debug().fan, false); assert.equal(table.debug().cards[0].landed, true);
  draw(130); draw(500); draw(4500); assert.deepEqual(cues, []);
  table.addCard({ owner: 0, slot: 1, code: 'As' }, 5000); draw(5500);
  assert.deepEqual(cues, ['card-land']);
  table.dispose(); assert.equal(scene.children.length, 0);
});
