import test from 'node:test';
import assert from 'node:assert/strict';
import * as T from '../../../vendor/three/three.module.min.js';
import { createEmiInteraction } from '../emi-interaction.js';
import { score } from '../../shared/sound/kit.js';

function rig() {
  const doc = new EventTarget(); doc.defaultView = new EventTarget();
  doc.createElement = () => ({ style: {}, hidden: true, offsetWidth: 180, offsetHeight: 55, setAttribute() {}, remove() {} });
  const canvas = new EventTarget(); canvas.ownerDocument = doc;
  canvas.getBoundingClientRect = () => ({ left: 0, top: 0, right: 600, bottom: 400, width: 600, height: 400 });
  canvas.setPointerCapture = () => {};
  const camera = new T.PerspectiveCamera(60, 1.5, .1, 30); camera.position.z = 5; camera.updateMatrixWorld();
  const scene = new T.Scene(), root = new T.Mesh(new T.BoxGeometry(1, 1, 1), new T.MeshBasicMaterial()); scene.add(root);
  let gestures = 0;
  const interaction = createEmiInteraction({ canvas, camera, scene, emis: [{ id: 'cards', root, trigger() { gestures++; } }], mount: { appendChild() {} } });
  const send = (type, x = 300, y = 200) => {
    const e = new Event(type, { cancelable: true });
    Object.assign(e, { clientX: x, clientY: y, button: 0, isPrimary: true, pointerId: 1 }); canvas.dispatchEvent(e); return e;
  };
  return { scene, interaction, send, canvas, gestures: () => gestures };
}
test('mascot taps own input before game handlers and show an anchored reply', () => {
  const r = rig(); let gameActions = 0;
  r.canvas.addEventListener('pointerup', () => gameActions++);
  assert.equal(r.send('pointerdown').defaultPrevented, true); r.send('pointerup');
  assert.equal(gameActions, 0); assert.equal(r.gestures(), 1);
  assert.equal(r.interaction.debug().id, 'cards'); assert.ok(r.interaction.debug().text);
  r.interaction.dispose();
});
test('a drag is not a mascot tap, and an opaque wall blocks it', () => {
  const r = rig(); r.send('pointerdown'); r.send('pointermove', 330); r.send('pointerup', 330);
  assert.equal(r.gestures(), 0);
  const wall = new T.Mesh(new T.BoxGeometry(3, 3, .2), new T.MeshBasicMaterial()); wall.position.z = 2; r.scene.add(wall);
  r.send('pointerdown'); r.send('pointerup'); assert.equal(r.gestures(), 0);
  r.interaction.dispose();
});
test('disabled input stays quiet and disposal removes listeners', () => {
  const r = rig(); r.interaction.setEnabled(false); r.send('pointerdown'); r.send('pointerup');
  assert.equal(r.gestures(), 0); r.interaction.dispose();
  assert.equal(r.send('pointerdown').defaultPrevented, false);
});
test('electronic replies are short, quiet, varied and never a long sound bed', () => {
  const scores = [0, 1, 2].map(variant => score('emi-bleep', { variant }));
  for (const s of scores) { assert.ok(s.notes.length >= 3); assert.ok(s.notes.every(n => n.level <= .07 && n.at + n.dur < .4)); }
  assert.notDeepEqual(scores[0].notes, scores[1].notes);
});
