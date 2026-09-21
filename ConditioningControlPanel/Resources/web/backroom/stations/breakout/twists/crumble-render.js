/* ============================================================================
 * stations/breakout/twists/crumble-render.js - the twist's own drawing.
 * Twist: Crumble (Pink Fog). clay takes three hits, and a precarious brick pops its precarious neighbours.
 * SCAFFOLD STUB. The crumble lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(ctx2d, br, snap, t) { /* the crumble lane fills this in */ }

/** Drawn under the bricks, in field coordinates. */
export function under(ctx2d, snap, t) { /* the crumble lane fills this in */ }

/** Drawn over everything but the HUD, in field coordinates. */
export function over(ctx2d, snap, t) { /* the crumble lane fills this in */ }

export default { brick, under, over };
