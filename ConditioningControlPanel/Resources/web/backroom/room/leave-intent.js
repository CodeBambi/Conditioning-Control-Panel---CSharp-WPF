/* ============================================================================
 * backroom/room/leave-intent.js - the two ways out of a seat that are not the
 * Back chip: a tap that lands on the room instead of the station (the floor, a
 * wall, another fixture, empty air), and a step backwards (S, the down arrow,
 * the touch stick pushed back).
 *
 * Neither is its own exit: scene.js hands both to the room's Back path, so a
 * spin in flight settles first (Law VI).
 *
 * PURE: no DOM, no three.js. scene.js feeds it the raycast hit and the move.
 * ==========================================================================*/

/** Walking away from the seat. Backwards only: forward and the strafes stay walking keys. */
export const BACK_KEYS = Object.freeze(['KeyS', 'ArrowDown']);

/** How far back the touch stick has to be pushed before it reads as leaving. */
export const BACK_PUSH = 0.55;

/**
 * A station's own runtime dressing, hung on the room scene instead of on the
 * fixture: the dealt cards (cards_runtime) and the wheel's reward (wheel_reward).
 * Tapping one of those is tapping the station, not the room.
 */
export const STATION_NODES = /runtime|^wheel_reward$/;

export function isBackKey(code) { return BACK_KEYS.includes(code); }

/** A stick push that means back and not a strafe: past the threshold and mostly along +z. */
export function isBackwardMove(move, threshold = BACK_PUSH) {
  const z = Number(move && move.z), x = Number(move && move.x) || 0;
  if (!Number.isFinite(z) || z < threshold) return false;
  return Math.abs(x) <= z;
}

/**
 * Did this hit land on the station you are sitting at? The fixture group (walking
 * the parents, so the lever, the rim and the mat all count) or one of its runtime
 * groups. Everything else - the floor, the walls, the props, another fixture - is
 * the room, and a tap there stands you up.
 */
export function isStationHit(object, group) {
  for (let node = object; node; node = node.parent) {
    if (group && node === group) return true;
    if (typeof node.name === 'string' && STATION_NODES.test(node.name)) return true;
  }
  return false;
}
