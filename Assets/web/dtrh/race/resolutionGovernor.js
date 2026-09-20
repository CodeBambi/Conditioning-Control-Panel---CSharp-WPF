/** Frame intervals include display vsync: healthy 60 Hz is 16.7 ms, never 13 ms.
 * Recover after eight healthy seconds, one rung at a time; shed load above 24 ms.
 * The gap between thresholds and the recovery hold avoid chasing brief bursts. */
export const SLOW_FRAME_MS = 24, RECOVER_FRAME_MS = 18, RECOVER_SEC = 8;
export function createResolutionGovernor({ touch = false, lite = false, floor = 0.8, liteCap = 0.6 } = {}) {
  let cap = lite ? liteCap : Infinity, healthySince = null;
  return {
    get cap() { return cap; },
    sample(avg, now, count, native) {
      if (lite || count < 60 || !Number.isFinite(avg) || avg <= 0) return cap;
      let next = cap;
      if (avg > SLOW_FRAME_MS) {
        healthySince = null;
        if (cap > 1 && native > 1) next = 1;
        else if (touch && cap > floor) next = floor;
      } else if (avg <= RECOVER_FRAME_MS && cap < native) {
        if (healthySince == null) healthySince = now;
        if (now - healthySince >= RECOVER_SEC) next = cap < 1 ? 1 : Infinity;
      } else healthySince = null;
      if (next !== cap) { cap = next; healthySince = null; }
      return cap;
    },
  };
}
