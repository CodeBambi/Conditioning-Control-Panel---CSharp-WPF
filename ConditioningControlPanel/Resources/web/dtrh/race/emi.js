// race/emi.js - The Caucus Race: the teacup rig with EMI riding in it, seen from behind.
// Visual half of CONTRACT.md "race/kart.js (PR 3)": saucer + cup + handle + tea surface, EMI as a
// CRT body with vents, two glove arms on the rim, a bead antenna with the six mood poses, pooled
// sweat drops, drift sparks, and the tea reflection. Canon lock: her face is ONLY a text emoticon
// drawn on a canvas and mirrored in the tea. No drawn face, no generated face, no mouthed words.
// Ported from the approved pitch demo (C:\wt-race-ref\pitch-demo.html) into ESM; poses now blend
// on a spring (House Book Law XI), never a linear tween, and always return to the breath.

import * as THREE from 'three';

const PINK = 0xff69b4, GOLD = 0xF2C14E, PALE = 0xB3C7FF, PORCELAIN = 0xF6E7C8;
const BREATH_SEC = 3.9;      // the one breath on screen (Law III)
const SWEAT_N = 28, SPARK_N = 32;

/** Antenna poses per mood. antX pitches the whole stem (negative = streamed back), kinkX/Z bend
 *  the joint, wind = how much speed is allowed to stream the stem, w = spring speed (rad/s). */
export const MOODS = {
  calm:     { face: 'o_o', antX: 0,    kinkX: 0,    kinkZ: 0,    bead: PINK, scale: 1,   sway: 1,   wind: 1,   w: 8,  zeta: 0.65 },
  streamed: { face: ':3',  antX: -1.0, kinkX: -0.3, kinkZ: 0,    bead: PINK, scale: 1,   sway: 0,   wind: 1,   w: 14, zeta: 0.7 },
  fraught:  { face: ';_;', antX: 0.25, kinkX: 1.4,  kinkZ: 0,    bead: PALE, scale: 0.9, sway: 0.4, wind: 0.3, w: 10, zeta: 0.6 },
  smug:     { face: '^_^', antX: 0,    kinkX: 0,    kinkZ: 0.55, bead: PINK, scale: 1,   sway: 1,   wind: 0.6, w: 7,  zeta: 0.6 },
  shock:    { face: '>_<', antX: 0.1,  kinkX: 0,    kinkZ: 0,    bead: PINK, scale: 1.6, sway: 0,   wind: 0,   w: 22, zeta: 0.45 },
  jackpot:  { face: '$_$', antX: 0,    kinkX: 0,    kinkZ: 0.2,  bead: GOLD, scale: 1.3, sway: 1,   wind: 0.6, w: 12, zeta: 0.5 },
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
 * createEmiRig({ scene, reducedMotion }) -> { group, update(dt, ctx), setMood, setFraught, squash, dispose }
 * ctx = { t, up, right, tangent, speedNorm, airborne, steerVel, drift, driftSide }  (world-space frame)
 * `group` is the whole rig; the caller positions and orients it. Particles are added to `scene`.
 */
export function createEmiRig({ scene, reducedMotion = false }) {
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
  const handle = new THREE.Mesh(new THREE.TorusGeometry(0.19, 0.045, 10, 24, Math.PI), porcelain); handle.position.set(0.62, 0.42, 0); handle.rotation.z = -Math.PI / 2; body.add(handle);

  // ---- the tea: the only mirror she has. Her face shows in it, reversed. ----
  // The chase camera sits low and ~6 m back, so a flat surface is a sliver. The reflection is the
  // NEAR half of the tea only: it pivots at the near rim and leans up toward EMI (the tea looks up
  // at you), which turns the band between the rim and her body toward the camera. The far half
  // stays flat under her. The emoticon is drawn in that band; canvas bottom = the near (camera) side.
  const TEA_PX = 128, TEA_TILT = 0.5, TEA_R = 0.46;   // rad: near edge at the rim, chord lifted
  const teaCanvas = document.createElement('canvas'); teaCanvas.width = teaCanvas.height = TEA_PX;
  const teaTex = new THREE.CanvasTexture(teaCanvas); teaTex.magFilter = THREE.NearestFilter; teaTex.minFilter = THREE.LinearFilter;
  let faceDrawn = '';
  function drawTea(face) {
    if (face === faceDrawn) return; faceDrawn = face;
    const g = teaCanvas.getContext('2d'); g.setTransform(1, 0, 0, 1, 0, 0);
    const c = TEA_PX / 2;
    g.fillStyle = '#B23282'; g.fillRect(0, 0, TEA_PX, TEA_PX);
    const rg = g.createRadialGradient(c, c * 1.4, 6, c, c * 1.4, c * 1.1); rg.addColorStop(0, 'rgba(255,140,200,.6)'); rg.addColorStop(1, 'rgba(120,20,80,.3)');
    g.fillStyle = rg; g.fillRect(0, 0, TEA_PX, TEA_PX);
    g.translate(TEA_PX, 0); g.scale(-1, 1);                    // mirrored: it is a reflection
    g.font = '700 ' + (face.length > 4 ? 22 : 34) + 'px "Noto Sans Mono", "JetBrains Mono", Consolas, monospace';
    g.textAlign = 'center'; g.textBaseline = 'middle';
    g.lineWidth = 5; g.strokeStyle = 'rgba(70,10,50,.85)'; g.strokeText(face, c, c * 1.44);
    g.fillStyle = '#FFE3F3'; g.fillText(face, c, c * 1.44);
    teaTex.needsUpdate = true;
  }
  drawTea(MOODS.calm.face);
  const teaTilt = new THREE.Group(); teaTilt.position.set(0, 0.69, -TEA_R); teaTilt.rotation.x = -TEA_TILT; body.add(teaTilt);
  // geometry y < 0 is the canvas bottom = the near half (thetaStart PI); the partial circle keeps full-disc UVs
  const reflection = new THREE.Mesh(new THREE.CircleGeometry(TEA_R, 32, Math.PI, Math.PI), new THREE.MeshBasicMaterial({ map: teaTex, side: THREE.DoubleSide }));
  reflection.rotation.set(-Math.PI / 2, 0, Math.PI); reflection.position.set(0, 0, TEA_R); teaTilt.add(reflection);
  const teaMat = new THREE.MeshStandardMaterial({ color: 0xC94A9A, roughness: 0.15, metalness: 0.2, transparent: true, opacity: 0.35 });
  const tea = new THREE.Mesh(new THREE.CircleGeometry(TEA_R + 0.01, 32), teaMat);
  tea.rotation.set(-Math.PI / 2, 0, Math.PI); tea.position.y = 0.66; body.add(tea);

  // ---- EMI: a living pixel CRT seen from behind, gripping the rim ----
  // she sits a little forward in the cup so the near band of tea is wide enough to hold her face
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
  const antenna = new THREE.Group(); antenna.position.y = 0.21; emi.add(antenna);
  const stem = new THREE.Mesh(new THREE.CylinderGeometry(0.018, 0.022, 0.22, 8), stemMat); stem.position.y = 0.11; antenna.add(stem);
  const kink = new THREE.Group(); kink.position.y = 0.22; antenna.add(kink);
  const stem2 = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.018, 0.16, 8), stemMat); stem2.position.y = 0.08; kink.add(stem2);
  const beadMat = new THREE.MeshStandardMaterial({ color: PINK, emissive: PINK, emissiveIntensity: 0.9, roughness: 0.3 });
  const bead = new THREE.Mesh(new THREE.SphereGeometry(0.06, 12, 10), beadMat); bead.position.y = 0.17; kink.add(bead);
  const beadLight = new THREE.PointLight(PINK, 0.5, 2); bead.add(beadLight);
  const cupLight = new THREE.PointLight(PINK, 1.4, 14); cupLight.position.y = 1.2; group.add(cupLight);

  // ---- sweat (pale blue drops) and drift sparks, both world-space pools ----
  const sweat = makePool(SWEAT_N, new THREE.SphereGeometry(0.04, 8, 6), new THREE.MeshBasicMaterial({ color: 0xCFF6FF }));
  const sparks = makePool(SPARK_N, new THREE.BoxGeometry(0.07, 0.07, 0.07), new THREE.MeshBasicMaterial({ color: 0xFFD27A }));
  scene.add(sweat.mesh); scene.add(sparks.mesh);

  // ---- state ----
  let mood = MOODS.calm, fraught = 0, sweatAcc = 0, sparkAcc = 0, blinkAt = 0;
  const sAnt = new Spring(0), sKinkX = new Spring(0), sKinkZ = new Spring(0), sBead = new Spring(1), sSquash = new Spring(0);
  const beadTarget = new THREE.Color(PINK);

  function setMood(id) { mood = MOODS[id] || MOODS.calm; }
  function setFraught(v) { fraught = Math.max(0, Math.min(1, +v || 0)); }
  function squash(amount) { sSquash.x = Math.max(sSquash.x, Math.min(1, amount)); sSquash.v = 0; }

  function emitFrom(obj, lx, ly, lz, ctx, pool, ttl, size, spread, upSpeed, backSpeed) {
    _p.set(lx, ly, lz); obj.localToWorld(_p);
    _g.copy(ctx.up).multiplyScalar(upSpeed).addScaledVector(ctx.right, (Math.random() - 0.5) * spread).addScaledVector(ctx.tangent, backSpeed);
    pool.spawn(_p, _g, ttl, size);
  }

  function update(dt, ctx) {
    dt = Math.min(dt, 0.05);
    const t = ctx.t || 0, m = mood;
    const wind = Math.max(0, Math.min(1, (ctx.speedNorm - 0.55) / 0.45)) * 0.8 + (ctx.airborne ? 0.5 : 0);
    const antTarget = m.antX - Math.min(1, wind) * m.wind + (fraught > 0.4 ? 0.25 * fraught : 0);
    const kinkXT = m.kinkX + 1.7 * fraught * (m === MOODS.fraught ? 0.3 : 1);
    const hush = ctx.airborne || m.sway === 0 ? 0 : m.sway * (1 - 0.6 * fraught);
    const sway = hush * 0.14 * Math.sin(t * (Math.PI * 2) / BREATH_SEC);
    antenna.rotation.set(sAnt.step(antTarget, dt, m.w, m.zeta), 0, sway - (ctx.steerVel || 0) * 0.06);
    kink.rotation.set(sKinkX.step(kinkXT, dt, m.w, m.zeta), 0, sKinkZ.step(m.kinkZ, dt, m.w, m.zeta));
    bead.scale.setScalar(Math.max(0.3, sBead.step(m.scale, dt, m.w, m.zeta)));
    beadTarget.setHex(m.bead); if (m !== MOODS.jackpot) beadTarget.lerp(_c.setHex(PALE), fraught);
    beadMat.color.lerp(beadTarget, Math.min(1, dt * 6)); beadMat.emissive.copy(beadMat.color); beadLight.color.copy(beadMat.color);
    beadMat.emissiveIntensity = m === MOODS.jackpot ? 1.4 : 0.9;
    screenMat.opacity = 0.6 + 0.25 * Math.sin(t * 4);

    // the tea swirls and leans up a touch more the faster the cup goes; the face in it is text and nothing else
    tea.rotation.z = reflection.rotation.z = Math.PI + Math.sin(t * 1.3) * 0.05;
    teaTilt.rotation.x = -(TEA_TILT + 0.1 * Math.max(0, ctx.speedNorm - 0.65) / 0.35);
    let face = fraught > 0.6 && (m === MOODS.calm || m === MOODS.smug) ? MOODS.fraught.face : m.face;
    if (face === MOODS.calm.face) { if (t > blinkAt) blinkAt = t + 5.2; else if (t > blinkAt - 0.11) face = '-_-'; }
    drawTea(face);

    // landing squash with overshoot (Law XI)
    const sq = sSquash.step(0, dt, 9, 0.45);
    root.scale.set(1 + sq * 0.18, 1 - sq * 0.28, 1 + sq * 0.18);
    saucer.rotation.y += (0.7 + ctx.speedNorm * 2.5) * dt;

    // sweat off the bead and the CRT's top corners; a Bambi-scale anime sweat, not a rain
    if (!reducedMotion) {
      const rate = fraught > 0.3 || m === MOODS.fraught ? 4 + 6 * Math.max(fraught, m === MOODS.fraught ? 0.5 : 0) : 0;
      sweatAcc += dt * rate;
      while (sweatAcc >= 1) {
        sweatAcc -= 1;
        const src = Math.random() < 0.5 ? bead : crt, sd = Math.random() < 0.5 ? -1 : 1;
        if (src === bead) emitFrom(bead, 0, 0, 0, ctx, sweat, 0.7, 1, 2.2, 1.4 + Math.random() * 1.2, -0.6);
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
    scene.remove(sweat.mesh); scene.remove(sparks.mesh);
    for (const o of [group, sweat.mesh, sparks.mesh]) o.traverse((n) => {
      if (n.geometry) n.geometry.dispose();
      const mats = Array.isArray(n.material) ? n.material : n.material ? [n.material] : [];
      for (const mt of mats) { if (mt.map) mt.map.dispose(); mt.dispose(); }
    });
  }

  return { group, update, setMood, setFraught, squash, dispose };
}
