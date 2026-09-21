/* ============================================================================
 * stations/breakout/twists/keys-render.js - the twist's own drawing.
 * Twist: Keys and Gates (Lock and Key). a key brick opens every gate brick of its colour; the cracked key primes the clay.
 * SCAFFOLD STUB. The keys lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

/** Drawn for a brick the twist flags, inside the brick's own transform (origin = its centre). */
export function brick(ctx2d, br, snap, t) { /* the keys lane fills this in */ }

/** Drawn under the bricks, in field coordinates. */
export function under(ctx2d, snap, t) { /* the keys lane fills this in */ }

/** Drawn over everything but the HUD, in field coordinates. */
export function over(ctx2d, snap, t) { /* the keys lane fills this in */ }

export default { brick, under, over };
