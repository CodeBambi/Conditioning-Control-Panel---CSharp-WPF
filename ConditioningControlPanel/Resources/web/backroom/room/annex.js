/* ============================================================================
 * backroom/room/annex.js - THE ANNEX, the second room (2026-09-18, tester ask:
 * "turn this into a hub for all the 3D bits, starting with the racing game").
 *
 * A doorway cut through the WEST wall (the wall with the fewest fixtures, see
 * walk.js) leads to a small procedural room: floor in the casino's own spiral
 * material, plum walls, brass jambs, neon skirting, two of the room's point
 * lights. Three doors: "Racing Thoughts" is LIVE (E hands the row to main.js,
 * which opens the same race-portal.js door as the cabinet); the other two are
 * LOCKED behind a padlock and a "Coming soon" plaque, and only rattle.
 *
 * The cut is a clip box on the shell's wall materials (local clipping,
 * clipIntersection), so the GLB is never edited. Boxes and one canvas sign per
 * plaque, no new assets. Geometry numbers match walk.js (ANNEX, DOORWAY): the
 * inner faces sit one body radius outside the walkable rectangles.
 * ==========================================================================*/
import * as T from 'three';
import { ANNEX_PORTALS } from './walk.js';

const PALETTE = { pink: 0xff65bf, lavender: 0xb18aff, mint: 0x67ffe0, gold: 0xffcb73 };
const DOOR_W = 1.1, DOOR_H = 2.1;

/** One canvas plaque; the text is drawn once, the mesh is a lit sign (toneMapped off, like the venue bulbs). */
function makeSign(text, { width = 1.2, height = 0.32, ink = '#ffd6ef', glow = '#ff65bf', dim = false } = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = 512; canvas.height = Math.round(512 * height / width);
  const g = canvas.getContext('2d');
  g.fillStyle = dim ? '#1a1024' : '#24142f'; g.fillRect(0, 0, canvas.width, canvas.height);
  g.strokeStyle = dim ? '#5a4a6a' : '#bd8b50'; g.lineWidth = 8; g.strokeRect(6, 6, canvas.width - 12, canvas.height - 12);
  g.font = `bold ${Math.round(canvas.height * 0.44)}px "Segoe UI", system-ui, sans-serif`;
  g.textAlign = 'center'; g.textBaseline = 'middle';
  g.shadowColor = glow; g.shadowBlur = dim ? 4 : 18;
  g.fillStyle = ink; g.fillText(text, canvas.width / 2, canvas.height / 2 + 2, canvas.width - 40);
  const texture = new T.CanvasTexture(canvas); texture.colorSpace = T.SRGBColorSpace;
  const mesh = new T.Mesh(new T.PlaneGeometry(width, height), new T.MeshBasicMaterial({ map: texture, toneMapped: false, side: T.DoubleSide }));
  return mesh;
}

export function createAnnex({ scene, room, renderer, lex = (k, f) => f } = {}) {
  const group = new T.Group(); group.name = 'annex';
  const own = { geometries: new Set(), materials: new Set(), textures: new Set() };
  const wallMat = new T.MeshStandardMaterial({ color: 0x2a1638, roughness: 0.82, metalness: 0.05 });
  const ceilingMat = new T.MeshStandardMaterial({ color: 0x1b1026, roughness: 0.9 });
  const brass = new T.MeshStandardMaterial({ color: 0xbd8b50, metalness: 0.7, roughness: 0.35 });
  const lacquer = new T.MeshStandardMaterial({ color: 0x251334, metalness: 0.25, roughness: 0.4 });
  const neon = (hex) => { const m = new T.MeshBasicMaterial({ color: hex, toneMapped: false }); own.materials.add(m); return m; };
  for (const m of [wallMat, ceilingMat, brass, lacquer]) own.materials.add(m);
  const box = (w, h, d, mat, x, y, z, name) => {
    const g = new T.BoxGeometry(w, h, d); own.geometries.add(g);
    const mesh = new T.Mesh(g, mat); mesh.position.set(x, y, z); if (name) mesh.name = name; group.add(mesh); return mesh;
  };

  // ---- the shell: x -12.55..-7.55 (inner), z -1.5..3.5, 3.2 m ceiling; the east wall carries the doorway z 0.1..1.9, 2.05 tall
  const H = 3.2, CX = -10.05, CZ = 1;
  box(0.3, H, 5.6, wallMat, -12.7, H / 2, CZ, 'annex_wall_west');
  box(5.3, H, 0.3, wallMat, CX, H / 2, -1.65, 'annex_wall_north');
  box(5.3, H, 0.3, wallMat, CX, H / 2, 3.65, 'annex_wall_south');
  box(0.3, H, 1.9, wallMat, -7.4, H / 2, -0.85, 'annex_wall_east_a');
  box(0.3, H, 1.9, wallMat, -7.4, H / 2, 2.85, 'annex_wall_east_b');
  box(0.3, H - 2.05, 1.8, wallMat, -7.4, 2.05 + (H - 2.05) / 2, CZ, 'annex_wall_east_lintel');
  // The doorway lining runs the wall's depth (the shell's wall plus ours) and a brass trim faces the casino.
  box(0.72, 2.05, 0.06, lacquer, -7.195, 1.025, 0.13, 'annex_jamb_a');
  box(0.72, 2.05, 0.06, lacquer, -7.195, 1.025, 1.87, 'annex_jamb_b');
  box(0.72, 0.06, 1.8, lacquer, -7.195, 2.02, CZ, 'annex_jamb_top');
  box(0.08, 2.1, 0.12, brass, -6.84, 1.05, 0.06);
  box(0.08, 2.1, 0.12, brass, -6.84, 1.05, 1.94);
  box(0.08, 0.1, 1.96, brass, -6.84, 2.05, CZ);
  // Floor: the casino's own spiral shader, its pattern centred on this room; it meets the main floor at x -7.
  const floorGeo = new T.PlaneGeometry(5.9, 5.4); own.geometries.add(floorGeo);
  const floor = new T.Mesh(floorGeo, room?.floor?.material || wallMat);
  floor.rotation.x = -Math.PI / 2; floor.position.set(-9.95, 0.012, CZ); floor.name = 'annex_floor'; group.add(floor);
  const ceilGeo = new T.PlaneGeometry(5.9, 5.4); own.geometries.add(ceilGeo);
  const ceiling = new T.Mesh(ceilGeo, ceilingMat); ceiling.rotation.x = Math.PI / 2; ceiling.position.set(-9.95, H, CZ); ceiling.name = 'annex_ceiling'; group.add(ceiling);
  // Neon skirting and a cove line, the venue palette.
  const skirtPink = neon(PALETTE.pink), skirtLav = neon(PALETTE.lavender);
  box(0.03, 0.03, 5.0, skirtPink, -12.53, 0.06, CZ); box(5.0, 0.03, 0.03, skirtLav, CX, 0.06, -1.48); box(5.0, 0.03, 0.03, skirtLav, CX, 0.06, 3.48);
  box(0.03, 0.03, 5.0, skirtLav, -12.53, H - 0.06, CZ); box(5.0, 0.03, 0.03, skirtPink, CX, H - 0.06, -1.48); box(5.0, 0.03, 0.03, skirtPink, CX, H - 0.06, 3.48);
  // Lights: the same rig as scene.js's point() helper.
  const lamps = [];
  const lamp = (hex, p, at, d) => { const l = new T.PointLight(hex, p, d, 2); l.position.set(...at); group.add(l); lamps.push(l); return l; };
  lamp(PALETTE.lavender, 9, [CX, 2.9, CZ], 7);
  const doorLamp = lamp(PALETTE.pink, 7, [-11.9, 2.4, CZ], 4.5);
  // The header sign: a blade sign in the casino beside the doorway, on a brass bracket, readable from both sides.
  const header = makeSign(lex('br_annex_sign', 'The Annex'), { width: 1.4, height: 0.38 });
  header.position.set(-6.15, 2.65, CZ); header.name = 'annex_header_sign'; group.add(header);
  own.geometries.add(header.geometry); own.materials.add(header.material); own.textures.add(header.material.map);
  box(0.75, 0.04, 0.04, brass, -6.52, 2.9, CZ);
  box(0.04, 0.08, 0.04, brass, -6.15, 2.88, CZ);

  // ---- the doors: a group per portal row, local +z faces the room
  const doors = new Map(), locks = [];
  const YAW = { west: Math.PI / 2, north: 0, south: Math.PI };
  const FACE = { west: -12.55, north: -1.5, south: 3.5 };
  for (const row of ANNEX_PORTALS) {
    const d = new T.Group(); d.name = 'annex_door_' + row.key;
    const [ax, , az] = row.approach;
    d.position.set(row.wall === 'west' ? FACE.west : ax, 0, row.wall === 'west' ? az : FACE[row.wall]);
    d.rotation.y = YAW[row.wall];
    const part = (w, h, dp, mat, x, y, z) => { const g = new T.BoxGeometry(w, h, dp); own.geometries.add(g); const m = new T.Mesh(g, mat); m.position.set(x, y, z); d.add(m); return m; };
    part(DOOR_W, DOOR_H, 0.08, lacquer, 0, DOOR_H / 2, 0.04);
    part(0.1, DOOR_H + 0.1, 0.12, brass, -(DOOR_W / 2 + 0.05), (DOOR_H + 0.1) / 2, 0.06);
    part(0.1, DOOR_H + 0.1, 0.12, brass, DOOR_W / 2 + 0.05, (DOOR_H + 0.1) / 2, 0.06);
    part(DOOR_W + 0.3, 0.1, 0.12, brass, 0, DOOR_H + 0.15, 0.06);
    const live = row.portal === 'race';
    const plaque = makeSign(live ? lex(row.labelKey, row.name) : lex('br_annex_locked', 'Coming soon'),
      live ? { width: 1.2, height: 0.32 } : { width: 1.0, height: 0.26, ink: '#c9bce0', glow: '#b18aff', dim: true });
    plaque.position.set(0, DOOR_H + 0.45, 0.03); d.add(plaque);
    own.geometries.add(plaque.geometry); own.materials.add(plaque.material); own.textures.add(plaque.material.map);
    if (live) {
      const knob = new T.Mesh(new T.SphereGeometry(0.045, 12, 8), brass); own.geometries.add(knob.geometry);
      knob.position.set(DOOR_W / 2 - 0.12, 1.05, 0.1); d.add(knob);
      // A thin lit strip under the door: the race is on the other side.
      part(DOOR_W, 0.02, 0.02, neon(PALETTE.mint), 0, 0.01, 0.09);
    } else {
      const lock = new T.Group(); lock.position.set(DOOR_W / 2 - 0.16, 1.05, 0.12);
      const body = new T.Mesh(new T.BoxGeometry(0.16, 0.14, 0.05), brass); own.geometries.add(body.geometry); lock.add(body);
      const shackle = new T.Mesh(new T.TorusGeometry(0.055, 0.014, 8, 16, Math.PI), brass); own.geometries.add(shackle.geometry);
      shackle.position.y = 0.07; lock.add(shackle);
      const hasp = new T.Mesh(new T.BoxGeometry(0.22, 0.06, 0.02), lacquer); own.geometries.add(hasp.geometry); hasp.position.set(-0.1, 0, -0.03); lock.add(hasp);
      d.add(lock);
      locks.push({ key: row.key, lock, plaque, t: 0 });
    }
    group.add(d); doors.set(row.key, d);
  }
  scene.add(group);

  // ---- the cut: clip the shell's wall materials inside the doorway box (x -7.7..-6.84, y < 2.05, z 0.1..1.9)
  const planes = [
    new T.Plane(new T.Vector3(-1, 0, 0), -7.7), new T.Plane(new T.Vector3(1, 0, 0), 6.84),
    new T.Plane(new T.Vector3(0, 0, -1), 0.1), new T.Plane(new T.Vector3(0, 0, 1), -1.9),
    new T.Plane(new T.Vector3(0, 1, 0), -2.05), new T.Plane(new T.Vector3(0, -1, 0), 0.1),
  ];
  const cut = new T.Box3(new T.Vector3(-7.7, -0.1, 0.1), new T.Vector3(-6.84, 2.05, 1.9)), probe = new T.Box3();
  const clipped = [];
  if (renderer && room?.shell) {
    renderer.localClippingEnabled = true;
    room.shell.traverse((n) => {
      if (!n.isMesh || !n.geometry) return;
      if (!n.geometry.boundingBox) n.geometry.computeBoundingBox();
      probe.copy(n.geometry.boundingBox).applyMatrix4(n.matrixWorld);
      if (!probe.intersectsBox(cut)) return;
      for (const m of [].concat(n.material)) {
        if (!m || clipped.includes(m)) continue;
        m.clippingPlanes = planes; m.clipIntersection = true; m.needsUpdate = true; clipped.push(m);
      }
    });
  }

  let time = 0;
  return {
    group, rows: ANNEX_PORTALS, ceiling, doors,
    fixtureOf(key) { return doors.get(key) || null; },
    /** E at a locked door: the padlock rattles and the plaque blinks for a moment. No HUD line needed. */
    refuse(key) { const l = locks.find((x) => x.key === key); if (!l) return false; l.t = 0.7; return true; },
    update(dt, still, overview = false) {
      const step = Math.min(0.05, Math.max(0, dt || 0));
      ceiling.visible = !overview;
      if (!still) { time += step; doorLamp.intensity = 7 + Math.sin(time * 2.1) * 0.9; }
      for (const l of locks) {
        if (l.t <= 0) continue;
        l.t = Math.max(0, l.t - step);
        const k = l.t / 0.7;
        l.lock.rotation.z = Math.sin(l.t * 42) * 0.3 * k;
        l.plaque.material.color.setScalar(1 + 0.9 * k * (0.5 + 0.5 * Math.sin(l.t * 30)));
        if (l.t === 0) { l.lock.rotation.z = 0; l.plaque.material.color.setScalar(1); }
      }
    },
    debug() { return { doors: [...doors.keys()], clipped: clipped.length, rattling: locks.filter((l) => l.t > 0).map((l) => l.key) }; },
    dispose() {
      for (const m of clipped) { m.clippingPlanes = null; m.clipIntersection = false; m.needsUpdate = true; }
      group.removeFromParent();
      own.textures.forEach((t) => t.dispose()); own.materials.forEach((m) => m.dispose()); own.geometries.forEach((g) => g.dispose());
    },
  };
}
