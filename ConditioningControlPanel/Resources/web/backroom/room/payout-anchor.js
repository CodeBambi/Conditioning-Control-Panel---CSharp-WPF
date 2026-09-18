/* ============================================================================
 * backroom/room/payout-anchor.js - WHERE A FIXTURE PAYS (lane BR2-room, CONTRACT 10.22.A).
 *
 * `room/fixtures.js` built its coin shower behind `if (row.id === 'slot')`, and the shower's own
 * geometry (coin-shower.js: a spread of +-.21 across, a fall from .40 to .276, a tray at z .45) is
 * written in the SLOT CABINET's local space at the slot's own fixture scale. Drop the gate and that
 * arithmetic lands inside a card table, under a roulette bowl and halfway up a wheel plinth.
 *
 * So the gate goes and this comes in: one anchor per fixture, and a host group that puts the shower's
 * OWN rest point on it. coin-shower.js is not touched (10.22.A says it is unchanged) - it keeps
 * dropping coins where it always did, and the group under it is moved so that "where it always did"
 * is this fixture's tray line.
 *
 *   PAYOUT NODES FIRST. `payout_spawn` / `payout_tray` are what the slot's CLOSE-UP model carries
 *   (stations/slot/nodes.js names them both). NOT ONE ROOM FIXTURE HAS EITHER: the room glbs were
 *   checked node by node before this file was written - slot.glb, wheel.glb, roulette.glb,
 *   card-table.glb and counter.glb carry no payout node at all. They are read for first anyway,
 *   because the day a model gains one is the day this guesswork should stop being used, and that day
 *   should need no code.
 *
 *   THEN THE FIXTURE'S OWN BOUNDS. The front face is the side the player walks up to (the row's own
 *   `approach` point, in the fixture's space), the tray is a fraction of the way up, and the coins
 *   come out at the front rather than inside the cabinet.
 *
 * CALIBRATION, so nobody has to rederive it: the Candy Rose tray sits at model-local (0, .276, .45)
 * inside a fixture whose local box is roughly y 0..1.80 and z -.59..+.63. .276 / 1.80 = .153 up, and
 * .45 is .72 of the way from the middle to the front face. Those two fractions are TRAY and FRONT
 * below, which is why the slot lands within a centimetre of where it has always landed and every
 * other fixture gets the same tray in its own proportions.
 *
 * PURE: no three, no DOM, no timers - plain {x,y,z}. fixtures.js does the measuring; this does the
 * arithmetic, and room/tests/payout-anchor.test.mjs holds it.
 * ==========================================================================*/

/** The shower's own shape, read off room/coin-shower.js. Change that file and these change with it. */
export const SHOWER = Object.freeze({
  /** Where a coin comes to rest in the shower's own space: the middle of the tray. */
  REST: Object.freeze({ x: 0, y: 0.276, z: 0.45 }),
  /** How far it spreads across the tray and how far it falls, for anyone sizing a fixture's face. */
  SPAN: 0.42,
  FALL: 0.124,
  /** The Candy Rose fixture scale. A Back Room coin is this big in the room, at every fixture. */
  REF_SCALE: 1.21,
  /** Out of the middle toward the front face (0 = dead centre, 1 = on the face). */
  FRONT: 0.72,
  /** And up from the fixture's floor, as a fraction of its height. */
  TRAY: 0.155,
  /** Authored anchors, best first. A model that carries one is believed over every fraction above. */
  NODES: Object.freeze(['payout_spawn', 'payout_tray', 'payout', 'coin_tray']),
});

const finite = (n) => Number.isFinite(n);
const point = (p) => (p && finite(p.x) && finite(p.y) && finite(p.z) ? p : null);

/** A box is usable when it is finite and has a non-degenerate span in every axis we measure from. */
export function usableBox(box) {
  if (!box || !point(box.min) || !point(box.max)) return false;
  return box.max.x > box.min.x && box.max.y > box.min.y && box.max.z > box.min.z;
}

/**
 * Which way the fixture faces, from the approach point in the FIXTURE'S OWN space. The bigger of the
 * two horizontal components wins: a player stands in front of a cabinet, never beside it. A degenerate
 * approach (dead centre, or junk) reads as +z, which is the authored front of every Back Room prop.
 */
export function frontAxis(approach) {
  const a = point(approach) || { x: 0, y: 0, z: 1 };
  const axis = Math.abs(a.x) > Math.abs(a.z) ? 'x' : 'z';
  const v = a[axis];
  return Object.freeze({ axis, sign: v < 0 ? -1 : 1 });
}

/**
 * The tray line of a fixture that authored no payout node: centred across its face, `FRONT` of the way
 * out toward the side the player walks up to, `TRAY` of the way up. In the fixture's own space.
 */
export function payoutAnchor(box, approach) {
  if (!usableBox(box)) return null;
  const { min, max } = box;
  const f = frontAxis(approach);
  const mid = { x: (min.x + max.x) / 2, y: 0, z: (min.z + max.z) / 2 };
  const half = f.axis === 'x' ? (max.x - min.x) / 2 : (max.z - min.z) / 2;
  const out = { x: mid.x, y: min.y + SHOWER.TRAY * (max.y - min.y), z: mid.z };
  out[f.axis] = mid[f.axis] + f.sign * half * SHOWER.FRONT;
  return Object.freeze(out);
}

/**
 * The host group for one fixture's shower: the transform that puts SHOWER.REST on `anchor` and keeps a
 * coin the same size in the ROOM whatever the fixture's own scale is (the wheel is 1.68, the cards .8,
 * and a coin is a coin). The height scale is undone separately, because a fixture holder may be
 * squashed in y alone (the roulette's .78) and a squashed coin reads as a bug.
 */
export function hostAt(anchor, scale = 1, heightScale = 1) {
  const a = point(anchor);
  const s = finite(scale) && scale > 0 ? scale : 1;
  const h = finite(heightScale) && heightScale > 0 ? heightScale : 1;
  if (!a) return null;
  const size = Object.freeze({ x: SHOWER.REF_SCALE / s, y: SHOWER.REF_SCALE / (s * h), z: SHOWER.REF_SCALE / s });
  return Object.freeze({
    anchor: a,
    scale: size,
    position: Object.freeze({
      x: a.x - size.x * SHOWER.REST.x,
      y: a.y - size.y * SHOWER.REST.y,
      z: a.z - size.z * SHOWER.REST.z,
    }),
  });
}

/** The whole answer for a fixture with no authored node: measure, aim, place. Null when unmeasurable. */
export function payoutHost(box, approach, scale = 1, heightScale = 1) {
  return hostAt(payoutAnchor(box, approach), scale, heightScale);
}
