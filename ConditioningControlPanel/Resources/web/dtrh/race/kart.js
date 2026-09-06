// race/kart.js - Racing Thoughts: the kart (the teacup) in track space, its placement on the road,
// and the chase camera. Implements CONTRACT.md "race/kart.js (PR 3)". The cup + EMI meshes,
// antenna moods, sweat and sparks live in race/emi.js; this file owns speed, steering, drift,
// the drift mini-turbo, ramps, the lap counter and the camera seat. There is no fail state: speed
// never drops below KART_MIN_SPEED and the road walls are soft.
//
// Events (kart.onEvent(cb)), all fired from inside update():
//   { type:'driftTier', tier }        the drift charge crossed a tier (1 blue, 2 orange, 3 purple)
//   { type:'driftBoost', tier, sec }  drift released with a charge: that many seconds of boost
//   { type:'scrub', sec }             the road edge has been scrubbed for WALL_SCRUB_SEC (speed eases off a touch)
//   { type:'trick', name, points, streak }   a ramp trick was thrown (one per launch): 'spin left' | 'spin right' | 'backflip'
//   { type:'landing', clean, trick, streak } touched down; clean = not on the kerb; a clean trick landing boosts
//   { type:'inverted', on }           roll past 120 degrees (inside THE BIG WHEEL) began / ended; state.inverted mirrors it
//   { type:'lap', lap, sec }          the Tea Garden gate crossed again: a timed lap (the first crossing only starts the clock)
//   { type:'split', frac, sec }       a quarter mark of the lap (0.25 / 0.5 / 0.75) passed at `sec` into the lap
//
// THE KERB HOLDS THE SAUCER, NOT THE CUP. Steering used to clamp the kart CENTRE to ROAD_HALF_W
// (3.2), which is not even the edge of the asphalt: the ribbon in rooms.js runs out to KERB_INNER_W
// (2.875) and the kerb face steps up from there. So at full lock the whole dish hung over the kerb
// and the kerb face cut straight through it. The clamp is KART_X_MAX now (consts.js), measured off
// the saucer's own rim, so the rim stops on the kerb line and the kart is as wide as it looks. The
// feel is unchanged: the same WALL_SOFT bleed eases the outward push away over the last stretch and
// the same vx damping catches the limit, so it is still a soft kerb and never a bounce.
//
// THE SAUCER NEVER BANKS. The steer lean used to roll the whole group about the forward axis, and
// the group sits ON the road (h = 0), so the saucer's outer edge swung sin(lean) * SAUCER_R_ROAD
// straight down through the road surface, most of a metre under it at a drift lean. The lean now
// rides on `ctx.lean` into race/emi.js, which tips the cup alone on a saucer that stays flat (a saucer slides, it does
// not bank). Pitch is still the group's, capped here at what the height above the road allows.
//
// SCREENSHOT AIDS: `?lean=1` (steer right) / `?lean=-1` pins the lean at LEAN_MAX for as long as
// the page is open, so a headless shot can be taken at full lock without holding a key. It only
// writes `lean`; speed, steering and the pop box are untouched. Its sibling `?x=-1` / `?x=1` pins
// the kart against the left / right kerb (state.x = -/+ KART_X_MAX) so a shot shows where the rim
// actually sits. Both take -1..1 and scale, and anything else is ignored.

import * as THREE from 'three';
import {
  KART_BASE_SPEED, KART_MAX_SPEED, KART_MIN_SPEED, GRAVITY, KART_X_MAX, SAUCER_R_ROAD,
  CAM_BACK, CAM_UP, CAM_LOOK_AHEAD, CAM_LOOK_SPEED, KART_SCALE,
  POP_HIT_D, POP_HIT_X, POP_HIT_H, LANE_H, DRIFT_TIER_SEC, DRIFT_BOOST_SEC, WALL_SCRUB_SEC,
} from './consts.js';
import { createEmiRig } from './emi.js';

const STEER_VMAX = 6.5;       // lateral m/s at full lock, cruise speed, no drift
const DRIFT_STEER = 1.45;     // drift tightens the steer
const DRIFT_SNAP = 1.6;       // m/s of counter-steer kick when a drift lets go (the cup straightens with a snap)
const WALL_SOFT = 0.7;        // metres from the road edge where the soft wall starts easing you back
const WALL_SCRUB_MULT = 0.9;  // speed target while scrubbing the edge past WALL_SCRUB_SEC; the floor still holds
const HOP_SEC = 0.28, HOP_H = 0.3;      // the drift-press hop: cosmetic, the pop box never leaves the road
const TRICK_SEC = 0.62;                 // one full rotation of the cup (spin about up, backflip about right)
const TRICK_POINTS = { spin: 150, flip: 250 };
const TRICK_STREAK_MAX = 4;             // consecutive clean trick landings scale trick points by 1 + 0.5 * streak
const LAND_BOOST_SEC = 0.7;             // a clean landing after a trick
const LAND_CLEAN_M = 0.45;              // metres inside KART_X_MAX that still count as clean
const SCRUB_EDGE_M = 0.12;              // metres of KART_X_MAX left: the rim is on the kerb, this is a scrub
const INVERT_DEG = 120;                 // roll past this = upside down (pops count double, score.js)
const TIER_COLORS = [0xFFD27A, 0x5BB8FF, 0xFF8A3D, 0xC46BFF];   // tier 0 (gold) is emi.js's own sparks
const TARGET_AHEAD = POP_HIT_D * 0.5;   // the pop ring floats this far in front of the cup
const TARGET_H = LANE_H - 0.15;         // ring centre: between the road box centre and a lane bubble
const RING_ALPHA = 0.14;                // resting alpha; a pop pulses it up
const CAM_BOOST_BACK = 0.7;             // the seat slides back a touch under boost
// The saucer's outer radius in road metres (SAUCER_R_ROAD, 1.08), widened by the landing squash's
// fattest frame (emi.js scales the rig 1.18 in x and z at squash 1) so the pitch cap below holds
// through a THUD. The steering clamp uses the resting rim (KART_X_MAX) rather than this one: a
// squash lasts a fifth of a second and giving it 0.19 m of road either side all run would cost the
// kerb its kiss.
const SAUCER_R_W = SAUCER_R_ROAD * 1.18;
// The steepest lean the physics below can reach: full lock at the speed floor, drifting, plus the
// drift term. Only the screenshot aid uses it, and it is derived so it cannot go stale.
const LEAN_MAX = STEER_VMAX * 1.15 * DRIFT_STEER * 0.07 + 0.1;
const forced = (key) => {                   // `?lean=-1..1` / `?x=-1..1`, see the header. No DOM, no aid.
  try { const v = +new URLSearchParams(location.search).get(key) || 0; return Math.max(-1, Math.min(1, v)); }
  catch (e) { return 0; }                   // node, or a page with no location: the aid is simply off
};
const FORCE_LEAN = forced('lean'), FORCE_X = forced('x');
const WORLD_UP = new THREE.Vector3(0, 1, 0), ZERO = new THREE.Vector3();
const _m = new THREE.Matrix4(), _q = new THREE.Quaternion(), _v = new THREE.Vector3();
const _fwd = new THREE.Vector3(), _up = new THREE.Vector3(), _right = new THREE.Vector3(), _lvl = new THREE.Vector3();
const AX_X = new THREE.Vector3(1, 0, 0), AX_Y = new THREE.Vector3(0, 1, 0);   // z (the roll) is emi.js's

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const ease = (k, dt) => 1 - Math.exp(-k * dt);
const smooth = (u) => u <= 0 ? 0 : u >= 1 ? 1 : u * u * (3 - 2 * u);
const tierFor = (sec) => { let t = 0; for (const at of DRIFT_TIER_SEC) if (sec >= at) t++; return t; };

/** Tier sparks: a small world-space point pool that takes the tier colour (emi.js keeps the gold tier 0). */
function makeTierSparks(scene, n) {
  const pos = new Float32Array(n * 3), vel = new Float32Array(n * 3), life = new Float32Array(n);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const mat = new THREE.PointsMaterial({ color: TIER_COLORS[1], size: 0.2, sizeAttenuation: true, transparent: true, opacity: 0.95, depthWrite: false });
  const pts = new THREE.Points(geo, mat); pts.frustumCulled = false; pts.visible = false;
  scene.add(pts);
  let head = 0;
  return {
    setTier(t) { mat.color.setHex(TIER_COLORS[clamp(t | 0, 0, 3)]); },
    spawn(p, v, ttl) { const i = head; head = (head + 1) % n; pos.set([p.x, p.y, p.z], i * 3); vel.set([v.x, v.y, v.z], i * 3); life[i] = ttl; },
    update(dt, g) {
      let alive = 0;
      for (let i = 0; i < n; i++) {
        if (life[i] <= 0) continue;
        life[i] -= dt; alive++;
        vel[i * 3] += g.x * dt; vel[i * 3 + 1] += g.y * dt; vel[i * 3 + 2] += g.z * dt;
        pos[i * 3] += vel[i * 3] * dt; pos[i * 3 + 1] += vel[i * 3 + 1] * dt; pos[i * 3 + 2] += vel[i * 3 + 2] * dt;
        if (life[i] <= 0) pos[i * 3 + 1] = -1e4;
      }
      pts.visible = alive > 0; geo.attributes.position.needsUpdate = true;
    },
    dispose() { scene.remove(pts); geo.dispose(); mat.dispose(); },
  };
}

/**
 * createKart({ scene, layout, reducedMotion, pixel }) -> kart
 * kart.update(dt, input, layout)  input = { steer:-1..1, accel:0..1, brake:0..1, drift:bool }
 * kart.camera(out)                out = { pos: Vector3, look: Vector3, up?: Vector3, roll: number }
 * kart.emiModel()                 the mounted EMI glb root, or null while it is still loading
 * kart.emiReady(cb)               cb(root) once she is mounted (immediately if she already is)
 * kart.setFace(i)                 atlas frame 0..4 on the glass (menus and results; unseen in race)
 * kart.pose(name, opts)           the pose layer (race/emiPoses.js)
 * `pixel` is race/pixel.js, passed down so the glb's textures join the pixel pass on arrival.
 */
export function createKart({ scene, layout, reducedMotion = false, pixel = null }) {
  const state = {
    d: 0, x: 0, h: 0, vh: 0, speed: KART_BASE_SPEED, steer: 0, drift: false, airborne: false,
    boostSec: 0, slowMult: 1, slowSec: 0, lap: 0,
    driftSec: 0, driftTier: 0, scrub: false,
    inverted: false, roll: 0, trick: null, trickStreak: 0,
    lapSec: 0, lapsTimed: 0,
  };
  const rig = createEmiRig({ scene, reducedMotion, pixel });
  const group = rig.group;
  group.scale.setScalar(KART_SCALE);
  scene.add(group);
  const tierSparks = reducedMotion ? null : makeTierSparks(scene, 48);

  // the pop target: a faint ring at the pop box, just ahead of the cup, that pulses on a pop.
  // Its own object (not a child of the scaled rig) so it stays in honest pop-box metres.
  const ringMat = new THREE.MeshBasicMaterial({ color: 0xff9bd0, transparent: true, opacity: RING_ALPHA,
    blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide });
  const ring = new THREE.Mesh(new THREE.RingGeometry(0.88, 1, 48), ringMat);
  ring.frustumCulled = false; ring.renderOrder = 2;
  scene.add(ring);
  let pulse = 0, reach = 1;

  let vx = 0, lean = 0, pitch = 0, elapsed = 0, lastRampD = -1, driftSide = 1, camReady = false, camX = 0, camH = 0;
  let camBoost = 0, steerS = 0, hopT = 0, scrubSec = 0, sparkAcc = 0, driftWas = false;
  let airWas = false, steerWas = 0, trickArmed = false, trickKind = null, trickDir = 1, trickT = 1;
  let gateLay = null, gateD = 0, lapTimed = false, lapMark = 0;
  const cam = { pos: new THREE.Vector3(), look: new THREE.Vector3(), up: new THREE.Vector3(0, 1, 0), roll: 0 };
  const ctx = { t: 0, up: _up, right: _right, tangent: _fwd, speedNorm: 0, airborne: false, steerVel: 0, drift: false, driftSide: 1, driftTier: 0, lean: 0 };
  const listeners = [];
  const emit = (ev) => { for (const cb of listeners) { try { cb(ev); } catch (e) { /* a listener never breaks the kart */ } } };

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
    if (state.scrub) target *= WALL_SCRUB_MULT;                         // scrubbing the kerb costs a touch, never a stop
    target = clamp(target, KART_MIN_SPEED, KART_MAX_SPEED);
    const rising = target > state.speed;
    const k = rising ? (state.boostSec > 0 ? 6 : 2.5) : (input.brake > 0 ? 3 : state.drift ? 0.8 : 1.5);
    state.speed += (target - state.speed) * ease(k, dt);
    state.speed = clamp(state.speed, KART_MIN_SPEED, KART_MAX_SPEED);
  }

  /** Drift let go: the charge becomes boost by tier, and the cup straightens with a counter-steer snap. */
  function releaseDrift() {
    const tier = state.driftTier;
    state.driftSec = 0; state.driftTier = 0; ctx.driftTier = 0;
    if (state.airborne) return;                                        // a ramp mid-drift: the charge just fizzles
    vx -= driftSide * DRIFT_SNAP;
    lean += driftSide * 0.08;
    if (tier > 0) {
      const sec = DRIFT_BOOST_SEC[tier] || 0;
      applyBoost(sec);
      emit({ type: 'driftBoost', tier, sec });
    }
  }

  function stepSteer(dt, input) {
    state.steer = clamp(input.steer, -1, 1);
    const wasDrift = state.drift;
    state.drift = !!input.drift && !state.airborne && Math.abs(state.steer) > 0.15;
    // the hop: a fresh press of drift on the road bounces the cup (cosmetic; state.h stays put)
    if (input.drift && !driftWas && !state.airborne && hopT <= 0) hopT = HOP_SEC;
    driftWas = !!input.drift;
    if (state.drift) {
      driftSide = state.steer > 0 ? 1 : -1;
      state.driftSec += dt;
      const tier = tierFor(state.driftSec);
      if (tier > state.driftTier) { state.driftTier = tier; ctx.driftTier = tier; if (tierSparks) tierSparks.setTier(tier); emit({ type: 'driftTier', tier }); }
    } else if (wasDrift) releaseDrift();
    // speed-dependent authority: nimble at the floor, planted under boost; drift claws it back
    const sn = clamp((state.speed - KART_MIN_SPEED) / (KART_MAX_SPEED - KART_MIN_SPEED), 0, 1);
    let auth = 1.15 - 0.4 * smooth(sn);
    if (state.drift) auth *= DRIFT_STEER;
    if (state.airborne) auth *= 0.35;
    let vTarget = state.steer * STEER_VMAX * auth;
    // soft wall: the last WALL_SOFT metres bleed the outward push away, no bounce shock. `edge` is
    // road left under the saucer's RIM now, not under the cup, so the bleed starts where the dish
    // starts running out of asphalt.
    const edge = KART_X_MAX - Math.abs(state.x);
    const outward = state.steer !== 0 && Math.sign(state.steer) === Math.sign(state.x);
    if (edge < WALL_SOFT && outward) vTarget *= clamp(edge / WALL_SOFT, 0, 1);
    vx += (vTarget - vx) * ease(state.drift ? 9 : 6, dt);
    state.x += vx * dt;
    if (Math.abs(state.x) > KART_X_MAX) {
      state.x = Math.sign(state.x) * KART_X_MAX;
      if (Math.sign(vx) === Math.sign(state.x)) vx *= 0.25;
    }
    if (FORCE_X) { state.x = FORCE_X * KART_X_MAX; vx = 0; }           // screenshot aid, see the header
    // the kerb: leaning on the edge for WALL_SCRUB_SEC eases the speed target off a little (stepSpeed)
    const scrubbing = edge < SCRUB_EDGE_M && outward && Math.abs(state.steer) > 0.3 && !state.airborne;
    scrubSec = scrubbing ? scrubSec + dt : 0;
    const scrub = scrubSec >= WALL_SCRUB_SEC;
    if (scrub && !state.scrub) emit({ type: 'scrub', sec: scrubSec });
    state.scrub = scrub;
    const leanT = -vx * 0.07 - (state.drift ? state.steer * 0.1 : 0);
    lean += (leanT - lean) * ease(8, dt);
    if (FORCE_LEAN) lean = -FORCE_LEAN * LEAN_MAX;      // screenshot aid, see the header
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

  function stepHop(dt) {
    if (hopT <= 0) return 0;
    hopT -= dt;
    if (hopT <= 0) { hopT = 0; rig.squash(0.3); return 0; }
    return HOP_H * Math.sin(Math.PI * (1 - hopT / HOP_SEC));
  }

  /** Airborne input: a fresh drift press backflips, a fresh steer past half lock spins. One per launch. */
  function stepTricks(dt, inp, driftPress) {
    if (trickT < 1) trickT = Math.min(1, trickT + dt / TRICK_SEC);
    if (state.airborne && !airWas) { trickArmed = true; state.trick = null; }
    if (state.airborne && trickArmed) {
      let kind = null, dir = 1;
      if (driftPress) kind = 'flip';
      else if (Math.abs(inp.steer) > 0.5 && Math.abs(steerWas) <= 0.5) { kind = 'spin'; dir = inp.steer > 0 ? 1 : -1; }
      if (kind) {
        trickArmed = false; trickKind = kind; trickDir = dir; trickT = 0;
        state.trick = kind === 'flip' ? 'backflip' : dir > 0 ? 'spin right' : 'spin left';
        const points = Math.round(TRICK_POINTS[kind] * (1 + 0.5 * Math.min(state.trickStreak, TRICK_STREAK_MAX)));
        emit({ type: 'trick', name: state.trick, points, streak: state.trickStreak });
      }
    }
    if (!state.airborne && airWas) {
      const clean = Math.abs(state.x) < KART_X_MAX - LAND_CLEAN_M, trick = state.trick;
      if (trick && clean) { applyBoost(LAND_BOOST_SEC); state.trickStreak = Math.min(state.trickStreak + 1, TRICK_STREAK_MAX + 1); }
      else state.trickStreak = 0;
      emit({ type: 'landing', clean, trick, streak: state.trickStreak });
      state.trick = null; trickArmed = false;
    }
    airWas = state.airborne; steerWas = inp.steer;
  }

  /** The lap clock runs gate to gate (the Tea Garden gate in chunk 0; d = 0 when a layout has none). */
  function gateFor(lay, atD) {
    if (lay !== gateLay) {
      const c = lay.chunks && lay.chunks[0], g = c && c.features && c.features.find((f) => f.type === 'gate');
      gateLay = lay; gateD = g ? g.d : 0;
      if (Math.abs(atD - gateD) < 1e-6) { lapTimed = true; state.lapSec = 0; lapMark = 0; }   // spawned on the gate: the clock runs from here
    }
    return gateD;
  }
  function stepLaps(dt, lay, prevD) {
    const g = gateFor(lay, prevD);
    if (lapTimed) state.lapSec += dt;
    const crossed = prevD <= state.d ? (g > prevD && g <= state.d) : (g > prevD || g <= state.d);
    if (crossed) {
      if (lapTimed) { state.lapsTimed++; emit({ type: 'lap', lap: state.lapsTimed, sec: state.lapSec }); }
      lapTimed = true; state.lapSec = 0; lapMark = 0;
      return;
    }
    if (!lapTimed) return;
    const prog = lay.wrap(state.d - g) / lay.totalDepth;
    for (const f of [0.25, 0.5, 0.75]) if (prog >= f && lapMark < f) { lapMark = f; emit({ type: 'split', frac: f, sec: state.lapSec }); }
  }

  function stepTierSparks(dt) {
    if (!tierSparks) return;
    const on = state.drift && state.driftTier > 0 && !state.airborne;
    sparkAcc = on ? sparkAcc + dt * (30 + 20 * state.driftTier) : 0;
    while (sparkAcc >= 1) {
      sparkAcc -= 1;
      _v.copy(group.position).addScaledVector(_right, -driftSide * 0.85 * KART_SCALE).addScaledVector(_fwd, -0.4).addScaledVector(_up, 0.05);
      const vel = _lvl.copy(_up).multiplyScalar(1 + Math.random() * 2.5).addScaledVector(_right, -driftSide * (0.5 + Math.random() * 2)).addScaledVector(_fwd, -(3 + Math.random() * 4));
      tierSparks.spawn(_v, vel, 0.28 + Math.random() * 0.18);
    }
    tierSparks.update(dt, _lvl.copy(_up).multiplyScalar(-12));
  }

  function place(lay, hopH) {
    lay.toWorld(state.d, state.x, state.h, group.position);
    const f = lay.frameAtDepth(state.d);
    _fwd.copy(f.tangent); _up.copy(f.up); _right.copy(f.right || _v.crossVectors(_up, _fwd));
    if (hopH > 0) group.position.addScaledVector(_up, hopH);
    _m.lookAt(_fwd, ZERO, _up);                                        // +z faces down the road
    group.quaternion.setFromRotationMatrix(_m);
    ring.quaternion.copy(group.quaternion);                            // the ring never leans
    // The lean is emi.js's now (the cup tips, the saucer stays flat). Pitch is still the whole
    // kart's, but only as far as the air under the saucer allows: on the road the nose-up left
    // over from a landing would otherwise drive the saucer's back edge sin(pitch) * SAUCER_R_W
    // under the surface, so it is capped at asin(clearance / SAUCER_R_W) and touches down level.
    const clear = Math.asin(clamp((state.h + Math.max(0, hopH)) / SAUCER_R_W, 0, 1));
    group.quaternion.multiply(_q.setFromAxisAngle(AX_X, clamp(pitch, -clear, clear)));
    if (trickT < 1) {                                                  // the trick: one full turn, eased
      const ang = Math.PI * 2 * smooth(trickT);
      group.quaternion.multiply(trickKind === 'flip' ? _q.setFromAxisAngle(AX_X, -ang) : _q.setFromAxisAngle(AX_Y, ang * trickDir));
    }
    group.updateMatrixWorld(true);
    // upside down: how far the road's up has rolled from the world's (THE BIG WHEEL)
    state.roll = Math.acos(clamp(_up.dot(WORLD_UP), -1, 1)) * 180 / Math.PI;
    const inv = state.roll > INVERT_DEG;
    if (inv !== state.inverted) { state.inverted = inv; emit({ type: 'inverted', on: inv }); }
    lay.toWorld(lay.wrap(state.d + TARGET_AHEAD), state.x, state.h + TARGET_H, ring.position);
    const swell = 1 + 0.22 * pulse;
    ring.scale.set(POP_HIT_X * reach * swell, POP_HIT_H * reach * swell, 1);
    ringMat.opacity = RING_ALPHA + 0.5 * pulse;
  }

  function stepCamera(dt, lay) {
    // the seat rides the track frame CAM_BACK behind the cup (a touch further under boost); only
    // the lateral / height offsets and the up vector are smoothed, so there is no lag and nothing
    // to snap at the wrap. Look-ahead grows with speed; the look point leans into the turn.
    camX += (state.x - camX) * ease(6, dt);
    camH += (state.h - camH) * ease(8, dt);
    steerS += (state.steer - steerS) * ease(5, dt);
    const boostT = state.boostSec > 0 ? 1 : 0;
    camBoost += (boostT - camBoost) * ease(boostT ? 5 : 1.5, dt);
    const spd = clamp((state.speed - KART_BASE_SPEED) / (KART_MAX_SPEED - KART_BASE_SPEED), 0, 1);
    const back = CAM_BACK + CAM_BOOST_BACK * camBoost;
    const lookAhead = CAM_LOOK_AHEAD + CAM_LOOK_SPEED * Math.max(spd, camBoost * 0.6);
    const turnLook = reducedMotion ? 0 : steerS * 0.7;
    const camD = lay.wrap(state.d - back), lookD = lay.wrap(state.d + lookAhead);
    lay.toWorld(camD, camX * 0.45, CAM_UP + camH * 0.4, cam.pos);
    lay.toWorld(lookD, camX * 0.3 + turnLook, LANE_H + camH * 0.5, cam.look);
    const cf = lay.frameAtDepth(camD);
    let rollT = 0;
    if (reducedMotion) {
      // comfort: hold the camera level and let the world turn through the Wheel
      _v.subVectors(cam.look, cam.pos).normalize();
      _lvl.copy(WORLD_UP).addScaledVector(_v, -WORLD_UP.dot(_v));
      if (_lvl.lengthSq() < 0.09) _lvl.copy(cf.up); else _lvl.normalize();
    } else {
      // a small lean into the steer on top of the lateral-offset roll and the cup's own lean
      rollT = -camX * 0.04 + lean * 0.35 - steerS * 0.045 * (state.drift ? 1.5 : 1);
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
    if (pulse > 0) pulse = Math.max(0, pulse - pulse * ease(7, dt) - 0.2 * dt);
    const driftPress = inp.drift && !driftWas;
    stepSpeed(dt, inp);
    stepSteer(dt, inp);
    const prevD = state.d;
    let d = state.d + state.speed * dt;
    if (d >= lay.totalDepth) state.lap += 1;
    d = lay.wrap(d);
    state.d = d;
    stepRamps(dt, lay, prevD);
    stepTricks(dt, inp, driftPress);
    stepLaps(dt, lay, prevD);
    place(lay, stepHop(dt));
    stepCamera(dt, lay);
    stepTierSparks(dt);
    ctx.t = elapsed; ctx.speedNorm = state.speed / KART_MAX_SPEED; ctx.airborne = state.airborne;
    ctx.steerVel = vx; ctx.drift = state.drift; ctx.driftSide = driftSide; ctx.lean = lean;
    rig.update(dt, ctx);
  }

  function camera(out) {
    out.pos.copy(cam.pos); out.look.copy(cam.look);
    if (out.up && out.up.copy) out.up.copy(cam.up);
    out.roll = reducedMotion ? 0 : cam.roll;
    return out;
  }

  /** A pop landed: the target ring swells and brightens, then settles (run.js onPop). */
  function pulseTarget() { pulse = 1; }
  /** Magnet: the ring grows with the field's reach so the wider pop box is visible. */
  function setReach(mult) { reach = clamp(+mult || 1, 0.5, 3); }
  function onEvent(cb) { if (typeof cb === 'function') listeners.push(cb); return () => { const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); }; }

  function dispose() {
    scene.remove(group); scene.remove(ring);
    ring.geometry.dispose(); ringMat.dispose();
    if (tierSparks) tierSparks.dispose();
    rig.dispose();
    listeners.length = 0;
  }

  return { state, update, applyBoost, applySlow, setMood: rig.setMood, setFraught: rig.setFraught, camera, group,
    pulseTarget, setReach, onEvent, dispose,
    emiModel: () => rig.model(), emiReady: (cb) => rig.onReady(cb),
    setFace: (i) => rig.setFace(i), pose: (name, opts) => rig.pose(name, opts) };
}

// self-check: node --check is the bar; the stub-layout drift/ramp/lap tests live in the pass-three
// harness (straight + circular layouts against the vendored three) and are not committed.
