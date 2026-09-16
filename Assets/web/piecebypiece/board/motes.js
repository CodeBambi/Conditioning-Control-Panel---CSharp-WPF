/* ============================================================================
 * board/motes.js - dust in the air of the room.
 *
 * board/room.js gave the board a floor and a sky; on their own they read as a
 * gradient. This is the air between: a few hundred motes drifting slowly
 * around and above the board, catching the light, so the room has depth and
 * the camera has something to move against. They are scenery under Law III
 * and never react to the game (board/dust.js is the dust that does - it
 * puffs when a man lands, and this file is deliberately not that).
 *
 * All the motes live in ONE THREE.Points; the vertex shader moves each one
 * from the clock and its own seed, so a frame costs one draw call and no
 * attribute upload. A mote is a soft additive disc: it fades out when it
 * would come too close to the lens (a blob), when it is far enough to be in
 * the fog, and it twinkles a little on its own clock. Reduced motion freezes
 * the drift and the twinkle; the motes stay, still.
 *
 * Gate: window.PBP.settings.motes (default true).
 *
 * Wiring: boot.js builds one after the room and pumps update(dt, camera,
 * renderer) before render, like dust.js.
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number the air is made of. One place, on purpose. */
export const MOTES = Object.freeze({
  count: 440,
  box: [11, 6.0, 11],      // half-extents in x, y (from floor up), z
  floorY: -0.5,            // the lowest a mote floats
  sizeMin: 0.035,          // world units at distance 1 (see uScale)
  sizeMax: 0.115,
  drift: 0.55,             // how far a mote wanders sideways, world units
  driftRate: 0.09,         // and how fast, cycles per second (slow)
  rise: 0.06,              // world units per second, straight up, wrapping
  twinkle: 0.45,           // depth of the flicker, 0..1
  alpha: 0.58,             // the brightest a mote gets
  pinkShare: 0.3,          // the rest are cream
  cream: 0xF5E6C8,
  pink: 0xFF8FCB,
  nearFade: [1.2, 3.2],    // gone this close to the lens, full this far
  renderOrder: 4,          // after the room, before the landing dust
});

const T = MOTES;

const VERT = /* glsl */`
attribute float aSeed;
attribute float aSize;
attribute vec3 aColor;
uniform float uTime;
uniform float uScale;
uniform float uFogNear;
uniform float uFogFar;
uniform float uNear0;
uniform float uNear1;
uniform float uDrift;
uniform float uRate;
uniform float uRise;
uniform float uTwinkle;
uniform float uFloor;
uniform float uHeight;
varying float vAlpha;
varying vec3 vColor;
void main() {
  vColor = aColor;
  float s = aSeed * 6.2831853;
  float t = uTime;
  vec3 p = position;
  // a slow wander: two sines at unrelated rates, phase from the seed
  p.x += sin(t * uRate * (0.7 + aSeed * 0.6) + s) * uDrift;
  p.z += cos(t * uRate * (0.5 + aSeed * 0.9) + s * 1.7) * uDrift;
  // and a slower rise that wraps inside the box, so the air is never empty
  p.y = uFloor + mod(p.y - uFloor + t * uRise * (0.6 + aSeed * 0.8) + aSeed * uHeight, uHeight);
  vec4 mv = modelViewMatrix * vec4(p, 1.0);
  float depth = -mv.z;
  float near = smoothstep(uNear0, uNear1, depth);
  float far = 1.0 - smoothstep(uFogNear, uFogFar, depth);
  float wink = 1.0 - uTwinkle * (0.5 + 0.5 * sin(t * (1.1 + aSeed * 1.7) + s * 2.3));
  vAlpha = near * far * wink;
  gl_PointSize = aSize * uScale / max(depth, 0.05);
  gl_Position = projectionMatrix * mv;
}`;

const FRAG = /* glsl */`
uniform float uAlpha;
varying float vAlpha;
varying vec3 vColor;
void main() {
  float d = length(gl_PointCoord - 0.5) * 2.0;
  float a = smoothstep(1.0, 0.15, d) * vAlpha * uAlpha;
  if (a < 0.004) discard;
  gl_FragColor = vec4(vColor * a, a);
}`;

function motesOn() {
  const s = (typeof window !== 'undefined' && window.PBP && window.PBP.settings) || {};
  return s.motes !== false;
}

function reduced() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if (s && s.reducedMotion) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/** A small deterministic rng, so a harness photographs the same air twice. */
function rng(seed = 7) {
  let x = seed >>> 0 || 1;
  return () => { x ^= x << 13; x >>>= 0; x ^= x >> 17; x ^= x << 5; x >>>= 0; return x / 4294967296; };
}

/**
 * createMotes({ scene, seed }) -> { points, update, stats, dispose }
 */
export function createMotes({ scene, seed = 7 }) {
  const rnd = rng(seed);
  const N = T.count;
  const pos = new Float32Array(N * 3);
  const seeds = new Float32Array(N);
  const size = new Float32Array(N);
  const color = new Float32Array(N * 3);
  const cream = new THREE.Color(T.cream);
  const pink = new THREE.Color(T.pink);
  const height = T.box[1];
  for (let i = 0; i < N; i++) {
    pos[i * 3] = (rnd() * 2 - 1) * T.box[0];
    pos[i * 3 + 1] = T.floorY + rnd() * height;
    pos[i * 3 + 2] = (rnd() * 2 - 1) * T.box[2];
    seeds[i] = rnd();
    // more small motes than big ones
    size[i] = T.sizeMin + (T.sizeMax - T.sizeMin) * Math.pow(rnd(), 2.2);
    const c = rnd() < T.pinkShare ? pink : cream;
    color[i * 3] = c.r; color[i * 3 + 1] = c.g; color[i * 3 + 2] = c.b;
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('aSeed', new THREE.BufferAttribute(seeds, 1));
  geo.setAttribute('aSize', new THREE.BufferAttribute(size, 1));
  geo.setAttribute('aColor', new THREE.BufferAttribute(color, 3));
  geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, height / 2, 0), 20);

  const uniforms = {
    uTime: { value: 0 }, uScale: { value: 400 },
    uFogNear: { value: 12 }, uFogFar: { value: 34 },
    uNear0: { value: T.nearFade[0] }, uNear1: { value: T.nearFade[1] },
    uDrift: { value: T.drift }, uRate: { value: T.driftRate * 6.2831853 }, uRise: { value: T.rise },
    uTwinkle: { value: T.twinkle }, uFloor: { value: T.floorY }, uHeight: { value: height },
    uAlpha: { value: T.alpha },
  };
  const points = new THREE.Points(geo, new THREE.ShaderMaterial({
    uniforms, vertexShader: VERT, fragmentShader: FRAG,
    transparent: true, depthWrite: false, depthTest: true,
    blending: THREE.AdditiveBlending, toneMapped: false,
  }));
  points.frustumCulled = false;
  points.renderOrder = T.renderOrder;
  points.name = 'pbp-motes';
  scene.add(points);

  let clock = 0;
  let still = false;
  let hpx = 0;

  function update(dt, camera, renderer) {
    const on = motesOn();
    points.visible = on;
    if (!on) return;
    still = reduced();
    if (!still) clock += dt;
    uniforms.uTime.value = clock;
    if (scene.fog && scene.fog.isFog) { uniforms.uFogNear.value = scene.fog.near; uniforms.uFogFar.value = scene.fog.far; }
    if (camera && renderer) {
      const h = renderer.domElement.height || 0;
      if (h !== hpx) { hpx = h; }
      // pixels per world unit at distance 1: the same reading dust.js takes
      uniforms.uScale.value = (hpx * (camera.projectionMatrix.elements[5] || 1)) / 2;
    }
  }

  function stats() {
    return { count: N, visible: points.visible, still, clock: +clock.toFixed(2), scale: +uniforms.uScale.value.toFixed(1) };
  }

  function dispose() {
    scene.remove(points);
    geo.dispose();
    points.material.dispose();
  }

  return { points, update, stats, dispose };
}
