// The Caucus Race - shared constants. Import these; never redeclare them in a module.
// Track space is (d, x, h): depth along the spine, lateral offset, height above the road.
// See race/CONTRACT.md.

/** Tube radius. Must match engine/tunnel.js RADIUS so createTunnel(layout) lines up. */
export const RADIUS = 5.5;

/** How far below the spine centre the road surface sits (a flat ribbon on the tube floor). */
export const ROAD_DROP = RADIUS * 0.82;

/** Half the drivable width. The tube is 11 m across; the road is a 6.4 m ribbon. */
export const ROAD_HALF_W = 3.2;

/** Ceiling height above the road in track space (where rain bubbles start). */
export const CEILING_H = RADIUS + ROAD_DROP - 0.6;

// Kart speeds in metres per second. There is no fail state: the floor is never zero.
export const KART_BASE_SPEED = 22;
export const KART_MAX_SPEED = 34;
export const KART_MIN_SPEED = 8;
export const GRAVITY = 18;

// Pass-through pop box, metres, kart-centred.
export const POP_HIT_D = 1.4;
export const POP_HIT_X = 1.0;
export const POP_HIT_H = 1.1;

/** Combo -> score multiplier ladder: [comboAtLeast, mult]. */
export const MULT_LADDER = [[0, 1], [5, 2], [12, 3], [22, 4], [36, 6], [50, 8]];

/** Seconds without a pop before the combo lets go. */
export const COMBO_HOLD_SEC = 3.0;

/** Seconds for the run intensity to ramp from 0 to 1 (gates which bubble kinds may appear). */
export const INTENSITY_RAMP_SEC = 360;

/** Camera seat behind the cup, track space offsets. */
export const CAM_BACK = 7.5;
export const CAM_UP = 3.1;
export const CAM_LOOK_AHEAD = 9;

/** Room ids in canonical order; teagarden is always the start and the BANK. */
export const ROOM_IDS = ['teagarden', 'toybox', 'casino', 'undertow', 'mirrors', 'chapel', 'greyward', 'coronation'];

/** Tiny seeded PRNG (mulberry32) so a seed replays the same track. */
export function makeRng(seed) {
  let a = (seed >>> 0) || 0x9e3779b9;
  return function rng() {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
