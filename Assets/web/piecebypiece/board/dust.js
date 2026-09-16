/* ============================================================================
 * board/dust.js - what the board does when a man lands on it.
 *
 * A landing is the beat the player performs forty times a game, so it pays out
 * every time, and cheaply: a ring of soft cream-and-pink motes puffs outward
 * along the board from the man's base and fades in half a second, the square
 * under him flashes pink for a few frames, and a faint ripple ring spreads out
 * and dies. A capture makes it all bigger. A king landing throws a few gold
 * sparks off his crown.
 *
 * All the motes live in ONE THREE.Points with a ring buffer of slots; each
 * burst writes its slots and the vertex shader animates them from their spawn
 * time, so a burst costs one attribute upload and the whole system is one
 * draw call. Flashes and rings are tiny pooled meshes.
 *
 * Wiring:
 *   boot.js  createDust({ scene, bus }) and update(dt, camera, renderer) once
 *            a frame; it listens on the bus for anim.js's `land`, and for
 *            its `hit` (a whip crossing a man: a short puff off the victim,
 *            no flash and no ring, the landing on the square pays those)
 *   settings window.PBP.settings.reducedMotion (or the media query) makes it
 *            smaller and shorter; a refused drop's landing puffs less
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number that decides how a landing looks. */
export const TUNING = Object.freeze({
  pool: 256,               // motes alive at once; a burst past this recycles the oldest
  count: 14,               // motes per landing
  countReduced: 6,
  life: 0.5,               // seconds a mote lives
  lifeReduced: 0.25,
  spread: 0.5,             // world units the ring travels, times the man's height
  rise: 0.07,              // how far a mote lifts before it settles
  size: 0.15,              // world size of a mote at a pawn's height
  captureGain: 1.6,        // taking a man: bigger and wider
  refusedGain: 0.45,       // a spring-back landing: a shrug of dust
  hitGain: 0.8,            // a whip crack: knocked dust, no ring
  colorCream: 0xFFF1E0,
  colorPink: 0xFFB0D8,
  flashSec: 0.14,
  flashAlpha: 0.6,
  flashColor: 0xFF8FCB,
  ringSec: 0.42,
  ringFrom: 0.35,          // ring scale at birth, in squares
  ringTo: 0.9,
  ringAlpha: 0.5,
  ringColor: 0xFFC2E4,
  sparks: 8,               // the king's crown
  sparkLife: 0.45,
  sparkColor: 0xFFE39A,
  sparkRise: 0.35,
  flashPool: 4,
  ringPool: 4,
});

const T = TUNING;

const VERT = `
attribute vec3 aDir;
attribute float aSpawn;
attribute float aLife;
attribute float aSize;
attribute float aRise;
attribute vec3 aColor;
uniform float uTime;
uniform float uScale;
varying float vAlpha;
varying vec3 vColor;
void main() {
  float age = (uTime - aSpawn) / max(aLife, 0.001);
  vColor = aColor;
  if (age < 0.0 || age > 1.0) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); gl_PointSize = 0.0; vAlpha = 0.0; return; }
  float reach = 1.0 - pow(1.0 - age, 2.2);
  vec3 p = position + aDir * reach;
  p.y += aRise * sin(3.14159 * min(age * 1.6, 1.0)) * (1.0 - age * 0.5);
  vec4 mv = modelViewMatrix * vec4(p, 1.0);
  float grow = 0.6 + 0.8 * reach;
  gl_PointSize = aSize * grow * uScale / max(-mv.z, 0.05);
  vAlpha = pow(1.0 - age, 1.4) * 0.85;
  gl_Position = projectionMatrix * mv;
}`;

const FRAG = `
varying float vAlpha;
varying vec3 vColor;
void main() {
  float d = length(gl_PointCoord - 0.5) * 2.0;
  float a = smoothstep(1.0, 0.3, d) * vAlpha;
  if (a < 0.01) discard;
  gl_FragColor = vec4(vColor, a);
}`;

function reduced() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if ((s && s.reducedMotion) || (window.PBP && window.PBP.reducedMotion)) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

export function createDust({ scene, bus = null }) {
  let clock = 0;
  let cursor = 0;
  let bursts = 0;
  const cream = new THREE.Color(T.colorCream);
  const pink = new THREE.Color(T.colorPink);
  const tmp = new THREE.Color();

  // --- the motes ------------------------------------------------------------
  const geo = new THREE.BufferGeometry();
  const N = T.pool;
  const pos = new Float32Array(N * 3);
  const dir = new Float32Array(N * 3);
  const spawn = new Float32Array(N).fill(-1e9);
  const life = new Float32Array(N).fill(1);
  const size = new Float32Array(N);
  const rise = new Float32Array(N);
  const color = new Float32Array(N * 3);
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('aDir', new THREE.BufferAttribute(dir, 3));
  geo.setAttribute('aSpawn', new THREE.BufferAttribute(spawn, 1));
  geo.setAttribute('aLife', new THREE.BufferAttribute(life, 1));
  geo.setAttribute('aSize', new THREE.BufferAttribute(size, 1));
  geo.setAttribute('aRise', new THREE.BufferAttribute(rise, 1));
  geo.setAttribute('aColor', new THREE.BufferAttribute(color, 3));
  geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0.5, 0), 8);   // never culled
  const uniforms = { uTime: { value: 0 }, uScale: { value: 400 } };
  const motes = new THREE.Points(geo, new THREE.ShaderMaterial({
    uniforms, vertexShader: VERT, fragmentShader: FRAG,
    transparent: true, depthWrite: false, depthTest: true,
  }));
  motes.frustumCulled = false;
  motes.renderOrder = 5;
  motes.raycast = () => {};
  scene.add(motes);

  /** Write one mote into the next slot. */
  function mote(x, y, z, dx, dy, dz, sz, up, lifeSec, col) {
    const i = cursor++ % N;
    pos[i * 3] = x; pos[i * 3 + 1] = y; pos[i * 3 + 2] = z;
    dir[i * 3] = dx; dir[i * 3 + 1] = dy; dir[i * 3 + 2] = dz;
    spawn[i] = clock;
    life[i] = lifeSec;
    size[i] = sz;
    rise[i] = up;
    color[i * 3] = col.r; color[i * 3 + 1] = col.g; color[i * 3 + 2] = col.b;
  }

  function upload() {
    for (const a of Object.values(geo.attributes)) a.needsUpdate = true;
  }

  // --- flashes and rings ----------------------------------------------------
  const flat = (geometry, hex, y) => {
    const m = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({
      color: hex, transparent: true, opacity: 0, depthWrite: false, fog: false, toneMapped: false,
    }));
    m.rotation.x = -Math.PI / 2;
    m.position.y = y;
    m.visible = false;
    m.raycast = () => {};
    m.renderOrder = 4;
    scene.add(m);
    return m;
  };
  const flashGeo = new THREE.PlaneGeometry(0.96, 0.96);
  const ringGeo = new THREE.RingGeometry(0.42, 0.5, 48);
  const flashes = Array.from({ length: T.flashPool }, () => ({ mesh: flat(flashGeo, T.flashColor, 0.004), t: 1e9, alpha: 0 }));
  const rings = Array.from({ length: T.ringPool }, () => ({ mesh: flat(ringGeo, T.ringColor, 0.005), t: 1e9, gain: 1 }));
  let fCursor = 0;
  let rCursor = 0;

  function flash(x, z, alpha) {
    const f = flashes[fCursor++ % flashes.length];
    f.mesh.position.set(x, 0.004, z);
    f.mesh.visible = true;
    f.t = 0; f.alpha = alpha;
  }

  function ring(x, z, gain) {
    const r = rings[rCursor++ % rings.length];
    r.mesh.position.set(x, 0.005, z);
    r.mesh.visible = true;
    r.t = 0; r.gain = gain;
  }

  // --- the landing ----------------------------------------------------------
  /**
   * A man has touched down. `at` is world x/y/z of his base, `height` his
   * height in squares, `kind` one of 'move' | 'capture' | 'refused'.
   */
  function puff(at, height = 1, kind = 'move', type = null) {
    const low = reduced();
    const gain = kind === 'capture' ? T.captureGain : kind === 'refused' ? T.refusedGain : kind === 'hit' ? T.hitGain : 1;
    const h = Math.max(0.4, Math.min(1.6, height));
    const n = Math.round((low ? T.countReduced : T.count) * (kind === 'capture' ? 1.4 : 1));
    const lifeSec = low ? T.lifeReduced : T.life;
    const reach = T.spread * h * gain * (low ? 0.6 : 1);
    for (let i = 0; i < n; i++) {
      const a = (i / n) * Math.PI * 2 + Math.random() * 0.5;
      const d = reach * (0.7 + Math.random() * 0.6);
      tmp.copy(cream).lerp(pink, Math.random());
      mote(at.x + Math.cos(a) * 0.12, at.y + 0.03, at.z + Math.sin(a) * 0.12,
        Math.cos(a) * d, 0, Math.sin(a) * d,
        T.size * (0.7 + Math.random() * 0.6) * Math.sqrt(h) * gain, T.rise * (0.5 + Math.random()),
        lifeSec * (0.8 + Math.random() * 0.4), tmp);
    }
    if (type === 'k' && !low) {
      tmp.setHex(T.sparkColor);
      for (let i = 0; i < T.sparks; i++) {
        const a = Math.random() * Math.PI * 2;
        const d = 0.08 + Math.random() * 0.12;
        mote(at.x, at.y + h * 0.96, at.z, Math.cos(a) * d, 0.1, Math.sin(a) * d,
          0.06 + Math.random() * 0.04, T.sparkRise * (0.6 + Math.random() * 0.6), T.sparkLife, tmp);
      }
    }
    upload();
    if (kind !== 'hit') flash(at.x, at.z, T.flashAlpha * (low ? 0.5 : 1) * (kind === 'refused' ? 0.5 : 1));
    if (!low && kind !== 'refused' && kind !== 'hit') ring(at.x, at.z, gain);
    bursts++;
  }

  let unsub = null;
  if (bus && typeof bus.on === 'function') {
    unsub = bus.on('land', (p) => {
      if (!p || !p.world) return;
      puff(p.world, p.height, p.capture ? 'capture' : p.refused ? 'refused' : 'move', p.piece);
    });
    const unsubHit = bus.on('hit', (p) => {
      if (!p || !p.world) return;
      puff(p.world, p.height, 'hit', p.victim);
    });
    const unsubLand = unsub;
    unsub = () => { unsubLand(); unsubHit(); };
  }

  function update(dt, camera, renderer) {
    clock += dt;
    uniforms.uTime.value = clock;
    if (camera && renderer) {
      const hpx = renderer.domElement.height || 1;
      uniforms.uScale.value = (hpx * (camera.projectionMatrix.elements[5] || 1)) / 2;
    }
    for (const f of flashes) {
      if (!f.mesh.visible) continue;
      f.t += dt;
      const p = f.t / T.flashSec;
      if (p >= 1) { f.mesh.visible = false; continue; }
      f.mesh.material.opacity = f.alpha * (1 - p);
    }
    for (const r of rings) {
      if (!r.mesh.visible) continue;
      r.t += dt;
      const p = r.t / T.ringSec;
      if (p >= 1) { r.mesh.visible = false; continue; }
      const s = THREE.MathUtils.lerp(T.ringFrom, T.ringTo * r.gain, 1 - Math.pow(1 - p, 2));
      r.mesh.scale.setScalar(s * 2);   // the ring geometry is half a square across
      r.mesh.material.opacity = T.ringAlpha * (1 - p);
    }
  }

  return {
    update, puff,
    /** For the harness: how many bursts fired and how many motes are alive. */
    stats() {
      let alive = 0;
      for (let i = 0; i < N; i++) if (clock - spawn[i] >= 0 && clock - spawn[i] <= life[i]) alive++;
      return { bursts, alive, flashes: flashes.filter((f) => f.mesh.visible).length, rings: rings.filter((r) => r.mesh.visible).length, reduced: reduced(), clock };
    },
    dispose() {
      if (unsub) unsub();
      scene.remove(motes);
      geo.dispose();
      motes.material.dispose();
      for (const f of flashes) { scene.remove(f.mesh); f.mesh.material.dispose(); }
      for (const r of rings) { scene.remove(r.mesh); r.mesh.material.dispose(); }
      flashGeo.dispose(); ringGeo.dispose();
    },
  };
}
