/* ============================================================================
 * tunnel.js - the endless tunnel for the Sissy Fall.
 *
 * Geometry strategy: a CLOSED-LOOP treadmill. One baked TubeGeometry wraps a
 * closed CatmullRom spine (integer sine harmonics -> the curve closes exactly);
 * the camera's endless `depth` maps onto the loop as depth % LOOP_DEPTH. Fog
 * limits sightlines to ~70u against a 1000u loop, and card content is always
 * novel from the media pool, so the geometry repetition is invisible.
 *
 * The shader is copied from js/rabbit-hole/scene.js:198-268 with the
 * branch-hole carving removed (this tunnel has no veins) and a pinker palette.
 * The ring / spiral coords derive from vUv with INTEGER uRings/uSpiralTurns/
 * uArms, so fract() is continuous across the vUv wrap - no seam at the loop.
 * ==========================================================================*/

import * as THREE from 'three';
import { Q } from '../shared/quality.js';

export const RADIUS = 5.5;
export const LOOP_DEPTH = 1000;         // world units around the loop
export const FOG_COLOR = 0x220d1e;      // deep pink-tinted haze
export const FOG_DENSITY = 0.02;

const SPINE_UP = new THREE.Vector3(0, 1, 0);

// The loop spine: a big ring warped by integer sine harmonics (integer -> the
// curve closes exactly at 2*PI). Amplitudes stay small against the ring radius
// so bend radii are ~10x the tube radius: gentle, anti-nausea. Phases are
// randomized once per session so every fall winds a little differently.
export function buildLoopLayout() {
  const R = LOOP_DEPTH / (2 * Math.PI); // ~159
  const p1 = Math.random() * Math.PI * 2, p2 = Math.random() * Math.PI * 2;
  const p3 = Math.random() * Math.PI * 2, p4 = Math.random() * Math.PI * 2;

  const N = 128;
  const pts = [];
  for (let k = 0; k < N; k++) {
    const th = (k / N) * Math.PI * 2;
    const r = R + 30 * Math.sin(3 * th + p1) + 18 * Math.sin(5 * th + p2);
    const y = 22 * Math.sin(2 * th + p3) + 12 * Math.sin(7 * th + p4);
    pts.push(new THREE.Vector3(r * Math.cos(th), y, r * Math.sin(th)));
  }
  const spine = new THREE.CatmullRomCurve3(pts, true, 'catmullrom', 0.5);

  const wrap01 = (t) => ((t % 1) + 1) % 1;
  const pointAt = (t, out) => spine.getPoint(wrap01(t), out);
  // {pos, tangent, normal, binormal} basis for placing content in the spine's
  // local frame (binormal ~ local right, normal ~ local up) - same contract as
  // the Explore engine's layout, so fog.js consumes this object as-is.
  const frameAt = (t) => {
    const u = wrap01(t);
    const pos = spine.getPoint(u);
    const tangent = spine.getTangent(u).normalize();
    const binormal = new THREE.Vector3().crossVectors(tangent, SPINE_UP);
    if (binormal.lengthSq() < 1e-5) binormal.set(1, 0, 0);
    binormal.normalize();
    const normal = new THREE.Vector3().crossVectors(binormal, tangent).normalize();
    return { pos, tangent, normal, binormal };
  };
  const frameAtDepth = (d) => frameAt(d / LOOP_DEPTH);

  return {
    RADIUS, totalDepth: LOOP_DEPTH, loopDepth: LOOP_DEPTH,
    spine, pointAt, frameAt, frameAtDepth,
  };
}

// ---- tube shader (copied from scene.js, holes stripped, palette re-tinted) --
const TUBE_VERT = `
  varying vec2 vUv;
  varying float vFogDepth;
  void main() {
    vUv = uv;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const TUBE_FRAG = `
  precision highp float;
  varying vec2 vUv;
  varying float vFogDepth;
  uniform float uTime;
  uniform vec3 uBg1, uBg2, uLineColor, uSpiralColor, uFogColor;
  uniform float uFogDensity, uRings, uSpiralTurns, uArms, uScroll, uFlash, uRush;

  // 1.0 on the line (integer coord), fading to 0 within half-width w.
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5); // distance to nearest integer
    return 1.0 - smoothstep(0.0, w, di);
  }
  // sharp periodic pulse (mostly dark, brief bright peaks = intermittent).
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }

  void main() {
    float len = vUv.x;
    float around = vUv.y;
    float scroll = uTime * uScroll;

    // base: subtle tint variation around the tube
    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin((around + 0.25) * 6.2831));

    // perpendicular rings + a helical spiral (around + length), both scrolling
    float ringCoord = len * uRings - scroll;
    float spiralCoord = around * uArms + len * uSpiralTurns - scroll;
    float ring = lineMask(ringCoord, 0.06);
    float spiral = lineMask(spiralCoord, 0.05);

    // intermittent glow: travelling pulses down the tube + a global shimmer
    float globalFlash = pulse(uTime * 0.7, 6.0);
    float ringGlow   = 0.35 + 1.7 * pulse(ringCoord * 0.5 - uTime * 1.6, 3.0) + 1.1 * globalFlash;
    float spiralGlow = 0.45 + 2.0 * pulse(spiralCoord * 0.4 - uTime * 2.1, 3.0) + 0.9 * globalFlash;

    // rush: the faster the fall, the hotter the line work burns, plus streaky
    // longitudinal speed-lines that only exist at velocity
    ringGlow   *= 1.0 + uRush * 0.9;
    spiralGlow *= 1.0 + uRush * 0.7;
    float streak = lineMask(around * 24.0 + sin(len * 40.0) * 0.15, 0.03)
                 * pulse(len * 60.0 - uTime * 9.0, 2.0);

    vec3 col = base
      + uLineColor   * ring   * ringGlow
      + uSpiralColor * spiral * spiralGlow
      + uLineColor   * streak * uRush * 1.4
      + uLineColor   * uRush * 0.05;

    // lightning strike: a brief whole-tube glare that favours the line work
    col += uFlash * (0.18 + ring * 1.4 + spiral * 0.9) * vec3(0.95, 0.8, 1.05);

    // exp2 fog to match scene.fog
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(f, 0.0, 1.0));
    gl_FragColor = vec4(col, 1.0);
  }`;

// Build the tunnel mesh around a loop layout. `uScroll` is driven by the scene
// each frame (faster fall = faster ring travel).
export function createTunnel(layout) {
  const geo = new THREE.TubeGeometry(
    layout.spine, Math.max(400, Math.round(LOOP_DEPTH * Q.tubeSegMult)), RADIUS, Q.tubeRadial, true);
  const mat = new THREE.ShaderMaterial({
    uniforms: {
      uTime: { value: 0 },
      uBg1: { value: new THREE.Color(0x2b1024) },
      uBg2: { value: new THREE.Color(0x160a18) },
      uLineColor: { value: new THREE.Color(0xff69b4) },   // pink rings
      uSpiralColor: { value: new THREE.Color(0xe56cc0) }, // pink-violet spiral
      uFogColor: { value: new THREE.Color(FOG_COLOR) },
      uFogDensity: { value: FOG_DENSITY },
      uRings: { value: Math.round(LOOP_DEPTH / 8) },        // 125 - integer: loop-seam free
      uSpiralTurns: { value: Math.round(LOOP_DEPTH / 22) }, // 45  - integer: loop-seam free
      uArms: { value: 4.0 },
      uScroll: { value: 0.8 },
      uFlash: { value: 0 }, // lightning glare, driven by fx.js
      uRush: { value: 0 },  // speed heat, driven by scene.js (0 calm - 1 flying)

    },
    vertexShader: TUBE_VERT,
    fragmentShader: TUBE_FRAG,
    side: THREE.BackSide,
  });
  const mesh = new THREE.Mesh(geo, mat);
  return {
    mesh, material: mat,
    dispose() { geo.dispose(); mat.dispose(); },
  };
}
