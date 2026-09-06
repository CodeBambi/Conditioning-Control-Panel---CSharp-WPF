/* ============================================================================
 * race/gltf.js - Racing Thoughts glTF pack loader.
 *
 * Two packs, both authored in Blender and dropped in race/assets/:
 *
 *   emi.glb    the mascot rig. Nodes: EMI_root, EMI_case, EMI_glass, ant0,
 *              ant1, ant2, ballpiv, shoulderL, shoulderR, footL, footR,
 *              button. Clips: idle, wave, hop, peek, drum. EMI_glass carries
 *              the face atlas (see setFace / FACES).
 *   props.glb  every prop as a NAMED TOP-LEVEL node: kart_cup, kart_saucer,
 *              item_cube, item_shard_00 .. item_shard_11, boost_pad, ramp_lip,
 *              air_marker, gantry, podium, floor_tile, plus one <room>_<slot>
 *              per roomProps ROOM_PROPS entry (teagarden_wall, casino_shoulder,
 *              toybox_extra, ...).
 *
 * NOTHING here depends on any of those names existing. byName() returns null
 * for a miss and every caller falls back to the hand-built voxel kit, so a
 * half-finished pack degrades one prop at a time instead of blanking the race.
 *
 *   loadPack(url, { log }) -> Promise<Pack>       cached per url
 *   Pack = { scene, animations, byName, clone, names, stats }
 *   toInstanceGeometry(node) -> BufferGeometry    roomProps-shaped, ready for
 *                                                 InstancedMesh + Lambert
 *   setFace(model, index) / FACES
 *   flattenRig(root, animations, { keep })   one draw per pivot + material
 *   preparePixel(model, pixel)
 *   disposePack(url)
 *
 * COLOUR SPACE. roomProps voxel() writes Color.setHex(hex).r/g/b into its
 * `color` attribute, and setHex decodes sRGB into the linear working space, so
 * that attribute is LINEAR. glTF agrees: baseColorFactor and COLOR_0 are both
 * linear by spec and GLTFLoader keeps them that way. So the merge below copies
 * material.color / COLOR_0 straight through with no conversion, and a merged
 * prop shades identically to a voxel() one beside it.
 * ==========================================================================*/

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

/** Face atlas order, left to right across EMI_glass's map. */
export const FACES = ['^_^', ':3', '>_<', 'o_o', '$_$'];
const FACE_N = FACES.length;
const GLASS = 'EMI_glass';
const OUTLINE = 'outline';                 // material name: inverted hull, never re-wound
const MAP_KEYS = ['map', 'emissiveMap', 'alphaMap', 'normalMap', 'roughnessMap', 'metalnessMap', 'aoMap'];

const _cache = new Map();                  // url -> { promise, pack }
const _mat = new THREE.Matrix4();
const _inv = new THREE.Matrix4();

// ---- loading ---------------------------------------------------------------------------

/**
 * Load (or hand back) a pack. One in-flight request per url; a failed load drops out of the
 * cache so a later call can retry. `log` is the race's one-argument logger (bridge.log style).
 */
export function loadPack(url, { log } = {}) {
  const hit = _cache.get(url);
  if (hit) return hit.promise;
  const entry = { promise: null, pack: null };
  entry.promise = new Promise((resolve, reject) => {
    new GLTFLoader().load(url, resolve, undefined, reject);
  }).then((gltf) => {
    const pack = makePack(gltf, url);
    entry.pack = pack;
    if (log) log(`gltf ${url}: ${pack.stats.nodes} nodes, ${pack.stats.tris} tris, ${(pack.stats.bytes / 1024).toFixed(0)} KB`);
    return pack;
  }).catch((e) => {
    _cache.delete(url);
    if (log) log(`gltf ${url} failed: ${(e && e.message) || e}`);
    throw e;
  });
  _cache.set(url, entry);
  return entry.promise;
}

/**
 * Wrap a parsed gltf. Exported so the node smoke can go through GLTFLoader.parse() without a
 * network fetch; the race itself only ever calls loadPack.
 */
export function makePack(gltf, url = '') {
  const scene = gltf.scene || (gltf.scenes && gltf.scenes[0]) || new THREE.Group();
  const animations = gltf.animations || [];
  const index = new Map();
  let nodes = 0, tris = 0;
  scene.traverse((o) => {
    nodes++;
    if (o.name && !index.has(o.name)) index.set(o.name, o);
    const g = o.geometry;
    if (!g || !g.attributes || !g.attributes.position) return;
    tris += (g.index ? g.index.count : g.attributes.position.count) / 3;
  });
  return {
    scene,
    animations,
    url,
    byName: (name) => index.get(name) || null,
    names: () => Array.from(index.keys()),
    clone: (name) => {
      const src = index.get(name);
      return src ? src.clone(true) : null;      // Object3D.clone shares geometry AND material
    },
    stats: { nodes, tris: Math.round(tris), bytes: byteSize(gltf) },
  };
}

/** Best effort source size: the GLB body hangs off the parser on the binary path, so fall back
 *  to the json length rather than guessing when a pack arrives as plain .gltf. */
function byteSize(gltf) {
  const p = gltf.parser;
  const bin = p && p.extensions && p.extensions.KHR_binary_glTF;
  if (bin && bin.body && bin.body.byteLength) return bin.body.byteLength;
  if (p && p.json) { try { return JSON.stringify(p.json).length; } catch (e) { /* cyclic, give up */ } }
  return 0;
}

/** Dispose everything a pack owns and forget it. Safe on an unknown or still-loading url. */
export function disposePack(url) {
  const entry = _cache.get(url);
  if (!entry) return;
  _cache.delete(url);
  const kill = (pack) => {
    if (!pack || !pack.scene) return;
    const seen = new Set();
    pack.scene.traverse((o) => {
      if (o.geometry && !seen.has(o.geometry)) { seen.add(o.geometry); o.geometry.dispose(); }
      const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
      for (const m of mats) {
        if (seen.has(m)) continue;
        seen.add(m);
        for (const k of MAP_KEYS) if (m[k] && !seen.has(m[k])) { seen.add(m[k]); m[k].dispose(); }
        m.dispose();
      }
    });
  };
  if (entry.pack) kill(entry.pack);
  else entry.promise.then(kill, () => {});
}

// ---- merge to a roomProps-shaped instance geometry --------------------------------------

/**
 * Flatten every mesh under `node` into ONE geometry in `node`'s local frame, with a flat
 * per-vertex colour, exactly the shape voxel() produces: attributes position + normal + color,
 * ready for `new THREE.InstancedMesh(geo, new THREE.MeshLambertMaterial({ vertexColors: true }), n)`.
 *
 * Colour source per mesh: COLOR_0 if the mesh has one, else the material's base color, both
 * already linear (see the header). Meshes whose material is named `outline` are inverted hulls:
 * they come through untouched, winding included. Everything else gets its index re-wound when
 * its baked matrix mirrors, so it still lights right.
 *
 * Nothing shared is disposed: the per-mesh copies are throwaway clones and only those go.
 */
export function toInstanceGeometry(node) {
  if (!node) return null;
  node.updateWorldMatrix(true, true);
  _inv.copy(node.matrixWorld).invert();
  const parts = [];
  let anyNonIndexed = false;
  node.traverse((o) => {
    if (!o.isMesh || !o.geometry || !o.geometry.attributes || !o.geometry.attributes.position) return;
    if (o.visible === false) return;
    const g = o.geometry.clone();
    for (const key of Object.keys(g.attributes)) {
      if (key !== 'position' && key !== 'normal' && key !== 'color') g.deleteAttribute(key);
    }
    if (!g.attributes.normal) g.computeVertexNormals();
    _mat.multiplyMatrices(_inv, o.matrixWorld);
    g.applyMatrix4(_mat);
    if (_mat.determinant() < 0 && !isOutline(o.material)) flipWinding(g);
    if (!g.attributes.color) g.setAttribute('color', flatColor(o.material, g.attributes.position.count));
    else normalizeColor(g);
    g.morphAttributes = {};
    g.clearGroups();
    if (!g.index) anyNonIndexed = true;
    parts.push(g);
  });
  if (!parts.length) return null;
  const ready = anyNonIndexed ? parts.map((g) => (g.index ? g.toNonIndexed() : g)) : parts;
  const merged = ready.length === 1 ? ready[0].clone() : mergeGeometries(ready, false);
  for (const g of ready) if (!parts.includes(g)) g.dispose();
  for (const g of parts) g.dispose();
  if (merged) merged.computeBoundingSphere();
  return merged;
}

function isOutline(material) {
  const mats = Array.isArray(material) ? material : material ? [material] : [];
  return mats.some((m) => m && m.name === OUTLINE);
}

/** Reverse each triangle's winding in place (index only: positions and normals are already baked). */
function flipWinding(g) {
  if (!g.index) g.setIndex(Array.from({ length: g.attributes.position.count }, (_, i) => i));
  const a = g.index.array;
  for (let i = 0; i + 2 < a.length; i += 3) { const t = a[i]; a[i] = a[i + 2]; a[i + 2] = t; }
  g.index.needsUpdate = true;
}

/** One flat colour repeated per vertex, taken from the material (linear, see the header). */
function flatColor(material, count) {
  const m = Array.isArray(material) ? material[0] : material;
  const c = m && m.color ? m.color : null;
  const r = c ? c.r : 1, gg = c ? c.g : 1, b = c ? c.b : 1;
  const arr = new Float32Array(count * 3);
  for (let i = 0; i < count; i++) { arr[i * 3] = r; arr[i * 3 + 1] = gg; arr[i * 3 + 2] = b; }
  return new THREE.BufferAttribute(arr, 3);
}

/** COLOR_0 may arrive as RGBA or as normalized bytes; the merge wants a plain float RGB. */
function normalizeColor(g) {
  const src = g.attributes.color;
  if (src.itemSize === 3 && src.array instanceof Float32Array && !src.normalized) return;
  const n = src.count;
  const arr = new Float32Array(n * 3);
  for (let i = 0; i < n; i++) { arr[i * 3] = src.getX(i); arr[i * 3 + 1] = src.getY(i); arr[i * 3 + 2] = src.getZ(i); }
  g.setAttribute('color', new THREE.BufferAttribute(arr, 3));
}

// ---- rig flatten: one draw per animated node and material -------------------------------

/**
 * Bake a rig down to one mesh per (animated pivot, material). A Blender export arrives as one
 * mesh per part (emi.glb: 69 primitives, so 69 draw calls per EMI on screen); only the nodes a
 * clip drives ever move, so every static part under a pivot can share one buffer with its
 * siblings of the same material and still animate right. Rules:
 *   - the pivot set = every node any clip in `animations` targets, plus `root` and `keep`
 *   - a mesh whose material carries a texture stays as it is (the face glass and its atlas)
 *   - a mesh named in `keep`, or that is itself a pivot, stays as it is
 *   - the merged mesh hangs off the first contributor's parent (static under the pivot by
 *     construction), so names like EMI_case / ball keep resolving to a node that holds geometry
 *   - outline hulls keep their winding; mirrored bakes of everything else are re-wound
 * Returns { before, after }: mesh counts. Safe to call twice (the second pass finds nothing).
 */
export function flattenRig(root, animations = [], { keep = [] } = {}) {
  const out = { before: 0, after: 0 };
  if (!root || !root.traverse) return out;
  const pivots = new Set(keep);
  for (const clip of animations || []) for (const t of clip.tracks || []) {
    const name = THREE.PropertyBinding.parseTrackName(t.name).nodeName;
    if (name) pivots.add(name);
  }
  const isPivot = (o) => o === root || pivots.has(o.name);
  const pivotOf = (o) => { let p = o.parent; while (p && p !== root && !isPivot(p)) p = p.parent; return p || root; };
  root.updateWorldMatrix(true, true);
  const groups = new Map();                   // pivot -> Map(material -> { host, name, parts, nonIndexed })
  const dead = [];
  root.traverse((o) => {
    if (!o.isMesh || !o.geometry || !o.geometry.attributes || !o.geometry.attributes.position) return;
    if (o.visible === false || isPivot(o) || Array.isArray(o.material) || !o.material) return;
    if (MAP_KEYS.some((k) => o.material[k])) return;
    const pivot = pivotOf(o);
    const host = o.parent || pivot;
    let byMat = groups.get(pivot);
    if (!byMat) groups.set(pivot, (byMat = new Map()));
    let part = byMat.get(o.material);
    if (!part) byMat.set(o.material, (part = { host, name: o.name, parts: [], nonIndexed: false, shadow: o.castShadow }));
    const g = o.geometry.clone();
    for (const key of Object.keys(g.attributes)) if (key !== 'position' && key !== 'normal') g.deleteAttribute(key);
    if (!g.attributes.normal) g.computeVertexNormals();
    _inv.copy(part.host.matrixWorld).invert();
    _mat.multiplyMatrices(_inv, o.matrixWorld);
    g.applyMatrix4(_mat);
    if (_mat.determinant() < 0 && !isOutline(o.material)) flipWinding(g);
    g.morphAttributes = {};
    g.clearGroups();
    if (!g.index) part.nonIndexed = true;
    part.parts.push(g);
    dead.push(o);
  });
  out.before = dead.length;
  for (const byMat of groups.values()) for (const [material, part] of byMat) {
    const ready = part.nonIndexed ? part.parts.map((g) => (g.index ? g.toNonIndexed() : g)) : part.parts;
    const merged = ready.length === 1 ? ready[0] : mergeGeometries(ready, false);
    for (const g of ready) if (g !== merged && !part.parts.includes(g)) g.dispose();
    for (const g of part.parts) if (g !== merged) g.dispose();
    if (!merged) continue;
    merged.computeBoundingSphere();
    const m = new THREE.Mesh(merged, material);
    m.name = part.name;
    m.castShadow = part.shadow;
    part.host.add(m);
    out.after++;
  }
  for (const o of dead) {
    if (o.children.length) {                  // a mesh with children becomes a plain node, kids kept
      const g = new THREE.Group();
      g.name = o.name; g.position.copy(o.position); g.quaternion.copy(o.quaternion); g.scale.copy(o.scale);
      while (o.children.length) g.add(o.children[0]);
      o.parent.add(g);
    }
    o.removeFromParent();
  }
  return out;
}

// ---- the face atlas ---------------------------------------------------------------------

/**
 * Point EMI_glass's map at one of the five frames. The atlas is a horizontal strip of FACE_N
 * frames and the authored UVs span frame 0 only, so a repeat of 1/FACE_N is already baked into
 * the mesh and all we move is the offset. Returns true if a glass mesh with a map was found.
 */
export function setFace(model, index) {
  if (!model || !model.traverse) return false;
  let mesh = null;
  model.traverse((o) => { if (!mesh && o.isMesh && o.name === GLASS) mesh = o; });
  if (!mesh) return false;
  const mats = Array.isArray(mesh.material) ? mesh.material : mesh.material ? [mesh.material] : [];
  const i = Math.min(FACE_N - 1, Math.max(0, index | 0));
  let hit = false;
  // the atlas may ride the base map or the emissive map (emi.glb puts it on the emissive
  // slot so the screen stays self-lit); shift whichever the material carries
  for (const m of mats) for (const key of ['map', 'emissiveMap']) {
    const tex = m && m[key];
    if (!tex) continue;
    tex.wrapS = THREE.RepeatWrapping;
    tex.magFilter = THREE.NearestFilter;
    tex.minFilter = THREE.NearestFilter;
    tex.generateMipmaps = false;
    tex.offset.x = i / FACE_N;
    tex.needsUpdate = true;
    hit = true;
  }
  return hit;
}

// ---- the pixel pass ---------------------------------------------------------------------

/** Run every texture on `model` through the pixelizer so it snaps to NearestFilter with the
 *  rest of the world while a block size is on. No pixelizer, nothing to do. */
export function preparePixel(model, pixel) {
  if (!model || !model.traverse || !pixel || !pixel.filterTexture) return;
  model.traverse((o) => {
    const mats = Array.isArray(o.material) ? o.material : o.material ? [o.material] : [];
    for (const m of mats) {
      for (const k of MAP_KEYS) if (m[k]) pixel.filterTexture(m[k]);
      if (m.uniforms) for (const u of Object.values(m.uniforms)) if (u && u.value && u.value.isTexture) pixel.filterTexture(u.value);
    }
  });
}

// self-check: node --check is the bar; race/smoke/gltf-smoke.mjs exercises the rest.
