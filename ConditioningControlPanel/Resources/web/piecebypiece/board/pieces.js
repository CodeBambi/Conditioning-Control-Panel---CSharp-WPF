/* ============================================================================
 * board/pieces.js - the men on the board.
 *
 * Placeholder art: every type is a lathed profile (turned on a lathe, like real
 * wooden chessmen) in a glossy silicone material. If a matching glb turns up in
 * assets/pieces/ it is used instead, per type, without a reload. A glb must be a
 * single mesh, +Y up, origin at the centre of its base, height exactly 1.0; it
 * gets scaled to the type height below.
 * ==========================================================================*/

import * as THREE from 'three';
import { squareToWorld } from './scene.js';

export const TYPES = ['p', 'n', 'b', 'r', 'q', 'k'];
export const GLB_NAMES = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
export const HEIGHT = { p: 0.62, n: 0.80, b: 0.90, r: 0.70, q: 1.02, k: 1.15 };

// Lathe profiles in NORMALISED space: y runs 0 (base) to 1 (crown), x is the
// radius at that height. A piece is built at height 1 and then scaled by
// HEIGHT[type], which is exactly the contract a supplied glb has to meet.
const PROFILE = {
  p: [[0,0],[0.34,0],[0.36,0.06],[0.26,0.14],[0.16,0.22],[0.145,0.46],[0.22,0.53],[0.13,0.58],[0.10,0.66],[0.17,0.74],[0.19,0.84],[0.14,0.94],[0.06,0.99],[0,1]],
  r: [[0,0],[0.38,0],[0.40,0.07],[0.30,0.15],[0.26,0.58],[0.30,0.68],[0.38,0.72],[0.38,1.0],[0.30,1.0],[0.30,0.86],[0,0.84]],
  n: [[0,0],[0.37,0],[0.39,0.07],[0.29,0.15],[0.24,0.30],[0.21,0.42],[0,0.44]],
  b: [[0,0],[0.36,0],[0.38,0.06],[0.27,0.14],[0.17,0.26],[0.155,0.46],[0.24,0.54],[0.27,0.60],[0.20,0.64],[0.13,0.70],[0.15,0.80],[0.11,0.88],[0.06,0.95],[0.065,0.965],[0,0.97]],
  q: [[0,0],[0.38,0],[0.40,0.06],[0.29,0.14],[0.18,0.28],[0.16,0.48],[0.26,0.58],[0.30,0.64],[0.23,0.70],[0.28,0.78],[0.20,0.84],[0.10,0.88],[0.11,0.94],[0.06,0.97],[0,0.98]],
  k: [[0,0],[0.38,0],[0.40,0.06],[0.29,0.14],[0.19,0.28],[0.17,0.50],[0.27,0.60],[0.31,0.66],[0.24,0.72],[0.15,0.80],[0.13,0.86],[0.16,0.89],[0.10,0.90],[0,0.90]],
};

const SKIN = { w: { color: 0xFFF0F5, sheen: 0xFF9EC4 }, b: { color: 0x3B2A63, sheen: 0x7B6CFF } };

function lathe(points, segments = 40) {
  const geo = new THREE.LatheGeometry(points.map(([x, y]) => new THREE.Vector2(Math.max(x, 0.0001), y)), segments);
  geo.computeVertexNormals();
  return geo;
}

function material(side) {
  const skin = SKIN[side] || SKIN.w;
  return new THREE.MeshPhysicalMaterial({
    color: skin.color, roughness: 0.35, clearcoat: 0.6, clearcoatRoughness: 0.22, metalness: 0.0,
    sheen: 0.5, sheenColor: new THREE.Color(skin.sheen), sheenRoughness: 0.6,
    emissive: new THREE.Color(skin.sheen), emissiveIntensity: 0,
  });
}

/** Extra bits a lathe cannot turn, also in normalised space. */
function trim(type, mat) {
  const parts = [];
  if (type === 'n') {
    const neck = new THREE.Mesh(new THREE.CylinderGeometry(0.19, 0.23, 0.20, 20), mat);
    neck.position.y = 0.50;
    const head = new THREE.Mesh(new THREE.BoxGeometry(0.26, 0.46, 0.34), mat);
    head.position.set(0, 0.68, -0.03);
    head.rotation.x = -0.24;
    const snout = new THREE.Mesh(new THREE.BoxGeometry(0.23, 0.20, 0.30), mat);
    snout.position.set(0, 0.74, -0.26);
    snout.rotation.x = 0.22;
    const ear = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.18, 8), mat);
    ear.position.set(0, 0.94, 0.04);
    parts.push(neck, head, snout, ear);
  } else if (type === 'q') {
    const ball = new THREE.Mesh(new THREE.SphereGeometry(0.07, 16, 12), mat);
    ball.position.y = 1.0;
    parts.push(ball);
  } else if (type === 'k') {
    const up = new THREE.Mesh(new THREE.BoxGeometry(0.07, 0.20, 0.07), mat);
    up.position.y = 0.94;
    const across = new THREE.Mesh(new THREE.BoxGeometry(0.19, 0.065, 0.07), mat);
    across.position.y = 0.965;
    parts.push(up, across);
  } else if (type === 'b') {
    const ball = new THREE.Mesh(new THREE.SphereGeometry(0.058, 14, 10), mat);
    ball.position.y = 0.99;
    parts.push(ball);
  }
  return parts;
}

export function createPieces({ group, assetsBase = './assets/pieces/', hooks = {}, jiggle = null }) {
  const geoCache = new Map();
  const glb = new Map();          // type -> geometry from a supplied glb
  const bySquare = new Map();     // square -> piece object
  let wobble = 0;
  let clock = 0;

  function geometryFor(type) {
    if (glb.has(type)) return glb.get(type);
    if (!geoCache.has(type)) geoCache.set(type, lathe(PROFILE[type]));
    return geoCache.get(type);
  }

  function build(type, side) {
    const root = new THREE.Group();
    const mat = material(side);
    const body = new THREE.Mesh(geometryFor(type), mat);
    body.castShadow = true;
    body.receiveShadow = true;
    root.add(body);
    // Trim is baked into the body's own space before it joins the piece: the
    // flex shader reads position.y as a height up the piece, so a part that
    // carried its own offset would bend around the wrong origin.
    if (!glb.has(type)) for (const part of trim(type, mat)) {
      part.updateMatrix();
      part.geometry = part.geometry.clone().applyMatrix4(part.matrix);
      part.position.set(0, 0, 0);
      part.rotation.set(0, 0, 0);
      part.castShadow = true;
      root.add(part);
    }
    root.scale.setScalar(HEIGHT[type]);
    // Knights (and any modelled piece) look at the far side.
    root.rotation.y = side === 'w' ? 0 : Math.PI;
    root.userData = { type, side, material: mat, scaleBase: HEIGHT[type], phase: Math.random() * Math.PI * 2 };
    if (jiggle) jiggle.attach(root);
    return root;
  }

  function place(piece, square) {
    const p = squareToWorld(square, 0);
    piece.position.set(p.x, 0, p.z);
    piece.userData.home = { x: p.x, z: p.z };
    piece.userData.square = square;
  }

  /** Position map: { e1: {type:'k', side:'w'}, ... }. Rebuilds only what moved. */
  function setPosition(map) {
    for (const [sq, piece] of [...bySquare]) {
      const want = map[sq];
      if (!want || want.type !== piece.userData.type || want.side !== piece.userData.side) {
        group.remove(piece);
        bySquare.delete(sq);
      }
    }
    for (const sq of Object.keys(map)) {
      if (bySquare.has(sq)) continue;
      const { type, side } = map[sq];
      const piece = build(type, side);
      place(piece, sq);
      group.add(piece);
      bySquare.set(sq, piece);
    }
  }

  function pieceAt(sq) { return bySquare.get(sq) || null; }

  /** A man leaves the board. anim.js takes him if it wants a send-off. */
  function retire(piece) {
    if (hooks.onCaptured) hooks.onCaptured(piece);
    else group.remove(piece);
  }

  function move(from, to) {
    const piece = bySquare.get(from);
    if (!piece) return null;
    bySquare.delete(from);
    const taken = bySquare.get(to) || null;
    if (taken) { retire(taken); bySquare.delete(to); piece.userData.tookOne = true; }
    const was = piece.position.clone();
    place(piece, to);
    bySquare.set(to, piece);
    if (hooks.onMoved) hooks.onMoved(piece, was);
    return piece;
  }

  function remove(sq) {
    const piece = bySquare.get(sq);
    if (piece) { retire(piece); bySquare.delete(sq); }
    return piece || null;
  }

  function update(dt) {
    clock += dt;
    if (wobble <= 0.001) return;
    for (const piece of bySquare.values()) {
      if (piece.userData.held || piece.userData.busy) continue;
      const ph = piece.userData.phase;
      piece.rotation.z = Math.sin(clock * 1.7 + ph) * 0.055 * wobble;
      piece.rotation.x = Math.sin(clock * 1.3 + ph * 1.7) * 0.04 * wobble;
      piece.position.y = Math.abs(Math.sin(clock * 0.9 + ph)) * 0.025 * wobble;
    }
  }

  // --- optional glb art ------------------------------------------------------
  // Probed with HEAD first so a missing file is a quiet 404, not a loader throw.
  async function tryLoadGlb() {
    let loader = null;
    for (const type of TYPES) {
      const url = assetsBase + GLB_NAMES[type] + '.glb';
      try {
        const head = await fetch(url, { method: 'HEAD' });
        if (!head.ok) continue;
        if (!loader) {
          const mod = await import('three/addons/loaders/GLTFLoader.js');
          loader = new mod.GLTFLoader();
        }
        const gltf = await loader.loadAsync(url);
        let geo = null;
        gltf.scene.traverse((o) => { if (!geo && o.isMesh) geo = o.geometry; });
        if (!geo) continue;
        glb.set(type, geo);
        // Swap the art under any piece of this type already on the board.
        for (const [sq, piece] of [...bySquare]) {
          if (piece.userData.type !== type) continue;
          const side = piece.userData.side;
          group.remove(piece);
          bySquare.delete(sq);
          const next = build(type, side);
          place(next, sq);
          group.add(next);
          bySquare.set(sq, next);
        }
      } catch { /* no art for this type, the lathe stands in */ }
    }
  }

  // The flex system talks in pieces; the board talks in squares. This is the
  // translation, and it is what the harness and the ramp reach for.
  const jiggleApi = jiggle ? {
    poke(square, opts = {}) { jiggle.poke(bySquare.get(square), opts); },
    debug(square) { return jiggle.debug(bySquare.get(square)); },
    stats() { return jiggle.stats(); },
    system: jiggle,
  } : null;

  return {
    setPosition, pieceAt, move, remove, update, tryLoadGlb,
    pieces: bySquare,
    jiggle: jiggleApi,
    setWobble(v) {
      wobble = Math.max(0, Math.min(1, Number(v) || 0));
      if (jiggle) jiggle.setWobble(wobble);
    },
    getWobble() { return wobble; },
  };
}
