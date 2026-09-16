// Quick-draw lock card — port of Services/GoonGame/Rounds/QuickDrawRound.cs.
//
// Both players get the IDENTICAL card at the same shared-clock instant and race to type it.
// Fastest solve wins; a solve always beats a non-solve.
//
// The phrase pool is FIXED IN CODE, not drawn from the local lock-card phrase list: that list is
// per-mod and per-user, so a local draw would hand the two players different cards. Index N must be
// the same string on every client, in every language — do not edit, reorder, or localize this pool.

import { GoonRoundKind, makeRoundResult } from '../contracts.js';
import {
  GoonRoundConsts, INT_MAX, clamp, createStopwatch, deferred, present, signalAsync, subscribe,
} from './model.js';

export const kind = GoonRoundKind.QuickDrawLockCard;

/**
 * Fixed, mod-independent phrase pool — byte-identical to QuickDrawRound.PhrasePool. ASCII only
 * (these get typed), well under GoonConsts.OpponentTextMaxChars, and authored in code rather than
 * sourced from either player: no opponent-supplied text enters a sudden-death card.
 */
export const PHRASE_POOL = Object.freeze([
  'I hold the line',
  'Focus is a choice I keep making',
  'Steady hands steady breath',
  'I am still here and still counting',
  'One more breath one more second',
  'My edge is my own',
  'Slow down and stay sharp',
  'Nothing shakes me tonight',
  'I answer only to the timer',
  'Keep the rhythm keep the room',
  'Composure over everything',
  'I do not blink first',
  'Quiet mind quick hands',
  'Hold steady finish clean',
  'The clock moves I do not',
  'I outlast the noise',
]);

/**
 * DRAW ORDER (normative): phrase index, then nothing else — repeats is pure arithmetic on the
 * difficulty and must NOT consume a draw.
 */
export function buildSpec(rng, difficulty) {
  const idx = rng.nextInt(0, PHRASE_POOL.length);
  const repeats = clamp(difficulty, 1, GoonRoundConsts.QuickDrawMaxRepeats);
  return {
    phrase: PHRASE_POOL[idx],
    repeats,
    strict: false,   // never: Esc must stay mapped to Mercy
    voice: false,    // GG never forces voice; the receiver's own setting still applies
    timeLimitMs: GoonRoundConsts.QuickDrawTimeoutMs,
  };
}

export async function runAsync(ctx, signal) {
  if (!ctx) throw new Error('quickDraw.runAsync: ctx required');

  const spec = buildSpec(ctx.rng, ctx.difficulty);
  const solved = deferred();
  const abandoned = deferred();
  const sw = createStopwatch();

  const feed = ctx.inputs && ctx.inputs.lockCard;
  const offSolved = subscribe(feed, 'onSolved', (e) => solved.resolve(e || { mistakes: 0 }));
  const offAbandoned = subscribe(feed, 'onAbandoned', () => abandoned.resolve(true));

  try {
    // Render, then start the local stopwatch in the same turn: elapsed time is measured locally
    // end-to-end and only the delta ever crosses the wire.
    present(ctx, 'showLockCard', spec);
    sw.restart();

    const either = Promise.race([solved.promise, abandoned.promise]);
    const signalled = await signalAsync(either, spec.timeLimitMs, signal);
    sw.stop();

    if (!signalled) {
      log(ctx, `quick-draw round ${ctx.roundNo} timed out at ${spec.timeLimitMs}ms`);
      return makeRoundResult({ round_no: ctx.roundNo, completed: false, elapsed_ms: spec.timeLimitMs });
    }

    if (solved.isResolved) {
      return makeRoundResult({
        round_no: ctx.roundNo,
        completed: true,
        elapsed_ms: Math.min(INT_MAX, sw.elapsedMs),
        progress: (solved.value && solved.value.mistakes) | 0,
      });
    }

    // Dismissed without solving — not a mercy, just a lost card.
    return makeRoundResult({
      round_no: ctx.roundNo,
      completed: false,
      elapsed_ms: Math.min(spec.timeLimitMs, sw.elapsedMs),
    });
  } finally {
    offSolved();
    offAbandoned();
    present(ctx, 'hideLockCard');
  }
}

function log(ctx, msg) {
  const l = ctx.logger || (typeof console !== 'undefined' ? console : null);
  if (l && l.info) l.info(`[GG] ${msg}`);
}

export const quickDrawRound = Object.freeze({ kind, buildSpec, runAsync });
