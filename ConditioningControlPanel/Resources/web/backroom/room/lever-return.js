// Sculpture-only flex after the lever stops: a small overshoot and a tiny correction.
export const SILICONE_SETTLE_MS = 90;
export function siliconeRebound(ms) {
  if (ms <= 0 || ms >= SILICONE_SETTLE_MS) return 0;
  if (ms < 55) return -0.06 * Math.sin(ms / 55 * Math.PI);
  return 0.006 * Math.sin((ms - 55) / 35 * Math.PI);
}

// The entire lever briefly travels behind rest, without a forward rebound.
export function customLeverReturn(ms) {
  if (ms <= 0 || ms >= SILICONE_SETTLE_MS) return 0;
  return -0.09 * Math.sin(ms / SILICONE_SETTLE_MS * Math.PI);
}
