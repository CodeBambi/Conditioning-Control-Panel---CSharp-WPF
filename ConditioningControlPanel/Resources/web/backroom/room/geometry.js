/* ============================================================================
 * backroom/room/geometry.js - where things are in the room, and nothing else.
 *
 * Every coordinate is a pixel of room/backroom_final.png (1376 x 768). The room
 * scales the picture to the window with an SVG viewBox, so these numbers never
 * change with the window size. Station rects live in stations.json (CONTRACT
 * section 7); the floor and the door live here because they belong to the art,
 * not to any station.
 *
 * PURE: no DOM, importable from node for the smoke checks.
 * ==========================================================================*/

export const ROOM_W = 1376;
export const ROOM_H = 768;

/** The walkable floor: the plum carpet inside the four walls. Convex, so a
 * straight walk between two floor points never leaves it. */
export const FLOOR = Object.freeze([[272, 200], [1106, 200], [1258, 664], [142, 664]]);

/** Where the player comes in (just inside the door, bottom middle). */
export const DOOR = Object.freeze([688, 640]);

/** Sprite scale against the art: a stool is about 40 px tall, so is the player. */
export const SPRITE_SCALE = 3.2;

/** Is [x, y] inside a convex polygon (clockwise or counter-clockwise)? */
export function inPolygon(pt, poly) {
  if (!pt || !poly || poly.length < 3) return false;
  const [x, y] = pt;
  let sign = 0;
  for (let i = 0; i < poly.length; i++) {
    const [ax, ay] = poly[i];
    const [bx, by] = poly[(i + 1) % poly.length];
    const cross = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
    if (cross === 0) continue;
    const s = cross > 0 ? 1 : -1;
    if (sign === 0) sign = s;
    else if (s !== sign) return false;
  }
  return true;
}

export const onFloor = (pt) => inPolygon(pt, FLOOR);

/** Is [x, y] inside a [x, y, w, h] rect? */
export function inRect(pt, r) {
  return !!(pt && r && r.length >= 4
    && pt[0] >= r[0] && pt[0] <= r[0] + r[2] && pt[1] >= r[1] && pt[1] <= r[1] + r[3]);
}

/**
 * Validate stations.json into the rows the room can use. A row with no id is
 * dropped; a row whose hotspot or stand is missing or off the floor keeps its
 * place in the registry but is marked `placed:false` so the room draws no
 * hotspot for it rather than one the player cannot walk to. `live` rows with no
 * `entry` are treated as `soon` (nothing to load).
 */
export function normaliseStations(rows) {
  const out = [];
  if (!Array.isArray(rows)) return out;
  for (const r of rows) {
    if (!r || typeof r.id !== 'string' || !/^[a-z0-9_]{1,24}$/.test(r.id)) continue;
    const hotspot = Array.isArray(r.hotspot) && r.hotspot.length === 4 && r.hotspot.every(Number.isFinite)
      ? r.hotspot.slice() : null;
    const stand = Array.isArray(r.stand) && r.stand.length === 2 && r.stand.every(Number.isFinite)
      ? r.stand.slice() : null;
    const live = r.state === 'live' && typeof r.entry === 'string' && /^stations\/[a-z0-9_/-]+\.js$/.test(r.entry);
    out.push({
      id: r.id,
      spot: String(r.spot || ''),
      state: live ? 'live' : 'soon',
      entry: live ? r.entry : null,
      hotspot,
      stand,
      labelKey: typeof r.labelKey === 'string' ? r.labelKey : 'br_station_' + r.id,
      placed: !!(hotspot && stand && onFloor(stand)),
    });
  }
  return out;
}

/** The station whose hotspot holds [x, y], or null. Later rows win a tie. */
export function stationAt(pt, stations) {
  let hit = null;
  for (const s of stations || []) if (s.placed && inRect(pt, s.hotspot)) hit = s;
  return hit;
}

/**
 * What a click at [x, y] means: walk to a station, walk to a floor point, or
 * nothing. Pure, so the smoke check can ask it without a mouse.
 */
export function resolveClick(pt, stations) {
  const s = stationAt(pt, stations);
  if (s) return { kind: 'station', station: s, to: s.stand };
  if (onFloor(pt)) return { kind: 'floor', to: [pt[0], pt[1]] };
  return { kind: 'none' };
}

/** Client pixels to art pixels for an SVG drawn with preserveAspectRatio meet. */
export function toArt(clientX, clientY, box) {
  const scale = Math.min(box.width / ROOM_W, box.height / ROOM_H);
  const ox = box.left + (box.width - ROOM_W * scale) / 2;
  const oy = box.top + (box.height - ROOM_H * scale) / 2;
  return [(clientX - ox) / scale, (clientY - oy) / scale];
}
