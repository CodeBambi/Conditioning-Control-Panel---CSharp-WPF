import * as T from 'three';

/** Room dressing stays above head height or inside existing fixture blockers. */
export function createCasinoDecor({ scene }) {
  const root = new T.Group(); root.name = 'casino_decor'; scene.add(root);
  const materials = {
    brass: new T.MeshStandardMaterial({ color: 0xba8654, metalness: .72, roughness: .32 }),
    velvet: new T.MeshStandardMaterial({ color: 0x522644, roughness: .94, side: T.DoubleSide }),
    plum: new T.MeshStandardMaterial({ color: 0x281832, roughness: .7 }),
    leaf: new T.MeshStandardMaterial({ color: 0x367369, metalness: .15, roughness: .65, side: T.DoubleSide }),
    pink: new T.MeshStandardMaterial({ color: 0xffb2df, emissive: 0xe96dbb, emissiveIntensity: .65, roughness: .35 }),
  };
  const shapes = {
    box: new T.BoxGeometry(1, 1, 1), ball: new T.SphereGeometry(1, 10, 6),
    ring: new T.TorusGeometry(1, .022, 6, 48),
    stem: new T.CylinderGeometry(1, 1, 1, 10),
    pot: new T.CylinderGeometry(.72, 1, 1, 12),
    diamond: new T.OctahedronGeometry(1),
  };
  const leafPoints = [], leafIndices = [];
  for (let i = 0; i <= 8; i++) {
    const t = i / 8, width = Math.sin(t * Math.PI) * .13, bend = .18 * Math.sin(t * Math.PI) - .2 * t * t;
    leafPoints.push(-width, bend, t, 0, bend + .025, t, width, bend, t);
    if (i < 8) for (let j = 0; j < 2; j++) { const n = i * 3 + j; leafIndices.push(n, n + 3, n + 1, n + 1, n + 3, n + 4); }
  }
  shapes.leaf = new T.BufferGeometry(); shapes.leaf.setAttribute('position', new T.Float32BufferAttribute(leafPoints, 3)); shapes.leaf.setIndex(leafIndices); shapes.leaf.computeVertexNormals();
  const canopy = new T.Group(); canopy.name = 'casino_decor_canopy'; root.add(canopy);
  let overhead = true;
  const batches = new Map(), dummy = new T.Object3D();
  function put(shape, material, position, scale, rotation = [0, 0, 0]) {
    const key = shape + ':' + material + ':' + overhead;
    if (!batches.has(key)) batches.set(key, { shape, material, overhead, transforms: [] });
    dummy.position.fromArray(position); dummy.scale.fromArray(scale); dummy.rotation.set(...rotation); dummy.updateMatrix();
    batches.get(key).transforms.push(dummy.matrix.clone());
  }
  function rod(a, b, thickness = .018, material = 'brass') {
    const start = new T.Vector3(...a), end = new T.Vector3(...b), mid = start.clone().add(end).multiplyScalar(.5);
    const direction = end.sub(start), rotation = new T.Euler().setFromQuaternion(new T.Quaternion().setFromUnitVectors(new T.Vector3(0, 1, 0), direction.clone().normalize()));
    put('stem', material, mid.toArray(), [thickness, direction.length(), thickness], [rotation.x, rotation.y, rotation.z]);
  }
  // Open, tiered chandeliers leave the centre of every room sightline transparent.
  for (const z of [-1.5, 4.4]) {
    rod([0, 4.53, z], [0, 4.05, z], .035);
    for (const [radius, y] of [[1.2, 4.03], [.77, 3.78]]) {
      put('ring', 'brass', [0, y, z], [radius, radius, radius], [Math.PI / 2, 0, 0]);
      for (let i = 0; i < 16; i++) {
        const a = i * Math.PI / 8, x = Math.cos(a) * radius, dz = Math.sin(a) * radius;
        rod([x, y, z + dz], [x, y - .17, z + dz], .012);
        put('diamond', 'pink', [x, y - .23, z + dz], [.048, .085, .048]);
        if (i % 4 === 0) rod([x, y, z + dz], [0, 4.3, z], .014);
      }
    }
    put('ball', 'brass', [0, 4.26, z], [.08, .08, .08]);
  }
  // Repeating ceiling jewels and coving, kept close to the existing perimeter.
  for (const x of [-6.65, 6.65]) for (const z of [-6.5, -3.7, -.9, 1.9, 4.7, 7.2]) {
    put('diamond', 'brass', [x, 4.13, z], [.075, .18, .14]);
    put('ball', 'pink', [x * .993, 4.13, z], [.055, .055, .055]);
  }
  overhead = false;
  // Fan-shaped reliefs and pennants on the entrance wall.
  for (const z of [7.86]) for (const x of [-4.6, 4.6]) {
    const front = z < 0 ? .045 : -.045;
    put('box', 'velvet', [x, 3.23, z], [1.08, 1.2, .035]);
    put('diamond', 'velvet', [x, 2.64, z], [.54, .27, .023]);
    put('box', 'brass', [x, 3.85, z + front], [1.2, .045, .055]);
    for (let i = -4; i <= 4; i++) {
      const a = i * .19;
      rod([x, 3.08, z + front], [x + Math.sin(a) * .69, 3.08 + Math.cos(a) * .69, z + front], .014);
    }
    put('diamond', 'brass', [x, 2.87, z + front], [.14, .2, .025]);
    for (const side of [-1, 1]) {
      rod([x + side * .52, 3.83, z + front], [x + side * .52, 2.72, z + front], .012);
      put('ball', 'brass', [x + side * .52, 2.67, z + front], [.04, .07, .04]);
    }
  }
  // Slim jewel drops fill the clear gaps between the rear screens and counter.
  for (const x of [-3.32, 3.32]) {
    rod([x, 4.12, -7.72], [x, 3.1, -7.72], .016);
    for (let i = 0; i < 3; i++) {
      put('diamond', 'brass', [x, 3.85 - i * .28, -7.72], [.16, .19, .035]);
      put('diamond', 'pink', [x, 3.85 - i * .28, -7.675], [.065, .09, .025]);
    }
  }
  // A flush entrance runner with fine geometric inlay. No raised walking obstacles.
  for (const x of [-1.52, 1.52]) put('box', 'brass', [x, .016, 4.25], [.018, .005, 6.3]);
  for (const z of [1.1, 7.4]) put('box', 'brass', [0, .016, z], [3.05, .005, .018]);
  for (const z of [1.3, 7.2]) for (const x of [-1.3, 1.3]) {
    put('box', 'brass', [x, .019, z], [.16, .004, .16], [0, Math.PI / 4, 0]);
  }
  // Sculptural palms occupy the counter platform's already blocked front corners.
  for (const x of [-2.96, 2.96]) {
    const z = -4.4;
    put('pot', 'brass', [x, .39, z], [.2, .5, .2]);
    put('ring', 'brass', [x, .64, z], [.148, .148, .148], [Math.PI / 2, 0, 0]);
    put('stem', 'plum', [x, .66, z], [.14, .025, .14]);
    rod([x, .64, z], [x, 1.91, z], .033);
    for (let i = 0; i < 9; i++) {
      const a = i * Math.PI * 2 / 9;
      const end = [x + Math.cos(a) * .38, 1.94 + (i % 3) * .12, z + Math.sin(a) * .38];
      rod([x, 1.85, z], end, .012, 'leaf');
      put('leaf', 'leaf', [x, 1.9 + (i % 3) * .08, z], [1, 1, .68], [0, Math.PI / 2 - a, 0]);
    }
  }
  // Suit-like diamond finials link the new dressing to the casino's existing trim.
  for (const x of [-3.12, 3.12]) for (const z of [-4.45, -5.3, -6.15]) {
    put('diamond', 'brass', [x, .32, z], [.065, .12, .065]);
  }
  let instances = 0;
  for (const { shape, material, overhead, transforms } of batches.values()) {
    const mesh = new T.InstancedMesh(shapes[shape], materials[material], transforms.length);
    mesh.name = 'decor_' + shape + '_' + material;
    transforms.forEach((matrix, i) => mesh.setMatrixAt(i, matrix));
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); (overhead ? canopy : root).add(mesh); instances += transforms.length;
  }
  let time = 0;
  return {
    root,
    setOverview(on) { canopy.visible = !on; },
    update(dt, still) {
      if (still) return;
      time += Math.min(.05, Math.max(0, dt));
      materials.pink.emissiveIntensity = .65 + Math.sin(time * .55) * .08;
    },
    debug: () => ({ draws: batches.size, instances, time }),
    dispose() {
      root.removeFromParent();
      Object.values(shapes).forEach(g => g.dispose()); Object.values(materials).forEach(m => m.dispose());
    },
  };
}
