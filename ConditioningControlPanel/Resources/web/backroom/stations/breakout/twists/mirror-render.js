/* ============================================================================
 * stations/breakout/twists/mirror-render.js - the twist's own drawing.
 * Twist: Mirror (The Wardrobe). breaking a brick breaks its twin at column 15 - col, steel twin included.
 * SCAFFOLD STUB. The mirror lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(ctx2d, br, snap, t) { /* the mirror lane fills this in */ }

/** Drawn under the bricks, in field coordinates. */
export function under(ctx2d, snap, t) { /* the mirror lane fills this in */ }

/** Drawn over everything but the HUD, in field coordinates. */
export function over(ctx2d, snap, t) { /* the mirror lane fills this in */ }

export default { brick, under, over };
