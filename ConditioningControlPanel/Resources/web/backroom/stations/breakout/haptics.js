/* ============================================================================
 * stations/breakout/haptics.js - the cabinet's touch. Filled by the haptics lane.
 *
 *   createHaptics({ ctx, reduced, enabled }) -> { onEvent(name, d, s), frame(s, now), stop(), destroy() }
 *
 * station.js calls onEvent for every game event, frame once per rendered frame
 * and stop() whenever play is held (menu, pause, suspend, close).
 * ==========================================================================*/
export function createHaptics() {
  return { onEvent() {}, frame() {}, stop() {}, destroy() {} };
}
export default createHaptics;
