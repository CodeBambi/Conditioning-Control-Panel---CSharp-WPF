/* ============================================================================
 * stations/breakout/gamepad.js - pad input (the W3C "standard" mapping).
 *
 *   createGamepad() -> { poll(input, { menuOpen, paused, suspended }) -> { pause, start, any, moved } | null }
 *
 * poll() runs at the top of every frame, before the pause gate. No pad connected = null, zero cost.
 *   left stick X / d-pad   steer: input.left / input.right, and input.x = null exactly like the arrow
 *                          keys, so the pad never fights the mouse. The pad only writes while it is
 *                          steering and lets go ONCE when the stick centres, so a held arrow key survives.
 *   A / Cross (0)          launch on the press edge; in the menu it is { start }; while paused it resumes
 *   Start / Options (9)    { pause } on the press edge; in the menu it is { start } too
 *   any                    true on any press edge, so audio can start inside a gesture
 *   moved                  the pad steered or launched this frame (for the first-move hint)
 * ==========================================================================*/

export const DEADZONE = 0.25;
const BTN_A = 0, BTN_START = 9, DPAD_LEFT = 14, DPAD_RIGHT = 15;
const down = (b) => (b && typeof b === 'object' ? !!b.pressed || b.value > 0.5 : b > 0.5);

export function createGamepad({ nav = null } = {}) {
  const was = new Map();   // pad.index -> [pressed] of the frame before
  let steering = false;

  function poll(input, { menuOpen = false, paused = false, suspended = false } = {}) {
    const n = nav || (typeof navigator !== 'undefined' ? navigator : null);
    if (!n || typeof n.getGamepads !== 'function') return null;
    let list;
    try { list = n.getGamepads(); } catch (e) { return null; }
    let dir = 0, a = false, start = false, any = false, seen = 0;
    for (const pad of list || []) {
      if (!pad || pad.connected === false) continue;
      seen++;
      const x = pad.axes && Number.isFinite(pad.axes[0]) ? pad.axes[0] : 0;
      if (Math.abs(x) > DEADZONE) dir = x < 0 ? -1 : 1;
      const buttons = pad.buttons || [], before = was.get(pad.index) || [], next = [];
      for (let i = 0; i < buttons.length; i++) {
        next[i] = down(buttons[i]);
        if (next[i] && !before[i]) { any = true; if (i === BTN_A) a = true; else if (i === BTN_START) start = true; }
      }
      was.set(pad.index, next);
      if (next[DPAD_LEFT]) dir = -1; else if (next[DPAD_RIGHT]) dir = 1;
    }
    if (!seen) {
      if (steering && input) { input.left = input.right = false; }
      steering = false; was.clear();
      return null;
    }
    const held = menuOpen || paused || suspended;
    let moved = false;
    if (input) {
      if (dir && !held) { input.left = dir < 0; input.right = dir > 0; input.x = null; steering = true; moved = true; }
      else if (steering) { input.left = input.right = false; steering = false; }
      if (a && !held) { input.launch = true; moved = true; }
    }
    return {
      pause: !suspended && !menuOpen && (start || (a && paused)),
      start: !suspended && menuOpen && (a || start),
      any, moved,
    };
  }
  return { poll };
}
export default createGamepad;
