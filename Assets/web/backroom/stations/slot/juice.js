/* Short, bounded mechanical responses. Units are reel cells or radians, never outcomes. */
const clamp = x => Math.max(0, Math.min(1, x));
export function settleCells(ms, duration = 340) {
  const q = clamp(ms / duration);
  return ms < 0 || q >= 1 ? 0 : 0.14 * Math.sin(q * Math.PI * 3) * (1 - q) ** 2;
}
export function recoilCells(ms) {
  return ms <= 0 || ms >= 100 ? 0 : -0.12 * Math.sin(Math.PI * ms / 100);
}
export function leverRebound(ms) {
  const q = clamp(ms / 200);
  return ms < 0 || q >= 1 ? 0 : -0.055 * Math.sin(q * Math.PI * 2) * (1 - q) ** 2;
}
/** The freeze latch is a TOGGLE (tester, Sep 18): it drops over 100 ms and STAYS down while its column is held;
 *  the spring-back only plays once the hold is released, spent by a spin, or moved to another button. */
export const LATCH_DEPTH = 0.012, LATCH_DOWN_MS = 100, LATCH_UP_MS = 200;
export function latchDown(ms) {
  if (ms < 0) return 0;
  return ms >= LATCH_DOWN_MS ? LATCH_DEPTH : LATCH_DEPTH * Math.sin(ms / LATCH_DOWN_MS * Math.PI / 2);
}
export function latchUp(ms) {
  if (ms < 0) return LATCH_DEPTH;
  if (ms >= LATCH_UP_MS) return 0;
  const q = ms / LATCH_UP_MS;
  return LATCH_DEPTH * (1 - q) ** 2 * Math.cos(q * Math.PI * 2);
}
/** A full tap-and-release: the down joined to the up (the pre-toggle pulse, kept for reference and tests). */
export function latchTravel(ms) {
  if (ms < 0 || ms >= LATCH_DOWN_MS + LATCH_UP_MS) return 0;
  return ms < LATCH_DOWN_MS ? latchDown(ms) : latchUp(ms - LATCH_DOWN_MS);
}
/** Where the latch sits at `now`: dropping and then parked at LATCH_DEPTH while `held`; otherwise springing
 *  back from `upAt` (the release), or at rest if it was never released. */
export function latchPose({ held, downAt = -Infinity, upAt = -Infinity }, now) {
  if (held) return latchDown(now - downAt);
  return upAt === -Infinity ? 0 : latchUp(now - upAt);
}
