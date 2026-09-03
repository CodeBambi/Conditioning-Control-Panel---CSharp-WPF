// Reaction duel — port of Services/GoonGame/Rounds/ReactionDuelRound.cs.
//
// The universal round: used whenever either side is NoCam (a staring contest needs two cameras) and
// as the caps fallback every client must support. After a seed-determined 2-6 s wait — sprinkled
// with seed-determined feints once difficulty climbs — the real stimulus fires and the lower
// reaction time wins.
//
// Fairness: the stimulus instant and the input instant are both stamped on the LOCAL stopwatch and
// only the delta crosses the wire, so latency never enters the measurement. If the presenter raises
// inputs.reaction.onStimulusRendered, the baseline is rebased onto the frame that actually hit the
// screen. Deltas below GoonConsts.SuspectReactionMs are scored but flagged suspect.
//
// Pressing before the real stimulus (feints included) is a false start and loses the round outright.

import { GoonConsts, GoonRoundKind, makeRoundResult } from '../contracts.js';
import {
  GoonRoundConsts, GoonStimulusKind, INT_MAX, clamp, createStopwatch, deferred, delay, invokeUi,
  present, signalAsync, subscribe,
} from './model.js';

export const kind = GoonRoundKind.ReactionDuel;

/** Progress value that marks a false start, for the recap badge. */
export const PROGRESS_FALSE_START = 1;

/**
 * DRAW ORDER (normative): the REAL delay first, then one draw per feint. Feints only exist from
 * difficulty 2 (count = clamp(difficulty-1, 0, 3)) and are always at least 400 ms clear of the real
 * stimulus, so they can never be mistaken for it by timing alone.
 *
 * The `earliest < latest` guard is part of the draw order too: once the window closes the loop
 * stops early and the remaining feints consume NO draws.
 */
export function buildSpec(rng, difficulty) {
  const delayMs = rng.nextInt(GoonRoundConsts.ReactionMinDelayMs, GoonRoundConsts.ReactionMaxDelayMs + 1);

  const decoyCount = clamp(difficulty - 1, 0, GoonRoundConsts.ReactionMaxDecoys);
  const decoys = [];
  let earliest = 400;
  const latest = delayMs - 400;
  for (let i = 0; i < decoyCount && earliest < latest; i++) {
    const offset = rng.nextInt(earliest, latest);
    decoys.push(offset);
    earliest = offset + 300;    // keep feints apart and ordered
  }

  return {
    delayMs,
    maxResponseMs: GoonRoundConsts.ReactionMaxResponseMs,
    difficulty,
    decoyOffsetsMs: decoys,
  };
}

export async function runAsync(ctx, signal) {
  if (!ctx) throw new Error('reactionDuel.runAsync: ctx required');

  const spec = buildSpec(ctx.rng, ctx.difficulty);
  const sw = createStopwatch();
  const falseStart = deferred();
  const pressed = deferred();     // reaction ms, already baseline-corrected

  let realFired = false;
  let settled = false;
  let baselineMs = 0;             // sw elapsed at the moment the stimulus was presented

  const feed = ctx.inputs && ctx.inputs.reaction;

  const offPress = subscribe(feed, 'onInputPressed', () => {
    const raw = sw.elapsedMs;
    if (settled) return;
    settled = true;
    if (!realFired) { falseStart.resolve(true); return; }
    pressed.resolve(Math.max(0, raw - baselineMs));
  });

  const offRendered = subscribe(feed, 'onStimulusRendered', () => {
    const at = sw.elapsedMs;
    // Only meaningful between the real stimulus firing and the first input.
    if (settled || !realFired) return;
    baselineMs = at;
  });

  try {
    present(ctx, 'armReactionDuel', spec);
    sw.restart();

    // Feints. Any input during this stretch has already settled as a false start.
    for (const decoyAt of spec.decoyOffsetsMs) {
      if (!(await waitElapsed(sw, decoyAt, falseStart, signal))) return falseStartResult(ctx, sw);
      present(ctx, 'fireReactionStimulus', GoonStimulusKind.Decoy);
    }

    if (!(await waitElapsed(sw, spec.delayMs, falseStart, signal))) return falseStartResult(ctx, sw);

    // The real one. The baseline is taken in the same turn as the render call, then refined by
    // onStimulusRendered when the presenter provides it.
    const p = ctx.presenter;
    invokeUi(() => {
      if (p && typeof p.fireReactionStimulus === 'function') p.fireReactionStimulus(GoonStimulusKind.Real);
      if (settled) return;
      baselineMs = sw.elapsedMs;
      realFired = true;
    }, 'fireReactionStimulus', ctx.logger);

    if (!realFired && !settled) {
      // Presenter threw before the flags were set: still run the round honestly.
      baselineMs = sw.elapsedMs;
      realFired = true;
    }

    const got = await signalAsync(pressed.promise, spec.maxResponseMs, signal);
    if (!got) {
      settled = true;
      log(ctx, 'info', `reaction duel round ${ctx.roundNo} — no input within ${spec.maxResponseMs}ms`);
      return makeRoundResult({
        round_no: ctx.roundNo,
        completed: false,
        elapsed_ms: Math.min(INT_MAX, sw.elapsedMs),
        reaction_ms: null,
      });
    }

    const reactionMs = pressed.value;
    const suspect = reactionMs < GoonConsts.SuspectReactionMs;
    if (suspect) {
      log(ctx, 'warn', `reaction ${reactionMs}ms is below the suspect floor ` +
        `(${GoonConsts.SuspectReactionMs}ms) — scoring it, badged`);
    }

    return makeRoundResult({
      round_no: ctx.roundNo,
      completed: true,
      elapsed_ms: Math.min(INT_MAX, sw.elapsedMs),
      reaction_ms: reactionMs,
      suspect,
    });
  } finally {
    offPress();
    offRendered();
    present(ctx, 'endReactionDuel');
  }
}

function falseStartResult(ctx, sw) {
  log(ctx, 'info', `reaction duel round ${ctx.roundNo} lost to a false start at ${sw.elapsedMs}ms`);
  return makeRoundResult({
    round_no: ctx.roundNo,
    completed: false,
    elapsed_ms: Math.min(INT_MAX, sw.elapsedMs),
    reaction_ms: null,
    progress: PROGRESS_FALSE_START,
  });
}

/** Waits until the round stopwatch passes targetMs. False if the false-start signal fired first. */
async function waitElapsed(sw, targetMs, abort, signal) {
  for (;;) {
    if (abort.isResolved) return false;
    const remaining = targetMs - sw.elapsedMs;
    if (remaining <= 0) return true;
    await delay(Math.min(remaining, 50), signal);
  }
}

function log(ctx, level, msg) {
  const l = ctx.logger || (typeof console !== 'undefined' ? console : null);
  if (l && l[level]) l[level](`[GG] ${msg}`);
}

export const reactionDuelRound = Object.freeze({ kind, buildSpec, runAsync });
