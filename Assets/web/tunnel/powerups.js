/* ============================================================================
 * powerups.js — clickable 3D pickups (infrastructure + demo).
 *
 * This is the wiring the game will hang real power-ups on later. Right now it:
 *   - spawns a glowing orb on the tube wall a little ahead of the camera,
 *   - streams it toward the camera (it rides a fixed arc length along the rail),
 *   - raycasts pointer clicks against live orbs,
 *   - on a hit: pops it, plays the collect SFX, and posts {powerup-click,id}
 *     to the C# host,
 *   - despawns orbs once they pass behind the camera.
 *
 * The host asks for one with a `spawn-powerup` message (verification), and the
 * page auto-spawns a demo every few seconds when opened with ?demo. Real
 * gameplay effects are a later phase — the host just logs the click for now.
 * ==========================================================================*/
import * as THREE from 'three';

export class Powerups {
  constructor(scene, camera, tunnel, host) {
    this.scene = scene;
    this.cam = camera;
    this.tunnel = tunnel;
    this.host = host;                 // { post(type, data), sfx(name, scale) }
    this.items = [];                  // [{ mesh, s, id, born }]
    this._ray = new THREE.Raycaster();
    this._ndc = new THREE.Vector2();
    this._id = 0;
    this._geo = new THREE.IcosahedronGeometry(0.9, 1);
    this._frame = { pos: new THREE.Vector3(), tan: new THREE.Vector3(), nor: new THREE.Vector3(), bin: new THREE.Vector3() };
  }

  /** Spawn an orb `ahead` units down-tube, offset toward one wall. */
  spawn(id, ahead = 90) {
    const s = (this._railS || 0) + ahead;
    const mat = new THREE.MeshBasicMaterial({ color: 0x8affff, transparent: true, opacity: 0.95 });
    const mesh = new THREE.Mesh(this._geo, mat);
    // soft additive halo
    const halo = new THREE.Mesh(this._geo, new THREE.MeshBasicMaterial({
      color: 0x59d0ff, transparent: true, opacity: 0.35, blending: THREE.AdditiveBlending, depthWrite: false,
    }));
    halo.scale.setScalar(2.1);
    mesh.add(halo);
    mesh.frustumCulled = false;
    mesh.userData.pickable = true;
    this.scene.add(mesh);
    const item = { mesh, s, id: id || `pw${++this._id}`, born: 0, ang: Math.random() * Math.PI * 2, wall: 0.55 };
    this.items.push(item);
    if (this.host) this.host.sfx('tunnel_powerup_spawn', 0.6);
    return item;
  }

  /** Raycast a click (clientX/Y in CSS px) against live orbs. */
  click(clientX, clientY, w, h) {
    if (!this.items.length) return;
    this._ndc.set((clientX / w) * 2 - 1, -(clientY / h) * 2 + 1);
    this._ray.setFromCamera(this._ndc, this.cam);
    const meshes = this.items.map((it) => it.mesh);
    const hits = this._ray.intersectObjects(meshes, false);
    if (!hits.length) return;
    const hit = hits[0].object;
    const idx = this.items.findIndex((it) => it.mesh === hit);
    if (idx < 0) return;
    const it = this.items[idx];
    if (this.host) { this.host.post('powerup-click', { id: it.id }); this.host.sfx('tunnel_powerup_collect', 0.7); }
    this._despawn(idx);
  }

  _despawn(idx) {
    const it = this.items[idx];
    this.scene.remove(it.mesh);
    it.mesh.material.dispose();
    if (it.mesh.children[0]) it.mesh.children[0].material.dispose();
    this.items.splice(idx, 1);
  }

  update(railS, dt) {
    this._railS = railS;
    for (let i = this.items.length - 1; i >= 0; i--) {
      const it = this.items[i];
      it.born += dt;
      if (it.s < railS - 12) { this._despawn(i); continue; }   // passed the camera
      const f = this.tunnel.frameAt(it.s, this._frame);
      it.mesh.position.copy(f.pos)
        .addScaledVector(f.nor, Math.cos(it.ang) * this.tunnel.radius * it.wall)
        .addScaledVector(f.bin, Math.sin(it.ang) * this.tunnel.radius * it.wall);
      it.mesh.rotation.y += dt * 1.6;
      it.mesh.rotation.x += dt * 0.9;
      const pulse = 1 + 0.12 * Math.sin(it.born * 5);
      it.mesh.scale.setScalar(pulse);
    }
  }
}
