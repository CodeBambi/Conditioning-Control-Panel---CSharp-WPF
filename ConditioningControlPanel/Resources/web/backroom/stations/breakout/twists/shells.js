/* ============================================================================
 * stations/breakout/twists/shells.js - the pure sim half of the twist.
 * Twist: Shells (act 4, ENOUGH). Every relapse this run left an OLD SELF behind.
 * Up to five pale shells drift slowly across the field. The ball passes THROUGH
 * a shell and comes out slowed for a beat and desaturated (g.sat minus a little,
 * floor 0). Three touches pop a shell for good, with a soft release.
 * Failure subtracts, release gives back (AGENTS.md audio rule 6).
 *
 * SCAFFOLD STUB: the shape only. The act 4 lane fills it.
 * Nothing here may touch the DOM, the clock or Math.random (twists/CONTRACT.md).
 * ==========================================================================*/
export const id = 'shells';

/** Never more than five, however many relapses the run has had. */
export const MAX_SHELLS = 5;
/** Touches before a shell pops. */
export const SHELL_HP = 3;

/** The twist's own state, on its own key. Rebuilt with the board. */
function state(g) {
  if (!g.shells || g.shells.board !== g.doorBoard) {
    g.shells = { board: g.doorBoard, list: [] };
  }
  return g.shells;
}

export default {
  id,
  build(g) { g.shells = null; state(g); },
  // update(g, dt, ctx) - act 4 lane: drift, shellTouch {x,y}, shellPop {x,y,left}
};
