import * as T from '../../vendor/three/three.module.min.js';
import { sampleEmiGesture } from './emi-gestures.js';

const ROOTS = { counter: 'golden_emi_attendant', wheel: 'emi_topper', cards: 'emi_dealer', roulette: 'emi_dealer' };
const PHASES = { counter: 1.7, wheel: 4.3, cards: 8.1, roulette: 11.6 };

/** Shared room idle. Animate added pivots, never GLB quantization transforms. */
export function createEmiIdle({ model, row, atlas }) {
  const root = model.getObjectByName(ROOTS[row.id] || '');
  if (!ROOTS[row.id] || !root || !root.parent) return null;
  const parent = root.parent, slot = parent.children.indexOf(root);
  const pivot = new T.Group(), offset = new T.Group();
  pivot.name = 'emi_idle_' + row.id;
  pivot.position.copy(root.position);
  offset.position.copy(root.position).multiplyScalar(-1);
  parent.add(pivot); pivot.add(offset); offset.add(root);
  const face = root.getObjectByName('EMI_glass');
  const originalMaterial = face?.material;
  const texture = atlas && face ? atlas.clone() : null;
  if (texture && originalMaterial) {
    texture.needsUpdate = true;
    face.material = originalMaterial.clone();
    face.material.map = texture; face.material.emissiveMap = texture;
    face.material.needsUpdate = true;
  }
  let elapsed = 0, disposed = false, faceIndex = 3;
  const phase = PHASES[row.id];
  function expression(index) {
    faceIndex = index;
    if (texture) texture.offset.set((index * 152 + .5) / 1672, .5 / 137);
  }
  function rest() {
    pivot.rotation.set(0, 0, 0); pivot.position.copy(root.position); expression(3);
  }
  function update(dt, still = false) {
    if (disposed) return;
    if (still) { rest(); return; }
    // The scene skips this call while paused. Discard large resume deltas.
    elapsed += Math.max(0, Math.min(Number.isFinite(dt) ? dt : 0, .05));
    const t = elapsed + phase;
    const gesture = sampleEmiGesture(row.id, t);
    const glance = Math.pow(Math.max(0, Math.sin(t * .31)), 6);
    pivot.rotation.set(
      .005 * Math.sin(t * 1.25) + gesture.pitch,
      .026 * glance * Math.sin(t * .43) + gesture.yaw,
      .006 * Math.sin(t * .71) + gesture.roll,
    );
    // Small rocking reads as breath without scaling the face or lifting feet.
    pivot.position.copy(root.position);
    const blink = t % 5.9;
    expression(blink < .14 ? 2 : (gesture.face ?? 3));
  }
  rest();
  return {
    root, pivot, update,
    debug: () => ({ id: row.id, phase: elapsed, pose: [pivot.rotation.x, pivot.rotation.y, pivot.rotation.z], blink: faceIndex === 2 }),
    dispose() {
      if (disposed) return;
      disposed = true; rest();
      if (texture && originalMaterial) { face.material.dispose(); face.material = originalMaterial; texture.dispose(); }
      parent.add(root); parent.remove(pivot);
      parent.children.splice(parent.children.indexOf(root), 1); parent.children.splice(slot, 0, root);
    },
  };
}
