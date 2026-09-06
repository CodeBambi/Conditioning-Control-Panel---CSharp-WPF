// Racing Thoughts - shared constants. Import these; never redeclare them in a module.
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

// Drift mini-turbo (kart.js): hold drift while steering; the charge crosses these seconds to reach
// tier 1/2/3 (blue / orange / purple sparks) and releasing hands out that many seconds of boost.
export const DRIFT_TIER_SEC = [0.8, 1.6, 2.6];
export const DRIFT_BOOST_SEC = [0, 1.15, 1.35, 1.6];
/** Seconds of scrubbing the road edge before the soft wall costs a little speed (never a stop). */
export const WALL_SCRUB_SEC = 0.5;

// Pass-through pop box, metres, kart-centred. Sized for the 1.35x cup (KART_SCALE below);
// bubbles.js field.setReach(mult) widens X/H for the magnet item.
export const POP_HIT_D = 1.4;
export const POP_HIT_X = 1.15;
export const POP_HIT_H = 1.25;

/** Height of a lane bubble above the road (bubbles.js), and where the pop ring sits (kart.js). */
export const LANE_H = 0.9;

/** Combo -> score multiplier ladder: [comboAtLeast, mult]. The first rung is three pops away. */
export const MULT_LADDER = [[0, 1], [3, 2], [8, 3], [15, 4], [25, 6], [40, 8]];

/** Seconds without a pop before the combo lets go. */
export const COMBO_HOLD_SEC = 4.0;

/** Seconds for the run intensity to ramp from 0 to 1 (gates which bubble kinds may appear). */
export const INTENSITY_RAMP_SEC = 360;

/** The opening is treats only: no effect bubble rolls before this, then they trickle in over the next minute. */
export const TREATS_ONLY_SEC = 45;

/** The cup + EMI rig scale (the pitch demo cup read tiny at the chase camera). */
export const KART_SCALE = 1.35;

// ---- the road profile and what the kerb holds -------------------------------------------
// Metres from the centre line, and the x the road ribbon in rooms.js is actually drawn to: the
// asphalt runs out to KERB_INNER_W, then the kerb face steps KERB up and the chequered kerb top
// carries on to KERB_OUTER_W. ROAD_HALF_W above is the track-space lateral EXTENT (bubbles, audio
// pan, wall props), not the edge of the asphalt: it sits 0.325 m out on the kerb top.
/** Outer edge of the kerb top. */
export const KERB_OUTER_W = 3.5;
/** The kerb face: the last x that is still asphalt. */
export const KERB_INNER_W = 2.875;

/** The saucer's outer radius in rig units (race/emi.js builds the dish to it, race/menu.js keeps
 *  its own podium copy). Shrunk from 0.95 on 2026-09-06: the old dish read too wide on the road. */
export const SAUCER_R = 0.8;
/** The dish's radius once the rig is scaled up onto the road: 0.8 * 1.35 = 1.08 m. */
export const SAUCER_R_ROAD = SAUCER_R * KART_SCALE;
/** Metres of asphalt kept under the rim at the limit, so a float wobble never pokes the kerb face. */
export const KERB_KISS = 0.02;
/** How far the kart CENTRE may travel. THE KERB HOLDS THE SAUCER, NOT THE CUP: steering clamped
 *  the centre to ROAD_HALF_W, which hung the whole 1.28 m dish over the kerb and let the kerb face
 *  cut straight through it. The rim now stops on the kerb line instead.
 *  2.875 - 1.08 - 0.02 = 1.775 m. */
export const KART_X_MAX = KERB_INNER_W - SAUCER_R_ROAD - KERB_KISS;
/** The widest a bubble may sit and still be poppable with the kart parked against the kerb:
 *  KART_X_MAX plus 70 percent of the pop box's half width, 1.775 + 0.805 = 2.58 m. */
export const LANE_X_MAX = KART_X_MAX + POP_HIT_X * 0.7;

/** Camera seat behind the cup, track space offsets: low and close, the road fills the frame. */
export const CAM_BACK = 5.8;
export const CAM_UP = 2.45;
export const CAM_LOOK_AHEAD = 7;
/** Extra metres of look-ahead at the speed cap (grows with speed so boost reads as reach). */
export const CAM_LOOK_SPEED = 5;

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
