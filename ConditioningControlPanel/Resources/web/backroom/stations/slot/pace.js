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
/** Reel `i` (0..2) ends its travel and thuds; the last thud lands; one outcome, spin start to next spin start.
 *  `holdMs` is A1's anticipation hold (ANTICIPATION below), reel 3 only and 0 on every other spin. */
export const reelStopMs = (i, p = PACE, holdMs = 0) => p.SPIN_MS + i * p.STAGGER_MS + (i === 2 ? Math.max(0, holdMs || 0) : 0);
export const reelsMs = (p = PACE, holdMs = 0) => reelStopMs(2, p, holdMs) + p.THUD_MS;

/* C1 THE EMI PAIR RE-SPIN (CONTRACT 10.16.D). Reels 1 and 2 stay exactly where they are and reel 3 alone
 * comes back, so there is no stagger to wait through: SPIN_MS, then the FULL 1,400 ms gold hold (never
 * halved here, not even while melted), then THE THUD. The hold is pace, not animation, so reduced motion and
 * Calm keep it like every other duration above. */
export const respinStopMs = (p = PACE, holdMs = ANTICIPATION.emi) => p.SPIN_MS + Math.max(0, holdMs || 0);
export const respinMs = (p = PACE, holdMs = ANTICIPATION.emi) => respinStopMs(p, holdMs) + p.THUD_MS;

/** One outcome, spin start to next spin start. `kind` is the tape's own kind: an `emi_respin` is reel 3
 *  alone (about 4,660 ms with today's PACE), every other kind is all three reels. */
export const outcomeMs = (p = PACE, holdMs = 0, kind = null) =>
  (kind === 'emi_respin' ? respinMs(p, holdMs || ANTICIPATION.emi) : reelsMs(p, holdMs)) + p.REVEAL_MS + p.BREATH_MS;

/* A1 THE ANTICIPATION REEL (playbook Tier A, CONTRACT 10.15). When reels 1 and 2 land a live pair,
 * reel 3 keeps spinning this much longer before its thud. The tape already carries the outcome, so a
 * hold is PURELY a delay: nothing here can change what lands. Reduced motion keeps the hold like every
 * other duration above; only the animation inside it is calmer. Brake 5 halves it while melted. */
export const ANTICIPATION = Object.freeze({
  none: 0,
  gif: 900,        // the same gif on both reels (gif3same is the live one)
  spiral: 1100,    // two spirals, any mix
  sub: 1100,       // two subliminals, any mix
  emi: 1400,       // two EMI: the cabinet goes gold
});
