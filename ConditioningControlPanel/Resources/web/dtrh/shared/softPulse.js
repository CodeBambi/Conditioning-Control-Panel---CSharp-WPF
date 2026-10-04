/* ============================================================================
 * softPulse.js - the motion-aware door to the tunnel's brightness pulse.
 *
 * fx.pulseFlash() bumps the whole tunnel's brightness. Under the 'reduced'
 * motion level nothing may be brighter than a soft pulse, and under 'off' it
 * does not fire at all. Callers pass the amount they would use at full motion.
 * ==========================================================================*/

import { motionLevel } from './motion.js';

/* The brightest a full-screen pulse may be under 'reduced' (full motion uses 0.35 to 0.8). */
export const SOFT_PULSE_REDUCED_CAP = 0.25;

/* Pure: the amount to hand fx.pulseFlash for a motion level. 0 means skip. */
export function softPulseAmount(amount, level) {
  const a = Number.isFinite(amount) ? Math.max(0, amount) : 0;
  if (level === 'off') return 0;
  if (level === 'reduced') return Math.min(a, SOFT_PULSE_REDUCED_CAP);
  return a;
}

export function softPulse(fx, amount) {
  const a = softPulseAmount(amount, motionLevel());
  if (a <= 0) return;
  try { fx && fx.pulseFlash && fx.pulseFlash(a); } catch (_) { /* engine build without it */ }
}
