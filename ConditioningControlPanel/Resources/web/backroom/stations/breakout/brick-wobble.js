/**
 * Idle life on the wall: now and then a brick that can be hit gives a small wobble on its own (owner, 2026-09-21:
 * "randomly, just to give liveness to the scene"). Display only. It never touches the sim or the game's dice:
 * the renderer hands in its own cosmetic rng. Colour only, never under reduced motion (the caller gates both).
 */
export const WOBBLE = { everyMin: .45, everyMax: 1.5, life: .75, tilt: .075, swings: 3, squash: .05, pairChance: .25, max: 4 };
const TAU = Math.PI * 2;
/** A wobble's shape at u = 0..1 of its life: a damped swing, and a little squash that rides with it. */
export function wobbleShape(u) {
  if (!(u >= 0 && u < 1)) return { rot: 0, sx: 1, sy: 1 };
  const k = (1 - u) * (1 - u), s = Math.sin(u * TAU * WOBBLE.swings);
  return { rot: WOBBLE.tilt * s * k, sx: 1 + WOBBLE.squash * s * k, sy: 1 - WOBBLE.squash * s * k };
}
const STILL = Object.freeze({ rot: 0, sx: 1, sy: 1 });
export function createBrickWobble(rng = Math.random) {
  const live = new Map();                                   // brick -> age in seconds
  let wait = WOBBLE.everyMax;
  const pick = (bricks, can) => {
    // A few blind draws instead of filtering the whole wall every time: cheap, and a miss just means a quiet beat.
    for (let tries = 0; tries < 6; tries++) {
      const br = bricks[Math.floor(rng() * bricks.length)];
      if (br && br.alive && !live.has(br) && can(br)) return br;
    }
    return null;
  };
  return {
    /** Advance. `on` false (grey, reduced motion, a wall still landing, the finale) lets running wobbles die and starts none. */
    step(dt, bricks, on = true, can = () => true) {
      dt = Math.max(0, Math.min(.1, Number.isFinite(dt) ? dt : 0));
      for (const [br, age] of live) { const next = age + dt; if (next >= WOBBLE.life || !br.alive) live.delete(br); else live.set(br, next); }
      if (!on || !bricks?.length) { wait = Math.max(wait, WOBBLE.everyMin); return; }
      wait -= dt;
      if (wait > 0) return;
      wait = WOBBLE.everyMin + rng() * (WOBBLE.everyMax - WOBBLE.everyMin);
      const count = rng() < WOBBLE.pairChance ? 2 : 1;
      for (let i = 0; i < count && live.size < WOBBLE.max; i++) { const br = pick(bricks, can); if (br) live.set(br, 0); }
    },
    of(br) { const age = live.get(br); return age === undefined ? STILL : wobbleShape(age / WOBBLE.life); },
    get count() { return live.size; },
    reset() { live.clear(); wait = WOBBLE.everyMax; },
  };
}
