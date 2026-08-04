// Bubble race — port of Services/GoonGame/Rounds/BubbleRaceRound.cs.
//
// Third rung of the ladder: an identical swarm on both machines. First to clear it wins; if nobody
// clears it inside the timeout the higher pop count wins.
//
// The round does NOT drive a bubble service. It emits a layout the presenter spawns and consumes
// pops through inputs.bubbles — keeping the swarm identical on both sides regardless of either
// player's local bubble settings.

import { GoonRoundKind, makeRoundResult } from '../contracts.js';
import {
  GoonRoundConsts, createStopwatch, deferred, present, signalAsync, subscribe,
} from './model.js';

export const kind = GoonRoundKind.BubbleRace;

/**
 * DRAW ORDER (normative), per bubble and in this order:
 *   normX -> normY -> scale -> spawn offset -> drift angle -> drift speed
 * Only the COUNT depends on difficulty (6 + 3 per level, cap 18) — but note the spawn-offset RANGE
 * and the drift-speed range are difficulty-scaled, which changes the rejection-sampling draw count
 * for the offset. Keep both expressions exactly as written.
 *
 * Positions are screen-normalized and kept off the extreme edges so every bubble is reachable on
 * any aspect ratio (and cannot hide under the HUD).
 */
export function buildSpec(rng, difficulty) {
  const count = Math.min(
    GoonRoundConsts.BubbleMaxCount,
    GoonRoundConsts.BubbleBaseCount + GoonRoundConsts.BubbleCountStep * (difficulty - 1));

  const bubbles = [];
  for (let i = 0; i < count; i++) {
    const nx = 0.08 + rng.nextDouble() * 0.84;
    const ny = 0.12 + rng.nextDouble() * 0.76;
    const scale = 0.75 + rng.nextDouble() * 0.6;
    const spawnOffset = rng.nextInt(0, 1500 + 250 * difficulty);
    const angle = rng.nextDouble() * 360.0;
    const speed = 0.4 + rng.nextDouble() * (0.4 + 0.2 * difficulty);

    bubbles.push({
      index: i,
      normX: nx,
      normY: ny,
      scale,
      spawnOffsetMs: spawnOffset,
      driftAngleDeg: angle,
      driftSpeed: speed,
    });
  }

  return {
    count,
    timeoutMs: GoonRoundConsts.BubbleRaceTimeoutMs,
    difficulty,
    bubbles,
  };
}

export async function runAsync(ctx, signal) {
  if (!ctx) throw new Error('bubbleRace.runAsync: ctx required');

  const spec = buildSpec(ctx.rng, ctx.difficulty);
  const sw = createStopwatch();
  const cleared = deferred();
  const popped = new Set();

  const feed = ctx.inputs && ctx.inputs.bubbles;
  const offPopped = subscribe(feed, 'onBubblePopped', (e) => {
    const index = e && typeof e.index === 'number' ? e.index : -1;
    // Ignore indices outside the spec and double-reports of the same bubble.
    if (index < 0 || index >= spec.count) return;
    if (popped.has(index)) return;
    popped.add(index);
    if (popped.size >= spec.count) cleared.resolve(true);
  });

  try {
    present(ctx, 'startBubbleRace', spec);
    sw.restart();

    const didClear = await signalAsync(cleared.promise, spec.timeoutMs, signal);
    sw.stop();

    const count = popped.size;
    const elapsed = didClear ? Math.min(spec.timeoutMs, sw.elapsedMs) : spec.timeoutMs;

    log(ctx, `bubble race round ${ctx.roundNo} -> ${count}/${spec.count} in ${elapsed}ms`);

    return makeRoundResult({
      round_no: ctx.roundNo,
      completed: didClear,
      elapsed_ms: elapsed,
      progress: count,
    });
  } finally {
    offPopped();
    present(ctx, 'endBubbleRace');
  }
}

function log(ctx, msg) {
  const l = ctx.logger || (typeof console !== 'undefined' ? console : null);
  if (l && l.info) l.info(`[GG] ${msg}`);
}

export const bubbleRaceRound = Object.freeze({ kind, buildSpec, runAsync });
