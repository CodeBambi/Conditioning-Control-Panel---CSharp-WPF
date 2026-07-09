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
  // chamber pattern knobs (fx.js eases these; ALL 0 = the classic look).
  // Each of the Four Chambers wears its own subset:
  //   uBreath      I   - slow ring width+brightness swell (an invitation, not a demand)
  //   uRingDash    II  - rings segmented into doorframe posts
  //   uSpiralDrift II  - spiral phase wanders/stalls/reverses + depth shear (compass-loss)
  //   uLineWave    III - rings undulate around the circumference like vines
  //   uThrob       III - heartbeat lub-dub swell on the line glow
  //   uRingDouble  IV  - crisp twin rings (authoritative)
  //   uStrobe      IV  - hard metronomic neon tick (~1.4Hz, capped - signage, not strobe)
  uniform float uBreath, uRingDash, uLineWave, uRingDouble, uSpiralDrift, uThrob, uStrobe;
  // branch mouths carved into the wall (world space)
  uniform int uHoleCount;
  uniform vec3 uHolePos[MAX_HOLES];
  uniform vec3 uHoleAxis[MAX_HOLES];
  uniform float uHoleR[MAX_HOLES];
  uniform float uHoleBack, uHoleFwd;
  uniform vec3 uRimColor;
  // forward dead-end cut: FADE a stretch of the tube (in vUv.x arc-length space)
  // to the fog color just past a fork so the trunk visually ENDS there in haze and
  // the only ways on are the two carved branch mouths. The wall is NOT discarded -
  // it stays an opaque fog-colored enclosure, so no sightline ever leaks the page
  // background as a black void (the renderer clear is transparent).
  // uCutHide=1 switches the window to DISCARD instead: used for the fresh loop's
  // incoming tail arc during a vein ride - that arc converges on the very point
  // the vein exits at (a closed ring must return to its entry), so left solid it
  // z-fights the vein wall and slices across the ride corridor. Discarding is
  // safe THERE because the ridden vein fully encloses the camera meanwhile.
  uniform int uCutOn;
  uniform int uCutHide;
  uniform float uCutLo, uCutHi;   // arc-length window [0..1]; if hi<lo the window wraps the seam

  // 1.0 on the line (integer coord), fading to 0 within half-width w. The edge
  // is widened to at least ~1.5 screen pixels (fwidth) so a far-off or grazing
  // (tube-wall side, seen near edge-on) line is never sub-pixel-thin. That
  // sub-pixel thinness is what makes the rings appear to "skip"/step when the
  // fall is slow - at speed the per-frame motion hides it, but crawling slowly a
  // hairline line snaps pixel-to-pixel. Screen-space AA keeps slow motion fluid.
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5); // distance to nearest integer
    // never sharper than the designed width, never so wide the line stops fully
    // darkening at the midpoint (di maxes at 0.5) - that upper clamp keeps rings
    // crisp instead of washing to a bright fill at grazing/foreshortened angles.
    float aa = clamp(1.5 * fwidth(coord), w, 0.5);
    return 1.0 - smoothstep(0.0, aa, di);
  }
  // sharp periodic pulse (mostly dark, brief bright peaks = intermittent).
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }

  void main() {
    float len = vUv.x;
    float around = vUv.y;
    float scroll = uTime * uScroll;

    // forward dead-end: fade the trunk to fog over the cut window so it reads as
    // haze/depth (a tube vanishing into mist), not an erased wall showing void.
    float cutFade = 0.0;
    if (uCutOn == 1) {
      float span = (uCutHi >= uCutLo) ? (uCutHi - uCutLo) : (1.0 - uCutLo + uCutHi);
      float into = len - uCutLo;
      if (into < 0.0) into += 1.0;          // wrapped window
      if (into > 0.0 && into < span) {
        if (uCutHide == 1) discard;         // loop-tail arc: the vein encloses the camera
        float e = min(0.02, span * 0.35);   // ~20u ramp on the 1000u loop
        cutFade = smoothstep(0.0, e, into) * smoothstep(0.0, e, span - into);
      }
    }

    // base: subtle tint variation around the tube
    vec3 base = mix(uBg1, uBg2, 0.5 + 0.5 * sin((around + 0.25) * 6.2831));

    // ---- chamber pattern knobs (all zero -> the classic coords below) ----
    // breath (I): slow width/brightness swell, ~8.4s period. The width factor
    // only scales the designed half-width; the fwidth floor in lineMask keeps AA.
    float brW = 1.0 + uBreath * 0.5 * sin(uTime * 0.75);
    float brG = 1.0 + uBreath * 0.3 * sin(uTime * 0.75 - 0.6);
    // vine wave (III): rings undulate around the circumference. INTEGER
    // circumference frequencies -> seam-free at the vUv wrap; fwidth() is taken
    // on the perturbed coord so the wavy line keeps its screen-space AA.
    float wave = uLineWave * (0.35 * sin(around * 6.2831 * 3.0 + uTime * 0.6)
                            + 0.2  * sin(around * 6.2831 * 7.0 - uTime * 0.9));
    // compass-loss (II): BOUNDED spiral phase offsets (never scale scroll - it
    // grows unbounded). When d(drift)/dt beats the scroll rate the spiral
    // visibly stalls and runs backwards; shear makes depths disagree about
    // direction (integer len frequency: wrap-safe).
    float drift = uSpiralDrift * (10.0 * sin(uTime * 0.16)
                                 + 3.0 * sin(uTime * 0.041 + 1.7));
    float shear = uSpiralDrift * 1.5 * sin(len * 6.2831 * 2.0 + uTime * 0.21);

    // perpendicular rings + a helical spiral (around + length), both scrolling
    float ringCoord = len * uRings - scroll + wave;
    float spiralCoord = around * uArms + len * uSpiralTurns - scroll + drift + shear;
    // ring: single line, or the Court's crisp twin (offset > AA width so the
    // pair never fuses); narrower twins = the authoritative look
    float ringS = lineMask(ringCoord, 0.06 * max(brW, 0.5));
    float dblOff = 0.10 + 0.06 * uRingDouble;
    float ringD = max(lineMask(ringCoord - dblOff, 0.035), lineMask(ringCoord + dblOff, 0.035));
    float ring = mix(ringS, ringD, uRingDouble);
    // doorframe dash (II): segment rings into door-post arcs. 10 posts
    // (integer: wrap-safe), 62% duty, own fwidth AA on the segment coord.
    float segC = around * 10.0;
    float segD = abs(fract(segC) - 0.5) * 2.0;
    float dw = clamp(1.5 * fwidth(segC), 0.03, 0.4);
    float dash = 1.0 - smoothstep(0.62 - dw, 0.62 + dw, segD);
    ring *= mix(1.0, dash, uRingDash);
    float spiral = lineMask(spiralCoord, 0.05);

    // intermittent glow: travelling pulses down the tube + a global shimmer.
    // the flicker RATE ramps with depth (uGlowRate): only the time term is
    // scaled, so the line spacing is unchanged - just how fast they pulse.
    float gr = 0.1 + 0.35 * uGlowRate;   // slow up top -> fast deep (flicker cut ~3x - lines flash gently)
    float globalFlash = pulse(uTime * 0.7 * gr, 6.0);
    float ringGlow   = 0.35 + 1.7 * pulse(ringCoord * 0.5 - uTime * 1.6 * gr, 3.0) + 1.1 * globalFlash;
    float spiralGlow = 0.45 + 2.0 * pulse(spiralCoord * 0.4 - uTime * 2.1 * gr, 3.0) + 0.9 * globalFlash;

    // breath (I): brightness follows the width swell, slightly lagged
    ringGlow   *= brG;
    spiralGlow *= brG;
    // heartbeat throb (III): lub-dub envelope; the beat rate rides the same
    // depth ramp as the flicker so the pulse quickens the deeper you fall
    float hb  = fract(uTime * (1.05 + 0.5 * uGlowRate) * 0.5);
    float thr = uThrob * (exp(-18.0 * hb) + 0.55 * exp(-18.0 * fract(hb - 0.32)));
    ringGlow   *= 1.0 + thr * 1.6;
    spiralGlow *= 1.0 + thr * 1.2;
    // metronome (IV): hard brief neon ticks at ~84bpm. Amplitude is capped by
    // the authored knob (<=0.65) and scaled CPU-side by the effect-intensity
    // dial - it reads as neon signage, never a full-screen strobe.
    float strobe = uStrobe * pulse(uTime * 6.2831 * 1.4, 12.0);
    ringGlow   *= 1.0 + strobe * 2.2;
    spiralGlow *= 1.0 + strobe * 1.6;

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
      + uLineColor   * uRush * 0.05
      // faint whole-wall swell on the heartbeat + the Court's whole-tube tick
      + uLineColor   * (thr * 0.05 + strobe * 0.1);

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

    // exp2 fog to match scene.fog; the dead-end cut fades to the same live fog
    // color (fx.js region tint), so it blends with distance haze in every region
    float f = 1.0 - exp(-uFogDensity * uFogDensity * vFogDepth * vFogDepth);
    col = mix(col, uFogColor, clamp(max(f, cutFade), 0.0, 1.0));
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
      // chamber pattern knobs, eased by fx.js from the region styles (0 = classic)
      uBreath: { value: 0 },
      uRingDash: { value: 0 },
      uLineWave: { value: 0 },
      uRingDouble: { value: 0 },
      uSpiralDrift: { value: 0 },
      uThrob: { value: 0 },
      uStrobe: { value: 0 },
      // branch-hole carving (junctions.js sets these; 0 count => idle/no overhead)
      uHoleCount: { value: 0 },
      uHolePos: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3()) },
      uHoleAxis: { value: Array.from({ length: MAX_HOLES }, () => new THREE.Vector3(0, 0, 1)) },
      uHoleR: { value: new Array(MAX_HOLES).fill(0) },
      uHoleBack: { value: RADIUS * 0.35 },
      uHoleFwd: { value: RADIUS * 3.0 }, // a ~45deg vein finishes crossing the wall ~13u along its
                                         // axis (near lip ~5u, + a tube-width of oblique travel);
                                         // any shortfall leaves a wall arc slicing across the mouth
      uRimColor: { value: new THREE.Color(0xff8fd8) },
      // forward dead-end cut (junctions.js sets this; off => no overhead)
      uCutOn: { value: 0 },
      uCutHide: { value: 0 },
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
  // fade a forward stretch of the trunk to fog in arc-length space [lo,hi] (0..1,
  // wraps if hi<lo) so a fork reads as a hazy dead-end (the wall stays as an
  // opaque enclosure - never a black void). clearCut() restores the wall.
  // hide=true DISCARDS the window instead - only safe while something else
  // encloses the camera (the ridden vein, during a junction dive).
  function setCut(lo, hi, hide = false) {
    mat.uniforms.uCutLo.value = ((lo % 1) + 1) % 1;
    mat.uniforms.uCutHi.value = ((hi % 1) + 1) % 1;
    mat.uniforms.uCutHide.value = hide ? 1 : 0;
    mat.uniforms.uCutOn.value = 1;
  }
  function clearCut() { mat.uniforms.uCutOn.value = 0; mat.uniforms.uCutHide.value = 0; }

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
