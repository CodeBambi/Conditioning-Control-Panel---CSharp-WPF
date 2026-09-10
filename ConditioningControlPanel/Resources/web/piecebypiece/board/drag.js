/* ============================================================================
 * board/drag.js - picking a man up and putting him down.
 *
 * Pointer down raycasts the men, the held piece follows the cursor a beat
 * behind and hangs above the board, the squares it may legally land on glow,
 * and a drop either snaps home or springs back with a wobble. The screen
 * position of the held piece goes out on the bus every frame so the effects
 * layer can draw over it.
 * ==========================================================================*/

import * as THREE from 'three';
import { worldToSquare } from './scene.js';

const LIFT = 1.15;      // how high above the board a held piece rides
const LAG = 11;         // follow stiffness; lower is lazier
const TILT = 0.22;      // how far it leans into the direction of travel

export function createDrag({ view, pieces, anim, bus, game, jiggle = null }) {
  const canvas = view.renderer.domElement;
  const ray = new THREE.Raycaster();
  const ndc = new THREE.Vector2();
  const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const hit = new THREE.Vector3();
  const target = new THREE.Vector3();

  let held = null;        // the piece object under the cursor
  let from = null;        // the square it was picked up from
  let legal = [];
  let hover = null;

  function toNdc(ev) {
    const r = canvas.getBoundingClientRect();
    ndc.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, view.camera);
  }

  /** Where the cursor meets the board, as a square name (or null, off board). */
  function squareUnder(ev) {
    toNdc(ev);
    if (!ray.ray.intersectPlane(plane, hit)) return null;
    return worldToSquare(hit.x, hit.z);
  }

  function pieceUnder(ev) {
    toNdc(ev);
    const hits = ray.intersectObjects(view.pieceGroup.children, true);
    for (const h of hits) {
      let node = h.object;
      while (node && node.parent !== view.pieceGroup) node = node.parent;
      if (node && node.userData.square) return node;
    }
    return null;
  }

  function glow() {
    view.setHighlights(legal.map((sq) => ({
      square: sq,
      color: sq === hover ? 0xFFFFFF : 0xFF3FA4,
      glow: sq === hover ? 0.85 : 0.42,
    })));
  }

  function onDown(ev) {
    if (held || ev.button > 0) return;
    const square = squareUnder(ev);
    const piece = pieceUnder(ev) || (square ? pieces.pieceAt(square) : null);
    if (!piece || !piece.userData.square) return;
    const sq = piece.userData.square;
    if (!game.canPick(sq)) return;

    held = piece;
    from = sq;
    legal = game.legalTargets(sq);
    hover = null;
    held.userData.held = true;
    held.userData.busy = true;
    if (jiggle) jiggle.grab(held);   // lifted off the board, so the tip sags
    target.copy(held.position);
    glow();
    canvas.setPointerCapture?.(ev.pointerId);
    bus.emit('grab', { square: sq, piece: piece.userData.type, screen: view.projectPoint(held.position) });
    ev.preventDefault();
  }

  function onMove(ev) {
    if (!held) return;
    toNdc(ev);
    if (ray.ray.intersectPlane(plane, hit)) target.set(hit.x, LIFT, hit.z);
    const next = worldToSquare(hit.x, hit.z);
    if (next !== hover) { hover = legal.includes(next) ? next : null; glow(); }
  }

  function onUp(ev) {
    if (!held) return;
    const piece = held;
    const start = from;
    const to = squareUnder(ev);
    held = null;
    from = null;
    hover = null;
    legal = [];
    view.setHighlights([]);
    piece.userData.held = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    canvas.releasePointerCapture?.(ev.pointerId);

    // A legal drop is played by the game, which moves the man and lets anim.js
    // fly it down from where it was being held.
    const played = to && to !== start ? game.tryMove(start, to) : null;
    if (played) {
      bus.emit('drop', { ok: true });
    } else {
      anim.springBack(piece);
      bus.emit('drop', { ok: false });
    }
  }

  function onCancel() {
    if (!held) return;
    const piece = held;
    held = null; from = null; hover = null; legal = [];
    view.setHighlights([]);
    piece.userData.held = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    anim.springBack(piece);
    bus.emit('drop', { ok: false });
  }

  /** Lagged follow, a lean into the travel, and the per-frame screen ping. */
  function update(dt) {
    if (!held) return;
    const k = 1 - Math.exp(-LAG * dt);
    const dx = target.x - held.position.x;
    const dz = target.z - held.position.z;
    held.position.x += dx * k;
    held.position.y += (target.y - held.position.y) * k;
    held.position.z += dz * k;
    held.rotation.z = THREE.MathUtils.clamp(-dx * TILT, -0.35, 0.35);
    held.rotation.x = THREE.MathUtils.clamp(dz * TILT, -0.35, 0.35);
    // Whatever the body just covered, the soft top has yet to catch up on.
    if (jiggle) jiggle.lag(held, dx * k, dz * k);
    bus.emit('dragmove', { screen: view.projectPoint(held.position) });
  }

  canvas.addEventListener('pointerdown', onDown);
  canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp);
  canvas.addEventListener('pointercancel', onCancel);
  window.addEventListener('blur', onCancel);

  return {
    update,
    isDragging: () => !!held,
    dispose() {
      canvas.removeEventListener('pointerdown', onDown);
      canvas.removeEventListener('pointermove', onMove);
      canvas.removeEventListener('pointerup', onUp);
      canvas.removeEventListener('pointercancel', onCancel);
      window.removeEventListener('blur', onCancel);
    },
  };
}
