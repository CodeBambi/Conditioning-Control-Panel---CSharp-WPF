/* ============================================================================
 * board/scene.js - renderer, camera rig, lights and the board itself.
 *
 * Coordinate law for the whole game: one square is 1.0 world unit, the board is
 * centred on the origin, +Y is up. File a..h maps to x -3.5..3.5, rank 1..8 maps
 * to z 3.5..-3.5, so rank 1 (white's home) is the near edge of the +Z side.
 * ==========================================================================*/

import * as THREE from 'three';

export const SQUARE = 1.0;
export const FILES = 'abcdefgh';

const CREAM = 0xF5E6C8;
const PINK = 0xFF69B4;
const BACKDROP = 0x1A1A3E;

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

const easeInOut = (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

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

  // --- lights: one soft key over the board, one pink rim from behind ---------
  scene.add(new THREE.HemisphereLight(0x8f86d6, 0x141433, 0.55));
  const key = new THREE.DirectionalLight(0xFFF3E4, 2.1);
  key.position.set(4.5, 9.5, 5.5);
  key.castShadow = true;
  key.shadow.mapSize.set(1024, 1024);
  key.shadow.radius = 3;
  const cam = key.shadow.camera;
  cam.left = -7; cam.right = 7; cam.top = 7; cam.bottom = -7; cam.near = 1; cam.far = 26;
  scene.add(key, key.target);
  const rim = new THREE.DirectionalLight(PINK, 1.15);
  rim.position.set(-5.5, 3.2, -6.5);
  scene.add(rim, rim.target);

  // --- board ----------------------------------------------------------------
  const boardGroup = new THREE.Group();
  const pieceGroup = new THREE.Group();
  scene.add(boardGroup, pieceGroup);

  const plinthMat = new THREE.MeshPhysicalMaterial({ color: 0x241F45, roughness: 0.45, clearcoat: 0.35 });
  const plinth = new THREE.Mesh(new THREE.BoxGeometry(9.4, 0.5, 9.4), plinthMat);
  plinth.position.y = -0.33;
  plinth.receiveShadow = true;
  boardGroup.add(plinth);

  const squareGeo = new THREE.BoxGeometry(SQUARE, 0.16, SQUARE);
  const squares = new Map();
  for (let f = 0; f < 8; f++) {
    for (let r = 0; r < 8; r++) {
      const light = (f + r) % 2 === 1; // a1 is dark, h1 is light
      const mat = new THREE.MeshPhysicalMaterial({
        color: light ? CREAM : PINK, roughness: light ? 0.42 : 0.34, clearcoat: 0.5, clearcoatRoughness: 0.3,
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
  // Sits behind whichever side is to move and swings around with a small dolly.
  const RADIUS = 9.7;
  const HEIGHT = 7.0;
  const LOOK = new THREE.Vector3(0, 0.10, 0);
  let angleFrom = 0;
  let angleTo = 0;
  let swing = 1;
  let sway = 0;
  let clock = 0;
  const lookAt = new THREE.Vector3();

  function setSide(side, instant = false) {
    const to = side === 'b' ? Math.PI : 0;
    if (Math.abs(to - angleTo) < 1e-6 && swing >= 1) { if (instant) swing = 1; return; }
    angleFrom = THREE.MathUtils.lerp(angleFrom, angleTo, easeInOut(Math.min(swing, 1)));
    angleTo = to;
    swing = instant ? 1 : 0;
  }

  function updateCamera(dt) {
    if (swing < 1) swing = Math.min(1, swing + dt / 0.9);
    const e = easeInOut(swing);
    const ang = THREE.MathUtils.lerp(angleFrom, angleTo, e);
    const dolly = 1 + 0.12 * Math.sin(Math.PI * e) * (swing < 1 ? 1 : 0);
    const swayX = Math.sin(clock * 0.53) * 0.55 * sway;
    const swayY = Math.sin(clock * 0.37 + 1.1) * 0.28 * sway;
    camera.position.set(Math.sin(ang) * RADIUS * dolly + swayX, HEIGHT * dolly + swayY, Math.cos(ang) * RADIUS * dolly);
    lookAt.copy(LOOK);
    lookAt.x += Math.sin(clock * 0.29) * 0.4 * sway;
    camera.lookAt(lookAt);
    camera.rotateZ(Math.sin(clock * 0.23) * 0.035 * sway);
  }

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
    renderer.dispose();
  }

  return {
    renderer, scene, camera, boardGroup, pieceGroup, squares,
    update, render, resize, dispose,
    setSide, setHighlights, setHighlightSink, projectPoint, projectSquare,
    setCameraSway(v) { sway = Math.max(0, Math.min(1, Number(v) || 0)); },
  };
}
