/** Live-rally multiplier, adapted from the parked feel-3 target-speed ramp.
 * Adds 0.4% of the selected base pace per active second, capped at 30%.
 * The game owns the clock so pauses, slow motion and lost rallies stay coherent. */
export const RALLY_RAMP_S = 75, RALLY_MAX = .30;
export function rallyBoost(seconds) {
  const elapsed = Math.max(0, Math.min(RALLY_RAMP_S, Number(seconds) || 0));
  return 1 + RALLY_MAX * elapsed / RALLY_RAMP_S;
}
