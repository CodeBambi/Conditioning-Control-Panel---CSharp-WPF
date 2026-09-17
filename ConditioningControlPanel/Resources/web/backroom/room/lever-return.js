// Sculpture-only flex after the lever stops: a small overshoot and a tiny correction.
export const SILICONE_SETTLE_MS = 180;
export function siliconeRebound(ms) {
  if (ms <= 0 || ms >= SILICONE_SETTLE_MS) return 0;
  if (ms < 110) return -0.06 * Math.sin(ms / 110 * Math.PI);
  return 0.006 * Math.sin((ms - 110) / 70 * Math.PI);
}
