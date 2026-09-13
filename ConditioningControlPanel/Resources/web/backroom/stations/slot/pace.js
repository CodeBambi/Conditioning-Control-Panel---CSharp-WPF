/* pace.js - THE PACE. One outcome takes about 4 s (owner, 2026-09-13), so a small SP balance lasts.
 * The one block the feel pass tunes. Reduced motion keeps every duration (the economy pace never
 * depends on the motion setting); only the animation inside each phase is calmer. */
export const PACE = Object.freeze({
  SPIN_MS: 1800,     // lever answers, all reels blur, reel 1 starts its stop
  STAGGER_MS: 380,   // reel 2 and reel 3 stop this long after the one before
  DECEL_MS: 760,     // each reel's slow-down into its stop, inside its spin time
  THUD_MS: 340,      // THE THUD per reel (House Book), unchanged
  REVEAL_MS: 600,    // the payline glows and reads before anything else moves
  BREATH_MS: 520,    // idle cabinet life (lights chase, lever breathing) before the next spin starts
});
/** Reel `i` (0..2) ends its travel and thuds; the last thud lands; one outcome, spin start to next spin start. */
export const reelStopMs = (i, p = PACE) => p.SPIN_MS + i * p.STAGGER_MS;
export const reelsMs = (p = PACE) => reelStopMs(2, p) + p.THUD_MS;
export const outcomeMs = (p = PACE) => reelsMs(p) + p.REVEAL_MS + p.BREATH_MS;
