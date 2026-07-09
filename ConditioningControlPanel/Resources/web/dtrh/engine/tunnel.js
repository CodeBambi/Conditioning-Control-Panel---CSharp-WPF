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

// ---- tube shader --------------------------------------------------------------
// Branch-hole carving (uHole*) is back: junctions.js hands the main-tube material
// up to MAX_HOLES branch mouths, and the frag discards wall fragments inside each
// mouth's cylinder so a diverging vein reads as a real opening cut in the wall
// (ported from the Explore rabbit-hole scene.js). uHoleCount = 0 => zero overhead.
const MAX_HOLES = 2;

const TUBE_VERT = `
  varying vec2 vUv;
  varying float vFogDepth;
  varying vec3 vWorld;
  void main() {
    vUv = uv;
    vWorld = (modelMatrix * vec4(position, 1.0)).xyz;
    vec4 mv = modelViewMatrix * vec4(position, 1.0);
    vFogDepth = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const TUBE_FRAG = `
  precision highp float;
  #define MAX_HOLES ${MAX_HOLES}
  varying vec2 vUv;
  varying float vFogDepth;
  varying vec3 vWorld;
  uniform float uTime;
  uniform vec3 uBg1, uBg2, uLineColor, uSpiralColor, uFogColor;
  uniform float uFogDensity, uRings, uSpiralTurns, uArms, uScroll, uFlash, uRush;
  // 0 near the top of the fall, ramping to 1 the deeper you go: speeds up the
  // intermittent line-glow flicker so the rays flash slow up top, frantic deep.
  uniform float uGlowRate;
  // branch mouths carved into the wall (world space)
  uniform int uHoleCount;
  uniform vec3 uHolePos[MAX_HOLES];
  uniform vec3 uHoleAxis[MAX_HOLES];
  uniform float uHoleR[MAX_HOLES];
  uniform float uHoleBack, uHoleFwd;
  uniform vec3 uRimColor;
  // forward dead-end cut: erase a stretch of the tube (in vUv.x arc-length space)
  // just past a fork so the trunk genuinely ENDS there and the only ways on are the
  // two carved branch mouths (a real bifurcation, not a tube-continues-behind-it).
  uniform int uCutOn;
  uniform float uCutLo, uCutHi;   // arc-length window [0..1]; if hi<lo the window wraps the seam

  // 1.0 on the line (integer coord), fading to 0 within half-width w. The edge
  // is widened to at least ~1.5 screen pixels (fwidth) so a far-off or grazing
  // (tube-wall side, seen near edge-on) line is never sub-pixel-thin. That
  // sub-pixel thinness is what makes the rings appear to "skip"/step when the
  // fall is slow - at speed the per-frame motion hides it, but crawling slowly a
  // hairline line snaps pixel-to-pixel. Screen-space AA keeps slow motion fluid.
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5); // distance to nearest integer
    float aa = max(w, 1.5 * fwidth(coord));   // never sharper than a soft pixel
    return 1.0 - smoothstep(0.0, aa, di);
  }
  // sharp periodic pulse (mostly dark, brief bright peaks = intermittent).
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }

  void main() {
    float len = vUv.x;
    float around = vUv.y;
    float scroll = uTime * uScroll;

    // forward dead-end: discard the trunk in the cut window so it truly ends here.
    if (uCutOn == 1) {
      bool inCut = (uCutHi >= uCutLo) ? (len > uCutLo && len < uCutHi)
                                      : (len > uCutLo || len < uCutHi);
      if (inCut) discard;
    }

    // base: subtle tint variation around the tube
    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin((around + 0.25) * 6.2831));

    // perpendicular rings + a helical spiral (around + length), both scrolling
    float ringCoord = len * uRings - scroll;
    float spiralCoord = around * uArms + len * uSpiralTurns - scroll;
    float ring = lineMask(ringCoord, 0.06);
    float spiral = lineMask(spiralCoord, 0.05);

    // intermittent glow: travelling pulses down the tube + a global shimmer.
    // the flicker RATE ramps with depth (uGlowRate): only the time term is
    // scaled, so the line spacing is unchanged - just how fast they pulse.
    float gr = 0.1 + 0.35 * uGlowRate;   // slow up top -> fast deep (flicker cut ~3x - lines flash gently)
    float globalFlash = pulse(uTime * 0.7 * gr, 6.0);
    float ringGlow   = 0.35 + 1.7 * pulse(ringCoord * 0.5 - uTime * 1.6 * gr, 3.0) + 1.1 * globalFlash;
    float spiralGlow = 0.45 + 2.0 * pulse(spiralCoord * 0.4 - uTime * 2.1 * gr, 3.0) + 0.9 * globalFlash;

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

    // branch mouths: discard the wall where a diverging vein pierces it, and light
    // a glowing rim on the cut boundary so the seam reads as a portal (bloom-fed).
    float holeEdge = 1e9;
    for (int i = 0; i < MAX_HOLES; i++) {
      if (i >= uHoleCount) break;
      vec3 d = vWorld - uHolePos[i];
      float al = dot(d, uHoleAxis[i]);
      vec3 pe = d - al * uHoleAxis[i];
      float dr = length(pe);
      if (al > -uHoleBack && al < uHoleFwd) {
        if (dr < uHoleR[i]) discard;
        holeEdge = min(holeEdge, dr - uHoleR[i]);
      }
    }
    float holeRim = 1.0 - smoothstep(0.0, 0.9, holeEdge);
    col += uRimColor * holeRim * (2.4 + 0.6 * sin(uTime * 2.0));

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
      uGlowRate: { value: 0 }, // ray-flicker rate ramp, driven by fx.js (0 shallow - 1 deep)
      // branch-hole carving (junctions.js sets these; 0 count => idle/no overhead)
      uHoleCount: { value: 0 },
      uHolePos: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3()) },
      uHoleAxis: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3(0, 0, 1)) },
      uHoleR: { value: new Array(MAX_HOLES).fill(0) },
      uHoleBack: { value: RADIUS * 0.35 },
      uHoleFwd: { value: RADIUS * 1.6 },
      uRimColor: { value: new THREE.Color(0xff8fd8) },
      // forward dead-end cut (junctions.js sets this; off => no overhead)
      uCutOn: { value: 0 },
      uCutLo: { value: 0 },
      uCutHi: { value: 0 },
    },
    vertexShader: TUBE_VERT,
    fragmentShader: TUBE_FRAG,
    side: THREE.BackSide,
    extensions: { derivatives: true }, // fwidth() line AA (core on WebGL2; flag is a WebGL1 safety net)
  });
  let geoRef = geo;
  const mesh = new THREE.Mesh(geo, mat);

  // junctions.js hands us up to MAX_HOLES branch mouths, each {point, axis, r}
  // (world space). Writing uHoleCount = 0 restores a solid wall.
  function setHoles(holes) {
    const n = Math.min(MAX_HOLES, holes ? holes.length : 0);
    for (let i = 0; i < n; i++) {
      mat.uniforms.uHolePos.value[i].copy(holes[i].point);
      mat.uniforms.uHoleAxis.value[i].copy(holes[i].axis).normalize();
      mat.uniforms.uHoleR.value[i] = holes[i].r;
    }
    mat.uniforms.uHoleCount.value = n;
  }
  function clearHoles() { mat.uniforms.uHoleCount.value = 0; }
  // erase a forward stretch of the trunk in arc-length space [lo,hi] (0..1, wraps
  // if hi<lo) so a fork reads as a true dead-end. clearCut() restores the wall.
  function setCut(lo, hi) {
    mat.uniforms.uCutLo.value = ((lo % 1) + 1) % 1;
    mat.uniforms.uCutHi.value = ((hi % 1) + 1) % 1;
    mat.uniforms.uCutOn.value = 1;
  }
  function clearCut() { mat.uniforms.uCutOn.value = 0; }

  return {
    mesh, material: mat,
    setHoles, clearHoles, setCut, clearCut,
    // Rebase: swap ONLY the geometry onto a fresh loop spine, keeping the same
    // material so fx.js's tunnelMat binding + all tint/warp uniforms survive.
    rebuild(newLayout) {
      const ng = new THREE.TubeGeometry(
        newLayout.spine, Math.max(400, Math.round(LOOP_DEPTH * Q.tubeSegMult)), RADIUS, Q.tubeRadial, true);
      mesh.geometry = ng;
      geoRef.dispose();
      geoRef = ng;
      clearHoles();
      clearCut();  // the cut window was in the OLD arc-length space; the fresh loop is whole
    },
    dispose() { geoRef.dispose(); mat.dispose(); },
  };
}
