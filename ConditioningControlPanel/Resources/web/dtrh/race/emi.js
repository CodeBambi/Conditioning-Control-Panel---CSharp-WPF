// race/emi.js - Racing Thoughts: the teacup rig with EMI riding in it, seen from behind.
// Visual half of CONTRACT.md "race/kart.js (PR 3)": saucer + cup + handle + tea surface, EMI as a
// CRT body with vents, two glove arms on the rim, a bead antenna with the six mood poses, pooled
// sweat drops, drift sparks, and a plain disc of tea. Canon lock: EMI faces forward, away from the
// camera, so no face is ever visible. No drawn face, no reflection of one, no generated face, no
// mouthed words. The moods live in the antenna and the bead alone.
// Ported from the approved pitch demo (C:\wt-race-ref\pitch-demo.html) into ESM; poses now blend
// on a spring (House Book Law XI), never a linear tween, and always return to the breath.
//
// THE MODEL. The primitive CRT below is now only the fallback. On creation the rig asks gltf.js for
// race/assets/emi.glb (the Blender hard-surface EMI); the moment it lands the primitive group comes
// off the graph and is freed, and the glb clone takes the same seat in the cup. Until then, and for
// good if the load fails, the primitive rides. Everything else (tea, cupLight, sweat, sparks) is
// untouched and the MOODS table drives the glb's own pivots instead.
//
// THE CROCKERY. The same deal for the cup and the saucer: race/assets/props.glb ships kart_cup
// (lathe, glaze rim and handle in one node) and kart_saucer, and they take over from the
// LatheGeometry cup, its rim torus, the handle tube, the saucer cylinder and its rim the moment
// the pack lands. Needs feat/race-b4-props-glb underneath for the file itself.

import * as THREE from 'three';
import { loadPack, setFace as packSetFace, preparePixel, flattenRig, FACES } from './gltf.js';
import { createPoseLayer } from './emiPoses.js';

const PINK = 0xff69b4, GOLD = 0xF2C14E, PALE = 0xB3C7FF, PORCELAIN = 0xF6E7C8;
const BREATH_SEC = 3.9;      // the one breath on screen (Law III)
const SWEAT_N = 28, SPARK_N = 32;
const EMI_GLB = '/dtrh/race/assets/emi.glb';
const PROPS_GLB = '/dtrh/race/assets/props.glb';
// props.glb, measured off the node bboxes (assets/PROPS.md): the saucer stands 0.125 tall to the
// top of its glaze band, the cup is 0.695 to its rim with the band at 0.645..0.690, handle on +x.
// The cup sits CUP_Y above the saucer base, the way the JS rig always sat it.
const CUP_Y = 0.09, SAUCER_TOP = 0.125, CUP_RIM_TOP = 0.695;
/** The saucer's outer radius in rig units (kart_saucer is 1.90 across, the JS dish 0.95 too).
 *  kart.js needs it to know how much air a pitch costs. */
export const SAUCER_R = 0.95;
// THE CUP TIPS, THE SAUCER DOES NOT. kart.js hands its steer lean over as `ctx.lean` now instead
// of rolling the whole kart about the forward axis: the group rides ON the road, so a banked
// saucer put its outer edge sin(lean) * 0.95 * KART_SCALE under the surface, 0.84 m under it at a
// drift lean. A saucer slides, it does not bank. So `body` alone turns, about the cup's foot on
// the dish (CUP_PIVOT_Y), lifted by sin(tip) * CUP_FOOT_R so the low half of the foot kisses the
// dish instead of cutting into it, and slid CUP_SLIDE * sin(tip) toward the outside of the turn so
// the lean still reads as g-force. CUP_TIP_MAX saturates the angle (tanh, so small leans are one
// for one and only the big ones are held back): the lift keeps the foot honest at any angle, but
// the handle bows out to x 0.86 and reaches the road on its own at about 45 degrees, and a cup
// tipped that far on a flat saucer reads as falling off it. At the 21 degree cap the foot clears
// the road by 0.13 m, the handle by 0.24 m, and the cup rises 0.15 m off the dish at full lock.
// This is geometry, not a transition, so reduced motion keeps every bit of it.
const CUP_FOOT_R = 0.3, CUP_PIVOT_Y = CUP_Y, CUP_TIP_MAX = 0.38, CUP_SLIDE = 0.07;
const OUTLINE = 'outline';   // the inverted hull's material name (gltf.js header, assets/README.md)
// The glb is metres, sole centre at the origin, +Z forward, case top at 1.00 m. 0.64 puts the case
// at the old 1.25-scaled CRT's 0.525 m; with the sole on the tea (y 0.66) the case bottom lands at
// 0.775, just clear of the cup rim at 0.75, so the legs stay in the cup and the case reads as before.
// Owner call 2026-09-06: she rides INSIDE the cup, not on the tea. At 1.0 the case is 0.83 wide
// in a 1.1 m bore, and GLB_SINK drops the sole 0.33 under the tea line so the case bottom (0.167 in
// the model) sits 0.30 below the rim: the tea disc cuts her at the chest and the legs are never seen.
const GLB_SCALE = 0.8;        // owner call 2026-09-06: full size read too big in the bore
const GLB_SINK = 0.28;       // metres below TEA_Y the sole rests; the case bottom lands 0.28 m under the rim
const GLB_SEAT_Z = 0.25;     // the case spans z -0.59..0.08 in the model, so this centres it in the bore
const CASE_TOP = [0.36, 0.99, -0.2];   // EMI_case local: the top corner sweat comes off
const GLOW_Y = 0.62, GLOW_Z = 0.9;     // the screen light, model local (0.58 m ahead of the glass)

/** Antenna poses per mood. antX pitches the whole stem (negative = streamed back), kinkX/Z bend
 *  the joint, wind = how much speed is allowed to stream the stem, w = spring speed (rad/s).
 *  There is no face field on purpose: the antenna is the whole expression. */
export const MOODS = {
  calm:     { antX: 0,    kinkX: 0,    kinkZ: 0,    bead: PINK, scale: 1,   sway: 1,   wind: 1,   w: 8,  zeta: 0.65 },
  streamed: { antX: -1.0, kinkX: -0.3, kinkZ: 0,    bead: PINK, scale: 1,   sway: 0,   wind: 1,   w: 14, zeta: 0.7 },
  fraught:  { antX: 0.25, kinkX: 1.4,  kinkZ: 0,    bead: PALE, scale: 0.9, sway: 0.4, wind: 0.3, w: 10, zeta: 0.6 },
  smug:     { antX: 0,    kinkX: 0,    kinkZ: 0.55, bead: PINK, scale: 1,   sway: 1,   wind: 0.6, w: 7,  zeta: 0.6 },
  shock:    { antX: 0.1,  kinkX: 0,    kinkZ: 0,    bead: PINK, scale: 1.6, sway: 0,   wind: 0,   w: 22, zeta: 0.45 },
  jackpot:  { antX: 0,    kinkX: 0,    kinkZ: 0.2,  bead: GOLD, scale: 1.3, sway: 1,   wind: 0.6, w: 12, zeta: 0.5 },
};

/** Damped spring toward a target; zeta < 1 overshoots a little, which is the point. */
class Spring {
  constructor(x = 0) { this.x = x; this.v = 0; }
  step(target, dt, w, zeta = 0.65) {
    this.v += (w * w * (target - this.x) - 2 * zeta * w * this.v) * dt;
    this.x += this.v * dt;
    return this.x;
  }
}

const _m = new THREE.Matrix4(), _q = new THREE.Quaternion(), _s = new THREE.Vector3(), _p = new THREE.Vector3();
const _g = new THREE.Vector3(), _c = new THREE.Color();

/** Tiny pooled particle system in WORLD space (drops fall toward the road, not with the cup). */
function makePool(n, geo, mat) {
  const mesh = new THREE.InstancedMesh(geo, mat, n);
  mesh.frustumCulled = false;
  const items = [];
  for (let i = 0; i < n; i++) {
    items.push({ life: 0, ttl: 1, size: 1, p: new THREE.Vector3(), v: new THREE.Vector3() });
    mesh.setMatrixAt(i, _m.compose(_p.set(0, -999, 0), _q, _s.setScalar(0.0001)));
  }
  let cursor = 0;
  return {
    mesh,
    spawn(p, v, ttl, size) {
      const it = items[cursor]; cursor = (cursor + 1) % n;
      it.life = it.ttl = ttl; it.size = size; it.p.copy(p); it.v.copy(v);
    },
    update(dt, gravity) {
      for (let i = 0; i < n; i++) {
        const it = items[i];
        if (it.life <= 0) continue;
        it.life -= dt;
        it.v.addScaledVector(gravity, dt);
        it.p.addScaledVector(it.v, dt);
        const k = it.life > 0 ? it.size * (0.55 + 0.45 * it.life / it.ttl) : 0.0001;
        mesh.setMatrixAt(i, _m.compose(it.life > 0 ? it.p : _p.set(0, -999, 0), _q, _s.setScalar(k)));
      }
      mesh.instanceMatrix.needsUpdate = true;
    },
  };
}

function ventTexture() {
  const c = document.createElement('canvas'); c.width = c.height = 64;
  const g = c.getContext('2d');
  g.fillStyle = '#1B1A33'; g.fillRect(0, 0, 64, 64);
  g.fillStyle = '#0F0E22'; for (let y = 10; y < 44; y += 6) g.fillRect(10, y, 44, 2);
  g.fillStyle = '#F2C14E'; g.beginPath(); g.arc(50, 54, 3.2, 0, 7); g.fill();
  g.fillStyle = '#FF69B4'; g.fillRect(10, 52, 14, 4);
  const t = new THREE.CanvasTexture(c); t.magFilter = THREE.NearestFilter; t.minFilter = THREE.LinearFilter;
  return t;
}

/**
 * createEmiRig({ scene, reducedMotion, pixel }) ->
 *   { group, update(dt, ctx), setMood, setFraught, squash, dispose, onReady(cb), model(), setFace(i), pose(name, opts) }
 * ctx = { t, up, right, tangent, speedNorm, airborne, steerVel, drift, driftSide, lean }  (world-space frame)
 * `group` is the whole rig; the caller positions and orients it. Particles are added to `scene`.
 * `pixel` is race/pixel.js: the glb's textures arrive after the run's one retexture(scene) pass, so
 * they go through preparePixel on mount. `model()` is the glb root once it lands, else null.
 */
export function createEmiRig({ scene, reducedMotion = false, pixel = null }) {
  const group = new THREE.Group(); group.name = 'kart';
  const root = new THREE.Group(); group.add(root);          // squash scales this, not the frame
  const porcelain = new THREE.MeshStandardMaterial({ color: PORCELAIN, roughness: 0.35, metalness: 0.05 });
  const pinkGlow = new THREE.MeshStandardMaterial({ color: PINK, emissive: PINK, emissiveIntensity: 1.2, roughness: 0.4 });
  const navy = new THREE.MeshStandardMaterial({ color: 0x252542, roughness: 0.6 });
  const navyDark = new THREE.MeshStandardMaterial({ color: 0x1B1A33, roughness: 0.7 });
  const stemMat = new THREE.MeshStandardMaterial({ color: 0x2C2B4A, roughness: 0.5 });
  const glove = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.5 });

  // ---- saucer ----
  const saucer = new THREE.Group(); root.add(saucer);
  const sc = new THREE.Mesh(new THREE.CylinderGeometry(0.95, 0.7, 0.09, 40), porcelain); sc.position.y = 0.05; saucer.add(sc);
  const saucerRim = new THREE.Mesh(new THREE.TorusGeometry(0.93, 0.03, 8, 48), pinkGlow); saucerRim.rotation.x = Math.PI / 2; saucerRim.position.y = 0.09; saucer.add(saucerRim);
  const saucerMark = new THREE.Mesh(new THREE.BoxGeometry(0.3, 0.02, 0.08), pinkGlow); saucerMark.position.set(0.7, 0.1, 0); saucer.add(saucerMark);

  // ---- cup ----
  const body = new THREE.Group(); root.add(body);
  const prof = [[0.001, 0], [0.28, 0], [0.34, 0.06], [0.44, 0.3], [0.52, 0.58], [0.55, 0.66], [0.5, 0.68], [0.47, 0.6], [0.4, 0.3], [0.3, 0.1], [0.001, 0.08]]
    .map(([x, y]) => new THREE.Vector2(x, y));
  const cupMat = new THREE.MeshStandardMaterial({ color: PORCELAIN, roughness: 0.35, metalness: 0.05, side: THREE.DoubleSide });
  const cupMesh = new THREE.Mesh(new THREE.LatheGeometry(prof, 40), cupMat); cupMesh.position.y = 0.09; body.add(cupMesh);
  const rim = new THREE.Mesh(new THREE.TorusGeometry(0.53, 0.025, 8, 48), pinkGlow); rim.rotation.x = Math.PI / 2; rim.position.y = 0.75; body.add(rim);
  // the handle: a C along a bezier whose two ends start INSIDE the cup wall (the wall radius is
  // ~0.37 at the low root and ~0.51 at the high root, so 0.33 and 0.46 sit 4-5 cm inside it),
  // bows out to 0.86 and comes back. Same material as the cup so the retexture tints them alike.
  const handleCurve = new THREE.CubicBezierCurve3(
    new THREE.Vector3(0.33, 0.24, 0), new THREE.Vector3(0.86, 0.14, 0),
    new THREE.Vector3(0.86, 0.74, 0), new THREE.Vector3(0.46, 0.63, 0));
  const handle = new THREE.Mesh(new THREE.TubeGeometry(handleCurve, 18, 0.048, 8, false), cupMat); body.add(handle);

  // ---- the tea: a plain tinted liquid disc, nothing drawn in it. It takes the room's colour
  // (the scene fog hue, which fx grades per room) and ripples a little. EMI faces forward, away
  // from the camera, so there is no face to reflect and none is ever painted here.
  // the tea sits the same 0.11 under the rim it always did, now measured off the glb cup's node
  // bbox: CUP_Y + CUP_RIM_TOP is the rim at 0.785, so the surface lands at 0.675.
  const TEA_R = 0.46, TEA_Y = CUP_Y + CUP_RIM_TOP - 0.11;
  const teaMat = new THREE.MeshStandardMaterial({ color: 0xC94A9A, emissive: 0x3c1630, roughness: 0.12, metalness: 0.25, transparent: true, opacity: 0.9 });
  const tea = new THREE.Mesh(new THREE.CircleGeometry(TEA_R + 0.01, 32), teaMat);
  tea.rotation.set(-Math.PI / 2, 0, Math.PI); tea.position.y = TEA_Y; body.add(tea);
  const teaTarget = new THREE.Color(0xC94A9A), teaHsl = { h: 0, s: 0, l: 0 };

  // ---- EMI: a living pixel CRT seen from behind, gripping the rim ----
  // she sits a little forward in the cup, hands on the far rim
  const EMI_Z = 0.12;
  const emi = new THREE.Group(); emi.position.set(0, 0.82, EMI_Z); emi.scale.setScalar(1.25); body.add(emi);
  const crt = new THREE.Mesh(new THREE.BoxGeometry(0.5, 0.42, 0.34), navy); emi.add(crt);
  const ventMat = new THREE.MeshStandardMaterial({ map: ventTexture(), roughness: 0.8 });
  const back = new THREE.Mesh(new THREE.BoxGeometry(0.42, 0.34, 0.06), ventMat); back.position.z = -0.19; emi.add(back);
  const shoulderL = new THREE.Mesh(new THREE.BoxGeometry(0.1, 0.22, 0.26), navyDark); shoulderL.position.set(-0.29, -0.06, 0); emi.add(shoulderL);
  const shoulderR = shoulderL.clone(); shoulderR.position.x = 0.29; emi.add(shoulderR);
  const screenMat = new THREE.MeshBasicMaterial({ color: PINK, transparent: true, opacity: 0.85 });
  const screenGlow = new THREE.Mesh(new THREE.PlaneGeometry(0.4, 0.3), screenMat); screenGlow.position.z = 0.172; emi.add(screenGlow);
  const screenLight = new THREE.PointLight(PINK, 0.9, 3); screenLight.position.set(0, 0, 0.5); emi.add(screenLight);
  const armGeo = new THREE.CylinderGeometry(0.035, 0.03, 0.42, 8), handGeo = new THREE.SphereGeometry(0.06, 10, 8);
  for (const sd of [-1, 1]) {
    const arm = new THREE.Mesh(armGeo, navyDark); arm.position.set(sd * 0.3, -0.1, 0.25 - EMI_Z / 1.25); arm.rotation.set(-0.9, 0, sd * 0.35); emi.add(arm);
    const hand = new THREE.Mesh(handGeo, glove); hand.position.set(sd * 0.36, -0.22, 0.44 - EMI_Z / 1.25); emi.add(hand);
  }
  // the antenna: a short stem, a kink joint and the bead, about half the reach of the first
  // draft (STEM_L + KINK_L). The moods read through the kink angle, not the length.
  const STEM_L = 0.11, KINK_L = 0.08;
  const antenna = new THREE.Group(); antenna.position.y = 0.21; emi.add(antenna);
  const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.022, STEM_L, 8), stemMat); stem.position.y = STEM_L / 2; antenna.add(stem);
  const kink = new THREE.Group(); kink.position.y = STEM_L; antenna.add(kink);
  const stem2 = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.018, KINK_L, 8), stemMat); stem2.position.y = KINK_L / 2; kink.add(stem2);
  const beadMat = new THREE.MeshStandardMaterial({ color: PINK, emissive: PINK, emissiveIntensity: 0.9, roughness: 0.3 });
  const bead = new THREE.Mesh(new THREE.SphereGeometry(0.055, 12, 10), beadMat); bead.position.y = KINK_L + 0.01; kink.add(bead);
  const beadLight = new THREE.PointLight(PINK, 0.5, 2); bead.add(beadLight);
  const cupLight = new THREE.PointLight(PINK, 1.4, 14); cupLight.position.y = 1.2; group.add(cupLight);

  // ---- sweat (pale blue drops) and drift sparks, both world-space pools ----
  const sweat = makePool(SWEAT_N, new THREE.SphereGeometry(0.04, 8, 6), new THREE.MeshBasicMaterial({ color: 0xCFF6FF }));
  const sparks = makePool(SPARK_N, new THREE.BoxGeometry(0.07, 0.07, 0.07), new THREE.MeshBasicMaterial({ color: 0xFFD27A }));
  scene.add(sweat.mesh); scene.add(sparks.mesh);

  // ---- state ----
  let mood = MOODS.calm, fraught = 0, sweatAcc = 0, sparkAcc = 0;
  const sAnt = new Spring(0), sKinkX = new Spring(0), sKinkZ = new Spring(0), sBead = new Spring(1), sSquash = new Spring(0);
  const beadTarget = new THREE.Color(PINK);

  function setMood(id) { mood = MOODS[id] || MOODS.calm; }
  function setFraught(v) { fraught = Math.max(0, Math.min(1, +v || 0)); }
  function squash(amount) { sSquash.x = Math.max(sSquash.x, Math.min(1, amount)); sSquash.v = 0; }

  // ---- the glb: loaded once, mounted in the same seat, primitive freed ----
  let G = null;                     // { root, ant0, ant1, ant2, ballpiv, caseNode, ballMats, glassMats, rest }
  let P = null;                     // { cup, dish }: the props.glb crockery once it lands
  let poses = null;                 // race/emiPoses.js, built with the model
  let dead = false;
  const readyCbs = [], owned = [];  // owned = the materials this rig cloned or made, freed with it
  const seat = new THREE.Group();   // the mount point: the seat, the scale, the breath and the steer lean
  const SEAT_Y = TEA_Y - GLB_SINK;
  seat.position.set(0, SEAT_Y, GLB_SEAT_Z); seat.scale.setScalar(GLB_SCALE);

  const matsOf = (o) => (Array.isArray(o.material) ? o.material : o.material ? [o.material] : []);
  const isOutline = (o) => matsOf(o).some((m) => m && m.name === OUTLINE);
  function disposeTree(node) {
    node.traverse((n) => {
      if (n.geometry) n.geometry.dispose();
      for (const mt of matsOf(n)) { if (mt.map) mt.map.dispose(); mt.dispose(); }
    });
  }
  /** Give a subtree its own copy of every non-outline material, so the moods never write to the
   *  cached pack's materials (a second rig, or the next run, gets clean ones). */
  function ownMaterials(node, into) {
    if (!node) return;
    node.traverse((o) => {
      if (!o.isMesh || isOutline(o) || Array.isArray(o.material) || !o.material) return;
      o.material = o.material.clone(); owned.push(o.material); into.push(o.material);
    });
  }

  // the inverted hulls: a lit standard material reads grey, so every pack's outline meshes take
  // this one flat near-black, front faces only (the hull is already wound inside out)
  const black = new THREE.MeshBasicMaterial({ name: OUTLINE, color: 0x07060f, side: THREE.FrontSide });
  owned.push(black);
  const blackOutline = (node) => node.traverse((o) => { if (o.isMesh && isOutline(o)) o.material = black; });
  /** Take a primitive off the graph and free its geometry. Its material may be shared, so that is
   *  left to dispose() (the ones that go unshared are pushed onto `owned` at the call site). */
  const retire = (m) => { if (m.parent) m.parent.remove(m); if (m.geometry) m.geometry.dispose(); };

  function mountGlb(pack) {
    if (dead || G) return;
    const root = pack.clone('EMI_root');
    const ant0 = root && root.getObjectByName('ant0'), ballpiv = root && root.getObjectByName('ballpiv');
    if (!root || !ant0 || !ballpiv) return;         // a pack without the contract pivots: the primitive stays
    flattenRig(root, pack.animations);                // 69 draws a frame down to ~30: one per pivot and material
    blackOutline(root);
    const ballMats = [], glassMats = [];
    ownMaterials(root.getObjectByName('ball') || ballpiv, ballMats);
    ownMaterials(root.getObjectByName('EMI_glass'), glassMats);
    const ant1 = root.getObjectByName('ant1'), ant2 = root.getObjectByName('ant2');
    // the joints ship with a rest tilt: the moods are added on top of it, never instead of it
    const rest = { a0x: ant0.rotation.x, a0z: ant0.rotation.z, a1x: 0, a1z: 0, a2x: 0 };
    if (ant1) { rest.a1x = ant1.rotation.x; rest.a1z = ant1.rotation.z; }
    if (ant2) rest.a2x = ant2.rotation.x;
    G = { root, ant0, ant1, ant2, ballpiv, caseNode: root.getObjectByName('EMI_case') || root, ballMats, glassMats, rest };
    seat.add(root); body.add(seat);
    // the primitive stands down; its two lights move over to the model before it is freed
    emi.remove(screenLight); bead.remove(beadLight);
    body.remove(emi); disposeTree(emi);
    ballpiv.add(beadLight);
    seat.add(screenLight); screenLight.position.set(0, GLOW_Y, GLOW_Z);
    poses = createPoseLayer(root);
    if (pixel) preparePixel(root, pixel);
    setFaceFrame(0);
    for (const cb of readyCbs.splice(0)) { try { cb(root); } catch (e) { /* a listener never breaks the rig */ } }
  }

  /** Point the glass at one atlas frame. gltf.js setFace walks `map`; this pack carries the atlas on
   *  `emissiveMap` (the screen is self lit), so that path is the fallback here. Returns whether a
   *  frame was actually set, the same contract either way. */
  function setFaceFrame(i) {
    if (!G) return false;
    if (packSetFace(G.root, i)) return true;
    const n = Math.min(FACES.length - 1, Math.max(0, i | 0));
    let hit = false;
    for (const m of G.glassMats) {
      const tex = m && m.emissiveMap;
      if (!tex) continue;
      tex.wrapS = THREE.RepeatWrapping;
      tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.NearestFilter;
      tex.generateMipmaps = false;
      tex.offset.x = n / FACES.length;
      tex.needsUpdate = true; hit = true;
    }
    return hit;
  }
  /** The crockery. kart_cup carries the lathe, the glaze rim and the handle in one node, kart_saucer
   *  the dish and its rim band, so five primitives come off for two clones. The tea disc, the pink
   *  saucer mark, cupLight, the seat and every y this rig quotes are untouched: the glb heights are
   *  the ones those numbers were measured against. */
  function mountProps(pack) {
    if (dead || P) return;
    const cup = pack.clone('kart_cup'), dish = pack.clone('kart_saucer');
    if (!cup || !dish) return;                      // a pack without the crockery: the primitives stay
    blackOutline(cup); blackOutline(dish);
    P = { cup, dish };
    retire(sc); retire(saucerRim);                  // pinkGlow still dresses the mark, so it lives on
    saucer.add(dish);
    saucerMark.position.y = SAUCER_TOP + 0.01;      // back on top of the new glaze band
    retire(cupMesh); retire(rim); retire(handle);
    owned.push(cupMat, porcelain);                  // nothing on the graph wears these two now
    cup.position.y = CUP_Y;
    body.add(cup);
    if (pixel) { preparePixel(cup, pixel); preparePixel(dish, pixel); }
  }

  function onReady(cb) { if (typeof cb !== 'function') return; if (G) cb(G.root); else readyCbs.push(cb); }
  function model() { return G ? G.root : null; }
  /** Race events pose her body (race/emiPoses.js). No model yet, nothing to pose. */
  function pose(name, opts) { return poses ? poses.set(name, opts || {}) : false; }

  function emitFrom(obj, lx, ly, lz, ctx, pool, ttl, size, spread, upSpeed, backSpeed) {
    _p.set(lx, ly, lz); obj.localToWorld(_p);
    _g.copy(ctx.up).multiplyScalar(upSpeed).addScaledVector(ctx.right, (Math.random() - 0.5) * spread).addScaledVector(ctx.tangent, backSpeed);
    pool.spawn(_p, _g, ttl, size);
  }

  function update(dt, ctx) {
    dt = Math.min(dt, 0.05);
    const t = ctx.t || 0, m = mood;
    // the clamp and the kerbed landing carry their own fraught; the run brain's own value still wins
    // whenever it is higher, so a stack of effects is never talked down by a pose
    const fr = poses ? Math.max(fraught, poses.fraught) : fraught;
    const wind = Math.max(0, Math.min(1, (ctx.speedNorm - 0.55) / 0.45)) * 0.8 + (ctx.airborne ? 0.5 : 0);
    const antTarget = m.antX - Math.min(1, wind) * m.wind + (fr > 0.4 ? 0.25 * fr : 0);
    const kinkXT = m.kinkX + 1.7 * fr * (m === MOODS.fraught ? 0.3 : 1);
    const hush = ctx.airborne || m.sway === 0 ? 0 : m.sway * (1 - 0.6 * fr);
    const sway = hush * 0.14 * Math.sin(t * (Math.PI * 2) / BREATH_SEC);
    // one set of springs, two bodies to hang them on: the glb's authored pivots or the primitive's
    const antX = sAnt.step(antTarget, dt, m.w, m.zeta);
    const kinkX = sKinkX.step(kinkXT, dt, m.w, m.zeta), kinkZ = sKinkZ.step(m.kinkZ, dt, m.w, m.zeta);
    const beadScale = Math.max(0.3, sBead.step(m.scale, dt, m.w, m.zeta));
    const roll = sway - (ctx.steerVel || 0) * 0.06;
    if (G) {
      const r = G.rest;
      G.ant0.rotation.set(r.a0x + antX, 0, r.a0z + roll);
      if (G.ant1) G.ant1.rotation.set(r.a1x + kinkX, 0, r.a1z + kinkZ);
      if (G.ant2) G.ant2.rotation.x = r.a2x + kinkX * 0.4;      // the tip carries a little of the kink
      G.ballpiv.scale.setScalar(beadScale);
      seat.position.y = SEAT_Y + (reducedMotion ? 0 : 0.012 * Math.sin(t * (Math.PI * 2) / BREATH_SEC));
      seat.rotation.z = -(ctx.steerVel || 0) * 0.012;           // the steer lean, on the whole body now
    } else {
      antenna.rotation.set(antX, 0, roll);
      kink.rotation.set(kinkX, 0, kinkZ);
      bead.scale.setScalar(beadScale);
    }
    if (poses) poses.update(dt, ctx);        // the body, on top of the mood the antenna just took
    beadTarget.setHex(m.bead); if (m !== MOODS.jackpot) beadTarget.lerp(_c.setHex(PALE), fr);
    const glow = m === MOODS.jackpot ? 1.4 : 0.9;
    for (const bm of G ? G.ballMats : [beadMat]) {
      bm.color.lerp(beadTarget, Math.min(1, dt * 6));
      bm.emissive.copy(bm.color); bm.emissiveIntensity = glow;
      beadLight.color.copy(bm.color);
    }
    if (G) for (const gm of G.glassMats) gm.emissiveIntensity = 1.05 + 0.25 * Math.sin(t * 4);
    else screenMat.opacity = 0.6 + 0.25 * Math.sin(t * 4);

    // the tea swirls, bobs and settles lower at speed; its colour follows the room (fog hue) with a
    // floor on saturation and lightness so it always reads as a liquid, never as a hole in the cup
    tea.rotation.z = Math.PI + Math.sin(t * 1.3) * 0.05;
    tea.position.y = TEA_Y + 0.006 * Math.sin(t * 2.7) - 0.02 * Math.max(0, ctx.speedNorm - 0.65) / 0.35;
    tea.scale.set(1 + 0.02 * Math.sin(t * 1.9), 1 + 0.02 * Math.cos(t * 2.3), 1);
    if (scene.fog && scene.fog.color) {
      scene.fog.color.getHSL(teaHsl);
      teaTarget.setHSL(teaHsl.h, Math.max(0.3, teaHsl.s), 0.42);
      teaMat.color.lerp(teaTarget, Math.min(1, dt * 1.5));
      teaMat.emissive.copy(teaMat.color).multiplyScalar(0.28);
    }

    // landing squash with overshoot (Law XI)
    const sq = sSquash.step(0, dt, 9, 0.45);
    root.scale.set(1 + sq * 0.18, 1 - sq * 0.28, 1 + sq * 0.18);
    saucer.rotation.y += (0.7 + ctx.speedNorm * 2.5) * dt;

    // the steer lean: the cup tips on the dish, the saucer stays flat on the road (see CUP_TIP_MAX
    // above). Turning about the foot and lifting by sin(tip) * CUP_FOOT_R puts the low half of the
    // foot on the dish at every angle, so nothing here can reach the road however hard the turn.
    const tip = CUP_TIP_MAX * Math.tanh((ctx.lean || 0) / CUP_TIP_MAX);
    const ts = Math.sin(tip), tc = Math.cos(tip);
    body.rotation.z = tip;
    body.position.x = (CUP_PIVOT_Y + CUP_SLIDE) * ts;      // hold the foot, then slide it outward
    body.position.y = CUP_PIVOT_Y * (1 - tc) + Math.abs(ts) * CUP_FOOT_R;

    // sweat off the bead and the CRT's top corners; a Bambi-scale anime sweat, not a rain
    if (!reducedMotion) {
      const rate = fr > 0.3 || m === MOODS.fraught ? 4 + 6 * Math.max(fr, m === MOODS.fraught ? 0.5 : 0) : 0;
      sweatAcc += dt * rate;
      while (sweatAcc >= 1) {
        sweatAcc -= 1;
        // off the bead, or off a top corner of the case: the glb's own nodes once it is mounted
        const sd = Math.random() < 0.5 ? -1 : 1;
        if (Math.random() < 0.5) emitFrom(G ? G.ballpiv : bead, 0, 0, 0, ctx, sweat, 0.7, 1, 2.2, 1.4 + Math.random() * 1.2, -0.6);
        else if (G) emitFrom(G.caseNode, sd * CASE_TOP[0], CASE_TOP[1], CASE_TOP[2], ctx, sweat, 0.7, 0.9, 2.6, 1.2 + Math.random(), -0.5);
        else emitFrom(crt, sd * 0.25, 0.21, 0, ctx, sweat, 0.7, 0.9, 2.6, 1.2 + Math.random(), -0.5);
      }
      const drifting = ctx.drift && !ctx.airborne ? 1 : 0;
      sparkAcc += dt * 40 * drifting;
      while (sparkAcc >= 1) {
        sparkAcc -= 1;
        const side = -(ctx.driftSide || 1);
        emitFrom(saucer, side * 0.8, 0.04, -0.5 + Math.random() * 0.3, ctx, sparks, 0.3 + Math.random() * 0.15, 0.6 + Math.random() * 0.8, 2.5,
          0.8 + Math.random() * 2, -(3 + Math.random() * 4));
      }
      if (!drifting) sparkAcc = 0;
    }
    _g.copy(ctx.up).multiplyScalar(-5); sweat.update(dt, _g);
    _g.copy(ctx.up).multiplyScalar(-12); sparks.update(dt, _g);
  }

  function dispose() {
    dead = true;
    // the glb clone shares the cached pack's geometry and textures, so it comes off the graph before
    // the blanket teardown and only the materials this rig made are freed. The pack stays cached.
    if (G) { seat.remove(G.root); body.remove(seat); G = null; }
    if (P) { body.remove(P.cup); saucer.remove(P.dish); P = null; }
    for (const m of owned.splice(0)) m.dispose();
    readyCbs.length = 0;
    scene.remove(sweat.mesh); scene.remove(sparks.mesh);
    for (const o of [group, sweat.mesh, sparks.mesh]) disposeTree(o);
  }

  loadPack(EMI_GLB).then(mountGlb).catch(() => { /* no pack, no swap: the primitive EMI rides on */ });
  loadPack(PROPS_GLB).then(mountProps).catch(() => { /* no pack, no swap: the JS crockery rides on */ });

  return { group, update, setMood, setFraught, squash, dispose, onReady, model, setFace: setFaceFrame, pose };
}
