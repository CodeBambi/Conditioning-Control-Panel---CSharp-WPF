// Silent, bounded picture handoff. No extra textures, timers, audio or render loops.
export const GLITCH_SECONDS = .48;
export function screenTransition(seconds, turn = 9, still = false, pictures = 2, screen = 0) {
  const interval = Number.isFinite(turn) && turn > 0 ? turn : 9;
  // Stagger screens so a wall never flashes as one sheet.
  const clock = Math.max(0, Number.isFinite(seconds) ? seconds : 0) + screen * .73;
  const index = Math.floor(clock / interval), phase = clock % interval;
  const duration = Math.min(GLITCH_SECONDS, interval * .2);
  const p = Math.max(0, (phase - interval + duration) / duration);
  return {index, mix: pictures > 1 && p >= .5 ? 1 : 0,
    glitch: !still && pictures > 1 ? Math.sin(Math.PI * p) ** 2 : 0,
    frame: still ? 0 : Math.floor(clock * 18)};
}
