/* ============================================================================
 * race/intro.js - the intro sequence and the results camera of Racing Thoughts.
 *
 *   createIntro({ stage, hud, audio, reducedMotion, log }) ->
 *     { play(): Promise<void>, skip(), update(dt), render(), dispose() }
 *   cameraWhip(sec)                  -> a run.js camera override: front seat to chase seat
 *   resultsCamera({ tier, reducedMotion }) -> a camera override that swings to her face and holds
 *   preRollCamera()                  -> the chase seat before the first step (the "again" countdown)
 *   resultTier(total, best, pb)      -> the face index the End card wears
 *
 * Six to eight seconds on the menu stage, drawn through the same renderer and
 * pixelizer (the boot hands this object to race.setStage while it plays):
 *   A  0.0..2.42 she waddles from the podium to the cup: two hops while a
 *                spring carries her, face ^_^
 *   B  2.42..3.5 THE LEAP. 0.18 s of squat with >_<, then a 0.55 s parabola
 *                to the rim (the apex a metre over it) with a forward tumble
 *                and o_o, then the landing: the cup body squashes deep and
 *                stretches back, the tea rings out and throws eight beads
 *                over the rim, the camera dips 8 cm, >_< then ^_^ 0.35 s on
 *   C  3.5..5.4  the cup rolls to the start gantry; the camera settles into a
 *                front seat (props.glb `gantry`, else two pink posts + a cream bar)
 *   D  5.4..     3 2 1 on the HUD with o_o, :3 on GO; play() resolves on GO
 * Then the boot starts the run and installs cameraWhip: 0.8 s from the run's
 * own front seat to the chase seat while the run's kart takes over.
 *
 * Springs, never linear tweens (Law XI: physics reads as care). Any key,
 * pointer or pad press skips to the line (Law VI: the exits are sacred).
 * Reduced motion cuts to the line and only counts down. One screen effect at
 * a time: the countdown is the only HUD element during the intro.
 * ==========================================================================*/

import * as THREE from 'three';
import { loadPack } from './gltf.js';
import { PODIUM_H, CUP_SCALE, PROPS_URL } from './menu.js';
import { CAM_BACK, CAM_UP, CAM_LOOK_AHEAD, LANE_H } from './consts.js';

const GANTRY_Z = -3.6, RIM_H = 0.75 * CUP_SCALE, PINK = 0xff69b4, CREAM = 0xf6e7c8;
// The waddle ends at the podium's near edge, on the plate and facing the cup: the plate runs to
// r 0.98 and her shoes are 0.22 wide, so 0.7 keeps both soles on porcelain. She hops the gap from
// there; walking any further would put her over the rim with nothing under her feet.
const STAND_R = 0.7;
const FACE = { happy: 0, cat: 1, squint: 2, wide: 3, money: 4 };
const T_CROUCH = 2.42, T_HOP_IN = 2.6, T_ARC = 0.55, T_SINK = T_HOP_IN + T_ARC, T_ROLL = 3.5, T_COUNT = 5.4;
// the leap: how far over the rim the apex goes, how far she tips into it, where she comes down.
const APEX_UP = 0.72, TUMBLE = -25 * Math.PI / 180, RIM_CLEAR = 0.16;
// the tea that jumps the rim on the landing: eight cream beads, ballistic, gone in half a second.
const DROPS = 8, DROP_SEC = 0.5, DROP_G = 9.5, CAM_DIP = 0.08;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** The longest step a spring integrates in one go. run.js clamps a frame at 0.1 s, and explicit Euler
 *  on a stiff spring (w 22, w 26) diverges long before that, so a slow frame is walked in pieces. */
const SUB_H = 0.02, SUB_MAX = 8;
const subSteps = (dt) => (dt > SUB_H ? Math.min(SUB_MAX, Math.ceil(dt / SUB_H)) : 1);

/** Damped spring on a scalar: `w` rad/s, `zeta` < 1 overshoots (the stretch), 1 settles clean. */
export class Spring {
  constructor(x = 0, w = 10, zeta = 0.9) { this.x = x; this.v = 0; this.target = x; this.w = w; this.zeta = zeta; }
  to(t) { this.target = t; return this; }
  set(x) { this.x = x; this.target = x; this.v = 0; return this; }
  step(dt) {
    const n = subSteps(dt), h = dt / n;
    for (let i = 0; i < n; i++) { this.v += (this.w * this.w * (this.target - this.x) - 2 * this.zeta * this.w * this.v) * h; this.x += this.v * h; }
    return this.x;
  }
  get done() { return Math.abs(this.target - this.x) < 0.002 && Math.abs(this.v) < 0.02; }
}
/** The same spring on a Vector3, written in place on `x`. */
export class Spring3 {
  constructor(x = new THREE.Vector3(), w = 10, zeta = 0.9) { this.x = x; this.v = new THREE.Vector3(); this.target = x.clone(); this.w = w; this.zeta = zeta; this._a = new THREE.Vector3(); }
  to(t) { this.target.copy(t); return this; }
  set(p) { this.x.copy(p); this.target.copy(p); this.v.set(0, 0, 0); return this; }
  step(dt) {
    const n = subSteps(dt), h = dt / n;
    for (let i = 0; i < n; i++) {
      this._a.subVectors(this.target, this.x).multiplyScalar(this.w * this.w).addScaledVector(this.v, -2 * this.zeta * this.w);
      this.v.addScaledVector(this._a, h); this.x.addScaledVector(this.v, h);
    }
    return this.x;
  }
}

export function createIntro({ stage, hud, audio = null, reducedMotion = false, log = null } = {}) {
  const scene = stage.scene, camera = stage.camera, emi = stage.emi, cup = stage.cup.group, body = stage.cup.body;
  const own = [];
  const keep = (x) => { own.push(x); return x; };
  const gantry = new THREE.Group(); gantry.position.set(0, 0, GANTRY_Z); scene.add(gantry);
  const post = keep(new THREE.CylinderGeometry(0.07, 0.09, 2.3, 10)), postMat = keep(new THREE.MeshStandardMaterial({ color: PINK, emissive: PINK, emissiveIntensity: 0.35, roughness: 0.5 }));
  for (const sx of [-1.7, 1.7]) { const p = new THREE.Mesh(post, postMat); p.position.set(sx, 1.15, 0); gantry.add(p); }
  const bar = new THREE.Mesh(keep(new THREE.BoxGeometry(3.6, 0.16, 0.16)), keep(new THREE.MeshStandardMaterial({ color: CREAM, roughness: 0.4 }))); bar.position.y = 2.3; gantry.add(bar);
  loadPack(PROPS_URL, { log }).then((pack) => { const g = pack.clone('gantry'); if (g && !disposed) { gantry.clear(); gantry.add(g); } }).catch(() => { /* placeholders stand */ });

  const cupHome = cup.position.clone(), lineAt = new THREE.Vector3(0, 0, GANTRY_Z + 1.0);
  const walk = new Spring3(emi.root.position, 6, 0.95), ride = new Spring3(cup.position, 5, 0.9);
  const camPos = new Spring3(camera.position, 3, 1), camLook = new Spring3(new THREE.Vector3(0, 0.6, 0), 3, 1);   // gentle for the waddle, 5.5 for the roll
  const squash = new Spring(1, 22, 0.28);            // the cup body: deeper now, and it rings back harder
  const crouch = new Spring(1, 26, 0.42);            // her root: the squat, then the stretch off the floor
  const dip = new Spring(0, 24, 0.9);                // the camera nods 8 cm on the landing and comes back
  let t = 0, phase = -1, inCup = false, hops = 0, disposed = false, resolveGo = null, counting = false, atLine = false;
  let faceWant = -1, dipOn = 0, dropT = -1, arcMid = 0, liftWant = 0;
  const _v = new THREE.Vector3(), _m = new THREE.Vector3(), _w = new THREE.Vector3();
  const arcFrom = new THREE.Vector3(), arcTo = new THREE.Vector3(), base = new THREE.Vector3(1, 1, 1);

  // ---- the splash: eight beads on one instanced mesh, thrown off the rim and integrated under gravity ----
  const dropGeo = keep(new THREE.SphereGeometry(0.038, 6, 4));
  const drops = new THREE.InstancedMesh(dropGeo, keep(new THREE.MeshStandardMaterial({ color: CREAM, roughness: 0.25, emissive: 0x3a1428 })), DROPS);
  drops.frustumCulled = false; drops.visible = false; scene.add(drops);
  const dropP = [], dropV = [], _mx = new THREE.Matrix4(), _sc = new THREE.Vector3();
  for (let i = 0; i < DROPS; i++) { dropP.push(new THREE.Vector3()); dropV.push(new THREE.Vector3()); }
  function splash() {
    cup.getWorldPosition(_w);
    dropT = 0; drops.visible = true;
    for (let i = 0; i < DROPS; i++) {
      const a = (i / DROPS) * Math.PI * 2 + 0.4, out = 1.05 + (i % 3) * 0.3;
      dropP[i].set(_w.x + Math.cos(a) * 0.44 * CUP_SCALE, _w.y + 0.68 * CUP_SCALE, _w.z + Math.sin(a) * 0.44 * CUP_SCALE);
      dropV[i].set(Math.cos(a) * out, 2.4 + (i % 4) * 0.4, Math.sin(a) * out);
    }
  }
  function stepDrops(dt) {
    if (dropT < 0) return;
    dropT += dt;
    if (dropT >= DROP_SEC) { dropT = -1; drops.visible = false; return; }
    const k = 1 - dropT / DROP_SEC;
    for (let i = 0; i < DROPS; i++) {
      dropV[i].y -= DROP_G * dt; dropP[i].addScaledVector(dropV[i], dt);
      _mx.makeTranslation(dropP[i].x, dropP[i].y, dropP[i].z);
      drops.setMatrixAt(i, _mx.scale(_sc.setScalar(0.45 + k * 0.55)));
    }
    drops.instanceMatrix.needsUpdate = true;
  }

  function sinkIn() {
    if (inCup) return;
    inCup = true;
    cup.attach(emi.root);   // she rides with the cup from here on
    walk.set(emi.root.position).to(_v.set(0, 0.42, 0));   // cup-local: she sinks in past the rim (tea at 0.66), her face still clears it
    squash.set(0.72).to(1); // deeper in, and the stretch is the overshoot
    stage.cup.ripple();
    faceWant = FACE.squint; emi.face(FACE.squint);
  }
  /** The front seat on the cup (the roll and the line). */
  function seat(p) { camPos.to(_v.set(p.x, p.y + 1.35, p.z + 3.7)); camLook.to(_v.set(p.x, p.y + 0.95, p.z)); }
  /** The wide seat for the waddle and the leap: the menu camera, drifting with the midpoint of her and the
   *  cup. `lift` is how far off the floor she is, so the seat tips up with the jump and she stays in frame. */
  function wide(p, lift = 0) { camPos.to(_v.set(p.x * 0.5, 1.05 + lift * 0.6, 4.4)); camLook.to(_v.set(p.x * 0.5, 0.6 + lift * 1.15, 0)); }
  function toLine() {
    if (atLine) return;
    atLine = true;
    emi.root.rotation.x = 0; emi.root.scale.set(1, 1, 1);
    sinkIn();
    base.copy(emi.root.scale); crouch.set(1);
    dip.set(0); dipOn = 0; dropT = -1; drops.visible = false; liftWant = 0;
    walk.set(_v.set(0, 0.55, 0));
    ride.set(lineAt); squash.set(1); body.rotation.z = 0; body.scale.setScalar(1);
    seat(lineAt); camPos.set(camPos.target); camLook.set(camLook.target);
    camera.lookAt(camLook.x);
    faceWant = -1; emi.face(FACE.happy);
    count();
  }
  function count() {
    if (counting || disposed) return;
    counting = true;
    emi.face(FACE.wide);
    const onTick = (s) => {
      if (audio) { try { audio.sfx(s === 'go' ? 'streak_milestone' : 'ui_click', s === 'go' ? 0.7 : 0.5); } catch (e) { /* muted */ } }
      if (s === 'go') { emi.face(FACE.cat); if (resolveGo) { const r = resolveGo; resolveGo = null; r(); } }
    };
    if (hud && typeof hud.countdown === 'function') hud.countdown({ onTick }); else onTick('go');
  }
  function skip() { if (!disposed) toLine(); }
  const onKey = (e) => { if (!e.repeat) skip(); };
  const onPointer = () => skip();
  function pollPad() {
    const list = navigator.getGamepads ? navigator.getGamepads() : null;
    if (!list) return;
    for (const p of list) if (p && p.connected && p.buttons.some((b) => b && b.pressed)) { skip(); return; }
  }

  function play() {
    stage.setMode('intro');
    stage.setViewFraction(0.5);
    window.addEventListener('keydown', onKey); window.addEventListener('pointerdown', onPointer);
    const p = new Promise((res) => { resolveGo = res; });
    if (reducedMotion) { toLine(); return p; }
    phase = 0; t = 0;
    _m.set(cupHome.x, 0, cupHome.z).setLength(STAND_R);
    walk.to(_v.set(_m.x, PODIUM_H, _m.z));
    faceWant = FACE.happy; emi.face(FACE.happy);
    base.copy(emi.root.scale);
    if (emi.play('hop', { timeScale: 1.25 })) hops = 1;
    return p;
  }
  function update(dt) {
    stage.update(dt);
    if (disposed || phase < 0) return;
    pollPad();
    if (atLine) { camPos.step(dt); camLook.step(dt); camera.lookAt(camLook.x); return; }
    t += dt;
    if (phase === 0 && t >= 1.28 && hops === 1) { hops = 2; emi.play('hop', { timeScale: 1.25 }); }
    if (phase === 0 && t >= T_CROUCH) { phase = 1; crouch.to(0.85); faceWant = FACE.squint; }   // load the legs
    if (phase === 1 && t >= T_HOP_IN) {                                  // the leap. Ballistics, never a spring to a point
      phase = 2; walk.w = 14;
      arcFrom.copy(emi.root.position);
      arcTo.set(cupHome.x, cupHome.y + RIM_H + RIM_CLEAR, cupHome.z);
      arcMid = cupHome.y + RIM_H + APEX_UP - (arcFrom.y + arcTo.y) * 0.5;
      liftWant = cupHome.y + RIM_H + APEX_UP - PODIUM_H;   // the seat starts tipping up now, so the apex lands in frame
      crouch.set(0.82).to(1); faceWant = FACE.wide; camPos.w = camLook.w = 4.5;
    }
    if (phase === 2) {
      const a = clamp((t - T_HOP_IN) / T_ARC, 0, 1);
      emi.root.position.set(arcFrom.x + (arcTo.x - arcFrom.x) * a,
        arcFrom.y + (arcTo.y - arcFrom.y) * a + 4 * arcMid * a * (1 - a),
        arcFrom.z + (arcTo.z - arcFrom.z) * a);
      emi.root.rotation.x = TUMBLE * Math.sin(Math.PI * a);             // tips into the jump, level again at the rim
      walk.set(emi.root.position);                                      // the spring rides along, so the landing has no jump
    }
    if (phase === 2 && t >= T_SINK) {
      phase = 3;
      emi.root.rotation.x = 0; emi.root.scale.set(1, 1, 1);
      sinkIn(); splash();
      base.copy(emi.root.scale); crouch.set(0.88).to(1); dip.set(-CAM_DIP).to(0);
      liftWant = 0; camPos.w = camLook.w = 5;   // the seat drops back onto the cup as she does
    }
    if (phase === 3 && t >= T_ROLL) { phase = 4; ride.to(lineAt); faceWant = -1; emi.face(FACE.happy); camPos.w = camLook.w = 5.5; }
    if (phase === 4 && t >= T_COUNT) { phase = 5; count(); }
    if (faceWant >= 0 && phase < 4) emi.face(faceWant);
    walk.step(dt); ride.step(dt); stepDrops(dt);
    const c = crouch.step(dt);
    emi.root.scale.set(base.x / Math.sqrt(c), base.y * c, base.z / Math.sqrt(c));
    body.scale.set(1 / Math.sqrt(squash.step(dt)), squash.x, 1 / Math.sqrt(squash.x));
    if (phase >= 4) { const rolling = clamp(ride.v.length() * 0.6, 0, 1); body.rotation.z = Math.sin(t * 11) * 0.08 * rolling; }
    if (phase >= 4) seat(cup.position);
    else wide(_m.copy(cupHome).lerp(emi.root.getWorldPosition(_v), 0.5), liftWant);
    camera.position.y -= dipOn;                                         // the dip lives outside the spring's own state
    camPos.step(dt); camLook.step(dt);
    dipOn = dip.step(dt); camera.position.y += dipOn;
    camera.lookAt(camLook.x);
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    window.removeEventListener('keydown', onKey); window.removeEventListener('pointerdown', onPointer);
    scene.remove(gantry); scene.remove(drops); drops.dispose();
    for (const x of own) x.dispose();
  }
  return { play, skip, update, render: stage.render, dispose, get atLine() { return atLine; }, get time() { return t; } };
}

// ---- run-side cameras: run.js overrides, fn(camera, dt, w, camOut), `false` when done ----
const _p = new THREE.Vector3(), _l = new THREE.Vector3(), _sr = new THREE.Vector3();
function frontSeat(w, back, up, side, pos, look) {
  const k = w.kart, f = w.layout.frameAtDepth(k.state.d);
  pos.copy(k.group.position).addScaledVector(f.tangent, back).addScaledVector(f.up, up);
  look.copy(k.group.position).addScaledVector(f.up, up * 0.68);
  if (side) { _sr.subVectors(look, pos).cross(f.up).normalize(); look.addScaledVector(_sr, side); }
}
/** 0.8 s from the run's own front seat to the chase seat the kart computes; the run keeps driving under it. */
export function cameraWhip(sec = 0.8) {
  const s = new Spring(0, 5.2 / Math.max(0.2, sec), 1);
  s.to(1);
  const from = new THREE.Vector3(), fromLook = new THREE.Vector3();
  let armed = false;
  return (camera, dt, w, camOut) => {
    if (!armed) { armed = true; frontSeat(w, 3.0, 1.1, 0, from, fromLook); }
    const k = clamp(s.step(dt), 0, 1);
    camera.position.copy(from).lerp(camOut.pos, k); camera.up.copy(camOut.up);
    camera.lookAt(_l.copy(fromLook).lerp(camOut.look, k));
    return k < 0.995;
  };
}
/** Swing to the front of the cup over ~0.9 s so she faces the card, then hold (a cut under reduced motion, Law VI). */
export function resultsCamera({ tier = 0, reducedMotion = false, sec = 0.9 } = {}) {
  const s = new Spring(0, 5.2 / sec, 1);
  s.to(1);
  const from = new THREE.Vector3(), fromLook = new THREE.Vector3(), look = new THREE.Vector3();
  let armed = false;
  return (camera, dt, w, camOut) => {
    if (!armed) { armed = true; from.copy(camera.position); fromLook.copy(camOut.look); if (w.kart.setFace) { try { w.kart.setFace(tier); } catch (e) { /* no face yet */ } } }
    const k = reducedMotion ? 1 : clamp(s.step(dt), 0, 1);
    frontSeat(w, 3.4, 1.35, -0.55, _p, _l);   // she sits right of centre, the card slides in on the left
    camera.position.copy(from).lerp(_p, k); camera.up.set(0, 1, 0);
    camera.lookAt(look.copy(fromLook).lerp(_l, k));
    return true;
  };
}
/** The chase seat before the first step, from the same numbers kart.js uses; start() clears it. */
export function preRollCamera() {
  return (camera, dt, w) => {
    const lay = w.layout, ks = w.kart.state;
    lay.toWorld(lay.wrap(ks.d - CAM_BACK), ks.x * 0.45, CAM_UP, _p);
    lay.toWorld(lay.wrap(ks.d + CAM_LOOK_AHEAD), ks.x * 0.3, LANE_H, _l);
    camera.position.copy(_p); camera.up.set(0, 1, 0); camera.lookAt(_l);
    return true;
  };
}
/** The face the End card wears: a personal best is $_$, a strong run ^_^, a middling one :3, a quiet one o_o. */
export function resultTier(total, best, pb) {
  if (pb) return FACE.money;
  if (!(best > 0) || total > best * 0.6) return FACE.happy;
  return total > best * 0.3 ? FACE.cat : FACE.wide;
}

// self-check: node --check is the bar; resultTier(10, 0, false) === 0, resultTier(1, 100, true) === 4.
