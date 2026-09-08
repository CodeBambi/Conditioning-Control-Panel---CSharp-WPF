/* ============================================================================
 * board/jiggle.js - the men are made of silicone, so they flex.
 *
 * Every piece carries one damped spring (a sideways bend plus a squash) and a
 * vertex shader that reads it. The bend is weighted by height, so the base
 * stays planted on its square and the tip does the swinging, the way a soft
 * toy flicked at the top behaves. Nothing here scales the object: the whole
 * deformation happens in the vertex shader, normals included, so the lighting
 * follows the bend instead of sitting painted on a rigid shape.
 *
 * The spring lives in the piece's LOCAL space. World-space impulses (a slide
 * direction, a drag delta) are rotated into it first, so a black man, who is
 * turned to face down the board, leans the same way a white one does.
 *
 * Wiring, all of it optional, all of it one line at the call site:
 *   pieces.js  attach on build, idle sway from the meter wobble
 *   anim.js    land on arrival, buzz drives a fast small bend
 *   drag.js    sag on grab, lag while the pointer drags it around
 *   boot.js    update(dt) once a frame, after everything else has moved
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number that decides how the men feel. One place, on purpose. */
export const TUNING = Object.freeze({
  // Bend spring: about 4 Hz with a light damping, so a flick rings for roughly
  // 0.8 s and shows two to three swings before it settles.
  bendStiffness: 620,
  bendDamping: 7.4,
  // Squash rings faster and dies sooner; a landing should not keep pumping.
  squashStiffness: 1150,
  squashDamping: 11.5,
  // Height weighting. Bend is quadratic (planted base, whippy tip), squash is
  // linear so the body squats as one instead of only deflating the head.
  bendWeightPow: 2.0,
  squashWeightPow: 1.0,
  volumeGain: 0.5,          // how far x/z inflate to pay for the lost height
  // Impulses. All are divided by the piece's world height, so a king barely
  // stirs where a pawn whips.
  landBend: 3.2,
  landSquash: 5.0,
  captureGain: 1.75,        // the man doing the taking arrives harder
  grabSquash: -2.6,         // negative squash is a stretch: the tip sags
  grabBend: 1.1,
  dragLag: 8,               // bend velocity per world unit of pointer travel
  buzzAmp: 0.05,            // forced bend while a piece is giving check
  buzzFastX: 62,
  buzzFastZ: 47,
  idleAmp: 0.08,            // forced bend at wobble 1.0, zero at wobble 0
  idleFreq: 1.15,
  idleCross: 0.72,          // the z lobe runs at a different rate than x
  rippleWaves: 5.0,         // travelling ripple, scaled by the live bend
  rippleSpeed: 9.0,
  rippleGain: 0.22,
  maxBend: 0.34,            // local units; the men are 1.0 tall in local space
  maxSquash: 0.45,
  reducedScale: 0.3,        // impulse multiplier when motion is turned down
  substep: 1 / 240,         // the spring is stiff, so integrate it small
});

const CACHE_KEY = 'pbp-jiggle-1';
const T = TUNING;
const f = (n) => (Number.isInteger(n) ? n.toFixed(1) : String(n));

const PRELUDE = `
uniform vec2 uBend;
uniform float uSquash;
uniform float uHeight;
uniform float uPhase;
uniform float uTime;
float pbpH(float y) { return clamp(y / max(uHeight, 0.0001), 0.0, 1.0); }
`;

const BEND_VERTEX = `#include <begin_vertex>
{
  float h = pbpH(position.y);
  float wb = pow(h, ${f(T.bendWeightPow)});
  float ws = pow(h, ${f(T.squashWeightPow)});
  vec2 wave = uBend * (sin(h * ${f(T.rippleWaves)} - uTime * ${f(T.rippleSpeed)} + uPhase) * ${f(T.rippleGain)});
  vec2 off = (uBend + wave) * wb;
  transformed.y *= (1.0 - uSquash * ws);
  transformed.xz *= (1.0 + ${f(T.volumeGain)} * uSquash);
  transformed.x += off.x;
  transformed.z += off.y;
}`;

// The normal is rotated by the derivative of the bend and rescaled by the
// inverse of the squash. It is an approximation (the ripple term is left out)
// but it is enough to keep a bent flank lit like a bent flank.
const BEND_NORMAL = `#include <beginnormal_vertex>
{
  float h = pbpH(position.y);
  vec2 slope = uBend * (${f(T.bendWeightPow)} * pow(h, ${f(T.bendWeightPow - 1)}) / max(uHeight, 0.0001));
  objectNormal.y -= slope.x * objectNormal.x + slope.y * objectNormal.z;
  float sy = max(1.0 - uSquash * pow(h, ${f(T.squashWeightPow)}), 0.05);
  float sxz = max(1.0 + ${f(T.volumeGain)} * uSquash, 0.05);
  objectNormal = normalize(vec3(objectNormal.x / sxz, objectNormal.y / sy, objectNormal.z / sxz));
}`;

function prefersReducedMotion() {
  if (typeof window === 'undefined') return false;
  if (window.PBP && window.PBP.reducedMotion) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

export function createJiggle() {
  const states = new Map();       // piece root -> spring state
  const dir = new THREE.Vector3();
  const inv = new THREE.Quaternion();
  let clock = 0;
  let wobble = 0;
  let lastCost = 0;

  const gain = () => (prefersReducedMotion() ? T.reducedScale : 1);

  function install(material, u) {
    material.userData.pbpJiggle = u;
    material.onBeforeCompile = (shader) => {
      shader.uniforms.uBend = u.uBend;
      shader.uniforms.uSquash = u.uSquash;
      shader.uniforms.uHeight = u.uHeight;
      shader.uniforms.uPhase = u.uPhase;
      shader.uniforms.uTime = u.uTime;
      shader.vertexShader = PRELUDE + shader.vertexShader
        .replace('#include <beginnormal_vertex>', BEND_NORMAL)
        .replace('#include <begin_vertex>', BEND_VERTEX);
    };
    // Every jiggling material compiles the same program, so they share one
    // cache entry; without a key of our own three would key them apart.
    material.customProgramCacheKey = () => CACHE_KEY;
    material.needsUpdate = true;
  }

  /** Give a freshly built piece its spring and hook up every mesh it owns. */
  function attach(piece) {
    if (states.has(piece)) return states.get(piece);
    const u = {
      uBend: { value: new THREE.Vector2(0, 0) },
      uSquash: { value: 0 },
      uHeight: { value: 1 },
      uPhase: { value: Math.random() * Math.PI * 2 },
      uTime: { value: 0 },
    };
    let height = 0;
    piece.traverse((o) => {
      if (!o.isMesh || !o.material) return;
      if (o.geometry) {
        if (!o.geometry.boundingBox) o.geometry.computeBoundingBox();
        const bb = o.geometry.boundingBox;
        if (bb) height = Math.max(height, o.position.y + bb.max.y);
      }
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      const next = mats.map((m) => {
        // A material already bound to another piece would share its spring, so
        // that piece gets its own copy. Art loaded from a glb can land here.
        const mine = m.userData && m.userData.pbpJiggle;
        const use = mine && mine !== u ? m.clone() : m;
        if (!use.userData.pbpJiggle || use.userData.pbpJiggle !== u) install(use, u);
        return use;
      });
      o.material = Array.isArray(o.material) ? next : next[0];
    });
    u.uHeight.value = height > 0.01 ? height : 1;
    const world = (piece.scale.y || 1) * u.uHeight.value;
    const state = {
      piece, u,
      bend: new THREE.Vector2(0, 0),
      vel: new THREE.Vector2(0, 0),
      squash: 0, sVel: 0,
      forced: new THREE.Vector2(0, 0),
      phase: Math.random() * Math.PI * 2,
      height: u.uHeight.value,
      world: world > 0.05 ? world : 1,
    };
    states.set(piece, state);
    return state;
  }

  function release(piece) { states.delete(piece); }

  const stateOf = (piece) => (piece ? states.get(piece) || null : null);

  /** World-space x/z into the piece's own frame, so black leans like white. */
  function toLocal(piece, x, z) {
    inv.copy(piece.quaternion).invert();
    dir.set(x, 0, z).applyQuaternion(inv);
    return dir;
  }

  /** Kick the spring. bend is world x/z; squash is positive for a squat. */
  function impulse(piece, { bend = null, squash = 0, local = false } = {}) {
    const s = stateOf(piece);
    if (!s) return;
    const g = gain() / s.world;
    if (bend) {
      const v = local ? dir.set(bend[0], 0, bend[1]) : toLocal(piece, bend[0], bend[1]);
      s.vel.x += v.x * g;
      s.vel.y += v.z * g;
    }
    if (squash) s.sVel += squash * g;
  }

  /**
   * A piece has arrived on its square after travelling `travel` (world x/z).
   * The tip lags behind the travel, then whips through it.
   */
  function land(piece, travel = null) {
    const s = stateOf(piece);
    if (!s) return;
    const hard = piece.userData.tookOne ? T.captureGain : 1;
    piece.userData.tookOne = false;
    let bx = 0, bz = 0;
    if (travel) {
      const len = Math.hypot(travel[0], travel[1]) || 1;
      bx = (-travel[0] / len) * T.landBend * hard;
      bz = (-travel[1] / len) * T.landBend * hard;
    }
    impulse(piece, { bend: [bx, bz], squash: T.landSquash * hard });
  }

  /** Lifted off the board: the tip stretches down under its own weight. */
  function grab(piece) {
    const s = stateOf(piece);
    if (!s) return;
    impulse(piece, { squash: T.grabSquash });
    const a = Math.random() * Math.PI * 2;
    impulse(piece, { bend: [Math.cos(a) * T.grabBend, Math.sin(a) * T.grabBend] });
  }

  /** Dragged: the tip trails the pointer by however far the body just moved. */
  function lag(piece, dx, dz) {
    if (!dx && !dz) return;
    impulse(piece, { bend: [-dx * T.dragLag, -dz * T.dragLag] });
  }

  /** A bend held on for one frame, on top of the spring. Buzz and idle use it. */
  function drive(piece, x, z) {
    const s = stateOf(piece);
    if (!s) return;
    s.forced.x += x;
    s.forced.y += z;
  }

  function step(s, dt) {
    s.vel.x += (-T.bendStiffness * s.bend.x - T.bendDamping * s.vel.x) * dt;
    s.vel.y += (-T.bendStiffness * s.bend.y - T.bendDamping * s.vel.y) * dt;
    s.bend.x += s.vel.x * dt;
    s.bend.y += s.vel.y * dt;
    s.sVel += (-T.squashStiffness * s.squash - T.squashDamping * s.sVel) * dt;
    s.squash += s.sVel * dt;
  }

  function update(dt) {
    const t0 = performance.now();
    clock += dt;
    const steps = Math.max(1, Math.min(24, Math.ceil(dt / T.substep)));
    const h = dt / steps;
    const idle = wobble > 0.001 && !prefersReducedMotion() ? wobble * T.idleAmp : 0;
    for (const [piece, s] of states) {
      if (!piece.parent) { states.delete(piece); continue; }
      for (let i = 0; i < steps; i++) step(s, h);
      s.bend.x = THREE.MathUtils.clamp(s.bend.x, -T.maxBend, T.maxBend);
      s.bend.y = THREE.MathUtils.clamp(s.bend.y, -T.maxBend, T.maxBend);
      s.squash = THREE.MathUtils.clamp(s.squash, -T.maxSquash, T.maxSquash);
      if (idle && !piece.userData.busy && !piece.userData.held) {
        s.forced.x += Math.sin(clock * T.idleFreq + s.phase) * idle;
        s.forced.y += Math.cos(clock * T.idleFreq * T.idleCross + s.phase * 1.7) * idle;
      }
      s.u.uBend.value.set(s.bend.x + s.forced.x, s.bend.y + s.forced.y);
      s.u.uSquash.value = s.squash;
      s.u.uTime.value = clock;
      s.forced.set(0, 0);
    }
    lastCost = performance.now() - t0;
  }

  return {
    attach, release, update, impulse, land, grab, lag, drive,
    setWobble(v) { wobble = Math.max(0, Math.min(1, Number(v) || 0)); },
    /** Harness and ramp entry point: poke(piece, {bend:[x,z], squash}). */
    poke(piece, opts = {}) { impulse(piece, { bend: opts.bend || null, squash: opts.squash || 0 }); },
    /** Live spring state for one piece, for the smoke harness. */
    debug(piece) {
      const s = stateOf(piece);
      if (!s) return null;
      return {
        bend: [s.bend.x, s.bend.y], vel: [s.vel.x, s.vel.y],
        squash: s.squash, squashVel: s.sVel,
        height: s.height, world: s.world,
        uBend: [s.u.uBend.value.x, s.u.uBend.value.y], uSquash: s.u.uSquash.value,
      };
    },
    stats() { return { pieces: states.size, lastMs: lastCost, reduced: prefersReducedMotion() }; },
  };
}
