import * as T from 'three';
import { sampleLanding } from './prop-landing.js';

/* ============================================================================
 * backroom/room/customization-props.js - the six Room Service props that had no
 * room spot: three plants and three wall frames.
 *
 * Each one is a fixed placement, off until owned and explicitly enabled from
 * the Room Service panel's own row of items. Nothing here persists and nothing
 * here talks to the bridge: the room is the preview (CONTRACT 10.13.7).
 *
 * DRAW CALLS: the authored planters carry one node per leaf, so a prop is baked
 * down to one mesh per material the moment it is placed. The monstera's 47 mesh
 * nodes become six draws, the ivy's 85 become six. Media planes are left alone:
 * they keep their own mesh, their UVs and their place in the screen feed.
 * ==========================================================================*/

/** Front is +z on every authored prop, so `yaw` turns that front into the room.
 *  `camYaw` is the side the catalogue close-up flies to, which is not always the
 *  front: a planter's mass leans off its own origin. */
const PROPS = Object.freeze([
  { key: 'monstera', file: 'monstera', node: 'prop_monstera',
    position: [-2.35, 0.02, 7.18], yaw: 0, camYaw: Math.PI,
    spot: 'Entrance wall, left of the runner' },
  { key: 'ivy', file: 'hanging_ivy', node: 'prop_hanging_ivy',
    position: [-5.90, 2.95, -6.55], yaw: 0.5, camYaw: Math.PI / 4, hanger: 4.40,
    spot: 'Northwest corner, hung from the ceiling' },
  { key: 'terrarium', file: 'terrarium', node: 'prop_terrarium',
    position: [-3.75, 0.42, -4.90], yaw: -0.6, camYaw: Math.PI / 4, plinth: 0.42,
    spot: 'West flank of the Prize Parlour, on a brass plinth' },
  { key: 'gallery', file: 'gallery-landscape', node: 'prop_gallery_landscape',
    position: [-6.86, 2.72, 4.10], yaw: Math.PI / 2, camYaw: Math.PI / 2,
    screens: [['screen_surface', 16 / 9]],
    spot: 'Left wall, above the Candy Rose and Candy Violet booths' },
  { key: 'portraits', file: 'portrait-pair', node: 'prop_portrait_pair',
    position: [-6.86, 2.72, 5.80], yaw: Math.PI / 2, camYaw: Math.PI / 2,
    screens: [['screen_surface_1', 3 / 4], ['screen_surface_2', 3 / 4]],
    spot: 'Left wall, above the Candy Violet and Candy Mint booths' },
  { key: 'billboard', file: 'deco-billboard', node: 'prop_deco_billboard',
    position: [0, 3.10, 7.72], yaw: Math.PI, camYaw: Math.PI,
    screens: [['screen_surface', 21 / 9]],
    /* Not the back wall: the Prize Parlour's own marquee stands across that whole span. */
    spot: 'Entrance wall, over the door, facing the room' },
]);

const round = (n) => Math.round(n * 1000) / 1000;

/**
 * One mesh per material, in the model's own space. The authored nodes that went
 * into a batch are dropped; a media plane is kept exactly as authored.
 * @returns {{meshes: T.Mesh[], triangles: number}}
 */
function bakeStatic(model) {
  model.updateMatrixWorld(true);
  const groups = new Map(), spent = [];
  model.traverse((node) => {
    if (!node.isMesh || /^screen_surface/.test(node.name)) return;
    if (Array.isArray(node.material) ? node.material.length !== 1 : !node.material) return;
    const material = Array.isArray(node.material) ? node.material[0] : node.material;
    if (!groups.has(material)) groups.set(material, []);
    groups.get(material).push(node);
    spent.push(node);
  });
  for (const node of spent) node.removeFromParent();
  const meshes = [], v = new T.Vector3(), normalMatrix = new T.Matrix3();
  // The batches become children of the model root, so bake into the root's own space, not the world's.
  const toLocal = new T.Matrix4().copy(model.matrixWorld).invert(), local = new T.Matrix4();
  let triangles = 0;
  for (const [material, nodes] of groups) {
    const position = [], normal = [], index = [];
    let offset = 0;
    for (const node of nodes) {
      const g = node.geometry, p = g.attributes.position, n = g.attributes.normal;
      if (!p) continue;
      local.multiplyMatrices(toLocal, node.matrixWorld);
      normalMatrix.getNormalMatrix(local);
      for (let i = 0; i < p.count; i++) {
        v.fromBufferAttribute(p, i).applyMatrix4(local);
        position.push(v.x, v.y, v.z);
        if (n) { v.fromBufferAttribute(n, i).applyMatrix3(normalMatrix).normalize(); normal.push(v.x, v.y, v.z); }
        else normal.push(0, 1, 0);
      }
      const gi = g.index;
      if (gi) for (let i = 0; i < gi.count; i++) index.push(gi.getX(i) + offset);
      else for (let i = 0; i < p.count; i++) index.push(i + offset);
      offset += p.count;
    }
    if (!index.length) continue;
    const geometry = new T.BufferGeometry();
    geometry.setAttribute('position', new T.Float32BufferAttribute(position, 3));
    geometry.setAttribute('normal', new T.Float32BufferAttribute(normal, 3));
    geometry.setIndex(index);
    geometry.computeBoundingBox(); geometry.computeBoundingSphere();
    const mesh = new T.Mesh(geometry, material);
    mesh.name = 'prop_batch_' + String(material.name || 'part').replace(/[^a-z0-9]+/gi, '_').toLowerCase();
    model.add(mesh); meshes.push(mesh);
    triangles += index.length / 3;
  }
  // A planter carries one authored empty per leaf. With their meshes batched away those empties are
  // pure recursion for updateMatrixWorld, so drop the ones that ended up holding nothing.
  let pruned = 0;
  for (let again = true; again;) {
    again = false;
    const dead = [];
    model.traverse((node) => {
      if (node === model || node.isMesh || node.children.length || !node.parent) return;
      dead.push(node);
    });
    for (const node of dead) { node.removeFromParent(); pruned++; again = true; }
  }
  return { meshes, triangles, pruned };
}

/**
 * @param {Object} o  { root, loader, base }
 * The props hang off the customization root, so the room disposes them with it.
 */
export async function createCustomizationProps({ root, loader, base }) {
  const brass = new T.MeshStandardMaterial({ color: 0xba8654, metalness: 0.72, roughness: 0.32 });
  const geometries = new Set(), orphaned = new Set(), screens = [], placed = [], enabled = PROPS.map(() => false);
  let triangles = 0, batches = 0, pruned = 0;
  /** THE LANDING (10.22.D): one live arrival per prop, and the emissives it is borrowing while it lands. */
  const landings = PROPS.map(() => null), lit = PROPS.map(() => []);
  let landed = 0;

  const models = await Promise.all(PROPS.map((row) => loader.loadAsync(base + 'customization/' + row.file + '.glb')));
  PROPS.forEach((row, i) => {
    const group = new T.Group();
    group.name = row.node;
    const model = models[i].scene;
    const baked = bakeStatic(model);
    triangles += baked.triangles; batches += baked.meshes.length; pruned += baked.pruned;
    group.add(model);

    if (row.plinth) {
      const g = new T.CylinderGeometry(0.27, 0.30, row.plinth, 18);
      geometries.add(g);
      const plinth = new T.Mesh(g, brass);
      plinth.name = row.node + '_plinth';
      plinth.position.y = -row.plinth / 2;
      group.add(plinth); batches++;
    }
    if (row.hanger) {
      const top = new T.Box3().setFromObject(model).max.y;
      const length = Math.max(0.12, row.hanger - row.position[1] - top);
      const g = new T.CylinderGeometry(0.016, 0.016, length, 8);
      geometries.add(g);
      const rod = new T.Mesh(g, brass);
      rod.name = row.node + '_hanger';
      rod.position.y = top + length / 2;
      group.add(rod); batches++;
    }
    const fedFrom = screens.length;
    for (const [name, aspect] of row.screens || []) {
      const surface = model.getObjectByName(name);
      if (!surface || !surface.isMesh) continue;
      surface.userData.screenAspect = aspect;
      // screens.js replaces the authored placeholder with its own shader material and owns that one
      // from then on, so keep the placeholder to free and leave the replacement alone.
      for (const m of (Array.isArray(surface.material) ? surface.material : [surface.material])) if (m) orphaned.add(m);
      screens.push(surface);
    }

    group.visible=false;
    group.position.fromArray(row.position);
    group.rotation.y = row.yaw;
    group.updateMatrixWorld(true);
    group.traverse((node) => { node.updateMatrix(); node.matrixAutoUpdate = false; });
    root.add(group);
    placed.push(group);
    // THE GLOW's own surfaces, taken once: this prop's OWN materials with an emissive to lend. The
    // brass is shared between every plinth and hanger in the room and is left out on purpose, and a
    // media plane belongs to the screen feed from the moment screens.js adopts it.
    const fed = new Set(screens.slice(fedFrom));
    const seen = new Set();
    group.traverse((node) => {
      if (!node.isMesh || fed.has(node)) return;
      for (const m of (Array.isArray(node.material) ? node.material : [node.material])) {
        if (!m || m === brass || !m.emissive || seen.has(m)) continue;
        seen.add(m);
        lit[i].push({ m, colour: m.emissive.clone(), power: Number.isFinite(m.emissiveIntensity) ? m.emissiveIntensity : 1 });
      }
    });
  });

  const bounds = new T.Box3(), center = new T.Vector3(), size = new T.Vector3();
  const GOLD = new T.Color(0xffcf6b);

  /** Put a prop back exactly where it was authored and give its emissives back. */
  function settle(index) {
    const group = placed[index], row = PROPS[index];
    landings[index] = null;
    group.position.fromArray(row.position);
    group.scale.set(1, 1, 1);
    group.updateMatrix();
    for (const s of lit[index]) { s.m.emissive.copy(s.colour); s.m.emissiveIntensity = s.power; }
  }

  return {
    models: placed.map(group => group.children[0]),
    screens,
    /**
     * @param {number} index  @param {boolean} on
     * @param {Object} [feel]  room/prop-landing.js's placeFeel, when this is a PLACEMENT rather than a
     *   restore. A won decoration going up lands with a thud and a glow (10.22.D); the room reading its
     *   own saved state back on entry hands nothing and the prop is simply there.
     */
    set(index, on, feel) {
      if (!Number.isInteger(index) || index < 0 || index >= placed.length || typeof on !== 'boolean') return false;
      enabled[index] = on; placed[index].visible = on;
      settle(index);
      if (on && feel && feel.ms > 0) { landings[index] = { feel, age: 0 }; landed++; }
      return true;
    },
    /** Drive the arrivals. Seconds, like every other room update; `still` is already in the feel. */
    update(dt) {
      const step = Math.min(50, Math.max(0, (Number(dt) || 0) * 1000));
      for (let i = 0; i < landings.length; i++) {
        const land = landings[i];
        if (!land) continue;
        land.age += step;
        const at = sampleLanding(land.age, land.feel);
        if (at.done) { settle(i); continue; }
        const group = placed[i];
        group.position.y = PROPS[i].position[1] + at.lift;
        group.scale.set(at.scaleXZ, at.scaleY, at.scaleXZ);
        group.updateMatrix();
        for (const s of lit[i]) { s.m.emissive.copy(s.colour).lerp(GOLD, at.glow); s.m.emissiveIntensity = s.power + at.glow * 0.8; }
      }
    },
    getState: () => [...enabled],
    endPreview(){placed.forEach((group,i)=>{group.visible=enabled[i];});},
    /** Where the catalogue close-up stands to look at one prop, in room coordinates. */
    preview(index, { show = enabled[index] } = {}) {
      const group = placed[index], row = PROPS[index];
      if (!group) return null;
      group.visible = show;
      bounds.setFromObject(group);
      // Locked catalogue samples may be shown without changing ownership or enabled state.
      group.visible = show;
      bounds.getCenter(center); bounds.getSize(size);
      const distance = Math.max(1.35, size.y * 1.6, size.x * 1.15, size.z * 1.15);
      return {
        position: [center.x + Math.sin(row.camYaw) * distance, center.y + 0.1, center.z + Math.cos(row.camYaw) * distance],
        look: center.toArray(), width: Math.max(size.x, size.z), height: size.y,
      };
    },
    /** Test seam (scene.js debug): plain numbers, so the smoke can check the spots against the fixtures. */
    debug: () => ({
      props: placed.length, batches, pruned, triangles: Math.round(triangles), on: enabled.filter(Boolean).length,
      landed, landing: landings.filter(Boolean).length,
      boxes: placed.map((group) => {
        bounds.setFromObject(group);
        return { name: group.name, min: bounds.min.toArray().map(round), max: bounds.max.toArray().map(round) };
      }),
    }),
    /* Collect BEFORE detaching: the room's own sweep in customization.js traverses the catalogue root,
       and a group already removed from it is invisible to that sweep. The baked batch geometries and
       the GLB materials only exist here, so they are only freed here. */
    dispose() {
      // A prop mid-landing is holding somebody else's emissive. Hand it back before anything is freed.
      for (let i = 0; i < landings.length; i++) if (landings[i]) settle(i);
      const fed = new Set(screens);
      const materials = new Set([brass, ...orphaned]);
      for (const group of placed) {
        group.traverse((node) => {
          if (node.geometry) geometries.add(node.geometry);
          if (fed.has(node)) return;   // its material belongs to the room's screen feed, not here
          for (const m of (Array.isArray(node.material) ? node.material : [node.material])) if (m) materials.add(m);
        });
        group.removeFromParent();
      }
      geometries.forEach((g) => g.dispose());
      materials.forEach((m) => m.dispose());
    },
  };
}
