/* ============================================================================
 * race/propPack.js - one shared handle on race/assets/props.glb.
 *
 * rooms.js (road furniture) and roomProps.js (room vignettes) dress from the same pack,
 * so they share one request and one parsed scene. The promise NEVER rejects: a missing or
 * broken pack resolves to null and every caller keeps its hand-built voxel primitive.
 *   propPack({ log }) -> Promise<Pack|null>  (Pack: race/gltf.js)
 *   packGeo(pack, name, off, scaleX) -> BufferGeometry|null   geoSize(geo) -> { w, h, d }
 * ==========================================================================*/

import { loadPack, toInstanceGeometry } from './gltf.js';

export const PROPS_URL = '/dtrh/race/assets/props.glb';
let _pack = null;

/** Shared, never-rejecting handle on props.glb. null means "no pack, keep the voxels". */
export function propPack(opts = {}) {
  if (!_pack) _pack = loadPack(PROPS_URL, opts).then((p) => p || null, () => null);
  return _pack;
}

/** One merged geometry for a named node, scaled on x then moved by `off` so its origin lands
 *  where the JS primitive's did (the pack authors every node base-centre on the ground).
 *  Null when the pack or the node is missing: always a "keep the fallback" signal. */
export function packGeo(pack, name, off, scaleX) {
  const node = pack && pack.byName ? pack.byName(name) : null;
  const geo = node ? toInstanceGeometry(node) : null;
  if (!geo) return null;
  if (scaleX && scaleX !== 1) geo.scale(scaleX, 1, 1);
  if (off && (off[0] || off[1] || off[2])) geo.translate(off[0] || 0, off[1] || 0, off[2] || 0);
  geo.computeBoundingSphere();
  return geo;
}

/** Extents of a merged geometry in its own frame (glTF Y-up), plus `cy`, the height of the box
 *  centre over the node origin (what a JS primitive centred on its own origin wants). */
export function geoSize(geo) {
  if (geo && !geo.boundingBox) geo.computeBoundingBox();
  const b = geo && geo.boundingBox;
  return b ? { w: b.max.x - b.min.x, h: b.max.y - b.min.y, d: b.max.z - b.min.z, cy: (b.max.y + b.min.y) / 2 }
    : { w: 0, h: 0, d: 0, cy: 0 };
}
