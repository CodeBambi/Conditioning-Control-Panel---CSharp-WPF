// The sculpts carry vertex paint, not image textures. Small neighbouring paint
// and normal jumps become speckles under the silicone highlights. Filter only
// the shading attributes once at load; positions, silhouette and metal stay exact.
const originals = new WeakMap();

// A hull expands along the geometric normals. Smoothed lighting normals can
// push that hull back through a concave sculpt and leave black scratches.
export const sculptOutlineGeometry = geometry => originals.get(geometry) || geometry;

export function finishSculptSurface(geometry) {
  const { normal, color } = geometry.attributes;
  const index = geometry.index;
  if (!normal || !color || !index) return geometry;
  if (originals.has(geometry)) return geometry;
  originals.set(geometry, geometry.clone());
  const count = normal.count;
  const neighbours = Array.from({ length: count }, () => new Set());
  for (let i = 0; i < index.count; i += 3) {
    const triangle = [index.getX(i), index.getX(i + 1), index.getX(i + 2)];
    for (let k = 0; k < 3; k++) {
      const a = triangle[k], b = triangle[(k + 1) % 3];
      neighbours[a].add(b);
      neighbours[b].add(a);
    }
  }
  const read = attr => Float32Array.from({ length: count * 3 }, (_, i) =>
    [attr.getX, attr.getY, attr.getZ][i % 3].call(attr, Math.floor(i / 3)));
  const sourceNormal = read(normal), sourceColor = read(color);
  let normals = sourceNormal, colors = sourceColor;
  for (let pass = 0; pass < 2; pass++) {
    const nextNormal = normals.slice(), nextColor = colors.slice();
    for (let i = 0; i < count; i++) {
      const p = i * 3;
      let nx = normals[p] * 2, ny = normals[p + 1] * 2, nz = normals[p + 2] * 2;
      let red = colors[p] * 2, green = colors[p + 1] * 2, blue = colors[p + 2] * 2, weight = 2;
      for (const j of neighbours[i]) {
        const q = j * 3;
        const dot = sourceNormal[p] * sourceNormal[q] + sourceNormal[p + 1] * sourceNormal[q + 1] + sourceNormal[p + 2] * sourceNormal[q + 2];
        // Preserve a sculpted crease. Use the original normals on both passes
        // so the filter cannot creep across an edge it softened previously.
        if (dot < .75) continue;
        nx += normals[q]; ny += normals[q + 1]; nz += normals[q + 2];
        const distance = Math.hypot(sourceColor[p] - sourceColor[q], sourceColor[p + 1] - sourceColor[q + 1], sourceColor[p + 2] - sourceColor[q + 2]);
        // White cuffs and contrasting paint are design, not surface noise.
        if (distance >= .14) continue;
        const w = 1 - distance / .14;
        red += colors[q] * w; green += colors[q + 1] * w; blue += colors[q + 2] * w; weight += w;
      }
      const length = Math.hypot(nx, ny, nz) || 1;
      nextNormal[p] = nx / length; nextNormal[p + 1] = ny / length; nextNormal[p + 2] = nz / length;
      nextColor[p] = red / weight; nextColor[p + 1] = green / weight; nextColor[p + 2] = blue / weight;
    }
    normals = nextNormal; colors = nextColor;
  }
  for (let i = 0; i < count; i++) {
    const p = i * 3;
    const nx = sourceNormal[p] * .35 + normals[p] * .65;
    const ny = sourceNormal[p + 1] * .35 + normals[p + 1] * .65;
    const nz = sourceNormal[p + 2] * .35 + normals[p + 2] * .65;
    const length = Math.hypot(nx, ny, nz) || 1;
    normal.setXYZ(i, nx / length, ny / length, nz / length);
    color.setXYZ(i, colors[p], colors[p + 1], colors[p + 2]);
  }
  normal.needsUpdate = true;
  color.needsUpdate = true;
  return geometry;
}
