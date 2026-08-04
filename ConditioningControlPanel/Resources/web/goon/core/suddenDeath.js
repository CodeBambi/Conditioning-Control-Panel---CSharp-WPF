// Sudden death — JS port of Services/GoonGame/GoonSuddenDeath.cs (runner + GoonRoundJudge).
//
// When the endurance phase runs out with nobody having mercied, the match goes to escalating
// synchronized rounds:
//
//     QuickDrawLockCard -> StaringContest (cam+cam) | ReactionDuel (any NoCam)
//                       -> BubbleRace -> repeat at difficulty + 1
//
// Per round the two clients agree a schedule (host proposes round_no/kind/fire_at_match_ms; both
// contribute half the seed, XOR = the round seed, so neither side can pick the round it is about to
// be graded on), fire at the shared-clock instant, measure LOCALLY, exchange round_result, and
// judge the pair with the same pure function on both machines. Net +/-3 ends sudden death.
//
// Nothing here fires on message arrival: every start is a shared-clock instant at least
// GoonConsts.MinScheduleBufferMs in the future.
//
// ============================================================================================
// SEAM — how the match service (core/match.js) drives this runner
// ============================================================================================
// Mechanical PascalCase->camelCase mapping of the C# IGoonSuddenDeathRunner seam. The match owns
// the transport and the message pump; the runner NEVER subscribes to the transport itself.
//
//   match.suddenDeathRunner = runner            // property, mirrors GoonMatchService.SuddenDeathRunner
//
//   On entering sudden death, the match:
//     const offWon  = runner.onRoundWon((roundNo) => ...)         // C# RoundWon  += / -=
//     const offLost = runner.onRoundLost((roundNo) => ...)        // C# RoundLost
//     const offNet  = runner.onNetLossReached((localLost) => ...) // C# NetLossReached, true = we lost
//     runner.startAsync(context, signal)                          // C# StartAsync(ctx, ct); returns a
//                                                                 // Promise that resolves when the
//                                                                 // ladder ends (never rejects on
//                                                                 // abort — it raises onAborted)
//
//   context (C# GoonSuddenDeathContext, fields camelCased 1:1):
//     {
//       transport,           // needs sendAsync(msg) (or send(msg)); optional `clock` property
//       clock,               // MatchClock; null/undefined => transport.clock
//       rngFactory,          // (seedBigInt) => rng; null => new GoonRng(seed)
//       matchSeed,           // bigint (informational here; per-round seeds are re-XOR'd)
//       isHost,              // bool — host proposes, guest echoes
//       localMode,           // GoonAttentionMode
//       remoteMode,          // GoonAttentionMode
//       netLossThreshold,    // GoonConsts.SuddenDeathNetLoss
//       allowedRoundKinds,   // caps intersection; ReactionDuel is always the fallback
//     }
//
//   Inbound traffic (already parsed by wire.js) is FORWARDED, never subscribed:
//     runner.handleMessage(msg)   // for msg.t === 'round' | 'round_result'; also 'mercy' while the
//                                 // phase is SuddenDeath, which stops the ladder sooner
//
//   Teardown (match end / mercy / dispose), idempotent:
//     offWon(); offLost(); offNet();
//     await runner.stopAsync();   // C# StopAsync — bounded, never throws
//
//   Outbound: everything this runner sends goes out through context.transport.sendAsync(msg) as
//   contracts.js message objects (makeRound / makeRoundResult). A send failure is survivable — the
//   schedule retry and the result timeout cover it.
//
//   Additional events beyond the C# interface (they exist on the C# class too, for the HUD/recap):
//     runner.onRoundResolved((outcome) => ...)   // every outcome incl. draws, both measurements
//     runner.onAborted((reason) => ...)          // 'schedule_timeout' | 'peer_result_timeout' |
//                                                // 'peer_mercy' | 'stopped' | 'disposed' |
//                                                // 'cancelled' | 'error: ...'
//   ABORT NEVER FABRICATES A WIN. An abort ends the ladder with no verdict; the match engine decides
//   what that means.

import {
  GoonAttentionMode, GoonConsts, GoonRoundKind, enumName, makeRound, makeRoundResult,
} from './contracts.js';
import { GoonRng, combineSeeds, newSeedContribution } from './rng.js';
import {
  GoonRoundConsts, GoonRoundVerdict, createEmitter, deferred, delay, isAbortError, nullRoundInputs,
  nullRoundPresenter, present, signalAsync, waitUntilMatchMs,
} from './rounds/model.js';
import * as quickDraw from './rounds/quickDraw.js';
import * as staringContest from './rounds/staringContest.js';
import * as reactionDuel from './rounds/reactionDuel.js';
import * as bubbleRace from './rounds/bubbleRace.js';

/**
 * Every client must be able to run a reaction duel — the universal fallback round
 * (C# GoonCapabilities.UniversalRound). Declared here rather than imported from core/caps.js so the
 * round layer stays free of the draft/caps dependency chain; both are the FROZEN protocol value
 * (GoonRoundKind.ReactionDuel, §9) and can only ever disagree via a protocol break.
 */
export const UNIVERSAL_ROUND = GoonRoundKind.ReactionDuel;

const ROUNDS = Object.freeze({
  [GoonRoundKind.QuickDrawLockCard]: quickDraw,
  [GoonRoundKind.StaringContest]: staringContest,
  [GoonRoundKind.ReactionDuel]: reactionDuel,
  [GoonRoundKind.BubbleRace]: bubbleRace,
});

function createRound(kind) {
  return ROUNDS[kind] || quickDraw;
}

// ------------------------------------------------------------------ ladder shape

/** A null/empty intersection is treated as "reaction duel only" (the caps floor). */
export function isAllowed(kind, allowed) {
  if (!allowed || allowed.length === 0) return kind === UNIVERSAL_ROUND;
  return allowed.includes(kind);
}

/**
 * The ladder shape. Pure and computed from data both sides hold identically (round number, both
 * attention modes, the caps intersection), so host and guest agree without an extra message.
 *
 * `allowed` OMITTED (undefined) = the C# 3-arg overload: the pure shape before caps.
 * `allowed` null or [] = the caps floor, i.e. reaction duel only.
 *
 * The rung ORDER and the difficulty escalation never change — a rung neither client can run is
 * substituted with the reaction duel. StaringContest therefore needs BOTH conditions: two cameras
 * AND membership in `allowed`.
 */
export function kindFor(roundNo, localMode, remoteMode, allowed) {
  const step = (Math.max(1, roundNo) - 1) % 3;
  const base = step === 0
    ? GoonRoundKind.QuickDrawLockCard
    : step === 1
      ? ((localMode === GoonAttentionMode.Cam && remoteMode === GoonAttentionMode.Cam)
        ? GoonRoundKind.StaringContest
        : GoonRoundKind.ReactionDuel)
      : GoonRoundKind.BubbleRace;

  if (allowed === undefined) return base;
  return isAllowed(base, allowed) ? base : UNIVERSAL_ROUND;
}

/** Difficulty climbs by one every full pass through the three rungs. */
export function difficultyFor(roundNo) {
  return 1 + Math.trunc((Math.max(1, roundNo) - 1) / 3);
}

// ------------------------------------------------------------------ judge

const verdictLower = (mine, theirs) =>
  mine < theirs ? GoonRoundVerdict.Win : mine > theirs ? GoonRoundVerdict.Loss : GoonRoundVerdict.Draw;

const verdictHigher = (mine, theirs) =>
  mine > theirs ? GoonRoundVerdict.Win : mine < theirs ? GoonRoundVerdict.Loss : GoonRoundVerdict.Draw;

/**
 * Pure round judgement. Called with the same two results on both machines (arguments swapped), so
 * it MUST be antisymmetric: decide(k, a, b) === Win exactly when decide(k, b, a) === Loss.
 */
export const GoonRoundJudge = Object.freeze({
  decide(kind, mine, theirs) {
    if (!mine || !theirs) return GoonRoundVerdict.Draw;

    switch (kind) {
      case GoonRoundKind.QuickDrawLockCard:
        // Solved beats unsolved; two solves go to the faster typist.
        if (mine.completed && theirs.completed) return verdictLower(mine.elapsed_ms, theirs.elapsed_ms);
        if (mine.completed) return GoonRoundVerdict.Win;
        if (theirs.completed) return GoonRoundVerdict.Loss;
        return GoonRoundVerdict.Draw;

      case GoonRoundKind.StaringContest:
        // Completed = survived the barrage without blinking.
        if (mine.completed && theirs.completed) return verdictHigher(mine.progress, theirs.progress);  // avg attention %
        if (mine.completed) return GoonRoundVerdict.Win;
        if (theirs.completed) return GoonRoundVerdict.Loss;
        return verdictHigher(mine.elapsed_ms, theirs.elapsed_ms);   // whoever blinked later

      case GoonRoundKind.ReactionDuel: {
        // Completed = clean input inside the window; a false start never completes.
        const mineOk = !!mine.completed && mine.reaction_ms !== null && mine.reaction_ms !== undefined;
        const theirsOk = !!theirs.completed && theirs.reaction_ms !== null && theirs.reaction_ms !== undefined;
        if (mineOk && theirsOk) return verdictLower(mine.reaction_ms, theirs.reaction_ms);
        if (mineOk) return GoonRoundVerdict.Win;
        if (theirsOk) return GoonRoundVerdict.Loss;
        return GoonRoundVerdict.Draw;
      }

      case GoonRoundKind.BubbleRace:
        // Cleared beats not-cleared; two clears go to the faster; otherwise most popped.
        if (mine.completed && theirs.completed) return verdictLower(mine.elapsed_ms, theirs.elapsed_ms);
        if (mine.completed) return GoonRoundVerdict.Win;
        if (theirs.completed) return GoonRoundVerdict.Loss;
        return verdictHigher(mine.progress, theirs.progress);

      default:
        return GoonRoundVerdict.Draw;
    }
  },
});

// ------------------------------------------------------------------ peer inbox

/**
 * Keyed one-shot mailbox. A message that arrives before anyone waits for it is HELD, so the protocol
 * never depends on who reached the await first (relay mode reorders freely).
 */
class PeerInbox {
  constructor() {
    this._pending = new Map();   // key -> item[]
    this._waiters = new Map();   // key -> deferred
  }

  post(key, item) {
    const waiter = this._waiters.get(key);
    if (waiter) {
      this._waiters.delete(key);
      waiter.resolve(item);
      return;
    }
    let q = this._pending.get(key);
    if (!q) { q = []; this._pending.set(key, q); }
    q.push(item);
  }

  async wait(key, timeoutMs, signal) {
    const q = this._pending.get(key);
    if (q && q.length > 0) {
      const item = q.shift();
      if (q.length === 0) this._pending.delete(key);
      return item;
    }

    let d = this._waiters.get(key);
    if (!d) { d = deferred(); this._waiters.set(key, d); }

    const got = await signalAsync(d.promise, timeoutMs, signal);
    if (!got) {
      // Re-check: a post could have landed in the same turn we timed out.
      if (d.isResolved) return d.value;
      if (this._waiters.get(key) === d) this._waiters.delete(key);
      return null;
    }
    return d.value;
  }

  clear() {
    this._pending.clear();
    this._waiters.clear();
  }
}

// ------------------------------------------------------------------ runner

export class GoonSuddenDeathRunner {
  /**
   * @param {object} [o]
   * @param {object} [o.presenter] render side (UI wave); defaults to the headless presenter
   * @param {object} [o.inputs] input feeds (UI wave); defaults to feeds that never fire
   * @param {object} [o.logger] console-shaped
   */
  constructor({ presenter = null, inputs = null, logger = null } = {}) {
    this.presenter = presenter || nullRoundPresenter();
    this.inputs = inputs || nullRoundInputs();
    /** Countdown shown before each round, on top of GoonConsts.MinScheduleBufferMs. */
    this.countdownMs = GoonRoundConsts.CountdownMs;

    this._log = logger || (typeof console !== 'undefined' ? console : null);
    this._schedules = new PeerInbox();
    this._results = new PeerInbox();
    this._myContributions = new Map();

    this._ctx = null;
    this._controller = null;
    this._unlink = null;
    this._runTask = null;
    this._disposed = false;
    this._abortReason = null;

    this._isRunning = false;
    this._netScore = 0;
    this._currentRoundNo = 0;

    this._roundWon = createEmitter();
    this._roundLost = createEmitter();
    this._netLossReached = createEmitter();
    this._roundResolved = createEmitter();
    this._aborted = createEmitter();
  }

  // ------------------------------------------------------------- state

  get isRunning() { return this._isRunning; }
  /** Net rounds from the LOCAL player's point of view: +1 per win, -1 per loss. */
  get netScore() { return this._netScore; }
  /** Round currently being scheduled/played (1-based). 0 before the first round. */
  get currentRoundNo() { return this._currentRoundNo; }

  // ------------------------------------------------------------ events

  /** Local player took the round. Arg = round number. @returns {() => void} unsub */
  onRoundWon(fn) { return this._roundWon.on(fn); }
  /** Local player dropped the round. Arg = round number. */
  onRoundLost(fn) { return this._roundLost.on(fn); }
  /** Net-loss threshold hit. Arg: true = the LOCAL player lost. */
  onNetLossReached(fn) { return this._netLossReached.on(fn); }
  /** Every round outcome including draws, with both measurements — for the HUD and recap. */
  onRoundResolved(fn) { return this._roundResolved.on(fn); }
  /** Mercy, disconnect, or a protocol stall. The match engine decides what that means. */
  onAborted(fn) { return this._aborted.on(fn); }

  // ------------------------------------------------------------- entry

  /**
   * Begins the escalating round ladder. Resolves when the ladder ends (net loss, abort, or stop) —
   * it does not reject on cancellation, it raises onAborted.
   * @param {object} context see the SEAM block at the top of this file
   * @param {AbortSignal} [signal]
   */
  startAsync(context, signal) {
    if (this._disposed) throw new Error('GoonSuddenDeathRunner: disposed');
    if (!context) throw new Error('GoonSuddenDeathRunner: context required');
    if (this._isRunning) return this._runTask || Promise.resolve();

    this._ctx = context;
    this._runTask = this._runLadder(context, signal);
    return this._runTask;
  }

  async _runLadder(ctx, outerSignal) {
    if (!ctx.transport) throw new Error('GoonSuddenDeath: no transport in context.');
    const clock = ctx.clock || ctx.transport.clock;
    if (!clock) throw new Error('GoonSuddenDeath: no match clock available.');

    const netLossThreshold = Math.max(1, ctx.netLossThreshold || GoonConsts.SuddenDeathNetLoss);

    this._isRunning = true;
    this._netScore = 0;
    this._currentRoundNo = 0;
    this._abortReason = null;

    this._controller = new AbortController();
    const token = this._controller.signal;
    if (outerSignal) {
      if (outerSignal.aborted) this._controller.abort();
      else {
        const relay = () => this._controller && this._controller.abort();
        outerSignal.addEventListener('abort', relay, { once: true });
        this._unlink = () => outerSignal.removeEventListener('abort', relay);
      }
    }

    this._info(`sudden death: starting ladder (host=${!!ctx.isHost}, modes ` +
      `${enumName(GoonAttentionMode, ctx.localMode)}/${enumName(GoonAttentionMode, ctx.remoteMode)}, ` +
      `net loss ${netLossThreshold})`);

    try {
      for (let roundNo = 1; ; roundNo++) {
        if (token.aborted) throw abortError();
        this._currentRoundNo = roundNo;

        let kind = kindFor(roundNo, ctx.localMode, ctx.remoteMode, ctx.allowedRoundKinds);
        let difficulty = difficultyFor(roundNo);

        const agreed = await this._agreeSchedule(ctx, clock, roundNo, kind, difficulty, token);
        if (agreed == null) {
          this._raiseAborted('schedule_timeout');
          return;
        }

        kind = agreed.kind;
        difficulty = agreed.difficulty;

        const intro = {
          roundNo,
          kind,
          difficulty,
          fireAtMatchMs: agreed.fireAtMatchMs,
        };
        present(this._roundCtxShell(), 'showRoundIntro', intro);

        const lateBy = clock.nowMatchMs() - agreed.fireAtMatchMs;
        if (lateBy > 0) {
          this._warn(`sudden death: round ${roundNo} schedule was already ${lateBy}ms in the past — firing now`);
        } else {
          await waitUntilMatchMs(clock, agreed.fireAtMatchMs, token);
        }

        const local = await this._runRound(ctx, kind, roundNo, difficulty, agreed.seed, agreed.fireAtMatchMs, clock, token);
        local.round_no = roundNo;

        await this._send(ctx, local, token);

        const peer = await this._results.wait(roundNo, GoonRoundConsts.PeerResultTimeoutMs, token);
        if (peer == null) {
          this._warn(`sudden death: no peer result for round ${roundNo} within ${GoonRoundConsts.PeerResultTimeoutMs}ms`);
          this._raiseAborted('peer_result_timeout');
          return;
        }

        const verdict = GoonRoundJudge.decide(kind, local, peer);
        this._netScore += verdict === GoonRoundVerdict.Win ? 1 : verdict === GoonRoundVerdict.Loss ? -1 : 0;

        const outcome = {
          roundNo,
          kind,
          difficulty,
          verdict,
          netScore: this._netScore,
          local,
          peer,
          localSuspect: !!local.suspect,
          peerSuspect: !!peer.suspect,
        };

        this._info(`sudden death: round ${roundNo} (${enumName(GoonRoundKind, kind)}, difficulty ` +
          `${difficulty}) -> ${enumName(GoonRoundVerdict, verdict)}, net ${this._netScore}`);

        present(this._roundCtxShell(), 'showRoundVerdict', outcome);
        this._raiseOutcome(outcome);

        if (this._netScore <= -netLossThreshold || this._netScore >= netLossThreshold) {
          const localLost = this._netScore <= -netLossThreshold;
          this._info(`sudden death: net loss reached after ${roundNo} rounds (local lost = ${localLost})`);
          this._netLossReached.emit(localLost);
          return;
        }

        await delay(GoonRoundConsts.InterRoundGapMs, token);
      }
    } catch (e) {
      if (isAbortError(e)) {
        // Mercy, match end, or dispose. Never a round result.
        this._raiseAborted(this._abortReason || 'cancelled');
      } else {
        this._error(`sudden death: ladder failed: ${(e && e.stack) || e}`);
        this._raiseAborted('error: ' + (e && e.message));
      }
    } finally {
      this._isRunning = false;
      this._schedules.clear();
      this._results.clear();
      if (this._unlink) { try { this._unlink(); } catch { /* gone */ } this._unlink = null; }
      this._controller = null;
    }
  }

  /**
   * Tear down immediately — mercy, abandon, or dispose. Idempotent. Mercy stays available at every
   * point of a round: the loop aborts between awaits, the running round unwinds through its finally
   * (presenter teardown, feed unsubscribe) and nothing further is raised.
   */
  async stopAsync() {
    if (this._abortReason == null) this._abortReason = 'stopped';
    try { if (this._controller) this._controller.abort(); } catch { /* already finished */ }

    const task = this._runTask;
    if (!task) return;

    try {
      // Bounded: a wedged round must not hold up the recap.
      await Promise.race([task, delay(2000)]);
    } catch (e) {
      this._warn(`sudden death: stopAsync wait threw: ${e && e.message}`);
    }
  }

  /** Round traffic forwarded by the match engine. Unrelated types are ignored. */
  handleMessage(message) {
    if (!message) return;
    switch (message.t) {
      case 'round':
        this._schedules.post(message.round_no, message);
        break;
      case 'round_result':
        this._results.post(message.round_no, message);
        break;
      case 'mercy':
        // The match engine also handles this; aborting here just stops the ladder sooner.
        this._info('sudden death: peer mercied mid-ladder — stopping rounds');
        this._abortReason = 'peer_mercy';
        try { if (this._controller) this._controller.abort(); } catch { /* gone */ }
        break;
      default:
        break;
    }
  }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;
    if (this._abortReason == null) this._abortReason = 'disposed';
    try { if (this._controller) this._controller.abort(); } catch { /* already gone */ }
    this._controller = null;
    if (this._unlink) { try { this._unlink(); } catch { /* gone */ } this._unlink = null; }
  }

  // -------------------------------------------------------- scheduling

  /**
   * Host proposes, guest echoes; the round seed is the XOR of the two contributions, so neither
   * client can choose the content on its own. A contribution is minted ONCE per round and reused
   * across schedule retries, so bumping fire_at never changes the seed.
   *
   * @returns {Promise<{kind:number,difficulty:number,fireAtMatchMs:number,seed:bigint}|null>}
   */
  async _agreeSchedule(ctx, clock, roundNo, kind, difficulty, token) {
    const mine = this._contributionFor(roundNo);

    if (ctx.isHost) {
      for (let attempt = 0; attempt < GoonRoundConsts.ScheduleAttempts; attempt++) {
        // Fire-at is always in the future by at least the schedule buffer + countdown; a retry
        // pushes it further out rather than reusing a deadline we already missed.
        const fireAt = clock.nowMatchMs() + GoonConsts.MinScheduleBufferMs + this.countdownMs + attempt * 2000;

        await this._send(ctx, makeRound({
          round_no: roundNo,
          kind,
          fire_at_match_ms: fireAt,
          seed_contribution: mine,
          difficulty,
        }), token);

        const budget = Math.max(500, fireAt - GoonRoundConsts.SeedDeadlineSlackMs - clock.nowMatchMs());
        const reply = await this._schedules.wait(roundNo, budget, token);
        if (reply != null) {
          return {
            kind,
            difficulty,
            fireAtMatchMs: fireAt,
            seed: combineSeeds(mine, reply.seed_contribution),
          };
        }

        this._warn(`sudden death: guest did not return a seed half for round ${roundNo} (attempt ${attempt + 1})`);
      }
      return null;
    }

    // Guest: wait for the proposal, answer with our half, then keep listening for a re-proposal
    // (the host bumps fire_at if our reply was slow) right up to the fire instant.
    const proposal = await this._schedules.wait(roundNo, GoonRoundConsts.ScheduleWaitTimeoutMs, token);
    if (proposal == null) {
      this._warn(`sudden death: no round ${roundNo} proposal from host within ${GoonRoundConsts.ScheduleWaitTimeoutMs}ms`);
      return null;
    }

    const expected = kindFor(roundNo, ctx.localMode, ctx.remoteMode, ctx.allowedRoundKinds);
    if (proposal.kind !== expected) {
      // Follow the host so the ladder cannot deadlock, but leave a trail: the kind is a pure
      // function of round number + both attention modes + the caps intersection, so a mismatch means
      // the two sides disagree about the modes or about the intersection. A kind outside the
      // intersection lands here too — same warn-but-follow treatment.
      const outsideCaps = !isAllowed(proposal.kind, ctx.allowedRoundKinds);
      this._warn(`sudden death: host proposed ${enumName(GoonRoundKind, proposal.kind)} for round ${roundNo}, ` +
        `expected ${enumName(GoonRoundKind, expected)}${outsideCaps ? ' (outside the caps intersection)' : ''} — following the host`);
    }

    let fireAtGuest = proposal.fire_at_match_ms;
    const seed = combineSeeds(mine, proposal.seed_contribution);

    for (;;) {
      await this._send(ctx, makeRound({
        round_no: roundNo,
        kind: proposal.kind,
        fire_at_match_ms: fireAtGuest,
        seed_contribution: mine,
        difficulty: proposal.difficulty,
      }), token);

      const untilFire = fireAtGuest - clock.nowMatchMs();
      if (untilFire <= 0) break;

      const update = await this._schedules.wait(roundNo, untilFire, token);
      if (update == null) break;              // nothing new before the fire instant — go

      fireAtGuest = update.fire_at_match_ms;   // host bumped the schedule; re-ack with the same half
    }

    return {
      kind: proposal.kind,
      difficulty: Math.max(1, proposal.difficulty),
      fireAtMatchMs: fireAtGuest,
      seed,
    };
  }

  _contributionFor(roundNo) {
    const existing = this._myContributions.get(roundNo);
    if (existing !== undefined) return existing;
    const value = newSeedContribution();
    this._myContributions.set(roundNo, value);
    return value;
  }

  // ------------------------------------------------------------ round

  async _runRound(ctx, kind, roundNo, difficulty, seed, fireAtMatchMs, clock, token) {
    const roundCtx = {
      roundNo,
      difficulty,
      seed,
      rng: createRng(ctx, seed),
      clock,
      fireAtMatchMs,
      presenter: this.presenter,
      inputs: this.inputs,
      logger: this._log,
    };

    try {
      return await createRound(kind).runAsync(roundCtx, token);
    } catch (e) {
      if (isAbortError(e)) throw e;   // mercy / match end — the loop's handler turns this into an abort
      // A broken round is a round we did not complete, not a crashed match.
      this._error(`sudden death: round ${roundNo} (${enumName(GoonRoundKind, kind)}) threw: ${(e && e.stack) || e}`);
      return makeRoundResult({ round_no: roundNo, completed: false, elapsed_ms: 0 });
    }
  }

  // ---------------------------------------------------------- plumbing

  /** A ctx-shaped shell so ladder-level presenter calls go through the same guarded path. */
  _roundCtxShell() {
    return { presenter: this.presenter, logger: this._log };
  }

  async _send(ctx, msg, token) {
    try {
      const t = ctx.transport;
      if (!t) return;
      if (typeof t.sendAsync === 'function') await t.sendAsync(msg, token);
      else if (typeof t.send === 'function') await t.send(msg);
      else throw new Error('transport exposes neither sendAsync nor send');
    } catch (e) {
      if (isAbortError(e)) throw e;
      // Losing a send is survivable: the schedule retry / result timeout paths cover it.
      this._warn(`sudden death: send of ${msg && msg.t} failed: ${e && e.message}`);
    }
  }

  _raiseOutcome(outcome) {
    this._roundResolved.emit(outcome);
    if (outcome.verdict === GoonRoundVerdict.Win) this._roundWon.emit(outcome.roundNo);
    else if (outcome.verdict === GoonRoundVerdict.Loss) this._roundLost.emit(outcome.roundNo);
  }

  _raiseAborted(reason) {
    this._info(`sudden death: ladder aborted (${reason})`);
    this._aborted.emit(reason);
  }

  _info(m) { if (this._log && this._log.info) this._log.info(`[GG] ${m}`); }
  _warn(m) { if (this._log && this._log.warn) this._log.warn(`[GG] ${m}`); }
  _error(m) { if (this._log && this._log.error) this._log.error(`[GG] ${m}`); }
}

/** Per-round PRNG. Uses the context's factory when supplied, else a GoonRng on the round seed. */
function createRng(ctx, seed) {
  const factory = ctx.rngFactory;
  return typeof factory === 'function' ? factory(seed) : new GoonRng(seed);
}

function abortError() {
  const e = new Error('aborted');
  e.name = 'AbortError';
  return e;
}
