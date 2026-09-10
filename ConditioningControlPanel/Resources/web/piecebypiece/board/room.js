/* ============================================================================
 * board/room.js - the room the board stands in, and how it answers the game.
 *
 * Until now the board sat in a flat navy void: one background colour and a
 * fog of the same colour. env.js gave the crowns a studio to reflect, but it
 * was rendered once into a cube map and nobody could see it. This is the room
 * the eye gets:
 *
 *   the FLOOR    a wide dark glossy plane the plinth stands on, taking the
 *                same studio room in its gloss that the men take, and melting
 *                into the horizon under the existing fog;
 *   the POOL     a soft pool of shade on the floor around the plinth, so the
 *                box is standing on something and not hovering over it;
 *   the DOME     a sphere round everything, plum at the horizon, a pink haze
 *                just above it, black overhead. Drawn by one small shader.
 *
 * And the first of the room's reactions, the TURN TELL (PBP-BEATS beat 6):
 *
 *   the LEAN     the light rig (key, fill and rim together, scene.js groups
 *                them) swings round to stand behind whoever is to move, over
 *                leanMs. From the fixed camera that keeps the light on the
 *                mover's shoulder as the view swings; from the top or a free
 *                camera the shadows sweeping round IS the tell;
 *   the EDGE     a pink line on the bevel of the plinth on the mover's side,
 *                full on the first frame of the turn and easing to a resting
 *                glow, while the other edge waits dim.
 *
 * Reduced motion (window.PBP.settings.reducedMotion) takes the state and not
 * the travel: the rig and the edges land at once.
 *
 * Wiring: boot.js builds one after the scene, forwards `turn`, `local` and
 * `gameover` off the bus, and pumps update(dt) before render. env.js dresses
 * the floor with the cube map whichever of the two lands first.
 * ==========================================================================*/

import * as THREE from 'three';
import { ROOM, createTweens, yawFor, glowTargets, easeOutCubic } from './roomMath.js';

export { ROOM } from './roomMath.js';

const T = ROOM;

function reducedMotion() {
  try {
    const s = window.PBP && window.PBP.settings;
    if (s && s.reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

// --- the dome ---------------------------------------------------------------
// Colours come in as linear THREE.Colors and go out through the renderer's
// colour space chunk but NOT its tone mapping: three converts the fog colour
// to the output space and applies it after tone mapping, so a tone-mapped
// dome could never meet the fully fogged floor at the horizon without a
// seam. Untouched, the horizon IS the fog colour, on screen and inside
// bloom.js's composer alike (where neither is tone mapped until the output
// pass, which then does both the same).
const domeVertex = /* glsl */`
  varying vec3 vDir;
  void main() {
    vDir = normalize((modelMatrix * vec4(position, 1.0)).xyz);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
  }`;
const domeFragment = /* glsl */`
  #include <common>
  uniform vec3 horizon; uniform vec3 haze; uniform vec3 zenith; uniform vec3 under;
  uniform float hazeAt; uniform float zenithAt; uniform float underAt;
  varying vec3 vDir;
  void main() {
    float h = normalize(vDir).y;
    vec3 c;
    if (h < 0.0) {
      c = mix(horizon, under, smoothstep(0.0, underAt, -h));
    } else {
      c = mix(horizon, haze, smoothstep(0.0, hazeAt, h));
      c = mix(c, zenith, smoothstep(hazeAt, zenithAt, h));
    }
    gl_FragColor = vec4(c, 1.0);
    #include <colorspace_fragment>
  }`;

function buildDome() {
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      horizon: { value: new THREE.Color(T.horizon) },
      haze: { value: new THREE.Color(T.haze) },
      zenith: { value: new THREE.Color(T.zenith) },
      under: { value: new THREE.Color(T.under) },
      hazeAt: { value: T.hazeAt }, zenithAt: { value: T.zenithAt }, underAt: { value: T.underAt },
    },
    vertexShader: domeVertex, fragmentShader: domeFragment,
    side: THREE.BackSide, depthWrite: false, fog: false,
  });
  mat.toneMapped = false;
  const dome = new THREE.Mesh(new THREE.SphereGeometry(T.domeRadius, 48, 24), mat);
  dome.name = 'pbp-dome';
  dome.frustumCulled = false;
  dome.renderOrder = -10;
  return dome;
}

// --- the floor and the pool of shade under the plinth ----------------------
function buildFloor() {
  const mat = new THREE.MeshPhysicalMaterial({
    color: T.floorColor, roughness: T.floorRough, metalness: 0,
    clearcoat: T.floorClearcoat, clearcoatRoughness: T.floorClearcoatRough,
    envMapIntensity: T.floorEnv,
  });
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(T.floorSize, T.floorSize).rotateX(-Math.PI / 2), mat);
  floor.position.y = T.floorY;
  floor.receiveShadow = true;
  floor.name = 'pbp-floor';
  return floor;
}

/** A blurred dark rounded square, drawn once, laid on the floor. */
function buildPool() {
  const px = T.poolPx;
  let texture = null;
  try {
    const canvas = document.createElement('canvas');
    canvas.width = px; canvas.height = px;
    const ctx = canvas.getContext('2d');
    const inset = px * T.poolInset;
    const r = px * 0.06;
    ctx.shadowColor = 'rgba(0, 0, 0, 1)';
    ctx.shadowBlur = inset * 0.9;
    ctx.fillStyle = 'rgba(0, 0, 0, 1)';
    // three passes stack the blur into a soft falloff instead of one hard-edged smear
    for (let i = 0; i < 3; i++) {
      ctx.beginPath();
      ctx.roundRect(inset, inset, px - inset * 2, px - inset * 2, r);
      ctx.fill();
    }
    texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
  } catch { return null; }   // no 2D canvas (a stripped-down harness): no pool, no harm
  const mat = new THREE.MeshBasicMaterial({
    map: texture, alphaMap: texture, color: 0x000000, transparent: true, opacity: T.poolAlpha,
    depthWrite: false, toneMapped: false,
  });
  const pool = new THREE.Mesh(new THREE.PlaneGeometry(T.poolSize, T.poolSize).rotateX(-Math.PI / 2), mat);
  pool.position.y = T.floorY + 0.004;
  pool.renderOrder = -5;
  pool.name = 'pbp-pool';
  return pool;
}

// --- the edge glow: one line per side, laid on the plinth's top bevel ------
// The bevel is 0.05 out and 0.05 down from the lid's edge at 4.7 / -0.08, so
// its slope is centred at 4.725 / -0.105 and faces up-and-out at 45 degrees.
// The bar floats a hair off that slope along its normal: on the surface
// itself it z-fought the plinth and came through in flickers.
function buildEdge(side) {
  const holder = new THREE.Group();
  holder.rotation.y = side === 'b' ? Math.PI : 0;
  const mat = new THREE.MeshBasicMaterial({
    color: T.glowColor, transparent: true, opacity: T.glowOff,
    depthWrite: false, toneMapped: false, side: THREE.DoubleSide,
  });
  const bar = new THREE.Mesh(new THREE.PlaneGeometry(T.glowLength, T.glowWidth), mat);
  bar.position.set(0, -0.105 + T.glowLift, 4.725 + T.glowLift);
  bar.rotation.x = -Math.PI / 4;
  bar.name = 'pbp-edge-' + side;
  holder.add(bar);
  return { holder, mat };
}

/**
 * createRoom({ view, bus, env }) -> { group, update, lean, tell, settle, debug, dispose }
 *
 * `view` is board/scene.js (its lightRig is what leans); `env` is a getter
 * for board/env.js, which may or may not have landed yet.
 */
export function createRoom({ view, bus, env = () => null }) {
  const scene = view.scene;
  const group = new THREE.Group();
  group.name = 'pbp-room';
  const dome = buildDome();
  const floor = buildFloor();
  const pool = buildPool();
  group.add(dome, floor);
  if (pool) group.add(pool);
  const edges = { w: buildEdge('w'), b: buildEdge('b') };
  group.add(edges.w.holder, edges.b.holder);
  scene.add(group);

  // The fog and the background now belong to the horizon, so the far floor
  // melts into exactly the colour the dome shows behind it.
  const horizon = new THREE.Color(T.horizon);
  scene.background = horizon.clone();
  scene.fog = new THREE.Fog(horizon.clone(), T.fogNear, T.fogFar);

  // env.js hands the floor the cube map if it is already here; if it lands
  // later, boot.js asks it to dress this group too.
  const e = env();
  if (e && e.dressBoard) e.dressBoard(group);

  const tweens = createTweens();
  const rig = view.lightRig || null;
  const state = { side: 'w', yaw: 0 };
  const yaw = { value: 0 };   // tweened, then written onto the rig each frame

  /** Swing the rig to stand behind `side`. */
  function lean(side, instant = false) {
    state.side = side;
    const want = yawFor(side);
    tweens.to(yaw, 'value', want, instant || reducedMotion() ? 0 : T.leanMs);
  }

  /** The mover's edge hits full and settles; the other waits. null = over. */
  function tell(side, instant = false) {
    const want = glowTargets(side);
    const ms = instant || reducedMotion() ? 0 : T.glowMs;
    for (const s of ['w', 'b']) {
      const m = edges[s].mat;
      if (s === side) m.opacity = T.glowHit;   // frame one, before the ease
      tweens.to(m, 'opacity', want[s], ms, easeOutCubic);
    }
  }

  const unsubs = [];
  if (bus && bus.on) {
    unsubs.push(bus.on('turn', (p) => { const s = p && p.side === 'b' ? 'b' : 'w'; lean(s); tell(s); }));
    unsubs.push(bus.on('local', () => { lean('w', true); tell(null, true); }));
    unsubs.push(bus.on('gameover', () => { tell(null); }));
  }

  function update(dt) {
    tweens.update(dt);
    if (rig) rig.rotation.y = yaw.value;
    state.yaw = yaw.value;
  }

  /** Jump every ease to its end, for a still frame. */
  function settle() { tweens.settle(); update(0); }

  function debug() {
    return {
      side: state.side,
      yawDeg: +((yaw.value * 180) / Math.PI).toFixed(1),
      glow: { w: +edges.w.mat.opacity.toFixed(3), b: +edges.b.mat.opacity.toFixed(3) },
      tweens: tweens.count(),
      pool: !!pool,
      fog: scene.fog ? [scene.fog.near, scene.fog.far] : null,
    };
  }

  function dispose() {
    for (const off of unsubs) { try { off(); } catch { /* already gone */ } }
    scene.remove(group);
    group.traverse((o) => {
      if (o.geometry) o.geometry.dispose();
      if (o.material) { if (o.material.map) o.material.map.dispose(); o.material.dispose(); }
    });
    if (rig) rig.rotation.y = 0;
  }

  return { group, dome, floor, pool, edges, update, lean, tell, settle, debug, dispose };
}
