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
/* One per door twist (twists/CONTRACT.md section 7). A lane adds a visual in its own file. */
import TWIST_CRUMBLE from './reactions/twist-crumble.js';
import TWIST_MIRROR from './reactions/twist-mirror.js';
import TWIST_KEYS from './reactions/twist-keys.js';
import TWIST_NODE from './reactions/twist-node.js';
import TWIST_JUSTONE from './reactions/twist-justone.js';
/* The two story twists (story/CONTRACT.md section 8). */
import TWIST_STARE from './reactions/twist-stare.js';
import TWIST_SHELLS from './reactions/twist-shells.js';

export const REACTIONS = { ...POWER,
  ...TWIST_CRUMBLE, ...TWIST_MIRROR, ...TWIST_KEYS, ...TWIST_NODE, ...TWIST_JUSTONE,
  ...TWIST_STARE, ...TWIST_SHELLS };
export default REACTIONS;
