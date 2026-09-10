/* ============================================================================
 * board/drag.js - picking a man up and putting him down.
 *
 * Pointer down raycasts the men, and the spot on his body the ray struck is
 * remembered: from then on that spot rides under the cursor, so a held man is
 * in your hand rather than floating over it. He is lifted just enough to skim
 * the tops of the others, his base tells you which square he would land on,
 * and board/markers.js draws a dot on every square he may reach and a red ring
 * around every man he may take. A drop either plays the move or springs back
 * with a wobble. The screen position of the held piece goes out on the bus
 * every frame so the effects layer can draw over it.
 * ==========================================================================*/

import * as THREE from 'three';
import { worldToSquare, squareToWorld } from './scene.js';
import { createMarkers } from './markers.js';

const LIFT = 0.52;      // how high above the board a held man rides
const LAG = 11;         // follow stiffness; lower is lazier
const TILT = 0.22;      // how far it leans into the direction of travel
const GRAB_MAX = 0.75;  // highest point on a man the hand is allowed to hold
const GRAB_MID = 0.45;  // where the hand lands when the square, not the man, was hit

export function createDrag({ view, pieces, anim, bus, game, jiggle = null }) {
  const canvas = view.renderer.domElement;
  const ray = new THREE.Raycaster();
  const ndc = new THREE.Vector2();
  const ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const holdPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const hit = new THREE.Vector3();
  const target = new THREE.Vector3();
  const grab = new THREE.Vector2();   // piece origin minus the grabbed point, in x/z

  const markers = createMarkers({ group: view.boardGroup });
  if (view.setHighlightSink) view.setHighlightSink(markers.show);

  let held = null;        // the piece object under the cursor
  let from = null;        // the square it was picked up from
  let legal = [];         // every square it may land on
  let takes = new Set();  // the ones holding a man it may take
  let hover = null;
  let holdY = LIFT;       // world height of the plane the cursor drags along
  const at = { x: 0, y: 0 };   // last pointer position, in client pixels

  function toNdc(ev) {
    if (ev) { at.x = ev.clientX; at.y = ev.clientY; }
    const r = canvas.getBoundingClientRect();
    ndc.set(((at.x - r.left) / r.width) * 2 - 1, -((at.y - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, view.camera);
  }

  /** Where the cursor meets the board, as a square name (or null, off board). */
  function squareUnder(ev) {
    toNdc(ev);
    if (!ray.ray.intersectPlane(ground, hit)) return null;
    return worldToSquare(hit.x, hit.z);
  }

  /** The man under the cursor, and the point on him the ray landed on. */
  function pieceUnder(ev) {
    toNdc(ev);
    const hits = ray.intersectObjects(view.pieceGroup.children, true);
    for (const h of hits) {
      let node = h.object;
      while (node && node.parent !== view.pieceGroup) node = node.parent;
      if (node && node.userData.square) return { piece: node, point: h.point };
    }
    return null;
  }

  /**
   * Aim the held man at the cursor. The ray is cut with a horizontal plane at
   * the height of the spot that was grabbed, not with the board, so that spot
   * stays under the cursor whatever angle the camera sits at. The man hangs off
   * it by the offset the grab recorded, which leaves his base over the square
   * the markers say he is going to. Called with no event it re-uses the last
   * pointer position, which is how the camera can sway under a still cursor
   * without the man sliding out of your hand.
   */
  function aim(ev) {
    toNdc(ev);
    holdPlane.constant = -holdY;
    if (!ray.ray.intersectPlane(holdPlane, hit)) return false;
    target.set(hit.x + grab.x, LIFT, hit.z + grab.y);
    return true;
  }

  /** The square the held man is standing over right now. */
  function overSquare() {
    return worldToSquare(target.x, target.z);
  }

  function paint() {
    const list = legal.map((sq) => ({
      square: sq,
      kind: takes.has(sq) ? 'capture' : 'move',
      hover: sq === hover,
    }));
    if (from) list.push({ square: from, kind: 'origin' });
    markers.show(list);
  }

  /**
   * chess.js hands back a flag string per move: 'c' is a capture and 'e' is en
   * passant, which takes a man standing on a square the pawn does not land on.
   * Castling ('k' and 'q') takes nothing, so it stays a plain dot.
   */
  function readMoves(sq) {
    const rules = game.rules;
    const verbose = rules && rules.movesFrom ? rules.movesFrom(sq) : [];
    if (!verbose.length) return { squares: game.legalTargets(sq), captures: new Set() };
    const captures = new Set();
    for (const m of verbose) {
      const flags = String(m.flags || '');
      if (m.captured || flags.includes('c') || flags.includes('e')) captures.add(m.to);
    }
    return { squares: verbose.map((m) => m.to), captures };
  }

  function onDown(ev) {
    if (held || ev.button > 0) return;
    const found = pieceUnder(ev);
    const square = found ? null : squareUnder(ev);
    const piece = found ? found.piece : (square ? pieces.pieceAt(square) : null);
    if (!piece || !piece.userData.square) return;
    const sq = piece.userData.square;
    if (!game.canPick(sq)) return;

    held = piece;
    from = sq;
    const moves = readMoves(sq);
    legal = moves.squares;
    takes = moves.captures;
    hover = null;
    held.userData.held = true;
    held.userData.busy = true;
    if (jiggle) jiggle.grab(held);   // lifted off the board, so the tip sags

    // Hold him where he was actually grabbed. A hit on the body gives the exact
    // spot; the square fallback (a click that slipped past the mesh) takes him
    // around the middle. The height is capped, so grabbing a king by his crown
    // does not swing his base half a board away from the cursor.
    const base = squareToWorld(sq, 0);
    if (found) {
      grab.set(base.x - found.point.x, base.z - found.point.z);
      holdY = LIFT + THREE.MathUtils.clamp(found.point.y - piece.position.y, 0.04, GRAB_MAX);
    } else {
      grab.set(0, 0);
      holdY = LIFT + Math.min(GRAB_MAX, GRAB_MID * (piece.userData.scaleBase || 1));
    }
    target.copy(held.position);
    aim(ev);                     // so he lifts on the click, not on the first move
    const on = overSquare();
    hover = legal.includes(on) ? on : null;
    paint();
    // A synthetic pointer (the smoke harness, some remote-control paths) has no
    // capture to take, and asking for one throws.
    try { canvas.setPointerCapture(ev.pointerId); } catch { /* nothing to hold */ }
    bus.emit('grab', { square: sq, piece: piece.userData.type, screen: view.projectPoint(held.position) });
    ev.preventDefault();
  }

  function onMove(ev) {
    if (!held) return;
    if (!aim(ev)) return;
    const next = overSquare();
    const want = legal.includes(next) ? next : null;
    if (want !== hover) { hover = want; paint(); }
  }

  function onUp(ev) {
    if (!held) return;
    const piece = held;
    const start = from;
    aim(ev);
    const to = overSquare();
    held = null;
    from = null;
    hover = null;
    legal = [];
    takes = new Set();
    markers.clear();
    piece.userData.held = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    try { canvas.releasePointerCapture(ev.pointerId); } catch { /* never taken */ }

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
    held = null; from = null; hover = null; legal = []; takes = new Set();
    markers.clear();
    piece.userData.held = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    anim.springBack(piece);
    bus.emit('drop', { ok: false });
  }

  /** Lagged follow, a lean into the travel, and the per-frame screen ping. */
  function update(dt) {
    markers.update(dt);
    if (!held) return;
    // The camera sways, so where the cursor points moves even when it does not.
    const was = hover;
    if (aim(null)) {
      const next = overSquare();
      hover = legal.includes(next) ? next : null;
      if (hover !== was) paint();
    }
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
    markers,
    isDragging: () => !!held,
    // The height of the plane the cursor is dragging along. The screenshot
    // harness aims its release with this, because board level is no longer
    // where the held man is.
    holdHeight: () => (held ? holdY : 0),
    dispose() {
      canvas.removeEventListener('pointerdown', onDown);
      canvas.removeEventListener('pointermove', onMove);
      canvas.removeEventListener('pointerup', onUp);
      canvas.removeEventListener('pointercancel', onCancel);
      window.removeEventListener('blur', onCancel);
      if (view.setHighlightSink) view.setHighlightSink(null);
      markers.dispose();
    },
  };
}
