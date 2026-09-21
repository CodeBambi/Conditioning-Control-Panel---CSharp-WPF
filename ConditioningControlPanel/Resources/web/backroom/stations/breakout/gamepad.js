/* ============================================================================
 * stations/breakout/gamepad.js - pad input. Filled by the haptics lane.
 *
 *   createGamepad() -> { poll(input, { menuOpen, paused, suspended }) -> { pause, start, any } | null }
 *
 * poll() runs at the top of every frame, before the pause gate. It may write
 * input.x / input.left / input.right / input.launch like the keyboard does.
 * ==========================================================================*/
export function createGamepad() {
  return { poll() { return null; } };
}
export default createGamepad;
