import * as THREE from 'three';

// One preallocated draw call, only on a physical beat. No lights or fullscreen layer.
export const CONTACT_CAP = 96;
const DRIFT_COLORS = [0x5BB8FF, 0xFF8A3D, 0xC46BFF].map(hex => new THREE.Color(hex).toArray());
export function createContactFx({ scene, reducedMotion = false }) {
  const pool = Array.from({ length: CONTACT_CAP }, () => ({ life: 0 }));
  const positions = new Float32Array(CONTACT_CAP * 6), colors = new Float32Array(CONTACT_CAP * 6);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  const material = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true,
    blending: THREE.AdditiveBlending, depthWrite: false, fog: true });
  const lines = new THREE.LineSegments(geometry, material);
  lines.name = 'race-contact-sparks'; lines.frustumCulled = false; lines.visible = false;
  scene.add(lines);
  const a = new THREE.Vector3(), b = new THREE.Vector3();
  let cursor = 0, disposed = false;
  function beat(e, k, layout) {
    if (disposed || reducedMotion || !layout || !k || !['driftBoost', 'landing'].includes(e.type)) return;
    const drift = e.type === 'driftBoost', tier = Math.max(1, Math.min(3, e.tier || 1));
    const impact = Math.max(0.25, Math.min(1, e.impact || 0.5));
    const count = drift ? 24 + tier * 12 : Math.round(24 + impact * 40);
    const color = drift ? DRIFT_COLORS[tier - 1]
      : e.clean ? [0.75, 1, 0.75] : [1, 0.34, 0.42];
    for (let i = 0; i < count; i++) {
      const p = pool[cursor++ % CONTACT_CAP], side = i % 2 ? 1 : -1;
      p.d = k.d; p.x = k.x + side * 0.7; p.h = (layout.surfaceH ? layout.surfaceH(k.d) : k.h || 0) + 0.1;
      p.vd = k.speed * (drift ? 0.65 : 0.75) - Math.random() * 3;
      p.vx = side * (1.2 + Math.random() * (drift ? 2 + tier : 2 + impact * 3));
      p.vh = 0.8 + Math.random() * (drift ? 1.8 : 1.1 + impact * 2);
      p.life = p.maxLife = 0.3 + Math.random() * 0.3; p.color = color;
    }
  }
  function update(dt, layout) {
    if (disposed || !layout || !(dt > 0)) return;
    let n = 0;
    for (const p of pool) {
      if (p.life <= 0) continue;
      p.life -= dt; if (p.life <= 0) continue;
      p.d = layout.wrap(p.d + p.vd * dt); p.x += p.vx * dt;
      p.vh -= 7 * dt; p.h += p.vh * dt;
      const floor = (layout.surfaceH ? layout.surfaceH(p.d) : 0) + 0.04;
      if (p.h < floor) { p.life = 0; continue; }
      layout.toWorld(p.d, p.x, p.h, a);
      layout.toWorld(layout.wrap(p.d - p.vd * 0.018), p.x - p.vx * 0.035, Math.max(floor, p.h - p.vh * 0.035), b);
      const at = n++ * 6, fade = Math.pow(p.life / p.maxLife, 1.5);
      positions[at]=a.x; positions[at+1]=a.y; positions[at+2]=a.z;
      positions[at+3]=b.x; positions[at+4]=b.y; positions[at+5]=b.z;
      for (let c = 0; c < 3; c++) { colors[at+c] = p.color[c] * fade; colors[at+c+3] = p.color[c] * fade * 0.12; }
    }
    geometry.setDrawRange(0, n * 2); lines.visible = n > 0;
    if (n) { geometry.attributes.position.needsUpdate = true; geometry.attributes.color.needsUpdate = true; }
  }
  function clear() { for (const p of pool) p.life = 0; lines.visible = false; geometry.setDrawRange(0, 0); }
  function dispose() { if (disposed) return; disposed = true; clear(); scene.remove(lines); geometry.dispose(); material.dispose(); }
  return { beat, update, clear, dispose };
}
