// race/kart.js - The Caucus Race: the kart (the teacup) in track space, its placement on the road,
// and the chase camera. Implements CONTRACT.md "race/kart.js (PR 3)". The cup + EMI meshes,
// antenna moods, sweat and sparks live in race/emi.js; this file owns speed, steering, drift,
// ramps, the lap counter and the camera seat. There is no fail state: speed never drops below
// KART_MIN_SPEED and the road walls are soft.

import * as THREE from 'three';
import {
  KART_BASE_SPEED, KART_MAX_SPEED, KART_MIN_SPEED, GRAVITY, ROAD_HALF_W,
  CAM_BACK, CAM_UP, CAM_LOOK_AHEAD,
} from './consts.js';
import { createEmiRig } from './emi.js';

const STEER_VMAX = 6.5;       // lateral m/s at full lock, cruise speed, no drift
const DRIFT_STEER = 1.45;     // drift tightens the steer
const WALL_SOFT = 0.7;        // metres from the road edge where the soft wall starts easing you back
const WORLD_UP = new THREE.Vector3(0, 1, 0), ZERO = new THREE.Vector3();
const _m = new THREE.Matrix4(), _q = new THREE.Quaternion(), _v = new THREE.Vector3();
const _fwd = new THREE.Vector3(), _up = new THREE.Vector3(), _right = new THREE.Vector3(), _lvl = new THREE.Vector3();
const AX_X = new THREE.Vector3(1, 0, 0), AX_Z = new THREE.Vector3(0, 0, 1);

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (k, dt) => 1 - Math.exp(-k * dt);

/**
 * createKart({ scene, layout, reducedMotion }) -> kart
 * kart.update(dt, input, layout)  input = { steer:-1..1, accel:0..1, brake:0..1, drift:bool }
 * kart.camera(out)                out = { pos: Vector3, look: Vector3, up?: Vector3, roll: number }
 */
export function createKart({ scene, layout, reducedMotion = false }) {
  const state = {
    d: 0, x: 0, h: 0, vh: 0, speed: KART_BASE_SPEED, steer: 0, drift: false, airborne: false,
    boostSec: 0, slowMult: 1, slowSec: 0, lap: 0,
  };
  const rig = createEmiRig({ scene, reducedMotion });
  const group = rig.group;
  scene.add(group);

  let vx = 0, lean = 0, pitch = 0, elapsed = 0, lastRampD = -1, driftSide = 1, camReady = false, camX = 0, camH = 0;
  const cam = { pos: new THREE.Vector3(), look: new THREE.Vector3(), up: new THREE.Vector3(0, 1, 0), roll: 0 };
  const ctx = { t: 0, up: _up, right: _right, tangent: _fwd, speedNorm: 0, airborne: false, steerVel: 0, drift: false, driftSide: 1 };

  function applyBoost(sec) { state.boostSec = Math.max(state.boostSec, +sec || 0); }
  function applySlow(mult, sec) {
    state.slowMult = clamp(+mult || 1, 0.2, 1);
    state.slowSec = Math.max(state.slowSec, +sec || 0);
  }

  function launch(ramp) {
    const height = Math.max(0.5, +ramp.height || 2.4);
    const scale = clamp(state.speed / KART_BASE_SPEED, 0.7, 1.3);   // faster = higher, never a fail
    state.vh = Math.sqrt(2 * GRAVITY * height * scale);
    state.airborne = true;
    lastRampD = ramp.d;
  }

  function stepSpeed(dt, input) {
    if (state.boostSec > 0) state.boostSec = Math.max(0, state.boostSec - dt);
    if (state.slowSec > 0) { state.slowSec = Math.max(0, state.slowSec - dt); if (state.slowSec === 0) state.slowMult = 1; }
    const cap = state.boostSec > 0 ? KART_MAX_SPEED : KART_BASE_SPEED;
    // accel holds cruise, letting go coasts a touch under it, boost pins the cap
    let target = state.boostSec > 0 ? cap : cap * (0.88 + 0.12 * input.accel);
    if (state.drift) target *= 1.04;                                   // small speed keep while drifting
    target = target * (1 - input.brake) + KART_MIN_SPEED * input.brake;
    if (state.slowSec > 0) target *= state.slowMult;
    target = clamp(target, KART_MIN_SPEED, KART_MAX_SPEED);
    const rising = target > state.speed;
    const k = rising ? (state.boostSec > 0 ? 6 : 2.5) : (input.brake > 0 ? 3 : state.drift ? 0.8 : 1.5);
    state.speed += (target - state.speed) * ease(k, dt);
    state.speed = clamp(state.speed, KART_MIN_SPEED, KART_MAX_SPEED);
  }

  function stepSteer(dt, input) {
    state.steer = clamp(input.steer, -1, 1);
    state.drift = !!input.drift && !state.airborne && Math.abs(state.steer) > 0.15;
    if (state.drift) driftSide = state.steer > 0 ? 1 : -1;
    // speed-dependent authority: the faster you go the wider the sweep, drift claws it back
    let auth = 0.72 + 0.28 * Math.min(1, KART_BASE_SPEED / state.speed);
    if (state.drift) auth *= DRIFT_STEER;
    if (state.airborne) auth *= 0.35;
    let vTarget = state.steer * STEER_VMAX * auth;
    // soft wall: the last WALL_SOFT metres bleed the outward push away, no bounce shock
    const edge = ROAD_HALF_W - Math.abs(state.x);
    if (edge < WALL_SOFT && Math.sign(vTarget) === Math.sign(state.x)) vTarget *= clamp(edge / WALL_SOFT, 0, 1);
    vx += (vTarget - vx) * ease(state.drift ? 9 : 6, dt);
    state.x += vx * dt;
    if (Math.abs(state.x) > ROAD_HALF_W) {
      state.x = Math.sign(state.x) * ROAD_HALF_W;
      if (Math.sign(vx) === Math.sign(state.x)) vx *= 0.25;
    }
    const leanT = -vx * 0.07 - (state.drift ? state.steer * 0.1 : 0);
    lean += (leanT - lean) * ease(8, dt);
  }

  function stepRamps(dt, lay, prevD) {
    if (!state.airborne) {
      const here = lay.rampAt ? lay.rampAt(state.d) : null;
      if (!here) lastRampD = -1;                                        // past the air line: re-arm for next lap
      let hit = null;
      const feats = lay.featuresBetween ? lay.featuresBetween(prevD, state.d) : null;
      if (feats) for (const f of feats) if (f.type === 'ramp' && Math.abs(f.d - lastRampD) > 0.01) { hit = f; break; }
      if (!hit && here && Math.abs(here.d - lastRampD) > 0.01) hit = here;
      if (hit) launch(hit);
    }
    if (state.airborne || state.h > 0) {
      state.vh -= GRAVITY * dt;
      state.h += state.vh * dt;
      if (state.h <= 0) {                                              // THUD: land, squash, spring back
        rig.squash(clamp(-state.vh / 12, 0.25, 1));
        state.h = 0; state.vh = 0; state.airborne = false;
      }
    }
    state.airborne = state.h > 0.05 || (state.airborne && state.h > 0);
    const pitchT = state.airborne ? -clamp(state.vh / 12, -1, 1) * 0.35 : 0;
    pitch += (pitchT - pitch) * ease(6, dt);
  }

  function place(lay) {
    lay.toWorld(state.d, state.x, state.h, group.position);
    const f = lay.frameAtDepth(state.d);
    _fwd.copy(f.tangent); _up.copy(f.up); _right.copy(f.right || _v.crossVectors(_up, _fwd));
    _m.lookAt(_fwd, ZERO, _up);                                        // +z faces down the road
    group.quaternion.setFromRotationMatrix(_m);
    group.quaternion.multiply(_q.setFromAxisAngle(AX_Z, lean)).multiply(_q.setFromAxisAngle(AX_X, pitch));
    group.updateMatrixWorld(true);
  }

  function stepCamera(dt, lay) {
    // the seat rides the track frame exactly CAM_BACK behind the cup; only the lateral / height
    // offsets and the up vector are smoothed, so there is no lag and nothing to snap at the wrap
    camX += (state.x - camX) * ease(6, dt);
    camH += (state.h - camH) * ease(8, dt);
    const camD = lay.wrap(state.d - CAM_BACK), lookD = lay.wrap(state.d + CAM_LOOK_AHEAD);
    lay.toWorld(camD, camX * 0.45, CAM_UP + camH * 0.4, cam.pos);
    lay.toWorld(lookD, camX * 0.3, 0.9 + camH * 0.5, cam.look);
    const cf = lay.frameAtDepth(camD);
    let rollT = 0;
    if (reducedMotion) {
      // comfort: hold the camera level and let the world turn through the Wheel
      _v.subVectors(cam.look, cam.pos).normalize();
      _lvl.copy(WORLD_UP).addScaledVector(_v, -WORLD_UP.dot(_v));
      if (_lvl.lengthSq() < 0.09) _lvl.copy(cf.up); else _lvl.normalize();
    } else {
      rollT = -camX * 0.04 + lean * 0.35;
      _lvl.copy(cf.up).applyAxisAngle(cf.tangent, rollT);
    }
    if (!camReady) { cam.up.copy(_lvl); cam.roll = rollT; camReady = true; return; }
    cam.up.lerp(_lvl, ease(reducedMotion ? 4 : 10, dt)).normalize();
    cam.roll += (rollT - cam.roll) * ease(8, dt);
  }

  function update(dt, input, lay) {
    lay = lay || layout;
    dt = clamp(+dt || 0, 0, 0.1);
    input = input || {};
    const inp = { steer: +input.steer || 0, accel: clamp(+input.accel || 0, 0, 1), brake: clamp(+input.brake || 0, 0, 1), drift: !!input.drift };
    elapsed += dt;
    stepSpeed(dt, inp);
    stepSteer(dt, inp);
    const prevD = state.d;
    let d = state.d + state.speed * dt;
    if (d >= lay.totalDepth) state.lap += 1;
    d = lay.wrap(d);
    state.d = d;
    stepRamps(dt, lay, prevD);
    place(lay);
    stepCamera(dt, lay);
    ctx.t = elapsed; ctx.speedNorm = state.speed / KART_MAX_SPEED; ctx.airborne = state.airborne;
    ctx.steerVel = vx; ctx.drift = state.drift; ctx.driftSide = driftSide;
    rig.update(dt, ctx);
  }

  function camera(out) {
    out.pos.copy(cam.pos); out.look.copy(cam.look);
    if (out.up && out.up.copy) out.up.copy(cam.up);
    out.roll = reducedMotion ? 0 : cam.roll;
    return out;
  }

  function dispose() { scene.remove(group); rig.dispose(); }

  return { state, update, applyBoost, applySlow, setMood: rig.setMood, setFraught: rig.setFraught, camera, group, dispose };
}
