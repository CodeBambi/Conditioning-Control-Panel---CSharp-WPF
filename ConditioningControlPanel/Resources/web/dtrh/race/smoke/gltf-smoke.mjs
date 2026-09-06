/* ============================================================================
 * race/smoke/gltf-smoke.mjs - node smoke for race/gltf.js.
 *
 *   node race/smoke/gltf-smoke.mjs        (exits 0 on pass, 1 on the first failure)
 *
 * Node has no DOM and no WebGL, so this file:
 *   1. resolves the bare specifiers `three` and `three/addons/` with a local
 *      module hook (node has no import map; the browser gets the same two
 *      mappings from race.html), then dynamically imports everything;
 *   2. writes a small TEXTURELESS GLB itself with a ~60 line writer, rather
 *      than vendoring GLTFExporter: two top-level nodes (kart_cup with two
 *      coloured box children, item_cube with a COLOR_0 attribute) and one
 *      1-second `idle` clip;
 *   3. parses it with GLTFLoader.parse(arrayBuffer, '', onLoad, onError);
 *   4. asserts names(), stats, toInstanceGeometry (vertex count, colours, the
 *      node-relative bake, outline winding), setFace on a hand-built
 *      EMI_glass + DataTexture, preparePixel, and the clip names.
 *
 * SHIMS: none. three r169 and GLTFLoader both import clean under node 22+,
 * and the textureless GLB path never reaches createImageBitmap, ImageBitmap-
 * Loader or document. TextDecoder / TextEncoder / URL are node globals
 * already. If a future pack gains textures this smoke must stay textureless.
 * ==========================================================================*/

import { registerHooks } from 'node:module';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const VENDOR = pathToFileURL(path.resolve(import.meta.dirname, '../../vendor/three/') + path.sep).href;
registerHooks({
  resolve(spec, ctx, next) {
    if (spec === 'three') return { url: VENDOR + 'three.module.min.js', shortCircuit: true };
    if (spec.startsWith('three/addons/')) return { url: VENDOR + 'addons/' + spec.slice('three/addons/'.length), shortCircuit: true };
    return next(spec, ctx);
  },
});

const THREE = await import('three');
const { makePack, toInstanceGeometry, setFace, preparePixel, flattenRig, disposePack, FACES, faceFrames, atlasScale, forceAtlasSampler } = await import('../gltf.js');
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');

// ---- the tiny assert kit ----------------------------------------------------------------
let passed = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); process.exit(1); } passed++; console.log('  ok  ' + what); };
const near = (a, b, eps = 1e-4) => Math.abs(a - b) <= eps;

// ---- the GLB writer ---------------------------------------------------------------------
const COMP = { SCALAR: 1, VEC3: 3, VEC4: 4 };
const FLOAT = 5126, USHORT = 5123;

function makeGlb() {
  const chunks = [];
  let len = 0;
  const bufferViews = [], accessors = [];
  const put = (arr, componentType, type, extra) => {
    const bytes = new Uint8Array(arr.buffer, arr.byteOffset, arr.byteLength);
    bufferViews.push({ buffer: 0, byteOffset: len, byteLength: bytes.length });
    chunks.push(bytes); len += bytes.length;
    const pad = (4 - (len % 4)) % 4;
    if (pad) { chunks.push(new Uint8Array(pad)); len += pad; }
    accessors.push({ bufferView: bufferViews.length - 1, componentType, count: arr.length / COMP[type], type, ...extra });
    return accessors.length - 1;
  };

  const box = new THREE.BoxGeometry(1, 1, 1);
  const aPos = put(new Float32Array(box.attributes.position.array), FLOAT, 'VEC3', { min: [-0.5, -0.5, -0.5], max: [0.5, 0.5, 0.5] });
  const aNor = put(new Float32Array(box.attributes.normal.array), FLOAT, 'VEC3', {});
  const aIdx = put(new Uint16Array(box.index.array), USHORT, 'SCALAR', {});
  const rgb = new Float32Array(box.attributes.position.count * 3);
  for (let i = 0; i < box.attributes.position.count; i++) { rgb[i * 3] = 0.25; rgb[i * 3 + 1] = 0.5; rgb[i * 3 + 2] = 0.75; }
  const aCol = put(rgb, FLOAT, 'VEC3', {});
  const aTime = put(new Float32Array([0, 1]), FLOAT, 'SCALAR', { min: [0], max: [1] });
  const aMove = put(new Float32Array([2, 0, 0, 2, 0.4, 0]), FLOAT, 'VEC3', {});
  box.dispose();

  const prim = (mat, withColor) => ({
    attributes: withColor ? { POSITION: aPos, NORMAL: aNor, COLOR_0: aCol } : { POSITION: aPos, NORMAL: aNor },
    indices: aIdx, material: mat,
  });
  const json = {
    asset: { version: '2.0', generator: 'race/smoke/gltf-smoke.mjs' },
    scene: 0,
    scenes: [{ nodes: [0, 3] }],
    nodes: [
      { name: 'kart_cup', translation: [2, 0, 0], children: [1, 2] },
      { name: 'cup_body', mesh: 0, translation: [0, 0.5, 0] },
      { name: 'cup_handle', mesh: 1 },
      { name: 'item_cube', mesh: 2, translation: [-3, 0, 0] },
    ],
    meshes: [
      { name: 'cup_body_mesh', primitives: [prim(0, false)] },
      { name: 'cup_handle_mesh', primitives: [prim(1, false)] },
      { name: 'item_cube_mesh', primitives: [prim(2, true)] },
    ],
    materials: [
      { name: 'body', pbrMetallicRoughness: { baseColorFactor: [0.2, 0.4, 0.6, 1] } },
      { name: 'outline', pbrMetallicRoughness: { baseColorFactor: [0, 0, 0, 1] } },
      { name: 'cube', pbrMetallicRoughness: { baseColorFactor: [1, 1, 1, 1] } },
    ],
    animations: [{
      name: 'idle',
      samplers: [{ input: aTime, output: aMove, interpolation: 'LINEAR' }],
      channels: [{ sampler: 0, target: { node: 0, path: 'translation' } }],
    }],
    bufferViews,
    accessors,
    buffers: [{ byteLength: len }],
  };

  const jsonBytes = new TextEncoder().encode(JSON.stringify(json));
  const jsonPad = (4 - (jsonBytes.length % 4)) % 4;
  const jsonLen = jsonBytes.length + jsonPad;
  const total = 12 + 8 + jsonLen + 8 + len;
  const out = new Uint8Array(total);
  const dv = new DataView(out.buffer);
  dv.setUint32(0, 0x46546c67, true); dv.setUint32(4, 2, true); dv.setUint32(8, total, true);
  dv.setUint32(12, jsonLen, true); dv.setUint32(16, 0x4e4f534a, true);
  out.set(jsonBytes, 20);
  for (let i = 0; i < jsonPad; i++) out[20 + jsonBytes.length + i] = 0x20;   // JSON chunk pads with spaces
  let at = 20 + jsonLen;
  dv.setUint32(at, len, true); dv.setUint32(at + 4, 0x004e4942, true);
  at += 8;
  for (const c of chunks) { out.set(c, at); at += c.length; }
  return out.buffer;
}

// ---- parse ------------------------------------------------------------------------------
const glb = makeGlb();
const gltf = await new Promise((resolve, reject) => new GLTFLoader().parse(glb, '', resolve, reject));
const pack = makePack(gltf, 'race/assets/smoke.glb');

console.log('pack: ' + JSON.stringify(pack.stats) + ' names ' + JSON.stringify(pack.names()));

// ---- pack --------------------------------------------------------------------------------
const names = pack.names();
ok(names.includes('kart_cup') && names.includes('item_cube'), 'names() lists the top-level nodes');
ok(names.includes('cup_body') && names.includes('cup_handle'), 'names() lists the children too');
ok(pack.byName('kart_cup') !== null, 'byName finds a node');
ok(pack.byName('nope_not_here') === null, 'byName returns null for a miss (callers fall back)');
ok(pack.stats.tris === 36, 'stats.tris counts 3 boxes = 36 (got ' + pack.stats.tris + ')');
ok(pack.stats.nodes === 5, 'stats.nodes counts scene + 4 nodes (got ' + pack.stats.nodes + ')');
ok(pack.stats.bytes > 0, 'stats.bytes sees the GLB body (' + pack.stats.bytes + ')');

const clone = pack.clone('kart_cup');
ok(clone && clone !== pack.byName('kart_cup'), 'clone() returns a fresh object');
ok(clone.children.length === 2, 'clone() is deep');
ok(clone.children[0].material === pack.byName('cup_body').material, 'clone() shares materials');
ok(pack.clone('nope_not_here') === null, 'clone() returns null for a miss');

// ---- animations --------------------------------------------------------------------------
ok(pack.animations.length === 1 && pack.animations[0].name === 'idle', 'animations carry the clip names');
ok(near(pack.animations[0].duration, 1), 'the clip is 1 second');

// ---- toInstanceGeometry ------------------------------------------------------------------
const geo = toInstanceGeometry(pack.byName('kart_cup'));
ok(geo && geo.attributes.position.count === 48, 'kart_cup merges 2 boxes into 48 verts (got ' + (geo && geo.attributes.position.count) + ')');
ok(geo.index && geo.index.count === 72, 'and 72 indices (got ' + (geo.index && geo.index.count) + ')');
ok(!!geo.attributes.normal && !!geo.attributes.color, 'attributes are position + normal + color');
ok(Object.keys(geo.attributes).sort().join(',') === 'color,normal,position', 'and nothing else (no uv, so it drops into MeshLambertMaterial({vertexColors:true}))');
const c = geo.attributes.color;
ok(near(c.getX(0), 0.2) && near(c.getY(0), 0.4) && near(c.getZ(0), 0.6), 'flat colour comes from the material baseColorFactor, linear, uncorrected');
ok(near(c.getX(47), 0) && near(c.getY(47), 0), 'the second box carries its own flat colour');
geo.computeBoundingBox();
const bb = geo.boundingBox;
ok(near(bb.min.x, -0.5) && near(bb.max.x, 0.5), 'the bake is RELATIVE to the node: the +2 x offset is gone');
ok(near(bb.min.y, -0.5) && near(bb.max.y, 1), 'the child translation IS baked in (y spans -0.5..1)');

const cubeGeo = toInstanceGeometry(pack.byName('item_cube'));
const cc = cubeGeo.attributes.color;
ok(near(cc.getX(0), 0.25) && near(cc.getY(0), 0.5) && near(cc.getZ(0), 0.75), 'COLOR_0 wins over the material colour when a mesh has one');
ok(cubeGeo.attributes.color.itemSize === 3, 'COLOR_0 is normalised to a float RGB attribute');
ok(toInstanceGeometry(null) === null, 'toInstanceGeometry(null) is null, not a throw');
ok(toInstanceGeometry(new THREE.Group()) === null, 'an empty node merges to null (caller falls back)');

// ---- outline winding ----------------------------------------------------------------------
// Both hulls are mirrored on x. The plain one gets re-wound so it lights right; the one whose
// material is named `outline` is an inverted hull and must come through exactly as authored.
function mirroredHull(matName) {
  const g = new THREE.Group();
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshBasicMaterial({ name: matName }));
  mesh.scale.set(-1, 1, 1);
  g.add(mesh);
  return g;
}
const src = Array.from(new THREE.BoxGeometry(1, 1, 1).index.array);
const plain = Array.from(toInstanceGeometry(mirroredHull('body')).index.array);
const hull = Array.from(toInstanceGeometry(mirroredHull('outline')).index.array);
ok(hull.join() === src.join(), 'an `outline` material keeps its winding, mirror or not');
ok(plain[0] === src[2] && plain[2] === src[0], 'a mirrored ordinary mesh is re-wound');

// ---- flattenRig --------------------------------------------------------------------------
// scene: kart_cup (the clip target) holds cup_body (`body`) + cup_handle (`outline`), item_cube
// stands alone. A second `body` box is added 1 m along x so the pivot has something to merge:
// 4 meshes -> 3 (cup_body + twin fold into one, handle and cube stay).
{
  const rig = pack.scene.clone(true);
  const cupBefore = rig.getObjectByName('kart_cup');
  const twin = cupBefore.getObjectByName('cup_body').clone(); twin.name = 'cup_twin'; twin.position.x += 1; cupBefore.add(twin);
  const meshesBefore = []; rig.traverse((o) => { if (o.isMesh) meshesBefore.push(o); });
  const pos0 = cupBefore.getObjectByName('cup_body').position.clone(); pos0.x += 0.5;   // merged centre sits between the two
  const r = flattenRig(rig, pack.animations);
  ok(r.before === meshesBefore.length, 'flattenRig visits every mesh (' + r.before + ')');
  const meshesAfter = []; rig.traverse((o) => { if (o.isMesh) meshesAfter.push(o); });
  ok(meshesAfter.length === r.after && r.after < r.before, 'flattenRig leaves fewer meshes (' + r.before + ' -> ' + r.after + ')');
  const cup = rig.getObjectByName('kart_cup');
  ok(cup === cupBefore, 'the clip target node survives untouched');
  const byMat = new Map(); cup.traverse((o) => { if (o.isMesh) byMat.set(o.material.name, o); });
  ok(byMat.size === cup.children.filter((c) => c.isMesh).length, 'one mesh per material under the pivot (' + [...byMat.keys()].join(',') + ')');
  const body = byMat.get('body');
  ok(body && body.geometry.attributes.position && !body.geometry.attributes.color, 'merged geometry keeps position/normal only');
  body.geometry.computeBoundingBox();
  const c = body.geometry.boundingBox.getCenter(new THREE.Vector3());
  ok(near(c.x, pos0.x, 1e-3) && near(c.y, pos0.y, 1e-3) && near(c.z, pos0.z, 1e-3), 'the child offsets are baked into the pivot frame');
  ok(rig.getObjectByName('cup_body') === body, 'the merged mesh keeps the first contributor name');
  const again = flattenRig(rig, pack.animations);
  ok(again.before === meshesAfter.length && again.after === meshesAfter.length, 'a second pass is a no-op in mesh count');
  const glass = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: new THREE.DataTexture(new Uint8Array(4), 1, 1) }));
  glass.name = 'EMI_glass'; rig.add(glass);
  flattenRig(rig, pack.animations);
  ok(glass.parent === rig, 'a textured mesh is left alone');
}

// ---- setFace -------------------------------------------------------------------------------
const tex = new THREE.DataTexture(new Uint8Array(FACES.length * 4 * 4), FACES.length * 4, 4);
const glass = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: tex }));
glass.name = 'EMI_glass';
const emi = new THREE.Group();
emi.name = 'EMI_root';
emi.add(glass);

ok(FACES.join(' ') === '^_^ :3 >_< o_o $_$ ★_★ @_@', 'FACES is the authored atlas order');
ok(faceFrames(tex) === 5, 'an untagged texture reads as the five frame strip baked into the glb');
ok(setFace(emi, 2) === true, 'setFace finds EMI_glass');
ok(near(tex.offset.x, 2 / 5), 'frame 2 sits at offset 0.4 (got ' + tex.offset.x + ')');
ok(tex.wrapS === THREE.RepeatWrapping, 'wrapS is RepeatWrapping');
ok(tex.magFilter === THREE.NearestFilter && tex.minFilter === THREE.NearestFilter, 'filters are Nearest both ways');
ok(tex.generateMipmaps === false, 'mipmaps are off');
setFace(emi, 99); ok(near(tex.offset.x, 4 / 5), 'an out of range index clamps to the last frame');
setFace(emi, -7); ok(near(tex.offset.x, 0), 'a negative index clamps to the first frame');
// the live atlas (assets/emi-faces.png) is wider than the strip inside the glb: loadPack tags the
// swapped texture with its frame count and setFace divides by that, never by a spelled out five
tex.userData.faceFrames = FACES.length;
ok(faceFrames(tex) === FACES.length, 'a tagged texture reports the swapped strip width');
setFace(emi, 6); ok(near(tex.offset.x, 6 / FACES.length), 'frame 6 sits at 6/7 on the live atlas');
setFace(emi, 99); ok(near(tex.offset.x, (FACES.length - 1) / FACES.length), 'the clamp follows the wider strip');
delete tex.userData.faceFrames;
setFace(emi, 0);
ok(setFace(new THREE.Group(), 1) === false, 'no EMI_glass, no throw, just false');
ok(setFace(null, 1) === false, 'setFace(null) is false');

// ---- the padded atlas ------------------------------------------------------------------------
// padAtlasToPot needs a document, so node never takes that branch; what the smoke CAN hold is the
// contract setFace reads off the far side of it. The live strip is 1064x137 in a 2048x256 canvas.
ok(atlasScale(tex).x === 1 && atlasScale(tex).y === 1, 'a raw strip reports a 1,1 atlas scale');
const SX = 1064 / 2048, SY = 137 / 256;
tex.userData.atlasScale = { x: SX, y: SY };
tex.userData.faceFrames = FACES.length;
ok(atlasScale(tex).x === SX, 'a padded strip reports the scale it was padded by');
for (let i = 0; i < FACES.length; i++) {
  setFace(emi, i);
  ok(near(tex.offset.x, (i / FACES.length) * SX), 'padded frame ' + i + ' sits at ' + i + '/7 squeezed into the pad');
}
setFace(emi, 99);
ok(near(tex.offset.x, ((FACES.length - 1) / FACES.length) * SX), 'the clamp still lands on the last frame when padded');
// the authored EMI_glass u span is 0.00066 .. 0.19934 over one baked frame, so with repeat.x
// squeezed the same way the last window must stop inside the strip: never in the pad, and never
// over a wrap seam, which is what lets the padded texture be ClampToEdge
ok(0.19934 * (5 / FACES.length) * SX + tex.offset.x < SX, 'the last frame window stops inside the strip, not in the pad');
// the pixel budget: setFace must never ask for a re-upload of the atlas
tex.wrapS = THREE.ClampToEdgeWrapping;
const ver = tex.version;
setFace(emi, 3);
ok(tex.version === ver, 'setFace never bumps the texture version (an offset is a uniform, not pixels)');
ok(tex.wrapS === THREE.ClampToEdgeWrapping, 'setFace leaves a padded texture clamped');
forceAtlasSampler(tex);
ok(tex.wrapS === THREE.ClampToEdgeWrapping, 'forceAtlasSampler skips a padded texture');
delete tex.userData.atlasScale;
forceAtlasSampler(tex);
ok(tex.wrapS === THREE.RepeatWrapping && tex.generateMipmaps === false, 'forceAtlasSampler fixes a raw strip');
const ver2 = tex.version;
forceAtlasSampler(tex);
ok(tex.version === ver2, 'a sampler that is already right costs no second upload');
delete tex.userData.faceFrames;
setFace(emi, 0);

// ---- preparePixel ----------------------------------------------------------------------------
const seen = [];
preparePixel(emi, { filterTexture: (t) => seen.push(t) });
ok(seen.length === 1 && seen[0] === tex, 'preparePixel walks every texture through pixel.filterTexture');
preparePixel(emi, null);
ok(true, 'preparePixel with no pixelizer is a no-op');

// ---- disposePack ------------------------------------------------------------------------------
disposePack('race/assets/never-loaded.glb');
ok(true, 'disposePack on an unknown url is a no-op');

console.log('\ngltf smoke PASS: ' + passed + ' assertions');
process.exit(0);
