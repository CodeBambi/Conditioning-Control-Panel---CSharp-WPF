import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { BufferGeometry, BufferAttribute } from '../../vendor/three/three.module.min.js';
import { finishSculptSurface, sculptOutlineGeometry } from '../board/sculpt-finish.js';

const root = new URL('../assets/pieces/', import.meta.url);
let checked = 0;
for (const file of readdirSync(root).filter(name => name.endsWith('.glb'))) {
  const bytes = readFileSync(new URL(file, root));
  const jsonLength = bytes.readUInt32LE(12);
  const gltf = JSON.parse(bytes.toString('utf8', 20, 20 + jsonLength));
  const primitive = gltf.meshes.flatMap(mesh => mesh.primitives).find(p => p.attributes.COLOR_0 != null);
  const attribute = index => {
    const a = gltf.accessors[index], view = gltf.bufferViews[a.bufferView];
    const width = { SCALAR: 1, VEC3: 3, VEC4: 4 }[a.type];
    const Type = a.componentType === 5126 ? Float32Array : Uint16Array;
    const start = 28 + jsonLength + (view.byteOffset || 0) + (a.byteOffset || 0);
    const data = new Type(a.count * width);
    for (let i = 0; i < data.length; i++) data[i] = Type === Float32Array
      ? bytes.readFloatLE(start + i * 4) : bytes.readUInt16LE(start + i * 2);
    return new BufferAttribute(data, width, !!a.normalized);
  };
  const geometry = new BufferGeometry();
  for (const [name, key] of [['position', 'POSITION'], ['normal', 'NORMAL'], ['color', 'COLOR_0']]) geometry.setAttribute(name, attribute(primitive.attributes[key]));
  geometry.setIndex(attribute(primitive.indices));
  const before = geometry.clone();
  finishSculptSurface(geometry);
  assert.deepEqual(geometry.attributes.position.array, before.attributes.position.array, file + ': silhouette preserved');
  assert.deepEqual(geometry.index.array, before.index.array, file + ': topology preserved');
  assert.deepEqual(sculptOutlineGeometry(geometry).attributes.normal.array, before.attributes.normal.array, file + ': outline preserves geometric normals');
  for (let i = 0; i < geometry.attributes.normal.count; i++) {
    const n = geometry.attributes.normal;
    assert.ok(Math.abs(Math.hypot(n.getX(i), n.getY(i), n.getZ(i)) - 1) < .00001, file + ': unit lighting normal');
    assert.equal(geometry.attributes.color.getW(i), before.attributes.color.getW(i), file + ': alpha preserved');
  }
  const finished = geometry.attributes.normal.array.slice();
  finishSculptSurface(geometry);
  assert.deepEqual(geometry.attributes.normal.array, finished, file + ': finish runs once');
  checked++;
}

// Adjacent painted regions must not bleed, and unpainted metal is untouched.
const seam = new BufferGeometry();
seam.setAttribute('position', new BufferAttribute(new Float32Array([0,0,0, 1,0,0, 0,1,0]), 3));
seam.setAttribute('normal', new BufferAttribute(new Float32Array([0,0,1, 0,0,1, 0,0,1]), 3));
seam.setAttribute('color', new BufferAttribute(new Float32Array([1,.1,.4,1, 1,1,1,1, 1,.1,.4,1]), 4));
seam.setIndex([0,1,2]);
const colors = seam.attributes.color.array.slice();
finishSculptSurface(seam);
assert.deepEqual(seam.attributes.color.array, colors, 'white trim remains distinct from pink');
const metal = seam.clone();
metal.deleteAttribute('color');
finishSculptSurface(metal);
assert.equal(sculptOutlineGeometry(metal), metal, 'unpainted jewellery bypasses the finish');
console.log(`Sculpt finish: ${checked} shipped sculpts, shape, outline, alpha, paint boundaries and repeat-load checks passed.`);
