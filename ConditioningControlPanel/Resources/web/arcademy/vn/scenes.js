/* ============================================================================
 * vn/scenes.js - FIRST BELL as DATA. Pure: this file imports nothing and
 * exports frozen plain objects, so it can be read, diffed and asserted without
 * a DOM. All behaviour lives in index.js.
 *
 * A SCENE is {id, image, motion, steps}. A STEP is one of:
 *   {caption:<lex key>}   lower third card, waits for a tap / click / Enter
 *   {paper:<PAPERS key>}  the cream slip, waits for a tap / click / Enter
 *   {hold:<ms>}           a beat of nothing (a threshold, a neon flicker)
 *   {fx:<name>}           a one-shot class on the frame ('neon' today)
 *   {board:true}          THE HANDOFF: the live split-flap board mounts into
 *                         the reserved wall zone and deals tonight's slots
 *
 * MOTION is the slow camera on the still: 'still' | 'push' | 'pan'. Under
 * reduced motion every one of them renders as a static frame (index.js adds
 * `.is-reduced`, which zeroes the transforms and the fades).
 *
 * THE RESERVED WALL ZONE (SET-NOTES, owner order: the board is NEVER baked into
 * art) is percentages OF THE 16:9 FRAME, not of the window: x 37.5-62.5%,
 * y 21.6-45.2% of `art/vn/vn-02-entrance-hall.png`. index.js positions the
 * real `shell/splitflap.js` board there and scales it to fit.
 *
 * THE NUMBERS FOLLOW THE PAINT, NOT THE OTHER WAY AROUND. The 2026-08-24 art
 * re-gen (text-hygiene pass - the first plates grew gibberish on every notice
 * and cabinet) landed the bare warm panel at x 516-860, y 166-347 of the
 * 1376x768 plate: narrower and further right than the first plate's
 * x 25-60% / y 18-48%, which now straddles the left doorway. These fractions
 * were measured off the new panel. Re-measure if set-02 is ever re-generated.
 * ==========================================================================*/

/** The reserved mount zone on set-02, as fractions of the letterboxed frame. */
export const BOARD_ZONE = Object.freeze({ x: 0.375, y: 0.216, w: 0.250, h: 0.236 });

/** Backgrounds, relative to the arcademy web root. */
export const ART = Object.freeze({
  gates: 'art/vn/vn-01-entrance-gates.png',
  hall: 'art/vn/vn-02-entrance-hall.png',
  midway: 'art/vn/vn-03-midway-101-104.png',
  homeroom: 'art/vn/vn-04-homeroom-101.png',
});

/**
 * THE COLD OPEN: s01 (the gates) then s02 (the desk) then the board handoff.
 * One flight, two ledger entries - a player who closes the app between them
 * comes back with s01 spent and s02 still armed, which is the beat sheet's
 * "the ledger simply leaves that scene armed" rule.
 */
export const COLD_OPEN = Object.freeze([
  Object.freeze({
    id: 's01',
    image: ART.gates,
    motion: 'still',
    steps: Object.freeze([
      Object.freeze({ caption: 'vn_s01_cap1' }),
      Object.freeze({ hold: 900 }),
      Object.freeze({ fx: 'neon' }),
      Object.freeze({ caption: 'vn_s01_cap2' }),
    ]),
  }),
  Object.freeze({
    id: 's02',
    image: ART.hall,
    motion: 'push',
    steps: Object.freeze([
      Object.freeze({ hold: 900 }),
      Object.freeze({ paper: 'p1' }),
      /* B4. The painting becomes the app: no caption, and the VN's input claim
       * ends the moment the flaps stop. */
      Object.freeze({ board: true }),
    ]),
  }),
]);

/**
 * THE WALK. One caption on the midway, then a threshold hold on the empty
 * Homeroom, then the shipped class takeover - untouched, and the very next
 * thing the player sees.
 */
export const WALK = Object.freeze({
  id: 's03',
  image: ART.midway,
  motion: 'pan',
  steps: Object.freeze([
    Object.freeze({ caption: 'vn_s03_cap', autoMs: 4000 }),
    Object.freeze({ swap: ART.homeroom, motion: 'still' }),
    Object.freeze({ hold: 1000 }),
  ]),
});

/**
 * PAPER #2. No background: the slip slides out from under the LIVE board on
 * whatever screen the player is standing on, because the paper defers to the
 * machine (B11's staging note).
 */
export const MAIL = Object.freeze({
  id: 'm01',
  image: null,
  motion: 'still',
  steps: Object.freeze([
    Object.freeze({ paper: 'p2' }),
  ]),
});

/** Every scene id the ledger can hold, in the order a first night meets them. */
export const SCENE_IDS = Object.freeze(['s01', 's02', 's03', 'm01']);

export default { COLD_OPEN, WALK, MAIL, ART, BOARD_ZONE, SCENE_IDS };
