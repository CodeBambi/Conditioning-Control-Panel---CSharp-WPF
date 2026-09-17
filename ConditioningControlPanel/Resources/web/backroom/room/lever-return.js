// Sculpture-only flex after the lever stops: a small overshoot and a tiny correction.
export const SILICONE_SETTLE_MS = 240;
export function siliconeRebound(ms) {
  if (ms <= 0 || ms >= SILICONE_SETTLE_MS) return 0;
  if (ms < 100) return -0.10 * Math.sin(ms / 100 * Math.PI);
  if (ms < 180) return 0.05 * Math.sin((ms - 100) / 80 * Math.PI);
  return -0.025 * Math.sin((ms - 180) / 60 * Math.PI);
}

// The entire lever briefly travels behind rest, without a forward rebound.
export function customLeverReturn(ms) {
  if (ms <= 0 || ms >= 90) return 0;
  return -0.09 * Math.sin(ms / 90 * Math.PI);
}

export const LEVER_RETURN_MS = 290;
const smooth = t => { t = Math.max(0, Math.min(1, t)); return t*t*(3-2*t); };
// One continuous release: pulled angle -> behind rest -> exact rest.
export function releasedLeverAngle(ms, from) {
  if (ms <= 0) return from;
  if (ms < 220) return from + (-0.09 - from) * smooth(ms / 220);
  if (ms < LEVER_RETURN_MS) return -0.09 * (1 - smooth((ms - 220) / 70));
  return 0;
}
export function customSpinLeverAngle(ms, from) {
  // A click needs a pull; an already pulled handle must never be pulled again.
  const lead = from > 0.04 ? 0 : 120;
  if (ms < lead) return from + (0.5 - from) * smooth(ms / lead);
  return releasedLeverAngle(ms - lead, lead ? 0.5 : from);
}
export const customSpinReturnMs = from => LEVER_RETURN_MS + (from > 0.04 ? 0 : 120);
