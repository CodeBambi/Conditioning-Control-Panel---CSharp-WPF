/* ============================================================================
 * background.js — Agent F. The ambient tube behind everything (canvas #intake-bg).
 *
 * A DtRH-style tube (a low-poly cylinder seen from the inside, with scrolling
 * ring + spiral shader work) that is a FULL PARTICIPANT in the run, not
 * wallpaper. On top of the original `setDepth` sink (tint/speed via the
 * contract's `bgIntensity`) it now implements the reactive extensions from
 * contracts.js "BACKGROUND (Agent F)":
 *
 *   setBand(band)            per-band dressing (palette/geometry/speed mood),
 *                            crossfaded over ~1.5s — Calibration sterile ->
 *                            Climax vein-plunge -> Recovery dawn wash.
 *   onAnswer({correct,steered}) correct -> speed surge + a glow pulse that
 *                            travels down the tube; wrong -> stall + desaturate
 *                            + a brief counter-rotation kick; steered wobbles.
 *   onReward(rewardEvent)    drop -> plunge burst (FOV/speed kick + ring smear);
 *                            jackpot -> gold strobe cascade; praise -> warm
 *                            bloom; bubble/chime -> wall glints; nearMiss ->
 *                            one faint shimmer ring. Intensity arrives ALREADY
 *                            cap-clamped by the caller (invariant #2).
 *   transition(kind)->Promise scripted 1-2s set-pieces: 'iris' | 'fork' |
 *                            'plunge' | 'surface'. Never rejects; resolves
 *                            immediately when disabled/reduced/dead, and a
 *                            safety timer resolves it even if the loop stalls.
 *   setRouteTint({hue,strength}) archetype signature hue bleeds into the
 *                            line/spiral palette as the route solidifies.
 *   setSteerWarp(intensity)  0..1 eased barrel/wobble wall distortion while a
 *                            heavy steer is live; 0 resets clean.
 *
 * All of it still holds the original guarantees:
 *   - It sits behind the beat stage and NEVER touches the beat loop: it owns
 *     its own rAF and every public method is a cheap state write.
 *   - three.js is imported LAZILY (dynamic import inside the async init) so a
 *     missing/failed vendor bundle can never break module load or the page.
 *   - No/weak WebGL (or reduced-motion) falls back to a cheap animated 2D
 *     tunnel that still honors every method at reduced fidelity — never blank,
 *     never throw. Mobile tier caps DPR, thins geometry, paces to ~30fps.
 *   - ONE mesh, ONE material, ONE draw call. All reactions are uniforms inside
 *     the existing shader (no post-processing passes).
 *   - Every method no-ops safely before init and after dispose.
 *
 *   nextBiome()              rotate to the next of DtRH's 16 biome dresses
 *                            (call once per beat) — shuffled, no immediate
 *                            repeat, depth-biased, ~1.4s crossfade. Once called,
 *                            biomes own the palette/geometry and setBand becomes
 *                            a depth-bias hint (a host that never rotates keeps
 *                            the original band-dressed tube).
 *
 *   WALL DRESSING            from Section 2 onward, stretches of the bore are
 *                            plastered with the user's own gifs (render/
 *                            wallDecals.js — a port of DtRH's wallPosters.js).
 *                            Band-driven: clean in Calibration, sparse patches
 *                            in Establishing, busy in Deepening, saturated in
 *                            Climax, scrubbed clean again through Recovery.
 *                            Needs the optional `media` manifest; without it
 *                            (or under reduced motion) the wall simply stays
 *                            bare, exactly as before.
 *
 * CONTRACT (contracts.js "BACKGROUND (Agent F)"):
 *   createBackground({ canvas, media? }) -> { setDepth, setEnabled, dispose,
 *     setBand, onAnswer, onReward, transition, setRouteTint, setSteerWarp,
 *     nextBiome }
 *
 * The ONLY top-level import is the pure contract module (no DOM, no three), so
 * importing this file is always safe. Everything heavy happens at runtime.
 * ==========================================================================*/

import { depthToChannels } from '../core/contracts.js';
// DtRH biome DATA reused in place (pure data-only module: it imports only
// regions.js, which pulls in no three/DOM — so this stays a safe, headless
// top-level import, same class as the contract module below). We drive the
// existing self-contained tube shader with these 16 biome palettes/geometries
// rather than importing dtrh/engine/tunnel.js, which is entangled with the whole
// game scene graph (renderer/scene/spawner/fallNav) and can't be lifted cleanly.
import { BIOMES_ALL } from '../../dtrh/game/biomes.js';

/* --- tiny local helpers (kept self-contained; no DtRH deps) ---------------- */

// Diagnostic seam: the hosted page has no devtools, so a GL/shader failure would
// otherwise vanish. boot.js relays 'intake-log' CustomEvents to the C# shim log;
// we also console.error so a browser/harness run shows it. Never throws.
function logSeam(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixbg: ' + msg } }));
    }
  } catch (_e) {}
  try { if (typeof console !== 'undefined' && console.error) console.error('[ixbg] ' + msg); } catch (_e) {}
}

// depth (0..1) -> the background channel magnitude (0..1). Single source of
// truth is the contract; we only read `.bgIntensity` so the curve can't drift.
function bgIntensityFor(depth) {
  try {
    const ch = depthToChannels(depth);
    const v = ch && typeof ch.bgIntensity === 'number' ? ch.bgIntensity : 0;
    return v < 0 ? 0 : v > 1 ? 1 : v;
  } catch (_e) {
    // Contract shim / stub could throw; degrade to a linear read rather than die.
    const d = depth < 0 ? 0 : depth > 1 ? 1 : depth;
    return 0.2 + d * 0.8;
  }
}

// Cheap, dependency-free WebGL probe on a throwaway canvas (never uses the real
// one, so a failed probe leaves #intake-bg pristine for the 2D fallback).
function hasWebGL() {
  try {
    const c = document.createElement('canvas');
    return !!(window.WebGLRenderingContext &&
      (c.getContext('webgl2') || c.getContext('webgl') || c.getContext('experimental-webgl')));
  } catch (_e) {
    return false;
  }
}

function prefersReducedMotion() {
  try { return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); }
  catch (_e) { return false; }
}

// Mobile-ish tier: coarse pointer OR a small viewport. Only used to pick lighter
// knobs; the tube look survives either way.
function detectTier() {
  try {
    const coarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
    const small = Math.min(window.innerWidth || 1024, window.innerHeight || 768) < 720;
    return (coarse || small) ? 'mobile' : 'desktop';
  } catch (_e) {
    return 'desktop';
  }
}

const clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);
const clampN = (n, lo, hi) => (n < lo ? lo : n > hi ? hi : n);
const byte = (n) => (n < 0 ? 0 : n > 255 ? 255 : Math.round(n));
// 0xRRGGBB -> [r,g,b] (DtRH biome palettes are packed hex ints).
function rgbFromHex(h) { return [(h >> 16) & 255, (h >> 8) & 255, h & 255]; }
// scale a colour toward black (calm variants of the biome's hot colour).
function scaleRGB(c, m) { return [byte(c[0] * m), byte(c[1] * m), byte(c[2] * m)]; }

// hue (0..360) -> [r,g,b] 0..255 at a saturated, luminous point (route tints).
function hueToRGB(hue) {
  const h = (((hue % 360) + 360) % 360) / 60;
  const s = 0.85, l = 0.62;
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs((h % 2) - 1));
  const m = l - c / 2;
  let r = 0, g = 0, b = 0;
  if (h < 1) { r = c; g = x; } else if (h < 2) { r = x; g = c; }
  else if (h < 3) { g = c; b = x; } else if (h < 4) { g = x; b = c; }
  else if (h < 5) { r = x; b = c; } else { r = c; b = x; }
  return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}

/* --- the shader (grown from dtrh/engine/tunnel.js techniques) --------------
 * Rings + TWO helical spiral arm-sets (A live / B incoming), both scrolling;
 * tint lerps two palettes by uIntensity (depth modulates WITHIN the band's
 * dressing); a distance fade hides the far end so we don't need scene.fog.
 * The dual spiral slots exist because arm counts must stay INTEGER to keep the
 * circumference seam invisible (DtRH rule: SNAP pattern counts, ease weights) —
 * band changes crossfade slot A -> slot B, and the 'fork' transition borrows
 * slot B for its counter-rotating twin. All reactions (wave, strobe, smear,
 * warp, iris, sparkle, bloom, desat, throb) are uniforms in this ONE program:
 * still a single draw call, no post-processing. */
const VERT = `
  varying vec2 vUv;
  varying float vFog;
  uniform float uTime;
  uniform float uWarp;      // 0..1 steer-warp: wall wobble (barrel feel)
  uniform float uPulseAmt;  // band throb: organic radius pulse (Climax veins)
  void main() {
    vUv = uv;
    vec3 p = position;
    float ang = atan(p.y, p.x);
    // steer-warp wobbles the bore; throb breathes it like a vein
    float wob = uWarp * (0.10 * sin(ang * 3.0 + uTime * 1.3 + p.z * 0.15)
                       + 0.06 * sin(p.z * 0.4 - uTime * 0.9));
    float thr = uPulseAmt * 0.05 * sin(uTime * 2.3 + p.z * 0.35);
    p.xy *= 1.0 + wob + thr;
    vec4 mv = modelViewMatrix * vec4(p, 1.0);
    vFog = -mv.z;
    gl_Position = projectionMatrix * mv;
  }`;

const FRAG = `
  precision highp float;   // MUST match the vertex stage's precision (three.js
  // prepends 'precision highp float;' to both stages). Re-declaring this as
  // mediump made shared uniforms (uTime, ...) mismatch precision across stages,
  // which is a hard LINK error -> the whole tube silently drew nothing (flat bg).
  varying vec2 vUv;
  varying float vFog;
  uniform float uScroll;     // accumulated scroll phase (units)
  uniform float uIntensity;  // 0..1 depth-derived tint/glow (WITHIN the band dress)
  uniform float uTime;
  uniform vec3  uBgCalm, uBgHot, uLineCalm, uLineHot, uSpiralCalm, uSpiralHot, uFog;
  uniform float uRings, uFar;
  uniform float uRingW, uSpiralW;   // line half-widths (band-eased)
  uniform vec4  uSpiralA;    // x=arms (integer) y=turns z=scroll dir w=weight
  uniform vec4  uSpiralB;    // incoming/fork arm-set, same packing
  uniform float uGlowMul;    // band glow multiplier
  uniform float uPulseAmt;   // band throb (also brightens rhythmically here)
  uniform float uDesat;      // 0..1 desaturation dip (wrong answers)
  uniform float uSmear;      // 0..1 ring blur/stretch (drop burst / plunge)
  uniform float uWarp;       // fragment share of steer-warp: mild fisheye of len
  uniform float uWavePos;    // 0..1.3 traveling glow-pulse position (-1 = off)
  uniform float uWaveAmp;    // wave amplitude
  uniform vec3  uWaveColor;  // wave tint (praise-pink surge / near-miss silver)
  uniform float uStrobe;     // jackpot gold cascade amplitude
  uniform float uSparkle;    // wall glint amplitude (bubble/chime)
  uniform float uBloom;      // warm bloom swell (praise)
  uniform float uIris;       // radial iris radius (norm. screen units; >=2 = open)
  uniform vec2  uResolution; // drawing-buffer size (iris mask)

  // 1.0 on an integer coord, fading within half-width w (screen-space AA-ish).
  float lineMask(float coord, float w) {
    float di = 0.5 - abs(fract(coord) - 0.5);
    float aa = clamp(1.5 * fwidth(coord), w, 0.5);
    return 1.0 - smoothstep(0.0, aa, di);
  }
  float pulse(float p, float k) { return pow(0.5 + 0.5 * sin(p), k); }
  float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

  void main() {
    // mild fisheye while steer-warped: stretch the near pattern toward the eye
    float len = pow(vUv.y, 1.0 + uWarp * 0.30);  // along the tube
    float around = vUv.x;                        // around the circumference

    vec3 base   = mix(uBgCalm,   uBgHot,   uIntensity);
    vec3 lineC  = mix(uLineCalm, uLineHot, uIntensity);
    vec3 spirC  = mix(uSpiralCalm, uSpiralHot, uIntensity);

    // gentle tint variation around the bore
    vec3 col = base * (0.85 + 0.15 * sin((around + 0.25) * 6.2831));

    // smear widens + dims the linework (motion-blur feel on drops/plunges)
    float wMul = 1.0 + 3.0 * uSmear;
    float gDim = 1.0 / (1.0 + 1.2 * uSmear);

    float ringCoord = len * uRings - uScroll;
    float spCoordA  = around * uSpiralA.x + len * uSpiralA.y - uScroll * uSpiralA.z;
    float spCoordB  = around * uSpiralB.x + len * uSpiralB.y - uScroll * uSpiralB.z;

    float ring   = lineMask(ringCoord, uRingW * wMul);
    float spiral = lineMask(spCoordA, uSpiralW * wMul) * uSpiralA.w
                 + lineMask(spCoordB, uSpiralW * wMul) * uSpiralB.w;

    // intermittent glow, faster + hotter as depth climbs; band throb breathes it
    float gr = 0.25 + 0.75 * uIntensity;
    float ringGlow   = 0.4 + 1.4 * pulse(ringCoord * 0.5 - uTime * 1.4 * gr, 3.0);
    float spiralGlow = 0.5 + 1.6 * pulse(spCoordA * 0.4 - uTime * 1.9 * gr, 3.0);
    float glow = (0.4 + 0.9 * uIntensity) * uGlowMul * gDim;
    glow *= 1.0 + uPulseAmt * 0.55 * sin(uTime * 2.6);

    col += lineC * ring   * ringGlow   * glow;
    col += spirC * spiral * spiralGlow * glow;

    // traveling glow pulse (correct-answer surge / near-miss shimmer ring)
    float wv = uWaveAmp * exp(-pow((len - uWavePos) * 9.0, 2.0));
    col += uWaveColor * wv * (0.35 + ring + spiral);

    // jackpot: rhythmic gold strobes cascading down the tube
    float st = uStrobe * pulse(len * 6.0 - uTime * 9.0, 6.0);
    col += vec3(1.0, 0.82, 0.45) * st * (0.6 + ring + spiral);

    // wall glints (bubble/chime): hashed twinkle cells, near-wall only
    float glint = step(0.985, hash(vec2(floor(around * 40.0), floor(len * 120.0))
                                   + floor(uTime * 10.0)));
    col += lineC * uSparkle * glint;

    // praise: warm bloom swell washes the bore
    col += (vec3(1.0, 0.72, 0.48) * 0.35 + lineC * 0.30) * uBloom;

    // wrong-answer desaturation dip
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(lum), uDesat);

    // distance fade to the fog color so the far end melts into the page bg
    float f = clamp(vFog / uFar, 0.0, 1.0);
    col = mix(col, uFog, f * f);

    // iris mask (transition 'iris'): tube opens from a point; bright rim at the lip
    if (uIris < 2.0) {
      vec2 sc = (gl_FragCoord.xy - 0.5 * uResolution) / min(uResolution.x, uResolution.y);
      float d = length(sc) * 2.0;
      float open = 1.0 - smoothstep(uIris * 0.92, uIris, d);
      float rim = smoothstep(uIris * 0.86, uIris * 0.95, d) * open;
      col = col * open + lineC * rim * 1.4;
    }

    gl_FragColor = vec4(col, 1.0);
  }`;

/* --- band dressings ---------------------------------------------------------
 * Each band is a full parameter set the render loop EASES toward (~1.5s
 * crossfade; nothing snaps except integer arm counts, which ride the dual
 * spiral slots). `*Calm`/`*Hot` pairs keep depth (uIntensity) modulating
 * WITHIN the dressing, exactly as setDepth always did. The DEFAULT dress is
 * the legacy look, so a host that never calls setBand sees the original tube. */
const DRESS_FIELDS_COLOR = ['bgCalm', 'bgHot', 'lineCalm', 'lineHot', 'spiralCalm', 'spiralHot', 'fog'];
const DRESS_FIELDS_SCALAR = ['rings', 'speedMul', 'glowMul', 'pulseAmt', 'ringW', 'spiralW', 'farMul'];

const DEFAULT_DRESS = {
  // legacy palette/params (pre-setBand behavior, byte-for-byte)
  bgCalm: [0x14, 0x10, 0x22], bgHot: [0x2b, 0x0f, 0x24],
  lineCalm: [0x6a, 0x4a, 0x9a], lineHot: [0xff, 0x69, 0xb4],
  spiralCalm: [0x8a, 0x6c, 0xc0], spiralHot: [0xb0, 0x6c, 0xff],
  fog: [0x14, 0x10, 0x1f], // matches styles.css --intake-bg0
  rings: 22, arms: 3, turns: 6, dir: 1,
  speedMul: 1.0, glowMul: 1.0, pulseAmt: 0.0, ringW: 0.06, spiralW: 0.05, farMul: 1.0,
};

const BAND_DRESS = {
  // sterile: near-white/cool-grey, thin precise rings, slow — clinical grid
  calibration: {
    bgCalm: [0x12, 0x15, 0x1b], bgHot: [0x18, 0x1d, 0x26],
    lineCalm: [0xb9, 0xc3, 0xd2], lineHot: [0xe6, 0xee, 0xf8],
    spiralCalm: [0x77, 0x84, 0x99], spiralHot: [0x9f, 0xae, 0xc4],
    fog: [0x0d, 0x10, 0x16],
    rings: 30, arms: 1, turns: 3, dir: 1,
    speedMul: 0.50, glowMul: 0.55, pulseAmt: 0.0, ringW: 0.030, spiralW: 0.035, farMul: 1.0,
  },
  // warmth bleeds in: soft pink, rings gain glow, slight speed
  establishing: {
    bgCalm: [0x19, 0x11, 0x20], bgHot: [0x26, 0x14, 0x2a],
    lineCalm: [0xd6, 0x9c, 0xc2], lineHot: [0xff, 0x8f, 0xc8],
    spiralCalm: [0xa2, 0x74, 0xcc], spiralHot: [0xc8, 0x6c, 0xe6],
    fog: [0x14, 0x0e, 0x1b],
    rings: 24, arms: 2, turns: 5, dir: 1,
    speedMul: 0.78, glowMul: 0.85, pulseAmt: 0.06, ringW: 0.045, spiralW: 0.050, farMul: 0.95,
  },
  // spiral arms thicken/multiply, saturated purple-pink, fog closes in
  deepening: {
    bgCalm: [0x1b, 0x0e, 0x2b], bgHot: [0x2b, 0x10, 0x3a],
    lineCalm: [0xf0, 0x62, 0xb0], lineHot: [0xff, 0x4f, 0xae],
    spiralCalm: [0xa8, 0x5c, 0xf4], spiralHot: [0xc8, 0x4f, 0xff],
    fog: [0x10, 0x08, 0x1a],
    rings: 20, arms: 4, turns: 8, dir: 1,
    speedMul: 1.05, glowMul: 1.10, pulseAmt: 0.18, ringW: 0.060, spiralW: 0.065, farMul: 0.78,
  },
  // vein-plunge: deep red-violet, organic throb, fastest scroll, spiral dominant
  climax: {
    bgCalm: [0x23, 0x07, 0x15], bgHot: [0x36, 0x09, 0x1e],
    lineCalm: [0xe8, 0x2e, 0x68], lineHot: [0xff, 0x5f, 0x8f],
    spiralCalm: [0xc2, 0x2e, 0x86], spiralHot: [0xf2, 0x3f, 0xa8],
    fog: [0x14, 0x04, 0x0c],
    rings: 16, arms: 5, turns: 11, dir: 1,
    speedMul: 1.55, glowMul: 1.35, pulseAmt: 0.50, ringW: 0.070, spiralW: 0.075, farMul: 0.60,
  },
  // dawn wash: pale gold / soft blue, slows to a crawl, glow softens
  recovery: {
    bgCalm: [0x10, 0x16, 0x24], bgHot: [0x17, 0x1f, 0x30],
    lineCalm: [0xe4, 0xd2, 0xa6], lineHot: [0xff, 0xe6, 0xb4],
    spiralCalm: [0x94, 0xb2, 0xd4], spiralHot: [0xb6, 0xce, 0xe8],
    fog: [0x0e, 0x13, 0x1e],
    rings: 22, arms: 1, turns: 3, dir: 1,
    speedMul: 0.30, glowMul: 0.60, pulseAmt: 0.0, ringW: 0.040, spiralW: 0.045, farMul: 1.1,
  },
};

function cloneDress(d) {
  const out = {};
  for (const k of DRESS_FIELDS_COLOR) out[k] = d[k].slice();
  for (const k of DRESS_FIELDS_SCALAR) out[k] = d[k];
  out.arms = d.arms; out.turns = d.turns; out.dir = d.dir;
  return out;
}

/* --- DtRH biome dressings ---------------------------------------------------
 * Convert a dtrh/game/biomes.js biome (its regions-shaped `style`) into ONE of
 * our tube dresses so the intake tube can wear any of the game's 16 places.
 * The mapping is deliberately SUBDUED — this is a soothing hypno intake, not
 * the game: glow is pulled below the biome's bloom, scroll speed capped well
 * under DtRH gameplay, throb gentled — so the quiz text stays readable over it.
 * Colours come straight from the biome palette (colLine/colSpiral/colBg/colFog);
 * calm variants are the same hue toward black so depth (uIntensity) still
 * modulates WITHIN the biome dress exactly as setDepth always did. Geometry
 * (rings/arms/turns/dir) is rescaled from the biome density to our short bore,
 * keeping arm/turn counts INTEGER (seam-free treadmill rule). */
function dressFromBiome(b) {
  const st = (b && b.style) || {};
  const pal = st.palette || {};
  const dens = st.density || {};
  const peak = (st.knobs && st.knobs.peak) || {};
  const room = (b && b.room) || 1;
  const line = rgbFromHex(pal.colLine != null ? pal.colLine : 0xff69b4);
  const spir = rgbFromHex(pal.colSpiral != null ? pal.colSpiral : 0xb06cff);
  const bg = rgbFromHex(pal.colBg != null ? pal.colBg : 0x1a1a2e);
  const fog = rgbFromHex(pal.colFog != null ? pal.colFog : 0x14101f);
  const bloom = typeof st.bloom === 'number' ? st.bloom : 1.0;
  const throb = peak.throb || 0, breath = peak.breath || 0;
  const streaks = peak.streaks || 0, surge = peak.surge || 0, chase = peak.chase || 0;
  return {
    // palette straight from the biome; calm = same hue nearer black
    bgCalm: scaleRGB(bg, 0.72), bgHot: bg.slice(),
    lineCalm: scaleRGB(line, 0.50), lineHot: line.slice(),
    spiralCalm: scaleRGB(spir, 0.55), spiralHot: spir.slice(),
    fog: fog.slice(),
    // geometry rescaled to the short intake bore (integers stay seam-free)
    rings: clampN(Math.round((dens.rings || 110) / 5), 14, 34),
    arms: clampN(Math.round(dens.arms || 3), 1, 6),
    turns: clampN(Math.round((dens.spiralTurns || 45) / 6), 4, 10),
    dir: (st.params && st.params.spiralDir === -1) ? -1 : 1,
    // motion/brightness — capped calm & readable (below DtRH gameplay)
    speedMul: clampN(0.42 + room * 0.06 + streaks * 0.22 + surge * 0.16 + chase * 0.10, 0.34, 1.05),
    glowMul: clampN(0.45 + bloom * 0.40, 0.45, 1.05),
    pulseAmt: clampN(throb * 0.70 + breath * 0.18 + surge * 0.10, 0, 0.42),
    ringW: clampN(0.036 + (peak.ringDouble ? 0.020 : 0), 0.030, 0.075),
    spiralW: clampN(0.040 + (peak.kaleido ? 0.020 : 0), 0.030, 0.075),
    farMul: clampN(1.06 - room * 0.06, 0.68, 1.10),
  };
}

// All 16 biomes, in the stable room I->IV order biomes.js exports (calmest ->
// wildest), each with a ready-to-ease dress. `room` (1..4) is kept for the
// light depth-bias in the rotation picker.
const BIOME_DRESSES = (Array.isArray(BIOMES_ALL) ? BIOMES_ALL : []).map((b) => ({
  id: b.id, name: b.name, glyph: b.glyph, room: b.room || 1, dress: dressFromBiome(b),
}));

/* ==========================================================================
 * FACTORY
 * ========================================================================== */
export function createBackground({ canvas, media }) {
  // --- shared state ---------------------------------------------------------
  let enabled = false;
  let disposed = false;
  let mode = 'pending';        // 'pending' | 'webgl' | '2d' | 'dead'
  let starting = false;        // an async init is in flight

  let rafId = 0;
  let running = false;         // the rAF loop is live
  let frozen = false;          // setFrozen(true): hold the animation stone-still
  let lastT = 0;               // last frame timestamp (ms)
  let scroll = 0;              // accumulated scroll phase

  let targetIntensity = bgIntensityFor(0);
  let curIntensity = targetIntensity;

  const tier = detectTier();
  const reduced = prefersReducedMotion();
  const dprCap = 1.5; // background layer: 1.5 is plenty and eases the GPU everywhere
  const minFrameMs = tier === 'mobile' ? 30 : 0; // ~30fps pace on phones

  // --- dressing state (eased each frame; ~1.5s crossfade to a new band) -----
  const DRESS_TAU = 0.45; // exp ease time constant -> ~95% settled in ~1.4s
  const dressCur = cloneDress(DEFAULT_DRESS);
  let dressTarget = cloneDress(DEFAULT_DRESS);
  // spiral slots: arms/turns/dir SNAP into slot B and crossfade by weight so
  // arm counts stay integer (seam-free); slot A is the live set.
  const sp = {
    A: { arms: DEFAULT_DRESS.arms, turns: DEFAULT_DRESS.turns, dir: DEFAULT_DRESS.dir },
    B: null,     // incoming set (band change) — null when idle
    mix: 0,      // 0..1 weight of B during a band crossfade
  };

  // --- biome rotation (DtRH's 16 places, one per beat) ----------------------
  // Once nextBiome() is called the biomes OWN the dressing (setBand becomes a
  // depth-bias hint, not a palette stomp), so a host that never rotates still
  // sees the original band-dressed tube. A shuffled deck guarantees no immediate
  // repeat and even coverage across the 16; the pick is lightly biased toward
  // biomes whose room matches the current depth (calm places shallow, wild deep).
  let curBand = null;
  let biomeEngaged = false;
  let biomeIdx = -1;         // index into BIOME_DRESSES of the live biome
  let biomeDeck = [];        // shuffled remaining indices for this cycle
  let bandBiasDepth = 0;     // last depth (0..1) — steers the room bias

  function refillDeck() {
    const idxs = BIOME_DRESSES.map((_d, i) => i);
    for (let i = idxs.length - 1; i > 0; i--) { // Fisher-Yates
      const j = (Math.random() * (i + 1)) | 0;
      const t = idxs[i]; idxs[i] = idxs[j]; idxs[j] = t;
    }
    // never repeat the last-shown biome across the deck seam
    if (biomeIdx >= 0 && idxs.length > 1 && idxs[0] === biomeIdx) {
      const t = idxs[0]; idxs[0] = idxs[1]; idxs[1] = t;
    }
    biomeDeck = idxs;
  }
  function pickNextBiomeIndex() {
    if (!biomeDeck.length) refillDeck();
    const targetRoom = 1 + Math.round(clamp01(bandBiasDepth) * 3); // 1..4 by depth
    let pos = 0;
    for (let k = 0; k < Math.min(biomeDeck.length, 4); k++) {
      if (Math.abs((BIOME_DRESSES[biomeDeck[k]].room || 1) - targetRoom) <= 1) { pos = k; break; }
    }
    return biomeDeck.splice(pos, 1)[0];
  }

  // --- reaction state (transients decayed each frame; all cheap scalars) ----
  const anim = {
    surge: 0,      // correct-answer forward speed spike (decays ~800ms)
    stall: 0,      // wrong-answer speed dip toward 0 (decays ~600ms)
    desat: 0,      // wrong-answer desaturation dip
    rotVel: 0,     // counter-rotation kick velocity (rad/s, decays)
    rot: 0,        // integrated tube roll
    wobble: 0,     // steered-answer wobble transient
    smear: 0,      // ring blur/stretch (drop burst / plunge)
    bloom: 0,      // praise warm swell
    sparkle: 0,    // bubble/chime wall glints
    strobe: 0,     // jackpot cascade amplitude
    strobeT: 0,    // jackpot remaining seconds
    fovKick: 0,    // camera FOV kick (drop/plunge lurch)
    wave: { pos: -1, amp: 0, speed: 1.3, color: [255, 105, 180] }, // traveling pulse
  };
  const warp = { cur: 0, target: 0 };                    // setSteerWarp (eased)
  const route = { color: [255, 105, 180], strength: 0,   // setRouteTint targets
                  curColor: [255, 105, 180], curStrength: 0 };

  // --- transition state (one active set-piece at a time) --------------------
  let trans = null;          // { kind, t, dur, resolve, timer, ... }
  let transSpeedMul = 1;     // scripted speed multiplier (eases back to 1)
  let irisR = 10;            // >=2 disables the iris mask

  // WebGL bucket (populated by initWebGL)
  let three = null, renderer = null, scene = null, camera = null, mat = null, geo = null, mesh = null;
  let baseFov = 78, curFov = 78;
  // Wall dressing (render/wallDecals.js — DtRH's wallPosters port). WebGL only,
  // built once the tube exists and only when the manifest actually has media;
  // null everywhere else (reduced motion, 2D fallback, no user assets).
  let decals = null;
  let tubeRadius = 5.2, tubeLength = 140;
  // 2D bucket
  let ctx2d = null;

  // --- resize handling ------------------------------------------------------
  function currentDpr() {
    const d = (window.devicePixelRatio || 1);
    return Math.min(dprCap, Math.max(1, d));
  }
  function sizeWebGL() {
    if (!renderer) return;
    const w = window.innerWidth || 1, h = window.innerHeight || 1;
    renderer.setPixelRatio(currentDpr());
    renderer.setSize(w, h, false);
    if (camera) { camera.aspect = w / h; camera.updateProjectionMatrix(); }
    if (mat) mat.uniforms.uResolution.value.set(canvas.width || 1, canvas.height || 1);
  }
  function size2D() {
    const dpr = currentDpr();
    const w = window.innerWidth || 1, h = window.innerHeight || 1;
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
  }
  function onResize() {
    if (disposed) return;
    if (mode === 'webgl') sizeWebGL();
    else if (mode === '2d') size2D();
  }
  window.addEventListener('resize', onResize);

  // Pause the rAF entirely while the page/tab is hidden (WebView2 minimised or
  // the host swaps away): no wasted GPU, and we resume cleanly on return.
  function onVisibility() {
    if (disposed) return;
    if (typeof document !== 'undefined' && document.hidden) { stopLoop(); }
    else if (enabled && (mode === 'webgl' || mode === '2d')) { startLoop(); }
  }
  if (typeof document !== 'undefined') document.addEventListener('visibilitychange', onVisibility);

  // --- WebGL init (lazy three import) --------------------------------------
  async function initWebGL() {
    // Dynamic import: a missing/broken vendor bundle throws HERE (caught below)
    // and routes to the 2D fallback — module load is never at risk.
    const THREE = await import('three');
    if (disposed) return false;
    three = THREE;

    renderer = new THREE.WebGLRenderer({ canvas, antialias: tier !== 'mobile', alpha: true, powerPreference: 'high-performance' });
    renderer.setClearColor(0x000000, 0); // let the CSS gradient show behind

    // Make shader breakage LOUD. The hosted page has no devtools, and a failed
    // compile/link does NOT throw (three only console.errors internally) — that is
    // exactly how the tube once drew nothing in silence. This hook records the
    // failure so we can log it AND drop to the 2D fallback (a dressed tube) instead
    // of a flat void.
    let shaderFailed = null;
    try {
      renderer.debug.checkShaderErrors = true;
      renderer.debug.onShaderError = (gl, program, glVertexShader, glFragmentShader) => {
        let info = '';
        try { info = (gl.getProgramInfoLog(program) || '').trim(); } catch (_e) {}
        shaderFailed = info || 'shader compile/link failed';
        logSeam('tube shader FAILED to link -> falling back to 2D :: ' + shaderFailed);
      };
    } catch (_e) { /* older three without debug hook — the compile check below still guards */ }

    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(baseFov, (window.innerWidth || 1) / (window.innerHeight || 1), 0.1, 220);
    camera.position.set(0, 0, 0);
    camera.lookAt(0, 0, -1);

    const radial = tier === 'mobile' ? 18 : 32; // low-poly; shader does the detail
    const RADIUS = 5.2, LENGTH = 140;
    tubeRadius = RADIUS; tubeLength = LENGTH;
    // Cylinder is along Y by default; rotate so its length runs down -Z.
    geo = new THREE.CylinderGeometry(RADIUS, RADIUS, LENGTH, radial, 1, true);
    geo.rotateX(Math.PI / 2);
    geo.translate(0, 0, -LENGTH / 2 + 6); // near lip just ahead of the camera

    const C = (rgb) => new THREE.Color(rgb[0] / 255, rgb[1] / 255, rgb[2] / 255);
    mat = new THREE.ShaderMaterial({
      uniforms: {
        uScroll: { value: 0 },
        uIntensity: { value: curIntensity },
        uTime: { value: 0 },
        uBgCalm: { value: C(dressCur.bgCalm) },
        uBgHot: { value: C(dressCur.bgHot) },
        uLineCalm: { value: C(dressCur.lineCalm) },
        uLineHot: { value: C(dressCur.lineHot) },
        uSpiralCalm: { value: C(dressCur.spiralCalm) },
        uSpiralHot: { value: C(dressCur.spiralHot) },
        uFog: { value: C(dressCur.fog) },
        uRings: { value: dressCur.rings },     // fractional OK (len doesn't wrap)
        uRingW: { value: dressCur.ringW },
        uSpiralW: { value: dressCur.spiralW },
        uFar: { value: LENGTH * 0.85 },
        // spiral slots: x=arms (INTEGER — circumference wraps) y=turns z=dir w=weight
        uSpiralA: { value: new THREE.Vector4(sp.A.arms, sp.A.turns, sp.A.dir, 1) },
        uSpiralB: { value: new THREE.Vector4(0, 0, 1, 0) },
        uGlowMul: { value: dressCur.glowMul },
        uPulseAmt: { value: dressCur.pulseAmt },
        uDesat: { value: 0 },
        uSmear: { value: 0 },
        uWarp: { value: 0 },
        uWavePos: { value: -1 },
        uWaveAmp: { value: 0 },
        uWaveColor: { value: C([255, 105, 180]) },
        uStrobe: { value: 0 },
        uSparkle: { value: 0 },
        uBloom: { value: 0 },
        uIris: { value: 10 },
        uResolution: { value: new THREE.Vector2(1, 1) },
      },
      vertexShader: VERT,
      fragmentShader: FRAG,
      side: THREE.BackSide,
      depthWrite: false,
      extensions: { derivatives: true }, // fwidth() (core on WebGL2; safety net on WebGL1)
    });
    mesh = new THREE.Mesh(geo, mat);
    scene.add(mesh);
    mat.userData.LENGTH = LENGTH;
    sizeWebGL();

    // Force the program to compile NOW so a shader fault surfaces here (via the
    // onShaderError hook above) instead of silently producing a blank tube on the
    // first frame. If it failed, tear down and report false so the caller drops to
    // the 2D fallback rather than leaving the canvas an empty void.
    try { renderer.compile(scene, camera); } catch (e) { shaderFailed = shaderFailed || (e && e.message) || 'compile threw'; }
    if (shaderFailed) { teardownWebGL(); return false; }
    initDecals();
    return true;
  }

  // Wall dressing: plastered sections of the user's gifs on the bore (Section 2
  // onward). Lazily imported like three itself, so a broken/absent module can
  // never cost us the tube — it just leaves the wall clean. Skipped outright
  // under reduced motion and when the manifest carries no visual media.
  function initDecals() {
    if (reduced || decals) return;
    const hasMedia = !!media && ((Array.isArray(media.gifs) && media.gifs.length)
      || (Array.isArray(media.images) && media.images.length));
    if (!hasMedia) return;
    import('./wallDecals.js').then((m) => {
      if (disposed || decals || mode === 'dead' || !scene || !three) return;
      decals = m.createWallDecals({
        THREE: three, scene, renderer, media, tier, radius: tubeRadius,
      });
      // catch up to whatever the run already told us (setBand/setDepth may have
      // landed while the module was still loading)
      decals.setDepth(bandBiasDepth);
      if (curBand) decals.setBand(curBand);
    }).catch((e) => { logSeam('wall decals unavailable (' + ((e && e.message) || e) + ')'); });
  }

  // --- 2D fallback init -----------------------------------------------------
  function init2D() {
    try {
      ctx2d = canvas.getContext('2d');
    } catch (_e) {
      ctx2d = null;
    }
    if (!ctx2d) { mode = 'dead'; return false; } // nothing more we can do; stay blank (CSS gradient shows)
    size2D();
    return true;
  }

  function lerpRGB(a, b, t) {
    return [
      Math.round(a[0] + (b[0] - a[0]) * t),
      Math.round(a[1] + (b[1] - a[1]) * t),
      Math.round(a[2] + (b[2] - a[2]) * t),
    ];
  }
  const rgbStr = (c, alpha) => `rgba(${c[0]},${c[1]},${c[2]},${alpha})`;

  // route tint + desat applied JS-side so BOTH paths share one palette read
  function styledLine() {
    let line = lerpRGB(dressCur.lineCalm, dressCur.lineHot, curIntensity);
    if (route.curStrength > 0.005) line = lerpRGB(line, route.curColor, route.curStrength * 0.45);
    if (anim.desat > 0.005) {
      const lum = Math.round(line[0] * 0.299 + line[1] * 0.587 + line[2] * 0.114);
      line = lerpRGB(line, [lum, lum, lum], anim.desat);
    }
    return line;
  }

  /* --- 2D fallback frame: every reaction lands as a tint/flash approximation.
   * Rings ride the band dress (count/speed/palette), the answer wave is a
   * traveling bright ring, jackpot is a gold overlay strobe, praise a warm
   * wash, sparkle a few hashed glints, iris an evenodd mask, warp a wobble of
   * the vanishing point. Reduced fidelity, full API. */
  function render2D(nowMs) {
    if (!ctx2d) return;
    const w = canvas.width, h = canvas.height;
    const wob = warp.cur + anim.wobble * 0.4;
    const cx = w / 2 + Math.sin(nowMs * 0.0011) * wob * w * 0.02;
    const cy = h / 2 + Math.cos(nowMs * 0.0009) * wob * h * 0.02;
    const inten = curIntensity;
    const bg = lerpRGB(dressCur.bgCalm, dressCur.bgHot, inten);
    const line = styledLine();

    // background wash
    const maxR = Math.hypot(w / 2, h / 2);
    const g = ctx2d.createRadialGradient(cx, cy, 0, cx, cy, maxR);
    g.addColorStop(0, rgbStr(lerpRGB(bg, line, 0.15 * inten), 1));
    g.addColorStop(1, rgbStr(dressCur.fog, 1));
    ctx2d.fillStyle = g;
    ctx2d.fillRect(0, 0, w, h);

    // concentric rings scrolling outward = a cheap tunnel; count/glow ride the dress
    const RINGS = Math.max(8, Math.min(26, Math.round(dressCur.rings * 0.75)));
    const glow = (0.12 + 0.5 * inten) * dressCur.glowMul * (1 - anim.smear * 0.5);
    const throb = 1 + dressCur.pulseAmt * 0.4 * Math.sin(nowMs * 0.0023);
    ctx2d.lineWidth = Math.max(1, maxR * 0.006 * (1 + anim.smear * 3));
    for (let i = 0; i < RINGS; i++) {
      // phase 0..1 per ring, offset by scroll; radius grows non-linearly outward
      const p = ((i / RINGS) + (scroll % 1) + 1) % 1;
      const r = Math.pow(p, 1.8) * maxR * 1.05;
      const a = glow * throb * (0.25 + 0.75 * p); // brighter toward the rim
      ctx2d.strokeStyle = rgbStr(line, clamp01(a));
      ctx2d.beginPath();
      ctx2d.arc(cx, cy, r, 0, Math.PI * 2);
      ctx2d.stroke();
    }

    // traveling answer wave -> one bright expanding ring
    if (anim.wave.amp > 0.01 && anim.wave.pos >= 0) {
      const r = Math.pow(clamp01(anim.wave.pos), 1.8) * maxR * 1.05;
      ctx2d.lineWidth = Math.max(2, maxR * 0.012);
      ctx2d.strokeStyle = rgbStr(anim.wave.color, clamp01(anim.wave.amp));
      ctx2d.beginPath();
      ctx2d.arc(cx, cy, r, 0, Math.PI * 2);
      ctx2d.stroke();
    }

    // jackpot strobe cascade -> rhythmic gold wash
    if (anim.strobe > 0.01) {
      const s = anim.strobe * (0.35 + 0.65 * Math.pow(0.5 + 0.5 * Math.sin(nowMs * 0.02), 4));
      ctx2d.fillStyle = rgbStr([255, 209, 115], clamp01(s * 0.35));
      ctx2d.fillRect(0, 0, w, h);
    }

    // praise bloom -> warm radial wash
    if (anim.bloom > 0.01) {
      const bg2 = ctx2d.createRadialGradient(cx, cy, 0, cx, cy, maxR);
      bg2.addColorStop(0, rgbStr([255, 184, 122], clamp01(anim.bloom * 0.4)));
      bg2.addColorStop(1, rgbStr([255, 184, 122], 0));
      ctx2d.fillStyle = bg2;
      ctx2d.fillRect(0, 0, w, h);
    }

    // sparkle glints -> a handful of hashed dots
    if (anim.sparkle > 0.02) {
      const cell = Math.floor(nowMs / 100);
      ctx2d.fillStyle = rgbStr(line, clamp01(anim.sparkle));
      for (let i = 0; i < 6; i++) {
        const hx = Math.abs(Math.sin((cell + i) * 127.1 + i * 311.7)) % 1;
        const hy = Math.abs(Math.sin((cell + i) * 269.5 + i * 183.3)) % 1;
        const r = Math.max(1, maxR * 0.004);
        ctx2d.fillRect(hx * w, hy * h, r, r);
      }
    }

    // iris transition -> evenodd mask: fog outside the opening circle
    if (irisR < 2) {
      ctx2d.fillStyle = rgbStr(dressCur.fog, 1);
      ctx2d.beginPath();
      ctx2d.rect(0, 0, w, h);
      ctx2d.arc(cx, cy, Math.max(0.001, irisR * 0.5 * Math.min(w, h)), 0, Math.PI * 2, true);
      ctx2d.fill('evenodd');
    }
  }

  /* --- spiral slot machinery (band crossfades keep arm counts integer) ------ */
  function retargetSpiral(next) {
    const logical = sp.B || sp.A;
    if (logical.arms === next.arms && logical.turns === next.turns && logical.dir === next.dir) return;
    if (sp.B) { sp.A = sp.B; sp.mix = 0; } // interrupted mid-fade: promote, restart
    sp.B = { arms: next.arms, turns: next.turns, dir: next.dir };
    sp.mix = 0;
  }
  function easeSpiral(k) {
    if (!sp.B) return;
    sp.mix += (1 - sp.mix) * k;
    if (sp.mix > 0.995) { sp.A = sp.B; sp.B = null; sp.mix = 0; }
  }

  /* --- transition state machine (scripted set-pieces; shared by both modes) - */
  function finishTrans() {
    if (!trans) return;
    const t = trans;
    trans = null;
    irisR = 10;
    if (t.timer) { clearTimeout(t.timer); t.timer = null; }
    try { t.resolve(); } catch (_e) {}
  }

  function stepTrans(dt, nowMs) {
    if (!trans) {
      // scripted speed eases home when nothing is running
      transSpeedMul += (1 - transSpeedMul) * Math.min(1, dt * 3);
      return;
    }
    trans.t += dt;
    const p = clamp01(trans.t / trans.dur);
    const ease = p * p * (3 - 2 * p); // smoothstep
    switch (trans.kind) {
      case 'iris': {
        // the tube irises open from a point (radial mask sweep + bright rim)
        irisR = 0.02 + 1.9 * ease;
        break;
      }
      case 'fork': {
        // junction flyby: the spiral splits into two counter-rotating arm sets
        // that diverge, then one wins. Borrows slot B (weights driven directly).
        const env = p < 0.35 ? p / 0.35 : (p < 0.7 ? 1 : clamp01((1 - p) / 0.3));
        trans.forkMix = env * 0.85;
        trans.forkTurns = trans.forkBase.turns * (1 + p * 1.2); // the twin diverges
        anim.wobble = Math.max(anim.wobble, 0.25 * Math.sin(Math.PI * p));
        break;
      }
      case 'plunge': {
        // freefall: hard speed ramp + FOV stretch + ring smear; lands in Climax
        transSpeedMul = 1 + 2.4 * ease * (p < 0.85 ? 1 : clamp01((1 - p) / 0.15) * 0.6 + 0.4);
        anim.smear = Math.max(anim.smear, 0.9 * Math.sin(Math.PI * Math.min(1, p * 1.15)));
        anim.fovKick = Math.max(anim.fovKick, 16 * Math.sin(Math.PI * p));
        break;
      }
      case 'surface': {
        // rise-out: speed reverses feel then settles calm; palette lifts (dress)
        transSpeedMul = p < 0.4 ? 1 - 3.2 * (p / 0.4) : -2.2 + 2.45 * clamp01((p - 0.4) / 0.6);
        anim.bloom = Math.max(anim.bloom, 0.35 * Math.sin(Math.PI * p));
        break;
      }
    }
    if (p >= 1) finishTrans();
  }

  // --- the frame loop (owned rAF; never blocks the beat loop) ---------------
  function frame(now) {
    if (!running || disposed) return;
    // FROZEN (freeze-gate): keep the rAF alive but advance nothing — the tube
    // holds stone-still. Track lastT so dt doesn't spike on resume.
    if (frozen) { lastT = now; rafId = requestAnimationFrame(frame); return; }
    const dt = lastT ? Math.min(0.05, (now - lastT) / 1000) : 0.016;

    // pace on mobile: skip the heavy work but keep the rAF alive
    if (minFrameMs && lastT && (now - lastT) < minFrameMs) {
      rafId = requestAnimationFrame(frame);
      return;
    }
    lastT = now;

    // ease the displayed intensity toward the target (Recovery ramps down smoothly)
    curIntensity += (targetIntensity - curIntensity) * Math.min(1, dt * 3);

    // ~1.5s band-dress crossfade: colors + scalars ease, spiral slots blend
    const kDress = Math.min(1, dt / DRESS_TAU);
    for (const f of DRESS_FIELDS_COLOR) {
      const c = dressCur[f], t = dressTarget[f];
      c[0] += (t[0] - c[0]) * kDress;
      c[1] += (t[1] - c[1]) * kDress;
      c[2] += (t[2] - c[2]) * kDress;
    }
    for (const f of DRESS_FIELDS_SCALAR) dressCur[f] += (dressTarget[f] - dressCur[f]) * kDress;
    easeSpiral(kDress);

    // route tint + steer warp ease toward their targets
    route.curStrength += (route.strength - route.curStrength) * Math.min(1, dt * 1.5);
    const rc = route.curColor, rt = route.color;
    rc[0] += (rt[0] - rc[0]) * kDress; rc[1] += (rt[1] - rc[1]) * kDress; rc[2] += (rt[2] - rc[2]) * kDress;
    warp.cur += (warp.target - warp.cur) * Math.min(1, dt * 2.5);

    // transient decays (exp): surge ~800ms, stall/rot ~600ms, the rest softer
    anim.surge *= Math.exp(-dt / 0.28);
    anim.stall *= Math.exp(-dt / 0.20);
    anim.desat *= Math.exp(-dt / 0.35);
    anim.rotVel *= Math.exp(-dt / 0.20);
    anim.wobble *= Math.exp(-dt / 0.40);
    anim.smear *= Math.exp(-dt / 0.50);
    anim.bloom *= Math.exp(-dt / 0.55);
    anim.sparkle *= Math.exp(-dt / 0.30);
    anim.fovKick *= Math.exp(-dt / 0.35);
    anim.rot += anim.rotVel * dt;
    if (anim.strobeT > 0) {
      anim.strobeT -= dt;
      if (anim.strobeT <= 0) { anim.strobe = 0; anim.strobeT = 0; }
      else anim.strobe *= anim.strobeT < 0.4 ? Math.exp(-dt / 0.15) : 1;
    }
    if (anim.wave.amp > 0.01 && anim.wave.pos >= 0) {
      anim.wave.pos += dt * anim.wave.speed;
      if (anim.wave.pos > 1.3) { anim.wave.amp = 0; anim.wave.pos = -1; }
    }

    // scripted set-piece (iris/fork/plunge/surface)
    stepTrans(dt, now);

    // scroll speed rides depth * band dress * reactions; reduced-motion crawls
    const base = (reduced ? 0.04 : 0.18) + (reduced ? 0.06 : 0.9) * curIntensity;
    const speed = base * dressCur.speedMul * transSpeedMul
      * (1 + 1.4 * anim.surge) * (1 - 0.9 * clamp01(anim.stall));
    scroll += dt * speed;

    if (mode === 'webgl' && renderer && mat) {
      const u = mat.uniforms;
      u.uScroll.value = scroll;
      u.uIntensity.value = curIntensity;
      u.uTime.value += dt;
      // dress colors -> uniforms (route tint bleeds into line/spiral, faintly bg)
      const rs = route.curStrength;
      const T = (uName, rgb, bleed) => {
        let c = rgb;
        if (rs > 0.005 && bleed) c = lerpRGB(rgb, route.curColor, rs * bleed);
        u[uName].value.setRGB(c[0] / 255, c[1] / 255, c[2] / 255);
      };
      T('uBgCalm', dressCur.bgCalm, 0.15);
      T('uBgHot', dressCur.bgHot, 0.20);
      T('uLineCalm', dressCur.lineCalm, 0.45);
      T('uLineHot', dressCur.lineHot, 0.50);
      T('uSpiralCalm', dressCur.spiralCalm, 0.45);
      T('uSpiralHot', dressCur.spiralHot, 0.50);
      T('uFog', dressCur.fog, 0);
      u.uRings.value = dressCur.rings;
      u.uRingW.value = dressCur.ringW;
      u.uSpiralW.value = dressCur.spiralW;
      u.uGlowMul.value = dressCur.glowMul;
      u.uPulseAmt.value = dressCur.pulseAmt;
      const far = (mat.userData.LENGTH || 140) * 0.85 * dressCur.farMul;
      u.uFar.value = far;
      // Wall dressing rides the SAME scroll: one shader scroll unit advances the
      // ring pattern by LENGTH/rings world units, so feeding the decals that
      // distance glues them to the rings instead of sliding against them.
      if (decals) {
        try { decals.update(dt, dt * speed * (tubeLength / Math.max(1, dressCur.rings)), far); }
        catch (_e) { /* one bad decal frame never kills the tube */ }
      }
      // spiral slots: band crossfade — or the fork's counter-rotating twin
      if (trans && trans.kind === 'fork') {
        const fb = trans.forkBase;
        u.uSpiralA.value.set(fb.arms, fb.turns, fb.dir, 1 - 0.35 * trans.forkMix);
        u.uSpiralB.value.set(fb.arms, trans.forkTurns, -fb.dir, trans.forkMix);
      } else if (sp.B) {
        u.uSpiralA.value.set(sp.A.arms, sp.A.turns, sp.A.dir, 1 - sp.mix);
        u.uSpiralB.value.set(sp.B.arms, sp.B.turns, sp.B.dir, sp.mix);
      } else {
        u.uSpiralA.value.set(sp.A.arms, sp.A.turns, sp.A.dir, 1);
        u.uSpiralB.value.w = 0;
      }
      // reactions
      u.uDesat.value = clamp01(anim.desat);
      u.uSmear.value = clamp01(anim.smear);
      u.uWarp.value = clamp01(warp.cur + anim.wobble * 0.25);
      u.uWavePos.value = anim.wave.pos;
      u.uWaveAmp.value = anim.wave.amp;
      u.uWaveColor.value.setRGB(anim.wave.color[0] / 255, anim.wave.color[1] / 255, anim.wave.color[2] / 255);
      u.uStrobe.value = anim.strobe;
      u.uSparkle.value = clamp01(anim.sparkle);
      u.uBloom.value = clamp01(anim.bloom);
      u.uIris.value = irisR;
      // camera: roll kick + wobble; FOV lurch on drops/plunges + steer fisheye
      if (mesh) mesh.rotation.z = anim.rot + anim.wobble * 0.05 * Math.sin(now * 0.007);
      const wantFov = baseFov + anim.fovKick + warp.cur * 6;
      if (camera && Math.abs(wantFov - curFov) > 0.1) {
        curFov = wantFov;
        camera.fov = curFov;
        camera.updateProjectionMatrix();
      }
      try { renderer.render(scene, camera); }
      catch (_e) { /* GL hiccup — drop the frame, keep the loop */ }
    } else if (mode === '2d') {
      render2D(now);
    }

    rafId = requestAnimationFrame(frame);
  }

  function startLoop() {
    if (running || disposed) return;
    running = true;
    lastT = 0;
    rafId = requestAnimationFrame(frame);
  }
  function stopLoop() {
    running = false;
    if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
  }

  // Resolve the render mode once (lazy), then start the loop if still enabled.
  async function ensureStarted() {
    if (disposed) return;
    if (mode === 'webgl' || mode === '2d') { startLoop(); return; }
    if (mode === 'dead') return;
    if (starting) return; // an init is already in flight
    starting = true;
    try {
      const canWebGL = !reduced && hasWebGL();
      let ok = false;
      if (canWebGL) {
        try {
          ok = await initWebGL();
          if (ok) mode = 'webgl';
        } catch (e) {
          // three failed to load/compile — make it LOUD (no devtools in-app), then
          // tear down any partial GL and drop to 2D.
          logSeam('WebGL init failed (' + ((e && e.message) || e) + ') -> 2D fallback');
          teardownWebGL();
          ok = false;
        }
      }
      if (!ok && !disposed) {
        if (init2D()) mode = '2d';
      }
    } finally {
      starting = false;
    }
    if (disposed) { teardownWebGL(); return; }
    if (enabled && (mode === 'webgl' || mode === '2d')) {
      canvas.style.display = '';
      startLoop();
    }
  }

  function teardownWebGL() {
    try { if (decals) decals.dispose(); } catch (_e) {}
    decals = null;
    try { if (geo) geo.dispose(); } catch (_e) {}
    try { if (mat) mat.dispose(); } catch (_e) {}
    try {
      if (renderer) {
        renderer.dispose();
        // free the GL context promptly (important on mobile GPUs)
        const gl = renderer.getContext && renderer.getContext();
        const lose = gl && gl.getExtension && gl.getExtension('WEBGL_lose_context');
        if (lose) lose.loseContext();
      }
    } catch (_e) {}
    if (mesh && scene) { try { scene.remove(mesh); } catch (_e) {} }
    three = renderer = scene = camera = mat = geo = mesh = null;
  }

  // --- public surface (contracts.js BACKGROUND) -----------------------------
  // Named (not returned inline) so internal cross-calls (transition -> setBand)
  // survive callers destructuring methods off the object.
  const api = {
    /** depth 0..1 -> tube tint/speed WITHIN the band/biome dressing. Cheap store. */
    setDepth(depth) {
      if (disposed) return;
      targetIntensity = bgIntensityFor(depth);
      bandBiasDepth = clamp01(typeof depth === 'number' ? depth : 0); // steers biome pick
      if (decals) decals.setDepth(bandBiasDepth); // density breathes within the band
    },

    /** Band.* -> per-band dressing target; the loop crossfades ~1.5s. Once biome
     *  rotation is engaged the biomes own the palette/geometry, so setBand only
     *  records the band context (used for the depth bias) and leaves the dress. */
    setBand(band) {
      if (disposed) return;
      curBand = band;
      // The wall dressing is BAND-driven, never biome-driven: it stays clean
      // through Calibration, thickens Establishing -> Climax, and is scrubbed
      // off again through Recovery — so this runs before the biome bail-out.
      if (decals) decals.setBand(band);
      if (biomeEngaged) return; // biomes own the dress now — don't stomp it
      const d = BAND_DRESS[band];
      if (!d) return; // unknown band -> keep the current dress
      dressTarget = cloneDress(d);
      retargetSpiral({ arms: d.arms, turns: d.turns, dir: d.dir });
    },

    /** Advance to the next DtRH biome dress — call once per beat. Shuffled deck
     *  (no immediate repeat, even coverage), lightly biased so calm biomes tend
     *  to land shallow and wild ones deep. The dress crossfades over ~1.4s via
     *  the same easing setBand uses; nothing hard-cuts. Reduced-motion strips the
     *  throb and holds the speed to a crawl so the switch never strobes. Returns
     *  the chosen biome's identity (or null if disabled). Safe before init. */
    nextBiome() {
      if (disposed || !BIOME_DRESSES.length) return null;
      biomeEngaged = true;
      biomeIdx = pickNextBiomeIndex();
      const entry = BIOME_DRESSES[biomeIdx];
      dressTarget = cloneDress(entry.dress);
      if (reduced) { dressTarget.pulseAmt = 0; dressTarget.speedMul = Math.min(dressTarget.speedMul, 0.38); }
      retargetSpiral({ arms: entry.dress.arms, turns: entry.dress.turns, dir: entry.dress.dir });
      return { id: entry.id, name: entry.name, glyph: entry.glyph, room: entry.room };
    },

    /** Correct -> surge + traveling glow pulse; wrong -> stall/desat/counter-kick. */
    onAnswer(ev) {
      if (disposed) return;
      const correct = !!(ev && ev.correct);
      const steered = !!(ev && ev.steered);
      if (correct) {
        anim.surge = Math.max(anim.surge, 1);                    // ~800ms ease-out
        anim.wave.pos = 0;
        anim.wave.speed = 1.3;
        anim.wave.amp = 0.9;
        anim.wave.color = styledLine();                          // band-hot pulse
      } else {
        anim.stall = Math.max(anim.stall, 1);                    // ~600ms dip
        anim.desat = Math.max(anim.desat, 0.6);
        anim.rotVel += (Math.random() < 0.5 ? -1 : 1) * 0.9;     // counter-rotation kick
      }
      if (steered) anim.wobble = Math.max(anim.wobble, 0.6);     // assisted-commit wobble
    },

    /** RewardEvent -> tube payoff. `intensity` is ALREADY cap-clamped upstream. */
    onReward(re) {
      if (disposed || !re) return;
      const i = clamp01(typeof re.intensity === 'number' ? re.intensity : 0);
      if (!re.fire) {
        if (re.nearMiss) { // single faint shimmer ring, slow + silver
          anim.wave.pos = 0;
          anim.wave.speed = 0.8;
          anim.wave.amp = Math.max(anim.wave.amp, 0.18);
          anim.wave.color = [200, 210, 230];
        }
        return;
      }
      if (re.jackpot) { // full-tube gold strobe cascade ~1.5s
        anim.strobe = Math.max(anim.strobe, 0.35 + 0.65 * i);
        anim.strobeT = 1.5;
      }
      switch (re.kind) {
        case 'drop':   // plunge burst: z-speed + FOV lurch + ring smear
          anim.surge = Math.max(anim.surge, 1.5 * i);
          anim.smear = Math.max(anim.smear, i);
          anim.fovKick = Math.max(anim.fovKick, 12 * i);
          break;
        case 'praise': // warm bloom swell
          anim.bloom = Math.max(anim.bloom, i);
          break;
        case 'bubble':
        case 'chime':  // small wall glints (cheap glow-noise boost)
          anim.sparkle = Math.max(anim.sparkle, 0.6 * i);
          break;
        case 'flash':  // brief glow lift rides the bloom channel, gently
          anim.bloom = Math.max(anim.bloom, 0.35 * i);
          break;
        default: break;
      }
    },

    /** Scripted 1-2s set-piece. Resolves when it lands; NEVER rejects. */
    transition(kind) {
      if (disposed || !enabled || mode === 'dead' || reduced ||
          !['iris', 'fork', 'plunge', 'surface'].includes(kind)) {
        // still honor the destination dressings so the story reads
        if (!disposed) {
          if (kind === 'plunge') api.setBand('climax');
          else if (kind === 'surface') api.setBand('recovery');
        }
        return Promise.resolve();
      }
      finishTrans(); // one at a time; the interrupted one resolves immediately
      return new Promise((resolve) => {
        const durs = { iris: 1.4, fork: 1.8, plunge: 1.8, surface: 2.0 };
        trans = { kind, t: 0, dur: durs[kind] || 1.5, resolve, timer: 0 };
        if (kind === 'iris') irisR = 0.02;
        if (kind === 'fork') {
          const logical = sp.B || sp.A;
          trans.forkBase = { arms: logical.arms, turns: logical.turns, dir: logical.dir };
          trans.forkMix = 0;
          trans.forkTurns = trans.forkBase.turns;
        }
        if (kind === 'plunge') api.setBand('climax');    // lands settled in Climax
        if (kind === 'surface') api.setBand('recovery'); // lands calm in Recovery
        // safety: resolve even if the loop stalls/stops mid-piece
        trans.timer = setTimeout(() => { if (trans && trans.resolve === resolve) finishTrans(); else resolve(); },
          (trans.dur + 0.6) * 1000);
      });
    },

    /** Leading archetype's signature hue bleeds into the palette; eased. */
    setRouteTint(rt) {
      if (disposed || !rt) return;
      route.color = hueToRGB(typeof rt.hue === 'number' ? rt.hue : 330);
      route.strength = clamp01(typeof rt.strength === 'number' ? rt.strength : 0);
    },

    /** 0..1 heavy-steer distortion (barrel wobble + FOV push); 0 resets clean. */
    setSteerWarp(intensity) {
      if (disposed) return;
      warp.target = clamp01(typeof intensity === 'number' ? intensity : 0);
    },

    /** FREEZE the tube (freeze-gate): true holds the animation stone-still (rAF
     *  stays alive so resume is seamless); false resumes. Idempotent; safe to call
     *  before the loop has started (the flag is honored once frames run). */
    setFrozen(on) {
      if (disposed) return;
      frozen = !!on;
    },

    /** Off-switch. true (re)starts + shows the canvas; false stops + hides it. */
    setEnabled(on) {
      if (disposed) return;
      enabled = !!on;
      if (enabled) {
        canvas.style.display = '';
        ensureStarted(); // fire-and-forget; safe if called repeatedly
      } else {
        stopLoop();
        finishTrans(); // never leave a set-piece promise hanging while dark
        try { canvas.style.display = 'none'; } catch (_e) {}
      }
    },

    /** Release GL resources + cancel rAF + drop listeners. Idempotent. */
    dispose() {
      if (disposed) return;
      finishTrans(); // resolve any in-flight set-piece before going dark
      disposed = true;
      enabled = false;
      stopLoop();
      window.removeEventListener('resize', onResize);
      if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', onVisibility);
      teardownWebGL();
      ctx2d = null;
      mode = 'dead';
      try { canvas.style.display = 'none'; } catch (_e) {}
    },
  };
  return api;
}
