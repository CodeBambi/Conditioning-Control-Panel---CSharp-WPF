/* ============================================================================
 * board/scene.js - renderer, camera rig, lights, and the board and its plinth.
 *
 * Coordinate law for the whole game: one square is 1.0 world unit, the board is
 * centred on the origin, +Y is up. File a..h maps to x -3.5..3.5, rank 1..8 maps
 * to z 3.5..-3.5, so rank 1 (white's home) is the near edge of the +Z side.
 * ==========================================================================*/

import * as THREE from 'three';
import { createCameraRig } from './camera.js';

export const SQUARE = 1.0;
export const FILES = 'abcdefgh';

const CREAM = 0xF5E6C8;
const PINK = 0xFF69B4;
const BACKDROP = 0x1A1A3E;

/** Every number the lighting rig is made of. One place, on purpose. */
export const LIGHT = Object.freeze({
  shadowMap: 2048,          // texels per side of the key light's shadow map
  shadowHalfExtent: 5.2,    // the frustum, in squares from the middle out
  shadowBias: -0.0006,      // pulls the comparison off the surface it came from
  shadowNormalBias: 0.02,   // and walks the lookup out along the normal
  skyColor: 0x9E93E8,       // the room over the board
  groundColor: 0x4A3A5E,    // and the bounce off the plinth, warmer than it was
  hemi: 0.72,
  keyColor: 0xFFF3E4,
  key: 2.05,
  fillColor: 0xCBB9FF,      // cool lavender, opposite the key
  fill: 0.58,               // low: it is there to stop a flank going black
  rimColor: 0xFF69B4,
  rim: 1.15,
  // board/env.js hangs a little studio room on scene.environment. The men take
  // it at their own strength; the board sets its own here, because a square is
  // paint under a clearcoat and taking the room whole simply washes it out.
  envSquares: 0.16,        // a hint in the gloss, not a second light
  envPlinth: 0.55,         // the dark box wants it: it is what gives it an edge
});

/** The plinth the board is bedded into. */
const PLINTH = Object.freeze({
  half: 4.7,        // out to the widest point, which is halfway up the wall
  corner: 0.26,     // rounded, so the four corners are not a knife
  deep: 0.40,       // plus a bevel at each end: 0.5 of plinth in total
  bevel: 0.05,
  top: 0x2E2757,    // the face the board sits on, lighter than the walls
  side: 0x1B1738,   // and the walls, which are what the room falls away into
});

/** A square with rounded corners, centred on the origin, as a THREE.Shape. */
function roundedSquare(half, r) {
  const s = new THREE.Shape();
  s.moveTo(-half + r, -half);
  s.lineTo(half - r, -half);
  s.quadraticCurveTo(half, -half, half, -half + r);
  s.lineTo(half, half - r);
  s.quadraticCurveTo(half, half, half - r, half);
  s.lineTo(-half + r, half);
  s.quadraticCurveTo(-half, half, -half, half - r);
  s.lineTo(-half, -half + r);
  s.quadraticCurveTo(-half, -half, -half + r, -half);
  return s;
}

/** Square name ("e4") to the world position of its centre. */
export function squareToWorld(sq, y = 0, out = new THREE.Vector3()) {
  const f = FILES.indexOf(sq[0]);
  const r = Number(sq[1]) - 1;
  return out.set((f - 3.5) * SQUARE, y, (3.5 - r) * SQUARE);
}

/** World x/z to a square name, or null when the point is off the board. */
export function worldToSquare(x, z) {
  const f = Math.round(x / SQUARE + 3.5);
  const r = Math.round(3.5 - z / SQUARE);
  if (f < 0 || f > 7 || r < 0 || r > 7) return null;
  return FILES[f] + (r + 1);
}

export function createScene({ canvas }) {
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.06;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(BACKDROP);
  scene.fog = new THREE.Fog(BACKDROP, 14, 30);

  const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 120);

  // --- lights: a soft key over the board, a fill facing it, a pink rim behind -
  // A body this thick has no truly black side. The hemisphere is warmer and
  // brighter at the ground than it was, so an underside picks up the plinth
  // instead of the void, and the fill stands opposite the key at about a
  // quarter of its strength: enough to find the far edge of a man, not enough
  // to flatten the modelling the key is doing. The fill casts nothing.
  const hemi = new THREE.HemisphereLight(LIGHT.skyColor, LIGHT.groundColor, LIGHT.hemi);
  scene.add(hemi);
  const key = new THREE.DirectionalLight(LIGHT.keyColor, LIGHT.key);
  key.position.set(4.5, 9.5, 5.5);
  key.castShadow = true;
  // 2048 over a frustum drawn to the board, instead of 1024 over one with three
  // squares of empty air on every side. A shadow texel is now about 5 mm of a
  // 1.0 square, so a bent tip casts a bent tip and not a stair.
  key.shadow.mapSize.set(LIGHT.shadowMap, LIGHT.shadowMap);
  key.shadow.radius = 3;
  // The men are round and glossy, which is the shape self-shadowing goes wrong
  // on. Without these the map wrote a whole flank of every piece into its own
  // shadow and the man read as a black shell; normalBias walks the lookup off
  // the surface along its normal, which is what a curved caster wants.
  key.shadow.bias = LIGHT.shadowBias;
  key.shadow.normalBias = LIGHT.shadowNormalBias;
  const cam = key.shadow.camera;
  const half = LIGHT.shadowHalfExtent;
  cam.left = -half; cam.right = half; cam.top = half; cam.bottom = -half;
  cam.near = 1; cam.far = 26;
  // The three directionals and their targets ride in ONE group, so the whole
  // rig can be swung about +Y as a unit: board/room.js turns it to stand
  // behind whoever is to move, and key, fill and rim keep their places
  // relative to each other. At rotation 0 nothing about the light has moved.
  const lightRig = new THREE.Group();
  lightRig.name = 'pbp-light-rig';
  lightRig.add(key, key.target);
  const fill = new THREE.DirectionalLight(LIGHT.fillColor, LIGHT.fill);
  fill.position.set(-5.0, 4.0, 4.5);
  fill.castShadow = false;
  lightRig.add(fill, fill.target);
  const rim = new THREE.DirectionalLight(LIGHT.rimColor, LIGHT.rim);
  rim.position.set(-5.5, 3.2, -6.5);
  lightRig.add(rim, rim.target);
  scene.add(lightRig);

  // --- board ----------------------------------------------------------------
  const boardGroup = new THREE.Group();
  const pieceGroup = new THREE.Group();
  scene.add(boardGroup, pieceGroup);

  // The plinth. It used to be a plain box, which from any angle read as a
  // shadow with a board floating on it. It is an extruded rounded square now,
  // bevelled top and bottom: the bevel is a real facet, so the key light finds
  // the far corner and the rim light draws a line down the near edge, and the
  // top face is its own material a shade lighter than the walls, which is what
  // tells you the board is standing ON something.
  const plinthShape = roundedSquare(PLINTH.half, PLINTH.corner);
  const plinthGeo = new THREE.ExtrudeGeometry(plinthShape, {
    depth: PLINTH.deep, curveSegments: 8, bevelEnabled: true, bevelSegments: 2,
    bevelThickness: PLINTH.bevel, bevelSize: PLINTH.bevel, bevelOffset: 0,
  });
  plinthGeo.rotateX(-Math.PI / 2);      // extruded along +Z, stood up along +Y
  const plinthTopMat = new THREE.MeshPhysicalMaterial({
    color: PLINTH.top, roughness: 0.38, clearcoat: 0.45, clearcoatRoughness: 0.25,
    envMapIntensity: LIGHT.envPlinth,
  });
  const plinthSideMat = new THREE.MeshPhysicalMaterial({
    color: PLINTH.side, roughness: 0.52, clearcoat: 0.3,
    envMapIntensity: LIGHT.envPlinth * 0.8,
  });
  // ExtrudeGeometry groups the two lids as material 0 and the walls, bevel and
  // all, as material 1. The bottom lid is never seen, so the top face and the
  // one nobody looks at can happily share.
  const plinth = new THREE.Mesh(plinthGeo, [plinthTopMat, plinthSideMat]);
  // The geometry runs from -bevel to deep + bevel, so its top lands on -0.08:
  // the height the squares are bedded into, exactly where the old box top was.
  plinth.position.y = -(PLINTH.deep + PLINTH.bevel) - 0.08;
  plinth.receiveShadow = true;
  boardGroup.add(plinth);

  const squareGeo = new THREE.BoxGeometry(SQUARE, 0.16, SQUARE);
  const squares = new Map();
  for (let f = 0; f < 8; f++) {
    for (let r = 0; r < 8; r++) {
      const light = (f + r) % 2 === 1; // a1 is dark, h1 is light
      const mat = new THREE.MeshPhysicalMaterial({
        color: light ? CREAM : PINK, roughness: light ? 0.42 : 0.34, clearcoat: 0.5,
        clearcoatRoughness: 0.3, envMapIntensity: LIGHT.envSquares,
      });
      const mesh = new THREE.Mesh(squareGeo, mat);
      mesh.position.set((f - 3.5) * SQUARE, -0.08, (3.5 - r) * SQUARE);
      mesh.receiveShadow = true;
      const name = FILES[f] + (r + 1);
      mesh.userData.square = name;
      mesh.userData.base = mat.color.clone();
      boardGroup.add(mesh);
      squares.set(name, mesh);
    }
  }

  /**
   * Legal-move hints. `list` is [{square, kind, hover}]; the hints themselves
   * are objects standing on the board, drawn by board/markers.js, because an
   * emissive tint on a pink or cream square cannot be seen. drag.js owns the
   * markers and installs them here, so a caller holding only the view (the
   * effects layer, through board.setHighlights) still lights squares up.
   */
  let markerSink = null;
  let waiting = null;
  function setHighlights(list = []) {
    if (markerSink) markerSink(list);
    else waiting = list;          // asked before the markers existed; keep it
  }
  function setHighlightSink(fn) {
    markerSink = fn || null;
    if (markerSink && waiting) { markerSink(waiting); waiting = null; }
  }

  // --- camera rig -----------------------------------------------------------
  // The rig itself is camera.js: an orbit the player drives with the right
  // button, the wheel and a touch, plus the four view presets. What the game
  // always did is layered back on top of it - the swing round behind whoever
  // is to move, and the ramp's sway as an additive wobble applied last.
  const rig = createCameraRig({ camera, canvas });
  let sway = 0;
  let clock = 0;

  function setSide(side, instant = false) { rig.setSide(side, instant); }
  function updateCamera(dt) { rig.update(dt, sway, clock); }

  function resize() {
    const w = canvas.clientWidth || window.innerWidth;
    const h = canvas.clientHeight || window.innerHeight;
    renderer.setSize(w, h, false);
    camera.aspect = w / Math.max(1, h);
    camera.updateProjectionMatrix();
  }
  window.addEventListener('resize', resize);
  resize();

  function update(dt) {
    clock += dt;
    updateCamera(dt);
  }

  function render() { renderer.render(scene, camera); }

  // --- projection (the effects layer draws DOM over these) ------------------
  const tmp = new THREE.Vector3();
  function projectPoint(v) {
    tmp.copy(v).project(camera);
    const r = canvas.getBoundingClientRect();
    return { x: r.left + (tmp.x * 0.5 + 0.5) * r.width, y: r.top + (-tmp.y * 0.5 + 0.5) * r.height };
  }
  // Default height is roughly where a man's middle sits, which is what the
  // effects layer wants to draw over. Pass 0 for the square itself.
  function projectSquare(sq, height = 0.45) {
    if (!sq || !squares.has(sq)) return { x: 0, y: 0 };
    return projectPoint(squareToWorld(sq, height, tmp.clone()));
  }

  function dispose() {
    window.removeEventListener('resize', resize);
    rig.dispose();
    renderer.dispose();
  }

  return {
    renderer, scene, camera, boardGroup, pieceGroup, squares,
    lightRig, lights: { hemi, key, fill, rim },
    update, render, resize, dispose,
    setSide, setHighlights, setHighlightSink, projectPoint, projectSquare,
    cameraRig: rig,
    setCameraSway(v) { sway = Math.max(0, Math.min(1, Number(v) || 0)); },
  };
}
