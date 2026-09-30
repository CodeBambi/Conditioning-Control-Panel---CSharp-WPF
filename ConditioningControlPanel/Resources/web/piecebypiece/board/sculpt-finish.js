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
  // Cream has warm ordered channels; both body palettes have blue above green.
  // Classify from the source once so cleanup never grows a patch into the body.
  const cream = Array.from({ length: count }, (_, i) => {
    const p = i * 3;
    return sourceColor[p] >= sourceColor[p + 1] && sourceColor[p + 1] >= sourceColor[p + 2] && sourceColor[p + 1] > .1;
  });
  const creamIndices = cream.flatMap((painted, i) => painted ? [i * 3] : []);
  const creamBase = [0, 1, 2].map(channel => {
    const values = creamIndices.map(p => sourceColor[p + channel]).sort((a, b) => a - b);
    return values[Math.floor(values.length / 2)] || 0;
  });
  let normals = sourceNormal, colors = sourceColor;
  for (let pass = 0; pass < 4; pass++) {
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
        if (pass < 2 && dot >= .75) {
          nx += normals[q]; ny += normals[q + 1]; nz += normals[q + 2];
        }
        if (cream[i] !== cream[j]) continue;
        const distance = Math.hypot(sourceColor[p] - sourceColor[q], sourceColor[p + 1] - sourceColor[q + 1], sourceColor[p + 2] - sourceColor[q + 2]);
        // The cream's baked dark speckles exceed the body filter's threshold.
        // They are paint, so a geometric crease need not preserve that noise.
        if (!cream[i] && (pass >= 2 || dot < .75 || distance >= .14)) continue;
        const w = cream[i] ? 1 : 1 - distance / .14;
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
    // Real lights still shade the cream relief. Keep a quarter of its baked
    // variation so the pale areas read as clean paint, with their hue intact.
    color.setXYZ(i, ...[0, 1, 2].map(k => cream[i] ? creamBase[k] * .75 + colors[p + k] * .25 : colors[p + k]));
  }
  normal.needsUpdate = true;
  color.needsUpdate = true;
  return geometry;
}
