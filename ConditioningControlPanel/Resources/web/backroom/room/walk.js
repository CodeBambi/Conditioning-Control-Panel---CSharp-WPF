/* ============================================================================
 * backroom/room/walk.js - where things are in the 3D room, and where you may
 * stand. The registry check, the collision test, the walk step and the
 * proximity rule. Metres, y up, the room centred on the origin.
 *
 * PURE: no DOM, no three.js, importable from node for the smoke checks.
 * Numbers from blender-scripting backroom/layout.json and placements.json.
 * ==========================================================================*/

export const EYE = 1.65;
export const RADIUS = 0.25;
export const START = Object.freeze([0, EYE, 6.5]);
/** Stand within this many metres of an approach point and E visits. */
export const VISIT_RANGE = 1.65;
export const WALK_SPEED = 3.575;
export const RUN_SPEED = 4.8;

/** Walls (inner faces less the radius) and the architecture no fixture bounds cover. */
export const WALLS = Object.freeze({ x: 6.65, z: 7.55 });
export const BLOCKERS = Object.freeze([
  Object.freeze({ min: [6.35, 5.35], max: [7.95, 7.72] }), // customization cabinet
  Object.freeze({ min: [-4.6, -7.4], max: [-3.6, -6.3] }), // northwest sculpture
  Object.freeze({ min: [3.6, -7.4], max: [4.6, -6.3] }), // northeast sculpture
  Object.freeze({ min: [3.2, 6.7], max: [4.2, 7.75] }), // entrance sculpture
  Object.freeze({ min: [-3.24, -7.44], max: [3.24, -4.06] }),   // prize counter platform
  Object.freeze({ min: [-6.54, -3.94], max: [-4.46, -0.06] }),  // wheel plinth
]);

const ID = /^[a-z0-9_]{1,24}$/;
const FILE = /^[a-z0-9_-]{1,40}\.glb$/;
const ENTRY = /^stations\/[a-z0-9_/-]+\.js$/;
const vec3 = (v) => Array.isArray(v) && v.length === 3 && v.every(Number.isFinite);

/**
 * Validate stations.json (CONTRACT 7, 3D shape). A row with a bad id or no
 * usable fixture is dropped. A `live` row with no local entry is demoted to
 * `soon`. `key` is unique per row (the three slots share id `slot`).
 */
export function normaliseStations(rows) {
  const out = [];
  if (!Array.isArray(rows)) return out;
  const seen = new Set();
  for (const r of rows) {
    if (!r || typeof r.id !== 'string' || !ID.test(r.id)) continue;
    const f = r.fixture;
    if (!f || typeof f.file !== 'string' || !FILE.test(f.file) || !vec3(f.position) || !vec3(r.approach) || !vec3(r.look)) continue;
    if (!f.bounds || !vec3(f.bounds.min) || !vec3(f.bounds.max)) continue;
    const variant = typeof r.variant === 'string' && ID.test(r.variant) ? r.variant : null;
    const key = variant ? r.id + ':' + variant : r.id;
    if (seen.has(key)) continue;
    seen.add(key);
    const live = r.state === 'live' && typeof r.entry === 'string' && ENTRY.test(r.entry);
    out.push({
      key, id: r.id, variant,
      name: typeof r.name === 'string' ? r.name : r.id,
      labelKey: typeof r.labelKey === 'string' ? r.labelKey : 'br_station_' + r.id,
      state: live ? 'live' : 'soon',
      entry: live ? r.entry : null,
      approach: r.approach.slice(), look: r.look.slice(),
      fixture: {
        file: f.file, position: f.position.slice(),
        yaw: Number.isFinite(f.yaw) ? f.yaw : 0,
        scale: Number.isFinite(f.scale) && f.scale > 0 ? f.scale : 1,
        heightScale: Number.isFinite(f.heightScale) && f.heightScale > 0 ? f.heightScale : 1,
        palette: f.palette && typeof f.palette === 'object' ? { ...f.palette } : null,
        preserveCharacter: f.preserveCharacter && typeof f.preserveCharacter.name === 'string' ? { ...f.preserveCharacter } : null,
        omitPrefixes: Array.isArray(f.omitPrefixes) ? f.omitPrefixes.filter((p) => typeof p === 'string') : [],
        labels: f.labels && typeof f.labels === 'object' ? { ...f.labels } : {},
        faces: !!f.faces, reels: !!f.reels, hub: !!f.hub,
        bounds: { min: f.bounds.min.slice(), max: f.bounds.max.slice() },
      },
    });
  }
  return out;
}

/** Would a body centred at (x, z) overlap a wall, the architecture or a fixture? */
export function blocked(x, z, stations) {
  if (x < -WALLS.x || x > WALLS.x + 1.2*Math.max(0,z)/8 || Math.abs(z) > WALLS.z) return true;
  for (const b of BLOCKERS) if (x > b.min[0] && x < b.max[0] && z > b.min[1] && z < b.max[1]) return true;
  for (const s of stations || []) {
    const { min, max } = s.fixture.bounds;
    if (x > min[0] - RADIUS && x < max[0] + RADIUS && z > min[2] - RADIUS && z < max[2] + RADIUS) return true;
  }
  return false;
}

/** One step, axis by axis, so a wall slides you along it instead of stopping you dead. Mutates pos. */
export function step(pos, dx, dz, stations) {
  if (!blocked(pos[0] + dx, pos[2], stations)) pos[0] += dx;
  if (!blocked(pos[0], pos[2] + dz, stations)) pos[2] += dz;
  return pos;
}

/** World-space move for a camera yaw and a local input (right, back). */
export function worldDelta(yaw, right, back) {
  return [Math.cos(yaw) * right + Math.sin(yaw) * back, -Math.sin(yaw) * right + Math.cos(yaw) * back];
}

/** The nearest station whose approach is within VISIT_RANGE, or null. */
export function nearestStation(pos, stations) {
  let best = null, dist = VISIT_RANGE;
  for (const s of stations || []) {
    const d = Math.hypot(pos[0] - s.approach[0], pos[2] - s.approach[2]);
    if (d < dist) { dist = d; best = s; }
  }
  return best;
}

/** Yaw and pitch that look from `from` at `to` (three.js YXZ, -z forward). */
export function facing(from, to) {
  const dx = to[0] - from[0], dy = to[1] - from[1], dz = to[2] - from[2];
  return { yaw: Math.atan2(-dx, -dz), pitch: Math.atan2(dy, Math.hypot(dx, dz)) };
}

/**
 * Can a walker get from `from` to `to` on the floor? A flood fill on a 10 cm
 * grid with the real collision test. For the smoke check and for the registry
 * sanity pass; the room never calls it per frame.
 */
export function reachable(from, to, stations, cell = 0.1) {
  const nx = Math.ceil((WALLS.x * 2) / cell) + 1, nz = Math.ceil((WALLS.z * 2) / cell) + 1;
  const ix = (x) => Math.round((x + WALLS.x) / cell), iz = (z) => Math.round((z + WALLS.z) / cell);
  const seen = new Uint8Array(nx * nz);
  const goal = [ix(to[0]), iz(to[2])];
  const q = [[ix(from[0]), iz(from[2])]];
  if (blocked(from[0], from[2], stations) || blocked(to[0], to[2], stations)) return false;
  seen[q[0][0] + q[0][1] * nx] = 1;
  while (q.length) {
    const [a, b] = q.pop();
    if (a === goal[0] && b === goal[1]) return true;
    for (const [da, db] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const na = a + da, nb = b + db;
      if (na < 0 || nb < 0 || na >= nx || nb >= nz || seen[na + nb * nx]) continue;
      seen[na + nb * nx] = 1;
      if (!blocked(na * cell - WALLS.x, nb * cell - WALLS.z, stations)) q.push([na, nb]);
    }
  }
  return false;
}
