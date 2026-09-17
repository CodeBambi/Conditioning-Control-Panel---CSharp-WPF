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
export function latchTravel(ms) {
  if (ms < 0 || ms >= 300) return 0;
  return ms < 100 ? 0.012 * Math.sin(ms / 100 * Math.PI / 2)
    : 0.012 * (1 - (ms - 100) / 200) ** 2 * Math.cos((ms - 100) / 200 * Math.PI * 2);
}
