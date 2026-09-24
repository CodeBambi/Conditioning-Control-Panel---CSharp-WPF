/* ============================================================================
 * core/noiseSets.js - the NOISE boards a Sort duel sorts against. PURE.
 *
 * Owner, 2026-09-24: a Sort duel opens with a VS reveal of both players' niches,
 * then each player picks a NOISE set (8 s, a random roll for a player who does
 * not pick). The Sort then deals the player's own niche pictures as the right
 * pile (keep) and their noise pictures as the left pile (bin).
 *
 * Seven safe-for-work Scrolller boards, every one checked on 2026-09-25 against
 * api.scrolller.com (PICTURE, RANDOM, limit 30: 30 of 30 usable stills each).
 * The C# host keeps the SAME table (GoonNoiseSets in GoonOnlineMedia.cs) and
 * test/selftest-noise.js fails if the two drift.
 *
 * WIRE: only the set ID crosses (`t:'duel' sub:'noise' set`). An id that is not
 * in this table clamps to '' and means "no pick". Nobody's pictures, urls or
 * board names travel: each side fetches its own noise from its own id.
 * ==========================================================================*/

export const NOISE_SETS = Object.freeze([
  Object.freeze({ id: 'architecture', sub: 'ArchitecturePorn', glyph: '\u{1F3DB}️', tint: '#b9a6ff' }),
  Object.freeze({ id: 'landscapes', sub: 'EarthPorn', glyph: '\u{1F3D4}️', tint: '#7fe0b0' }),
  Object.freeze({ id: 'space', sub: 'spaceporn', glyph: '\u{1FA90}', tint: '#8fb4ff' }),
  Object.freeze({ id: 'food', sub: 'FoodPorn', glyph: '\u{1F370}', tint: '#ffc48a' }),
  Object.freeze({ id: 'cars', sub: 'carporn', glyph: '\u{1F3CE}️', tint: '#ff8fa3' }),
  Object.freeze({ id: 'rooms', sub: 'RoomPorn', glyph: '\u{1F6CB}️', tint: '#e6c3ff' }),
  Object.freeze({ id: 'cats', sub: 'cats', glyph: '\u{1F408}', tint: '#ffd36e' }),
]);

export const NOISE_SET_IDS = Object.freeze(NOISE_SETS.map((s) => s.id));

/** How long the VS reveal holds before the pick opens. */
export const NOISE_REVEAL_MS = 2000;
/** How long the pick stays open. A player who has not picked by then gets a roll. */
export const NOISE_PICK_MS = 8000;
/** The beat after the pick closes: both picks on screen, then the class. */
export const NOISE_LOCK_MS = 700;
/** Everything before the class is up, on each side's own clock. */
export const NOISE_PRE_PLAY_MS = NOISE_REVEAL_MS + NOISE_PICK_MS + NOISE_LOCK_MS;
/** Stills one noise board fetches: plenty for the left pile of a short Sort. */
export const NOISE_STILLS = 20;

const BY_ID = new Map(NOISE_SETS.map((s) => [s.id, s]));

/** A set id from the known list, or '' (no pick / unknown / junk). */
export function clampNoiseSet(v) {
  return typeof v === 'string' && BY_ID.has(v) ? v : '';
}

/** The row for a set id, or null. */
export function noiseSet(id) { return BY_ID.get(clampNoiseSet(id)) || null; }

/** A random set id. `rand` is () => [0,1). */
export function rollNoiseSet(rand = Math.random) {
  let r = Number(rand());
  if (!Number.isFinite(r) || r < 0) r = 0;
  if (r >= 1) r = 0.999999;
  return NOISE_SET_IDS[Math.floor(r * NOISE_SET_IDS.length)];
}

export default NOISE_SETS;
