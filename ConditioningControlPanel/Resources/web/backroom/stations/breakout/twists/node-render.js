/* ============================================================================
 * stations/breakout/twists/node-render.js - the twist's own drawing.
 * Twist: Node (The Hive). wire bricks powered by the core are armoured; cut a branch and it goes pale and soft.
 * SCAFFOLD STUB. The node lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(ctx2d, br, snap, t) { /* the node lane fills this in */ }

/** Drawn under the bricks, in field coordinates. */
export function under(ctx2d, snap, t) { /* the node lane fills this in */ }

/** Drawn over everything but the HUD, in field coordinates. */
export function over(ctx2d, snap, t) { /* the node lane fills this in */ }

export default { brick, under, over };
