/* ============================================================================
 * board/poses.js - how the board ends.
 *
 * The last beat of a game, played by the men themselves:
 *
 *   mate    the losing king tips over toward his own side, slowly (1.2 s),
 *           pivoting on the edge of his base, and stays down. His outline
 *           fades to grey. The winner's men take one sway together the
 *           moment he hits, and the pink squares warm for two seconds.
 *   draw    the two kings lean toward each other over a second, and hold.
 *   flag / resignation   the mate pose for whoever lost.
 *
 * Everything here happens after `gameover`, when nothing else moves a man,
 * so the posed kings are simply written every frame; `busy` keeps the idle
 * wobble off them. A new game (`local`) stands them back up before pieces.js
 * decides whether to keep them.
 *
 * Reduced motion (window.PBP.settings.reducedMotion or the media query): the
 * final pose is set at once, no fall, no sway, no warm.
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number in the last beat. */
export const TUNING = Object.freeze({
  fallSec: 1.2,          // the king takes this long to hit the board
  fallAngle: Math.PI / 2 - 0.06,   // he rests on his crown, not quite flat
  bounce: 0.05,          // radians of the little rebound after he hits
  bounceSec: 0.28,
  leanSec: 1.0,          // a draw: how long the kings take to lean in
  leanAngle: 0.30,       // and how far (radians)
  swayBend: 2.4,         // the winner's men, one bend together, in jiggle units
  warmSec: 2.0,          // the pink squares
  warmPeak: 0.9,         // emissive intensity at the top of the warm
  warmColor: 0xFF3FA4,
  greyLine: 0x8A8390,    // the fallen king's outline
});

function reduced() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if ((s && s.reducedMotion) || (window.PBP && window.PBP.reducedMotion)) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

const easeIn = (p) => p * p * p;
const easeOut = (p) => 1 - Math.pow(1 - p, 3);
const UP = new THREE.Vector3(0, 1, 0);
const tmpV = new THREE.Vector3();
const tmpQ = new THREE.Quaternion();
const baseQ = new THREE.Quaternion();

/** Half the man's footprint in world units, off the body he was built from. */
function footRadius(piece) {
  let best = 0.25;
  piece.traverse((o) => {
    if (!o.isMesh || o.userData.pbpHull || o === piece.userData.contact || !o.geometry) return;
    if (!o.geometry.boundingBox) o.geometry.computeBoundingBox();
    const bb = o.geometry.boundingBox;
    best = Math.max(best, (bb.max.x - bb.min.x) / 2, (bb.max.z - bb.min.z) / 2);
  });
  return best * (piece.scale.x || 1);
}

/**
 * `view` is board/scene.js; `pieces` the roster (pieceAt, pieces map); `bus`
 * the game bus; `jiggle` the flex system; `outline` a getter for the outline
 * module (it loads separately) with setBase(piece, hex).
 */
export function createPoses({ view, pieces, bus = null, jiggle = null, outline = null, project = null }) {
  const T = TUNING;
  let scene = null;          // { kind, poses: [...], t, swayed, warmed }
  const posed = [];          // kings we have written, to stand back up

  function kingOf(side) {
    for (const p of view.pieceGroup.children) {
      if (p.userData && p.userData.type === 'k' && p.userData.side === side && !p.userData.parade) return p;
    }
    return null;
  }

  /** Lean `piece` by `angle` toward the unit direction `d`, pivoting on his base edge. */
  function lean(pose, angle) {
    const { piece, d, r, home } = pose;
    tmpV.crossVectors(UP, d).normalize();          // rotates +Y toward d
    tmpQ.setFromAxisAngle(tmpV, angle);
    baseQ.setFromAxisAngle(UP, piece.userData.side === 'b' ? Math.PI : 0);
    piece.quaternion.copy(baseQ).premultiply(tmpQ);
    // The pivot p = d * r sits on the base edge; keep it where it was.
    tmpV.copy(d).multiplyScalar(r);
    const moved = tmpV.clone().applyQuaternion(tmpQ);
    piece.position.set(home.x + tmpV.x - moved.x, home.y + tmpV.y - moved.y, home.z + tmpV.z - moved.z);
  }

  function makePose(piece, d) {
    piece.userData.busy = true;
    piece.userData.pose = true;
    if (!posed.includes(piece)) posed.push(piece);
    return { piece, d: d.clone().normalize(), r: footRadius(piece), home: piece.position.clone() };
  }

  function standUp() {
    for (const piece of posed) {
      piece.rotation.set(0, piece.userData.side === 'b' ? Math.PI : 0, 0);
      if (piece.userData.home) piece.position.set(piece.userData.home.x, 0, piece.userData.home.z);
      piece.userData.busy = false;
      piece.userData.pose = false;
      const o = outline && outline();
      if (o && o.setBase) o.setBase(piece, null);
    }
    posed.length = 0;
    coolSquares(0);
    scene = null;
  }

  const warmSquares = [];
  function coolSquares(intensity) {
    if (!warmSquares.length) {
      for (const mesh of view.squares.values()) {
        const b = mesh.userData.base;
        if (b && b.r > b.g + 0.2) warmSquares.push(mesh);   // the pink ones
      }
    }
    for (const mesh of warmSquares) {
      const mat = mesh.material;
      if (intensity <= 0) { mat.emissive.setHex(0x000000); mat.emissiveIntensity = 1; }
      else { mat.emissive.setHex(T.warmColor); mat.emissiveIntensity = intensity; }
    }
  }

  function begin(result, winner) {
    standUp();
    const drawn = !winner || result === 'stalemate' || result === 'draw';
    if (drawn) {
      const wk = kingOf('w');
      const bk = kingOf('b');
      if (!wk || !bk) return;
      const toward = (a, b) => tmpV.set(b.position.x - a.position.x, 0, b.position.z - a.position.z);
      const dw = toward(wk, bk).lengthSq() > 1e-6 ? toward(wk, bk).clone() : new THREE.Vector3(0, 0, -1);
      const db = toward(bk, wk).lengthSq() > 1e-6 ? toward(bk, wk).clone() : new THREE.Vector3(0, 0, 1);
      scene = { kind: 'draw', t: 0, poses: [makePose(wk, dw), makePose(bk, db)] };
    } else {
      const loser = winner === 'w' ? 'b' : 'w';
      const king = kingOf(loser);
      if (!king) return;
      // He falls toward his own side: white's is +z, black's is -z.
      const d = new THREE.Vector3(0, 0, loser === 'w' ? 1 : -1);
      scene = { kind: 'mate', t: 0, loser, poses: [makePose(king, d)], hit: false };
    }
    if (reduced()) { update(1e6); }
  }

  function hit(scene) {
    scene.hit = true;
    const king = scene.poses[0].piece;
    const o = outline && outline();
    if (o && o.setBase) o.setBase(king, T.greyLine);
    if (reduced()) return;
    // The winner's men, one sway together, away from the fallen king.
    const winner = scene.loser === 'w' ? 'b' : 'w';
    const away = scene.loser === 'w' ? -1 : 1;
    if (jiggle) {
      for (const p of view.pieceGroup.children) {
        if (p.userData && p.userData.side === winner && !p.userData.parade) jiggle.impulse(p, { bend: [0, T.swayBend * away] });
      }
    }
    // The dust and the thud hear him hit.
    if (bus && typeof bus.emit === 'function') {
      const world = { x: king.position.x, y: 0, z: king.position.z };
      bus.emit('land', {
        square: king.userData.square, piece: 'k', side: scene.loser, capture: false, refused: false, pose: true,
        height: king.userData.scaleBase || 1, world, screen: project ? project(new THREE.Vector3(world.x, 0, world.z)) : { x: 0, y: 0 },
      });
    }
  }

  function update(dt) {
    if (!scene) return;
    scene.t += dt;
    const t = scene.t;
    if (scene.kind === 'mate') {
      const p = Math.min(1, t / T.fallSec);
      let angle = T.fallAngle * easeIn(p);
      if (p >= 1) {
        if (!scene.hit) hit(scene);
        const b = Math.min(1, (t - T.fallSec) / T.bounceSec);
        angle = T.fallAngle - Math.sin(b * Math.PI) * T.bounce;
      }
      lean(scene.poses[0], angle);
      if (scene.hit) {
        const w = Math.min(1, (t - T.fallSec) / T.warmSec);
        const glow = reduced() ? 0 : Math.sin(w * Math.PI) * T.warmPeak;
        coolSquares(w >= 1 ? 0 : glow);
      }
    } else {
      const p = Math.min(1, t / T.leanSec);
      for (const pose of scene.poses) lean(pose, T.leanAngle * easeOut(p));
    }
  }

  const unsubs = [];
  if (bus && typeof bus.on === 'function') {
    unsubs.push(bus.on('gameover', (p) => begin(p && p.result, p && p.winner)));
    unsubs.push(bus.on('local', () => standUp()));
  }

  return {
    update,
    /** For the harness. */
    stats() {
      if (!scene) return { kind: null, posed: posed.length };
      return {
        kind: scene.kind, t: +scene.t.toFixed(3), hit: !!scene.hit,
        kings: scene.poses.map((q) => ({ side: q.piece.userData.side, y: +q.piece.position.y.toFixed(3), up: +UP.clone().applyQuaternion(q.piece.quaternion).y.toFixed(3) })),
      };
    },
    dispose() { for (const u of unsubs) if (typeof u === 'function') u(); standUp(); },
  };
}
