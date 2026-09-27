// Four opposing currents. Fixed brick identities flow with the ribbon, never respawn.
export const TIDE_ROWS = 4, TIDE_COLS = 20;
export function tidePose(row, col, age, width = 1280, height = 720, reduced = false) {
  const scale = Math.min(1, width / 1280, height / 720);
  const time = reduced ? 0 : age;
  const direction = row % 2 ? -1 : 1;
  const phase = col / (TIDE_COLS - 1) * Math.PI * 2 + time * direction * .45 + row * .75;
  const bw = 48.96 * scale, bh = 27.54 * scale;
  const cx = width / 2 + ((col - (TIDE_COLS - 1) / 2) * 54 + direction * 10 * Math.sin(time * .45)) * scale;
  const cy = (76 + row * 68 + 18 * Math.sin(phase)) * scale;
  return { x: cx - bw / 2, y: cy - bh / 2, w: bw, h: bh,
    angle: Math.atan(18 * Math.PI * 2 / ((TIDE_COLS - 1) * 54) * Math.cos(phase)) };
}
