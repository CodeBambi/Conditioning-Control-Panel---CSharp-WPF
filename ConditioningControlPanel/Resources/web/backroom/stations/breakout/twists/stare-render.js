/* ============================================================================
 * stations/breakout/twists/stare-render.js - the twist's own drawing.
 * Twist: Stare (act 3). The eye lives UNDER the bricks, in the background, and
 * a judged brick is drawn desaturated over its own face.
 * SCAFFOLD STUB: the act 3 lane fills it. Reduced motion: the eye holds still,
 * the grey of a judged brick still reads.
 * ==========================================================================*/

/** `judged` is this twist's own flag, so its brick painter has to claim it. */
export const paintsBrick = ['judged'];

/** Inside the brick's transform: origin is its centre, drawn after the brick face. */
export function brick(/* x, br, snap, t */) { /* act 3 lane */ }
/** Field coordinates, under the bricks: the eye. */
export function under(/* x, snap, t */) { /* act 3 lane */ }

export default { brick, under, paintsBrick };
