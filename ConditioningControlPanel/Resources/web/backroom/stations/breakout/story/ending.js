/* ============================================================================
 * story/ending.js - the story's own ending. Owned by the act 5 lane.
 *
 * SCAFFOLD STUB. story/CONTRACT.md section 7 is the shape:
 *   playStoryEnding(host, opts) -> boolean (true = "I took the ending over")
 *
 * The lane fills the new beat here and nowhere else: the 0.9 crack from the
 * juice ladder runs across the grey office still, BREAK OUT stamps once over
 * the real world, cut to black, then the card. Reduced motion is a static crack
 * and the stamp, no shake. The house game's ending is untouched.
 * ==========================================================================*/

/**
 * @param host { el, canvas, reduced, actions, officeEnding, paintCard }
 * @param opts { wall, house }  `house` = the run ended on the house finale and
 *              the office pan out is already playing behind this call.
 * @returns true when this function owns the screen from here; false to let
 *          station.js run its ordinary ending card path.
 */
export async function playStoryEnding(host, opts = {}) {
  // The office pan out owns the screen on the house ending. Until the lane
  // fills the new beat, let it finish on its own.
  if (opts.house) return false;
  // An authored last wall has no clip behind it: the ordinary card is right.
  return false;
}

export default playStoryEnding;
