// Staring contest — port of Services/GoonGame/Rounds/StaringContestRound.cs.
//
// Cam-vs-cam round: both players endure the SAME flash barrage and the first to blink loses. If
// neither blinks the round is decided on average attention, and a tie there is a draw that
// escalates into the next, harder round.
//
// Nothing from the camera crosses the wire: the blink/attention stream is consumed locally through
// inputs.attention and only the derived round_result is sent.

import { GoonRoundKind, makeRoundResult } from '../contracts.js';
import {
  GoonRoundConsts, clamp, createStopwatch, deferred, present, roundHalfToEven, signalAsync, subscribe,
} from './model.js';

export const kind = GoonRoundKind.StaringContest;

/**
 * DRAW ORDER (normative), per beat and in this order:
 *   offset jitter -> duration -> intensity -> normX -> normY -> scale
 * Only the beat COUNT and the duration depend on difficulty; the draws themselves never do.
 *
 * The jitter is scoped to the beat's own slot (spacing * 0.6) so the barrage feels organic but
 * stays evenly spread and can never bunch at the very end.
 */
export function buildSpec(rng, difficulty) {
  const durationMs = Math.min(
    GoonRoundConsts.StaringMaxDurationMs,
    GoonRoundConsts.StaringBaseDurationMs + GoonRoundConsts.StaringDurationStepMs * (difficulty - 1));

  const beatCount = Math.min(
    GoonRoundConsts.StaringMaxBeats,
    GoonRoundConsts.StaringBaseBeats + GoonRoundConsts.StaringBeatsStep * (difficulty - 1));

  const beats = [];
  const spacing = durationMs / Math.max(1, beatCount);

  for (let i = 0; i < beatCount; i++) {
    const jitter = rng.nextDouble() * spacing * 0.6;
    // C# (int)Math.Round(...) — banker's rounding, then the cast.
    const offset = Math.trunc(roundHalfToEven(i * spacing + jitter));
    const duration = 120 + rng.nextInt(0, 180);
    const intensity = 0.45 + rng.nextDouble() * 0.55;
    const nx = rng.nextDouble();
    const ny = rng.nextDouble();
    const scale = 0.6 + rng.nextDouble() * 0.8;

    beats.push({
      offsetMs: Math.min(offset, Math.max(0, durationMs - duration)),
      durationMs: duration,
      intensity,
      normX: nx,
      normY: ny,
      scale,
    });
  }

  return { durationMs, difficulty, beats };
}

export async function runAsync(ctx, signal) {
  if (!ctx) throw new Error('staringContest.runAsync: ctx required');

  const spec = buildSpec(ctx.rng, ctx.difficulty);
  const blinked = deferred();
  const sw = createStopwatch();

  // Attention samples are averaged for the both-survived tiebreak; the wire field carries 0..100.
  let attentionSum = 0;
  let attentionSamples = 0;

  const feed = ctx.inputs && ctx.inputs.attention;
  const offBlink = subscribe(feed, 'onBlinkDetected', () => blinked.resolve(true));
  const offSample = subscribe(feed, 'onAttentionSample', (e) => {
    const v = e && typeof e.attention01 === 'number' ? e.attention01 : 0;
    attentionSum += clamp(v, 0, 1);
    attentionSamples++;
  });

  try {
    present(ctx, 'startStaringContest', spec);
    sw.restart();

    const survived = !(await signalAsync(blinked.promise, spec.durationMs, signal));
    sw.stop();

    const attentionPct = attentionSamples > 0
      ? Math.trunc(roundHalfToEven((attentionSum / attentionSamples) * 100))
      : 0;

    const elapsed = survived ? spec.durationMs : Math.min(spec.durationMs, sw.elapsedMs);

    log(ctx, `staring contest round ${ctx.roundNo} -> ${survived ? 'survived' : 'blinked'} ` +
      `at ${elapsed}ms (attention ${attentionPct}%)`);

    return makeRoundResult({
      round_no: ctx.roundNo,
      completed: survived,                       // survived the full barrage without blinking
      elapsed_ms: elapsed,                       // time to blink, or the full duration
      progress: clamp(attentionPct, 0, 100),
    });
  } finally {
    offBlink();
    offSample();
    present(ctx, 'endStaringContest');
  }
}

function log(ctx, msg) {
  const l = ctx.logger || (typeof console !== 'undefined' ? console : null);
  if (l && l.info) l.info(`[GG] ${msg}`);
}

export const staringContestRound = Object.freeze({ kind, buildSpec, runAsync });
