// Sudden-death round model — JS port of Services/GoonGame/Rounds/GoonRoundModel.cs.
//
// A round NEVER touches a service. It derives a SPEC from the round seed + difficulty (identical
// on both machines because it is a pure function of them), renders through an injected presenter,
// and consumes input through injected feeds. Nothing in this file touches the DOM: the UI wave
// implements the presenter/inputs contracts documented below over the real overlay + services.
//
// ============================================================================================
// PRESENTER CONTRACT  (C# IGoonRoundPresenter -> duck-typed object, members camelCased 1:1)
// ============================================================================================
// Every member is optional at runtime (a missing one is skipped and logged), but a real presenter
// implements all of them. Calls are never re-entrant and never awaited — a presenter that throws is
// swallowed: a broken render must not take the match down mid-round.
//
//   showRoundIntro(intro)          intro = {roundNo, kind, difficulty, fireAtMatchMs}
//                                  Handed over as soon as the round is SCHEDULED, so the UI can
//                                  count down to fireAtMatchMs on the shared clock.
//   showLockCard(spec)             spec = LockCardSpec (below). Render the card and let the player
//                                  type; report completion through inputs.lockCard.
//   hideLockCard()                 Tear the card down (always called, even on timeout/abort).
//   startStaringContest(spec)      spec = StaringContestSpec. Run the flash barrage.
//   endStaringContest()
//   armReactionDuel(spec)          spec = ReactionDuelSpec. Show the "wait for it" state ONLY —
//                                  the round drives the stimulus timing itself.
//   fireReactionStimulus(kind)     kind = GoonStimulusKind.Decoy | .Real. Decoys are feints; the
//                                  presenter must render them indistinguishably from the real one.
//                                  If the presenter can tell when the pixels actually hit the
//                                  screen it should raise inputs.reaction.onStimulusRendered then.
//   endReactionDuel()
//   startBubbleRace(spec)          spec = BubbleRaceSpec. Spawn exactly spec.bubbles, keyed by
//                                  .index — the round scores by index, not by identity.
//   endBubbleRace()
//   showRoundVerdict(outcome)      outcome = RoundOutcome (below), raised once both results are in.
//
// ============================================================================================
// INPUTS CONTRACT  (C# IGoonRoundInputs + the four feeds -> duck-typed objects)
// ============================================================================================
// C# events become subscribe functions that RETURN AN UNSUBSCRIBE: `onFoo(fn) -> () => void`.
// Rounds always unsubscribe in their finally, so a feed must honour the returned unsub.
//
//   inputs.lockCard.onSolved(fn)              fn({mistakes})  local player solved the card.
//                                             `mistakes` is carried into the recap only — it lands
//                                             in the wire result's `progress` for QuickDraw and is
//                                             NOT part of the win check.
//   inputs.lockCard.onAbandoned(fn)           fn()            dismissed without solving (Esc). Not
//                                             a mercy — just a lost card.
//   inputs.attention.onBlinkDetected(fn)      fn()            first blink loses the staring contest.
//   inputs.attention.onAttentionSample(fn)    fn({attention01, lookingAway})  rolling attention 0..1;
//                                             the mean over the round becomes `progress` (0..100)
//                                             and breaks a both-survived tie.
//   inputs.reaction.onInputPressed(fn)        fn()            first qualifying key/click. Before the
//                                             real stimulus (feints included) this is a FALSE START:
//                                             the round ends immediately, completed=false and
//                                             progress=1 (PROGRESS_FALSE_START) as the recap badge.
//   inputs.reaction.onStimulusRendered(fn)    fn()            optional; the frame the real stimulus
//                                             actually hit the screen. The round rebases its
//                                             measurement onto that instant so presentation latency
//                                             does not count against the player.
//   inputs.bubbles.onBubblePopped(fn)         fn({index})     index into spec.bubbles; out-of-range
//                                             and duplicate indices are ignored.
//
// Reaction deltas below GoonConsts.SuspectReactionMs (100 ms) are still SCORED, but the result
// carries suspect=true so the recap can badge them. The round never rejects a fast human.
//
// ============================================================================================
// SPEC SHAPES  (C# PascalCase properties -> camelCase; the wire never sees these)
// ============================================================================================
//   LockCardSpec         {phrase, repeats, strict, voice, timeLimitMs}
//                        strict is ALWAYS false: strict mode disables Esc and Esc must stay mapped
//                        to Mercy for the whole match. There is no lockdown flag anywhere in GG.
//   FlashBeat            {offsetMs, durationMs, intensity, normX, normY, scale}   positions 0..1
//   StaringContestSpec   {durationMs, difficulty, beats: FlashBeat[]}
//   ReactionDuelSpec     {delayMs, maxResponseMs, difficulty, decoyOffsetsMs: number[]}
//   BubbleSpawn          {index, normX, normY, scale, spawnOffsetMs, driftAngleDeg, driftSpeed}
//   BubbleRaceSpec       {count, timeoutMs, difficulty, bubbles: BubbleSpawn[]}
//   RoundIntro           {roundNo, kind, difficulty, fireAtMatchMs}
//   RoundOutcome         {roundNo, kind, difficulty, verdict, netScore, local, peer,
//                         localSuspect, peerSuspect}   local/peer are round_result wire objects.
//   RoundContext         {roundNo, difficulty, seed, rng, clock, fireAtMatchMs, presenter, inputs,
//                         logger}   built by the runner once the seed is agreed.

import { localMonotonicMs } from '../clock.js';

// ------------------------------------------------------------------ consts

/** Phase-D tuning. Verbatim from GoonRoundConsts — round-internal, never on the wire. */
export const GoonRoundConsts = Object.freeze({
  /** Countdown shown before a round fires, on top of GoonConsts.MinScheduleBufferMs. */
  CountdownMs: 3000,

  // Quick draw
  QuickDrawTimeoutMs: 60000,
  QuickDrawMaxRepeats: 3,

  // Staring contest
  StaringBaseDurationMs: 20000,
  StaringDurationStepMs: 5000,
  StaringMaxDurationMs: 45000,
  StaringBaseBeats: 12,
  StaringBeatsStep: 6,
  StaringMaxBeats: 48,

  // Reaction duel
  ReactionMinDelayMs: 2000,
  ReactionMaxDelayMs: 6000,
  ReactionMaxResponseMs: 2000,
  ReactionMaxDecoys: 3,

  // Bubble race
  BubbleRaceTimeoutMs: 30000,
  BubbleBaseCount: 6,
  BubbleCountStep: 3,
  BubbleMaxCount: 18,

  // Harness
  /** How long we wait for the peer's round result after posting our own. */
  PeerResultTimeoutMs: 25000,
  /** How long a guest waits for the host's round schedule. */
  ScheduleWaitTimeoutMs: 30000,
  /** Slack before fireAt by which the peer's seed contribution must have landed. */
  SeedDeadlineSlackMs: 300,
  /** Attempts to agree a schedule before the harness gives up on the round. */
  ScheduleAttempts: 2,
  /** Gap between rounds so verdict UI can breathe. */
  InterRoundGapMs: 2500,
});

/** C# enum GoonRoundVerdict — declaration order. */
export const GoonRoundVerdict = Object.freeze({ Win: 0, Loss: 1, Draw: 2 });

/** C# enum GoonStimulusKind — declaration order. Decoy = feint, Real = the measured one. */
export const GoonStimulusKind = Object.freeze({ Decoy: 0, Real: 1 });

/** int.MaxValue — the C# rounds clamp elapsed ms to it before it becomes an int field. */
export const INT_MAX = 2147483647;

// ------------------------------------------------------------------ numeric parity

/** C# Math.Clamp. */
export function clamp(v, lo, hi) {
  return v < lo ? lo : v > hi ? hi : v;
}

/**
 * C# Math.Round(double) is banker's rounding (ties to even); JS Math.round is ties-away-from-zero.
 * The staring-contest beat offsets land on exact .5 often enough that this matters.
 */
export function roundHalfToEven(x) {
  const f = Math.floor(x);
  const diff = x - f;
  if (diff > 0.5) return f + 1;
  if (diff < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}

// ------------------------------------------------------------------ cancellation

/** Rejection used everywhere a C# OperationCanceledException would fly. */
export class GoonAbortError extends Error {
  constructor(message = 'aborted') {
    super(message);
    this.name = 'AbortError';
  }
}

export function isAbortError(e) {
  return !!e && e.name === 'AbortError';
}

export function throwIfAborted(signal) {
  if (signal && signal.aborted) throw new GoonAbortError();
}

/** Cancellable sleep. Rejects with AbortError, never resolves early. */
export function delay(ms, signal) {
  return new Promise((resolve, reject) => {
    if (signal && signal.aborted) { reject(new GoonAbortError()); return; }
    const timer = setTimeout(() => {
      if (signal) signal.removeEventListener('abort', onAbort);
      resolve();
    }, Math.max(0, ms));
    function onAbort() {
      clearTimeout(timer);
      reject(new GoonAbortError());
    }
    if (signal) signal.addEventListener('abort', onAbort, { once: true });
  });
}

// ------------------------------------------------------------------ async primitives

/**
 * GoonTcs.Create parity: a promise plus its settle function, and the synchronously-readable
 * `isResolved`/`value` the rounds use in place of C#'s Task.IsCompletedSuccessfully.
 */
export function deferred() {
  const d = { isResolved: false, value: undefined };
  d.promise = new Promise((resolve) => {
    d.resolve = (v) => {
      if (d.isResolved) return false;
      d.isResolved = true;
      d.value = v;
      resolve(v);
      return true;
    };
  });
  return d;
}

/**
 * GoonWait.SignalAsync parity: true if the signal settled first, false on timeout. Rejects with
 * AbortError if the signal (cancellation) fires — including when it fires during the timeout.
 */
export function signalAsync(promise, timeoutMs, signal) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const finish = (fn, v) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (signal) signal.removeEventListener('abort', onAbort);
      fn(v);
    };
    function onAbort() { finish(reject, new GoonAbortError()); }

    if (signal && signal.aborted) { settled = true; reject(new GoonAbortError()); return; }

    const timer = setTimeout(() => {
      if (signal && signal.aborted) finish(reject, new GoonAbortError());
      else finish(resolve, false);
    }, Math.max(0, timeoutMs));

    if (signal) signal.addEventListener('abort', onAbort, { once: true });
    Promise.resolve(promise).then(() => finish(resolve, true), () => finish(resolve, true));
  });
}

/**
 * GoonSchedule.WaitUntilMatchMsAsync parity. Re-reads the clock every slice so a mid-wait 30 s
 * re-sync is honoured instead of being baked into one long sleep.
 */
export async function waitUntilMatchMs(clock, targetMatchMs, signal) {
  if (!clock || typeof clock.nowMatchMs !== 'function') throw new Error('waitUntilMatchMs: clock required');
  const sliceMs = 100;
  for (;;) {
    throwIfAborted(signal);
    const remaining = targetMatchMs - clock.nowMatchMs();
    if (remaining <= 0) return;
    await delay(Math.min(remaining, sliceMs), signal);
  }
}

/** C# Stopwatch, ms resolution (ElapsedMilliseconds truncates). */
export function createStopwatch(now = localMonotonicMs) {
  let startedAt = 0;
  let frozen = 0;
  let running = false;
  return {
    get isRunning() { return running; },
    restart() { startedAt = now(); frozen = 0; running = true; },
    stop() { if (running) { frozen = now() - startedAt; running = false; } },
    get elapsedMs() { return Math.trunc(running ? now() - startedAt : frozen); },
  };
}

// ------------------------------------------------------------------ presenter dispatch

/**
 * GoonUi.Invoke parity. There is no dispatcher here — JS is single-threaded — so this is only the
 * "presenter exceptions never kill the match" half. A missing member is a no-op, so a partial
 * presenter (e.g. one that cannot render bubbles) still runs the round honestly.
 */
export function invokeUi(fn, what, logger) {
  try {
    fn();
  } catch (e) {
    const log = logger || (typeof console !== 'undefined' ? console : null);
    if (log && log.warn) log.warn(`[GG] sudden death: presenter call ${what} failed: ${e && e.message}`);
  }
}

/** Calls presenter[member](arg) through invokeUi; silently skips a presenter that lacks it. */
export function present(ctx, member, arg) {
  const p = ctx && ctx.presenter;
  if (!p || typeof p[member] !== 'function') return;
  invokeUi(() => p[member](arg), member, ctx.logger);
}

// ------------------------------------------------------------------ feeds

/** Subscribe-set helper: `on(fn) -> unsub`, plus a throwing-listener-safe emit. */
export function createEmitter() {
  const listeners = new Set();
  return {
    on(fn) {
      if (typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },
    emit(arg) {
      for (const fn of Array.from(listeners)) {
        try { fn(arg); } catch { /* a listener must not break the round */ }
      }
    },
    get size() { return listeners.size; },
  };
}

const noSub = () => () => {};

/** Feeds that never fire — the "no input wired yet" default. */
export function nullRoundInputs() {
  return {
    lockCard: { onSolved: noSub(), onAbandoned: noSub() },
    attention: { onBlinkDetected: noSub(), onAttentionSample: noSub() },
    reaction: { onInputPressed: noSub(), onStimulusRendered: noSub() },
    bubbles: { onBubblePopped: noSub() },
  };
}

/** Headless presenter — every member present, every member a no-op. */
export function nullRoundPresenter() {
  return {
    showRoundIntro() {},
    showLockCard() {},
    hideLockCard() {},
    startStaringContest() {},
    endStaringContest() {},
    armReactionDuel() {},
    fireReactionStimulus() {},
    endReactionDuel() {},
    startBubbleRace() {},
    endBubbleRace() {},
    showRoundVerdict() {},
  };
}

/** GoonFakeRoundInputs parity: deterministic drivers for tests and the loopback play-test. */
export function fakeRoundInputs() {
  const solved = createEmitter();
  const abandoned = createEmitter();
  const blink = createEmitter();
  const sample = createEmitter();
  const press = createEmitter();
  const rendered = createEmitter();
  const popped = createEmitter();

  return {
    lockCard: { onSolved: solved.on, onAbandoned: abandoned.on },
    attention: { onBlinkDetected: blink.on, onAttentionSample: sample.on },
    reaction: { onInputPressed: press.on, onStimulusRendered: rendered.on },
    bubbles: { onBubblePopped: popped.on },

    raiseSolved(mistakes = 0) { solved.emit({ mistakes }); },
    raiseAbandoned() { abandoned.emit(); },
    raiseBlink() { blink.emit(); },
    raiseSample(attention01, lookingAway = false) { sample.emit({ attention01, lookingAway }); },
    raisePress() { press.emit(); },
    raiseRendered() { rendered.emit(); },
    raisePop(index) { popped.emit({ index }); },
  };
}

/** Subscribes to `feed[member]` if present; returns an unsub that is always safe to call. */
export function subscribe(feed, member, fn) {
  if (!feed || typeof feed[member] !== 'function') return () => {};
  const off = feed[member](fn);
  return typeof off === 'function' ? off : () => {};
}
