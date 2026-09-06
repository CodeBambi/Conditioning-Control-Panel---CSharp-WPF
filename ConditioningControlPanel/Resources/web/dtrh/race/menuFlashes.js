/* ============================================================================
 * race/menuFlashes.js - the idle show on the menu stage.
 *
 *   createMenuFlashes({ scene, emiRoot, baseY, faces, setFace, holdBeat, log })
 *     -> { update(dt, ctx), attach(model, animations), fire(kind), release(), pending, dispose() }
 *
 * Every 8 to 13 s (the first 4 to 6 s after the stage is up) a FLASH pops in
 * the air around EMI and she looks at it. Two kinds, alternating on a coin so
 * it never reads as a metronome: a CAMERA FLASH (fast, white, starry eyes) and
 * a BUBBLE POP (slower, pink, a ring behind it, spiral eyes). Each is one
 * additive sprite on pixel.js's CRISP_LAYER (a soft radial with four soft
 * spokes through it), a handful of shards falling out of it and a faint round
 * glow on the floor under it.
 *
 * THE GLANCE. Law III says EMI is the one breath on screen, and Law XI says a
 * blend and never a cut: the idle clip keeps running under the whole show and
 * nothing here ever touches the mixer. She turns by a damped spring on
 * `emiRoot.rotation.y` that overshoots a little and settles in about half a
 * second, her antenna leans into the turn and tips at the flash while she
 * holds it (`ant0` / `ant1` written after the mixer has had the frame; a rig
 * without those pivots gets a small lean of the whole root instead), the face
 * swaps for the hold and then everything springs home together.
 *
 * The show is menu-mode only, it never starts while a one-shot has her, and a
 * one-shot or a peek that starts mid-glance ends the glance through the same
 * return path. Reduced motion: no flashes at all, the sprites stay hidden.
 * `?flash=<ms>` fires one at that time for a screenshot, `?flash=<ms>,pop`
 * picks the bubble pop.
 * ==========================================================================*/

import * as THREE from 'three';
import { CRISP_LAYER } from './pixel.js';

// The cadence, in seconds.
export const FIRST_MIN = 4, FIRST_MAX = 6, GAP_MIN = 8, GAP_MAX = 13;
export const HOLD_MIN = 1.1, HOLD_MAX = 1.5;
/**
 * The ring she is looked at from: [bearing in degrees (0 is straight at the camera, negative is her
 * left, which is screen left), weight, the far end of the radius]. Seven slots across the half the
 * camera can see, weighted to the middle of it. The menu column parks her right of the frame centre,
 * so there is little room on her right: those slots are rarer and closer in. The cup on the floor
 * beside the podium sits at -119 degrees and nothing lands behind it.
 */
const SLOTS = [[-100, 1, 1.8], [-70, 2, 1.9], [-44, 3, 1.8], [-16, 3, 1.7], [12, 3, 1.6], [42, 2, 1.4], [72, 1, 1.25]];
const R_MIN = 1.2, Y_MIN = 0.72, Y_MAX = 1.06;   // metres, the height above the podium plate
const KINDS = {
  flash: { sec: 0.45, color: 0xfff4fb, ring: false, face: 'starry', grow: 0.32, shards: 8, spread: 1.5 },
  pop:   { sec: 0.62, color: 0xff8fd0, ring: true,  face: 'spiral', grow: 0.62, shards: 6, spread: 1.1 },
};
const SIZE_MIN = 0.2, SIZE_MAX = 1.4, SHARD_N = 8, SHARD_G = 4.2;
const YAW_W = 13, YAW_Z = 0.55;        // overshoots a little, settled in about half a second
// She turns her body part of the way and the antenna does the rest: past this the screen she wears
// for a face would swing out of the shot, and the whole point is that the camera sees her react.
const YAW_MAX = 0.85;
const LEAN_MAX = 0.42, TIP = 0.22;     // the antenna: into the turn, then up at the flash
const RETURN_SEC = 0.55;

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const rand = (a, b) => a + Math.random() * (b - a);
/** The same angle folded into (-pi, pi], so she always turns the short way round. */
const wrapPi = (a) => { let r = a % (Math.PI * 2); if (r > Math.PI) r -= Math.PI * 2; else if (r <= -Math.PI) r += Math.PI * 2; return r; };

/** Damped spring on a scalar, the same one intro.js runs (kept local: intro.js imports menu.js). */
class Spring {
  constructor(w = 10, z = 0.9) { this.x = 0; this.v = 0; this.target = 0; this.w = w; this.z = z; }
  to(t) { this.target = t; return this; }
  set(x) { this.x = x; this.target = x; this.v = 0; return this; }
  step(dt) {
    const n = dt > 0.02 ? Math.min(8, Math.ceil(dt / 0.02)) : 1, h = dt / n;
    for (let i = 0; i < n; i++) { this.v += (this.w * this.w * (this.target - this.x) - 2 * this.z * this.w * this.v) * h; this.x += this.v * h; }
    return this.x;
  }
}

// ---- the three canvas faces: the pop, the ring behind it, the shard --------------------------
function canvasTex(size, draw) {
  try {
    const c = document.createElement('canvas'); c.width = c.height = size;
    const g = c.getContext('2d');
    if (!g) return null;
    g.globalCompositeOperation = 'lighter';
    draw(g, size);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
  } catch (e) { return null; }
}
/** A soft radial core with four soft spokes through it: the pop of light itself. */
function popTexture() {
  return canvasTex(128, (g) => {
    const core = g.createRadialGradient(64, 64, 0, 64, 64, 34);
    core.addColorStop(0, 'rgba(255,255,255,1)'); core.addColorStop(0.16, 'rgba(255,240,251,0.92)');
    core.addColorStop(0.4, 'rgba(255,190,232,0.3)'); core.addColorStop(0.72, 'rgba(255,150,210,0.07)'); core.addColorStop(1, 'rgba(255,150,210,0)');
    g.fillStyle = core; g.fillRect(0, 0, 128, 128);
    for (const rot of [0, Math.PI / 2]) {   // one squashed radial per axis: four spokes, soft at every end
      g.save(); g.translate(64, 64); g.rotate(rot); g.scale(1, 0.055);
      const sp = g.createRadialGradient(0, 0, 0, 0, 0, 60);
      sp.addColorStop(0, 'rgba(255,255,255,1)'); sp.addColorStop(0.3, 'rgba(255,240,250,0.45)'); sp.addColorStop(0.7, 'rgba(255,220,245,0.12)'); sp.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = sp; g.beginPath(); g.arc(0, 0, 60, 0, Math.PI * 2); g.fill(); g.restore();
    }
  });
}
/** The bubble pop's ring: nothing in the middle, a bright band, a soft outside. */
function ringTexture() {
  return canvasTex(96, (g, s) => {
    const h = s / 2, gr = g.createRadialGradient(h, h, 0, h, h, h);
    gr.addColorStop(0, 'rgba(255,255,255,0)'); gr.addColorStop(0.62, 'rgba(255,255,255,0)');
    gr.addColorStop(0.78, 'rgba(255,235,248,1)'); gr.addColorStop(0.88, 'rgba(255,170,220,0.5)'); gr.addColorStop(1, 'rgba(255,160,215,0)');
    g.fillStyle = gr; g.fillRect(0, 0, s, s);
  });
}
function dotTexture() {
  return canvasTex(32, (g, s) => {
    const h = s / 2, gr = g.createRadialGradient(h, h, 0, h, h, h - 1);
    gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(0.55, 'rgba(255,225,245,0.6)'); gr.addColorStop(1, 'rgba(255,200,235,0)');
    g.fillStyle = gr; g.fillRect(0, 0, s, s);
  });
}

/**
 * The screenshot aid. `?flash=<ms>` fires one flash that many ms after the stage is up,
 * `?flash=<ms>,pop` picks the bubble pop and a third field pins the slot (`?flash=3000,pop,3`) so
 * two captures of the same beat land in the same place; `?freeze=<ms>` then holds the whole stage
 * still that many ms after it fired, so a headless capture is on exactly the frame it asked for.
 * Null when the query string says nothing, which is every path a player takes.
 */
export function flashAid(search) {
  try {
    const q = new URLSearchParams(search != null ? search : location.search), raw = q.get('flash');
    if (!raw) return null;
    const bits = String(raw).split(','), ms = Number(bits[0]), fz = Number(q.get('freeze')), sl = Number(bits[2]);
    if (!isFinite(ms) || ms < 0) return null;
    return { at: ms / 1000, kind: bits[1] === 'pop' ? 'pop' : 'flash',
      slot: isFinite(sl) && sl >= 0 && sl < SLOTS.length ? sl | 0 : -1,
      freeze: isFinite(fz) && fz >= 0 ? fz / 1000 : null };
  } catch (e) { return null; }
}

export function createMenuFlashes({ scene, emiRoot, baseY = 0, faces = null, setFace = null, holdBeat = null, log = null } = {}) {
  const own = [], keep = (x) => { if (x) own.push(x); return x; };
  const group = new THREE.Group(); group.name = 'menu-flashes';
  if (scene) scene.add(group);
  const popTex = keep(popTexture()), ringTex = keep(ringTexture()), dotTex = keep(dotTexture());
  const sprite = (map) => {
    const mat = keep(new THREE.SpriteMaterial({ map, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, opacity: 0, fog: false }));
    const s = new THREE.Sprite(mat);
    s.visible = false; s.layers.set(CRISP_LAYER); group.add(s);
    return s;
  };
  const core = sprite(popTex), ring = sprite(ringTex);
  const shards = [];
  for (let i = 0; i < SHARD_N; i++) shards.push({ s: sprite(dotTex), vx: 0, vy: 0, vz: 0, age: 0, life: 0, size: 0.08 });
  // the floor takes a faint round glow under the pop: one flat disc, additive, never written to depth
  const spot = new THREE.Mesh(keep(new THREE.CircleGeometry(0.55, 18)), keep(new THREE.MeshBasicMaterial({ color: 0xff9ad6, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, fog: false })));
  spot.rotation.x = -Math.PI / 2; spot.position.y = 0.03; spot.visible = false; spot.layers.set(CRISP_LAYER); group.add(spot);

  const at = new THREE.Vector3();
  const yaw = new Spring(YAW_W, YAW_Z), lean = new Spring(15, 0.7), tip = new Spring(11, 0.8);
  const aid = flashAid();
  let disposed = false, lastSlot = -1, nextAt = rand(FIRST_MIN, FIRST_MAX), aidLive = !!aid, aidFrom = -1, frozen = false;
  let fxOn = false, fxAge = 0, fxKind = KINDS.flash;
  let phase = '', glanceAge = 0, hold = 0;      // '' | 'turn' | 'home'
  let ant0 = null, ant1 = null, antDriven = false, antApplied = false;
  const rest = { a0x: 0, a0z: 0, a1x: 0, a1z: 0 };

  /**
   * Take the antenna pivots off the stage clone. A pivot the idle clip drives is written ADDITIVELY
   * after the mixer has had the frame (the mixer rewrites it from the clip every frame, so the offset
   * cannot pile up); a pivot no clip touches is written as its authored rest plus the offset. With no
   * `ant0` at all the lean lands on the root instead, which reads as a smaller version of the same beat.
   */
  function attach(model, animations) {
    if (!model || !model.getObjectByName) return;
    const driven = new Set();
    for (const clip of animations || []) for (const tr of (clip && clip.tracks) || []) {
      try { const n = THREE.PropertyBinding.parseTrackName(tr.name).nodeName; if (n) driven.add(n); } catch (e) { /* an odd track name is not ours */ }
    }
    ant0 = model.getObjectByName('ant0') || null;
    ant1 = model.getObjectByName('ant1') || null;
    antDriven = !!(ant0 && driven.has('ant0'));
    if (ant0) { rest.a0x = ant0.rotation.x; rest.a0z = ant0.rotation.z; }
    if (ant1) { rest.a1x = ant1.rotation.x; rest.a1z = ant1.rotation.z; }
    if (log) log(`[menu] flashes: antenna ${ant0 ? (antDriven ? 'ant0 additive over the clip' : 'ant0 over its rest') : 'absent, the root leans instead'}`);
  }

  /** One slot, never the one the last flash used, weighted to the half the camera can see. */
  function pickSlot() {
    let total = 0;
    for (let i = 0; i < SLOTS.length; i++) if (i !== lastSlot) total += SLOTS[i][1];
    let r = Math.random() * total;
    for (let i = 0; i < SLOTS.length; i++) { if (i === lastSlot) continue; r -= SLOTS[i][1]; if (r <= 0) return i; }
    return lastSlot === 0 ? 1 : 0;
  }
  function faceFor(k) {
    if (!faces) return null;
    const i = faces[k.face];
    return i == null ? null : i;
  }
  /** Pop one flash and start the glance. `kind` is 'flash' or 'pop'; anything else rolls one. */
  function fire(kind, slotAt = -1) {
    if (disposed) return false;
    const k = KINDS[kind] || (Math.random() < 0.5 ? KINDS.flash : KINDS.pop);
    const slot = slotAt >= 0 && slotAt < SLOTS.length ? slotAt : pickSlot();
    lastSlot = slot;
    const [deg, , rMax] = SLOTS[slot], a = deg * Math.PI / 180, r = rand(R_MIN, rMax);
    at.set(Math.sin(a) * r, baseY + rand(Y_MIN, Y_MAX), Math.cos(a) * r);
    fxKind = k; fxOn = true; fxAge = 0;
    core.position.copy(at); core.material.color.setHex(k.color); core.visible = true;
    ring.position.copy(at); ring.material.color.setHex(k.color); ring.visible = !!k.ring;
    spot.position.set(at.x, 0.03, at.z); spot.visible = true;
    const n = Math.min(SHARD_N, k.shards);
    for (let i = 0; i < SHARD_N; i++) {
      const sh = shards[i];
      if (i >= n) { sh.life = 0; sh.s.visible = false; continue; }
      const th = rand(0, Math.PI * 2), sp = rand(0.5, 1.4) * k.spread;
      sh.vx = Math.cos(th) * sp; sh.vz = Math.sin(th) * sp * 0.6; sh.vy = rand(0.2, 1.1);
      sh.age = 0; sh.life = rand(0.45, 0.8); sh.size = rand(0.05, 0.11);
      sh.s.position.copy(at); sh.s.material.color.setHex(k.color); sh.s.visible = true;
    }
    // she turns to it: the short way round, overshooting a hair, and holds it while the face is on
    phase = 'turn'; glanceAge = 0; hold = rand(HOLD_MIN, HOLD_MAX);
    yaw.to(clamp(wrapPi(Math.atan2(at.x, at.z)), -YAW_MAX, YAW_MAX));
    tip.to(TIP);
    const fi = faceFor(k);
    if (fi != null && setFace) setFace(fi);
    if (holdBeat) holdBeat(k.sec + hold + RETURN_SEC + 0.4);
    return true;
  }
  /** End the glance early and go home the way it would have gone anyway (a one-shot or a peek took her). */
  function release() {
    if (!phase) return;
    phase = 'home'; yaw.to(0); tip.to(0);
  }
  function hideFx() {
    fxOn = false;
    core.visible = ring.visible = spot.visible = false;
    core.material.opacity = ring.material.opacity = spot.material.opacity = 0;
    for (const sh of shards) { sh.life = 0; sh.s.visible = false; }
  }

  function update(dt, ctx) {
    if (disposed || !ctx) return;
    const t = ctx.t || 0, live = ctx.mode === 'menu' && !ctx.reduced;
    // the run, the story and the options all stop the show where it stands; the menu picks it back up
    if (!live) { if (phase) release(); if (fxOn) hideFx(); if (nextAt < t + FIRST_MIN) nextAt = t + FIRST_MIN; }
    else if (aidLive && t >= aid.at && !ctx.busy) { aidLive = false; aidFrom = t; if (log) log(`[menu] flash aid: ${aid.kind} at t ${t.toFixed(2)}`); fire(aid.kind, aid.slot); }
    else if (!phase && !fxOn && !ctx.busy && t >= nextAt) fire(null);
    if (aid && aid.freeze != null && aidFrom >= 0 && t - aidFrom >= aid.freeze) frozen = true;

    // the pop of light: it grows out of nothing and is gone inside half a second
    if (fxOn) {
      fxAge += dt;
      const u = clamp(fxAge / fxKind.sec, 0, 1), g = Math.pow(u, fxKind.grow);
      const s = SIZE_MIN + (SIZE_MAX - SIZE_MIN) * g, o = Math.pow(1 - u, 1.7) * (u < 0.06 ? u / 0.06 : 1);
      core.scale.set(s, s, 1); core.material.opacity = o;
      if (ring.visible) { const rs = 0.3 + 2.1 * u; ring.scale.set(rs, rs, 1); ring.material.opacity = Math.min(1, o * 1.15); }
      spot.scale.setScalar(0.6 + 1.4 * u); spot.material.opacity = o * 0.3;
      let any = false;
      for (const sh of shards) {
        if (sh.life <= 0) continue;
        sh.age += dt;
        if (sh.age >= sh.life) { sh.life = 0; sh.s.visible = false; continue; }
        any = true;
        sh.vy -= SHARD_G * dt;
        sh.s.position.set(sh.s.position.x + sh.vx * dt, sh.s.position.y + sh.vy * dt, sh.s.position.z + sh.vz * dt);
        const su = 1 - sh.age / sh.life;
        sh.s.scale.set(sh.size * su, sh.size * su, 1); sh.s.material.opacity = su * 0.9;
      }
      if (u >= 1 && !any) hideFx();
    }
    // the glance: hold her on it, then send her home. The idle clip never stops under any of this.
    if (phase) {
      glanceAge += dt;
      if (phase === 'turn' && glanceAge >= fxKind.sec + hold) release();
      else if (phase === 'home' && Math.abs(yaw.x) < 0.01 && Math.abs(yaw.v) < 0.05) {
        phase = ''; yaw.set(0); tip.set(0);
        nextAt = t + rand(GAP_MIN, GAP_MAX);
        if (setFace && faces && faces.idle != null && !ctx.busy) setFace(faces.idle);
      }
    }
    yaw.step(dt); tip.step(dt);
    lean.to(clamp(-yaw.v * 0.05, -LEAN_MAX, LEAN_MAX)).step(dt);
    if (emiRoot) emiRoot.rotation.y = yaw.x;
    const on = Math.abs(lean.x) > 1e-4 || Math.abs(tip.x) > 1e-4;
    if (ant0) {
      if (on) {
        if (antDriven) { ant0.rotation.z += lean.x; ant0.rotation.x -= tip.x; if (ant1) { ant1.rotation.z += lean.x * 0.6; ant1.rotation.x -= tip.x * 0.5; } }
        else { ant0.rotation.z = rest.a0z + lean.x; ant0.rotation.x = rest.a0x - tip.x; if (ant1) { ant1.rotation.z = rest.a1z + lean.x * 0.6; ant1.rotation.x = rest.a1x - tip.x * 0.5; } }
        antApplied = true;
      } else if (antApplied && !antDriven) {
        ant0.rotation.z = rest.a0z; ant0.rotation.x = rest.a0x;
        if (ant1) { ant1.rotation.z = rest.a1z; ant1.rotation.x = rest.a1x; }
        antApplied = false;
      } else antApplied = false;
    } else if (emiRoot) emiRoot.rotation.z = lean.x * 0.35;   // no antenna pivots: the whole of her leans a little
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    hideFx();
    if (emiRoot) { emiRoot.rotation.y = 0; emiRoot.rotation.z = 0; }
    group.removeFromParent(); group.clear();
    for (const x of own) { try { x.dispose(); } catch (e) { /* shared or gone */ } }
  }
  return { update, attach, fire, release, dispose, get pending() { return !!phase; }, get frozen() { return frozen; }, get slots() { return SLOTS.length; } };
}

// self-check: node --check is the bar. pickSlot / the KINDS table are pure; the rest needs THREE.
