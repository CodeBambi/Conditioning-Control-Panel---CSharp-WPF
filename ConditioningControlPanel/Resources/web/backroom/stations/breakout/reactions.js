/* ============================================================================
 * stations/breakout/reactions.js - the visual reaction registry.
 * name -> (fx, d, snapshot) => void, called at the end of render.js onEvent.
 *   fx = { P, stamps, cam, debris, shockwaves, reduced, rng, W, H, colour, sat,
 *          rungs(i), colours: { PINK, MINT, GOLD, VIOLET, WHITE, GREY },
 *          flash(v), aberr(v) }
 * Cosmetic only. Honour fx.reduced (no shake, no bursts) and fx.colour (grey is
 * payload-free).
 * ==========================================================================*/
import POWER from './reactions/power.js';

export const REACTIONS = { ...POWER };
export default REACTIONS;
