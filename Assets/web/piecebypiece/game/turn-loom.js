// One Loom recipe and phase for a turn, independent of seat and polling.
import { makeRng } from '../../arcademy/core/rng.js';
import { seededParams2 } from '../../arcademy/engine/loom/seeded.js';

export const TURN_LOOM = Object.freeze({ delayMs: 10000, fullMs: 22000, fadeSec: .8 });
export function turnOpacity(ageMs) {
  const t = Math.max(0, Math.min(1, (ageMs - TURN_LOOM.delayMs) / (TURN_LOOM.fullMs - TURN_LOOM.delayMs)));
  return t * t * (3 - 2 * t);
}
export function turnRecipe(seed) {
  const recipe = seededParams2(makeRng('pbp-turn:' + seed), { centerpiece: false });
  recipe.layer.direction = recipe.layer2.direction = 1;
  recipe.speed = 1; recipe.glow = .12;
  recipe.pulse = { amp: 0, cycles: 1 };
  recipe.wobble = { amp: 0, freq: 2, cycles: 1 };
  recipe.hueCycles = 0;
  return recipe;
}
export function readTurn(game, localSeed) {
  const clock = game.clock;
  const active = clock?.snapshot().active;
  // Optimistic online moves advance the board before the server changes clocks.
  if (game.isOver() || !clock?.isRunning() || active !== game.turn()) return null;
  const ageMs = Math.max(0, clock.turnElapsedMs?.() || 0);
  const id = game.current?.matchId || localSeed;
  const key = id + '|' + game.plies() + '|' + game.rules.chess.fen();
  return { key, ageMs, alpha: turnOpacity(ageMs) };
}
